using LupiraLocationApi.Core.Domain;
using LupiraLocationApi.Core.Dtos.Devices;

namespace LupiraLocationApi.Core.Mappers;

internal static class DeviceMapper
{
    public static DeviceDto ToResponse(this Device d) =>
        new() { Id = d.Id, Kind = d.Kind, Label = d.Label, ExternalId = d.ExternalId, RegisteredAt = d.RegisteredAt, RetiredAt = d.RetiredAt };
}
