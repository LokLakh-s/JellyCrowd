using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Services;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="FingerprintExtractor"/>: the same extractor serves intro detection (head of the
/// file) and outro detection (its tail), so the seek is what tells them apart.
/// </summary>
public class FingerprintExtractorTests
{
  private string _args = string.Empty;

  private FingerprintExtractor Create()
  {
    var encoder = Mock.Of<IMediaEncoder>(e => e.EncoderPath == "/usr/bin/ffmpeg");
    var runner = new Mock<IProcessRunner>();
    runner
      .Setup(r => r.RunCaptureBytesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
      .Callback<string, string, int, CancellationToken>((_, args, _, _) => _args = args)
      .ReturnsAsync(Array.Empty<byte>());

    return new FingerprintExtractor(encoder, runner.Object, NullLogger<FingerprintExtractor>.Instance);
  }

  [Fact]
  public async Task Extract_FromTheStart_HasNoSeek()
  {
    await Create().ExtractAsync("/media/ep.mkv", 0, 600, 120, CancellationToken.None);

    Assert.DoesNotContain("-ss", _args, StringComparison.Ordinal);
    Assert.Contains("-t 600 -i \"/media/ep.mkv\"", _args, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Extract_FromTheTail_SeeksBeforeTheInput()
  {
    // -ss BEFORE -i is an input seek: fingerprinting the last 300s of a 24-minute episode must not cost
    // more than fingerprinting its first 300s (otherwise outro analysis would be unusably slow).
    await Create().ExtractAsync("/media/ep.mkv", 1140.5, 300, 120, CancellationToken.None);

    Assert.Contains("-ss 1140.5 -t 300 -i \"/media/ep.mkv\"", _args, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Extract_AlwaysDecodesThePrimaryAudioToMono22k()
  {
    await Create().ExtractAsync("/media/ep.mkv", 10, 300, 120, CancellationToken.None);

    // Chromaprint only matches when both sides are decoded identically.
    Assert.Contains("-map 0:a:0 -ac 1 -ar 22050 -f chromaprint -fp_format raw -", _args, StringComparison.Ordinal);
  }
}
