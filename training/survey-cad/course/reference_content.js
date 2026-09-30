// Content for the Survey CAD Command Reference (rendered by build_reference.js).
// Markup: **type this**  !!warning!!  __bold__.  ↵ = Enter.
// Row options: ribbon (grey location), warn (red line), survey: safe | care | derived | draft | never.
// Ribbon locations: AutoCAD 2D Drafting & Annotation workspace unless the row says Civil 3D. ▾ = drop-down / expanded panel.
// Commands and corrections were checked against help.autodesk.com (2024-2026); see REFERENCE_SOURCES.md.

const TITLE = "Survey CAD Command Reference";
const SUBTITLE = "The AutoCAD and Civil 3D commands a survey drafter should know, from the first day to the power drafter";
const FOOTER = "Parametrix Survey CAD Command Reference";
const FILE = "SURVEY_CAD_Command_Reference";

const COVER = [
  { type: "box", title: "HOW TO READ IT", lines: [
    "Start at the front: section 1 is what you use every day. Each section gets more advanced; the back is for when you're ready.  **BOLD MONO** = type it.  **↵** = Enter (Space works too).  Italics in the Type column = click through menus.  Grey = where the button is (▾ = drop-down or expanded panel).  !!Red = warning or destructive.!!",
    "__Survey use__ = what a command may do to coordinate-controlled survey geometry (points, control, monuments, boundary, field-to-finish linework):  __Safe__ doesn't move or reshape it.  __With care__ changes it: check the result.  __Derived geometry only__ builds new geometry from it, never replaces it.  __Drafted items only__ = text, symbols, title block, notes.  __Never__ on survey data.",
  ] },
  { type: "box", kind: "warn", title: "⚠ GOLDEN RULES", lines: [
    "- Survey points and linework sit on real coordinates: never MOVE, SCALE, ROTATE, STRETCH, FLATTEN or EXPLODE them. Never scale survey geometry to fit a sheet.",
    "- Practice on a SAVEAS copy. Save before PURGE, AUDIT, OVERKILL, MAPCLEAN, LAYMRG or LAYDEL.",
    "- A Civil 3D object looks wrong? Don't explode or redraw it: check the object, style, label style, layer and source. Referenced? Fix it in the source drawing.",
  ] },
];

// ================================================================================================
const S1 = { n: 1, title: "START HERE: EVERYDAY COMMANDS", tag: "DAY ONE", size: 16, sub: "What a new drafter uses constantly", blocks: [
  { type: "cmds", title: "Open, move around, save", rows: [
    ["OPEN / SAVEAS", "OPEN ↵ / SAVEAS ↵", "Open a drawing / save a copy under a new name", { ribbon: "Quick Access toolbar", warn: "Practicing? SAVEAS your own copy first" }],
    ["QSAVE", "CTRL+S", "Save"],
    ["ZOOM", "Z ↵ E ↵ / W ↵ / P ↵", "Extents (or double-click the wheel) / window / previous view. Roll the wheel to zoom at the cursor"],
    ["Pan", "hold the wheel", "Drag the view"],
    ["ESC", "ESC", "Cancel the command; clear the selection"],
    ["Undo / Redo", "CTRL+Z / CTRL+Y", "Step back / forward. **U ↵** undoes one step"],
    ["Repeat", "↵ or Space", "On an empty command line: repeat the last command"],
    ["PROPERTIES", "CTRL+1", "Palette: layer, color, length, elevation... of what's selected"],
    ["REGEN", "RE ↵", "Redraw: fixes arcs that look like polygons"],
    ["Command line / next drawing", "CTRL+9 / CTRL+TAB", "Show or hide the command line / switch open drawings. Read the command line: it says what it wants"],
  ] },
  { type: "cmds", title: "Selecting and grips", rows: [
    ["Pick", "click", "Select an object; keep clicking to add"],
    ["Window", "click, move right, click", "__Left to right, blue solid box:__ only objects __completely inside__"],
    ["Crossing", "click, move left, click", "__Right to left, green dashed box:__ anything the box __touches__"],
    ["Remove from selection", "Shift + pick", "Take objects back out (Shift + box works too)"],
    ["Grips", "click a grip", "Blue boxes on a selected object. Click one (red) and drag to stretch; Space cycles Move, Rotate, Scale, Mirror", { warn: "Dragging a grip on survey linework moves it: snap it back to the Node" }],
  ] },
  { type: "cmds", title: "Object snaps", note: "snap to real geometry, never eyeball a point", rows: [
    ["OSNAP", "OS ↵ / F3", "Pick the running snaps / turn them all on and off", { ribbon: "Status bar" }],
    ["One-time snap", "Shift + right-click", "Snap menu for the next pick only"],
    ["Node", "NOD", "A survey point (COGO point or POINT)", { warn: "Points carry elevations: see OSNAPZ, section 8" }],
    ["Endpoint / Midpoint", "END / MID", "End / middle of a line, arc or polyline segment"],
    ["Insertion", "INS", "A symbol's or text's base point"],
    ["Perpendicular", "PER", "Square off to a line (setbacks)"],
    ["Nearest", "NEA", "Anywhere on a line"],
  ] },
  { type: "cmds", title: "Inspect and layers", rows: [
    ["ID", "ID ↵", "Coordinates of a point: X = Easting, Y = Northing, Z = elevation", { ribbon: "Home › Utilities ▾" }],
    ["LIST", "LI ↵", "Layer, bearing, length, area, closed or not", { ribbon: "Home › Properties ▾" }],
    ["DIST", "DI ↵", "Distance and angle between two points", { ribbon: "Home › Utilities › Measure" }],
    ["LAYER", "LA ↵", "Layer manager: on/off, freeze, lock, plot, current", { ribbon: "Home › Layers" }],
    ["LAYFRZ", "LAYFRZ ↵", "Freeze the layer of whatever you pick", { ribbon: "Home › Layers" }],
    ["LAYMCUR", "LAYMCUR ↵", "Make the picked object's layer current", { ribbon: "Home › Layers" }],
  ] },
  { type: "box", title: "PARAMETRIX SURVEY LAYERS", lines: [
    "Survey layers start with **V-**: V-NODE-E points, V-PROP-BNDY-E boundary, V-UTIL-STRM-SYMB-E storm symbols, V-TREE-TEXT tree labels (last field often the status: -E existing). Layers control visibility, plotting and organization. Keep color and linetype __ByLayer__; don't draw on 0 or Defpoints (Defpoints never plots). __Off__ hides; __Freeze__ hides and skips regens. Neither deletes.",
  ] },
  { type: "cmds", title: "Draw and edit", rows: [
    ["LINE", "L ↵", "Lines; snap Node to Node or type **@dist<bearing**", { ribbon: "Home › Draw", survey: "safe" }],
    ["PLINE", "PL ↵", "One connected polyline; **C ↵** closes it", { ribbon: "Home › Draw", survey: "safe" }],
    ["MOVE", "M ↵", "Move by base point and second point", { ribbon: "Home › Modify", survey: "draft" }],
    ["COPY", "CO ↵", "Copy by base point and second point", { ribbon: "Home › Modify", survey: "draft" }],
    ["ROTATE", "RO ↵", "Rotate about a base point; **R ↵** Reference lines a symbol up with linework", { ribbon: "Home › Modify", survey: "draft" }],
    ["FILLET R0", "F ↵ R ↵ 0 ↵", "Close a corner exactly: both lines end at their intersection", { ribbon: "Home › Modify", survey: "care" }],
    ["TRIM", "TR ↵", "Cut off an overshoot; Shift + pick extends instead", { ribbon: "Home › Modify", survey: "care" }],
    ["EXTEND", "EX ↵ B ↵", "Grow a line to a boundary you pick first (B)", { ribbon: "Home › Modify (Trim ▾)", survey: "care" }],
    ["OFFSET", "O ↵", "Parallel copy at a distance (curb, ROW, setbacks). **L ↵ C ↵** puts it on the current layer", { ribbon: "Home › Modify", survey: "derived" }],
    ["ERASE / OOPS", "E ↵ / OOPS ↵", "Delete / bring back the last erase, even after other commands", { ribbon: "Home › Modify", survey: "care" }],
    ["MATCHPROP", "MA ↵", "Paint one object's properties onto others", { ribbon: "Home › Properties", survey: "care", warn: "Copies the layer too: S ↵ (Settings) to limit" }],
    ["MTEXT", "MT ↵", "Paragraph text: callouts, notes. Background Mask in the editor", { ribbon: "Home › Annotation" }],
    ["MLEADER", "MLD ↵", "Arrow + landing + text: trees, structures, control", { ribbon: "Home › Annotation" }],
  ] },
  { type: "how", items: [
    { title: "FILLET R0: close a corner", steps: [
      "**F ↵ R ↵ 0 ↵** (radius sticks until changed)",
      "Pick the first line on the part to __keep__",
      "Pick the second line on the part to __keep__",
    ], tip: "Shift on the second pick = R0 at any radius." },
    { title: "ROTATE by Reference: square a symbol", steps: [
      "**RO ↵**, pick the symbol, **↵**; base: Insertion snap",
      "**R ↵**, type **0 ↵** (the symbol sits at 0)",
      "**P ↵**, pick two points along the curb (Nearest)",
    ], tip: "180° off? Pick the curb points the other way." },
  ] },
] };

