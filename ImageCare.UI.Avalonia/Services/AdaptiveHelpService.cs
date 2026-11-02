using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Notification;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.FolderService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.NotificationService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using Prism.Commands.Ex;
using Prism.Mvvm;

using Serilog;

namespace ImageCare.UI.Avalonia.Services;

/// <summary>
/// Owns adaptive-help event handling, recommendation decisions and presentation data. Feature views only
/// publish their normal domain events; they do not decide which help content to display.
/// </summary>
internal sealed class AdaptiveHelpService : BindableBase, IDisposable
{
    private const int CurrentExperienceVersion = 2;
    private static readonly TimeSpan ErrorWindow = TimeSpan.FromMinutes(5);

    private readonly IConfigurationService _configurationService;
    private readonly IFolderService _folderService;
    private readonly IMediaPreviewOperationsService _fileOperationsService;
    private readonly INotificationService _notificationService;
    private readonly MediaSelectionCoordinator _selectionCoordinator;
    private readonly AdaptiveHelpContextService _helpContext;
    private readonly ShellUiActionService _shellActions;
    private readonly AdaptiveHelpPolicy _policy;
    private readonly AdaptiveHelpCatalog _catalog;
    private readonly AdaptiveHelpProgressStore _progressStore;
    private readonly LocalizationManager _localization;
    private readonly SynchronizationContext _synchronizationContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly Queue<DateTimeOffset> _recentErrors = new();
    private CompositeDisposable? _subscriptions;
    private bool _isVisible;
    private bool _isGoalSelectionVisible;
    private bool _isAutomaticMode;
    private bool _automaticShownThisSession;
    private bool _isReturningSession;
    private bool _hasNewFeatures;

    public AdaptiveHelpService(
        IConfigurationService configurationService,
        IFolderService folderService,
        IMediaPreviewOperationsService fileOperationsService,
        INotificationService notificationService,
        MediaSelectionCoordinator selectionCoordinator,
        AdaptiveHelpContextService helpContext,
        ShellUiActionService shellActions,
        AdaptiveHelpPolicy policy,
        AdaptiveHelpCatalog catalog,
        AdaptiveHelpProgressStore progressStore,
        LocalizationManager localization,
        SynchronizationContext synchronizationContext,
        TimeProvider timeProvider,
        ILogger logger)
    {
        _configurationService = configurationService;
        _folderService = folderService;
        _fileOperationsService = fileOperationsService;
        _notificationService = notificationService;
        _selectionCoordinator = selectionCoordinator;
        _helpContext = helpContext;
        _shellActions = shellActions;
        _policy = policy;
        _catalog = catalog;
        _progressStore = progressStore;
        _localization = localization;
        _synchronizationContext = synchronizationContext;
        _timeProvider = timeProvider;
        _logger = logger;

        ShowHelpCommand = new DelegateCommand(ShowHelp);
        HideCommand = new DelegateCommand(Hide);
        OpenHelpSettingsCommand = new DelegateCommand(_shellActions.OpenSettings);
        SelectGoalCommand = new DelegateCommand<string>(SelectGoal);
    }

    public ICommand ShowHelpCommand { get; }

    public ICommand HideCommand { get; }

    public ICommand OpenHelpSettingsCommand { get; }

    public ICommand SelectGoalCommand { get; }

    public ObservableCollection<AdaptiveHelpRecommendationViewModel> Recommendations { get; } = new();

    public bool IsVisible
    {
        get => _isVisible;
        private set => SetProperty(ref _isVisible, value);
    }

    public bool IsGoalSelectionVisible
    {
        get => _isGoalSelectionVisible;
        private set => SetProperty(ref _isGoalSelectionVisible, value);
    }

    public bool IsAutomaticMode
    {
        get => _isAutomaticMode;
        private set => SetProperty(ref _isAutomaticMode, value);
    }

    public string PanelTitle => _localization[IsAutomaticMode ? "Help_SuggestionTitle" : "Help_CenterTitle"];

    public string PanelSubtitle => _localization[IsAutomaticMode ? "Help_SuggestionSubtitle" : "Help_CenterSubtitle"];

