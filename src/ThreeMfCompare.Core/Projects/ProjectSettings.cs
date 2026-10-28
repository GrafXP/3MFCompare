using System.Globalization;
using System.Text.Json;
using ThreeMfCompare.Core.Compare;

namespace ThreeMfCompare.Core.Projects;

/// <summary>A setting as Bambu Studio stores it in project_settings.config: one string, or a list of them.</summary>
public sealed record SettingValue(IReadOnlyList<string> Items, bool IsList)
{
    public string First => Items.Count > 0 ? Items[0] : "";

    public static SettingValue Scalar(string value) => new([value], false);
}

/// <summary>
/// How the entries of a list setting line up with the machine. Bambu Studio keeps one entry per
/// filament, per extruder, per nozzle variant of the printer ("Direct Drive Standard", "… High Flow")
/// or per filament and variant; only one variant is in use, so most of those entries are never printed with.
/// </summary>
public enum SettingShape
{
    /// <summary>One value, or a list whose entries all agree.</summary>
    Single,
    PerFilament,
    PerExtruder,
    /// <summary>Machine limits: the normal mode and the silent mode.</summary>
    Modes,
    /// <summary>A list that is none of the above (points of an area, a matrix, names).</summary>
    List,
}

/// <summary>
/// A setting as it takes effect: the entries of the variant in use, one per filament or extruder
/// where that is what the list holds. <see cref="Detail"/> keeps everything that was left out.
/// </summary>
/// <param name="Labels">Per value, what it belongs to: "1" for filament 1, "E1", "normal".</param>
public sealed record ResolvedSetting(SettingShape Shape, IReadOnlyList<string> Values, IReadOnlyList<string> Labels, string? Detail = null)
{
    /// <summary>Every value is the same: shown and compared as that one value.</summary>
    public bool IsUniform => Values.Count <= 1 || Values.All(v => v == Values[0]);

    public string Text => IsUniform ? Values.Count == 0 ? "" : Values[0] : string.Join(", ", Values);

    /// <summary>What two files are compared by: lists whose entries all agree count as their one value.</summary>
    public string Canonical => IsUniform ? Values.Count == 0 ? "" : Values[0] : $"{Shape}:{string.Join("\u001f", Values)}";

    public static ResolvedSetting One(string value, string? detail = null) => new(SettingShape.Single, [value], [""], detail);
}

/// <summary>Where a setting the user changed lives: the process, a filament or the printer preset.</summary>
/// <param name="Preset">The preset's name as saved in the project.</param>
/// <param name="SystemPreset">The system preset it was changed from.</param>
public sealed record SettingOrigin(string Preset, string SystemPreset);

/// <summary>
/// The flattened settings of a Bambu Studio project (process, filaments and printer in one
/// dictionary), and what is needed to read its lists: how many filaments and extruders there are,
/// which nozzle variant each extruder has, and which settings differ from the system presets.
/// </summary>
public sealed class ProjectSettings
{
    readonly Dictionary<string, SettingValue> values;
    readonly Dictionary<string, SettingOrigin> customized = new(StringComparer.Ordinal);

    public ProjectSettings(Dictionary<string, SettingValue> values)
    {
        this.values = values;
        FilamentCount = Math.Max(1, Items("filament_settings_id").Count);
        ExtruderCount = Math.Max(1, Items("nozzle_diameter").Count);
        var types = Items("extruder_type");
        var volumes = Items("nozzle_volume_type");
        ActiveVariants = [.. Enumerable.Range(0, ExtruderCount).Select(e => $"{At(types, e)} {At(volumes, e)}".Trim())];
        ReadCustomized();
        AddDerived();
    }

    public IReadOnlyDictionary<string, SettingValue> Values => values;

    public int FilamentCount { get; }
    public int ExtruderCount { get; }

    /// <summary>Per extruder, the variant its lists are read at, e.g. "Direct Drive Standard".</summary>
    public IReadOnlyList<string> ActiveVariants { get; }

    /// <summary>The settings that differ from the system presets they are based on.</summary>
    public IReadOnlyDictionary<string, SettingOrigin> Customized => customized;

    public static ProjectSettings Parse(Stream json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
        var values = new Dictionary<string, SettingValue>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
            values[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.Array => new SettingValue([.. property.Value.EnumerateArray().Select(Text)], true),
                _ => SettingValue.Scalar(Text(property.Value)),
            };
        return new ProjectSettings(values);

        static string Text(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Null => "",
            _ => element.GetRawText(),
        };
    }

    /// <summary>The setting's value, or the first entry of a list; null if the project does not have it.</summary>
    public string? Text(string key) => values.TryGetValue(key, out var value) ? value.First : null;

