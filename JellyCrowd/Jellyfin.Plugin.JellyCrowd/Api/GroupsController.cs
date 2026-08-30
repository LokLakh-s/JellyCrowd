using System;
using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Api;

/// <summary>
/// Admin operations on user groups that touch Jellyfin itself. The groups (name, membership, inherited
/// Jelly Crowd settings, chosen libraries) are stored in the plugin configuration and edited through the
/// standard plugin-configuration API; this controller only performs the side effect the configuration
/// cannot: pushing a group's chosen libraries onto its members' Jellyfin accounts.
/// </summary>
[ApiController]
[Route("JellyCrowd/Groups")]
[Produces(MediaTypeNames.Application.Json)]
[Authorize(Policy = "RequiresElevation")]
public class GroupsController : ControllerBase
{
  private readonly Func<PluginConfiguration> _config;
  private readonly IUserManager _userManager;
  private readonly ILibraryManager _libraryManager;
  private readonly ILogger<GroupsController> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="GroupsController"/> class.
  /// </summary>
  /// <param name="config">Accessor for the current plugin configuration.</param>
  /// <param name="userManager">The Jellyfin user manager (reads and writes user policies).</param>
  /// <param name="libraryManager">The Jellyfin library manager (lists existing libraries).</param>
  /// <param name="logger">The logger.</param>
  public GroupsController(Func<PluginConfiguration> config, IUserManager userManager, ILibraryManager libraryManager, ILogger<GroupsController> logger)
  {
    _config = config;
    _userManager = userManager;
    _libraryManager = libraryManager;
    _logger = logger;
  }

  /// <summary>
  /// Lists the user groups (id, name, member count) and which of them the current announcement targets.
  /// Used by admin UIs such as the announcement audience picker.
  /// </summary>
  /// <response code="200">The groups overview.</response>
  /// <returns>The groups and the current announcement's target group ids.</returns>
  [HttpGet]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public ActionResult<GroupsAdminDto> GetGroups()
  {
    var config = _config();
    var dto = new GroupsAdminDto();
    foreach (var group in config.UserGroups)
    {
      dto.Groups.Add(new GroupSummaryDto
      {
        Id = group.Id,
        Name = group.Name ?? string.Empty,
        MemberCount = group.Members.Count,
      });
    }

    foreach (var groupId in config.AnnouncementGroupIds)
    {
      dto.AnnouncementGroupIds.Add(groupId);
    }

    return Ok(dto);
  }

  /// <summary>
  /// Pushes a group's chosen libraries onto every member's Jellyfin account, authoritatively: each member's
  /// policy is set to exactly the group's libraries (<c>EnableAllFolders = false</c>). Only the folder access
  /// is changed; every other policy field is preserved. Ids that no longer match a library are dropped.
  /// </summary>
  /// <param name="id">The group id.</param>
  /// <response code="200">The access was applied; the body reports how many members were updated.</response>
  /// <response code="404">No group with that id exists.</response>
  /// <returns>The apply outcome.</returns>
  [HttpPost("{id}/ApplyLibraryAccess")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<ApplyLibraryAccessResultDto>> ApplyLibraryAccess([FromRoute] Guid id)
  {
    var config = _config();
    var group = config.UserGroups.FirstOrDefault(g => g.Id == id);
    if (group is null)
    {
      return NotFound();
    }

    var existingItemIds = _libraryManager.GetVirtualFolders().Select(vf => vf.ItemId);
    var folders = LibraryAccessResolver.ResolveEnabledFolders(group.LibraryIds, existingItemIds);

    var applied = 0;
    var skipped = 0;
    foreach (var memberId in group.Members)
    {
      var user = _userManager.GetUserById(memberId);
      if (user is null)
      {
        skipped++;
        continue;
      }

      var policy = _userManager.GetUserDto(user, string.Empty)?.Policy;
      if (policy is null)
      {
        skipped++;
        continue;
      }

      policy.EnableAllFolders = false;
      policy.EnabledFolders = folders;
      await _userManager.UpdatePolicyAsync(memberId, policy).ConfigureAwait(false);
      applied++;
    }

    _logger.LogInformation(
      "Applied library access for group {GroupId}: {Applied} updated, {Skipped} skipped, {Libraries} libraries.",
      id,
      applied,
      skipped,
      folders.Length);

    return Ok(new ApplyLibraryAccessResultDto
    {
      Applied = applied,
      Skipped = skipped,
      Total = group.Members.Count,
      Libraries = folders.Length,
    });
  }
}
