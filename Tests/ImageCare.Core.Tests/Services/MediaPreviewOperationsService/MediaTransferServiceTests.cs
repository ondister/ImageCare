using ImageCare.Core.Exceptions;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.MediaPreviewOperationsService;

using Moq;

namespace ImageCare.Core.Tests.Services.MediaPreviewOperationsService;

[TestFixture]
public class MediaTransferServiceTests
{
    private const string SourceDirectory = "source";
    private const string DestinationDirectory = "target";

    private Mock<IFileSystemService> _fileSystemServiceMock;
    private HashSet<string> _files;
    private MediaTransferService _service;

    [SetUp]
    public void SetUp()
    {
        _files = new HashSet<string>(StringComparer.Ordinal);
        _fileSystemServiceMock = new Mock<IFileSystemService>();
        _fileSystemServiceMock
            .Setup(fileSystem => fileSystem.DirectoryExists(DestinationDirectory))
            .Returns(true);
        _fileSystemServiceMock
            .Setup(fileSystem => fileSystem.GetFiles(DestinationDirectory))
            .Returns(Array.Empty<string>());
        _fileSystemServiceMock
            .Setup(fileSystem => fileSystem.FileExists(It.IsAny<string>()))
            .Returns((string path) => _files.Contains(path));
        _service = new MediaTransferService(_fileSystemServiceMock.Object);
    }

    [Test]
    public void CopyToDirectory_WithMediaOnly_CopiesMediaAndReturnsPaths()
    {
        var sourcePath = Path.Combine(SourceDirectory, "photo.jpg");
        var destinationPath = Path.Combine(DestinationDirectory, "photo.jpg");
        _files.Add(sourcePath);

        var result = _service.CopyToDirectory(sourcePath, DestinationDirectory);

        Assert.That(result, Is.EqualTo(new MediaTransferResult(sourcePath, destinationPath)));
        _fileSystemServiceMock.Verify(
            fileSystem => fileSystem.CopyFile(sourcePath, destinationPath),
            Times.Once);
    }

    [Test]
    public void MoveToDirectory_WithMediaOnly_MovesMediaAndReturnsPaths()
    {
        var sourcePath = Path.Combine(SourceDirectory, "photo.jpg");
        var destinationPath = Path.Combine(DestinationDirectory, "photo.jpg");
        _files.Add(sourcePath);

        var result = _service.MoveToDirectory(sourcePath, DestinationDirectory);

        Assert.That(result, Is.EqualTo(new MediaTransferResult(sourcePath, destinationPath)));
        _fileSystemServiceMock.Verify(
            fileSystem => fileSystem.MoveFile(sourcePath, destinationPath),
            Times.Once);
    }

    [Test]
    public void CopyToDirectory_WithExternalSidecar_CopiesSidecarBeforeMediaAndReturnsMediaPaths()
    {
        var sourcePath = Path.Combine(SourceDirectory, "clip.mp4");
        var sourceSidecarPath = sourcePath + ".xmp";
        var destinationPath = Path.Combine(DestinationDirectory, "clip.mp4");
        var destinationSidecarPath = destinationPath + ".xmp";
        var operations = new List<(string Source, string Destination)>();
        _files.UnionWith([sourcePath, sourceSidecarPath]);
        _fileSystemServiceMock
            .Setup(fileSystem => fileSystem.CopyFile(It.IsAny<string>(), It.IsAny<string>()))
            .Callback((string source, string destination) => operations.Add((source, destination)));

        var result = _service.CopyToDirectory(sourcePath, DestinationDirectory);

        Assert.That(result, Is.EqualTo(new MediaTransferResult(sourcePath, destinationPath)));
        Assert.That(operations, Is.EqualTo(new[]
        {
            (sourceSidecarPath, destinationSidecarPath),
            (sourcePath, destinationPath)
        }));
    }

    [Test]
    public void MoveToDirectory_WithExternalSidecar_MovesSidecarBeforeMediaAndReturnsMediaPaths()
    {
        var sourcePath = Path.Combine(SourceDirectory, "clip.mp4");
        var sourceSidecarPath = sourcePath + ".xmp";
        var destinationPath = Path.Combine(DestinationDirectory, "clip.mp4");
        var destinationSidecarPath = destinationPath + ".xmp";
        var operations = new List<(string Source, string Destination)>();
        _files.UnionWith([sourcePath, sourceSidecarPath]);
        _fileSystemServiceMock
            .Setup(fileSystem => fileSystem.MoveFile(It.IsAny<string>(), It.IsAny<string>()))
            .Callback((string source, string destination) => operations.Add((source, destination)));

        var result = _service.MoveToDirectory(sourcePath, DestinationDirectory);

        Assert.That(result, Is.EqualTo(new MediaTransferResult(sourcePath, destinationPath)));
        Assert.That(operations, Is.EqualTo(new[]
        {
            (sourceSidecarPath, destinationSidecarPath),
            (sourcePath, destinationPath)
        }));
    }

