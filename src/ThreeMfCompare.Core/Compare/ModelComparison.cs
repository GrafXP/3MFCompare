using System.Globalization;
using ThreeMfCompare.Core.Projects;

namespace ThreeMfCompare.Core.Compare;

public enum ModelMatch
{
    /// <summary>The same mesh files, byte for byte.</summary>
    Identical,
    /// <summary>The same objects, parts and face counts; the mesh files were saved again (Bambu Studio re-centres vertices, which moves their last digits).</summary>
    SameGeometry,
    Different,
}

public sealed record ModelVerdict(ModelMatch Match, string Summary);

/// <summary>Whether two projects print the same model, judged without reading the meshes.</summary>
public static class ModelComparison
{
    public static ModelVerdict Compare(BambuProject project, BambuProject baseline)
    {
        var differences = Differences(project, baseline).ToList();
        if (differences.Count > 0) return new ModelVerdict(ModelMatch.Different, string.Join("; ", differences));

        var sameFiles = project.MeshChecksums.Count == baseline.MeshChecksums.Count
                        && project.MeshChecksums.All(m => baseline.MeshChecksums.TryGetValue(m.Key, out var crc) && crc == m.Value);
        return sameFiles
            ? new ModelVerdict(ModelMatch.Identical, "Same model: identical mesh files")
            : new ModelVerdict(ModelMatch.SameGeometry, "Same model: same objects, parts and face counts");
    }

    /// <summary>
    /// For each object of <paramref name="objects"/>, the baseline object it stands for: the one of the
    /// same name, else (an object renamed in one of the files) the one in the same place among those
    /// left over; null for an object the baseline does not have.
    /// </summary>
    public static ProjectObject?[] Pair(IReadOnlyList<ProjectObject> objects, IReadOnlyList<ProjectObject> baseline)
    {
        var pairs = new ProjectObject?[objects.Count];
        var taken = new HashSet<ProjectObject>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < objects.Count; i++)
            if (baseline.FirstOrDefault(b => b.Name == objects[i].Name && !taken.Contains(b)) is { } same)
                taken.Add(pairs[i] = same);
        var left = baseline.Where(b => !taken.Contains(b)).GetEnumerator();
        for (var i = 0; i < objects.Count; i++)
            if (pairs[i] is null && left.MoveNext())
                pairs[i] = left.Current;
        return pairs;
    }

    static IEnumerable<string> Differences(BambuProject project, BambuProject baseline)
    {
        var pairs = Pair(project.Objects, baseline.Objects);
        var added = project.Objects.Where((_, i) => pairs[i] is null).Select(o => o.Name).ToList();
        var removed = baseline.Objects.Where(b => !pairs.Any(p => ReferenceEquals(p, b))).Select(o => o.Name).ToList();
        if (added.Count > 0) yield return $"more objects: {string.Join(", ", added)}";
        if (removed.Count > 0) yield return $"without {string.Join(", ", removed)}";

        for (var i = 0; i < project.Objects.Count; i++)
        {
            if (pairs[i] is not { } other) continue;
            var obj = project.Objects[i];
            var parts = obj.Parts.Select(p => p.Name).ToList();
            var baseParts = other.Parts.Select(p => p.Name).ToList();
            var more = parts.Except(baseParts).ToList();
            var fewer = baseParts.Except(parts).ToList();
            if (more.Count > 0) yield return $"{obj.Name}: {Count(more.Count, "more part")} ({string.Join(", ", more)})";
            if (fewer.Count > 0) yield return $"{obj.Name}: without {string.Join(", ", fewer)}";
            if (more.Count == 0 && fewer.Count == 0 && obj.FaceCount != other.FaceCount)
                yield return $"{obj.Name}: {obj.FaceCount.ToString("N0", CultureInfo.InvariantCulture)} faces, not {other.FaceCount.ToString("N0", CultureInfo.InvariantCulture)}";
        }
    }

    static string Count(int n, string what) => n == 1 ? $"1 {what}" : $"{n} {what}s";
}
