using System;
using System.Threading.Tasks;

using HanumanInstitute.LibMpv;

namespace ImageCare.UI.Avalonia.Services.MediaPlayer;

internal sealed class MpvPlayerAdapter : IMediaPlayerAdapter
{
    private MpvContext? _context;
    private MpvContext? _subscribedContext;
    private string _source = string.Empty;
    private bool _isPlaying;
    private bool _hasMediaLoaded;
    private bool _attached;
    private bool _disposed;
    private int _sourceVersion;

    public event EventHandler? StateChanged;

    public event EventHandler? PositionChanged;

    public event Action<Exception>? Failed;

    public MpvContext? Context
    {
        get => _context;
        set
        {
            if (_disposed || ReferenceEquals(_context, value))
            {
                return;
            }

            UnsubscribeContextEvents();
            _context = value;
            _sourceVersion++;
            _hasMediaLoaded = false;
            _isPlaying = false;
            SubscribeContextEvents();
            StateChanged?.Invoke(this, EventArgs.Empty);
            PositionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string Source
    {
        get => _source;
        set
        {
            var normalizedSource = value ?? string.Empty;
            if (string.Equals(_source, normalizedSource, StringComparison.Ordinal))
            {
                return;
            }

            _source = normalizedSource;
            _sourceVersion++;
            _hasMediaLoaded = false;
            _isPlaying = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
            PositionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public TimeSpan Position
    {
        get
        {
            var seconds = TryGet(() => _context?.TimePos?.Get());
            return seconds.HasValue && seconds.Value >= 0
                ? TimeSpan.FromSeconds(seconds.Value)
                : TimeSpan.Zero;
        }
        set
        {
            if (_disposed || value < TimeSpan.Zero || _context == null)
            {
                return;
            }

            TryRun(() => _context.TimePos.Set(value.TotalSeconds));
        }
    }

    public TimeSpan Duration => Position + Remaining;

    public TimeSpan Remaining
    {
        get
        {
            var seconds = TryGet(() => _context?.TimeRemaining?.Get());
            return seconds.HasValue && seconds.Value >= 0
                ? TimeSpan.FromSeconds(seconds.Value)
                : TimeSpan.Zero;
        }
    }

    public double Percent
    {
        get => TryGet(() => _context?.PercentPos?.Get()) ?? 0;
        set
        {
            if (_disposed || value is < 0 or > 100 || _context == null)
            {
                return;
            }

            TryRun(() => _context.PercentPos.Set(value));
        }
    }

    public bool IsSeekable => TryGet(() => _context?.Seekable?.Get()) ?? false;

    public double Volume
    {
        get => TryGet(() => _context?.Volume?.Get()) ?? 0;
        set
        {
            if (_disposed || value is < 0 or > 100 || _context == null)
            {
                return;
            }

            TryRun(() => _context.Volume.Set(value));
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
        SubscribeContextEvents();
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
        _sourceVersion++;
        UnsubscribeContextEvents();
        _hasMediaLoaded = false;
        _isPlaying = false;
    }

    public async Task LoadAsync()
    {
        var context = _context;
        var source = _source;
        var sourceVersion = _sourceVersion;
        if (_disposed || context == null || string.IsNullOrEmpty(source))
        {
            return;
        }

        try
        {
            await context.LoadFile(source).InvokeAsync();
            if (_disposed || !ReferenceEquals(context, _context) || sourceVersion != _sourceVersion)
            {
                return;
            }

            _hasMediaLoaded = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            NotifyFailure(exception);
        }
    }

    public async Task PlayAsync()
    {
        if (_disposed || _context == null || string.IsNullOrEmpty(_source) || _hasMediaLoaded && _isPlaying)
        {
            return;
        }

        if (!_hasMediaLoaded)
        {
            await LoadAsync();
        }

        if (_disposed || _context == null || !_hasMediaLoaded)
        {
            return;
        }

        try
        {
            if (_context.Pause.Get() == true)
            {
                _context.Pause.Set(false);
            }

            _isPlaying = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            NotifyFailure(exception);
        }
    }

    public void TogglePause()
    {
        if (_disposed || _context == null || !_hasMediaLoaded)
        {
            return;
        }

        try
        {
            var isPaused = _context.Pause.Get();
            if (!isPaused.HasValue)
            {
                return;
            }

            _context.Pause.Set(!isPaused.Value);
            _isPlaying = isPaused.Value;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            NotifyFailure(exception);
        }
    }

    public void Stop()
    {
        if (_disposed || _context == null || !_hasMediaLoaded)
        {
            return;
        }

        try
        {
            _context.Stop().Invoke();
            _hasMediaLoaded = false;
            _isPlaying = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
            PositionChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            NotifyFailure(exception);
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
        _sourceVersion++;
        _context = null;
        _hasMediaLoaded = false;
        _isPlaying = false;
    }

    private void SubscribeContextEvents()
    {
        if (!_attached || _context == null || _subscribedContext != null)
        {
            return;
        }

        _subscribedContext = _context;
        try
        {
            _subscribedContext.TimePos.Changed += OnPositionChanged;
            _subscribedContext.TimeRemaining.Changed += OnPositionChanged;
            _subscribedContext.Seekable.Changed += OnSeekableChanged;
            _subscribedContext.PercentPos.Changed += OnPercentChanged;
            _subscribedContext.Volume.Changed += OnVolumeChanged;
            _subscribedContext.Pause.Changed += OnPauseChanged;
            _subscribedContext.FileLoaded += OnFileLoaded;
        }
        catch (Exception exception)
        {
            UnsubscribeContextEvents();
            NotifyFailure(exception);
        }
    }

    private void UnsubscribeContextEvents()
    {
        var context = _subscribedContext;
        _subscribedContext = null;
        if (context == null)
        {
            return;
        }

        try
        {
            context.TimePos.Changed -= OnPositionChanged;
            context.TimeRemaining.Changed -= OnPositionChanged;
            context.Seekable.Changed -= OnSeekableChanged;
            context.PercentPos.Changed -= OnPercentChanged;
            context.Volume.Changed -= OnVolumeChanged;
            context.Pause.Changed -= OnPauseChanged;
            context.FileLoaded -= OnFileLoaded;
        }
        catch (Exception exception)
        {
            Failed?.Invoke(exception);
        }
    }

    private void OnFileLoaded(object? sender, EventArgs args)
    {
        _hasMediaLoaded = true;
        _isPlaying = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPauseChanged(object? sender, MpvValueChangedEventArgs<bool, bool> args)
    {
        if (!args.NewValue.HasValue)
        {
            return;
        }

        _isPlaying = !args.NewValue.Value && _hasMediaLoaded;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnVolumeChanged(object? sender, MpvValueChangedEventArgs<double, double> args) =>
        StateChanged?.Invoke(this, EventArgs.Empty);

    private void OnPercentChanged(object? sender, MpvValueChangedEventArgs<double, double> args) =>
        PositionChanged?.Invoke(this, EventArgs.Empty);

    private void OnSeekableChanged(object? sender, MpvValueChangedEventArgs<bool, bool> args) =>
        PositionChanged?.Invoke(this, EventArgs.Empty);

    private void OnPositionChanged(object? sender, EventArgs args) =>
        PositionChanged?.Invoke(this, EventArgs.Empty);

    private T? TryGet<T>(Func<T?> getter) where T : struct
    {
        try
        {
            return getter();
        }
        catch (MpvException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void TryRun(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            NotifyFailure(exception);
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
