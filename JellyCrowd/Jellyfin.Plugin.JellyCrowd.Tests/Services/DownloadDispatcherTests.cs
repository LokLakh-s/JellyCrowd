using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="DownloadDispatcher"/> using a real <see cref="JsonRequestStore"/> and a fake client.
/// </summary>
public sealed class DownloadDispatcherTests : IDisposable
{
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jc-" + Guid.NewGuid() + ".json");
  private readonly JsonRequestStore _store;
  private readonly FakeDownloadClient _client = new();
  private readonly RecordingNotificationService _notifier = new();
  private readonly PluginConfiguration _config = new() { DownloadBackend = "webhook", DownloadWebhookUrl = "http://example/hook" };

  public DownloadDispatcherTests()
  {
    _store = new JsonRequestStore(_path);
  }

  public void Dispose()
  {
    _store.Dispose();
    if (File.Exists(_path))
    {
      File.Delete(_path);
    }
  }

  private DownloadDispatcher CreateDispatcher(bool withinQuota = true)
    => new(new IDownloadClient[] { _client }, _store, new StubQuotaService(withinQuota), _ => "tester", () => _config, new NoOpActivityLog(), _notifier, NullLogger<DownloadDispatcher>.Instance);

  private async Task<RequestRecord> SeedApprovedAsync()
  {
    var created = await _store.CreateAsync(new RequestRecord { TmdbId = 603, MediaType = "movie", Title = "The Matrix" }, CancellationToken.None);
    return (await _store.UpdateStatusAsync(created.Id, RequestStatus.Approved, Guid.NewGuid(), CancellationToken.None))!;
  }

  [Fact]
  public async Task DispatchAsync_Eligible_DispatchesAndStamps()
  {
    var request = await SeedApprovedAsync();

    var dispatched = await CreateDispatcher().DispatchAsync(request, CancellationToken.None);

    Assert.True(dispatched);
    Assert.Single(_client.Dispatched);
    Assert.Equal(603, _client.Dispatched[0].TmdbId);
    var stored = await _store.GetByIdAsync(request.Id, CancellationToken.None);
    Assert.NotNull(stored!.DispatchedAt);
  }

  [Fact]
  public async Task DispatchAsync_Pending_NotDispatched()
  {
    var created = await _store.CreateAsync(new RequestRecord { TmdbId = 1, MediaType = "movie", Title = "X" }, CancellationToken.None);

    var dispatched = await CreateDispatcher().DispatchAsync(created, CancellationToken.None);

    Assert.False(dispatched);
    Assert.Empty(_client.Dispatched);
  }

  [Fact]
  public async Task DispatchAsync_BackendNone_NotDispatched()
  {
    _config.DownloadBackend = "none";
    var request = await SeedApprovedAsync();

    var dispatched = await CreateDispatcher().DispatchAsync(request, CancellationToken.None);

    Assert.False(dispatched);
    Assert.Empty(_client.Dispatched);
  }

  [Fact]
  public async Task DispatchAsync_ClientThrows_NotStamped_RecordsError()
  {
    _client.Throw = true;
    var request = await SeedApprovedAsync();

    var dispatched = await CreateDispatcher().DispatchAsync(request, CancellationToken.None);

    Assert.False(dispatched);
    var stored = await _store.GetByIdAsync(request.Id, CancellationToken.None);
    Assert.Null(stored!.DispatchedAt);
    Assert.Equal("boom", stored.DispatchError);
    Assert.NotNull(stored.DispatchAttemptedAt);
  }

  [Fact]
  public async Task DispatchAsync_SuccessAfterFailure_ClearsError()
  {
    _client.Throw = true;
    var request = await SeedApprovedAsync();
    await CreateDispatcher().DispatchAsync(request, CancellationToken.None);

    _client.Throw = false;
    var dispatched = await CreateDispatcher().DispatchAsync(request, CancellationToken.None);

    Assert.True(dispatched);
    var stored = await _store.GetByIdAsync(request.Id, CancellationToken.None);
    Assert.NotNull(stored!.DispatchedAt);
    Assert.Null(stored.DispatchError);
  }

