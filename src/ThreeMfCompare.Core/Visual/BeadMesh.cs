using System.Numerics;
using ThreeMfCompare.Core.Gcode;

namespace ThreeMfCompare.Core.Visual;

/// <summary>
/// Every line of a toolpath as a bead, built once: a tube with a diamond cross-section of the line's
/// width and height. Segments that run on from one another (same layer, line type and filament, no
/// gap, no sharp turn) make one bead and share the ring of four vertices where they meet, mitred so the
/// bead keeps its width round a bend; each bead comes to a point half a width beyond its ends, which
/// closes the corners between one bead and the next. What is shown is chosen by the index buffer alone:
/// a bead lies in one layer and is of one line type, so it is shown whole or not at all.
/// </summary>
public sealed class BeadMesh
{
    /// <summary>A turn sharper than 60° ends one bead and starts the next, whose tips then close the corner.</summary>
    const float SharpTurnCos = 0.5f;

    public required Vector3[] Positions { get; init; }
    public required Vector3[] Normals { get; init; }

    /// <summary>Per vertex, the segment whose colour it takes.</summary>
    public required int[] VertexSegments { get; init; }

    /// <summary>The triangles of all beads, bead after bead in the order of the G-code.</summary>
    public required int[] Indices { get; init; }

    /// <summary>Per bead, its first segment; one more entry at the end: the number of segments.</summary>
    public required int[] BeadStarts { get; init; }

    /// <summary>Per bead, where its triangles start in <see cref="Indices"/>; one more entry at the end.</summary>
    public required int[] IndexStarts { get; init; }

    public int BeadCount => BeadStarts.Length - 1;

    public static BeadMesh Build(Toolpath toolpath)
    {
        var segments = toolpath.Segments;
        var starts = new List<int>(segments.Length / 4 + 1);
        for (var s = 0; s < segments.Length; s++)
            if (s == 0 || !RunsOn(segments[s - 1], segments[s])) starts.Add(s);
        starts.Add(segments.Length);
        var beads = starts.Count - 1;

        // Each bead: a ring at every point of it, two tips; four sides per segment, four faces per tip.
        var vertexStarts = new int[beads + 1];
        var indexStarts = new int[beads + 1];
        for (var b = 0; b < beads; b++)
        {
            var n = starts[b + 1] - starts[b];
            vertexStarts[b + 1] = vertexStarts[b] + 4 * (n + 1) + 2;
            indexStarts[b + 1] = indexStarts[b] + 24 * n + 24;
        }

        var mesh = new BeadMesh
        {
            Positions = new Vector3[vertexStarts[beads]],
            Normals = new Vector3[vertexStarts[beads]],
            VertexSegments = new int[vertexStarts[beads]],
            Indices = new int[indexStarts[beads]],
            BeadStarts = [.. starts],
            IndexStarts = indexStarts,
        };
        Parallel.For(0, beads, b => mesh.Fill(segments, starts[b], starts[b + 1] - starts[b], vertexStarts[b], indexStarts[b]));
        return mesh;
    }

    static Vector3 Direction(in ExtrusionSegment s) => Vector3.Normalize(s.End - s.Start);

    static bool RunsOn(in ExtrusionSegment previous, in ExtrusionSegment next) =>
        previous.Layer == next.Layer && previous.Feature == next.Feature && previous.Filament == next.Filament
        && Vector3.DistanceSquared(previous.End, next.Start) < 1e-8f
        && Vector3.Dot(Direction(previous), Direction(next)) > SharpTurnCos
        && MathF.Abs(previous.Width - next.Width) <= 0.25f * MathF.Max(previous.Width, next.Width)
        && MathF.Abs(previous.Height - next.Height) <= 0.25f * MathF.Max(previous.Height, next.Height);

