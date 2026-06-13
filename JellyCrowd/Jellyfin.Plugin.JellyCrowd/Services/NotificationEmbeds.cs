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
  /// <returns>A serializable payload object (<c>{ embeds: [ ... ] }</c>).</returns>
  public static object BuildRequest(
    RequestRecord request,
    NotificationEvent notificationEvent,
    string subject,
    string body,
    string? overview,
    string? posterPath,
    string username,
    DateTime timestampUtc)
  {
    ArgumentNullException.ThrowIfNull(request);

    var fields = new List<object>
    {
      new { name = "Requested by", value = username, inline = true },
      new { name = "Status", value = StatusText(notificationEvent), inline = true },
    };

    if (request.Season.HasValue)
    {
      fields.Add(new { name = "Season", value = request.Season.Value.ToString(CultureInfo.InvariantCulture), inline = true });
    }

    var embed = new Dictionary<string, object?>
    {
      ["title"] = subject,
      ["description"] = string.IsNullOrWhiteSpace(overview) ? body : overview,
      ["color"] = ColorFor(notificationEvent),
      ["timestamp"] = timestampUtc.ToString("o", CultureInfo.InvariantCulture),
      ["url"] = TmdbUrl(request.MediaType, request.TmdbId),
      ["fields"] = fields,
    };

    if (!string.IsNullOrWhiteSpace(posterPath))
    {
      embed["thumbnail"] = new { url = PosterBaseUrl + posterPath };
    }

    return new { embeds = new[] { embed } };
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

  private static int ColorFor(NotificationEvent notificationEvent) => notificationEvent switch
  {
    NotificationEvent.Created => CreatedColor,
    NotificationEvent.Approved => ApprovedColor,
    NotificationEvent.Available => AvailableColor,
    NotificationEvent.Denied => DeniedColor,
    _ => CreatedColor
  };

  private static string StatusText(NotificationEvent notificationEvent) => notificationEvent switch
  {
    NotificationEvent.Created => "Pending",
    NotificationEvent.Approved => "Approved",
    NotificationEvent.Available => "Available",
    NotificationEvent.Denied => "Denied",
    _ => "Updated"
  };
}
