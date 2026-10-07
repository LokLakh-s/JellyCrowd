using System;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// An episode's number within its season and the day it aired (or airs).
/// </summary>
/// <param name="Number">The episode number.</param>
/// <param name="Day">The UTC air day.</param>
public sealed record AiredEpisode(int Number, DateTime Day);
