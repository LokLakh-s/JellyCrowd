using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Runs a local process (abstracted so the script download backend can be unit-tested).
/// </summary>
public interface IProcessRunner
{
  /// <summary>
  /// Starts a process, writes <paramref name="standardInput"/> to its stdin, and waits for it to
  /// exit. Throws when the process fails (non-zero exit) or times out.
  /// </summary>
  /// <param name="fileName">The executable to run.</param>
  /// <param name="arguments">Optional command-line arguments.</param>
  /// <param name="standardInput">Text written to the process's standard input.</param>
  /// <param name="environment">Extra environment variables to set.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task that completes when the process exits successfully.</returns>
  Task RunAsync(string fileName, string? arguments, string standardInput, IReadOnlyDictionary<string, string?> environment, CancellationToken cancellationToken);
}
