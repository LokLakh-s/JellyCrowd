using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Default <see cref="IProcessRunner"/> backed by <see cref="Process"/>.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
  private const int TimeoutSeconds = 60;

  /// <inheritdoc />
  public async Task RunAsync(string fileName, string? arguments, string standardInput, IReadOnlyDictionary<string, string?> environment, CancellationToken cancellationToken)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
    ArgumentNullException.ThrowIfNull(environment);

    var startInfo = new ProcessStartInfo
    {
      FileName = fileName,
      RedirectStandardInput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };
    if (!string.IsNullOrWhiteSpace(arguments))
    {
      startInfo.Arguments = arguments;
    }

    foreach (var pair in environment)
    {
      startInfo.Environment[pair.Key] = pair.Value;
    }

    using var process = new Process { StartInfo = startInfo };
    process.Start();

    var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
    await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken).ConfigureAwait(false);
    process.StandardInput.Close();

    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
    try
    {
      await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
      TryKill(process);
      throw new InvalidOperationException($"The script did not finish within {TimeoutSeconds.ToString(CultureInfo.InvariantCulture)}s.");
    }

    if (process.ExitCode != 0)
    {
      var error = await stderrTask.ConfigureAwait(false);
      throw new InvalidOperationException($"The script exited with code {process.ExitCode.ToString(CultureInfo.InvariantCulture)}. {error}".Trim());
    }
  }

  /// <inheritdoc />
  public async Task<string> RunCaptureAsync(string fileName, string? arguments, int timeoutSeconds, CancellationToken cancellationToken)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

    var startInfo = new ProcessStartInfo
    {
      FileName = fileName,
      RedirectStandardError = true,
      RedirectStandardOutput = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };
    if (!string.IsNullOrWhiteSpace(arguments))
    {
      startInfo.Arguments = arguments;
    }

    using var process = new Process { StartInfo = startInfo };
    process.Start();

    // Drain both streams concurrently so a full pipe buffer can never deadlock the wait.
    var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
    var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);

    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
    try
    {
      await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
      TryKill(process);
      throw new InvalidOperationException($"The analysis process did not finish within {timeoutSeconds.ToString(CultureInfo.InvariantCulture)}s.");
    }

    var stderr = await stderrTask.ConfigureAwait(false);
    await stdoutTask.ConfigureAwait(false);
    return stderr;
  }

  /// <inheritdoc />
  public async Task<byte[]> RunCaptureBytesAsync(string fileName, string? arguments, int timeoutSeconds, CancellationToken cancellationToken)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

    var startInfo = new ProcessStartInfo
    {
      FileName = fileName,
      RedirectStandardError = true,
      RedirectStandardOutput = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };
    if (!string.IsNullOrWhiteSpace(arguments))
    {
      startInfo.Arguments = arguments;
    }

    using var process = new Process { StartInfo = startInfo };
    process.Start();

    // Copy stdout (the binary fingerprint) and drain stderr concurrently so neither pipe can deadlock.
    using var stdout = new System.IO.MemoryStream();
    var stdoutTask = process.StandardOutput.BaseStream.CopyToAsync(stdout, cancellationToken);
    var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
    try
    {
      await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
      TryKill(process);
      throw new InvalidOperationException($"The analysis process did not finish within {timeoutSeconds.ToString(CultureInfo.InvariantCulture)}s.");
    }

    await stdoutTask.ConfigureAwait(false);
    await stderrTask.ConfigureAwait(false);
    return stdout.ToArray();
  }

  private static void TryKill(Process process)
  {
    try
    {
      process.Kill(entireProcessTree: true);
    }
#pragma warning disable CA1031 // Best-effort cleanup of a timed-out process.
    catch (Exception)
#pragma warning restore CA1031
    {
      // Ignore: the process may have already exited.
    }
  }
}
