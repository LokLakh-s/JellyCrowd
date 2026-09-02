namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// The configured UI/notification language exposed to the user-facing pages.
/// </summary>
public class LanguageSettingDto
{
  /// <summary>
  /// Gets or sets the configured language: <c>"auto"</c> (follow the user) or a 2-letter code.
  /// </summary>
  public string Language { get; set; } = "auto";

  /// <summary>
  /// Gets or sets a value indicating whether the plugin is in "config mode" (hidden from non-admins).
  /// Exposed so the client can hide instantly via a token-free request; the admin check (for whether
  /// the current user is exempt) is done separately against the authenticated visibility endpoint.
  /// </summary>
  public bool Hidden { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether community comments/reviews are enabled (admin opt-in).
  /// </summary>
  public bool CommentsEnabled { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether regular users may trigger a manual "retry search" (admin opt-in).
  /// </summary>
  public bool AllowUserRetrySearch { get; set; }

  /// <summary>Gets or sets the admin announcement banner text (empty = no banner).</summary>
  public string AnnouncementText { get; set; } = string.Empty;

  /// <summary>Gets or sets the announcement severity colour (<c>green</c>/<c>yellow</c>/<c>red</c>).</summary>
  public string AnnouncementLevel { get; set; } = "green";

  /// <summary>Gets or sets the Discord invite URL for the header icon (empty = icon hidden).</summary>
  public string DiscordInviteUrl { get; set; } = string.Empty;

  /// <summary>Gets or sets the support/donation URL for the header icon (empty = icon hidden).</summary>
  public string SupportLinkUrl { get; set; } = string.Empty;

  /// <summary>Gets or sets the user-guide URL for the header icon (empty = icon hidden). Legacy: the guide
  /// is now hosted in-app; kept for back-compat.</summary>
  public string GuideLinkUrl { get; set; } = string.Empty;

  /// <summary>Gets or sets a value indicating whether the header guide (?) icon is shown; it opens the
  /// in-app guide view.</summary>
  public bool GuideEnabled { get; set; }

  /// <summary>Gets or sets a value indicating whether the smart Skip Outro control is enabled, so the
  /// injected client only installs its playback watcher when the feature is on.</summary>
  public bool SkipOutroEnabled { get; set; }

  /// <summary>Gets or sets a value indicating whether to hide Jellyfin's left drawer for non-admins.</summary>
  public bool HideNativeDrawer { get; set; }

  /// <summary>Gets or sets a value indicating whether movies are offered (catalog + requests).</summary>
  public bool MoviesEnabled { get; set; } = true;

  /// <summary>Gets or sets a value indicating whether TV shows are offered (catalog + requests).</summary>
  public bool SeriesEnabled { get; set; } = true;

  /// <summary>Gets or sets a value indicating whether users may request a whole series (TV).</summary>
  public bool AllowSeriesRequests { get; set; } = true;

  /// <summary>Gets or sets a value indicating whether users may request a single season (TV).</summary>
  public bool AllowSeasonRequests { get; set; } = true;

  /// <summary>Gets or sets a value indicating whether users may request individual episodes (TV).</summary>
  public bool AllowEpisodeRequests { get; set; } = true;
}
