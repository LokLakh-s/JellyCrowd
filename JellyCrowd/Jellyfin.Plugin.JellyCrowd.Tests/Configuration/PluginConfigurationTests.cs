using Jellyfin.Plugin.JellyCrowd.Configuration;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Configuration;

/// <summary>
/// Tests for <see cref="PluginConfiguration"/> defaults.
/// </summary>
public class PluginConfigurationTests
{
  private const long Gib = 1024L * 1024 * 1024;

  [Fact]
  public void Defaults_AreSet()
  {
    var config = new PluginConfiguration();

    Assert.Equal(string.Empty, config.TmdbApiKey);
    Assert.True(config.RequireApproval);
    Assert.Equal(50 * Gib, config.DefaultUserQuotaBytes);
    Assert.Equal(4 * Gib, config.EstimatedMovieSizeBytes);
    Assert.Equal(1 * Gib, config.EstimatedEpisodeSizeBytes);
  }

  [Fact]
  public void DefaultQuotaConstant_Matches50Gib()
  {
    Assert.Equal(50 * Gib, PluginConfiguration.DefaultQuotaBytes);
  }
}
