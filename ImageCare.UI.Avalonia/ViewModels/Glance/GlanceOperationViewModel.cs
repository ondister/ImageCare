using System;
using System.Threading;
using System.Windows.Input;

using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.Services;

namespace ImageCare.UI.Avalonia.ViewModels.Glance;

/// <summary>
/// Shared progress and cancellation state for the two kinds of long-running work in the
/// folder overview: file batch operations and annotation edits. Both facets use it so the
/// selection bar can present one consistent progress area.
/// </summary>
internal sealed class GlanceOperationViewModel : ViewModelBase
{
    private readonly LocalizationManager _localization;
    private readonly AdaptiveHelpContextService _helpContext;

    private CancellationTokenSource? _batchOperationCancellation;
    private CancellationTokenSource? _annotationOperationCancellation;
    private bool _isBatchOperationRunning;
    private bool _isAnnotationOperationRunning;
    private int _processed;
    private int _total;
    private string _status = string.Empty;

    public GlanceOperationViewModel(
        LocalizationManager localization,
        AdaptiveHelpContextService helpContext)
    {
        _localization = localization;
        _helpContext = helpContext;
        CancelOverviewOperationCommand = CreateCommand(CancelAll);
    }

    public ICommand CancelOverviewOperationCommand { get; }

    public bool IsBatchOperationRunning
    {
        get => _isBatchOperationRunning;
        private set
        {
            if (SetProperty(ref _isBatchOperationRunning, value))
            {
                _helpContext.SetBatchOperationRunning(value);
                RaisePropertyChanged(nameof(IsOverviewOperationRunning));
            }
        }
    }

    public bool IsAnnotationOperationRunning
    {
        get => _isAnnotationOperationRunning;
        private set
        {
            if (SetProperty(ref _isAnnotationOperationRunning, value))
            {
                RaisePropertyChanged(nameof(IsOverviewOperationRunning));
            }
        }
    }

    public bool IsOverviewOperationRunning => IsBatchOperationRunning || IsAnnotationOperationRunning;

    public int BatchProcessed
    {
        get => _processed;
        private set
        {
            if (SetProperty(ref _processed, value))
            {
                RaisePropertyChanged(nameof(BatchProgressText));
            }
        }
    }

    public int BatchTotal
    {
        get => _total;
        private set
        {
            if (SetProperty(ref _total, value))
            {
                RaisePropertyChanged(nameof(BatchProgressText));
            }
        }
    }

    public string BatchProgressText => _localization.Get("Batch_Progress", BatchProcessed, BatchTotal);

    public string OverviewOperationStatus
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public CancellationTokenSource BeginBatchOperation(int total, string status)
    {
        var cancellation = new CancellationTokenSource();
        _batchOperationCancellation = cancellation;
        BatchProcessed = 0;
        BatchTotal = total;
        OverviewOperationStatus = status;
        IsBatchOperationRunning = true;
        return cancellation;
    }

    public void CompleteBatchOperation(CancellationTokenSource cancellation)
    {
        IsBatchOperationRunning = false;
        if (ReferenceEquals(_batchOperationCancellation, cancellation))
        {
            _batchOperationCancellation = null;
        }

        cancellation.Dispose();
    }

    public CancellationTokenSource BeginAnnotationOperation(int total, string status)
    {
        var cancellation = new CancellationTokenSource();
        _annotationOperationCancellation = cancellation;
        BatchProcessed = 0;
        BatchTotal = total;
        OverviewOperationStatus = status;
        IsAnnotationOperationRunning = true;
        return cancellation;
    }

    public void CompleteAnnotationOperation(CancellationTokenSource cancellation)
    {
        IsAnnotationOperationRunning = false;
        if (ReferenceEquals(_annotationOperationCancellation, cancellation))
        {
            _annotationOperationCancellation = null;
        }

        cancellation.Dispose();
    }

    public void ReportProgress(int processed, int total)
    {
        BatchProcessed = processed;
        BatchTotal = total;
    }

    public void ReportProgress(int processed) => BatchProcessed = processed;

    public void SetStatus(string status) => OverviewOperationStatus = status;

    public void CancelAll()
    {
        _batchOperationCancellation?.Cancel();
        _annotationOperationCancellation?.Cancel();
    }
}
