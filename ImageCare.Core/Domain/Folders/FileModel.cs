namespace ImageCare.Core.Domain.Folders;

public sealed class FileModel
{
	public FileModel(string? name, string fullName, DateTime? createdDateTime)
	{
		Name = name;
		FullName = fullName;
		CreatedDateTime = createdDateTime ?? GetFileCreationTime(fullName);
	}

	public string? Name { get; }

	public string FullName { get; }

	public DateTime? CreatedDateTime { get; }

	private static DateTime? GetFileCreationTime(string filePath)
	{
		try
		{
			var fileInfo = new FileInfo(filePath);
			return fileInfo.LastWriteTime;
		}
		catch (Exception)
		{
			return null;
		}
	}
}