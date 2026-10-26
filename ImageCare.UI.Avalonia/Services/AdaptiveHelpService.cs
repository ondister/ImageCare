using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Windows.Input;

using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.FolderService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.UI.Avalonia.Localization;

using Prism.Commands.Ex;
using Prism.Mvvm;

using Serilog;

namespace ImageCare.UI.Avalonia.Services;

/// <summary>
/// Owns adaptive-help state, recommendation rules and presentation data. Feature views only
/// publish their normal domain events; they do not decide which hint to display.
/// </summary>
internal sealed class AdaptiveHelpService : BindableBase, IDisposable
{
    private const int CurrentExperienceVersion = 1;
    private static readonly TimeSpan ReturningUserThreshold = TimeSpan.FromDays(30);

    private const string WelcomeHintId = "welcome";
    private const string ReturningHintId = "returning";
    private const string SelectSourceHintId = "select-source";
    private const string SelectTargetHintId = "select-target";
    private const string SelectMediaHintId = "select-media";
    private const string TransferHintId = "transfer-media";
    private const string ReadyHintId = "ready";

    private static readonly IReadOnlyDictionary<string, HelpHint> Hints =
        new Dictionary<string, HelpHint>(StringComparer.Ordinal)
        {
            [WelcomeHintId] = new(WelcomeHintId, "Help_WelcomeTitle", "Help_WelcomeMessage", "Help_ReasonFirstRun"),
            [ReturningHintId] = new(ReturningHintId, "Help_ReturningTitle", "Help_ReturningMessage", "Help_ReasonReturning"),
            [SelectSourceHintId] = new(SelectSourceHintId, "Help_SelectSourceTitle", "Help_SelectSourceMessage", "Help_ReasonContext"),
            [SelectTargetHintId] = new(SelectTargetHintId, "Help_SelectTargetTitle", "Help_SelectTargetMessage", "Help_ReasonContext"),
            [SelectMediaHintId] = new(SelectMediaHintId, "Help_SelectMediaTitle", "Help_SelectMediaMessage", "Help_ReasonContext"),
            [TransferHintId] = new(TransferHintId, "Help_TransferTitle", "Help_TransferMessage", "Help_ReasonContext"),
            [ReadyHintId] = new(ReadyHintId, "Help_ReadyTitle", "Help_ReadyMessage", "Help_ReasonManual")
        };

    private readonly IConfigurationService _configurationService;
    private readonly IFolderService _folderService;
    private readonly IMediaPreviewOperationsService _fileOperationsService;
    private readonly MediaSelectionCoordinator _selectionCoordinator;
    private readonly LocalizationManager _localization;
    private readonly SynchronizationContext _synchronizationContext;
    private readonly ILogger _logger;
    private CompositeDisposable? _subscriptions;
    private HelpHint? _currentHint;
    private bool _isVisible;
    private string _title = string.Empty;
    private string _message = string.Empty;
    private string _reason = string.Empty;

    public AdaptiveHelpService(
        IConfigurationService configurationService,
        IFolderService folderService,
        IMediaPreviewOperationsService fileOperationsService,
        MediaSelectionCoordinator selectionCoordinator,
        LocalizationManager localization,
        SynchronizationContext synchronizationContext,
        ILogger logger)
    {
        _configurationService = configurationService;
        _folderService = folderService;
        _fileOperationsService = fileOperationsService;
        _selectionCoordinator = selectionCoordinator;
        _localization = localization;
        _synchronizationContext = synchronizationContext;
        _logger = logger;

        ShowHelpCommand = new DelegateCommand(ShowHelp);
        HideCommand = new DelegateCommand(Hide);
        CompleteCommand = new DelegateCommand(CompleteCurrentHint);
        DismissCommand = new DelegateCommand(DismissCurrentHint);
    }

    public ICommand ShowHelpCommand { get; }

    public ICommand HideCommand { get; }

    public ICommand CompleteCommand { get; }

    public ICommand DismissCommand { get; }

    public bool IsVisible
    {
        get => _isVisible;
        private set => SetProperty(ref _isVisible, value);
    }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public string Reason
    {
        get => _reason;
        private set => SetProperty(ref _reason, value);
    }

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
            var previousSessionUtc = configuration.AdaptiveHelpLastSessionUtc;
            var now = DateTimeOffset.UtcNow;
            configuration.AdaptiveHelpLastSessionUtc = now;
            configuration.AdaptiveHelpExperienceVersion = CurrentExperienceVersion;

            _selectionCoordinator.SelectionChanged += OnSelectionChanged;
            _localization.CultureChanged += OnCultureChanged;
            _subscriptions = new CompositeDisposable
            {
                Disposable.Create(() => _selectionCoordinator.SelectionChanged -= OnSelectionChanged),
                Disposable.Create(() => _localization.CultureChanged -= OnCultureChanged),
                _folderService.FileSystemItemSelected
                    .ObserveOn(_synchronizationContext)
                    .Subscribe(OnFolderSelected, OnObservableError),
                _fileOperationsService.ImagePreviewSelected
                    .ObserveOn(_synchronizationContext)
                    .Subscribe(_ => OnMediaSelected(), OnObservableError),
                _fileOperationsService.FileOperationCompleted
                    .ObserveOn(_synchronizationContext)
                    .Subscribe(_ => OnFileOperationCompleted(), OnObservableError),
                _configurationService.ConfigurationSaved
                    .ObserveOn(_synchronizationContext)
                    .Subscribe(OnConfigurationSaved, OnObservableError)
            };

