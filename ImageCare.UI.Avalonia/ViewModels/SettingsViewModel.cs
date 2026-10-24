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

using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.MediaAnnotationsService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.Services;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using MapsterMapper;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly IConfigurationService _configService;
    private readonly IFileDialogService _fileDialogService;
    private readonly IMapper _mapper;
    private readonly ILogger _logger;
    private readonly LocalizationManager _localization;
    private readonly IMediaAnnotationsService _annotationsService;
    private readonly ImageCareMcpServerController _mcpServerController;
    private readonly SynchronizationContext _synchronizationContext;
    private IDisposable? _annotationsStatusSubscription;

    private string _newExtension = string.Empty;
    private string _newApplicationPath = string.Empty;
    private FileApplicationAssociationViewModel? _selectedAssociation;
    private ApplicationChoiceViewModel? _selectedApplicationChoice;
    private LanguageOption? _selectedLanguage;
    private DragDropModifierOption? _selectedDragDropModifier;
    private bool _copyOnDragDropByDefault;
    private bool _folderTagsEnabled;
    private bool _folderTagApplicationLogEnabled;
    private bool _restoreLastFoldersOnStartup;
    private bool _safeDeletionEnabled;
    private bool _mcpServerEnabled;
    private int _mcpServerPort = 5277;
    private string _mcpServerStatus = string.Empty;
    private string _safeDeletionDirectoryPath = string.Empty;
    private int _safeDeletionMaxFileCount = 100;
    private string? _selectedMediaSearchRoot;
    private string? _selectedPinnedTag;
    private string _newPinnedTag = string.Empty;
    private string _tagImportStatus = string.Empty;
    private string _annotationsStatus = string.Empty;
    private bool _isAnnotationsBusy;

    public SettingsViewModel(
        IConfigurationService configurationService,
        IFileDialogService fileDialogService,
        IMediaAnnotationsService annotationsService,
        ImageCareMcpServerController mcpServerController,
        SynchronizationContext synchronizationContext,
        IMapper mapper,
        ILogger logger,
        LocalizationManager localization)
    {
        _configService = configurationService;
        _fileDialogService = fileDialogService;
        _annotationsService = annotationsService;
        _mcpServerController = mcpServerController;
        _synchronizationContext = synchronizationContext;
        _mapper = mapper;
        _logger = logger;
        _localization = localization;

        SupportedExtensions = MediaFormat.GetSupportedExtensions()
            .OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        AddAssociationCommand = CreateCommand(AddAssociation, CanAddAssociation, OnCommandException)
            .ObservesProperty(() => NewExtension)
            .ObservesProperty(() => SelectedApplicationChoice)
            .ObservesProperty(() => NewApplicationPath);
        RemoveAssociationCommand = CreateCommand(RemoveAssociation, CanRemoveAssociation, OnCommandException)
            .ObservesProperty(() => SelectedAssociation);
        ChooseApplicationCommand = CreateAsyncCommand(
                ChooseApplicationAsync,
                () => IsCustomApplicationSelected,
                OnCommandException)
            .ObservesProperty(() => IsCustomApplicationSelected);
        RestoreRecommendedApplicationsCommand = CreateCommand(
            RestoreRecommendedApplications,
            () => true,
            OnCommandException);
        ChooseSafeDeletionDirectoryCommand = CreateAsyncCommand(
            ChooseSafeDeletionDirectoryAsync,
            OnCommandException);
        AddMediaSearchRootCommand = CreateAsyncCommand(AddMediaSearchRootAsync, OnCommandException);
        RemoveMediaSearchRootCommand = CreateCommand(RemoveMediaSearchRoot, () => SelectedMediaSearchRoot != null)
            .ObservesProperty(() => SelectedMediaSearchRoot);
        RebuildMediaIndexCommand = CreateAsyncCommand(RebuildMediaIndexAsync, () => !IsAnnotationsBusy, OnCommandException)
            .ObservesProperty(() => IsAnnotationsBusy);
        AddPinnedTagCommand = CreateCommand(AddPinnedTag, () => !string.IsNullOrWhiteSpace(NewPinnedTag))
            .ObservesProperty(() => NewPinnedTag);
        RemovePinnedTagCommand = CreateCommand(RemovePinnedTag, () => SelectedPinnedTag != null)
            .ObservesProperty(() => SelectedPinnedTag);
        ImportPinnedTagsCommand = CreateAsyncCommand(ImportPinnedTagsAsync, OnCommandException);

        _newExtension = SupportedExtensions.FirstOrDefault(extension =>
            extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase))
            ?? SupportedExtensions.FirstOrDefault()
            ?? string.Empty;
        RebuildDragDropModifierOptions(DragDropModifierKey.Control);
        RebuildApplicationChoices();
    }

    public string Title => _localization["Settings_Title"];

    public IReadOnlyList<LanguageOption> AvailableLanguages => _localization.SupportedLanguages;

    public IReadOnlyList<string> SupportedExtensions { get; }

    public ObservableCollection<DragDropModifierOption> DragDropModifierOptions { get; } = new();

    public ObservableCollection<ApplicationChoiceViewModel> ApplicationChoices { get; } = new();

    public ObservableCollection<FileApplicationAssociationViewModel> Associations { get; } = new();

    public bool RestoreLastFoldersOnStartup
    {
        get => _restoreLastFoldersOnStartup;
        set
        {
            if (!SetProperty(ref _restoreLastFoldersOnStartup, value))
            {
                return;
            }

            _configService.Configuration.Value.RestoreLastFoldersOnStartup = value;
            SaveConfiguration();
        }
    }

    public DragDropModifierOption? SelectedDragDropModifier
    {
        get => _selectedDragDropModifier;
        set
        {
            if (!SetProperty(ref _selectedDragDropModifier, value) || value == null)
            {
                return;
            }

            _configService.Configuration.Value.DragDropModifier = value.Modifier;
            SaveConfiguration();
        }
    }

    public bool CopyOnDragDropByDefault
    {
        get => _copyOnDragDropByDefault;
        set
        {
            if (!SetProperty(ref _copyOnDragDropByDefault, value))
            {
                return;
            }

            _configService.Configuration.Value.CopyOnDragDropByDefault = value;
            SaveConfiguration();
        }
    }

    public bool SafeDeletionEnabled
    {
        get => _safeDeletionEnabled;
        set
        {
            if (!SetProperty(ref _safeDeletionEnabled, value))
            {
                return;
            }

            _configService.Configuration.Value.SafeDeletionEnabled = value;
            SaveConfiguration();
        }
    }

    public bool FolderTagsEnabled
    {
        get => _folderTagsEnabled;
        set
        {
            if (!SetProperty(ref _folderTagsEnabled, value))
            {
                return;
            }

            _configService.Configuration.Value.FolderTagsEnabled = value;
            SaveConfiguration();
        }
    }

    public bool FolderTagApplicationLogEnabled
    {
        get => _folderTagApplicationLogEnabled;
        set
        {
            if (!SetProperty(ref _folderTagApplicationLogEnabled, value))
            {
                return;
            }

            _configService.Configuration.Value.FolderTagApplicationLogEnabled = value;
            SaveConfiguration();
        }
    }

    public bool McpServerEnabled
    {
        get => _mcpServerEnabled;
        set
        {
            if (!SetProperty(ref _mcpServerEnabled, value))
            {
                return;
            }

            _configService.Configuration.Value.McpServerEnabled = value;
            SaveConfiguration();
            _ = ApplyMcpServerStateAsync();
        }
    }

    public int McpServerPort
    {
        get => _mcpServerPort;
        set
        {
            var normalized = Math.Clamp(value, 1024, 65535);
            if (!SetProperty(ref _mcpServerPort, normalized))
            {
                return;
            }

            _configService.Configuration.Value.McpServerPort = normalized;
            SaveConfiguration();
            if (_mcpServerEnabled)
            {
                _ = ApplyMcpServerStateAsync();
            }
        }
    }

    public string McpServerStatus
    {
        get => _mcpServerStatus;
        private set => SetProperty(ref _mcpServerStatus, value);
    }

    public string SafeDeletionDirectoryPath
    {
        get => _safeDeletionDirectoryPath;
        set
        {
            var normalizedValue = value ?? string.Empty;
            if (!SetProperty(ref _safeDeletionDirectoryPath, normalizedValue))
            {
                return;
            }

            _configService.Configuration.Value.SafeDeletionDirectoryPath = normalizedValue;
            SaveConfiguration();
        }
    }

    public int SafeDeletionMaxFileCount
    {
        get => _safeDeletionMaxFileCount;
        set
        {
            var normalizedValue = Math.Clamp(value, 1, 10_000);
            if (!SetProperty(ref _safeDeletionMaxFileCount, normalizedValue))
            {
                return;
            }

            _configService.Configuration.Value.SafeDeletionMaxFileCount = normalizedValue;
            SaveConfiguration();
        }
    }

    public LanguageOption? SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (!SetProperty(ref _selectedLanguage, value) || value == null)
            {
                return;
            }

            _localization.SetCulture(value.Name);
            _configService.Configuration.Value.UiCultureName = value.Name;
            SaveConfiguration();
            RebuildDragDropModifierOptions(_configService.Configuration.Value.DragDropModifier);
            RebuildApplicationChoices();
            RaisePropertyChanged(nameof(Title));
        }
    }

    public string NewExtension
    {
        get => _newExtension;
        set
        {
            if (!SetProperty(ref _newExtension, value ?? string.Empty))
            {
                return;
            }

            RebuildApplicationChoices();
        }
    }

    public string NewApplicationPath
    {
        get => _newApplicationPath;
        private set => SetProperty(ref _newApplicationPath, value);
    }

    public ApplicationChoiceViewModel? SelectedApplicationChoice
    {
        get => _selectedApplicationChoice;
        set
        {
            if (!SetProperty(ref _selectedApplicationChoice, value))
            {
                return;
            }

            if (value?.IsCustom != true)
            {
                NewApplicationPath = string.Empty;
            }

            RaisePropertyChanged(nameof(IsCustomApplicationSelected));
        }
    }

    public bool IsCustomApplicationSelected => SelectedApplicationChoice?.IsCustom == true;

    public FileApplicationAssociationViewModel? SelectedAssociation
    {
        get => _selectedAssociation;
        set => SetProperty(ref _selectedAssociation, value);
    }

    public ICommand AddAssociationCommand { get; }

    public ICommand RemoveAssociationCommand { get; }

    public ICommand ChooseApplicationCommand { get; }

    public ICommand RestoreRecommendedApplicationsCommand { get; }

    public ICommand ChooseSafeDeletionDirectoryCommand { get; }

    public ObservableCollection<string> MediaSearchRootPaths { get; } = new();

    public ObservableCollection<string> AvailableTags { get; } = new();

    public string? SelectedMediaSearchRoot
    {
        get => _selectedMediaSearchRoot;
        set => SetProperty(ref _selectedMediaSearchRoot, value);
    }

    public string? SelectedPinnedTag
    {
        get => _selectedPinnedTag;
        set => SetProperty(ref _selectedPinnedTag, value);
    }

    public string NewPinnedTag
    {
        get => _newPinnedTag;
        set => SetProperty(ref _newPinnedTag, value ?? string.Empty);
    }

    public string TagImportStatus
    {
        get => _tagImportStatus;
        private set => SetProperty(ref _tagImportStatus, value);
    }

    public string AnnotationsStatus
    {
        get => _annotationsStatus;
        private set => SetProperty(ref _annotationsStatus, value);
    }

    public bool IsAnnotationsBusy
    {
        get => _isAnnotationsBusy;
        private set => SetProperty(ref _isAnnotationsBusy, value);
    }

    public ICommand AddMediaSearchRootCommand { get; }

    public ICommand RemoveMediaSearchRootCommand { get; }

    public ICommand RebuildMediaIndexCommand { get; }

    public ICommand AddPinnedTagCommand { get; }

    public ICommand RemovePinnedTagCommand { get; }

    public ICommand ImportPinnedTagsCommand { get; }

    public void Close()
    {
        try
        {
            _annotationsStatusSubscription?.Dispose();
            _annotationsStatusSubscription = null;
            Associations.Clear();
            MediaSearchRootPaths.Clear();
            AvailableTags.Clear();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during SettingsViewModel disposal");
        }
    }

    public void Open()
    {
        try
        {
            var configuration = _configService.Configuration.Value;
            TagImportStatus = string.Empty;
            if (NormalizePinnedTags(configuration))
            {
                SaveConfiguration();
            }

            _selectedLanguage = AvailableLanguages.FirstOrDefault(language =>
                string.Equals(language.Name, _localization.Culture.Name, StringComparison.OrdinalIgnoreCase));
            RaisePropertyChanged(nameof(SelectedLanguage));

            _restoreLastFoldersOnStartup = configuration.RestoreLastFoldersOnStartup;
            RaisePropertyChanged(nameof(RestoreLastFoldersOnStartup));

            RebuildDragDropModifierOptions(configuration.DragDropModifier);

            _copyOnDragDropByDefault = configuration.CopyOnDragDropByDefault;
            RaisePropertyChanged(nameof(CopyOnDragDropByDefault));

            _safeDeletionEnabled = configuration.SafeDeletionEnabled;
            RaisePropertyChanged(nameof(SafeDeletionEnabled));

            _folderTagsEnabled = configuration.FolderTagsEnabled;
            RaisePropertyChanged(nameof(FolderTagsEnabled));

            _folderTagApplicationLogEnabled = configuration.FolderTagApplicationLogEnabled;
            RaisePropertyChanged(nameof(FolderTagApplicationLogEnabled));

            _mcpServerEnabled = configuration.McpServerEnabled;
            RaisePropertyChanged(nameof(McpServerEnabled));

            _mcpServerPort = Math.Clamp(configuration.McpServerPort, 1024, 65535);
            RaisePropertyChanged(nameof(McpServerPort));
            RefreshMcpServerStatus();

            _safeDeletionDirectoryPath = configuration.SafeDeletionDirectoryPath;
            RaisePropertyChanged(nameof(SafeDeletionDirectoryPath));

            _safeDeletionMaxFileCount = Math.Clamp(
                configuration.SafeDeletionMaxFileCount,
                1,
                10_000);
            RaisePropertyChanged(nameof(SafeDeletionMaxFileCount));

            MediaSearchRootPaths.Clear();
            foreach (var root in configuration.MediaSearchRootPaths ?? [])
            {
                MediaSearchRootPaths.Add(root);
            }

            AnnotationsStatus = _localization["Annotations_StatusReady"];
            _annotationsStatusSubscription?.Dispose();
            _annotationsStatusSubscription = _annotationsService.ReconciliationStatus
                .ObserveOn(_synchronizationContext)
                .Subscribe(ApplyAnnotationsStatus, OnCommandException);
            _ = RefreshAvailableTagsAsync();

            LoadAssociations();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during dialog open");
        }
    }

    private void AddAssociation()
    {
        var choice = SelectedApplicationChoice;
        if (choice == null)
        {
            return;
        }

        var applicationPath = choice.IsCustom ? NewApplicationPath : choice.ApplicationPath;
        var name = choice.IsCustom
            ? Path.GetFileNameWithoutExtension(applicationPath)
            : choice.DisplayName;

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var associations = _configService.Configuration.Value.ApplicationAssociationPairs;
        if (associations.Any(association =>
                association.FileExtension.Equals(NewExtension, StringComparison.OrdinalIgnoreCase)
                && association.LaunchMode == choice.LaunchMode
                && association.ApplicationPath.Equals(applicationPath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        associations.Add(
            new FileApplicationAssociation
            {
                FileExtension = NewExtension,
                Name = name,
                ApplicationPath = applicationPath,
                LaunchMode = choice.LaunchMode
            });

        SaveConfiguration();
        NewApplicationPath = string.Empty;
        LoadAssociations();
    }

    private bool CanAddAssociation()
    {
        var choice = SelectedApplicationChoice;
        return !string.IsNullOrWhiteSpace(NewExtension)
            && choice != null
            && (!choice.IsCustom || !string.IsNullOrWhiteSpace(NewApplicationPath));
    }

    private void RemoveAssociation()
    {
        try
        {
            if (SelectedAssociation == null)
            {
                return;
            }

            var associationToRemove = _configService.Configuration.Value.ApplicationAssociationPairs
                .FirstOrDefault(association =>
                    association.FileExtension.Equals(SelectedAssociation.FileExtension, StringComparison.OrdinalIgnoreCase)
                    && association.Name.Equals(SelectedAssociation.Name, StringComparison.OrdinalIgnoreCase)
                    && association.LaunchMode == SelectedAssociation.LaunchMode
                    && association.ApplicationPath.Equals(
                        SelectedAssociation.ApplicationPath,
                        StringComparison.OrdinalIgnoreCase));

            if (associationToRemove == null)
            {
                return;
            }

            _configService.Configuration.Value.ApplicationAssociationPairs.Remove(associationToRemove);
            SaveConfiguration();
            LoadAssociations();
            SelectedAssociation = null;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to remove file association");
        }
    }

    private bool CanRemoveAssociation()
    {
        return SelectedAssociation != null;
    }

    private async Task ChooseApplicationAsync(
        IProgress<int> progress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var filePath = await _fileDialogService.ShowOpenApplicationDialogAsync(
                _localization["Settings_SelectApplication"]);

            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                NewApplicationPath = filePath;
            }
        }
        catch (OperationCanceledException)
        {
            // The application picker was cancelled.
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to select an external application");
        }
    }

    private async Task ChooseSafeDeletionDirectoryAsync(
        IProgress<int> progress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var directoryPath = await _fileDialogService.ShowOpenFolderDialogAsync(
                _localization["Settings_SelectSafeDeletionFolder"]);

            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                SafeDeletionDirectoryPath = directoryPath;
            }
        }
        catch (OperationCanceledException)
        {
            // The folder picker was cancelled.
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to select the safe deletion directory");
        }
    }

    private async Task AddMediaSearchRootAsync(
        IProgress<int> progress,
        CancellationToken cancellationToken = default)
    {
        var directoryPath = await _fileDialogService.ShowOpenFolderDialogAsync(
            _localization["Annotations_SelectRoot"]);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return;
        }

        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath));
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        if (MediaSearchRootPaths.Contains(fullPath, comparer))
        {
            return;
        }

        MediaSearchRootPaths.Add(fullPath);
        _configService.Configuration.Value.MediaSearchRootPaths = MediaSearchRootPaths.ToList();
        SaveConfiguration();
        _ = IndexRootInBackgroundAsync(fullPath);
    }

    private void RemoveMediaSearchRoot()
    {
        if (SelectedMediaSearchRoot is not { } selected)
        {
            return;
        }

        MediaSearchRootPaths.Remove(selected);
        _configService.Configuration.Value.MediaSearchRootPaths = MediaSearchRootPaths.ToList();
        SaveConfiguration();
        SelectedMediaSearchRoot = null;
        AnnotationsStatus = _localization["Annotations_RootRemoved"];
    }

    private async Task RebuildMediaIndexAsync(
        IProgress<int> progress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (MediaSearchRootPaths.Count == 0)
            {
                AnnotationsStatus = _localization["Search_NoRoots"];
                return;
            }

            IsAnnotationsBusy = true;
            AnnotationsStatus = _localization["Annotations_Rebuilding"];
            await _annotationsService.RebuildRootsAsync(MediaSearchRootPaths, cancellationToken);
            await RefreshAvailableTagsAsync(cancellationToken);
        }
        finally
        {
            IsAnnotationsBusy = false;
        }
    }

    private async Task IndexRootInBackgroundAsync(string root)
    {
        try
        {
            await _annotationsService.RegisterDirectoryAsync(root);
            await RefreshAvailableTagsAsync();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to index media annotations root {Root}", root);
        }
    }

    private async Task ImportPinnedTagsAsync(
        IProgress<int> progress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var filePath = await _fileDialogService.ShowOpenFileDialogAsync(
                _localization["Annotations_SelectTagsFile"],
                (_localization["Annotations_TextFiles"], ["*.txt"]),
                (_localization["Settings_AllFiles"], ["*"]));
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
            var importedTags = lines
                .Where(line => !line.TrimStart().StartsWith('#'));
            var configuration = _configService.Configuration.Value;
            var existingTags = new MediaAnnotations(0, configuration.PinnedTags).Tags;
            var existingSet = existingTags.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var mergedTags = new MediaAnnotations(0, existingTags.Concat(importedTags)).Tags;
            var addedCount = mergedTags.Count(tag => !existingSet.Contains(tag));

            configuration.PinnedTags = mergedTags.ToList();
            SaveConfiguration();
            await RefreshAvailableTagsAsync(cancellationToken);
            TagImportStatus = _localization.Get("Annotations_TagsImported", addedCount);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to import annotation tags");
            TagImportStatus = _localization.Get("Annotations_TagsImportFailed", exception.Message);
        }
    }

    private bool NormalizePinnedTags(Configuration configuration)
    {
        var existing = configuration.PinnedTags ?? [];
        var normalized = new MediaAnnotations(0, existing).Tags.ToList();
        if (normalized.SequenceEqual(existing, StringComparer.Ordinal))
        {
            return false;
        }

        configuration.PinnedTags = normalized;
        return true;
    }

    private void AddPinnedTag()
    {
        var normalized = MediaAnnotations.NormalizeTag(NewPinnedTag);
        if (normalized.Length == 0)
        {
            return;
        }

        var pinned = _configService.Configuration.Value.PinnedTags ??= new List<string>();
        if (!pinned.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            pinned.Add(normalized);
            pinned.Sort(StringComparer.Create(_localization.Culture, ignoreCase: true));
            SaveConfiguration();
        }

        NewPinnedTag = string.Empty;
        _ = RefreshAvailableTagsAsync();
    }

    private void RemovePinnedTag()
    {
        if (SelectedPinnedTag is not { } selected)
        {
            return;
        }

        var pinned = _configService.Configuration.Value.PinnedTags ??= new List<string>();
        pinned.RemoveAll(tag => tag.Equals(selected, StringComparison.OrdinalIgnoreCase));
        SaveConfiguration();
        SelectedPinnedTag = null;
        _ = RefreshAvailableTagsAsync();
    }

    private async Task RefreshAvailableTagsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var configuration = _configService.Configuration.Value;
            if (NormalizePinnedTags(configuration))
            {
                SaveConfiguration();
            }

            var pinned = configuration.PinnedTags ?? [];
            var tags = pinned
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(MediaAnnotations.NormalizeTag)
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(tag => tag, StringComparer.Create(_localization.Culture, ignoreCase: true))
                .ThenBy(tag => tag, StringComparer.Ordinal)
                .ToArray();

            AvailableTags.Clear();
            foreach (var tag in tags)
            {
                AvailableTags.Add(tag);
            }

            if (SelectedPinnedTag != null && !AvailableTags.Contains(SelectedPinnedTag, StringComparer.OrdinalIgnoreCase))
            {
                SelectedPinnedTag = null;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to load annotation tag vocabulary");
        }
    }

    private void ApplyAnnotationsStatus(AnnotationsReconciliationStatus status)
    {
        IsAnnotationsBusy = status.State == AnnotationsReconciliationState.Running;
        AnnotationsStatus = status.State switch
        {
            AnnotationsReconciliationState.Running =>
                _localization.Get("Annotations_StatusRunning", status.ProcessedFiles),
            AnnotationsReconciliationState.Completed =>
                _localization.Get("Annotations_StatusCompleted", status.ProcessedFiles),
            AnnotationsReconciliationState.Cancelled => _localization["Annotations_StatusCancelled"],
            AnnotationsReconciliationState.Failed =>
                _localization.Get("Annotations_StatusFailed", status.Error ?? string.Empty),
            _ => _localization["Annotations_StatusReady"]
        };
    }

    private void RestoreRecommendedApplications()
    {
        if (!DefaultFileApplicationAssociations.AddMissing(_configService.Configuration.Value))
        {
            return;
        }

        SaveConfiguration();
        LoadAssociations();
    }

    private void RebuildDragDropModifierOptions(DragDropModifierKey selectedModifier)
    {
        DragDropModifierOptions.Clear();
        DragDropModifierOptions.Add(
            new DragDropModifierOption(_localization["Settings_ModifierControl"], DragDropModifierKey.Control));
        DragDropModifierOptions.Add(
            new DragDropModifierOption(_localization["Settings_ModifierAlt"], DragDropModifierKey.Alt));
        DragDropModifierOptions.Add(
            new DragDropModifierOption(_localization["Settings_ModifierShift"], DragDropModifierKey.Shift));
        DragDropModifierOptions.Add(
            new DragDropModifierOption(_localization["Settings_ModifierMeta"], DragDropModifierKey.Meta));

        _selectedDragDropModifier = DragDropModifierOptions.FirstOrDefault(option =>
            option.Modifier == selectedModifier)
            ?? DragDropModifierOptions[0];
        RaisePropertyChanged(nameof(SelectedDragDropModifier));
    }

    private void RebuildApplicationChoices()
    {
        var previousMode = SelectedApplicationChoice?.LaunchMode;
        var wasCustom = SelectedApplicationChoice?.IsCustom == true;

        ApplicationChoices.Clear();
        ApplicationChoices.Add(
            new ApplicationChoiceViewModel(
                _localization["Settings_SystemDefaultApplication"],
                ApplicationLaunchMode.SystemDefault));
        ApplicationChoices.Add(
            new ApplicationChoiceViewModel(
                OperatingSystem.IsMacOS()
                    ? _localization["Settings_ShowInFinder"]
                    : _localization["Settings_ShowInFileExplorer"],
                ApplicationLaunchMode.FileManager));

        if (OperatingSystem.IsMacOS())
        {
            var isVideo = NewExtension.Equals(".mp4", StringComparison.OrdinalIgnoreCase);
            ApplicationChoices.Add(
                isVideo
                    ? new ApplicationChoiceViewModel(
                        "QuickTime Player",
                        ApplicationLaunchMode.MacApplication,
                        "/System/Applications/QuickTime Player.app")
                    : new ApplicationChoiceViewModel(
                        "Preview",
                        ApplicationLaunchMode.MacApplication,
                        "/System/Applications/Preview.app"));
        }

        ApplicationChoices.Add(
            new ApplicationChoiceViewModel(
                _localization["Settings_CustomApplication"],
                OperatingSystem.IsMacOS()
                    ? ApplicationLaunchMode.MacApplication
                    : ApplicationLaunchMode.Executable,
                isCustom: true));

        SelectedApplicationChoice = ApplicationChoices.FirstOrDefault(choice =>
            choice.LaunchMode == previousMode && choice.IsCustom == wasCustom)
            ?? ApplicationChoices.FirstOrDefault();
    }

    private void LoadAssociations()
    {
        try
        {
            Associations.Clear();
            var orderedList = _configService.Configuration.Value.ApplicationAssociationPairs
                .OrderBy(pair => pair.FileExtension, StringComparer.OrdinalIgnoreCase)
                .ThenBy(pair => pair.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var association in orderedList)
            {
                try
                {
                    Associations.Add(_mapper.Map<FileApplicationAssociationViewModel>(association));
                }
                catch (Exception ex)
                {
                    _logger.Error(
                        ex,
                        "Failed to map association: {Extension} - {Name}",
                        association.FileExtension,
                        association.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load associations");
        }
    }

    private void SaveConfiguration()
    {
        try
        {
            _configService.SaveConfiguration();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save configuration");
        }
    }

    private async Task ApplyMcpServerStateAsync()
    {
        try
        {
            if (_mcpServerEnabled)
            {
                if (_mcpServerController.IsRunning)
                {
                    await _mcpServerController.StopAsync().ConfigureAwait(false);
                }

                await _mcpServerController
                    .StartAsync(_mcpServerPort)
                    .ConfigureAwait(false);
            }
            else
            {
                await _mcpServerController.StopAsync().ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to apply MCP server settings");
        }
        finally
        {
            _synchronizationContext.Post(_ => RefreshMcpServerStatus(), null);
        }
    }

    private void RefreshMcpServerStatus()
    {
        if (!_mcpServerEnabled)
        {
            McpServerStatus = _localization["Settings_McpServerStatusStopped"];
            return;
        }

        if (_mcpServerController.IsRunning && !string.IsNullOrWhiteSpace(_mcpServerController.EndpointUrl))
        {
            McpServerStatus = _localization.Get("Settings_McpServerStatusRunning", _mcpServerController.EndpointUrl);
            return;
        }

        McpServerStatus = string.IsNullOrWhiteSpace(_mcpServerController.LastError)
            ? _localization["Settings_McpServerStatusStarting"]
            : _localization.Get("Settings_McpServerStatusFailed", _mcpServerController.LastError);
    }

    private void OnCommandException(Exception exception)
    {
        _logger.Error(exception, "Command execution failed");
    }
}
