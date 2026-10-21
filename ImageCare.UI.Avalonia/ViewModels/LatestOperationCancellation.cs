using System;
using System.Threading;

namespace ImageCare.UI.Avalonia.ViewModels;

/// <summary>
/// Coordinates operations where a new invocation cancels the previous one.
/// Each operation retains ownership of its cancellation source until it finishes.
/// </summary>
internal sealed class LatestOperationCancellation
{
    private readonly object _sync = new();
    private CancellationTokenSource? _current;

    public Operation Begin()
    {
        var source = new CancellationTokenSource();
        var token = source.Token;

        try
        {
            lock (_sync)
            {
                _current?.Cancel();
                _current = source;
            }
        }
        catch
        {
            source.Dispose();
            throw;
        }

        return new Operation(this, source, token);
    }

    public void Cancel()
    {
        lock (_sync)
        {
            _current?.Cancel();
            _current = null;
        }
    }

    private void Complete(CancellationTokenSource source)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_current, source))
            {
                _current = null;
            }

            source.Dispose();
        }
    }

    internal sealed class Operation : IDisposable
    {
        private LatestOperationCancellation? _owner;
        private readonly CancellationTokenSource _source;

        public Operation(
            LatestOperationCancellation owner,
            CancellationTokenSource source,
            CancellationToken token)
        {
            _owner = owner;
            _source = source;
            Token = token;
        }

        public CancellationToken Token { get; }

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.Complete(_source);
        }
    }
}
