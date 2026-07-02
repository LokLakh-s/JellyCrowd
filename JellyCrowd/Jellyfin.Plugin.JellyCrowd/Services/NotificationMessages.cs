using System;
using System.Globalization;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Builds the subject and body text for request notifications. Pure and network-free so it can be
/// unit tested independently of the delivery channels.
/// </summary>
public static class NotificationMessages
{
  /// <summary>
  /// Builds the notification subject and body for a request event.
  /// </summary>
  /// <param name="request">The request the notification is about.</param>
  /// <param name="notificationEvent">The lifecycle event.</param>
  /// <returns>A subject/body pair.</returns>
  public static (string Subject, string Body) Build(RequestRecord request, NotificationEvent notificationEvent)
  {
    ArgumentNullException.ThrowIfNull(request);

    var kind = string.Equals(request.MediaType, "tv", StringComparison.Ordinal) ? "show" : "movie";
    var title = request.Season.HasValue
      ? string.Format(CultureInfo.InvariantCulture, "{0} (Season {1})", request.Title, request.Season.Value)
      : request.Title;

    return notificationEvent switch
    {
      NotificationEvent.Created => (
        string.Format(CultureInfo.InvariantCulture, "New request: {0}", title),
        string.Format(CultureInfo.InvariantCulture, "A new {0} request is pending approval: {1}.", kind, title)),
      NotificationEvent.Approved => (
        string.Format(CultureInfo.InvariantCulture, "Request approved: {0}", title),
        string.Format(CultureInfo.InvariantCulture, "The {0} request \"{1}\" was approved.", kind, title)),
      NotificationEvent.Denied => (
        string.Format(CultureInfo.InvariantCulture, "Request denied: {0}", title),
        string.Format(CultureInfo.InvariantCulture, "The {0} request \"{1}\" was denied.", kind, title)),
      NotificationEvent.Available => (
        string.Format(CultureInfo.InvariantCulture, "Now available: {0}", title),
        string.Format(CultureInfo.InvariantCulture, "\"{0}\" is now available in the library.", title)),
      NotificationEvent.Failed => (
        string.Format(CultureInfo.InvariantCulture, "Request needs attention: {0}", title),
        string.Format(CultureInfo.InvariantCulture, "The {0} request \"{1}\" could not be fulfilled yet (no release found or a backend error). You can retry the search.", kind, title)),
      _ => (
        string.Format(CultureInfo.InvariantCulture, "Jelly Crowd: {0}", title),
        title)
    };
  }

  /// <summary>
  /// Builds the subject/body for a grouped "now available" notification covering several episodes of the
  /// same season that became available together (so channels get one message, not one per episode).
  /// </summary>
  /// <param name="representative">Any request from the group (supplies title and season).</param>
  /// <param name="episodes">The episode numbers that became available.</param>
  /// <returns>A subject/body pair.</returns>
  public static (string Subject, string Body) BuildAvailableBatch(RequestRecord representative, System.Collections.Generic.IReadOnlyList<int> episodes)
  {
    ArgumentNullException.ThrowIfNull(representative);
    ArgumentNullException.ThrowIfNull(episodes);

    var title = representative.Season.HasValue
      ? string.Format(CultureInfo.InvariantCulture, "{0} (Season {1})", representative.Title, representative.Season.Value)
      : representative.Title;

    var subject = string.Format(CultureInfo.InvariantCulture, "Now available: {0}", title);
    var body = string.Format(
      CultureInfo.InvariantCulture,
      "{0} episodes of \"{1}\" are now available in the library (episodes {2}).",
      episodes.Count,
      title,
      FormatEpisodeRanges(episodes));
    return (subject, body);
  }

  // Formats a set of episode numbers compactly, collapsing contiguous runs into ranges:
  // [1,2,3,4,5,6] -> "1–6", [1,2,4,5] -> "1–2, 4–5", [1,3,5] -> "1, 3, 5".
  private static string FormatEpisodeRanges(System.Collections.Generic.IReadOnlyList<int> episodes)
  {
    var sorted = new System.Collections.Generic.List<int>(episodes);
    sorted.Sort();

    var parts = new System.Collections.Generic.List<string>();
    var i = 0;
    while (i < sorted.Count)
    {
      var start = sorted[i];
      var end = start;
      while (i + 1 < sorted.Count && sorted[i + 1] == end + 1)
      {
        end = sorted[++i];
      }

      parts.Add(start == end
        ? start.ToString(CultureInfo.InvariantCulture)
        : string.Format(CultureInfo.InvariantCulture, "{0}–{1}", start, end));
      i++;
    }

    return string.Join(", ", parts);
  }
}
