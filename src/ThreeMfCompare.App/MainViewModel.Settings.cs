using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThreeMfCompare.Core.Compare;

namespace ThreeMfCompare.App;

/// <summary>A key setting that differs, for the files page: its label and each file's value.</summary>
public sealed record KeyDifference(string Label, IReadOnlyList<SettingCellViewModel> Cells);

/// <summary>Raised to show a long text, or two of them side by side as a diff.</summary>
public sealed record TextDiffRequest(string Title, string? OldName, string? OldText, string NewName, string NewText);

public sealed partial class MainViewModel
{
    IReadOnlyList<SettingRow> comparison = [];

    public ObservableCollection<CategoryFilter> Categories { get; } = [];

    /// <summary>The rows shown, with a heading before each group when all groups are shown.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<object> SettingItems { get; set; } = [];

    [ObservableProperty]
    public partial CategoryFilter? SelectedCategory { get; set; }

    [ObservableProperty]
    public partial string Search { get; set; } = "";

    [ObservableProperty]
    public partial bool OnlyDifferences { get; set; } = true;

    /// <summary>Only settings that some file changed from its system preset.</summary>
    [ObservableProperty]
    public partial bool OnlyCustomized { get; set; }

    /// <summary>Hide the lists that only name things (variant names, compatible printers, version): they differ between any two saves.</summary>
    [ObservableProperty]
    public partial bool HideBookkeeping { get; set; } = true;

    [ObservableProperty]
    public partial string SettingsSummary { get; set; } = "";

    /// <summary>The key settings that differ, for the files page.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<KeyDifference> KeyDifferences { get; set; } = [];

    [ObservableProperty]
    public partial string DifferenceSummary { get; set; } = "";

    public event Action<TextDiffRequest>? TextDiffRequested;

    void InitializeSettings()
    {
        Categories.Add(new CategoryFilter(FilterKind.Key, "Key settings"));
        Categories.Add(new CategoryFilter(FilterKind.All, "All settings"));
        Categories.Add(new CategoryFilter(FilterKind.Customized, "Changed from presets"));
        foreach (var category in Enum.GetValues<SettingCategory>())
            Categories.Add(new CategoryFilter(FilterKind.Category, SettingCatalog.CategoryName(category), category));
        SelectedCategory = Categories[1];
    }

    partial void OnSelectedCategoryChanged(CategoryFilter? value) => RefreshRows();
    partial void OnSearchChanged(string value) => RefreshRows();
    partial void OnOnlyDifferencesChanged(bool value) => RefreshRows();
    partial void OnOnlyCustomizedChanged(bool value) => RefreshRows();

    partial void OnHideBookkeepingChanged(bool value)
    {
        CountCategories();
        RefreshRows();
    }

    /// <summary>Picks the group shown by its name or kind, as the command line names it.</summary>
    public void SelectCategory(string name)
    {
        var match = Categories.FirstOrDefault(c => c.Name.Contains(name, StringComparison.OrdinalIgnoreCase) || c.Category.ToString().Equals(name, StringComparison.OrdinalIgnoreCase));
        if (match is not null) SelectedCategory = match;
    }

    void Recompare()
    {
        var projects = Projects.Select(p => p.Project).ToList();
        comparison = SettingsComparison.Compare(projects, BaselineIndex);
        CountCategories();
        RefreshRows();

        var key = comparison.Where(r => r.KeyRank >= 0 && r.Differs).OrderBy(r => r.KeyRank).ToList();
        KeyDifferences = [.. key.Select(r => new KeyDifference(r.Label, new SettingRowViewModel(r, Projects, BaselineIndex).Cells))];
        var differing = comparison.Count(r => r.Differs && Shown(r));
        DifferenceSummary = Projects.Count < 2 ? "" :
            differing == 0 ? "All settings are the same." :
            $"{differing.ToString("N0", CultureInfo.CurrentCulture)} settings differ from the baseline, {key.Count} of them key settings.";
    }

    bool Shown(SettingRow row) => !HideBookkeeping || !SettingCatalog.Bookkeeping.Contains(row.Key);

    void CountCategories()
    {
        foreach (var category in Categories)
        {
            var rows = comparison.Where(r => category.Matches(r) && Shown(r)).ToList();
            category.Total = rows.Count;
            category.Differences = Projects.Count < 2 ? 0 : rows.Count(r => r.Differs);
        }
    }

    void RefreshRows()
    {
        if (SelectedCategory is not { } filter || Projects.Count == 0)
        {
            SettingItems = [];
            SettingsSummary = "";
            return;
        }

        var search = Search.Trim();
        var rows = comparison.Where(r => filter.Matches(r) && Shown(r)).ToList();
        var total = rows.Count;
        if (OnlyDifferences && Projects.Count > 1) rows = [.. rows.Where(r => r.Differs)];
        if (OnlyCustomized) rows = [.. rows.Where(r => r.IsCustomized)];
        if (search.Length > 0) rows = [.. rows.Where(r => Contains(r, search))];

        var items = new List<object>();
        if (filter.Kind == FilterKind.Key) rows = [.. rows.OrderBy(r => r.KeyRank)];
        var grouped = filter.Kind is FilterKind.All or FilterKind.Customized;
        foreach (var group in rows.GroupBy(r => grouped ? r.Category : default).OrderBy(g => g.Key))
        {
            if (grouped)
            {
                var all = comparison.Count(r => r.Category == group.Key && Shown(r));
                items.Add(new CategoryHeader(SettingCatalog.CategoryName(group.Key), $"{group.Count()} of {all}"));
            }
            items.AddRange(group.Select(r => new SettingRowViewModel(r, Projects, BaselineIndex)));
        }
        SettingItems = items;

        var shown = items.OfType<SettingRowViewModel>().Count();
        SettingsSummary = shown == total ? $"{total:N0} settings" : $"{shown:N0} of {total:N0} settings";
    }

    static bool Contains(SettingRow row, string search) =>
        row.Key.Contains(search, StringComparison.OrdinalIgnoreCase)
        || row.Label.Contains(search, StringComparison.OrdinalIgnoreCase)
        || row.Cells.Any(c => c.Value is { } v && v.Values.Any(t => t.Contains(search, StringComparison.OrdinalIgnoreCase)));

    [RelayCommand]
    void ShowText(SettingCellViewModel cell)
    {
        var project = Projects[cell.Column];
        var baseline = Baseline;
        var baseCell = cell.Row.Cells[BaselineIndex];
        if (cell.Differs && !cell.IsBaseline && baseline is not null && baseCell.FullText is { } oldText)
            TextDiffRequested?.Invoke(new TextDiffRequest(cell.Row.Label, baseline.Name, oldText, project.Name, cell.FullText ?? ""));
        else
            TextDiffRequested?.Invoke(new TextDiffRequest(cell.Row.Label, null, null, project.Name, cell.FullText ?? ""));
    }

    [RelayCommand]
    void ShowSettingsOf(string category)
    {
        SelectCategory(category);
        Page = Page.Settings;
    }
}
