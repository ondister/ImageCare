using ImageCare.Core.Domain.MediaFormats;

namespace ImageCare.Core.Tests.Domain.MediaFormats;

[TestFixture]
public class MediaFormatTests
{
	[TestCase("MediaFormatJpg")]
	[TestCase("MediaFormatNef")]
	[TestCase("MediaFormatArw")]
	[TestCase("MediaFormatCr3")]
	public void SupportsAnnotations_ForImageFormat_ShouldReturnTrue(string formatName)
	{
		var format = Resolve(formatName);

		Assert.That(format.SupportsAnnotations, Is.True);
	}

	[TestCase("MediaFormatMp4")]
	[TestCase("MediaFormatUnknown")]
	public void SupportsAnnotations_ForNonImageFormat_ShouldReturnFalse(string formatName)
	{
		var format = Resolve(formatName);

		Assert.That(format.SupportsAnnotations, Is.False);
	}

	private static MediaFormat Resolve(string formatName)
	{
		var format = typeof(MediaFormat)
			.GetProperty(formatName)!
			.GetValue(null) as MediaFormat;

		return format ?? throw new InvalidOperationException($"Unknown media format {formatName}.");
	}
}
