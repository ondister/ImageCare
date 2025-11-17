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

        Assert.That(stream, Is.Not.Null);
        Assert.That(stream.Length, Is.GreaterThan(0));
    }

    [Test]
    public async Task GetJpegImageStreamAsync_WithCr3File_ReturnsStream()
    {
        using var service = new CommonMediaPreviewService();
        var imagePath = GetTestImagePath("canon_geo.CR3");
        var mediaPreview = await service.GetMediaPreviewAsync(imagePath);

        var stream = await service.GetJpegImageStreamAsync(mediaPreview, MediaPreviewSize.Medium);

        Assert.That(stream, Is.Not.Null);
        Assert.That(stream.Length, Is.GreaterThan(0));
    }

    [Test]
    public async Task GetJpegImageStreamAsync_WithArwFile_ReturnsStream()
    {
        using var service = new CommonMediaPreviewService();
        var imagePath = GetTestImagePath("sony.ARW");
        var mediaPreview = await service.GetMediaPreviewAsync(imagePath);

        var stream = await service.GetJpegImageStreamAsync(mediaPreview, MediaPreviewSize.Medium);

        Assert.That(stream, Is.Not.Null);
        Assert.That(stream.Length, Is.GreaterThan(0));
    }

    [Test]
    public async Task GetMediaPreviewAsync_WithJpgFile_ReturnsCorrectMediaPreview()
    {
        using var service = new CommonMediaPreviewService();
        var imagePath = GetTestImagePath("test.JPG");

        var result = await service.GetMediaPreviewAsync(imagePath);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.MediaFormat, Is.EqualTo(MediaFormat.MediaFormatJpg));
        Assert.That(result.Title, Is.EqualTo("test.JPG"));
        Assert.That(result.Url, Is.EqualTo(imagePath));
        Assert.That(result.MaxImageHeight, Is.EqualTo(200));
    }

    [Test]
    public async Task GetMediaPreviewAsync_WithCr3File_ReturnsCr3Format()
    {
        using var service = new CommonMediaPreviewService();
        var imagePath = GetTestImagePath("canon_geo.CR3");

        var result = await service.GetMediaPreviewAsync(imagePath);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.MediaFormat, Is.EqualTo(MediaFormat.MediaFormatCr3));
    }

    [Test]
    public async Task GetMediaPreviewAsync_WithArwFile_ReturnsArwFormat()
    {
        using var service = new CommonMediaPreviewService();
        var imagePath = GetTestImagePath("sony.ARW");

        var result = await service.GetMediaPreviewAsync(imagePath);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.MediaFormat, Is.EqualTo(MediaFormat.MediaFormatArw));
    }

    [Test]
    public async Task GetMediaPreviewAsync_WithMp4File_ReturnsMp4Format()
    {
        using var service = new CommonMediaPreviewService();
        var imagePath = GetTestImagePath("test.MP4");

        var result = await service.GetMediaPreviewAsync(imagePath);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.MediaFormat, Is.EqualTo(MediaFormat.MediaFormatMp4));
    }

    [Test]
    public async Task GetMediaMetadataAsync_WithJpgFile_ReturnsMetadata()
    {
        using var service = new CommonMediaPreviewService();
        var imagePath = GetTestImagePath("test.JPG");
        var mediaPreview = await service.GetMediaPreviewAsync(imagePath);

        var metadata = await service.GetMediaMetadataAsync(mediaPreview);

        Assert.That(metadata, Is.Not.Null);
        Assert.That(metadata.Width, Is.GreaterThan(0));
        Assert.That(metadata.Height, Is.GreaterThan(0));
    }

    [Test]
    public async Task GetMediaMetadataAsync_WithCr3File_ReturnsMetadata()
    {
        using var service = new CommonMediaPreviewService();
        var imagePath = GetTestImagePath("canon_geo.CR3");
        var mediaPreview = await service.GetMediaPreviewAsync(imagePath);

        var metadata = await service.GetMediaMetadataAsync(mediaPreview);

        Assert.That(metadata, Is.Not.Null);
    }

    [Test]
    public async Task GetMediaMetadataAsync_WithArwFile_ReturnsMetadata()
    {
        using var service = new CommonMediaPreviewService();
        var imagePath = GetTestImagePath("sony.ARW");
        var mediaPreview = await service.GetMediaPreviewAsync(imagePath);

        var metadata = await service.GetMediaMetadataAsync(mediaPreview);

        Assert.That(metadata, Is.Not.Null);
    }

    [Test]
    public async Task GetMediaMetadataAsync_CalledTwice_ReturnsCachedMetadata()
    {
        using var service = new CommonMediaPreviewService();
        var imagePath = GetTestImagePath("test.JPG");
        var mediaPreview = await service.GetMediaPreviewAsync(imagePath);

        var metadata1 = await service.GetMediaMetadataAsync(mediaPreview);
        var metadata2 = await service.GetMediaMetadataAsync(mediaPreview);

        Assert.That(metadata1, Is.Not.Null);
        Assert.That(metadata2, Is.Not.Null);
        Assert.That(metadata2.CreationDateTime, Is.EqualTo(metadata1.CreationDateTime));
    }

    [Test]
    public void GetMediaMetadataAsync_WithInvalidMediaPreview_ThrowsServiceException()
    {
        using var service = new CommonMediaPreviewService();
        var mediaPreview = new MediaPreview("Test", "invalid.jpg", MediaFormat.MediaFormatJpg, 200);

        Assert.That(async () => await service.GetMediaMetadataAsync(mediaPreview),
            Throws.TypeOf<ServiceException>());
    }

    [Test]
    public async Task GetCreationDateTime_WithJpgFile_ReturnsValidDateTime()
    {
        using var service = new CommonMediaPreviewService();
        var imagePath = GetTestImagePath("test.JPG");
        var mediaPreview = await service.GetMediaPreviewAsync(imagePath);

        var creationDate = await service.GetCreationDateTime(mediaPreview);

        Assert.That(creationDate, Is.GreaterThan(DateTime.MinValue));
        Assert.That(creationDate, Is.LessThan(DateTime.MaxValue));
    }

    [Test]
    public async Task GetCreationDateTime_WithCr3File_ReturnsValidDateTime()
    {
        using var service = new CommonMediaPreviewService();
        var imagePath = GetTestImagePath("canon_geo.CR3");
        var mediaPreview = await service.GetMediaPreviewAsync(imagePath);

        var creationDate = await service.GetCreationDateTime(mediaPreview);

        Assert.That(creationDate, Is.GreaterThan(DateTime.MinValue));
    }

    [Test]
    public async Task GetCreationDateTime_WithArwFile_ReturnsValidDateTime()
    {
        using var service = new CommonMediaPreviewService();
        var imagePath = GetTestImagePath("sony.ARW");
        var mediaPreview = await service.GetMediaPreviewAsync(imagePath);

        var creationDate = await service.GetCreationDateTime(mediaPreview);

        Assert.That(creationDate, Is.GreaterThan(DateTime.MinValue));
    }

    [Test]
    public void GetCreationDateTime_WithInvalidMediaPreview_ThrowsServiceException()
    {
        using var service = new CommonMediaPreviewService();
        var mediaPreview = new MediaPreview("Test", "invalid.jpg", MediaFormat.MediaFormatJpg, 200);

        Assert.That(async () => await service.GetCreationDateTime(mediaPreview),
            Throws.TypeOf<ServiceException>());
    }

    [Test]
    public void Dispose_WhenCalled_PreventsNewOperations()
    {
        var service = new CommonMediaPreviewService();
        service.Dispose();

        var mediaPreview = new MediaPreview("Test", "test.jpg", MediaFormat.MediaFormatJpg, 200);

        Assert.That(async () => await service.GetMediaMetadataAsync(mediaPreview),
                    Throws.TypeOf<ServiceException>());
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

        Assert.That(smallStream, Is.Not.Null);
        Assert.That(mediumStream, Is.Not.Null);
        Assert.That(largeStream, Is.Not.Null);
    }

    [Test]
    public async Task CancellationToken_WhenCancelled_ThrowsOperationCanceledException()
    {
        using var service = new CommonMediaPreviewService();
        var imagePath = GetTestImagePath("test.JPG");
        var mediaPreview = await service.GetMediaPreviewAsync(imagePath);
        var cancellationTokenSource = new CancellationTokenSource();

        cancellationTokenSource.Cancel();

        Assert.That(async () => await service.GetJpegImageStreamAsync(mediaPreview, MediaPreviewSize.Medium, cancellationTokenSource.Token),
            Throws.TypeOf<OperationCanceledException>());
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