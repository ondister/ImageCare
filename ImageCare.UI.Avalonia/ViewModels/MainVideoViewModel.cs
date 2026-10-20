using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Windows.Input;

using HanumanInstitute.LibMpv;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.FolderService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.UI.Avalonia.Platforms.MacOS;

using Prism.Commands;
using Prism.Navigation.Regions;

using Serilog;

using MpvContext = HanumanInstitute.LibMpv.MpvContext;

namespace ImageCare.UI.Avalonia.ViewModels;

internal class MainVideoViewModel : NavigatedViewModelBase
{
    private readonly IMediaPreviewOperationsService _fileOperationsService;
    private readonly IFolderService _folderService;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;

    private string _mediaUrl = string.Empty;
    private CompositeDisposable? _compositeDisposable;
    private MpvContext? _mpv;
    private MacVideoPlayerView? _macPlayer;
    private bool _isPlaying;
    private bool _hasMediaLoaded;
    private bool _mpvEventsSubscribed;

    public MainVideoViewModel(IMediaPreviewOperationsService fileOperationsService,
                              IFolderService folderService,
                              ILogger logger,
                              SynchronizationContext synchronizationContext)
    {
        _fileOperationsService = fileOperationsService;
        _folderService = folderService;
        _logger = logger;
        _synchronizationContext = synchronizationContext;

        PlayCommand = new DelegateCommand(Play, CanPlay);
        PauseCommand = new DelegateCommand(Pause, CanPause);
        StopCommand = new DelegateCommand(Stop, CanStop);
    }

    public ICommand PauseCommand { get; }

    public ICommand PlayCommand { get; }

