namespace ImageCare.Core.Domain;

public sealed class DeviceModel(string name, string path) : DriveModel(name, path)
{
}