namespace ImageCare.Core.Domain;

public sealed class FileApplicationInfo
{
    public FileApplicationInfo(string name, string applicationPath)
    {
        Name = name;
        ApplicationPath = applicationPath;
    }

    public string Name { get; }

    public string ApplicationPath { get; }
}