// ================================================================================================
const S2 = { n: 2, title: "SURVEY DRAFTING COMMANDS", sub: "Finishing and labeling a topo", blocks: [
  { type: "cmds", title: "Text, leaders and dimensions", rows: [
    ["TEXT", "DT ↵ J ↵ BC ↵", "Single-line text; Bottom Center for text on a line", { ribbon: "Home › Annotation (Text ▾)" }],
    ["Degree sign", "%%d", "In text: **N88%%d24'53\"W**"],
    ["DIMALIGNED", "DAL ↵", "Dimension parallel to what you measure (setbacks)", { ribbon: "Home › Annotation (Dim ▾)" }],
    ["TORIENT", "TORIENT ↵", "Flip upside-down text and leaders to read correctly (Express Tools)"],
    ["MLEADERALIGN", "MLA ↵", "Line leader labels up in a neat column", { ribbon: "Annotate › Leaders" }],
    ["MLEADEREDIT", "MLE ↵", "Add or remove leader arms on one label", { ribbon: "Annotate › Leaders" }],
    ["TJUST", "TJUST ↵", "Change justification without moving the text (Express Tools)"],
    ["FIND", "FIND ↵", "Find and replace text in the drawing", { ribbon: "Annotate › Text" }],
    ["SCALETEXT", "SCALETEXT ↵", "Set many texts to one height", { ribbon: "Annotate › Text ▾" }],
    ["Text grip", "drag", "Moves text; MTEXT side grips set the width"],
    ["Leader grips", "drag", "Landing or text grip moves the text, arrow stays put", { warn: "Leave the arrowhead grip: it sits on the feature" }],
    ["Dimension text grip", "hover", "Move with dim line, move text only, reset text position"],
    ["Civil 3D segment labels", "ADDSEGMENTLABELS ↵", "Bearing/distance on every segment; follows the line if it changes (section 5)", { ribbon: "Civil 3D: Annotate › Add Labels" }],
  ] },
  { type: "cmds", title: "Linework", rows: [
    ["JOIN", "J ↵", "Join touching lines/arcs into one polyline", { ribbon: "Home › Modify ▾", survey: "safe" }],
    ["PEDIT", "PE ↵", "Edit a polyline: Close, Join, Width; converts lines to polylines", { ribbon: "Home › Modify ▾", survey: "care" }],
    ["LENGTHEN", "LEN ↵ DE ↵ / T ↵", "Add a length (Delta) or set a Total length", { ribbon: "Home › Modify ▾", survey: "care" }],
    ["BREAKATPOINT", "BREAKATPOINT ↵", "Split a line or polyline in two at one point", { ribbon: "Home › Modify ▾", survey: "safe" }],
    ["STRETCH", "S ↵", "Crossing window (right to left) moves the vertices inside", { ribbon: "Home › Modify", survey: "draft", warn: "Objects fully inside move completely" }],
    ["SCALE", "SC ↵", "Resize about a base point: a symbol, a note, a block", { ribbon: "Home › Modify", survey: "draft" }],
    ["MIRROR", "MI ↵", "Mirror copy; text stays readable while MIRRTEXT = 0", { ribbon: "Home › Modify", survey: "draft" }],
    ["MEASUREGEOM", "MEA ↵", "Distance, radius, angle, area; Quick mode measures as you hover", { ribbon: "Home › Utilities › Measure" }],
  ] },
  { type: "pairs", title: "Typing coordinates, bearings and distances", code: true, split: 0.3, rows: [
    ["#1125170.86,733969.17", "Absolute point: Easting,Northing (# beats dynamic input's relative default)"],
    ["@25,0", "Relative: 25' east of the last point.  **@** alone = the last point"],
    ["@249.14<S88d24'53\"E", "Distance and bearing, no spaces. Needs Surveyor's units (**UN ↵**)"],
  ] },
  { type: "cmds", title: "Civil 3D transparent commands", note: "type while a command asks for a point: L ↵ then 'BD ↵", rows: [
    ["Bearing / distance", "'BD ↵", "Quadrant (1 NE, 2 SE, 3 SW, 4 NW), bearing, distance"],
    ["Azimuth / distance", "'ZD ↵", "Azimuth from north, then distance"],
    ["Northing / Easting", "'NE ↵", "Northing first by default (Ambient Settings can swap it)"],
    ["Point number / object", "'PN ↵ / 'PO ↵", "Use a COGO point by number / by picking it"],
    ["Side shot", "'SS ↵", "Angle and distance off the line through the last two points"],
    ["Zoom to point", "'ZTP ↵", "Zoom to a point number or range; works on its own too"],
    ["Match radius / length", "'MR ↵ / 'ML ↵", "Take a radius or length from an existing object"],
  ] },
  { type: "triple", title: "Survey labeling examples", note: "formats from the Parametrix survey base drawing", heads: ["Feature", "Tool", "Label"], widths: [2050, 1700, 6610], rows: [
    ["Catch basin", "MLEADER", "**CB #10012 / RIM=75.20 / IE 6\" PVC (W)=73.20 / BOTTOM=73.10**: one line each, inverts by direction"],
    ["Tree", "MLEADER", "**22\" CFR / 25' DRIP** (trunk, type, drip).  Cluster: **CLUSTER / 12\"(2) DEC / 14' DRIP**"],
    ["Control / monument", "MLEADER", "**PMX #2000 / MAG NAIL**.  Found: **FOUND IRON PIPE, / 0.8' ABOVE GRADE / (date)**"],
    ["Building", "MTEXT", "Inside the footprint: **HOUSE** or **GARAGE** and **FFE=65.39**"],
    ["Surfaces, edges", "MTEXT", "**ASPH**, **CONC**, **GRASS**, **CW**, **EDGE OF PAVEMENT**, **4' CHAIN LINK FENCE**; mask on"],
    ["Bearing and distance", "TEXT / segment label", "BC on the line's midpoint, rotated with it: **N88%%d24'53\"W 249.14'**; TORIENT if upside down"],
    ["Setback", "DIMALIGNED", "Endpoint on the building corner, Perpendicular to the lot line"],
    ["Road name", "MTEXT", "Centered in the road, rotated with it"],
  ] },
  { type: "box", title: "LABELING STANDARDS", lines: [
    "Text reads from the __bottom or right__ of the sheet. 0.08\" plotted = 1.6' at 1\" = 20' (training drawing height; leader/dim style SRV-20). Labels on the feature's V- text layer (V-TREE-TEXT, V-UTIL-STRM-TEXT, V-CTRL-TEXT in the office field-code rules). Leaders don't cross each other, text or linework. Move a crowded label by its landing grip, never the arrowhead.",
  ] },
  { type: "how", items: [
    { title: "A line by bearing and distance ('BD)", steps: [
      "**L ↵**, Node snap on the starting monument",
      "**'BD ↵**, **↵** to accept the start point",
      "Quadrant **3 ↵** (SW), bearing **88.2453 ↵** (DD.MMSS), distance **249.14 ↵**",
      "**ESC** ends 'BD, **↵** ends LINE",
    ], tip: "Rejected? Bearings use the drawing's angle format (Ambient Settings)." },
    { title: "Dimension a setback", steps: [
      "Dimension layer current (training: V-ANNO-DIMS-E), style SRV-20",
      "**DAL ↵**, Endpoint on the building corner",
      "Shift + right-click › Perpendicular, pick the lot line",
      "Click to place the dimension off the building",
    ] },
  ] },
] };

