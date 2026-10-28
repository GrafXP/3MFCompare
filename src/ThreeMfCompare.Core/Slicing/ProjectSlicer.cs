using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ThreeMfCompare.Core.Slicing;

/// <param name="GcodePaths">Per plate number, its G-code file.</param>
public sealed record ProjectSlice(string Directory, SliceReport Report, IReadOnlyDictionary<int, string> GcodePaths, bool FromCache, TimeSpan Elapsed)
{
    public PlateReport? Plate(int index) => Report.Plates.FirstOrDefault(p => p.Index == index);
}

/// <summary>
/// Slices a project with Bambu Studio's CLI exactly as it is saved: its own printer, process,
/// filaments, plates, object settings and variable layer heights, nothing arranged or replaced.
/// Results are cached by the project's bytes and the Bambu Studio version.
/// </summary>
public sealed class ProjectSlicer(BambuInstallation bambu, string cacheDirectory)
{
    const string InputFile = "project.3mf";
    const string ResultFile = "result.json";
    const string CompleteMarker = "complete";

    public static string DefaultCacheDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "3MFCompare", "cache", "slices");

    public BambuInstallation Bambu => bambu;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>The cache folder this project's slice is (or would be) in.</summary>
    public string DirectoryFor(string projectHash)
    {
        var key = SHA256.HashData(Encoding.UTF8.GetBytes($"{bambu.Version}\n{projectHash}"));
        return Path.Combine(cacheDirectory, Convert.ToHexString(key)[..20].ToLowerInvariant());
    }

    /// <summary>The slice from the cache, or null if this project has not been sliced with this Bambu Studio.</summary>
    public ProjectSlice? Cached(string projectHash)
    {
        var directory = DirectoryFor(projectHash);
        return File.Exists(Path.Combine(directory, CompleteMarker)) ? Load(directory, fromCache: true, TimeSpan.Zero) : null;
    }

    public async Task<ProjectSlice> SliceAsync(string projectPath, string projectHash, bool useCache = true, CancellationToken cancellationToken = default)
    {
        if (useCache && Cached(projectHash) is { } cached) return cached;

        var watch = Stopwatch.StartNew();
        var directory = DirectoryFor(projectHash);
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        Directory.CreateDirectory(directory);
        // A copy under a plain name: the CLI then never sees the user's path, whatever characters it has.
        File.Copy(projectPath, Path.Combine(directory, InputFile));
        await RunAsync(directory, cancellationToken);

        var slice = Load(directory, fromCache: false, watch.Elapsed);
        File.Delete(Path.Combine(directory, InputFile));
        await File.WriteAllTextAsync(Path.Combine(directory, CompleteMarker), DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), cancellationToken);
        return slice;
    }

    static ProjectSlice Load(string directory, bool fromCache, TimeSpan elapsed)
    {
        var report = SliceReport.Read(Path.Combine(directory, ResultFile));
        if (report.ReturnCode != 0)
            throw new SlicerException($"Bambu Studio could not slice the project (return code {report.ReturnCode}): {report.Error}");

        var gcode = new Dictionary<int, string>();
        foreach (var file in Directory.EnumerateFiles(directory, "plate_*.gcode"))
            if (int.TryParse(Path.GetFileNameWithoutExtension(file)["plate_".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var plate))
                gcode[plate] = file;
        if (gcode.Count == 0)
            throw new SlicerException($"Bambu Studio reported success but wrote no G-code in '{directory}'.");
        return new ProjectSlice(directory, report, gcode, fromCache, elapsed);
    }

    async Task RunAsync(string directory, CancellationToken cancellationToken)
    {
        var start = DateTime.UtcNow;
        var info = new ProcessStartInfo(bambu.ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = directory,
        };
        // All plates (0), into the cache folder, with nothing else asked of it: no arranging, no other presets.
        foreach (var argument in new[] { "--debug", "2", "--slice", "0", "--outputdir", directory, Path.Combine(directory, InputFile) })
            info.ArgumentList.Add(argument);

        using var process = Process.Start(info) ?? throw new SlicerException($"Could not start '{bambu.ExecutablePath}'.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            cancellationToken.ThrowIfCancellationRequested();
            throw new SlicerException($"Bambu Studio did not finish within {Timeout.TotalMinutes:0.#} min.{LogTail(start)}");
        }

        // The GUI-subsystem exe prints nothing; result.json and the log file are the only diagnostics.
        var result = Path.Combine(directory, ResultFile);
        if (process.ExitCode != 0 || !File.Exists(result))
        {
            var reason = File.Exists(result) ? SliceReport.Read(result).Error : "no result.json written";
            throw new SlicerException($"Bambu Studio exited with code {process.ExitCode}: {reason}.{LogTail(start)}");
        }
    }

    /// <summary>Last lines of the newest Bambu log written since <paramref name="since"/>, for error messages.</summary>
    string LogTail(DateTime since)
    {
        try
        {
            if (!Directory.Exists(bambu.LogDirectory)) return "";
            var log = new DirectoryInfo(bambu.LogDirectory).EnumerateFiles("debug_*.log*")
                .Where(f => f.LastWriteTimeUtc >= since.AddSeconds(-2) && !f.Name.EndsWith(".enc", StringComparison.Ordinal))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
            if (log is null) return "";

            using var stream = new FileStream(log.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var lines = new StreamReader(stream).ReadToEnd().Split('\n');
            return $"\nLast lines of {log.Name}:\n" + string.Join('\n', lines.TakeLast(30));
        }
        catch (IOException)
        {
            return "";
        }
    }
}
