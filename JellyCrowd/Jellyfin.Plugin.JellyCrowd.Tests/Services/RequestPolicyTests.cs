using System;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="RequestPolicy"/>.
/// </summary>
public class RequestPolicyTests
{
  private const long Gib = 1024L * 1024 * 1024;
  private static readonly Guid User = Guid.NewGuid();

  private static PluginConfiguration Config() => new()
  {
    MaxRequestsPerPeriod = 5,
    EstimatedMovieSizeBytes = 4 * Gib,
    EstimatedEpisodeSizeBytes = 1 * Gib,
    AutoApproveMaxSizeBytes = 0
  };

  [Fact]
  public void CanRequest_DefaultsTrue_WhenNoOverrideOrUnset()
  {
    var config = Config();
    Assert.True(RequestPolicy.CanRequest(config, User));

    config.QuotaOverrides.Add(new UserQuotaOverride { UserId = User, QuotaBytes = 2 * Gib });
    Assert.True(RequestPolicy.CanRequest(config, User)); // quota-only entry doesn't disable requests
  }

  [Fact]
  public void CanRequest_FalseWhenDisabled()
  {
    var config = Config();
    config.QuotaOverrides.Add(new UserQuotaOverride { UserId = User, CanRequest = false });
    Assert.False(RequestPolicy.CanRequest(config, User));
  }

  [Fact]
  public void MaxRequestsPerPeriod_UsesOverrideThenGlobal()
  {
    var config = Config();
    Assert.Equal(5, RequestPolicy.MaxRequestsPerPeriod(config, User));

    config.QuotaOverrides.Add(new UserQuotaOverride { UserId = User, MaxRequestsPerPeriod = 2 });
    Assert.Equal(2, RequestPolicy.MaxRequestsPerPeriod(config, User));
  }

  [Fact]
  public void ShouldAutoApprove_TrueForTrustedUser()
  {
    var config = Config();
    config.QuotaOverrides.Add(new UserQuotaOverride { UserId = User, AutoApprove = true });
    Assert.True(RequestPolicy.ShouldAutoApprove(config, User, "movie"));
  }

  [Fact]
  public void ShouldAutoApprove_SizeRule_ByMediaType()
  {
    var config = Config();
    config.AutoApproveMaxSizeBytes = 2 * Gib;

    Assert.False(RequestPolicy.ShouldAutoApprove(config, User, "movie")); // 4 GiB estimate > 2 GiB
    Assert.True(RequestPolicy.ShouldAutoApprove(config, User, "tv"));     // 1 GiB estimate <= 2 GiB
  }

  [Fact]
  public void ShouldAutoApprove_FalseWhenRuleOffAndNotTrusted()
  {
    var config = Config();
    Assert.False(RequestPolicy.ShouldAutoApprove(config, User, "movie"));
    Assert.False(RequestPolicy.ShouldAutoApprove(config, User, "tv"));
  }

  [Fact]
  public void ShouldAutoApprove_GenreGate_RequiresMatchWhenConfigured()
  {
    var config = Config();
    config.AutoApproveMaxSizeBytes = 2 * Gib; // tv (1 GiB) passes the size gate
    config.AutoApproveGenres.Add("Documentary");

    // Size passes but no genre supplied -> blocked by the genre gate.
    Assert.False(RequestPolicy.ShouldAutoApprove(config, User, "tv", Array.Empty<string>()));

    // A matching genre (case-insensitive) passes.
    Assert.True(RequestPolicy.ShouldAutoApprove(config, User, "tv", new[] { "documentary" }));

    // A non-matching genre stays pending.
    Assert.False(RequestPolicy.ShouldAutoApprove(config, User, "tv", new[] { "Horror" }));
  }

  [Fact]
  public void ShouldAutoApprove_GenreGate_IgnoredWhenListEmpty()
  {
    var config = Config();
    config.AutoApproveMaxSizeBytes = 2 * Gib;

    // No genre list configured -> size rule alone decides, genres irrelevant.
    Assert.True(RequestPolicy.ShouldAutoApprove(config, User, "tv", Array.Empty<string>()));
  }

