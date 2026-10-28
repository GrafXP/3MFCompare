namespace ThreeMfCompare.Core.Gcode;

/// <summary>Line types from Bambu's "; FEATURE:" comments.</summary>
public enum FeatureType : byte
{
    Unknown,
    OuterWall,
    InnerWall,
    OverhangWall,
    SparseInfill,
    InternalSolidInfill,
    TopSurface,
    BottomSurface,
    Bridge,
    InternalBridge,
    GapInfill,
    FloatingVerticalShell,
    Ironing,
    Skirt,
    Brim,
    Support,
    SupportInterface,
    SupportTransition,
    PrimeTower,
    Custom,
}

public static class FeatureTypes
{
    public const int Count = (int)FeatureType.Custom + 1;

    static readonly Dictionary<string, FeatureType> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Outer wall"] = FeatureType.OuterWall,
        ["Inner wall"] = FeatureType.InnerWall,
        ["Overhang wall"] = FeatureType.OverhangWall,
        ["Sparse infill"] = FeatureType.SparseInfill,
        ["Internal solid infill"] = FeatureType.InternalSolidInfill,
        ["Top surface"] = FeatureType.TopSurface,
        ["Bottom surface"] = FeatureType.BottomSurface,
        ["Bridge"] = FeatureType.Bridge,
        ["Internal bridge"] = FeatureType.InternalBridge,
        ["Gap infill"] = FeatureType.GapInfill,
        ["Floating vertical shell"] = FeatureType.FloatingVerticalShell,
        ["Ironing"] = FeatureType.Ironing,
        ["Skirt"] = FeatureType.Skirt,
        ["Brim"] = FeatureType.Brim,
        ["Support"] = FeatureType.Support,
        ["Support interface"] = FeatureType.SupportInterface,
        ["Support transition"] = FeatureType.SupportTransition,
        ["Prime tower"] = FeatureType.PrimeTower,
        ["Custom"] = FeatureType.Custom,
    };

    static readonly Dictionary<FeatureType, string> NameOf = ByName.ToDictionary(p => p.Value, p => p.Key);

    public static FeatureType Parse(ReadOnlySpan<char> name) =>
        ByName.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(name.Trim(), out var feature) ? feature : FeatureType.Unknown;

    public static string DisplayName(this FeatureType feature) => NameOf.GetValueOrDefault(feature, "Other");

    /// <summary>Lines that end up as the part itself (not skirt, brim, support, prime tower or start/end G-code).</summary>
    public static bool IsPartMaterial(this FeatureType feature) =>
        feature is >= FeatureType.OuterWall and <= FeatureType.Ironing;
}