    void Fill(ExtrusionSegment[] segments, int first, int n, int v0, int i0)
    {
        var last = first + n - 1;
        for (var k = 0; k <= n; k++)
        {
            // The ring at point k: where segment k starts, or where the last one ends.
            ref readonly var before = ref segments[Math.Max(first, first + k - 1)];
            ref readonly var after = ref segments[Math.Min(last, first + k)];
            var point = k < n ? after.Start : after.End;
            var along = Direction(after);
            var miter = 1f;
            if (k > 0 && k < n)
            {
                var sum = Direction(before) + along;
                if (sum.LengthSquared() > 1e-6f)
                {
                    var bisector = Vector3.Normalize(sum);
                    // Wider at the joint by 1 / cos(half the turn), so both segments keep their width there.
                    miter = 1 / MathF.Max(Vector3.Dot(bisector, along), SharpTurnCos);
                    along = bisector;
                }
            }
            var width = (before.Width + after.Width) / 2;
            var height = (before.Height + after.Height) / 2;
            var side = Vector3.Cross(along, Vector3.UnitZ);
            side = side.LengthSquared() > 1e-6f ? Vector3.Normalize(side) : Vector3.UnitX;
            var up = Vector3.Cross(side, along);

            var ring = v0 + 4 * k;
            var segment = first + Math.Min(k, n - 1);
            (Positions[ring], Normals[ring]) = (point + up * height / 2, up);
            (Positions[ring + 1], Normals[ring + 1]) = (point + side * width * miter / 2, side);
            (Positions[ring + 2], Normals[ring + 2]) = (point - up * height / 2, -up);
            (Positions[ring + 3], Normals[ring + 3]) = (point - side * width * miter / 2, -side);
            VertexSegments.AsSpan(ring, 4).Fill(segment);
        }

        // The tips, half a width beyond the ends.
        var startTip = v0 + 4 * (n + 1);
        var endTip = startTip + 1;
        var startAlong = Direction(segments[first]);
        var endAlong = Direction(segments[last]);
        (Positions[startTip], Normals[startTip]) = (segments[first].Start - startAlong * segments[first].Width / 2, -startAlong);
        (Positions[endTip], Normals[endTip]) = (segments[last].End + endAlong * segments[last].Width / 2, endAlong);
        (VertexSegments[startTip], VertexSegments[endTip]) = (first, last);

        var at = i0;
        for (var k = 0; k < n; k++)
            for (var m = 0; m < 4; m++)
            {
                // The side between corners m and m + 1 of rings k and k + 1, counter-clockwise from outside.
                int a = v0 + 4 * k + m, b = v0 + 4 * k + (m + 1) % 4, c = b + 4, d = a + 4;
                (Indices[at], Indices[at + 1], Indices[at + 2]) = (a, b, c);
                (Indices[at + 3], Indices[at + 4], Indices[at + 5]) = (a, c, d);
                at += 6;
            }
        for (var m = 0; m < 4; m++)
        {
            int a = v0 + m, b = v0 + (m + 1) % 4;
            int d = v0 + 4 * n + m, c = v0 + 4 * n + (m + 1) % 4;
            (Indices[at], Indices[at + 1], Indices[at + 2]) = (startTip, b, a);
            (Indices[at + 3], Indices[at + 4], Indices[at + 5]) = (endTip, d, c);
            at += 6;
        }
    }

    /// <summary>
    /// The triangles of the beads of layers <paramref name="layerLow"/> to <paramref name="layerHigh"/>
    /// (both included) whose line type is <paramref name="shown"/>, and how many segments they make.
    /// </summary>
    public (int[] Indices, int Segments) IndicesFor(Toolpath toolpath, int layerLow, int layerHigh, bool[] shown)
    {
        var layers = toolpath.Layers;
        if (layers.Length == 0 || layerHigh < layerLow || BeadCount == 0) return ([], 0);
        layerLow = Math.Clamp(layerLow, 0, layers.Length - 1);
        layerHigh = Math.Clamp(layerHigh, 0, layers.Length - 1);
        var firstSegment = layers[layerLow].FirstSegment;
        var endSegment = layers[layerHigh].FirstSegment + layers[layerHigh].SegmentCount;

        // Beads do not cross layers, so those of the layers are the ones starting inside them.
        var low = LowerBound(BeadStarts, firstSegment);
        var high = LowerBound(BeadStarts, endSegment);
        var segments = toolpath.Segments;
        bool Shows(int bead) => shown[(int)segments[BeadStarts[bead]].Feature];

        int count = 0, segmentCount = 0;
        for (var b = low; b < high; b++)
            if (Shows(b))
            {
                count += IndexStarts[b + 1] - IndexStarts[b];
                segmentCount += BeadStarts[b + 1] - BeadStarts[b];
            }

        var indices = new int[count];
        var at = 0;
        for (var b = low; b < high;)
        {
            if (!Shows(b))
            {
                b++;
                continue;
            }
            // Beads shown one after another are one stretch of the index buffer.
            var run = b;
            while (run < high && Shows(run)) run++;
            var length = IndexStarts[run] - IndexStarts[b];
            Array.Copy(Indices, IndexStarts[b], indices, at, length);
            at += length;
            b = run;
        }
        return (indices, segmentCount);
    }

    /// <summary>Per vertex, the colour of its segment.</summary>
    public Vector4[] Colours(Vector4[] segmentColours)
    {
        var colours = new Vector4[VertexSegments.Length];
        Parallel.For(0, colours.Length, v => colours[v] = segmentColours[VertexSegments[v]]);
        return colours;
    }

    /// <summary>The first index whose value is at least <paramref name="value"/>.</summary>
    static int LowerBound(int[] sorted, int value)
    {
        int low = 0, high = sorted.Length - 1;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (sorted[mid] >= value) high = mid;
            else low = mid + 1;
        }
        return low;
    }
}
