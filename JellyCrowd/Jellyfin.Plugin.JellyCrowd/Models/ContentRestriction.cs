namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// The age restriction that applies to one user's catalog and requests: the stricter of their Jellyfin
/// parental control (maximum rating, blocked unrated items) and their Jelly Crowd child group. Scores are
/// Jellyfin parental rating scores, which are minimum ages (PG = 10, PG-13 = 13, R = 17).
/// </summary>
public sealed class ContentRestriction
{
  /// <summary>
  /// Gets the restriction of a user with no limit at all.
  /// </summary>
  public static ContentRestriction None { get; } = new();

  /// <summary>
  /// Gets the highest rating score allowed, or <c>null</c> for no limit.
  /// </summary>
  public int? MaxScore { get; init; }

  /// <summary>
  /// Gets the highest rating sub-score allowed when a title's score equals <see cref="MaxScore"/>, or
  /// <c>null</c> to allow any sub-score at that score.
  /// </summary>
  public int? MaxSubScore { get; init; }

  /// <summary>
  /// Gets a value indicating whether movies without a recognised rating are hidden and refused.
  /// </summary>
  public bool BlockUnratedMovies { get; init; }

  /// <summary>
  /// Gets a value indicating whether shows without a recognised rating are hidden and refused.
  /// </summary>
  public bool BlockUnratedShows { get; init; }

  /// <summary>
  /// Gets the age tier of the user's child group, or <c>null</c> when they are not a child account.
  /// </summary>
  public int? ChildMaxAge { get; init; }

  /// <summary>
  /// Gets a value indicating whether the user is a child account (member of a child group).
  /// </summary>
  public bool IsChild => ChildMaxAge.HasValue;

  /// <summary>
  /// Gets a value indicating whether anything is filtered for this user.
  /// </summary>
  public bool IsRestricted => MaxScore.HasValue || BlockUnratedMovies || BlockUnratedShows;
}
