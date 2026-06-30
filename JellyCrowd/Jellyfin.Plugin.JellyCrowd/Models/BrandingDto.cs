using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// The presentational branding settings exposed to the web client (anonymous) so it can theme the whole
/// Jellyfin UI at runtime: logo, favicon, default avatar, background, accent colour, font, layout presets,
/// free custom CSS and custom drawer entries. All fields are cosmetic; nothing sensitive is exposed.
/// </summary>
public class BrandingDto
{
  /// <summary>
  /// Gets or sets a value indicating whether branding is applied at all (master switch).
  /// </summary>
  public bool Enabled { get; set; }

  /// <summary>Gets or sets the navbar logo image URL (empty leaves the default).</summary>
  public string LogoUrl { get; set; } = string.Empty;

  /// <summary>Gets or sets the favicon image URL (empty leaves the default).</summary>
  public string FaviconUrl { get; set; } = string.Empty;

  /// <summary>Gets or sets the image shown for users who have no profile picture (empty leaves the default initials).</summary>
  public string DefaultAvatarUrl { get; set; } = string.Empty;

  /// <summary>Gets or sets the page background image URL (empty leaves the default).</summary>
  public string BackgroundUrl { get; set; } = string.Empty;

  /// <summary>Gets or sets the page background colour (CSS colour; empty leaves the default).</summary>
  public string BackgroundColor { get; set; } = string.Empty;

  /// <summary>Gets or sets the accent colour applied to primary buttons and active states (CSS colour).</summary>
  public string AccentColor { get; set; } = string.Empty;

  /// <summary>Gets or sets the CSS <c>font-family</c> applied to the UI (empty leaves the default).</summary>
  public string FontFamily { get; set; } = string.Empty;

  /// <summary>Gets or sets an optional stylesheet URL providing the font (e.g. a Google Fonts URL), imported before use.</summary>
  public string FontUrl { get; set; } = string.Empty;

  /// <summary>Gets or sets free-form custom CSS appended last (highest priority).</summary>
  public string CustomCss { get; set; } = string.Empty;

  /// <summary>Gets or sets a value indicating whether the compact episode-list layout preset is on.</summary>
  public bool PresetCompactEpisodes { get; set; }

  /// <summary>Gets or sets a value indicating whether the dark/transparent watched &amp; count indicators preset is on.</summary>
  public bool PresetDarkIndicators { get; set; }

  /// <summary>Gets or sets a value indicating whether the narrower Live TV channels preset is on.</summary>
  public bool PresetNarrowChannels { get; set; }

  /// <summary>Gets or sets a value indicating whether the hide-item-backdrop preset is on.</summary>
  public bool PresetHideBackdrop { get; set; }

  /// <summary>Gets or sets a value indicating whether the roomier raised-button preset is on.</summary>
  public bool PresetButtonTweaks { get; set; }

  /// <summary>Gets or sets the custom drawer entries to inject into the left navigation.</summary>
  public IReadOnlyList<DrawerLink> DrawerLinks { get; set; } = Array.Empty<DrawerLink>();
}
