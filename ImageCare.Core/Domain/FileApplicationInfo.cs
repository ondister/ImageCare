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

// Comparer for distinct FileApplicationInfo
public sealed class FileApplicationInfoComparer : IEqualityComparer<FileApplicationInfo>
{
	public static readonly FileApplicationInfoComparer Instance = new();

	public bool Equals(FileApplicationInfo? x, FileApplicationInfo? y)
	{
		if (ReferenceEquals(x, y))
		{
			return true;
		}

		if (x is null || y is null)
		{
			return false;
		}

		return string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase) && string.Equals(x.ApplicationPath, y.ApplicationPath, StringComparison.OrdinalIgnoreCase);
	}

	public int GetHashCode(FileApplicationInfo obj)
	{
		return HashCode.Combine(
			obj.Name?.ToLowerInvariant(),
			obj.ApplicationPath?.ToLowerInvariant());
	}
}