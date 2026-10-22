using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Avalonia.Media.Imaging;

using ImageCare.Core.Domain.Annotations;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

public sealed class MediaSearchResultViewModel : ViewModelBase
{
    private readonly Func<string, Task<Bitmap?>> _thumbnailLoader;
    private Bitmap? _thumbnail;
    private bool _thumbnailRequested;

    public MediaSearchResultViewModel(
        IndexedMediaAnnotations indexed,
        Func<string, Task<Bitmap?>> thumbnailLoader)
    {
        _thumbnailLoader = thumbnailLoader;
        Path = indexed.MediaPath;
        FileName = System.IO.Path.GetFileName(indexed.MediaPath);
        Rating = indexed.Annotations.Rating;
        Tags = indexed.Annotations.Tags;
    }

    public string FileName { get; }

    public string Path { get; }

    public int Rating { get; }

    public IReadOnlyList<string> Tags { get; }

    public string RatingStars => Rating == 0 ? "☆☆☆☆☆" : new string('★', Rating) + new string('☆', 5 - Rating);

    public string TagSummary => string.Join(", ", Tags);

    public Bitmap? Thumbnail
    {
        get => _thumbnail;
        private set => SetProperty(ref _thumbnail, value);
    }

    /// <summary>
    /// Starts loading the thumbnail once. The owning view model calls this when a result row is
    /// realized by the grid so that only visible rows are decoded.
    /// </summary>
    public void RequestThumbnail()
    {
        if (_thumbnailRequested)
        {
            return;
        }

        _thumbnailRequested = true;
        _ = LoadThumbnailAsync();
    }

    private async Task LoadThumbnailAsync()
    {
        try
        {
            Thumbnail = await _thumbnailLoader(Path);
        }
        catch (Exception)
        {
            // Thumbnail failures are logged by the owning view model; the row keeps the placeholder.
        }
    }
}
