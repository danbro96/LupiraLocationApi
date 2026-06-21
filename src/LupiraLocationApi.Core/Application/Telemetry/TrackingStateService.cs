using LupiraLocationApi.Domain.Telemetry;
using LupiraLocationApi.Dtos.Location;
using Marten;

namespace LupiraLocationApi.Application.Telemetry;

/// <summary>The per-device tracking kill-switch (pause/resume). While paused, location ingest is accepted but discarded;
/// the uploader polls the state to learn it should stop collecting.</summary>
public sealed class TrackingStateService(IDocumentSession session)
{
    public async Task<bool> IsPausedAsync(Guid principalId, Guid deviceId, CancellationToken ct = default)
    {
        var s = await session.LoadAsync<TrackingState>(TrackingState.MakeId(principalId, deviceId), ct);
        return s?.Paused ?? false;
    }

    public async Task<OpResult> PauseAsync(Guid principalId, Guid deviceId, string? reason, CancellationToken ct = default)
    {
        session.Store(new TrackingState
        {
            Id = TrackingState.MakeId(principalId, deviceId),
            PrincipalId = principalId,
            DeviceId = deviceId,
            Paused = true,
            PausedAt = DateTimeOffset.UtcNow,
            Reason = reason,
        });
        await session.SaveChangesAsync(ct);
        return OpResult.Ok();
    }

    public async Task<OpResult> ResumeAsync(Guid principalId, Guid deviceId, CancellationToken ct = default)
    {
        var s = await session.LoadAsync<TrackingState>(TrackingState.MakeId(principalId, deviceId), ct)
            ?? new TrackingState { Id = TrackingState.MakeId(principalId, deviceId), PrincipalId = principalId, DeviceId = deviceId };
        s.Paused = false;
        s.PausedAt = null;
        s.Reason = null;
        session.Store(s);
        await session.SaveChangesAsync(ct);
        return OpResult.Ok();
    }

    public async Task<OpResult<TrackingStateDto>> StateAsync(Guid principalId, Guid deviceId, CancellationToken ct = default)
    {
        var s = await session.LoadAsync<TrackingState>(TrackingState.MakeId(principalId, deviceId), ct);
        return OpResult<TrackingStateDto>.Ok(new TrackingStateDto { DeviceId = deviceId, Paused = s?.Paused ?? false, PausedAt = s?.PausedAt, Reason = s?.Reason });
    }
}
