using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="JsonRequestStore"/>.
/// </summary>
public sealed class JsonRequestStoreTests : IDisposable
{
  private readonly string _path = Path.Combine(Path.GetTempPath(), "jellycrowd-tests", Guid.NewGuid() + ".json");
  private readonly JsonRequestStore _store;

  public JsonRequestStoreTests()
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

  [Fact]
  public async Task CreateAsync_AssignsIdStatusAndTimestamp()
  {
    var created = await _store.CreateAsync(NewRecord(Guid.NewGuid()), CancellationToken.None);

    Assert.NotEqual(Guid.Empty, created.Id);
    Assert.Equal(RequestStatus.Pending, created.Status);
    Assert.NotEqual(default, created.RequestedAt);
  }

  [Fact]
  public async Task GetByUserAsync_ReturnsOnlyThatUser()
  {
    var alice = Guid.NewGuid();
    var bob = Guid.NewGuid();
    await _store.CreateAsync(NewRecord(alice, 1), CancellationToken.None);
    await _store.CreateAsync(NewRecord(bob, 2), CancellationToken.None);

    var mine = await _store.GetByUserAsync(alice, CancellationToken.None);

    Assert.Single(mine);
    Assert.Equal(alice, mine[0].UserId);
  }

  [Fact]
  public async Task ExistsActiveAsync_TrueForPending_FalseForDenied()
  {
    var user = Guid.NewGuid();
    var created = await _store.CreateAsync(NewRecord(user, 42), CancellationToken.None);

    Assert.True(await _store.ExistsActiveAsync(user, 42, "movie", null, null, CancellationToken.None));

    await _store.UpdateStatusAsync(created.Id, RequestStatus.Denied, Guid.NewGuid(), CancellationToken.None);

    Assert.False(await _store.ExistsActiveAsync(user, 42, "movie", null, null, CancellationToken.None));
  }

  [Fact]
  public async Task ExistsActiveAsync_DistinguishesEpisodes()
  {
    var user = Guid.NewGuid();
    await _store.CreateAsync(
      new RequestRecord { UserId = user, TmdbId = 7, MediaType = "tv", Title = "Show", Season = 2, Episode = 3 },
      CancellationToken.None);

    Assert.True(await _store.ExistsActiveAsync(user, 7, "tv", 2, 3, CancellationToken.None));
    Assert.False(await _store.ExistsActiveAsync(user, 7, "tv", 2, 4, CancellationToken.None));
    Assert.False(await _store.ExistsActiveAsync(user, 7, "tv", 2, null, CancellationToken.None));
  }

  [Fact]
  public async Task UpdateStatusAsync_SetsStatusAndDecider()
  {
    var admin = Guid.NewGuid();
    var created = await _store.CreateAsync(NewRecord(Guid.NewGuid()), CancellationToken.None);

    var updated = await _store.UpdateStatusAsync(created.Id, RequestStatus.Approved, admin, CancellationToken.None);

    Assert.NotNull(updated);
    Assert.Equal(RequestStatus.Approved, updated!.Status);
    Assert.Equal(admin, updated.DecidedBy);
    Assert.NotNull(updated.DecidedAt);
  }

  [Fact]
  public async Task MarkAvailableAsync_SetsStatusItemIdAndAvailableAt()
  {
    var created = await _store.CreateAsync(NewRecord(Guid.NewGuid()), CancellationToken.None);

    var updated = await _store.MarkAvailableAsync(created.Id, "deadbeef", CancellationToken.None);

    Assert.NotNull(updated);
    Assert.Equal(RequestStatus.Available, updated!.Status);
    Assert.Equal("deadbeef", updated.JellyfinItemId);
    Assert.NotNull(updated.AvailableAt);
  }

  [Fact]
  public async Task CancelDeletionAsync_ClearsTheFlag()
  {
    var user = Guid.NewGuid();
    var created = await _store.CreateAsync(NewRecord(user), CancellationToken.None);
    await _store.MarkAvailableAsync(created.Id, "item", CancellationToken.None);
    await _store.RequestDeletionAsync(created.Id, user, CancellationToken.None);

    var updated = await _store.CancelDeletionAsync(created.Id, user, CancellationToken.None);

    Assert.NotNull(updated);
    Assert.Null(updated!.DeletionRequestedAt);
    Assert.Null(await _store.CancelDeletionAsync(created.Id, user, CancellationToken.None)); // nothing to cancel now
  }

