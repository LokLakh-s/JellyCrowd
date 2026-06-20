using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// On-disk envelope wrapping a store's items with a schema version, so the format can evolve and be
/// migrated between plugin versions.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
internal sealed class StoreEnvelope<T>
{
  /// <summary>Gets or sets the schema version the items were written with.</summary>
  public int SchemaVersion { get; set; }

  /// <summary>Gets or sets the stored items.</summary>
  public List<T> Items { get; set; } = new();
}
