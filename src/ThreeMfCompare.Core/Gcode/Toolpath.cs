using System.Globalization;
using System.Numerics;

namespace ThreeMfCompare.Core.Gcode;

/// <summary>One straight piece of deposited bead, in bed coordinates (mm, s, °C).</summary>
/// <param name="Volume">Deposited plastic in mm³ (extruded filament length × filament cross-section).</param>
/// <param name="Duration">Seconds at the programmed feed rate (no acceleration).</param>
/// <param name="Filament">The filament (tool) printing it, from 0.</param>
/// <param name="Fan">Part-cooling fan, 0–255.</param>
public readonly record struct ExtrusionSegment(
    Vector3 Start,
    Vector3 End,
    float Width,
    float Height,
    float Volume,
    float Duration,
    int Layer,
    FeatureType Feature,
    byte Filament,
    byte Fan,
    ushort NozzleTemperature)
{
    public float Length => Vector3.Distance(Start, End);

    /// <summary>mm/s at the programmed feed rate.</summary>
    public float Speed => Duration > 0 ? Length / Duration : 0;

    /// <summary>mm³/s at the programmed feed rate.</summary>
    public float Flow => Duration > 0 ? Volume / Duration : 0;
}

/// <param name="Z">Top of the layer (Bambu's Z_HEIGHT).</param>
/// <param name="FirstSegment">Index into <see cref="Toolpath.Segments"/>; a layer's segments are contiguous.</param>
/// <param name="Seconds">The layer's time from feed rates: moves, travel and dwells, from its CHANGE_LAYER to the next.</param>
public sealed record ToolpathLayer(int Index, float Z, float Height, int FirstSegment, int SegmentCount, double Seconds);

/// <summary>Everything deposited by a G-code file, plus the header and the settings embedded in it.</summary>
public sealed class Toolpath
{
    public required ExtrusionSegment[] Segments { get; init; }
    public required ToolpathLayer[] Layers { get; init; }

    /// <summary>The CONFIG_BLOCK, as written: lists are comma separated.</summary>
    public required IReadOnlyDictionary<string, string> Settings { get; init; }

    /// <summary>The HEADER_BLOCK: "model printing time", "total estimated time", "total layer number", …</summary>
    public required IReadOnlyDictionary<string, string> Header { get; init; }

    /// <summary>Feature names the parser didn't recognise; their lines are typed Unknown.</summary>
    public required IReadOnlySet<string> UnknownFeatures { get; init; }

    /// <summary>Feed-rate time of the whole file in seconds. Shorter than the slicer's estimate, which includes acceleration.</summary>
    public required double TotalSeconds { get; init; }

    /// <summary>Number of tool changes.</summary>
    public required int ToolChanges { get; init; }

    public ReadOnlySpan<ExtrusionSegment> LayerSegments(ToolpathLayer layer) => Segments.AsSpan(layer.FirstSegment, layer.SegmentCount);

    /// <summary>Seconds from a header time such as "2h 9m 17s"; null if there is no such entry.</summary>
    public double? HeaderSeconds(string key) => Header.TryGetValue(key, out var text) ? ParseDuration(text) : null;

    /// <summary>A number of the header; a list of them, one per filament ("16345.66,210.69"), adds up.</summary>
    public double? HeaderNumber(string key)
    {
        if (!Header.TryGetValue(key, out var text)) return null;
        double sum = 0;
        foreach (var part in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return null;
            sum += value;
        }
        return sum;
    }

    /// <summary>
    /// The layer times on the slicer's clock: the feed-rate times of the layers, scaled so that they
    /// add up to the slicer's model printing time. Feed rates alone leave out acceleration, which
    /// short moves suffer most, so this is an approximation of each layer's share.
    /// </summary>
    public double[] LayerSeconds()
    {
        var raw = Layers.Select(l => l.Seconds).ToArray();
        var sum = raw.Sum();
        var target = HeaderSeconds("model printing time");
        var scale = target is > 0 && sum > 0 ? target.Value / sum : 1;
        return [.. raw.Select(s => s * scale)];
    }

    /// <summary>The layer printing height <paramref name="z"/>: the first whose top is at or above it; the last if none is.</summary>
    public int LayerAt(float z)
    {
        int low = 0, high = Layers.Length - 1;
        if (high < 0) return -1;
        if (z > Layers[high].Z) return high;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (Layers[mid].Z >= z - 1e-4f) high = mid;
            else low = mid + 1;
        }
        return low;
    }

    public static double? ParseDuration(string text)
    {
        double total = 0;
        var any = false;
        foreach (var part in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var unit = part[^1];
            if (!double.TryParse(part[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) continue;
            var seconds = unit switch { 'd' => 86400, 'h' => 3600, 'm' => 60, 's' => 1, _ => 0 };
            if (seconds == 0) continue;
            total += value * seconds;
            any = true;
        }
        return any ? total : null;
    }
}
