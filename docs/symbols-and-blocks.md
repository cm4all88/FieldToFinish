# Symbols and blocks

How FTF turns survey symbols, when it inserts a block of its own, and how both are
set up in **Settings > Symbols and Rotations**.

The division of labour does not change: Civil 3D draws the survey symbols (COGO
point styles chosen by description keys). FTF never draws a second copy of one on
its own initiative. It does two things with symbols:

1. **Turns the markers Civil 3D drew.** FTFPOINTS turns a marker from a field
   azimuth in the description ("SN 135"); FTFORIENT decides a facing from the
   crew's notes and the linework around the point. Both use the **symbol
   standard** below to know which way each block points at rotation 0.
2. **Inserts a block only where a rule opts in** (`insertBlock: true`), for a
   symbol a point style cannot provide. How that block is placed is set per rule
   (the second tab).

Nothing on the page draws in the drawing. The only thing it ever changes in the
drawing is **Bring into Drawing**, which copies one block definition from the
approved symbol library when you press it.

Everything is saved to the active rules file (`rules.json`): the office
configuration, or the drawing override when one is active, never the shipped
factory file. Comments (`"//..."` keys) and properties this build does not know are
kept. Save Symbols re-reads the file and writes only this page's parts onto it, so
it cannot undo a change saved from Point Features meanwhile.

## The symbol standard (tab 1)

The rules file's `orientation > symbols` list. Each rule:

| Property | Meaning |
|---|---|
| `codes` | Point codes it covers. `*` and `?` are wildcards (`SN*`); figure digits are ignored (`SN2` reads as `SN`). |
| `kind` | `Sign`, `Hydrant`, `WaterValve`, `GasValve`, `StreetLight`, `SignalPole`, `PavementArrow`, `PavementWord` or `Other`: what FTFORIENT reasons about. |
| `block` | The block the symbol is drawn with: for people and for the preview. Wildcards allowed. |
| `referenceCadDegrees` | **Native zero**: the AutoCAD direction (0 east, 90 north) the block's meaningful direction points at rotation 0: a sign's face, a hydrant's nozzle, a light's arm, a valve's pipe. |
| `period` | 360: one direction. 180: an axis (a bowtie valve looks the same turned half way). 0: looks the same at every rotation, never turned. |
| `twoSided` | Read from both sides (street name blades): only a measured direction turns it. |
| `rotateLabel` | The point label turns with the marker (painted words). |
| `meaning` | A note for people. |

A marker's rotation is the facing direction's AutoCAD angle minus the native zero,
reduced to the period (`SymbolRotation.MarkerDegrees`). A PMX sign (zero 90) turned
to face azimuth 135 gets marker rotation 225.

**First match wins.** A point gets the first rule, top to bottom, with a code that
matches. A code's own rule therefore goes **above** its family's. **Override for
Code** does that for you: it copies the rule that covers the code now, gives the
copy that one code, puts it directly above, and takes a literal entry for the code
out of the family's list (it could never be reached below the override). The
"Which rule does a code get?" box shows which rule a code lands on.

**The built-in PMX list.** A rules file with no `symbols` list of its own uses the
built-in PMX standard (`OrientationStandard.Pmx()`). The page shows that list
(greyed) and says so; the first edit copies all of its rules into the rules file
(with a `"//symbols"` note above them), and from then on the file's list is the one
in force. Nothing changes for FTFORIENT until you save. **Use Built-in PMX List**
removes the file's list again.

