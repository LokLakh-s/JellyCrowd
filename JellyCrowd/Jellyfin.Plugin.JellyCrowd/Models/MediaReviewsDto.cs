using System.Collections.ObjectModel;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// A title's internal reviews: the average rating, the number of ratings, and the (anonymised for
/// non-admins) individual reviews.
/// </summary>
public class MediaReviewsDto
{
  /// <summary>Gets or sets the internal average rating on a 1–10 scale (0 when there are no ratings).</summary>
  public double Average { get; set; }

  /// <summary>Gets or sets the number of ratings counted in the average.</summary>
  public int Count { get; set; }

  /// <summary>Gets the individual reviews, newest first.</summary>
  public Collection<ReviewView> Reviews { get; } = new();
}