    public void Start()
    {
        if (_subscriptions != null)
        {
            return;
        }

        try
        {
            var configuration = _configurationService.Configuration.Value;
            var now = _timeProvider.GetUtcNow();
            var currentVersion = GetApplicationVersion();
            var session = _progressStore.RecordSession(
                configuration,
                now,
                CurrentExperienceVersion,
                currentVersion);

            _isReturningSession = _policy.IsReturningUser(
                session.PreviousSessionUtc,
                session.SessionHistoryUtc,
                now);
            _hasNewFeatures = !string.IsNullOrWhiteSpace(session.LastSeenVersion)
                && !string.Equals(session.LastSeenVersion, currentVersion, StringComparison.Ordinal);

            _selectionCoordinator.SelectionChanged += OnSelectionChanged;
            _helpContext.Changed += OnHelpContextChanged;
            _localization.CultureChanged += OnCultureChanged;
            _subscriptions = new CompositeDisposable
            {
                Disposable.Create(() => _selectionCoordinator.SelectionChanged -= OnSelectionChanged),
                Disposable.Create(() => _helpContext.Changed -= OnHelpContextChanged),
                Disposable.Create(() => _localization.CultureChanged -= OnCultureChanged),
                _folderService.FileSystemItemSelected
                    .ObserveOn(_synchronizationContext)
                    .Subscribe(OnFolderSelected, OnObservableError),
                _fileOperationsService.ImagePreviewSelected
                    .ObserveOn(_synchronizationContext)
                    .Subscribe(_ => OnMediaSelected(), OnObservableError),
                _fileOperationsService.FileOperationCompleted
                    .ObserveOn(_synchronizationContext)
                    .Subscribe(OnFileOperationCompleted, OnObservableError),
                _notificationService.NotificationReceived
                    .OfType<ErrorNotification>()
                    .ObserveOn(_synchronizationContext)
                    .Subscribe(_ => OnUserVisibleError(), OnObservableError),
                _configurationService.ConfigurationSaved
                    .ObserveOn(_synchronizationContext)
                    .Subscribe(OnConfigurationSaved, OnObservableError)
            };

            var shouldIntroduce = _configurationService.IsFirstRun
                && configuration.AdaptiveHelpUserGoal == AdaptiveHelpUserGoal.Unknown;
            var shouldSuggest = shouldIntroduce || _isReturningSession || _hasNewFeatures;
            if (shouldSuggest && _policy.CanShowAutomatically(configuration, now))
            {
                ShowPanel(automatic: true, showGoalSelection: shouldIntroduce);
            }
            else
            {
                _progressStore.Save();
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to start adaptive help");
        }
    }

    public void Stop()
    {
        _subscriptions?.Dispose();
        _subscriptions = null;
        Recommendations.Clear();
        IsVisible = false;
    }

    public void Dispose() => Stop();

    private void ShowHelp()
    {
        try
        {
            var configuration = _configurationService.Configuration.Value;
            ShowPanel(
                automatic: false,
                showGoalSelection: configuration.AdaptiveHelpUserGoal == AdaptiveHelpUserGoal.Unknown);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to show adaptive help center");
        }
    }

    private void ShowPanel(bool automatic, bool showGoalSelection)
    {
        IsAutomaticMode = automatic;
        IsGoalSelectionVisible = showGoalSelection;
        RaisePropertyChanged(nameof(PanelTitle));
        RaisePropertyChanged(nameof(PanelSubtitle));
        BuildRecommendations(automatic, trackExposure: true);
        IsVisible = true;

        var configuration = _configurationService.Configuration.Value;
        if (automatic)
        {
            _automaticShownThisSession = true;
            configuration.AdaptiveHelpLastShownUtc = _timeProvider.GetUtcNow();
        }

        _progressStore.Save();
    }

    private void Hide()
    {
        if (IsAutomaticMode && Recommendations.FirstOrDefault() is { } recommendation)
        {
            var configuration = _configurationService.Configuration.Value;
            _progressStore.RecordDismissal(
                configuration,
                recommendation.Id,
                dismissImmediately: false);
            _progressStore.Save();
        }

        IsVisible = false;
    }

    private void SelectGoal(string goalName)
    {
        if (!Enum.TryParse<AdaptiveHelpUserGoal>(goalName, out var goal))
        {
            return;
        }

        var configuration = _configurationService.Configuration.Value;
        configuration.AdaptiveHelpUserGoal = goal;
        IsGoalSelectionVisible = false;
        IsAutomaticMode = false;
        RaisePropertyChanged(nameof(PanelTitle));
        RaisePropertyChanged(nameof(PanelSubtitle));
        BuildRecommendations(automatic: false, trackExposure: true);
        _progressStore.Save();
    }

    private void OnFolderSelected(SelectedDirectory directory)
    {
        RecordSuccessfulAction(directory.FileManagerPanel switch
        {
            FileManagerPanel.Left => AdaptiveHelpPolicy.SelectSourceHintId,
            FileManagerPanel.Right => AdaptiveHelpPolicy.SelectTargetHintId,
            _ => string.Empty
        });
        RefreshVisibleRecommendations();
    }

    private void OnMediaSelected()
    {
        RecordSuccessfulAction(AdaptiveHelpPolicy.SelectMediaHintId);
        RefreshVisibleRecommendations();
    }

    private void OnSelectionChanged(object? sender, EventArgs args)
    {
        if (_selectionCoordinator.Count > 0)
        {
            RecordSuccessfulAction(AdaptiveHelpPolicy.SelectMediaHintId);
        }

        RefreshVisibleRecommendations();
    }

    private void OnHelpContextChanged(object? sender, EventArgs args)
    {
        RefreshVisibleRecommendations();
    }

    private void OnFileOperationCompleted(MediaFileOperationCompleted operation)
    {
        switch (operation.Kind)
        {
            case MediaFileOperationKind.Copy:
            case MediaFileOperationKind.Move:
                RecordSuccessfulAction(AdaptiveHelpPolicy.TransferHintId);
                break;
            case MediaFileOperationKind.Delete:
            case MediaFileOperationKind.Restore:
                RecordSuccessfulAction(AdaptiveHelpPolicy.SafeDeletionHintId);
                break;
        }

        RefreshVisibleRecommendations();
    }

    private void OnUserVisibleError()
    {
        var now = _timeProvider.GetUtcNow();
        _recentErrors.Enqueue(now);
        TrimRecentErrors(now);

        var configuration = _configurationService.Configuration.Value;
        if (_recentErrors.Count < 2
            || !configuration.AdaptiveHelpDifficultyAssistanceEnabled
            || _automaticShownThisSession
            || IsVisible
            || _helpContext.IsBatchOperationRunning
            || !_policy.CanShowAutomatically(configuration, now))
        {
            RefreshVisibleRecommendations();
            return;
        }

        ShowPanel(automatic: true, showGoalSelection: false);
    }

    private void OnConfigurationSaved(Configuration configuration)
    {
        if (!configuration.AdaptiveHelpEnabled && IsAutomaticMode && IsVisible)
        {
            IsVisible = false;
        }
    }

    private void OnCultureChanged(object? sender, EventArgs args)
    {
        RaisePropertyChanged(nameof(PanelTitle));
        RaisePropertyChanged(nameof(PanelSubtitle));
        if (IsVisible)
        {
            BuildRecommendations(IsAutomaticMode, trackExposure: false);
        }
    }

    private void RefreshVisibleRecommendations()
    {
        if (IsVisible)
        {
            BuildRecommendations(IsAutomaticMode, trackExposure: false);
        }
    }

    private void BuildRecommendations(bool automatic, bool trackExposure)
    {
        var configuration = _configurationService.Configuration.Value;
        var now = _timeProvider.GetUtcNow();
        TrimRecentErrors(now);
        var context = new AdaptiveHelpContext(
            _configurationService.IsFirstRun,
            _folderService.GetSelectedDirectory(FileManagerPanel.Left) != null,
            _folderService.GetSelectedDirectory(FileManagerPanel.Right) != null,
            _selectionCoordinator.Count,
            _recentErrors.Count,
            HasRestorableFolders(configuration),
            _hasNewFeatures,
            _isReturningSession,
            _helpContext.IsFolderOverviewOpen,
            _helpContext.FolderOverviewSelectionCount,
            _helpContext.IsBatchOperationRunning);
        var decisions = _policy.Rank(context, configuration, automatic);
        var returningThreshold = _policy.GetReturningThreshold(configuration.AdaptiveHelpSessionHistoryUtc);

        Recommendations.Clear();
        foreach (var decision in decisions)
        {
            if (!_catalog.TryGetHint(decision.HintId, out var hint))
            {
                continue;
            }

            if (trackExposure)
            {
                _progressStore.RecordExposure(configuration, hint.Id, now);
            }

            Recommendations.Add(
                new AdaptiveHelpRecommendationViewModel(
                    hint.Id,
                    _catalog.GetTitle(hint),
                    _catalog.GetMessage(hint, configuration),
                    _catalog.GetReason(decision, context, returningThreshold),
                    _catalog.GetActionText(hint),
                    _localization["Help_DontShowAgain"],
                    ExecuteActionAsync,
                    DismissRecommendation));
        }
    }

    private async Task ExecuteActionAsync(string hintId)
    {
        if (!_catalog.TryGetHint(hintId, out var hint))
        {
            return;
        }

        try
        {
            switch (hint.Action)
            {
                case AdaptiveHelpActionKind.FocusSource:
                    _shellActions.FocusFolders(FileManagerPanel.Left);
                    break;
                case AdaptiveHelpActionKind.FocusTarget:
                    _shellActions.FocusFolders(FileManagerPanel.Right);
                    break;
                case AdaptiveHelpActionKind.FocusPreviews:
                    var panel = _selectionCoordinator.FileManagerPanel == FileManagerPanel.Right
                        ? FileManagerPanel.Right
                        : FileManagerPanel.Left;
                    _shellActions.FocusPreviews(panel);
                    break;
                case AdaptiveHelpActionKind.RestoreFolders:
                    await _shellActions.RestoreLastFoldersAsync();
                    RecordSuccessfulAction(AdaptiveHelpPolicy.RestoreFoldersHintId);
                    break;
                case AdaptiveHelpActionKind.OpenSearch:
                    _shellActions.OpenMediaSearch();
                    RecordSuccessfulAction(AdaptiveHelpPolicy.SearchHintId);
                    break;
                case AdaptiveHelpActionKind.OpenSettings:
                    _shellActions.OpenSettings();
                    break;
            }

            IsVisible = false;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to execute adaptive help action {HintId}", hintId);
        }
    }

    private void DismissRecommendation(string hintId)
    {
        var configuration = _configurationService.Configuration.Value;
        _progressStore.RecordDismissal(configuration, hintId, dismissImmediately: true);
        if (IsAutomaticMode)
        {
            IsVisible = false;
        }
        else
        {
            BuildRecommendations(automatic: false, trackExposure: false);
        }

        _progressStore.Save();
    }

    private void RecordSuccessfulAction(string hintId)
    {
        if (string.IsNullOrEmpty(hintId))
        {
            return;
        }

        _progressStore.RecordSuccessfulAction(
            _configurationService.Configuration.Value,
            hintId,
            _timeProvider.GetUtcNow());
        _progressStore.Save();
    }

    private static bool HasRestorableFolders(Configuration configuration) =>
        !string.IsNullOrWhiteSpace(configuration.LastSourceDirectoryPath)
        || !string.IsNullOrWhiteSpace(configuration.LastTargetDirectoryPath);

    private void TrimRecentErrors(DateTimeOffset now)
    {
        while (_recentErrors.Count > 0 && now - _recentErrors.Peek() > ErrorWindow)
        {
            _recentErrors.Dequeue();
        }
    }

    private void OnObservableError(Exception exception)
    {
        _logger.Error(exception, "Error in adaptive help event stream");
    }

    private static string GetApplicationVersion() =>
        typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
