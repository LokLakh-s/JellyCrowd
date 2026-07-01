using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="DiagnosticsService"/> (the Plugin.Instance-dependent data-folder and
/// footprint checks are exercised live, not here).
/// </summary>
public class DiagnosticsServiceTests
{
  private static DiagnosticsService Create(ITmdbClient tmdb, IDownloadDispatcher dispatcher, PluginConfiguration config)
    => new(tmdb, dispatcher, Mock.Of<IServarrClient>(), Mock.Of<IRequestStore>(), () => config, NullLogger<DiagnosticsService>.Instance);

  private static DiagnosticResult Find(IReadOnlyList<DiagnosticResult> results, string name)
    => results.First(r => r.Name == name);

  [Fact]
  public async Task Tmdb_NoKey_Error()
  {
    var results = await Create(Mock.Of<ITmdbClient>(), Mock.Of<IDownloadDispatcher>(), new PluginConfiguration()).RunAsync(CancellationToken.None);

    Assert.Equal("error", Find(results, "TMDB").Status);
  }

  [Fact]
  public async Task Tmdb_TrendingReturned_Ok()
  {
    var tmdb = new Mock<ITmdbClient>();
    tmdb.Setup(t => t.GetTrendingAsync("en-US", It.IsAny<CancellationToken>()))
      .ReturnsAsync(new List<CatalogItem> { new() { TmdbId = 1, Title = "X" } });
    var config = new PluginConfiguration { TmdbApiKey = "k", DownloadBackend = "none" };

    var results = await Create(tmdb.Object, Mock.Of<IDownloadDispatcher>(), config).RunAsync(CancellationToken.None);

    Assert.Equal("ok", Find(results, "TMDB").Status);
  }

  [Fact]
  public async Task Backend_None_Info()
  {
    var config = new PluginConfiguration { TmdbApiKey = "k", DownloadBackend = "none" };

    var results = await Create(Mock.Of<ITmdbClient>(), Mock.Of<IDownloadDispatcher>(), config).RunAsync(CancellationToken.None);

    Assert.Equal("info", Find(results, "Download backend").Status);
  }

  [Fact]
  public async Task Backend_TestSucceeds_Ok()
  {
    var dispatcher = new Mock<IDownloadDispatcher>();
    dispatcher.Setup(d => d.TestActiveAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    var config = new PluginConfiguration { TmdbApiKey = "k", DownloadBackend = "servarr" };

    var results = await Create(Mock.Of<ITmdbClient>(), dispatcher.Object, config).RunAsync(CancellationToken.None);

    Assert.Equal("ok", Find(results, "Download backend").Status);
  }

  [Fact]
  public async Task Backend_TestThrows_Error()
  {
    var dispatcher = new Mock<IDownloadDispatcher>();
    dispatcher.Setup(d => d.TestActiveAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("down"));
    var config = new PluginConfiguration { TmdbApiKey = "k", DownloadBackend = "servarr" };

    var results = await Create(Mock.Of<ITmdbClient>(), dispatcher.Object, config).RunAsync(CancellationToken.None);

    var backend = Find(results, "Download backend");
    Assert.Equal("error", backend.Status);
    Assert.Contains("down", backend.Detail, StringComparison.Ordinal);
  }
}
