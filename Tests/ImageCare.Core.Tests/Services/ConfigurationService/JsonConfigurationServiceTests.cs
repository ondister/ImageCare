using System.Text.Json;
using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Exceptions;
using ImageCare.Core.Services.ConfigurationService;

using Moq;

namespace ImageCare.Core.Tests.Services.ConfigurationService;

[TestFixture]
public class JsonConfigurationServiceTests
{
	private string _testBaseDirectory;

	[SetUp]
	public void SetUp()
	{
		_testBaseDirectory = Path.Combine(Path.GetTempPath(), $"ImageCareTest_{Guid.NewGuid()}");
	}

	[TearDown]
	public void TearDown()
	{
		if (Directory.Exists(_testBaseDirectory))
		{
			Directory.Delete(_testBaseDirectory, true);
		}
	}

	[Test]
	public void Constructor_WithFileSource_CreatesConfigurationFile()
	{
		var testFilePath = Path.Combine(_testBaseDirectory, "test1", "configuration.json");
		var fileSource = CreateMockFileSource(testFilePath);
		using var service = new JsonConfigurationService(fileSource.Object);

		Assert.That(File.Exists(testFilePath), Is.True);
		fileSource.Verify(x => x.EnsureDirectoryExists(), Times.AtLeastOnce);
	}

	[Test]
	public void SaveConfiguration_PersistsConfiguration()
	{
		var testFilePath = Path.Combine(_testBaseDirectory, "test2", "configuration.json");
		var fileSource = CreateMockFileSource(testFilePath);
		using var service = new JsonConfigurationService(fileSource.Object);

		var config = service.Configuration.Value;
		config.LastSourceDirectoryPath = "E:\\NewSource";
		config.ApplicationAssociationPairs.Add(
			new FileApplicationAssociation
			{
				Name = "TestApp",
				FileExtension = ".test",
				ApplicationPath = "C:\\Test.exe"
			});

		service.SaveConfiguration();

		var savedContent = File.ReadAllText(testFilePath);
		var savedConfig = JsonSerializer.Deserialize<Configuration>(savedContent);

		Assert.That(savedConfig.LastSourceDirectoryPath, Is.EqualTo("E:\\NewSource"));
		Assert.That(savedConfig.ApplicationAssociationPairs, Has.Count.EqualTo(1));
		fileSource.Verify(x => x.EnsureDirectoryExists(), Times.AtLeast(2));
	}

	[Test]
	public void SaveConfiguration_NotifiesObservers()
	{
		var testFilePath = Path.Combine(_testBaseDirectory, "test3", "configuration.json");
		var fileSource = CreateMockFileSource(testFilePath);
		using var service = new JsonConfigurationService(fileSource.Object);

		Configuration receivedConfig = null;
		using var subscription = service.ConfigurationSaved.Subscribe(config => receivedConfig = config);

		service.SaveConfiguration();

		Assert.That(receivedConfig, Is.Not.Null);
		Assert.That(receivedConfig, Is.EqualTo(service.Configuration.Value));
	}

	[Test]
	public void LoadConfiguration_WithCorruptedFile_Throws_ServiceException()
	{
		var testFilePath = Path.Combine(_testBaseDirectory, "test4", "configuration.json");
		var fileSource = CreateMockFileSource(testFilePath);
		fileSource.Object.EnsureDirectoryExists();
		File.WriteAllText(testFilePath, "{ invalid json }");

		var service = new JsonConfigurationService(fileSource.Object);
		Assert.Throws<ServiceException>(() => { _ = service.Configuration.Value; });
	}

	[Test]
	public void SaveConfiguration_AfterDisposal_ThrowsObjectDisposedException()
	{
		var testFilePath = Path.Combine(_testBaseDirectory, "test5", "configuration.json");
		var fileSource = CreateMockFileSource(testFilePath);
		var service = new JsonConfigurationService(fileSource.Object);
		service.Dispose();

		Assert.Throws<ObjectDisposedException>(() => service.SaveConfiguration());
	}

	[Test]
	public void Constructor_NullFileSource_ThrowsArgumentNullException()
	{
		Assert.Throws<ArgumentNullException>(() => new JsonConfigurationService(null));
	}

	[Test]
	public void Configuration_IsLazyLoaded()
	{
		var testFilePath = Path.Combine(_testBaseDirectory, "test6", "configuration.json");
		var fileSource = CreateMockFileSource(testFilePath);
		using var service = new JsonConfigurationService(fileSource.Object);

		Assert.That(service.Configuration.IsValueCreated, Is.False);

		var config = service.Configuration.Value;

		Assert.That(service.Configuration.IsValueCreated, Is.True);
	}

	[Test]
	public void FileSource_MethodsAreCalledCorrectly()
	{
		var testFilePath = Path.Combine(_testBaseDirectory, "test7", "configuration.json");
		var mockFileSource = CreateMockFileSource(testFilePath);

		using var service = new JsonConfigurationService(mockFileSource.Object);

		mockFileSource.Verify(x => x.ConfigurationFileExists(), Times.AtLeastOnce);
		mockFileSource.Verify(x => x.EnsureDirectoryExists(), Times.AtLeastOnce);
	}

	[Test]
	public void LoadConfiguration_WithExistingValidFile_ReturnsDeserializedConfiguration()
	{
		var testFilePath = Path.Combine(_testBaseDirectory, "test8", "configuration.json");
		var expectedConfig = new Configuration
		{
			LastSourceDirectoryPath = "C:\\ExistingSource",
			LastTargetDirectoryPath = "D:\\ExistingTarget"
		};

		Directory.CreateDirectory(Path.GetDirectoryName(testFilePath));
		File.WriteAllText(testFilePath, JsonSerializer.Serialize(expectedConfig));

		var fileSource = CreateMockFileSource(testFilePath);
		using var service = new JsonConfigurationService(fileSource.Object);

		var result = service.Configuration.Value;

		Assert.That(result.LastSourceDirectoryPath, Is.EqualTo("C:\\ExistingSource"));
		Assert.That(result.LastTargetDirectoryPath, Is.EqualTo("D:\\ExistingTarget"));
	}

	private static Mock<IConfigurationFileSource> CreateMockFileSource(string filePath)
	{
		var mock = new Mock<IConfigurationFileSource>();
		mock.Setup(x => x.GetConfigurationFilePath()).Returns(filePath);
		mock.Setup(x => x.ConfigurationFileExists()).Returns(() => File.Exists(filePath));
		mock.Setup(x => x.EnsureDirectoryExists())
		    .Callback(() =>
		    {
			    var directory = Path.GetDirectoryName(filePath);
			    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
			    {
				    Directory.CreateDirectory(directory);
			    }
		    });
		return mock;
	}
}