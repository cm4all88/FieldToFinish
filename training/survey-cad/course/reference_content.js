// Content for the Survey CAD Drafter Reference (rendered by build_reference.js).
// Markup: **type this**  !!warning!!  __bold__.  ↵ = Enter.
// Row options: ribbon (grey location line), warn (red line), survey: ok | draft | never | care | view.
// Ribbon locations: AutoCAD 2D Drafting & Annotation workspace unless the row says Civil 3D. ▾ = drop-down / expanded panel.
// Every Civil 3D command and every correction was checked against help.autodesk.com (2024-2026); see REFERENCE_SOURCES.md.

const TITLE = "Survey CAD Drafter Reference";
const SUBTITLE = "New drafter → productive survey drafter → strong AutoCAD drafter → Civil 3D survey technician";
const FOOTER = "Parametrix Survey CAD Drafter Reference";
const FILE = "SURVEY_CAD_Drafter_Reference";

const COVER = [
  { type: "box", title: "HOW TO USE THIS REFERENCE", lines: [
    "- __New drafter:__ work Levels 1 to 5 in order. Do every EXERCISE before you move on. Each level ends with what you should be able to do.",
    "- __Everyone else:__ Levels 6 to 9 are the desk reference. Level 9 starts from the problem you're seeing.",
    "- **↵** = press Enter (Space works too).  **BOLD MONO** = type it exactly.  Grey text after a description = where its button is. Plain italics in the Type column = click through the menus, don't type it.  ▾ = drop-down or expanded panel.",
    "- !!Red!! = warning or destructive.  __On survey data?__ column: __Yes__, __Drafted items only__ (text, symbols, title block), __Never__.",
    "- __Parametrix / training__ notes are settings from the Survey CAD training drawings or the office field-code files. Where a note says 'training drawing', confirm it against the current Parametrix template before you treat it as office standard.",
  ] },
  { type: "triple", title: "THE PATH", heads: ["Level", "You will be able to", "Key tools"], widths: [2700, 4000, 3660], rows: [
    ["1  Work safely in a drawing", "Open a survey drawing, move around, inspect it, and not damage it", "Zoom, select, OSNAP, layers, LIST, ID, DIST"],
    ["2  Basic production drafting", "Clean up and complete ordinary survey linework", "Grips, FILLET, TRIM, EXTEND, OFFSET, JOIN"],
    ["3  Annotation and labeling", "Turn field information into a readable topo", "MLEADER, MTEXT, TEXT, DIMALIGNED, TORIENT"],
    ["4  Civil 3D survey production", "Work with points, styles and labels instead of dumb linework", "Toolspace, COGO points, 'BD, 'PN, segment labels"],
    ["5  Sheets, viewports, plotting", "Turn a finished drawing into a deliverable sheet", "MVIEW, viewport scale and lock, PLOT"],
    ["6  Higher level drafting", "Work faster and fix complicated problems without blind edits", "Layer tools, QSELECT, PEDIT, blocks, OVERKILL"],
    ["7  Higher level Civil 3D", "Diagnose what you see and make the right kind of edit", "Point groups, description keys, surfaces, parcels"],
    ["8  Project references", "Know where project data lives before you edit it", "Xrefs, data shortcuts, source drawings"],
    ["9  Troubleshooting and health", "Fix what looks wrong and keep drawings healthy", "Diagnostic checklists, AUDIT, PURGE, settings"],
  ] },
  { type: "box", kind: "warn", title: "⚠ GOLDEN RULES", lines: [
    "- Survey linework and points sit on real coordinates. Never MOVE, SCALE, ROTATE, STRETCH, FLATTEN or EXPLODE them.",
    "- SAVEAS your own copy before you practice on a job drawing. Save before PURGE, AUDIT, OVERKILL, MAPCLEAN or LAYDEL.",
    "- If a Civil 3D object looks wrong, don't explode or redraw it. Check the object, its style, label style, layer and source first.",
    "- If an object is referenced (xref or data shortcut), fix it in its source drawing, not here.",
    "- Lock every viewport once its scale is set.  CTRL+Z undoes almost anything; OOPS brings back only the last erase.",
  ] },
];

// ------------------------------------------------------------------------------------------------
const L1 = { n: 1, title: "WORK SAFELY IN A DRAWING", goal: "I can open a real survey drawing, navigate it, inspect it and not damage it.", blocks: [
  { type: "box", title: "HOW AUTOCAD TALKS TO YOU", lines: [
    "- Type a command (or its short alias) and press **↵**. Then __read the command line__: it tells you what it wants next.",
    "- Options appear in [brackets]. Type the capital letter of the option and **↵**, or click it.",
    "- **ESC** cancels. **↵** or Space on an empty line repeats the last command. **CTRL+Z** undoes.",
  ] },
  { type: "cmds", title: "Open, move around, save", rows: [
    ["OPEN / SAVEAS", "OPEN ↵ / SAVEAS ↵", "Open a drawing / save a copy under a new name", { ribbon: "Quick Access toolbar", warn: "Practicing? SAVEAS your own copy first" }],
    ["QSAVE", "CTRL+S", "Save" ],
    ["ZOOM Extents", "Z ↵ E ↵", "See the whole drawing (or double-click the wheel)"],
    ["ZOOM Window / Previous", "Z ↵ W ↵ / Z ↵ P ↵", "Zoom to a box / back to the last view"],
    ["Pan", "hold the wheel", "Drag the view. The wheel alone zooms at the cursor"],
    ["REGEN", "RE ↵", "Redraw: fixes arcs that look like polygons"],
    ["Undo / Redo", "CTRL+Z / CTRL+Y", "Step back / forward. **U ↵** undoes one step"],
    ["Command line", "CTRL+9", "Show or hide the command line"],
    ["PROPERTIES", "CTRL+1", "Palette showing layer, color, length, elevation... of what's selected"],
    ["Switch drawings", "CTRL+TAB", "Next open drawing"],
  ] },
  { type: "cmds", title: "Selecting objects", note: "see what you picked before you change it", rows: [
    ["Pick", "click", "Select one object. Keep clicking to add more"],
    ["Window (left → right)", "click, move right, click", "__Blue, solid box.__ Selects only objects __completely inside__"],
    ["Crossing (right → left)", "click, move left, click", "__Green, dashed box.__ Selects anything the box __touches__"],
    ["Lasso", "click and drag", "Freehand: drag right = window, drag left = crossing. Space cycles Window / Crossing / Fence"],
    ["Remove from selection", "Shift + pick", "Take objects back out (Shift + box works too)"],
    ["Clear selection", "ESC", "Deselect everything"],
    ["At a Select objects: prompt", "P  L  ALL  F  WP  CP", "Previous set, Last drawn, everything, Fence line, Window/Crossing Polygon"],
    ["Overlapping objects", "CTRL+W", "Selection cycling on: pick from a list of what's under the cursor"],
  ] },
  { type: "cmds", title: "Object snaps", note: "snap to real geometry, never eyeball a point", rows: [
    ["OSNAP", "OS ↵ / F3", "Running snaps: pick which ones are on / turn them all on and off", { ribbon: "Status bar" }],
    ["One-time snap", "Shift + right-click", "Snap menu for the next pick only"],
    ["Node", "NOD", "A survey point (COGO point or POINT)", { warn: "Points carry elevations: see OSNAPZ, Level 9" }],
    ["Endpoint", "END", "End of a line, arc or polyline segment"],
    ["Insertion", "INS", "A block's or text's base point (symbols)"],
    ["Midpoint", "MID", "Middle of a segment"],
    ["Intersection", "INT", "Where two objects cross"],
    ["Perpendicular", "PER", "Square off to a line (setbacks)"],
    ["Nearest", "NEA", "Anywhere on a line"],
  ] },
  { type: "box", title: "LAYERS: HOW A SURVEY DRAWING IS ORGANIZED", lines: [
    "- Every object sits on a layer. Layers control __visibility__ (on/off, freeze), __plotting__ (plot/no-plot, color, lineweight) and __organization__.",
    "- Parametrix survey layers start with **V-** (the survey/mapping discipline): V-NODE-E points, V-PROP-BNDY-E boundary, V-UTIL-STRM-SYMB-E storm symbols, V-TREE-TEXT tree labels. The last field is often the status: -E existing.",
    "- Keep color, linetype and lineweight __ByLayer__. Don't draw on layer 0 or Defpoints (Defpoints never plots).",
    "- __Off__ hides a layer. __Freeze__ hides it and skips it in regens (faster). Neither deletes anything.",
  ] },
  { type: "cmds", title: "Layers and inspecting", rows: [
    ["LAYER", "LA ↵", "Layer Properties Manager: on/off, freeze, lock, plot, current", { ribbon: "Home › Layers" }],
    ["Layer drop-down", "", "Set current layer, flip on/off and freeze quickly", { ribbon: "Home › Layers" }],
    ["LAYFRZ", "LAYFRZ ↵", "Freeze the layer of whatever you pick", { ribbon: "Home › Layers" }],
    ["LAYMCUR", "LAYMCUR ↵", "Make the picked object's layer current", { ribbon: "Home › Layers" }],
    ["ID", "ID ↵", "Coordinates of a point: X = Easting, Y = Northing, Z = elevation", { ribbon: "Home › Utilities ▾" }],
    ["LIST", "LI ↵", "Everything about an object: layer, bearing, length, area, closed or not", { ribbon: "Home › Properties ▾" }],
    ["DIST", "DI ↵", "Distance and angle between two picked points", { ribbon: "Home › Utilities › Measure" }],
    ["MEASUREGEOM", "MEA ↵", "Distance, radius, angle, area; Quick mode shows dimensions as you hover", { ribbon: "Home › Utilities › Measure" }],
    ["UNITS", "UN ↵", "How lengths and angles display and are typed (Surveyor's units, 0.00')", { ribbon: "App menu › Drawing Utilities", warn: "Display only. It does NOT set the Civil 3D coordinate system: Level 4" }],
  ] },
  { type: "pairs", title: "Typing coordinates and bearings", code: true, split: 0.36, rows: [
    ["#1125170.86,733969.17", "Absolute point: Easting,Northing (the # beats dynamic input's relative default)"],
    ["@25,0", "Relative: 25' east of the last point"],
    ["@249.14<S88d24'53\"E", "Distance and bearing from the last point. No spaces. Needs Surveyor's units"],
    ["@", "Alone: the last point picked"],
  ] },
  { type: "exercises", items: [
    { title: "Look, don't touch", steps: [
      "**OPEN ↵** the job drawing, then **SAVEAS ↵** a practice copy with your initials",
      "**Z ↵ E ↵**. Zoom in with the wheel and pan with the wheel held down",
      "**ID ↵**, Node snap on a control point. Compare X,Y with its northing/easting",
      "**LI ↵**, pick a boundary line: read its bearing and length",
      "**DI ↵** between two monuments (Node)",
      "Pick a curb line, **CTRL+1**: note its layer",
    ], check: "You changed nothing: **CTRL+Z** has nothing to undo but zooms." },
    { title: "Layers and selecting", steps: [
      "**LAYFRZ ↵**, pick a tree symbol: every tree layer object disappears",
      "**LA ↵**, find that V- layer, thaw it (snowflake icon)",
      "Draw a box __left to right__ around half a building: only fully enclosed lines highlight",
      "Now __right to left__ over the same area: everything touched highlights",
      "Shift + pick one line to remove it, then **ESC**",
      "**OS ↵**: turn on Endpoint, Node, Insertion, Midpoint, Perpendicular, Nearest",
    ], check: "You can explain why the two boxes selected different objects." },
  ] },
] };

