using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.Services;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using MapsterMapper;

namespace ImageCare.UI.Avalonia.ViewModels.Preview;

/// <summary>
/// Owns the multi-selection state of one preview panel and keeps the shell-wide
/// <see cref="MediaSelectionCoordinator"/> in sync with it.
/// </summary>
internal sealed class PreviewSelectionViewModel : ViewModelBase
{
    private readonly LocalizationManager _localization;
    private readonly MediaSelectionCoordinator _selectionCoordinator;
    private readonly IMapper _mapper;
    private readonly Func<FileManagerPanel> _fileManagerPanel;
    private readonly Func<MediaPreviewViewModel, bool> _isKnownPreview;
    private readonly List<MediaPreviewViewModel> _selectedPreviews = [];

    private MediaPreviewViewModel? _selectedPreview;
    private Func<Task>? _copyAction;
    private Func<Task>? _moveAction;

    public PreviewSelectionViewModel(
        LocalizationManager localization,
        MediaSelectionCoordinator selectionCoordinator,
        IMapper mapper,
        Func<FileManagerPanel> fileManagerPanel,
        Func<MediaPreviewViewModel, bool> isKnownPreview)
    {
        _localization = localization;
        _selectionCoordinator = selectionCoordinator;
        _mapper = mapper;
        _fileManagerPanel = fileManagerPanel;
        _isKnownPreview = isKnownPreview;

        ClearSelectionCommand = CreateCommand(Clear);
    }

    /// <summary>Raised when the set of selected previews or the primary preview changes.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Raised only when the primary (focused) preview changes.</summary>
    public event EventHandler? PrimarySelectionChanged;

    public IReadOnlyList<MediaPreviewViewModel> SelectedPreviews => _selectedPreviews;

    public int SelectionCount => _selectedPreviews.Count;

    public bool HasSelection => SelectionCount > 0;

    public bool HasMultipleSelection => SelectionCount > 1;

    public string SelectionSummary => _localization.Get("Batch_SelectionCount", SelectionCount);

    public ICommand ClearSelectionCommand { get; }

    public MediaPreviewViewModel? SelectedPreview
    {
        get => _selectedPreview;
        set
        {
            if (!SetProperty(ref _selectedPreview, value))
            {
                return;
            }

            PrimarySelectionChanged?.Invoke(this, EventArgs.Empty);
            RaiseSelectionProperties();
        }
    }

    /// <summary>
    /// Supplies the actions advertised to the shell-level copy/move commands. They are wired
    /// after construction to avoid a dependency cycle with the batch-operations facet.
    /// </summary>
    public void SetActions(Func<Task> copyAction, Func<Task> moveAction)
    {
        _copyAction = copyAction ?? throw new ArgumentNullException(nameof(copyAction));
        _moveAction = moveAction ?? throw new ArgumentNullException(nameof(moveAction));
        if (_selectedPreviews.Count > 0)
        {
            PublishToCoordinator();
        }
    }

    public void UpdateSelection(
        IReadOnlyList<MediaPreviewViewModel> selectedPreviews,
        MediaPreviewViewModel? primary = null)
    {
        ArgumentNullException.ThrowIfNull(selectedPreviews);

        var valid = selectedPreviews
            .Where(preview => preview is not null && _isKnownPreview(preview))
            .Distinct()
            .ToArray();

        foreach (var preview in _selectedPreviews)
        {
            preview.Selected = false;
        }

        _selectedPreviews.Clear();
        _selectedPreviews.AddRange(valid);
        foreach (var preview in valid)
        {
            preview.Selected = true;
        }

        var nextPrimary = primary is not null && valid.Contains(primary)
            ? primary
            : valid.LastOrDefault();
        if (!ReferenceEquals(SelectedPreview, nextPrimary))
        {
            SelectedPreview = nextPrimary;
        }

        PublishToCoordinator();
        RaiseSelectionProperties();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        UpdateSelection(Array.Empty<MediaPreviewViewModel>(), primary: null);
    }

    private void PublishToCoordinator()
    {
        if (_selectedPreviews.Count == 0)
        {
            _selectionCoordinator.Clear(_fileManagerPanel());
            return;
        }

        _selectionCoordinator.Update(
            _fileManagerPanel(),
            _selectedPreviews.Select(_mapper.Map<MediaPreview>),
            _copyAction ?? (() => Task.CompletedTask),
            _moveAction ?? (() => Task.CompletedTask));
    }

    private void RaiseSelectionProperties()
    {
        RaisePropertyChanged(nameof(SelectedPreviews));
        RaisePropertyChanged(nameof(SelectionCount));
        RaisePropertyChanged(nameof(HasSelection));
        RaisePropertyChanged(nameof(HasMultipleSelection));
        RaisePropertyChanged(nameof(SelectionSummary));
    }
}
