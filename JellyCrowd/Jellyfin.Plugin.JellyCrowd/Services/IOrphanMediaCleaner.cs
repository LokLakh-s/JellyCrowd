using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Deletes the media nobody owns any more, when the admin turned it on (<see cref="Configuration.PluginConfiguration.DeleteOrphanMedia"/>).
/// </summary>
public interface IOrphanMediaCleaner
{
  /// <summary>
  /// Deletes, from the library and from Radarr/Sonarr, the media of the chosen libraries that nobody has owned
  /// or requested for the retention period. Does nothing while the option is off.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>How many media were deleted.</returns>
  Task<int> CleanAsync(CancellationToken cancellationToken);
}
