using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Windows.Input;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.FolderService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.UI.Avalonia.Services.MediaPlayer;

using Prism.Commands;
using Prism.Navigation.Regions;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels;

internal class MainVideoViewModel : NavigatedViewModelBase
{
    private readonly IObservable<SelectedMediaPreview> _previewSelections;
    private readonly IObservable<SelectedDirectory> _folderSelections;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;

    private string _mediaUrl = string.Empty;
    private CompositeDisposable? _compositeDisposable;
    private IMediaPlayerAdapter? _player;
    private bool _isPlaying;
    private bool _hasMediaLoaded;
    private bool _isPlayPending;

    public MainVideoViewModel(IMediaPreviewOperationsService fileOperationsService,
                              IFolderService folderService,
                              ILogger logger,
                              SynchronizationContext synchronizationContext)
        : this(fileOperationsService.ImagePreviewSelected,
               folderService.FileSystemItemSelected,
               logger,
               synchronizationContext)
    {
    }

    internal MainVideoViewModel(IObservable<SelectedMediaPreview> previewSelections,
                                IObservable<SelectedDirectory> folderSelections,
                                ILogger logger,
                                SynchronizationContext synchronizationContext)
    {
        _previewSelections = previewSelections;
        _folderSelections = folderSelections;
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

    internal IMediaPlayerAdapter? Player
    {
        get => _player;
        set
        {
            if (ReferenceEquals(_player, value))
            {
                return;
            }

            UnsubscribePlayerEvents();
            _player = value;
            SubscribePlayerEvents();

            if (_player != null)
            {
                _player.Source = MediaUrl;
            }

            SyncPlayerState();
        }
    }

    public TimeSpan TimePosition
    {
        get => Player?.Position ?? TimeSpan.Zero;
        set
        {
            try
            {
                if (value >= TimeSpan.Zero && Player != null)
                {
                    Player.Position = value;
                }
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Error setting time position to {TimePosition}", value);
            }
        }
    }

    public TimeSpan Duration => Player?.Duration ?? TimeSpan.Zero;

    public TimeSpan TimeRemaining => Player?.Remaining ?? TimeSpan.Zero;

    public double? PercentPos
    {
        get => Player?.Percent ?? 0;
        set
        {
            try
            {
                if (value is >= 0 and <= 100 && Player != null)
                {
                    Player.Percent = value.Value;
                }
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Error setting percent position to {PercentPos}", value);
            }
        }
    }

    public bool IsSeekable => Player?.IsSeekable ?? false;

    public double Volume
    {
        get => Player?.Volume ?? 0;
        set
        {
            try
            {
                if (value is >= 0 and <= 100 && Player != null)
                {
                    Player.Volume = value;
                }
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Error setting volume to {Volume}", value);
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

            IsPlaying = false;
            HasMediaLoaded = false;

            if (Player != null)
            {
                Player.Source = _mediaUrl;
                SyncPlayerState();
            }

            UpdateCommandsState();
        }
    }

    internal void Show(SelectedMediaPreview preview) => OnPreviewSelected(preview);

    internal void Deactivate()
    {
        Stop();
        MediaUrl = string.Empty;
    }

    public override void OnNavigatedTo(NavigationContext navigationContext)
    {
        try
        {
            _compositeDisposable?.Dispose();
            _compositeDisposable = new CompositeDisposable
            {
                _folderSelections
                    .ObserveOn(_synchronizationContext)
                    .Subscribe(OnFolderSelected, OnError),
                _previewSelections
                    .Throttle(TimeSpan.FromMilliseconds(150))
                    .ObserveOn(_synchronizationContext)
                    .Subscribe(OnPreviewSelected, OnError)
            };

            if (navigationContext.Parameters["imagePreview"] is SelectedMediaPreview imagePreview)
            {
                OnPreviewSelected(imagePreview);
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Error during navigation to MainVideoViewModel");
        }
    }

    public override void OnNavigatedFrom(NavigationContext navigationContext)
    {
        try
        {
            Stop();
            _compositeDisposable?.Dispose();
            _compositeDisposable = null;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Error during navigation from MainVideoViewModel");
        }
    }

    public async void Play()
    {
        if (!CanPlay() || Player == null)
        {
            return;
        }

        _isPlayPending = true;
        UpdateCommandsState();
        try
        {
            await Player.PlayAsync();
            SyncPlayerState();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Error playing media: {MediaUrl}", MediaUrl);
            IsPlaying = false;
            HasMediaLoaded = false;
        }
        finally
        {
            _isPlayPending = false;
            UpdateCommandsState();
        }
    }

    public void Pause()
    {
        try
        {
            Player?.TogglePause();
            SyncPlayerState();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Error pausing/resuming media");
            IsPlaying = false;
        }
    }

    public void Stop()
    {
        try
        {
            Player?.Stop();
            SyncPlayerState();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Error stopping media");
            IsPlaying = false;
            HasMediaLoaded = false;
        }
    }

    private bool CanPlay() =>
        Player != null && !string.IsNullOrEmpty(MediaUrl) && !IsPlaying && !_isPlayPending;

    private bool CanPause() => Player != null && HasMediaLoaded;

    private bool CanStop() => Player != null && HasMediaLoaded;

    private void SubscribePlayerEvents()
    {
        if (Player == null)
        {
            return;
        }

        Player.StateChanged += OnPlayerStateChanged;
        Player.PositionChanged += OnPlayerPositionChanged;
        Player.Failed += OnPlayerFailed;
    }

    private void UnsubscribePlayerEvents()
    {
        if (Player == null)
        {
            return;
        }

        Player.StateChanged -= OnPlayerStateChanged;
        Player.PositionChanged -= OnPlayerPositionChanged;
        Player.Failed -= OnPlayerFailed;
    }

    private void OnPlayerStateChanged(object? sender, EventArgs args) =>
        RunOnUiThread(SyncPlayerState);

    private void OnPlayerPositionChanged(object? sender, EventArgs args) =>
        RunOnUiThread(RaisePositionPropertiesChanged);

    private void OnPlayerFailed(Exception exception)
    {
        _logger.Error(exception, "Media player failed to play media: {MediaUrl}", MediaUrl);
        RunOnUiThread(SyncPlayerState);
    }

    private void SyncPlayerState()
    {
        HasMediaLoaded = Player?.HasMediaLoaded ?? false;
        IsPlaying = Player?.IsPlaying ?? false;
        RaisePropertyChanged(nameof(Volume));
        RaisePositionPropertiesChanged();
    }

    private void RaisePositionPropertiesChanged()
    {
        RaisePropertyChanged(nameof(TimePosition));
        RaisePropertyChanged(nameof(Duration));
        RaisePropertyChanged(nameof(TimeRemaining));
        RaisePropertyChanged(nameof(PercentPos));
        RaisePropertyChanged(nameof(IsSeekable));
    }

    private void OnFolderSelected(SelectedDirectory directory)
    {
        try
        {
            Deactivate();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Error handling folder selection");
        }
    }

    private void OnPreviewSelected(SelectedMediaPreview preview)
    {
        try
        {
            Stop();
            MediaUrl = preview?.Url ?? string.Empty;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Error handling preview selection");
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
        catch (Exception exception)
        {
            _logger.Error(exception, "Error updating commands state");
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

    private void OnError(Exception exception) =>
        _logger.Error(exception, "Error in observable sequence");
}
