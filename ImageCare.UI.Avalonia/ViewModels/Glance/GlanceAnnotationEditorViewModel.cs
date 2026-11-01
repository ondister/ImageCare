using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Domain.Notification;
using ImageCare.Core.Services.MediaAnnotationsService;
using ImageCare.Core.Services.NotificationService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Glance;

/// <summary>
/// Drives the rating/tag editor shown above the media grid. Edits are applied to every
/// selected annotatable image through <see cref="IMediaAnnotationsService"/>.
/// </summary>
internal sealed class GlanceAnnotationEditorViewModel : ViewModelBase
{
    private readonly GlanceSelectionViewModel _selection;
    private readonly IMediaAnnotationsService _annotationsService;
    private readonly INotificationService _notificationService;
    private readonly LocalizationManager _localization;
    private readonly ILogger _logger;
    private readonly GlanceOperationViewModel _operations;
    private readonly ObservableCollection<string> _selectionTags = [];

    private int _selectedRating = -1;
    private bool _hasMixedRating;
    private string _newTag = string.Empty;

    public GlanceAnnotationEditorViewModel(
        GlanceSelectionViewModel selection,
        IMediaAnnotationsService annotationsService,
        INotificationService notificationService,
        LocalizationManager localization,
        ILogger logger,
        GlanceOperationViewModel operations)
    {
        _selection = selection;
        _annotationsService = annotationsService;
        _notificationService = notificationService;
        _localization = localization;
        _logger = logger;
        _operations = operations;

        ApplyRatingCommand = CreateAsyncCommand(ApplyRatingToSelectionAsync);
        AddTagCommand = CreateAsyncCommand(AddTagToSelectionAsync);
        RemoveTagCommand = CreateAsyncCommand<string>(RemoveTagFromSelectionAsync);

        _selection.SelectionChanged += OnSelectionChanged;
        _operations.PropertyChanged += OnOperationsPropertyChanged;
    }

    public ICommand ApplyRatingCommand { get; }

    public ICommand AddTagCommand { get; }

    public ICommand RemoveTagCommand { get; }

    public ObservableCollection<string> SelectionTags => _selectionTags;

    public bool HasSelectionTags => SelectionTags.Count > 0;

    public IReadOnlyList<int> RatingOptions { get; } = [0, 1, 2, 3, 4, 5];

    public int SelectedRating
    {
        get => _selectedRating;
        set
        {
            var clamped = Math.Clamp(value, -1, 5);
            if (!SetProperty(ref _selectedRating, clamped))
            {
                return;
            }

            if (clamped >= 0)
            {
                HasMixedRating = false;
                if (CanEditSelectionAnnotations)
                {
                    _ = ApplyRatingToSelectionAsync();
                }
            }

            RaisePropertyChanged(nameof(CanApplyRating));
        }
    }

    public bool HasMixedRating
    {
        get => _hasMixedRating;
        private set => SetProperty(ref _hasMixedRating, value);
    }

    public string NewTag
    {
        get => _newTag;
        set => SetProperty(ref _newTag, value ?? string.Empty);
    }

    public bool CanEditSelectionAnnotations =>
        _selection.AnnotatableSelectionCount > 0 && !_operations.IsOverviewOperationRunning;

    public bool CanApplyRating => CanEditSelectionAnnotations && SelectedRating >= 0;

    private void OnSelectionChanged(object? sender, EventArgs args) => RefreshEditor();

