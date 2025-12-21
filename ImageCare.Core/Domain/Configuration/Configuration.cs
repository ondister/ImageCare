
namespace ImageCare.Core.Domain.Configuration;

public sealed class Configuration
{

	public string LastSourceDirectoryPath { get; set; } = string.Empty;


	public string LastTargetDirectoryPath { get; set; } = string.Empty;


	public List<FileApplicationAssociation> ApplicationAssociationPairs { get; set; } = new();


    public List<string> RecentFolderPaths { get; set; } = new();


    public List<string> SmartFolderPaths { get; set; } = new();
}