    public IReadOnlyList<string> Items(string key) => values.TryGetValue(key, out var value) ? value.Items : [];

    public double? Number(string key) =>
        Text(key) is { } text && double.TryParse(text.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;

    /// <summary>The filament's colour as "#RRGGBB", or null.</summary>
    public string? FilamentColour(int filament) => At(Items("filament_colour"), filament) is { Length: >= 7 } colour ? colour[..7] : null;

    /// <summary>The extruder a filament (0-based) prints from, 0-based.</summary>
    public int ExtruderOf(int filament) =>
        int.TryParse(At(Items("filament_map"), filament), out var extruder) ? Math.Clamp(extruder - 1, 0, ExtruderCount - 1) : 0;

    public ResolvedSetting? Resolve(string key)
    {
        if (!values.TryGetValue(key, out var value)) return null;
        if (!value.IsList) return ResolvedSetting.One(value.First);
        var items = value.Items;
        if (items.Count == 0) return ResolvedSetting.One("");

        return KindOf(key, items.Count) switch
        {
            ListKind.PrintVariant => ByVariant(items, "print_extruder_variant", "print_extruder_id"),
            ListKind.PrinterVariant => ByVariant(items, "printer_extruder_variant", "printer_extruder_id"),
            ListKind.FilamentVariant => ByFilamentVariant(items),
            ListKind.MachineLimit => ByMode(items),
            ListKind.Filament => new ResolvedSetting(SettingShape.PerFilament, items, [.. items.Select((_, i) => (i + 1).ToString(CultureInfo.InvariantCulture))]),
            ListKind.Extruder when ExtruderCount > 1 && items.Count == ExtruderCount =>
                new ResolvedSetting(SettingShape.PerExtruder, items, [.. items.Select((_, i) => $"E{i + 1}")]),
            _ => new ResolvedSetting(items.Count == 1 ? SettingShape.Single : SettingShape.List, items, [.. items.Select(_ => "")]),
        };
    }

    enum ListKind { Plain, Filament, Extruder, PrintVariant, PrinterVariant, FilamentVariant, MachineLimit }

    ListKind KindOf(string key, int count)
    {
        if (SettingCatalog.FilamentVariantKeys.Contains(key)) return ListKind.FilamentVariant;
        if (SettingCatalog.PrinterVariantKeys.Contains(key)) return ListKind.PrinterVariant;
        if (SettingCatalog.PrintVariantKeys.Contains(key)) return ListKind.PrintVariant;
        if (SettingCatalog.MachineLimitKeys.Contains(key)) return ListKind.MachineLimit;
        if (SettingCatalog.FilamentKeys.Contains(key)) return ListKind.Filament;
        if (SettingCatalog.ExtruderKeys.Contains(key)) return ListKind.Extruder;
        if (SettingCatalog.PlainListKeys.Contains(key)) return ListKind.Plain;

        // A setting newer than the catalogue: by its length, where that can only mean one thing.
        int filaments = FilamentCount, printVariants = Items("print_extruder_variant").Count, filamentVariants = Items("filament_extruder_variant").Count;
        if (count == filamentVariants && count != filaments && count != printVariants) return ListKind.FilamentVariant;
        if (count == printVariants && count != filaments) return ListKind.PrintVariant;
        if (count == filaments && filaments > 1) return ListKind.Filament;
        return ListKind.Plain;
    }

    /// <summary>Per extruder, the entry of its active variant; the others go to the detail.</summary>
    ResolvedSetting ByVariant(IReadOnlyList<string> items, string variantKey, string idKey)
    {
        var variants = Items(variantKey);
        var ids = Items(idKey);
        if (variants.Count != items.Count) return Plain(items);

        var picked = new string[ExtruderCount];
        for (var e = 0; e < ExtruderCount; e++)
        {
            var index = Find(variants, ids, ActiveVariants[e], e + 1);
            if (index < 0) return Plain(items);
            picked[e] = items[index];
        }
        var detail = string.Join(" · ", items.Select((v, i) => $"{(ExtruderCount > 1 ? $"E{At(ids, i)} " : "")}{variants[i]}: {v}"));
        return ExtruderCount > 1
            ? new ResolvedSetting(SettingShape.PerExtruder, picked, [.. picked.Select((_, e) => $"E{e + 1}")], detail)
            : new ResolvedSetting(SettingShape.Single, picked, [""], detail);
    }

    /// <summary>Per filament, the entry of the variant of the extruder it prints from.</summary>
    ResolvedSetting ByFilamentVariant(IReadOnlyList<string> items)
    {
        var variants = Items("filament_extruder_variant");
        var owners = Items("filament_self_index");
        if (variants.Count != items.Count || owners.Count != items.Count) return Plain(items);

        var picked = new string[FilamentCount];
        for (var f = 0; f < FilamentCount; f++)
        {
            var index = Find(variants, owners, ActiveVariants[ExtruderOf(f)], f + 1);
            if (index < 0) return Plain(items);
            picked[f] = items[index];
        }
        var detail = string.Join(" · ", items.Select((v, i) => $"{(FilamentCount > 1 ? $"F{owners[i]} " : "")}{variants[i]}: {v}"));
        return new ResolvedSetting(SettingShape.PerFilament, picked, [.. picked.Select((_, f) => (f + 1).ToString(CultureInfo.InvariantCulture))], detail);
    }

    /// <summary>Machine limits come in pairs per printer variant: normal mode, then silent mode.</summary>
    ResolvedSetting ByMode(IReadOnlyList<string> items)
    {
        var variants = Items("printer_extruder_variant");
        var at = 0;
        if (variants.Count > 0 && items.Count == 2 * variants.Count)
        {
            var index = Find(variants, Items("printer_extruder_id"), ActiveVariants[0], 1);
            if (index >= 0) at = 2 * index;
        }
        if (at + 1 >= items.Count) return Plain(items);
        var detail = variants.Count > 1 ? string.Join(" · ", variants.Select((v, i) => $"{v}: {At(items, 2 * i)} / {At(items, 2 * i + 1)}")) : null;
        return new ResolvedSetting(SettingShape.Modes, [items[at], items[at + 1]], ["normal", "silent"], detail);
    }

    static ResolvedSetting Plain(IReadOnlyList<string> items) =>
        new(items.Count == 1 ? SettingShape.Single : SettingShape.List, items, [.. items.Select(_ => "")]);

    /// <summary>The entry for <paramref name="variant"/> of the owner (extruder or filament, 1-based); else the owner's first.</summary>
    static int Find(IReadOnlyList<string> variants, IReadOnlyList<string> owners, string variant, int owner)
    {
        var id = owner.ToString(CultureInfo.InvariantCulture);
        bool Owns(int i) => owners.Count != variants.Count || owners[i] == id;
        for (var i = 0; i < variants.Count; i++)
            if (Owns(i) && variants[i] == variant) return i;
        for (var i = 0; i < variants.Count; i++)
            if (Owns(i)) return i;
        return -1;
    }

    /// <summary>
    /// different_settings_to_system holds, separated by ';', the keys changed in the process preset,
    /// then in each filament preset, then in the printer preset; inherits_group names the system
    /// preset a user preset is based on, in the same order (empty for a system preset).
    /// </summary>
    void ReadCustomized()
    {
        var changed = Items("different_settings_to_system");
        if (changed.Count == 0) return;
        var parents = Items("inherits_group");
        var filaments = Items("filament_settings_id");

        for (var group = 0; group < changed.Count; group++)
        {
            var preset = group == 0 ? Text("print_settings_id") ?? ""
                : group == changed.Count - 1 ? Text("printer_settings_id") ?? ""
                : At(filaments, group - 1);
            var parent = At(parents, group) is { Length: > 0 } p ? p : preset;
            foreach (var key in changed[group].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                customized.TryAdd(key, new SettingOrigin(preset, parent));
        }
    }

    /// <summary>The bed temperature of the plate type in use, which Bambu keeps under one key per plate type.</summary>
    void AddDerived()
    {
        if (Text("curr_bed_type") is not { } plate || BedTemperatureKey(plate) is not { } key) return;
        if (values.TryGetValue(key, out var temperature)) values[SettingCatalog.BedTemperature] = temperature;
        if (values.TryGetValue(key + "_initial_layer", out var initial)) values[SettingCatalog.BedTemperatureInitialLayer] = initial;
    }

    public static string? BedTemperatureKey(string plate) => plate.ToLowerInvariant() switch
    {
        var p when p.Contains("supertack") => "supertack_plate_temp",
        var p when p.Contains("textured cool") => "textured_cool_plate_temp",
        var p when p.Contains("textured") => "textured_plate_temp",
        var p when p.Contains("engineering") => "eng_plate_temp",
        var p when p.Contains("high temp") || p.Contains("smooth") => "hot_plate_temp",
        var p when p.Contains("cool") => "cool_plate_temp",
        _ => null,
    };

    static string At(IReadOnlyList<string> list, int index) => index >= 0 && index < list.Count ? list[index] : "";
}
