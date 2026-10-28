using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThreeMfCompare.Core.Gcode;
using ThreeMfCompare.Core.Slicing;

namespace ThreeMfCompare.App;

/// <summary>A file's value of a slice result.</summary>
/// <param name="Delta">Against the baseline: "+12m 30s (+8 %)".</param>
/// <param name="Fraction">Of the largest value in the row, for a bar; negative for none.</param>
public sealed record MetricCell(string Text, string Delta, double Fraction, Brush Bar, bool Differs, bool IsMissing = false)
{
    public bool HasBar => Fraction >= 0;
}

public sealed record MetricRow(string Label, IReadOnlyList<MetricCell> Cells, bool HasBars = false);

/// <summary>A heading between groups of slice results.</summary>
public sealed record MetricHeader(string Name, string Note = "");

public sealed partial class MainViewModel
{
    BambuInstallation? bambu;
    ProjectSlicer? slicer;
    CancellationTokenSource? sliceCancel;
    int metricsVersion;

    public string BambuText => bambu is null
        ? $"Bambu Studio not found in {BambuInstallation.DefaultDirectory}: set {BambuInstallation.DirectoryVariable} to its folder to slice."
        : $"Bambu Studio {bambu.Version}";

    public bool CanSlice => slicer is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SliceAllCommand))]
    public partial bool IsSlicing { get; set; }

    [ObservableProperty]
    public partial string SliceProgress { get; set; } = "";

    public ObservableCollection<int> PlateNumbers { get; } = [1];

    [ObservableProperty]
    public partial int SelectedPlate { get; set; } = 1;

    public bool HasPlates => PlateNumbers.Count > 1;

    [ObservableProperty]
    public partial IReadOnlyList<object> MetricItems { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<ChartSeries> LayerChart { get; set; } = [];

    [ObservableProperty]
    public partial int SlicedCount { get; set; }

    [ObservableProperty]
    public partial string MetricsNote { get; set; } = "";

    void InitializeSlicing()
    {
        bambu = BambuInstallation.TryLocate();
        if (bambu is not null) slicer = new ProjectSlicer(bambu, ProjectSlicer.DefaultCacheDirectory);
    }

    partial void OnSelectedPlateChanged(int value)
    {
        if (Page == Page.Slices) _ = RefreshMetricsAsync();
        if (Page == Page.Gcode) _ = RefreshPanesAsync();
    }

    bool CanSliceAll() => slicer is not null && !IsSlicing;

    /// <summary>Slices, one after the other, every project that has no slice yet; Bambu Studio uses all cores for each.</summary>
    [RelayCommand(CanExecute = nameof(CanSliceAll))]
    async Task SliceAll() => await SliceAsync([.. Projects.Where(p => !p.IsSliced)]);

    [RelayCommand]
    async Task SliceProject(ProjectViewModel project)
    {
        if (IsSlicing) return;
        await SliceAsync([project]);
    }

    [RelayCommand]
    void CancelSlicing() => sliceCancel?.Cancel();

    public async Task SliceAsync(IReadOnlyList<ProjectViewModel> projects)
    {
        if (slicer is null)
        {
            Failed?.Invoke("Bambu Studio not found", BambuText);
            return;
        }
        if (projects.Count == 0 || IsSlicing) return;

        IsSlicing = true;
        sliceCancel = new CancellationTokenSource();
        var token = sliceCancel.Token;
        foreach (var project in projects) (project.SliceState, project.SliceText) = (SliceState.Queued, "Waiting to be sliced");
        var failures = new List<string>();
        try
        {
            for (var n = 0; n < projects.Count; n++)
            {
                var project = projects[n];
                if (!Projects.Contains(project)) continue;
                token.ThrowIfCancellationRequested();
                (project.SliceState, project.SliceText) = (SliceState.Slicing, "Slicing with Bambu Studio…");
                SliceProgress = $"Slicing {n + 1} of {projects.Count}: {project.Name}";
                Status = SliceProgress;
                try
                {
                    project.Slice = await slicer.SliceAsync(project.FullPath, project.Project.Hash, useCache: true, token);
                }
                catch (SlicerException ex)
                {
                    (project.SliceState, project.SliceText) = (SliceState.Failed, ex.Message.Split('\n')[0]);
                    failures.Add($"{project.Name}: {ex.Message}");
                }
                SlicesChanged();
            }
            Status = failures.Count == 0 ? $"Sliced {projects.Count} {(projects.Count == 1 ? "project" : "projects")}." : $"{failures.Count} of {projects.Count} could not be sliced.";
        }
        catch (OperationCanceledException)
        {
            foreach (var project in projects.Where(p => p.SliceState is SliceState.Queued or SliceState.Slicing))
                (project.SliceState, project.SliceText) = (SliceState.NotSliced, "Slicing cancelled");
            Status = "Slicing cancelled.";
        }
        finally
        {
            IsSlicing = false;
            SliceProgress = "";
            sliceCancel.Dispose();
            sliceCancel = null;
        }
        if (failures.Count > 0) Failed?.Invoke("Bambu Studio could not slice every project", string.Join("\n\n", failures));
    }

    /// <summary>The plates there are, and whatever shows slices.</summary>
    void SlicesChanged()
    {
        SlicedCount = Projects.Count(p => p.IsSliced);
        var plates = Projects.SelectMany(p => p.Slice?.GcodePaths.Keys ?? (IEnumerable<int>)[]).Concat(Projects.SelectMany(p => p.Project.Plates.Select(pl => pl.Index)))
            .Append(1).Distinct().Order().ToList();
        if (!plates.SequenceEqual(PlateNumbers))
        {
            PlateNumbers.Clear();
            foreach (var plate in plates) PlateNumbers.Add(plate);
            OnPropertyChanged(nameof(HasPlates));
        }
        if (!PlateNumbers.Contains(SelectedPlate)) SelectedPlate = PlateNumbers[0];
        if (Page == Page.Slices) _ = RefreshMetricsAsync();
        if (Page == Page.Gcode) _ = RefreshPanesAsync();
    }

    /// <summary>The slice results of the plate shown, side by side; the G-code of each is read for its layers.</summary>
    public async Task RefreshMetricsAsync()
    {
        var version = ++metricsVersion;
        var projects = Projects.ToList();
        var plate = SelectedPlate;
        var baseline = BaselineIndex;
        var reading = projects.Select(p => p.Toolpath(plate)).ToList();
        if (reading.Any(t => t is { IsCompleted: false })) MetricsNote = "Reading the G-code…";

        var toolpaths = new LoadedToolpath?[projects.Count];
        for (var i = 0; i < projects.Count; i++)
        {
            if (reading[i] is not { } task) continue;
            try
            {
                toolpaths[i] = await task;
            }
            catch (Exception ex) when (ex is IOException or FormatException or InvalidOperationException)
            {
                toolpaths[i] = null;
            }
        }
        if (version != metricsVersion) return;

        var reports = projects.Select(p => p.Slice?.Plate(plate)).ToList();
        var items = new List<object>();
        MetricRow Row(string label, Func<int, double?> value, Func<double, string> format, bool bars = false, Func<double, string>? deltaFormat = null)
        {
            var values = Enumerable.Range(0, projects.Count).Select(value).ToList();
            var max = values.Where(v => v is not null).Select(v => v!.Value).DefaultIfEmpty(0).Max();
            var reference = baseline < values.Count ? values[baseline] : null;
            return new MetricRow(label, [.. values.Select((v, i) => v is not { } x
                ? new MetricCell(projects[i].IsSliced ? "—" : "not sliced", "", -1, projects[i].IdentityBrush, false, IsMissing: true)
                : new MetricCell(format(x), i == baseline ? "" : Formats.Delta(x, reference, deltaFormat ?? format), bars && max > 0 ? x / max : -1,
                    projects[i].IdentityBrush, i != baseline && reference is { } r && Math.Abs(r - x) > 1e-9))], bars);
        }

        string Grams(double g) => $"{g.ToString("0.0", CultureInfo.CurrentCulture)} g";
        string Count(double n) => n.ToString("N0", CultureInfo.CurrentCulture);

        items.Add(new MetricHeader("Print", "Bambu Studio's estimates"));
        items.Add(Row("Total time", i => reports[i]?.TotalSeconds, Formats.Duration, bars: true));
        items.Add(Row("Model printing time", i => reports[i]?.ModelSeconds is > 0 ? reports[i]!.ModelSeconds : toolpaths[i]?.Toolpath.HeaderSeconds("model printing time"), Formats.Duration, bars: true));
        items.Add(Row("Filament", i => reports[i]?.Grams, Grams, bars: true));
        var filamentCount = reports.Max(r => r?.Filaments.Count ?? 0);
        if (filamentCount > 1 || projects.Any(p => p.Filaments.Count > 1))
            foreach (var id in reports.SelectMany(r => r?.Filaments.Select(f => f.Id) ?? []).Distinct().Order())
                items.Add(Row($"Filament {id}", i => reports[i]?.Filaments.FirstOrDefault(f => f.Id == id)?.Grams, Grams, bars: true));
        items.Add(Row("Filament length", i => toolpaths[i]?.Toolpath.HeaderNumber("total filament length [mm]") / 1000, m => $"{m.ToString("0.00", CultureInfo.CurrentCulture)} m"));
        items.Add(Row("Filament changes", i => reports[i]?.FilamentChanges, Count));
        items.Add(Row("Layers", i => toolpaths[i]?.Toolpath.Layers.Length, Count));
        items.Add(Row("Height", i => toolpaths[i]?.Toolpath.HeaderNumber("max_z_height"), z => $"{z.ToString("0.00", CultureInfo.CurrentCulture)} mm"));
        items.Add(Row("Extrusion moves", i => toolpaths[i]?.Toolpath.Segments.Length, Count));
        items.Add(Row("Slowest layer", i => toolpaths[i]?.LayerSeconds.DefaultIfEmpty(0).Max(), Formats.Duration));

        var features = reports.Where(r => r is not null).SelectMany(r => r!.FeatureSeconds.Keys).Distinct()
            .OrderByDescending(f => reports.Where(r => r is not null).Max(r => r!.FeatureSeconds.GetValueOrDefault(f)))
            .Where(f => reports.Any(r => r?.FeatureSeconds.GetValueOrDefault(f) > 0.5))
            .ToList();
        if (features.Count > 0)
        {
            items.Add(new MetricHeader("Time by line type", "includes acceleration; Custom is the start and end G-code, Undefined what Bambu Studio does not assign"));
            foreach (var feature in features)
                items.Add(Row(feature, i => reports[i]?.FeatureSeconds.GetValueOrDefault(feature), Formats.Duration, bars: true));
        }
        if (reports.Any(r => r?.Warning is { Length: > 0 }))
        {
            items.Add(new MetricHeader("Warnings"));
            items.Add(new MetricRow("Slicer warning", [.. reports.Select((r, i) => new MetricCell(r?.Warning ?? "", "", -1, projects[i].IdentityBrush, false))]));
        }
        MetricItems = items;

        LayerChart = [.. toolpaths.Select((t, i) => t is null ? null : new ChartSeries(projects[i].ShortName, projects[i].IdentityColour,
            [.. t.Toolpath.Layers.Select((layer, n) => ((double)layer.Z, t.LayerSeconds[n]))])).OfType<ChartSeries>()];
        MetricsNote = toolpaths.Any(t => t is not null)
            ? "Layer times: the G-code's feed-rate times, scaled to add up to Bambu Studio's model printing time."
            : "";
    }
}
