using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using ThreeMfCompare.Core.Compare;

namespace ThreeMfCompare.App;

public enum DiffKind { Same, Removed, Added, Gap }

/// <summary>A line of the diff as shown: its numbers in the old and the new text, and whether it was taken out or put in.</summary>
public sealed record DiffRow(DiffKind Kind, string Old, string New, string Mark, string Text);

/// <summary>A long text (a G-code template) on its own, or against the baseline's line by line.</summary>
public partial class TextDiffWindow : Window
{
    public TextDiffWindow(TextDiffRequest request)
    {
        InitializeComponent();
        Look.DarkTitleBar(this);
        DataContext = new TextDiffViewModel(request);
    }
}

public sealed partial class TextDiffViewModel : ObservableObject
{
    const int Context = 3;
    readonly IReadOnlyList<DiffLine> diff;

    public TextDiffViewModel(TextDiffRequest request)
    {
        IsDiff = request.OldText is not null;
        Title = $"{request.Title} — 3MF Compare";
        Heading = request.Title;
        diff = LineDiff.Compare(request.OldText ?? request.NewText, request.NewText);
        var removed = diff.Count(d => d.Change == LineChange.Removed);
        var added = diff.Count(d => d.Change == LineChange.Added);
        Summary = IsDiff
            ? $"{request.OldName} (baseline) → {request.NewName}: {removed.ToString("N0", CultureInfo.CurrentCulture)} lines taken out, {added.ToString("N0", CultureInfo.CurrentCulture)} put in"
            : $"{request.NewName}: {diff.Count.ToString("N0", CultureInfo.CurrentCulture)} lines";
        OnlyChanges = IsDiff;
        Lines = Rows();
    }

    public string Title { get; }
    public string Heading { get; }
    public string Summary { get; }
    public bool IsDiff { get; }

    [ObservableProperty]
    public partial bool OnlyChanges { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<DiffRow> Lines { get; set; }

    partial void OnOnlyChangesChanged(bool value) => Lines = Rows();

    IReadOnlyList<DiffRow> Rows()
    {
        var keep = new bool[diff.Count];
        for (var i = 0; i < diff.Count; i++)
        {
            if (!OnlyChanges || diff[i].Change == LineChange.Same) continue;
            for (var k = Math.Max(0, i - Context); k <= Math.Min(diff.Count - 1, i + Context); k++) keep[k] = true;
        }

        var rows = new List<DiffRow>();
        var skipped = 0;
        for (var i = 0; i < diff.Count; i++)
        {
            if (OnlyChanges && !keep[i])
            {
                skipped++;
                continue;
            }
            if (skipped > 0) rows.Add(new DiffRow(DiffKind.Gap, "", "", "", $"… {skipped:N0} equal lines …"));
            skipped = 0;
            var line = diff[i];
            rows.Add(new DiffRow(
                line.Change switch { LineChange.Added => DiffKind.Added, LineChange.Removed => DiffKind.Removed, _ => DiffKind.Same },
                line.Old > 0 ? line.Old.ToString(CultureInfo.InvariantCulture) : "",
                line.New > 0 ? line.New.ToString(CultureInfo.InvariantCulture) : "",
                line.Change switch { LineChange.Added => "+", LineChange.Removed => "−", _ => "" },
                line.Text));
        }
        if (skipped > 0) rows.Add(new DiffRow(DiffKind.Gap, "", "", "", $"… {skipped:N0} equal lines …"));
        return rows;
    }
}
