using System.Reflection;

namespace ImageCare.Core.Domain.MediaFormats;

public sealed class MediaFormat:IEquatable<MediaFormat>
{
    private static readonly Dictionary<string, MediaFormat> _supportedMediaFormats = new(StringComparer.OrdinalIgnoreCase);

    static MediaFormat()
    {
        var properties = typeof(MediaFormat).GetProperties(BindingFlags.Static | BindingFlags.Public);

        foreach (var property in properties)
        {
            if (property.GetMethod == null || !property.GetMethod.IsStatic)
            {
                continue;
            }

            if (property.GetValue(null) is not MediaFormat mediaFormat || mediaFormat == MediaFormatUnknown)
            {
                continue;
            }

            foreach (var extension in mediaFormat.FileExtensions)
            {
                _supportedMediaFormats.Add(extension, mediaFormat);
            }
        }
    }

    private MediaFormat(HashSet<string> fileExtensions, MediaType mediaType)
    {
        FileExtensions = fileExtensions;
        MediaType = mediaType;
    }

    public static MediaFormat MediaFormatCr3 { get; } = new([".CR3"], MediaType.Image);

    public static MediaFormat MediaFormatJpg { get; } = new([".jpg", ".jpeg"], MediaType.Image);

    public static MediaFormat MediaFormatMp4 { get; } = new([".mp4"], MediaType.Video);

    public static MediaFormat MediaFormatUnknown { get; } = new(["-"], MediaType.Unknown);

    public IReadOnlySet<string> FileExtensions { get; }

    public MediaType MediaType { get; }

    public static bool IsSupportedExtension(string fileInfoExtension)
    {
        return _supportedMediaFormats.ContainsKey(fileInfoExtension);
    }

    internal static MediaFormat Create(FileModel fileModel)
    {
        var fileInfo = new FileInfo(fileModel.FullName);
        return _supportedMediaFormats.GetValueOrDefault(fileInfo.Extension, MediaFormatUnknown);
    }

    /// <inheritdoc />
    public bool Equals(MediaFormat? other)
    {
        if (ReferenceEquals(null, other))
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return FileExtensions.SequenceEqual(other.FileExtensions)
            && MediaType == other.MediaType;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || obj is MediaFormat other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(FileExtensions, (int)MediaType);
    }

    public static bool operator ==(MediaFormat? left, MediaFormat? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(MediaFormat? left, MediaFormat? right)
    {
        return !Equals(left, right);
    }
}