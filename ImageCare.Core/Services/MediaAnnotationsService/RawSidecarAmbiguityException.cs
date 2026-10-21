namespace ImageCare.Core.Services.MediaAnnotationsService;

public sealed class RawSidecarAmbiguityException : IOException
{
    public RawSidecarAmbiguityException(string sidecarPath, IReadOnlyCollection<string> ownerPaths)
        : base($"The XMP sidecar '{sidecarPath}' has multiple proprietary RAW owners: {string.Join(", ", ownerPaths)}")
    {
        SidecarPath = sidecarPath;
        OwnerPaths = ownerPaths.ToArray();
    }

    public string SidecarPath { get; }

    public IReadOnlyList<string> OwnerPaths { get; }
}
