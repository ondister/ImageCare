using ImageCare.Core.Exceptions;
using ImageCare.Core.Services.FileSystemWatcherService;

namespace ImageCare.Core.Tests.Services.FileSystemWatcherService;

[TestFixture]
public class LocalFileSystemWatcherServiceTests
{
	private LocalFileSystemWatcherService _service;
	private string _testDirectory;

	[SetUp]
	public void Setup()
	{
		_service = new LocalFileSystemWatcherService();
		_testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		Directory.CreateDirectory(_testDirectory);
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
	public void Constructor_InitializesAllObservables()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_service.FileCreated, Is.Not.Null);
			Assert.That(_service.FileDeleted, Is.Not.Null);
			Assert.That(_service.FileRenamed, Is.Not.Null);
			Assert.That(_service.DirectoryCreated, Is.Not.Null);
			Assert.That(_service.DirectoryDeleted, Is.Not.Null);
			Assert.That(_service.DirectoryRenamed, Is.Not.Null);
		});
	}

	[Test]
	public void StartWatchingDirectory_WithValidPath_CompletesSuccessfully()
	{
		Assert.DoesNotThrow(() => _service.StartWatchingDirectory(_testDirectory));
	}

	[Test]
	public void StartWatchingDirectory_WithNullPath_ThrowsServiceException()
	{
		Assert.Throws<ServiceException>(() => _service.StartWatchingDirectory(null));
	}

	[Test]
	public void StartWatchingDirectory_WithEmptyPath_ThrowsServiceException()
	{
		Assert.Throws<ServiceException>(() => _service.StartWatchingDirectory(string.Empty));
	}

	[Test]
	public void StartWatchingDirectory_WithWhitespacePath_ThrowsServiceException()
	{
		Assert.Throws<ServiceException>(() => _service.StartWatchingDirectory("   "));
	}

	[Test]
	public void StartWatchingDirectory_WithNonExistentPath_ThrowsServiceException()
	{
		var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

		Assert.Throws<ServiceException>(() => _service.StartWatchingDirectory(nonExistentPath));
	}

	[Test]
	public void StartWatching_AfterDirectorySetup_CompletesSuccessfully()
	{
		_service.StartWatchingDirectory(_testDirectory);

		Assert.DoesNotThrow(() => _service.StartWatching());
	}

	[Test]
	public void StopWatching_AfterStartWatching_CompletesSuccessfully()
	{
		_service.StartWatchingDirectory(_testDirectory);
		_service.StartWatching();

		Assert.DoesNotThrow(() => _service.StopWatching());
	}

	[Test]
	public void Dispose_MakesServiceUnusableForStartWatching()
	{
		_service.Dispose();

		Assert.Throws<ObjectDisposedException>(() => _service.StartWatching());
	}

	[Test]
	public void Dispose_MakesServiceUnusableForStartWatchingDirectory()
	{
		_service.Dispose();

		Assert.Throws<ObjectDisposedException>(() => _service.StartWatchingDirectory(_testDirectory));
	}

	[Test]
	public void MultipleDisposeCalls_DoNotThrowExceptions()
	{
		Assert.DoesNotThrow(() =>
		{
			_service.Dispose();
			_service.Dispose();
		});
	}
}