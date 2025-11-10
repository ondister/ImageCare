using ImageCare.Core.Exceptions;
using ImageCare.Core.Services.FileSystemService;

namespace ImageCare.Core.Tests.Services.FileSystemService;

[TestFixture]
public class WindowsFileSystemServiceTests
{
	[Test]
	public void IsDirectory_WhenDirectoryExists_ReturnsTrue()
	{
		var service = new WindowsFileSystemService();
		var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		Directory.CreateDirectory(tempDir);

		try
		{
			var result = service.IsDirectory(tempDir);
			Assert.IsTrue(result);
		}
		finally
		{
			Directory.Delete(tempDir);
		}
	}

	[Test]
	public void IsDirectory_WhenFileExists_ReturnsFalse()
	{
		var service = new WindowsFileSystemService();
		var tempFile = Path.GetTempFileName();

		try
		{
			var result = service.IsDirectory(tempFile);
			Assert.IsFalse(result);
		}
		finally
		{
			File.Delete(tempFile);
		}
	}

	[Test]
	public void IsDirectory_WhenPathNotFound_ThrowsFileNotFoundException()
	{
		var service = new WindowsFileSystemService();
		var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

		Assert.Throws<FileNotFoundException>(() => service.IsDirectory(nonExistentPath));
	}

	[Test]
	public void CopyFileMetadata_CopiesAttributesAndTimestamps()
	{
		var service = new WindowsFileSystemService();
		var sourceFile = Path.GetTempFileName();
		var destFile = Path.GetTempFileName();

		try
		{
			var originalAttributes = FileAttributes.Hidden | FileAttributes.Archive;
			var originalCreationTime = DateTime.Now.AddDays(-1);
			var originalWriteTime = DateTime.Now.AddHours(-1);
			var originalAccessTime = DateTime.Now.AddHours(-2);

			File.SetAttributes(sourceFile, originalAttributes);
			File.SetCreationTime(sourceFile, originalCreationTime);
			File.SetLastWriteTime(sourceFile, originalWriteTime);
			File.SetLastAccessTime(sourceFile, originalAccessTime);

			service.CopyFileMetadata(sourceFile, destFile);

			Assert.AreEqual(originalAttributes, File.GetAttributes(destFile));
			Assert.AreEqual(originalCreationTime, File.GetCreationTime(destFile));
			Assert.AreEqual(originalWriteTime, File.GetLastWriteTime(destFile));
			Assert.AreEqual(originalAccessTime, File.GetLastAccessTime(destFile));
		}
		finally
		{
			File.SetAttributes(sourceFile, FileAttributes.Normal);
			File.SetAttributes(destFile, FileAttributes.Normal);
			File.Delete(sourceFile);
			File.Delete(destFile);
		}
	}

	[Test]
	public void CopyDirectoryMetadata_CopiesAttributesAndTimestamps()
	{
		var service = new WindowsFileSystemService();
		var sourceDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		var destDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

		Directory.CreateDirectory(sourceDir);
		Directory.CreateDirectory(destDir);

		try
		{
			var originalAttributes = FileAttributes.Hidden | FileAttributes.Directory;
			var originalCreationTime = DateTime.Now.AddDays(-1);
			var originalWriteTime = DateTime.Now.AddHours(-1);
			var originalAccessTime = DateTime.Now.AddHours(-2);

			File.SetAttributes(sourceDir, originalAttributes);
			Directory.SetCreationTime(sourceDir, originalCreationTime);
			Directory.SetLastWriteTime(sourceDir, originalWriteTime);
			Directory.SetLastAccessTime(sourceDir, originalAccessTime);

			service.CopyDirectoryMetadata(sourceDir, destDir);

			Assert.AreEqual(originalAttributes, File.GetAttributes(destDir));
			Assert.AreEqual(originalCreationTime, Directory.GetCreationTime(destDir));
			Assert.AreEqual(originalWriteTime, Directory.GetLastWriteTime(destDir));
			Assert.AreEqual(originalAccessTime, Directory.GetLastAccessTime(destDir));
		}
		finally
		{
			File.SetAttributes(sourceDir, FileAttributes.Normal);
			File.SetAttributes(destDir, FileAttributes.Normal);
			Directory.Delete(sourceDir);
			Directory.Delete(destDir);
		}
	}

	[Test]
	public void SafeDelete_RemovesReadOnlyFile()
	{
		var service = new WindowsFileSystemService();
		var tempFile = Path.GetTempFileName();

		File.SetAttributes(tempFile, FileAttributes.ReadOnly);
		service.SafeDelete(tempFile);

		Assert.IsFalse(File.Exists(tempFile));
	}

	[Test]
	public void SafeDelete_WhenFileDoesNotExist_DoesNothing()
	{
		var service = new WindowsFileSystemService();
		var nonExistentFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

		Assert.DoesNotThrow(() => service.SafeDelete(nonExistentFile));
	}

	[Test]
	public void SafeDeleteDirectory_RemovesDirectoryWithReadOnlyFiles()
	{
		var service = new WindowsFileSystemService();
		var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		Directory.CreateDirectory(tempDir);

		var readOnlyFile = Path.Combine(tempDir, "readonly.txt");
		File.WriteAllText(readOnlyFile, "test");
		File.SetAttributes(readOnlyFile, FileAttributes.ReadOnly);

		service.SafeDeleteDirectory(tempDir);

		Assert.IsFalse(Directory.Exists(tempDir));
	}

	[Test]
	public void GetFileInfo_ReturnsValidFileInfo()
	{
		var service = new WindowsFileSystemService();
		var tempFile = Path.GetTempFileName();

		try
		{
			var fileInfo = service.GetFileInfo(tempFile);

			Assert.IsNotNull(fileInfo);
			Assert.IsTrue(fileInfo.Exists);
			Assert.AreEqual(tempFile, fileInfo.FullName);
		}
		finally
		{
			File.Delete(tempFile);
		}
	}

