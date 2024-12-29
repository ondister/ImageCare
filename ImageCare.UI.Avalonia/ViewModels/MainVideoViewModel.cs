using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reflection.Metadata;
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
    private double _volume;
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
                Mpv.TimePos.Changed -= OnTimePosChanged;
                Mpv.TimeRemaining.Changed -= OnTimeRemainingChanged;
                Mpv.Seekable.Changed -= OnSeekableChanged;
                Mpv.PercentPos.Changed -= OnPercentPosChanged;
            }
            if (SetProperty(ref _mpv, value))
            {
                if (_mpv != null)
                {
                    Mpv.TimePos.Changed += OnTimePosChanged;
                    Mpv.TimeRemaining.Changed += OnTimeRemainingChanged;
                    Mpv.Seekable.Changed += OnSeekableChanged;
                    Mpv.PercentPos.Changed += OnPercentPosChanged;
                    Mpv.Volume.Changed += OnVolumeChanged;
                }
               
            }
        }
    }

    private void OnVolumeChanged(object? sender, MpvValueChangedEventArgs<double, double> e)
    {
        OnPropertyChanged(nameof(Volume));
    }

    private void OnPercentPosChanged(object? sender, HanumanInstitute.LibMpv.MpvValueChangedEventArgs<double, double> e)
    {
        OnPropertyChanged(nameof(PercentPos));
    }

    private void OnSeekableChanged(object? sender, HanumanInstitute.LibMpv.MpvValueChangedEventArgs<bool, bool> e)
    {
        OnPropertyChanged(nameof(IsSeekable));
    }

    private void OnTimePosChanged(object? sender, HanumanInstitute.LibMpv.MpvValueChangedEventArgs<double, double> e)
    {
       OnPropertyChanged(nameof(TimePosition));
    }

    private void OnTimeRemainingChanged(object? sender, HanumanInstitute.LibMpv.MpvValueChangedEventArgs<double, double> e)
    {
        OnPropertyChanged(nameof(TimeRemaining));
    }

    public TimeSpan TimePosition
    {
        get
        {
            if (Mpv == null)
            {
                return TimeSpan.Zero;
            }

            return TimeSpan.FromSeconds(Mpv.TimePos.Get().Value);
        }
        set
        {
            Mpv.TimePos.Set(value.TotalSeconds);
        } 
    }

    public double? Volume
    {
        get
        {
            if (Mpv == null)
            {
                return 0.0;
            }

            return  Mpv.Volume.Get();
        }
        set
        {
            if (Mpv == null)
            {
                return;
            }

            Mpv.Volume.Set(value.Value);

            OnPropertyChanged(nameof(Volume));
        } 
    }

    public TimeSpan TimeRemaining
    {
        get
        {
            if (Mpv == null)
            {
                return TimeSpan.Zero;
            }

            return TimeSpan.FromSeconds(Mpv.TimeRemaining.Get().Value);
        }
    }

    public double? PercentPos
    {
        get
        {
            if (Mpv == null)
            {
                return 0.0;
            }
            return Mpv.PercentPos.Get();
        }
        set
        {
            if (Mpv == null)
            {
                return;
            }

            Mpv.PercentPos.Set(value.Value);
            OnPropertyChanged(nameof(PercentPos));
        }
    }

    public bool? IsSeekable
    {
        get
        {
            if (Mpv == null)
            {
                return false;
            }

            return Mpv.Seekable.Get();
        }
    }

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
}