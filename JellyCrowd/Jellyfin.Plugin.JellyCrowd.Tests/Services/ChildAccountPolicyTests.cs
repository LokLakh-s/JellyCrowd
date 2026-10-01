using System;
using System.Linq;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="ChildAccountPolicy"/>.
/// </summary>
public class ChildAccountPolicyTests
{
  private static readonly Guid Mum = Guid.NewGuid();
  private static readonly Guid Dad = Guid.NewGuid();
  private static readonly Guid Kid = Guid.NewGuid();

  private static PluginConfiguration Family()
  {
    var config = new PluginConfiguration();
    var kid = new ChildAccount { UserId = Kid, MaxAge = 10 };
    kid.ParentIds.Add(Mum);
    kid.ParentIds.Add(Dad);
    config.ChildAccounts.Add(kid);
    return config;
  }

  [Fact]
  public void BothParents_RequestForTheChild()
  {
    var config = Family();

    Assert.True(ChildAccountPolicy.IsParentOf(config, Mum, Kid));
    Assert.True(ChildAccountPolicy.IsParentOf(config, Dad, Kid));
    Assert.False(ChildAccountPolicy.IsParentOf(config, Guid.NewGuid(), Kid));
    Assert.Equal(Kid, Assert.Single(ChildAccountPolicy.ChildrenOf(config, Mum)).UserId);
    Assert.Empty(ChildAccountPolicy.ChildrenOf(config, Kid));
  }

  [Fact]
  public void IsChild_OnlyChildAccounts()
  {
    var config = Family();

    Assert.True(ChildAccountPolicy.IsChild(config, Kid));
    Assert.False(ChildAccountPolicy.IsChild(config, Mum));
    Assert.False(ChildAccountPolicy.IsChild(config, Guid.Empty));
  }

  [Fact]
  public void AChildIsNeverTheirOwnParent()
  {
    var config = new PluginConfiguration();
    var odd = new ChildAccount { UserId = Kid };
    odd.ParentIds.Add(Kid);
    config.ChildAccounts.Add(odd);

    Assert.False(ChildAccountPolicy.IsParentOf(config, Kid, Kid));
  }

  [Fact]
  public void MigrateChildGroups_MembersBecomeChildAccounts_FlagCleared()
  {
    var config = new PluginConfiguration();
    var other = Guid.NewGuid();
    var group = new UserGroup { Id = Guid.NewGuid(), Name = "Kids", ChildMode = true, ChildMaxAge = 12 };
    group.Members.Add(Kid);
    group.Members.Add(other);
    config.UserGroups.Add(group);

    Assert.True(ChildAccountPolicy.MigrateChildGroups(config));

    Assert.False(group.ChildMode);
    Assert.Equal(new[] { Kid, other }, config.ChildAccounts.Select(c => c.UserId));
    Assert.All(config.ChildAccounts, c => Assert.Equal(12, c.MaxAge));
    Assert.All(config.ChildAccounts, c => Assert.Empty(c.ParentIds)); // parents are for the admin to pick
  }

  [Fact]
  public void MigrateChildGroups_KeepsExistingChildAccounts_AndIsIdempotent()
  {
    var config = Family();
    var group = new UserGroup { Id = Guid.NewGuid(), Name = "Kids", ChildMode = true, ChildMaxAge = 16 };
    group.Members.Add(Kid);
    config.UserGroups.Add(group);

    Assert.True(ChildAccountPolicy.MigrateChildGroups(config));
    Assert.False(ChildAccountPolicy.MigrateChildGroups(config));

    var kid = Assert.Single(config.ChildAccounts);
    Assert.Equal(10, kid.MaxAge);         // the existing account wins
    Assert.Equal(2, kid.ParentIds.Count);
  }

  [Fact]
  public void MigrateChildGroups_NothingToDo()
  {
    var config = new PluginConfiguration();
    config.UserGroups.Add(new UserGroup { Id = Guid.NewGuid(), Name = "Adults" });

    Assert.False(ChildAccountPolicy.MigrateChildGroups(config));
    Assert.Empty(config.ChildAccounts);
  }
}
