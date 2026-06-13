using System;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="DownloadPayloadBuilder"/>.
/// </summary>
public class DownloadPayloadBuilderTests
{
  [Fact]
  public void Build_MapsFieldsAndYearAndTmdbUrl()
  {
    var request = new RequestRecord
    {
      Id = Guid.NewGuid(),
      UserId = Guid.NewGuid(),
      TmdbId = 603,
      MediaType = "movie",
      Title = "The Matrix",
      ReleaseDate = "1999-03-30",
      Season = null
    };

    var dispatch = DownloadPayloadBuilder.Build(request, "alice");

    Assert.Equal(request.Id, dispatch.RequestId);
    Assert.Equal("alice", dispatch.UserName);
    Assert.Equal(603, dispatch.TmdbId);
    Assert.Equal(1999, dispatch.Year);
    Assert.Equal("https://www.themoviedb.org/movie/603", dispatch.TmdbUrl);
  }

  [Theory]
  [InlineData("2021-05-04", 2021)]
  [InlineData("1980", 1980)]
  [InlineData("", null)]
  [InlineData(null, null)]
  [InlineData("not-a-date", null)]
  public void ParseYear_HandlesVariousInputs(string? releaseDate, int? expected)
  {
    Assert.Equal(expected, DownloadPayloadBuilder.ParseYear(releaseDate));
  }
}
