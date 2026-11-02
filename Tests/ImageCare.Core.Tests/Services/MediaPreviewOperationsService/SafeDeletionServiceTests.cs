using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.MediaPreviewOperationsService;

using Moq;

namespace ImageCare.Core.Tests.Services.MediaPreviewOperationsService;

[TestFixture]
public class SafeDeletionServiceTests
{
    private const string SourceDirectory = "source";
    private const string BufferDirectory = "buffer";

    private HashSet<string> _files;
    private HashSet<string> _directories;
    private List<string> _operations;
    private Mock<IFileSystemService> _fileSystemMock;
    private Configuration _configuration;
    private Func<string, string, Exception?>? _moveFailure;
    private Func<string, Exception?>? _deleteFailure;

    [SetUp]
    public void SetUp()
    {
        _files = new HashSet<string>(StringComparer.Ordinal);
        _directories = new HashSet<string>(StringComparer.Ordinal)
        {
            SourceDirectory,
            BufferDirectory
        };
        _operations = new List<string>();
        _configuration = new Configuration
        {
            SafeDeletionEnabled = true,
            SafeDeletionDirectoryPath = BufferDirectory,
            SafeDeletionMaxFileCount = 100
        };
        _fileSystemMock = new Mock<IFileSystemService>();
        _fileSystemMock
            .Setup(fileSystem => fileSystem.FileExists(It.IsAny<string>()))
            .Returns((string path) => _files.Contains(path));
        _fileSystemMock
            .Setup(fileSystem => fileSystem.DirectoryExists(It.IsAny<string>()))
            .Returns((string path) => _directories.Contains(path));
        _fileSystemMock
            .Setup(fileSystem => fileSystem.CreateDirectory(It.IsAny<string>()))
            .Callback((string path) => _directories.Add(path));
        _fileSystemMock
            .Setup(fileSystem => fileSystem.GetFiles(It.IsAny<string>()))
            .Returns((string directory) => _files
                .Where(path => string.Equals(
                    Path.GetDirectoryName(path),
                    directory,
                    StringComparison.Ordinal))
                .ToArray());
        _fileSystemMock
            .Setup(fileSystem => fileSystem.MoveFile(It.IsAny<string>(), It.IsAny<string>()))
            .Callback((string source, string destination) =>
            {
                _operations.Add($"move:{source}->{destination}");
                var failure = _moveFailure?.Invoke(source, destination);
                if (failure is not null)
                {
                    throw failure;
                }

                _files.Remove(source);
                _files.Add(destination);
            });
        _fileSystemMock
            .Setup(fileSystem => fileSystem.CopyFile(It.IsAny<string>(), It.IsAny<string>()))
            .Callback((string source, string destination) =>
            {
                _operations.Add($"copy:{source}->{destination}");
                _files.Add(destination);
            });
        _fileSystemMock
            .Setup(fileSystem => fileSystem.SafeDelete(It.IsAny<string>()))
            .Callback((string path) =>
            {
                _operations.Add($"delete:{path}");
                var failure = _deleteFailure?.Invoke(path);
                if (failure is not null)
                {
                    throw failure;
                }

                _files.Remove(path);
            });
    }

    [Test]
    public void Delete_SafeDeletionMovesSidecarBeforeMediaAndPersistsPendingThenCommitted()
    {
        var sourcePath = Path.Combine(SourceDirectory, "clip.mp4");
        var sourceSidecarPath = sourcePath + ".xmp";
        _files.UnionWith([sourcePath, sourceSidecarPath]);
        var store = new RecordingDeletionManifestStore();
        using var service = CreateService(store);

        service.Delete(sourcePath, sourceSidecarPath);

        var pending = store.Snapshots[0].Single();
        var committed = store.Snapshots[1].Single();
        Assert.Multiple(() =>
        {
            Assert.That(pending.State, Is.EqualTo(DeletedMediaFileState.PendingDelete));
            Assert.That(committed.State, Is.EqualTo(DeletedMediaFileState.Committed));
            Assert.That(Path.GetFileName(pending.BufferPath),
                Does.StartWith(SafeDeletionService.SafeDeleteFilePrefix));
            Assert.That(Path.GetFileName(pending.BufferPath), Does.EndWith("__clip.mp4"));
            Assert.That(Path.GetFileName(pending.BufferSidecarPath!), Does.EndWith("__clip.mp4.xmp"));
            Assert.That(_operations, Is.EqualTo(new[]
            {
                $"move:{sourceSidecarPath}->{pending.BufferSidecarPath}",
                $"move:{sourcePath}->{pending.BufferPath}"
            }));
            Assert.That(_files, Does.Contain(pending.BufferPath));
            Assert.That(_files, Does.Contain(pending.BufferSidecarPath!));
            Assert.That(_files, Does.Not.Contain(sourcePath));
            Assert.That(store.Snapshots.Last().Single().State,
                Is.EqualTo(DeletedMediaFileState.Committed));
        });
    }

