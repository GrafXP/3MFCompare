using System.Globalization;
using System.IO;
using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using ThreeMfCompare.Core.Compare;
using ThreeMfCompare.Core.Gcode;
using ThreeMfCompare.Core.Projects;
using ThreeMfCompare.Core.Slicing;
using ThreeMfCompare.Core.Visual;

namespace ThreeMfCompare.App;

public enum SliceState { NotSliced, Queued, Slicing, Sliced, Failed }

/// <summary>A filament of a project, as its card lists it.</summary>
public sealed record FilamentItem(int Number, Brush Swatch, string Preset, string Type);

/// <summary>A label and a value, as a card lists them.</summary>
public sealed record Fact(string Label, string Value, string Tip = "");

static class Swatches
{
    public static Color Colour(Vector4 c) => Color.FromRgb((byte)(c.X * 255 + 0.5f), (byte)(c.Y * 255 + 0.5f), (byte)(c.Z * 255 + 0.5f));

    public static Brush Frozen(Vector4 colour) => Frozen(Colour(colour));

    public static Brush Frozen(Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }
}

/// <summary>A G-code file read, with what the views derive from it once.</summary>
public sealed class LoadedToolpath(Toolpath toolpath)
{
    public Toolpath Toolpath { get; } = toolpath;
    public double[] LayerSeconds { get; } = toolpath.LayerSeconds();
    public (Vector3 Min, Vector3 Max)? Bounds { get; } = ToolpathGeometry.Bounds(toolpath);

    Vector3[]? linePositions;

    /// <summary>Two points per segment; built on first use by the scene builder, which runs one build at a time.</summary>
    public Vector3[] LinePositions => linePositions ??= ToolpathGeometry.LinePositions(Toolpath);

    /// <summary>The last colouring asked for, per segment, and what it was made for.</summary>
    public (object Key, Vector4[] Colours)? Colours;

    BeadMesh? beadMesh;

    /// <summary>Every line as a bead; built on first use by the scene builder.</summary>
    public BeadMesh BeadMesh => beadMesh ??= BeadMesh.Build(Toolpath);

    /// <summary>The bead mesh as Helix takes it, and what its colours were made for; its triangles and colours change in place.</summary>
    public HelixToolkit.SharpDX.MeshGeometry3D? BeadGeometry;
    public object? BeadColoursKey;

    /// <summary>The line points and colours as Helix takes them, kept from one redraw to the next.</summary>
    public HelixToolkit.Vector3Collection? LinePoints;
    public (object Key, HelixToolkit.Color4Collection Colours)? LineColours;
}

/// <summary>One loaded project: its card on the files page, its column in the tables, its pane in the G-code view.</summary>
public sealed partial class ProjectViewModel : ObservableObject
{
    readonly Dictionary<int, Task<LoadedToolpath>> toolpaths = [];

    public ProjectViewModel(BambuProject project)
    {
        Project = project;
        var settings = project.Settings;
        Thumbnail = Image(project.Thumbnails.OrderBy(t => t.Key).Select(t => t.Value).FirstOrDefault());

        var colours = settings.Items("filament_colour");
        var presets = settings.Items("filament_settings_id");
        var types = settings.Items("filament_type");
        Filaments = [.. Enumerable.Range(0, settings.FilamentCount).Select(f => new FilamentItem(
            f + 1, Swatches.Frozen(Palette.Parse(At(colours, f))), At(presets, f), At(types, f)))];
        FilamentColours = [.. Enumerable.Range(0, settings.FilamentCount).Select(f => Palette.Parse(At(colours, f)))];

        var process = settings.Text("print_settings_id") ?? "";
        var parents = settings.Items("inherits_group");
        var processParent = parents.Count > 0 ? parents[0] : "";
        var parts = project.Objects.Sum(o => o.Parts.Count);
        var faces = project.Objects.Sum(o => o.FaceCount);
        var customized = settings.Customized.Count;

        Facts =
        [
            new("Printer", settings.Text("printer_settings_id") ?? settings.Text("printer_model") ?? "?"),
            new("Plate type", settings.Text("curr_bed_type") ?? "?"),
            new("Process", processParent.Length > 0 ? $"{process} (user preset, based on {processParent})" : process),
            new("Objects", $"{Plural(project.Objects.Count, "object")}, {Plural(parts, "part")}, {faces.ToString("N0", CultureInfo.CurrentCulture)} faces"),
            new("Plates", project.Plates.Count.ToString(CultureInfo.CurrentCulture)),
            new("Changed", customized == 0 ? "nothing changed from the presets" : $"{Plural(customized, "setting")} changed from the presets",
                string.Join("\n", settings.Customized.Keys.Order(StringComparer.Ordinal).Select(k => $"{SettingCatalog.Label(k)}  ({k})"))),
            new("Saved with", project.Metadata.GetValueOrDefault("Application") ?? settings.Text("version") ?? "?"),
            new("File", $"{project.LastWriteTime.ToString("g", CultureInfo.CurrentCulture)}, {(project.FileSize / 1e6).ToString("0.0", CultureInfo.CurrentCulture)} MB", project.Path),
        ];
    }

