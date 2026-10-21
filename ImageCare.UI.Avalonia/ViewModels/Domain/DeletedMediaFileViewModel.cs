using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using Avalonia.Media.Imaging;

using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.MediaPreviewService;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal sealed class DeletedMediaFileViewModel : ViewModelBase, IDisposable
{
    private const int PreviewDecodeWidth = 64;
    private static readonly SemaphoreSlim PreviewDecodeGate = new(4, 4);

    private readonly Func<DeletedMediaFile, Task> _restore;
    private readonly IMediaPreviewService _mediaPreviewService;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _previewCancellation = new();
    private Bitmap? _previewBitmap;
    private bool _isRestoring;

    public DeletedMediaFileViewModel(
        DeletedMediaFile model,
        Func<DeletedMediaFile, Task> restore,
        IMediaPreviewService mediaPreviewService,
        ILogger logger)
    {
        Model = model;
        _restore = restore;
        _mediaPreviewService = mediaPreviewService;
        _logger = logger;
        RestoreCommand = CreateAsyncCommand(RestoreAsync, () => !IsRestoring)
            .ObservesProperty(() => IsRestoring);
    }

    public DeletedMediaFile Model { get; }

    public string FileName => Path.GetFileName(Model.OriginalPath);

    public string OriginalFolder => Path.GetDirectoryName(Model.OriginalPath) ?? string.Empty;

    public string DeletedAt => Model.DeletedAtUtc.ToLocalTime().ToString("g");

    public ICommand RestoreCommand { get; }

    public Bitmap? PreviewBitmap
    {
        get => _previewBitmap;
        private set
        {
            if (ReferenceEquals(_previewBitmap, value))
            {
                return;
            }

            var previous = _previewBitmap;
            if (SetProperty(ref _previewBitmap, value))
            {
                previous?.Dispose();
                RaisePropertyChanged(nameof(HasPreview));
            }
        }
    }

    public bool HasPreview => PreviewBitmap != null;

    public bool IsRestoring
    {
        get => _isRestoring;
        private set => SetProperty(ref _isRestoring, value);
    }

    public async Task LoadPreviewAsync()
    {
        var token = _previewCancellation.Token;
        Bitmap? decodedBitmap = null;
        try
        {
            var mediaPreview = await _mediaPreviewService.GetMediaPreviewAsync(Model.BufferPath, token);
            if (mediaPreview == null)
            {
                return;
            }

            await PreviewDecodeGate.WaitAsync(token);
            try
            {
                await using var imageStream = await _mediaPreviewService.GetJpegImageStreamAsync(
                    mediaPreview,
                    MediaPreviewSize.Small,
                    token);
                decodedBitmap = await Task.Run(
                    () => Bitmap.DecodeToWidth(
                        imageStream,
                        PreviewDecodeWidth,
                        BitmapInterpolationMode.MediumQuality),
                    token);
                token.ThrowIfCancellationRequested();
                PreviewBitmap = decodedBitmap;
                decodedBitmap = null;
            }
            finally
            {
                PreviewDecodeGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the deleted-files list is refreshed or the overview closes.
        }
        catch (Exception exception)
        {
            _logger.Warning(exception, "Failed to load deleted media preview for {Path}", Model.BufferPath);
        }
        finally
        {
            decodedBitmap?.Dispose();
        }
    }

    public void Dispose()
    {
        _previewCancellation.Cancel();
        PreviewBitmap = null;
        _previewCancellation.Dispose();
    }

    private async Task RestoreAsync()
    {
        try
        {
            IsRestoring = true;
            await _restore(Model);
        }
        finally
        {
            IsRestoring = false;
        }
    }
}
