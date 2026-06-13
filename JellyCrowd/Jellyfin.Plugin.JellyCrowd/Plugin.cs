using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.JellyCrowd;

/// <summary>
/// The Jelly Crowd plugin: discovery catalog, media requests and per-user disk quotas inside Jellyfin.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
  /// <summary>
  /// Initializes a new instance of the <see cref="Plugin"/> class.
  /// </summary>
  /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
  /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
  public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
    : base(applicationPaths, xmlSerializer)
  {
    Instance = this;
  }

  /// <inheritdoc />
  public override string Name => "Jelly Crowd";

  /// <inheritdoc />
  public override Guid Id => Guid.Parse("a1994160-4ea2-4d81-bd3c-ffe825700d98");

  /// <summary>
  /// Gets the current plugin instance.
  /// </summary>
  public static Plugin? Instance { get; private set; }

  /// <inheritdoc />
  public IEnumerable<PluginPageInfo> GetPages()
  {
    return
    [
      new PluginPageInfo
      {
        Name = Name,
        DisplayName = "Jelly Crowd",
        EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace),
        EnableInMainMenu = true,
        MenuIcon = "movie"
      }
    ];
  }
}
