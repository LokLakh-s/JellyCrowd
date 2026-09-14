namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// The outcome of <see cref="RequestCoverage.Evaluate"/> for a prospective request.
/// </summary>
/// <param name="AlreadyCovered">Whether the user already has everything the request asks for, on disk or on its way.</param>
/// <param name="EpisodesToReserve">How many episodes the request would actually have to download.</param>
public readonly record struct CoverageDecision(bool AlreadyCovered, int EpisodesToReserve);
