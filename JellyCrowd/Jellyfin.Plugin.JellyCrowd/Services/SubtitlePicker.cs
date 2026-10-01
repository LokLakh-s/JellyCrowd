using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Picks the subtitle to fetch among a provider's results: never a machine- or AI-translated one, nor a
/// forced one (it only covers foreign-language lines); then an exact match of the file first, regular
/// subtitles before hearing-impaired ones, and the most downloaded, best rated.
/// </summary>
public static class SubtitlePicker
{
  /// <summary>
  /// Picks the best acceptable result.
  /// </summary>
  /// <param name="results">The provider's results.</param>
  /// <returns>The result to download, or <c>null</c> when none is acceptable.</returns>
  public static RemoteSubtitleInfo? Pick(IEnumerable<RemoteSubtitleInfo>? results)
  {
    return (results ?? Enumerable.Empty<RemoteSubtitleInfo>())
      .Where(r => r is not null && !string.IsNullOrEmpty(r.Id))
      .Where(r => r.AiTranslated != true && r.MachineTranslated != true && r.Forced != true)
      .OrderByDescending(r => r.IsHashMatch == true)
      .ThenBy(r => r.HearingImpaired == true)
      .ThenByDescending(r => r.DownloadCount ?? 0)
      .ThenByDescending(r => r.CommunityRating ?? 0)
      .FirstOrDefault();
  }
}
