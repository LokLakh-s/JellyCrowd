using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="WebInjectionService"/>.
/// </summary>
public class WebInjectionServiceTests
{
  [Fact]
  public async Task StartAsync_WithoutFileTransformation_DoesNotThrow()
  {
    var service = new WebInjectionService(NullLogger<WebInjectionService>.Instance);

    // File Transformation is absent at test runtime: the service must swallow that and let
    // startup continue rather than throwing.
    await service.StartAsync(CancellationToken.None);
    await service.StopAsync(CancellationToken.None);

    Assert.True(true);
  }
}
