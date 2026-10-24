using System.Text.Json.Serialization;

namespace ImageCare.Mcp.Sessions;

[JsonConverter(typeof(JsonStringEnumConverter<ReviewBatchState>))]
public enum ReviewBatchState
{
    Pending,
    Active,
    Completed
}

/// <summary>
/// A single review batch. <see cref="Items"/> keeps the original order so an agent can
/// reason about a shooting sequence.
/// </summary>
public sealed class ReviewBatch
{
    public int Index { get; set; }

    public List<string> Items { get; set; } = [];

    public ReviewBatchState State { get; set; } = ReviewBatchState.Pending;

    public DateTime? StartedUtc { get; set; }

    public DateTime? CompletedUtc { get; set; }
}

/// <summary>
/// A long-running "flash drive culling" session: a source folder split into deterministic
/// batches. The session is the shared source of truth between the user (working in the UI)
/// and the model (working through MCP).
/// </summary>
public sealed class ReviewSession
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string SourcePath { get; set; } = string.Empty;

    public bool Recursive { get; set; }

    public int BatchSize { get; set; } = 50;

    /// <summary>Rating at or above which a present file counts as kept. Zero disables rating-based keep.</summary>
    public int KeepRating { get; set; } = 1;

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }

    public int CurrentBatchIndex { get; set; }

    public int DeletedByAgent { get; set; }

    public List<ReviewBatch> Batches { get; set; } = [];

    [JsonIgnore]
    public int TotalFiles => Batches.Sum(batch => batch.Items.Count);

    public ReviewBatch? FindBatch(int? index)
    {
        if (index is { } explicitIndex)
        {
            return Batches.FirstOrDefault(batch => batch.Index == explicitIndex);
        }

        return Batches.FirstOrDefault(batch => batch.Index == CurrentBatchIndex)
               ?? Batches.FirstOrDefault(batch => batch.State != ReviewBatchState.Completed)
               ?? Batches.LastOrDefault();
    }

    public int EffectiveBatchIndex(int? requested) => FindBatch(requested)?.Index ?? CurrentBatchIndex;
}
