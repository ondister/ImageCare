using System.Reflection;

using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Exceptions;
using ImageCare.Core.Services.MediaPreviewService;

namespace ImageCare.Core.Tests.Services.MediaPreviewService;

[TestFixture]
public class CommonMediaPreviewServiceTests
{
	private string _testImagesPath;

	[SetUp]
	public void SetUp()
	{
		_testImagesPath = GetTestImagesPath();
	}

	[Test]
	public async Task GetJpegImageStreamAsync_WithJpgFile_ReturnsStream()
	{
		using var service = new CommonMediaPreviewService();
		var imagePath = GetTestImagePath("test.JPG");
		var mediaPreview = await service.GetMediaPreviewAsync(imagePath);

		var stream = await service.GetJpegImageStreamAsync(mediaPreview, MediaPreviewSize.Medium);

		Assert.IsNotNull(stream);
		Assert.IsTrue(stream.Length > 0);
	}

	[Test]
	public async Task GetJpegImageStreamAsync_WithCr3File_ReturnsStream()
	{
		using var service = new CommonMediaPreviewService();
		var imagePath = GetTestImagePath("canon_geo.CR3");
		var mediaPreview = await service.GetMediaPreviewAsync(imagePath);

		var stream = await service.GetJpegImageStreamAsync(mediaPreview, MediaPreviewSize.Medium);

		Assert.IsNotNull(stream);
		Assert.IsTrue(stream.Length > 0);
	}

	[Test]
	public async Task GetJpegImageStreamAsync_WithArwFile_ReturnsStream()
	{
		using var service = new CommonMediaPreviewService();
		var imagePath = GetTestImagePath("sony.ARW");
		var mediaPreview = await service.GetMediaPreviewAsync(imagePath);

		var stream = await service.GetJpegImageStreamAsync(mediaPreview, MediaPreviewSize.Medium);

		Assert.IsNotNull(stream);
		Assert.IsTrue(stream.Length > 0);
	}

	[Test]
	public async Task GetMediaPreviewAsync_WithJpgFile_ReturnsCorrectMediaPreview()
	{
		using var service = new CommonMediaPreviewService();
		var imagePath = GetTestImagePath("test.JPG");

		var result = await service.GetMediaPreviewAsync(imagePath);

		Assert.IsNotNull(result);
		Assert.AreEqual(MediaFormat.MediaFormatJpg, result.MediaFormat);
		Assert.AreEqual("test.JPG", result.Title);
		Assert.AreEqual(imagePath, result.Url);
		Assert.AreEqual(200, result.MaxImageHeight);
	}

	[Test]
	public async Task GetMediaPreviewAsync_WithCr3File_ReturnsCr3Format()
	{
		using var service = new CommonMediaPreviewService();
		var imagePath = GetTestImagePath("canon_geo.CR3");

		var result = await service.GetMediaPreviewAsync(imagePath);

		Assert.IsNotNull(result);
		Assert.AreEqual(MediaFormat.MediaFormatCr3, result.MediaFormat);
	}

	[Test]
	public async Task GetMediaPreviewAsync_WithArwFile_ReturnsArwFormat()
	{
		using var service = new CommonMediaPreviewService();
		var imagePath = GetTestImagePath("sony.ARW");

		var result = await service.GetMediaPreviewAsync(imagePath);

		Assert.IsNotNull(result);
		Assert.AreEqual(MediaFormat.MediaFormatArw, result.MediaFormat);
	}

	[Test]
	public async Task GetMediaPreviewAsync_WithMp4File_ReturnsMp4Format()
	{
		using var service = new CommonMediaPreviewService();
		var imagePath = GetTestImagePath("test.MP4");

		var result = await service.GetMediaPreviewAsync(imagePath);

		Assert.IsNotNull(result);
		Assert.AreEqual(MediaFormat.MediaFormatMp4, result.MediaFormat);
	}

	[Test]
	public async Task GetMediaMetadataAsync_WithJpgFile_ReturnsMetadata()
	{
		using var service = new CommonMediaPreviewService();
		var imagePath = GetTestImagePath("test.JPG");
		var mediaPreview = await service.GetMediaPreviewAsync(imagePath);

		var metadata = await service.GetMediaMetadataAsync(mediaPreview);

		Assert.IsNotNull(metadata);
		Assert.IsTrue(metadata.Width > 0);
		Assert.IsTrue(metadata.Height > 0);
	}

	[Test]
	public async Task GetMediaMetadataAsync_WithCr3File_ReturnsMetadata()
	{
		using var service = new CommonMediaPreviewService();
		var imagePath = GetTestImagePath("canon_geo.CR3");
		var mediaPreview = await service.GetMediaPreviewAsync(imagePath);

		var metadata = await service.GetMediaMetadataAsync(mediaPreview);

		Assert.IsNotNull(metadata);
	}

	[Test]
	public async Task GetMediaMetadataAsync_WithArwFile_ReturnsMetadata()
	{
		using var service = new CommonMediaPreviewService();
		var imagePath = GetTestImagePath("sony.ARW");
		var mediaPreview = await service.GetMediaPreviewAsync(imagePath);

		var metadata = await service.GetMediaMetadataAsync(mediaPreview);

		Assert.IsNotNull(metadata);
	}