    [Test]
    public void DeletedFilesChanged_ReplaysInitialSnapshotAndPublishesCommittedDelete()
    {
        var existing = CreateDeletedFile("old.jpg", DateTime.UtcNow.AddMinutes(-1));
        _files.Add(existing.BufferPath);
        var store = new RecordingDeletionManifestStore([existing]);
        using var service = CreateService(store);
        var notifications = new List<IReadOnlyList<DeletedMediaFile>>();

        service.DeletedFilesChanged.Subscribe(notifications.Add);
        var sourcePath = Path.Combine(SourceDirectory, "new.jpg");
        _files.Add(sourcePath);
        service.Delete(sourcePath, null);

        Assert.Multiple(() =>
        {
            Assert.That(notifications, Has.Count.EqualTo(2));
            Assert.That(notifications[0], Is.EqualTo(new[] { existing }));
            Assert.That(notifications[1], Has.Count.EqualTo(2));
            Assert.That(notifications[1][0].OriginalPath, Is.EqualTo(sourcePath));
        });
    }

    [Test]
    public void Delete_WhenInitialManifestWriteFails_DoesNotMoveFilesOrExposeEntry()
    {
        var sourcePath = Path.Combine(SourceDirectory, "photo.jpg");
        _files.Add(sourcePath);
        var store = new RecordingDeletionManifestStore { FailOnSaveCalls = { 1 } };
        using var service = CreateService(store);

        Assert.That(() => service.Delete(sourcePath, null), Throws.TypeOf<IOException>());

        Assert.Multiple(() =>
        {
            Assert.That(_files, Does.Contain(sourcePath));
            Assert.That(_operations, Is.Empty);
            Assert.That(service.GetDeletedFiles(), Is.Empty);
        });
    }

    [Test]
    public void Delete_WhenSafeDeletionDirectoryMatchesSourceDirectoryWithTrailingSeparator_RejectsOperation()
    {
        var sourcePath = Path.Combine(BufferDirectory, "photo.jpg");
        _configuration.SafeDeletionDirectoryPath = BufferDirectory + Path.DirectorySeparatorChar;
        _files.Add(sourcePath);
        var store = new RecordingDeletionManifestStore();
        using var service = CreateService(store);

        Assert.That(
            () => service.Delete(sourcePath, null),
            Throws.TypeOf<ImageCare.Core.Exceptions.ServiceException>());

        Assert.Multiple(() =>
        {
            Assert.That(_files, Does.Contain(sourcePath));
            Assert.That(store.Snapshots, Is.Empty);
        });
    }

    [Test]
    public void Delete_WhenSafeDeletionIsDisabled_CommitsMediaBeforeBestEffortSidecarCleanup()
    {
        _configuration.SafeDeletionEnabled = false;
        var sourcePath = Path.Combine(SourceDirectory, "clip.mp4");
        var sidecarPath = sourcePath + ".xmp";
        _files.UnionWith([sourcePath, sidecarPath]);
        _deleteFailure = path => path == sidecarPath
            ? new IOException("sidecar locked")
            : null;
        var store = new RecordingDeletionManifestStore();
        using var service = CreateService(store);

        service.Delete(sourcePath, sidecarPath);

        Assert.Multiple(() =>
        {
            Assert.That(_files, Does.Not.Contain(sourcePath));
            Assert.That(_files, Does.Contain(sidecarPath));
            Assert.That(_operations, Is.EqualTo(new[]
            {
                $"delete:{sourcePath}",
                $"delete:{sidecarPath}"
            }));
            Assert.That(store.Snapshots, Is.Empty);
        });
    }

