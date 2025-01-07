using System;

using AutoMapper;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactions.DragAndDrop;

using ImageCare.Core.Domain;
using ImageCare.Core.Services.FileOperationsService;
using ImageCare.Core.Services.NotificationService;
using ImageCare.UI.Avalonia.ViewModels;
using ImageCare.UI.Avalonia.ViewModels.Domain;

namespace ImageCare.UI.Avalonia.Behaviors;

public class ImagePreviewDropHandler : DropHandlerBase
{
    private readonly IFileOperationsService _fileOperationsService;
    private readonly INotificationService _notificationService;
    private readonly IMapper _mapper;

    public ImagePreviewDropHandler(IFileOperationsService fileOperationsService,
                                   INotificationService notificationService,
                                   IMapper mapper)
    {
        _fileOperationsService = fileOperationsService;
        _notificationService = notificationService;
        _mapper = mapper;
    }

    /// <inheritdoc />
    public override bool Execute(object? sender, DragEventArgs e, object? sourceContext, object? targetContext, object? state)
    {
        if (e.Source is Control && sender is ListBox listBox)
        {
            return Validate<MediaPreviewViewModel>(listBox, e, sourceContext, targetContext, true);
        }

        return false;
    }

    /// <inheritdoc />
    public override bool Validate(object? sender, DragEventArgs e, object? sourceContext, object? targetContext, object? state)
    {
        if (e.Source is Control && sender is ListBox listBox)
        {
            return Validate<MediaPreviewViewModel>(listBox, e, sourceContext, targetContext, false);
        }

        return false;
    }

    private bool Validate<T>(ListBox listBox, DragEventArgs e, object? sourceContext, object? targetContext, bool bExecute) where T : MediaPreviewViewModel
    {
        if (sourceContext is not T sourceImagePreview
         || targetContext is not PreviewPanelViewModel previewImageViewModel
         || listBox.GetVisualAt(e.GetPosition(listBox)) is not Control targetControl)
        {
            return true;
        }

        if (string.IsNullOrEmpty(previewImageViewModel.SelectedFolderPath))
        {
            return false;
        }

        switch (e.DragEffects)
        {
            case DragDropEffects.Copy:
            {
                if (bExecute && !previewImageViewModel.ImagePreviews.Contains(sourceImagePreview))
                {
                    var notificationTitle = $"{sourceImagePreview.Url} => {previewImageViewModel.SelectedFolderPath}";
                    _notificationService.SendNotification(new Notification(notificationTitle, string.Empty));

                    var progress = new Progress<OperationInfo>();

                    progress.ProgressChanged += (o, info) => { _notificationService.SendNotification(new Notification(notificationTitle, info.Percentage.ToString("F1"))); };

                    _fileOperationsService.CopyImagePreviewToDirectoryAsync(
                                              _mapper.Map<MediaPreview>(sourceImagePreview),
                                              previewImageViewModel.SelectedFolderPath,
                                              progress)
                                          .ContinueWith(
                                              task =>
                                              {
                                                  switch (task.Result)
                                                  {
                                                      case OperationResult.Success:
                                                          _notificationService.SendNotification(new SuccessNotification(notificationTitle, ""));
                                                          break;
                                                      case OperationResult.Failed:
                                                          _notificationService.SendNotification(new ErrorNotification(notificationTitle, ""));
                                                          break;
                                                  }
                                              });
                }

                break;
            }
            case DragDropEffects.Move:
            {
                if (bExecute && !previewImageViewModel.ImagePreviews.Contains(sourceImagePreview))
                {
                    var notificationTitle = $"{sourceImagePreview.Url} => {previewImageViewModel.SelectedFolderPath}";
                    _notificationService.SendNotification(new Notification(notificationTitle, string.Empty));

                    var progress = new Progress<OperationInfo>();

                    progress.ProgressChanged += (o, info) => { _notificationService.SendNotification(new Notification(notificationTitle, info.Percentage.ToString("F1"))); };

                    _fileOperationsService.MoveImagePreviewToDirectoryAsync(
                                              _mapper.Map<MediaPreview>(sourceImagePreview),
                                              previewImageViewModel.SelectedFolderPath,
                                              progress)
                                          .ContinueWith(
                                              task =>
                                              {
                                                  switch (task.Result)
                                                  {
                                                      case OperationResult.Success:
                                                          _notificationService.SendNotification(new SuccessNotification(notificationTitle, ""));
                                                          break;
                                                      case OperationResult.Failed:
                                                          _notificationService.SendNotification(new ErrorNotification(notificationTitle, ""));
                                                          break;
                                                  }
                                              });
                }

                break;
            }
        }

        return true;
    }
}