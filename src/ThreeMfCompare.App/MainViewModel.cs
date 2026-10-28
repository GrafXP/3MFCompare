using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThreeMfCompare.Core.Compare;
using ThreeMfCompare.Core.Projects;

namespace ThreeMfCompare.App;

public enum Page { Files, Settings, Slices, Gcode }

/// <summary>
/// The app: the projects being compared, which of them the others are held against, and the four
/// pages that compare them (the files, their settings, their slices and their G-code).
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    public MainViewModel()
    {
        InitializeSlicing();
        InitializeSettings();
        InitializeToolpaths();
        Projects.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasProjects));
    }

    public ObservableCollection<ProjectViewModel> Projects { get; } = [];

    public bool HasProjects => Projects.Count > 0;

    [ObservableProperty]
    public partial Page Page { get; set; } = Page.Files;

    [ObservableProperty]
    public partial string Status { get; set; } = "Drop Bambu Studio projects (.3mf) on the window, or open them.";

    /// <summary>The file the others are compared against.</summary>
    public ProjectViewModel? Baseline => Projects.FirstOrDefault(p => p.IsBaseline) ?? Projects.FirstOrDefault();

    public int BaselineIndex => Baseline is { } baseline ? Projects.IndexOf(baseline) : 0;

    public string Title => Projects.Count == 0 ? "3MF Compare" : $"3MF Compare — {string.Join(" · ", Projects.Select(p => p.ShortName))}";

    /// <summary>Raised with a title and a message when something the user asked for failed.</summary>
    public event Action<string, string>? Failed;

    partial void OnPageChanged(Page value)
    {
        if (value == Page.Gcode) _ = RefreshPanesAsync();
        if (value == Page.Slices) _ = RefreshMetricsAsync();
    }

    public async Task AddFilesAsync(IEnumerable<string> paths)
    {
        var files = paths.Where(p => File.Exists(p) && p.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase)).ToList();
        if (files.Count == 0) return;
        Status = files.Count == 1 ? $"Reading {Path.GetFileName(files[0])}…" : $"Reading {files.Count} projects…";

        var errors = new List<string>();
        var loaded = await Task.Run(() => files.Select(file =>
        {
            try
            {
                return BambuProject.Load(file);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                lock (errors) errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
                return null;
            }
        }).ToList());

        foreach (var project in loaded.OfType<BambuProject>())
        {
            if (Projects.Any(p => string.Equals(p.FullPath, project.Path, StringComparison.OrdinalIgnoreCase))) continue;
            var item = new ProjectViewModel(project);
            if (slicer?.Cached(project.Hash) is { } cached) item.Slice = cached;
            Projects.Add(item);
        }
        ProjectsChanged();
        Status = $"{Projects.Count} {(Projects.Count == 1 ? "project" : "projects")} open.";
        if (errors.Count > 0) Failed?.Invoke("Could not read every file", string.Join("\n\n", errors));
        if (Projects.Count >= 2 && Page == Page.Files && loaded.Count > 0) Status += " The Settings page lists what differs.";
    }

    [RelayCommand]
    void Remove(ProjectViewModel project)
    {
        Projects.Remove(project);
        ProjectsChanged();
    }

    [RelayCommand]
    void SetBaseline(ProjectViewModel project)
    {
        foreach (var p in Projects) p.IsBaseline = ReferenceEquals(p, project);
        ProjectsChanged();
    }

    [RelayCommand]
    void MoveLeft(ProjectViewModel project) => Move(project, -1);

    [RelayCommand]
    void MoveRight(ProjectViewModel project) => Move(project, 1);

    void Move(ProjectViewModel project, int by)
    {
        var from = Projects.IndexOf(project);
        var to = Math.Clamp(from + by, 0, Projects.Count - 1);
        if (from < 0 || from == to) return;
        Projects.Move(from, to);
        ProjectsChanged();
    }

    [RelayCommand]
    void Clear()
    {
        Projects.Clear();
        ProjectsChanged();
    }

    /// <summary>Everything that depends on which files there are, in which order, against which baseline.</summary>
    void ProjectsChanged()
    {
        if (Projects.Count > 0 && !Projects.Any(p => p.IsBaseline)) Projects[0].IsBaseline = true;
        var baseline = Baseline;
        for (var i = 0; i < Projects.Count; i++)
        {
            var project = Projects[i];
            project.Index = i;
            if (baseline is null || ReferenceEquals(project, baseline))
            {
                project.ModelMatch = ModelMatch.Identical;
                project.ModelVerdict = Projects.Count > 1 ? "Baseline: the others are compared with it" : "";
            }
            else
            {
                var verdict = ModelComparison.Compare(project.Project, baseline.Project);
                project.ModelMatch = verdict.Match;
                project.ModelVerdict = verdict.Summary;
            }
        }
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Baseline));
        Recompare();
        SlicesChanged();
    }

    public void Dispose()
    {
        sliceCancel?.Cancel();
        EffectsManager.Dispose();
    }
}
