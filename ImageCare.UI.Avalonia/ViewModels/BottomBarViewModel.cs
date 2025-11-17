using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Windows.Input;

using AutoMapper;

using ImageCare.Core.Services.NotificationService;
using ImageCare.Modules.Logging.Services;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using Prism.Commands;
using Prism.Dialogs;
using Prism.Navigation.Regions;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels;

internal class BottomBarViewModel : NavigatedViewModelBase
{
	private readonly IDialogService _dialogService;
	private readonly INotificationService _notificationService;
	private readonly ILogNotificationService _logNotificationService;
	private readonly IMapper _mapper;
	private readonly ILogger _logger;
	private readonly SynchronizationContext _synchronizationContext;
	private CompositeDisposable? _compositeDisposable;
	private int? _messagesCount;
	private bool _isDisposed;

	private int _errorsCount;
	private int _warningsCount;
	private NotificationViewModel? _notificationViewModel;

	public BottomBarViewModel(IDialogService dialogService,
	                          INotificationService notificationService,
	                          ILogNotificationService logNotificationService,
	                          IMapper mapper,
	                          ILogger logger,
	                          SynchronizationContext synchronizationContext)
	{
		_dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
		_notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
		_logNotificationService = logNotificationService ?? throw new ArgumentNullException(nameof(logNotificationService));
		_mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_synchronizationContext = synchronizationContext ?? throw new ArgumentNullException(nameof(synchronizationContext));

		OpenLogWindowCommand = new DelegateCommand(OpenLogWindow, CanOpenLogWindow)
			.ObservesProperty(() => MessagesCount);
		OpenSettingsWindowCommand = new DelegateCommand(OpenSettingsWindow);
	}

	public int? MessagesCount
	{
		get => _messagesCount;
		private set => SetProperty(ref _messagesCount, value);
	}

	public NotificationViewModel? NotificationViewModel
	{
		get => _notificationViewModel;
		private set => SetProperty(ref _notificationViewModel, value);
	}

	public ICommand OpenLogWindowCommand { get; }

	public ICommand OpenSettingsWindowCommand { get; }

	public override void OnNavigatedTo(NavigationContext navigationContext)
	{
		ThrowIfDisposed();

		try
		{
			base.OnNavigatedTo(navigationContext);

			_errorsCount = _logNotificationService.GetErrorsCount();
			_warningsCount = _logNotificationService.GetWarningsCount();
			UpdateMessagesCount();

			_compositeDisposable = new CompositeDisposable
			{
				_logNotificationService.ErrorsCountUpdated
				                       .Subscribe(OnErrorsMessagesCountUpdated, OnError),
				_logNotificationService.WarningsCountUpdated
				                       .Subscribe(OnWarningsMessagesCountUpdated, OnError),
				_notificationService.NotificationReceived
				                    .ObserveOn(_synchronizationContext)
				                    .Subscribe(OnNotificationReceived, OnError)
			};
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Failed to navigate to BottomBarViewModel");
		}
	}

	public override void OnNavigatedFrom(NavigationContext navigationContext)
	{
		try
		{
			base.OnNavigatedFrom(navigationContext);
			SafeDispose(ref _compositeDisposable);
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Failed to navigate from BottomBarViewModel");
		}
	}

	/// <inheritdoc />
	public override void Dispose()
	{
		SafeDispose(ref _compositeDisposable);
		base.Dispose();
	}

	private void OpenLogWindow()
	{
		try
		{
			var parameters = new DialogParameters();
			_dialogService.Show(
				"logViewer",
				parameters,
				null,
				"childWindow");

			_logger.Debug("Log window opened");
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Failed to open log window");
		}
	}

	private void OpenSettingsWindow()
	{
		try
		{
			var parameters = new DialogParameters();
			_dialogService.ShowDialog(
				"settingsViewer",
				parameters);

			_logger.Debug("Settings window opened");
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Failed to open settings window");
		}
	}

	private bool CanOpenLogWindow()
	{
		try
		{
			return MessagesCount > 0;
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Error in CanOpenLogWindow");
			return false;
		}
	}

	private void UpdateMessagesCount()
	{
		try
		{
			var total = _errorsCount + _warningsCount;
			MessagesCount = total > 0 ? total : null;
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Failed to update messages count");
			MessagesCount = null;
		}
	}

	private void OnErrorsMessagesCountUpdated(int errorsCount)
	{
		try
		{
			_errorsCount = errorsCount;
			UpdateMessagesCount();
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Failed to process errors count update");
		}
	}

	private void OnWarningsMessagesCountUpdated(int warningsCount)
	{
		try
		{
			_warningsCount = warningsCount;
			UpdateMessagesCount();
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Failed to process warnings count update");
		}
	}

	private void OnNotificationReceived(Notification? notification)
	{
		try
		{
			if (notification == null)
			{
				_logger.Warning("Received null notification");
				return;
			}

			NotificationViewModel = _mapper.Map<NotificationViewModel>(notification);
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Failed to process notification");
		}
	}

	private void OnError(Exception exception)
	{
		_logger.Error(exception, "Error in observable subscription");
	}

	private void ThrowIfDisposed()
	{
		if (_isDisposed)
		{
			throw new ObjectDisposedException(nameof(BottomBarViewModel));
		}
	}

	private void SafeDispose<T>(ref T? disposable) where T : IDisposable?
	{
		try
		{
			disposable?.Dispose();
			disposable = default;
			_isDisposed = true;
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Failed to dispose {Type}", typeof(T).Name);
		}
	}
}