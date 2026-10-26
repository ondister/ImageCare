namespace ImageCare.Core.Domain.Configuration;

public enum AdaptiveHelpUserGoal
{
    Unknown,
    OrganizeMedia,
    CopyMedia,
    SearchMedia,
    CleanUpMedia,
    Explore
}

public enum AdaptiveHelpDetailLevel
{
    Minimal,
    Standard,
    Detailed
}

public enum AdaptiveHelpMasteryLevel
{
    NotUsed,
    Tried,
    Completed,
    Mastered
}

public sealed class AdaptiveHelpProgressEntry
{
    public string HintId { get; set; } = string.Empty;

    public int ExposureCount { get; set; }

    public int SuccessfulActionCount { get; set; }

    public int DismissCount { get; set; }

    public AdaptiveHelpMasteryLevel MasteryLevel { get; set; }

    public DateTimeOffset? LastShownUtc { get; set; }

    public DateTimeOffset? LastCompletedUtc { get; set; }
}