// ------------------------------------------------------------------------------------------------
const L2 = { n: 2, title: "BASIC PRODUCTION DRAFTING", goal: "I can clean up and complete ordinary survey linework.", blocks: [
  { type: "box", kind: "warn", title: "⚠ WHAT YOU MAY EDIT, AND WHAT YOU MAY NOT", lines: [
    "- __Coordinate-controlled survey objects__ (COGO points, control, monuments, boundary and lot lines from calcs, field-to-finish linework) are where the field crew measured them. Close gaps and trim overshoots __between their own points__; never move, scale, rotate or stretch them.",
    "- __Drafted items__ (text, leaders, symbols' rotation, north arrow, title block, notes) are yours to move, copy, rotate and scale.",
    "- Not sure which one you're looking at? **LI ↵** it: layer and type tell you. A COGO point lists as a Civil 3D object.",
  ] },
  { type: "cmds", title: "Draw", rows: [
    ["LINE", "L ↵", "Lines; type **@dist<bearing** or snap point to point", { ribbon: "Home › Draw", survey: "ok" }],
    ["PLINE", "PL ↵", "One connected polyline, Node to Node. **C ↵** closes it", { ribbon: "Home › Draw", survey: "ok" }],
    ["OFFSET", "O ↵", "Parallel copy at a distance (curb, ROW, setback lines). **L ↵ C ↵** puts copies on the current layer", { ribbon: "Home › Modify", survey: "ok" }],
  ] },
  { type: "cmds", title: "Edit", rows: [
    ["FILLET radius 0", "F ↵ R ↵ 0 ↵", "Close a corner exactly: both lines end at their intersection", { ribbon: "Home › Modify (Fillet ▾)", survey: "ok" }],
    ["TRIM", "TR ↵", "Cut off an overshoot at the line it crosses. Shift + pick extends instead", { ribbon: "Home › Modify", survey: "ok" }],
    ["EXTEND", "EX ↵ B ↵", "Grow a line to a chosen boundary (B = pick the boundary first)", { ribbon: "Home › Modify (Trim ▾)", survey: "ok" }],
    ["JOIN", "J ↵", "Join touching lines/arcs into one polyline", { ribbon: "Home › Modify ▾", survey: "ok" }],
    ["LENGTHEN", "LEN ↵ DE ↵ / T ↵", "Add a length (Delta) or set a Total length", { ribbon: "Home › Modify ▾", survey: "care" }],
    ["BREAKATPOINT", "BREAKATPOINT ↵", "Split a line or polyline in two at one point", { ribbon: "Home › Modify ▾", survey: "ok" }],
    ["ERASE", "E ↵", "Delete", { ribbon: "Home › Modify", survey: "care", warn: "OOPS undoes the last erase" }],
    ["OOPS", "OOPS ↵", "Bring back the last erased objects, even after other commands"],
    ["MOVE", "M ↵", "Move by base point and second point", { ribbon: "Home › Modify", survey: "draft", warn: "Never on survey linework or points" }],
    ["COPY", "CO ↵", "Copy by base point and second point", { ribbon: "Home › Modify", survey: "draft" }],
    ["ROTATE", "RO ↵", "Rotate about a base point. **R ↵** Reference lines a symbol up with linework", { ribbon: "Home › Modify", survey: "draft", warn: "Symbols and text only" }],
    ["SCALE", "SC ↵", "Resize about a base point", { ribbon: "Home › Modify", survey: "never" }],
    ["STRETCH", "S ↵", "Crossing window (right to left) moves the vertices inside it", { ribbon: "Home › Modify", survey: "draft", warn: "Objects fully inside the window move completely" }],
    ["MIRROR", "MI ↵", "Mirror copy. Text stays readable while MIRRTEXT = 0", { ribbon: "Home › Modify", survey: "draft" }],
  ] },
  { type: "cmds", title: "Grips: edit without starting a command", note: "select an object, then use its blue boxes", rows: [
    ["Hot grip", "click a grip", "It turns red: drag to stretch it. Snaps and typed points still work"],
    ["Grip modes", "Space", "With a hot grip: cycles Stretch → Move → Rotate → Scale → Mirror"],
    ["Copy with grips", "hold CTRL", "While moving a hot grip: leaves copies"],
    ["Polyline vertex grip", "hover", "Menu: Stretch vertex, Add vertex, Remove vertex"],
    ["Polyline midpoint grip", "hover", "Menu: Stretch, Add vertex, Convert to arc"],
    ["Text / block grip", "drag", "Moves text or a symbol by its insertion point", { survey: "draft" }],
    ["Line endpoint grip", "drag", "Moves only that end", { warn: "On survey linework that's a move: snap it back to the Node" }],
  ] },
  { type: "exercises", items: [
    { title: "FILLET R0: close a corner", steps: [
      "**F ↵** then **R ↵ 0 ↵** (the radius sticks until you change it)",
      "Pick the first line on the part you want to __keep__",
      "Pick the second line on the part you want to __keep__",
    ], check: "**LI ↵** both lines: they end at the same point.", tip: "Hold Shift on the second pick for an R0 corner whatever the radius." },
    { title: "EXTEND and TRIM", steps: [
      "**EX ↵ B ↵**, pick the line to extend TO, **↵**",
      "Pick the short line near the end that should grow, **↵**",
      "**TR ↵**, pick the overshoot on the side to remove, **↵**",
    ], check: "No gap, no overshoot when you zoom right in.", tip: "Since AutoCAD 2021 TRIM/EXTEND use Quick mode: every object is a boundary until you pick B." },
    { title: "JOIN the boundary and check the area", steps: [
      "**J ↵**, select all four boundary lines, **↵**",
      "**LI ↵**, pick the boundary",
      "Read: Closed, Area, Perimeter",
    ], check: "It lists as one closed polyline with an area.", tip: "Not closed? A corner still has a gap: FILLET R0 it." },
    { title: "ROTATE by Reference: square a symbol to the curb", steps: [
      "**RO ↵**, pick the catch basin symbol, **↵**",
      "Base point: Insertion snap on the symbol",
      "**R ↵**, type **0 ↵** (the symbol sits at 0 now)",
      "**P ↵**, pick two points along the curb (Nearest)",
    ], check: "The symbol edge runs parallel to the curb.", tip: "180° off? Pick the two curb points the other way." },
  ] },
] };

