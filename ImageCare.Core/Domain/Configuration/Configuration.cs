
namespace ImageCare.Core.Domain.Configuration;

public sealed class Configuration
{
    public static string GetDefaultSafeDeletionDirectoryPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ImageCare",
            "DeletedFiles");
    }

    public string UiCultureName { get; set; } = string.Empty;


    public bool TimelineExpanded { get; set; } = true;


    public MediaSortDirection LeftPreviewSortDirection { get; set; } = MediaSortDirection.NewestFirst;


    public MediaSortDirection RightPreviewSortDirection { get; set; } = MediaSortDirection.NewestFirst;


    public MediaSortDirection FolderOverviewSortDirection { get; set; } = MediaSortDirection.NewestFirst;


    public bool RestoreLastFoldersOnStartup { get; set; } = true;


    public DragDropModifierKey DragDropModifier { get; set; } = DragDropModifierKey.Control;


    public bool CopyOnDragDropByDefault { get; set; }


    public bool SafeDeletionEnabled { get; set; } = true;


    public string SafeDeletionDirectoryPath { get; set; } = GetDefaultSafeDeletionDirectoryPath();


    public int SafeDeletionMaxFileCount { get; set; } = 100;


	public string LastSourceDirectoryPath { get; set; } = string.Empty;


	public string LastTargetDirectoryPath { get; set; } = string.Empty;


	public List<FileApplicationAssociation> ApplicationAssociationPairs { get; set; } = new();


    public int ApplicationAssociationsDefaultsVersion { get; set; }


    public List<string> RecentFolderPaths { get; set; } = new();


    public List<string> PopularFolderPaths { get; set; } = new();


    public List<string> SmartFolderPaths { get; set; } = new();
}