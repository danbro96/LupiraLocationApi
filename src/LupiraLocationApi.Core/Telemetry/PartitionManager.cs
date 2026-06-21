using Npgsql;

namespace LupiraLocationApi.Telemetry;

public enum PartitionInterval { Weekly, Monthly }

/// <summary>Creates time-range partitions on demand (idempotent) and drops expired ones for retention. Singleton; an
/// in-memory cache avoids re-issuing DDL for partitions already known to exist. Ingest calls <see cref="EnsureAsync"/>
/// for every period present in a batch so a (possibly late/backfilled) row always has a home partition — no DEFAULT
/// partition (which would be a silent COPY catch-all).</summary>
public sealed class PartitionManager
{
    private readonly HashSet<string> _ensured = new();
    private readonly object _gate = new();

    // A Monday epoch so weekly buckets align to week starts.
    private static readonly DateTimeOffset WeekEpoch = new(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public async Task EnsureAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, string parentTable, PartitionInterval interval, DateTimeOffset ts, CancellationToken ct = default)
    {
        var (name, lower, upper) = Bounds(parentTable, interval, ts);
        lock (_gate) { if (!_ensured.Add(name)) return; }

        var sql = $"CREATE TABLE IF NOT EXISTS telemetry.{name} PARTITION OF telemetry.{parentTable} " +
                  $"FOR VALUES FROM ('{Literal(lower)}') TO ('{Literal(upper)}')";
        try
        {
            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch
        {
            // Failed to create — forget it so a later attempt retries.
            lock (_gate) { _ensured.Remove(name); }
            throw;
        }
    }

    /// <summary>Drops partitions of <paramref name="parentTable"/> whose entire range is older than <paramref name="cutoff"/>.</summary>
    public async Task DropExpiredAsync(NpgsqlConnection conn, string parentTable, PartitionInterval interval, DateTimeOffset cutoff, CancellationToken ct = default)
    {
        var children = new List<string>();
        await using (var list = new NpgsqlCommand(
            "SELECT c.relname FROM pg_inherits i " +
            "JOIN pg_class c ON c.oid = i.inhrelid " +
            "JOIN pg_class p ON p.oid = i.inhparent " +
            "JOIN pg_namespace n ON n.oid = p.relnamespace " +
            "WHERE n.nspname = 'telemetry' AND p.relname = @parent", conn))
        {
            list.Parameters.AddWithValue("parent", parentTable);
            await using var reader = await list.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) children.Add(reader.GetString(0));
        }

        foreach (var name in children)
        {
            if (!TryParseUpper(parentTable, interval, name, out var upper)) continue;
            if (upper > cutoff) continue;
            await using var drop = new NpgsqlCommand($"DROP TABLE IF EXISTS telemetry.{name}", conn);
            await drop.ExecuteNonQueryAsync(ct);
            lock (_gate) { _ensured.Remove(name); }
        }
    }

    /// <summary>Pre-creates the partitions spanning [from, to] so upcoming/active windows exist before ingest.</summary>
    public async Task EnsureRangeAsync(NpgsqlConnection conn, string parentTable, PartitionInterval interval, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var cursor = Bounds(parentTable, interval, from).Lower;
        while (cursor <= to)
        {
            await EnsureAsync(conn, null, parentTable, interval, cursor, ct);
            cursor = interval == PartitionInterval.Weekly ? cursor.AddDays(7) : cursor.AddMonths(1);
        }
    }

    internal static (string Name, DateTimeOffset Lower, DateTimeOffset Upper) Bounds(string parentTable, PartitionInterval interval, DateTimeOffset ts)
    {
        var utc = ts.ToUniversalTime();
        if (interval == PartitionInterval.Weekly)
        {
            var weeks = (long)Math.Floor((utc - WeekEpoch).TotalDays / 7.0);
            var lower = WeekEpoch.AddDays(weeks * 7);
            return ($"{parentTable}_w{lower:yyyyMMdd}", lower, lower.AddDays(7));
        }
        var monthStart = new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return ($"{parentTable}_m{monthStart:yyyyMM}", monthStart, monthStart.AddMonths(1));
    }

    private static bool TryParseUpper(string parentTable, PartitionInterval interval, string childName, out DateTimeOffset upper)
    {
        upper = default;
        var prefix = interval == PartitionInterval.Weekly ? $"{parentTable}_w" : $"{parentTable}_m";
        if (!childName.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var stamp = childName[prefix.Length..];
        if (interval == PartitionInterval.Weekly)
        {
            if (!DateTimeOffset.TryParseExact(stamp, "yyyyMMdd", null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var lower)) return false;
            upper = lower.AddDays(7);
            return true;
        }
        if (!DateTimeOffset.TryParseExact(stamp, "yyyyMM", null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var mStart)) return false;
        upper = mStart.AddMonths(1);
        return true;
    }

    private static string Literal(DateTimeOffset b) => b.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss") + "+00";
}