// ------------------------------------------------------------------------------------------------
const L3 = { n: 3, title: "ANNOTATION AND SURVEY LABELING", goal: "I can turn imported field information into a readable survey topo.", blocks: [
  { type: "box", title: "LABELING STANDARDS", lines: [
    "- Text reads from the __bottom or right side__ of the sheet. Never upside down.",
    "- Size by the plotted height: 0.08\" plotted = 1.6' at 1\" = 20' (training drawing text height and leader/dimension style SRV-20).",
    "- Label layers: the feature's V- text layer (V-TREE-TEXT, V-UTIL-STRM-TEXT, V-CTRL-TEXT in the office field-code rules). Dimensions on V-ANNO-DIMS-E in the training drawing.",
    "- Leaders don't cross each other, the text, or other linework. Background masks keep text readable over hatches.",
  ] },
  { type: "cmds", title: "Text, leaders and dimensions", rows: [
    ["MTEXT", "MT ↵", "Paragraph text: callouts, building notes. Background Mask in the editor", { ribbon: "Home › Annotation (Text ▾)" }],
    ["TEXT", "DT ↵ J ↵ BC ↵", "Single-line text; Bottom Center justification for text on a line", { ribbon: "Home › Annotation (Text ▾)" }],
    ["Degree sign", "%%d", "Typed in text: N88%%d24'53\"W"],
    ["MLEADER", "MLD ↵", "Arrow + landing + text: trees, structures, control", { ribbon: "Home › Annotation" }],
    ["MLEADEREDIT", "MLE ↵", "Add or remove leader arms on one label", { ribbon: "Annotate › Leaders (Add/Remove)" }],
    ["MLEADERALIGN", "MLA ↵", "Line leader labels up in a neat column", { ribbon: "Annotate › Leaders" }],
    ["DIMALIGNED", "DAL ↵", "Dimension parallel to what you measure (setbacks)", { ribbon: "Home › Annotation (Dim ▾)" }],
    ["MATCHPROP", "MA ↵", "Paint one object's properties onto others", { ribbon: "Home › Properties", warn: "Copies the layer too: S ↵ (Settings) to limit" }],
    ["TORIENT", "TORIENT ↵", "Flip upside-down text and leaders to read correctly (Express Tools)", { ribbon: "Express Tools › Text ▾" }],
    ["TJUST", "TJUST ↵", "Change justification without moving the text", { ribbon: "Express Tools › Text ▾" }],
    ["FIND", "FIND ↵", "Find and replace text anywhere in the drawing", { ribbon: "Annotate › Text" }],
    ["SCALETEXT", "SCALETEXT ↵", "Set many texts to one height", { ribbon: "Annotate › Text ▾" }],
    ["Civil 3D segment label", "ADDSEGMENTLABELS ↵", "Bearing/distance labels that follow the line if it changes (Level 4)", { ribbon: "Civil 3D: Annotate › Add Labels" }],
  ] },
  { type: "cmds", title: "Grips on annotation", rows: [
    ["Text grip", "drag", "Moves the text. MTEXT side grips set the column width"],
    ["MLEADER arrowhead grip", "drag", "Moves the arrow tip", { warn: "Leave it: it sits on the feature" }],
    ["MLEADER landing / text grip", "drag", "Moves text and landing; the arrow stays put"],
    ["Dimension text grip", "hover", "Menu: move with dim line, move text only, reset text position"],
  ] },
  { type: "triple", title: "Survey labeling tasks", heads: ["Task", "Tool", "How"], widths: [2200, 1700, FULL_W() - 3900], rows: [
    ["Tree", "MLEADER", "Arrow on the symbol: **22\" CFR / 25' DRIP** (trunk, type, drip). Clusters: **CLUSTER / 12\"(2) DEC / 14' DRIP**"],
    ["Catch basin / manhole", "MLEADER", "**CB #10012 / RIM=75.20 / IE 6\" PVC (W)=73.20 / BOTTOM=73.10**: one line each, inverts by direction"],
    ["Control point / monument", "MLEADER", "**PMX #2000 / MAG NAIL**.  Found: **FOUND IRON PIPE, / 0.8' ABOVE GRADE / (date)**"],
    ["Building", "MTEXT", "Inside the footprint: **HOUSE** or **GARAGE**, and **FFE=65.39**"],
    ["Edge of pavement, fences, walls", "MTEXT or linetype", "Callout parallel to the line (**EDGE OF PAVEMENT**, **4' CHAIN LINK FENCE**, **CONC RETWALL**), or the feature's text linetype"],
    ["Asphalt, concrete, grass", "MTEXT", "**ASPH**, **CONC**, **GRASS**, **CW**, **ASPH DW** centered in the area, background mask on"],
    ["Bearings and distances", "TEXT or segment label", "Bottom Center on the line's midpoint, rotated with the line; TORIENT if upside down"],
    ["Building setback", "DIMALIGNED", "Endpoint on the building corner, Perpendicular to the lot line"],
    ["Road name", "MTEXT", "Centered in the road, rotated with it, reading from the bottom or right"],
    ["Move a crowded label", "MLEADER grips", "Drag the landing or text grip, never the arrowhead"],
  ] },
  { type: "exercises", items: [
    { title: "Label a catch basin", steps: [
      "Set the leader style (training drawing: SRV-20) on Annotate › Leaders",
      "Make the storm text layer current (**LAYMCUR ↵**, pick an existing storm label)",
      "**MLD ↵**, Insertion snap on the CB symbol",
      "Pick the landing clear of other linework",
      "Type **CB #10012**, **↵**, then **RIM=75.20**, each invert, **BOTTOM=**, one per line",
      "Click outside the text box",
    ], check: "Arrow on the symbol, leader crosses nothing, text reads from the bottom." },
    { title: "Bearing and distance on a boundary line", steps: [
      "**DT ↵ J ↵ BC ↵**, Midpoint snap on the line",
      "Height **1.6 ↵** if asked (0.08\" at 1\" = 20')",
      "Rotation: pick two points along the line",
      "Type **N88%%d24'53\"W 249.14'** **↵ ↵**",
      "Upside down? **TORIENT ↵**, select it, **↵**",
    ], check: "The label matches **LI ↵** on that line.", tip: "Civil 3D segment labels do this automatically: Level 4." },
    { title: "Dimension a building setback", steps: [
      "Make the dimension layer current (training: V-ANNO-DIMS-E), style SRV-20",
      "**DAL ↵**, Endpoint on the house corner",
      "Shift + right-click › Perpendicular, pick the lot line",
      "Click to place the dimension off the building",
    ], check: "The dimension line is square to the lot line." },
    { title: "Reposition a leader, keep the arrow", steps: [
      "Click the leader: note its three grips",
      "Drag the __landing__ grip to open space",
      "Too long? Drag the text grip closer",
      "Two labels overlapping? **MLA ↵** to line them up",
    ], check: "The arrowhead never moved off the symbol." },
  ] },
] };


