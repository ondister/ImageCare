using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Input;

using AutoMapper;

using Avalonia.Media.Imaging;

using CommunityToolkit.Mvvm.Input;

using ImageCare.Core.Domain;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Services.FileOperationsService;
using ImageCare.Core.Services.FileSystemImageService;
using ImageCare.Mvvm;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal class ImagePreviewViewModel : ViewModelBase, IComparable<ImagePreviewViewModel>
{
    private readonly IFileSystemImageService _imageService;
    private readonly IFileOperationsService _fileOperationsService;
    private readonly IMapper _mapper;
    private readonly ILogger _logger;
    private Bitmap? _previewBitmap;
    private bool _selected;
    private bool _isLoading;

    public ImagePreviewViewModel(string? title,
                                 string url,
                                 MediaFormat mediaFormat,
                                 IFileSystemImageService imageService,
                                 IFileOperationsService fileOperationsService,
                                 IMapper mapper,
                                 ILogger logger)
    {
        Title = title;
        Url = url;
        MediaFormat = mediaFormat;

        _imageService = imageService;
        _fileOperationsService = fileOperationsService;
        _mapper = mapper;
        _logger = logger;

        RemoveImagePreviewCommand = new AsyncRelayCommand(RemoveImagePreviewAsync);

        _ = LoadPreviewAsync();
    }

    public string? Title { get; }

    public string Url { get; }

    public MediaFormat MediaFormat { get; }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

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
        IsLoading = true;

        try
        {
            await using (var imageStream = await _imageService.GetJpegImageStreamAsync(_mapper.Map<ImagePreview>(this), ImagePreviewSize.Medium))
            {
                PreviewBitmap = await Task.Run(() => Bitmap.DecodeToWidth(imageStream, 300, BitmapInterpolationMode.LowQuality));
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, $"Unexpected exception during creating bitmap preview for file {Url}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task RemoveImagePreviewAsync()
    {
        try
        {
            await _fileOperationsService.DeleteImagePreviewAsync(_mapper.Map<ImagePreview>(this));
        }
        catch (Exception exception)
        {
            _logger.Error(exception, $"Unexpected exception during preview deletion for file {Url}");
        }
    }
}