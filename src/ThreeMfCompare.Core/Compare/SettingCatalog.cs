using System.Globalization;

namespace ThreeMfCompare.Core.Compare;

/// <summary>The groups of the settings table, close to the tabs of Bambu Studio.</summary>
public enum SettingCategory
{
    Quality,
    Strength,
    Speed,
    Support,
    Others,
    Multimaterial,
    Filament,
    Cooling,
    Printer,
    Gcode,
    Objects,
    Project,
}

/// <summary>
/// What is known about Bambu Studio's settings: what the entries of each list stand for, which tab a
/// setting belongs to, its label there, and the few settings that matter most when two prints of one
/// model are held against each other.
/// </summary>
/// <remarks>
/// The list kinds were read off projects of Bambu Studio 2.08 (one and two filaments, a P2S with three
/// nozzle variants and an X1C with two). A setting not listed here is told by its length, where that
/// is unambiguous, else shown as the list it is.
/// </remarks>
public static class SettingCatalog
{
    /// <summary>Derived: the bed temperature of the plate type in use.</summary>
    public const string BedTemperature = "@bed_temperature";
    public const string BedTemperatureInitialLayer = "@bed_temperature_initial_layer";

    static HashSet<string> Keys(string spaceSeparated) => [.. spaceSeparated.Split(' ', StringSplitOptions.RemoveEmptyEntries)];

    /// <summary>One entry per filament.</summary>
    public static readonly HashSet<string> FilamentKeys = Keys(
        "activate_air_filtration additional_cooling_fan_speed additional_fan_full_speed_layer chamber_temperatures circle_compensation_speed " +
        "close_additional_fan_first_x_layers close_fan_the_first_x_layers complete_print_exhaust_fan_speed cool_plate_temp cool_plate_temp_initial_layer " +
        "cooling_perimeter_transition_distance cooling_slowdown_logic counter_coef_1 counter_coef_2 counter_coef_3 counter_limit_max counter_limit_min " +
        "default_filament_colour diameter_limit during_print_exhaust_fan_speed enable_overhang_bridge_fan enable_pressure_advance eng_plate_temp " +
        "eng_plate_temp_initial_layer fan_cooling_layer_time fan_max_speed fan_min_speed filament_adhesiveness_category filament_change_length " +
        "filament_change_length_nc filament_colour filament_colour_type filament_cost filament_density filament_dev_ams_drying_heat_distortion_temperature " +
        "filament_dev_chamber_drying_bed_temperature filament_dev_chamber_drying_time filament_dev_drying_cooling_temperature " +
        "filament_dev_drying_softening_temperature filament_diameter filament_end_gcode filament_extruder_compatibility filament_ids filament_is_mixed " +
        "filament_is_support filament_map filament_metal_stickiness filament_minimal_purge_on_wipe_tower filament_mixed_components filament_mixed_gradient " +
        "filament_mixed_gradient_curve filament_mixed_gradient_per_part filament_mixed_gradient_range filament_mixed_sublayer_ratios filament_multi_colour " +
        "filament_nozzle_map filament_prime_volume filament_prime_volume_nc filament_printable filament_scarf_gap filament_scarf_height filament_scarf_length " +
        "filament_scarf_seam_type filament_settings_id filament_shrink filament_soluble filament_start_gcode filament_tower_interface_pre_extrusion_dist " +
        "filament_tower_interface_pre_extrusion_length filament_tower_interface_print_temp filament_tower_interface_purge_volume filament_tower_ironing_area " +
        "filament_type filament_velocity_adaptation_factor filament_vendor filament_volume_map first_x_layer_fan_speed first_x_layer_part_fan_speed " +
        "full_fan_speed_layer hole_coef_1 hole_coef_2 hole_coef_3 hole_limit_max hole_limit_min hot_plate_temp hot_plate_temp_initial_layer " +
        "impact_strength_z ironing_fan_speed no_slow_down_for_cooling_on_outwalls nozzle_temperature_range_high nozzle_temperature_range_low " +
        "overhang_fan_speed overhang_fan_threshold overhang_threshold_participating_cooling pre_start_fan_time pressure_advance reduce_fan_stop_start_freq " +
        "required_nozzle_HRC slow_down_for_layer_cooling slow_down_layer_time supertack_plate_temp supertack_plate_temp_initial_layer " +
        "temperature_vitrification textured_plate_temp textured_plate_temp_initial_layer textured_cool_plate_temp textured_cool_plate_temp_initial_layer " +
        $"{BedTemperature} {BedTemperatureInitialLayer}");