// ------------------------------------------------------------------------------------------------
const L4 = { n: 4, title: "SURVEY AND CIVIL 3D PRODUCTION", goal: "I can work intelligently in a Civil 3D survey drawing instead of treating everything like dumb AutoCAD entities.", blocks: [
  { type: "box", title: "A CIVIL 3D OBJECT IS NOT JUST LINEWORK", lines: [
    "- __COGO point__: a database object with a number, northing, easting, elevation and description. It is not a block or a POINT.",
    "- __Point style__ draws the marker (the symbol). __Point label style__ draws the text next to it (number, elevation, description).",
    "- __Point group__: a named set of points that can override their styles. __Description key__: turns a field code (the first word, like TREE or CB) into a style, label style and layer as points come in.",
    "- __Object style__ = how any Civil 3D object looks; __label style__ = what its label says. Change the style, not the geometry, when it only __looks__ wrong.",
    "- !!Never EXPLODE Civil 3D objects!!. The data is gone and nothing will update. Need plain AutoCAD for someone else? EXPORTTOAUTOCAD makes a separate copy.",
  ] },
  { type: "cmds", title: "Toolspace", note: "Civil 3D's control panel", rows: [
    ["SHOWTS", "SHOWTS ↵", "Opens Toolspace (Home › Palettes buttons also toggle each tab)", { ribbon: "Civil 3D: Home › Palettes" }],
    ["Prospector tab", "", "What's in this drawing: points, point groups, surfaces, sites, parcels, data shortcuts"],
    ["Settings tab", "", "Styles and label styles, description key sets, command settings, Edit Drawing Settings"],
    ["Survey tab", "", "Survey databases, figures, figure prefix and linework code sets (usually office-managed)"],
    ["Toolbox tab", "", "Reports and add-on tools"],
  ] },
  { type: "box", title: "UNITS IS NOT THE COORDINATE SYSTEM", lines: [
    "- AutoCAD **UNITS** only sets how lengths and angles display and are typed.",
    "- Civil 3D keeps its real settings in __Drawing Settings__: **EDITDRAWINGSETTINGS ↵** (or Settings tab › right-click the drawing name › Edit Drawing Settings).",
    "- __Units and Zone__ tab: drawing units, angular units, drawing (plot) scale, coordinate system zone.  __Transformation__ tab: grid/ground scale factors and rotation (available only after a zone is set).  __Ambient Settings__ tab: precision and how angles and directions are typed and shown.",
    "- !!Don't assign or change a coordinate system or transformation on a job drawing!!. It changes where everything is. Ask the project surveyor.",
  ] },
  { type: "cmds", title: "Transparent commands", note: "type while another command asks for a point", rows: [
    ["Bearing and distance", "'BD ↵", "Quadrant (1 NE, 2 SE, 3 SW, 4 NW), bearing, distance from the last point"],
    ["Azimuth and distance", "'ZD ↵", "Azimuth from north, then distance"],
    ["Northing / Easting", "'NE ↵", "Type coordinates. Northing first by default (Ambient Settings can swap it)"],
    ["Point number", "'PN ↵", "Use a COGO point by its number"],
    ["Point object", "'PO ↵", "Pick a COGO point (uses its location)"],
    ["Side shot", "'SS ↵", "Angle and distance off the line through the last two points"],
    ["Zoom to point", "'ZTP ↵", "Zoom to a point number or range; also works on its own"],
    ["Match radius / length", "'MR ↵ / 'ML ↵", "Take a radius or length from an existing object"],
  ] },
  { type: "box", title: "USING TRANSPARENT COMMANDS", lines: [
    "- They work only when a command is asking for a point: **L ↵**, then **'BD ↵**. A __Transparent__ ribbon tab appears while a command is running; right-click › Transparent Commands works too.",
    "- Bearings are typed in the drawing's angle format. Usually DD.MMSS: **88.2453** = 88°24'53\". Check Ambient Settings if it rejects your input.",
    "- They keep repeating: **ESC** ends the transparent command, **↵** ends the LINE.",
  ] },
  { type: "cmds", title: "Points and labels", rows: [
    ["CREATEPOINTS", "CREATEPOINTS ↵", "Point Creation toolbar: manual, on objects, from file", { ribbon: "Civil 3D: Home › Create Ground Data › Points" }],
    ["Import points", "IMPORTPOINTS ↵", "Points from a PNEZD text file into the drawing", { ribbon: "Civil 3D: Insert › Import › Points From File" }],
    ["Point Editor", "right-click point › Edit Points", "Spreadsheet-style editing of many points (Panorama)"],
    ["Lock / Unlock points", "Prospector › Points › right-click", "Protect control points from accidental edits"],
    ["Segment labels", "ADDSEGMENTLABELS ↵", "Bearing/distance on every segment of a line or polyline; updates with the geometry", { ribbon: "Civil 3D: Annotate › Add Labels › Line and Curve" }],
    ["Single segment label", "ADDSEGMENTLABEL ↵", "Label one segment where you pick it"],
    ["Reset Label", "select label › right-click", "Put a dragged label back where its style places it"],
  ] },
  { type: "box", title: "FIELD TO FINISH: WHAT YOU'RE LOOKING AT", lines: [
    "- The crew's descriptions carry codes. The office __linework code set__ (PMX-Universal) reads them: **B** begin, **E** end, **C** close, **P**/**T** start/end curve, **U** continue, **G** rectangle, **CIR** circle, **X** extend.",
    "- The __figure prefix database__ (Civil3D parametrix) turns each figure name into a layer and style: EC → edge of concrete, RWC → V-SURF-WALL-CONC-E. EC1 and EC2 are separate strings.",
    "- __Survey figures__ live in the survey database; plain polylines don't. Reprocessing linework in the Survey tab rebuilds figures, so edits made only in the drawing can be lost.",
    "- What to look for: jumpers from an out-of-sequence shot, a code typo (**RWB1 B RW2 B**), a zig-zag from two strings sharing one code, a missing close. Fix the linework, and tell the data processor so the code gets fixed too.",
  ] },
  { type: "exercises", items: [
    { title: "A line by bearing and distance", steps: [
      "**L ↵**, Node snap on the starting monument",
      "**'BD ↵**, **↵** to accept the start point",
      "Quadrant **3 ↵** (SW), bearing **88.2453 ↵**, distance **249.14 ↵**",
      "**ESC** ends 'BD, **↵** ends LINE",
    ], check: "**LI ↵** the line: S88°24'53\"W 249.14'.", tip: "Keep going: type the next quadrant for the next course of a traverse." },
    { title: "Inspect a COGO point", steps: [
      "**'ZTP ↵** or **ZTP ↵**, type the control point number",
      "Click the point, **CTRL+1**: number, northing, easting, elevation, raw and full description",
      "Note its point style and label style",
      "**SHOWTS ↵** › Prospector › Point Groups: find which groups hold it",
    ], check: "You can say which style draws its symbol and which draws its text." },
    { title: "Label a lot line with segment labels", steps: [
      "Annotate › Add Labels › Line and Curve › Add Multiple Segment Labels (**ADDSEGMENTLABELS ↵**)",
      "Pick the boundary polyline",
      "Too crowded? Drag a label; right-click › Reset Label to put it back",
    ], check: "Every side has bearing and distance matching **LI ↵**." },
  ] },
] };

