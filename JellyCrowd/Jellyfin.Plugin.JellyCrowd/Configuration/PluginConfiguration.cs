using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using Jellyfin.Plugin.JellyCrowd.Models;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyCrowd.Configuration;

/// <summary>
/// Jelly Crowd plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
  /// <summary>
  /// Default per-user disk quota (in bytes) applied when no explicit override exists. 0 means unlimited.
  /// </summary>
  public const long DefaultQuotaBytes = 50L * 1024 * 1024 * 1024; // 50 GiB

  /// <summary>
  /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
  /// </summary>
  public PluginConfiguration()
  {
    TmdbApiKey = string.Empty;
    DefaultUserQuotaBytes = DefaultQuotaBytes;
    RequireApproval = true;
    HiddenFromUsers = false;
    RateLimitPerMinute = 120;
    CommentsEnabled = false;
    AnnouncementText = string.Empty;
    AnnouncementLevel = "green";
    MediaExpiryDays = 90;
    EstimatedMovieSizeBytes = 4L * 1024 * 1024 * 1024; // 4 GiB
    EstimatedEpisodeSizeBytes = 1L * 1024 * 1024 * 1024; // 1 GiB
    MaxRequestsPerPeriod = 0;
    RequestPeriod = RequestPeriod.Week;
    DeletionRetentionHours = 24;
    DiscordWebhookUrl = string.Empty;
    DiscordNotifyCreated = true;
    DiscordNotifyApproved = true;
    DiscordNotifyDenied = true;
    DiscordNotifyAvailable = true;
    DiscordColorCreated = "#3B82F6";
    DiscordColorApproved = "#6366F1";
    DiscordColorDenied = "#EF4444";
    DiscordColorAvailable = "#10B981";
    DiscordShowPoster = true;
    DiscordShowSynopsis = true;
    DiscordShowRequestedBy = true;
    DiscordShowStatus = true;
    DiscordShowSeason = true;
    DiscordShowLink = true;
    DiscordMention = string.Empty;
    SmtpHost = string.Empty;
    SmtpPort = 587;
    SmtpUseSsl = true;
    SmtpUsername = string.Empty;
    SmtpPassword = string.Empty;
    SmtpFromAddress = string.Empty;
    NotificationEmailTo = string.Empty;
    SmtpAllowInvalidCertificate = false;
    TelegramBotToken = string.Empty;
    TelegramChatId = string.Empty;
    NtfyServer = string.Empty;
    NtfyTopic = string.Empty;
    NtfyToken = string.Empty;
    GotifyServer = string.Empty;
    GotifyToken = string.Empty;
    PushoverToken = string.Empty;
    PushoverUser = string.Empty;
    SlackWebhookUrl = string.Empty;
    NotifyWebhookUrl = string.Empty;
    Language = "auto";
    DownloadBackend = "none";
    DownloadWebhookUrl = string.Empty;
    DownloadWebhookHeaders = string.Empty;
    RadarrUrl = string.Empty;
    RadarrApiKey = string.Empty;
    ProwlarrUrl = string.Empty;
    ProwlarrApiKey = string.Empty;
    RadarrRootFolderPath = string.Empty;
    RadarrQualityProfileId = 0;
    SonarrUrl = string.Empty;
    SonarrApiKey = string.Empty;
    SonarrRootFolderPath = string.Empty;
    SonarrQualityProfileId = 0;
    SonarrLanguageProfileId = 1;
    ScriptPath = string.Empty;
    ScriptArguments = string.Empty;
  }

  /// <summary>
  /// Gets or sets the TMDB API key used to power the discovery catalog.
  /// </summary>
  public string TmdbApiKey { get; set; }

  /// <summary>
  /// Gets or sets the default per-user disk quota in bytes. 0 means unlimited.
  /// </summary>
  public long DefaultUserQuotaBytes { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether new requests require admin approval before fulfillment.
  /// </summary>
  public bool RequireApproval { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether regular users may trigger a manual "retry search" on their
  /// approved requests. Off by default: Radarr/Sonarr already search automatically, so the button is
  /// admin-only unless this is enabled.
  /// </summary>
  public bool AllowUserRetrySearch { get; set; }

  /// <summary>
  /// Gets or sets the estimated size (in bytes) of a movie, used for quota pre-checks before the real size is known.
  /// </summary>
  public long EstimatedMovieSizeBytes { get; set; }

  /// <summary>
  /// Gets or sets the estimated size (in bytes) of a single episode, used for quota pre-checks.
  /// </summary>
  public long EstimatedEpisodeSizeBytes { get; set; }

  /// <summary>
  /// Gets or sets the maximum number of requests a user may make per <see cref="RequestPeriod"/>. 0 means unlimited.
  /// </summary>
  public int MaxRequestsPerPeriod { get; set; }

  /// <summary>
  /// Gets or sets the rolling window for the request rate limit.
  /// </summary>
  public RequestPeriod RequestPeriod { get; set; }

  /// <summary>
  /// Gets or sets the grace period (in hours) between a user requesting deletion and the media being
  /// actually removed from disk by the scheduled task. 0 deletes at the next task run.
  /// </summary>
  public int DeletionRetentionHours { get; set; }

  /// <summary>
  /// Gets or sets the auto-approval size threshold (in bytes): when &gt; 0, requests whose estimated
  /// size is at or below this value are auto-approved even if admin approval is otherwise required.
  /// 0 disables the rule.
  /// </summary>
  public long AutoApproveMaxSizeBytes { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether the plugin is hidden from regular users ("config mode").
  /// When <c>true</c>, the header injection is skipped for non-administrators and the user-facing API
  /// endpoints return 403 for them, so the admin can hide the plugin until it is configured and working.
  /// Administrators are unaffected.
  /// </summary>
  public bool HiddenFromUsers { get; set; }

  /// <summary>
  /// Gets or sets the maximum number of write (POST/PUT/DELETE) requests a single non-admin user may
  /// make per minute to the plugin's API, in addition to the per-period request cap. 0 disables it.
  /// Defends against rapid-fire abuse (e.g. spamming requests faster than the quota refreshes).
  /// </summary>
  public int RateLimitPerMinute { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether community comments/reviews are enabled. Off by default so
  /// the admin opts in; when off, the comments section is hidden and the comments API is disabled.
  /// </summary>
  public bool CommentsEnabled { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether the author's name is shown on reviews to regular users.
  /// Off by default (reviews are anonymous to non-admins; admins always see the author).
  /// </summary>
  public bool ShowReviewAuthors { get; set; }

  /// <summary>
  /// Gets or sets the admin announcement shown in the header banner. Empty hides the banner.
  /// </summary>
  public string AnnouncementText { get; set; }

  /// <summary>
  /// Gets or sets the announcement severity colour: <c>green</c>, <c>yellow</c> or <c>red</c>.
  /// </summary>
  public string AnnouncementLevel { get; set; }

  /// <summary>
  /// Gets or sets the media ownership expiry window in days. A user's ownership of an available title
  /// lapses (freeing their quota) this many days after it became available / was last claimed; claiming
  /// again resets the countdown. The media file is never auto-deleted (deletion stays on-demand).
  /// 0 disables expiry (ownerships never lapse).
  /// </summary>
  public int MediaExpiryDays { get; set; }

  /// <summary>
  /// Gets or sets the genre allow-list (TMDB English genre names) gating size-based auto-approval: when
  /// non-empty, a request is only auto-approved by the size rule if at least one of the title's genres is
  /// listed. Empty disables the genre criterion (size rule applies to all genres). Trusted users bypass this.
  /// </summary>
  [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Must be settable so System.Text.Json can replace it when deserializing the posted plugin configuration (a get-only collection is silently skipped on deserialize, which dropped the saved value).")]
  public Collection<string> AutoApproveGenres { get; set; } = new();

  /// <summary>
  /// Gets or sets the per-user quota overrides. A user not listed here uses <see cref="DefaultUserQuotaBytes"/>.
  /// </summary>
  [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Must be settable so System.Text.Json can replace it when deserializing the posted plugin configuration (a get-only collection is silently skipped on deserialize, which dropped the saved value).")]
  public Collection<UserQuotaOverride> QuotaOverrides { get; set; } = new();

  /// <summary>
  /// Gets or sets the Discord webhook URL used for request notifications. Empty disables Discord.
  /// </summary>
  public string DiscordWebhookUrl { get; set; }

  /// <summary>Gets or sets a value indicating whether Discord notifies on the "created" event.</summary>
  public bool DiscordNotifyCreated { get; set; }

  /// <summary>Gets or sets a value indicating whether Discord notifies on the "approved" event.</summary>
  public bool DiscordNotifyApproved { get; set; }

  /// <summary>Gets or sets a value indicating whether Discord notifies on the "denied" event.</summary>
  public bool DiscordNotifyDenied { get; set; }

  /// <summary>Gets or sets a value indicating whether Discord notifies on the "available" event.</summary>
  public bool DiscordNotifyAvailable { get; set; }

  /// <summary>Gets or sets the embed color (hex, e.g. <c>#3B82F6</c>) for the "created" event.</summary>
  public string DiscordColorCreated { get; set; }

  /// <summary>Gets or sets the embed color (hex) for the "approved" event.</summary>
  public string DiscordColorApproved { get; set; }

  /// <summary>Gets or sets the embed color (hex) for the "denied" event.</summary>
  public string DiscordColorDenied { get; set; }

  /// <summary>Gets or sets the embed color (hex) for the "available" event.</summary>
  public string DiscordColorAvailable { get; set; }

  /// <summary>Gets or sets a value indicating whether the embed shows the poster thumbnail.</summary>
  public bool DiscordShowPoster { get; set; }

  /// <summary>Gets or sets a value indicating whether the embed shows the synopsis as description.</summary>
  public bool DiscordShowSynopsis { get; set; }

  /// <summary>Gets or sets a value indicating whether the embed shows the "Requested by" field.</summary>
  public bool DiscordShowRequestedBy { get; set; }

  /// <summary>Gets or sets a value indicating whether the embed shows the "Status" field.</summary>
  public bool DiscordShowStatus { get; set; }

  /// <summary>Gets or sets a value indicating whether the embed shows the "Season" field.</summary>
  public bool DiscordShowSeason { get; set; }

  /// <summary>Gets or sets a value indicating whether the embed title links to the TMDB page.</summary>
  public bool DiscordShowLink { get; set; }

  /// <summary>
  /// Gets or sets an optional message posted above the embed (e.g. <c>&lt;@&amp;roleId&gt;</c> to ping a role).
  /// </summary>
  public string DiscordMention { get; set; }

  /// <summary>
  /// Gets or sets the SMTP server host for email notifications. Empty disables email.
  /// </summary>
  public string SmtpHost { get; set; }

  /// <summary>
  /// Gets or sets the SMTP server port.
  /// </summary>
  public int SmtpPort { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether SMTP uses SSL/TLS.
  /// </summary>
  public bool SmtpUseSsl { get; set; }

  /// <summary>
  /// Gets or sets the SMTP username (empty for unauthenticated relays).
  /// </summary>
  public string SmtpUsername { get; set; }

  /// <summary>
  /// Gets or sets the SMTP password.
  /// </summary>
  public string SmtpPassword { get; set; }

  /// <summary>
  /// Gets or sets the "from" address for notification emails.
  /// </summary>
  public string SmtpFromAddress { get; set; }

  /// <summary>
  /// Gets or sets the recipient address for notification emails (typically the admin/ops mailbox).
  /// </summary>
  public string NotificationEmailTo { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether to accept self-signed/invalid SMTP TLS certificates (insecure).
  /// </summary>
  public bool SmtpAllowInvalidCertificate { get; set; }

  /// <summary>
  /// Gets or sets the Telegram bot token (from @BotFather). Empty disables Telegram.
  /// </summary>
  public string TelegramBotToken { get; set; }

  /// <summary>
  /// Gets or sets the Telegram chat id messages are sent to.
  /// </summary>
  public string TelegramChatId { get; set; }

  /// <summary>
  /// Gets or sets the ntfy server base URL (defaults to <c>https://ntfy.sh</c> when empty).
  /// </summary>
  public string NtfyServer { get; set; }

  /// <summary>
  /// Gets or sets the ntfy topic. Empty disables ntfy.
  /// </summary>
  public string NtfyTopic { get; set; }

  /// <summary>
  /// Gets or sets an optional ntfy access token (Bearer) for protected topics.
  /// </summary>
  public string NtfyToken { get; set; }

  /// <summary>
  /// Gets or sets the Gotify server base URL. Empty disables Gotify.
  /// </summary>
  public string GotifyServer { get; set; }

  /// <summary>
  /// Gets or sets the Gotify application token.
  /// </summary>
  public string GotifyToken { get; set; }

  /// <summary>
  /// Gets or sets the Pushover application API token. Empty disables Pushover.
  /// </summary>
  public string PushoverToken { get; set; }

  /// <summary>
  /// Gets or sets the Pushover user/group key.
  /// </summary>
  public string PushoverUser { get; set; }

  /// <summary>
  /// Gets or sets the Slack incoming-webhook URL. Empty disables Slack.
  /// </summary>
  public string SlackWebhookUrl { get; set; }

  /// <summary>
  /// Gets or sets a generic notification webhook URL (POSTed a <c>{title, body}</c> JSON). Empty disables it.
  /// </summary>
  public string NotifyWebhookUrl { get; set; }

  /// <summary>
  /// Gets or sets the UI/notification language. <c>"auto"</c> (default) follows each user's
  /// browser/Jellyfin language; a 2-letter code (e.g. <c>"en"</c>, <c>"fr"</c>) forces that language.
  /// </summary>
  public string Language { get; set; }

  /// <summary>
  /// Gets or sets the download backend that fulfills approved requests:
  /// <c>"none"</c> (manual admin queue, default), <c>"webhook"</c> (POST the request to a URL),
  /// or <c>"servarr"</c> (Radarr for movies / Sonarr for shows).
  /// </summary>
  public string DownloadBackend { get; set; }

  /// <summary>
  /// Gets or sets the URL the request is POSTed to when <see cref="DownloadBackend"/> is <c>"webhook"</c>.
  /// </summary>
  public string DownloadWebhookUrl { get; set; }

  /// <summary>
  /// Gets or sets optional HTTP headers for the webhook, one per line as <c>Name: Value</c>.
  /// </summary>
  public string DownloadWebhookHeaders { get; set; }

  /// <summary>
  /// Gets or sets the Radarr base URL (e.g. <c>http://localhost:7878</c>) used to add requested movies.
  /// </summary>
  public string RadarrUrl { get; set; }

  /// <summary>
  /// Gets or sets the Radarr API key.
  /// </summary>
  public string RadarrApiKey { get; set; }

  /// <summary>
  /// Gets or sets the Prowlarr base URL (e.g. <c>http://localhost:9696</c>). Optional — used only by the
  /// Diagnostics indexer check to report Prowlarr's own enabled-indexer count (the upstream source).
  /// </summary>
  public string ProwlarrUrl { get; set; }

  /// <summary>
  /// Gets or sets the Prowlarr API key.
  /// </summary>
  public string ProwlarrApiKey { get; set; }

  /// <summary>
  /// Gets or sets the Radarr root folder path new movies are added under.
  /// </summary>
  public string RadarrRootFolderPath { get; set; }

  /// <summary>
  /// Gets or sets the Radarr quality profile id applied to new movies.
  /// </summary>
  public int RadarrQualityProfileId { get; set; }

  /// <summary>
  /// Gets or sets the Sonarr base URL (e.g. <c>http://localhost:8989</c>) used to add requested shows.
  /// </summary>
  public string SonarrUrl { get; set; }

  /// <summary>
  /// Gets or sets the Sonarr API key.
  /// </summary>
  public string SonarrApiKey { get; set; }

  /// <summary>
  /// Gets or sets the Sonarr root folder path new series are added under.
  /// </summary>
  public string SonarrRootFolderPath { get; set; }

  /// <summary>
  /// Gets or sets the Sonarr quality profile id applied to new series.
  /// </summary>
  public int SonarrQualityProfileId { get; set; }

  /// <summary>
  /// Gets or sets the Sonarr language profile id (Sonarr v3 requires one; ignored by v4). Defaults to 1.
  /// </summary>
  public int SonarrLanguageProfileId { get; set; }

  /// <summary>
  /// Gets or sets the executable/script run by the <c>"script"</c> download backend. The request is
  /// passed as JSON on stdin (plus <c>JELLYCROWD_*</c> environment variables). Empty disables it.
  /// </summary>
  public string ScriptPath { get; set; }

  /// <summary>
  /// Gets or sets optional command-line arguments passed to <see cref="ScriptPath"/>.
  /// </summary>
  public string ScriptArguments { get; set; }
}
