using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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
/// Owns adaptive-help state, recommendation rules and presentation data. Feature views only
/// publish their normal domain events; they do not decide which help content to display.
/// </summary>
internal sealed class AdaptiveHelpService : BindableBase, IDisposable
{
    private const int CurrentExperienceVersion = 2;
    private static readonly TimeSpan ErrorWindow = TimeSpan.FromMinutes(5);

    private static readonly IReadOnlyDictionary<string, HelpHint> Hints =
        new Dictionary<string, HelpHint>(StringComparer.Ordinal)
        {
            [AdaptiveHelpPolicy.WelcomeHintId] = new(
                AdaptiveHelpPolicy.WelcomeHintId,
                "Help_WelcomeTitle",
                "Help_WelcomeMessage",
                HelpActionKind.FocusSource,
                "Help_ActionChooseSource"),
            [AdaptiveHelpPolicy.ReturningHintId] = new(
                AdaptiveHelpPolicy.ReturningHintId,
                "Help_ReturningTitle",
                "Help_ReturningMessage",
                HelpActionKind.FocusSource,
                "Help_ActionContinue"),
            [AdaptiveHelpPolicy.RestoreFoldersHintId] = new(
                AdaptiveHelpPolicy.RestoreFoldersHintId,
                "Help_RestoreFoldersTitle",
                "Help_RestoreFoldersMessage",
                HelpActionKind.RestoreFolders,
                "Help_ActionRestoreFolders"),
            [AdaptiveHelpPolicy.DifficultyHintId] = new(
                AdaptiveHelpPolicy.DifficultyHintId,
                "Help_DifficultyTitle",
                "Help_DifficultyMessage",
                HelpActionKind.OpenSettings,
                "Help_ActionReviewSettings"),
            [AdaptiveHelpPolicy.SelectSourceHintId] = new(
                AdaptiveHelpPolicy.SelectSourceHintId,
                "Help_SelectSourceTitle",
                "Help_SelectSourceMessage",
                HelpActionKind.FocusSource,
                "Help_ActionChooseSource"),
            [AdaptiveHelpPolicy.SelectTargetHintId] = new(
                AdaptiveHelpPolicy.SelectTargetHintId,
                "Help_SelectTargetTitle",
                "Help_SelectTargetMessage",
                HelpActionKind.FocusTarget,
                "Help_ActionChooseTarget"),
            [AdaptiveHelpPolicy.SelectMediaHintId] = new(
                AdaptiveHelpPolicy.SelectMediaHintId,
                "Help_SelectMediaTitle",
                "Help_SelectMediaMessage",
                HelpActionKind.FocusPreviews,
                "Help_ActionGoToMedia"),
            [AdaptiveHelpPolicy.TransferHintId] = new(
                AdaptiveHelpPolicy.TransferHintId,
                "Help_TransferTitle",
                "Help_TransferMessage",
                HelpActionKind.FocusPreviews,
                "Help_ActionGoToSelection"),
            [AdaptiveHelpPolicy.SearchHintId] = new(
                AdaptiveHelpPolicy.SearchHintId,
                "Help_SearchTitle",
                "Help_SearchMessage",
                HelpActionKind.OpenSearch,
                "Help_ActionOpenSearch"),
            [AdaptiveHelpPolicy.SafeDeletionHintId] = new(
                AdaptiveHelpPolicy.SafeDeletionHintId,
                "Help_SafeDeletionTitle",
                "Help_SafeDeletionMessage",
                HelpActionKind.OpenSettings,
                "Help_ActionOpenSettings"),
            [AdaptiveHelpPolicy.NewFeaturesHintId] = new(
                AdaptiveHelpPolicy.NewFeaturesHintId,
                "Help_NewFeaturesTitle",
                "Help_NewFeaturesMessage",
                HelpActionKind.OpenSearch,
                "Help_ActionTrySearch"),
            [AdaptiveHelpPolicy.SettingsHintId] = new(
                AdaptiveHelpPolicy.SettingsHintId,
                "Help_SettingsTitle",
                "Help_SettingsMessage",
                HelpActionKind.OpenSettings,
                "Help_ActionOpenSettings"),
            [AdaptiveHelpPolicy.OverviewSelectHintId] = new(
                AdaptiveHelpPolicy.OverviewSelectHintId,
                "Help_OverviewSelectTitle",
                "Help_OverviewSelectMessage",
                HelpActionKind.None,
                string.Empty),
            [AdaptiveHelpPolicy.OverviewAnnotationsHintId] = new(
                AdaptiveHelpPolicy.OverviewAnnotationsHintId,
                "Help_OverviewAnnotationsTitle",
                "Help_OverviewAnnotationsMessage",
                HelpActionKind.None,
                string.Empty),
            [AdaptiveHelpPolicy.OverviewBatchHintId] = new(
                AdaptiveHelpPolicy.OverviewBatchHintId,
                "Help_OverviewBatchTitle",
                "Help_OverviewBatchMessage",
                HelpActionKind.None,
                string.Empty),
            [AdaptiveHelpPolicy.OverviewBatchActiveHintId] = new(
                AdaptiveHelpPolicy.OverviewBatchActiveHintId,
                "Help_OverviewBatchActiveTitle",
                "Help_OverviewBatchActiveMessage",
                HelpActionKind.None,
                string.Empty),
            [AdaptiveHelpPolicy.ReadyHintId] = new(
                AdaptiveHelpPolicy.ReadyHintId,
                "Help_ReadyTitle",
                "Help_ReadyMessage",
                HelpActionKind.None,
                string.Empty)
        };

