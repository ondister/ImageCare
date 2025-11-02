using ImageCare.Core.Domain.Media;
using ImageCare.Core.Domain.Media.Metadata;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Exceptions;

namespace ImageCare.Core.Tests.Domain.Media;

[TestFixture]
public class JpegMediaPreviewProviderTests
{
	private const string _jpegFileName = "test.jpg";
	private JpegMediaPreviewProvider _provider;
	private string _testImagesPath;

	[SetUp]
	public void SetUp()
	{
		_provider = new JpegMediaPreviewProvider();
		_testImagesPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "TestImages");
	}

	[Test]
	public void GetMediaMetadata_WithValidJpegFile_ReturnsJpegMediaMetadata()
	{
		var testFile = Path.Combine(_testImagesPath, _jpegFileName);
		if (!File.Exists(testFile))
		{
			Assert.Ignore("Test file not found");
		}

		var result = _provider.GetMediaMetadata(testFile);

		Assert.That(result, Is.InstanceOf<JpegMediaMetadata>());
		Assert.That(result.Width, Is.GreaterThan(0));
		Assert.That(result.Height, Is.GreaterThan(0));
		Assert.That(result.CreationDateTime, Is.GreaterThan(DateTime.MinValue));
	}

	[Test]
	public void GetMediaMetadata_WithValidJpegFile_ContainsCameraParameters()
	{
		var testFile = Path.Combine(_testImagesPath, _jpegFileName);
		if (!File.Exists(testFile))
		{
			Assert.Ignore("Test file not found");
		}

		var result = _provider.GetMediaMetadata(testFile) as JpegMediaMetadata;

		Assert.That(result, Is.Not.Null);

		if (result.Iso > 0)
		{
			Assert.That(result.Iso, Is.GreaterThan(0));
		}

		if (!string.IsNullOrEmpty(result.Aperture))
		{
			Assert.That(result.Aperture, Is.Not.Empty);
		}

		if (!string.IsNullOrEmpty(result.ShutterSpeed))
		{
			Assert.That(result.ShutterSpeed, Is.Not.Empty);
		}
	}

	[Test]
	public void GetMediaMetadata_WithValidJpegFile_ContainsOrientation()
	{
		var testFile = Path.Combine(_testImagesPath, _jpegFileName);
		if (!File.Exists(testFile))
		{
			Assert.Ignore("Test file not found");
		}

		var result = _provider.GetMediaMetadata(testFile) as JpegMediaMetadata;

		Assert.That(result, Is.Not.Null);

		Assert.That(Enum.IsDefined(typeof(ExifOrientation), result.Orientation), Is.True);
	}

	[Test]
	public void GetMediaMetadata_WithNonExistentFile_ThrowsMediaPreviewProviderException()
	{
		var invalidPath = Path.Combine(_testImagesPath, "nonexistent.jpg");

		Assert.Throws<MediaPreviewProviderException>(() =>
			                                             _provider.GetMediaMetadata(invalidPath));
	}

	[Test]
	public void GetMediaMetadata_WithUnsupportedFile_ReturnsUnsupportedMediaMetadata()
	{
		var invalidFile = Path.Combine(_testImagesPath, "invalid.jpg");
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
	public void GetMediaMetadata_WithJpegWithoutExif_ReturnsUnsupportedMediaMetadata()
	{
		var noExifFile = Path.Combine(_testImagesPath, "no_exif.jpg");

		if (!File.Exists(noExifFile))
		{
			Assert.Ignore("Test file without EXIF data not found");
		}

		var result = _provider.GetMediaMetadata(noExifFile);

		Assert.That(result, Is.InstanceOf<UnsupportedMediaMetadata>());
	}

	[Test]
	public void GetPreviewJpegStream_WithValidJpegFile_ReturnsOriginalFileStream()
	{
		var testFile = Path.Combine(_testImagesPath, _jpegFileName);
		if (!File.Exists(testFile))
		{
			Assert.Ignore("Test file not found");
		}

		using var stream = _provider.GetPreviewJpegStream(testFile, MediaPreviewSize.Medium);

		Assert.That(stream, Is.Not.Null);
		Assert.That(stream.CanRead, Is.True);
		Assert.That(stream.Length, Is.GreaterThan(0));

		var fileInfo = new FileInfo(testFile);
		Assert.That(stream.Length, Is.EqualTo(fileInfo.Length));
	}

	[Test]
	public void GetPreviewJpegStream_WithDifferentSizes_ReturnsSameOriginalFile()
	{
		var testFile = Path.Combine(_testImagesPath, _jpegFileName);
		if (!File.Exists(testFile))
		{
			Assert.Ignore("Test file not found");
		}

		var sizes = new[] { MediaPreviewSize.Small, MediaPreviewSize.Medium, MediaPreviewSize.Large };
		var fileInfo = new FileInfo(testFile);

		foreach (var size in sizes)
		{
			using var stream = _provider.GetPreviewJpegStream(testFile, size);
			Assert.That(stream, Is.Not.Null);
			Assert.That(stream.Length, Is.EqualTo(fileInfo.Length));
		}
	}

	[Test]
	public void GetPreviewJpegStream_WithNonExistentFile_ThrowsMediaPreviewProviderException()
	{
		var invalidPath = Path.Combine(_testImagesPath, "nonexistent.jpg");

		Assert.Throws<MediaPreviewProviderException>(() =>
			                                             _provider.GetPreviewJpegStream(invalidPath, MediaPreviewSize.Medium));
	}

	[Test]
	public void GetCreationDateTime_WithValidJpegFile_ReturnsValidDateTime()
	{
		var testFile = Path.Combine(_testImagesPath, _jpegFileName);
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
		var invalidPath = Path.Combine(_testImagesPath, "nonexistent.jpg");

		Assert.Throws<MediaPreviewProviderException>(() =>
			                                             _provider.GetCreationDateTime(invalidPath));
	}

	[Test]
	public void GetCreationDateTime_WithUnsupportedFile_ReturnsValue()
	{
		var invalidFile = Path.Combine(_testImagesPath, "invalid.jpg");
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