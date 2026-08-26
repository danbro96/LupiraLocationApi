namespace LupiraLocationApi.Core.Dtos.Location;

/// <summary>The resume cursor for a device: the highest accepted seq + its timestamp (from the latest-snapshot table).</summary>
public sealed class LocationCursor
{
    public required Guid DeviceId { get; set; }

    public long? LastSeq { get; set; }

    public DateTimeOffset? LastTs { get; set; }
}
