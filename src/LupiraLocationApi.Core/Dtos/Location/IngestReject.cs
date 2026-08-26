namespace LupiraLocationApi.Core.Dtos.Location;

/// <summary>A per-row rejection within an otherwise-accepted batch (permanent — the uploader should drop + log it).</summary>
public sealed class IngestReject
{
    public long? Seq { get; set; }

    public required string Reason { get; set; }
}
