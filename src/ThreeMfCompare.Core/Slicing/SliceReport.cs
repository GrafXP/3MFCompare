using System.Text.Json;

namespace ThreeMfCompare.Core.Slicing;

/// <summary>Filament used on a plate, by its number in the project (from 1).</summary>
public sealed record FilamentUse(int Id, string FilamentId, double Grams);

/// <param name="TotalSeconds">The slicer's estimate of the whole print, with acceleration, the start G-code and filament changes.</param>
/// <param name="ModelSeconds">The same without the start G-code: the time spent on the layers.</param>
/// <param name="FeatureSeconds">Time per line type ("Outer wall", "Sparse infill", "Travel", …).</param>
public sealed record PlateReport(
    int Index,
    double TotalSeconds,
    double ModelSeconds,
    IReadOnlyDictionary<string, double> FeatureSeconds,
    IReadOnlyList<FilamentUse> Filaments,
    int FilamentChanges,
    string Warning)
{
    public double Grams => Filaments.Sum(f => f.Grams);
}

/// <summary>Bambu Studio's result.json, written beside the G-code by the CLI.</summary>
public sealed record SliceReport(int ReturnCode, string Error, IReadOnlyList<PlateReport> Plates)
{
    public static SliceReport Read(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        var plates = new List<PlateReport>();
        if (root.TryGetProperty("sliced_plates", out var sliced))
            foreach (var plate in sliced.EnumerateArray())
            {
                var features = new Dictionary<string, double>(StringComparer.Ordinal);
                if (plate.TryGetProperty("feature_type_times", out var times))
                    foreach (var feature in times.EnumerateObject())
                        features[feature.Name] = feature.Value.GetDouble();
                var filaments = plate.TryGetProperty("filaments", out var used)
                    ? used.EnumerateArray().Select(f => new FilamentUse(
                        Int(f, "id"),
                        f.TryGetProperty("filament_id", out var id) ? id.GetString() ?? "" : "",
                        Double(f, "total_used_g"))).ToList()
                    : [];
                plates.Add(new PlateReport(
                    Int(plate, "id"),
                    Double(plate, "total_predication"),
                    Double(plate, "main_predication"),
                    features,
                    filaments,
                    Int(plate, "filament_change_times"),
                    plate.TryGetProperty("warning_message", out var warning) ? warning.GetString() ?? "" : ""));
            }

        return new SliceReport(
            root.TryGetProperty("return_code", out var code) ? code.GetInt32() : -1,
            root.TryGetProperty("error_string", out var error) ? error.GetString() ?? "" : "",
            plates);
    }

    static int Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;

    static double Double(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;
}

public sealed class SlicerException(string message) : Exception(message);
