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

	[Test]
	public void FileCreated_Event_FromMultipleDirectories_EmittedCorrectly()
	{
		var eventsReceived = 0;
		var expectedEvents = 2;

		using var subscription = _service.FileCreated.Subscribe(_ => eventsReceived++);

		File.WriteAllText(Path.Combine(_testDirectory1, "file1.txt"), "content1");
		File.WriteAllText(Path.Combine(_testDirectory2, "file2.txt"), "content2");

		Assert.That(() => eventsReceived, Is.EqualTo(expectedEvents).After(3000, 100));
	}

	[Test]
	public void StopWatchingDirectory_StopsEventsFromThatDirectory()
	{
		var eventsReceived = 0;

		using var subscription = _service.FileCreated.Subscribe(_ => eventsReceived++);

		_service.StopWatchingDirectory(_testDirectory1);

		File.WriteAllText(Path.Combine(_testDirectory1, "file1.txt"), "content1"); // Should not trigger
		File.WriteAllText(Path.Combine(_testDirectory2, "file2.txt"), "content2"); // Should trigger

		Assert.That(() => eventsReceived, Is.EqualTo(1).After(3000, 50));
	}
}