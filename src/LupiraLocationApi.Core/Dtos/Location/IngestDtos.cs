namespace LupiraLocationApi.Dtos.Location;

/// <summary>A per-row rejection within an otherwise-accepted batch (permanent — the uploader should drop + log it).</summary>
public sealed class IngestReject
{
    public long? Seq { get; set; }
    public required string Reason { get; set; }
}

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
        Submitted = 0, Inserted = 0, Duplicates = 0, Rejected = 0, HighWaterSeq = null, Rejects = [], Paused = true,
    };
}

/// <summary>The resume cursor for a device: the highest accepted seq + its timestamp (from the latest-snapshot table).</summary>
public sealed class LocationCursor
{
    public required Guid DeviceId { get; set; }
    public long? LastSeq { get; set; }
    public DateTimeOffset? LastTs { get; set; }
}
