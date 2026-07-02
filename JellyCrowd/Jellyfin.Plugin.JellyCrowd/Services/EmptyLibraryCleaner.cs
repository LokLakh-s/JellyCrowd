using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Default <see cref="IEmptyLibraryCleaner"/> backed by <see cref="ILibraryManager"/>.
/// </summary>
public sealed class EmptyLibraryCleaner : IEmptyLibraryCleaner
{
  private readonly ILibraryManager _libraryManager;
  private readonly ILogger<EmptyLibraryCleaner> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="EmptyLibraryCleaner"/> class.
  /// </summary>
  /// <param name="libraryManager">The Jellyfin library manager.</param>
  /// <param name="logger">The logger.</param>
  public EmptyLibraryCleaner(ILibraryManager libraryManager, ILogger<EmptyLibraryCleaner> logger)
  {
    _libraryManager = libraryManager;
    _logger = logger;
  }

  /// <inheritdoc />
  public int RemoveEmptySeries(int minAgeHours, IReadOnlySet<int> wantedTmdbIds)
  {
    ArgumentNullException.ThrowIfNull(wantedTmdbIds);

    var minAge = TimeSpan.FromHours(minAgeHours < 0 ? 0 : minAgeHours);
    var now = DateTime.UtcNow;

    var series = _libraryManager.GetItemList(new InternalItemsQuery
    {
      IncludeItemTypes = new[] { BaseItemKind.Series },
      Recursive = true
    });

    var removed = 0;
    foreach (var candidate in series)
    {
      // Only need to know empty vs non-empty, so cap the query at one episode.
      var episodeCount = _libraryManager.GetItemList(new InternalItemsQuery
      {
        IncludeItemTypes = new[] { BaseItemKind.Episode },
        AncestorIds = new[] { candidate.Id },
        Recursive = true,
        Limit = 1
      }).Count;

      if (!EmptySeriesPolicy.ShouldRemove(episodeCount, candidate.DateCreated, now, minAge, ParseTmdb(candidate), wantedTmdbIds))
      {
        continue;
      }

      try
      {
        _libraryManager.DeleteItem(candidate, new DeleteOptions { DeleteFileLocation = true, DeleteFromExternalProvider = false });
        removed++;
        _logger.LogInformation("Jelly Crowd removed empty series {Name} ({Id}).", candidate.Name, candidate.Id);
      }
#pragma warning disable CA1031 // A single failed deletion must not abort the whole sweep.
      catch (Exception ex)
#pragma warning restore CA1031
      {
        _logger.LogWarning(ex, "Jelly Crowd could not remove empty series {Id}.", candidate.Id);
      }
    }

    return removed;
  }

  private static int? ParseTmdb(BaseItem series)
  {
    var tmdb = series.GetProviderId(MetadataProvider.Tmdb);
    return int.TryParse(tmdb, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;
  }
}
