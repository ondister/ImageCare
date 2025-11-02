using ImageCare.Core.Domain.Folders;

namespace ImageCare.Core.Tests.Domain.Folders;

[TestFixture]
public class FileModelTests
{
	private string _testFilePath;

	[SetUp]
	public void SetUp()
	{
		_testFilePath = Path.GetTempFileName();
		File.WriteAllText(_testFilePath, "test content");
	}

	[TearDown]
	public void TearDown()
	{
		if (File.Exists(_testFilePath))
		{
			File.Delete(_testFilePath);
		}
	}

	[Test]
	public void Constructor_WithValidParameters_ShouldSetProperties()
	{
		var name = "test.txt";
		var fullName = _testFilePath;
		var createdDateTime = DateTime.Now;

		var fileModel = new FileModel(name, fullName, createdDateTime);

		Assert.That(fileModel.Name, Is.EqualTo(name));
		Assert.That(fileModel.FullName, Is.EqualTo(fullName));
		Assert.That(fileModel.CreatedDateTime, Is.EqualTo(createdDateTime));
	}

	[Test]
	public void Constructor_WithNullCreatedDateTime_ShouldGetFromFileSystem()
	{
		var expectedTime = File.GetLastWriteTime(_testFilePath);

		var fileModel = new FileModel("test.txt", _testFilePath, null);

		Assert.That(fileModel.CreatedDateTime, Is.EqualTo(expectedTime));
	}

	[Test]
	public void Constructor_WithInvalidFilePath_ShouldSetNullCreatedDateTime()
	{
		var invalidPath = "invalid_path://invalid_file.txt";

		var fileModel = new FileModel("test.txt", invalidPath, null);

		Assert.That(fileModel.CreatedDateTime, Is.Null);
	}

	[Test]
	public void Constructor_WithNullName_ShouldNotThrow()
	{
		Assert.DoesNotThrow(() => new FileModel(null, _testFilePath, DateTime.Now));
	}
}