    private void OnOperationsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(GlanceOperationViewModel.IsOverviewOperationRunning))
        {
            return;
        }

        RaisePropertyChanged(nameof(CanEditSelectionAnnotations));
        RaisePropertyChanged(nameof(CanApplyRating));
    }

    private void RefreshEditor()
    {
        RaisePropertyChanged(nameof(CanEditSelectionAnnotations));
        RaisePropertyChanged(nameof(CanApplyRating));

        var annotatable = _selection.SelectedPreviews
            .Where(preview => preview.MediaFormat.SupportsAnnotations)
            .ToArray();
        if (annotatable.Length == 0)
        {
            UpdateSelectedRatingEditor(-1, hasMixedRating: false);
            SelectionTags.Clear();
            RaisePropertyChanged(nameof(HasSelectionTags));
            return;
        }

        var firstRating = annotatable[0].Rating;
        var hasMixedRating = annotatable.Any(preview => preview.Rating != firstRating);
        UpdateSelectedRatingEditor(hasMixedRating ? -1 : firstRating, hasMixedRating);

        var tags = annotatable
            .SelectMany(preview => preview.Tags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.Create(_localization.Culture, ignoreCase: true))
            .ThenBy(tag => tag, StringComparer.Ordinal)
            .ToArray();
        SelectionTags.Clear();
        foreach (var tag in tags)
        {
            SelectionTags.Add(tag);
        }

        RaisePropertyChanged(nameof(HasSelectionTags));
    }

    private void UpdateSelectedRatingEditor(int rating, bool hasMixedRating)
    {
        HasMixedRating = hasMixedRating;
        var normalized = Math.Clamp(rating, -1, 5);
        if (_selectedRating == normalized)
        {
            return;
        }

        _selectedRating = normalized;
        RaisePropertyChanged(nameof(SelectedRating));
        RaisePropertyChanged(nameof(CanApplyRating));
    }

    private Task ApplyRatingToSelectionAsync()
    {
        var rating = SelectedRating;
        return rating < 0
            ? Task.CompletedTask
            : ApplyAnnotationsToSelectionAsync(
                annotations => new MediaAnnotations(rating, annotations.Tags));
    }

    private async Task AddTagToSelectionAsync()
    {
        var tag = MediaAnnotations.NormalizeTag(NewTag);
        if (tag.Length == 0)
        {
            return;
        }

        await ApplyAnnotationsToSelectionAsync(
            annotations => new MediaAnnotations(annotations.Rating, annotations.Tags.Append(tag)));
        NewTag = string.Empty;
    }

    private Task RemoveTagFromSelectionAsync(string tag) =>
        ApplyAnnotationsToSelectionAsync(
            annotations => new MediaAnnotations(
                annotations.Rating,
                annotations.Tags.Where(existing =>
                    !existing.Equals(tag, StringComparison.OrdinalIgnoreCase))));

    private async Task ApplyAnnotationsToSelectionAsync(Func<MediaAnnotations, MediaAnnotations> transform)
    {
        var items = _selection.SelectedPreviews
            .Where(preview => preview.MediaFormat.SupportsAnnotations)
            .ToArray();
        if (items.Length == 0 || _operations.IsOverviewOperationRunning)
        {
            return;
        }

        var cancellation = _operations.BeginAnnotationOperation(
            items.Length,
            _localization["Annotations_StateSaving"]);
        var succeeded = 0;
        var processed = 0;
        var failures = new List<string>();

        try
        {
            foreach (var preview in items)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                try
                {
                    var snapshot = await _annotationsService.GetSnapshotAsync(
                        preview.Url,
                        cancellation.Token);
                    var updated = transform(snapshot.Annotations);
                    var saved = await _annotationsService.SaveAsync(
                        preview.Url,
                        updated,
                        snapshot.FileState,
                        cancellation.Token);
                    preview.ApplyAnnotations(
                        saved.Annotations,
                        _localization["Annotations_StateSaved"],
                        saved);
                    succeeded++;
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failures.Add(exception.Message);
                    _logger.Error(
                        exception,
                        "Failed to update overview annotations for {Path}",
                        preview.Url);
                }
                finally
                {
                    processed++;
                    _operations.ReportProgress(processed);
                }
            }

            var completedStatus = _localization.Get(
                "Overview_AnnotationsCompleted",
                succeeded,
                items.Length);
            _operations.SetStatus(completedStatus);
            if (failures.Count > 0)
            {
                _notificationService.SendNotification(
                    new ErrorNotification(
                        completedStatus,
                        _localization.Get("Overview_AnnotationsFailed", failures.Count, failures[0])));
            }
        }
        catch (OperationCanceledException)
        {
            _operations.SetStatus(_localization["Batch_CancelledTitle"]);
        }
        finally
        {
            _operations.CompleteAnnotationOperation(cancellation);
            RefreshEditor();
        }
    }
}
