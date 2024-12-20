using System;
using System.Threading.Tasks;
using System.Windows.Input;

using Avalonia.Media.Imaging;

using CommunityToolkit.Mvvm.Input;

using ImageCare.Core.Domain;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Services;
using ImageCare.Core.Services.FileOperationsService;
using ImageCare.Mvvm;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal class ImagePreviewViewModel : ViewModelBase, IComparable<ImagePreviewViewModel>
{
    private readonly ImagePreview _imagePreview;
    private readonly IFileSystemImageService _imageService;
    private readonly IFileOperationsService _fileOperationsService;
    private Bitmap? _previewBitmap;
    private bool _selected;

    public ImagePreviewViewModel(ImagePreview imagePreview, 
                                 IFileSystemImageService imageService,
                                 IFileOperationsService fileOperationsService)
    {
        _imagePreview = imagePreview;
        _imageService = imageService;
        _fileOperationsService = fileOperationsService;

        RemoveImagePreviewCommand = new AsyncRelayCommand(RemoveImagePreviewAsync);

        LoadPreviewAsync();
    }

    public string? Title => _imagePreview.Title;

    public string Url => _imagePreview.Url;

    public MediaFormat MediaFormat => _imagePreview.MediaFormat;

    public bool Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    public Bitmap? PreviewBitmap
    {
        get => _previewBitmap;
        set => SetProperty(ref _previewBitmap, value);
    }

    public ICommand RemoveImagePreviewCommand { get; }

    public int CompareTo(ImagePreviewViewModel? other)
    {
        if (other == null)
        {
            return 1;
        }

        return string.CompareOrdinal(Url, other.Url);
    }

    private async Task LoadPreviewAsync()
    {
        try
        {
            await using (var imageStream = await _imageService.GetImageStreamAsync(_imagePreview))
            {
                PreviewBitmap = Bitmap.DecodeToWidth(imageStream, 300);
            }
        }
        catch (Exception exception)
        {
            throw;
        }
    }

    private async Task RemoveImagePreviewAsync()
    {
        await _fileOperationsService.DeleteImagePreviewAsync(new ImagePreview(Title, Url, MediaFormat));
    }
}