    [Test]
    public void Dispose_DisposesDeletedFilesChangedSubject()
    {
        var store = new RecordingDeletionManifestStore();
        var service = CreateService(store);

        service.Dispose();

        Assert.That(
            () => service.DeletedFilesChanged.Subscribe(_ => { }),
            Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public void Delete_WhenMediaMoveFails_RollsBackSidecarAndClearsPendingEntry()
    {
        var sourcePath = Path.Combine(SourceDirectory, "clip.mp4");
        var sourceSidecarPath = sourcePath + ".xmp";
        _files.UnionWith([sourcePath, sourceSidecarPath]);
        _moveFailure = (source, _) => source == sourcePath
            ? new UnauthorizedAccessException("media move failed")
            : null;
        var store = new RecordingDeletionManifestStore();
        using var service = CreateService(store);

        Assert.That(
            () => service.Delete(sourcePath, sourceSidecarPath),
            Throws.TypeOf<UnauthorizedAccessException>());

        Assert.Multiple(() =>
        {
            Assert.That(_files, Does.Contain(sourcePath));
            Assert.That(_files, Does.Contain(sourceSidecarPath));
            Assert.That(store.Current, Is.Empty);
            Assert.That(service.CanUndoDelete, Is.False);
        });
    }

    [Test]
    public void Delete_WhenCommittedManifestWriteFails_ReportsSuccessAndKeepsDurablePendingDelete()
    {
        var sourcePath = Path.Combine(SourceDirectory, "photo.jpg");
        _files.Add(sourcePath);
        var store = new RecordingDeletionManifestStore { FailOnSaveCalls = { 2 } };
        using var service = CreateService(store);

        service.Delete(sourcePath, null);

        Assert.Multiple(() =>
        {
            Assert.That(_files, Does.Not.Contain(sourcePath));
            Assert.That(store.Current, Has.Count.EqualTo(1));
            Assert.That(store.Current[0].State, Is.EqualTo(DeletedMediaFileState.PendingDelete));
            Assert.That(service.GetDeletedFiles(), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Delete_TrimsOldestUndoableFileAtConfiguredMaximum()
    {
        _configuration.SafeDeletionMaxFileCount = 2;
        var oldest = CreateDeletedFile("oldest.jpg", DateTime.UtcNow.AddHours(-3));
        var newer = CreateDeletedFile("newer.jpg", DateTime.UtcNow.AddHours(-2));
        _files.UnionWith([oldest.BufferPath, newer.BufferPath]);
        var store = new RecordingDeletionManifestStore([oldest, newer]);
        using var service = CreateService(store);
        var sourcePath = Path.Combine(SourceDirectory, "latest.jpg");
        _files.Add(sourcePath);

        service.Delete(sourcePath, null);

        Assert.Multiple(() =>
        {
            Assert.That(_files, Does.Not.Contain(oldest.BufferPath));
            Assert.That(_files, Does.Contain(newer.BufferPath));
            Assert.That(service.GetDeletedFiles().Select(file => file.OriginalPath),
                Is.EqualTo(new[] { sourcePath, newer.OriginalPath }));
        });
    }

    [Test]
    public void UndoLastDelete_RestoresNewestEntry()
    {
        var older = CreateDeletedFile("older.jpg", DateTime.UtcNow.AddMinutes(-2));
        var newest = CreateDeletedFile("newest.jpg", DateTime.UtcNow.AddMinutes(-1));
        _files.UnionWith([older.BufferPath, newest.BufferPath]);
        var store = new RecordingDeletionManifestStore([older, newest]);
        using var service = CreateService(store);

        var result = service.UndoLastDelete();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(
                new SafeDeletionRestoreResult(newest.BufferPath, newest.OriginalPath)));
            Assert.That(_files, Does.Contain(newest.OriginalPath));
            Assert.That(_files, Does.Not.Contain(newest.BufferPath));
            Assert.That(service.GetDeletedFiles(), Is.EqualTo(new[] { older }));
        });
    }

    [Test]
    public void RestoreDeletedFile_WhenOriginalBundleConflicts_UsesUniqueMediaAndSidecarPaths()
    {
        var deletedFile = CreateDeletedFile("clip.mp4", DateTime.UtcNow);
        var bufferSidecarPath = deletedFile.BufferPath + ".xmp";
        deletedFile = deletedFile with
        {
            OriginalSidecarPath = deletedFile.OriginalPath + ".xmp",
            BufferSidecarPath = bufferSidecarPath
        };
        _files.UnionWith([deletedFile.BufferPath, bufferSidecarPath, deletedFile.OriginalPath]);
        var store = new RecordingDeletionManifestStore([deletedFile]);
        using var service = CreateService(store);
        var expectedDestination = Path.Combine(SourceDirectory, "clip (1).mp4");
        var expectedSidecar = expectedDestination + ".xmp";

        var result = service.RestoreDeletedFile(deletedFile.BufferPath);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(
                new SafeDeletionRestoreResult(deletedFile.BufferPath, expectedDestination)));
            Assert.That(_operations.Take(2), Is.EqualTo(new[]
            {
                $"move:{bufferSidecarPath}->{expectedSidecar}",
                $"move:{deletedFile.BufferPath}->{expectedDestination}"
            }));
            Assert.That(_files, Does.Contain(expectedDestination));
            Assert.That(_files, Does.Contain(expectedSidecar));
            Assert.That(_files, Does.Contain(deletedFile.OriginalPath));
        });
    }

