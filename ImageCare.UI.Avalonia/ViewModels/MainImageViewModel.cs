using System;
using System.Threading.Tasks;

using Avalonia.Media.Imaging;

using CommunityToolkit.Mvvm.Messaging;

using ImageCare.Core.Domain;
using ImageCare.Core.Services;
using ImageCare.UI.Avalonia.Messages;

namespace ImageCare.UI.Avalonia.ViewModels;

internal class MainImageViewModel : ViewModelBase, IRecipient<ImagePreviewSelectedMessage>
{
    private readonly IFileSystemImageService _imageService;
    private Bitmap? _mainBitmap;

    public MainImageViewModel(IFileSystemImageService imageService)
    {
        _imageService = imageService;

        WeakReferenceMessenger.Default.Register(this);
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

        LoadPreviewAsync(message.Value);
    }

    private void ClearPreview()
    {
        MainBitmap = null;
    }

    private async Task LoadPreviewAsync(ImagePreview imagePreview)
    {
        try
        {
            await using (var imageStream = await _imageService.GetImageStreamAsync(imagePreview))
            {
                MainBitmap = new Bitmap(imageStream);
            }
        }
        catch (Exception exception) { }
    }
}