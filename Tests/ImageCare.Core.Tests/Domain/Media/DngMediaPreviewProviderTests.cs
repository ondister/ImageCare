using ImageCare.Core.Domain.Media;
using ImageCare.Core.Domain.Media.Metadata;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Exceptions;

namespace ImageCare.Core.Tests.Domain.Media;

[TestFixture]
public class DngMediaPreviewProviderTests
{
	private const string _dngFileName = "dng_test.dng";
	private DngMediaPreviewProvider _provider;
	private string _testImagesPath;

	[SetUp]
	public void SetUp()
	{
		_provider = new DngMediaPreviewProvider();
		_testImagesPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "TestImages");
	}

	[Test]
	public void GetMediaMetadata_WithValidDngFile_ReturnsRawMediaMetadata()
	{
		var testFile = GetTestFile();

		var result = _provider.GetMediaMetadata(testFile);

		Assert.That(result, Is.InstanceOf<RawMediaMetadata>());
		Assert.That(result.Width, Is.EqualTo(6000));
		Assert.That(result.Height, Is.EqualTo(4000));
		Assert.That(result.CreationDateTime, Is.EqualTo(new DateTime(2025, 2, 27, 17, 3, 15)));
	}

	[Test]
	public void GetMediaMetadata_WithValidDngFile_ContainsCameraParameters()
	{
		var testFile = GetTestFile();

		var result = _provider.GetMediaMetadata(testFile) as RawMediaMetadata;

		Assert.That(result, Is.Not.Null);
		Assert.That(result!.Iso, Is.EqualTo(5000));
		Assert.That(result.Aperture, Is.Not.Null.And.Not.Empty);
		Assert.That(result.ShutterSpeed, Is.Not.Null.And.Not.Empty);
		Assert.That(result.Orientation, Is.EqualTo(ExifOrientation.TopLeft));
	}

	[Test]
	public void GetMediaMetadata_WithValidDngFile_ContainsLocation()
	{
		var testFile = GetTestFile();

		var result = _provider.GetMediaMetadata(testFile);

		Assert.That(result.Location, Is.Not.EqualTo(Location.Empty));
		Assert.That(result.Location.Latitude, Is.EqualTo(53.7039805556).Within(0.00001));
		Assert.That(result.Location.Longitude, Is.EqualTo(27.6160611111).Within(0.00001));
		Assert.That(result.Location.Altitude, Is.EqualTo(220.42).Within(0.01));
	}

	[Test]
	public void GetMediaMetadata_WithNonExistentFile_ThrowsMediaPreviewProviderException()
	{
		var invalidPath = Path.Combine(_testImagesPath, "nonexistent.dng");

		Assert.Throws<MediaPreviewProviderException>(() => _provider.GetMediaMetadata(invalidPath));
	}

	[Test]
	public void GetMediaMetadata_WithUnsupportedFile_ThrowsMediaPreviewProviderException()
	{
		var invalidFile = Path.Combine(_testImagesPath, $"invalid-{Guid.NewGuid():N}.dng");
		File.WriteAllText(invalidFile, "invalid content");

		try
		{
			Assert.Throws<MediaPreviewProviderException>(() => _provider.GetMediaMetadata(invalidFile));
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
	public void GetPreviewJpegStream_WithDifferentSizes_ReturnsJpegStreams()
	{
		var testFile = GetTestFile();
		var sizes = new[] { MediaPreviewSize.Small, MediaPreviewSize.Medium, MediaPreviewSize.Large };

		foreach (var size in sizes)
		{
			using var stream = _provider.GetPreviewJpegStream(testFile, size);
			var header = new byte[3];
			var read = stream.Read(header, 0, header.Length);

			Assert.That(stream.Length, Is.GreaterThan(0));
			Assert.That(read, Is.EqualTo(3));
			Assert.That(header, Is.EqualTo(new byte[] { 0xFF, 0xD8, 0xFF }));
		}
	}

	[Test]
	public void GetPreviewJpegStream_WithNonExistentFile_ThrowsMediaPreviewProviderException()
	{
		var invalidPath = Path.Combine(_testImagesPath, "nonexistent.dng");

		Assert.Throws<MediaPreviewProviderException>(() =>
			_provider.GetPreviewJpegStream(invalidPath, MediaPreviewSize.Medium));
	}

	[Test]
	public void GetPreviewJpegStream_WithInvalidSize_ThrowsMediaPreviewProviderException()
	{
		var testFile = GetTestFile();

		Assert.Throws<MediaPreviewProviderException>(() =>
			_provider.GetPreviewJpegStream(testFile, (MediaPreviewSize)999));
	}

	[Test]
	public void GetCreationDateTime_WithValidDngFile_ReturnsValidDateTime()
	{
		var testFile = GetTestFile();

		var result = _provider.GetCreationDateTime(testFile);

		Assert.That(result, Is.EqualTo(new DateTime(2025, 2, 27, 17, 3, 15)));
	}

	[Test]
	public void GetCreationDateTime_WithNonExistentFile_ThrowsMediaPreviewProviderException()
	{
		var invalidPath = Path.Combine(_testImagesPath, "nonexistent.dng");

		Assert.Throws<MediaPreviewProviderException>(() => _provider.GetCreationDateTime(invalidPath));
	}

	private string GetTestFile()
	{
		var path = Path.Combine(_testImagesPath, _dngFileName);
		if (!File.Exists(path))
		{
			Assert.Ignore("Test file not found");
		}

		return path;
	}
}
