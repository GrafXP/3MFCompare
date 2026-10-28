using System.Numerics;
using ThreeMfCompare.Core.Gcode;

namespace ThreeMfCompare.Core.Visual;

public enum ToolpathColouring { Feature, Filament, Speed, Flow, Width, LayerHeight, LayerTime, Fan, Temperature }

/// <summary>A colour scale's range and unit, shared by every pane so that one colour means one value everywhere.</summary>
public sealed record ScaleRange(string Title, string Unit, float Min, float Max);

/// <summary>What colour each line gets.</summary>
public static class ToolpathColours
{
    public static string Title(ToolpathColouring by) => by switch
    {
        ToolpathColouring.Feature => "Line type",
        ToolpathColouring.Filament => "Filament",
        ToolpathColouring.Speed => "Speed",
        ToolpathColouring.Flow => "Volumetric flow",
        ToolpathColouring.Width => "Line width",
        ToolpathColouring.LayerHeight => "Layer height",
        ToolpathColouring.LayerTime => "Layer time",
        ToolpathColouring.Fan => "Part-cooling fan",
        ToolpathColouring.Temperature => "Nozzle temperature",
        _ => by.ToString(),
    };

    public static string Unit(ToolpathColouring by) => by switch
    {
        ToolpathColouring.Speed => "mm/s",
        ToolpathColouring.Flow => "mm³/s",
        ToolpathColouring.Width or ToolpathColouring.LayerHeight => "mm",
        ToolpathColouring.LayerTime => "s",
        ToolpathColouring.Fan => "%",
        ToolpathColouring.Temperature => "°C",
        _ => "",
    };

    public static bool IsScale(ToolpathColouring by) => by is not (ToolpathColouring.Feature or ToolpathColouring.Filament);

    /// <summary>The quantity a scale colours by, per segment; <paramref name="layerSeconds"/> is needed for the layer time.</summary>
    public static float Value(in ExtrusionSegment s, ToolpathColouring by, double[]? layerSeconds) => by switch
    {
        ToolpathColouring.Speed => s.Speed,
        ToolpathColouring.Flow => s.Flow,
        ToolpathColouring.Width => s.Width,
        ToolpathColouring.LayerHeight => s.Height,
        ToolpathColouring.LayerTime => layerSeconds is not null && (uint)s.Layer < (uint)layerSeconds.Length ? (float)layerSeconds[s.Layer] : 0,
        ToolpathColouring.Fan => s.Fan / 2.55f,
        ToolpathColouring.Temperature => s.NozzleTemperature,
        _ => 0,
    };

    /// <summary>
    /// The range of a quantity over the part's lines of all toolpaths together. Speeds, flows and layer
    /// times run from the 1st to the 99th percentile: a few very short pieces, or one slow first layer,
    /// would otherwise stretch the scale until everything else is one colour.
    /// </summary>
    public static ScaleRange Range(IReadOnlyList<(Toolpath Toolpath, double[] LayerSeconds)> toolpaths, ToolpathColouring by)
    {
        var robust = by is ToolpathColouring.Speed or ToolpathColouring.Flow or ToolpathColouring.LayerTime;
        var samples = new List<float>();
        float min = float.MaxValue, max = float.MinValue;
        foreach (var (toolpath, layerSeconds) in toolpaths)
        {
            var segments = toolpath.Segments;
            var step = robust ? Math.Max(1, segments.Length / 200_000) : 1;
            for (var i = 0; i < segments.Length; i += step)
            {
                if (!segments[i].Feature.IsPartMaterial()) continue;
                var value = Value(segments[i], by, layerSeconds);
                if (robust) samples.Add(value);
                min = MathF.Min(min, value);
                max = MathF.Max(max, value);
            }
        }
        if (robust && samples.Count > 0)
        {
            samples.Sort();
            min = samples[(int)(0.01 * (samples.Count - 1))];
            max = samples[(int)(0.99 * (samples.Count - 1))];
        }
        if (min > max) (min, max) = (0, 0);
        return new ScaleRange(Title(by), Unit(by), min, max);
    }

