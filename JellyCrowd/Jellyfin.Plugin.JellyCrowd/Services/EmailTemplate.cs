using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using Jellyfin.Plugin.JellyCrowd.Models;
using MimeKit;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Builds the HTML (and plain-text alternative) body of Jelly Crowd emails: a dark card carrying the
/// poster, the title, the event accent, the synopsis and a call to action. Pure and network-free so it
/// can be unit tested independently of SMTP.
/// <para>
/// Written to the constraints of email clients, not of browsers: tables for layout, every style inlined,
/// no flexbox/grid, no external stylesheet and no media query. Everything interpolated is HTML-encoded,
/// because titles, synopses and user names come from TMDB and from users.
/// </para>
/// </summary>
public static class EmailTemplate
{
  /// <summary>The poster width requested from TMDB; 300px covers a 96px slot on a retina screen.</summary>
  private const string PosterBaseUrl = "https://image.tmdb.org/t/p/w300";

  private const string Font = "-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif";

  private const string PageBackground = "#0d0f13";
  private const string CardBackground = "#16191f";
  private const string PanelBackground = "#1b1f27";
  private const string BorderColor = "#262b34";
  private const string PrimaryText = "#e9ecf1";
  private const string SecondaryText = "#c2cad6";
  private const string MutedText = "#8b94a3";
  private const string FooterText = "#6b7482";

  /// <summary>The accent used when an email is not tied to a lifecycle event (test, generic notice).</summary>
  private const string NeutralAccent = "#3B82F6";

  /// <summary>
  /// Builds the email for a request lifecycle event: poster, title, status pill, synopsis and a TMDB link.
  /// </summary>
  /// <param name="request">The request the notification is about.</param>
  /// <param name="notificationEvent">The lifecycle event.</param>
  /// <param name="subject">The email heading (the subject without its channel prefix).</param>
  /// <param name="body">The lead sentence describing what happened.</param>
  /// <param name="overview">The TMDB synopsis, or <c>null</c>.</param>
  /// <param name="posterPath">The TMDB relative poster path, or <c>null</c>.</param>
  /// <param name="username">The requesting user's display name.</param>
  /// <param name="showRequestedBy">
  /// Whether to show who requested the title. True for the shared ops mailbox; false when the email goes to
  /// the requester themselves, who does not need to be told they are the requester.
  /// </param>
  /// <returns>The HTML body and its plain-text alternative.</returns>
  public static (string Html, string Text) BuildRequest(
    RequestRecord request,
    NotificationEvent notificationEvent,
    string subject,
    string body,
    string? overview,
    string? posterPath,
    string username,
    bool showRequestedBy)
  {
    ArgumentNullException.ThrowIfNull(request);

    // Normalize the media type rather than trusting the stored value: it ends up in a URL.
    var isShow = string.Equals(request.MediaType, "tv", StringComparison.Ordinal);
    var pairs = new List<(string Label, string Value)>();
    if (showRequestedBy && !string.IsNullOrWhiteSpace(username))
    {
      pairs.Add(("Requested by", username));
    }

    if (request.Season.HasValue)
    {
      pairs.Add(("Season", request.Season.Value.ToString(CultureInfo.InvariantCulture)));
    }

    var card = new Card
    {
      Heading = subject,
      Lead = body,
      Accent = AccentFor(notificationEvent),
      Status = StatusText(notificationEvent),
      Title = request.Title,
      Subtitle = SubtitleFor(isShow, request.Season),
      PosterUrl = PosterUrl(posterPath),
      Overview = overview,
      Pairs = pairs,
      LinkUrl = NotificationEmbeds.TmdbUrl(isShow ? "tv" : "movie", request.TmdbId),
      LinkLabel = "View on TMDB"
    };

    return (RenderHtml(card), RenderText(card));
  }

  /// <summary>
  /// Builds the email for a notice that is not a request lifecycle event (a channel test, a deletion
  /// warning...): the same card, without a status pill or call to action.
  /// </summary>
  /// <param name="subject">The email heading.</param>
  /// <param name="body">The message.</param>
  /// <param name="title">The media title to feature, or <c>null</c> for a plain message.</param>
  /// <param name="posterPath">The TMDB relative poster path, or <c>null</c>.</param>
  /// <returns>The HTML body and its plain-text alternative.</returns>
  public static (string Html, string Text) BuildNotice(string subject, string body, string? title, string? posterPath)
  {
    var card = new Card
    {
      Heading = subject,
      Lead = body,
      Accent = NeutralAccent,
      Title = title,
      PosterUrl = PosterUrl(posterPath),
      Pairs = new List<(string Label, string Value)>()
    };

    return (RenderHtml(card), RenderText(card));
  }

