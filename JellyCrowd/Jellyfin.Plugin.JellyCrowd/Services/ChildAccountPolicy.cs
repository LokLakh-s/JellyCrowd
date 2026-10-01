using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Pure rules of child accounts: who is a child, who their parents are, and the one-time migration of the
/// former "child groups".
/// </summary>
public static class ChildAccountPolicy
{
  /// <summary>
  /// The child account of a user, or <c>null</c> when the user is not a child.
  /// </summary>
  /// <param name="config">The plugin configuration.</param>
  /// <param name="userId">The user id.</param>
  /// <returns>The child account, or <c>null</c>.</returns>
  public static ChildAccount? ChildAccountOf(PluginConfiguration config, Guid userId)
  {
    ArgumentNullException.ThrowIfNull(config);
    return userId == Guid.Empty ? null : config.ChildAccounts.FirstOrDefault(c => c.UserId == userId);
  }

  /// <summary>
  /// Whether a user is a child account.
  /// </summary>
  /// <param name="config">The plugin configuration.</param>
  /// <param name="userId">The user id.</param>
  /// <returns><c>true</c> for a child.</returns>
  public static bool IsChild(PluginConfiguration config, Guid userId) => ChildAccountOf(config, userId) is not null;

  /// <summary>
  /// The children a parent requests for.
  /// </summary>
  /// <param name="config">The plugin configuration.</param>
  /// <param name="parentId">The parent's user id.</param>
  /// <returns>The children's accounts (none when the user is not a parent).</returns>
  public static IReadOnlyList<ChildAccount> ChildrenOf(PluginConfiguration config, Guid parentId)
  {
    ArgumentNullException.ThrowIfNull(config);
    return parentId == Guid.Empty
      ? Array.Empty<ChildAccount>()
      : config.ChildAccounts.Where(c => c.ParentIds.Contains(parentId) && c.UserId != parentId).ToList();
  }

  /// <summary>
  /// Whether a user is one of a child's parents.
  /// </summary>
  /// <param name="config">The plugin configuration.</param>
  /// <param name="parentId">The would-be parent.</param>
  /// <param name="childId">The child.</param>
  /// <returns><c>true</c> when the user may request for the child.</returns>
  public static bool IsParentOf(PluginConfiguration config, Guid parentId, Guid childId)
    => ChildrenOf(config, parentId).Any(c => c.UserId == childId);

  /// <summary>
  /// Turns the members of former "child groups" into child accounts, with the group's age and no parent yet
  /// (an administrator picks them), and clears the group flag. Idempotent; a member who already is a child
  /// account keeps it as it is.
  /// </summary>
  /// <param name="config">The plugin configuration (changed in place).</param>
  /// <returns><c>true</c> when something changed and the configuration must be saved.</returns>
  public static bool MigrateChildGroups(PluginConfiguration config)
  {
    ArgumentNullException.ThrowIfNull(config);
    var changed = false;
    foreach (var group in config.UserGroups.Where(g => g.ChildMode))
    {
      foreach (var member in group.Members.Where(m => m != Guid.Empty && !IsChild(config, m)).ToList())
      {
        config.ChildAccounts.Add(new ChildAccount { UserId = member, MaxAge = Math.Max(0, group.ChildMaxAge) });
      }

      group.ChildMode = false;
      changed = true;
    }

    return changed;
  }
}
