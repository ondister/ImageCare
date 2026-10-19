
namespace ImageCare.Core.Domain.Configuration;

public sealed class Configuration
{
    public string UiCultureName { get; set; } = string.Empty;


    public bool RestoreLastFoldersOnStartup { get; set; } = true;


	public string LastSourceDirectoryPath { get; set; } = string.Empty;


	public string LastTargetDirectoryPath { get; set; } = string.Empty;


	public List<FileApplicationAssociation> ApplicationAssociationPairs { get; set; } = new();


    public int ApplicationAssociationsDefaultsVersion { get; set; }


    public List<string> RecentFolderPaths { get; set; } = new();


    public List<string> SmartFolderPaths { get; set; } = new();
}