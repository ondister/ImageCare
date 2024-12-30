using ImageCare.Core.Domain.MediaFormats;

namespace ImageCare.Core.Domain;

public class ImagePreview : IEquatable<ImagePreview>
{
    public ImagePreview(string? title, string url, MediaFormat mediaFormat, int maxImageHeight)
    {
        Title = title;
        Url = url;
        MediaFormat = mediaFormat;
        MaxImageHeight = maxImageHeight;
    }

    public static ImagePreview Empty { get; } = new(string.Empty, string.Empty, MediaFormat.MediaFormatUnknown, 0);

    public string? Title { get; }

    public string Url { get; }

    public MediaFormat MediaFormat { get; }

    public int MaxImageHeight { get; }

    /// <inheritdoc />
    public bool Equals(ImagePreview? other)
    {
        if (ReferenceEquals(null, other))
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Url == other.Url;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj))
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj.GetType() != GetType())
        {
            return false;
        }

        return Equals((ImagePreview)obj);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return Url.GetHashCode();
    }

    public static bool operator ==(ImagePreview? left, ImagePreview? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(ImagePreview? left, ImagePreview? right)
    {
        return !Equals(left, right);
    }
}