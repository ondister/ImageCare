using ImageCare.Core.Exceptions;
using ImageCare.Core.Services.FileSystemWatcherService;

namespace ImageCare.Core.Tests.Services.FileSystemWatcherService;

[TestFixture]
[NonParallelizable]
public class MultiSourcesLocalFileSystemWatcherServiceTests
{
	private MultiSourcesLocalFileSystemWatcherService _service;
	private string _testDirectory1;
	private string _testDirectory2;

	[SetUp]
	public void Setup()
	{
		_service = new MultiSourcesLocalFileSystemWatcherService();
		_testDirectory1 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		_testDirectory2 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		Directory.CreateDirectory(_testDirectory1);
		Directory.CreateDirectory(_testDirectory2);
	}

	[TearDown]
	public void TearDown()
	{
        _service?.Dispose();
		if (Directory.Exists(_testDirectory1))
		{
			Directory.Delete(_testDirectory1, true);
		}

		if (Directory.Exists(_testDirectory2))
		{
			Directory.Delete(_testDirectory2, true);
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
	public void StartWatchingDirectory_WithValidPath_AddsService()
	{
		_service.StartWatchingDirectory(_testDirectory1);
		_service.StartWatching();

		Assert.DoesNotThrow(() => _service.StopWatching());
	}

	[Test]
	public void StartWatchingDirectory_WithNullPath_ThrowsServiceException()
	{
		Assert.Throws<ServiceException>(() => _service.StartWatchingDirectory(null));
	}

	[Test]
	public void StartWatchingDirectory_WithNonExistentPath_ThrowsServiceException()
	{
		var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		Assert.Throws<ServiceException>(() => _service.StartWatchingDirectory(nonExistentPath));
	}

	[Test]
	public void StartWatchingDirectory_WithSamePathTwice_IgnoresDuplicate()
	{
		_service.StartWatchingDirectory(_testDirectory1);
		_service.StartWatchingDirectory(_testDirectory1); // Should not throw

		Assert.DoesNotThrow(() => _service.StartWatching());
	}

	[Test]
	public void StartWatchingDirectory_WithSubdirectoryOfWatchedPath_IgnoresSubdirectory()
	{
		var subDirectory = Path.Combine(_testDirectory1, "subdir");
		Directory.CreateDirectory(subDirectory);

		_service.StartWatchingDirectory(_testDirectory1);
		_service.StartWatchingDirectory(subDirectory); // Should be ignored

		Assert.DoesNotThrow(() => _service.StartWatching());
	}

	[Test]
	public void StopWatchingDirectory_WithWatchedPath_RemovesService()
	{
		_service.StartWatchingDirectory(_testDirectory1);
		_service.StartWatchingDirectory(_testDirectory2);

		_service.StopWatchingDirectory(_testDirectory1);

		_service.StartWatching(); // Should only start _testDirectory2
	}

	[Test]
	public void StopWatchingDirectory_WithNullPath_ThrowsServiceException()
	{
		Assert.Throws<ServiceException>(() => _service.StopWatchingDirectory(null));
	}

	[Test]
	public void StopWatchingDirectory_WithNotWatchedPath_CompletesSilently()
	{
		Assert.DoesNotThrow(() => _service.StopWatchingDirectory(_testDirectory1));
	}

	[Test]
	public void ClearWatchers_RemovesAllServices()
	{
		_service.StartWatchingDirectory(_testDirectory1);
		_service.StartWatchingDirectory(_testDirectory2);

		_service.ClearWatchers();

		_service.StartWatching(); // No services should be started
	}

	[Test]
	public void Dispose_MakesServiceUnusable()
	{
		_service.Dispose();

		Assert.Throws<ObjectDisposedException>(() => _service.StartWatching());
		Assert.Throws<ObjectDisposedException>(() => _service.StartWatchingDirectory(_testDirectory1));
		Assert.Throws<ObjectDisposedException>(() => _service.StopWatchingDirectory(_testDirectory1));
		Assert.Throws<ObjectDisposedException>(() => _service.ClearWatchers());
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