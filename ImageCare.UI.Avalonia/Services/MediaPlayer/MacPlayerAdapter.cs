using System;
using System.Threading.Tasks;

using ImageCare.UI.Avalonia.Platforms.MacOS;

namespace ImageCare.UI.Avalonia.Services.MediaPlayer;

internal sealed class MacPlayerAdapter : IMediaPlayerAdapter
{
    private readonly MacVideoPlayerView _player;
    private bool _isPlaying;
    private bool _hasMediaLoaded;
    private bool _attached;
    private bool _disposed;

    public MacPlayerAdapter(MacVideoPlayerView player)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        Attach();
    }

    public event EventHandler? StateChanged;

    public event EventHandler? PositionChanged;

    public event Action<Exception>? Failed;

    public string Source
    {
        get => _player.Source;
        set
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                _player.Source = value ?? string.Empty;
                SyncStateFromPlayer();
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                NotifyFailure(exception);
            }
        }
    }

    public TimeSpan Position
    {
        get => TryGet(() => _player.Position, TimeSpan.Zero);
        set
        {
            if (_disposed || value < TimeSpan.Zero)
            {
                return;
            }

            TryRun(() => _player.Seek(value));
        }
    }

    public TimeSpan Duration => TryGet(() => _player.Duration, TimeSpan.Zero);

    public TimeSpan Remaining
    {
        get
        {
            var remaining = Duration - Position;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    public double Percent
    {
        get
        {
            var duration = Duration;
            return duration > TimeSpan.Zero
                ? Math.Clamp(Position.TotalMilliseconds / duration.TotalMilliseconds * 100, 0, 100)
                : 0;
        }
        set
        {
            if (_disposed || value is < 0 or > 100)
            {
                return;
            }

            var duration = Duration;
            if (duration > TimeSpan.Zero)
            {
                Position = TimeSpan.FromMilliseconds(duration.TotalMilliseconds * value / 100);
            }
        }
    }

    public bool IsSeekable => TryGet(() => _player.IsSeekable, false);

    public double Volume
    {
        get => TryGet(() => _player.Volume, 100d);
        set
        {
            if (_disposed || value is < 0 or > 100)
            {
                return;
            }

            TryRun(() => _player.Volume = value);
        }
    }

    public bool IsPlaying => !_disposed && _isPlaying;

    public bool HasMediaLoaded => !_disposed && _hasMediaLoaded;

    public void Attach()
    {
        if (_disposed || _attached)
        {
            return;
        }

        _attached = true;
        _player.PlaybackStateChanged += OnPlaybackStateChanged;
        _player.PositionChanged += OnPositionChanged;
        _player.PlaybackFailed += OnPlaybackFailed;
        SyncStateFromPlayer();
        StateChanged?.Invoke(this, EventArgs.Empty);
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Detach()
    {
        if (!_attached)
        {
            return;
        }

        _attached = false;
        _player.PlaybackStateChanged -= OnPlaybackStateChanged;
        _player.PositionChanged -= OnPositionChanged;
        _player.PlaybackFailed -= OnPlaybackFailed;
        _isPlaying = false;
        _hasMediaLoaded = false;
    }

    public Task LoadAsync()
    {
        if (!_disposed)
        {
            SyncStateFromPlayer();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        return Task.CompletedTask;
    }

    public Task PlayAsync()
    {
        TryRun(_player.Play);
        return Task.CompletedTask;
    }

    public void TogglePause()
    {
        if (_disposed || !_hasMediaLoaded)
        {
            return;
        }

        TryRun(_isPlaying ? _player.Pause : _player.Play);
    }

    public void Stop()
    {
        if (!_disposed)
        {
            TryRun(_player.Stop);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Detach();
        _disposed = true;
        _isPlaying = false;
        _hasMediaLoaded = false;
    }

    private void OnPlaybackStateChanged(object? sender, EventArgs args)
    {
        SyncStateFromPlayer();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPositionChanged(object? sender, EventArgs args) =>
        PositionChanged?.Invoke(this, EventArgs.Empty);

    private void OnPlaybackFailed(Exception exception) => NotifyFailure(exception);

    private void TryRun(Action action)
    {
        try
        {
            action();
            SyncStateFromPlayer();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            NotifyFailure(exception);
        }
    }

    private void SyncStateFromPlayer()
    {
        _hasMediaLoaded = _player.HasMediaLoaded;
        _isPlaying = _player.IsPlaying;
    }

    private static T TryGet<T>(Func<T> getter, T fallback)
    {
        try
        {
            return getter();
        }
        catch
        {
            return fallback;
        }
    }

    private void NotifyFailure(Exception exception)
    {
        _hasMediaLoaded = false;
        _isPlaying = false;
        Failed?.Invoke(exception);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