// ------------------------------------------------------------------------------------------------
const L5 = { n: 5, title: "SHEETS, VIEWPORTS AND PLOTTING", goal: "I can finish a drawing and turn it into a deliverable sheet.", blocks: [
  { type: "box", title: "MODEL, PAPER, VIEWPORT", lines: [
    "- __Model Space__ = the real world at full size, in real coordinates. All survey drafting happens here.",
    "- __Paper Space__ (a layout tab) = the plotted sheet: title block, notes, north arrow, legend.",
    "- __Viewport__ = a window cut in the sheet looking into model space at a set scale. 1\" = 20' means 100' measures 5\" on paper.",
  ] },
  { type: "cmds", title: "Viewports and layouts", rows: [
    ["MVIEW", "MV ↵", "Make a viewport on the layout (pick two corners)", { ribbon: "Layout › Layout Viewports" }],
    ["MSPACE / PSPACE", "MS ↵ / PS ↵", "Work inside the viewport / back on the paper. Or double-click in / out", { ribbon: "Status bar MODEL / PAPER" }],
    ["Viewport scale", "Z ↵ 1/20XP ↵", "Inside the viewport: 1\" = 20'. Or pick the scale on the status bar", { ribbon: "Status bar scale list", warn: "Lock it or the next zoom ruins it" }],
    ["Lock viewport", "CTRL+1 › Display locked: Yes", "Zooming then zooms the sheet, not the viewport"],
    ["VPMAX / VPMIN", "VPMAX ↵", "Fill the screen with the viewport to work in it, then restore", { ribbon: "Status bar" }],
    ["VPLAYER / VP Freeze", "VPLAYER ↵", "Freeze a layer in one viewport only (VP Freeze column in LA)"],
    ["VPCLIP", "VPCLIP ↵", "Clip a viewport to a polyline shape", { ribbon: "Layout › Layout Viewports › Clip" }],
    ["CHSPACE", "CHSPACE ↵", "Move objects between paper and the viewport's model space, rescaled to look the same", { ribbon: "Home › Modify ▾ (Change Space)" }],
    ["SPACETRANS", "'SPACETRANS ↵", "Convert a height between paper and model (0.08\" = 1.6' at 1\" = 20')"],
    ["Turn the view", "UCS ↵ OB ↵ / PLAN ↵ ↵", "Line the viewport up with a road", { warn: "PLAN zooms extents: reset the scale, turn the north arrow" }],
    ["LAYOUT", "LAYOUT ↵ C ↵", "Copy a layout tab for another sheet"],
    ["PAGESETUP", "PAGESETUP ↵", "Save printer, paper, scale and plot style once per layout", { ribbon: "Output › Page Setup Manager" }],
    ["PLOT", "CTRL+P", "Plot the layout (DWG To PDF for a PDF)", { ribbon: "Output › Plot" }],
    ["PUBLISH", "PUBLISH ↵", "Plot many layouts or drawings to one PDF set", { ribbon: "Output › Batch Plot" }],
  ] },
  { type: "exercises", items: [
    { title: "Viewport at 1\" = 20'", steps: [
      "On the layout: **MV ↵**, pick two corners",
      "Double-click inside the viewport",
      "**Z ↵ 1/20XP ↵**, then pan with the wheel held down (don't zoom)",
      "Double-click outside, select the viewport border, **CTRL+1**, Display locked: Yes",
      "Put the viewport on a no-plot layer",
    ], check: "**DI ↵** on paper across a 100' line reads 5.00\"." },
    { title: "Plot to PDF", steps: [
      "On the layout tab: **CTRL+P**",
      "Printer: DWG To PDF.pc3; paper: the sheet size",
      "Plot area Layout, scale 1:1",
      "Plot style table: monochrome.ctb (training drawing; use the office table if different)",
      "Preview, then OK and name the file",
    ], check: "Everything plots black, nothing is cut off.", tip: "Save it all once in PAGESETUP; Apply to Layout." },
    { title: "Turn the view to line up with a road", steps: [
      "Double-click into the viewport: **UCS ↵ OB ↵**, pick the road centerline",
      "**PLAN ↵ ↵** (Current UCS)",
      "**Z ↵ 1/20XP ↵**, pan back to the site, lock the viewport",
      "Rotate the north arrow on the sheet to match (ROTATE Reference to the grid)",
      "Later reset: **UCS ↵ ↵** (World), **PLAN ↵ ↵**",
    ], check: "North arrow still points to real north." },
    { title: "CHSPACE: move a note onto the sheet", steps: [
      "Model to paper: double-click into the viewport, **CHSPACE ↵**, select the note, **↵**",
      "Paper to model: on the paper, **CHSPACE ↵**, select, **↵**, click in the target viewport, **↵**",
    ], check: "A 1.6' model text becomes 0.08\" on paper, and back." },
  ] },
] };

