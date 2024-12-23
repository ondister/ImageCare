using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using CommunityToolkit.Mvvm.Messaging;
using ImageCare.Core.Domain;
using ImageCare.Core.Services;
using ImageCare.Mvvm;
using ImageCare.UI.Avalonia.Messages;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels;

internal class MainImageViewModel : ViewModelBase, IRecipient<FolderSelectedMessage>, IRecipient<ImagePreviewSelectedMessage>
{
    private readonly IFileSystemImageService _imageService;
    private readonly ILogger _logger;
    private Bitmap? _mainBitmap;

    public MainImageViewModel(IFileSystemImageService imageService, ILogger logger)
    {
        _imageService = imageService;
        _logger = logger;

        WeakReferenceMessenger.Default.Register<FolderSelectedMessage>(this);
        WeakReferenceMessenger.Default.Register<ImagePreviewSelectedMessage>(this);
    }

    public Bitmap? MainBitmap
    {
        get => _mainBitmap;
        set => SetProperty(ref _mainBitmap, value);
    }

    /// <inheritdoc />
    public void Receive(ImagePreviewSelectedMessage message)
    {
        if (message.Value == ImagePreview.Empty)
        {
            ClearPreview();

            return;
        }

        _ = LoadImageAsync(message.Value);
    }

    /// <inheritdoc />
    public void Receive(FolderSelectedMessage message)
    {
        ClearPreview();
    }

    private void ClearPreview()
    {
        MainBitmap = null;
    }

    private async Task LoadImageAsync(ImagePreview imagePreview)
    {
        try
        {
            await using (var imageStream = await _imageService.GetJpegImageStreamAsync(imagePreview))
            {
                MainBitmap = new Bitmap(imageStream);

            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, $"Unexpected exception during loading main image {imagePreview.Url}");
        }
    }
}