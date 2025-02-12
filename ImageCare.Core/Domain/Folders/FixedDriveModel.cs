namespace ImageCare.Core.Domain.Folders;

public sealed class FixedDriveModel(string name, string path) : DriveModel(name, path) { }