using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellyCrowd.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using JfEpisode = MediaBrowser.Controller.Entities.TV.Episode;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Default <see cref="IStatsService"/>: aggregates captured playback history (via <see cref="StatsAggregator"/>)
/// and folds in live library counts, a per-user dashboard (viewing + requests + quota), and live sessions.
/// </summary>
public sealed class StatsService : IStatsService
{
  private const int TopN = 8;
  private const int RecentN = 20;
  private const int UserTopN = 5;
  private const int UserRecentN = 12;
  private const int ChartMaxDays = 90;

  private readonly IPlaybackHistoryStore _store;
  private readonly ILibraryManager _libraryManager;
  private readonly IRequestStore _requestStore;
  private readonly IQuotaService _quotaService;
  private readonly ISessionManager _sessionManager;

  /// <summary>
  /// Initializes a new instance of the <see cref="StatsService"/> class.
  /// </summary>
  /// <param name="store">The playback-history store.</param>
  /// <param name="libraryManager">The Jellyfin library manager (for library counts).</param>
  /// <param name="requestStore">The request store (for a user's request activity).</param>
  /// <param name="quotaService">The quota service (for a user's disk usage).</param>
  /// <param name="sessionManager">The Jellyfin session manager (for live "now playing").</param>
  public StatsService(IPlaybackHistoryStore store, ILibraryManager libraryManager, IRequestStore requestStore, IQuotaService quotaService, ISessionManager sessionManager)
  {
    _store = store;
    _libraryManager = libraryManager;
    _requestStore = requestStore;
    _quotaService = quotaService;
    _sessionManager = sessionManager;
  }

  /// <inheritdoc />
  public async Task<StatsOverviewDto> GetOverviewAsync(int windowDays, CancellationToken cancellationToken)
  {
    var records = await GetRecordsAsync(windowDays, cancellationToken).ConfigureAwait(false);

    var dto = StatsAggregator.BuildOverview(records, TopN, RecentN);
    dto.WindowDays = windowDays;
    dto.LibraryMovies = CountOf(BaseItemKind.Movie);
    dto.LibraryShows = CountOf(BaseItemKind.Series);
    dto.LibraryEpisodes = CountOf(BaseItemKind.Episode);
    var chartDays = windowDays > 0 ? Math.Min(windowDays, ChartMaxDays) : ChartMaxDays;
    dto.Daily = StatsAggregator.BuildDailySeries(records, DateTime.UtcNow, chartDays);

    // Earliest play on record = how far back the statistics go (cached store, so this is in-memory).
    var allRecords = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
    dto.DataSinceUtc = allRecords.Count > 0 ? allRecords.Min(r => r.PlayedAtUtc) : (DateTime?)null;
    return dto;
  }

  /// <inheritdoc />
  public IReadOnlyList<StatsSessionDto> GetSessions()
  {
    var list = new List<StatsSessionDto>();
    foreach (var session in _sessionManager.Sessions)
    {
      var item = session.FullNowPlayingItem;
      if (item is null)
      {
        continue;
      }

      var runtime = item.RunTimeTicks ?? 0;
      var position = session.PlayState?.PositionTicks ?? 0;
      var pct = runtime > 0 ? (int)Math.Clamp(position * 100.0 / runtime, 0, 100) : 0;

      list.Add(new StatsSessionDto
      {
        UserName = session.UserName ?? string.Empty,
        Label = SessionLabel(item),
        Type = ItemTypeOf(item),
        Client = session.Client ?? string.Empty,
        PositionPercent = pct,
        Paused = session.PlayState?.IsPaused ?? false
      });
    }

    return list;
  }

  private static string SessionLabel(BaseItem item)
  {
    if (item is JfEpisode ep && !string.IsNullOrEmpty(ep.SeriesName))
    {
      var code = ep.ParentIndexNumber.HasValue && ep.IndexNumber.HasValue
        ? " · S" + ep.ParentIndexNumber.Value + "E" + ep.IndexNumber.Value
        : string.Empty;
      return ep.SeriesName + code;
    }

    return item.Name ?? string.Empty;
  }

