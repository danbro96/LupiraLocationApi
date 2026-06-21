using LupiraLocationApi.Domain;
using LupiraLocationApi.Dtos.Devices;
using LupiraLocationApi.Mappers;
using Marten;

namespace LupiraLocationApi.Application;

/// <summary>Registers and manages a principal's location-tracking devices (plain-doc CRUD). Registration mints a
/// per-device ingest API key (the plaintext is returned once); retiring a device revokes its keys. Every device is
/// owned directly by the principal that registered it — phase 1 has no sharing, so ownership is the only gate.</summary>
public sealed class DeviceService(IDocumentSession session)
{
    public async Task<OpResult<List<DeviceDto>>> ListAsync(Guid principalId, CancellationToken ct = default)
    {
        var devices = await session.Query<Device>().Where(d => d.PrincipalId == principalId).ToListAsync(ct);
        return OpResult<List<DeviceDto>>.Ok(devices.Select(d => d.ToResponse()).ToList());
    }

    public async Task<OpResult<RegisterDeviceResponse>> RegisterAsync(Guid principalId, RegisterDeviceRequest r, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.Label)) return OpResult<RegisterDeviceResponse>.Invalid("Label is required.");

        var device = new Device
        {
            Id = Guid.NewGuid(),
            PrincipalId = principalId,
            Kind = r.Kind,
            Label = r.Label.Trim(),
            ExternalId = r.ExternalId,
            RegisteredAt = DateTimeOffset.UtcNow,
        };
        session.Store(device);

        var (keyId, secret, hash) = DeviceKeyHashing.Mint();
        session.Store(new DeviceApiKey
        {
            Id = keyId,
            PrincipalId = principalId,
            DeviceId = device.Id,
            KeyHash = hash,
            Scopes = ["ingest"],
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await session.SaveChangesAsync(ct);

        return OpResult<RegisterDeviceResponse>.Ok(new RegisterDeviceResponse
        {
            Device = device.ToResponse(),
            KeyId = keyId,
            ApiKey = DeviceKeyHashing.Format(keyId, secret),
        });
    }

    public async Task<OpResult<DeviceDto>> RenameAsync(Guid principalId, Guid deviceId, RenameDeviceRequest r, CancellationToken ct = default)
    {
        var device = await session.LoadAsync<Device>(deviceId, ct);
        if (device is null || device.PrincipalId != principalId) return OpResult<DeviceDto>.NotFound();
        if (string.IsNullOrWhiteSpace(r.Label)) return OpResult<DeviceDto>.Invalid("Label is required.");
        device.Label = r.Label.Trim();
        session.Store(device);
        await session.SaveChangesAsync(ct);
        return OpResult<DeviceDto>.Ok(device.ToResponse());
    }

    public async Task<OpResult> RetireAsync(Guid principalId, Guid deviceId, CancellationToken ct = default)
    {
        var device = await session.LoadAsync<Device>(deviceId, ct);
        if (device is null || device.PrincipalId != principalId || device.RetiredAt is not null) return OpResult.NotFound();
        device.RetiredAt = DateTimeOffset.UtcNow;
        session.Store(device);

        // Revoke the device's ingest keys so a retired device can no longer push.
        var keys = await session.Query<DeviceApiKey>().Where(k => k.DeviceId == deviceId && k.RevokedAt == null).ToListAsync(ct);
        foreach (var k in keys) { k.RevokedAt = DateTimeOffset.UtcNow; session.Store(k); }
        await session.SaveChangesAsync(ct);
        return OpResult.Ok();
    }
}
