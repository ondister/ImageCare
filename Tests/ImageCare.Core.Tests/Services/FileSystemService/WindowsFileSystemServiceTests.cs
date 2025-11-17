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
            Assert.That(result, Is.True);
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
            Assert.That(result, Is.False);
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

        Assert.That(() => service.IsDirectory(nonExistentPath), Throws.TypeOf<FileNotFoundException>());
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

            Assert.That(File.GetAttributes(destFile), Is.EqualTo(originalAttributes));
            Assert.That(File.GetCreationTime(destFile), Is.EqualTo(originalCreationTime).Within(10).Milliseconds);
            Assert.That(File.GetLastWriteTime(destFile), Is.EqualTo(originalWriteTime).Within(10).Milliseconds);
            Assert.That(File.GetLastAccessTime(destFile), Is.EqualTo(originalAccessTime).Within(10).Milliseconds);
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

            Assert.That(File.GetAttributes(destDir), Is.EqualTo(originalAttributes));
            Assert.That(Directory.GetCreationTime(destDir), Is.EqualTo(originalCreationTime).Within(10).Milliseconds);
            Assert.That(Directory.GetLastWriteTime(destDir), Is.EqualTo(originalWriteTime).Within(10).Milliseconds);
            Assert.That(Directory.GetLastAccessTime(destDir), Is.EqualTo(originalAccessTime).Within(10).Milliseconds);
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

        Assert.That(File.Exists(tempFile), Is.False);
    }

    [Test]
    public void SafeDelete_WhenFileDoesNotExist_DoesNothing()
    {
        var service = new WindowsFileSystemService();
        var nonExistentFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        Assert.That(() => service.SafeDelete(nonExistentFile), Throws.Nothing);
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

        Assert.That(Directory.Exists(tempDir), Is.False);
    }

    [Test]
    public void GetFileInfo_ReturnsValidFileInfo()
    {
        var service = new WindowsFileSystemService();
        var tempFile = Path.GetTempFileName();

        try
        {
            var fileInfo = service.GetFileInfo(tempFile);

            Assert.That(fileInfo, Is.Not.Null);
            Assert.That(fileInfo.Exists, Is.True);
            Assert.That(fileInfo.FullName, Is.EqualTo(tempFile));
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

            Assert.That(stream, Is.Not.Null);
            Assert.That(stream.CanRead, Is.True);
            Assert.That(stream.CanWrite, Is.False);
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

            Assert.That(stream, Is.Not.Null);
            Assert.That(stream.CanWrite, Is.True);
            Assert.That(File.Exists(tempFile), Is.True);
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
            var parentDir = Directory.GetParent(tempDir)!.FullName;
            var newPath = Path.Combine(parentDir, newName);
            Assert.That(Directory.Exists(newPath), Is.True);
        }
        finally
        {
            var parentDir = Directory.GetParent(tempDir)!.FullName;
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

        Assert.That(() => service.RenameFolder("NewName", null!), Throws.TypeOf<ServiceException>());
    }

    [Test]
    public void RenameFolder_WithEmptyPath_ThrowsServiceException()
    {
        var service = new WindowsFileSystemService();

        Assert.That(() => service.RenameFolder("NewName", ""), Throws.TypeOf<ServiceException>());
    }

    [Test]
    public void RenameFolder_WithWhitespacePath_ThrowsServiceException()
    {
        var service = new WindowsFileSystemService();

        Assert.That(() => service.RenameFolder("NewName", "   "), Throws.TypeOf<ServiceException>());
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
            var result = service.RenameFolder(null!, tempDir);

            Assert.That(result, Is.EqualTo(Path.GetFileName(tempDir)));
            Assert.That(Directory.Exists(tempDir), Is.True); // Directory should still exist
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
            Assert.That(Directory.Exists(tempDir), Is.True); // Directory should still exist
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
            Assert.That(Directory.Exists(tempDir1), Is.True); // Original directory should still exist
            Assert.That(Directory.Exists(tempDir2), Is.True); // Existing directory should still exist
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

            Assert.That(() => service.RenameFolder(invalidName, tempDir), Throws.TypeOf<ServiceException>());
            Assert.That(Directory.Exists(tempDir), Is.True); // Directory should still exist after failed rename
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