using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using ThreeMfCompare.Core.Gcode;
using ThreeMfCompare.Core.Visual;
using Point3D = System.Windows.Media.Media3D.Point3D;
using Vector3D = System.Windows.Media.Media3D.Vector3D;

namespace ThreeMfCompare.App;

/// <summary>An entry of a drop-down list.</summary>
public sealed record Choice<T>(T Value, string Label);

/// <summary>What the panes should show; a snapshot of the view settings taken when a redraw starts.</summary>
sealed record ToolpathView(float ZLow, float ZHigh, ToolpathColouring Colouring, ScaleRange? Range, bool[] Shown, bool Beads);

public sealed partial class MainViewModel
{
    /// <summary>The heights the slider steps through: every layer top of every file, so each file's layers can be reached.</summary>
    float[] levels = [];
    Task redraw = Task.CompletedTask;
    bool redrawWanted;
    int panesVersion;
    bool syncingCameras;
    bool camerasFitted;

    public DefaultEffectsManager EffectsManager { get; } = new();

    public ObservableCollection<ToolpathPaneViewModel> Panes { get; } = [];

    public IReadOnlyList<Choice<ToolpathColouring>> Colourings { get; } =
        [.. Enum.GetValues<ToolpathColouring>().Select(c => new Choice<ToolpathColouring>(c, ToolpathColours.Title(c)))];

    [ObservableProperty]
    public partial ToolpathColouring Colouring { get; set; } = ToolpathColouring.Feature;

    public ObservableCollection<LegendItem> LegendItems { get; } = [];

    /// <summary>The filaments of the files, for the filament colouring.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<FilamentItem> FilamentLegend { get; set; } = [];

    [ObservableProperty]
    public partial ScaleRange? Scale { get; set; }

    public Brush ScaleBrush { get; } = ScaleGradient();

    public string ScaleMin => Scale is { } s ? Number(s.Min) : "";
    public string ScaleMax => Scale is { } s ? Number(s.Max) : "";

    partial void OnScaleChanged(ScaleRange? value)
    {
        OnPropertyChanged(nameof(ScaleMin));
        OnPropertyChanged(nameof(ScaleMax));
    }

    [ObservableProperty]
    public partial int LevelCount { get; set; } = 1;

    [ObservableProperty]
    public partial IReadOnlyList<string> LevelLabels { get; set; } = [];

    [ObservableProperty]
    public partial int LowerLevel { get; set; } = 1;

    [ObservableProperty]
    public partial int UpperLevel { get; set; } = 1;

    /// <summary>Draw beads (tubes of each line's width and height) where few enough lines are shown; else plain lines.</summary>
    [ObservableProperty]
    public partial bool UseBeads { get; set; } = true;

    /// <summary>Turning one pane turns them all.</summary>
    [ObservableProperty]
    public partial bool SyncCameras { get; set; } = true;

    [ObservableProperty]
    public partial int PaneColumns { get; set; } = 1;

    [ObservableProperty]
    public partial string ToolpathNote { get; set; } = "";

    /// <summary>Completes when the panes show the current settings.</summary>
    public Task SceneReady => redraw;

    void InitializeToolpaths() { }

    partial void OnColouringChanged(ToolpathColouring value)
    {
        UpdateScale();
        Redraw();
    }

    partial void OnLowerLevelChanged(int value) => Redraw();
    partial void OnUpperLevelChanged(int value) => Redraw();
    partial void OnUseBeadsChanged(bool value) => Redraw();

    partial void OnSyncCamerasChanged(bool value)
    {
        if (value && Panes.Count > 0) PaneCameraMoved(Panes[0]);
    }