    [Test]
    public void PermanentlyDelete_DeletesMediaThenSidecarAndMissingEntryReturnsFalse()
    {
        var deletedFile = CreateDeletedFile("clip.mp4", DateTime.UtcNow) with
        {
            OriginalSidecarPath = Path.Combine(SourceDirectory, "clip.mp4.xmp"),
            BufferSidecarPath = Path.Combine(BufferDirectory, "clip.mp4.xmp")
        };
        _files.UnionWith([deletedFile.BufferPath, deletedFile.BufferSidecarPath!]);
        var store = new RecordingDeletionManifestStore([deletedFile]);
        using var service = CreateService(store);

        var deleted = service.PermanentlyDelete(deletedFile.BufferPath);
        var missing = service.PermanentlyDelete("missing.jpg");

        Assert.Multiple(() =>
        {
            Assert.That(deleted, Is.True);
            Assert.That(missing, Is.False);
            Assert.That(_operations.Take(2), Is.EqualTo(new[]
            {
                $"delete:{deletedFile.BufferPath}",
                $"delete:{deletedFile.BufferSidecarPath}"
            }));
            Assert.That(store.Current, Is.Empty);
        });
    }

    [TestCase(DeletedMediaFileState.PendingDelete, true, false, true)]
    [TestCase(DeletedMediaFileState.PendingDelete, false, true, false)]
    [TestCase(DeletedMediaFileState.PendingDelete, false, false, false)]
    [TestCase(DeletedMediaFileState.PendingRestore, true, false, true)]
    [TestCase(DeletedMediaFileState.PendingRestore, false, true, false)]
    [TestCase(DeletedMediaFileState.PendingRestore, true, true, true)]
    [TestCase(DeletedMediaFileState.PendingRestore, false, false, false)]
    [TestCase(DeletedMediaFileState.CleanupPending, true, false, false)]
    [TestCase(DeletedMediaFileState.Committed, false, false, false)]
    public void StartupRecovery_NormalizesMeaningfulMediaCombinations(
        DeletedMediaFileState state,
        bool bufferExists,
        bool originalOrRestoreExists,
        bool expectedUndoable)
    {
        var deletedFile = CreateDeletedFile("recover.jpg", DateTime.UtcNow) with
        {
            State = state,
            RestorePath = state == DeletedMediaFileState.PendingRestore
                ? Path.Combine(SourceDirectory, "restored.jpg")
                : null
        };
        if (bufferExists)
        {
            _files.Add(deletedFile.BufferPath);
        }

        var otherPath = state == DeletedMediaFileState.PendingRestore
            ? deletedFile.RestorePath!
            : deletedFile.OriginalPath;
        if (originalOrRestoreExists)
        {
            _files.Add(otherPath);
        }

        var store = new RecordingDeletionManifestStore([deletedFile]);
        using var service = CreateService(store);

        Assert.Multiple(() =>
        {
            Assert.That(service.CanUndoDelete, Is.EqualTo(expectedUndoable));
            Assert.That(service.GetDeletedFiles(),
                expectedUndoable ? Has.Count.EqualTo(1) : Is.Empty);
            if (expectedUndoable)
            {
                Assert.That(store.Current.Single().State,
                    Is.EqualTo(DeletedMediaFileState.Committed));
                Assert.That(store.Current.Single().RestorePath, Is.Null);
            }
            else
            {
                Assert.That(store.Current, Is.Empty);
            }
        });
    }

    [Test]
    public void StartupRecovery_PendingDeleteMovesSurvivingSidecarBackToOriginal()
    {
        var deletedFile = CreateDeletedFile("clip.mp4", DateTime.UtcNow) with
        {
            State = DeletedMediaFileState.PendingDelete,
            OriginalSidecarPath = Path.Combine(SourceDirectory, "clip.mp4.xmp"),
            BufferSidecarPath = Path.Combine(BufferDirectory, "clip.mp4.xmp")
        };
        _files.UnionWith([deletedFile.OriginalPath, deletedFile.BufferSidecarPath!]);
        var store = new RecordingDeletionManifestStore([deletedFile]);

        using var service = CreateService(store);

        Assert.Multiple(() =>
        {
            Assert.That(_files, Does.Contain(deletedFile.OriginalSidecarPath!));
            Assert.That(_files, Does.Not.Contain(deletedFile.BufferSidecarPath!));
            Assert.That(store.Current, Is.Empty);
            Assert.That(service.CanUndoDelete, Is.False);
        });
    }

