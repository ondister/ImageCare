using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Reactive.Linq;

using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.MediaAnnotationsService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.Services;

using Prism.Commands.Ex;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Settings;

public sealed class AnnotationsSettingsViewModel : ViewModelBase
{
    private readonly IConfigurationService _configurationService;
    private readonly IFileDialogService _fileDialogService;
    private readonly IMediaAnnotationsService _annotationsService;
    private readonly SynchronizationContext _synchronizationContext;
    private readonly ILogger _logger;
    private readonly LocalizationManager _localization;
    private readonly Action _saveConfiguration;
    private IDisposable? _annotationsStatusSubscription;
    private string? _selectedMediaSearchRoot;
    private string? _selectedPinnedTag;
    private string _newPinnedTag = string.Empty;
    private string _tagImportStatus = string.Empty;
    private string _annotationsStatus = string.Empty;
    private bool _isAnnotationsBusy;

    public AnnotationsSettingsViewModel(
        IConfigurationService configurationService,
        IFileDialogService fileDialogService,
        IMediaAnnotationsService annotationsService,
        SynchronizationContext synchronizationContext,
        ILogger logger,
        LocalizationManager localization,
        Action saveConfiguration)
    {
        _configurationService = configurationService;
        _fileDialogService = fileDialogService;
        _annotationsService = annotationsService;
        _synchronizationContext = synchronizationContext;
        _logger = logger;
        _localization = localization;
        _saveConfiguration = saveConfiguration;

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
    }

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

    public AsyncDelegateCommand AddMediaSearchRootCommand { get; }

    public DelegateCommand RemoveMediaSearchRootCommand { get; }

    public AsyncDelegateCommand RebuildMediaIndexCommand { get; }

    public DelegateCommand AddPinnedTagCommand { get; }

    public DelegateCommand RemovePinnedTagCommand { get; }

    public AsyncDelegateCommand ImportPinnedTagsCommand { get; }

    public void Open()
    {
        TagImportStatus = string.Empty;
        var configuration = _configurationService.Configuration.Value;
        var changed = NormalizePinnedTags(configuration);
        changed |= NormalizeMediaSearchRoots(configuration);
        if (changed)
        {
            _saveConfiguration();
        }

        MediaSearchRootPaths.Clear();
        foreach (var root in configuration.MediaSearchRootPaths)
        {
            MediaSearchRootPaths.Add(root);
        }

        AnnotationsStatus = _localization["Annotations_StatusReady"];
        _annotationsStatusSubscription?.Dispose();
        _annotationsStatusSubscription = _annotationsService.ReconciliationStatus
            .ObserveOn(_synchronizationContext)
            .Subscribe(ApplyAnnotationsStatus, OnCommandException);
        _ = RefreshAvailableTagsAsync();
    }

    public void Close()
    {
        _annotationsStatusSubscription?.Dispose();
        _annotationsStatusSubscription = null;
        MediaSearchRootPaths.Clear();
        AvailableTags.Clear();
        SelectedMediaSearchRoot = null;
        SelectedPinnedTag = null;
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

        var fullPath = NormalizeRoot(directoryPath);
        var comparer = GetPathComparer();
        if (MediaSearchRootPaths.Contains(fullPath, comparer))
        {
            return;
        }

        MediaSearchRootPaths.Add(fullPath);
        _configurationService.Configuration.Value.MediaSearchRootPaths = MediaSearchRootPaths.ToList();
        _saveConfiguration();
        _ = IndexRootInBackgroundAsync(fullPath);
    }

    private void RemoveMediaSearchRoot()
    {
        if (SelectedMediaSearchRoot is not { } selected)
        {
            return;
        }

        MediaSearchRootPaths.Remove(selected);
        _configurationService.Configuration.Value.MediaSearchRootPaths = MediaSearchRootPaths.ToList();
        _saveConfiguration();
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
            var importedTags = lines.Where(line => !line.TrimStart().StartsWith('#'));
            var configuration = _configurationService.Configuration.Value;
            var existingTags = new MediaAnnotations(0, configuration.PinnedTags).Tags;
            var existingSet = existingTags.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var mergedTags = new MediaAnnotations(0, existingTags.Concat(importedTags)).Tags;
            var addedCount = mergedTags.Count(tag => !existingSet.Contains(tag));

            configuration.PinnedTags = mergedTags.ToList();
            _saveConfiguration();
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

    private static bool NormalizePinnedTags(Configuration configuration)
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

    private static bool NormalizeMediaSearchRoots(Configuration configuration)
    {
        var existing = configuration.MediaSearchRootPaths ?? [];
        var comparer = GetPathComparer();
        var normalized = existing
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(NormalizeRoot)
            .Distinct(comparer)
            .ToList();
        if (normalized.SequenceEqual(existing, comparer))
        {
            return false;
        }

        configuration.MediaSearchRootPaths = normalized;
        return true;
    }

    private void AddPinnedTag()
    {
        var normalized = MediaAnnotations.NormalizeTag(NewPinnedTag);
        if (normalized.Length == 0)
        {
            return;
        }

        var pinned = _configurationService.Configuration.Value.PinnedTags ??= new List<string>();
        if (!pinned.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            pinned.Add(normalized);
            pinned.Sort(StringComparer.Create(_localization.Culture, ignoreCase: true));
            _saveConfiguration();
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

        var pinned = _configurationService.Configuration.Value.PinnedTags ??= new List<string>();
        pinned.RemoveAll(tag => tag.Equals(selected, StringComparison.OrdinalIgnoreCase));
        _saveConfiguration();
        SelectedPinnedTag = null;
        _ = RefreshAvailableTagsAsync();
    }

    private Task RefreshAvailableTagsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var configuration = _configurationService.Configuration.Value;
            if (NormalizePinnedTags(configuration))
            {
                _saveConfiguration();
            }

            var tags = (configuration.PinnedTags ?? [])
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

            if (SelectedPinnedTag != null
                && !AvailableTags.Contains(SelectedPinnedTag, StringComparer.OrdinalIgnoreCase))
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

        return Task.CompletedTask;
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

    private static string NormalizeRoot(string root) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

    private static StringComparer GetPathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private void OnCommandException(Exception exception) =>
        _logger.Error(exception, "Command execution failed");
}