    public BambuProject Project { get; }
    public string Name => Project.FileName;
    public string ShortName => Path.GetFileNameWithoutExtension(Project.FileName);
    public string FullPath => Project.Path;
    public ImageSource? Thumbnail { get; }
    public IReadOnlyList<FilamentItem> Filaments { get; }
    public IReadOnlyList<Vector4> FilamentColours { get; }
    public IReadOnlyList<Fact> Facts { get; }

    /// <summary>Its place in the list, from 0 (-1 until it is placed); sets its identity colour.</summary>
    [ObservableProperty]
    public partial int Index { get; set; } = -1;

    [ObservableProperty]
    public partial Brush IdentityBrush { get; set; } = Brushes.Gray;

    public Color IdentityColour => ((SolidColorBrush)IdentityBrush).Color;

    partial void OnIndexChanged(int value) => IdentityBrush = Swatches.Frozen(Palette.File(value));

    [ObservableProperty]
    public partial bool IsBaseline { get; set; }

    /// <summary>Whether this is the model of the baseline, and if not, how it differs.</summary>
    [ObservableProperty]
    public partial string ModelVerdict { get; set; } = "";

    [ObservableProperty]
    public partial ModelMatch ModelMatch { get; set; }

    [ObservableProperty]
    public partial SliceState SliceState { get; set; }

    [ObservableProperty]
    public partial string SliceText { get; set; } = "Not sliced yet";

    /// <summary>The slice once there is one: from the cache, or just made.</summary>
    [ObservableProperty]
    public partial ProjectSlice? Slice { get; set; }

    public bool IsSliced => Slice is not null;

    partial void OnSliceChanged(ProjectSlice? value)
    {
        toolpaths.Clear();
        OnPropertyChanged(nameof(IsSliced));
        if (value is null) return;
        SliceState = SliceState.Sliced;
        var plates = value.Report.Plates;
        SliceText = plates.Count == 0 ? "Sliced" : string.Join("; ", plates.Select(p =>
            $"{(plates.Count > 1 ? $"plate {p.Index}: " : "")}{Formats.Duration(p.TotalSeconds)}, {p.Grams.ToString("0.0", CultureInfo.CurrentCulture)} g"))
            + (value.FromCache ? "" : $"  (sliced in {value.Elapsed.TotalSeconds.ToString("0", CultureInfo.CurrentCulture)} s)");
    }

    /// <summary>The G-code of a plate, read once.</summary>
    public Task<LoadedToolpath>? Toolpath(int plate)
    {
        if (Slice is not { } slice || !slice.GcodePaths.TryGetValue(plate, out var path)) return null;
        if (!toolpaths.TryGetValue(plate, out var task))
            toolpaths[plate] = task = Task.Run(() => new LoadedToolpath(GcodeParser.Parse(path)));
        return task;
    }

    static ImageSource? Image(byte[]? png)
    {
        if (png is null) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(png);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    static string At(IReadOnlyList<string> list, int index) => index < list.Count ? list[index] : "";

    static string Plural(int n, string what) => n == 1 ? $"1 {what}" : $"{n.ToString("N0", CultureInfo.CurrentCulture)} {what}s";
}

static class Formats
{
    public static string Duration(double seconds)
    {
        if (double.IsNaN(seconds)) return "?";
        var t = TimeSpan.FromSeconds(Math.Round(Math.Abs(seconds)));
        var text = t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m" : t.TotalMinutes >= 1 ? $"{t.Minutes}m {t.Seconds:00}s" : $"{t.Seconds}s";
        return seconds < 0 ? "−" + text : text;
    }

    /// <summary>"+12m 30s (+8 %)" against the baseline's value; empty when equal or there is no baseline.</summary>
    public static string Delta(double value, double? baseline, Func<double, string> format)
    {
        if (baseline is not { } reference || Math.Abs(value - reference) < 1e-9) return "";
        var sign = value > reference ? "+" : "−";
        var share = reference != 0 ? Math.Abs((value - reference) / reference * 100) : 0;
        var percent = share >= 0.05 ? $" ({sign}{share.ToString("0.#", CultureInfo.CurrentCulture)} %)" : "";
        return $"{sign}{format(Math.Abs(value - reference))}{percent}";
    }
}