    /// <summary>One colour per segment.</summary>
    /// <param name="filamentColours">Per filament of the project, for <see cref="ToolpathColouring.Filament"/>.</param>
    public static Vector4[] Colour(Toolpath toolpath, ToolpathColouring by, ScaleRange? range, IReadOnlyList<Vector4> filamentColours, double[]? layerSeconds)
    {
        var segments = toolpath.Segments;
        var colours = new Vector4[segments.Length];
        var scale = range is null ? null : new ColourScale(range.Min, range.Max);
        Parallel.For(0, segments.Length, s =>
        {
            ref readonly var segment = ref segments[s];
            colours[s] = by switch
            {
                ToolpathColouring.Feature => Palette.Feature(segment.Feature),
                ToolpathColouring.Filament => segment.Filament < filamentColours.Count ? filamentColours[segment.Filament] : Palette.Neutral,
                _ => !segment.Feature.IsPartMaterial() ? Palette.Neutral : scale?.Colour(Value(segment, by, layerSeconds)) ?? Palette.Neutral,
            };
        });
        return colours;
    }
}

/// <summary>
/// The toolpath's lines as plain lines (two points per segment), what is visible of them, and their
/// bounds; beads are <see cref="BeadMesh"/>. Adapted from GcodeFEM's toolpath geometry.
/// </summary>
public static class ToolpathGeometry
{
    /// <summary>The segments of layers <paramref name="layerLow"/> to <paramref name="layerHigh"/> (both included) whose line type is shown, ascending.</summary>
    public static int[] Visible(Toolpath toolpath, int layerLow, int layerHigh, Func<FeatureType, bool> shows)
    {
        var layers = toolpath.Layers;
        if (layers.Length == 0 || layerHigh < layerLow) return [];
        layerLow = Math.Clamp(layerLow, 0, layers.Length - 1);
        layerHigh = Math.Clamp(layerHigh, 0, layers.Length - 1);
        var first = layers[layerLow].FirstSegment;
        var end = layers[layerHigh].FirstSegment + layers[layerHigh].SegmentCount;

        var shown = new bool[FeatureTypes.Count];
        for (var f = 0; f < shown.Length; f++) shown[f] = shows((FeatureType)f);
        var visible = new List<int>(end - first);
        var segments = toolpath.Segments;
        for (var s = first; s < end; s++)
            if (shown[(int)segments[s].Feature]) visible.Add(s);
        return [.. visible];
    }

    /// <summary>Two points per segment, for every segment of the toolpath.</summary>
    public static Vector3[] LinePositions(Toolpath toolpath)
    {
        var segments = toolpath.Segments;
        var positions = new Vector3[2 * segments.Length];
        Parallel.For(0, segments.Length, s => (positions[2 * s], positions[2 * s + 1]) = (segments[s].Start, segments[s].End));
        return positions;
    }

    public static Vector4[] LineColours(Vector4[] segmentColours)
    {
        var colours = new Vector4[2 * segmentColours.Length];
        Parallel.For(0, segmentColours.Length, s => colours[2 * s] = colours[2 * s + 1] = segmentColours[s]);
        return colours;
    }

    public static int[] LineIndices(int[] visible)
    {
        var pairs = new int[2 * visible.Length];
        for (var n = 0; n < visible.Length; n++) (pairs[2 * n], pairs[2 * n + 1]) = (2 * visible[n], 2 * visible[n] + 1);
        return pairs;
    }

    /// <summary>Bounds of the part's lines (skirt, support, prime tower excluded); of all lines if there are no part lines.</summary>
    public static (Vector3 Min, Vector3 Max)? Bounds(Toolpath toolpath)
    {
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        var any = false;
        foreach (var partOnly in new[] { true, false })
        {
            foreach (var s in toolpath.Segments)
            {
                if (partOnly && !s.Feature.IsPartMaterial()) continue;
                min = Vector3.Min(min, Vector3.Min(s.Start, s.End));
                max = Vector3.Max(max, Vector3.Max(s.Start, s.End));
                any = true;
            }
            if (any) return (min, max);
        }
        return null;
    }
}
