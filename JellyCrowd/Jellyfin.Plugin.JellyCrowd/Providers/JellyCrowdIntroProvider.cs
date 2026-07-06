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
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Http;

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
  private readonly IHttpContextAccessor? _httpContextAccessor;
  private readonly IAuthorizationContext? _authorizationContext;

  /// <summary>
  /// Initializes a new instance of the <see cref="JellyCrowdIntroProvider"/> class.
  /// </summary>
  /// <param name="libraryManager">Resolves pre-roll files to their indexed library items.</param>
  /// <param name="config">Accessor for the current plugin configuration.</param>
  /// <param name="httpContextAccessor">The ambient request accessor (used to detect the client). Optional
  /// so the provider still instantiates if it isn't available — the client gate then fails open.</param>
  /// <param name="authorizationContext">Resolves the request's client name. Optional (fails open).</param>
  public JellyCrowdIntroProvider(
    ILibraryManager libraryManager,
    Func<PluginConfiguration> config,
    IHttpContextAccessor? httpContextAccessor = null,
    IAuthorizationContext? authorizationContext = null)
  {
    _libraryManager = libraryManager;
    _config = config;
    _httpContextAccessor = httpContextAccessor;
    _authorizationContext = authorizationContext;
  }

  /// <inheritdoc />
  public string Name => "Jelly Crowd Local Intros";

  /// <inheritdoc />
  public async Task<IEnumerable<IntroInfo>> GetIntros(BaseItem item, User user)
  {
    ArgumentNullException.ThrowIfNull(item);

    var config = _config();
    if (!config.LocalIntrosEnabled || !AppliesTo(item, config))
    {
      return Enumerable.Empty<IntroInfo>();
    }

    // Native mobile/TV apps can't run the pre-roll injection, and some (reported on iPad) fail to start
    // playback when a raw pre-roll is prepended to the queue — so by default only web/desktop players get
    // local intros.
    if (config.LocalIntrosWebOnly && !await IsWebOrDesktopClientAsync().ConfigureAwait(false))
    {
      return Enumerable.Empty<IntroInfo>();
    }

    // The server plays an intro only when its item exists in the database (see remarks).
    var ids = LocalIntrosDiscovery.FindItemIds(_libraryManager, config.LocalIntrosFolderName);
    if (ids.Count == 0)
    {
      return Enumerable.Empty<IntroInfo>();
    }

    return config.LocalIntrosRandomizeSingle
      ? new[] { new IntroInfo { ItemId = ids[Random.Shared.Next(ids.Count)] } }
      : ids.Select(id => new IntroInfo { ItemId = id });
  }

  // Only the clients that reliably play a prepended pre-roll receive local intros when the web-only
  // restriction is on: the browser web client and the desktop Jellyfin Media Player. Resolved from the
  // current request's authorization. Fails CLOSED (returns false) when the client can't be positively
  // identified — the restriction exists to keep a pre-roll off native apps, where it breaks playback, so an
  // unidentified client is treated as "not web/desktop". The normal web flow always carries the request, so
  // this never blocks a genuine web user.
  private async Task<bool> IsWebOrDesktopClientAsync()
  {
    var request = _httpContextAccessor?.HttpContext?.Request;
    if (request is null || _authorizationContext is null)
    {
      return false;
    }

    string client;
    try
    {
      var info = await _authorizationContext.GetAuthorizationInfo(request).ConfigureAwait(false);
      client = info.Client ?? string.Empty;
    }
#pragma warning disable CA1031 // A detection failure must not risk a broken pre-roll on native: treat as "not web".
    catch (Exception)
#pragma warning restore CA1031
    {
      return false;
    }

    return IsWebOrDesktopClient(client);
  }

  // Match precisely: the browser client identifies as exactly "Jellyfin Web", and the desktop player's name
  // contains "Media Player". A loose "web" substring would wrongly allow native clients whose name merely
  // contains it — e.g. the LG TV client "Jellyfin webOS" — and hand them a pre-roll they can't play.
  private static bool IsWebOrDesktopClient(string client)
    => client.Equals("Jellyfin Web", StringComparison.OrdinalIgnoreCase)
       || client.Contains("Media Player", StringComparison.OrdinalIgnoreCase);

  private static bool AppliesTo(BaseItem item, PluginConfiguration config) => item switch
  {
    Movie => config.LocalIntrosOnMovies,

    // First episode of a series: season 1, episode 1.
    Episode episode => config.LocalIntrosOnFirstEpisode && episode.IndexNumber == 1 && episode.ParentIndexNumber == 1,

    _ => false,
  };
}
