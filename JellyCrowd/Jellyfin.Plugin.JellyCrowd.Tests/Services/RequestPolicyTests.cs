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
}