**Checked before saving** (each problem names the rule's number):

- every rule has at least one code, and no code contains a space;
- `kind` is one of the kinds above;
- `period` is 0, 180 or 360;
- the native zero is between 0 and 360;
- no code is listed below a rule that already matches it (it would never be used);
- `applyMinimumConfidence` is 0 to 1 and `applyWeakestTier` is Measured,
  Relationship, Alignment or Inferred.

**When FTFORIENT applies a decision**: the two thresholds under the list
(`applyMinimumConfidence`, `applyWeakestTier`).

## Rotation evidence and review (tab 3)

How FTFORIENT weighs the road lines around a feature and what a feature's fallback rule may do:

- the search distance (`roadSearchFeet`);
- how far a line that stops short is followed (`maxExtensionFeet`);
- the length of line a direction is taken over (`localTangentFeet`);
- when road lines disagree (`alignmentToleranceDegrees`);
- the trust in each kind of road line (`roadReferences`);
- intersections (`intersectionHandling`);
- per kind, Apply, Review or Leave (`fallbacks`);
- hand rotations (`handTurned`);
- QC graphics (`qcGraphics`, `qcShowInferredConnections`), their layers (`qcLayer`,
  `qcInferredLayer`; FTFORIENT sets both not to plot, so use layers that hold only QC) and
  plotted sizes (`qcArrowPlotted`, 0.3 in; `qcTextPlotted`, 0.06 in);
- **How near is near**: how far FTFORIENT looks for an intersection
  (`intersectionSearchFeet`, 150 ft), a stop sign's stop bar (`stopBarSearchFeet`, 60),
  signs on one post (`samePostFeet`, 1.5), a shot sign panel (`panelSnapFeet`, 3), a
  luminaire shot (`luminaireSearchFeet`, 40), a valve's water or gas line (`mainSnapFeet`,
  5), the rest of a valve cluster (`valveClusterFeet`, 20) and a hydrant near a cluster
  (`hydrantSearchFeet`, 60).

All are in the rules file's `orientation` section and checked on save. Why they exist, with
before and after: `docs/orientation-review-2026-11.md`.

## Road lines and lists (tab 4)

- **Road lines**: which survey linework is which road or utility line (`orientation >
  lines`): what the line is (centre line, lane line, pavement edge, stop bar, main...), the
  layers it is on and the survey figure codes it is drawn from. PMX's list is shown until
  your first edit copies it into the rules file; **Use the PMX List** takes the file's list
  out again. A list in the file replaces PMX's whole list.
- **Lists**, one a line: the sign codes that control an approach (stop, yield;
  `stopSignCodes`), the words that make a sign a stop or yield sign (`stopSignWords`) and
  the codes of a luminaire shot at the end of an arm (`luminaireCodes`). An empty box means
  PMX's list, so an office cannot have none.

## Blocks FTF inserts (tab 2)

The code rules that name a block. Each setting is an optional property of the code
rule; absent, it is what FTFPOINTS always did.

| Property | Default (absent) | Meaning |
|---|---|---|
| `insertBlock` | false | **Visibility**: FTF inserts the block at all. Turning it on asks first: the block goes on top of whatever the point style draws, so a symbol the point style already draws would show twice. The editor never sets it as a side effect. |
| `block` | - | Block name; `{placeholders}` allowed (`TREE-{species}`). FTFPOINTS needs it in the drawing. |
| `blockLayer` | current layer | **Symbol layer**; created on first use, project layer mappings apply. |
| `blockScale` | 1 | **Fixed scale**, used when `blockScaleFrom` is not set. |
| `blockScaleFrom`, `blockScaleDivisor` | -, 1 | **Scale from a field**: a named group or derived field (`trunk.exact`) divided by the divisor. |
| `blockInsertion` | `AtPoint` | **Insertion point**: `AtPoint`; `Offset` (`blockOffsetX` east, `blockOffsetY` north); `OffsetRotated` (the offset turns with the block). Offsets are drawing units and are not scaled. |
| `blockRotation` | `Field` | **Rotation method**: `Field` (the rule's rotation field, turned against the native zero when the standard describes this very block, as before); `None`; `Marker` (the point's marker rotation as FTFPOINTS / FTFORIENT set it). |
| `blockAttributes` | none | **Attribute formatting**: tag -> template. With at least one, FTFPOINTS creates the block's attributes, fills the tags that have a template and leaves the others at the block's default. |

Example:

