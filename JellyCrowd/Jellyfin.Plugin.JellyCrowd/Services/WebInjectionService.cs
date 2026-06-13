using System;
using System.Linq;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Registers Jelly Crowd's web-client integration at startup: it asks the File Transformation
/// plugin to inject our shell script (<c>header.js</c>) into <c>index.html</c>. The shell then adds
/// the navigation entries and hosts the user pages itself, so Jelly Crowd no longer depends on the
/// Plugin Pages plugin. Uses reflection (no compile-time dependency) so the plugin builds and loads
/// cleanly whether or not File Transformation is installed.
/// </summary>
public sealed class WebInjectionService : IHostedService
{
  private readonly ILogger<WebInjectionService> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="WebInjectionService"/> class.
  /// </summary>
  /// <param name="logger">The logger.</param>
  public WebInjectionService(ILogger<WebInjectionService> logger)
  {
    _logger = logger;
  }

  /// <inheritdoc />
  public Task StartAsync(CancellationToken cancellationToken)
  {
    try
    {
      RegisterHeaderInjection();
    }
#pragma warning disable CA1031 // Web injection is optional and must never break server startup.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogInformation(ex, "Jelly Crowd web injection not registered (File Transformation unavailable?).");
    }

    return Task.CompletedTask;
  }

  /// <inheritdoc />
  public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

  private void RegisterHeaderInjection()
  {
    // Plugins load into their own AssemblyLoadContext, so traverse every context (not just the
    // default AppDomain assemblies) to find File Transformation when it is installed.
    var assembly = AssemblyLoadContext.All
      .SelectMany(context => context.Assemblies)
      .FirstOrDefault(a => a.FullName?.Contains(".FileTransformation", StringComparison.Ordinal) == true);
    if (assembly is null)
    {
      _logger.LogInformation(
        "File Transformation is not installed; Jelly Crowd's pages and navigation are unavailable. "
        + "Install the 'File Transformation' plugin to enable them.");
      return;
    }

    var pluginInterface = assembly.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface");
    var register = pluginInterface?.GetMethod("RegisterTransformation");
    if (register is null)
    {
      return;
    }

    var payload = new JObject
    {
      ["id"] = "a1994160-4ea2-4d81-bd3c-ffe825700d99",
      ["fileNamePattern"] = "index.html",
      ["callbackAssembly"] = typeof(TransformationPatches).Assembly.FullName,
      ["callbackClass"] = typeof(TransformationPatches).FullName,
      ["callbackMethod"] = nameof(TransformationPatches.InjectHeader)
    };

    register.Invoke(null, new object?[] { payload });
    _logger.LogInformation("Registered Jelly Crowd web injection with File Transformation.");
  }
}
