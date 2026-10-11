using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml.Media;

namespace SharpSaw;

public sealed class Detection
{
    public string Timestamp { get; init; } = "";
    public string Name { get; init; } = "";
    public string Group { get; init; } = "";
    public string Level { get; init; } = "";
    public string Status { get; init; } = "";
    public string Authors { get; init; } = "";
    public string RawJson { get; init; } = "";

    /// <summary>4 = critical, 3 = high, 2 = medium, 1 = low, 0 = autre / informatif.</summary>
    public int Rank => Level.ToLowerInvariant() switch
    {
        "critical" => 4, "high" => 3, "medium" => 2, "low" => 1, _ => 0,
    };

    public string SeverityLabel => Sev.Label(Rank);
    public string SeverityGlyph => Sev.Glyph(Rank);
    public Brush SeverityBrush => Sev.Brush(Rank);

    /// <summary>Heure locale lisible (l'UTC complet reste dans le JSON du détail).</summary>
    public string DisplayTime =>
        DateTimeOffset.TryParse(Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)
            : Timestamp;

    public static Detection FromJson(JsonElement e)
    {
        string S(string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        var authors = e.TryGetProperty("authors", out var a) && a.ValueKind == JsonValueKind.Array
            ? string.Join(", ", a.EnumerateArray().Select(x => x.GetString()))
            : "";
        return new Detection
        {
            Timestamp = S("timestamp"), Name = S("name"), Group = S("group"),
            Level = S("level"), Status = S("status"), Authors = authors,
            RawJson = JsonSerializer.Serialize(e, new JsonSerializerOptions { WriteIndented = true }),
        };
    }
}
