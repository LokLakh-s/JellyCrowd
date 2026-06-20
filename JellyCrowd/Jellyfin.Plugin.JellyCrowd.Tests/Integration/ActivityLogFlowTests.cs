using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Api;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Integration;

/// <summary>
/// End-to-end test of the M26 activity-log flow: a real <see cref="JsonRequestStore"/> +
/// <see cref="DownloadDispatcher"/> + <see cref="JsonActivityLog"/> dispatch a request, and the
/// resulting entry is read back through the same <see cref="LogsController"/> path the admin UI uses.
/// </summary>
public sealed class ActivityLogFlowTests : IDisposable
{
  private readonly string _dir = Path.Combine(Path.GetTempPath(), "jellycrowd-tests", Guid.NewGuid().ToString("N"));
  private readonly JsonRequestStore _store;
  private readonly JsonActivityLog _activityLog;

  public ActivityLogFlowTests()
  {
    Directory.CreateDirectory(_dir);
    _store = new JsonRequestStore(Path.Combine(_dir, "requests.json"));
    _activityLog = new JsonActivityLog(Path.Combine(_dir, "activity.json"));
  }

  public void Dispose()
  {
    _store.Dispose();
    _activityLog.Dispose();
    if (Directory.Exists(_dir))
    {
      Directory.Delete(_dir, recursive: true);
    }
  }

  [Fact]
  public async Task DispatchSuccess_RecordsInfoEntry_ReadableViaLogsController()
  {
    var client = new RecordingDownloadClient();
    var config = new PluginConfiguration { DownloadBackend = "webhook", DownloadWebhookUrl = "http://example/hook" };
    var dispatcher = new DownloadDispatcher(
      new IDownloadClient[] { client }, _store, _ => "victor", () => config, _activityLog, new RecordingNotificationService(), NullLogger<DownloadDispatcher>.Instance);

    var created = await _store.CreateAsync(
      new RequestRecord { TmdbId = 603, MediaType = "movie", Title = "The Matrix" }, CancellationToken.None);
    var approved = (await _store.UpdateStatusAsync(created.Id, RequestStatus.Approved, Guid.NewGuid(), CancellationToken.None))!;

    var ok = await dispatcher.DispatchAsync(approved, CancellationToken.None);

    Assert.True(ok);
    Assert.Single(client.Dispatched);

    var entry = await WaitForEntryAsync(e => e.Category == "download" && e.Level == "info");
    Assert.Contains("The Matrix", entry.Message, StringComparison.Ordinal);
    Assert.Contains("webhook", entry.Message, StringComparison.Ordinal);

    // Read it back the way the admin Logs tab does.
    var controller = new LogsController(_activityLog);
    var result = await controller.Get(term: "Matrix", category: "download", level: "info", limit: 50, CancellationToken.None);
    var entries = Assert.IsAssignableFrom<System.Collections.Generic.IReadOnlyList<ActivityEntry>>(
      Assert.IsType<OkObjectResult>(result.Result).Value);
    Assert.Single(entries);
    Assert.Equal("download", entries[0].Category);
  }

  [Fact]
  public async Task DispatchFailure_RecordsErrorEntry()
  {
    var client = new RecordingDownloadClient { Throw = true };
    var config = new PluginConfiguration { DownloadBackend = "webhook", DownloadWebhookUrl = "http://example/hook" };
    var dispatcher = new DownloadDispatcher(
      new IDownloadClient[] { client }, _store, _ => "victor", () => config, _activityLog, new RecordingNotificationService(), NullLogger<DownloadDispatcher>.Instance);

    var created = await _store.CreateAsync(
      new RequestRecord { TmdbId = 438631, MediaType = "movie", Title = "Dune" }, CancellationToken.None);
    var approved = (await _store.UpdateStatusAsync(created.Id, RequestStatus.Approved, Guid.NewGuid(), CancellationToken.None))!;

    var ok = await dispatcher.DispatchAsync(approved, CancellationToken.None);

    Assert.False(ok);
    var entry = await WaitForEntryAsync(e => e.Category == "download" && e.Level == "error");
    Assert.Contains("Dune", entry.Message, StringComparison.Ordinal);
  }

  private async Task<ActivityEntry> WaitForEntryAsync(Func<ActivityEntry, bool> predicate)
  {
    // Dispatch logging is fire-and-forget; poll briefly until the entry lands.
    for (var i = 0; i < 50; i++)
    {
      var all = await _activityLog.QueryAsync(null, null, null, 100, CancellationToken.None);
      var match = all.FirstOrDefault(predicate);
      if (match is not null)
      {
        return match;
      }

      await Task.Delay(20);
    }

    throw new Xunit.Sdk.XunitException("The expected activity entry was never recorded.");
  }

  private sealed class RecordingDownloadClient : IDownloadClient
  {
    public System.Collections.Generic.List<DownloadDispatch> Dispatched { get; } = new();

    public bool Throw { get; set; }

    public string Backend => "webhook";

    public bool IsConfigured(PluginConfiguration config) => true;

    public Task DispatchAsync(DownloadDispatch dispatch, CancellationToken cancellationToken)
    {
      if (Throw)
      {
        throw new InvalidOperationException("boom");
      }

      Dispatched.Add(dispatch);
      return Task.CompletedTask;
    }

    public Task TestAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task CancelAsync(DownloadDispatch dispatch, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RetryAsync(DownloadDispatch dispatch, CancellationToken cancellationToken)
    {
      if (Throw)
      {
        throw new InvalidOperationException("boom");
      }

      Dispatched.Add(dispatch);
      return Task.CompletedTask;
    }
  }
}
