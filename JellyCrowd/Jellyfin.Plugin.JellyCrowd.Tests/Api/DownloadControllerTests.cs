using System;
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
/// Tests for <see cref="DownloadController"/>.
/// </summary>
public class DownloadControllerTests
{
  [Fact]
  public async Task Test_Success_ReturnsNoContent()
  {
    var controller = new DownloadController(new FakeDispatcher(null));

    var result = await controller.Test(CancellationToken.None);

    Assert.IsType<NoContentResult>(result);
  }

  [Fact]
  public async Task Test_Failure_ReturnsBadRequest()
  {
    var controller = new DownloadController(new FakeDispatcher(new InvalidOperationException("no backend")));

    var result = await controller.Test(CancellationToken.None);

    var obj = Assert.IsType<ObjectResult>(result);
    Assert.Equal(StatusCodes.Status400BadRequest, obj.StatusCode);
  }

  private sealed class FakeDispatcher : IDownloadDispatcher
  {
    private readonly Exception? _error;

    public FakeDispatcher(Exception? error) => _error = error;

    public Task<bool> DispatchAsync(RequestRecord request, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task DispatchDueAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task TestActiveAsync(CancellationToken cancellationToken)
      => _error is null ? Task.CompletedTask : throw _error;
  }
}
