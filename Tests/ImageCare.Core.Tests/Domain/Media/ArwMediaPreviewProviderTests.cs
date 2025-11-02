using ImageCare.Core.Domain.Media;
using ImageCare.Core.Domain.Media.Metadata;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Exceptions;

namespace ImageCare.Core.Tests.Domain.Media;

[TestFixture]
public class ArwMediaPreviewProviderTests
{
	private const string _sonyFileName = "sony.ARW";
	private ArwMediaPreviewProvider _provider;
	private string _testImagesPath;

	[SetUp]
	public void SetUp()
	{
		_provider = new ArwMediaPreviewProvider();
		_testImagesPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "TestImages");
	}

	[Test]
	public void GetMediaMetadata_WithValidArwFile_ReturnsRawMediaMetadata()
	{
		var testFile = Path.Combine(_testImagesPath, _sonyFileName);
		if (!File.Exists(testFile))
		{
			Assert.Ignore("Test file not found");
		}

		var result = _provider.GetMediaMetadata(testFile);

		Assert.That(result, Is.InstanceOf<RawMediaMetadata>());
		Assert.That(result.Width, Is.GreaterThan(0));
		Assert.That(result.Height, Is.GreaterThan(0));
		Assert.That(result.CreationDateTime, Is.GreaterThan(DateTime.MinValue));
	}

	[Test]
	public void GetMediaMetadata_WithValidArwFile_ContainsCameraParameters()
	{
		var testFile = Path.Combine(_testImagesPath, _sonyFileName);
		if (!File.Exists(testFile))
		{
			Assert.Ignore("Test file not found");
		}

		var result = _provider.GetMediaMetadata(testFile) as RawMediaMetadata;

		Assert.That(result, Is.Not.Null);
		Assert.That(result.Iso, Is.GreaterThan(0));
		Assert.That(result.Aperture, Is.Not.Null.And.Not.Empty);
		Assert.That(result.ShutterSpeed, Is.Not.Null.And.Not.Empty);
	}

	[Test]
	public void GetMediaMetadata_WithNonExistentFile_ThrowsMediaPreviewProviderException()
	{
		var invalidPath = Path.Combine(_testImagesPath, "nonexistent.arw");

		Assert.Throws<MediaPreviewProviderException>(() =>
			                                             _provider.GetMediaMetadata(invalidPath));
	}

	[Test]
	public void GetMediaMetadata_WithUnsupportedFile_ReturnsUnsupportedMediaMetadata()
	{
		var invalidFile = Path.Combine(_testImagesPath, "invalid.arw");
		File.WriteAllText(invalidFile, "invalid content");

		try
		{
			var result = _provider.GetMediaMetadata(invalidFile);

			Assert.That(result, Is.InstanceOf<UnsupportedMediaMetadata>());
			Assert.That(result.Width, Is.EqualTo(0));
			Assert.That(result.Height, Is.EqualTo(0));
		}
		finally
		{
			if (File.Exists(invalidFile))
			{
				File.Delete(invalidFile);
			}
		}
	}

	[Test]
	public void GetPreviewJpegStream_WithValidArwFile_ReturnsValidStream()
	{
		var testFile = Path.Combine(_testImagesPath, _sonyFileName);
		if (!File.Exists(testFile))
		{
			Assert.Ignore("Test file not found");
		}

		using var stream = _provider.GetPreviewJpegStream(testFile, MediaPreviewSize.Medium);

		Assert.That(stream, Is.Not.Null);
		Assert.That(stream.CanRead, Is.True);
		Assert.That(stream.Length, Is.GreaterThan(0));
	}

	[Test]
	public void GetPreviewJpegStream_WithDifferentSizes_ReturnsStreams()
	{
		var testFile = Path.Combine(_testImagesPath, _sonyFileName);
		if (!File.Exists(testFile))
		{
			Assert.Ignore("Test file not found");
		}

		var sizes = new[] { MediaPreviewSize.Small, MediaPreviewSize.Medium, MediaPreviewSize.Large };

		foreach (var size in sizes)
		{
			using var stream = _provider.GetPreviewJpegStream(testFile, size);
			Assert.That(stream, Is.Not.Null);
			Assert.That(stream.Length, Is.GreaterThan(0));
		}
	}

	[Test]
	public void GetPreviewJpegStream_WithNonExistentFile_ThrowsMediaPreviewProviderException()
	{
		var invalidPath = Path.Combine(_testImagesPath, "nonexistent.arw");

		Assert.Throws<MediaPreviewProviderException>(() =>
			                                             _provider.GetPreviewJpegStream(invalidPath, MediaPreviewSize.Medium));
	}

	[Test]
	public void GetPreviewJpegStream_WithInvalidSize_ThrowsMediaPreviewProviderException()
	{
		var testFile = Path.Combine(_testImagesPath, _sonyFileName);
		if (!File.Exists(testFile))
		{
			Assert.Ignore("Test file not found");
		}

		Assert.Throws<MediaPreviewProviderException>(() =>
			                                             _provider.GetPreviewJpegStream(testFile, (MediaPreviewSize)999));
	}

	[Test]
	public void GetCreationDateTime_WithValidArwFile_ReturnsValidDateTime()
	{
		var testFile = Path.Combine(_testImagesPath, _sonyFileName);
		if (!File.Exists(testFile))
		{
			Assert.Ignore("Test file not found");
		}

		var result = _provider.GetCreationDateTime(testFile);

		Assert.That(result, Is.Not.Null);
		Assert.That(result, Is.GreaterThan(DateTime.MinValue));
	}

	[Test]
	public void GetCreationDateTime_WithNonExistentFile_ThrowsMediaPreviewProviderException()
	{
		var invalidPath = Path.Combine(_testImagesPath, "nonexistent.arw");

		Assert.Throws<MediaPreviewProviderException>(() =>
			                                             _provider.GetCreationDateTime(invalidPath));
	}

	[Test]
	public void GetCreationDateTime_WithUnsupportedFile_ReturnsValue()
	{
		var invalidFile = Path.Combine(_testImagesPath, "invalid.arw");
		File.WriteAllText(invalidFile, "invalid content");

		try
		{
			var result = _provider.GetCreationDateTime(invalidFile);

			Assert.That(result, Is.Not.Null);
			Assert.That(result, Is.GreaterThan(DateTime.MinValue));
		}
		finally
		{
			if (File.Exists(invalidFile))
			{
				File.Delete(invalidFile);
			}
		}
	}
}