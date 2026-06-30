using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellyCrowd.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Default <see cref="IStatsService"/>: aggregates captured playback history (via <see cref="StatsAggregator"/>)
/// and folds in live library counts.
/// </summary>
public sealed class StatsService : IStatsService
{
  private const int TopN = 8;
  private const int RecentN = 20;

  private readonly IPlaybackHistoryStore _store;
  private readonly ILibraryManager _libraryManager;

  /// <summary>
  /// Initializes a new instance of the <see cref="StatsService"/> class.
  /// </summary>
  /// <param name="store">The playback-history store.</param>
  /// <param name="libraryManager">The Jellyfin library manager (for library counts).</param>
  public StatsService(IPlaybackHistoryStore store, ILibraryManager libraryManager)
  {
    _store = store;
    _libraryManager = libraryManager;
  }

  /// <inheritdoc />
  public async Task<StatsOverviewDto> GetOverviewAsync(int windowDays, CancellationToken cancellationToken)
  {
    var records = windowDays > 0
      ? await _store.GetSinceAsync(DateTime.UtcNow.AddDays(-windowDays), cancellationToken).ConfigureAwait(false)
      : await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);

    var dto = StatsAggregator.BuildOverview(records, TopN, RecentN);
    dto.WindowDays = windowDays;
    dto.LibraryMovies = CountOf(BaseItemKind.Movie);
    dto.LibraryShows = CountOf(BaseItemKind.Series);
    dto.LibraryEpisodes = CountOf(BaseItemKind.Episode);
    return dto;
  }

  private int CountOf(BaseItemKind kind)
    => _libraryManager.GetCount(new InternalItemsQuery { IncludeItemTypes = new[] { kind }, Recursive = true });
}