  [Fact]
  public async Task DispatchAsync_FirstFailure_NotifiesRequesterOnce()
  {
    _client.Throw = true;
    var request = await SeedApprovedAsync();

    await CreateDispatcher().DispatchAsync(request, CancellationToken.None);
    // Second attempt: the request now already carries a DispatchError, so no duplicate notification.
    var reloaded = await _store.GetByIdAsync(request.Id, CancellationToken.None);
    await CreateDispatcher().DispatchAsync(reloaded!, CancellationToken.None);

    Assert.Single(_notifier.Events);
    Assert.Equal(NotificationEvent.Failed, _notifier.Events[0]);
  }

  [Fact]
  public async Task DispatchAsync_Failure_StampsWhenTheFailuresStarted_AndSuccessClearsIt()
  {
    _client.Throw = true;
    var request = await SeedApprovedAsync();
    await CreateDispatcher().DispatchAsync(request, CancellationToken.None);
    var first = await _store.GetByIdAsync(request.Id, CancellationToken.None);
    await CreateDispatcher().DispatchAsync(first!, CancellationToken.None);
    var second = await _store.GetByIdAsync(request.Id, CancellationToken.None);

    Assert.NotNull(first!.DispatchFailingSince);
    Assert.Equal(first.DispatchFailingSince, second!.DispatchFailingSince);

    _client.Throw = false;
    await CreateDispatcher().DispatchAsync(second, CancellationToken.None);
    Assert.Null((await _store.GetByIdAsync(request.Id, CancellationToken.None))!.DispatchFailingSince);
  }

  [Fact]
  public async Task DispatchAsync_SameFailureAgain_IsLoggedOnce()
  {
    // The activity log is capped: one stuck request retried every few minutes used to fill it.
    _client.Throw = true;
    var activity = new RecordingActivityLog();
    var dispatcher = new DownloadDispatcher(new IDownloadClient[] { _client }, _store, new StubQuotaService(true), _ => "tester", () => _config, activity, _notifier, NullLogger<DownloadDispatcher>.Instance);
    var request = await SeedApprovedAsync();

    for (var i = 0; i < 3; i++)
    {
      var current = await _store.GetByIdAsync(request.Id, CancellationToken.None);
      await dispatcher.DispatchAsync(current!, CancellationToken.None);
    }

    Assert.Single(activity.Messages, m => m.StartsWith("Dispatch failed", StringComparison.Ordinal));
  }

  [Fact]
  public async Task DispatchDueAsync_SkipsAFailureStillInItsBackOff()
  {
    var request = await SeedApprovedAsync();
    await _store.SetDispatchErrorAsync(request.Id, "Could not resolve a TVDB id", DateTime.UtcNow.AddDays(-2), CancellationToken.None);
    await _store.SetDispatchErrorAsync(request.Id, "Could not resolve a TVDB id", DateTime.UtcNow.AddMinutes(-6), CancellationToken.None);

    await CreateDispatcher().DispatchDueAsync(CancellationToken.None);

    Assert.Empty(_client.Dispatched);
  }

  [Fact]
  public async Task DispatchDueAsync_RetriesAFailureOnceItsBackOffElapsed()
  {
    var request = await SeedApprovedAsync();
    await _store.SetDispatchErrorAsync(request.Id, "Sonarr unreachable", DateTime.UtcNow.AddDays(-2), CancellationToken.None);
    await _store.SetDispatchErrorAsync(request.Id, "Sonarr unreachable", DateTime.UtcNow.AddHours(-7), CancellationToken.None);

    await CreateDispatcher().DispatchDueAsync(CancellationToken.None);

    Assert.Single(_client.Dispatched);
  }

  [Fact]
  public async Task DispatchDueAsync_DispatchesEveryDueRequest()
  {
    await SeedApprovedAsync();
    await SeedApprovedAsync();

    await CreateDispatcher().DispatchDueAsync(CancellationToken.None);

    Assert.Equal(2, _client.Dispatched.Count);
  }