	[Test]
	public async Task GetMediaMetadataAsync_CalledTwice_ReturnsCachedMetadata()
	{
		using var service = new CommonMediaPreviewService();
		var imagePath = GetTestImagePath("test.JPG");
		var mediaPreview = await service.GetMediaPreviewAsync(imagePath);

		var metadata1 = await service.GetMediaMetadataAsync(mediaPreview);
		var metadata2 = await service.GetMediaMetadataAsync(mediaPreview);

		Assert.IsNotNull(metadata1);
		Assert.IsNotNull(metadata2);
		Assert.AreEqual(metadata1.CreationDateTime, metadata2.CreationDateTime);
	}

	[Test]
	public void GetMediaMetadataAsync_WithInvalidMediaPreview_ThrowsServiceException()
	{
		using var service = new CommonMediaPreviewService();
		var mediaPreview = new MediaPreview("Test", "invalid.jpg", MediaFormat.MediaFormatJpg, 200);

		Assert.ThrowsAsync<ServiceException>(() => service.GetMediaMetadataAsync(mediaPreview));
	}

	[Test]
	public async Task GetCreationDateTime_WithJpgFile_ReturnsValidDateTime()
	{
		using var service = new CommonMediaPreviewService();
		var imagePath = GetTestImagePath("test.JPG");
		var mediaPreview = await service.GetMediaPreviewAsync(imagePath);

		var creationDate = await service.GetCreationDateTime(mediaPreview);

		Assert.IsTrue(creationDate > DateTime.MinValue);
		Assert.IsTrue(creationDate < DateTime.MaxValue);
	}

	[Test]
	public async Task GetCreationDateTime_WithCr3File_ReturnsValidDateTime()
	{
		using var service = new CommonMediaPreviewService();
		var imagePath = GetTestImagePath("canon_geo.CR3");
		var mediaPreview = await service.GetMediaPreviewAsync(imagePath);

		var creationDate = await service.GetCreationDateTime(mediaPreview);

		Assert.IsTrue(creationDate > DateTime.MinValue);
	}

	[Test]
	public async Task GetCreationDateTime_WithArwFile_ReturnsValidDateTime()
	{
		using var service = new CommonMediaPreviewService();
		var imagePath = GetTestImagePath("sony.ARW");
		var mediaPreview = await service.GetMediaPreviewAsync(imagePath);

		var creationDate = await service.GetCreationDateTime(mediaPreview);

		Assert.IsTrue(creationDate > DateTime.MinValue);
	}

	[Test]
	public void GetCreationDateTime_WithInvalidMediaPreview_ThrowsServiceException()
	{
		using var service = new CommonMediaPreviewService();
		var mediaPreview = new MediaPreview("Test", "invalid.jpg", MediaFormat.MediaFormatJpg, 200);

		Assert.ThrowsAsync<ServiceException>(() => service.GetCreationDateTime(mediaPreview));
	}

	[Test]
	public void Dispose_WhenCalled_PreventsNewOperations()
	{
		var service = new CommonMediaPreviewService();
		service.Dispose();

		var mediaPreview = new MediaPreview("Test", "test.jpg", MediaFormat.MediaFormatJpg, 200);

		var exception = Assert.ThrowsAsync<ServiceException>(async () =>
			                                                     await service.GetMediaMetadataAsync(mediaPreview));

		Assert.IsInstanceOf<ObjectDisposedException>(exception.InnerException);
	}

	[Test]
	public async Task DifferentPreviewSizes_ReturnDifferentStreams()
	{
		using var service = new CommonMediaPreviewService();
		var imagePath = GetTestImagePath("test.JPG");
		var mediaPreview = await service.GetMediaPreviewAsync(imagePath);

		var smallStream = await service.GetJpegImageStreamAsync(mediaPreview, MediaPreviewSize.Small);
		var mediumStream = await service.GetJpegImageStreamAsync(mediaPreview, MediaPreviewSize.Medium);
		var largeStream = await service.GetJpegImageStreamAsync(mediaPreview, MediaPreviewSize.Large);

		Assert.IsNotNull(smallStream);
		Assert.IsNotNull(mediumStream);
		Assert.IsNotNull(largeStream);
	}

	[Test]
	public async Task CancellationToken_WhenCancelled_ThrowsOperationCanceledException()
	{
		using var service = new CommonMediaPreviewService();
		var imagePath = GetTestImagePath("test.JPG");
		var mediaPreview = await service.GetMediaPreviewAsync(imagePath);
		var cancellationTokenSource = new CancellationTokenSource();

		cancellationTokenSource.Cancel();

		Assert.ThrowsAsync<OperationCanceledException>(() =>
			                                               service.GetJpegImageStreamAsync(mediaPreview, MediaPreviewSize.Medium, cancellationTokenSource.Token));
	}

	private string GetTestImagesPath()
	{
		var assemblyLocation = Assembly.GetExecutingAssembly().Location;
		var assemblyDirectory = Path.GetDirectoryName(assemblyLocation);
		var projectDirectory = Directory.GetParent(assemblyDirectory).Parent.Parent.FullName;
		return Path.Combine(projectDirectory, "TestImages");
	}

	private string GetTestImagePath(string fileName)
	{
		var fullPath = Path.Combine(_testImagesPath, fileName);
		if (!File.Exists(fullPath))
		{
			throw new FileNotFoundException($"Test image not found: {fullPath}");
		}

		return fullPath;
	}
}