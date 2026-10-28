using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace ThreeMfCompare.Core.Projects;

/// <param name="Overrides">Settings changed for this part alone (right click › add settings in Bambu Studio), and its filament.</param>
public sealed record ProjectPart(int Id, string Name, string Subtype, int FaceCount, IReadOnlyDictionary<string, string> Overrides);

/// <param name="Overrides">Settings changed for the whole object, and its filament.</param>
/// <param name="LayerRanges">Settings changed for height ranges of the object.</param>
/// <param name="LayerHeightProfile">The variable layer height profile, or null if the object has none.</param>
public sealed record ProjectObject(
    int Id,
    string Name,
    int FaceCount,
    IReadOnlyList<ProjectPart> Parts,
    IReadOnlyDictionary<string, string> Overrides,
    IReadOnlyList<LayerRange> LayerRanges,
    string? LayerHeightProfile);

/// <summary>A height range of an object with settings of its own.</summary>
public sealed record LayerRange(double MinZ, double MaxZ, IReadOnlyDictionary<string, string> Settings);

/// <param name="Settings">The plate's own settings: plate type, print sequence and the like, where set.</param>
public sealed record ProjectPlate(int Index, string Name, IReadOnlyDictionary<string, string> Settings, IReadOnlyList<int> ObjectIds);

/// <summary>What a sliced project (.gcode.3mf) says about a plate.</summary>
public sealed record SlicedPlateInfo(int Index, double PredictionSeconds, double WeightGrams, IReadOnlyList<SlicedFilamentInfo> Filaments, bool SupportUsed);

public sealed record SlicedFilamentInfo(int Id, string Type, string Colour, double UsedMetres, double UsedGrams);

/// <summary>
/// A Bambu Studio (or Orca Slicer) project file: a 3MF with the slicer's settings in
/// Metadata/project_settings.config, the objects and plates in Metadata/model_settings.config and
/// one thumbnail per plate. The meshes themselves are not read, only checked by their CRC.
/// </summary>
public sealed class BambuProject
{
    public required string Path { get; init; }
    public string FileName => System.IO.Path.GetFileName(Path);
    public required long FileSize { get; init; }
    public required DateTime LastWriteTime { get; init; }

    /// <summary>SHA-256 of the file: same hash, same project.</summary>
    public required string Hash { get; init; }

    public required ProjectSettings Settings { get; init; }

    /// <summary>The 3MF's own metadata: Title, Application, CreationDate, ModificationDate, Designer…</summary>
    public required IReadOnlyDictionary<string, string> Metadata { get; init; }

    public required IReadOnlyList<ProjectObject> Objects { get; init; }
    public required IReadOnlyList<ProjectPlate> Plates { get; init; }

    /// <summary>PNG per plate number, the one Bambu Studio shows for the plate.</summary>
    public required IReadOnlyDictionary<int, byte[]> Thumbnails { get; init; }

    /// <summary>The CRC of each mesh file (3D/Objects/*.model): equal CRCs, equal meshes.</summary>
    public required IReadOnlyDictionary<string, uint> MeshChecksums { get; init; }

    /// <summary>Plates with G-code inside: the file is a sliced project.</summary>
    public required IReadOnlyList<SlicedPlateInfo> SlicedPlates { get; init; }

    public string Title => Metadata.GetValueOrDefault("Title") is { Length: > 0 } title ? title : System.IO.Path.GetFileNameWithoutExtension(Path);

    public static BambuProject Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var settingsEntry = archive.GetEntry("Metadata/project_settings.config")
            ?? throw new InvalidDataException($"'{System.IO.Path.GetFileName(path)}' has no Metadata/project_settings.config: it is not a Bambu Studio or Orca Slicer project.");

        ProjectSettings settings;
        using (var stream = settingsEntry.Open()) settings = ProjectSettings.Parse(stream);

        var metadata = ReadModelMetadata(archive);
        var profiles = ReadLayerHeightProfiles(archive);
        var ranges = ReadLayerRanges(archive);
        var (objects, plates) = ReadModelSettings(archive, profiles, ranges);

        var thumbnails = new Dictionary<int, byte[]>();
        foreach (var entry in archive.Entries)
            if (entry.FullName.StartsWith("Metadata/plate_", StringComparison.Ordinal) && entry.FullName.EndsWith(".png", StringComparison.Ordinal)
                && int.TryParse(entry.FullName["Metadata/plate_".Length..^".png".Length], NumberStyles.None, CultureInfo.InvariantCulture, out var plate))
                thumbnails[plate] = ReadBytes(entry);