    private readonly IConfigurationService _configurationService;
    private readonly IFolderService _folderService;
    private readonly IMediaPreviewOperationsService _fileOperationsService;
    private readonly INotificationService _notificationService;
    private readonly MediaSelectionCoordinator _selectionCoordinator;
    private readonly AdaptiveHelpContextService _helpContext;
    private readonly ShellUiActionService _shellActions;
    private readonly AdaptiveHelpPolicy _policy;
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
            NormalizeState(configuration);
            var now = _timeProvider.GetUtcNow();
            var previousSessionUtc = configuration.AdaptiveHelpLastSessionUtc;
            var sessionHistory = configuration.AdaptiveHelpSessionHistoryUtc.ToArray();
            var currentVersion = GetApplicationVersion();

            _isReturningSession = _policy.IsReturningUser(previousSessionUtc, sessionHistory, now);
            _hasNewFeatures = !string.IsNullOrWhiteSpace(configuration.AdaptiveHelpLastSeenVersion)
                && !string.Equals(configuration.AdaptiveHelpLastSeenVersion, currentVersion, StringComparison.Ordinal);

            configuration.AdaptiveHelpLastSessionUtc = now;
            configuration.AdaptiveHelpExperienceVersion = CurrentExperienceVersion;
            configuration.AdaptiveHelpLastSeenVersion = currentVersion;
            configuration.AdaptiveHelpSessionHistoryUtc.Add(now);
            while (configuration.AdaptiveHelpSessionHistoryUtc.Count > 10)
            {
                configuration.AdaptiveHelpSessionHistoryUtc.RemoveAt(0);
            }

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
                SaveState();
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