// ------------------------------------------------------------------------------------------------
const L6 = { n: 6, title: "HIGHER LEVEL DRAFTING", advanced: true, goal: "I work faster and solve harder drafting problems without blindly editing the drawing.", blocks: [
  { type: "box", kind: "warn", title: "⚠ DESTRUCTIVE COMMANDS: SAVE FIRST, WORK ON A SMALL SELECTION, CHECK WITH LIST", lines: [
    "- **OVERKILL** can merge, split and rejoin polylines as well as deleting duplicates. Not a routine cleanup: tick 'Do not break polylines', run it on a window, check the result.",
    "- **LAYDEL** deletes every object on the layer, inside blocks too. **LAYMRG** deletes the first layer. **EXPLODE** kills blocks and Civil 3D objects. **FLATTEN** throws away elevations. **MAPCLEAN** edits geometry in place.",
  ] },
  { type: "cmds", title: "Layer tools", rows: [
    ["LAYISO / LAYUNISO", "LAYISO ↵", "Show only the picked objects' layers / undo that", { ribbon: "Home › Layers", warn: "Run LAYUNISO before saving or the rest stay off" }],
    ["LAYOFF / LAYON", "LAYOFF ↵", "Turn a picked layer off / turn all layers on", { ribbon: "Home › Layers" }],
    ["LAYFRZ / LAYTHW", "LAYFRZ ↵ / LAYTHW ↵", "Freeze a picked layer / thaw every layer", { ribbon: "Home › Layers (▾)" }],
    ["LAYLCK / LAYULK", "LAYLCK ↵ / LAYULK ↵", "Lock / unlock a picked layer: can snap to it, can't edit it", { ribbon: "Home › Layers" }],
    ["LAYWALK", "LAYWALK ↵", "Step through layers one at a time to see what's on each", { ribbon: "Home › Layers ▾" }],
    ["LAYCUR", "LAYCUR ↵", "Move picked objects to the current layer", { ribbon: "Home › Layers ▾" }],
    ["LAYMCH", "LAYMCH ↵", "Move objects to another object's layer", { ribbon: "Home › Layers" }],
    ["COPYTOLAYER", "COPYTOLAYER ↵", "Copy objects onto another layer, originals untouched", { ribbon: "Home › Layers ▾" }],
    ["LAYMRG", "LAYMRG ↵", "Merge one layer into another", { ribbon: "Home › Layers ▾", warn: "The first layer is deleted; read the names before Y" }],
    ["LAYDEL", "LAYDEL ↵", "Delete a layer and everything on it", { ribbon: "Home › Layers ▾", warn: "Deletes every object on the layer, blocks included" }],
    ["LAYERP", "LAYERP ↵", "Undo the last layer setting change", { ribbon: "Home › Layers" }],
    ["LAYERSTATE", "LAYERSTATE ↵", "Save and restore on/freeze/color sets (topo, boundary, utility sheets)", { ribbon: "Home › Layers (Layer State ▾)" }],
    ["SETBYLAYER", "SETBYLAYER ↵", "Reset color, linetype and lineweight to ByLayer", { ribbon: "Home › Modify ▾", warn: "Answer N to 'blocks too' unless you mean it" }],
  ] },
  { type: "cmds", title: "Selecting smarter", rows: [
    ["QSELECT", "QSELECT ↵", "Find objects by property (e.g. everything on layer 0)", { ribbon: "Home › Utilities" }],
    ["SELECTSIMILAR", "SELECTSIMILAR ↵", "Select everything like the picked object (same type and layer)", { ribbon: "Right-click › Select Similar" }],
    ["FILTER", "FI ↵", "Selection filter with saved rules"],
    ["ADDSELECTED", "ADDSELECTED ↵", "Start a new object like the picked one (layer, style)", { ribbon: "Right-click › Add Selected" }],
    ["ISOLATEOBJECTS", "ISOLATEOBJECTS ↵", "Hide everything else for now, without touching layers. UNISOLATEOBJECTS ↵ ends it", { ribbon: "Right-click › Isolate" }],
  ] },
  { type: "cmds", title: "Geometry, blocks and display order", rows: [
    ["PEDIT Multiple", "PE ↵ M ↵ ... J ↵", "Lines into polylines, joined within a fuzz distance", { ribbon: "Home › Modify ▾ (Edit Polyline)", survey: "care", warn: "Keep the fuzz small (0.01)" }],
    ["REVERSE", "REVERSE ↵", "Flip a polyline's direction (fixes upside-down linetype text)", { ribbon: "Home › Modify ▾", survey: "ok" }],
    ["PLINEGEN", "PLINEGEN ↵ 1 ↵", "Linetype pattern runs continuously past vertices (short fence legs)", { ribbon: "or Properties › Linetype generation", survey: "ok" }],
    ["ALIGN", "AL ↵", "Move and rotate by two point pairs", { ribbon: "Home › Modify ▾", survey: "draft", warn: "Answer N to scale objects" }],
    ["BOUNDARY", "BO ↵", "Closed polyline from inside an enclosed area (area checks)", { ribbon: "Home › Draw (Hatch ▾)", survey: "ok" }],
    ["DIVIDE / MEASURE", "DIV ↵ / ME ↵", "Points or blocks at equal parts / a set spacing", { ribbon: "Home › Draw ▾" }],
    ["OVERKILL", "OVERKILL ↵", "Delete duplicate and overlapping lines", { ribbon: "Home › Modify ▾", survey: "care", warn: "Save first; 'Do not break polylines'; a window, not ALL" }],
    ["FLATTEN", "FLATTEN ↵", "Project everything to 2D (Express Tools)", { survey: "never", warn: "Destroys elevations" }],
    ["EXPLODE", "X ↵", "Break a block or polyline into pieces", { survey: "never", warn: "Never on Civil 3D points, labels, surfaces or parcels" }],
    ["BEDIT", "BE ↵", "Edit a block definition", { ribbon: "Insert › Block Definition", warn: "Changes every copy of that block" }],
    ["ATTSYNC", "ATTSYNC ↵", "Update block attributes after a block edit", { ribbon: "Insert › Block Definition" }],
    ["BURST", "BURST ↵", "Explode a block but keep attribute values as text (Express Tools)", { warn: "The block is gone: later block edits won't reach it" }],
    ["NCOPY", "NCOPY ↵", "Copy linework out of an xref or block (Express Tools)"],
    ["TEXTTOFRONT", "TEXTTOFRONT ↵", "Text, dimensions, leaders on top of hatches and lines", { ribbon: "Home › Modify ▾ (Draw Order ▾)" }],
    ["DRAWORDER", "DR ↵", "Send one object to the front or back", { ribbon: "Home › Modify ▾ (Draw Order ▾)" }],
    ["Wipeout frames", "WIPEOUT ↵ F ↵", "On / Off / Display but not plot. TFRAMES ↵ toggles (Express)"],
    ["TXT2MTXT / TCASE", "TXT2MTXT ↵", "Combine TEXT lines into MTEXT (Insert › Import › Combine Text) / change case"],
  ] },
  { type: "side", left: { type: "pairs", title: "Precision tricks (at a point prompt)", code: true, split: 0.3, rows: [
    ["M2P", "Midpoint between two picked points"],
    ["FROM", "Offset from a base point: FROM, pick the base, then @dx,dy"],
    ["APP / EXT / PAR", "Apparent intersection / extension / parallel"],
    [".XY", "Snap X,Y from one place, then type the Z"],
    ["'Z", "Zoom in the middle of another command"],
  ] }, right: { type: "pairs", title: "Keys worth knowing", code: true, split: 0.3, rows: [
    ["F8 / F10", "Ortho / polar tracking"],
    ["F11 / F12", "Object snap tracking / dynamic input"],
    ["F2", "Command history window"],
    ["CTRL+0", "Clean screen on / off"],
    ["CTRL+8", "QuickCalc"],
  ] } },
  { type: "exercises", items: [
    { title: "PEDIT Multiple: lines into one polyline", steps: [
      "**PE ↵ M ↵**, select the lines, **↵**",
      "Convert to polylines? **Y ↵**",
      "**J ↵** fuzz **0.01 ↵**, then **↵** to finish",
    ], check: "**LI ↵**: one closed polyline with an area." },
    { title: "LAYMRG: merge a stray layer", steps: [
      "**SAVE** first",
      "**LAYMRG ↵**, pick an object on the layer to get rid of, **↵**",
      "Pick an object on the layer to keep",
      "Read both names, then **Y ↵**",
    ], tip: "**N ↵** at either prompt lets you pick layers by name." },
  ] },
] };

// ------------------------------------------------------------------------------------------------
const L7 = { n: 7, title: "HIGHER LEVEL CIVIL 3D", advanced: true, goal: "I understand enough Civil 3D to diagnose what I'm seeing and make the right kind of edit.", blocks: [
  { type: "box", kind: "warn", title: "⚠ IF A CIVIL 3D OBJECT LOOKS WRONG", lines: [
    "__Don't explode or redraw it.__ Check, in order: 1 the __object__ (CTRL+1: is it the one you think?)  2 its __style__  3 its __label style__  4 its __layer__  5 its __source__ (point group override, description key, data reference, xref). Fix the cause, not the picture.",
  ] },
  { type: "cmds", title: "Points, groups and styles", rows: [
    ["Point group display order", "Prospector › Point Groups › Properties", "The group __highest__ in the list controls how a point looks, over _All Points"],
    ["Out-of-date point group", "right-click › Update", "Groups flagged in Prospector don't show new or changed points until updated"],
    ["Description key set", "Settings › Point › Description Key Sets", "Code (first word) → point style, label style, layer, full description, scale/rotate parameter"],
    ["Object / label style", "Settings tab › object type", "Edit the style to change every object using it. Right-click › Edit Style is faster"],
    ["Label dragged state", "drag a label grip", "A dragged label uses the style's dragged-state layout (leader, stacked text)"],
    ["Reset Label", "right-click › Reset Label", "Back to the style's position. Clicking the grip resets the leader, twice resets the label"],
    ["Point Table", "Annotate › Add Tables", "Coordinate table from a point group or selection"],
    ["Line/Curve tables", "Annotate › Add Tables", "Tag mode swaps crowded labels for L1, C1 tags plus a table"],
    ["PURGESTYLES", "PURGESTYLES ↵", "Purge unused Civil 3D styles (PURGE doesn't); run it twice for child styles", { warn: "Office templates may keep styles on purpose" }],
  ] },
  { type: "cmds", title: "Surfaces for survey QC", note: "no design here: check what the field data built", rows: [
    ["TIN surface", "Prospector › Surfaces", "Triangles between points and breaklines; contours come from them"],
    ["Show triangles / contours", "Surface style › Display", "Turn on Triangles to see bad connections; contours are just a display setting"],
    ["Breaklines", "Surface › Definition › Breaklines", "Force triangles to follow curbs, walls, ditches (often from survey figures)"],
    ["Boundaries", "Surface › Definition › Boundaries", "Outer limits the surface; Hide cuts holes (buildings); only the last Outer counts"],
    ["Rebuild", "right-click surface › Rebuild", "An out-of-date surface (flag in Prospector) needs a rebuild; or tick Rebuild - Automatic"],
    ["Object Viewer", "OBJECTVIEWER ↵", "Look at a surface or object in 3D in its own window", { ribbon: "Civil 3D: View › Palettes" }],
    ["Inquiry Tool", "Analyze › Inquiry › Inquiry Tool", "Point and surface inquiries (elevation, slope) you can copy to Excel"],
  ] },
  { type: "cmds", title: "Parcels, figures and linework types", rows: [
    ["Create parcel", "CREATEPARCELFROMOBJECTS ↵", "Parcel with area from a closed polyline", { ribbon: "Civil 3D: Home › Create Design › Parcel", warn: "Uncheck 'Erase existing entities' to keep the polyline" }],
    ["Sites", "Prospector › Sites", "Parcels, feature lines and alignments in one site interact: keep them in separate sites"],
    ["Mapcheck Analysis", "Analyze › Ground Data › Survey ▾ › Mapcheck", "Closure error, precision and area from line/curve labels, parcels or figures"],
    ["Polyline vs 3D polyline", "", "2D drafting line with one elevation vs a line with a Z at every vertex"],
    ["Feature line", "", "Civil 3D linear object with elevations, used for breaklines and grading; lives in a site"],
    ["Survey figure", "", "Feature line stored in the survey database; can be a breakline or lot line"],
    ["MAPCLEAN", "MAPCLEAN ↵", "Drawing Cleanup: duplicates, undershoots, dangles within a tolerance", { warn: "Save first; run one action at a time" }],
    ["EXPORTTOAUTOCAD", "EXPORTTOAUTOCAD ↵", "Separate plain-AutoCAD copy for outside users (prefix ACAD-)", { warn: "Keep working in the original" }],
  ] },
  { type: "exercises", items: [
    { title: "Why can't I see this point?", steps: [
      "**'ZTP ↵** the point number: is it there at all?",
      "Click it (or find it in Prospector › Points), **CTRL+1**: layer, style",
      "Layer off or frozen? Style set to <none>?",
      "Prospector › Point Groups › Properties: which group is on top of the display order?",
      "Any group flagged out of date? right-click › Update",
    ], check: "You found the cause before changing anything." },
    { title: "Check a surface", steps: [
      "Select the surface, right-click › Edit Surface Style",
      "Display tab: turn on Triangles",
      "Look for long skinny triangles across buildings or off the site",
      "Is there an Outer boundary? Hide boundaries on buildings?",
      "Turn Triangles off again",
    ], check: "You can point to where a breakline or boundary is missing." },
  ] },
] };

