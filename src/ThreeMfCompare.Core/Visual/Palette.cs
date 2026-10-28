using System.Globalization;
using System.Numerics;
using ThreeMfCompare.Core.Gcode;

namespace ThreeMfCompare.Core.Visual;

/// <summary>
/// Colours as sRGB red, green, blue, alpha in 0..1 (the layout of a vertex colour). Line types keep
/// the colours of Bambu Studio's preview, so a print looks as it does there; one colour per file
/// tells the files apart wherever they meet (headers, chart lines); magnitudes go along one
/// perceptual ramp, dark to light, so a scale reads the same in every pane.
/// </summary>
public static class Palette
{
    /// <summary>One per file, in the order the files are listed; picked for the dark background and told apart with any colour vision when neighbours.</summary>
    public static readonly Vector4[] Files =
    [
        Hex("#3987e5"), Hex("#e8803a"), Hex("#2fb383"), Hex("#d55181"),
        Hex("#c9a400"), Hex("#9085e9"), Hex("#4cc0d9"), Hex("#e66767"),
    ];

    public static Vector4 File(int index) => Files[index % Files.Length];

    public static readonly Vector4 Neutral = Hex("#898781");

    /// <summary>Bambu Studio's preview colours per line type.</summary>
    public static Vector4 Feature(FeatureType feature) => feature switch
    {
        FeatureType.OuterWall => Hex("#ff7d38"),
        FeatureType.InnerWall => Hex("#ffe64d"),
        FeatureType.OverhangWall => Hex("#1f1fff"),
        FeatureType.SparseInfill => Hex("#b03029"),
        FeatureType.InternalSolidInfill => Hex("#9654cc"),
        FeatureType.TopSurface => Hex("#f04040"),
        FeatureType.BottomSurface => Hex("#665cc7"),
        FeatureType.Bridge => Hex("#4d66a1"),
        FeatureType.InternalBridge => Hex("#4d80ba"),
        FeatureType.GapInfill => Hex("#ffffff"),
        FeatureType.FloatingVerticalShell => Hex("#b48cd9"),
        FeatureType.Ironing => Hex("#ff8c69"),
        FeatureType.Skirt => Hex("#00876e"),
        FeatureType.Brim => Hex("#003b6e"),
        FeatureType.Support => Hex("#00ff00"),
        FeatureType.SupportInterface => Hex("#008000"),
        FeatureType.SupportTransition => Hex("#004000"),
        FeatureType.PrimeTower => Hex("#b3e3ab"),
        FeatureType.Custom => Hex("#5ed194"),
        _ => Neutral,
    };

    /// <summary>Dark violet through teal to yellow (viridis), interpolated in OKLab so equal steps look equal.</summary>
    public static readonly Vector4[] Sequential = Ramp([Hex("#440154"), Hex("#3b528b"), Hex("#21918c"), Hex("#5ec962"), Hex("#fde725")]);

    public static Vector4 Hex(string hex)
    {
        var value = uint.Parse(hex.AsSpan(1, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new Vector4(((value >> 16) & 255) / 255f, ((value >> 8) & 255) / 255f, (value & 255) / 255f, 1);
    }

    /// <summary>A filament colour as Bambu stores it ("#RRGGBB" or "#RRGGBBAA"); grey if it cannot be read.</summary>
    public static Vector4 Parse(string? hex) =>
        hex is { Length: >= 7 } && hex[0] == '#' && uint.TryParse(hex.AsSpan(1, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _) ? Hex(hex) : Neutral;

    /// <summary>256 colours through evenly spaced stops, interpolated in OKLab.</summary>
    static Vector4[] Ramp(Vector4[] stops)
    {
        var lab = stops.Select(ToOklab).ToArray();
        var ramp = new Vector4[256];
        for (var n = 0; n < ramp.Length; n++)
        {
            var at = n / (ramp.Length - 1f) * (stops.Length - 1);
            var stop = Math.Min((int)at, stops.Length - 2);
            ramp[n] = FromOklab(Vector3.Lerp(lab[stop], lab[stop + 1], at - stop));
        }
        return ramp;
    }

    static Vector3 ToOklab(Vector4 c)
    {
        float r = ToLinear(c.X), g = ToLinear(c.Y), b = ToLinear(c.Z);
        var l = MathF.Cbrt(0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b);
        var m = MathF.Cbrt(0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b);
        var s = MathF.Cbrt(0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b);
        return new Vector3(
            0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s,
            1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s,
            0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s);
    }

    static Vector4 FromOklab(Vector3 lab)
    {
        var l = lab.X + 0.3963377774f * lab.Y + 0.2158037573f * lab.Z;
        var m = lab.X - 0.1055613458f * lab.Y - 0.0638541728f * lab.Z;
        var s = lab.X - 0.0894841775f * lab.Y - 1.2914855480f * lab.Z;
        (l, m, s) = (l * l * l, m * m * m, s * s * s);
        return new Vector4(
            FromLinear(4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s),
            FromLinear(-1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s),
            FromLinear(-0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s),
            1);
    }

    static float ToLinear(float v) => v <= 0.04045f ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f);

    static float FromLinear(float v) =>
        Math.Clamp(v <= 0.0031308f ? 12.92f * v : 1.055f * MathF.Pow(v, 1 / 2.4f) - 0.055f, 0, 1);
}

/// <summary>Maps a range of values onto the sequential ramp; values outside the range take the end colours.</summary>
public sealed class ColourScale(float min, float max)
{
    public float Min { get; } = min;
    public float Max { get; } = max;

    public Vector4 Colour(float value)
    {
        var t = Max > Min ? (value - Min) / (Max - Min) : 0.5f;
        if (!(t > 0)) t = 0; // also catches NaN
        return Palette.Sequential[(int)(MathF.Min(t, 1) * (Palette.Sequential.Length - 1) + 0.5f)];
    }
}
