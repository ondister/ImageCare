namespace ImageCare.Core.Domain.Media;

/// <summary>
/// Extracts a display-oriented JPEG frame from a video file.
/// </summary>
public interface IVideoFrameExtractor
{
    byte[]? ExtractJpegFrame(string videoPath, TimeSpan position, int maxPixelSize);
}

internal sealed class NoOpVideoFrameExtractor : IVideoFrameExtractor
{
    public static NoOpVideoFrameExtractor Instance { get; } = new();

    private NoOpVideoFrameExtractor()
    {
    }

    public byte[]? ExtractJpegFrame(string videoPath, TimeSpan position, int maxPixelSize) => null;
}
