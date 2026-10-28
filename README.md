# 3MF Compare

Compares Bambu Studio projects (`.3mf`), typically one model saved with different print
settings. Each file is held against a baseline (the first one, or whichever you pick).

1. **Files**: a card per project (thumbnail, printer, plate, process, filaments, objects), whether
   it is the same model as the baseline, and the key settings that differ at a glance.
2. **Settings**: every setting side by side, grouped like Bambu Studio's tabs, with differences in
   amber and settings changed from the system preset marked with a pen. Search, only differences,
   only changed from presets. G-code templates open as a line diff against the baseline.
   Objects, parts, plates, per-object/part overrides, height ranges and variable layer
   height profiles are compared as well.
3. **Slice results**: each project sliced by Bambu Studio's CLI *exactly as saved*, then time,
   filament (per filament), layers, height and time per line type side by side with deltas, plus a
   chart of layer time by height.
4. **G-code**: one 3D pane per project, cameras turned together, one height slider for all panes
   (each pane shows the layer it prints at that height, so different layer heights stay level).
   Colour by line type, filament, speed, volumetric flow, line width, layer height, layer time,
   fan or nozzle temperature, on one scale for all panes. Line types can be switched off.

## Run

```text
dotnet build 3MFCompare.slnx
src\ThreeMfCompare.App\bin\Debug\net10.0-windows\3MFCompare.exe [files.3mf …]
```

Drop projects (or a folder) on the window, or use Open (Ctrl+O). Ctrl+1…4 switch pages, F5 slices,
Ctrl+F searches the settings. In the G-code view: left drag turns, right or middle drag pans, the
wheel zooms, a double click frames the prints; on the slider the wheel or arrow keys move the top
handle, with Shift the bottom one, with Ctrl both.

Needs .NET 10 and, for slicing, Bambu Studio in `C:\Program Files\Bambu Studio` (or set
`THREEMFCOMPARE_BAMBU_DIR`; GcodeFEM's `GCODEFEM_BAMBU_DIR` is honoured too).

## How it compares

- **Lists resolve to what prints.** Bambu Studio stores many settings as lists: one entry per
  filament, per extruder, per nozzle variant of the printer ("Direct Drive Standard", "… High
  Flow", …) or per filament *and* variant. Only one variant prints, so a P2S's `outer_wall_speed`
  `[200, 500, 500]` and an X1C's `[200, 350]` are both 200 on a standard nozzle and equal here.
  Hover a value to see all variants. Lists whose entries all agree count as that one value
  (`["70"]` for one filament equals `["70", "70"]` for two). The list kinds are in
  `SettingCatalog`, read off Bambu Studio 2.08 projects; unknown keys are classified by length.
- **"Changed from presets"** comes from the project's `different_settings_to_system` and
  `inherits_group`: the pen says which system preset the value was changed from.
- **Same model?** Objects are paired by name, else by position (a renamed object still meets
  itself), then parts and face counts are compared. Byte-identical mesh files are reported as such;
  Bambu Studio re-saves vertices with float drift, so equal face counts with different checksums
  still count as the same model.
- **Slicing** copies the project into `%LOCALAPPDATA%\3MFCompare\cache\slices\<hash>` and runs
  `bambu-studio.exe --slice 0 --outputdir <dir> project.3mf`: all plates, the project's own
  presets, nothing arranged. The cache key is the project's bytes plus the Bambu Studio version, so a
  project is sliced once. About 15 s per project for the examples.
- **Layer times** are the G-code's feed-rate times per layer (no acceleration), scaled so they add
  up to the slicer's "model printing time". Good for comparing where time goes, not exact.

## Command line

For scripted checks (this is how the pages were verified):

```text
3MFCompare a.3mf b.3mf [--baseline n] [--page files|settings|slices|gcode]
           [--category name] [--search text] [--all] [--customized]
           [--slice] [--plate n] [--colour feature|filament|speed|flow|width|layerheight|layertime|fan|temperature]
           [--z 12.4 | --z 10-12.4] [--view iso|top|front|left|right] [--lines] [--hide "sparse infill,inner wall"]
           [--turn degrees] [--diff machine_start_gcode] [--screenshot out.png] [--size 1600x1000] [--wait seconds]
```

With `--screenshot` the app saves the window and exits (exit code 1 if a step failed). `--turn`
turns only the first pane's camera, to check that the others follow.

## Layout

- `src/ThreeMfCompare.Core`: reading projects (`Projects/`), the comparison (`Compare/`), the
  Bambu CLI (`Slicing/`), the G-code parser (`Gcode/`) and geometry and colours (`Visual/`). The
  parser, the toolpath geometry, the layer slider and the theme are adapted from GcodeFEM.
- `src/ThreeMfCompare.App`: WPF, CommunityToolkit.Mvvm, HelixToolkit.Wpf.SharpDX.
- `tests/ThreeMfCompare.Tests`: against the projects in `Example/` (`dotnet test 3MFCompare.slnx`).

## Not tried yet

- Projects with several plates or two extruders (H2D): handled in the code, but there was no
  sample to check them against.
- Mouse interaction in the G-code panes was not tried by hand. Syncing was checked by turning one
  pane's camera from code (`--turn`), and the other panes followed.

## Beads

Every line is drawn as a bead, however many there are. Each pane builds its bead mesh once:
segments that run on from one another share the ring of vertices where they meet (about 4 vertices
per segment), and a bead is cut only at a gap, a sharp turn or a change of layer, line type or
filament. The slider and the line-type toggles then only choose the mesh's triangles, and a new
colouring only replaces its colours. For the examples (750k–930k lines) a pane takes 150–280 ms to
build and about 200 MB of memory, and about as much on the graphics card. Unticking Beads draws plain
lines instead.
