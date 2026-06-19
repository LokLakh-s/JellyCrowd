using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Builds Discord embed payloads for request notifications. Pure and network-free so it can be
/// unit tested independently of delivery. The presentation mirrors jelly-quotas: a colored bar,
/// the title, the synopsis as the description, an ISO-8601 timestamp, inline fields and a poster
/// thumbnail. The returned object serializes (via System.Text.Json) to the Discord webhook schema.
/// </summary>
public static class NotificationEmbeds
{
  /// <summary>The accent color used for test embeds (blue).</summary>
  public const int TestColor = 0x3B82F6;

  private const string PosterBaseUrl = "https://image.tmdb.org/t/p/w600_and_h900_bestv2";

  private const int CreatedColor = 0x3B82F6;   // blue
  private const int ApprovedColor = 0x6366F1;  // indigo
  private const int AvailableColor = 0x10B981; // green
  private const int DeniedColor = 0xEF4444;    // red

  /// <summary>
  /// Builds the TMDB web URL for a title.
  /// </summary>
  /// <param name="mediaType">The media type (<c>movie</c> or <c>tv</c>).</param>
  /// <param name="tmdbId">The TMDB identifier.</param>
  /// <returns>The absolute themoviedb.org URL.</returns>
  public static string TmdbUrl(string mediaType, int tmdbId)
    => string.Format(CultureInfo.InvariantCulture, "https://www.themoviedb.org/{0}/{1}", mediaType, tmdbId);

  /// <summary>
  /// Builds the Discord webhook payload for a request lifecycle event.
  /// </summary>
  /// <param name="request">The request the notification is about.</param>
  /// <param name="notificationEvent">The lifecycle event.</param>
  /// <param name="subject">The embed title.</param>
  /// <param name="body">The fallback description used when no synopsis is available.</param>
  /// <param name="overview">The TMDB synopsis, or <c>null</c>.</param>
  /// <param name="posterPath">The TMDB relative poster path, or <c>null</c>.</param>
  /// <param name="username">The requesting user's display name.</param>
  /// <param name="timestampUtc">The embed timestamp (UTC).</param>
  /// <param name="options">Presentation options (color, fields, poster, link, mention).</param>
  /// <returns>A serializable payload object (<c>{ content?, embeds: [ ... ] }</c>).</returns>
  public static object BuildRequest(
    RequestRecord request,
    NotificationEvent notificationEvent,
    string subject,
    string body,
    string? overview,
    string? posterPath,
    string username,
    DateTime timestampUtc,
    DiscordEmbedOptions options)
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(options);

    var fields = new List<object>();
    if (options.ShowRequestedBy)
    {
      fields.Add(new { name = "Requested by", value = username, inline = true });
    }

    if (options.ShowStatus)
    {
      fields.Add(new { name = "Status", value = StatusText(notificationEvent), inline = true });
    }

    if (options.ShowSeason && request.Season.HasValue)
    {
      fields.Add(new { name = "Season", value = request.Season.Value.ToString(CultureInfo.InvariantCulture), inline = true });
    }

    var embed = new Dictionary<string, object?>
    {
      ["title"] = subject,
      ["description"] = options.ShowSynopsis && !string.IsNullOrWhiteSpace(overview) ? overview : body,
      ["color"] = options.Color,
      ["timestamp"] = timestampUtc.ToString("o", CultureInfo.InvariantCulture),
      ["fields"] = fields,
    };

    if (options.ShowLink)
    {
      embed["url"] = TmdbUrl(request.MediaType, request.TmdbId);
    }

    if (options.ShowPoster && !string.IsNullOrWhiteSpace(posterPath))
    {
      embed["thumbnail"] = new { url = PosterBaseUrl + posterPath };
    }

    var payload = new Dictionary<string, object?> { ["embeds"] = new[] { embed } };
    if (!string.IsNullOrWhiteSpace(options.Mention))
    {
      payload["content"] = options.Mention;
    }

    return payload;
  }

  /// <summary>
  /// Builds a minimal Discord webhook payload (title + description), used for channel tests.
  /// </summary>
  /// <param name="subject">The embed title.</param>
  /// <param name="body">The embed description.</param>
  /// <param name="color">The accent color.</param>
  /// <param name="timestampUtc">The embed timestamp (UTC).</param>
  /// <returns>A serializable payload object.</returns>
  public static object BuildSimple(string subject, string body, int color, DateTime timestampUtc)
  {
    var embed = new Dictionary<string, object?>
    {
      ["title"] = subject,
      ["description"] = body,
      ["color"] = color,
      ["timestamp"] = timestampUtc.ToString("o", CultureInfo.InvariantCulture),
    };

    return new { embeds = new[] { embed } };
  }

  /// <summary>
  /// The built-in default embed color for an event (used as a fallback for an unset/invalid config color).
  /// </summary>
  /// <param name="notificationEvent">The lifecycle event.</param>
  /// <returns>The default RGB color.</returns>
  public static int DefaultColorFor(NotificationEvent notificationEvent) => notificationEvent switch
  {
    NotificationEvent.Created => CreatedColor,
    NotificationEvent.Approved => ApprovedColor,
    NotificationEvent.Available => AvailableColor,
    NotificationEvent.Denied => DeniedColor,
    _ => CreatedColor
  };

  /// <summary>
  /// Parses a hex color (<c>#RRGGBB</c> or <c>RRGGBB</c>) into an RGB integer, falling back when invalid.
  /// </summary>
  /// <param name="hex">The hex string, possibly null/empty.</param>
  /// <param name="fallback">The fallback color when parsing fails.</param>
  /// <returns>The parsed RGB integer, or <paramref name="fallback"/>.</returns>
  public static int ParseColor(string? hex, int fallback)
  {
    if (string.IsNullOrWhiteSpace(hex))
    {
      return fallback;
    }

    var trimmed = hex.Trim().TrimStart('#');
    return int.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
      ? value & 0xFFFFFF
      : fallback;
  }

  private static string StatusText(NotificationEvent notificationEvent) => notificationEvent switch
  {
    NotificationEvent.Created => "Pending",
    NotificationEvent.Approved => "Approved",
    NotificationEvent.Available => "Available",
    NotificationEvent.Denied => "Denied",
    _ => "Updated"
  };
}
