using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ThreeMfCompare.Core.Compare;
using ThreeMfCompare.Core.Projects;

namespace ThreeMfCompare.App;

/// <summary>One line of a cell: what the value belongs to (a filament with its colour, an extruder, a mode), and the value.</summary>
public sealed record CellLine(string Label, Brush? Swatch, string Text, bool IsMuted = false);

/// <summary>A file's value of a setting, ready to show.</summary>
public sealed class SettingCellViewModel
{
    public SettingCellViewModel(SettingRowViewModel row, int column, SettingCell cell, ProjectViewModel project, bool isBaseline)
    {
        Row = row;
        Column = column;
        Cell = cell;
        IsBaseline = isBaseline;
        Differs = cell.Differs;
        var value = cell.Value;
        IsMissing = value is null;

        if (value is null) Lines = [new CellLine("", null, "not in this file", IsMuted: true)];
        else if (row.IsLongText)
        {
            var text = value.Text;
            var lines = text.Replace("\r\n", "\n").Split('\n');
            var first = lines.FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
            Lines = [new CellLine("", null, first.Length > 80 ? first[..80] + "…" : first), new CellLine("", null, lines.Length == 1 ? $"{text.Length:N0} characters" : $"{lines.Length:N0} lines", IsMuted: true)];
            FullText = text;
        }
        else if (value.IsUniform)
            Lines = [Line("", null, value.Values.Count == 0 ? "" : value.Values[0], value.Shape == SettingShape.PerFilament && value.Values.Count > 1 ? $"all {value.Values.Count} filaments" : null)];
        else
            Lines = [.. value.Values.Select((v, i) => value.Shape switch
            {
                SettingShape.PerFilament => Line(value.Labels[i], i < project.Filaments.Count ? project.Filaments[i].Swatch : null, v),
                SettingShape.List => Line("", null, v),
                _ => Line(value.Labels[i], null, v),
            })];

        var tip = string.Join("\n\n", new[]
        {
            cell.Customized is { } origin ? $"Changed from the system preset \"{origin.SystemPreset}\"{(origin.Preset != origin.SystemPreset ? $" in \"{origin.Preset}\"" : "")}." : null,
            value?.Detail is { } detail ? "All variants:\n" + detail.Replace(" · ", "\n") : null,
        }.Where(t => t is not null));
        Tip = tip.Length == 0 ? null : tip;
    }

    public SettingRowViewModel Row { get; }
    public int Column { get; }
    public SettingCell Cell { get; }
    public IReadOnlyList<CellLine> Lines { get; }
    public bool Differs { get; }
    public bool IsMissing { get; }
    public bool IsBaseline { get; }
    public bool IsCustomized => Cell.Customized is not null;
    public bool IsLongText => Row.IsLongText && !IsMissing;
    public string? FullText { get; }
    /// <summary>Where the value came from and what the other nozzle variants hold; null when there is nothing to tell.</summary>
    public string? Tip { get; }

    /// <summary>"Compare" for a long text that differs from the baseline's, "View" for the rest.</summary>
    public string LongTextAction => Differs && !IsBaseline ? "Compare with baseline" : "View";

    /// <summary>An empty value reads as such, and "nil" (a filament's "use the printer's value") as what it means.</summary>
    static CellLine Line(string label, Brush? swatch, string text, string? note = null) => text switch
    {
        "" => new CellLine(label, swatch, "empty", IsMuted: true),
        "nil" => new CellLine(label, swatch, "printer's value", IsMuted: true),
        _ => new CellLine(label, swatch, note is null ? text : $"{text}   ({note})"),
    };
}

/// <summary>A setting across the files.</summary>
public sealed class SettingRowViewModel
{
    public SettingRowViewModel(SettingRow row, IReadOnlyList<ProjectViewModel> projects, int baseline)
    {
        Row = row;
        Cells = [.. row.Cells.Select((cell, i) => new SettingCellViewModel(this, i, cell, projects[i], i == baseline))];
    }

    public SettingRow Row { get; }
    public string Key => Row.Key;
    public string Label => Row.Label;
    /// <summary>The setting's key, as in the project file; rows the app made up (objects, plates, the file itself) have none.</summary>
    public string KeyText => Row.Key.Contains('/') ? "" : Row.Key;
    public bool Differs => Row.Differs;
    public bool IsLongText => Row.IsLongText;
    public bool IsKey => Row.KeyRank >= 0;
    public IReadOnlyList<SettingCellViewModel> Cells { get; }
}

/// <summary>A heading between the groups of rows, when all groups are shown.</summary>
public sealed record CategoryHeader(string Name, string Counts);

public enum FilterKind { Key, All, Customized, Category }

/// <summary>An entry of the list of groups beside the table.</summary>
public sealed partial class CategoryFilter(FilterKind kind, string name, SettingCategory category = default) : ObservableObject
{
    public FilterKind Kind => kind;
    public string Name => name;
    public SettingCategory Category => category;

    public bool Matches(SettingRow row) => kind switch
    {
        FilterKind.Key => row.KeyRank >= 0,
        FilterKind.Customized => row.IsCustomized,
        FilterKind.Category => row.Category == category,
        _ => true,
    };

    /// <summary>How many settings of the group differ from the baseline somewhere.</summary>
    [ObservableProperty]
    public partial int Differences { get; set; }

    [ObservableProperty]
    public partial int Total { get; set; }
}