  [Fact]
  public async Task CancelAsync_AllowsPendingAndApproved_NotAvailable()
  {
    var user = Guid.NewGuid();
    var pending = await _store.CreateAsync(NewRecord(user, 1), CancellationToken.None);
    var approved = await _store.CreateAsync(NewRecord(user, 2), CancellationToken.None);
    await _store.UpdateStatusAsync(approved.Id, RequestStatus.Approved, Guid.NewGuid(), CancellationToken.None);
    var available = await _store.CreateAsync(NewRecord(user, 3), CancellationToken.None);
    await _store.MarkAvailableAsync(available.Id, "item", CancellationToken.None);

    Assert.True(await _store.CancelAsync(pending.Id, user, CancellationToken.None));
    Assert.True(await _store.CancelAsync(approved.Id, user, CancellationToken.None));
    Assert.False(await _store.CancelAsync(available.Id, user, CancellationToken.None)); // available -> not cancellable
  }

  [Fact]
  public async Task UpdateStatusAsync_UnknownId_ReturnsNull()
  {
    Assert.Null(await _store.UpdateStatusAsync(Guid.NewGuid(), RequestStatus.Approved, Guid.NewGuid(), CancellationToken.None));
  }

  [Fact]
  public async Task CountUserRequestsSinceAsync_CountsRecentNonDenied()
  {
    var user = Guid.NewGuid();
    await _store.CreateAsync(NewRecord(user, 1), CancellationToken.None);
    var denied = await _store.CreateAsync(NewRecord(user, 2), CancellationToken.None);
    await _store.UpdateStatusAsync(denied.Id, RequestStatus.Denied, Guid.NewGuid(), CancellationToken.None);

    var past = DateTime.UtcNow.AddDays(-7);
    var future = DateTime.UtcNow.AddDays(1);

    Assert.Equal(1, await _store.CountUserRequestsSinceAsync(user, past, CancellationToken.None));
    Assert.Equal(0, await _store.CountUserRequestsSinceAsync(user, future, CancellationToken.None));
  }

  [Fact]
  public async Task Persistence_SurvivesNewInstance()
  {
    await _store.CreateAsync(NewRecord(Guid.NewGuid()), CancellationToken.None);

    var reopened = new JsonRequestStore(_path);
    try
    {
      var all = await reopened.GetAllAsync(CancellationToken.None);
      Assert.Single(all);
    }
    finally
    {
      reopened.Dispose();
    }
  }

  [Fact]
  public async Task MarkDispatchedAsync_SetsDispatchedAt()
  {
    var created = await _store.CreateAsync(NewRecord(Guid.NewGuid()), CancellationToken.None);
    var when = new DateTime(2026, 6, 13, 10, 0, 0, DateTimeKind.Utc);

    var updated = await _store.MarkDispatchedAsync(created.Id, when, CancellationToken.None);

    Assert.NotNull(updated);
    Assert.Equal(when, updated!.DispatchedAt);
    Assert.Null(await _store.MarkDispatchedAsync(Guid.NewGuid(), when, CancellationToken.None));
  }

  [Fact]
  public async Task GetDueForDispatchAsync_ReturnsApprovedNotDispatchedAndDue()
  {
    var admin = Guid.NewGuid();
    var now = DateTime.UtcNow;

    // Approved + due (no desired date) -> included.
    var due = await _store.CreateAsync(NewRecord(Guid.NewGuid(), 1), CancellationToken.None);
    await _store.UpdateStatusAsync(due.Id, RequestStatus.Approved, admin, CancellationToken.None);

    // Approved but already dispatched -> excluded.
    var dispatched = await _store.CreateAsync(NewRecord(Guid.NewGuid(), 2), CancellationToken.None);
    await _store.UpdateStatusAsync(dispatched.Id, RequestStatus.Approved, admin, CancellationToken.None);
    await _store.MarkDispatchedAsync(dispatched.Id, now, CancellationToken.None);

    // Approved but desired in the future -> excluded.
    var future = await _store.CreateAsync(
      new RequestRecord { UserId = Guid.NewGuid(), TmdbId = 3, MediaType = "movie", Title = "Later", DesiredAt = now.AddDays(2) },
      CancellationToken.None);
    await _store.UpdateStatusAsync(future.Id, RequestStatus.Approved, admin, CancellationToken.None);

    // Still pending -> excluded.
    await _store.CreateAsync(NewRecord(Guid.NewGuid(), 4), CancellationToken.None);

    var result = await _store.GetDueForDispatchAsync(now, CancellationToken.None);

    Assert.Single(result);
    Assert.Equal(due.Id, result[0].Id);
  }