  [Fact]
  public async Task DispatchDueAsync_OverQuota_HoldsInsteadOfDispatching()
  {
    var request = await SeedApprovedAsync();

    await CreateDispatcher(withinQuota: false).DispatchDueAsync(CancellationToken.None);

    // Nothing is sent to the backend; the request is demoted back to a quota hold.
    Assert.Empty(_client.Dispatched);
    var stored = await _store.GetByIdAsync(request.Id, CancellationToken.None);
    Assert.Equal(RequestStatus.Pending, stored!.Status);
    Assert.True(stored.HeldForQuota);
    Assert.Null(stored.DispatchedAt);
    // The requester is told the download is on hold.
    Assert.Contains(_notifier.Personal, e => e.Kind == PersonalNotifyKind.QuotaExpiry);
  }

  [Fact]
  public async Task DispatchDueAsync_WithinQuota_Dispatches()
  {
    var request = await SeedApprovedAsync();

    await CreateDispatcher(withinQuota: true).DispatchDueAsync(CancellationToken.None);

    Assert.Single(_client.Dispatched);
    var stored = await _store.GetByIdAsync(request.Id, CancellationToken.None);
    Assert.Equal(RequestStatus.Approved, stored!.Status);
    Assert.NotNull(stored.DispatchedAt);
  }

  [Fact]
  public async Task CancelAsync_DelegatesToActiveClient()
  {
    var request = await SeedApprovedAsync();

    await CreateDispatcher().CancelAsync(request, CancellationToken.None);

    Assert.Single(_client.Cancelled);
    Assert.Equal(603, _client.Cancelled[0].TmdbId);
  }

  [Fact]
  public async Task RetryAsync_DelegatesToClient_AndClearsError()
  {
    var request = await SeedApprovedAsync();
    await _store.SetDispatchErrorAsync(request.Id, "not found", DateTime.UtcNow, CancellationToken.None);

    var ok = await CreateDispatcher().RetryAsync(request, CancellationToken.None);

    Assert.True(ok);
    Assert.Single(_client.Retried);
    Assert.Equal(603, _client.Retried[0].TmdbId);
    var stored = await _store.GetByIdAsync(request.Id, CancellationToken.None);
    Assert.Null(stored!.DispatchError);
  }

  [Fact]
  public async Task RetryAsync_OnFailure_RecordsError()
  {
    var request = await SeedApprovedAsync();
    _client.Throw = true;

    var ok = await CreateDispatcher().RetryAsync(request, CancellationToken.None);

    Assert.False(ok);
    var stored = await _store.GetByIdAsync(request.Id, CancellationToken.None);
    Assert.NotNull(stored!.DispatchError);
  }

  [Fact]
  public async Task RetryStuckAsync_ReSearchesDispatchedButNotAvailable_AfterBackoff()
  {
    var request = await SeedApprovedAsync();
    await CreateDispatcher().DispatchAsync(request, CancellationToken.None); // sets DispatchedAt + attempt = now
    // Push the last attempt back beyond the back-off window so it's eligible for an auto re-search.
    await _store.SetDispatchErrorAsync(request.Id, null, DateTime.UtcNow.AddHours(-7), CancellationToken.None);

    await CreateDispatcher().RetryStuckAsync(CancellationToken.None);

    Assert.Single(_client.Retried);
    Assert.Equal(603, _client.Retried[0].TmdbId);
  }

  [Fact]
  public async Task RetryStuckAsync_SkipsRecentlyDispatched()
  {
    var request = await SeedApprovedAsync();
    await CreateDispatcher().DispatchAsync(request, CancellationToken.None); // attempt = now → within back-off

    await CreateDispatcher().RetryStuckAsync(CancellationToken.None);

    Assert.Empty(_client.Retried);
  }

