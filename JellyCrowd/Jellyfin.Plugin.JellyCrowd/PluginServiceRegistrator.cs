using System;
using System.IO;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.JellyCrowd;

/// <summary>
/// Registers the plugin's services into the Jellyfin dependency injection container at startup.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
  private const string RequestsFileName = "requests.json";
  private const string WatchlistFileName = "watchlist.json";
  private const string NotificationsFileName = "notifications.json";
  private const string UserPrefsFileName = "user-prefs.json";
  private const string CommentsFileName = "comments.json";
  private const string ReportsFileName = "reports.json";
  private const string PollsFileName = "polls.json";
  private const string ActivityLogFileName = "activity.json";
  private const string UserActivityFileName = "user-activity.json";
  private const string PlaybackHistoryFileName = "playback-history.json";
  private const string IntrosFileName = "intros.json";
  private const string OutrosFileName = "outros.json";
  private const string OutroSegmentsFileName = "outro-segments.json";
  private const string PrerollRegistryFileName = "local-intros.json";

  // Which companion half is live, for the admin diagnostics. Every failure in the companion probe is
  // swallowed on purpose, so without this the feature could be off on a server with nothing anywhere
  // saying so — the exact failure that shipped a broken Skip Outro unnoticed before.
  internal static string CompanionStatus { get; private set; } = "No media-segment companion was loaded: Skip Outro and Local Intros are inactive.";

  /// <inheritdoc />
  public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
  {
    // The Local Intros provider inspects the requesting client (web/desktop vs native apps) via the
    // ambient request; ensure the accessor is available. Idempotent — Jellyfin may already register it.
    serviceCollection.AddHttpContextAccessor();
    serviceCollection.AddSingleton<TmdbClient>();
    serviceCollection.AddSingleton<ITmdbClient>(sp => new CachingTmdbClient(sp.GetRequiredService<TmdbClient>()));
    serviceCollection.AddSingleton<ILibraryMatcher, LibraryMatcher>();
    serviceCollection.AddSingleton<IMediaDeleter, MediaDeleter>();
    serviceCollection.AddSingleton<IEmptyLibraryCleaner, EmptyLibraryCleaner>();
    serviceCollection.AddSingleton<ICurrentUserAccessor, CurrentUserAccessor>();
    serviceCollection.AddSingleton<Api.PluginVisibilityFilter>();
    serviceCollection.AddSingleton<RateLimiter>();
    serviceCollection.AddSingleton<Api.RateLimitFilter>();
    serviceCollection.AddSingleton<ITextNotifier, TelegramNotifier>();
    serviceCollection.AddSingleton<ITextNotifier, NtfyNotifier>();
    serviceCollection.AddSingleton<ITextNotifier, GotifyNotifier>();
    serviceCollection.AddSingleton<ITextNotifier, PushoverNotifier>();
    serviceCollection.AddSingleton<ITextNotifier, SlackNotifier>();
    serviceCollection.AddSingleton<ITextNotifier, WebhookNotifier>();
    serviceCollection.AddSingleton<INotificationService, NotificationService>();
    serviceCollection.AddSingleton<IRequestStore>(
      _ => new JsonRequestStore(Path.Combine(Plugin.Instance!.DataFolderPath, RequestsFileName)));
    serviceCollection.AddSingleton<IWatchlistStore>(
      _ => new JsonWatchlistStore(Path.Combine(Plugin.Instance!.DataFolderPath, WatchlistFileName)));
    serviceCollection.AddSingleton<IUserNotificationStore>(
      _ => new JsonUserNotificationStore(Path.Combine(Plugin.Instance!.DataFolderPath, NotificationsFileName)));
    serviceCollection.AddSingleton<IUserPrefsStore>(
      _ => new JsonUserPrefsStore(Path.Combine(Plugin.Instance!.DataFolderPath, UserPrefsFileName)));
    serviceCollection.AddSingleton<IMediaCommentStore>(
      _ => new JsonMediaCommentStore(Path.Combine(Plugin.Instance!.DataFolderPath, CommentsFileName)));
    serviceCollection.AddSingleton<IReportStore>(
      _ => new JsonReportStore(Path.Combine(Plugin.Instance!.DataFolderPath, ReportsFileName)));
    serviceCollection.AddSingleton<IPollStore>(
      _ => new JsonPollStore(Path.Combine(Plugin.Instance!.DataFolderPath, PollsFileName)));
    serviceCollection.AddSingleton<IActivityLog>(
      _ => new JsonActivityLog(Path.Combine(Plugin.Instance!.DataFolderPath, ActivityLogFileName)));
    serviceCollection.AddSingleton<IUserActivityStore>(
      _ => new JsonUserActivityStore(Path.Combine(Plugin.Instance!.DataFolderPath, UserActivityFileName)));
    serviceCollection.AddSingleton<IPlaybackHistoryStore>(
      _ => new JsonPlaybackHistoryStore(Path.Combine(Plugin.Instance!.DataFolderPath, PlaybackHistoryFileName)));
    serviceCollection.AddSingleton<IStatsService, StatsService>();
    serviceCollection.AddSingleton<IPlaybackReportingImporter, PlaybackReportingImporter>();
    serviceCollection.AddSingleton<Func<PluginConfiguration>>(_ => () => Plugin.Instance!.Configuration);
    // Persisting the configuration is a service of its own so the pieces that write settings back (the
    // open-reports reminder stamps when it last ran) stay testable without a live plugin instance.
    serviceCollection.AddSingleton<Action<PluginConfiguration>>(_ => _ => Plugin.Instance!.SaveConfiguration());
    serviceCollection.AddSingleton<IQuotaService>(sp => new QuotaService(
      sp.GetRequiredService<IRequestStore>(),
      sp.GetRequiredService<ILibraryMatcher>(),
      sp.GetRequiredService<IUserActivityStore>(),
      sp.GetRequiredService<Func<PluginConfiguration>>()));
    serviceCollection.AddSingleton<IRequestReconciler, RequestReconciler>();
    serviceCollection.AddSingleton<IQuotaHoldPromoter, QuotaHoldPromoter>();
    serviceCollection.AddSingleton<IRequestCreationGate, RequestCreationGate>();

    // Download backends (request fulfillment). Jelly Crowd only emits requests; the backend searches/downloads.
    serviceCollection.AddSingleton<IServarrClient, ServarrClient>();
    serviceCollection.AddSingleton<ISeriesStructureProvider, SeriesStructureProvider>();
    serviceCollection.AddSingleton<IServarrStatusService, ServarrStatusService>();
    serviceCollection.AddSingleton<IStalledDownloadRecovery, ServarrStalledRecovery>();
    serviceCollection.AddSingleton<IDiagnosticsService, DiagnosticsService>();
    serviceCollection.AddSingleton<IProcessRunner, ProcessRunner>();
    serviceCollection.AddSingleton<IFingerprintExtractor, FingerprintExtractor>();
    // Lazy path: the segment provider depends on these stores and is resolved very early in startup, before
    // Plugin.Instance is set — so the data path must not be touched until the store is first used.
    serviceCollection.AddSingleton<IIntroStore>(
      _ => new JsonIntroStore(() => Path.Combine(Plugin.Instance!.DataFolderPath, IntrosFileName)));
    // Remembers each item's outro analysis so the Media Segment Scan doesn't re-run ffmpeg every pass.
    serviceCollection.AddSingleton<IOutroStore>(
      _ => new JsonOutroStore(() => Path.Combine(Plugin.Instance!.DataFolderPath, OutrosFileName)));

    serviceCollection.AddSingleton<IOutroSegmentStore>(
      _ => new JsonOutroSegmentStore(() => Path.Combine(Plugin.Instance!.DataFolderPath, OutroSegmentsFileName)));
    // Registers local pre-roll videos as standalone items (no browsable "Local Intros" library).
    serviceCollection.AddSingleton<IIntroFileRegistry>(
      _ => new IntroFileRegistry(() => Path.Combine(Plugin.Instance!.DataFolderPath, PrerollRegistryFileName)));
    TryRegisterCompanionProviders(serviceCollection);
    serviceCollection.AddSingleton<IDownloadClient, WebhookDownloadClient>();
    serviceCollection.AddSingleton<IDownloadClient, ServarrDownloadClient>();
    serviceCollection.AddSingleton<IDownloadClient, ScriptDownloadClient>();
    serviceCollection.AddSingleton<Func<Guid, string>>(sp =>
    {
      var userManager = sp.GetRequiredService<IUserManager>();
      return userId =>
      {
        var user = userManager.GetUserById(userId);
        return user?.Username ?? "Unknown";
      };
    });
    serviceCollection.AddSingleton<IDownloadDispatcher, DownloadDispatcher>();

    // Inject the web-client shell at request time via our own middleware (no File Transformation dependency).
    serviceCollection.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter, WebInjectionStartupFilter>();
    serviceCollection.AddHostedService<LibraryEventEntryPoint>();
    serviceCollection.AddHostedService<LocalIntrosEntryPoint>();
    serviceCollection.AddHostedService<PlaybackActivityEntryPoint>();
    serviceCollection.AddHostedService<PlaybackHistoryEntryPoint>();
    serviceCollection.AddHostedService<ConfigChangeLogger>();
  }

  // Skip Outro's IMediaSegmentProvider lives in an ISOLATED companion assembly, shipped TWICE: one half
  // compiled against the 10.11 SDK, one against 12.x. Only one of them can load on a given host, and the
  // other throws — which is precisely why this is not in the main assembly, where a throw would take the
  // whole plugin down. The registrator loads them by reflection (no compile-time reference to the host
  // interfaces here) and keeps the first that actually yields the provider type.
  //
  // Why two halves rather than one portable build: 10.11's IMediaSegmentProvider had no
  // CleanupExtractedData, so the compiler emitted ours as an ordinary NON-VIRTUAL method. Jellyfin 12
  // added it to the interface, and the runtime cannot fill an interface slot with a non-virtual method —
  // "Method 'CleanupExtractedData' ... does not have an implementation". Recompiling the same source
  // against the 12 interface emits it as virtual final. The 12 SDK ships net10.0 only, so that half also
  // targets net10.0, which a 10.11 host (on .NET 9) cannot load — hence "first that loads" rather than a
  // version check: the runtime itself is the arbiter, and a future major needs no new branch here.
  //
  // Both ship as lib/*.dll.bin, and the ".bin" is what makes the isolation real. Jellyfin enumerates
  // "*.dll" under the plugin folder and loads every match: a companion that matches is scanned, throws,
  // and the host disables the WHOLE plugin ("Failed to load assembly ... Disabling plugin"). Two defences
  // that look sufficient are not — meta.json's "assemblies" allowlist is rewritten to [] ("scan
  // everything") by an install from a repository manifest, and the walk is RECURSIVE, so a plain
  // subfolder is scanned too (measured against 12.1). Only the extension takes the file out of that glob;
  // LoadFromAssemblyPath does not care what it is called.
  private static void TryRegisterCompanionProviders(IServiceCollection serviceCollection)
  {
    var mainAssembly = typeof(PluginServiceRegistrator).Assembly;
    var directory = Path.GetDirectoryName(mainAssembly.Location);
    if (directory is null)
    {
      return;
    }

    // Ordered by host: the 12 half first, then 10.11, then the pre-".bin" locations so an install made
    // before the move keeps its Skip Outro instead of silently losing the feature on upgrade. Probing is
    // cheap — the wrong half fails at load, before any of its code runs.
    string[] candidates =
    [
      Path.Combine(directory, "lib", "Jellyfin.Plugin.JellyCrowd.Segments12.dll.bin"),
      Path.Combine(directory, "lib", "Jellyfin.Plugin.JellyCrowd.Segments.dll.bin"),
      Path.Combine(directory, "lib", "Jellyfin.Plugin.JellyCrowd.Segments.dll"),
      Path.Combine(directory, "Jellyfin.Plugin.JellyCrowd.Segments.dll"),
    ];

    // Load into the SAME context as the main assembly so the companion resolves this plugin's own types
    // (PluginConfiguration, SegmentDetection) correctly.
    var context = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(mainAssembly)
      ?? System.Runtime.Loader.AssemblyLoadContext.Default;

    foreach (var path in candidates)
    {
      if (!File.Exists(path))
      {
        continue;
      }

      // Jellyfin consumes IMediaSegmentProvider via DI, so registering the hidden companion type works.
      // IIntroProvider is instead discovered by assembly scanning, so JellyCrowdIntroProvider lives in the
      // (scanned) main assembly, not here.
      if (TryRegisterCompanionProvider(serviceCollection, context, path, "Jellyfin.Plugin.JellyCrowd.Segments.JellyCrowdSegmentProvider", "IMediaSegmentProvider"))
      {
        CompanionStatus = "Loaded " + Path.GetFileName(path) + ": Skip Outro is active.";
        return;
      }
    }
  }

  // Returns true once a companion has been registered, so the caller stops probing. Every failure mode —
  // an assembly built for another runtime, a moved type, a changed interface — is swallowed on purpose:
  // the next candidate gets its turn, and if none fits, Skip Outro turns itself off while the rest of the
  // plugin carries on.
  private static bool TryRegisterCompanionProvider(
    IServiceCollection serviceCollection,
    System.Runtime.Loader.AssemblyLoadContext context,
    string path,
    string typeName,
    string interfaceName)
  {
    try
    {
      var assembly = context.LoadFromAssemblyPath(path);
      var providerType = assembly.GetType(typeName, throwOnError: false);
      if (providerType is null)
      {
        return false;
      }

      var hostInterface = Array.Find(providerType.GetInterfaces(), i => string.Equals(i.Name, interfaceName, StringComparison.Ordinal));
      if (hostInterface is null)
      {
        return false;
      }

      serviceCollection.AddSingleton(hostInterface, providerType);
      return true;
    }
#pragma warning disable CA1031 // Wrong half for this runtime: fall through and try the next candidate.
    catch (Exception)
#pragma warning restore CA1031
    {
      return false;
    }
  }
}
