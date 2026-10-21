using System.Collections.Generic;
using System.IO;

using ImageCare.Core.Domain.Annotations;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

public sealed class MediaSearchResultViewModel
{
    public MediaSearchResultViewModel(IndexedMediaAnnotations indexed)
    {
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
}
