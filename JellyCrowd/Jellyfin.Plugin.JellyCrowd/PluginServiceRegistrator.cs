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

  /// <inheritdoc />
  public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
  {
    serviceCollection.AddSingleton<ITmdbClient, TmdbClient>();
    serviceCollection.AddSingleton<ILibraryMatcher, LibraryMatcher>();
    serviceCollection.AddSingleton<IMediaDeleter, MediaDeleter>();
    serviceCollection.AddSingleton<ICurrentUserAccessor, CurrentUserAccessor>();
    serviceCollection.AddSingleton<INotificationService, NotificationService>();
    serviceCollection.AddSingleton<IRequestStore>(
      _ => new JsonRequestStore(Path.Combine(Plugin.Instance!.DataFolderPath, RequestsFileName)));
    serviceCollection.AddSingleton<Func<PluginConfiguration>>(_ => () => Plugin.Instance!.Configuration);
    serviceCollection.AddSingleton<IQuotaService>(sp => new QuotaService(
      sp.GetRequiredService<IRequestStore>(),
      sp.GetRequiredService<ILibraryMatcher>(),
      sp.GetRequiredService<Func<PluginConfiguration>>()));
    serviceCollection.AddSingleton<IRequestReconciler, RequestReconciler>();

    // Download backends (request fulfillment). Jelly Crowd only emits requests; the backend searches/downloads.
    serviceCollection.AddSingleton<IDownloadClient, WebhookDownloadClient>();
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
