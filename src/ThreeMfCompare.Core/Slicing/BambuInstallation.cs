using System.Diagnostics;

namespace ThreeMfCompare.Core.Slicing;

/// <summary>An installed Bambu Studio plus its per-user data folder.</summary>
public sealed class BambuInstallation
{
    public const string DefaultDirectory = @"C:\Program Files\Bambu Studio";

    /// <summary>Overrides where Bambu Studio is looked for.</summary>
    public const string DirectoryVariable = "THREEMFCOMPARE_BAMBU_DIR";

    BambuInstallation(string directory)
    {
        Directory = directory;
        AppDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BambuStudio");
        Version = FileVersionInfo.GetVersionInfo(ExecutablePath).FileVersion ?? "unknown";
    }

    public string Directory { get; }
    public string ExecutablePath => Path.Combine(Directory, "bambu-studio.exe");

    /// <summary>%APPDATA%\BambuStudio: BambuStudio.conf, user presets, logs.</summary>
    public string AppDataDirectory { get; }

    public string LogDirectory => Path.Combine(AppDataDirectory, "log");

    /// <summary>File version of the executable, e.g. "02.08.02.61". Part of every slice's cache key.</summary>
    public string Version { get; }

    /// <summary>
    /// Finds Bambu Studio in <paramref name="directory"/>, else in THREEMFCOMPARE_BAMBU_DIR, else in
    /// GCODEFEM_BAMBU_DIR (GcodeFEM's), else in the default install folder. Null when it is not there.
    /// </summary>
    public static BambuInstallation? TryLocate(string? directory = null)
    {
        foreach (var candidate in new[] { directory, Environment.GetEnvironmentVariable(DirectoryVariable), Environment.GetEnvironmentVariable("GCODEFEM_BAMBU_DIR"), DefaultDirectory })
            if (candidate is { Length: > 0 } && File.Exists(Path.Combine(candidate, "bambu-studio.exe")))
                return new BambuInstallation(candidate);
        return null;
    }
}