  [Fact]
  public async Task RetryStuckAsync_PastTheRetryWindow_WarnsTheRequesterOnce()
  {
    var request = await SeedApprovedAsync();
    // Searched for past the 14-day window since it was sent: the media was never found.
    await _store.MarkDispatchedAsync(request.Id, DateTime.UtcNow.AddDays(-20), CancellationToken.None);
    await _store.SetDispatchErrorAsync(request.Id, null, DateTime.UtcNow.AddHours(-7), CancellationToken.None);

    await CreateDispatcher().RetryStuckAsync(CancellationToken.None);
    await CreateDispatcher().RetryStuckAsync(CancellationToken.None); // a second sweep must stay silent

    Assert.Empty(_client.Retried); // past the window we stop searching...
    Assert.Single(_notifier.Personal, e => e.Kind == PersonalNotifyKind.Decision); // ...but say so, once
    var after = await _store.GetByIdAsync(request.Id, CancellationToken.None);
    Assert.NotNull(after!.NotFoundNotifiedAt);
  }

  [Fact]
  public async Task RetryStuckAsync_RequestedLongBeforeItsRelease_IsNotGivenUpOnWhenSent()
  {
    // The reported bug: an episode requested five weeks before it aired was sent on its air date and
    // reported "not found" in the same instant, because the window was counted from the request.
    var request = await SeedApprovedAsync();
    var stored = await _store.GetByIdAsync(request.Id, CancellationToken.None);
    stored!.RequestedAt = DateTime.UtcNow.AddDays(-35);
    stored.DesiredAt = DateTime.UtcNow.AddMinutes(-5);
    await _store.MarkDispatchedAsync(request.Id, DateTime.UtcNow.AddMinutes(-5), CancellationToken.None);

    await CreateDispatcher().RetryStuckAsync(CancellationToken.None);

    Assert.Empty(_notifier.Personal);
    Assert.Null((await _store.GetByIdAsync(request.Id, CancellationToken.None))!.NotFoundNotifiedAt);
  }

  [Fact]
  public async Task RetryStuckAsync_EpisodeNotAiredYet_IsNotSearchedFor()
  {
    // Sent before its air date was known; the date learned since is still ahead.
    var request = await SeedApprovedAsync();
    await _store.MarkDispatchedAsync(request.Id, DateTime.UtcNow.AddDays(-20), CancellationToken.None);
    await _store.RescheduleAsync(request.Id, "2099-01-01", DateTime.UtcNow.AddDays(7), CancellationToken.None);

    await CreateDispatcher().RetryStuckAsync(CancellationToken.None);

    Assert.Empty(_client.Retried);
    Assert.Empty(_notifier.Personal);
  }

  [Fact]
  public async Task RetryStuckAsync_PrematureNotFound_IsWithdrawnAndSearchedAgain()
  {
    // Stamped the instant it was sent (the old rule); its real window has barely started.
    var request = await SeedApprovedAsync();
    await _store.MarkDispatchedAsync(request.Id, DateTime.UtcNow.AddHours(-7), CancellationToken.None);
    await _store.MarkNotFoundNotifiedAsync(request.Id, DateTime.UtcNow.AddHours(-7), CancellationToken.None);

    await CreateDispatcher().RetryStuckAsync(CancellationToken.None);

    Assert.Null((await _store.GetByIdAsync(request.Id, CancellationToken.None))!.NotFoundNotifiedAt);
    Assert.Single(_client.Retried);
    Assert.Empty(_notifier.Personal);
  }

  [Fact]
  public async Task RetryStuckAsync_EarlyNotFoundWhoseWindowHasSinceRunOut_StaysWithoutASecondWarning()
  {
    var request = await SeedApprovedAsync();
    await _store.MarkDispatchedAsync(request.Id, DateTime.UtcNow.AddDays(-20), CancellationToken.None);
    await _store.MarkNotFoundNotifiedAsync(request.Id, DateTime.UtcNow.AddDays(-13), CancellationToken.None);

    await CreateDispatcher().RetryStuckAsync(CancellationToken.None);

    Assert.NotNull((await _store.GetByIdAsync(request.Id, CancellationToken.None))!.NotFoundNotifiedAt);
    Assert.Empty(_client.Retried);
    Assert.Empty(_notifier.Personal);
  }

