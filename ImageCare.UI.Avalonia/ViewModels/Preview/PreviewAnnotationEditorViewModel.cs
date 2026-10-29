using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Domain.Notification;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.MediaAnnotationsService;
using ImageCare.Core.Services.NotificationService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.Services;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Preview;

/// <summary>
/// Drives the rating/tag editor shown above the preview strip. It owns the in-place
/// annotation state of the primary preview but delegates the directory index to
/// <see cref="PreviewAnnotationCache"/>.
/// </summary>
internal sealed class PreviewAnnotationEditorViewModel : ViewModelBase
{
    private readonly PreviewSelectionViewModel _selection;
    private readonly PreviewAnnotationCache _annotationCache;
    private readonly IMediaAnnotationsService _annotationsService;
    private readonly IConfigurationService _configurationService;
    private readonly INotificationService _notificationService;
    private readonly LocalizationManager _localization;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;
    private readonly Func<CancellationToken> _folderToken;

    private CancellationTokenSource? _selectedAnnotationCancellation;
    private MediaPreviewViewModel? _annotationPreview;
    private int _selectedRating;
    private string _newTag = string.Empty;
    private string[] _allAvailableAnnotationTags = [];
    private string _annotationEditorStatus = string.Empty;
    private bool _isSavingAnnotations;
    private bool _isAnnotationLoading;

    public PreviewAnnotationEditorViewModel(
        PreviewSelectionViewModel selection,
        PreviewAnnotationCache annotationCache,
        IMediaAnnotationsService annotationsService,
        IConfigurationService configurationService,
        INotificationService notificationService,
        LocalizationManager localization,
        ILogger logger,
        SynchronizationContext synchronizationContext,
        Func<CancellationToken> folderToken)
    {
        _selection = selection;
        _annotationCache = annotationCache;
        _annotationsService = annotationsService;
        _configurationService = configurationService;
        _notificationService = notificationService;
        _localization = localization;
        _logger = logger;
        _synchronizationContext = synchronizationContext;
        _folderToken = folderToken;

        SaveRatingCommand = CreateAsyncCommand(SaveRatingAsync, () => CanEditAnnotations)
            .ObservesProperty(() => CanEditAnnotations);
        AddTagCommand = CreateAsyncCommand(
                AddTagAsync,
                () => CanEditAnnotations &&
                      !string.IsNullOrWhiteSpace(MediaAnnotations.NormalizeTag(NewTag)))
            .ObservesProperty(() => CanEditAnnotations)
            .ObservesProperty(() => NewTag);
        RemoveTagCommand = CreateAsyncCommand<string>(RemoveTagAsync);

        _annotationCache.AnnotationsApplied += OnAnnotationsApplied;
        _annotationCache.ChangedAnnotationApplied += OnChangedAnnotationApplied;
        _selection.PrimarySelectionChanged += OnPrimarySelectionChanged;
        _selection.SelectionChanged += OnSelectionChanged;
    }

    public ICommand SaveRatingCommand { get; }

    public ICommand AddTagCommand { get; }

    public ICommand RemoveTagCommand { get; }

    public bool HasAnnotatableSelection => _selection.SelectionCount == 1 &&
                                           _selection.SelectedPreview?.MediaFormat.SupportsAnnotations == true;

    public bool CanEditAnnotations => HasAnnotatableSelection &&
                                      !IsSavingAnnotations &&
                                      !IsAnnotationLoading &&
                                      _selection.SelectedPreview?.AnnotationSnapshot != null;

    public bool IsSavingAnnotations
    {
        get => _isSavingAnnotations;
        private set
        {
            if (SetProperty(ref _isSavingAnnotations, value))
            {
                RaisePropertyChanged(nameof(CanEditAnnotations));
                RaisePropertyChanged(nameof(HasAnnotatableSelection));
            }
        }
    }

    public bool IsAnnotationLoading
    {
        get => _isAnnotationLoading;
        private set
        {
            if (SetProperty(ref _isAnnotationLoading, value))
            {
                RaisePropertyChanged(nameof(CanEditAnnotations));
            }
        }
    }

    public int SelectedRating
    {
        get => _selectedRating;
        set
        {
            var clamped = Math.Clamp(value, 0, 5);
            if (!SetProperty(ref _selectedRating, clamped))
            {
                return;
            }

            var preview = _selection.SelectedPreview;
            if (preview != null &&
                ReferenceEquals(preview, _annotationPreview) &&
                preview.MediaFormat.SupportsAnnotations &&
                preview.AnnotationSnapshot != null &&
                !IsSavingAnnotations &&
                !IsAnnotationLoading)
            {
                _ = SaveSelectedAnnotationsAsync(clamped, CurrentTags, CancellationToken.None);
            }
        }
    }

    public string NewTag
    {
        get => _newTag;
        set
        {
            if (SetProperty(ref _newTag, value ?? string.Empty))
            {
                _synchronizationContext.Post(_ => ApplyAvailableAnnotationTagFilter(), null);
            }
        }
    }

