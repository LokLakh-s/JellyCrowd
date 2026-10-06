using System;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A user's personal notification-delivery preferences (in addition to the in-app bell). The server
/// (SMTP / ntfy) is admin-configured; the user supplies their own destination (email / ntfy topic).
/// </summary>
public class UserNotificationPrefs
{
  /// <summary>Gets or sets the user id.</summary>
  public Guid UserId { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether personal (external) delivery is enabled. Defaults to
  /// <c>true</c>; the in-app bell is unaffected by this flag.
  /// </summary>
  public bool Enabled { get; set; } = true;

  /// <summary>Gets or sets the user's email address for notifications (empty = no email).</summary>
  public string? Email { get; set; }

  /// <summary>Gets or sets the user's ntfy topic (empty = no ntfy).</summary>
  public string? NtfyTopic { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether to deliver, on the user's personal channels, the
  /// "available" notice for a title that was requested <em>before</em> its release (the deferred,
  /// not-yet-out case most users care about). Opt-in; defaults to <c>false</c>.
  /// </summary>
  public bool NotifyAvailableUnreleased { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether to deliver the "available" notice for an ordinary
  /// request of an already-released title. Opt-in; defaults to <c>false</c>.
  /// </summary>
  public bool NotifyAvailableReleased { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether to deliver decision notices (approved / denied / failed)
  /// on the user's personal channels. Opt-in; defaults to <c>false</c>.
  /// </summary>
  public bool NotifyDecisions { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether to deliver quota and ownership-expiry warnings on the
  /// user's personal channels. Opt-in; defaults to <c>false</c>.
  /// </summary>
  public bool NotifyQuotaExpiry { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether the user has hidden their own viewing history. Recording is
  /// unaffected (the admin statistics stay complete) — this only hides the personal history from them.
  /// Stored inverted (hidden, not shown) so a preferences record saved before this feature existed, which
  /// lacks the field, defaults to <c>false</c> and the history stays visible.
  /// </summary>
  public bool HistoryHidden { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether the next season of a show the user is watching is requested
  /// for them automatically near the end of the current one. Opt-in (defaults to <c>false</c>), and only
  /// effective while the administrator offers the feature (<c>AutoNextSeasonEnabled</c>).
  /// </summary>
  public bool AutoRequestNextSeason { get; set; }

  /// <summary>
  /// Gets or sets the user's preferred version: <c>original</c>, <c>dubbed</c>, <c>subtitled</c> (original
  /// with subtitles), or empty for no preference. Only effective while the administrator offers it.
  /// </summary>
  public string LanguagePreference { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the subtitle languages the user wants added to their requests (ISO 639-1 codes).
  /// </summary>
  [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Deserialized from the preferences store.")]
  public System.Collections.ObjectModel.Collection<string> SubtitleLanguages { get; set; } = new();

  /// <summary>
  /// Gets or sets the language the member's Jelly Crowd pages are shown in (two-letter code), as their
  /// browser last reported it. Under the "auto" language setting, their notifications are worded in it.
  /// </summary>
  public string? DisplayLanguage { get; set; }
}
