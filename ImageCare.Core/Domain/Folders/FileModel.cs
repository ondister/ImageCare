namespace ImageCare.Core.Domain.Folders;

public sealed class FileModel
{
	private DateTime? _createdDateTime;

	public FileModel(string? name, string fullName, DateTime? createdDateTime)
	{
		Name = name;
		FullName = fullName;
		CreatedDateTime = createdDateTime;
	}

	public string? Name { get; }

	public string FullName { get; }

	public DateTime? CreatedDateTime
	{
		get => _createdDateTime;
		private set
		{
			if (value == null)
			{
				try
				{
					var fileInfo = new FileInfo(FullName);
					_createdDateTime = fileInfo.LastWriteTime;
				}
				catch (Exception e)
				{
					// Ignored
				}
			}
			else
			{
				_createdDateTime = value;
			}
		}
	}
}