using System;
using System.Threading.Tasks;
using System.Windows.Input;

using Avalonia.Media.Imaging;

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

    public ICommand? ResetMatrixCommand { get; set; }

    /// <inheritdoc />
    public void Receive(FolderSelectedMessage message)
    {
        ClearPreview();
    }

    /// <inheritdoc />
    public void Receive(ImagePreviewSelectedMessage message)
    {
        if (message.Value == ImagePreview.Empty)
        {
            ClearPreview();

            return;
        }

        ResetMatrixCommand?.Execute(null);

        _ = LoadImageAsync(message.Value);
    }

    private void ClearPreview()
    {
        MainBitmap = null;
    }

    private async Task LoadImageAsync(ImagePreview imagePreview)
    {
        try
        {
            await using (var imageStream = await _imageService.GetJpegImageStreamAsync(imagePreview, ImagePreviewSize.Large))
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