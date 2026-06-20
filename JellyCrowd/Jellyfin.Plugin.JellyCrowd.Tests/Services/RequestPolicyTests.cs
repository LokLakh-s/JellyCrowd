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
}
