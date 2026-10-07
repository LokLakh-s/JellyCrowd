using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Api;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Api;

/// <summary>
/// Tests for <see cref="OwnershipController"/>.
/// </summary>
public class OwnershipControllerTests
{
  private static readonly Guid Admin = Guid.NewGuid();
  private static readonly Guid Member = Guid.NewGuid();

  [Fact]
  public async Task List_ReturnsTheServicesMedia()
  {
    var service = new RecordingOwnershipService();
    service.Media.Add(new OwnedMediaDto { MediaType = "movie", TmdbId = 10, Title = "Dune" });

    var result = await Controller(service).List(CancellationToken.None);

    var ok = Assert.IsType<OkObjectResult>(result.Result);
    Assert.Equal("Dune", Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<OwnedMediaDto>>(ok.Value)).Title);
  }

  [Fact]
  public async Task Give_PassesMembersMediaAndAdmin_AndReturnsTheCounts()
  {
    var service = new RecordingOwnershipService();

    var result = await Controller(service).Give(Change(new[] { Member }, 2), CancellationToken.None);

    var ok = Assert.IsType<OkObjectResult>(result.Result);
    Assert.Equal(2, Assert.IsType<OwnershipChangeResult>(ok.Value).Given);
    Assert.Equal(("give", "the-admin", 1, 2), service.Calls.Single());
  }

  [Fact]
  public async Task Remove_PassesMembersMediaAndAdmin()
  {
    var service = new RecordingOwnershipService();

    var result = await Controller(service).Remove(Change(new[] { Member, Guid.NewGuid() }, 1), CancellationToken.None);

    Assert.IsType<OkObjectResult>(result.Result);
    Assert.Equal(("remove", "the-admin", 2, 1), service.Calls.Single());
  }

  [Fact]
  public async Task Give_WithoutMember_IsRefused()
  {
    var service = new RecordingOwnershipService();

    var result = await Controller(service).Give(Change(new[] { Guid.Empty }, 1), CancellationToken.None);

    Assert.IsType<BadRequestObjectResult>(result.Result);
    Assert.Empty(service.Calls);
  }

  [Fact]
  public async Task Remove_WithoutMedia_IsRefused()
  {
    var service = new RecordingOwnershipService();

    var result = await Controller(service).Remove(Change(new[] { Member }, 0), CancellationToken.None);

    Assert.IsType<BadRequestObjectResult>(result.Result);
    Assert.Empty(service.Calls);
  }

  [Fact]
  public async Task Give_TooManyPairs_IsRefused()
  {
    var service = new RecordingOwnershipService();
    var members = Enumerable.Range(0, 2).Select(_ => Guid.NewGuid()).ToArray();

    var result = await Controller(service).Give(Change(members, (OwnershipController.MaxPairs / 2) + 1), CancellationToken.None);

    Assert.IsType<BadRequestObjectResult>(result.Result);
    Assert.Empty(service.Calls);
  }

  [Fact]
  public async Task Give_NullBody_IsRefused()
  {
    var result = await Controller(new RecordingOwnershipService()).Give(null!, CancellationToken.None);

    Assert.IsType<BadRequestObjectResult>(result.Result);
  }

  private static OwnershipChangeDto Change(IEnumerable<Guid> members, int media)
    => new()
    {
      UserIds = new Collection<Guid>(members.ToList()),
      Media = new Collection<OwnershipMediaRef>(Enumerable.Range(1, media).Select(i => new OwnershipMediaRef { MediaType = "movie", TmdbId = i, Title = "M" + i }).ToList())
    };

  private static OwnershipController Controller(IOwnershipService service)
    => new(service, new AdminAccessor(), id => id == Admin ? "the-admin" : "someone")
    {
      ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

  private sealed class AdminAccessor : ICurrentUserAccessor
  {
    public Task<Guid> GetUserIdAsync(HttpRequest request) => Task.FromResult(Admin);

    public Task<bool> IsAdministratorAsync(HttpRequest request) => Task.FromResult(true);
  }

  private sealed class RecordingOwnershipService : IOwnershipService
  {
    public List<OwnedMediaDto> Media { get; } = new();

    public List<(string Op, string Admin, int Members, int Media)> Calls { get; } = new();

    public Task<IReadOnlyList<OwnedMediaDto>> ListAsync(CancellationToken cancellationToken)
      => Task.FromResult<IReadOnlyList<OwnedMediaDto>>(Media);

    public Task<OwnershipGrant?> GiveOneAsync(Guid userId, OwnershipMediaRef media, CancellationToken cancellationToken)
      => Task.FromResult<OwnershipGrant?>(null);

    public Task<OwnershipChangeResult> GiveAsync(IReadOnlyCollection<Guid> userIds, IReadOnlyCollection<OwnershipMediaRef> media, string adminName, CancellationToken cancellationToken)
    {
      Calls.Add(("give", adminName, userIds.Count, media.Count));
      return Task.FromResult(new OwnershipChangeResult { Given = userIds.Count * media.Count });
    }

    public Task<OwnershipChangeResult> RemoveAsync(IReadOnlyCollection<Guid> userIds, IReadOnlyCollection<OwnershipMediaRef> media, string adminName, CancellationToken cancellationToken)
    {
      Calls.Add(("remove", adminName, userIds.Count, media.Count));
      return Task.FromResult(new OwnershipChangeResult { Removed = userIds.Count * media.Count });
    }
  }
}