```json
{
  "id": "hydrant",
  "match": "^(?<code>WFH|FH)\\s+(?<number>[0-9][0-9A-Z.-]*)(?<mods>(?:\\s+[A-Z0-9-]+)*)\\s*$",
  "label": { "format": "HYDRANT {number}", "layer": "V-UTIL-WATR-TEXT-E" },
  "insertBlock": true,
  "block": "OFC-HYD-TAG",
  "blockLayer": "V-UTIL-WATR-SYMB",
  "blockScale": 2,
  "blockInsertion": "Offset", "blockOffsetX": 1.5,
  "blockRotation": "Marker",
  "blockAttributes": { "NUM": "{number}", "ELEV": "[EL {elevation:0.00}]", "PT": "{point}" }
}
```

### Attribute templates

The label syntax (`FieldCodes.Symbols.AttributeTemplate`):

- `{group}`: any named group of the rule's pattern or note vocabulary (`{number}`,
  `{owner}`), and the derived fields (`{trunk.count}`...);
- `{name:0.00}`: a number formatted with a .NET format;
- `[text {x}]`: kept only when `{x}` has a value;
- `\P` between lines: joined with a space (an attribute is one line);
- always available: `{point}` point number, `{elevation}` / `{elev}`, `{northing}`,
  `{easting}`, `{raw}` the description as shot, `{note}`, `{label}` the label FTF
  writes, `{rule}`, `{code}`, `{azimuth}` the field azimuth. A pattern group of the
  same name wins (`{number}` is the rule's number group, as in labels; the point
  number is `{point}`).

An unknown placeholder, a tag with a space, or an invalid number format is refused
before saving.

## The approved symbol library

`orientation > symbolLibrary`: a drawing of the office's approved symbol blocks.
Its blocks are listed after the drawing's own in every block box (marked
"(library)") and previewed from the file. With no library of its own, the
standard offers the Easement Exhibits block library. A library block is copied
into the drawing only by **Bring into Drawing** (a block the drawing already has is
never redefined).

## The preview

`FieldCodes.Cad.Ui.BlockPreview` draws a block definition small: lines, polylines
(bulges as short chords), arcs, circles, 2D solids and one level of nested blocks.
Text, attributes, hatches, splines, ellipses and deeper nesting are left out and
the preview says "Simplified preview". Marked on it: the insertion point (red), the
native zero turned with the symbol (blue; two heads for an axis), and the test
facing (orange, dashed). Drag **Test facing** to see the rotation FTFPOINTS and
FTFORIENT give for that azimuth; on a directional symbol the blue arrow lands on the
orange one. With "Click the preview to set it" ticked, a click sets the native zero.

For other pages:

```csharp
var preview = new BlockPreview { Size = new Size(300, 300) };
preview.ShowBlock(db, "SSV-SN");                 // or ShowSketch(sketch) / ShowMessage(text)
preview.ApplySymbolRule(rule);                   // or ReferenceDegrees = 90; Period = 360;
preview.TestAzimuth = 135;                       // Rotation follows (225)
// or: preview.RotationFollowsAzimuth = false; preview.Rotation = 30;
preview.ShowAzimuthControls = false;             // hide the slider
preview.TestAzimuthChanged += ...; preview.ReferencePicked += ...;
```

The fit (a circle about the insertion point, so turning never rescales), the turn
about the insertion point and the arrow are `FieldCodes.Symbols.PreviewFrame` and
`SymbolPreview`, unit tested.

## Limits

- Untested in Civil 3D and as UI: the page, preview control, library reading and
  attribute creation compile against the API stand-ins only. The document editing,
  validation, preview arithmetic, placement and attribute text are unit tested.
- `blockRotation: "Marker"` reads the marker when FTFPOINTS places the block.
  FTFORIENT turns markers afterwards; `InsertedBlocks.FollowMarkers` brings such
  blocks round and is meant to be called at the end of FTFORIENT's apply.
- A dynamic block's preview shows every visibility state's geometry at once.
- The preview of a `{placeholder}` block name uses the block the sample
  description makes.