// ------------------------------------------------------------------------------------------------
const L8 = { n: 8, title: "PROJECT REFERENCES AND MULTI-DRAWING WORK", advanced: true, goal: "I understand where project data lives before I edit it.", blocks: [
  { type: "box", title: "WHERE DOES THIS OBJECT LIVE?", lines: [
    "- __Xref__ = another whole drawing shown inside this one. Its layers read NAME|LAYER. Edit it by opening that drawing.",
    "- __Data reference (data shortcut)__ = one Civil 3D object brought in read-only from another drawing: surfaces, alignments, profiles, sample line groups, corridors, pipe and pressure networks, view frame groups. __COGO points and point groups can't be shortcut__: they come by xref, point file or the survey database.",
    "- __Source drawing__ = the only drawing where that object's geometry can change. Here you can change its style, layer and labels; not its shape.",
    "- !!If an object is referenced, find the source before you try to fix it here.!!",
  ] },
  { type: "cmds", title: "Xrefs", rows: [
    ["XREF palette", "XR ↵", "List xrefs and their status (Loaded, Not Found, Needs Reloading); right-click to Reload, Unload, Detach"],
    ["XATTACH", "XA ↵", "Attach a drawing. Attach carries nested xrefs along; Overlay doesn't. Relative path for projects that move"],
    ["XCLIP", "XCLIP ↵", "Clip an xref to a boundary"],
    ["Open the source", "select xref › right-click › Open Xref", "Make the edit where the object lives"],
    ["Label xref'd Civil 3D objects", "Annotate › Add Labels", "Works for surfaces, parcels, alignments, lines/curves if the xref is at 0,0,0, scale 1, rotation 0"],
  ] },
  { type: "cmds", title: "Data shortcuts", note: "mostly right-click in Prospector › Data Shortcuts", rows: [
    ["Set working folder", "SETWORKINGFOLDER ↵", "The parent folder that holds the data shortcut projects", { ribbon: "Civil 3D: Manage › Data Shortcuts" }],
    ["Set shortcuts folder", "SETSHORTCUTSFOLDER ↵", "Pick the project this drawing works in", { ribbon: "Civil 3D: Manage › Data Shortcuts" }],
    ["Create data shortcuts", "CREATEDATASHORTCUTS ↵", "Publish objects from the source drawing", { ribbon: "Civil 3D: Manage › Data Shortcuts" }],
    ["Create Reference", "Prospector › shortcut › right-click", "Bring a read-only copy into this drawing"],
    ["Validate Data Shortcuts", "right-click Data Shortcuts (top node)", "Check every shortcut still points to a real source; broken ones get a flag"],
    ["Synchronize", "right-click the out-of-date reference", "Pull in the source's latest geometry (or click the status bar balloon)"],
    ["Repair Broken References", "right-click the broken reference", "Browse to the moved or renamed source drawing"],
    ["Open Source Drawing", "right-click the reference", "Go edit it where it lives"],
    ["Promote", "right-click the reference", "Turn the reference into a local, editable object", { warn: "It stops updating: two versions of the truth" }],
    ["Data Shortcuts Editor", "Windows Start › Civil 3D", "Separate app to fix paths in shortcut files; reload shortcuts after"],
  ] },
] };

