using System;
using System.Threading;
using System.Threading.Tasks;

using ImageCare.Core.Domain.Notification;
using ImageCare.Core.Services.MediaBatchOperationsService;
using ImageCare.Core.Services.NotificationService;
using ImageCare.UI.Avalonia.Localization;

using Serilog;

using Notification = ImageCare.Core.Domain.Notification.Notification;

namespace ImageCare.UI.Avalonia.Services;

/// <summary>
/// Runs a media batch operation and reports its localized outcome.
/// Operation state and cancellation-token ownership remain with the caller.
/// </summary>
internal sealed class MediaBatchOperationRunner(
    INotificationService notificationService,
    LocalizationManager localization,
    ILogger logger)
{
    public async Task RunAsync(
        MediaBatchOperationKind kind,
        string startTitle,
        CancellationToken cancellationToken,
        Action<MediaBatchOperationProgress> reportProgress,
        Func<IProgress<MediaBatchOperationProgress>, CancellationToken, Task<MediaBatchOperationSummary>> operation,
        Action<string>? setStatus = null,
        string failureLogMessage = "Batch {Operation} failed")
    {
        notificationService.SendNotification(new Notification(startTitle, string.Empty));

        try
        {
            var progress = new Progress<MediaBatchOperationProgress>(reportProgress);
            var summary = await operation(progress, cancellationToken);
            ReportSummary(kind, summary, setStatus);
        }
        catch (OperationCanceledException)
        {
            var title = localization["Batch_CancelledTitle"];
            setStatus?.Invoke(title);
            notificationService.SendNotification(new Notification(title, string.Empty));
        }
        catch (Exception exception)
        {
            logger.Error(exception, failureLogMessage, kind);
            var title = localization["Batch_FailedTitle"];
            setStatus?.Invoke(title);
            notificationService.SendNotification(new ErrorNotification(title, exception.Message));
        }
    }

    private void ReportSummary(
        MediaBatchOperationKind kind,
        MediaBatchOperationSummary summary,
        Action<string>? setStatus)
    {
        var title = localization.Get(
            kind switch
            {
                MediaBatchOperationKind.Copy => "Batch_Completed_Copy",
                MediaBatchOperationKind.Move => "Batch_Completed_Move",
                MediaBatchOperationKind.Delete => "Batch_Completed_Delete",
                MediaBatchOperationKind.ExportJpeg => "Batch_Completed_Export",
                _ => "Batch_FailedTitle"
            },
            summary.Succeeded,
            summary.Total);
        setStatus?.Invoke(title);

        if (summary.Cancelled)
        {
            notificationService.SendNotification(
                new Notification(localization["Batch_CancelledTitle"], title));
        }
        else if (summary.Failed > 0)
        {
            var detail = summary.Failures.Count == 0
                ? localization["Batch_FailedTitle"]
                : localization.Get("Batch_FailedDetail", summary.Failed, summary.Failures[0].Error);
            notificationService.SendNotification(new ErrorNotification(title, detail));
        }
        else
        {
            notificationService.SendNotification(new SuccessNotification(title, string.Empty));
        }
    }
}