    /// <summary>One entry per filament and nozzle variant, owned by filament_self_index and named by filament_extruder_variant.</summary>
    public static readonly HashSet<string> FilamentVariantKeys = Keys(
        "filament_adaptive_volumetric_speed filament_bridge_speed filament_cooling_before_tower filament_deretraction_speed filament_enable_overhang_speed " +
        "filament_extruder_variant filament_flow_ratio filament_flush_temp filament_flush_temp_fast filament_flush_volumetric_speed " +
        "filament_long_retractions_when_cut filament_max_volumetric_speed filament_overhang_1_4_speed filament_overhang_2_4_speed " +
        "filament_overhang_3_4_speed filament_overhang_4_4_speed filament_overhang_totally_speed filament_pre_cooling_temperature " +
        "filament_pre_cooling_temperature_nc filament_preheat_temperature_delta filament_ramming_travel_time filament_ramming_travel_time_nc " +
        "filament_ramming_volumetric_speed filament_ramming_volumetric_speed_nc filament_retract_before_wipe filament_retract_length_nc " +
        "filament_retract_restart_extra filament_retract_when_changing_layer filament_retraction_distances_when_cut filament_retraction_length " +
        "filament_retraction_minimum_travel filament_retraction_speed filament_self_index filament_wipe filament_wipe_distance filament_z_hop " +
        "filament_z_hop_types long_retractions_when_ec nozzle_temperature nozzle_temperature_initial_layer override_process_overhang_speed " +
        "retraction_distances_when_ec slow_down_min_speed volumetric_speed_coefficients");

    /// <summary>Printer settings with one entry per nozzle variant, named by printer_extruder_variant.</summary>
    public static readonly HashSet<string> PrinterVariantKeys = Keys(
        "deretraction_speed hotend_cooling_rate hotend_heating_rate long_retractions_when_cut nozzle_flush_dataset nozzle_type nozzle_volume " +
        "printer_extruder_id printer_extruder_variant retract_before_wipe retract_length_toolchange retract_lift_above retract_lift_below " +
        "retract_restart_extra retract_restart_extra_toolchange retract_when_changing_layer retraction_distances_when_cut retraction_length " +
        "retraction_minimum_travel retraction_speed wipe wipe_distance z_hop z_hop_types");

    /// <summary>Process settings with one entry per nozzle variant, named by print_extruder_variant.</summary>
    public static readonly HashSet<string> PrintVariantKeys = Keys(
        "bridge_speed default_acceleration enable_height_slowdown enable_overhang_speed gap_infill_speed initial_layer_acceleration " +
        "initial_layer_infill_speed initial_layer_speed initial_layer_travel_acceleration inner_wall_acceleration inner_wall_speed " +
        "internal_solid_infill_speed outer_wall_acceleration outer_wall_speed overhang_1_4_speed overhang_2_4_speed overhang_3_4_speed " +
        "overhang_4_4_speed overhang_totally_speed print_extruder_id print_extruder_variant slowdown_end_acc slowdown_end_height slowdown_end_speed " +
        "slowdown_start_acc slowdown_start_height slowdown_start_speed small_perimeter_speed small_perimeter_threshold sparse_infill_acceleration " +
        "sparse_infill_speed support_interface_speed support_speed top_solid_infill_flow_ratio top_surface_acceleration top_surface_speed " +
        "travel_acceleration travel_short_distance_acceleration travel_speed travel_speed_z vertical_shell_speed");

    /// <summary>Normal and silent mode for each printer variant.</summary>
    public static readonly HashSet<string> MachineLimitKeys = Keys(
        "machine_max_acceleration_e machine_max_acceleration_extruding machine_max_acceleration_retracting machine_max_acceleration_travel " +
        "machine_max_acceleration_x machine_max_acceleration_y machine_max_acceleration_z machine_max_jerk_e machine_max_jerk_x machine_max_jerk_y " +
        "machine_max_jerk_z machine_max_speed_e machine_max_speed_x machine_max_speed_y machine_max_speed_z");

