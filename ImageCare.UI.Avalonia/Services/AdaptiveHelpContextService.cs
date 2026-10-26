using System;

namespace ImageCare.UI.Avalonia.Services;

internal sealed class AdaptiveHelpContextService
{
    public event EventHandler? Changed;

    public bool IsFolderOverviewOpen { get; private set; }

    public int FolderOverviewSelectionCount { get; private set; }

    public bool IsBatchOperationRunning { get; private set; }

    public void EnterFolderOverview()
    {
        IsFolderOverviewOpen = true;
        FolderOverviewSelectionCount = 0;
        IsBatchOperationRunning = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void LeaveFolderOverview()
    {
        IsFolderOverviewOpen = false;
        FolderOverviewSelectionCount = 0;
        IsBatchOperationRunning = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetFolderOverviewSelection(int count)
    {
        var normalized = Math.Max(0, count);
        if (FolderOverviewSelectionCount == normalized)
        {
            return;
        }

        FolderOverviewSelectionCount = normalized;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetBatchOperationRunning(bool value)
    {
        if (IsBatchOperationRunning == value)
        {
            return;
        }

        IsBatchOperationRunning = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
