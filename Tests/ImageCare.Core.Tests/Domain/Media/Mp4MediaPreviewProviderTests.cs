using ImageCare.Core.Domain.Media;
using ImageCare.Core.Domain.Media.Metadata;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Exceptions;

namespace ImageCare.Core.Tests.Domain.Media;

[TestFixture]
public class Mp4MediaPreviewProviderTests
{
	private const string _mp4FileName = "test.mp4";
	private Mp4MediaPreviewProvider _provider;
	private string _testImagesPath;

	[SetUp]
	public void SetUp()
	{
		_provider = new Mp4MediaPreviewProvider();
		_testImagesPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "TestImages");
	}

	[Test]
	public void GetMediaMetadata_WithValidMp4File_ReturnsVideoMediaMetadata()
	{
		var testFile = Path.Combine(_testImagesPath, _mp4FileName);
		if (!File.Exists(testFile))
		{
			Assert.Ignore("Test file not found");
		}

		var result = _provider.GetMediaMetadata(testFile);

		Assert.That(result, Is.InstanceOf<VideoMediaMetadata>());
		Assert.That(result.Width, Is.GreaterThan(0));
		Assert.That(result.Height, Is.GreaterThan(0));
		Assert.That(result.CreationDateTime, Is.GreaterThan(DateTime.MinValue));
	}

	[Test]
	public void GetMediaMetadata_WithValidMp4File_ContainsDuration()
	{
		var testFile = Path.Combine(_testImagesPath, _mp4FileName);
		if (!File.Exists(testFile))
		{
			Assert.Ignore("Test file not found");
		}

		var result = _provider.GetMediaMetadata(testFile) as VideoMediaMetadata;

		Assert.That(result, Is.Not.Null);
		Assert.That(result.Duration, Is.Not.EqualTo(TimeSpan.Zero));
		Assert.That(result.Duration, Is.GreaterThan(TimeSpan.Zero));

		TestContext.WriteLine($"Video Duration: {result.Duration}");
	}

	[Test]
	public void GetMediaMetadata_WithValidMp4File_ContainsVideoDimensions()
	{
		var testFile = Path.Combine(_testImagesPath, _mp4FileName);
		if (!File.Exists(testFile))
		{
			Assert.Ignore("Test file not found");
		}

		var result = _provider.GetMediaMetadata(testFile) as VideoMediaMetadata;

		Assert.That(result, Is.Not.Null);
		Assert.That(result.Width, Is.GreaterThan(0));
		Assert.That(result.Height, Is.GreaterThan(0));

		Assert.That(result.Width, Is.AnyOf(1920, 1280, 1080, 720, 640, 480));
		Assert.That(result.Height, Is.AnyOf(1080, 720, 480, 360));

		TestContext.WriteLine($"Video Dimensions: {result.Width}x{result.Height}");
	}

	[Test]
	public void GetMediaMetadata_WithNonExistentFile_ThrowsMediaPreviewProviderException()
	{
		var invalidPath = Path.Combine(_testImagesPath, "nonexistent.mp4");

		Assert.Throws<MediaPreviewProviderException>(() =>
			                                             _provider.GetMediaMetadata(invalidPath));
	}

	[Test]
	public void GetMediaMetadata_WithUnsupportedFile_ReturnsUnsupportedMediaMetadata()
	{
		var invalidFile = Path.Combine(_testImagesPath, "invalid.mp4");
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
	public void GetPreviewJpegStream_WithValidMp4File_ReturnsPreviewStream()
	{
		var testFile = Path.Combine(_testImagesPath, _mp4FileName);
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
	public void GetPreviewJpegStream_WithDifferentSizes_ReturnsSamePreviewStream()
	{
		var testFile = Path.Combine(_testImagesPath, _mp4FileName);
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
	public void GetPreviewJpegStream_WithNonExistentFile_DoesNotThrowMediaPreviewProviderException()
	{
		var invalidPath = Path.Combine(_testImagesPath, "nonexistent.mp4");

		Assert.DoesNotThrow(() =>
			                    _provider.GetPreviewJpegStream(invalidPath, MediaPreviewSize.Medium));
	}

	[Test]
	public void GetCreationDateTime_WithValidMp4File_ReturnsValidDateTime()
	{
		var testFile = Path.Combine(_testImagesPath, _mp4FileName);
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
		var invalidPath = Path.Combine(_testImagesPath, "nonexistent.mp4");

		Assert.Throws<MediaPreviewProviderException>(() =>
			                                             _provider.GetCreationDateTime(invalidPath));
	}

	[Test]
	public void GetCreationDateTime_WithUnsupportedFile_ReturnsValue()
	{
		var invalidFile = Path.Combine(_testImagesPath, "invalid.mp4");
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