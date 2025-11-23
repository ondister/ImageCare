using ImageCare.Core.Exceptions;

using Microsoft.VisualBasic.FileIO;

using SearchOption = System.IO.SearchOption;

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

    public DirectoryInfo GetDirectoryInfo(string path)
    {
        return new DirectoryInfo(path);
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

    /// <inheritdoc />
    public void CopyFile(string source, string destination)
    {
        File.Copy(source, destination);
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

    /// <inheritdoc />
    public string[] GetFiles(string directory, string searchPattern)
    {
        return Directory.GetFiles(directory, searchPattern);
    }

    /// <inheritdoc />
    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern)
    {
        return Directory.EnumerateFiles(directory, searchPattern, SearchOption.TopDirectoryOnly);
    }

    public IEnumerable<FileData> EnumerateFiles(string directory, string searchPattern, SearchOption searchOption)
    {
        return FastDirectoryEnumerator.EnumerateFiles(directory, searchPattern, searchOption);
    }

    public string[] GetDirectories(string directory)
    {
        return Directory.GetDirectories(directory);
    }

    /// <inheritdoc />
    public IEnumerable<string> EnumerateDirectories(string directory, string searchPattern)
    {
        return Directory.EnumerateDirectories(directory, searchPattern);
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
        FileSystem.DeleteDirectory(path, UIOption.AllDialogs, RecycleOption.SendToRecycleBin);
    }

    public string GetFileExtension(string path)
    {
        return Path.GetExtension(path);
    }

    public string? RenameFolder(string? newName, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ServiceException("Path cannot be null or empty");
        }

        try
        {
            var directoryInfo = GetDirectoryInfo(path);
            if (!directoryInfo.Exists)
            {
                return directoryInfo.Name;
            }

            if (string.IsNullOrWhiteSpace(newName))
            {
                return directoryInfo.Name;
            }

            var newPath = Path.Combine(directoryInfo.Parent.FullName, newName);
            if (DirectoryExists(newPath))
            {
                return directoryInfo.Name;
            }

            FileSystem.RenameDirectory(path, newName);
            return newName;
        }
        catch (Exception ex) when (ex is not ServiceException)
        {
            throw new ServiceException($"Failed to rename folder from '{path}' to '{newName}'", ex);
        }
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