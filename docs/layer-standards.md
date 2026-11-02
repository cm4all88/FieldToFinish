# Layers: names, standards, updates and checks

Settings > Layers is where every layer FieldToFinish draws on is managed: which layer each kind
of object goes on, what each layer looks like, bringing layers already in a drawing up to that
look, and checking that everything the settings and rules name exists in the drawing.

## What is listed

One row per thing FTF draws on a layer. Every row has a kind, used by the "All kinds of objects"
filter:

| Kind | What is in it |
|---|---|
| COGO point labels | each code rule's label layer; the label mask layer when one is set; tags of non-tree features that follow their label |
| Point and feature symbols | blocks FTF inserts (`insertBlock` rules); labels at symbol points (`symbolLabels`) |
| Linework labels | explicit line-feature label layers; for every labelled line feature without one, the layer FTFLINELABELS will actually use in this drawing (the matching `-TEXT-E` layer when the drawing has it, otherwise the default label layer); drafting-line bearing/distance labels |
| Trees | tree labels, tags, driplines (and a tree symbol when a tree rule inserts one) |
| Utility dips and inverts | pipe, invert label and structure label layers per system |
| Leaders and multileaders | FTF draws every leader on the layer of the label it belongs to; these rows say which (point labels, tags, dip structure callouts, easement callouts, exhibit POB/POC and area leaders) |
| Spot elevations, Contours | their marker / text / line / label layers |
| Pavement markings | labels of stripe, crosswalk and stop-bar linework, symbol labels of pavement arrows and words |
| Survey control | control code labels, monument symbols |
| Tables and legends | tree schedule, control table, easement, exhibit and record tables, the FTFLEGEND text |
| Easement and boundary | strip easements, exhibits, recorded surveys, right of way, platted blocks |
| Other FTF objects | bench, sheet windows and matchlines, orientation QC, drafting linework |

Rows for the layers a modifier makes (`-DEAD`, `-RMV`, `-PROT` on a label, dripline or symbol
layer) are hidden until "Modifier layers" is ticked. The control table's layer
(`V-TEXT-TABL`) is fixed in the plugin and listed as *Built in*; New Layer still moves it.

Nothing new is hard-wired: the rows come from rules.json and from every `...Layer` field of
every settings section, found by reflection, so a new setting appears without a code change.

## New Layer (project mapping)

Unchanged: New Layer sends an FTF layer to the project's layer for every command
(`general.layerMappings`). Type a name (suggestions come from the drawing) or use Pick from
Drawing. Edit Feature Layer opens the page that changes one feature's layer only.

## Layer standards

Below the table, select one or more rows and set what their layers look like:

* **Color**: ACI 1-255, a colour name (red, yellow, green, cyan, blue, magenta, white) or a true
  colour `r,g,b`. ByLayer / ByBlock are not layer colours and are refused.
* **Linetype**: a linetype the drawing has, or type any name. A linetype the drawing lacks is
  loaded from acad.lin (then acadiso.lin) when it is applied; if neither has it, FTF says so on
  the command line and never puts another linetype in its place.
* **Lineweight**: Default or one of AutoCAD's widths (0.00 ... 2.11 mm).
* **Plot**: ticked plots, clear does not plot, filled is "not set".
* **Transparency**: 0-90 percent.
* **Description**.

A blank box is "not set": that property of the drawing's layer is left alone. Only the boxes
you change are written when you press **Apply to Selected**, so one property can be set on many
layers at once. **Copy from Drawing** makes the standard what the drawing has now; **Clear
Standard** removes the layer's own standard.

The table shows each layer's standard beside **In Drawing Now**. A standard value that differs
from the drawing is shaded; a value that comes from a pattern is in italics. Standards are kept
per layer FTF draws on, after New Layer.

**All Standards...** edits every standard as a list, including patterns: `V-*-TEXT-E` covers
every text layer, `FTF-QC-*` every QC layer. A layer's own standard beats a pattern property by
property; among patterns the more specific (more literal characters) wins, then the one listed
first.

### Settings file

```json
"layerStandards": {
  "applyToNewLayers": true,
  "layers": [
    { "name": "V-SURF-VEGE-TEXT-E", "color": "3", "linetype": "Continuous", "lineweight": "0.25", "plot": true, "description": "Tree labels" },
    { "name": "V-*-TEXT-E", "lineweight": "0.25" },
    { "name": "FTF-QC-*", "plot": false, "transparency": 50 }
  ]
}
```

Properties not set are not written. A settings file from before this section loads with no
standards, and layers are created as before.

## The three separate options

1. **New layers.** "Layers FTF creates take their standard" (`applyToNewLayers`, on by
   default): when a command has to create a layer it gets its standard's properties, which win
   over the look of its family and the settings' colour/lineweight. A layer that already exists
   is never changed by a command.
2. **Preview.** "Update Drawing Layers..." on the page, or `FTFLAYERUPDATE`, lists every
   property of every layer FTF uses that is in the drawing and differs from its standard. Nothing
   changes yet.
3. **Update existing layers.** Only after the preview: the page applies the ticked changes;
   `FTFLAYERUPDATE` asks `Apply these changes? [Yes/No] <No>:`. Layer properties only: no object
   is moved, restyled or erased, no layer is renamed, layers FTF does not use and xref layers
   (`NAME|LAYER`) are never touched. The result lists every property changed and anything that
   could not be (a linetype nobody has).

The page uses its own standards and New Layers, saved or not; the command uses the saved
settings.

## Check Standards

"Check Standards..." on the page, or `FTFCHECKSTANDARDS`, compares everything the settings and
rules name with the drawing and lists *Severity / What / Name / Referenced by / Problem /
Choices*:

| Checked | Problem | Fix offered |
|---|---|---|
| Layers FTF uses (after New Layer) | not in the drawing: note, FTF creates it on first use (with its standard); a similar layer exists or the same name spelled differently: warning | create it now; send it to an existing layer (New Layer) |
| Layer standards | invalid value: error; matches no layer FTF uses: note | correct it on the page |
| Linetypes of standards and settings (driplines, drafting lines, record entities) | not in the drawing: note when acad.lin has it, error when nothing has it | load from acad.lin; choose a linetype the drawing has (written to the setting) |
| Text, multileader, table and dimension styles, Civil 3D line/curve label styles named in settings (every `...TextStyle`, `...LeaderStyle`, `...TableStyle`, `...DimensionStyle`, `...LabelStyle` field) | not in the drawing: warning | choose one the drawing has (written to every setting that named it); import it; clear the setting |
| Blocks: code rules that insert a block, modifier blocks, `...Block` settings, orientation symbols | rule block missing: error (the point gets no symbol and is reported, never another block); orientation block: note | choose a block (settings only); insert the definition |

Nothing is applied by the check. A fix happens only when its button is pressed. On the page, a
setting fix writes to the settings being edited (saved with Save; the other pages are refreshed
to show it, so unsaved typing on them is reset — the window says so first). From the command,
a setting fix is saved to the settings file in force (the user's own when only defaults are in
use).

## Limits

* Not run inside Civil 3D yet: the commands, the page and the dialogs compile against the API
  stubs; the decisions (matching, validation, the preview and the check) are unit tested.
* The Survey Comparison review layers come from the comparison profile and are created by the
  comparison tools themselves; standards are not applied to them on creation (FTFLAYERUPDATE
  does not list them either).
* Text styles, multileader styles and the like are checked, not restyled: changing the styling
  of existing annotation is the annotation tools' job.
