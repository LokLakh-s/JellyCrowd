using System;
using System.Text.Json;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="NotificationEmbeds"/>, the pure Discord embed payload builder.
/// </summary>
public class NotificationEmbedsTests
{
  private static readonly DateTime Stamp = new(2026, 6, 13, 10, 0, 0, DateTimeKind.Utc);

  private static JsonElement FirstEmbed(object payload)
  {
    var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
    return doc.RootElement.GetProperty("embeds")[0];
  }

  [Fact]
  public void BuildRequest_ProducesEmbed_WithTitleColorTimestampUrlAndThumbnail()
  {
    var request = new RequestRecord { MediaType = "movie", TmdbId = 438631, Title = "Dune", PosterPath = "/poster.jpg" };

    var embed = FirstEmbed(NotificationEmbeds.BuildRequest(
      request, NotificationEvent.Created, "New request: Dune", "fallback body", "A synopsis.", "/poster.jpg", "alice", Stamp));

    Assert.Equal("New request: Dune", embed.GetProperty("title").GetString());
    // Description prefers the TMDB synopsis over the fallback body.
    Assert.Equal("A synopsis.", embed.GetProperty("description").GetString());
    Assert.Equal(0x3B82F6, embed.GetProperty("color").GetInt32());
    Assert.Equal("2026-06-13T10:00:00.0000000Z", embed.GetProperty("timestamp").GetString());
    Assert.Equal("https://www.themoviedb.org/movie/438631", embed.GetProperty("url").GetString());
    Assert.Equal(
      "https://image.tmdb.org/t/p/w600_and_h900_bestv2/poster.jpg",
      embed.GetProperty("thumbnail").GetProperty("url").GetString());
  }

  [Fact]
  public void BuildRequest_FallsBackToBody_WhenNoOverview()
  {
    var request = new RequestRecord { MediaType = "movie", TmdbId = 1, Title = "X" };

    var embed = FirstEmbed(NotificationEmbeds.BuildRequest(
      request, NotificationEvent.Created, "s", "fallback body", null, null, "bob", Stamp));

    Assert.Equal("fallback body", embed.GetProperty("description").GetString());
    // No poster path -> no thumbnail property at all.
    Assert.False(embed.TryGetProperty("thumbnail", out _));
  }

  [Fact]
  public void BuildRequest_IncludesRequestedByAndStatusFields()
  {
    var request = new RequestRecord { MediaType = "movie", TmdbId = 1, Title = "X" };

    var fields = FirstEmbed(NotificationEmbeds.BuildRequest(
      request, NotificationEvent.Approved, "s", "b", null, null, "alice", Stamp)).GetProperty("fields");

    Assert.Equal(2, fields.GetArrayLength());
    Assert.Equal("Requested by", fields[0].GetProperty("name").GetString());
    Assert.Equal("alice", fields[0].GetProperty("value").GetString());
    Assert.True(fields[0].GetProperty("inline").GetBoolean());
    Assert.Equal("Status", fields[1].GetProperty("name").GetString());
    Assert.Equal("Approved", fields[1].GetProperty("value").GetString());
  }

  [Fact]
  public void BuildRequest_AddsSeasonField_ForShows()
  {
    var request = new RequestRecord { MediaType = "tv", TmdbId = 1, Title = "Severance", Season = 2 };

    var fields = FirstEmbed(NotificationEmbeds.BuildRequest(
      request, NotificationEvent.Available, "s", "b", null, null, "carol", Stamp)).GetProperty("fields");

    Assert.Equal(3, fields.GetArrayLength());
    Assert.Equal("Season", fields[2].GetProperty("name").GetString());
    Assert.Equal("2", fields[2].GetProperty("value").GetString());
  }

  [Theory]
  [InlineData(NotificationEvent.Created, 0x3B82F6, "Pending")]
  [InlineData(NotificationEvent.Approved, 0x6366F1, "Approved")]
  [InlineData(NotificationEvent.Available, 0x10B981, "Available")]
  [InlineData(NotificationEvent.Denied, 0xEF4444, "Denied")]
  public void BuildRequest_MapsEventToColorAndStatus(NotificationEvent ev, int color, string status)
  {
    var request = new RequestRecord { MediaType = "movie", TmdbId = 1, Title = "X" };

    var embed = FirstEmbed(NotificationEmbeds.BuildRequest(request, ev, "s", "b", null, null, "u", Stamp));

    Assert.Equal(color, embed.GetProperty("color").GetInt32());
    Assert.Equal(status, embed.GetProperty("fields")[1].GetProperty("value").GetString());
  }

  [Fact]
  public void BuildSimple_ProducesTitleDescriptionAndColor()
  {
    var embed = FirstEmbed(NotificationEmbeds.BuildSimple("Test", "Body", NotificationEmbeds.TestColor, Stamp));

    Assert.Equal("Test", embed.GetProperty("title").GetString());
    Assert.Equal("Body", embed.GetProperty("description").GetString());
    Assert.Equal(0x3B82F6, embed.GetProperty("color").GetInt32());
    Assert.False(embed.TryGetProperty("fields", out _));
  }
}