// ================================================================================================
const S3 = { n: 3, title: "LAYERS, SELECTION AND DISPLAY", sub: "Stronger layer and selection tools", blocks: [
  { type: "cmds", title: "Layer tools", note: "work on the V- layers of whatever you pick", rows: [
    ["LAYISO / LAYUNISO", "LAYISO ↵", "Show only the picked objects' layers / undo that", { ribbon: "Home › Layers", warn: "LAYUNISO before saving or the rest stay off" }],
    ["LAYOFF / LAYON", "LAYOFF ↵ / LAYON ↵", "Turn a picked layer off / turn all layers on", { ribbon: "Home › Layers" }],
    ["LAYFRZ / LAYTHW", "LAYFRZ ↵ / LAYTHW ↵", "Freeze a picked layer / thaw every layer", { ribbon: "Home › Layers (▾)" }],
    ["LAYLCK / LAYULK", "LAYLCK ↵ / LAYULK ↵", "Lock / unlock a picked layer: can snap to it, can't edit it", { ribbon: "Home › Layers" }],
    ["LAYWALK", "LAYWALK ↵", "Step through layers one at a time to see what's on each", { ribbon: "Home › Layers ▾" }],
    ["LAYCUR", "LAYCUR ↵", "Move picked objects to the current layer", { ribbon: "Home › Layers ▾", survey: "safe" }],
    ["LAYMCH", "LAYMCH ↵", "Move objects to another object's layer", { ribbon: "Home › Layers", survey: "safe" }],
    ["COPYTOLAYER", "COPYTOLAYER ↵", "Copy objects onto another layer, originals untouched", { ribbon: "Home › Layers ▾", survey: "derived" }],
    ["LAYMRG", "LAYMRG ↵", "Merge one layer into another", { ribbon: "Home › Layers ▾", survey: "care", warn: "Deletes the first layer; read both names before Y" }],
    ["LAYDEL", "LAYDEL ↵", "Delete a layer and everything on it", { ribbon: "Home › Layers ▾", survey: "never", warn: "Deletes every object on the layer, blocks included" }],
    ["LAYERP", "LAYERP ↵", "Undo the last layer setting change", { ribbon: "Home › Layers" }],
    ["LAYERSTATE", "LAYERSTATE ↵", "Save and restore on/freeze/color sets (topo, boundary, utility sheets)", { ribbon: "Home › Layers (Layer State ▾)" }],
    ["SETBYLAYER", "SETBYLAYER ↵", "Reset color, linetype and lineweight to ByLayer", { ribbon: "Home › Modify ▾", survey: "safe", warn: "Answer N to 'blocks too' unless you mean it" }],
  ] },
  { type: "cmds", title: "Selecting smarter", rows: [
    ["QSELECT", "QSELECT ↵", "Find objects by property (e.g. everything on layer 0)", { ribbon: "Home › Utilities" }],
    ["SELECTSIMILAR", "SELECTSIMILAR ↵", "Select everything like the picked object (type and layer)", { ribbon: "Right-click › Select Similar" }],
    ["FILTER", "FI ↵", "Selection filter with saved rules"],
    ["ADDSELECTED", "ADDSELECTED ↵", "Start a new object like the picked one (layer, style)", { ribbon: "Right-click › Add Selected" }],
    ["Isolate objects", "ISOLATEOBJECTS ↵", "Hide everything else for now without touching layers; **UNISOLATEOBJECTS ↵** ends it", { ribbon: "Right-click › Isolate" }],
    ["COUNT", "COUNT ↵", "Count and highlight every copy of a picked block (trees, catch basins, signs) in the drawing or an area. **COUNTLIST ↵** palette; **COUNTTABLE ↵** inserts a count table (AutoCAD 2022+)"],
    ["Selection cycling", "CTRL+W", "Overlapping objects: pick from a list of what's under the cursor"],
    ["Lasso", "click and drag", "Freehand selection; Space cycles Window / Crossing / Fence"],
    ["At Select objects:", "P  L  ALL  F  WP  CP", "Previous set, Last drawn, everything, Fence, Window/Crossing Polygon"],
  ] },
] };

