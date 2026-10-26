using ImageCare.Core.Domain.Media;
using ImageCare.Core.Domain.MediaFormats;

namespace ImageCare.Core.Tests.Domain.Media;

[TestFixture]
public class MediaPreviewCapabilitiesTests
{
    [Test]
    public void Full_SupportsDng()
    {
        Assert.That(MediaPreviewCapabilities.Full.Supports(MediaFormat.MediaFormatDng), Is.True);
    }

    [Test]
    public void RawAndJpeg_SupportsDng()
    {
        Assert.That(MediaPreviewCapabilities.RawAndJpeg.Supports(MediaFormat.MediaFormatDng), Is.True);
    }

    [Test]
    public void JpegOnly_DoesNotSupportDng()
    {
        Assert.That(MediaPreviewCapabilities.JpegOnly.Supports(MediaFormat.MediaFormatDng), Is.False);
    }
}