  /// <summary>
  /// Builds the From mailbox. The admin usually configures a bare address, which inboxes then display as
  /// "jellycrowd@example.com"; falling back to a display name makes the sender read as "Jelly Crowd". An
  /// address that already carries a name (<c>Name &lt;a@b&gt;</c>) is left alone.
  /// </summary>
  /// <param name="configuredFrom">The configured SMTP from address.</param>
  /// <returns>The mailbox to put in the From header.</returns>
  public static MailboxAddress Sender(string configuredFrom)
  {
    var parsed = MailboxAddress.Parse(configuredFrom);
    return string.IsNullOrWhiteSpace(parsed.Name) ? new MailboxAddress("Jelly Crowd", parsed.Address) : parsed;
  }

  // The palette is shared with the Discord embeds so both channels read the same, except for Failed:
  // the embed default falls back to blue, which would say "nothing to see here" for a request that
  // actually needs a human. Amber is the warning color.
  private static string AccentFor(NotificationEvent notificationEvent) => notificationEvent == NotificationEvent.Failed
    ? "#F59E0B"
    : "#" + NotificationEmbeds.DefaultColorFor(notificationEvent).ToString("X6", CultureInfo.InvariantCulture);

  private static string StatusText(NotificationEvent notificationEvent) => notificationEvent switch
  {
    NotificationEvent.Created => "Pending approval",
    NotificationEvent.Approved => "Approved",
    NotificationEvent.Available => "Available",
    NotificationEvent.Denied => "Denied",
    _ => "Needs attention"
  };

  private static string SubtitleFor(bool isShow, int? season)
  {
    var kind = isShow ? "Show" : "Movie";
    return season.HasValue
      ? kind + " · Season " + season.Value.ToString(CultureInfo.InvariantCulture)
      : kind;
  }

  // TMDB poster paths look like "/aBc123.jpg". The value reaches us from an upstream API and ends up in a
  // src attribute, so anything that is not that exact shape is dropped rather than escaped-and-hoped-for.
  private static string? PosterUrl(string? posterPath)
  {
    if (string.IsNullOrWhiteSpace(posterPath) || posterPath.Length < 2 || posterPath.Length > 128 || posterPath[0] != '/')
    {
      return null;
    }

    foreach (var c in posterPath.AsSpan(1))
    {
      if (!char.IsAsciiLetterOrDigit(c) && c != '.' && c != '_' && c != '-')
      {
        return null;
      }
    }

    return PosterBaseUrl + posterPath;
  }

  private static string RenderHtml(Card card)
  {
    var sb = new StringBuilder(4096);
    var accent = card.Accent;

    sb.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n")
      .Append("<meta charset=\"utf-8\">\n")
      .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">\n")
      .Append("<meta name=\"color-scheme\" content=\"dark\">\n")
      .Append("<meta name=\"supported-color-schemes\" content=\"dark\">\n")
      .Append("<title>").Append(Escape(card.Heading)).Append("</title>\n")
      .Append("</head>\n")
      .Append("<body style=\"margin:0;padding:0;background-color:").Append(PageBackground).Append(";\">\n");

    // Preheader: the grey line clients show next to the subject in the inbox list. Hidden in the body.
    sb.Append("<div style=\"display:none;max-height:0;max-width:0;opacity:0;overflow:hidden;font-size:1px;line-height:1px;color:")
      .Append(PageBackground).Append(";\">").Append(Escape(card.Lead)).Append("</div>\n");

    sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"background-color:")
      .Append(PageBackground).Append(";\">\n<tr>\n<td align=\"center\" style=\"padding:32px 16px;\">\n")
      .Append("<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"width:100%;max-width:600px;background-color:")
      .Append(CardBackground).Append(";border:1px solid ").Append(BorderColor).Append(";border-radius:16px;\">\n");

    // Accent bar.
    sb.Append("<tr><td style=\"height:4px;line-height:4px;font-size:4px;background-color:").Append(accent)
      .Append(";border-radius:16px 16px 0 0;\">&nbsp;</td></tr>\n");

    AppendHeader(sb, card, accent);
    AppendHero(sb, card);
    AppendLead(sb, card);
    AppendOverview(sb, card, accent);
    AppendPairs(sb, card);
    AppendAction(sb, card, accent);

    sb.Append("<tr><td style=\"padding:26px 28px;font-family:").Append(Font)
      .Append(";font-size:11px;line-height:17px;color:").Append(FooterText)
      .Append(";\">Sent by Jelly Crowd, the request system on your Jellyfin server.</td></tr>\n");

    sb.Append("</table>\n</td>\n</tr>\n</table>\n</body>\n</html>");
    return sb.ToString();
  }

  // Wordmark on the left, status pill on the right.
  private static void AppendHeader(StringBuilder sb, Card card, string accent)
  {
    sb.Append("<tr><td style=\"padding:22px 28px 0;\">\n")
      .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr>\n")
      .Append("<td align=\"left\" style=\"font-family:").Append(Font)
      .Append(";font-size:12px;font-weight:700;letter-spacing:2px;text-transform:uppercase;color:").Append(MutedText)
      .Append(";\">Jelly&nbsp;Crowd</td>\n");

    if (!string.IsNullOrEmpty(card.Status))
    {
      sb.Append("<td align=\"right\"><span style=\"display:inline-block;padding:5px 12px;border-radius:999px;background-color:")
        .Append(PanelBackground).Append(";border:1px solid ").Append(BorderColor).Append(";font-family:").Append(Font)
        .Append(";font-size:11px;font-weight:700;letter-spacing:1px;text-transform:uppercase;color:").Append(accent)
        .Append(";\">").Append(Escape(card.Status)).Append("</span></td>\n");
    }
    else
    {
      sb.Append("<td></td>\n");
    }

    sb.Append("</tr></table>\n</td></tr>\n");
  }

  // Poster next to the title; the poster cell is dropped entirely when there is no artwork, so the title
  // does not sit next to an empty box.
  private static void AppendHero(StringBuilder sb, Card card)
  {
    if (string.IsNullOrEmpty(card.Title))
    {
      return;
    }

    sb.Append("<tr><td style=\"padding:20px 28px 0;\">\n")
      .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr>\n");

    if (card.PosterUrl is not null)
    {
      sb.Append("<td valign=\"top\" width=\"96\" style=\"width:96px;padding-right:18px;\">")
        .Append("<img src=\"").Append(Escape(card.PosterUrl)).Append("\" width=\"96\" height=\"144\" alt=\"")
        .Append(Escape(card.Title)).Append("\" style=\"display:block;width:96px;height:144px;border-radius:10px;border:1px solid ")
        .Append(BorderColor).Append(";\"></td>\n");
    }

    sb.Append("<td valign=\"top\" style=\"font-family:").Append(Font).Append(";\">")
      .Append("<div style=\"font-size:21px;line-height:28px;font-weight:700;color:").Append(PrimaryText)
      .Append(";\">").Append(Escape(card.Title)).Append("</div>");

    if (!string.IsNullOrEmpty(card.Subtitle))
    {
      sb.Append("<div style=\"padding-top:6px;font-size:13px;line-height:18px;color:").Append(MutedText)
        .Append(";\">").Append(Escape(card.Subtitle)).Append("</div>");
    }

    sb.Append("</td>\n</tr></table>\n</td></tr>\n");
  }

  private static void AppendLead(StringBuilder sb, Card card)
  {
    sb.Append("<tr><td style=\"padding:18px 28px 0;font-family:").Append(Font)
      .Append(";font-size:15px;line-height:23px;color:").Append(SecondaryText).Append(";\">")
      .Append(Escape(card.Lead)).Append("</td></tr>\n");
  }

  private static void AppendOverview(StringBuilder sb, Card card, string accent)
  {
    if (string.IsNullOrWhiteSpace(card.Overview))
    {
      return;
    }

    sb.Append("<tr><td style=\"padding:18px 28px 0;\">\n")
      .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr>")
      .Append("<td style=\"background-color:").Append(PanelBackground).Append(";border-left:3px solid ").Append(accent)
      .Append(";border-radius:0 10px 10px 0;padding:14px 16px;font-family:").Append(Font)
      .Append(";font-size:13px;line-height:20px;color:").Append(MutedText).Append(";\">")
      .Append(Escape(card.Overview)).Append("</td></tr></table>\n</td></tr>\n");
  }

  // Label/value pairs, two per row, above a hairline divider.
  private static void AppendPairs(StringBuilder sb, Card card)
  {
    if (card.Pairs.Count == 0)
    {
      return;
    }

    sb.Append("<tr><td style=\"padding:22px 28px 0;\"><div style=\"height:1px;font-size:0;line-height:0;background-color:")
      .Append(BorderColor).Append(";\">&nbsp;</div></td></tr>\n")
      .Append("<tr><td style=\"padding:16px 28px 0;\">\n")
      .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr>\n");

    foreach (var (label, value) in card.Pairs)
    {
      sb.Append("<td valign=\"top\" style=\"font-family:").Append(Font).Append(";padding-right:16px;\">")
        .Append("<div style=\"font-size:10px;font-weight:700;letter-spacing:1px;text-transform:uppercase;color:")
        .Append(FooterText).Append(";\">").Append(Escape(label)).Append("</div>")
        .Append("<div style=\"padding-top:4px;font-size:14px;line-height:20px;color:").Append(PrimaryText)
        .Append(";\">").Append(Escape(value)).Append("</div></td>\n");
    }

    sb.Append("</tr></table>\n</td></tr>\n");
  }

  // A "bulletproof" button: a table cell carries the background so Outlook renders it too.
  private static void AppendAction(StringBuilder sb, Card card, string accent)
  {
    if (string.IsNullOrEmpty(card.LinkUrl))
    {
      return;
    }

    sb.Append("<tr><td style=\"padding:24px 28px 0;\">\n")
      .Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr>")
      .Append("<td align=\"center\" bgcolor=\"").Append(accent).Append("\" style=\"border-radius:10px;\">")
      .Append("<a href=\"").Append(Escape(card.LinkUrl)).Append("\" style=\"display:inline-block;padding:12px 24px;font-family:")
      .Append(Font).Append(";font-size:14px;font-weight:600;color:#ffffff;text-decoration:none;\">")
      .Append(Escape(card.LinkLabel ?? "Open")).Append("</a></td></tr></table>\n</td></tr>\n");
  }

  // The plain-text alternative every multipart email must carry: same information, no markup.
  private static string RenderText(Card card)
  {
    var sb = new StringBuilder(512);
    sb.Append(card.Heading).Append("\n\n").Append(card.Lead).Append('\n');

    if (!string.IsNullOrEmpty(card.Title))
    {
      sb.Append('\n').Append(card.Title);
      if (!string.IsNullOrEmpty(card.Subtitle))
      {
        sb.Append(" — ").Append(card.Subtitle);
      }

      sb.Append('\n');
    }

    foreach (var (label, value) in card.Pairs)
    {
      sb.Append(label).Append(": ").Append(value).Append('\n');
    }

    if (!string.IsNullOrWhiteSpace(card.Overview))
    {
      sb.Append('\n').Append(card.Overview).Append('\n');
    }

    if (!string.IsNullOrEmpty(card.LinkUrl))
    {
      sb.Append('\n').Append(card.LinkLabel ?? "Open").Append(": ").Append(card.LinkUrl).Append('\n');
    }

    sb.Append("\n— Sent by Jelly Crowd, the request system on your Jellyfin server.\n");
    return sb.ToString();
  }

  private static string Escape(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

  // Everything the renderers need, so the two renderers stay in sync and the builders stay declarative.
  private sealed class Card
  {
    public string Heading { get; init; } = string.Empty;

    public string Lead { get; init; } = string.Empty;

    public string Accent { get; init; } = NeutralAccent;

    public string? Status { get; init; }

    public string? Title { get; init; }

    public string? Subtitle { get; init; }

    public string? PosterUrl { get; init; }

    public string? Overview { get; init; }

    public IReadOnlyList<(string Label, string Value)> Pairs { get; init; } = Array.Empty<(string, string)>();

    public string? LinkUrl { get; init; }

    public string? LinkLabel { get; init; }
  }
}
