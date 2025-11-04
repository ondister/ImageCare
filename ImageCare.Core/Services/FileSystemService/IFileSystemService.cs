namespace ImageCare.Core.Services.FileSystemService;

public interface IFileSystemService
{
	bool FileExists(string path);

	bool DirectoryExists(string path);

	bool IsDirectory(string path);

	FileInfo GetFileInfo(string path);

	Stream OpenRead(string path);

	Stream Create(string path);

	void MoveFile(string source, string destination);

	void DeleteFile(string path);

	void MoveDirectory(string source, string destination);

	void DeleteDirectory(string path, bool recursive);

	void CreateDirectory(string path);

	string[] GetFiles(string directory);

	string[] GetDirectories(string directory);

	void CopyFileMetadata(string source, string destination);

	void CopyDirectoryMetadata(string source, string destination);

	void SafeDelete(string path);

	void SafeDeleteDirectory(string path);
}