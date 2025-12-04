namespace ImageCare.Core.Domain.Folders;

public sealed class SmartDirectoryModel : DirectoryModel
{
    public SmartDirectoryModel(string? name, string path, int matchCount)
        : base(name, path)
    {
        if (matchCount < 1)
        {
            throw new ArgumentException("Match count must be positive", nameof(matchCount));
        }

        MatchCount = matchCount;
    }

    public int MatchCount { get; }
}