        SaveState();
    }

    private void Hide()
    {
        if (IsAutomaticMode && Recommendations.FirstOrDefault() is { } recommendation)
        {
            var configuration = _configurationService.Configuration.Value;
            var progress = GetOrCreateProgress(configuration, recommendation.Id);
            progress.DismissCount++;
            if (progress.DismissCount >= 2)
            {
                var dismissed = configuration.AdaptiveHelpDismissedHintIds ??= new List<string>();
                if (!dismissed.Contains(recommendation.Id, StringComparer.Ordinal))
                {
                    dismissed.Add(recommendation.Id);
                }
            }

            SaveState();
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
        SaveState();
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

        Recommendations.Clear();
        foreach (var decision in decisions)
        {
            if (!Hints.TryGetValue(decision.HintId, out var hint))
            {
                continue;
            }

            if (trackExposure)
            {
                RecordExposure(hint.Id, now);
            }

            Recommendations.Add(
                new AdaptiveHelpRecommendationViewModel(
                    hint.Id,
                    _localization[hint.TitleKey],
                    GetMessage(hint),
                    GetReason(decision, context),
                    string.IsNullOrEmpty(hint.ActionKey) ? string.Empty : _localization[hint.ActionKey],
                    _localization["Help_DontShowAgain"],
                    ExecuteActionAsync,
                    DismissRecommendation));
        }
    }

    private async Task ExecuteActionAsync(string hintId)
    {
        if (!Hints.TryGetValue(hintId, out var hint))
        {
            return;
        }

        try
        {
            switch (hint.Action)
            {
                case HelpActionKind.FocusSource:
                    _shellActions.FocusFolders(FileManagerPanel.Left);
                    break;
                case HelpActionKind.FocusTarget:
                    _shellActions.FocusFolders(FileManagerPanel.Right);
                    break;
                case HelpActionKind.FocusPreviews:
                    var panel = _selectionCoordinator.FileManagerPanel == FileManagerPanel.Right
                        ? FileManagerPanel.Right
                        : FileManagerPanel.Left;
                    _shellActions.FocusPreviews(panel);
                    break;
                case HelpActionKind.RestoreFolders:
                    await _shellActions.RestoreLastFoldersAsync();
                    RecordSuccessfulAction(AdaptiveHelpPolicy.RestoreFoldersHintId);
                    break;
                case HelpActionKind.OpenSearch:
                    _shellActions.OpenMediaSearch();
                    RecordSuccessfulAction(AdaptiveHelpPolicy.SearchHintId);
                    break;
                case HelpActionKind.OpenSettings:
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
        var dismissed = configuration.AdaptiveHelpDismissedHintIds ??= new List<string>();
        if (!dismissed.Contains(hintId, StringComparer.Ordinal))
        {
            dismissed.Add(hintId);
        }

        var progress = GetOrCreateProgress(configuration, hintId);
        progress.DismissCount++;
        if (IsAutomaticMode)
        {
            IsVisible = false;
        }
        else
        {
            BuildRecommendations(automatic: false, trackExposure: false);
        }

        SaveState();
    }

    private string GetMessage(HelpHint hint)
    {
        if (hint.Id is AdaptiveHelpPolicy.ReturningHintId or AdaptiveHelpPolicy.RestoreFoldersHintId)
        {
            return _localization.Get(hint.MessageKey, GetLastWorkspaceName());
        }

        if (hint.Id == AdaptiveHelpPolicy.TransferHintId)
        {
            var configuration = _configurationService.Configuration.Value;
            var defaultOperation = configuration.CopyOnDragDropByDefault
                ? _localization["Action_Copy"]
                : _localization["Action_Move"];
            var modifier = configuration.DragDropModifier switch
            {
                DragDropModifierKey.Alt => _localization["Settings_ModifierAlt"],
                DragDropModifierKey.Shift => _localization["Settings_ModifierShift"],
                DragDropModifierKey.Meta => _localization["Settings_ModifierMeta"],
                _ => _localization["Settings_ModifierControl"]
            };
            return _localization.Get(hint.MessageKey, defaultOperation, modifier);
        }

        return _localization[hint.MessageKey];
    }

    private string GetReason(AdaptiveHelpDecision decision, AdaptiveHelpContext context)
    {
        return decision.ReasonKey switch
        {
            "Help_ReasonReturning" => _localization.Get(
                decision.ReasonKey,
                Math.Round(_policy.GetReturningThreshold(
                    _configurationService.Configuration.Value.AdaptiveHelpSessionHistoryUtc).TotalDays)),
            "Help_ReasonRepeatedErrors" => _localization.Get(decision.ReasonKey, context.RecentErrorCount),
            "Help_ReasonSelectionReady" => _localization.Get(decision.ReasonKey, context.SelectedMediaCount),
            "Help_ReasonOverviewSelection" => _localization.Get(decision.ReasonKey, context.FolderOverviewSelectionCount),
            "Help_ReasonOverviewMultipleSelection" => _localization.Get(decision.ReasonKey, context.FolderOverviewSelectionCount),
            _ => _localization[decision.ReasonKey]
        };
    }

    private string GetLastWorkspaceName()
    {
        var configuration = _configurationService.Configuration.Value;
        var path = !string.IsNullOrWhiteSpace(configuration.LastSourceDirectoryPath)
            ? configuration.LastSourceDirectoryPath
            : configuration.LastTargetDirectoryPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return _localization["Help_LastWorkspaceUnknown"];
        }

        var normalizedPath = Path.TrimEndingDirectorySeparator(path);
        var name = Path.GetFileName(normalizedPath);
        return string.IsNullOrWhiteSpace(name) ? normalizedPath : name;
    }

    private void RecordExposure(string hintId, DateTimeOffset now)
    {
        var progress = GetOrCreateProgress(_configurationService.Configuration.Value, hintId);
        progress.ExposureCount++;
        progress.LastShownUtc = now;
        if (progress.MasteryLevel == AdaptiveHelpMasteryLevel.NotUsed)
        {
            progress.MasteryLevel = AdaptiveHelpMasteryLevel.Tried;
        }
    }

    private void RecordSuccessfulAction(string hintId)
    {
        if (string.IsNullOrEmpty(hintId))
        {
            return;
        }

        var configuration = _configurationService.Configuration.Value;
        var progress = GetOrCreateProgress(configuration, hintId);
        progress.SuccessfulActionCount++;
        progress.LastCompletedUtc = _timeProvider.GetUtcNow();
        progress.MasteryLevel = progress.SuccessfulActionCount >= 3
            ? AdaptiveHelpMasteryLevel.Mastered
            : AdaptiveHelpMasteryLevel.Completed;

        var completed = configuration.AdaptiveHelpCompletedHintIds ??= new List<string>();
        if (!completed.Contains(hintId, StringComparer.Ordinal))
        {
            completed.Add(hintId);
        }

        SaveState();
    }

    private static AdaptiveHelpProgressEntry GetOrCreateProgress(Configuration configuration, string hintId)
    {
        var progressEntries = configuration.AdaptiveHelpProgress ??= new List<AdaptiveHelpProgressEntry>();
        var progress = progressEntries.FirstOrDefault(item =>
            string.Equals(item.HintId, hintId, StringComparison.Ordinal));
        if (progress != null)
        {
            return progress;
        }

        progress = new AdaptiveHelpProgressEntry { HintId = hintId };
        progressEntries.Add(progress);
        return progress;
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

    private static void NormalizeState(Configuration configuration)
    {
        configuration.AdaptiveHelpCompletedHintIds ??= new List<string>();
        configuration.AdaptiveHelpDismissedHintIds ??= new List<string>();
        configuration.AdaptiveHelpSessionHistoryUtc ??= new List<DateTimeOffset>();
        configuration.AdaptiveHelpProgress ??= new List<AdaptiveHelpProgressEntry>();

        foreach (var completedHintId in configuration.AdaptiveHelpCompletedHintIds)
        {
            var progress = GetOrCreateProgress(configuration, completedHintId);
            if (progress.MasteryLevel < AdaptiveHelpMasteryLevel.Completed)
            {
                progress.MasteryLevel = AdaptiveHelpMasteryLevel.Completed;
            }
        }
    }

    private void SaveState()
    {
        try
        {
            _configurationService.SaveConfiguration();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save adaptive help state");
        }
    }

    private void OnObservableError(Exception exception)
    {
        _logger.Error(exception, "Error in adaptive help event stream");
    }

    private static string GetApplicationVersion() =>
        typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private sealed record HelpHint(
        string Id,
        string TitleKey,
        string MessageKey,
        HelpActionKind Action,
        string ActionKey);

    private enum HelpActionKind
    {
        None,
        FocusSource,
        FocusTarget,
        FocusPreviews,
        RestoreFolders,
        OpenSearch,
        OpenSettings
    }
}
