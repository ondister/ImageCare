using ImageCare.Core.Services.FileSystemWatcherService;

namespace ImageCare.Core.Tests.Services.FileSystemWatcherService;

[TestFixture]
public class MultiSourcesFileSystemWatcherServiceEventsTests
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

		_service.StartWatchingDirectory(_testDirectory1);
		_service.StartWatchingDirectory(_testDirectory2);
		_service.StartWatching();
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
}