  [Fact]
  public void ShouldAutoApprove_TrustedUser_BypassesGenreGate()
  {
    var config = Config();
    config.AutoApproveGenres.Add("Documentary");
    config.QuotaOverrides.Add(new UserQuotaOverride { UserId = User, AutoApprove = true });

    Assert.True(RequestPolicy.ShouldAutoApprove(config, User, "movie", Array.Empty<string>()));
  }

  [Fact]
  public void IsVisibleTo_NotHidden_VisibleToEveryone()
  {
    var config = Config(); // HiddenFromUsers defaults false
    Assert.True(RequestPolicy.IsVisibleTo(config, User, isAdmin: false));
  }

  [Fact]
  public void IsVisibleTo_Hidden_OnlyAdminsAndGrantedUsers()
  {
    var config = Config();
    config.HiddenFromUsers = true;
    var granted = Guid.NewGuid();
    config.QuotaOverrides.Add(new UserQuotaOverride { UserId = granted, PluginAccess = true });

    Assert.True(RequestPolicy.IsVisibleTo(config, User, isAdmin: true));        // admin
    Assert.True(RequestPolicy.IsVisibleTo(config, granted, isAdmin: false));    // granted access
    Assert.False(RequestPolicy.IsVisibleTo(config, User, isAdmin: false));      // regular, no access
  }

  [Fact]
  public void IsVisibleTo_EnabledOverride_ForcesAccess_EvenInConfigMode()
  {
    var config = Config();
    config.HiddenFromUsers = true; // hidden by default
    var user = Guid.NewGuid();
    config.QuotaOverrides.Add(new UserQuotaOverride { UserId = user, PluginAccess = true });

    Assert.True(RequestPolicy.IsVisibleTo(config, user, isAdmin: false)); // override forces access
  }

  [Fact]
  public void IsVisibleTo_DisabledOverride_BlocksAccess_EvenWhenPluginVisible()
  {
    var config = Config(); // config mode off → visible to all by default
    var blocked = Guid.NewGuid();
    config.QuotaOverrides.Add(new UserQuotaOverride { UserId = blocked, PluginAccess = false });

    Assert.False(RequestPolicy.IsVisibleTo(config, blocked, isAdmin: false)); // blocked despite plugin being visible
    Assert.True(RequestPolicy.IsVisibleTo(config, User, isAdmin: false));     // others unaffected
  }

  [Fact]
  public void IsVisibleTo_NoOverride_FollowsConfigMode()
  {
    var config = Config();
    Assert.True(RequestPolicy.IsVisibleTo(config, User, isAdmin: false)); // config mode off → visible

    config.HiddenFromUsers = true;
    Assert.False(RequestPolicy.IsVisibleTo(config, User, isAdmin: false)); // config mode on → hidden
  }

  [Fact]
  public void IsVisibleTo_Admin_AlwaysVisible_EvenWithDisabledOverride()
  {
    var config = Config();
    var admin = Guid.NewGuid();
    config.QuotaOverrides.Add(new UserQuotaOverride { UserId = admin, PluginAccess = false });

    Assert.True(RequestPolicy.IsVisibleTo(config, admin, isAdmin: true));
  }

  // ---------- User groups ----------
  private static UserGroup Group(Guid member) => new()
  {
    Id = Guid.NewGuid(),
    Name = "G",
    Members = { member },
  };

  [Fact]
  public void GroupOf_FindsMemberGroup_OrNull()
  {
    var config = Config();
    Assert.Null(RequestPolicy.GroupOf(config, User));

    var group = Group(User);
    config.UserGroups.Add(group);
    Assert.Same(group, RequestPolicy.GroupOf(config, User));
    Assert.Null(RequestPolicy.GroupOf(config, Guid.NewGuid())); // a non-member has no group
  }

  [Fact]
  public void CanRequest_UsesGroup_WhenNoPerUserValue()
  {
    var config = Config();
    var group = Group(User);
    group.CanRequest = false;
    config.UserGroups.Add(group);

    Assert.False(RequestPolicy.CanRequest(config, User)); // inherited from the group
  }

