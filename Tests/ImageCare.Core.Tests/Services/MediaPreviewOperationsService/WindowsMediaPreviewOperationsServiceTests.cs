using ImageCare.Core.Domain;
using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Exceptions;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.ProcessService;

using Moq;

namespace ImageCare.Core.Tests.Services.MediaPreviewOperationsService;

[TestFixture]
public class WindowsMediaPreviewOperationsServiceTests
{
    private Mock<IFileSystemService> _fileSystemServiceMock;
    private Mock<IProcessService> _processServiceMock;
    private MediaFormat _testMediaFormat;

    [SetUp]
    public void SetUp()
    {
        _fileSystemServiceMock = new Mock<IFileSystemService>();
        _processServiceMock = new Mock<IProcessService>();
        _testMediaFormat = MediaFormat.MediaFormatJpg;
    }

    [Test]
    public void SetSelectedPreview_UpdatesLastSelectedAndNotifiesObservers()
    {
        var service = CreateService();
        var selectedPreview = CreateSelectedMediaPreview();

        SelectedMediaPreview? notifiedPreview = null;
        service.ImagePreviewSelected.Subscribe(preview => notifiedPreview = preview);

        service.SetSelectedPreview(selectedPreview);

        Assert.That(service.GetLastSelectedMediaPreview(), Is.EqualTo(selectedPreview));
        Assert.That(notifiedPreview, Is.EqualTo(selectedPreview));
    }

    [Test]
    public void CopyImagePreviewToDirectoryAsync_WhenDirectoryNotExists_ThrowsException()
    {
        var service = CreateService();
        var mediaPreview = CreateMediaPreview();

        _fileSystemServiceMock.Setup(x => x.DirectoryExists("targetDir")).Returns(false);

        Assert.That(() => service.CopyImagePreviewToDirectoryAsync(mediaPreview, "targetDir", new Progress<OperationInfo>()),
            Throws.TypeOf<ServiceException>());
    }

    [Test]
    public void CopyImagePreviewToDirectoryAsync_WhenDirectoryExists_CopiesFile()
    {
        var service = CreateService();
        var mediaPreview = CreateMediaPreview("source.jpg");
        var progress = new Progress<OperationInfo>();

        _fileSystemServiceMock.Setup(x => x.DirectoryExists("targetDir")).Returns(true);
        _fileSystemServiceMock.Setup(x => x.GetFiles("targetDir")).Returns(Array.Empty<string>());
        _fileSystemServiceMock.Setup(x => x.FileExists("source.jpg")).Returns(true);
        _fileSystemServiceMock.Setup(x => x.GetFileInfo("source.jpg")).Returns(new FileInfo("source.jpg"));
        _fileSystemServiceMock.Setup(x => x.OpenRead("source.jpg")).Returns(new MemoryStream());
        _fileSystemServiceMock.Setup(x => x.Create(It.IsAny<string>())).Returns(new MemoryStream());

        var result = service.CopyImagePreviewToDirectoryAsync(mediaPreview, "targetDir", progress).Result;

        Assert.That(result, Is.EqualTo(OperationResult.Success));
    }

