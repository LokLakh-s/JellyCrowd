using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Builds the subject and body text for request notifications. Pure and network-free so it can be
/// unit tested independently of the delivery channels. The wording comes from a translation catalog
/// (see <see cref="ServerStrings"/>) rather than from this file, so every channel speaks the language
/// the plugin is configured in.
/// </summary>
public static class NotificationMessages
{
  /// <summary>
  /// Builds the notification subject and body for a request event.
  /// </summary>
  /// <param name="request">The request the notification is about.</param>
  /// <param name="notificationEvent">The lifecycle event.</param>
  /// <param name="t">The translation lookup (see <see cref="ServerStrings.For"/>).</param>
  /// <returns>A subject/body pair.</returns>
  public static (string Subject, string Body) Build(RequestRecord request, NotificationEvent notificationEvent, Func<string, string> t)
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(t);

    var kind = t(string.Equals(request.MediaType, "tv", StringComparison.Ordinal) ? "notif_kind_show" : "notif_kind_movie");
    var title = TitleOf(request, t);

    var prefix = notificationEvent switch
    {
      NotificationEvent.Created => "notif_created",
      NotificationEvent.Approved => "notif_approved",
      NotificationEvent.Denied => "notif_denied",
      NotificationEvent.Available => "notif_available",
      NotificationEvent.Failed => "notif_failed",
      _ => null
    };

    if (prefix is null)
    {
      return (Fill(t("notif_generic_subject"), title, kind), title);
    }

    return (Fill(t(prefix + "_subject"), title, kind), Fill(t(prefix + "_body"), title, kind));
  }

  /// <summary>
  /// Builds the subject/body for a grouped "now available" notification covering several episodes of the
  /// same season that became available together (so channels get one message, not one per episode).
  /// </summary>
  /// <param name="representative">Any request from the group (supplies title and season).</param>
  /// <param name="episodes">The episode numbers that became available.</param>
  /// <param name="t">The translation lookup.</param>
  /// <returns>A subject/body pair.</returns>
  public static (string Subject, string Body) BuildAvailableBatch(RequestRecord representative, IReadOnlyList<int> episodes, Func<string, string> t)
  {
    ArgumentNullException.ThrowIfNull(representative);
    ArgumentNullException.ThrowIfNull(episodes);
    ArgumentNullException.ThrowIfNull(t);

    var title = TitleOf(representative, t);
    var body = t("notif_batch_body")
      .Replace("{n}", episodes.Count.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
      .Replace("{title}", title, StringComparison.Ordinal)
      .Replace("{episodes}", FormatEpisodeRanges(episodes), StringComparison.Ordinal);

    return (Fill(t("notif_available_subject"), title, string.Empty), body);
  }

  /// <summary>
  /// The display title of a request: the title on its own, or with its season number, in the wording of
  /// the configured language (which is not always "{title} (Season {n})").
  /// </summary>
  /// <param name="request">The request.</param>
  /// <param name="t">The translation lookup.</param>
  /// <returns>The formatted title.</returns>
  public static string TitleOf(RequestRecord request, Func<string, string> t)
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(t);

    if (!request.Season.HasValue)
    {
      return request.Title;
    }

    return t("notif_title_season")
      .Replace("{title}", request.Title, StringComparison.Ordinal)
      .Replace("{n}", request.Season.Value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
  }

  private static string Fill(string template, string title, string kind)
    => template
      .Replace("{title}", title, StringComparison.Ordinal)
      .Replace("{kind}", kind, StringComparison.Ordinal);

  // Formats a set of episode numbers compactly, collapsing contiguous runs into ranges:
  // [1,2,3,4,5,6] -> "1–6", [1,2,4,5] -> "1–2, 4–5", [1,3,5] -> "1, 3, 5".
  private static string FormatEpisodeRanges(IReadOnlyList<int> episodes)
  {
    var sorted = new List<int>(episodes);
    sorted.Sort();

    var parts = new List<string>();
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