    [Test]
    public void CopyToDirectory_WhenDefaultNameCollides_UsesUniqueName()
    {
        var sourcePath = Path.Combine(SourceDirectory, "photo.jpg");
        var existingPath = Path.Combine(DestinationDirectory, "photo.jpg");
        var destinationPath = Path.Combine(DestinationDirectory, "photo (1).jpg");
        _files.UnionWith([sourcePath, existingPath]);
        _fileSystemServiceMock
            .Setup(fileSystem => fileSystem.GetFiles(DestinationDirectory))
            .Returns([existingPath]);

        var result = _service.CopyToDirectory(sourcePath, DestinationDirectory);

        Assert.That(result.DestinationPath, Is.EqualTo(destinationPath));
        _fileSystemServiceMock.Verify(
            fileSystem => fileSystem.CopyFile(sourcePath, destinationPath),
            Times.Once);
    }

    [Test]
    public void CopyToDirectory_WhenExplicitDestinationCollides_ThrowsServiceException()
    {
        var sourcePath = Path.Combine(SourceDirectory, "photo.jpg");
        var destinationPath = Path.Combine(DestinationDirectory, "renamed.jpg");
        _files.UnionWith([sourcePath, destinationPath]);

        var exception = Assert.Throws<ServiceException>(() =>
            _service.CopyToDirectory(sourcePath, DestinationDirectory, "renamed.jpg"));

        Assert.That(
            exception!.Message,
            Is.EqualTo($"Destination file or sidecar already exists: {destinationPath}"));
        _fileSystemServiceMock.Verify(
            fileSystem => fileSystem.CopyFile(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Test]
    public void CopyToDirectory_WhenExplicitNameIsInvalid_ThrowsServiceException()
    {
        var sourcePath = Path.Combine(SourceDirectory, "photo.jpg");
        _files.Add(sourcePath);

        var exception = Assert.Throws<ServiceException>(() =>
            _service.CopyToDirectory(sourcePath, DestinationDirectory, "../renamed.jpg"));

        Assert.That(exception!.Message, Is.EqualTo("Invalid destination file name: ../renamed.jpg"));
        _fileSystemServiceMock.Verify(
            fileSystem => fileSystem.CopyFile(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Test]
    public void MoveBundle_WhenMediaMoveFails_RollsSidecarBack()
    {
        var sourcePath = Path.Combine(SourceDirectory, "clip.mp4");
        var sourceSidecarPath = sourcePath + ".xmp";
        var destinationPath = Path.Combine(DestinationDirectory, "clip.mp4");
        var destinationSidecarPath = destinationPath + ".xmp";
        var operations = new List<(string Source, string Destination)>();
        _files.UnionWith([sourcePath, sourceSidecarPath]);
        _fileSystemServiceMock
            .Setup(fileSystem => fileSystem.MoveFile(It.IsAny<string>(), It.IsAny<string>()))
            .Callback((string source, string destination) =>
            {
                operations.Add((source, destination));
                if (source == sourcePath)
                {
                    throw new UnauthorizedAccessException("media move failed");
                }

                _files.Remove(source);
                _files.Add(destination);
            });

        var exception = Assert.Throws<UnauthorizedAccessException>(() =>
            _service.MoveBundle(
                sourcePath,
                destinationPath,
                sourceSidecarPath,
                destinationSidecarPath));

        Assert.That(exception!.Message, Is.EqualTo("media move failed"));
        Assert.That(operations, Is.EqualTo(new[]
        {
            (sourceSidecarPath, destinationSidecarPath),
            (sourcePath, destinationPath),
            (destinationSidecarPath, sourceSidecarPath)
        }));
        Assert.That(_files, Does.Contain(sourceSidecarPath));
        Assert.That(_files, Does.Not.Contain(destinationSidecarPath));
    }

    [Test]
    public void MoveBundle_WhenMediaMoveAndRollbackFail_ThrowsAggregateException()
    {
        var sourcePath = Path.Combine(SourceDirectory, "clip.mp4");
        var sourceSidecarPath = sourcePath + ".xmp";
        var destinationPath = Path.Combine(DestinationDirectory, "clip.mp4");
        var destinationSidecarPath = destinationPath + ".xmp";
        _files.UnionWith([sourcePath, sourceSidecarPath]);
        _fileSystemServiceMock
            .Setup(fileSystem => fileSystem.MoveFile(sourceSidecarPath, destinationSidecarPath))
            .Callback(() =>
            {
                _files.Remove(sourceSidecarPath);
                _files.Add(destinationSidecarPath);
            });
        _fileSystemServiceMock
            .Setup(fileSystem => fileSystem.MoveFile(sourcePath, destinationPath))
            .Throws(new UnauthorizedAccessException("media move failed"));
        _fileSystemServiceMock
            .Setup(fileSystem => fileSystem.MoveFile(destinationSidecarPath, sourceSidecarPath))
            .Throws(new InvalidOperationException("rollback failed"));

        var exception = Assert.Throws<AggregateException>(() =>
            _service.MoveBundle(
                sourcePath,
                destinationPath,
                sourceSidecarPath,
                destinationSidecarPath));

        Assert.That(exception!.InnerExceptions, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(exception.InnerExceptions[0].Message, Is.EqualTo("media move failed"));
            Assert.That(exception.InnerExceptions[1].Message, Is.EqualTo("rollback failed"));
        });
    }

    [Test]
    public void MoveFileReliably_WhenDirectMoveCrossesVolume_CopiesMetadataThenDeletesSource()
    {
        var sourcePath = Path.Combine(SourceDirectory, "photo.jpg");
        var destinationPath = Path.Combine(DestinationDirectory, "photo.jpg");
        var operations = new List<string>();
        _files.Add(sourcePath);
        _fileSystemServiceMock
            .Setup(fileSystem => fileSystem.MoveFile(sourcePath, destinationPath))
            .Callback(() => operations.Add("move"))
            .Throws(new IOException("cross-volume"));
        _fileSystemServiceMock
            .Setup(fileSystem => fileSystem.CopyFile(sourcePath, destinationPath))
            .Callback(() =>
            {
                operations.Add("copy");
                _files.Add(destinationPath);
            });
        _fileSystemServiceMock
            .Setup(fileSystem => fileSystem.CopyFileMetadata(sourcePath, destinationPath))
            .Callback(() => operations.Add("metadata"));
        _fileSystemServiceMock
            .Setup(fileSystem => fileSystem.SafeDelete(sourcePath))
            .Callback(() =>
            {
                operations.Add("delete-source");
                _files.Remove(sourcePath);
            });

        _service.MoveFileReliably(sourcePath, destinationPath);

        Assert.That(operations, Is.EqualTo(new[] { "move", "copy", "metadata", "delete-source" }));
        Assert.That(_files, Does.Not.Contain(sourcePath));
        Assert.That(_files, Does.Contain(destinationPath));
    }

    [Test]
    public void MoveFileReliably_WhenFallbackMetadataCopyFails_CleansDestinationAndPreservesSource()
    {
        var sourcePath = Path.Combine(SourceDirectory, "photo.jpg");
        var destinationPath = Path.Combine(DestinationDirectory, "photo.jpg");
        var operations = new List<string>();
        _files.Add(sourcePath);
        _fileSystemServiceMock
            .Setup(fileSystem => fileSystem.MoveFile(sourcePath, destinationPath))
            .Callback(() => operations.Add("move"))
            .Throws(new IOException("cross-volume"));
        _fileSystemServiceMock
            .Setup(fileSystem => fileSystem.CopyFile(sourcePath, destinationPath))
            .Callback(() =>
            {
                operations.Add("copy");
                _files.Add(destinationPath);
            });
        _fileSystemServiceMock
            .Setup(fileSystem => fileSystem.CopyFileMetadata(sourcePath, destinationPath))
            .Callback(() => operations.Add("metadata"))
            .Throws(new IOException("metadata failed"));
        _fileSystemServiceMock
            .Setup(fileSystem => fileSystem.SafeDelete(destinationPath))
            .Callback(() =>
            {
                operations.Add("cleanup-destination");
                _files.Remove(destinationPath);
            });

        var exception = Assert.Throws<IOException>(() =>
            _service.MoveFileReliably(sourcePath, destinationPath));

        Assert.That(exception!.Message, Is.EqualTo("metadata failed"));
        Assert.That(operations, Is.EqualTo(new[] { "move", "copy", "metadata", "cleanup-destination" }));
        Assert.That(_files, Does.Contain(sourcePath));
        Assert.That(_files, Does.Not.Contain(destinationPath));
        _fileSystemServiceMock.Verify(
            fileSystem => fileSystem.SafeDelete(sourcePath),
            Times.Never);
    }
}
