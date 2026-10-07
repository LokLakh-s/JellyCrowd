using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// What withdrawing a request from Sonarr changes — on cancellation (monitoring and queue) or deletion
/// (files too) — worked out from Sonarr's current state and what the title's other active requests still
/// cover, so that nothing another request wants is touched. Pure, so the hierarchy rules are unit-testable.
/// </summary>
public sealed class SonarrReleasePlan
{
  private SonarrReleasePlan(
    IReadOnlyList<int> seasonsToTurnOff,
    IReadOnlyList<int> episodesToRemonitor,
    IReadOnlyList<int> episodesToUnmonitor,
    IReadOnlyList<int> filesToDelete,
    IReadOnlySet<EpisodeKey> released,
    bool stopFollowingNewSeasons)
  {
    SeasonsToTurnOff = seasonsToTurnOff;
    EpisodesToRemonitor = episodesToRemonitor;
    EpisodesToUnmonitor = episodesToUnmonitor;
    FilesToDelete = filesToDelete;
    Released = released;
    StopFollowingNewSeasons = stopFollowingNewSeasons;
  }

  /// <summary>
  /// Gets the seasons whose monitored flag goes off: those in the withdrawn request's scope that no remaining
  /// season or series request holds. An episode's season goes off too: a season left monitored for nobody
  /// keeps fetching its new episodes, which then belong to no one.
  /// </summary>
  public IReadOnlyList<int> SeasonsToTurnOff { get; }

  /// <summary>
  /// Gets the episodes to monitor again right after <see cref="SeasonsToTurnOff"/> is applied: turning a
  /// season off makes Sonarr unmonitor all of its episodes, including those other requests still want.
  /// Only those, and only if they were monitored before — a pending request is not started early.
  /// </summary>
  public IReadOnlyList<int> EpisodesToRemonitor { get; }

  /// <summary>
  /// Gets the released episodes still monitored outside the seasons being turned off.
  /// </summary>
  public IReadOnlyList<int> EpisodesToUnmonitor { get; }

  /// <summary>
  /// Gets the files to delete (on deletion only): the files of released episodes, except a file that also
  /// holds an episode someone else still wants (one file can carry several episodes).
  /// </summary>
  public IReadOnlyList<int> FilesToDelete { get; }

  /// <summary>
  /// Gets the released episodes: in the withdrawn request's scope and wanted by no other request.
  /// </summary>
  public IReadOnlySet<EpisodeKey> Released { get; }

  /// <summary>
  /// Gets a value indicating whether Sonarr should stop monitoring the seasons it lists from now on: the
  /// withdrawn request was the title's last whole-series request.
  /// </summary>
  public bool StopFollowingNewSeasons { get; }

  /// <summary>
  /// Plans the withdrawal of a request's scope.
  /// </summary>
  /// <param name="season">The withdrawn request's season, or <c>null</c> for the whole series.</param>
  /// <param name="episode">The withdrawn request's episode, or <c>null</c> for the whole season/series.</param>
  /// <param name="keep">What the title's other active requests still cover.</param>
  /// <param name="seasonsOn">The seasons Sonarr currently monitors.</param>
  /// <param name="episodes">The series' episodes as Sonarr lists them.</param>
  /// <returns>The plan.</returns>
  public static SonarrReleasePlan Build(int? season, int? episode, IReadOnlyList<RequestScope> keep, IReadOnlyCollection<int> seasonsOn, IReadOnlyList<SonarrEpisode> episodes)
  {
    // A whole-series request only ever monitored the real seasons, never the specials (season 0).
    bool InScope(SonarrEpisode e) => season is int s
      ? e.Season == s && (episode is null || e.Number == episode)
      : e.Season > 0;
    bool Kept(SonarrEpisode e) => keep.Any(k => k.Season is null || (k.Season == e.Season && (k.Episode is null || k.Episode == e.Number)));
    bool SeasonHeld(int s) => keep.Any(k => k.Season is null || (k.Season == s && k.Episode is null));

    var released = episodes.Where(e => InScope(e) && !Kept(e)).ToList();
    var releasedKeys = released.Select(e => new EpisodeKey(e.Season, e.Number)).ToHashSet();

    var seasonsToTurnOff = seasonsOn.Where(s => s > 0 && (season is null || s == season) && !SeasonHeld(s)).OrderBy(s => s).ToList();
    var turnedOff = seasonsToTurnOff.ToHashSet();

    var remonitor = episodes
      .Where(e => turnedOff.Contains(e.Season) && e.Monitored && Kept(e))
      .Select(e => e.Id)
      .ToList();
    var unmonitor = released.Where(e => e.Monitored && !turnedOff.Contains(e.Season)).Select(e => e.Id).ToList();

    var files = episodes
      .Where(e => e.FileId > 0)
      .GroupBy(e => e.FileId)
      .Where(file => file.All(e => releasedKeys.Contains(new EpisodeKey(e.Season, e.Number))))
      .Select(file => file.Key)
      .OrderBy(id => id)
      .ToList();

    var stopFollowing = season is null && !keep.Any(k => k.Season is null);
    return new SonarrReleasePlan(seasonsToTurnOff, remonitor, unmonitor, files, releasedKeys, stopFollowing);
  }
}
