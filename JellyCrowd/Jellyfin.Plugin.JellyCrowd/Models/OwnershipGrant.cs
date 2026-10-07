namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// The ownership a member holds after being given a media: a new one, or the one they already had, renewed.
/// </summary>
/// <param name="Record">The ownership record (an available request).</param>
/// <param name="Created">Whether it was just created.</param>
public sealed record OwnershipGrant(RequestRecord Record, bool Created);
