namespace ThreeMfCompare.Core.Compare;

public enum LineChange { Same, Removed, Added }

/// <param name="Old">Line number in the old text (from 1), or 0 for an added line.</param>
/// <param name="New">Line number in the new text (from 1), or 0 for a removed line.</param>
public sealed record DiffLine(LineChange Change, string Text, int Old, int New);

/// <summary>A line-by-line diff of two texts (G-code templates are a few hundred lines): longest common subsequence, after the common start and end are taken off.</summary>
public static class LineDiff
{
    public static IReadOnlyList<DiffLine> Compare(string oldText, string newText)
    {
        var a = Lines(oldText);
        var b = Lines(newText);
        var start = 0;
        while (start < a.Length && start < b.Length && a[start] == b[start]) start++;
        var end = 0;
        while (end < a.Length - start && end < b.Length - start && a[^(end + 1)] == b[^(end + 1)]) end++;

        var result = new List<DiffLine>(a.Length + b.Length);
        for (var i = 0; i < start; i++) result.Add(new DiffLine(LineChange.Same, a[i], i + 1, i + 1));

        int n = a.Length - start - end, m = b.Length - start - end;
        // lengths[i, j]: longest common subsequence of a[start + i..] and b[start + j..] within the middle.
        var lengths = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
            for (var j = m - 1; j >= 0; j--)
                lengths[i, j] = a[start + i] == b[start + j] ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);

        int x = 0, y = 0;
        while (x < n || y < m)
        {
            if (x < n && y < m && a[start + x] == b[start + y])
            {
                result.Add(new DiffLine(LineChange.Same, a[start + x], start + x + 1, start + y + 1));
                x++;
                y++;
            }
            // Of a changed stretch, the old lines come first.
            else if (x < n && (y == m || lengths[x + 1, y] >= lengths[x, y + 1]))
            {
                result.Add(new DiffLine(LineChange.Removed, a[start + x], start + x + 1, 0));
                x++;
            }
            else
            {
                result.Add(new DiffLine(LineChange.Added, b[start + y], 0, start + y + 1));
                y++;
            }
        }

        for (var k = end; k > 0; k--)
            result.Add(new DiffLine(LineChange.Same, a[^k], a.Length - k + 1, b.Length - k + 1));
        return result;
    }

    static string[] Lines(string text) => text.Length == 0 ? [] : text.Replace("\r\n", "\n").Split('\n');
}
