using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// File-backed <see cref="IUserActivityStore"/>. Keeps every record in memory (the dataset is one small row
/// per user) so quota reads are lock-protected dictionary lookups; writes persist the whole file atomically.
/// </summary>
public sealed class JsonUserActivityStore : IUserActivityStore
{
  /// <summary>Days older than this are pruned on write (keeps the file bounded regardless of window size).</summary>
  private const int MaxDaysKept = 60;
  private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

  private readonly string _filePath;
  private readonly object _gate = new();
  private Dictionary<Guid, UserActivity>? _cache;

  /// <summary>
  /// Initializes a new instance of the <see cref="JsonUserActivityStore"/> class.
  /// </summary>
  /// <param name="filePath">The full path to the JSON file backing the store.</param>
  public JsonUserActivityStore(string filePath) => _filePath = filePath;

  /// <inheritdoc />
  public UserActivity Get(Guid userId)
  {
    lock (_gate)
    {
      return Cache().TryGetValue(userId, out var activity)
        ? activity
        : new UserActivity { UserId = userId, LastSeenUtc = DateTime.MinValue };
    }
  }

  /// <inheritdoc />
  public IReadOnlyList<UserActivity> GetAll()
  {
    lock (_gate)
    {
      return Cache().Values.ToList();
    }
  }

  /// <inheritdoc />
  public void RecordPlayback(Guid userId, DateTime nowUtc, double minutes)
  {
    if (minutes <= 0)
    {
      return;
    }

    lock (_gate)
    {
      var cache = Cache();
      if (!cache.TryGetValue(userId, out var activity))
      {
        activity = new UserActivity { UserId = userId };
        cache[userId] = activity;
      }

      var key = nowUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
      var day = activity.Days.FirstOrDefault(d => string.Equals(d.Date, key, StringComparison.Ordinal));
      if (day is null)
      {
        day = new DailyWatch { Date = key };
        activity.Days.Add(day);
      }

      day.Minutes += minutes;
      activity.LastSeenUtc = nowUtc;
      Prune(activity, nowUtc);
      Persist(cache);
    }
  }

  /// <inheritdoc />
  public void Update(UserActivity activity)
  {
    ArgumentNullException.ThrowIfNull(activity);
    lock (_gate)
    {
      var cache = Cache();
      cache[activity.UserId] = activity;
      Persist(cache);
    }
  }

  private static void Prune(UserActivity activity, DateTime nowUtc)
  {
    var cutoff = nowUtc.Date.AddDays(-MaxDaysKept);
    var kept = activity.Days
      .Where(d => DateTime.TryParseExact(d.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2) && d2 >= cutoff)
      .ToList();
    if (kept.Count == activity.Days.Count)
    {
      return;
    }

    activity.Days.Clear();
    foreach (var d in kept)
    {
      activity.Days.Add(d);
    }
  }

  private Dictionary<Guid, UserActivity> Cache()
  {
    if (_cache is not null)
    {
      return _cache;
    }

    var list = new List<UserActivity>();
    if (File.Exists(_filePath))
    {
      try
      {
        var json = File.ReadAllText(_filePath);
        if (!string.IsNullOrWhiteSpace(json))
        {
          list = JsonSerializer.Deserialize<List<UserActivity>>(json, SerializerOptions) ?? new List<UserActivity>();
        }
      }
#pragma warning disable CA1031 // A corrupt activity file must not crash playback handling; start fresh.
      catch (Exception)
#pragma warning restore CA1031
      {
        list = new List<UserActivity>();
      }
    }

    _cache = list
      .Where(a => a is not null)
      .GroupBy(a => a.UserId)
      .ToDictionary(g => g.Key, g => g.Last());
    return _cache;
  }

  private void Persist(Dictionary<Guid, UserActivity> cache)
  {
    var directory = Path.GetDirectoryName(_filePath);
    if (!string.IsNullOrEmpty(directory))
    {
      Directory.CreateDirectory(directory);
    }

    var temp = _filePath + ".tmp";
    File.WriteAllText(temp, JsonSerializer.Serialize(cache.Values.ToList(), SerializerOptions));
    File.Move(temp, _filePath, overwrite: true);
  }
}