  [Fact]
  public async Task RetryStuckAsync_SkipsNotYetDispatched()
  {
    await SeedApprovedAsync(); // approved but DispatchedAt == null → DispatchDueAsync handles it, not the retry backstop

    await CreateDispatcher().RetryStuckAsync(CancellationToken.None);

    Assert.Empty(_client.Retried);
  }

  private async Task<RequestRecord> SeedApprovedShowAsync(int? season, int? episode, Guid? user = null, RequestStatus status = RequestStatus.Approved, int tmdbId = 1396)
  {
    var created = await _store.CreateAsync(
      new RequestRecord { UserId = user ?? Guid.NewGuid(), TmdbId = tmdbId, MediaType = "tv", Title = "BB", Season = season, Episode = episode },
      CancellationToken.None);
    return (await _store.UpdateStatusAsync(created.Id, status, Guid.NewGuid(), CancellationToken.None))!;
  }

  [Fact]
  public async Task CancelAsync_WaitsForTheTitlesDispatchStillRunning_ThenWithdrawsIt()
  {
    // The user cancels while the dispatch is still adding the series: the cleanup must come after it, or
    // the dispatch finishing later would monitor the cancelled request again.
    var request = await SeedApprovedShowAsync(1, null);
    _client.DispatchGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var dispatcher = CreateDispatcher();

    var dispatching = dispatcher.DispatchAsync(request, CancellationToken.None);
    await _client.DispatchStarted.Task;
    await _store.CancelAsync(request.Id, request.UserId, CancellationToken.None);
    var cancelling = dispatcher.CancelAsync(request, CancellationToken.None);
    await Task.Delay(50);
    Assert.False(cancelling.IsCompleted); // held behind the dispatch

    _client.DispatchGate.SetResult();
    await Task.WhenAll(dispatching, cancelling);

    Assert.Equal(new[] { "dispatch", "cancel" }, _client.Calls);
  }

  [Fact]
  public async Task DispatchAsync_RequestCancelledMeanwhile_IsNotSent()
  {
    var request = await SeedApprovedShowAsync(1, 2);
    await _store.CancelAsync(request.Id, request.UserId, CancellationToken.None);

    var sent = await CreateDispatcher().DispatchAsync(request, CancellationToken.None);

    Assert.False(sent);
    Assert.Empty(_client.Dispatched);
  }

  [Fact]
  public async Task RetryAsync_RequestCancelledMeanwhile_IsNotSearched()
  {
    var request = await SeedApprovedShowAsync(1, 2);
    await _store.CancelAsync(request.Id, request.UserId, CancellationToken.None);

    var retried = await CreateDispatcher().RetryAsync(request, CancellationToken.None);

    Assert.False(retried);
    Assert.Empty(_client.Retried);
  }

  [Fact]
  public async Task CancelAndPurge_TellTheBackendWhatTheTitlesOtherActiveRequestsStillWant()
  {
    var request = await SeedApprovedShowAsync(1, null);
    await SeedApprovedShowAsync(1, 2);                                 // another user's episode: kept
    await SeedApprovedShowAsync(1, 3, status: RequestStatus.Pending);  // pending: kept, it will want it
    await SeedApprovedShowAsync(1, 4, status: RequestStatus.Denied);   // denied: wants nothing
    await SeedApprovedShowAsync(1, 5, tmdbId: 99);                     // another title
    var dispatcher = CreateDispatcher();

    await dispatcher.CancelAsync(request, CancellationToken.None);
    await dispatcher.PurgeAsync(request, CancellationToken.None);

    var expected = new[] { new RequestScope("tv", 1396, 1, 2), new RequestScope("tv", 1396, 1, 3) };
    Assert.Equal(expected.OrderBy(k => k.Episode), _client.Cancelled.Single().KeepScopes.OrderBy(k => k.Episode));
    Assert.Equal(expected.OrderBy(k => k.Episode), _client.Purged.Single().KeepScopes.OrderBy(k => k.Episode));
  }