  [Fact]
  public void CanRequest_PerUserWinsOverGroup()
  {
    var config = Config();
    var group = Group(User);
    group.CanRequest = false;
    config.UserGroups.Add(group);
    config.QuotaOverrides.Add(new UserQuotaOverride { UserId = User, CanRequest = true });

    Assert.True(RequestPolicy.CanRequest(config, User)); // per-user override wins
  }

  [Fact]
  public void MaxRequestsPerPeriod_PerUserThenGroupThenGlobal()
  {
    var config = Config(); // global = 5
    var group = Group(User);
    group.MaxRequestsPerPeriod = 3;
    config.UserGroups.Add(group);
    Assert.Equal(3, RequestPolicy.MaxRequestsPerPeriod(config, User)); // group value

    config.QuotaOverrides.Add(new UserQuotaOverride { UserId = User, MaxRequestsPerPeriod = 1 });
    Assert.Equal(1, RequestPolicy.MaxRequestsPerPeriod(config, User)); // per-user wins

    Assert.Equal(5, RequestPolicy.MaxRequestsPerPeriod(config, Guid.NewGuid())); // non-member → global
  }

  [Fact]
  public void IsTrusted_UsesGroupAutoApprove()
  {
    var config = Config();
    var group = Group(User);
    group.AutoApprove = true;
    config.UserGroups.Add(group);

    Assert.True(RequestPolicy.IsTrusted(config, User));
  }

  [Fact]
  public void IsVisibleTo_GroupPluginAccessBlocks_PerUserOverrides()
  {
    var config = Config();
    var group = Group(User);
    group.PluginAccess = false;
    config.UserGroups.Add(group);
    Assert.False(RequestPolicy.IsVisibleTo(config, User, isAdmin: false)); // blocked by group

    config.QuotaOverrides.Add(new UserQuotaOverride { UserId = User, PluginAccess = true });
    Assert.True(RequestPolicy.IsVisibleTo(config, User, isAdmin: false)); // per-user re-enables
  }

  [Fact]
  public void GroupSettings_DoNotAffectNonMembers()
  {
    var config = Config();
    var group = Group(User);
    group.CanRequest = false;
    group.MaxRequestsPerPeriod = 1;
    group.PluginAccess = false;
    config.UserGroups.Add(group);

    var other = Guid.NewGuid();
    Assert.True(RequestPolicy.CanRequest(config, other));
    Assert.Equal(5, RequestPolicy.MaxRequestsPerPeriod(config, other));
    Assert.True(RequestPolicy.IsVisibleTo(config, other, isAdmin: false));
  }

  [Fact]
  public void ShouldSeeAnnouncement_EmptyText_False()
  {
    var config = Config();
    config.AnnouncementText = "   ";
    Assert.False(RequestPolicy.ShouldSeeAnnouncement(config, User, isAdmin: false));
  }

  [Fact]
  public void ShouldSeeAnnouncement_Global_ShownToEveryone()
  {
    var config = Config();
    config.AnnouncementText = "Hello";
    Assert.True(RequestPolicy.ShouldSeeAnnouncement(config, User, isAdmin: false));
    Assert.True(RequestPolicy.ShouldSeeAnnouncement(config, Guid.NewGuid(), isAdmin: false));
  }

  [Fact]
  public void ShouldSeeAnnouncement_Targeted_OnlyMembersAndAdmins()
  {
    var config = Config();
    config.AnnouncementText = "Members only";
    var group = Group(User);
    config.UserGroups.Add(group);
    config.AnnouncementGroupIds.Add(group.Id);

    Assert.True(RequestPolicy.ShouldSeeAnnouncement(config, User, isAdmin: false));          // member
    Assert.False(RequestPolicy.ShouldSeeAnnouncement(config, Guid.NewGuid(), isAdmin: false)); // non-member
    Assert.True(RequestPolicy.ShouldSeeAnnouncement(config, Guid.NewGuid(), isAdmin: true));  // admin always
  }

  // ---------- Child mode ----------
  [Fact]
  public void ChildPolicyFor_ReadsChildGroup()
  {
    var config = Config();
    var group = Group(User);
    group.ChildMode = true;
    group.ChildMaxAge = 12;
    config.UserGroups.Add(group);

    var policy = RequestPolicy.ChildPolicyFor(config, User);
    Assert.True(policy.IsChild);
    Assert.Equal(12, policy.MaxAge);

    Assert.False(RequestPolicy.ChildPolicyFor(config, Guid.NewGuid()).IsChild); // non-member
  }

