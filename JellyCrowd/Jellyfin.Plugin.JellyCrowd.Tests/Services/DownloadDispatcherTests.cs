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
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jellycrowd-tests", Guid.NewGuid() + ".json");
  private readonly JsonRequestStore _store;
  private readonly FakeDownloadClient _client = new();
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

  private DownloadDispatcher CreateDispatcher()
    => new(new IDownloadClient[] { _client }, _store, _ => "tester", () => _config, new NoOpActivityLog(), NullLogger<DownloadDispatcher>.Instance);

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
  public async Task DispatchDueAsync_DispatchesEveryDueRequest()
  {
    await SeedApprovedAsync();
    await SeedApprovedAsync();

    await CreateDispatcher().DispatchDueAsync(CancellationToken.None);

    Assert.Equal(2, _client.Dispatched.Count);
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
  public async Task TestActiveAsync_NoneBackend_Throws()
  {
    _config.DownloadBackend = "none";

    await Assert.ThrowsAsync<InvalidOperationException>(() => CreateDispatcher().TestActiveAsync(CancellationToken.None));
  }

  private sealed class FakeDownloadClient : IDownloadClient
  {
    public List<DownloadDispatch> Dispatched { get; } = new();

    public List<DownloadDispatch> Cancelled { get; } = new();

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