  private static RequestRecord NewRecord(Guid userId, int tmdbId = 1)
    => new() { UserId = userId, TmdbId = tmdbId, MediaType = "movie", Title = "Test" };

  [Fact]
  public async Task ExpireOwnershipsAsync_RemovesOnlyLapsedAvailableNotFlaggedNotOther()
  {
    var user = Guid.NewGuid();
    async Task<RequestRecord> Avail(int id, DateTime availableAt, DateTime? deletionAt = null)
    {
      var r = await _store.CreateAsync(new RequestRecord { UserId = user, TmdbId = id, MediaType = "movie", Title = "T" + id, Status = RequestStatus.Available, AvailableAt = availableAt, DeletionRequestedAt = deletionAt }, CancellationToken.None);
      return r;
    }

    var lapsed = await Avail(1, DateTime.UtcNow.AddDays(-100));
    await Avail(2, DateTime.UtcNow.AddDays(-1));                                  // fresh -> kept
    await Avail(3, DateTime.UtcNow.AddDays(-100), DateTime.UtcNow);              // flagged -> kept (deletion flow owns it)
    await _store.CreateAsync(new RequestRecord { UserId = user, TmdbId = 4, MediaType = "movie", Title = "Pending", Status = RequestStatus.Pending }, CancellationToken.None);

    var removed = await _store.ExpireOwnershipsAsync(DateTime.UtcNow.AddDays(-90), CancellationToken.None);

    Assert.Equal(1, removed);
    var all = await _store.GetAllAsync(CancellationToken.None);
    Assert.DoesNotContain(all, r => r.Id == lapsed.Id);
    Assert.Equal(3, all.Count);
  }

  [Fact]
  public async Task RenewAvailableAsync_ResetsAvailableAtAndClearsDeletionFlag()
  {
    var created = await _store.CreateAsync(new RequestRecord { UserId = Guid.NewGuid(), TmdbId = 1, MediaType = "movie", Title = "T", Status = RequestStatus.Available, AvailableAt = DateTime.UtcNow.AddDays(-50), DeletionRequestedAt = DateTime.UtcNow.AddDays(-1) }, CancellationToken.None);
    var now = DateTime.UtcNow;

    var renewed = await _store.RenewAvailableAsync(created.Id, now, CancellationToken.None);

    Assert.NotNull(renewed);
    Assert.Equal(now, renewed!.AvailableAt);
    Assert.Null(renewed.DeletionRequestedAt);
  }

  [Fact]
  public async Task LoadsLegacyBareArray_ThenRewritesAsVersionedEnvelope()
  {
    // Simulate a file written by a pre-versioning build: a bare JSON array.
    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    var legacy = new[] { new RequestRecord { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), TmdbId = 42, MediaType = "movie", Title = "Legacy" } };
    await File.WriteAllTextAsync(_path, System.Text.Json.JsonSerializer.Serialize(legacy));

    // The store reads the legacy format transparently.
    var all = await _store.GetAllAsync(CancellationToken.None);
    Assert.Single(all);
    Assert.Equal("Legacy", all[0].Title);

    // After a mutation it is rewritten as a versioned envelope.
    await _store.CreateAsync(NewRecord(Guid.NewGuid(), 7), CancellationToken.None);
    var json = await File.ReadAllTextAsync(_path);
    Assert.Contains("\"SchemaVersion\"", json, StringComparison.Ordinal);
    Assert.Contains("\"Items\"", json, StringComparison.Ordinal);
  }
}