  private static string ItemTypeOf(BaseItem item)
  {
    if (item is JfEpisode)
    {
      return "Episode";
    }

    return item is Movie ? "Movie" : item.GetType().Name;
  }

  /// <inheritdoc />
  public async Task<UserDashboardDto> GetUserDashboardAsync(Guid userId, int windowDays, CancellationToken cancellationToken)
  {
    var windowRecords = await GetRecordsAsync(windowDays, cancellationToken).ConfigureAwait(false);
    var records = windowRecords.Where(r => r.UserId == userId).ToList();
    var view = StatsAggregator.BuildOverview(records, UserTopN, UserRecentN);

    // Rank the user among all viewers active in the window, by watch time (1 = most). FindIndex returns
    // -1 when the user has no plays in the window -> reported as rank 0.
    var byMinutes = windowRecords
      .GroupBy(r => r.UserId)
      .Select(g => new { UserId = g.Key, Minutes = g.Sum(r => r.Minutes) })
      .OrderByDescending(u => u.Minutes)
      .ToList();
    var rankByMinutes = byMinutes.FindIndex(u => u.UserId == userId) + 1;

    // Earliest play on record (any user) = how far back the statistics go. The store caches, so this
    // extra read is in-memory; the value is naturally bounded by the history retention window.
    var allRecords = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
    DateTime? dataSince = allRecords.Count > 0 ? allRecords.Min(r => r.PlayedAtUtc) : (DateTime?)null;

    var chartDays = windowDays > 0 ? Math.Min(windowDays, ChartMaxDays) : ChartMaxDays;
    var daily = StatsAggregator.BuildDailySeries(records, DateTime.UtcNow, chartDays);

    var byLibrary = records
      .GroupBy(r => r.LibraryName ?? string.Empty)
      .Select(g => new StatsItemDto { Name = g.Key, Plays = g.Count(), Minutes = Math.Round(g.Sum(r => r.Minutes), 1) })
      .OrderByDescending(x => x.Minutes)
      .Take(8)
      .ToList();

    var requests = await _requestStore.GetByUserAsync(userId, cancellationToken).ConfigureAwait(false);
    var quota = await _quotaService.GetUsageAsync(userId, cancellationToken).ConfigureAwait(false);

    return new UserDashboardDto
    {
      WindowDays = windowDays,
      TotalPlays = view.TotalPlays,
      TotalMinutes = view.TotalMinutes,
      TopMovies = view.TopMovies,
      TopShows = view.TopShows,
      Recent = view.Recent,
      RequestsTotal = requests.Count,
      RequestsPending = requests.Count(r => r.Status == RequestStatus.Pending),
      RequestsApproved = requests.Count(r => r.Status == RequestStatus.Approved),
      RequestsAvailable = requests.Count(r => r.Status == RequestStatus.Available),
      RequestsDenied = requests.Count(r => r.Status == RequestStatus.Denied),
      QuotaUsedBytes = quota.UsedBytes,
      QuotaTotalBytes = quota.QuotaBytes,
      QuotaUnlimited = quota.Unlimited,
      DataSinceUtc = dataSince,
      RankByMinutes = rankByMinutes,
      RankedUsers = byMinutes.Count,
      Daily = daily,
      ByLibrary = byLibrary
    };
  }

  private async Task<IReadOnlyList<PlaybackRecord>> GetRecordsAsync(int windowDays, CancellationToken cancellationToken)
    => windowDays > 0
      ? await _store.GetSinceAsync(DateTime.UtcNow.AddDays(-windowDays), cancellationToken).ConfigureAwait(false)
      : await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);

  private int CountOf(BaseItemKind kind)
    => _libraryManager.GetCount(new InternalItemsQuery { IncludeItemTypes = new[] { kind }, Recursive = true });
}