	[Test]
	public void OpenRead_CreatesReadableStream()
	{
		var service = new WindowsFileSystemService();
		var tempFile = Path.GetTempFileName();
		File.WriteAllText(tempFile, "test content");

		try
		{
			using var stream = service.OpenRead(tempFile);

			Assert.IsNotNull(stream);
			Assert.IsTrue(stream.CanRead);
			Assert.IsFalse(stream.CanWrite);
		}
		finally
		{
			File.Delete(tempFile);
		}
	}

	[Test]
	public void Create_CreatesWritableStream()
	{
		var service = new WindowsFileSystemService();
		var tempFile = Path.GetTempFileName();
		File.Delete(tempFile);

		try
		{
			using var stream = service.Create(tempFile);

			Assert.IsNotNull(stream);
			Assert.IsTrue(stream.CanWrite);
			Assert.IsTrue(File.Exists(tempFile));
		}
		finally
		{
			if (File.Exists(tempFile))
			{
				File.Delete(tempFile);
			}
		}
	}

	[Test]
	public void RenameFolder_WithValidName_ReturnsNewName()
	{
		var service = new WindowsFileSystemService();
		var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		var newName = "RenamedFolder";
		Directory.CreateDirectory(tempDir);

		try
		{
			var result = service.RenameFolder(newName, tempDir);

			Assert.That(result, Is.EqualTo(newName));
			var parentDir = Directory.GetParent(tempDir).FullName;
			var newPath = Path.Combine(parentDir, newName);
			Assert.IsTrue(Directory.Exists(newPath));
		}
		finally
		{
			var parentDir = Directory.GetParent(tempDir).FullName;
			var newPath = Path.Combine(parentDir, newName);
			if (Directory.Exists(newPath))
			{
				Directory.Delete(newPath);
			}

			if (Directory.Exists(tempDir))
			{
				Directory.Delete(tempDir);
			}
		}
	}

	[Test]
	public void RenameFolder_WithNullPath_ThrowsServiceException()
	{
		var service = new WindowsFileSystemService();

		Assert.Throws<ServiceException>(() => service.RenameFolder("NewName", null));
	}

	[Test]
	public void RenameFolder_WithEmptyPath_ThrowsServiceException()
	{
		var service = new WindowsFileSystemService();

		Assert.Throws<ServiceException>(() => service.RenameFolder("NewName", ""));
	}

	[Test]
	public void RenameFolder_WithWhitespacePath_ThrowsServiceException()
	{
		var service = new WindowsFileSystemService();

		Assert.Throws<ServiceException>(() => service.RenameFolder("NewName", "   "));
	}

	[Test]
	public void RenameFolder_WithNonExistentDirectory_ReturnsOriginalName()
	{
		var service = new WindowsFileSystemService();
		var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

		var result = service.RenameFolder("NewName", nonExistentPath);

		Assert.That(result, Is.EqualTo(Path.GetFileName(nonExistentPath)));
	}

	[Test]
	public void RenameFolder_WithNullNewName_ReturnsOriginalName()
	{
		var service = new WindowsFileSystemService();
		var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		Directory.CreateDirectory(tempDir);

		try
		{
			var result = service.RenameFolder(null, tempDir);

			Assert.That(result, Is.EqualTo(Path.GetFileName(tempDir)));
			Assert.IsTrue(Directory.Exists(tempDir)); // Directory should still exist
		}
		finally
		{
			if (Directory.Exists(tempDir))
			{
				Directory.Delete(tempDir);
			}
		}
	}

	[Test]
	public void RenameFolder_WithEmptyNewName_ReturnsOriginalName()
	{
		var service = new WindowsFileSystemService();
		var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		Directory.CreateDirectory(tempDir);

		try
		{
			var result = service.RenameFolder("", tempDir);

			Assert.That(result, Is.EqualTo(Path.GetFileName(tempDir)));
			Assert.IsTrue(Directory.Exists(tempDir)); // Directory should still exist
		}
		finally
		{
			if (Directory.Exists(tempDir))
			{
				Directory.Delete(tempDir);
			}
		}
	}

	[Test]
	public void RenameFolder_WithExistingDestinationName_ReturnsOriginalName()
	{
		var service = new WindowsFileSystemService();
		var tempDir1 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		var tempDir2 = Path.Combine(Path.GetTempPath(), "ExistingFolder");
		Directory.CreateDirectory(tempDir1);
		Directory.CreateDirectory(tempDir2);

		try
		{
			var result = service.RenameFolder("ExistingFolder", tempDir1);

			Assert.That(result, Is.EqualTo(Path.GetFileName(tempDir1)));
			Assert.IsTrue(Directory.Exists(tempDir1)); // Original directory should still exist
			Assert.IsTrue(Directory.Exists(tempDir2)); // Existing directory should still exist
		}
		finally
		{
			if (Directory.Exists(tempDir1))
			{
				Directory.Delete(tempDir1);
			}

			if (Directory.Exists(tempDir2))
			{
				Directory.Delete(tempDir2);
			}
		}
	}

	[Test]
	public void RenameFolder_WithInvalidCharactersInName_ThrowsServiceException()
	{
		var service = new WindowsFileSystemService();
		var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		Directory.CreateDirectory(tempDir);

		try
		{
			var invalidName = "Invalid<Name>";

			Assert.Throws<ServiceException>(() => service.RenameFolder(invalidName, tempDir));
			Assert.IsTrue(Directory.Exists(tempDir)); // Directory should still exist after failed rename
		}
		finally
		{
			if (Directory.Exists(tempDir))
			{
				Directory.Delete(tempDir);
			}
		}
	}
}