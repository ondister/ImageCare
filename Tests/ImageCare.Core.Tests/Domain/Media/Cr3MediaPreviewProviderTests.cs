using ImageCare.Core.Domain.Media;
using ImageCare.Core.Domain.Media.Metadata;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Exceptions;

namespace ImageCare.Core.Tests.Domain.Media;

[TestFixture]
public class Cr3MediaPreviewProviderTests
{
	private const string _canonFileName = "canon_geo.CR3";
	private Cr3MediaPreviewProvider _provider;
	private string _testImagesPath;

	[SetUp]
	public void SetUp()
	{
		_provider = new Cr3MediaPreviewProvider();
		_testImagesPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "TestImages");
	}

	[Test]
	public void GetMediaMetadata_WithValidCr3File_ReturnsRawMediaMetadata()
	{
		var testFile = Path.Combine(_testImagesPath, _canonFileName);
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
	public void GetMediaMetadata_WithValidCr3File_ContainsCameraParameters()
	{
		var testFile = Path.Combine(_testImagesPath, _canonFileName);
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
	public void GetMediaMetadata_WithValidCr3File_ContainsLocation()
	{
		var testFile = Path.Combine(_testImagesPath, _canonFileName);
		if (!File.Exists(testFile))
		{
			Assert.Ignore("Test file not found");
		}

		var result = _provider.GetMediaMetadata(testFile) as RawMediaMetadata;

		Assert.That(result, Is.Not.Null);
		Assert.That(result.Location, Is.Not.EqualTo(Location.Empty));
	}

	[Test]
	public void GetMediaMetadata_WithNonExistentFile_ThrowsMediaPreviewProviderException()
	{
		var invalidPath = Path.Combine(_testImagesPath, "nonexistent.cr3");

		Assert.Throws<MediaPreviewProviderException>(() =>
			                                             _provider.GetMediaMetadata(invalidPath));
	}

	[Test]
	public void GetMediaMetadata_WithUnsupportedFile_ThrowsMediaPreviewProviderException()
	{
		var invalidFile = Path.Combine(_testImagesPath, "invalid.cr3");
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
	public void GetPreviewJpegStream_WithValidCr3File_ReturnsValidStream()
	{
		var testFile = Path.Combine(_testImagesPath, _canonFileName);
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
		var testFile = Path.Combine(_testImagesPath, _canonFileName);
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
		var invalidPath = Path.Combine(_testImagesPath, "nonexistent.cr3");

		Assert.Throws<MediaPreviewProviderException>(() =>
			                                             _provider.GetPreviewJpegStream(invalidPath, MediaPreviewSize.Medium));
	}

	[Test]
	public void GetCreationDateTime_WithValidCr3File_ReturnsValidDateTime()
	{
		var testFile = Path.Combine(_testImagesPath, _canonFileName);
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
		var invalidPath = Path.Combine(_testImagesPath, "nonexistent.cr3");

		Assert.Throws<MediaPreviewProviderException>(() =>
			                                             _provider.GetCreationDateTime(invalidPath));
	}

	[Test]
	public void GetCreationDateTime_WithUnsupportedFile_ThrowsMediaPreviewProviderException()
	{
		var invalidFile = Path.Combine(_testImagesPath, "invalid.cr3");
		File.WriteAllText(invalidFile, "invalid content");

		try
		{
			Assert.Throws<MediaPreviewProviderException>(() => _provider.GetCreationDateTime(invalidFile));
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
	public void GetMediaMetadata_WithGpsData_ReturnsCorrectLocation()
	{
		var testFile = Path.Combine(_testImagesPath, _canonFileName);
		if (!File.Exists(testFile))
		{
			Assert.Ignore("Test file not found");
		}

		var result = _provider.GetMediaMetadata(testFile) as RawMediaMetadata;

		Assert.That(result, Is.Not.Null);
		Assert.That(result.Location, Is.Not.EqualTo(Location.Empty));

		Assert.That(result.Location.Latitude, Is.InRange(-90.0, 90.0));
		Assert.That(result.Location.Longitude, Is.InRange(-180.0, 180.0));

		Assert.That(result.Location.Latitude, Is.EqualTo(Math.Round(result.Location.Latitude, 5)));
		Assert.That(result.Location.Longitude, Is.EqualTo(Math.Round(result.Location.Longitude, 5)));

		TestContext.WriteLine($"GPS Coordinates: Lat={result.Location.Latitude}, Lon={result.Location.Longitude}, Alt={result.Location.Altitude}");
	}
}