using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Services.FileSystemWatcherService;

namespace ImageCare.Core.Tests.Services.FileSystemWatcherService;

[TestFixture]
[NonParallelizable]
public class LocalFileSystemWatcherServiceFileEventsTests
{
    private LocalFileSystemWatcherService _service;
    private string _testDirectory;

    [SetUp]
    public void Setup()
    {
        _service = new LocalFileSystemWatcherService();
        _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
        _service.StartWatchingDirectory(_testDirectory);
        _service.StartWatching();
    }

    [TearDown]
    public void TearDown()
    {
        _service?.Dispose();
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Test]
    public void FileCreated_Event_EmittedWhenFileCreated()
    {
        FileModel? receivedCreatedModel = null;
        FileModel? receivedChangedModel = null;
        var testFileName = "testfile.txt";
        var testFilePath = Path.Combine(_testDirectory, testFileName);

        using var createdSubscription = _service.FileCreated.Subscribe(model => receivedCreatedModel = model);
        using var changedSubscription = _service.FileChanged.Subscribe(model => receivedChangedModel = model);

        File.WriteAllText(testFilePath, "test content");

        Assert.That(() => receivedCreatedModel, Is.Not.Null.After(2000, 50));
        Assert.That(receivedCreatedModel!.Name, Is.EqualTo(testFileName));
        Assert.That(receivedCreatedModel.FullName, Is.EqualTo(testFilePath));
        Assert.That(() => receivedChangedModel, Is.Not.Null.After(2000, 50));
        Assert.That(receivedChangedModel!.Name, Is.EqualTo(testFileName));
        Assert.That(receivedChangedModel.FullName, Is.EqualTo(testFilePath));
    }

    [Test]
    public void FileDeleted_Event_EmittedWhenFileDeleted()
    {
        var testFileName = "testfile.txt";
        var testFilePath = Path.Combine(_testDirectory, testFileName);
        File.WriteAllText(testFilePath, "test content");

        FileModel receivedModel = null;
        using var subscription = _service.FileDeleted.Subscribe(model => receivedModel = model);

        File.Delete(testFilePath);

        Assert.That(() => receivedModel, Is.Not.Null.After(500, 50));
        Assert.That(receivedModel.Name, Is.EqualTo(testFileName));
    }
}

[TestFixture]
public class LocalFileSystemWatcherServiceDirectoryEventsTests
{
    private LocalFileSystemWatcherService _service;
    private string _testDirectory;

    [SetUp]
    public void Setup()
    {
        _service = new LocalFileSystemWatcherService();
        _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
        _service.StartWatchingDirectory(_testDirectory);
        _service.StartWatching();
    }

    [TearDown]
    public void TearDown()
    {
        _service?.Dispose();
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Test]
    public void DirectoryCreated_Event_EmittedWhenDirectoryCreated()
    {
        DirectoryModel receivedModel = null;
        var newDirectoryName = "newsubdir";
        var newDirectoryPath = Path.Combine(_testDirectory, newDirectoryName);

        using var subscription = _service.DirectoryCreated.Subscribe(model => receivedModel = model);

        Directory.CreateDirectory(newDirectoryPath);

        Assert.That(() => receivedModel, Is.Not.Null.After(500, 50));
        Assert.That(receivedModel.Name, Is.EqualTo(newDirectoryName));
        Assert.That(receivedModel.Path, Is.EqualTo(newDirectoryPath));
    }
}