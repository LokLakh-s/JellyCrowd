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

  /// <inheritdoc />
  public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
  {
    serviceCollection.AddSingleton<ITmdbClient, TmdbClient>();
    serviceCollection.AddSingleton<ILibraryMatcher, LibraryMatcher>();
    serviceCollection.AddSingleton<IMediaDeleter, MediaDeleter>();
    serviceCollection.AddSingleton<ICurrentUserAccessor, CurrentUserAccessor>();
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
    serviceCollection.AddSingleton<Func<PluginConfiguration>>(_ => () => Plugin.Instance!.Configuration);
    serviceCollection.AddSingleton<IQuotaService>(sp => new QuotaService(
      sp.GetRequiredService<IRequestStore>(),
      sp.GetRequiredService<ILibraryMatcher>(),
      sp.GetRequiredService<Func<PluginConfiguration>>()));
    serviceCollection.AddSingleton<IRequestReconciler, RequestReconciler>();

    // Download backends (request fulfillment). Jelly Crowd only emits requests; the backend searches/downloads.
    serviceCollection.AddSingleton<IServarrClient, ServarrClient>();
    serviceCollection.AddSingleton<IServarrStatusService, ServarrStatusService>();
    serviceCollection.AddSingleton<IDiagnosticsService, DiagnosticsService>();
    serviceCollection.AddSingleton<IProcessRunner, ProcessRunner>();
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

    serviceCollection.AddHostedService<WebInjectionService>();
    serviceCollection.AddHostedService<LibraryEventEntryPoint>();
  }
}
