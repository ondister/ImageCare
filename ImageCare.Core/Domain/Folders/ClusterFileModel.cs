namespace ImageCare.Core.Domain.Folders;

public sealed class ClusterFileModel : FileModel
{
    /// <inheritdoc />
    public ClusterFileModel(string? name, string fullName, DateTime? createdDateTime)
        : base(name, fullName, createdDateTime) { }

    public ClusterFileModel(FileModel fileModel)
        : base(fileModel.Name, fileModel.FullName, fileModel.CreatedDateTime) { }

    /// <summary>
    ///     Field for clusterization algorithm
    /// </summary>
    public double Timestamp => CreatedDateTime?.ToOADate() ?? 0;
}