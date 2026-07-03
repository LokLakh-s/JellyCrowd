using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JellyCrowd.Providers;

/// <summary>
/// Supplies a local pre-roll (a "Local Intro") before content via Jellyfin's Cinema Mode. The admin drops
/// pre-roll videos in a named folder beside their media; this returns them before movies and before the
/// first episode of a series.
/// </summary>
/// <remarks>
/// <para>Lives in the MAIN assembly because Jellyfin discovers <see cref="IIntroProvider"/> implementations
/// by scanning the plugin's listed assemblies (the legacy <c>AddParts</c>/<c>GetExports</c> path), not via
/// DI. Safe on Jellyfin 12: <see cref="IIntroProvider"/>/<see cref="IntroInfo"/> are unchanged there.</para>
/// <para>The pre-roll files must be indexed by a Jellyfin library: the server plays an intro only when its
/// resolved item exists in the database (<c>GetItemById</c>), so a bare file in an unscanned folder is
/// silently ignored. <see cref="LocalIntrosEntryPoint"/> indexes the folder, and this returns item ids.</para>
/// </remarks>
public sealed class JellyCrowdIntroProvider : IIntroProvider
{
  private readonly ILibraryManager _libraryManager;
  private readonly Func<PluginConfiguration> _config;

  /// <summary>
  /// Initializes a new instance of the <see cref="JellyCrowdIntroProvider"/> class.
  /// </summary>
  /// <param name="libraryManager">Resolves pre-roll files to their indexed library items.</param>
  /// <param name="config">Accessor for the current plugin configuration.</param>
  public JellyCrowdIntroProvider(ILibraryManager libraryManager, Func<PluginConfiguration> config)
  {
    _libraryManager = libraryManager;
    _config = config;
  }

  /// <inheritdoc />
  public string Name => "Jelly Crowd Local Intros";

  /// <inheritdoc />
  public Task<IEnumerable<IntroInfo>> GetIntros(BaseItem item, User user)
  {
    ArgumentNullException.ThrowIfNull(item);

    var config = _config();
    if (!config.LocalIntrosEnabled || !AppliesTo(item, config))
    {
      return Task.FromResult(Enumerable.Empty<IntroInfo>());
    }

    // The server plays an intro only when its item exists in the database (see remarks).
    var ids = LocalIntrosDiscovery.FindItemIds(_libraryManager, config.LocalIntrosFolderName);
    if (ids.Count == 0)
    {
      return Task.FromResult(Enumerable.Empty<IntroInfo>());
    }

    IEnumerable<IntroInfo> intros = config.LocalIntrosRandomizeSingle
      ? new[] { new IntroInfo { ItemId = ids[Random.Shared.Next(ids.Count)] } }
      : ids.Select(id => new IntroInfo { ItemId = id });

    return Task.FromResult(intros);
  }

  private static bool AppliesTo(BaseItem item, PluginConfiguration config) => item switch
  {
    Movie => config.LocalIntrosOnMovies,

    // First episode of a series: season 1, episode 1.
    Episode episode => config.LocalIntrosOnFirstEpisode && episode.IndexNumber == 1 && episode.ParentIndexNumber == 1,

    _ => false,
  };
}
