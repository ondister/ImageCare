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
	private bool _isPlaying;
	private bool _hasMediaLoaded;

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

		InitializeMpvContext();
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
		private set
		{
			UnsubscribeMpvEvents();
			if (SetProperty(ref _mpv, value))
			{
				SubscribeMpvEvents();
			}
		}
	}

	public TimeSpan TimePosition
	{
		get => Mpv == null ? TimeSpan.Zero : TimeSpan.FromSeconds(Mpv.TimePos.Get().GetValueOrDefault());
		set
		{
			try
			{
				if (Mpv != null && value >= TimeSpan.Zero)
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
		get => Mpv?.Volume.Get() ?? 0.0;
		set
		{
			try
			{
				if (Mpv != null && value is >= 0 and <= 100)
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

	public TimeSpan TimeRemaining => Mpv == null ? TimeSpan.Zero : TimeSpan.FromSeconds(Mpv.TimeRemaining.Get().GetValueOrDefault());

	public double PercentPos
	{
		get => Mpv?.PercentPos.Get() ?? 0.0;
		set
		{
			try
			{
				if (Mpv != null && value is >= 0 and <= 100)
				{
					Mpv.PercentPos.Set(value);
				}
			}
			catch (Exception ex)
			{
				_logger.Error(ex, "Error setting percent position to {PercentPos}", value);
			}
		}
	}

	public bool IsSeekable => Mpv?.Seekable.Get() ?? false;

	public string MediaUrl
	{
		get => _mediaUrl;
		set
		{
			if (SetProperty(ref _mediaUrl, value ?? string.Empty))
			{
				UpdateCommandsState();

				if (!string.IsNullOrEmpty(value))
				{
					HasMediaLoaded = false;
					IsPlaying = false;
				}
			}
		}
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
			if (Mpv == null || string.IsNullOrEmpty(MediaUrl))
			{
				return;
			}

			if (HasMediaLoaded && IsPlaying)
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
			if (Mpv == null || !HasMediaLoaded)
			{
				return;
			}

			var isPaused = Mpv.Pause.Get();
			if (!isPaused.HasValue)
			{
				return;
			}

			Mpv.Pause.Set(!isPaused.Value);
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

	protected override void OnDispose()
	{
		try
		{
			Stop();
			UnsubscribeMpvEvents();

			_compositeDisposable?.Dispose();
			_compositeDisposable = null;

			Mpv?.Dispose();
			Mpv = null;
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Error during disposal of MainVideoViewModel");
		}

		base.OnDispose();
	}

	private bool CanPlay()
	{
		return !string.IsNullOrEmpty(MediaUrl) && (!IsPlaying || !HasMediaLoaded);
	}

	private bool CanPause()
	{
		return HasMediaLoaded && IsPlaying;
	}

	private bool CanStop()
	{
		return HasMediaLoaded && IsPlaying;
	}

	private void InitializeMpvContext()
	{
		try
		{
			Mpv = new MpvContext();
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Error initializing MPV context");

			Mpv = null;
		}
	}

	private void SubscribeMpvEvents()
	{
		if (Mpv == null)
		{
			return;
		}

		try
		{
			Mpv.TimePos.Changed += OnTimePosChanged;
			Mpv.TimeRemaining.Changed += OnTimeRemainingChanged;
			Mpv.Seekable.Changed += OnSeekableChanged;
			Mpv.PercentPos.Changed += OnPercentPosChanged;
			Mpv.Volume.Changed += OnVolumeChanged;
			Mpv.Pause.Changed += OnPauseStateChanged;
			Mpv.FileLoaded += OnFileLoaded;
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Error subscribing to MPV events");
		}
	}

	private void UnsubscribeMpvEvents()
	{
		if (_mpv == null)
		{
			return;
		}

		try
		{
			_mpv.TimePos.Changed -= OnTimePosChanged;
			_mpv.TimeRemaining.Changed -= OnTimeRemainingChanged;
			_mpv.Seekable.Changed -= OnSeekableChanged;
			_mpv.PercentPos.Changed -= OnPercentPosChanged;
			_mpv.Volume.Changed -= OnVolumeChanged;
			_mpv.Pause.Changed -= OnPauseStateChanged;
			_mpv.FileLoaded -= OnFileLoaded;
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Error unsubscribing from MPV events");
		}
	}

	private void OnFileLoaded(object? sender, EventArgs e)
	{
		try
		{
			HasMediaLoaded = true;
			IsPlaying = true;
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Error handling file loaded event");
		}
	}

	private void OnPauseStateChanged(object? sender, MpvValueChangedEventArgs<bool, bool> e)
	{
		try
		{
			if (!e.NewValue.HasValue)
			{
				return;
			}

			IsPlaying = !e.NewValue.Value && HasMediaLoaded;
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Error handling pause state change");
		}
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

	private void OnVolumeChanged(object? sender, MpvValueChangedEventArgs<double, double> e)
	{
		RaisePropertyChanged(nameof(Volume));
	}

	private void OnPercentPosChanged(object? sender, MpvValueChangedEventArgs<double, double> e)
	{
		RaisePropertyChanged(nameof(PercentPos));
	}

	private void OnSeekableChanged(object? sender, MpvValueChangedEventArgs<bool, bool> e)
	{
		RaisePropertyChanged(nameof(IsSeekable));
	}

	private void OnTimePosChanged(object? sender, MpvValueChangedEventArgs<double, double> e)
	{
		RaisePropertyChanged(nameof(TimePosition));
	}

	private void OnTimeRemainingChanged(object? sender, MpvValueChangedEventArgs<double, double> e)
	{
		RaisePropertyChanged(nameof(TimeRemaining));
	}

	private void OnError(Exception exception)
	{
		_logger.Error(exception, "Error in observable sequence");
	}
}