    void LegendItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LegendItem.IsShown)) Redraw();
    }

    /// <summary>One pane per project with a slice of the plate shown, all at the same heights and in the same colours.</summary>
    public async Task RefreshPanesAsync()
    {
        var version = ++panesVersion;
        var plate = SelectedPlate;
        var reading = Projects.Select(p => (Project: p, Task: p.Toolpath(plate))).Where(p => p.Task is not null).ToList();
        if (reading.Count == 0)
        {
            ClearPanes();
            ToolpathNote = Projects.Count == 0 ? "Open some projects first."
                : CanSlice ? "Slice the projects to compare their G-code: Slice all, top right." : BambuText;
            return;
        }
        if (reading.Any(r => !r.Task!.IsCompleted)) ToolpathNote = "Reading the G-code…";

        var loaded = new List<(ProjectViewModel Project, LoadedToolpath Toolpath)>();
        foreach (var (project, task) in reading)
        {
            try
            {
                loaded.Add((project, await task!));
            }
            catch (Exception ex) when (ex is IOException or FormatException or InvalidOperationException)
            {
                Failed?.Invoke("Could not read the G-code", $"{project.Name}: {ex.Message}");
            }
        }
        if (version != panesVersion) return;
        ToolpathNote = "";

        // Panes that show the same thing stay, with their cameras.
        var panes = loaded.Select(l => Panes.FirstOrDefault(p => ReferenceEquals(p.Project, l.Project) && ReferenceEquals(p.Loaded, l.Toolpath))
                                       ?? new ToolpathPaneViewModel(l.Project, l.Toolpath, EffectsManager, plate)).ToList();
        if (!panes.SequenceEqual(Panes))
        {
            foreach (var pane in Panes.Except(panes)) pane.CameraMoved -= PaneCameraMoved;
            foreach (var pane in panes.Except(Panes)) pane.CameraMoved += PaneCameraMoved;
            Panes.Clear();
            foreach (var pane in panes) Panes.Add(pane);
            camerasFitted = false;
        }
        PaneColumns = Panes.Count switch { <= 3 => Math.Max(1, Panes.Count), 4 => 2, _ => 3 };

        var atTop = UpperLevel >= LevelCount;
        var (zLow, zHigh) = levels.Length > 0 ? (levels[Math.Clamp(LowerLevel - 1, 0, levels.Length - 1)], levels[Math.Clamp(UpperLevel - 1, 0, levels.Length - 1)]) : (0f, float.MaxValue);
        levels = [.. Panes.SelectMany(p => p.Loaded.Toolpath.Layers.Select(l => MathF.Round(l.Z, 3))).Where(z => !float.IsNaN(z)).Distinct().Order()];
        if (levels.Length == 0) levels = [0];
        LevelLabels = [.. levels.Select(z => z.ToString("0.00", CultureInfo.CurrentCulture))];
        LevelCount = levels.Length;
        // Keep the heights shown where they were; a fresh view, or one that showed the top, shows everything.
        UpperLevel = atTop || zHigh == float.MaxValue ? LevelCount : Math.Max(1, Array.FindLastIndex(levels, z => z <= zHigh + 1e-4f) + 1);
        LowerLevel = Math.Min(UpperLevel, Math.Max(1, Array.FindIndex(levels, z => z >= zLow - 1e-4f) + 1));

        var present = Panes.SelectMany(p => p.Loaded.Toolpath.Segments.Select(s => s.Feature).Distinct()).Distinct().Order().ToList();
        if (!present.SequenceEqual(LegendItems.Select(l => l.Feature)))
        {
            var hidden = LegendItems.Where(l => !l.IsShown).Select(l => l.Feature).ToHashSet();
            foreach (var item in LegendItems) item.PropertyChanged -= LegendItemChanged;
            LegendItems.Clear();
            foreach (var feature in present)
            {
                var item = new LegendItem(feature, Swatches.Frozen(Palette.Feature(feature))) { IsShown = !hidden.Contains(feature) };
                item.PropertyChanged += LegendItemChanged;
                LegendItems.Add(item);
            }
        }
        FilamentLegend = [.. Panes.SelectMany(p => p.Project.Filaments.Select(f => f with { Preset = $"{p.Project.ShortName}: {f.Preset}" }))];

        UpdateScale();
        if (!camerasFitted)
        {
            ViewFrom("iso");
            camerasFitted = true;
        }
        Redraw();
    }

    void ClearPanes()
    {
        foreach (var pane in Panes) pane.CameraMoved -= PaneCameraMoved;
        Panes.Clear();
        camerasFitted = false;
    }

    void UpdateScale() =>
        Scale = ToolpathColours.IsScale(Colouring) && Panes.Count > 0
            ? ToolpathColours.Range([.. Panes.Select(p => (p.Loaded.Toolpath, p.Loaded.LayerSeconds))], Colouring)
            : null;

    /// <summary>Asks for the panes to show the current settings; while a redraw runs, the last request wins.</summary>
    void Redraw()
    {
        redrawWanted = true;
        if (redraw.IsCompleted) redraw = RunRedraws();
    }

    async Task RunRedraws()
    {
        while (redrawWanted)
        {
            redrawWanted = false;
            if (Panes.Count == 0 || levels.Length == 0) return;
            var shown = new bool[FeatureTypes.Count];
            for (var f = 0; f < shown.Length; f++) shown[f] = LegendItems.FirstOrDefault(l => (int)l.Feature == f)?.IsShown ?? true;
            var view = new ToolpathView(
                levels[Math.Clamp(LowerLevel - 1, 0, levels.Length - 1)],
                levels[Math.Clamp(UpperLevel - 1, 0, levels.Length - 1)],
                Colouring, Scale, shown, UseBeads);
            var panes = Panes.ToList();
            try
            {
                var scenes = await Task.Run(() => panes.AsParallel().AsOrdered().Select(p => Build(p, view)).ToList());
                for (var i = 0; i < panes.Count; i++) panes[i].Show(scenes[i]);
            }
            catch (OutOfMemoryException)
            {
                Failed?.Invoke("Not enough memory", "There is not enough memory to draw every pane's lines as beads. Close a project, or untick Beads to draw plain lines.");
                return;
            }
        }
    }

    static PaneScene Build(ToolpathPaneViewModel pane, ToolpathView view)
    {
        var loaded = pane.Loaded;
        var toolpath = loaded.Toolpath;
        if (toolpath.Layers.Length == 0) return new PaneScene(Info: "No layers in this G-code");
        var low = toolpath.LayerAt(view.ZLow);
        var high = toolpath.LayerAt(view.ZHigh);
        // Below this file's first layer: nothing of it is printed yet at that height.
        var empty = view.ZHigh < toolpath.Layers[0].Z - toolpath.Layers[0].Height + 1e-4f;

        var key = (view.Colouring, view.Range);
        if (loaded.Colours is not { } cached || !Equals(cached.Key, key))
            loaded.Colours = cached = (key, ToolpathColours.Colour(toolpath, view.Colouring, view.Range, pane.Project.FilamentColours, loaded.LayerSeconds));

        var layer = toolpath.Layers[high];
        var info = empty
            ? $"below the first layer ({toolpath.Layers[0].Z.ToString("0.00", CultureInfo.CurrentCulture)} mm)"
            : $"layer {(high + 1).ToString("N0", CultureInfo.CurrentCulture)} of {toolpath.Layers.Length.ToString("N0", CultureInfo.CurrentCulture)} · Z {layer.Z.ToString("0.00", CultureInfo.CurrentCulture)} mm · " +
              $"{layer.Height.ToString("0.00", CultureInfo.CurrentCulture)} mm high · {Formats.Duration(loaded.LayerSeconds[high])}";
        if (!empty && low != high) info = $"layers {low + 1:N0}–{high + 1:N0}: " + info;

        if (view.Beads)
        {
            // The bead mesh is built once per toolpath; a redraw only chooses its triangles, and a new colouring only recolours it.
            var mesh = loaded.BeadMesh;
            var (indices, count) = empty ? (Array.Empty<int>(), 0) : mesh.IndicesFor(toolpath, low, high, view.Shown);
            var colours = Equals(loaded.BeadColoursKey, key) ? null : HelixGeometry.Colours(mesh.Colours(cached.Colours));
            loaded.BeadColoursKey = key;
            var triangles = HelixGeometry.Indices(indices);
            if (loaded.BeadGeometry is null)
            {
                loaded.BeadGeometry = HelixGeometry.Mesh(mesh.Positions, mesh.Normals, colours!, triangles);
                (colours, triangles) = (null, null);
            }
            return new PaneScene(loaded.BeadGeometry, triangles, colours, ShowBeads: count > 0, Info: info,
                Caption: $"{count.ToString("N0", CultureInfo.CurrentCulture)} lines");
        }

        // The points and their colours are kept from one redraw to the next: only which of them are drawn changes.
        var visible = empty ? [] : ToolpathGeometry.Visible(toolpath, low, high, f => view.Shown[(int)f]);
        loaded.LinePoints ??= HelixGeometry.Points(loaded.LinePositions);
        if (loaded.LineColours is not { } lineColours || !Equals(lineColours.Key, key))
            loaded.LineColours = lineColours = (key, HelixGeometry.Colours(ToolpathGeometry.LineColours(cached.Colours)));
        var lines = visible.Length == 0 ? null : HelixGeometry.Lines(loaded.LinePoints, ToolpathGeometry.LineIndices(visible), lineColours.Colours);
        return new PaneScene(Lines: lines, Info: info, Caption: $"{visible.Length.ToString("N0", CultureInfo.CurrentCulture)} lines, drawn as plain lines");
    }

    void PaneCameraMoved(ToolpathPaneViewModel moved)
    {
        if (!SyncCameras || syncingCameras) return;
        syncingCameras = true;
        try
        {
            foreach (var pane in Panes)
                if (!ReferenceEquals(pane, moved)) pane.CopyCamera(moved.Camera);
        }
        finally
        {
            syncingCameras = false;
        }
    }

    /// <summary>Every pane looks at the prints from the same side, framed alike: "iso", "top", "front", "left" or "right"; "fit" keeps the direction.</summary>
    [RelayCommand]
    void ViewFrom(string side)
    {
        var bounds = Panes.Select(p => p.Loaded.Bounds).OfType<(Vector3 Min, Vector3 Max)>().ToList();
        if (bounds.Count == 0) return;
        var min = bounds.Select(b => b.Min).Aggregate(Vector3.Min);
        var max = bounds.Select(b => b.Max).Aggregate(Vector3.Max);
        var centre = (min + max) / 2;
        var radius = Math.Max((max - min).Length() / 2, 1);
        // From above only the footprint has to fit, and the camera looks at the layers shown.
        if (side == "top" && levels.Length > 0)
        {
            radius = Math.Max((new Vector2(max.X, max.Y) - new Vector2(min.X, min.Y)).Length() / 2, 1);
            centre.Z = levels[Math.Clamp(UpperLevel - 1, 0, levels.Length - 1)];
        }

        Vector3D? direction = side switch
        {
            "top" => new Vector3D(0, 0.0001, -1),
            "front" => new Vector3D(0, 1, -0.0001),
            "left" => new Vector3D(1, 0, -0.0001),
            "right" => new Vector3D(-1, 0, -0.0001),
            "iso" => new Vector3D(-0.6, 1, -0.75),
            _ => null,
        };
        syncingCameras = true;
        try
        {
            foreach (var pane in Panes)
            {
                var camera = pane.Camera;
                var look = direction ?? camera.LookDirection;
                look.Normalize();
                var distance = radius / Math.Sin(camera.FieldOfView * Math.PI / 360) * 1.1;
                camera.LookDirection = look * distance;
                camera.Position = new Point3D(centre.X, centre.Y, centre.Z) - look * distance;
                camera.UpDirection = Math.Abs(look.Z) > 0.999 ? new Vector3D(0, 1, 0) : new Vector3D(0, 0, 1);
                camera.NearPlaneDistance = Math.Max(0.05, radius / 200);
                camera.FarPlaneDistance = Math.Max(2000, radius * 40);
            }
        }
        finally
        {
            syncingCameras = false;
        }
    }

    /// <summary>
    /// Turns the first pane's camera about the vertical through what it looks at, as a drag would,
    /// and leaves the other panes to follow through the sync; for the command line.
    /// </summary>
    public void TurnFirstPane(double degrees)
    {
        if (Panes.Count == 0) return;
        var camera = Panes[0].Camera;
        var target = camera.Position + camera.LookDirection;
        var turn = new System.Windows.Media.Media3D.RotateTransform3D(new System.Windows.Media.Media3D.AxisAngleRotation3D(new Vector3D(0, 0, 1), degrees), target);
        camera.Position = turn.Transform(camera.Position);
        camera.LookDirection = turn.Transform(camera.LookDirection);
    }

    /// <summary>Only the top layer shown, or everything up to it again.</summary>
    [RelayCommand]
    void SingleLayer()
    {
        if (LowerLevel == UpperLevel) LowerLevel = 1;
        else LowerLevel = UpperLevel;
    }

    /// <summary>Picks the heights shown by Z in mm, as the command line names them: "12.4" up to there, "10-12.4" that slab, "12.4-12.4" one layer.</summary>
    public void ShowHeights(string range)
    {
        var parts = range.Split('-', 2);
        if (!float.TryParse(parts[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var high)) return;
        var low = parts.Length == 2 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var l) ? l : high;
        UpperLevel = Math.Max(1, Array.FindLastIndex(levels, z => z <= high + 1e-4f) + 1);
        LowerLevel = parts.Length == 2 ? Math.Clamp(Array.FindIndex(levels, z => z >= low - 1e-4f) + 1, 1, UpperLevel) : 1;
    }

    static string Number(float value) => Math.Abs(value) >= 100 ? value.ToString("0", CultureInfo.CurrentCulture)
        : Math.Abs(value) >= 10 ? value.ToString("0.#", CultureInfo.CurrentCulture) : value.ToString("0.##", CultureInfo.CurrentCulture);

    static Brush ScaleGradient()
    {
        var stops = new GradientStopCollection();
        for (var i = 0; i <= 8; i++)
            stops.Add(new GradientStop(Swatches.Colour(Palette.Sequential[i * (Palette.Sequential.Length - 1) / 8]), i / 8.0));
        var brush = new LinearGradientBrush(stops, new System.Windows.Point(0, 0), new System.Windows.Point(1, 0));
        brush.Freeze();
        return brush;
    }
}
