using System;
using System.Collections.Generic;
using System.IO;
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
  public async Task RetryStuckAsync_SkipsNotYetDispatched()
  {
    await SeedApprovedAsync(); // approved but DispatchedAt == null → DispatchDueAsync handles it, not the retry backstop

    await CreateDispatcher().RetryStuckAsync(CancellationToken.None);

    Assert.Empty(_client.Retried);
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

  private sealed class FakeDownloadClient : IDownloadClient
  {
    public List<DownloadDispatch> Dispatched { get; } = new();

    public List<DownloadDispatch> Cancelled { get; } = new();

    public List<DownloadDispatch> Purged { get; } = new();

    public List<DownloadDispatch> Rescanned { get; } = new();

    public List<DownloadDispatch> Retried { get; } = new();

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

    public Task CancelAsync(DownloadDispatch dispatch, CancellationToken cancellationToken)
    {
      Cancelled.Add(dispatch);
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
