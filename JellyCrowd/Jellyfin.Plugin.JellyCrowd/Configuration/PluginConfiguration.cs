using System;
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
    AdaptiveQuotaEnabled = false;
    AdaptiveFloorPercent = 40;
    AdaptiveCeilingPercent = 200;
    AdaptiveWindowDays = 14;
    AdaptiveMinMinutes = 180;
    AdaptiveMinActiveDays = 3;
    AdaptiveInactivityDays = 30;
    AdaptiveProbationDays = 14;
    RequireApproval = true;
    MoviesEnabled = true;
    SeriesEnabled = true;
    AllowSeriesRequests = true;
    AllowSeasonRequests = true;
    AllowEpisodeRequests = true;
    HiddenFromUsers = false;
    RateLimitPerMinute = 120;
    RateLimitGetPerMinute = 600;
    CommentsEnabled = false;
    AnnouncementText = string.Empty;
    AnnouncementLevel = "green";
    DiscordInviteEnabled = false;
    DiscordInviteUrl = string.Empty;
    SupportLinkEnabled = false;
    SupportLinkUrl = string.Empty;
    GuideLinkEnabled = false;
    GuideLinkUrl = string.Empty;
    StatsEnabled = true;
    SkipOutroEnabled = false;
    SkipIntroEnabled = false;
    OutroAnalyzeMaxSeconds = 720;
    OutroAnalyzeTimeoutSeconds = 180;
    SegmentHwAccel = "auto";
    OutroMinCreditsSeconds = 20;
    OutroMaxCreditsSeconds = 900;
    OutroDarkFraction = 0.25;
    OutroMinCreditRunSeconds = 15;
    OutroMinBonusRunSeconds = 10;
    OutroMaxBonusGapSeconds = 90;
    OutroMaxTrailingBonusSeconds = 150;
    OutroMinTrailingSilenceSeconds = 20;
    OutroSilenceEndToleranceSeconds = 15;
    OutroFingerprintSeconds = 300;
    IntroAnalyzeSeconds = 600;
    IntroAnalyzeTimeoutSeconds = 120;
    IntroMinDurationSeconds = 15;
    IntroMinConfirmations = 1;
    SegmentMinSeasonCoveragePercent = 50;
    LocalIntrosEnabled = false;
    LocalIntrosFolderName = "intros";
    LocalIntrosOnMovies = true;
    LocalIntrosOnFirstEpisode = true;
    LocalIntrosRandomizeSingle = true;
    LocalIntrosForceCinemaMode = true;
    LocalIntrosNonSkippable = true;
    LocalIntrosWebOnly = true;
    BrandingEnabled = false;
    BrandingLogoUrl = string.Empty;
    BrandingFaviconUrl = string.Empty;
    BrandingDefaultAvatarUrl = string.Empty;
    BrandingBackgroundUrl = string.Empty;
    BrandingBackgroundColor = string.Empty;
    BrandingAccentColor = string.Empty;
    BrandingFontFamily = string.Empty;
    BrandingFontUrl = string.Empty;
    BrandingCustomCss = string.Empty;
    BrandingPresetCompactEpisodes = false;
    BrandingPresetDarkIndicators = false;
    BrandingPresetNarrowChannels = false;
    BrandingPresetHideBackdrop = false;
    BrandingPresetButtonTweaks = false;
    MediaExpiryDays = 90;
    PartialAvailabilityGraceHours = 48;
    EstimatedMovieSizeBytes = 5L * 1024 * 1024 * 1024; // 5 GiB
    EstimatedEpisodeSizeBytes = 1L * 1024 * 1024 * 1024; // 1 GiB
    MaxRequestsPerPeriod = 0;
    RequestPeriod = RequestPeriod.Week;
    DeletionRetentionHours = 24;
    RemoveEmptySeries = true;
    EmptySeriesMinAgeHours = 24;
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
    EmailNotifyCreated = true;
    EmailNotifyApproved = false;
    EmailNotifyDenied = false;
    EmailNotifyAvailable = false;
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
    RecoverStalledDownloads = false;
    StalledRecoveryMinutes = 60;
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
  /// Gets or sets a value indicating whether the adaptive quota is enabled. When off, every user keeps
  /// their fixed base quota (override or default) — the historical behaviour.
  /// </summary>
  public bool AdaptiveQuotaEnabled { get; set; }

  /// <summary>
  /// Gets or sets the floor tier as a percentage of the base quota (resting quota for inactive users, e.g. 40).
  /// </summary>
  public int AdaptiveFloorPercent { get; set; }

  /// <summary>
  /// Gets or sets the ceiling tier as a percentage of the base quota (reward for active users, e.g. 200).
  /// </summary>
  public int AdaptiveCeilingPercent { get; set; }

  /// <summary>
  /// Gets or sets the rolling window (in days) over which viewing activity is evaluated.
  /// </summary>
  public int AdaptiveWindowDays { get; set; }

  /// <summary>
  /// Gets or sets the minimum watch minutes within the window required to count as "active" (volume signal).
  /// </summary>
  public int AdaptiveMinMinutes { get; set; }

  /// <summary>
  /// Gets or sets the minimum number of distinct active days within the window required to count as "active" (regularity signal).
  /// </summary>
  public int AdaptiveMinActiveDays { get; set; }

  /// <summary>
  /// Gets or sets the number of days without activity that puts an elevated (or base) user on notice / decays them.
  /// </summary>
  public int AdaptiveInactivityDays { get; set; }

  /// <summary>
  /// Gets or sets the probation length (in days): a returning, previously-rewarded user keeps their frozen
  /// quota for this long; if they don't become active again, they drop to the base quota (not the floor).
  /// </summary>
  public int AdaptiveProbationDays { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether new requests require admin approval before fulfillment.
  /// </summary>
  public bool RequireApproval { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether movies are offered at all (catalog, calendar and requests).
  /// Turn off for a series-only instance. At least one of this and <see cref="SeriesEnabled"/> stays on.
  /// </summary>
  public bool MoviesEnabled { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether TV shows are offered at all (catalog, calendar and requests).
  /// Turn off for a movies-only instance. At least one of this and <see cref="MoviesEnabled"/> stays on.
  /// </summary>
  public bool SeriesEnabled { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether users may request a whole series at once. Applies to TV only;
  /// at least one of the three request-granularity options stays on.
  /// </summary>
  public bool AllowSeriesRequests { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether users may request a single season. Applies to TV only;
  /// at least one of the three request-granularity options stays on.
  /// </summary>
  public bool AllowSeasonRequests { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether users may request individual episodes. Applies to TV only;
  /// at least one of the three request-granularity options stays on.
  /// </summary>
  public bool AllowEpisodeRequests { get; set; }

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
  /// Gets or sets a value indicating whether the deletion task also sweeps the library for empty series
  /// (0 episodes) — ghost entries Jellyfin keeps after their files are deleted — and removes them. On by
  /// default; guarded by <see cref="EmptySeriesMinAgeHours"/> and by any active request for the title.
  /// </summary>
  public bool RemoveEmptySeries { get; set; }

  /// <summary>
  /// Gets or sets the minimum age (in hours) an empty series must have before it can be removed by the
  /// sweep, so a just-added series still downloading its first episodes is never deleted. Default 24.
  /// </summary>
  public int EmptySeriesMinAgeHours { get; set; }

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
  /// Gets or sets the maximum number of read (GET) requests a single non-admin user may make per minute
  /// to the plugin's API. 0 disables it. Set well above normal browsing (a catalog page is ~15 GETs):
  /// this only exists to stop a scripted loop from hammering the catalog endpoints, which fan out to
  /// TMDB (up to ~30 detail calls per "Popular"/"Calendar" call) and can exhaust the shared TMDB key.
  /// </summary>
  public int RateLimitGetPerMinute { get; set; }

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
  /// Gets or sets a value indicating whether Jellyfin's left navigation drawer (the hamburger menu) is
  /// hidden for non-administrators (admin opt-in, off by default). Admins always keep it.
  /// </summary>
  public bool HideNativeDrawerForNonAdmins { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether a Discord invite icon is shown in the header (admin opt-in,
  /// off by default). Only rendered when enabled and <see cref="DiscordInviteUrl"/> is set.
  /// </summary>
  public bool DiscordInviteEnabled { get; set; }

  /// <summary>
  /// Gets or sets the Discord invite URL the header icon links to.
  /// </summary>
  public string DiscordInviteUrl { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether a "support the platform" icon is shown in the header
  /// (admin opt-in, off by default). Only rendered when enabled and <see cref="SupportLinkUrl"/> is set.
  /// </summary>
  public bool SupportLinkEnabled { get; set; }

  /// <summary>
  /// Gets or sets the support/donation URL the header icon links to.
  /// </summary>
  public string SupportLinkUrl { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether a "user guide" icon is shown in the header (admin opt-in,
  /// off by default). Only rendered when enabled and <see cref="GuideLinkUrl"/> is set.
  /// </summary>
  public bool GuideLinkEnabled { get; set; }

  /// <summary>
  /// Gets or sets the user-guide URL the header icon links to.
  /// </summary>
  public string GuideLinkUrl { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether playback history is captured for the statistics screens.
  /// On by default; turning it off stops new capture (existing history is kept).
  /// </summary>
  public bool StatsEnabled { get; set; }

  // ----- Skip intro / outro (native Jellyfin media segments) -----

  /// <summary>
  /// Gets or sets a value indicating whether the plugin detects end credits and exposes a native
  /// "Skip Outro" segment on movies and episodes. Off by default (analysis is CPU-heavy and heuristic).
  /// </summary>
  public bool SkipOutroEnabled { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether the plugin detects episode intros (audio fingerprinting)
  /// and exposes a native "Skip Intro" segment. Off by default.
  /// </summary>
  public bool SkipIntroEnabled { get; set; }

  /// <summary>
  /// Gets or sets how many seconds from the start of each episode are fingerprinted when searching for the
  /// shared intro (the intro can sit well past a long cold open, so this window is generous).
  /// </summary>
  public int IntroAnalyzeSeconds { get; set; }

  /// <summary>
  /// Gets or sets the timeout (seconds) for a single episode fingerprint extraction.
  /// </summary>
  public int IntroAnalyzeTimeoutSeconds { get; set; }

  /// <summary>
  /// Gets or sets the minimum intro length (seconds); shorter shared snippets (logos, stings) are ignored.
  /// </summary>
  public int IntroMinDurationSeconds { get; set; }

  /// <summary>
  /// Gets or sets how many sibling episodes must confirm a shared region before it is accepted as the intro.
  /// </summary>
  public int IntroMinConfirmations { get; set; }

  /// <summary>
  /// Gets or sets the percentage of a season's episodes that must share a sequence before any of it is
  /// accepted, for both intro and end-credits fingerprinting. A real title or credits sequence recurs
  /// across the season; when only a couple of episodes "share" something it is a coincidence between two
  /// quiet passages, and serving it would put a skip button in the middle of a scene. <c>0</c> disables
  /// the check.
  /// </summary>
  public int SegmentMinSeasonCoveragePercent { get; set; }

  // ----- Local intros (pre-roll played before content via Jellyfin's Cinema Mode) -----

  /// <summary>
  /// Gets or sets a value indicating whether a local pre-roll is played before content. Off by default.
  /// </summary>
  public bool LocalIntrosEnabled { get; set; }

  /// <summary>
  /// Gets or sets the pre-roll folder name (default <c>intros</c>). The admin just creates a folder with
  /// this name beside their libraries; the plugin discovers it under the media roots, indexes it (as a
  /// hidden-ish "Local Intros" library so the files become playable items), and serves its videos.
  /// </summary>
  public string LocalIntrosFolderName { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether the pre-roll plays before movies.
  /// </summary>
  public bool LocalIntrosOnMovies { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether the pre-roll plays before the first episode of a series (S01E01).
  /// </summary>
  public bool LocalIntrosOnFirstEpisode { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether, when the folder holds several videos, one is picked at random
  /// per playback; when <c>false</c>, all of them play in order.
  /// </summary>
  public bool LocalIntrosRandomizeSingle { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether the web client's Cinema Mode is force-enabled so the pre-roll
  /// actually plays (it is a per-user setting, off by default).
  /// </summary>
  public bool LocalIntrosForceCinemaMode { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether the pre-roll is made non-skippable with hidden player controls
  /// (web client only, via the injected script).
  /// </summary>
  public bool LocalIntrosNonSkippable { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether local intros are served only to web and desktop players
  /// (Jellyfin Web / Jellyfin Media Player). Native mobile and TV apps can't run the pre-roll injection,
  /// and some (e.g. iOS/iPadOS) fail to start playback when a raw pre-roll is prepended to the queue, so
  /// they are excluded by default. Turn off to serve intros to every client.
  /// </summary>
  public bool LocalIntrosWebOnly { get; set; }

  /// <summary>
  /// Gets or sets the maximum number of seconds of the file tail to analyze for the outro (keeps long
  /// movie scans bounded). The window is the smaller of 20% of the runtime and this cap.
  /// </summary>
  public double OutroAnalyzeMaxSeconds { get; set; }

  /// <summary>
  /// Gets or sets how many seconds of each episode's TAIL are fingerprinted when looking for a season's
  /// shared end-credits sequence. The brightness/silence heuristic above cannot see credits that are
  /// bright and sung (an anime ED) — but those are identical in every episode, so a fingerprint finds
  /// them, exactly as it already does for intros.
  /// </summary>
  public int OutroFingerprintSeconds { get; set; }

  /// <summary>
  /// Gets or sets the timeout (seconds) for a single outro analysis process before it is killed.
  /// </summary>
  public int OutroAnalyzeTimeoutSeconds { get; set; }

  /// <summary>
  /// Gets or sets the ffmpeg hardware-acceleration mode for the Skip Outro video decode (the Media Segment
  /// Scan's heavy cost): <c>auto</c> (GPU if available, else CPU), <c>none</c>, <c>vaapi</c>, <c>qsv</c>,
  /// <c>cuda</c> or <c>videotoolbox</c>. Only the decode is offloaded; the analysis filters run on 1 fps.
  /// </summary>
  public string SegmentHwAccel { get; set; }

  /// <summary>
  /// Gets or sets the minimum credits length (seconds) after the end-of-content fade for it to count as
  /// an outro — guards against marking a brief mid-content fade.
  /// </summary>
  public double OutroMinCreditsSeconds { get; set; }

  /// <summary>
  /// Gets or sets the maximum credits length (seconds) after the fade for it to count as an outro.
  /// </summary>
  public double OutroMaxCreditsSeconds { get; set; }

  /// <summary>
  /// Gets or sets the darkness threshold as a fraction of the clip's reference brightness: a second whose
  /// luma is below this is treated as dark credits. Relative, so it works across 8- and 10-bit encodes.
  /// </summary>
  public double OutroDarkFraction { get; set; }

  /// <summary>
  /// Gets or sets the minimum length (seconds) of a dark-or-silent run for it to count as end credits
  /// (rejects a brief dark shot at the very end of the story).
  /// </summary>
  public double OutroMinCreditRunSeconds { get; set; }

  /// <summary>
  /// Gets or sets the minimum length (seconds) of a bright-with-audio break inside the credits for it to
  /// be treated as a bonus scene; shorter bright blips are absorbed into the credits.
  /// </summary>
  public double OutroMinBonusRunSeconds { get; set; }

  /// <summary>
  /// Gets or sets the maximum bright gap (seconds) between two credit runs for them to be grouped into one
  /// outro (a mid-credits bonus between them); a longer gap is the story body and stops the grouping.
  /// </summary>
  public double OutroMaxBonusGapSeconds { get; set; }

  /// <summary>
  /// Gets or sets the maximum bright content (seconds) allowed after the last credit run while still
  /// treating it as a post-credits bonus; more than this means the "credits" were a dark scene, so no
  /// segment is emitted.
  /// </summary>
  public double OutroMaxTrailingBonusSeconds { get; set; }

  /// <summary>
  /// Gets or sets the minimum length (seconds) of a silence that, running to the end of the item, counts
  /// as a silent/quiet end card. Scattered dialogue pauses are ignored — only this trailing silence does.
  /// </summary>
  public double OutroMinTrailingSilenceSeconds { get; set; }

  /// <summary>
  /// Gets or sets how close (seconds) to the end the trailing silence must reach to count as credits.
  /// </summary>
  public double OutroSilenceEndToleranceSeconds { get; set; }

  // ----- Branding (cosmetic theming applied to the whole Jellyfin web UI by header.js) -----

  /// <summary>
  /// Gets or sets a value indicating whether branding is applied (master switch). Off leaves Jellyfin's
  /// own appearance untouched.
  /// </summary>
  public bool BrandingEnabled { get; set; }

  /// <summary>Gets or sets the navbar logo image URL (empty = Jellyfin default).</summary>
  public string BrandingLogoUrl { get; set; }

  /// <summary>Gets or sets the favicon image URL (empty = Jellyfin default).</summary>
  public string BrandingFaviconUrl { get; set; }

  /// <summary>Gets or sets the default profile-picture URL for users without one (empty = Jellyfin initials).</summary>
  public string BrandingDefaultAvatarUrl { get; set; }

  /// <summary>Gets or sets the page background image URL (empty = Jellyfin default).</summary>
  public string BrandingBackgroundUrl { get; set; }

  /// <summary>Gets or sets the page background colour (CSS colour; empty = Jellyfin default).</summary>
  public string BrandingBackgroundColor { get; set; }

  /// <summary>Gets or sets the accent colour for primary buttons / active states (CSS colour; empty = default).</summary>
  public string BrandingAccentColor { get; set; }

  /// <summary>Gets or sets the CSS font-family applied to the UI (empty = Jellyfin default).</summary>
  public string BrandingFontFamily { get; set; }

  /// <summary>Gets or sets an optional font stylesheet URL (e.g. Google Fonts) imported before use.</summary>
  public string BrandingFontUrl { get; set; }

  /// <summary>Gets or sets free-form custom CSS appended last (highest priority).</summary>
  public string BrandingCustomCss { get; set; }

  /// <summary>Gets or sets a value indicating whether the compact episode-list layout preset is on.</summary>
  public bool BrandingPresetCompactEpisodes { get; set; }

  /// <summary>Gets or sets a value indicating whether the dark/transparent indicators preset is on.</summary>
  public bool BrandingPresetDarkIndicators { get; set; }

  /// <summary>Gets or sets a value indicating whether the narrower Live TV channels preset is on.</summary>
  public bool BrandingPresetNarrowChannels { get; set; }

  /// <summary>Gets or sets a value indicating whether the hide-item-backdrop preset is on.</summary>
  public bool BrandingPresetHideBackdrop { get; set; }

  /// <summary>Gets or sets a value indicating whether the roomier raised-button preset is on.</summary>
  public bool BrandingPresetButtonTweaks { get; set; }

  /// <summary>
  /// Gets or sets the custom entries injected into the Jellyfin left navigation drawer.
  /// </summary>
  [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Must be settable so System.Text.Json can replace it when deserializing the posted plugin configuration (a get-only collection is silently skipped on deserialize, which dropped the saved value).")]
  public Collection<DrawerLink> BrandingDrawerLinks { get; set; } = new();

  /// <summary>
  /// Gets or sets the media ownership expiry window in days. A user's ownership of an available title
  /// lapses (freeing their quota) this many days after it became available / was last claimed; claiming
  /// again resets the countdown. The media file is never auto-deleted (deletion stays on-demand).
  /// 0 disables expiry (ownerships never lapse).
  /// </summary>
  public int MediaExpiryDays { get; set; }

  /// <summary>
  /// Gets or sets how long a partly delivered season or whole-series request may go without a new episode
  /// arriving before it is marked available with what it has. It is otherwise marked available only once
  /// every aired episode is in the library — this keeps a never-found episode, or a show numbered
  /// differently by TMDB and the library, from holding it pending forever.
  /// </summary>
  public int PartialAvailabilityGraceHours { get; set; }

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
  /// Gets or sets the admin-defined user groups. A group supplies default settings its members inherit
  /// (a per-user override still wins) and libraries the admin can push onto members' Jellyfin accounts.
  /// </summary>
  [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Must be settable so System.Text.Json can replace it when deserializing the posted plugin configuration (a get-only collection is silently skipped on deserialize, which dropped the saved value).")]
  public Collection<UserGroup> UserGroups { get; set; } = new();

  /// <summary>
  /// Gets or sets the group ids a targeted announcement is shown to. Empty means the announcement is
  /// shown to everyone.
  /// </summary>
  [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Must be settable so System.Text.Json can replace it when deserializing the posted plugin configuration (a get-only collection is silently skipped on deserialize, which dropped the saved value).")]
  public Collection<Guid> AnnouncementGroupIds { get; set; } = new();

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
  /// Gets or sets a value indicating whether the ops mailbox is emailed when a request is created.
  /// Default on (new requests are admin-actionable). Per-user lifecycle notifications still go to the
  /// requester's own channels regardless of these flags.
  /// </summary>
  public bool EmailNotifyCreated { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether the ops mailbox is emailed when a request is approved. Default off.
  /// </summary>
  public bool EmailNotifyApproved { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether the ops mailbox is emailed when a request is denied/failed. Default off.
  /// </summary>
  public bool EmailNotifyDenied { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether the ops mailbox is emailed when a request becomes available. Default off.
  /// </summary>
  public bool EmailNotifyAvailable { get; set; }

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
  /// Gets or sets a value indicating whether to auto-recover stalled downloads: a grab stuck without
  /// progress past <see cref="StalledRecoveryMinutes"/> is blocklisted and re-searched so Radarr/Sonarr
  /// grab a different release. Off by default. Only applies to the Radarr/Sonarr backend.
  /// </summary>
  public bool RecoverStalledDownloads { get; set; }

  /// <summary>
  /// Gets or sets how many minutes a download may be stalled (no progress) before it is recovered.
  /// </summary>
  public int StalledRecoveryMinutes { get; set; }

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