// ================================================================================================
const S4 = { n: 4, title: "SHEETS, VIEWPORTS AND PLOTTING", sub: "From model space to a deliverable sheet", blocks: [
  { type: "box", title: "MODEL, PAPER, VIEWPORT", lines: [
    "__Model Space__ = the real world at full size, in real coordinates.  __Paper Space__ (a layout tab) = the sheet: title block, notes, north arrow.  __Viewport__ = a scaled window from the sheet into model space: at 1\" = 20', 100' measures 5\" on paper.  !!Never scale survey geometry to make it fit the sheet: change the viewport scale or the sheet.!!",
  ] },
  { type: "cmds", title: "Viewports, layouts and plotting", rows: [
    ["MVIEW", "MV ↵", "Viewport from two corners; **P ↵** Polygonal draws a polyline-shaped viewport, **O ↵** turns a closed polyline into one", { ribbon: "Layout › Layout Viewports" }],
    ["MSPACE / PSPACE", "MS ↵ / PS ↵", "Work inside the viewport / on the paper; or double-click in / out", { ribbon: "Status bar MODEL / PAPER" }],
    ["Viewport scale", "Z ↵ 1/20XP ↵", "Inside the viewport: 1\" = 20'; or the status bar scale list", { warn: "Lock it, or the next zoom ruins it" }],
    ["Viewport lock", "CTRL+1 › Display locked", "Yes: zooming zooms the sheet, not the viewport"],
    ["VPMAX / VPMIN", "VPMAX ↵ / VPMIN ↵", "Work full-screen in a viewport, then restore", { ribbon: "Status bar" }],
    ["VPLAYER", "VPLAYER ↵", "Freeze a layer in one viewport only (VP Freeze column in LA)"],
    ["VPCLIP", "VPCLIP ↵", "Clip a viewport to a polyline shape", { ribbon: "Layout › Layout Viewports › Clip" }],
    ["CHSPACE", "CHSPACE ↵", "Move objects between paper and model, rescaled to look the same", { ribbon: "Home › Modify ▾ (Change Space)", survey: "draft" }],
    ["SPACETRANS", "'SPACETRANS ↵", "Convert a height between paper and model (0.08\" = 1.6' at 1\" = 20')"],
    ["UCS / PLAN", "UCS ↵ OB ↵ / PLAN ↵ ↵", "Turn the view to line up with a road; reset with UCS ↵ ↵ then PLAN ↵ ↵", { warn: "PLAN zooms extents: reset the scale and turn the north arrow" }],
    ["SNAPANG", "SNAPANG ↵ angle ↵", "Rotate the crosshairs and snap to match a turned view (it doesn't follow automatically). **0** resets"],
    ["LAYOUT", "LAYOUT ↵ C ↵", "Copy a layout tab for another sheet"],
    ["PAGESETUP", "PAGESETUP ↵", "Save printer, paper, scale, plot style once per layout", { ribbon: "Output › Page Setup Manager" }],
    ["PLOT", "CTRL+P", "DWG To PDF.pc3, plot area Layout, 1:1, monochrome.ctb (training; use the office table)", { ribbon: "Output › Plot" }],
    ["PUBLISH", "PUBLISH ↵", "Plot many layouts or drawings to one PDF set", { ribbon: "Output › Batch Plot" }],
  ] },
  { type: "how", items: [
    { title: "Viewport scale and lock", steps: [
      "**MV ↵** on the layout, pick two corners",
      "Double-click inside; **Z ↵ 1/20XP ↵**; pan with the wheel held (don't zoom)",
      "Double-click outside, select the border, **CTRL+1** › Display locked: Yes",
      "Border on a no-plot layer",
    ], tip: "Check: DI on paper across 100' reads 5.00\"." },
    { title: "CHSPACE: move a note to or from the sheet", steps: [
      "Model to paper: double-click into the viewport, **CHSPACE ↵**, select, **↵**",
      "Paper to model: on the paper, **CHSPACE ↵**, select, **↵**, click the target viewport, **↵**",
    ], tip: "1.6' model text becomes 0.08\" on paper, and back." },
  ] },
] };

