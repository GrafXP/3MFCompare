using System.Globalization;

namespace ThreeMfCompare.App;

/// <summary>
/// The app's command line: "a.3mf b.3mf --option value --flag". It can open, slice, pick a page and
/// a view and save a screenshot without anyone touching the window, which is how the pages are checked:
///
///   3MFCompare a.3mf b.3mf … [--baseline n] [--page files|settings|slices|gcode]
///              [--category name] [--search text] [--all] [--customized]
///              [--slice] [--plate n] [--colour feature|filament|speed|flow|width|layerheight|layertime|fan|temperature]
///              [--z 12.4 | --z 10-12.4] [--view iso|top|front|left|right] [--lines] [--hide "sparse infill,inner wall"]
///              [--turn degrees] [--diff key] [--screenshot file.png] [--size 1600x1000] [--wait seconds]
///
/// --baseline counts files from 1. --all shows equal settings too. --turn turns the first pane only, as a
/// drag would, to check that the others follow. --diff opens the diff of a setting's
/// text (a G-code template) between the baseline and the second file. With --screenshot the app saves
/// the picture and exits; its exit code is 1 if a step failed.
/// </summary>
sealed class StartupOptions
{
    readonly Dictionary<string, string?> options = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Files { get; } = [];
    public string? Screenshot => Text("screenshot");

    public bool Has(string name) => options.ContainsKey(name);
    public string? Text(string name) => options.GetValueOrDefault(name);

    public double? Number(string name) =>
        Text(name) is { } text
            ? double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : throw new FormatException($"--{name} expects a number but got '{text}'.")
            : null;

    /// <summary>The option as a value of <typeparamref name="T"/>, or null if it is absent or names something else.</summary>
    public T? Enum<T>(string name) where T : struct, Enum =>
        Text(name) is { } text && System.Enum.TryParse<T>(text, ignoreCase: true, out var value) && System.Enum.IsDefined(value) ? value : null;

    /// <summary>"1600x1000" → (1600, 1000).</summary>
    public (double Width, double Height)? Size(string name) =>
        Text(name)?.Split('x') is [var width, var height]
            ? (double.Parse(width, CultureInfo.InvariantCulture), double.Parse(height, CultureInfo.InvariantCulture))
            : null;

    public static StartupOptions Parse(IEnumerable<string> arguments)
    {
        var result = new StartupOptions();
        var args = arguments.ToArray();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
                result.options[args[i][2..]] = hasValue ? args[++i] : null;
            }
            else result.Files.Add(args[i]);
        }
        return result;
    }
}
