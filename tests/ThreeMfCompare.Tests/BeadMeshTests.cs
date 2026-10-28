using ThreeMfCompare.Core.Gcode;
using ThreeMfCompare.Core.Visual;

namespace ThreeMfCompare.Tests;

public class BeadMeshTests
{
    // Layer 1: an outer wall of three segments, gently bent, then a sharp turn; then sparse infill.
    // Layer 2: one inner wall segment.
    const string Gcode = """
        ; CONFIG_BLOCK_START
        ; line_width = 0.4
        ; layer_height = 0.2
        ; CONFIG_BLOCK_END
        M83
        ; CHANGE_LAYER
        ; Z_HEIGHT: 0.2
        ; LAYER_HEIGHT: 0.2
        ; FEATURE: Outer wall
        G1 X0 Y0 Z0.2 F600
        G1 X10 Y0 E1
        G1 X20 Y1 E1
        G1 X30 Y1 E1
        G1 X30 Y-9 E1
        ; FEATURE: Sparse infill
        G1 X40 Y-9 E1
        ; CHANGE_LAYER
        ; Z_HEIGHT: 0.4
        ; LAYER_HEIGHT: 0.2
        ; FEATURE: Inner wall
        G1 X40 Y-9 Z0.4
        G1 X50 Y-9 E1
        """;

    static readonly bool[] All = Enumerable.Repeat(true, FeatureTypes.Count).ToArray();

    [Fact]
    public void Segments_that_run_on_share_their_rings()
    {
        var toolpath = GcodeParser.Parse(new StringReader(Gcode));
        Assert.Equal(6, toolpath.Segments.Length);
        var mesh = BeadMesh.Build(toolpath);
        // The bent wall of three, the wall after the sharp turn, the infill, the inner wall.
        Assert.Equal([0, 3, 4, 5, 6], mesh.BeadStarts);
        Assert.Equal(4 * 4 + 2 + 3 * (4 * 2 + 2), mesh.Positions.Length);
        Assert.All(mesh.Indices, i => Assert.InRange(i, 0, mesh.Positions.Length - 1));
    }

    [Fact]
    public void Shows_beads_by_layer_and_line_type()
    {
        var toolpath = GcodeParser.Parse(new StringReader(Gcode));
        var mesh = BeadMesh.Build(toolpath);
        Assert.Equal(6, mesh.IndicesFor(toolpath, 0, 1, All).Segments);
        Assert.Equal(1, mesh.IndicesFor(toolpath, 1, 1, All).Segments);
        var noInfill = (bool[])All.Clone();
        noInfill[(int)FeatureType.SparseInfill] = false;
        var (indices, segments) = mesh.IndicesFor(toolpath, 0, 0, noInfill);
        Assert.Equal(4, segments);
        Assert.Equal(24 * 4 + 24 * 2, indices.Length);
    }
}