// ================================================================================================
const S5 = { n: 5, title: "CIVIL 3D COMMANDS AND CONCEPTS", sub: "Points, styles, labels and surfaces", blocks: [
  { type: "pairs", title: "What you're looking at", split: 0.24, rows: [
    ["COGO Point", "Point number, northing, easting, elevation and description. A database object, not a block or POINT"],
    ["Point Style / Point Label Style", "The symbol / the text next to it (number, elevation, description)"],
    ["Point Group", "Named set of points; controls how they display. The group highest in the display order wins"],
    ["Description Key", "Field code (first word, e.g. TREE, CB) → point style, label style, layer, full description"],
    ["Object Style / Label Style", "How a Civil 3D object looks / what its label says and how it displays"],
    ["Feature line / Survey figure", "Linear Civil 3D objects with elevations (breaklines, grading). Figures live in the survey database"],
  ] },
  { type: "box", kind: "warn", title: "⚠ IF A CIVIL 3D OBJECT LOOKS WRONG", lines: [
    "Don't explode or redraw it. Check the __object__ (CTRL+1), its __style__, __label style__, __layer__ and __source__ (point group, description key, data reference, xref). Fix the cause, not the picture. !!Never EXPLODE Civil 3D objects!!: use EXPORTTOAUTOCAD for a plain copy.",
  ] },
  { type: "box", title: "UNITS IS NOT THE COORDINATE SYSTEM", lines: [
    "AutoCAD **UNITS** only sets how lengths and angles display and are typed. The drawing's real-world location lives in Civil 3D __Drawing Settings__. Assign or change it only with the project surveyor's zone and factors: !!it changes where the drawing sits on the earth and what grid and ground coordinates mean.!!",
  ] },
  { type: "cmds", title: "Coordinate system, transformation and online maps", note: "EDITDRAWINGSETTINGS ↵ or Settings tab › right-click the drawing › Edit Drawing Settings", rows: [
    ["Units and Zone tab", "EDITDRAWINGSETTINGS ↵", "Drawing units (feet), angular units, drawing scale, and the coordinate system: pick the category and the project's State Plane zone (or type its code)", { warn: "Project surveyor's call, not a drafting fix" }],
    ["Transformation tab", "EDITDRAWINGSETTINGS ↵", "Relates local (ground) northing/easting to grid: sea level scale factor, grid scale factor (Unity, User Defined, Reference Point, Prismoidal), rotation to grid north, reference point. Only available once a zone is set", { warn: "Use only the factors and reference point the surveyor gives you" }],
    ["Ambient Settings tab", "EDITDRAWINGSETTINGS ↵", "Precision and how angles, directions and coordinates are typed and shown ('BD format, 'NE order)"],
        ["Geographic location", "GEOGRAPHICLOCATION ↵", "AutoCAD's geolocation (map or KML/KMZ). In Civil 3D set the zone in Drawing Settings instead"],
    ["GEOMAP", "GEOMAP ↵", "Online map behind the drawing: Aerial, Road, Hybrid or Off. Needs a coordinate system (Geolocation tab appears) and an Autodesk sign-in", { ribbon: "Geolocation › Online Map", warn: "The online map doesn't plot" }],
    ["GEOMAPIMAGE", "GEOMAPIMAGE ↵", "Capture part of the online map as an image that plots and works offline (plan view of World UCS). **GEOMAPIMAGEUPDATE ↵** refreshes it", { ribbon: "Geolocation › Online Map" }],
  ] },
  { type: "cmds", title: "Toolspace, points and labels", rows: [
    ["SHOWTS", "SHOWTS ↵", "Open Toolspace (Home › Palettes buttons toggle each tab)", { ribbon: "Civil 3D: Home › Palettes" }],
    ["Prospector tab", "", "What's in the drawing: points, point groups, surfaces, sites, data shortcuts"],
    ["Settings tab", "", "Object and label styles, description key sets, command settings, Drawing Settings"],
    ["Survey tab", "", "Survey databases, figures, figure prefix and linework code sets (office-managed)"],
    ["CREATEPOINTS", "CREATEPOINTS ↵", "Point Creation toolbar: manual, on objects, from file", { ribbon: "Civil 3D: Home › Create Ground Data › Points" }],
    ["IMPORTPOINTS", "IMPORTPOINTS ↵", "Points from a text file. Parametrix point files use the __PNEZDN__ format: pick it in the Format list", { ribbon: "Civil 3D: Insert › Import › Points From File" }],
    ["Point Editor", "right-click point › Edit Points", "Spreadsheet editing of many points (Panorama)", { survey: "care" }],
    ["Lock / Unlock points", "Prospector › Points › right-click", "Protect control points from accidental edits"],
    ["Point numbers", "LISTAVAILABLEPOINTNUMBERS ↵", "Free (unused) point numbers at the command line; **LISTUSEDPOINTNUMBERS ↵** lists used ones", { ribbon: "Civil 3D: COGO Point tab › COGO Point Tools" }],
    ["Point groups", "Prospector › Point Groups", "Properties › display order; right-click › Update when flagged out of date"],
    ["Description keys", "Settings › Point › Description Key Sets", "Code → style, label style, layer; applied as points are created"],
    ["Segment labels", "ADDSEGMENTLABELS ↵", "Bearing/distance on every segment (**ADDSEGMENTLABEL ↵** for one); updates with the line", { ribbon: "Civil 3D: Annotate › Add Labels › Line and Curve" }],
    ["Reset Label", "select label › right-click", "Put a dragged label back where its style places it"],
    ["Edit a style", "select › right-click › Edit ... Style", "Change every object or label that uses the style"],
    ["Point / Line-Curve tables", "Annotate › Add Tables", "Coordinate table; tag mode swaps crowded labels for L1, C1 tags"],
  ] },
  { type: "cmds", title: "Surfaces, parcels and QC", note: "check what the field data built", rows: [
    ["Surfaces", "Prospector › Surfaces", "TIN from points and breaklines; contours are a style setting. Turn on Triangles to find bad connections"],
    ["Breaklines / boundaries", "Surface › Definition", "Breaklines follow curbs and walls; Outer boundary limits it, Hide cuts buildings out"],
    ["Rebuild", "right-click surface › Rebuild", "Out-of-date surface (flag in Prospector); or tick Rebuild - Automatic"],
    ["Level of detail", "LEVELOFDETAIL ↵ / LEVELOFDETAILOFF ↵", "On: zoomed-out surfaces draw fewer contours and triangles (faster; plots stay full). Contours missing on a big surface? Turn it off", { ribbon: "Civil 3D: View › Views" }],
    ["OBJECTVIEWER", "OBJECTVIEWER ↵", "Look at a surface or object in 3D in its own window", { ribbon: "Civil 3D: View › Palettes" }],
    ["Inquiry Tool", "Analyze › Inquiry › Inquiry Tool", "Point and surface inquiries (elevation, slope), copy to Excel"],
    ["Create parcel", "CREATEPARCELFROMOBJECTS ↵", "Parcel with area from a closed polyline; keep parcels in their own site", { ribbon: "Civil 3D: Home › Create Design › Parcel", survey: "derived", warn: "Uncheck 'Erase existing entities' to keep the polyline" }],
    ["Mapcheck", "Analyze › Ground Data › Survey ▾ › Mapcheck", "Closure error, precision and area from labels, parcels or figures"],
    ["OFFSETFEATURE", "OFFSETFEATURE ↵", "Offset a feature line, figure, 2D or 3D polyline with an elevation change; makes a feature line (core OFFSET can't do 3D polylines)", { ribbon: "Civil 3D: Home › Create Design › Feature Line ▾", survey: "derived" }],
    ["3D polyline linetypes", "LINETYPE3DPLINEON ↵", "Show dashed linetypes on 3D polylines; **LINETYPE3DPLINEOFF ↵** back. Civil 3D only (Autodesk KB); pattern follows the 3D length, PLINEGEN ignored"],
  ] },
  { type: "box", title: "FIELD TO FINISH AWARENESS", lines: [
    "The crew's codes drive the linework. PMX-Universal linework codes: **B** begin, **E** end, **C** close, **P**/**T** curve, **U** continue, **G** rectangle, **CIR** circle, **X** extend. The figure prefix database (Civil3D parametrix) sets each figure's layer and style: EC → V-SURF-CONC-E, RWC → V-SURF-WALL-CONC-E; EC1 and EC2 are separate strings. Watch for jumpers from out-of-sequence shots, code typos (**RWB1 B RW2 B**), zig-zags from two strings on one code, missing closes. Reprocessing linework rebuilds figures from the survey database, so drawing-only edits can be lost: fix the linework and tell the data processor.",
  ] },
] };

