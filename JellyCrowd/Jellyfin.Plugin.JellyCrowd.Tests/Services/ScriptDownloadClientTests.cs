using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="ScriptDownloadClient"/>.
/// </summary>
public class ScriptDownloadClientTests
{
  [Fact]
  public async Task DispatchAsync_RunsScript_WithJsonStdinAndEnv()
  {
    var runner = new FakeProcessRunner();
    var config = new PluginConfiguration { ScriptPath = "/opt/dl.sh", ScriptArguments = "--go" };
    var client = new ScriptDownloadClient(runner, () => config);

    await client.DispatchAsync(
      new DownloadDispatch { TmdbId = 603, MediaType = "movie", Title = "The Matrix", Season = 2, Episode = 3 },
      CancellationToken.None);

    Assert.Equal("/opt/dl.sh", runner.FileName);
    Assert.Equal("--go", runner.Arguments);
    Assert.Contains("\"TmdbId\":603", runner.StandardInput, StringComparison.Ordinal);
    Assert.Equal("603", runner.Environment["JELLYCROWD_TMDBID"]);
    Assert.Equal("movie", runner.Environment["JELLYCROWD_MEDIATYPE"]);
    Assert.Equal("2", runner.Environment["JELLYCROWD_SEASON"]);
    Assert.Equal("3", runner.Environment["JELLYCROWD_EPISODE"]);
  }

  [Fact]
  public void IsConfigured_RequiresScriptPath()
  {
    var client = new ScriptDownloadClient(new FakeProcessRunner(), () => new PluginConfiguration());
    Assert.False(client.IsConfigured(new PluginConfiguration()));
    Assert.True(client.IsConfigured(new PluginConfiguration { ScriptPath = "/x" }));
  }

  [Fact]
  public async Task DispatchAsync_NotConfigured_Throws()
  {
    var client = new ScriptDownloadClient(new FakeProcessRunner(), () => new PluginConfiguration());

    await Assert.ThrowsAsync<InvalidOperationException>(() =>
      client.DispatchAsync(new DownloadDispatch { TmdbId = 1, MediaType = "movie", Title = "X" }, CancellationToken.None));
  }

  private sealed class FakeProcessRunner : IProcessRunner
  {
    public string? FileName { get; private set; }

    public string? Arguments { get; private set; }

    public string StandardInput { get; private set; } = string.Empty;

    public IReadOnlyDictionary<string, string?> Environment { get; private set; } = new Dictionary<string, string?>();

    public Task RunAsync(string fileName, string? arguments, string standardInput, IReadOnlyDictionary<string, string?> environment, CancellationToken cancellationToken)
    {
      FileName = fileName;
      Arguments = arguments;
      StandardInput = standardInput;
      Environment = environment;
      return Task.CompletedTask;
    }

    public Task<string> RunCaptureAsync(string fileName, string? arguments, int timeoutSeconds, CancellationToken cancellationToken)
      => Task.FromResult(string.Empty);
  }
}
