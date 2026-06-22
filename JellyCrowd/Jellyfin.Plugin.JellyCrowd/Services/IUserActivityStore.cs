using System;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Stores the bounded per-user viewing-activity aggregates that drive the adaptive quota. Reads are
/// synchronous because they sit on the hot quota-resolution path.
/// </summary>
public interface IUserActivityStore
{
  /// <summary>
  /// Gets a user's activity, or a fresh default record (base tier, never seen) when none is stored.
  /// </summary>
  /// <param name="userId">The user identifier.</param>
  /// <returns>The user's activity.</returns>
  UserActivity Get(Guid userId);

  /// <summary>
  /// Gets every stored activity record.
  /// </summary>
  /// <returns>All activity records.</returns>
  IReadOnlyList<UserActivity> GetAll();

  /// <summary>
  /// Adds watch minutes to a user's current UTC day, updates their last-seen time, prunes old days and persists.
  /// </summary>
  /// <param name="userId">The user identifier.</param>
  /// <param name="nowUtc">The current UTC time.</param>
  /// <param name="minutes">The minutes to add (ignored when not positive).</param>
  void RecordPlayback(Guid userId, DateTime nowUtc, double minutes);

  /// <summary>
  /// Upserts a full activity record (used by the background evaluator to persist tier/probation changes).
  /// </summary>
  /// <param name="activity">The activity to persist.</param>
  void Update(UserActivity activity);
}
