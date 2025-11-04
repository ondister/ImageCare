namespace ImageCare.Core.Services.FileSystemService;

public sealed class WindowsFileSystemService : IFileSystemService
{
	public bool FileExists(string path)
	{
		return File.Exists(path);
	}

	public bool DirectoryExists(string path)
	{
		return Directory.Exists(path);
	}

	public bool IsDirectory(string path)
	{
		if (DirectoryExists(path))
		{
			return true;
		}

		return FileExists(path) ? false : throw new FileNotFoundException("Path not found", path);
	}

	public FileInfo GetFileInfo(string path)
	{
		return new FileInfo(path);
	}

	public Stream OpenRead(string path)
	{
		return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
	}

	public Stream Create(string path)
	{
		return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
	}

	public void MoveFile(string source, string destination)
	{
		File.Move(source, destination);
	}

	public void DeleteFile(string path)
	{
		File.Delete(path);
	}

	public void MoveDirectory(string source, string destination)
	{
		Directory.Move(source, destination);
	}

	public void DeleteDirectory(string path, bool recursive)
	{
		Directory.Delete(path, recursive);
	}

	public void CreateDirectory(string path)
	{
		Directory.CreateDirectory(path);
	}

	public string[] GetFiles(string directory)
	{
		return Directory.GetFiles(directory);
	}

	public string[] GetDirectories(string directory)
	{
		return Directory.GetDirectories(directory);
	}

	public void CopyFileMetadata(string source, string destination)
	{
		var sourceInfo = new FileInfo(source);

		// Copy file attributes and timestamps
		File.SetAttributes(destination, sourceInfo.Attributes);
		File.SetCreationTime(destination, sourceInfo.CreationTime);
		File.SetLastWriteTime(destination, sourceInfo.LastWriteTime);
		File.SetLastAccessTime(destination, sourceInfo.LastAccessTime);
	}

	public void CopyDirectoryMetadata(string source, string destination)
	{
		var sourceInfo = new DirectoryInfo(source);

		// Copy directory attributes and timestamps
		File.SetAttributes(destination, sourceInfo.Attributes);
		Directory.SetCreationTime(destination, sourceInfo.CreationTime);
		Directory.SetLastWriteTime(destination, sourceInfo.LastWriteTime);
		Directory.SetLastAccessTime(destination, sourceInfo.LastAccessTime);
	}

	public void SafeDelete(string path)
	{
		if (!FileExists(path))
		{
			return;
		}

		// Reset attributes in case file is read-only
		File.SetAttributes(path, FileAttributes.Normal);
		DeleteFile(path);
	}

	public void SafeDeleteDirectory(string path)
	{
		if (!DirectoryExists(path))
		{
			return;
		}

		// Recursively reset attributes before deletion
		ResetAttributesRecursive(path);
		DeleteDirectory(path, true);
	}

	private void ResetAttributesRecursive(string path)
	{
		// Reset attributes for the directory itself
		File.SetAttributes(path, FileAttributes.Normal);

		// Reset attributes for all files in directory
		foreach (var file in GetFiles(path))
		{
			File.SetAttributes(file, FileAttributes.Normal);
		}

		// Recursively process subdirectories
		foreach (var dir in GetDirectories(path))
		{
			ResetAttributesRecursive(dir);
		}
	}
}