  [Fact]
  public void ChildPolicyFor_NonChildGroup_IsNotChild()
  {
    var config = Config();
    var group = Group(User); // ChildMode defaults false
    config.UserGroups.Add(group);
    Assert.False(RequestPolicy.ChildPolicyFor(config, User).IsChild);
  }

  [Fact]
  public void ShouldSeeAnnouncement_HiddenForChildAccounts()
  {
    var config = Config();
    config.AnnouncementText = "Everyone"; // global announcement
    var group = Group(User);
    group.ChildMode = true;
    config.UserGroups.Add(group);

    Assert.False(RequestPolicy.ShouldSeeAnnouncement(config, User, isAdmin: false)); // child: hidden
    Assert.True(RequestPolicy.ShouldSeeAnnouncement(config, Guid.NewGuid(), isAdmin: false)); // others: shown
  }

  // ---------- Request scope (media types + granularity) ----------
  [Fact]
  public void IsMediaTypeEnabled_RespectsToggles()
  {
    var config = Config(); // both default on
    Assert.True(RequestPolicy.IsMediaTypeEnabled(config, "movie"));
    Assert.True(RequestPolicy.IsMediaTypeEnabled(config, "tv"));

    config.MoviesEnabled = false;
    Assert.False(RequestPolicy.IsMediaTypeEnabled(config, "movie"));
    Assert.True(RequestPolicy.IsMediaTypeEnabled(config, "tv"));

    config.MoviesEnabled = true;
    config.SeriesEnabled = false;
    Assert.True(RequestPolicy.IsMediaTypeEnabled(config, "movie"));
    Assert.False(RequestPolicy.IsMediaTypeEnabled(config, "tv"));
  }

  [Fact]
  public void IsRequestGranularityAllowed_MoviesAlwaysAllowed()
  {
    var config = Config();
    config.AllowSeriesRequests = false;
    config.AllowSeasonRequests = false;
    config.AllowEpisodeRequests = false; // even fully off, movies are unaffected
    Assert.True(RequestPolicy.IsRequestGranularityAllowed(config, "movie", null, null));
  }

  [Fact]
  public void IsRequestGranularityAllowed_GatesEachTvLevel()
  {
    var config = Config(); // all three on
    Assert.True(RequestPolicy.IsRequestGranularityAllowed(config, "tv", null, null)); // whole series
    Assert.True(RequestPolicy.IsRequestGranularityAllowed(config, "tv", 2, null));    // season
    Assert.True(RequestPolicy.IsRequestGranularityAllowed(config, "tv", 2, 5));       // episode

    config.AllowSeriesRequests = false;
    Assert.False(RequestPolicy.IsRequestGranularityAllowed(config, "tv", null, null)); // whole series blocked
    Assert.True(RequestPolicy.IsRequestGranularityAllowed(config, "tv", 2, null));     // season still ok
    Assert.True(RequestPolicy.IsRequestGranularityAllowed(config, "tv", 2, 5));        // episode still ok

    config.AllowSeasonRequests = false;
    Assert.False(RequestPolicy.IsRequestGranularityAllowed(config, "tv", 2, null));    // season blocked
    Assert.True(RequestPolicy.IsRequestGranularityAllowed(config, "tv", 2, 5));        // episode still ok
  }

  [Fact]
  public void IsRequestGranularityAllowed_AllOff_IsTreatedAsAllOn()
  {
    var config = Config();
    config.AllowSeriesRequests = false;
    config.AllowSeasonRequests = false;
    config.AllowEpisodeRequests = false; // misconfiguration → never block TV entirely
    Assert.True(RequestPolicy.IsRequestGranularityAllowed(config, "tv", null, null));
    Assert.True(RequestPolicy.IsRequestGranularityAllowed(config, "tv", 2, null));
    Assert.True(RequestPolicy.IsRequestGranularityAllowed(config, "tv", 2, 5));
  }
}
