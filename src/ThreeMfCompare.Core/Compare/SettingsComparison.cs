using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ThreeMfCompare.Core.Projects;

namespace ThreeMfCompare.Core.Compare;

/// <summary>One file's value of a setting.</summary>
/// <param name="Value">Null if the file does not have the setting at all.</param>
/// <param name="Differs">Not what the baseline file has.</param>
/// <param name="Customized">Set when the user changed the setting from the system preset in this file.</param>
public sealed record SettingCell(ResolvedSetting? Value, bool Differs, SettingOrigin? Customized);

/// <summary>A setting across all files, one cell per file in the order the files were given.</summary>
/// <param name="KeyRank">Position among <see cref="SettingCatalog.KeySettings"/>, or -1.</param>
/// <param name="IsLongText">A G-code template or other text of many lines: shown by its first line and compared line by line.</param>
public sealed record SettingRow(
    string Key,
    string Label,
    SettingCategory Category,
    IReadOnlyList<SettingCell> Cells,
    bool Differs,
    bool IsCustomized,
    int KeyRank,
    bool IsLongText);

/// <summary>Lines up the settings of several projects, each against the baseline's.</summary>
public static class SettingsComparison
{
    public static IReadOnlyList<SettingRow> Compare(IReadOnlyList<BambuProject> projects, int baseline)
    {
        if (projects.Count == 0) return [];
        baseline = Math.Clamp(baseline, 0, projects.Count - 1);
        var rows = new List<SettingRow>();

        var keys = new SortedSet<string>(projects.SelectMany(p => p.Settings.Values.Keys), StringComparer.Ordinal);
        foreach (var key in keys)
        {
            var values = projects.Select(p => p.Settings.Resolve(key)).ToList();
            rows.Add(Row(key, SettingCatalog.Label(key), SettingCatalog.Category(key), values, baseline,
                projects.Select(p => p.Settings.Customized.GetValueOrDefault(key)).ToList()));
        }

        // Objects, plates and the file itself, as settings of their own.
        // With one object in every file, its name says nothing and only makes the labels long.
        var single = projects.All(p => p.Objects.Count <= 1);
        var extras = projects.Select(p => Extras(p, projects[baseline], single)).ToList();
        var extraKeys = extras.SelectMany(e => e.Keys).Distinct().ToList();
        foreach (var key in extraKeys)
        {
            var first = extras.First(e => e.ContainsKey(key))[key];
            var values = extras.Select(e => e.TryGetValue(key, out var v) ? ResolvedSetting.One(v.Value) : null).ToList();
            rows.Add(Row(key, first.Label ?? key, first.Category, values, baseline, [.. projects.Select(_ => (SettingOrigin?)null)]));
        }
        return rows;
    }

    static SettingRow Row(string key, string label, SettingCategory category, IReadOnlyList<ResolvedSetting?> values, int baseline, IReadOnlyList<SettingOrigin?> customized)
    {
        var reference = values[baseline]?.Canonical;
        var cells = values.Select((v, i) => new SettingCell(v, v?.Canonical != reference, customized[i])).ToList();
        var longText = values.Any(v => v is not null && v.Values.Any(t => t.Contains('\n') || t.Length > 160));
        return new SettingRow(key, label, category, cells, cells.Any(c => c.Differs), customized.Any(c => c is not null), SettingCatalog.KeyRank(key), longText);
    }

