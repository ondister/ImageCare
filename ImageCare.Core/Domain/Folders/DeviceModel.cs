namespace ImageCare.Core.Domain.Folders;

public sealed class DeviceModel(string name, string path) : DriveModel(name, path)
{
}