    /// <summary>One entry per extruder (two on an H2D).</summary>
    public static readonly HashSet<string> ExtruderKeys = Keys(
        "default_nozzle_volume_type extruder_colour extruder_max_nozzle_count extruder_offset extruder_type extruder_variant_list flush_multiplier " +
        "flush_multiplier_fast grab_length machine_min_extruding_rate machine_min_travel_rate max_layer_height min_layer_height nozzle_diameter " +
        "nozzle_volume_type physical_extruder_map");

    /// <summary>Lists that are none of the above (points, matrices, names, one entry per plate), even where their length happens to match one.</summary>
    public static readonly HashSet<string> PlainListKeys = Keys(
        "bed_exclude_area bed_heat_soak_area different_settings_to_system extruder_ams_count extruder_printable_area extruder_printable_height " +
        "filament_dev_ams_drying_ams_limitations filament_dev_ams_drying_temperature filament_dev_ams_drying_time flush_volumes_matrix " +
        "flush_volumes_vector head_wrap_detect_zone inherits_group post_process print_compatible_printers printable_area start_end_points " +
        "upward_compatible_machine wrapping_exclude_area default_filament_profile first_layer_print_sequence other_layers_print_sequence " +
        "thumbnail_size wipe_tower_x wipe_tower_y extruder_nozzle_stats extruder_nozzle_stats_new compatible_printers compatible_prints");

    /// <summary>The settings that matter most, in the order they are shown at the top.</summary>
    public static readonly IReadOnlyList<string> KeySettings =
    [
        "printer_settings_id", "nozzle_diameter", "curr_bed_type", "print_settings_id", "filament_settings_id", "filament_type",
        "layer_height", "initial_layer_print_height", "line_width", "wall_loops", "wall_generator", "seam_position",
        "top_shell_layers", "bottom_shell_layers", "top_surface_pattern", "sparse_infill_density", "sparse_infill_pattern",
        "enable_support", "support_type", "support_threshold_angle", "brim_type", "enable_prime_tower",
        "outer_wall_speed", "inner_wall_speed", "sparse_infill_speed", "internal_solid_infill_speed", "top_surface_speed", "travel_speed",
        "default_acceleration", "nozzle_temperature", "nozzle_temperature_initial_layer", BedTemperature, BedTemperatureInitialLayer,
        "filament_flow_ratio", "filament_max_volumetric_speed", "fan_max_speed", "ironing_type", "print_sequence", "spiral_mode",
        "fuzzy_skin", "timelapse_type",
    ];

    static readonly Dictionary<string, int> KeyOrder = KeySettings.Select((key, i) => (key, i)).ToDictionary(p => p.key, p => p.i);

    /// <summary>Position among the key settings, or -1.</summary>
    public static int KeyRank(string key) => KeyOrder.GetValueOrDefault(key, -1);

    /// <summary>
    /// Bookkeeping that differs between any two saves and says nothing about the print: the
    /// lists that only name the variants, and such.
    /// </summary>
    public static readonly HashSet<string> Bookkeeping = Keys(
        "different_settings_to_system inherits_group print_extruder_id print_extruder_variant printer_extruder_id printer_extruder_variant " +
        "filament_extruder_variant filament_self_index extruder_variant_list name from version upward_compatible_machine print_compatible_printers " +
        "best_object_pos");

    public static string CategoryName(SettingCategory category) => category switch
    {
        SettingCategory.Multimaterial => "Prime tower & flush",
        SettingCategory.Gcode => "G-code templates",
        SettingCategory.Objects => "Objects & plates",
        SettingCategory.Project => "Profiles & project",
        _ => category.ToString(),
    };

