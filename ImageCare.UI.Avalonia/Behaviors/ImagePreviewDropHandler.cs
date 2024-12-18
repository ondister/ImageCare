using System;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactions.DragAndDrop;

using ImageCare.Core.Domain;
using ImageCare.Core.Services.FileOperationsService;
using ImageCare.UI.Avalonia.ViewModels;
using ImageCare.UI.Avalonia.ViewModels.Domain;

namespace ImageCare.UI.Avalonia.Behaviors;

public class ImagePreviewDropHandler : DropHandlerBase
{
    private readonly IFileOperationsService _fileOperationsService;

    public ImagePreviewDropHandler(IFileOperationsService fileOperationsService)
    {
        _fileOperationsService = fileOperationsService;
    }

    /// <inheritdoc />
    public override bool Execute(object? sender, DragEventArgs e, object? sourceContext, object? targetContext, object? state)
    {
        if (e.Source is Control && sender is ListBox listBox)
        {
            return Validate<ImagePreviewViewModel>(listBox, e, sourceContext, targetContext, true);
        }

        return false;
    }

    /// <inheritdoc />
    public override bool Validate(object? sender, DragEventArgs e, object? sourceContext, object? targetContext, object? state)
    {
        if (e.Source is Control && sender is ListBox listBox)
        {
            return Validate<ImagePreviewViewModel>(listBox, e, sourceContext, targetContext, false);
        }

        return false;
    }

    private bool Validate<T>(ListBox listBox, DragEventArgs e, object? sourceContext, object? targetContext, bool bExecute) where T : ImagePreviewViewModel
    {
        if (sourceContext is not T sourceImagePreview
         || targetContext is not PreviewImageViewModel previewImageViewModel
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
                    var progress = new Progress<OperationInfo>();
                    _fileOperationsService.CopyImagePreviewToDirectoryAsync(
                        new ImagePreview(sourceImagePreview.Title, sourceImagePreview.Url, sourceImagePreview.MediaFormat),
                        previewImageViewModel.SelectedFolderPath,
                        progress);
                }

                break;
            }
            case DragDropEffects.Move:
            {
                if (bExecute && !previewImageViewModel.ImagePreviews.Contains(sourceImagePreview))
                {
                    var progress = new Progress<OperationInfo>();
                    _fileOperationsService.MoveImagePreviewToDirectoryAsync(
                        new ImagePreview(sourceImagePreview.Title, sourceImagePreview.Url, sourceImagePreview.MediaFormat),
                        previewImageViewModel.SelectedFolderPath,
                        progress);
                }

                break;
            }
        }

        return true;
    }
}