    [Test]
    public void CopyImagePreviewToDirectoryAsync_PublishesCompletionOnlyAfterSuccessfulCommit()
    {
        var service = CreateService();
        var sourcePath = "source.jpg";
        var destinationPath = Path.Combine("targetDir", "source.jpg");
        var mediaPreview = CreateMediaPreview(sourcePath);
        var copyCommitted = false;
        MediaFileOperationCompleted? completedOperation = null;
        var eventObservedAfterCommit = false;
        _fileSystemServiceMock.Setup(x => x.DirectoryExists("targetDir")).Returns(true);
        _fileSystemServiceMock.Setup(x => x.GetFiles("targetDir")).Returns(Array.Empty<string>());
        _fileSystemServiceMock.Setup(x => x.FileExists(sourcePath)).Returns(true);
        _fileSystemServiceMock
            .Setup(x => x.CopyFile(sourcePath, destinationPath))
            .Callback(() => copyCommitted = true);
        service.FileOperationCompleted.Subscribe(operation =>
        {
            completedOperation = operation;
            eventObservedAfterCommit = copyCommitted;
        });

        var result = service.CopyImagePreviewToDirectoryAsync(
            mediaPreview,
            "targetDir",
            new Progress<OperationInfo>()).Result;

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(OperationResult.Success));
            Assert.That(eventObservedAfterCommit, Is.True);
            Assert.That(completedOperation, Is.EqualTo(new MediaFileOperationCompleted(
                MediaFileOperationKind.Copy,
                sourcePath,
                destinationPath)));
        });
    }

    [Test]
    public void CopyImagePreviewToDirectoryAsync_WhenCommitFails_DoesNotPublishCompletionAndWrapsFailure()
    {
        var service = CreateService();
        var sourcePath = "source.jpg";
        var destinationPath = Path.Combine("targetDir", "source.jpg");
        var mediaPreview = CreateMediaPreview(sourcePath);
        var completedOperations = new List<MediaFileOperationCompleted>();
        _fileSystemServiceMock.Setup(x => x.DirectoryExists("targetDir")).Returns(true);
        _fileSystemServiceMock.Setup(x => x.GetFiles("targetDir")).Returns(Array.Empty<string>());
        _fileSystemServiceMock.Setup(x => x.FileExists(sourcePath)).Returns(true);
        _fileSystemServiceMock
            .Setup(x => x.CopyFile(sourcePath, destinationPath))
            .Throws(new IOException("copy failed"));
        service.FileOperationCompleted.Subscribe(completedOperations.Add);

        var exception = Assert.Throws<ServiceException>(() =>
            service.CopyImagePreviewToDirectoryAsync(
                mediaPreview,
                "targetDir",
                new Progress<OperationInfo>()));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("Failed to copy image preview"));
            Assert.That(exception.InnerException, Is.TypeOf<IOException>());
            Assert.That(completedOperations, Is.Empty);
        });
    }

    [Test]
    public void DeleteImagePreviewAsync_WhenFileExists_DeletesFile()
    {
        var service = CreateService();
        var mediaPreview = CreateMediaPreview("existing.jpg");

        _fileSystemServiceMock.Setup(x => x.FileExists("existing.jpg")).Returns(true);

        var result = service.DeleteImagePreviewAsync(mediaPreview).Result;

        Assert.That(result, Is.EqualTo(OperationResult.Success));
        _fileSystemServiceMock.Verify(x => x.SafeDelete("existing.jpg"), Times.Once);
    }

    [Test]
    public void OpenInExternalProcess_WhenExecutableNotExists_ThrowsServiceException()
    {
        var service = CreateService();
        var mediaPreview = CreateMediaPreview("image.jpg");

        _fileSystemServiceMock.Setup(x => x.FileExists("app.exe")).Returns(false);

        Assert.That(() => service.OpenInExternalProcess(mediaPreview, "app.exe"),
            Throws.TypeOf<ServiceException>());
    }

    [Test]
    public void OpenInExternalProcess_WhenMediaFileNotExists_ThrowsServiceException()
    {
        var service = CreateService();
        var mediaPreview = CreateMediaPreview("image.jpg");

        _fileSystemServiceMock.Setup(x => x.FileExists("app.exe")).Returns(true);
        _fileSystemServiceMock.Setup(x => x.FileExists("image.jpg")).Returns(false);

        Assert.That(() => service.OpenInExternalProcess(mediaPreview, "app.exe"),
            Throws.TypeOf<ServiceException>());
    }

    [Test]
    public void OpenInExternalProcess_WithValidFiles_StartsProcess()
    {
        var service = CreateService();
        var mediaPreview = CreateMediaPreview("image.jpg");

        _fileSystemServiceMock.Setup(x => x.FileExists("app.exe")).Returns(true);
        _fileSystemServiceMock.Setup(x => x.FileExists("image.jpg")).Returns(true);

        service.OpenInExternalProcess(mediaPreview, "app.exe");

        _processServiceMock.Verify(x => x.StartProcess("app.exe", "\"image.jpg\""), Times.Once);
    }

    [Test]
    public void GetLastSelectedMediaPreview_Initially_ReturnsNull()
    {
        var service = CreateService();

        var result = service.GetLastSelectedMediaPreview();

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetLastSelectedMediaPreview_AfterSetting_ReturnsSelectedPreview()
    {
        var service = CreateService();
        var selectedPreview = CreateSelectedMediaPreview();

        service.SetSelectedPreview(selectedPreview);
        var result = service.GetLastSelectedMediaPreview();

        Assert.That(result, Is.EqualTo(selectedPreview));
    }

    [Test]
    public void Dispose_DisposesSubject()
    {
        var service = CreateService();

        service.Dispose();

        Assert.That(() => service.ImagePreviewSelected.Subscribe(_ => { }),
            Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public void MoveImagePreviewToDirectoryAsync_WhenDirectoryNotExists_ThrowsException()
    {
        var service = CreateService();
        var mediaPreview = CreateMediaPreview();

        _fileSystemServiceMock.Setup(x => x.DirectoryExists("targetDir")).Returns(false);

        Assert.That(() => service.MoveImagePreviewToDirectoryAsync(mediaPreview, "targetDir", new Progress<OperationInfo>()),
            Throws.TypeOf<ServiceException>());
    }

    [Test]
    public void DeleteImagePreviewAsync_SafeDeletePublishesCompletionAfterFilesystemCommit()
    {
        var sourcePath = Path.Combine("source", "photo.jpg");
        var bufferDirectory = "buffer";
        var files = new HashSet<string>(StringComparer.Ordinal) { sourcePath };
        var store = new RecordingManifestStore();
        var configurationService = CreateConfigurationService(new Configuration
        {
            SafeDeletionEnabled = true,
            SafeDeletionDirectoryPath = bufferDirectory
        });
        _fileSystemServiceMock
            .Setup(fileSystem => fileSystem.FileExists(It.IsAny<string>()))
            .Returns((string path) => files.Contains(path));
        _fileSystemServiceMock
            .Setup(fileSystem => fileSystem.DirectoryExists(bufferDirectory))
            .Returns(true);
        _fileSystemServiceMock
            .Setup(fileSystem => fileSystem.MoveFile(It.IsAny<string>(), It.IsAny<string>()))
            .Callback((string source, string destination) =>
            {
                files.Remove(source);
                files.Add(destination);
            });
        using var service = new WindowsMediaPreviewOperationsService(
            _fileSystemServiceMock.Object,
            _processServiceMock.Object,
            configurationService,
            store);
        var eventObservedAfterCommit = false;
        MediaFileOperationCompleted? completedOperation = null;
        service.FileOperationCompleted.Subscribe(operation =>
        {
            completedOperation = operation;
            eventObservedAfterCommit = !files.Contains(sourcePath)
                && store.Current.Single().State == DeletedMediaFileState.Committed;
        });

        var result = service.DeleteImagePreviewAsync(CreateMediaPreview(sourcePath)).Result;

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(OperationResult.Success));
            Assert.That(eventObservedAfterCommit, Is.True);
            Assert.That(completedOperation, Is.EqualTo(new MediaFileOperationCompleted(
                MediaFileOperationKind.Delete,
                sourcePath)));
        });
    }

    [Test]
    public void RestoreDeletedFileAsync_PublishesCompletionAfterFilesystemCommit()
    {
        var originalPath = Path.Combine("source", "photo.jpg");
        var bufferPath = Path.Combine("buffer", "buffered-photo.jpg");
        var files = new HashSet<string>(StringComparer.Ordinal) { bufferPath };
        var store = new RecordingManifestStore([
            new DeletedMediaFile(originalPath, bufferPath, DateTime.UtcNow)
        ]);
        var configurationService = CreateConfigurationService(new Configuration
        {
            SafeDeletionEnabled = true,
            SafeDeletionDirectoryPath = "buffer"
        });
        _fileSystemServiceMock
            .Setup(fileSystem => fileSystem.FileExists(It.IsAny<string>()))
            .Returns((string path) => files.Contains(path));
        _fileSystemServiceMock
            .Setup(fileSystem => fileSystem.DirectoryExists("source"))
            .Returns(true);
        _fileSystemServiceMock
            .Setup(fileSystem => fileSystem.GetFiles("source"))
            .Returns(Array.Empty<string>());
        _fileSystemServiceMock
            .Setup(fileSystem => fileSystem.MoveFile(bufferPath, originalPath))
            .Callback(() =>
            {
                files.Remove(bufferPath);
                files.Add(originalPath);
            });
        using var service = new WindowsMediaPreviewOperationsService(
            _fileSystemServiceMock.Object,
            _processServiceMock.Object,
            configurationService,
            store);
        var eventObservedAfterCommit = false;
        MediaFileOperationCompleted? completedOperation = null;
        service.FileOperationCompleted.Subscribe(operation =>
        {
            completedOperation = operation;
            eventObservedAfterCommit = files.Contains(originalPath) && store.Current.Count == 0;
        });

        var result = service.RestoreDeletedFileAsync(bufferPath).Result;

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(OperationResult.Success));
            Assert.That(eventObservedAfterCommit, Is.True);
            Assert.That(completedOperation, Is.EqualTo(new MediaFileOperationCompleted(
                MediaFileOperationKind.Restore,
                bufferPath,
                originalPath)));
        });
    }

    [Test]
    public void SetSelectedPreview_WithDifferentPanels_KeepsMostRecentPreviewGlobally()
    {
        using var service = CreateService();
        var leftPreview = CreateSelectedMediaPreview();
        var rightPreview = CreateSelectedMediaPreview(FileManagerPanel.Right);

        service.SetSelectedPreview(leftPreview);
        service.SetSelectedPreview(rightPreview);

        Assert.That(service.GetLastSelectedMediaPreview(), Is.EqualTo(rightPreview));
    }

    [Test]
    public void RequestMediaPreviewNavigation_ForwardsNotificationWithoutChangingSelection()
    {
        using var service = CreateService();
        var selectedPreview = CreateSelectedMediaPreview();
        var requestedPreview = CreateSelectedMediaPreview(FileManagerPanel.Right);
        SelectedMediaPreview? notification = null;
        service.SetSelectedPreview(selectedPreview);
        service.MediaPreviewNavigationRequested.Subscribe(preview => notification = preview);

        service.RequestMediaPreviewNavigation(requestedPreview);

        Assert.Multiple(() =>
        {
            Assert.That(notification, Is.SameAs(requestedPreview));
            Assert.That(service.GetLastSelectedMediaPreview(), Is.SameAs(selectedPreview));
        });
    }

    [Test]
    public void OpenInExternalProcess_WithApplication_ForwardsToLauncher()
    {
        using var service = CreateService();
        var mediaPreview = CreateMediaPreview("image.jpg");
        var application = new FileApplicationInfo(
            "System default",
            string.Empty,
            ApplicationLaunchMode.SystemDefault);
        _fileSystemServiceMock.Setup(fileSystem => fileSystem.FileExists(mediaPreview.Url)).Returns(true);

        service.OpenInExternalProcess(mediaPreview, application);

        _processServiceMock.Verify(
            process => process.OpenFile(
                mediaPreview.Url,
                application.LaunchMode,
                application.ApplicationPath),
            Times.Once);
    }

    [Test]
    public void Dispose_DisposesForwardedObservablesAndRejectsForwardedNotifications()
    {
        var service = CreateService();
        var selectedPreview = CreateSelectedMediaPreview();

        service.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(
                () => service.MediaPreviewNavigationRequested.Subscribe(_ => { }),
                Throws.TypeOf<ObjectDisposedException>());
            Assert.That(
                () => service.FileOperationCompleted.Subscribe(_ => { }),
                Throws.TypeOf<ObjectDisposedException>());
            Assert.That(
                () => service.DeletedFilesChanged.Subscribe(_ => { }),
                Throws.TypeOf<ObjectDisposedException>());
            Assert.That(
                () => service.SetSelectedPreview(selectedPreview),
                Throws.TypeOf<ObjectDisposedException>());
            Assert.That(
                () => service.RequestMediaPreviewNavigation(selectedPreview),
                Throws.TypeOf<ObjectDisposedException>());
        });
    }

    private static IConfigurationService CreateConfigurationService(Configuration configuration)
    {
        var configurationService = new Mock<IConfigurationService>();
        configurationService
            .Setup(service => service.Configuration)
            .Returns(new Lazy<Configuration>(() => configuration));
        return configurationService.Object;
    }

    private sealed class RecordingManifestStore : IDeletionManifestStore
    {
        public RecordingManifestStore(IEnumerable<DeletedMediaFile>? initial = null)
        {
            Current = initial?.ToArray() ?? Array.Empty<DeletedMediaFile>();
        }

        public IReadOnlyList<DeletedMediaFile> Current { get; private set; }

        public IReadOnlyList<DeletedMediaFile> Load() => Current.ToArray();

        public void Save(IReadOnlyList<DeletedMediaFile> deletedFiles)
        {
            Current = deletedFiles.ToArray();
        }
    }

    private WindowsMediaPreviewOperationsService CreateService()
    {
        return new WindowsMediaPreviewOperationsService(
            _fileSystemServiceMock.Object,
            _processServiceMock.Object);
    }

    private MediaPreview CreateMediaPreview(string url = "test.jpg")
    {
        return new MediaPreview("Test Title", url, _testMediaFormat, 800);
    }

    private SelectedMediaPreview CreateSelectedMediaPreview(FileManagerPanel panel = FileManagerPanel.Left)
    {
        return new SelectedMediaPreview(CreateMediaPreview(), panel);
    }
}