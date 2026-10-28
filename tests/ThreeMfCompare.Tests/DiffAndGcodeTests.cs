using ThreeMfCompare.Core.Compare;
using ThreeMfCompare.Core.Gcode;

namespace ThreeMfCompare.Tests;

public class LineDiffTests
{
    [Fact]
    public void Finds_changed_lines()
    {
        var diff = LineDiff.Compare("a\nb\nc\nd", "a\nx\nc\nd\ne");
        Assert.Equal(
            [(LineChange.Same, "a"), (LineChange.Removed, "b"), (LineChange.Added, "x"), (LineChange.Same, "c"), (LineChange.Same, "d"), (LineChange.Added, "e")],
            diff.Select(d => (d.Change, d.Text)));
    }

    [Fact]
    public void Equal_texts_have_no_changes() =>
        Assert.All(LineDiff.Compare("G28\nG1 X10", "G28\nG1 X10"), d => Assert.Equal(LineChange.Same, d.Change));
}

public class GcodeParserTests
{
    const string Gcode = """
        ; HEADER_BLOCK_START
        ; model printing time: 1m 40s; total estimated time: 2m 0s
        ; total layer number: 2
        ; total filament length [mm] : 100.5,20.25
        ; HEADER_BLOCK_END
        ; CONFIG_BLOCK_START
        ; filament_diameter = 1.75
        ; line_width = 0.42
        ; layer_height = 0.2
        ; CONFIG_BLOCK_END
        G90
        M83
        G1 X0 Y0 E5 F600 ; purge in the start G-code: not part of the print
        ; CHANGE_LAYER
        ; Z_HEIGHT: 0.2
        ; LAYER_HEIGHT: 0.2
        ; FEATURE: Outer wall
        G1 Z0.2 F600
        G1 X10 Y0 E0.5 F600
        G2 X20 Y0 I5 J0 E0.8
        ; CHANGE_LAYER
        ; Z_HEIGHT: 0.4
        ; LAYER_HEIGHT: 0.2
        T1
        ; FEATURE: Sparse infill
        G1 Z0.4
        G1 X0 Y0 E1 F1200
        ; MACHINE_END_GCODE_START
        G1 X50 Y50 E2
        """;

    [Fact]
    public void Reads_layers_features_and_tools()
    {
        var toolpath = GcodeParser.Parse(new StringReader(Gcode));
        Assert.Equal(2, toolpath.Layers.Length);
        Assert.Equal(0.4f, toolpath.Layers[1].Z, 3);
        Assert.Equal(FeatureType.OuterWall, toolpath.Segments[0].Feature);
        Assert.True(toolpath.Layers[0].SegmentCount > 2, "the arc is split into pieces");
        var last = toolpath.Segments[^1];
        Assert.Equal(FeatureType.SparseInfill, last.Feature);
        Assert.Equal(1, last.Filament);
        Assert.Equal(1, toolpath.ToolChanges);
        Assert.Equal(100, toolpath.HeaderSeconds("model printing time"));
        Assert.Equal(120.75, toolpath.HeaderNumber("total filament length [mm]"));
        Assert.Equal(1, toolpath.LayerAt(0.3f));
        Assert.Equal(0, toolpath.LayerAt(0.2f));
    }

    [Fact]
    public void Layer_times_add_up_to_the_slicer_estimate()
    {
        var toolpath = GcodeParser.Parse(new StringReader(Gcode));
        Assert.Equal(100, toolpath.LayerSeconds().Sum(), 6);
    }
}
