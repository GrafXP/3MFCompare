using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using HelixToolkit;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using ThreeMfCompare.Core.Gcode;
using ThreeMfCompare.Core.Visual;
using PerspectiveCamera = HelixToolkit.Wpf.SharpDX.PerspectiveCamera;
using Point3D = System.Windows.Media.Media3D.Point3D;
using Vector3D = System.Windows.Media.Media3D.Vector3D;

namespace ThreeMfCompare.App;

/// <summary>A line type of the G-code view, with its colour; unticked, its lines are not drawn in any pane.</summary>
public sealed partial class LegendItem(FeatureType feature, Brush swatch) : ObservableObject
{
    public FeatureType Feature => feature;
    public string Label => feature.DisplayName();
    public Brush Swatch => swatch;

    [ObservableProperty]
    public partial bool IsShown { get; set; } = true;
}

/// <summary>What one pane draws, built off the UI thread.</summary>
/// <param name="Beads">The pane's bead mesh, kept from one redraw to the next.</param>
/// <param name="BeadIndices">The triangles of it to draw now; null to keep those it has.</param>
/// <param name="BeadColours">Its new vertex colours; null to keep those it has.</param>
sealed record PaneScene(
    MeshGeometry3D? Beads = null,
    IntCollection? BeadIndices = null,
    Color4Collection? BeadColours = null,
    bool ShowBeads = false,
    LineGeometry3D? Lines = null,
    string Info = "",
    string Caption = "");

/// <summary>One project's G-code in a viewport of its own.</summary>
public sealed partial class ToolpathPaneViewModel : ObservableObject
{
    /// <summary>Vertex colours only: any ambient or specular share would shift them away from the legend.</summary>
    public static readonly PhongMaterial ColouredMaterial = new()
    {
        DiffuseColor = new Color4(1, 1, 1, 1),
        AmbientColor = new Color4(0, 0, 0, 1),
        SpecularColor = new Color4(0, 0, 0, 1),
        VertexColorBlendingFactor = 1,
    };

    public ToolpathPaneViewModel(ProjectViewModel project, LoadedToolpath loaded, IEffectsManager effectsManager, int plate)
    {
        Project = project;
        Loaded = loaded;
        Plate = plate;
        EffectsManager = effectsManager;
        Bed = BedGrid(project);
        var report = project.Slice?.Plate(plate);
        Summary = report is null ? "" : $"{Formats.Duration(report.TotalSeconds)} · {report.Grams.ToString("0.0", CultureInfo.CurrentCulture)} g · {loaded.Toolpath.Layers.Length.ToString("N0", CultureInfo.CurrentCulture)} layers";
    }

    public ProjectViewModel Project { get; }
    public LoadedToolpath Loaded { get; }
    public int Plate { get; }
    public IEffectsManager EffectsManager { get; }
    public PhongMaterial Material => ColouredMaterial;
    public string Summary { get; }

    public PerspectiveCamera Camera { get; } = new()
    {
        Position = new Point3D(128, -120, 160),
        LookDirection = new Vector3D(0, 250, -150),
        UpDirection = new Vector3D(0, 0, 1),
        NearPlaneDistance = 0.1,
        FarPlaneDistance = 10000,
        FieldOfView = 45,
    };

    public LineGeometry3D Bed { get; }

    [ObservableProperty]
    public partial MeshGeometry3D? Beads { get; set; }

    [ObservableProperty]
    public partial LineGeometry3D? Lines { get; set; }

    [ObservableProperty]
    public partial string Info { get; set; } = "";

    [ObservableProperty]
    public partial string Caption { get; set; } = "";

    /// <summary>Raised by the view when the user turned, panned or zoomed this pane's camera.</summary>
    public event Action<ToolpathPaneViewModel>? CameraMoved;

    public void OnCameraMoved() => CameraMoved?.Invoke(this);

    /// <summary>Takes the view of another pane.</summary>
    public void CopyCamera(PerspectiveCamera from)
    {
        Camera.Position = from.Position;
        Camera.LookDirection = from.LookDirection;
        Camera.UpDirection = from.UpDirection;
        Camera.FieldOfView = from.FieldOfView;
        Camera.NearPlaneDistance = from.NearPlaneDistance;
        Camera.FarPlaneDistance = from.FarPlaneDistance;
    }

