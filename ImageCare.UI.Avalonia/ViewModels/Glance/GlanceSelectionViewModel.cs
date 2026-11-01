using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Input;

using Avalonia.Input;

using ImageCare.UI.Avalonia.Controls;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.Services;
using ImageCare.UI.Avalonia.ViewModels.Domain;

namespace ImageCare.UI.Avalonia.ViewModels.Glance;

/// <summary>
/// Owns the multi-selection state of the folder overview. The overview uses desktop
/// selection semantics: click replaces, Ctrl/Command toggles and Shift selects a range.
/// </summary>
internal sealed class GlanceSelectionViewModel : ViewModelBase
{
    private readonly LocalizationManager _localization;
    private readonly GlanceOperationViewModel _operations;
    private readonly AdaptiveHelpContextService _helpContext;
    private readonly List<GlanceMediaPreviewViewModel> _selectedPreviews = [];

    private ObservableCollection<GlanceMediaPreviewViewModel>? _previews;
    private GlanceMediaPreviewViewModel? _selectedPreview;
    private GlanceMediaPreviewViewModel? _selectionAnchor;

    public GlanceSelectionViewModel(
        LocalizationManager localization,
        GlanceOperationViewModel operations,
        AdaptiveHelpContextService helpContext)
    {
        _localization = localization;
        _operations = operations;
        _helpContext = helpContext;
        SelectPreviewCommand = CreateCommand<MediaPreviewSelectionRequest>(HandlePreviewSelection);
        ClearSelectionCommand = CreateCommand(Clear);
    }

    /// <summary>Raised when the selected set or the primary preview changes.</summary>
    public event EventHandler? SelectionChanged;

    public ICommand SelectPreviewCommand { get; }

    public ICommand ClearSelectionCommand { get; }

    public IReadOnlyList<GlanceMediaPreviewViewModel> SelectedPreviews => _selectedPreviews;

    public int SelectionCount => _selectedPreviews.Count;

    public bool HasSelection => SelectionCount > 0;

    public bool HasMultipleSelection => SelectionCount > 1;

    public string SelectionSummary => _localization.Get("Batch_SelectionCount", SelectionCount);

    public int AnnotatableSelectionCount =>
        _selectedPreviews.Count(preview => preview.MediaFormat.SupportsAnnotations);

    public string AnnotatableSelectionSummary =>
        _localization.Get("Overview_AnnotatableSelection", AnnotatableSelectionCount);

    public GlanceMediaPreviewViewModel? SelectedPreview
    {
        get => _selectedPreview;
        set => SetSelection(
            value == null ? Array.Empty<GlanceMediaPreviewViewModel>() : [value],
            value,
            value);
    }

    /// <summary>
    /// Connects the selection to the collection of preview cards. Called once by the
    /// composition root after the preview grid has been created.
    /// </summary>
    public void Attach(ObservableCollection<GlanceMediaPreviewViewModel> previews)
    {
        ArgumentNullException.ThrowIfNull(previews);

        if (_previews != null)
        {
            _previews.CollectionChanged -= OnPreviewsChanged;
        }

        _previews = previews;
        _previews.CollectionChanged += OnPreviewsChanged;
    }

    public void SetSelection(
        IEnumerable<GlanceMediaPreviewViewModel> selection,
        GlanceMediaPreviewViewModel? primary,
        GlanceMediaPreviewViewModel? anchor)
    {
        var valid = selection
            .Where(preview => _previews?.Contains(preview) == true)
            .Distinct()
            .ToArray();

        foreach (var preview in _selectedPreviews)
        {
            preview.Selected = false;
        }

        _selectedPreviews.Clear();
        _selectedPreviews.AddRange(valid);
        foreach (var preview in _selectedPreviews)
        {
            preview.Selected = true;
        }

        var nextPrimary = primary != null && _selectedPreviews.Contains(primary)
            ? primary
            : _selectedPreviews.LastOrDefault();
        if (!ReferenceEquals(_selectedPreview, nextPrimary))
        {
            _selectedPreview = nextPrimary;
            RaisePropertyChanged(nameof(SelectedPreview));
        }

        _selectionAnchor = anchor != null && _previews?.Contains(anchor) == true
            ? anchor
            : nextPrimary;
        RaiseSelectionProperties();
        _helpContext.SetFolderOverviewSelection(SelectionCount);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear() => SetSelection(Array.Empty<GlanceMediaPreviewViewModel>(), null, null);

    public void RemoveMissingSelections()
    {
        var existing = _selectedPreviews
            .Where(preview => _previews?.Contains(preview) == true)
            .ToArray();
        if (existing.Length == _selectedPreviews.Count)
        {
            return;
        }

        SetSelection(existing, existing.LastOrDefault(), existing.LastOrDefault());
    }

    private void HandlePreviewSelection(MediaPreviewSelectionRequest request)
    {
        if (request.Item is not GlanceMediaPreviewViewModel preview ||
            _previews == null ||
            _operations.IsOverviewOperationRunning)
        {
            return;
        }

        var toggle = request.Modifiers.HasFlag(KeyModifiers.Control)
                     || request.Modifiers.HasFlag(KeyModifiers.Meta);
        var range = request.Modifiers.HasFlag(KeyModifiers.Shift);

        if (range && _selectionAnchor != null)
        {
            var anchorIndex = _previews.IndexOf(_selectionAnchor);
            var targetIndex = _previews.IndexOf(preview);
            if (anchorIndex >= 0 && targetIndex >= 0)
            {
                var first = Math.Min(anchorIndex, targetIndex);
                var last = Math.Max(anchorIndex, targetIndex);
                var rangeItems = Enumerable.Range(first, last - first + 1)
                    .Select(index => _previews[index]);
                var selection = toggle
                    ? _selectedPreviews.Concat(rangeItems).Distinct().ToArray()
                    : rangeItems.ToArray();
                SetSelection(selection, preview, _selectionAnchor);
                return;
            }
        }

        if (toggle)
        {
            var selection = _selectedPreviews.ToList();
            if (!selection.Remove(preview))
            {
                selection.Add(preview);
            }

            SetSelection(selection, selection.LastOrDefault(), preview);
            return;
        }

        SetSelection([preview], preview, preview);
    }

    private void OnPreviewsChanged(object? sender, NotifyCollectionChangedEventArgs args) =>
        RemoveMissingSelections();

    private void RaiseSelectionProperties()
    {
        RaisePropertyChanged(nameof(SelectedPreviews));
        RaisePropertyChanged(nameof(SelectionCount));
        RaisePropertyChanged(nameof(HasSelection));
        RaisePropertyChanged(nameof(HasMultipleSelection));
        RaisePropertyChanged(nameof(SelectionSummary));
        RaisePropertyChanged(nameof(AnnotatableSelectionCount));
        RaisePropertyChanged(nameof(AnnotatableSelectionSummary));
    }
}