    public static SettingCategory Category(string key)
    {
        var k = key.TrimStart('@');
        if (k.EndsWith("_gcode", StringComparison.Ordinal) || k is "post_process") return SettingCategory.Gcode;
        if (k.EndsWith("_settings_id", StringComparison.Ordinal) || k.StartsWith("default_", StringComparison.Ordinal) && k.EndsWith("_profile", StringComparison.Ordinal)
            || Bookkeeping.Contains(k) || k is "inherits" or "filament_ids" or "process_notes" or "filament_notes" or "printer_notes" or "compatible_printers" or "compatible_prints")
            return SettingCategory.Project;

        if (k.EndsWith("_plate_temp", StringComparison.Ordinal) || k.EndsWith("_plate_temp_initial_layer", StringComparison.Ordinal)
            || k.StartsWith("bed_temperature", StringComparison.Ordinal) && k != "bed_temperature_formula")
            return SettingCategory.Filament;
        if (IsCooling(k)) return SettingCategory.Cooling;
        if (k.StartsWith("filament_", StringComparison.Ordinal) && k is not ("filament_map" or "filament_map_mode"))
            return SettingCategory.Filament;
        if (k.StartsWith("nozzle_temperature", StringComparison.Ordinal) || k.StartsWith("hole_", StringComparison.Ordinal) || k.StartsWith("counter_", StringComparison.Ordinal)
            || k is "temperature_vitrification" or "required_nozzle_HRC" or "chamber_temperatures" or "pressure_advance" or "enable_pressure_advance"
                or "diameter_limit" or "circle_compensation_speed" or "impact_strength_z" or "volumetric_speed_coefficients" or "long_retractions_when_ec"
                or "retraction_distances_when_ec" or "override_process_overhang_speed" or "default_filament_colour")
            return SettingCategory.Filament;

        if (k.StartsWith("support_", StringComparison.Ordinal) && k is not ("support_chamber_temp_control" or "support_air_filtration" or "support_object_skip_flush")
            || k.StartsWith("raft_", StringComparison.Ordinal) || k.StartsWith("tree_support", StringComparison.Ordinal)
            || k is "enable_support" or "bridge_no_support" or "max_bridge_length" or "independent_support_layer_height" or "enforce_support_layers"
                or "top_z_overrides_xy_distance" or "enable_support_ironing")
            return SettingCategory.Support;

        if (k.StartsWith("prime_tower", StringComparison.Ordinal) || k.StartsWith("wipe_tower", StringComparison.Ordinal) || k.StartsWith("flush_", StringComparison.Ordinal)
            || k.StartsWith("mmu_", StringComparison.Ordinal) || k.StartsWith("interlocking_", StringComparison.Ordinal) || k.StartsWith("prime_volume", StringComparison.Ordinal)
            || k.EndsWith("_filament", StringComparison.Ordinal) || k.EndsWith("_color_penetration_layers", StringComparison.Ordinal)
            || k is "enable_prime_tower" or "ooze_prevention" or "standby_temperature_delta" or "enable_tower_interface_features" or "support_object_skip_flush"
                or "filament_map" or "filament_map_mode" or "enable_mixed_color_sublayer" or "single_extruder_multi_material_priming" or "enable_filament_dynamic_map"
                or "group_algo_with_time")
            return SettingCategory.Multimaterial;

        if (IsPrinter(k)) return SettingCategory.Printer;

        if (k.StartsWith("ironing_", StringComparison.Ordinal) || k.StartsWith("seam_", StringComparison.Ordinal) || k.StartsWith("scarf_", StringComparison.Ordinal)
            || k.StartsWith("wall_transition", StringComparison.Ordinal) || k.StartsWith("precise_", StringComparison.Ordinal) || k.StartsWith("only_one_wall", StringComparison.Ordinal)
            || k.EndsWith("line_width", StringComparison.Ordinal) || k.EndsWith("_compensation", StringComparison.Ordinal) || k.EndsWith("flow_ratio", StringComparison.Ordinal)
            || k is "layer_height" or "initial_layer_print_height" or "wall_generator" or "wall_distribution_count" or "min_bead_width" or "min_feature_size"
                or "resolution" or "slice_closing_radius" or "enable_arc_fitting" or "bridge_flow" or "thick_bridges" or "detect_overhang_wall"
                or "reduce_crossing_wall" or "max_travel_detour_distance" or "wall_sequence" or "is_infill_first" or "print_in_clockwise" or "wall_direction"
                or "bridge_angle" or "counterbore_hole_bridging" or "enable_circle_compensation" or "circle_compensation_manual_offset"
                or "apply_scarf_seam_on_circles" or "has_scarf_joint_seam" or "override_filament_scarf_seam_setting" or "avoid_crossing_wall_includes_support"
                or "monotonic_travel_into_wall" or "filter_out_gap_fill" or "interface_shells" or "top_one_wall_type" or "smooth_speed_discontinuity_area"
                or "smooth_coefficient" or "z_direction_outwall_speed_continuous")
            return SettingCategory.Quality;

        if (k.EndsWith("_speed", StringComparison.Ordinal) || k.EndsWith("_acceleration", StringComparison.Ordinal) || k.EndsWith("_jerk", StringComparison.Ordinal)
            || k.StartsWith("accel_", StringComparison.Ordinal) || k.StartsWith("slowdown_", StringComparison.Ordinal) || k.StartsWith("overhang_", StringComparison.Ordinal)
            || k is "enable_overhang_speed" or "enable_height_slowdown" or "small_perimeter_threshold" or "travel_speed_z" or "role_base_wipe_speed")
            return SettingCategory.Speed;

        if (k.Contains("infill", StringComparison.Ordinal) || k.Contains("shell", StringComparison.Ordinal) || k.StartsWith("skin_", StringComparison.Ordinal)
            || k.StartsWith("skeleton_", StringComparison.Ordinal) || k.StartsWith("locked_", StringComparison.Ordinal) || k.StartsWith("top_surface", StringComparison.Ordinal)
            || k.StartsWith("bottom_surface", StringComparison.Ordinal)
            || k is "wall_loops" or "detect_thin_wall" or "embedding_wall_into_infill" or "alternate_extra_wall" or "fill_multiline" or "symmetric_infill_y_axis"
                or "top_area_threshold" or "align_infill_direction_to_model")
            return SettingCategory.Strength;

        return SettingCategory.Others;
    }