  [Fact]
  public async Task PurgeAsync_DelegatesToActiveClient()
  {
    var request = await SeedApprovedAsync();

    await CreateDispatcher().PurgeAsync(request, CancellationToken.None);

    Assert.Single(_client.Purged);
    Assert.Equal(603, _client.Purged[0].TmdbId);
  }

  [Fact]
  public async Task RescanAsync_DelegatesToActiveClient()
  {
    var request = await SeedApprovedAsync();

    await CreateDispatcher().RescanAsync(request, CancellationToken.None);

    Assert.Single(_client.Rescanned);
    Assert.Equal(603, _client.Rescanned[0].TmdbId);
  }

  [Fact]
  public async Task TestActiveAsync_NoneBackend_Throws()
  {
    _config.DownloadBackend = "none";

    await Assert.ThrowsAsync<InvalidOperationException>(() => CreateDispatcher().TestActiveAsync(CancellationToken.None));
  }

  private sealed class RecordingActivityLog : IActivityLog
  {
    public List<string> Messages { get; } = new();

    public Task LogAsync(string level, string category, string message, CancellationToken cancellationToken)
      => LogAsync(level, category, message, null, cancellationToken);

    public Task LogAsync(string level, string category, string message, string? user, CancellationToken cancellationToken)
    {
      lock (Messages)
      {
        Messages.Add(message);
      }

      return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ActivityEntry>> QueryAsync(string? term, string? category, string? level, string? user, int limit, CancellationToken cancellationToken)
      => Task.FromResult<IReadOnlyList<ActivityEntry>>(new List<ActivityEntry>());
  }

  private sealed class FakeDownloadClient : IDownloadClient
  {
    public List<DownloadDispatch> Dispatched { get; } = new();

    public List<DownloadDispatch> Cancelled { get; } = new();

    public List<DownloadDispatch> Purged { get; } = new();

    public List<DownloadDispatch> Rescanned { get; } = new();

    public List<DownloadDispatch> Retried { get; } = new();

    public bool Throw { get; set; }

    /// <summary>Gets the order in which dispatches and cancellations reached the backend.</summary>
    public List<string> Calls { get; } = new();

    /// <summary>Gets or sets a gate a dispatch waits on, to hold it "in flight".</summary>
    public TaskCompletionSource? DispatchGate { get; set; }

    /// <summary>Gets a signal raised once a dispatch has reached the backend.</summary>
    public TaskCompletionSource DispatchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Backend => "webhook";

    public bool IsConfigured(PluginConfiguration config) => true;

    public async Task DispatchAsync(DownloadDispatch dispatch, CancellationToken cancellationToken)
    {
      if (Throw)
      {
        throw new InvalidOperationException("boom");
      }

      DispatchStarted.TrySetResult();
      if (DispatchGate is not null)
      {
        await DispatchGate.Task.ConfigureAwait(false);
      }

      Dispatched.Add(dispatch);
      Calls.Add("dispatch");
    }

    public Task TestAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task CancelAsync(DownloadDispatch dispatch, CancellationToken cancellationToken)
    {
      Cancelled.Add(dispatch);
      Calls.Add("cancel");
      return Task.CompletedTask;
    }

    public Task RescanAsync(DownloadDispatch dispatch, CancellationToken cancellationToken)
    {
      Rescanned.Add(dispatch);
      return Task.CompletedTask;
    }

    public Task<bool> PurgeAsync(DownloadDispatch dispatch, CancellationToken cancellationToken)
    {
      Purged.Add(dispatch);
      return Task.FromResult(true);
    }

    public Task RetryAsync(DownloadDispatch dispatch, CancellationToken cancellationToken)
    {
      if (Throw)
      {
        throw new InvalidOperationException("boom");
      }

      Retried.Add(dispatch);
      return Task.CompletedTask;
    }
  }
}