// ================================================================================================
const S6 = { n: 6, title: "ADVANCED / POWER DRAFTER COMMANDS", tag: "ADVANCED", advanced: true, sub: "Not day one, but a strong drafter uses them", blocks: [
  { type: "box", kind: "warn", title: "⚠ DESTRUCTIVE: SAVE FIRST, SELECT A SMALL AREA, CHECK WITH LIST", lines: [
    "**OVERKILL** merges, splits and rejoins polylines as well as deleting duplicates: not routine cleanup.  **FLATTEN** throws away elevations.  **EXPLODE** destroys blocks and Civil 3D objects.  **LAYDEL** deletes everything on a layer.  **LAYMRG** deletes the first layer.  **MAPCLEAN** edits geometry in place.",
  ] },
  { type: "cmds", title: "Geometry", rows: [
    ["PEDIT Multiple", "PE ↵ M ↵ ... Y ↵ J ↵", "Lines into polylines, joined within a fuzz distance", { ribbon: "Home › Modify ▾", survey: "care", warn: "Keep the fuzz small (0.01)" }],
    ["REVERSE", "REVERSE ↵", "Flip a polyline's direction (upside-down linetype text)", { ribbon: "Home › Modify ▾", survey: "safe" }],
    ["PLINEGEN", "PLINEGEN ↵ 1 ↵", "Linetype runs continuously past vertices (short fence legs)", { survey: "safe" }],
    ["ALIGN", "AL ↵", "Move and rotate by two point pairs", { ribbon: "Home › Modify ▾", survey: "draft", warn: "Answer N to scale objects" }],
    ["BOUNDARY", "BO ↵", "Closed polyline from inside an enclosed area (area checks)", { ribbon: "Home › Draw (Hatch ▾)", survey: "derived" }],
    ["DIVIDE / MEASURE", "DIV ↵ / ME ↵", "Points or blocks at equal parts / a set spacing", { ribbon: "Home › Draw ▾", survey: "derived" }],
    ["OVERKILL", "OVERKILL ↵", "Delete duplicate and overlapping lines", { ribbon: "Home › Modify ▾", survey: "care", warn: "Save; tick 'Do not break polylines'; a window, never ALL" }],
    ["MAPCLEAN", "MAPCLEAN ↵", "Drawing Cleanup: duplicates, undershoots, dangles within a tolerance", { survey: "care", warn: "Save first; one action at a time" }],
    ["FLATTEN", "FLATTEN ↵", "Project everything to 2D (Express Tools)", { survey: "never", warn: "Destroys elevations" }],
    ["EXPLODE", "X ↵", "Break a block or polyline into pieces", { survey: "never", warn: "Never on Civil 3D points, labels, surfaces, parcels" }],
  ] },
  { type: "cmds", title: "Blocks, text and display order", rows: [
    ["BEDIT / ATTSYNC", "BE ↵ / ATTSYNC ↵", "Edit a block definition / update attributes after", { ribbon: "Insert › Block Definition", warn: "BEDIT changes every copy of that block" }],
    ["BURST", "BURST ↵", "Explode a block, keep attribute values as text (Express Tools)", { warn: "The block is gone: later block edits won't reach it" }],
    ["NCOPY", "NCOPY ↵", "Copy linework out of an xref or block (Express Tools)", { survey: "derived" }],
    ["TEXTTOFRONT / DRAWORDER", "TEXTTOFRONT ↵ / DR ↵", "Text and dims over hatches / one object to front or back", { ribbon: "Home › Modify ▾ (Draw Order ▾)" }],
    ["Wipeout frames", "WIPEOUT ↵ F ↵", "On / Off / Display but not plot. TFRAMES ↵ toggles (Express)"],
    ["TXT2MTXT / TCASE", "TXT2MTXT ↵ / TCASE ↵", "Combine TEXT lines into MTEXT (Insert › Import › Combine Text) / change case"],
  ] },
  { type: "side", left: { type: "pairs", title: "Precision tricks (at a point prompt)", code: true, split: 0.3, rows: [
    ["M2P", "Midpoint between two picked points"],
    ["FROM", "From a base point: FROM, pick, @dx,dy"],
    ["APP / EXT / PAR", "Apparent intersection / extension / parallel"],
    [".XY", "Snap X,Y from one place, then type the Z"],
    ["'Z", "Zoom in the middle of another command"],
  ] }, right: { type: "pairs", title: "Keys worth knowing", code: true, split: 0.3, rows: [
    ["F8 / F9 / F10", "Ortho / snap mode / polar tracking"],
    ["F11 / F12", "Object snap tracking / dynamic input"],
    ["F2", "Command history window"],
    ["CTRL+0", "Clean screen on / off"],
    ["CTRL+8", "QUICKCALC: calculator that picks points"],
  ] } },
] };