    static bool IsCooling(string k) =>
        k.StartsWith("fan_", StringComparison.Ordinal) || k.Contains("_fan_", StringComparison.Ordinal) || k.StartsWith("close_fan", StringComparison.Ordinal)
        || k.StartsWith("cooling_", StringComparison.Ordinal) || k.StartsWith("slow_down_", StringComparison.Ordinal) || k.EndsWith("_exhaust_fan_speed", StringComparison.Ordinal)
        || k is "full_fan_speed_layer" or "reduce_fan_stop_start_freq" or "enable_overhang_bridge_fan" or "no_slow_down_for_cooling_on_outwalls"
            or "overhang_threshold_participating_cooling" or "activate_air_filtration" or "additional_cooling_fan_speed" or "overhang_fan_speed"
            or "overhang_fan_threshold" or "pre_start_fan_time";

    static bool IsPrinter(string k) =>
        k.StartsWith("machine_", StringComparison.Ordinal) || k.StartsWith("printer_", StringComparison.Ordinal) || k.StartsWith("printable_", StringComparison.Ordinal)
        || k.StartsWith("bed_", StringComparison.Ordinal) || k.StartsWith("extruder_", StringComparison.Ordinal) || k.StartsWith("nozzle_", StringComparison.Ordinal)
        || k.StartsWith("retract", StringComparison.Ordinal) || k.StartsWith("z_hop", StringComparison.Ordinal) || k.StartsWith("thumbnail", StringComparison.Ordinal)
        || k.StartsWith("printhost_", StringComparison.Ordinal) || k.StartsWith("ams_", StringComparison.Ordinal) || k.StartsWith("hotend_", StringComparison.Ordinal)
        || k.StartsWith("wrapping_", StringComparison.Ordinal) || k.StartsWith("head_wrap", StringComparison.Ordinal)
        || k is "deretraction_speed" or "wipe" or "wipe_distance" or "travel_slope" or "auxiliary_fan" or "support_chamber_temp_control" or "support_air_filtration"
            or "scan_first_layer" or "use_relative_e_distances" or "use_firmware_retraction" or "gcode_flavor" or "silent_mode" or "host_type"
            or "single_extruder_multi_material" or "grab_length" or "fan_direction" or "cooling_filter_enabled" or "physical_extruder_map" or "master_extruder_id"
            or "has_filament_switcher" or "default_ams_type" or "long_retractions_when_cut" or "enable_long_retraction_when_cut" or "max_layer_height"
            or "min_layer_height" or "enable_pre_heating" or "enable_wrapping_detection" or "default_nozzle_volume_type" or "upward_compatible_machine"
            or "bed_temperature_formula" or "time_cost" or "z_offset" or "curr_bed_type";

