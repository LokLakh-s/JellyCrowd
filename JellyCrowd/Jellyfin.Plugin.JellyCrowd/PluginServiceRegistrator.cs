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
  private const string ActivityLogFileName = "activity.json";
  private const string UserActivityFileName = "user-activity.json";
  private const string PlaybackHistoryFileName = "playback-history.json";

  /// <inheritdoc />
  public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
  {
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
    serviceCollection.AddSingleton<IActivityLog>(
      _ => new JsonActivityLog(Path.Combine(Plugin.Instance!.DataFolderPath, ActivityLogFileName)));
    serviceCollection.AddSingleton<IUserActivityStore>(
      _ => new JsonUserActivityStore(Path.Combine(Plugin.Instance!.DataFolderPath, UserActivityFileName)));
    serviceCollection.AddSingleton<IPlaybackHistoryStore>(
      _ => new JsonPlaybackHistoryStore(Path.Combine(Plugin.Instance!.DataFolderPath, PlaybackHistoryFileName)));
    serviceCollection.AddSingleton<IStatsService, StatsService>();
    serviceCollection.AddSingleton<IPlaybackReportingImporter, PlaybackReportingImporter>();
    serviceCollection.AddSingleton<Func<PluginConfiguration>>(_ => () => Plugin.Instance!.Configuration);
    serviceCollection.AddSingleton<IQuotaService>(sp => new QuotaService(
      sp.GetRequiredService<IRequestStore>(),
      sp.GetRequiredService<ILibraryMatcher>(),
      sp.GetRequiredService<IUserActivityStore>(),
      sp.GetRequiredService<Func<PluginConfiguration>>()));
    serviceCollection.AddSingleton<IRequestReconciler, RequestReconciler>();
    serviceCollection.AddSingleton<IQuotaHoldPromoter, QuotaHoldPromoter>();

    // Download backends (request fulfillment). Jelly Crowd only emits requests; the backend searches/downloads.
    serviceCollection.AddSingleton<IServarrClient, ServarrClient>();
    serviceCollection.AddSingleton<IServarrStatusService, ServarrStatusService>();
    serviceCollection.AddSingleton<IStalledDownloadRecovery, ServarrStalledRecovery>();
    serviceCollection.AddSingleton<IDiagnosticsService, DiagnosticsService>();
    serviceCollection.AddSingleton<IProcessRunner, ProcessRunner>();
    TryRegisterSegmentProvider(serviceCollection);
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
    serviceCollection.AddHostedService<PlaybackActivityEntryPoint>();
    serviceCollection.AddHostedService<PlaybackHistoryEntryPoint>();
    serviceCollection.AddHostedService<ConfigChangeLogger>();
  }

  // Skip Outro's IMediaSegmentProvider lives in an ISOLATED companion assembly
  // (Jellyfin.Plugin.JellyCrowd.Segments). Jellyfin 12 moved the segment types to different assemblies, so
  // a 10.11-compiled provider throws a TypeLoadException there — and if that type were in THIS assembly it
  // would take the whole plugin down. Loading + registering it by reflection (with no compile-time
  // reference to the segment API) keeps this assembly clean on any runtime: on an incompatible one the
  // load throws and is swallowed, so skip-outro turns itself off while the rest of the plugin keeps working.
  private static void TryRegisterSegmentProvider(IServiceCollection serviceCollection)
  {
    try
    {
      var mainAssembly = typeof(PluginServiceRegistrator).Assembly;
      var directory = Path.GetDirectoryName(mainAssembly.Location);
      if (directory is null)
      {
        return;
      }

      var path = Path.Combine(directory, "Jellyfin.Plugin.JellyCrowd.Segments.dll");
      if (!File.Exists(path))
      {
        return;
      }

      // Load into the SAME context as the main assembly so the companion resolves this plugin's own
      // types (PluginConfiguration, SegmentDetection) correctly.
      var context = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(mainAssembly)
        ?? System.Runtime.Loader.AssemblyLoadContext.Default;
      var assembly = context.LoadFromAssemblyPath(path);
      var providerType = assembly.GetType("Jellyfin.Plugin.JellyCrowd.Segments.JellyCrowdSegmentProvider", throwOnError: false);
      if (providerType is null)
      {
        return;
      }

      var segmentInterface = Array.Find(providerType.GetInterfaces(), i => string.Equals(i.Name, "IMediaSegmentProvider", StringComparison.Ordinal));
      if (segmentInterface is not null)
      {
        serviceCollection.AddSingleton(segmentInterface, providerType);
      }
    }
#pragma warning disable CA1031 // Incompatible runtime (e.g. Jellyfin 12's segment API): skip skip-outro, keep the rest of the plugin.
    catch (Exception)
#pragma warning restore CA1031
    {
    }
  }
}