        var meshes = archive.Entries
            .Where(e => e.FullName.StartsWith("3D/Objects/", StringComparison.Ordinal) && e.FullName.EndsWith(".model", StringComparison.Ordinal))
            .ToDictionary(e => e.FullName, e => e.Crc32, StringComparer.Ordinal);

        var info = new FileInfo(path);
        return new BambuProject
        {
            Path = System.IO.Path.GetFullPath(path),
            FileSize = info.Length,
            LastWriteTime = info.LastWriteTime,
            Hash = Convert.ToHexString(SHA256.HashData(bytes)),
            Settings = settings,
            Metadata = metadata,
            Objects = objects,
            Plates = plates,
            Thumbnails = thumbnails,
            MeshChecksums = meshes,
            SlicedPlates = ReadSliceInfo(archive),
        };
    }

    static byte[] ReadBytes(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    static XDocument? Xml(ZipArchive archive, string name)
    {
        if (archive.GetEntry(name) is not { } entry) return null;
        using var stream = entry.Open();
        try
        {
            return XDocument.Load(stream);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>The &lt;metadata name="…"&gt; of 3D/3dmodel.model: read up to the resources, which hold no metadata.</summary>
    static Dictionary<string, string> ReadModelMetadata(ZipArchive archive)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        if (archive.GetEntry("3D/3dmodel.model") is not { } entry) return metadata;
        using var reader = System.Xml.XmlReader.Create(entry.Open(), new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Ignore });
        while (reader.Read())
        {
            if (reader.NodeType != System.Xml.XmlNodeType.Element) continue;
            if (reader.LocalName is "resources" or "build") break;
            if (reader.LocalName == "metadata" && reader.GetAttribute("name") is { } name)
            {
                var key = name.Contains(':') ? name[(name.IndexOf(':') + 1)..] : name;
                metadata[key] = reader.ReadElementContentAsString();
            }
        }
        return metadata;
    }

    /// <summary>Metadata/layer_heights_profile.txt: "object_id=N|z;height;z;height…", one line per object (N counts objects from 1).</summary>
    static Dictionary<int, string> ReadLayerHeightProfiles(ZipArchive archive)
    {
        var profiles = new Dictionary<int, string>();
        if (archive.GetEntry("Metadata/layer_heights_profile.txt") is not { } entry) return profiles;
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            var bar = line.IndexOf('|');
            if (bar < 0 || !line.StartsWith("object_id=", StringComparison.Ordinal)) continue;
            if (int.TryParse(line["object_id=".Length..bar], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                profiles[id] = line[(bar + 1)..].Trim();
        }
        return profiles;
    }

    /// <summary>Metadata/layer_config_ranges.xml: per object, height ranges with settings of their own.</summary>
    static Dictionary<int, List<LayerRange>> ReadLayerRanges(ZipArchive archive)
    {
        var ranges = new Dictionary<int, List<LayerRange>>();
        if (Xml(archive, "Metadata/layer_config_ranges.xml")?.Root is not { } root) return ranges;
        foreach (var obj in root.Elements("object"))
        {
            if (!int.TryParse((string?)obj.Attribute("id"), out var id)) continue;
            var list = ranges[id] = [];
            foreach (var range in obj.Elements("range"))
            {
                var options = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var option in range.Elements("option"))
                    if ((string?)option.Attribute("opt_key") is { } key) options[key] = option.Value;
                list.Add(new LayerRange(Number((string?)range.Attribute("min_z")), Number((string?)range.Attribute("max_z")), options));
            }
        }
        return ranges;
    }

    /// <summary>Metadata of a part that places or traces it, rather than changing how it prints.</summary>
    static readonly HashSet<string> PartBookkeeping =
    [
        "name", "matrix", "source_file", "source_object_id", "source_volume_id", "source_offset_x", "source_offset_y", "source_offset_z", "mesh_stat",
    ];

    /// <summary>Metadata of a plate that is not a setting: its name, files and lock.</summary>
    static readonly HashSet<string> PlateBookkeeping =
    [
        "plater_id", "plater_name", "locked", "thumbnail_file", "thumbnail_no_light_file", "top_file", "pick_file", "gcode_file", "pattern_bbox_file", "pattern_file",
    ];

    static (List<ProjectObject>, List<ProjectPlate>) ReadModelSettings(ZipArchive archive, Dictionary<int, string> profiles, Dictionary<int, List<LayerRange>> ranges)
    {
        var objects = new List<ProjectObject>();
        var plates = new List<ProjectPlate>();
        if (Xml(archive, "Metadata/model_settings.config")?.Root is not { } root) return (objects, plates);

        var order = 0;
        foreach (var obj in root.Elements("object"))
        {
            order++;
            var id = (int?)obj.Attribute("id") ?? order;
            var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
            string name = "";
            var faces = 0;
            foreach (var meta in obj.Elements("metadata"))
            {
                if ((string?)meta.Attribute("face_count") is { } count) faces = (int)Number(count);
                if ((string?)meta.Attribute("key") is not { } key) continue;
                var value = (string?)meta.Attribute("value") ?? "";
                if (key == "name") name = value;
                else overrides[key] = value;
            }

            var parts = new List<ProjectPart>();
            foreach (var part in obj.Elements("part"))
            {
                var partOverrides = new Dictionary<string, string>(StringComparer.Ordinal);
                string partName = "";
                foreach (var meta in part.Elements("metadata"))
                {
                    if ((string?)meta.Attribute("key") is not { } key) continue;
                    var value = (string?)meta.Attribute("value") ?? "";
                    if (key == "name") partName = value;
                    else if (!PartBookkeeping.Contains(key)) partOverrides[key] = value;
                }
                var partFaces = (int?)part.Element("mesh_stat")?.Attribute("face_count") ?? 0;
                parts.Add(new ProjectPart((int?)part.Attribute("id") ?? parts.Count + 1, partName, (string?)part.Attribute("subtype") ?? "normal_part", partFaces, partOverrides));
            }
            if (faces == 0) faces = parts.Sum(p => p.FaceCount);

            // The profile and the ranges count objects in the order they are listed, from 1.
            objects.Add(new ProjectObject(id, name, faces, parts, overrides,
                ranges.GetValueOrDefault(order) ?? ranges.GetValueOrDefault(id) ?? [],
                profiles.GetValueOrDefault(order)));
        }

        foreach (var plate in root.Elements("plate"))
        {
            var settings = new Dictionary<string, string>(StringComparer.Ordinal);
            var (index, name) = (plates.Count + 1, "");
            foreach (var meta in plate.Elements("metadata"))
            {
                if ((string?)meta.Attribute("key") is not { } key) continue;
                var value = (string?)meta.Attribute("value") ?? "";
                if (key == "plater_id") index = (int)Number(value);
                else if (key == "plater_name") name = value;
                else if (!PlateBookkeeping.Contains(key)) settings[key] = value;
            }
            var instances = plate.Elements("model_instance")
                .Select(i => i.Elements("metadata").FirstOrDefault(m => (string?)m.Attribute("key") == "object_id"))
                .Where(m => m is not null)
                .Select(m => (int)Number((string?)m!.Attribute("value")))
                .ToList();
            plates.Add(new ProjectPlate(index, name, settings, instances));
        }
        return (objects, plates);
    }

    static List<SlicedPlateInfo> ReadSliceInfo(ZipArchive archive)
    {
        var plates = new List<SlicedPlateInfo>();
        if (Xml(archive, "Metadata/slice_info.config")?.Root is not { } root) return plates;
        foreach (var plate in root.Elements("plate"))
        {
            var meta = plate.Elements("metadata")
                .Where(m => m.Attribute("key") is not null)
                .GroupBy(m => (string)m.Attribute("key")!)
                .ToDictionary(g => g.Key, g => (string?)g.First().Attribute("value") ?? "");
            var filaments = plate.Elements("filament").Select(f => new SlicedFilamentInfo(
                (int?)f.Attribute("id") ?? 0,
                (string?)f.Attribute("type") ?? "",
                (string?)f.Attribute("color") ?? "",
                Number((string?)f.Attribute("used_m")),
                Number((string?)f.Attribute("used_g")))).ToList();
            plates.Add(new SlicedPlateInfo(
                (int)Number(meta.GetValueOrDefault("index")),
                Number(meta.GetValueOrDefault("prediction")),
                Number(meta.GetValueOrDefault("weight")),
                filaments,
                meta.GetValueOrDefault("support_used") == "true"));
        }
        return plates;
    }

    static double Number(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
}