    /// <summary>The label Bambu Studio shows for a setting, or one made from its key.</summary>
    public static string Label(string key)
    {
        if (Labels.TryGetValue(key, out var label)) return label;
        var words = key.TrimStart('@').Split('_', StringSplitOptions.RemoveEmptyEntries).Select(w => w switch
        {
            "gcode" => "G-code",
            "id" => "ID",
            "ids" => "IDs",
            "xy" => "XY",
            "z" => "Z",
            "x" => "X",
            "y" => "Y",
            "e" => "E",
            "nc" => "(no cutter)",
            "ams" => "AMS",
            "hrc" => "HRC",
            "HRC" => "HRC",
            "pa" => "PA",
            "mmu" => "MMU",
            "acc" => "acceleration",
            "accel" => "acceleration",
            "temp" => "temperature",
            "num" => "number",
            "dist" => "distance",
            _ => w,
        }).ToArray();
        if (words.Length == 0) return key;
        words[0] = char.ToUpper(words[0][0], CultureInfo.InvariantCulture) + words[0][1..];
        return string.Join(' ', words);
    }

    static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        [BedTemperature] = "Bed temperature (plate in use)",
        [BedTemperatureInitialLayer] = "Bed temperature, initial layer (plate in use)",
        ["printer_settings_id"] = "Printer preset",
        ["print_settings_id"] = "Process preset",
        ["filament_settings_id"] = "Filament preset",
        ["curr_bed_type"] = "Plate type",
        ["initial_layer_print_height"] = "Initial layer height",
        ["line_width"] = "Default line width",
        ["wall_loops"] = "Wall loops",
        ["wall_sequence"] = "Walls printing order",
        ["is_infill_first"] = "Print infill first",
        ["seam_gap"] = "Seam gap",
        ["seam_slope_type"] = "Scarf joint seam",
        ["seam_slope_conditional"] = "Conditional scarf joint",
        ["seam_slope_start_height"] = "Scarf start height",
        ["seam_slope_entire_loop"] = "Scarf around entire wall",
        ["seam_slope_min_length"] = "Scarf length",
        ["seam_slope_steps"] = "Scarf steps",
        ["seam_slope_inner_walls"] = "Scarf joint for inner walls",
        ["slice_closing_radius"] = "Slice gap closing radius",
        ["enable_arc_fitting"] = "Arc fitting",
        ["xy_hole_compensation"] = "X-Y hole compensation",
        ["xy_contour_compensation"] = "X-Y contour compensation",
        ["elefant_foot_compensation"] = "Elephant foot compensation",
        ["precise_outer_wall"] = "Precise wall",
        ["bridge_flow"] = "Bridge flow ratio",
        ["thick_bridges"] = "Thick bridges",
        ["only_one_wall_top"] = "Only one wall on top surfaces",
        ["only_one_wall_first_layer"] = "Only one wall on first layer",
        ["detect_overhang_wall"] = "Detect overhang wall",
        ["reduce_crossing_wall"] = "Avoid crossing walls",
        ["max_travel_detour_distance"] = "Avoid crossing walls: max detour length",
        ["detect_thin_wall"] = "Detect thin wall",
        ["top_shell_layers"] = "Top shell layers",
        ["top_shell_thickness"] = "Top shell thickness",
        ["bottom_shell_layers"] = "Bottom shell layers",
        ["bottom_shell_thickness"] = "Bottom shell thickness",
        ["sparse_infill_density"] = "Sparse infill density",
        ["sparse_infill_pattern"] = "Sparse infill pattern",
        ["infill_wall_overlap"] = "Infill/wall overlap",
        ["minimum_sparse_infill_area"] = "Minimum sparse infill threshold",
        ["ensure_vertical_shell_thickness"] = "Ensure vertical shell thickness",
        ["initial_layer_speed"] = "Initial layer speed",
        ["initial_layer_infill_speed"] = "Initial layer infill speed",
        ["outer_wall_speed"] = "Outer wall speed",
        ["inner_wall_speed"] = "Inner wall speed",
        ["sparse_infill_speed"] = "Sparse infill speed",
        ["internal_solid_infill_speed"] = "Internal solid infill speed",
        ["top_surface_speed"] = "Top surface speed",
        ["gap_infill_speed"] = "Gap infill speed",
        ["travel_speed"] = "Travel speed",
        ["enable_overhang_speed"] = "Slow down for overhang",
        ["overhang_1_4_speed"] = "Overhang speed 10–25 %",
        ["overhang_2_4_speed"] = "Overhang speed 25–50 %",
        ["overhang_3_4_speed"] = "Overhang speed 50–75 %",
        ["overhang_4_4_speed"] = "Overhang speed 75–100 %",
        ["overhang_totally_speed"] = "Overhang speed 100 %",
        ["default_acceleration"] = "Normal printing acceleration",
        ["enable_support"] = "Enable support",
        ["support_type"] = "Support type",
        ["support_style"] = "Support style",
        ["support_threshold_angle"] = "Support threshold angle",
        ["support_on_build_plate_only"] = "Support on build plate only",
        ["support_top_z_distance"] = "Support top Z distance",
        ["support_bottom_z_distance"] = "Support bottom Z distance",
        ["support_object_xy_distance"] = "Support/object XY distance",
        ["support_filament"] = "Support/raft base filament",
        ["support_interface_filament"] = "Support/raft interface filament",
        ["brim_type"] = "Brim type",
        ["brim_width"] = "Brim width",
        ["brim_object_gap"] = "Brim-object gap",
        ["skirt_loops"] = "Skirt loops",
        ["enable_prime_tower"] = "Enable prime tower",
        ["print_sequence"] = "Print sequence",
        ["spiral_mode"] = "Spiral vase",
        ["timelapse_type"] = "Timelapse",
        ["fuzzy_skin"] = "Fuzzy skin",
        ["nozzle_temperature"] = "Nozzle temperature",
        ["nozzle_temperature_initial_layer"] = "Nozzle temperature, initial layer",
        ["cool_plate_temp"] = "Bed: Cool Plate",
        ["cool_plate_temp_initial_layer"] = "Bed: Cool Plate, initial layer",
        ["eng_plate_temp"] = "Bed: Engineering Plate",
        ["eng_plate_temp_initial_layer"] = "Bed: Engineering Plate, initial layer",
        ["hot_plate_temp"] = "Bed: Smooth PEI / High Temp Plate",
        ["hot_plate_temp_initial_layer"] = "Bed: Smooth PEI / High Temp Plate, initial layer",
        ["textured_plate_temp"] = "Bed: Textured PEI Plate",
        ["textured_plate_temp_initial_layer"] = "Bed: Textured PEI Plate, initial layer",
        ["supertack_plate_temp"] = "Bed: Cool Plate SuperTack",
        ["supertack_plate_temp_initial_layer"] = "Bed: Cool Plate SuperTack, initial layer",
        ["filament_flow_ratio"] = "Flow ratio",
        ["filament_max_volumetric_speed"] = "Max volumetric speed",
        ["filament_type"] = "Filament type",
        ["filament_vendor"] = "Filament vendor",
        ["filament_colour"] = "Filament colour",
        ["temperature_vitrification"] = "Softening temperature",
        ["fan_min_speed"] = "Part cooling fan, min speed",
        ["fan_max_speed"] = "Part cooling fan, max speed",
        ["fan_cooling_layer_time"] = "Fan at max speed below layer time",
        ["slow_down_layer_time"] = "Slow printing down below layer time",
        ["slow_down_min_speed"] = "Min print speed",
        ["close_fan_the_first_x_layers"] = "No cooling for the first layers",
        ["full_fan_speed_layer"] = "Full fan speed at layer",
        ["additional_cooling_fan_speed"] = "Auxiliary part cooling fan",
        ["reduce_fan_stop_start_freq"] = "Keep fan always on",
        ["printable_height"] = "Printable height",
        ["retraction_length"] = "Retraction length",
        ["retraction_speed"] = "Retraction speed",
        ["deretraction_speed"] = "Deretraction speed",
        ["z_hop"] = "Z hop",
        ["z_hop_types"] = "Z hop type",
        ["retract_when_changing_layer"] = "Retract on layer change",
        ["retraction_minimum_travel"] = "Retraction: minimum travel",
        ["machine_start_gcode"] = "Machine start G-code",
        ["machine_end_gcode"] = "Machine end G-code",
        ["change_filament_gcode"] = "Change filament G-code",
        ["layer_change_gcode"] = "Layer change G-code",
        ["before_layer_change_gcode"] = "Before layer change G-code",
        ["time_lapse_gcode"] = "Timelapse G-code",
        ["filament_start_gcode"] = "Filament start G-code",
        ["filament_end_gcode"] = "Filament end G-code",
        ["different_settings_to_system"] = "Changed from system presets",
        ["inherits_group"] = "Based on system presets",
    };
}
