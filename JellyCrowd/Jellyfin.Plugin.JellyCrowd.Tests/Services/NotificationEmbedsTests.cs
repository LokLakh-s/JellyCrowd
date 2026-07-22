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
  private static readonly System.Func<string, string> En = ServerStrings.For("en");

  private static readonly DateTime Stamp = new(2026, 6, 13, 10, 0, 0, DateTimeKind.Utc);

  private static DiscordEmbedOptions Opts(NotificationEvent ev) => new() { Color = NotificationEmbeds.DefaultColorFor(ev) };

  private static JsonElement Root(object payload) => JsonDocument.Parse(JsonSerializer.Serialize(payload)).RootElement;

  private static JsonElement FirstEmbed(object payload) => Root(payload).GetProperty("embeds")[0];

  [Fact]
  public void BuildRequest_ProducesEmbed_WithTitleColorTimestampUrlAndThumbnail()
  {
    var request = new RequestRecord { MediaType = "movie", TmdbId = 438631, Title = "Dune", PosterPath = "/poster.jpg" };

    var embed = FirstEmbed(NotificationEmbeds.BuildRequest(
      request, NotificationEvent.Created, "New request: Dune", "fallback body", "A synopsis.", "/poster.jpg", "alice", Stamp, Opts(NotificationEvent.Created), En));

    Assert.Equal("New request: Dune", embed.GetProperty("title").GetString());
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
      request, NotificationEvent.Created, "s", "fallback body", null, null, "bob", Stamp, Opts(NotificationEvent.Created), En));

    Assert.Equal("fallback body", embed.GetProperty("description").GetString());
    Assert.False(embed.TryGetProperty("thumbnail", out _));
  }

  [Fact]
  public void BuildRequest_IncludesRequestedByAndStatusFields()
  {
    var request = new RequestRecord { MediaType = "movie", TmdbId = 1, Title = "X" };

    var fields = FirstEmbed(NotificationEmbeds.BuildRequest(
      request, NotificationEvent.Approved, "s", "b", null, null, "alice", Stamp, Opts(NotificationEvent.Approved), En)).GetProperty("fields");

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
      request, NotificationEvent.Available, "s", "b", null, null, "carol", Stamp, Opts(NotificationEvent.Available), En)).GetProperty("fields");

    Assert.Equal(3, fields.GetArrayLength());
    Assert.Equal("Season", fields[2].GetProperty("name").GetString());
    Assert.Equal("2", fields[2].GetProperty("value").GetString());
  }

  [Theory]
  [InlineData(NotificationEvent.Created, 0x3B82F6, "Pending approval")]
  [InlineData(NotificationEvent.Approved, 0x6366F1, "Approved")]
  [InlineData(NotificationEvent.Available, 0x10B981, "Available")]
  [InlineData(NotificationEvent.Denied, 0xEF4444, "Denied")]
  // The status wording now comes from the shared catalog, so an embed and an e-mail about the same
  // request say the same thing ("Pending approval", not "Pending" on one and "Pending approval" on
  // the other).
  public void DefaultColorFor_MapsEventToColorAndStatus(NotificationEvent ev, int color, string status)
  {
    var request = new RequestRecord { MediaType = "movie", TmdbId = 1, Title = "X" };

    var embed = FirstEmbed(NotificationEmbeds.BuildRequest(request, ev, "s", "b", null, null, "u", Stamp, Opts(ev), En));

    Assert.Equal(color, embed.GetProperty("color").GetInt32());
    Assert.Equal(status, embed.GetProperty("fields")[1].GetProperty("value").GetString());
  }

  [Fact]
  public void BuildRequest_HonorsCustomColor()
  {
    var request = new RequestRecord { MediaType = "movie", TmdbId = 1, Title = "X" };

    var embed = FirstEmbed(NotificationEmbeds.BuildRequest(
      request, NotificationEvent.Created, "s", "b", null, null, "u", Stamp, new DiscordEmbedOptions { Color = 0xABCDEF }, En));

    Assert.Equal(0xABCDEF, embed.GetProperty("color").GetInt32());
  }

  [Fact]
  public void BuildRequest_ContentToggles_HideFieldsLinkPosterAndSynopsis()
  {
    var request = new RequestRecord { MediaType = "tv", TmdbId = 1, Title = "X", Season = 1, PosterPath = "/p.jpg" };
    var options = new DiscordEmbedOptions
    {
      Color = 0,
      ShowRequestedBy = false,
      ShowStatus = false,
      ShowSeason = false,
      ShowLink = false,
      ShowPoster = false,
      ShowSynopsis = false
    };

    var embed = FirstEmbed(NotificationEmbeds.BuildRequest(
      request, NotificationEvent.Created, "s", "fallback", "a synopsis", "/p.jpg", "u", Stamp, options, En));

    Assert.Equal(0, embed.GetProperty("fields").GetArrayLength());
    Assert.False(embed.TryGetProperty("url", out _));
    Assert.False(embed.TryGetProperty("thumbnail", out _));
    Assert.Equal("fallback", embed.GetProperty("description").GetString()); // synopsis suppressed
  }

  [Fact]
  public void BuildRequest_Mention_AddsTopLevelContent()
  {
    var request = new RequestRecord { MediaType = "movie", TmdbId = 1, Title = "X" };

    var payload = Root(NotificationEmbeds.BuildRequest(
      request, NotificationEvent.Created, "s", "b", null, null, "u", Stamp, new DiscordEmbedOptions { Color = 0, Mention = "<@&123>" }, En));

    Assert.Equal("<@&123>", payload.GetProperty("content").GetString());
  }

  [Theory]
  [InlineData("#FF0000", 0xFF0000)]
  [InlineData("00FF00", 0x00FF00)]
  [InlineData("", 0x123456)]
  [InlineData("nope", 0x123456)]
  public void ParseColor_ParsesHexOrFallsBack(string hex, int expected)
    => Assert.Equal(expected, NotificationEmbeds.ParseColor(hex, 0x123456));

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
