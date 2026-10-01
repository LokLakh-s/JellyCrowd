namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// The Jellyfin playback settings a language preference stands for.
/// </summary>
/// <param name="SetAudio">Whether to write the two audio settings at all.</param>
/// <param name="AudioLanguage">The preferred audio language (three letters), or <c>null</c> for the file's default track.</param>
/// <param name="PlayDefaultAudioTrack">Whether to play the file's default audio track.</param>
/// <param name="SubtitleLanguage">The preferred subtitle language (three letters), or <c>null</c> to leave it.</param>
/// <param name="AlwaysSubtitles">Whether subtitles must always show (otherwise the mode is left alone).</param>
public sealed record PlaybackSettings(bool SetAudio, string? AudioLanguage, bool PlayDefaultAudioTrack, string? SubtitleLanguage, bool AlwaysSubtitles);
