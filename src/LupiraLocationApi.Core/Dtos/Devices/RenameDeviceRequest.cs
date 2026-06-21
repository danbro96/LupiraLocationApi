namespace LupiraLocationApi.Dtos.Devices;

/// <summary>Rename a device.</summary>
public sealed class RenameDeviceRequest
{
    public required string Label { get; set; }
}