    /// <summary>On the UI thread: the bead mesh takes its new triangles and colours in place, so only those are uploaded again.</summary>
    internal void Show(PaneScene scene)
    {
        if (scene.Beads is { } beads)
        {
            if (scene.BeadIndices is { } indices) beads.Indices = indices;
            if (scene.BeadColours is { } colours) beads.Colors = colours;
        }
        Beads = scene.ShowBeads ? scene.Beads : null;
        Lines = scene.Lines;
        Info = scene.Info;
        Caption = scene.Caption;
    }

    /// <summary>The printer's bed: its outline and a 10 mm grid, from the project's printable area.</summary>
    static LineGeometry3D BedGrid(ProjectViewModel project)
    {
        var corners = project.Project.Settings.Items("printable_area")
            .Select(p => p.Split('x'))
            .Where(xy => xy.Length == 2)
            .Select(xy => new Vector2(float.Parse(xy[0], CultureInfo.InvariantCulture), float.Parse(xy[1], CultureInfo.InvariantCulture)))
            .ToList();
        if (corners.Count < 3) corners = [new(0, 0), new(256, 0), new(256, 256), new(0, 256)];
        var min = corners.Aggregate(Vector2.Min);
        var max = corners.Aggregate(Vector2.Max);

        var points = new List<Vector3>();
        const float step = 10;
        for (var x = MathF.Ceiling(min.X / step) * step; x <= max.X + 1e-3f; x += step) points.AddRange([new Vector3(x, min.Y, 0), new Vector3(x, max.Y, 0)]);
        for (var y = MathF.Ceiling(min.Y / step) * step; y <= max.Y + 1e-3f; y += step) points.AddRange([new Vector3(min.X, y, 0), new Vector3(max.X, y, 0)]);
        for (var c = 0; c < corners.Count; c++) points.AddRange([new Vector3(corners[c], 0), new Vector3(corners[(c + 1) % corners.Count], 0)]);
        return HelixGeometry.Lines([.. points]);
    }
}

/// <summary>Helix geometry from plain arrays.</summary>
static class HelixGeometry
{
    public static MeshGeometry3D Mesh(Vector3[] positions, Vector3[] normals, Color4Collection colours, IntCollection indices) => new()
    {
        Positions = Fill(new Vector3Collection(positions.Length), positions),
        Normals = Fill(new Vector3Collection(normals.Length), normals),
        Indices = indices,
        Colors = colours,
    };

    public static IntCollection Indices(int[] indices) => Fill(new IntCollection(indices.Length), indices);

    public static Vector3Collection Points(Vector3[] points) => Fill(new Vector3Collection(points.Length), points);

    public static Color4Collection Colours(Vector4[] colours) => Fill(new Color4Collection(colours.Length), MemoryMarshal.Cast<Vector4, Color4>(colours));

    /// <summary>Lines between pairs of <paramref name="points"/>, which are shared, not copied.</summary>
    public static LineGeometry3D Lines(Vector3Collection points, int[] indices, Color4Collection colours) => new()
    {
        Positions = points,
        Indices = Fill(new IntCollection(indices.Length), indices),
        Colors = colours,
    };

    /// <summary>Point pairs; without <paramref name="indices"/> every two points are one line.</summary>
    public static LineGeometry3D Lines(Vector3[] points, int[]? indices = null, Vector4[]? colours = null) => new()
    {
        Positions = Fill(new Vector3Collection(points.Length), points),
        Indices = Fill(new IntCollection(indices?.Length ?? points.Length), indices ?? [.. Enumerable.Range(0, points.Length)]),
        Colors = colours is null ? null : Fill(new Color4Collection(colours.Length), MemoryMarshal.Cast<Vector4, Color4>(colours)),
    };

    static TList Fill<TList, T>(TList list, ReadOnlySpan<T> items) where TList : FastList<T>
    {
        list.Resize(items.Length, true);
        items.CopyTo(list.GetInternalArray());
        return list;
    }
}
