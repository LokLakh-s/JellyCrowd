using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Default <see cref="IRequestCreationGate"/>: one lock per user, so different users never wait on each
/// other and a single user's concurrent requests are handled one at a time.
/// </summary>
public sealed class RequestCreationGate : IRequestCreationGate
{
  private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = new();

  /// <inheritdoc />
  public async Task<IDisposable> EnterAsync(Guid userId, CancellationToken cancellationToken)
  {
    var gate = _gates.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
    await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
    return new Releaser(gate);
  }

  private sealed class Releaser : IDisposable
  {
    private SemaphoreSlim? _gate;

    public Releaser(SemaphoreSlim gate) => _gate = gate;

    public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
  }
}
