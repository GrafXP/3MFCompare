using ThreeMfCompare.Core.Compare;
using ThreeMfCompare.Core.Projects;

namespace ThreeMfCompare.Tests;

/// <summary>
/// The three projects in Example/: a hand on a P2S with one filament ("ohne Logo"), the same with a
/// two-colour logo and a user process preset ("mit Logo"), and the hand moved to an X1C with its
/// standard process ("ohne Logo_mp").
/// </summary>
public class ExampleProjectTests
{
    static readonly Lazy<BambuProject> Plain = new(() => BambuProject.Load(Examples.Path("Hand_ohne Logo.3mf")));
    static readonly Lazy<BambuProject> Logo = new(() => BambuProject.Load(Examples.Path("Hand_mit Logo.3mf")));
    static readonly Lazy<BambuProject> X1C = new(() => BambuProject.Load(Examples.Path("Hand_ohne Logo_mp.3mf")));

    [Fact]
    public void Reads_the_project()
    {
        var project = Plain.Value;
        Assert.Equal("Bambu Lab P2S 0.4 nozzle", project.Settings.Text("printer_settings_id"));
        Assert.Equal("Hand_Lupe v3", project.Title);
        Assert.Equal(1, project.Settings.FilamentCount);
        Assert.Equal(["Direct Drive Standard"], project.Settings.ActiveVariants);
        Assert.Single(project.Objects);
        Assert.Single(project.Plates);
        Assert.True(project.Thumbnails.ContainsKey(1));
        Assert.Single(project.MeshChecksums);
        Assert.NotNull(project.Objects[0].LayerHeightProfile);
    }

    [Fact]
    public void Reads_parts_and_their_filaments()
    {
        var hand = Logo.Value.Objects.Single();
        Assert.Equal(5, hand.Parts.Count);
        Assert.Equal("2", hand.Parts[1].Overrides["extruder"]);
        Assert.Equal(2, Logo.Value.Settings.FilamentCount);
    }

    [Fact]
    public void A_print_variant_list_resolves_to_the_nozzle_in_use()
    {
        // P2S: [200, 500, 500] for Standard, High Flow, E3D High Flow; X1C: [200, 350].
        var p2s = Plain.Value.Settings.Resolve("outer_wall_speed")!;
        var x1c = X1C.Value.Settings.Resolve("outer_wall_speed")!;
        Assert.Equal("200", p2s.Text);
        Assert.Equal(p2s.Canonical, x1c.Canonical);
        Assert.Contains("High Flow: 500", p2s.Detail);
    }

    [Fact]
    public void A_filament_variant_list_resolves_per_filament()
    {
        // Two filaments of three variants each: Generic PLA 12 mm³/s, PLA Basic 21 (Standard).
        var speed = Logo.Value.Settings.Resolve("filament_max_volumetric_speed")!;
        Assert.Equal(SettingShape.PerFilament, speed.Shape);
        Assert.Equal(["12", "21"], speed.Values);
    }

    [Fact]
    public void Lists_that_only_repeat_a_value_are_that_value()
    {
        // ["70"] for one filament, ["70", "70"] for two.
        Assert.Equal(Plain.Value.Settings.Resolve("additional_cooling_fan_speed")!.Canonical, Logo.Value.Settings.Resolve("additional_cooling_fan_speed")!.Canonical);
    }

    [Fact]
    public void Knows_what_the_user_changed()
    {
        var customized = Plain.Value.Settings.Customized;
        Assert.Equal("0.20mm Standard @BBL P2S", customized["layer_height"].SystemPreset);
        Assert.True(customized.ContainsKey("sparse_infill_pattern"));
        Assert.Equal("Einstellung für Handjob", Logo.Value.Settings.Customized["layer_height"].Preset);
    }

    [Fact]
    public void The_bed_temperature_is_that_of_the_plate_in_use()
    {
        Assert.Equal("Textured PEI Plate", Plain.Value.Settings.Text("curr_bed_type"));
        Assert.Equal(Plain.Value.Settings.Text("textured_plate_temp"), Plain.Value.Settings.Resolve(SettingCatalog.BedTemperature)!.Text);
    }

    [Fact]
    public void Compares_the_print_settings()
    {
        var rows = SettingsComparison.Compare([Plain.Value, X1C.Value], baseline: 0);
        bool Differs(string key) => rows.Single(r => r.Key == key).Differs;

        Assert.True(Differs("layer_height"));
        Assert.True(Differs("sparse_infill_density"));
        Assert.True(Differs("sparse_infill_pattern"));
        Assert.True(Differs("printer_settings_id"));
        Assert.False(Differs("inner_wall_speed"), "300 on the Standard nozzle of both printers");
        Assert.False(Differs("outer_wall_speed"));
        Assert.False(Differs("wall_loops"));
    }

    [Fact]
    public void Compares_the_objects()
    {
        var rows = SettingsComparison.Compare([Plain.Value, Logo.Value], baseline: 0);
        // The logo file renamed the object; its rows go by the baseline's name.
        const string hand = "object/Meshy_AI_Cupped_Hand_20261022_generate";
        Assert.True(rows.Single(r => r.Key == $"{hand}/parts").Differs);
        Assert.Equal("Part Körper10: filament", rows.Single(r => r.Key == $"{hand}/part/Körper10/extruder").Label);
        Assert.Contains(rows, r => r.Key == $"{hand}/part/Körper10/extruder" && r.Differs);
    }

    [Fact]
    public void Tells_the_same_model_from_another()
    {
        Assert.Equal(ModelMatch.SameGeometry, ModelComparison.Compare(X1C.Value, Plain.Value).Match);
        Assert.Equal(ModelMatch.Identical, ModelComparison.Compare(Plain.Value, Plain.Value).Match);
        var logo = ModelComparison.Compare(Logo.Value, Plain.Value);
        Assert.Equal(ModelMatch.Different, logo.Match);
        // The object was renamed in the logo file; it is still the same object, with the logo's four parts added.
        Assert.Equal("Hand_Lupe v8: 4 more parts (Körper10, Körper11, Körper12, Körper13)", logo.Summary);
    }
}

static class Examples
{
    /// <summary>The repository's Example folder, found by walking up from the test assembly.</summary>
    public static string Path(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = System.IO.Path.Combine(dir.FullName, "Example", name);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"Example/{name} not found above {AppContext.BaseDirectory}.");
    }
}