// ================================================================================================
const S7 = { n: 7, title: "PROJECT REFERENCES AND FILE TOOLS", tag: "ADVANCED", advanced: true, sub: "Where project data lives", blocks: [
  { type: "box", title: "XREF, DATA SHORTCUT, SOURCE DRAWING", lines: [
    "__Xref__ = another drawing shown in this one (layers read NAME|LAYER).  __Data shortcut__ = one Civil 3D object referenced read-only from another drawing: surfaces, alignments, profiles, sample lines, corridors, pipe networks. COGO points and point groups can't be shortcut.  __Source drawing__ = where the object actually lives; only there can its geometry change. Here you can change its style, layer and labels.  !!If an object is referenced, find the source before trying to fix it locally.!!",
  ] },
  { type: "cmds", title: "Xrefs and data shortcuts", note: "data shortcut items are mostly right-click in Prospector › Data Shortcuts", rows: [
    ["XREF", "XR ↵", "Xref palette: status (Loaded, Not Found, Needs Reloading); Reload, Unload, Detach"],
    ["XATTACH", "XA ↵", "Attach a drawing; Overlay doesn't carry nested xrefs; relative paths for projects that move"],
    ["XCLIP", "XCLIP ↵", "Clip an xref to a boundary"],
    ["Open Xref", "select xref › right-click", "Open the source drawing to edit it"],
    ["Working / shortcuts folder", "SETWORKINGFOLDER ↵", "Parent folder of shortcut projects. **SETSHORTCUTSFOLDER ↵** picks this drawing's project", { ribbon: "Civil 3D: Manage › Data Shortcuts" }],
    ["Create data shortcuts", "CREATEDATASHORTCUTS ↵", "Publish objects from the source drawing", { ribbon: "Civil 3D: Manage › Data Shortcuts" }],
    ["Create Reference", "Prospector › shortcut › right-click", "Bring a read-only copy into this drawing"],
    ["Validate Data Shortcuts", "right-click Data Shortcuts", "Check every shortcut still points to a real source (top node)"],
    ["Synchronize", "right-click out-of-date reference", "Pull in the source's latest geometry (or the status bar balloon)"],
    ["Repair Broken References", "right-click broken reference", "Browse to the moved or renamed source drawing"],
    ["Open Source Drawing", "right-click the reference", "Go edit it where it lives"],
    ["Promote", "right-click the reference", "Make it a local editable object", { warn: "It stops updating: two versions of the truth" }],
  ] },
  { type: "cmds", title: "File tools", rows: [
    ["COPYBASE / PASTEORIG", "CTRL+SHIFT+C / PASTEORIG ↵", "Copy with a base point / paste at the same coordinates in another drawing", { ribbon: "Home › Clipboard" }],
    ["WBLOCK", "W ↵", "Write objects to a new, clean DWG", { ribbon: "Insert › Block Definition (Write Block)", warn: "Pick Retain, not Delete from drawing" }],
    ["COMPARE", "COMPARE ↵", "DWG Compare: highlight changes between two versions", { ribbon: "Collaborate › Compare" }],
    ["ETRANSMIT", "ETRANSMIT ↵", "Zip a drawing with its xrefs, fonts and plot styles", { ribbon: "App menu › Publish" }],
    ["EXPORTTOAUTOCAD", "EXPORTTOAUTOCAD ↵", "Separate plain-AutoCAD copy (prefix ACAD-) for outside users", { warn: "Keep working in the original" }],
    ["DXF for data collectors", "SAVEAS ↵", "Linework for the field crew: Tools › Options › DXF Options › Select objects, pick the linework, save as DXF. Or WBLOCK it to a DWG, then SAVEAS DXF", { warn: "WBLOCK alone writes a DWG, not a DXF" }],
    ["MAPIMPORT", "MAPIMPORT ↵", "Import GIS data: SHP files from ArcGIS (also MIF, TAB)", { ribbon: "Civil 3D: Insert › Import › Map Import", warn: "Check its coordinate system matches the drawing" }],
    ["EXPORTKML", "EXPORTKML ↵", "Export to Google Earth as .kml or .kmz (Civil 3D; needs a coordinate system). Map 3D: MAPEXPORT, Google KML", { ribbon: "Civil 3D: Toolbox › Miscellaneous Utilities" }],
  ] },
] };

