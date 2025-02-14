namespace ImageCare.Core.Domain.Folders;

public class RemovableDriveModel(string name, string path, long totalSize, long availableFreeSpace) : DriveModel(name, path)
{
    public long TotalSize { get; } = totalSize;

    public long AvailableFreeSpace { get; } = availableFreeSpace;
}