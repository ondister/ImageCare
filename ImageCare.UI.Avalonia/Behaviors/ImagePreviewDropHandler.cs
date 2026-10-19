using System;
using System.IO;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Input;

using ImageCare.Core.Domain.Notification;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.NotificationService;
using ImageCare.UI.Avalonia.Services;
using ImageCare.UI.Avalonia.ViewModels;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using MapsterMapper;

using Serilog;

using Notification = ImageCare.Core.Domain.Notification.Notification;

namespace ImageCare.UI.Avalonia.Behaviors;

public class ImagePreviewDropHandler
{
    private readonly FileTransferOperationState _operationState;
    private readonly FileTransferCoordinator _fileTransferCoordinator;
    private readonly INotificationService _notificationService;
    private readonly IMapper _mapper;
    private readonly ILogger _logger;

    public ImagePreviewDropHandler(
        FileTransferOperationState operationState,
        FileTransferCoordinator fileTransferCoordinator,
        INotificationService notificationService,
        IMapper mapper,
        ILogger logger)
    {
        _operationState = operationState;
        _fileTransferCoordinator = fileTransferCoordinator;
        _notificationService = notificationService;
        _mapper = mapper;
        _logger = logger;
    }

    public FileTransferOperation CurrentOperation => _operationState.CurrentStatus.Operation;

    public IObservable<FileTransferOperationStatus> OperationStatusChanged =>
        _operationState.StatusChanged;

    public void BeginDrag(KeyModifiers modifiers) => _operationState.BeginDrag(modifiers);

    public FileTransferOperation UpdateOperation(KeyModifiers modifiers)
    {
        _operationState.UpdateFromDrag(modifiers);
        return CurrentOperation;
    }

    public void EndDrag() => _operationState.EndDrag();

    public async Task<bool> ExecuteAsync(
        object? sender,
        object? sourceContext,
        object? targetContext,
        FileTransferOperation operation)
    {
        if (!Validate(sender, sourceContext, targetContext) ||
            sourceContext is not MediaPreviewViewModel sourceImagePreview ||
            targetContext is not PreviewPanelViewModel targetPanel)
        {
            return false;
        }

        var notificationTitle = $"{sourceImagePreview.Url} => {targetPanel.SelectedFolderPath}";
        _notificationService.SendNotification(new Notification(notificationTitle, string.Empty));
        var progress = new Progress<OperationInfo>();
        progress.ProgressChanged += (_, info) =>
            _notificationService.SendNotification(
                new Notification(notificationTitle, info.Percentage.ToString("F1")));

        try
        {
            var sourcePreview = _mapper.Map<MediaPreview>(sourceImagePreview);
            var result = operation switch
            {
                FileTransferOperation.Copy => await _fileTransferCoordinator.CopyAsync(
                    sourcePreview,
                    targetPanel.SelectedFolderPath,
                    progress),
                FileTransferOperation.Move => await _fileTransferCoordinator.MoveAsync(
                    sourcePreview,
                    targetPanel.SelectedFolderPath,
                    progress),
                _ => OperationResult.Cancelled
            };

            if (result == OperationResult.Success)
            {
                _notificationService.SendNotification(new SuccessNotification(notificationTitle, string.Empty));
                return true;
            }

            if (result == OperationResult.Failed)
            {
                _notificationService.SendNotification(new ErrorNotification(notificationTitle, string.Empty));
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.Error(
                ex,
                "Failed to drop media preview from {Source} to {Target}",
                sourceImagePreview.Url,
                targetPanel.SelectedFolderPath);
            _notificationService.SendNotification(new ErrorNotification(notificationTitle, string.Empty));
            return false;
        }
    }

    public bool Validate(
        object? sender,
        object? sourceContext,
        object? targetContext)
    {
        if (sender is not ListBox ||
            sourceContext is not MediaPreviewViewModel sourceImagePreview ||
            targetContext is not PreviewPanelViewModel targetPanel ||
            string.IsNullOrWhiteSpace(targetPanel.SelectedFolderPath))
        {
            return false;
        }

        var sourceDirectory = Path.GetDirectoryName(sourceImagePreview.Url);
        return _fileTransferCoordinator.RenameEnabled ||
               !PathsEqual(sourceDirectory, targetPanel.SelectedFolderPath);
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (left == null || right == null)
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            comparison);
    }
}
