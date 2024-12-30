using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Windows.Input;

using HanumanInstitute.LibMpv;

using ImageCare.Core.Domain;
using ImageCare.Core.Services.FileOperationsService;
using ImageCare.Core.Services.FolderService;
using ImageCare.Mvvm;

using Prism.Commands;
using Prism.Regions;

using Serilog;

using MpvContext = HanumanInstitute.LibMpv.MpvContext;

namespace ImageCare.UI.Avalonia.ViewModels;

internal class MainVideoViewModel : ViewModelBase, IDisposable
{
    private readonly IFileOperationsService _fileOperationsService;
    private readonly IFolderService _folderService;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;

    private string _mediaUrl;
    private CompositeDisposable? _compositeDisposable;
    private MpvContext? _mpv = new();

    public MainVideoViewModel(IFileOperationsService fileOperationsService,
                              IFolderService folderService,
                              ILogger logger,
                              SynchronizationContext synchronizationContext)
    {
        _fileOperationsService = fileOperationsService;
        _folderService = folderService;
        _logger = logger;
        _synchronizationContext = synchronizationContext;

        PlayCommand = new DelegateCommand(Play);
        PauseCommand = new DelegateCommand(Pause);
        StopCommand = new DelegateCommand(Stop);
    }

    public ICommand PauseCommand { get; set; }

    public ICommand PlayCommand { get; }

    public ICommand StopCommand { get; }

    public MpvContext? Mpv
    {
        get => _mpv;
        set
        {
            if (_mpv != null)
            {
                try
                {
                    Mpv.TimePos.Changed -= OnTimePosChanged;
                    Mpv.TimeRemaining.Changed -= OnTimeRemainingChanged;
                    Mpv.Seekable.Changed -= OnSeekableChanged;
                    Mpv.PercentPos.Changed -= OnPercentPosChanged;
                    Mpv.Volume.Changed -= OnVolumeChanged;
                }
                catch (Exception exception)
                {
                    _logger.Error(exception, "Unexpected error during unsubscribing from mpv context events.");
                }
            }

            if (SetProperty(ref _mpv, value))
            {
                if (_mpv != null)
                {
                    try
                    {
                        Mpv.TimePos.Changed += OnTimePosChanged;
                        Mpv.TimeRemaining.Changed += OnTimeRemainingChanged;
                        Mpv.Seekable.Changed += OnSeekableChanged;
                        Mpv.PercentPos.Changed += OnPercentPosChanged;
                        Mpv.Volume.Changed += OnVolumeChanged;
                    }
                    catch (Exception exception)
                    {
                        _logger.Error(exception, "Unexpected error during subscribing from mpv context events.");
                    }
                }
            }
        }
    }

    public TimeSpan TimePosition
    {
        get => Mpv == null ? TimeSpan.Zero : TimeSpan.FromSeconds(Mpv.TimePos.Get().Value);
        set
        {
            if (Mpv == null)
            {
                return;
            }

            Mpv.TimePos.Set(value.TotalSeconds);
        }
    }

    public double? Volume
    {
        get => Mpv == null ? 0.0 : Mpv.Volume.Get();
        set
        {
            if (Mpv == null)
            {
                return;
            }

            if (value.HasValue)
            {
                Mpv.Volume.Set(value.Value);
            }
        }
    }

    public TimeSpan TimeRemaining => Mpv == null ? TimeSpan.Zero : TimeSpan.FromSeconds(Mpv.TimeRemaining.Get().Value);

    public double? PercentPos
    {
        get => Mpv == null ? 0.0 : Mpv.PercentPos.Get();
        set
        {
            if (value.HasValue)
            {
                Mpv?.PercentPos.Set(value.Value);
            }
        }
    }

    public bool? IsSeekable => Mpv == null ? false : Mpv.Seekable.Get();

    public string MediaUrl
    {
        get => _mediaUrl;
        set => SetProperty(ref _mediaUrl, value);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Mpv?.Dispose();
    }

    /// <inheritdoc />
    public override void OnNavigatedTo(NavigationContext navigationContext)
    {
        _compositeDisposable = new CompositeDisposable
        {
            _folderService.FileSystemItemSelected.Subscribe(OnFolderSelected),
            _fileOperationsService.ImagePreviewSelected.Throttle(TimeSpan.FromMilliseconds(150))
                                  .ObserveOn(_synchronizationContext)
                                  .Subscribe(OnPreviewSelected)
        };

        if (navigationContext.Parameters["imagePreview"] is SelectedImagePreview imagePreview)
        {
            OnPreviewSelected(imagePreview);
        }
    }

    /// <inheritdoc />
    public override void OnNavigatedFrom(NavigationContext navigationContext)
    {
        Stop();

        _compositeDisposable?.Dispose();
    }

    public async void Play()
    {
        Stop();

        await Mpv?.LoadFile(MediaUrl).InvokeAsync();
    }

    public void Pause()
    {
        Pause(null);
    }

    public void Pause(bool? value)
    {
        value ??= !Mpv?.Pause.Get()!;
        Mpv?.Pause.Set(value.Value);
    }

    public void Stop()
    {
        Mpv?.Stop().Invoke();
    }

    private void OnFolderSelected(SelectedDirectory directory)
    {
        Stop();
    }

    private void OnPreviewSelected(SelectedImagePreview preview)
    {
        Stop();
        MediaUrl = preview.Url;
    }

    private void OnVolumeChanged(object? sender, MpvValueChangedEventArgs<double, double> e)
    {
        OnPropertyChanged(nameof(Volume));
    }

    private void OnPercentPosChanged(object? sender, MpvValueChangedEventArgs<double, double> e)
    {
        OnPropertyChanged(nameof(PercentPos));
    }

    private void OnSeekableChanged(object? sender, MpvValueChangedEventArgs<bool, bool> e)
    {
        OnPropertyChanged(nameof(IsSeekable));
    }

    private void OnTimePosChanged(object? sender, MpvValueChangedEventArgs<double, double> e)
    {
        OnPropertyChanged(nameof(TimePosition));
    }

    private void OnTimeRemainingChanged(object? sender, MpvValueChangedEventArgs<double, double> e)
    {
        OnPropertyChanged(nameof(TimeRemaining));
    }
}