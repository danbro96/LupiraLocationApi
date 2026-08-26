namespace LupiraLocationApi.Core.Dtos.Location;

/// <summary>The outcome of a location ingest batch. Idempotent re-uploads show up as <see cref="Duplicates"/>; the
/// uploader advances past <see cref="HighWaterSeq"/>. When tracking is paused the body is discarded and
/// <see cref="Paused"/> is true.</summary>
public sealed class LocationIngestReceipt
{
    public required int Submitted { get; set; }

    public required int Inserted { get; set; }

    public required int Duplicates { get; set; }

    public required int Rejected { get; set; }

    public long? HighWaterSeq { get; set; }

    public required IReadOnlyList<IngestReject> Rejects { get; set; }

    public bool Paused { get; set; }

    public static LocationIngestReceipt PausedReceipt { get; } = new()
    {
        Submitted = 0,
        Inserted = 0,
        Duplicates = 0,
        Rejected = 0,
        HighWaterSeq = null,
        Rejects = [],
        Paused = true,
    };
}