// ------------------------------------------------------------------------------------------------
const L9 = { n: 9, title: "TROUBLESHOOTING AND DRAWING HEALTH", advanced: true, goal: "I can find why something looks wrong and keep drawings healthy.", blocks: [
  { type: "box", kind: "aid", title: "FIRST AID: TRY THESE FIRST", lines: [
    "1  **ESC ESC** to get out of whatever command you're in.   2  Read the command line: it usually says what it wants.   3  **CTRL+Z** or **U ↵** step by step.   4  **Z ↵ E ↵** and **RE ↵** if you're lost.   5  Save, close, reopen; then restart AutoCAD.",
  ] },
  { type: "diag", title: "I can't see it", rows: [
    ["An object is missing", ["Layer off or frozen (**LA ↵**)", "Frozen in this viewport (VP Freeze)", "Objects isolated or hidden (**UNISOLATEOBJECTS ↵**)", "Xref unloaded or Not Found (**XR ↵**)", "Civil 3D style set to <none>", "Point group display order / out of date", "Data reference broken or out of date (Prospector)"]],
    ["A label disappeared or looks wrong", ["Label style (CTRL+1)", "Parent object still there?", "Label layer on?", "Dragged: right-click › Reset Label", "Annotation scale on the status bar", "??? = the label can't find its data (no elevation, deleted or unresolved reference)"]],
    ["Viewport is blank", ["Viewport On (CTRL+1)", "Zoomed away inside it", "Layers VP-frozen in it"]],
    ["It's on screen but won't plot", ["Layer set to no-plot (printer icon)", "On Defpoints", "Outside the plot area", "Wipeout frames or viewport border plotting: put the border on a no-plot layer; **WIPEOUT ↵ F ↵**"]],
    ["Points invisible", ["**PDMODE ↵ 3 ↵** then **RE ↵** (plain POINTs)", "Point layer frozen", "COGO point style / point group"]],
  ] },
  { type: "diag", title: "It looks wrong", rows: [
    ["Linetypes look solid or wrong", ["Object linetype scale (CTRL+1)", "**LTSCALE**", "**PSLTSCALE** / **MSLTSCALE** (see settings below)", "**REGENALL ↵**", "Viewport scale", "Polyline: **PLINEGEN** on"]],
    ["Fence or wall text upside down", ["**REVERSE ↵** the polyline"]],
    ["Arcs look like polygons / leftovers on screen", ["**RE ↵**", "**REGENALL ↵**"]],
    ["ZOOM Extents shows a tiny drawing", ["Stray objects far away: window the empty area, erase, **Z ↵ E ↵**"]],
    ["Everything faded", ["Locked layers: **LAYLOCKFADECTL**", "Xrefs: **XDWGFADECTL**"]],
    ["Lines all thick / fills show as outlines", ["Lineweight display: **LWDISPLAY ↵ 0 ↵**", "**FILLMODE ↵ 1 ↵** **RE ↵**"]],
    ["Fonts show ??? or the wrong font", ["Missing SHX font: get it, or set the style to a TrueType font (**ST ↵**)", "**FONTALT** sets the substitute"]],
    ["Text or labels wrong size", ["Annotation scale matches the viewport", "Text style has a fixed height", "Civil 3D label heights are plotted units"]],
    ["Drawing suddenly tilted in 3D", ["Shift + wheel-drag orbits: **PLAN ↵ ↵** or ViewCube › Top"]],
    ["Dimension shows the wrong number", ["Text override (CTRL+1 › Text override)", "**DIMLFAC** not 1"]],
    ["Mirrored text backwards", ["**MIRRTEXT ↵ 0 ↵**, mirror again"]],
  ] },
  { type: "diag", title: "It acts wrong", rows: [
    ["Command line / ribbon / tabs gone", ["**CTRL+9**", "**RIBBON ↵** (tabs only: double-click a tab)", "**CTRL+0** clean screen", "Model/layout tabs: **LAYOUTTAB ↵ 1 ↵**", "Drawing tabs: **FILETAB ↵**", "**MENUBAR ↵ 1 ↵**", "Toolspace: **SHOWTS ↵**"]],
    ["Crosshairs missing or tilted", ["**CURSORTYPE ↵ 0 ↵**", "**GRAPHICSCONFIG ↵**: hardware acceleration off/on, restart", "Tilted: **SNAPANG ↵ 0 ↵** and **UCS ↵ ↵**"]],
    ["Cursor jumps / only straight lines", ["Snap mode **F9** off", "Ortho **F8** off"]],
    ["Selecting acts strange", ["Second pick drops the first: **PICKADD ↵ 2 ↵**", "Can't pick before the command: **PICKFIRST ↵ 1 ↵**", "No grips: **GRIPS ↵ 2 ↵**", "Window needs a drag: **PICKDRAG ↵ 2 ↵**", "Can see it, can't pick it: locked layer (**LAYULK ↵**), inside a block or xref, or you're on paper"]],
    ["Mouse acts strange", ["Wheel click shows a menu: **MBUTTONPAN ↵ 1 ↵**", "Wheel zooms backwards: **ZOOMWHEEL ↵ 0 ↵**", "Double-click doesn't edit: **DBLCLKEDIT ↵ ON ↵**", "Right-click: Options › User Preferences › Right-click Customization"]],
    ["Dialogs don't open", ["**FILEDIA ↵ 1 ↵**", "Attributes: **ATTDIA ↵ 1 ↵**"]],
    ["Workspace is a mess", ["Gear on the status bar › pick the workspace again", "Last resort: Options › Profiles › Reset"]],
    ["Typed input goes wrong", ["Point lands wrong: dynamic input is relative, use **#E,N**", "Bearing rejected: Surveyor's units (**UN ↵**), no spaces", "'NE wants easting first: Ambient Settings", "'BD won't start: it needs a command asking for a point"]],
    ["TRIM / EXTEND don't ask for edges", ["Quick mode (2021+): **B ↵** / **T ↵** for edges, or **TRIMEXTENDMODE ↵ 0 ↵** (Standard)"]],
    ["Hatch: 'boundary not closed'", ["Find the gap, or **HPGAPTOL ↵** 0.1"]],
    ["Pasted block 12 times too big or small", ["**INSUNITS** in both drawings (1 inches, 2 feet)"]],
    ["New lines have an elevation", ["**ELEV ↵ 0 ↵**", "Snapping to points pulls their Z: see **OSNAPZ**"]],
    ["A command does something odd", ["Someone redefined it: **REDEFINE ↵** name", "Autocomplete picks the wrong one: **INPUTSEARCHOPTIONS ↵**"]],
    ["Pasted objects land far off site", ["Use **PASTEORIG ↵** (with **CTRL+SHIFT+C** COPYBASE)"]],
    ["Civil 3D didn't update", ["Surface out of date: right-click › Rebuild", "Point group: right-click › Update", "Reference: right-click › Synchronize"]],
    ["Can't erase: 'on a locked layer' / layer won't delete", ["**LAYULK ↵**", "Layer is current, 0, Defpoints, or used by a block/xref: **LAYDEL ↵**"]],
  ] },
  { type: "diag", title: "Files and crashes", rows: [
    ["AutoCAD crashed", ["Reopen: Drawing Recovery palette (**DRAWINGRECOVERY ↵**)", "Autosave: **SAVEFILEPATH ↵** shows where; rename .sv$ to .dwg", "**SAVETIME ↵ 10 ↵**"]],
    ["Drawing won't open or has errors", ["**RECOVER ↵**", "Rename the .bak to .dwg", "Still bad: open what you can, **WBLOCK** the good objects to a new drawing"]],
    ["Opens read-only", ["Someone has it open. Delete the .dwl/.dwl2 only if you're sure nobody does"]],
    ["Drawing slow or huge", ["Xrefs: unload what you don't need", "Surface display: triangles/points off", "Thousands of point labels: label style off, or a point table", "**-PURGE ↵ R ↵ ↵ N ↵** regapps", "**PURGE** incl. orphaned data", "**PURGESTYLES ↵**", "Scale list: **-SCALELISTEDIT ↵ R ↵ Y ↵ E ↵**", "**AUDIT ↵ Y ↵**"]],
    ["Someone can't open your file", ["**SAVEAS** an older DWG version", "Plain AutoCAD: **EXPORTTOAUTOCAD**", "'Proxy objects': made in Civil 3D; open it there or send an export"]],
  ] },
  { type: "cmds", title: "Drawing health and hand-off tools", rows: [
    ["AUDIT", "AUDIT ↵ Y ↵", "Find and fix errors in the open drawing"],
    ["RECOVER", "RECOVER ↵", "Open a damaged drawing and repair it", { ribbon: "App menu › Drawing Utilities" }],
    ["PURGE", "PU ↵", "Remove unused layers, blocks, styles; tick Orphaned data", { warn: "Also purges empty template layers you may still need" }],
    ["-PURGE Regapps", "-PU ↵ R ↵ * ↵ N ↵", "Remove junk registered apps (slow, bloated files)"],
    ["WBLOCK", "W ↵", "Write objects to a new, clean DWG", { ribbon: "Insert › Block Definition (Write Block)", warn: "Pick Retain, not Delete from drawing" }],
    ["COPYBASE / PASTEORIG", "CTRL+SHIFT+C / PASTEORIG ↵", "Copy with a base point / paste at the same coordinates in another drawing", { ribbon: "Home › Clipboard" }],
    ["COMPARE", "COMPARE ↵", "DWG Compare: highlights changes between two versions", { ribbon: "Collaborate › Compare" }],
    ["ETRANSMIT", "ETRANSMIT ↵", "Zip a drawing with its xrefs, fonts and plot styles", { ribbon: "App menu › Publish" }],
    ["EXPORTTOAUTOCAD", "EXPORTTOAUTOCAD ↵", "Plain-AutoCAD copy for outside users (Level 7)"],
    ["QUICKCALC", "CTRL+8", "Calculator that can pick points and distances"],
  ] },
  { type: "sysvars", title: "System variables", note: "type the name, then the value", rows: [
    ["LTSCALE", "1", "Global linetype scale", "Training drawing: 20 (linetypes drawn in plotted inches for 1\" = 20')"],
    ["PSLTSCALE", "1", "1: dashes are the same paper size in every viewport. 0: dashes follow LTSCALE in model units, so they change with viewport scale", "Training drawing: 0, so it relies on LTSCALE 20. Match the Parametrix template"],
    ["MSLTSCALE", "1", "1: model tab linetypes scale with the annotation scale", ""],
    ["CELTSCALE", "1", "Linetype scale given to new objects (multiplies LTSCALE)", "Leave at 1"],
    ["OSNAPZ", "0", "0: a snap uses the snapped point's Z. 1: uses the current ELEV instead", "Not saved: resets each session", "Survey points carry elevations. At 0, a line snapped to Nodes gets their Z"],
    ["PICKADD", "2", "0: a new pick replaces the selection. 1/2: picks add (2 keeps them selected after SELECT)", ""],
    ["PICKFIRST", "1", "Pick objects first, then the command", ""],
    ["MIRRTEXT", "0", "0: mirrored text stays readable", ""],
    ["FILEDIA", "1", "0 = command-line prompts instead of file dialogs", "", "Scripts set it to 0 and sometimes leave it"],
    ["LWDISPLAY", "0", "Show lineweights on screen (they plot either way)", ""],
    ["LAYLOCKFADECTL", "50", "Fade of locked layers (−90 to 90; negative = off)", ""],
    ["XDWGFADECTL", "50", "Fade of xrefs (−90 to 90; negative = off)", ""],
  ] },
] };

module.exports = { TITLE, SUBTITLE, FOOTER, FILE, COVER, LEVELS: [L1, L2, L3, L4, L5, L6, L7, L8, L9] };

function FULL_W() { return 10360; }