    /// <summary>
    /// What the project says beyond its settings: its objects and their own settings, its plates, the
    /// app that saved it. An object goes by the name of the baseline's object it stands for, so that an
    /// object renamed in one file still meets itself.
    /// </summary>
    static Dictionary<string, (string? Label, SettingCategory Category, string Value)> Extras(BambuProject project, BambuProject baseline, bool singleObject)
    {
        var extras = new Dictionary<string, (string?, SettingCategory, string)>(StringComparer.Ordinal);
        void Add(string key, string label, string value, SettingCategory category = SettingCategory.Objects) => extras[key] = (label, category, value);

        Add("model/objects", "Objects", string.Join(", ", project.Objects.Select(o => o.Parts.Count > 1 ? $"{o.Name} ({o.Parts.Count} parts)" : o.Name)));
        Add("model/mesh_checksum", "Mesh files checksum", project.MeshChecksums.Count == 0 ? "" :
            string.Join(" ", project.MeshChecksums.OrderBy(m => m.Key, StringComparer.Ordinal).Select(m => m.Value.ToString("x8", CultureInfo.InvariantCulture))));

        var pairs = ModelComparison.Pair(project.Objects, baseline.Objects);
        var baseNames = new Dictionary<ProjectObject, string>(ReferenceEqualityComparer.Instance);
        foreach (var (baseName, item) in Named(baseline.Objects, o => o.Name)) baseNames[item] = baseName;
        var own = Named(project.Objects, o => o.Name).Select(n => n.Name).ToList();
        for (var i = 0; i < project.Objects.Count; i++)
        {
            var obj = project.Objects[i];
            var name = pairs[i] is { } paired ? baseNames[paired] : own[i];
            var prefix = $"object/{name}";
            var label = singleObject ? "" : $"{name}: ";
            var labelOf = (string part) => singleObject ? $"Part {part}: " : $"{name} › {part}: ";
            Add($"{prefix}/parts", $"{label}Parts", $"{obj.Parts.Count}: {string.Join(", ", obj.Parts.Select(p => p.Subtype == "normal_part" ? p.Name : $"{p.Name} ({Subtype(p.Subtype)})"))}");
            Add($"{prefix}/faces", $"{label}Faces", obj.FaceCount.ToString("N0", CultureInfo.InvariantCulture));
            Add($"{prefix}/variable_layer_height", $"{label}Variable layer height", obj.LayerHeightProfile is { } profile ? ProfileText(profile) : "none");
            foreach (var (key, value) in obj.Overrides)
                Add($"{prefix}/{key}", $"{label}{Capital(OverrideLabel(key))}", value);
            foreach (var (partName, part) in Named(obj.Parts, p => p.Name))
                foreach (var (key, value) in part.Overrides)
                    Add($"{prefix}/part/{partName}/{key}", $"{labelOf(partName)}{OverrideLabel(key)}", value);
            foreach (var range in obj.LayerRanges)
            {
                var span = $"{range.MinZ.ToString("0.##", CultureInfo.InvariantCulture)}–{range.MaxZ.ToString("0.##", CultureInfo.InvariantCulture)} mm";
                foreach (var (key, value) in range.Settings)
                    Add($"{prefix}/range/{span}/{key}", singleObject ? $"{span}: {OverrideLabel(key)}" : $"{name} › {span}: {OverrideLabel(key)}", value);
            }
        }

        foreach (var plate in project.Plates)
        {
            var prefix = $"plate/{plate.Index}";
            var title = plate.Name.Length > 0 ? $"Plate {plate.Index} ({plate.Name})" : $"Plate {plate.Index}";
            var names = plate.ObjectIds.Select(id => project.Objects.FirstOrDefault(o => o.Id == id)?.Name ?? $"#{id}");
            Add($"{prefix}/objects", $"{title}: objects", string.Join(", ", names));
            foreach (var (key, value) in plate.Settings)
                Add($"{prefix}/{key}", $"{title}: {OverrideLabel(key)}", value);
        }

        foreach (var key in new[] { "Title", "Application", "CreationDate", "ModificationDate", "Designer", "DesignerUserId" })
            if (project.Metadata.TryGetValue(key, out var value))
                Add($"3mf/{key}", $"3MF {key switch { "CreationDate" => "creation date", "ModificationDate" => "modification date", "DesignerUserId" => "designer ID", _ => key.ToLowerInvariant() }}", value, SettingCategory.Project);
        return extras;
    }

    static string OverrideLabel(string key) => key == "extruder" ? "filament" : SettingCatalog.Label(key);

    static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    static string Subtype(string subtype) => subtype switch
    {
        "negative_part" => "negative part",
        "modifier_part" => "modifier",
        "support_blocker" => "support blocker",
        "support_enforcer" => "support enforcer",
        _ => subtype.Replace('_', ' '),
    };

    /// <summary>A profile by its number of points and a short hash, so that two profiles can be told equal or not at a glance.</summary>
    static string ProfileText(string profile)
    {
        var points = profile.Split(';', StringSplitOptions.RemoveEmptyEntries).Length / 2;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile)))[..6].ToLowerInvariant();
        return $"custom: {points} points (#{hash})";
    }

    /// <summary>Items by name; a name that comes up again gets " #2", " #3"…, so that the n-th of a name meets the n-th in the other file.</summary>
    static IEnumerable<(string Name, T Item)> Named<T>(IEnumerable<T> items, Func<T, string> name)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var n = name(item);
            if (n.Length == 0) n = "(unnamed)";
            var count = seen[n] = seen.GetValueOrDefault(n) + 1;
            yield return (count == 1 ? n : $"{n} #{count}", item);
        }
    }
}
