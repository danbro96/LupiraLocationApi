namespace LupiraLocationApi.Domain.Telemetry;

/// <summary>A reverse-geocoded place name, cached and keyed by a quantized (~100 m grid) coordinate so nearby fixes share
/// one entry. Lives in <c>location</c>; lets raw GPS keep short retention without losing resolved names.</summary>
public sealed class PlaceLabel
{
    public Guid Id { get; set; }            // == MakeId(quantized lat, quantized lon)
    public double Lat { get; set; }
    public double Lon { get; set; }
    public string Label { get; set; } = "";
    public string Source { get; set; } = "";
    public DateTimeOffset ResolvedAt { get; set; }

    /// <summary>~100 m grid quantization (≈0.001° lat). Both coords are snapped, so the id is stable for a cell.</summary>
    public static (double Lat, double Lon) Quantize(double lat, double lon) =>
        (Math.Round(lat, 3), Math.Round(lon, 3));

    public static Guid MakeId(double lat, double lon)
    {
        var (qlat, qlon) = Quantize(lat, lon);
        return DeterministicGuid.From($"place:{qlat.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{qlon.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
    }
}
