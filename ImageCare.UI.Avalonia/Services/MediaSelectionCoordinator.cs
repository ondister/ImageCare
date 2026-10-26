using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Preview;

namespace ImageCare.UI.Avalonia.Services;

/// <summary>
/// Keeps the active preview-panel selection and its file actions available to shell-level
/// commands without making the shell depend on a particular panel instance.
/// </summary>
internal sealed class MediaSelectionCoordinator
{
    private IReadOnlyList<MediaPreview> _selectedPreviews = Array.Empty<MediaPreview>();
    private Func<Task>? _copyAction;
    private Func<Task>? _moveAction;

    public event EventHandler? SelectionChanged;

    public FileManagerPanel FileManagerPanel { get; private set; } = FileManagerPanel.Undefined;

    public IReadOnlyList<MediaPreview> SelectedPreviews => _selectedPreviews;

    public int Count => _selectedPreviews.Count;

    public bool CanCopyOrMove => Count > 0 && _copyAction is not null && _moveAction is not null;

    public void Update(
        FileManagerPanel fileManagerPanel,
        IEnumerable<MediaPreview> selectedPreviews,
        Func<Task> copyAction,
        Func<Task> moveAction)
    {
        ArgumentNullException.ThrowIfNull(selectedPreviews);
        ArgumentNullException.ThrowIfNull(copyAction);
        ArgumentNullException.ThrowIfNull(moveAction);

        var snapshot = selectedPreviews.ToArray();
        var actionsChanged = !ReferenceEquals(_copyAction, copyAction) ||
                             !ReferenceEquals(_moveAction, moveAction);
        if (fileManagerPanel == FileManagerPanel &&
            !actionsChanged &&
            snapshot.SequenceEqual(_selectedPreviews))
        {
            return;
        }

        FileManagerPanel = fileManagerPanel;
        _selectedPreviews = snapshot;
        _copyAction = copyAction;
        _moveAction = moveAction;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear(FileManagerPanel fileManagerPanel)
    {
        if (FileManagerPanel != fileManagerPanel)
        {
            return;
        }

        FileManagerPanel = fileManagerPanel;
        _selectedPreviews = Array.Empty<MediaPreview>();
        _copyAction = null;
        _moveAction = null;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task CopyAsync() => _copyAction?.Invoke() ?? Task.CompletedTask;

    public Task MoveAsync() => _moveAction?.Invoke() ?? Task.CompletedTask;
}