            HelpHint? automaticHint = null;
            if (configuration.AdaptiveHelpEnabled)
            {
                if (_configurationService.IsFirstRun)
                {
                    automaticHint = Hints[WelcomeHintId];
                }
                else if (previousSessionUtc == null || now - previousSessionUtc >= ReturningUserThreshold)
                {
                    automaticHint = Hints[ReturningHintId];
                }
            }

            if (automaticHint != null && !IsDismissed(automaticHint.Id))
            {
                configuration.AdaptiveHelpLastShownUtc = now;
                ShowHint(automaticHint);
            }

            SaveState();
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
        IsVisible = false;
    }

    public void Dispose() => Stop();

    private void ShowHelp()
    {
        try
        {
            ShowHint(GetContextualHint());
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to show adaptive help");
        }
    }

    private void Hide() => IsVisible = false;

    private void CompleteCurrentHint()
    {
        if (_currentHint != null)
        {
            MarkCompleted(_currentHint.Id);
        }

        IsVisible = false;
    }

    private void DismissCurrentHint()
    {
        if (_currentHint == null)
        {
            IsVisible = false;
            return;
        }

        var configuration = _configurationService.Configuration.Value;
        var dismissed = configuration.AdaptiveHelpDismissedHintIds ??= new List<string>();
        if (!dismissed.Contains(_currentHint.Id, StringComparer.Ordinal))
        {
            dismissed.Add(_currentHint.Id);
            SaveState();
        }

        IsVisible = false;
    }

    private void OnFolderSelected(SelectedDirectory directory)
    {
        MarkCompleted(directory.FileManagerPanel switch
        {
            FileManagerPanel.Left => SelectSourceHintId,
            FileManagerPanel.Right => SelectTargetHintId,
            _ => string.Empty
        });
        RefreshVisibleHint();
    }

    private void OnMediaSelected()
    {
        MarkCompleted(SelectMediaHintId);
        RefreshVisibleHint();
    }

    private void OnSelectionChanged(object? sender, EventArgs args)
    {
        if (_selectionCoordinator.Count > 0)
        {
            MarkCompleted(SelectMediaHintId);
        }

        RefreshVisibleHint();
    }

    private void OnFileOperationCompleted()
    {
        MarkCompleted(TransferHintId);
        RefreshVisibleHint();
    }

    private void OnConfigurationSaved(Configuration configuration)
    {
        if (!configuration.AdaptiveHelpEnabled && IsVisible)
        {
            IsVisible = false;
        }
    }

    private void OnCultureChanged(object? sender, EventArgs args)
    {
        if (_currentHint != null)
        {
            ApplyLocalizedText(_currentHint);
        }
    }

    private void RefreshVisibleHint()
    {
        if (IsVisible)
        {
            ShowHint(GetContextualHint());
        }
    }

    private HelpHint GetContextualHint()
    {
        var candidates = new List<string>();
        if (_folderService.GetSelectedDirectory(FileManagerPanel.Left) == null)
        {
            candidates.Add(SelectSourceHintId);
        }

        if (_folderService.GetSelectedDirectory(FileManagerPanel.Right) == null)
        {
            candidates.Add(SelectTargetHintId);
        }

        candidates.Add(_selectionCoordinator.Count == 0 ? SelectMediaHintId : TransferHintId);
        candidates.Add(ReadyHintId);

        var selectedId = candidates.FirstOrDefault(id => !IsDismissed(id)) ?? ReadyHintId;
        return Hints[selectedId];
    }

    private void ShowHint(HelpHint hint)
    {
        _currentHint = hint;
        ApplyLocalizedText(hint);
        IsVisible = true;
    }

    private void ApplyLocalizedText(HelpHint hint)
    {
        Title = _localization[hint.TitleKey];
        Message = hint.Id == ReturningHintId
            ? _localization.Get(hint.MessageKey, GetLastWorkspaceName())
            : _localization[hint.MessageKey];
        Reason = _localization[hint.ReasonKey];
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

    private bool IsDismissed(string hintId)
    {
        return (_configurationService.Configuration.Value.AdaptiveHelpDismissedHintIds ?? [])
            .Contains(hintId, StringComparer.Ordinal);
    }

    private void MarkCompleted(string hintId)
    {
        if (string.IsNullOrEmpty(hintId))
        {
            return;
        }

        var completed = _configurationService.Configuration.Value.AdaptiveHelpCompletedHintIds ??= new List<string>();
        if (completed.Contains(hintId, StringComparer.Ordinal))
        {
            return;
        }

        completed.Add(hintId);
        SaveState();
    }

    private static void NormalizeState(Configuration configuration)
    {
        configuration.AdaptiveHelpCompletedHintIds ??= new List<string>();
        configuration.AdaptiveHelpDismissedHintIds ??= new List<string>();
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

    private sealed record HelpHint(string Id, string TitleKey, string MessageKey, string ReasonKey);
}
