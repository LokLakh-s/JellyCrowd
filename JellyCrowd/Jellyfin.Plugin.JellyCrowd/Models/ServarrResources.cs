using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// The selectable resources fetched from a Radarr/Sonarr instance, used to populate the admin
/// download settings dropdowns.
/// </summary>
public sealed class ServarrResources
{
  /// <summary>
  /// Gets the available root folders.
  /// </summary>
  public IList<ServarrResource> RootFolders { get; } = new List<ServarrResource>();

  /// <summary>
  /// Gets the available quality profiles.
  /// </summary>
  public IList<ServarrResource> QualityProfiles { get; } = new List<ServarrResource>();

  /// <summary>
  /// Gets the available language profiles (Sonarr v3 only; empty otherwise).
  /// </summary>
  public IList<ServarrResource> LanguageProfiles { get; } = new List<ServarrResource>();
}