    [Test]
    public void StartupRecovery_PendingRestoreCompletesMissingDestinationSidecar()
    {
        var deletedFile = CreateDeletedFile("clip.mp4", DateTime.UtcNow) with
        {
            State = DeletedMediaFileState.PendingRestore,
            OriginalSidecarPath = Path.Combine(SourceDirectory, "clip.mp4.xmp"),
            BufferSidecarPath = Path.Combine(BufferDirectory, "clip.mp4.xmp"),
            RestorePath = Path.Combine(SourceDirectory, "restored.mp4"),
            RestoreSidecarPath = Path.Combine(SourceDirectory, "restored.mp4.xmp")
        };
        _files.UnionWith([deletedFile.RestorePath!, deletedFile.BufferSidecarPath!]);
        var store = new RecordingDeletionManifestStore([deletedFile]);

        using var service = CreateService(store);

        Assert.Multiple(() =>
        {
            Assert.That(_files, Does.Contain(deletedFile.RestoreSidecarPath!));
            Assert.That(_files, Does.Not.Contain(deletedFile.BufferSidecarPath!));
            Assert.That(store.Current, Is.Empty);
            Assert.That(service.CanUndoDelete, Is.False);
        });
    }

    [Test]
    public void StartupRecovery_CleanupPendingRetainsTombstoneWhenSidecarCleanupFails()
    {
        var deletedFile = CreateDeletedFile("clip.mp4", DateTime.UtcNow) with
        {
            State = DeletedMediaFileState.CleanupPending,
            BufferSidecarPath = Path.Combine(BufferDirectory, "clip.mp4.xmp")
        };
        _files.UnionWith([deletedFile.BufferPath, deletedFile.BufferSidecarPath!]);
        _deleteFailure = path => path == deletedFile.BufferSidecarPath
            ? new IOException("sidecar locked")
            : null;
        var store = new RecordingDeletionManifestStore([deletedFile]);

        using var service = CreateService(store);

        Assert.Multiple(() =>
        {
            Assert.That(_files, Does.Not.Contain(deletedFile.BufferPath));
            Assert.That(_files, Does.Contain(deletedFile.BufferSidecarPath!));
            Assert.That(store.Current.Single().State,
                Is.EqualTo(DeletedMediaFileState.CleanupPending));
            Assert.That(service.CanUndoDelete, Is.False);
        });
    }

    [Test]
    public void RestoreDeletedFile_WhenFinalManifestWriteFails_ReturnsCommittedRestore()
    {
        var deletedFile = CreateDeletedFile("photo.jpg", DateTime.UtcNow);
        _files.Add(deletedFile.BufferPath);
        var store = new RecordingDeletionManifestStore([deletedFile])
        {
            FailOnSaveCalls = { 2 }
        };
        using var service = CreateService(store);

        var result = service.RestoreDeletedFile(deletedFile.BufferPath);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(_files, Does.Contain(deletedFile.OriginalPath));
            Assert.That(store.Current.Single().State,
                Is.EqualTo(DeletedMediaFileState.PendingRestore));
        });
    }

    private SafeDeletionService CreateService(RecordingDeletionManifestStore store)
    {
        return new SafeDeletionService(
            _fileSystemMock.Object,
            new MediaTransferService(_fileSystemMock.Object),
            store,
            () => _configuration);
    }

    private static DeletedMediaFile CreateDeletedFile(string fileName, DateTime deletedAtUtc)
    {
        return new DeletedMediaFile(
            Path.Combine(SourceDirectory, fileName),
            Path.Combine(BufferDirectory, $"buffered-{fileName}"),
            deletedAtUtc);
    }

    private sealed class RecordingDeletionManifestStore : IDeletionManifestStore
    {
        public RecordingDeletionManifestStore(IEnumerable<DeletedMediaFile>? initial = null)
        {
            Current = initial?.ToArray() ?? Array.Empty<DeletedMediaFile>();
        }

        public HashSet<int> FailOnSaveCalls { get; } = new();

        public List<IReadOnlyList<DeletedMediaFile>> Snapshots { get; } = new();

        public IReadOnlyList<DeletedMediaFile> Current { get; private set; }

        public int SaveCalls { get; private set; }

        public IReadOnlyList<DeletedMediaFile> Load() => Current.ToArray();

        public void Save(IReadOnlyList<DeletedMediaFile> deletedFiles)
        {
            SaveCalls++;
            var snapshot = deletedFiles.ToArray();
            Snapshots.Add(snapshot);
            if (FailOnSaveCalls.Contains(SaveCalls))
            {
                throw new IOException($"manifest save {SaveCalls} failed");
            }

            Current = snapshot;
        }
    }
}