    public IReadOnlyList<string> CurrentTags =>
        _selection.SelectedPreview?.Tags ?? Array.Empty<string>();

    public bool HasCurrentTags => CurrentTags.Count > 0;

    public ObservableCollection<string> AvailableAnnotationTags { get; } = new();

    public bool HasAvailableAnnotationTags => AvailableAnnotationTags.Count > 0;

    public string AnnotationEditorStatus
    {
        get => _annotationEditorStatus;
        private set => SetProperty(ref _annotationEditorStatus, value);
    }

    /// <summary>
    /// Called when the primary preview changes. Starts loading the authoritative annotation
    /// snapshot and refreshes the editor state.
    /// </summary>
    public void SelectPreview(MediaPreviewViewModel? preview)
    {
        _selectedAnnotationCancellation?.Cancel();
        SetSelectedAnnotationLoading(false);
        UpdateAnnotationEditor();

        if (preview == null || !preview.MediaFormat.SupportsAnnotations)
        {
            return;
        }

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_folderToken());
        _selectedAnnotationCancellation = cancellation;
        SetSelectedAnnotationLoading(true);
        _ = LoadSelectedAnnotationSnapshotAsync(preview, cancellation);
    }

    /// <summary>Refreshes the editor properties that depend on the selection count.</summary>
    public void RefreshSelectionState()
    {
        RaisePropertyChanged(nameof(CanEditAnnotations));
        RaisePropertyChanged(nameof(HasAnnotatableSelection));
        RaisePropertyChanged(nameof(CurrentTags));
        RaisePropertyChanged(nameof(HasCurrentTags));
    }

    public void CancelPendingLoad()
    {
        _selectedAnnotationCancellation?.Cancel();
        _selectedAnnotationCancellation = null;
        SetSelectedAnnotationLoading(false);
    }

    /// <summary>
    /// Adds whatever is typed in the tag field. Used when the user presses Enter or clicks the add button.
    /// </summary>
    public void AddTagInput()
    {
        var normalized = MediaAnnotations.NormalizeTag(NewTag);
        if (normalized.Length == 0)
        {
            return;
        }

        _ = AddTagValueAsync(normalized);
    }

    public void RemoveTag(string tag) => _ = RemoveTagAsync(tag);

    public void AddExistingTag(string? tag)
    {
        var normalized = MediaAnnotations.NormalizeTag(tag ?? string.Empty);
        if (normalized.Length == 0)
        {
            return;
        }

        _ = AddExistingTagCoreAsync(normalized);
    }

    private void OnAnnotationsApplied(object? sender, EventArgs args) => UpdateAnnotationEditor();

    private void OnPrimarySelectionChanged(object? sender, EventArgs args) =>
        SelectPreview(_selection.SelectedPreview);

    private void OnSelectionChanged(object? sender, EventArgs args) => RefreshSelectionState();

    private void OnChangedAnnotationApplied(MediaPreviewViewModel preview)
    {
        if (ReferenceEquals(preview, _selection.SelectedPreview))
        {
            UpdateAnnotationEditor();
        }
    }

    private async Task AddExistingTagCoreAsync(string normalized)
    {
        await AddTagValueAsync(normalized);
        NewTag = string.Empty;
    }

    private async Task AddTagValueAsync(string normalized)
    {
        var preview = _selection.SelectedPreview;
        if (preview == null ||
            !ReferenceEquals(preview, _annotationPreview) ||
            !preview.MediaFormat.SupportsAnnotations ||
            preview.AnnotationSnapshot == null)
        {
            return;
        }

        await SaveSelectedAnnotationsAsync(SelectedRating, CurrentTags.Append(normalized), CancellationToken.None);
        NewTag = string.Empty;
    }

    private void UpdateAnnotationEditor()
    {
        _annotationPreview = _selection.SelectedPreview;
        var rating = _selection.SelectedPreview?.Rating ?? 0;
        if (_selectedRating != Math.Clamp(rating, 0, 5))
        {
            _selectedRating = Math.Clamp(rating, 0, 5);
            RaisePropertyChanged(nameof(SelectedRating));
        }

        AnnotationEditorStatus = _selection.SelectedPreview?.AnnotationState ?? string.Empty;
        RaisePropertyChanged(nameof(CurrentTags));
        RaisePropertyChanged(nameof(HasCurrentTags));
        RaisePropertyChanged(nameof(CanEditAnnotations));
        RaisePropertyChanged(nameof(HasAnnotatableSelection));
        _ = RefreshAvailableAnnotationTagsAsync();
    }

    private async Task RefreshAvailableAnnotationTagsAsync()
    {
        try
        {
            var indexedTags = await _annotationsService.GetAllTagsAsync();
            var pinnedTags = _configurationService.Configuration.Value.PinnedTags ?? [];
            var tags = pinnedTags.Concat(indexedTags)
                .Select(MediaAnnotations.NormalizeTag)
                .Where(tag => tag.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(tag => tag, StringComparer.Create(_localization.Culture, ignoreCase: true))
                .ThenBy(tag => tag, StringComparer.Ordinal)
                .ToArray();
            _synchronizationContext.Post(
                _ =>
                {
                    _allAvailableAnnotationTags = tags;
                    ApplyAvailableAnnotationTagFilter();
                },
                null);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to load available annotation tags");
        }
    }

    private void ApplyAvailableAnnotationTagFilter()
    {
        try
        {
            var query = MediaAnnotations.NormalizeTag(NewTag ?? string.Empty);
            var source = _allAvailableAnnotationTags ?? Array.Empty<string>();
            var filteredTags = query.Length == 0
                ? source
                : source.Where(tag => MediaAnnotations.MatchesTagQuery(tag, query));

            AvailableAnnotationTags.Clear();
            foreach (var tag in filteredTags)
            {
                AvailableAnnotationTags.Add(tag);
            }

            RaisePropertyChanged(nameof(HasAvailableAnnotationTags));
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to apply annotation tag filter");
            try
            {
                AvailableAnnotationTags.Clear();
            }
            catch
            {
            }
        }
    }

    private Task SaveRatingAsync(IProgress<int> progress, CancellationToken token) =>
        SaveSelectedAnnotationsAsync(SelectedRating, CurrentTags, token);

    private async Task AddTagAsync(IProgress<int> progress, CancellationToken token)
    {
        var normalized = MediaAnnotations.NormalizeTag(NewTag);
        if (normalized.Length == 0)
        {
            return;
        }

        await AddTagValueAsync(normalized);
    }

    private Task RemoveTagAsync(string tag) =>
        SaveSelectedAnnotationsAsync(
            SelectedRating,
            CurrentTags.Where(current => !current.Equals(tag, StringComparison.OrdinalIgnoreCase)),
            CancellationToken.None);

    private async Task SaveSelectedAnnotationsAsync(
        int rating,
        IEnumerable<string> tags,
        CancellationToken token)
    {
        var preview = _selection.SelectedPreview;
        if (preview == null ||
            !preview.MediaFormat.SupportsAnnotations ||
            preview.AnnotationSnapshot == null ||
            IsSavingAnnotations ||
            IsAnnotationLoading)
        {
            return;
        }

        var previous = new MediaAnnotations(preview.Rating, preview.Tags);
        var updated = new MediaAnnotations(rating, tags);
        try
        {
            IsSavingAnnotations = true;
            preview.IsAnnotationsSaving = true;
            preview.ApplyAnnotations(updated, _localization["Annotations_StateSaving"], preview.AnnotationSnapshot);
            UpdateAnnotationEditor();
            var snapshot = await _annotationsService.SaveAsync(
                preview.Url,
                updated,
                preview.AnnotationSnapshot?.FileState,
                token);
            preview.ApplyAnnotations(snapshot.Annotations, _localization["Annotations_StateSaved"], snapshot);
            AnnotationEditorStatus = preview.AnnotationState;
            RaisePropertyChanged(nameof(CurrentTags));
            await RefreshAvailableAnnotationTagsAsync();
        }
        catch (OperationCanceledException)
        {
            preview.ApplyAnnotations(previous, _localization["Annotations_StateCached"], preview.AnnotationSnapshot);
        }
        catch (Exception exception)
        {
            preview.ApplyAnnotations(previous, _localization["Annotations_StateError"], preview.AnnotationSnapshot);
            _logger.Error(exception, "Failed to save annotations for {Path}", preview.Url);
            _notificationService.SendNotification(
                new ErrorNotification(_localization["Annotations_SaveFailed"], exception.Message));
        }
        finally
        {
            preview.IsAnnotationsSaving = false;
            IsSavingAnnotations = false;
            UpdateAnnotationEditor();
        }
    }

    private async Task LoadSelectedAnnotationSnapshotAsync(
        MediaPreviewViewModel preview,
        CancellationTokenSource cancellation)
    {
        try
        {
            var snapshot = await _annotationsService.GetSnapshotAsync(preview.Url, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (ReferenceEquals(preview, _selection.SelectedPreview) &&
                PreviewPathUtility.PathsEqual(preview.Url, snapshot.MediaPath))
            {
                preview.ApplyAnnotations(
                    snapshot.Annotations,
                    _localization["Annotations_StateCurrent"],
                    snapshot);
                UpdateAnnotationEditor();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception) when (!File.Exists(preview.Url))
        {
            // The file was moved or deleted while its annotations were loading.
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to load current annotations for selected image {Path}", preview.Url);
        }
        finally
        {
            if (ReferenceEquals(_selectedAnnotationCancellation, cancellation))
            {
                _selectedAnnotationCancellation = null;
                SetSelectedAnnotationLoading(false);
                UpdateAnnotationEditor();
            }

            cancellation.Dispose();
        }
    }

    private void SetSelectedAnnotationLoading(bool value) => IsAnnotationLoading = value;
}
