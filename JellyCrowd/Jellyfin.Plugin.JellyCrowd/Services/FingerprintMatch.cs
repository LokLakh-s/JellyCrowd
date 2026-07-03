namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// A contiguous region of audio shared between two fingerprinted episodes (the intro), expressed as
/// inclusive frame indices into each episode's Chromaprint sub-fingerprint array.
/// </summary>
/// <param name="Length">Length of the shared run, in fingerprint frames.</param>
/// <param name="StartA">First matching frame in episode A.</param>
/// <param name="EndA">Last matching frame in episode A.</param>
/// <param name="StartB">First matching frame in episode B.</param>
/// <param name="EndB">Last matching frame in episode B.</param>
public readonly record struct FingerprintMatch(int Length, int StartA, int EndA, int StartB, int EndB);