    public ICommand StopCommand { get; }

    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (SetProperty(ref _isPlaying, value))
            {
                UpdateCommandsState();
            }
        }
    }

    public bool HasMediaLoaded
    {
        get => _hasMediaLoaded;
        private set
        {
            if (SetProperty(ref _hasMediaLoaded, value))
            {
                UpdateCommandsState();
            }
        }
    }

    public MpvContext? Mpv
    {
        get => _mpv;
        set
        {
            if (ReferenceEquals(_mpv, value))
            {
                return;
            }

            UnsubscribeMpvEvents();

            if (SetProperty(ref _mpv, value))
            {
                SubscribeMpvEvents();
            }
        }
    }

    public MacVideoPlayerView? MacPlayer
    {
        get => _macPlayer;
        set
        {
            if (ReferenceEquals(_macPlayer, value))
            {
                return;
            }

            UnsubscribeMacPlayerEvents();
            _macPlayer = value;
            SubscribeMacPlayerEvents();

            if (_macPlayer != null)
            {
                _macPlayer.Source = MediaUrl;
                SyncMacPlayerState();
            }
        }
    }

    public TimeSpan TimePosition
    {
        get
        {
            if (OperatingSystem.IsMacOS())
            {
                return MacPlayer?.Position ?? TimeSpan.Zero;
            }

            if (Mpv == null)
            {
                return TimeSpan.Zero;
            }

            try
            {
                var seconds = Mpv.TimePos?.Get();
                return seconds.HasValue && seconds.Value >= 0 ? TimeSpan.FromSeconds(seconds.Value) : TimeSpan.Zero;
            }
            catch (MpvException)
            {
                return TimeSpan.Zero;
            }
        }
        set
        {
            try
            {
                if (value < TimeSpan.Zero)
                {
                    return;
                }

                if (OperatingSystem.IsMacOS())
                {
                    MacPlayer?.Seek(value);
                }
                else if (Mpv != null)
                {
                    Mpv.TimePos.Set(value.TotalSeconds);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error setting time position to {TimePosition}", value);
            }
        }
    }

    public double Volume
    {
        get
        {
            try
            {
                return OperatingSystem.IsMacOS()
                    ? MacPlayer?.Volume ?? 100
                    : Mpv?.Volume.Get() ?? 0;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error reading player volume");
                return 0;
            }
        }
        set
        {
            try
            {
                if (value is < 0 or > 100)
                {
                    return;
                }

                if (OperatingSystem.IsMacOS())
                {
                    if (MacPlayer != null)
                    {
                        MacPlayer.Volume = value;
                    }
                }
                else if (Mpv != null)
                {
                    Mpv.Volume.Set(value);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error setting volume to {Volume}", value);
            }
        }
    }

    public TimeSpan TimeRemaining
    {
        get
        {
            if (OperatingSystem.IsMacOS())
            {
                var remaining = (MacPlayer?.Duration ?? TimeSpan.Zero) - TimePosition;
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }

            try
            {
                if (Mpv?.TimeRemaining?.Get() is double seconds && seconds >= 0)
                {
                    return TimeSpan.FromSeconds(seconds);
                }
            }
            catch (MpvException)
            {
                // The property is not available until MPV has loaded media.
            }

            return TimeSpan.Zero;
        }
    }

    public double? PercentPos
    {
        get
        {
            if (OperatingSystem.IsMacOS())
            {
                var duration = MacPlayer?.Duration ?? TimeSpan.Zero;
                return duration > TimeSpan.Zero
                    ? Math.Clamp(TimePosition.TotalMilliseconds / duration.TotalMilliseconds * 100, 0, 100)
                    : 0;
            }

            return GetMpvProperty(() => Mpv?.PercentPos?.Get(), 0.0);
        }
        set
        {
            try
            {
                if (value is not >= 0 or not <= 100)
                {
                    return;
                }

                if (OperatingSystem.IsMacOS())
                {
                    var duration = MacPlayer?.Duration ?? TimeSpan.Zero;
                    if (duration > TimeSpan.Zero)
                    {
                        MacPlayer?.Seek(TimeSpan.FromMilliseconds(duration.TotalMilliseconds * value.Value / 100));
                    }
                }
                else if (Mpv != null)
                {
                    Mpv.PercentPos.Set(value.Value);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error setting percent position to {PercentPos}", value);
            }
        }
    }

    public bool IsSeekable
    {
        get
        {
            if (OperatingSystem.IsMacOS())
            {
                return MacPlayer?.IsSeekable ?? false;
            }

            try
            {
                return Mpv?.Seekable?.Get() ?? false;
            }
            catch (MpvException)
            {
                return false;
            }
        }
    }

    public string MediaUrl
    {
        get => _mediaUrl;
        set
        {
            if (!SetProperty(ref _mediaUrl, value ?? string.Empty))
            {
                return;
            }

            HasMediaLoaded = false;
            IsPlaying = false;

            if (OperatingSystem.IsMacOS() && MacPlayer != null)
            {
                MacPlayer.Source = _mediaUrl;
                SyncMacPlayerState();
            }

            UpdateCommandsState();
        }
    }

    internal void Show(SelectedMediaPreview preview)
    {
        SubscribeMpvEvents();
        OnPreviewSelected(preview);
    }

    internal void Deactivate()
    {
        Stop();
        UnsubscribeMpvEvents();
        MediaUrl = string.Empty;
    }

    public override void OnNavigatedTo(NavigationContext navigationContext)
    {
        try
        {
            _compositeDisposable = new CompositeDisposable
            {
                _folderService.FileSystemItemSelected
                              .Subscribe(OnFolderSelected, OnError),

                _fileOperationsService.ImagePreviewSelected
                                      .Throttle(TimeSpan.FromMilliseconds(150))
                                      .ObserveOn(_synchronizationContext)
                                      .Subscribe(OnPreviewSelected, OnError)
            };

            if (navigationContext.Parameters["imagePreview"] is SelectedMediaPreview imagePreview)
            {
                OnPreviewSelected(imagePreview);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during navigation to MainVideoViewModel");
        }
    }

    public override void OnNavigatedFrom(NavigationContext navigationContext)
    {
        try
        {
            Stop();
            UnsubscribeMpvEvents();

            _compositeDisposable?.Dispose();
            _compositeDisposable = null;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during navigation from MainVideoViewModel");
        }
    }

    public async void Play()
    {
        try
        {
            if (string.IsNullOrEmpty(MediaUrl))
            {
                return;
            }

            if (OperatingSystem.IsMacOS())
            {
                if (MacPlayer == null)
                {
                    return;
                }

                MacPlayer.Source = MediaUrl;
                MacPlayer.Play();
                SyncMacPlayerState();
                return;
            }

            if (Mpv == null || HasMediaLoaded && IsPlaying)
            {
                return;
            }

            if (!HasMediaLoaded)
            {
                await Mpv.LoadFile(MediaUrl).InvokeAsync();
                HasMediaLoaded = true;
            }

            if (Mpv.Pause.Get() == true)
            {
                Mpv.Pause.Set(false);
            }

            IsPlaying = true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error playing media: {MediaUrl}", MediaUrl);
            IsPlaying = false;
            HasMediaLoaded = false;
        }
    }

    public void Pause()
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                if (MacPlayer == null || !MacPlayer.HasMediaLoaded)
                {
                    return;
                }

                if (MacPlayer.IsPlaying)
                {
                    MacPlayer.Pause();
                }
                else
                {
                    MacPlayer.Play();
                }

                SyncMacPlayerState();
                return;
            }

            if (Mpv == null || !HasMediaLoaded)
            {
                return;
            }

            var isPaused = Mpv.Pause.Get();
            if (isPaused.HasValue)
            {
                Mpv.Pause.Set(!isPaused.Value);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error pausing/resuming media");
            IsPlaying = false;
        }
    }

    public void Stop()
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                MacPlayer?.Stop();
                SyncMacPlayerState();
                return;
            }

            if (Mpv == null || !HasMediaLoaded)
            {
                return;
            }

            Mpv.Stop().Invoke();
            IsPlaying = false;
            HasMediaLoaded = false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error stopping media");
            IsPlaying = false;
            HasMediaLoaded = false;
        }
    }

    private bool CanPlay()
    {
        return !string.IsNullOrEmpty(MediaUrl) && !IsPlaying;
    }

    private bool CanPause()
    {
        return HasMediaLoaded;
    }

    private bool CanStop()
    {
        return HasMediaLoaded;
    }

    private void SubscribeMpvEvents()
    {
        if (Mpv == null || _mpvEventsSubscribed)
        {
            return;
        }

        try
        {
            Mpv.TimePos.Changed += OnMpvPositionChanged;
            Mpv.TimeRemaining.Changed += OnMpvPositionChanged;
            Mpv.Seekable.Changed += OnMpvSeekableChanged;
            Mpv.PercentPos.Changed += OnMpvPercentChanged;
            Mpv.Volume.Changed += OnMpvVolumeChanged;
            Mpv.Pause.Changed += OnMpvPauseStateChanged;
            Mpv.FileLoaded += OnMpvFileLoaded;
            _mpvEventsSubscribed = true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error subscribing to MPV events");
        }
    }

    private void UnsubscribeMpvEvents()
    {
        if (_mpv == null || !_mpvEventsSubscribed)
        {
            return;
        }

        try
        {
            _mpv.TimePos.Changed -= OnMpvPositionChanged;
            _mpv.TimeRemaining.Changed -= OnMpvPositionChanged;
            _mpv.Seekable.Changed -= OnMpvSeekableChanged;
            _mpv.PercentPos.Changed -= OnMpvPercentChanged;
            _mpv.Volume.Changed -= OnMpvVolumeChanged;
            _mpv.Pause.Changed -= OnMpvPauseStateChanged;
            _mpv.FileLoaded -= OnMpvFileLoaded;
            _mpvEventsSubscribed = false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error unsubscribing from MPV events");
        }
    }

    private void SubscribeMacPlayerEvents()
    {
        if (_macPlayer == null)
        {
            return;
        }

        _macPlayer.PlaybackStateChanged += OnMacPlaybackStateChanged;
        _macPlayer.PositionChanged += OnMacPositionChanged;
        _macPlayer.PlaybackFailed += OnMacPlaybackFailed;
    }

    private void UnsubscribeMacPlayerEvents()
    {
        if (_macPlayer == null)
        {
            return;
        }

        _macPlayer.PlaybackStateChanged -= OnMacPlaybackStateChanged;
        _macPlayer.PositionChanged -= OnMacPositionChanged;
        _macPlayer.PlaybackFailed -= OnMacPlaybackFailed;
    }

    private void OnMpvFileLoaded(object? sender, EventArgs e)
    {
        RunOnUiThread(() =>
        {
            HasMediaLoaded = true;
            IsPlaying = true;
        });
    }

    private void OnMpvPauseStateChanged(object? sender, MpvValueChangedEventArgs<bool, bool> e)
    {
        if (!e.NewValue.HasValue)
        {
            return;
        }

        RunOnUiThread(() => IsPlaying = !e.NewValue.Value && HasMediaLoaded);
    }

    private void OnMpvVolumeChanged(object? sender, MpvValueChangedEventArgs<double, double> e)
    {
        RunOnUiThread(() => RaisePropertyChanged(nameof(Volume)));
    }

    private void OnMpvPercentChanged(object? sender, MpvValueChangedEventArgs<double, double> e)
    {
        RunOnUiThread(() => RaisePropertyChanged(nameof(PercentPos)));
    }

    private void OnMpvSeekableChanged(object? sender, MpvValueChangedEventArgs<bool, bool> e)
    {
        RunOnUiThread(() => RaisePropertyChanged(nameof(IsSeekable)));
    }

    private void OnMpvPositionChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(RaisePositionPropertiesChanged);
    }

    private void OnMacPlaybackStateChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(SyncMacPlayerState);
    }

    private void OnMacPositionChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(RaisePositionPropertiesChanged);
    }

    private void OnMacPlaybackFailed(Exception exception)
    {
        _logger.Error(exception, "AVPlayer failed to play media: {MediaUrl}", MediaUrl);
        RunOnUiThread(() =>
        {
            IsPlaying = false;
            HasMediaLoaded = false;
        });
    }

    private void SyncMacPlayerState()
    {
        HasMediaLoaded = MacPlayer?.HasMediaLoaded ?? false;
        IsPlaying = MacPlayer?.IsPlaying ?? false;
        RaisePropertyChanged(nameof(Volume));
        RaisePropertyChanged(nameof(IsSeekable));
        RaisePositionPropertiesChanged();
    }

    private void RaisePositionPropertiesChanged()
    {
        RaisePropertyChanged(nameof(TimePosition));
        RaisePropertyChanged(nameof(TimeRemaining));
        RaisePropertyChanged(nameof(PercentPos));
        RaisePropertyChanged(nameof(IsSeekable));
    }

    private void OnFolderSelected(SelectedDirectory directory)
    {
        try
        {
            Stop();
            MediaUrl = string.Empty;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling folder selection");
        }
    }

    private void OnPreviewSelected(SelectedMediaPreview preview)
    {
        try
        {
            Stop();
            MediaUrl = preview?.Url ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling preview selection");
        }
    }

    private void UpdateCommandsState()
    {
        try
        {
            ((DelegateCommand)PlayCommand).RaiseCanExecuteChanged();
            ((DelegateCommand)PauseCommand).RaiseCanExecuteChanged();
            ((DelegateCommand)StopCommand).RaiseCanExecuteChanged();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error updating commands state");
        }
    }

    private void RunOnUiThread(Action action)
    {
        if (SynchronizationContext.Current == _synchronizationContext)
        {
            action();
            return;
        }

        _synchronizationContext.Post(_ => action(), null);
    }

    private void OnError(Exception exception)
    {
        _logger.Error(exception, "Error in observable sequence");
    }

    private T? GetMpvProperty<T>(Func<T?> propertyGetter, T? defaultValue = default)
    {
        try
        {
            return propertyGetter();
        }
        catch (MpvException)
        {
            return defaultValue;
        }
    }
}
