namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A recap of the reports left open long enough to deserve a reminder: how many are waiting, which one
/// has waited longest, and for how many days.
/// </summary>
/// <param name="OpenCount">How many reports have been waiting a full reminder window.</param>
/// <param name="Oldest">The one that has been waiting longest.</param>
/// <param name="OldestAgeDays">How many whole days <paramref name="Oldest"/> has been waiting.</param>
public sealed record ReportReminder(int OpenCount, MediaReport Oldest, int OldestAgeDays);