// ================================================================================================
const S8 = { n: 8, title: "TROUBLESHOOTING: WHEN SOMETHING GOES WRONG", tag: "REFERENCE", advanced: true, blocks: [
  { type: "box", kind: "aid", title: "FIRST AID", lines: [
    "1  **ESC ESC**   2  Read the command line   3  **CTRL+Z** or **U ↵**   4  **Z ↵ E ↵** and **RE ↵**   5  Save, close, reopen; then restart AutoCAD",
  ] },
  { type: "diag", title: "By problem", rows: [
    ["I can't see it", ["Layer off or frozen (**LA ↵**)", "VP Freeze in this viewport", "Isolated or hidden (**UNISOLATEOBJECTS ↵**)", "Xref unloaded / Not Found (**XR ↵**)", "Civil 3D style set to <none>", "Point group order or out of date", "Broken or out-of-date data reference", "Plain POINTs: **PDMODE ↵ 3 ↵**", "Surface contours missing: **LEVELOFDETAILOFF ↵**", "Online map missing: coordinate system assigned? signed in? **GEOMAP ↵**"]],
    ["It won't plot", ["Layer no-plot or Defpoints", "Outside the plot area", "Viewport On (CTRL+1)", "Online map: capture it with **GEOMAPIMAGE ↵** (put it on its own layer, not 0)"]],
    ["It looks wrong", ["Linetype scale: object, **LTSCALE**, **PSLTSCALE**, **MSLTSCALE**", "**RE ↵** / **REGENALL ↵**", "Viewport scale", "Annotation scale (status bar)", "Label style; dragged label: right-click › Reset Label", "??? in a label = missing data", "Linetype text upside down: **REVERSE ↵**", "Faded: **LAYLOCKFADECTL** / **XDWGFADECTL**", "Tilted in 3D: **PLAN ↵ ↵**", "Wrong dimension value: text override or **DIMLFAC**", "??? fonts: missing SHX; change the style font (**ST ↵**) or **FONTALT**"]],
    ["It acts wrong: selecting", ["Picks replace each other: **PICKADD ↵ 2 ↵**", "Selected first but MOVE asks again: **PICKFIRST ↵ 1 ↵**; still? **QAFLAGS ↵ 0 ↵** (undocumented; often left set by a LISP routine)", "No grips: **GRIPS ↵ 2 ↵**", "Window needs a drag: **PICKDRAG ↵ 2 ↵**", "Can see it, can't pick it: locked layer (**LAYULK ↵**), inside a block or xref, or you're on paper"]],
    ["It acts wrong: screen", ["Command line: **CTRL+9**", "Toolspace: **SHOWTS ↵**", "Ribbon: **RIBBON ↵**", "Tabs: **LAYOUTTAB ↵ 1 ↵**, **FILETAB ↵**", "**MENUBAR ↵ 1 ↵**", "Crosshairs: **CURSORTYPE ↵ 0 ↵**, **GRAPHICSCONFIG ↵**; tilted or not matching a turned view: **SNAPANG**", "Workspace: gear › pick it again; Options › Profiles › Reset"]],
    ["It acts wrong: keys and mouse", ["F8 ortho, F9 snap mode, F10 polar, F11 tracking, F12 dynamic input", "Wheel: **MBUTTONPAN ↵ 1 ↵**, **ZOOMWHEEL ↵ 0 ↵**; zoom step: **ZOOMFACTOR**", "Double-click: **DBLCLKEDIT ↵ ON ↵**"]],
    ["It acts wrong: drawing and typing", ["No dialogs: **FILEDIA ↵ 1 ↵**, **ATTDIA ↵ 1 ↵**", "Lines pick up elevations: **OSNAPZ**, **ELEV ↵ 0 ↵**", "Typed point lands wrong: use **#E,N**", "TRIM/EXTEND: **TRIMEXTENDMODE ↵ 0 ↵** for Standard", "Hatch gap: **HPGAPTOL**", "Block 12× off: **INSUNITS**", "Paste lands off site: **PASTEORIG ↵**", "Odd command: **REDEFINE ↵**, **INPUTSEARCHOPTIONS ↵**", "Mirrored text: **MIRRTEXT ↵ 0 ↵**", "Thick lines: **LWDISPLAY ↵ 0 ↵**; outline fills: **FILLMODE ↵ 1 ↵**"]],
    ["Civil 3D didn't update", ["Surface: right-click › Rebuild", "Point group: Update", "Reference: Synchronize", "'BD needs a running command; 'NE order: Ambient Settings"]],
    ["Slow or damaged drawing", ["**AUDIT ↵ Y ↵**", "**RECOVER ↵** (or rename the .bak)", "**PU ↵** PURGE incl. orphaned data", "**PURGESTYLES ↵**", "Regapps: **-PU ↵ R ↵ ↵ N ↵**", "**-SCALELISTEDIT ↵ R ↵ Y ↵ E ↵**", "Unload unused xrefs; surface triangles off; fewer point labels", "Still bad: **WBLOCK** the good objects to a new file"]],
    ["Crash, locks, sharing", ["**DRAWINGRECOVERY ↵**; autosave in **SAVEFILEPATH**, rename .sv$ to .dwg; **SAVETIME ↵ 10 ↵**", "Read-only: someone has it open (.dwl)", "Can't be opened elsewhere: SAVEAS older version, **EXPORTTOAUTOCAD**; proxy objects = made in Civil 3D"]],
  ] },
  { type: "sysvars", title: "System variables", note: "type the name, then the value", rows: [
    ["LTSCALE", "1", "Global linetype scale", "Training drawing: 20 (linetypes in plotted inches for 1\" = 20')"],
    ["PSLTSCALE", "1", "1: dashes same paper size in every viewport. 0: dashes follow LTSCALE in model units", "Training drawing: 0 with LTSCALE 20. Match the Parametrix template"],
    ["MSLTSCALE", "1", "Model tab linetypes scale with the annotation scale", ""],
    ["CELTSCALE", "1", "Linetype scale for new objects (multiplies LTSCALE)", "Leave at 1"],
    ["OSNAPZ", "0", "0: snaps use the point's Z. 1: use the current ELEV", "Not saved: resets each session", "Survey Nodes carry elevations: at 0 a line snapped to them gets their Z"],
    ["PICKADD / PICKFIRST", "2 / 1", "PICKADD 0: a pick replaces the selection; 1/2: picks add. PICKFIRST 1: pick objects first, then the command", ""],
    ["MIRRTEXT", "0", "0: mirrored text stays readable", ""],
    ["FILEDIA", "1", "0 = command-line prompts instead of file dialogs", "Scripts sometimes leave it at 0"],
    ["LWDISPLAY", "0", "Show lineweights on screen (they plot either way)", ""],
    ["LAYLOCKFADECTL / XDWGFADECTL", "50 / 50", "Fade of locked layers / xrefs (−90 to 90; negative = off)", ""],
    ["ZOOMFACTOR", "60", "How far each wheel step zooms (3 to 100; higher = bigger steps)", "Personal preference"],
    ["SELECTIONANNODISPLAY", "1", "Selecting annotative text shows its other scale versions dimmed. 0 = only the current scale", ""],
  ] },
] };

module.exports = { TITLE, SUBTITLE, FOOTER, FILE, COVER, LEVELS: [S1, S2, S3, S4, S5, S6, S7, S8] };
