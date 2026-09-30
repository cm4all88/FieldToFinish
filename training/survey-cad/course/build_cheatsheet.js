// Command sheet, portrait letter for a binder. build(true) would add the course's Stage column;
// the published sheet is build(false): no stages, no course wording.
const fs = require("fs");
const path = require("path");
const {
  Document, Packer, Paragraph, TextRun, Table, TableRow, TableCell, WidthType, BorderStyle,
  ShadingType, AlignmentType, ImageRun, SectionType, Footer, PageNumber,
} = require("docx");

const OUT = path.join(__dirname, "output");
const FONT = "Calibri", MONO = "Consolas", ACCENT = "1F4E79";
const PAGE_W = 12240, PAGE_H = 15840, LEFT = 1260, RIGHT = 620;   // wide left edge for binder holes
const CONTENT = PAGE_W - LEFT - RIGHT;
const GAP = 240;
const FULL = CONTENT, HALF = (CONTENT - GAP) / 2;
const thin = { style: BorderStyle.SINGLE, size: 4, color: "BFBFBF" };
const none = { style: BorderStyle.NONE, size: 0, color: "FFFFFF" };

// [command, what to type, what it's for, stage]
const GROUPS = [
  ["Getting around", [
    ["OPEN / SAVEAS", "OPEN / SAVEAS", "Open the start file, save your own DWG", "0"],
    ["ZOOM Extents", "Z ↵ E ↵", "See the whole drawing", "0"],
    ["UNITS", "UN ↵", "Surveyor's bearings, 0.00 ft", "0"],
    ["OSNAP", "OS ↵ / F3", "Running snaps on/off", "0"],
    ["ID", "ID ↵", "Coordinates of a point (X = E, Y = N)", "0"],
    ["LIST", "LI ↵", "Bearing, length, area of an object", "0, 1"],
    ["DIST", "DI ↵", "Distance and bearing between two points", "1"],
  ]],
  ["Layers", [
    ["LAYER", "LA ↵", "Layer manager: on/off, freeze, current", "all"],
    ["LAYFRZ", "LAYFRZ ↵", "Freeze the layer of a picked object", "0, 5"],
    ["LAYMCUR", "LAYMCUR ↵", "Make a picked object's layer current", "1"],
    ["LAYMCH", "LAYMCH ↵", "Move objects to another object's layer", "2"],
    ["MATCHPROP", "MA ↵", "Copy properties / clear overrides", "2"],
  ]],
  ["Drawing and editing", [
    ["LINE", "L ↵", "Lines; type @dist<bearing", "1"],
    ["PLINE", "PL ↵", "Polylines node to node; C ↵ closes", "2, 3"],
    ["FILLET", "F ↵ R ↵ 0 ↵", "Close a corner exactly", "1"],
    ["EXTEND", "EX ↵ B ↵", "Extend to a chosen boundary", "1"],
    ["TRIM", "TR ↵", "Cut off an overshoot", "1"],
    ["JOIN", "J ↵", "Lines into one polyline", "1"],
    ["ERASE", "E ↵", "Delete objects", "2"],
    ["ROTATE", "RO ↵ R ↵ ... P ↵", "Rotate by Reference to line up with linework", "3, 4"],
  ]],
  ["Annotation", [
    ["MLEADER", "MLD ↵", "Leader labels: structures, trees, control", "3, 4"],
    ["MTEXT", "MT ↵", "Callouts: ASPH, CONC, GRASS, FFE", "4"],
    ["TEXT", "DT ↵ J ↵ BC ↵", "Bearing/distance on a line", "4"],
    ["TORIENT", "TORIENT ↵ ↵", "Flip upside-down text to readable", "4"],
    ["DIMALIGNED", "DAL ↵", "Setback dimensions (use Perpendicular)", "4"],
  ]],
  ["Finish and plot", [
    ["QSELECT", "QSELECT ↵", "Find objects by property (e.g. layer 0)", "5"],
    ["PURGE / AUDIT", "PU ↵  AUDIT ↵ Y ↵", "Clean and check the drawing", "5"],
    ["MVIEW", "MV ↵", "Make a viewport in the layout", "5"],
    ["Viewport scale", "Z ↵ 1/20XP ↵", "1\" = 20' inside the viewport", "5"],
    ["PLOT", "CTRL+P", "PDF with monochrome.ctb", "5"],
  ]],
];

const INPUT = [
  ["@249.14<S88d24'53\"E", "Distance and bearing from the last point"],
  ["#1125170.86,733969.17", "Absolute coordinate: Easting,Northing"],
  ["%%d", "Degree sign in text"],
];
const SNAPS = [
  ["Node", "A survey point"], ["Endpoint", "End of a line"], ["Insertion", "A symbol's base point"],
  ["Midpoint", "Middle of a line"], ["Perpendicular", "Square off a line (setbacks)"], ["Nearest", "Anywhere on a line"],
];
const KEYS = [
  ["↵ / Space", "Enter, or repeat last command"], ["ESC", "Cancel"], ["F3", "Snaps on/off"],
  ["CTRL+1", "Properties"], ["Shift + right-click", "One-time snap menu"], ["CTRL+Z", "Undo"],
];


// Where the button is. "▾" = in the panel's drop-down or expanded area.
// AutoCAD rows: 2D Drafting & Annotation workspace. Civil 3D rows: Civil 3D workspace.
const RIBBON = {
  "OPEN / SAVEAS": "Quick Access toolbar", "ZOOM Extents": "or double-click the wheel", "UNITS": "App menu › Drawing Utilities",
  "OSNAP": "Status bar", "ID": "Home › Utilities ▾", "LIST": "Home › Properties ▾", "DIST": "Home › Utilities › Measure",
  "LAYER": "Home › Layers", "LAYFRZ": "Home › Layers", "LAYMCUR": "Home › Layers", "LAYMCH": "Home › Layers",
  "MATCHPROP": "Home › Properties", "LINE": "Home › Draw", "PLINE": "Home › Draw", "FILLET": "Home › Modify (Fillet ▾)",
  "EXTEND": "Home › Modify (Trim ▾)", "TRIM": "Home › Modify", "JOIN": "Home › Modify ▾", "ERASE": "Home › Modify",
  "ROTATE": "Home › Modify", "MLEADER": "Home › Annotation", "MTEXT": "Home › Annotation (Text ▾)",
  "TEXT": "Home › Annotation (Text ▾)", "TORIENT": "Express Tools › Text ▾", "DIMALIGNED": "Home › Annotation (Dim ▾)",
  "QSELECT": "Home › Utilities", "PURGE / AUDIT": "App menu › Drawing Utilities", "MVIEW": "Layout › Layout Viewports",
  "Viewport scale": "Status bar scale list", "PLOT": "Output › Plot",
  "CHSPACE": "Home › Modify ▾ (Change Space)", "MSPACE / PSPACE": "Status bar MODEL/PAPER", "VPMAX / VPMIN": "Status bar",
  "VPLAYER": "Layer Properties, VP Freeze column", "VPCLIP": "Layout › Layout Viewports › Clip",
  "LAYOUT": "or right-click a layout tab", "LAYISO / LAYUNISO": "Home › Layers", "LAYOFF / LAYON": "Home › Layers (▾)",
  "LAYTHW": "Home › Layers ▾", "LAYLCK / LAYULK": "Home › Layers", "LAYCUR": "Home › Layers ▾", "LAYMRG": "Home › Layers ▾",
  "LAYDEL": "Home › Layers ▾", "LAYWALK": "Home › Layers ▾", "LAYERP": "Home › Layers", "SETBYLAYER": "Home › Modify ▾",
  "SELECTSIMILAR": "Right-click › Select Similar", "ADDSELECTED": "Right-click › Add Selected", "Cycle overlaps": "Status bar",
  "OVERKILL": "Home › Modify ▾ (Delete Duplicates)", "PEDIT Multiple": "Home › Modify ▾ (Edit Polyline)",
  "REVERSE": "Home › Modify ▾", "PLINEGEN": "or Properties › Linetype generation", "BREAKATPOINT": "Home › Modify ▾",
  "LENGTHEN": "Home › Modify ▾", "ALIGN": "Home › Modify ▾", "STRETCH": "Home › Modify", "OFFSET Layer": "Home › Modify",
  "DIVIDE / MEASURE": "Home › Draw ▾", "BOUNDARY": "Home › Draw (Hatch ▾)", "FLATTEN": "Express Tools",
  "TXT2MTXT": "Express Tools › Text", "TJUST": "Express Tools › Text ▾", "TCASE": "Express Tools › Text ▾",
  "FIND": "Annotate › Text", "SCALETEXT": "Annotate › Text ▾", "MLEADERALIGN": "Annotate › Leaders",
  "MLEADEREDIT": "Annotate › Leaders (Add/Remove)", "TEXTTOFRONT": "Home › Modify ▾ (Draw Order ▾)",
  "DRAWORDER": "Home › Modify ▾ (Draw Order ▾)", "TFRAMES": "Express Tools", "BURST": "Express Tools › Blocks",
  "ATTSYNC": "Insert › Block Definition", "BEDIT": "Insert › Block Definition", "NCOPY": "Express Tools › Blocks",
  "Bearing/distance": "Transparent Commands toolbar", "Azimuth/distance": "Transparent Commands toolbar",
  "Northing/Easting": "Transparent Commands toolbar", "Point number": "Transparent Commands toolbar",
  "Point object": "Transparent Commands toolbar", "Side shot": "Transparent Commands toolbar",
  "Zoom to point": "Transparent Commands toolbar", "Match radius / length": "Transparent Commands toolbar",
  "CREATEPOINTS": "Home › Create Ground Data › Points", "Segment labels": "Annotate › Labels & Tables › Add Labels",
  "Parcels": "Home › Create Design › Parcel", "EXPORTTOAUTOCAD": "Output › Export",
  "RECOVER": "App menu › Drawing Utilities", "WBLOCK": "Insert › Block Definition (Write Block)",
  "COPYBASE": "Home › Clipboard (Copy ▾)", "PASTEORIG": "Home › Clipboard (Paste ▾)", "XATTACH / XCLIP": "Insert › Reference",
  "COMPARE": "Collaborate › Compare", "ETRANSMIT": "App menu › Publish", "MEASUREGEOM": "Home › Utilities › Measure",
  "QUICKCALC": "View › Palettes",
};
// Optional button pictures: course/icons/<NAME>.png, NAME = command with spaces and slashes as "_"
// (e.g. CHSPACE.png, OPEN_SAVEAS.png). Screenshot them from your own install; they are not committed.
const ICONS = path.join(__dirname, "icons");
const iconFor = (name) => {
  const f = path.join(ICONS, name.replace(/[ /]+/g, "_") + ".png");
  return fs.existsSync(f) ? new ImageRun({ type: "png", data: fs.readFileSync(f), transformation: { width: 16, height: 16 } }) : null;
};
const cmdCell = (name, w) => {
  const icon = iconFor(name);
  const paras = [new Paragraph({ children: [...(icon ? [icon, t(" ")] : []), t(name, { bold: true })] })];
  if (RIBBON[name]) paras.push(new Paragraph({ children: [t(RIBBON[name], { size: 12, color: "7F7F7F" })] }));
  return new TableCell({ width: { size: w, type: WidthType.DXA }, borders: { top: thin, bottom: thin, left: thin, right: thin },
    margins: { top: 10, bottom: 10, left: 70, right: 70 }, children: paras });
};

// ---- Beyond the course: pages 2-3 ----------------------------------------------
// [command, what to type, what it's for]
const MORE = {
  viewports: ["Layouts and viewports", [
    ["CHSPACE", "CHSPACE ↵", "Move objects between paper and model space through a viewport (rescaled)"],
    ["SPACETRANS", "SPACETRANS ↵", "Convert a height between paper and model units (0.08\" = 1.6')"],
    ["MSPACE / PSPACE", "MS ↵ / PS ↵", "Go inside a viewport / back out to paper (or double-click)"],
    ["VPMAX / VPMIN", "VPMAX ↵", "Fill the screen with a viewport to work in it, then restore"],
    ["VPLAYER", "VPLAYER ↵", "Freeze a layer in one viewport only"],
    ["VPCLIP", "VPCLIP ↵", "Clip a viewport to a polyline shape"],
    ["Turn the view", "UCS ↵ OB ↵  PLAN ↵ ↵", "Line the view up with a road; UCS ↵ W ↵ PLAN ↵ ↵ to reset"],
    ["LAYOUT", "LAYOUT ↵ C ↵", "Copy a layout tab for another sheet"],
  ]],
  layers: ["Layer tools", [
    ["LAYISO / LAYUNISO", "LAYISO ↵", "Show only the picked object's layers / undo that"],
    ["LAYOFF / LAYON", "LAYOFF ↵", "Turn off a picked layer / turn all back on"],
    ["LAYTHW", "LAYTHW ↵", "Thaw every layer"],
    ["LAYLCK / LAYULK", "LAYLCK ↵", "Lock / unlock a picked layer (can snap, can't edit)"],
    ["LAYCUR", "LAYCUR ↵", "Move objects to the current layer"],
    ["LAYMRG", "LAYMRG ↵", "Merge one layer into another, deleting the first"],
    ["LAYDEL", "LAYDEL ↵", "Delete a layer and everything on it"],
    ["LAYWALK", "LAYWALK ↵", "Step through layers one at a time to see what's on each"],
    ["LAYERP", "LAYERP ↵", "Undo the last layer setting change"],
    ["SETBYLAYER", "SETBYLAYER ↵", "Reset color, linetype and lineweight to ByLayer"],
  ]],
  select: ["Selecting", [
    ["SELECTSIMILAR", "SELECTSIMILAR ↵", "Select everything like the picked object (same layer, type)"],
    ["Previous", "P ↵ at Select objects", "Reuse the last selection set"],
    ["FILTER", "FI ↵", "Selection filter with saved rules"],
    ["ADDSELECTED", "ADDSELECTED ↵", "Draw a new object like the picked one (layer, style)"],
    ["Remove from set", "Shift + pick", "Take objects back out of a selection"],
    ["Cycle overlaps", "Shift + Space", "Pick between stacked objects"],
  ]],
  edit: ["Editing linework", [
    ["OVERKILL", "OVERKILL ↵", "Delete duplicate and overlapping lines"],
    ["PEDIT Multiple", "PE ↵ M ↵ ... J ↵", "Turn lines into polylines and join with a fuzz distance"],
    ["REVERSE", "REVERSE ↵", "Flip a polyline's direction (fixes upside-down linetype text)"],
    ["PLINEGEN", "PLINEGEN ↵ 1 ↵", "Linetype runs through polyline vertices (short fence legs)"],
    ["BREAKATPOINT", "BREAKATPOINT ↵", "Split a line at one point"],
    ["LENGTHEN", "LEN ↵ DE ↵ / T ↵", "Add a length or set a total length"],
    ["ALIGN", "AL ↵", "Move, rotate and scale by two point pairs"],
    ["STRETCH", "S ↵", "Move vertices with a crossing window"],
    ["OFFSET Layer", "O ↵ L ↵ C ↵", "Offsets land on the current layer (curb, ROW, setbacks)"],
    ["DIVIDE / MEASURE", "DIV ↵ / ME ↵", "Points or blocks at equal parts / a set spacing"],
    ["BOUNDARY", "BO ↵", "Closed polyline from inside an enclosed area"],
    ["FLATTEN", "FLATTEN ↵", "Set everything to elevation 0 (Express Tools)"],
    ["OOPS", "OOPS ↵", "Bring back the last erased objects"],
    ["MOVE / SCALE", "M ↵ / SC ↵", "Move or resize objects you drafted (symbols, text, blocks)"],
    ["EXPLODE", "X ↵", "Break a block or polyline into pieces"],
  ]],
  text: ["Text, leaders and blocks", [
    ["TXT2MTXT", "TXT2MTXT ↵", "Combine TEXT lines into one MTEXT"],
    ["TJUST", "TJUST ↵", "Change justification without moving text"],
    ["TCASE", "TCASE ↵", "Change text to UPPER case"],
    ["FIND", "FIND ↵", "Find and replace text in the drawing"],
    ["SCALETEXT", "SCALETEXT ↵", "Set many texts to one height"],
    ["MLEADERALIGN", "MLA ↵", "Line leader labels up in a neat column"],
    ["MLEADEREDIT", "MLE ↵", "Add or remove leader arms on one label"],
    ["TEXTTOFRONT", "TEXTTOFRONT ↵", "Text and dims on top of hatches and lines"],
    ["DRAWORDER", "DR ↵", "Send one object to the front or back"],
    ["TFRAMES", "TFRAMES ↵", "Show or hide wipeout frames"],
    ["BURST", "BURST ↵", "Explode a block but keep attribute values as text"],
    ["ATTSYNC", "ATTSYNC ↵", "Update block attributes after a block edit"],
    ["BEDIT", "BE ↵", "Edit a block definition"],
    ["NCOPY", "NCOPY ↵", "Copy linework out of an xref or block"],
  ]],
  c3d: ["Civil 3D", [
    ["Bearing/distance", "'BD ↵", "Quadrant, bearing, distance at any point prompt"],
    ["Azimuth/distance", "'ZD ↵", "Azimuth then distance"],
    ["Northing/Easting", "'NE ↵", "Type N first, then E"],
    ["Point number", "'PN ↵", "Snap to a COGO point by number"],
    ["Point object", "'PO ↵", "Pick a COGO point (uses its elevation)"],
    ["Side shot", "'SS ↵", "Angle and distance off a backsight line"],
    ["Zoom to point", "'ZTP ↵", "Zoom to a point number"],
    ["Match radius / length", "'MR ↵ / 'ML ↵", "Copy a curve's radius or length"],
    ["CREATEPOINTS", "CREATEPOINTS ↵", "Points toolbar: import, manual, on objects"],
    ["Segment labels", "ADDSEGMENTLABELS ↵", "Bearing/distance labels that update with the line"],
    ["Parcels", "CREATEPARCELFROMOBJECTS ↵", "Parcel with area from a closed polyline"],
    ["MAPCLEAN", "MAPCLEAN ↵", "Remove duplicates, fix undershoots and dangles"],
    ["EXPORTTOAUTOCAD", "EXPORTTOAUTOCAD ↵", "Save a copy plain AutoCAD can open"],
  ]],
  files: ["Files and cleanup", [
    ["-PURGE Regapps", "-PU ↵ R ↵ * ↵ N ↵", "Remove junk registered apps (slow, bloated files)"],
    ["RECOVER", "RECOVER ↵", "Open a damaged drawing and repair it"],
    ["WBLOCK", "W ↵", "Write objects to a new, clean DWG"],
    ["COPYBASE", "CTRL+SHIFT+C", "Copy with a base point"],
    ["PASTEORIG", "PASTEORIG ↵", "Paste at the same coordinates in another drawing"],
    ["XATTACH / XCLIP", "XA ↵ / XCLIP ↵", "Reference another DWG / clip it to a boundary"],
    ["COMPARE", "COMPARE ↵", "Highlight changes between two versions"],
    ["ETRANSMIT", "ETRANSMIT ↵", "Zip a drawing with its xrefs, fonts and plot styles"],
    ["MEASUREGEOM", "MEA ↵", "Distance, radius, angle, area in one tool"],
    ["QUICKCALC", "CTRL+8", "Calculator; can pick points and distances"],
  ]],
};
const SYSVARS = [
  ["FILEDIA", "1", "Open/Save dialogs gone? Set back to 1"],
  ["PICKADD", "2", "Each pick adds to the selection"],
  ["PICKFIRST", "1", "Pick objects first, then the command"],
  ["MIRRTEXT", "0", "Mirrored text stays readable"],
  ["PDMODE / PDSIZE", "3 / 0", "How POINT objects look"],
  ["LTSCALE / PSLTSCALE", "20 / 0", "Linetype scale for 1\" = 20' sheets"],
  ["MSLTSCALE", "1", "Model tab linetypes follow annotation scale"],
  ["CELTSCALE", "1", "Object linetype scale for new objects"],
  ["LWDISPLAY", "1", "Show lineweights on screen"],
  ["OSNAPZ", "1", "Snaps use the current elevation, not the point's Z"],
  ["LAYLOCKFADECTL", "50", "How faded locked layers look"],
  ["XDWGFADECTL", "50", "How faded xrefs look"],
];
const TRICKS = [
  ["M2P", "Midpoint between two picked points"],
  ["FROM", "Offset from a base point: FROM, pick, @dx,dy"],
  ["APP / EXT / PAR", "Apparent intersection / extension / parallel"],
  [".XY", "Point filter: snap X,Y, then type the Z"],
  ["'Z", "Zoom in the middle of a command"],
  ["@", "Alone = the last point picked"],
];
const MOREKEYS = [
  ["F8 / F10", "Ortho / polar tracking"], ["F11 / F12", "Object snap tracking / dynamic input"],
  ["F2", "Command history"], ["CTRL+9", "Command line on/off (if it vanishes)"],
  ["CTRL+0", "Clean screen"], ["CTRL+TAB", "Switch drawings"],
];


// Red warning line under "Use it to".
const WARN = {
  "ERASE": "OOPS undoes the last erase",
  "ROTATE": "Never on survey linework",
  "MATCHPROP": "Copies the layer too",
  "PURGE / AUDIT": "Save first",
  "Viewport scale": "Lock it, or a zoom ruins it",
  "CHSPACE": "Objects are rescaled by the viewport scale",
  "Turn the view": "PLAN zooms extents; reset the viewport scale after",
  "VPMAX / VPMIN": "Zooming inside an unlocked viewport changes its scale",
  "LAYISO / LAYUNISO": "Run LAYUNISO before you save or the rest stay off",
  "LAYMRG": "The first layer is deleted; check what you picked",
  "LAYDEL": "Deletes every object on the layer, blocks included",
  "SETBYLAYER": "Answer N to 'blocks too' unless you mean it",
  "OVERKILL": "Can split and rejoin polylines; select an area, not ALL",
  "PEDIT Multiple": "Keep the fuzz small (0.01); big fuzz joins the wrong lines",
  "ALIGN": "Answer N to scale objects",
  "STRETCH": "Crossing window right-to-left; objects fully inside move",
  "FLATTEN": "Destroys elevations. Never on points, breaklines or 3D polylines",
  "MOVE / SCALE": "Never on survey linework or points: coordinates are real",
  "EXPLODE": "Never on Civil 3D points, labels, surfaces or parcels",
  "BURST": "The block is gone; later block edits won't update it",
  "BEDIT": "Changes every copy of that block in the drawing",
  "Parcels": "Uncheck 'Erase existing entities' to keep the polyline",
  "MAPCLEAN": "Save first; it edits geometry in place",
  "EXPORTTOAUTOCAD": "Makes a separate ACAD- copy; keep working in the original",
  "WBLOCK": "Pick Retain, not Delete from drawing",
  "LTSCALE / PSLTSCALE": "Changes every linetype in the drawing",
  "FILEDIA": "At 0 you get command-line prompts instead of dialogs",
};

// Multi-step commands, walked through. [title, [steps], tip]
const STEPS = [
  ["FILLET R0 - close a corner", [
    "**F ↵** then **R ↵ 0 ↵** (the radius sticks until you change it)",
    "Pick the first line on the part you want to keep",
    "Pick the second line on the part you want to keep"],
    "Hold Shift on the second pick for an R0 corner at any radius."],
  ["EXTEND to a boundary", [
    "**EX ↵ B ↵** (Boundary edges)",
    "Pick the line to extend TO, then **↵**",
    "Pick the line to extend, near the end that should grow"],
    "Shift + pick trims instead of extending."],
  ["ROTATE by Reference - square a symbol to the curb", [
    "**RO ↵**, pick the symbol **↵**",
    "Base point: Insertion snap on the symbol",
    "**R ↵** (Reference), type **0 ↵** (the symbol sits at 0 now)",
    "**P ↵** (Points), pick two points along the curb (Nearest)"],
    "If it's 180 degrees off, pick the curb points the other way."],
  ["MLEADER - label a structure", [
    "Check the leader style is SRV-20 (Annotate › Leaders)",
    "**MLD ↵**, pick the arrow point (Insertion or Node)",
    "Pick the landing, away from other linework",
    "Type the label, then click outside the box"],
    "Leader arms should not cross each other or the text."],
  ["TEXT on a boundary line", [
    "**DT ↵ J ↵ BC ↵**, pick the line's Midpoint",
    "Height **1.6 ↵** if asked (0.08\" at 1\" = 20')",
    "Rotation: pick two points along the line",
    "Type the bearing and distance, **↵ ↵** to finish",
    "Upside down? **TORIENT ↵**, select, **↵**"],
    "Type %%d for the degree sign."],
  ["DIMALIGNED - a setback", [
    "Make V-ANNO-DIMS-E current, style SRV-20",
    "**DAL ↵**, pick the house corner (Endpoint)",
    "Second point: Shift + right-click › Perpendicular, pick the lot line",
    "Click to place the dimension off the building"],
    "LIST the dimension to confirm it's square to the line."],
  ["Viewport at 1\" = 20'", [
    "On the layout: **MV ↵**, pick two corners",
    "Double-click inside the viewport",
    "**Z ↵ 1/20XP ↵**, then pan with the wheel held down (don't zoom)",
    "Double-click outside, select the viewport, CTRL+1, Display locked: Yes"],
    "Check it: a 100' line measures 5\" on the paper."],
  ["PLOT to PDF", [
    "On the layout tab: **CTRL+P**",
    "Printer: DWG To PDF.pc3, paper: the sheet size",
    "Plot area Layout, scale 1:1",
    "Plot style table: monochrome.ctb",
    "Preview, then OK and name the file"],
    "Page Setup Manager saves these so you pick them once."],
  ["CHSPACE - move objects between model and paper", [
    "Model to paper: double-click into the viewport, **CHSPACE ↵**, select **↵**",
    "Paper to model: on the paper, **CHSPACE ↵**, select **↵**",
    "With several viewports, click in the one you want and **↵**"],
    "A 1.6' model text becomes 0.08\" on paper, and back."],
  ["Turn the view to line up with a road", [
    "In the viewport: **UCS ↵ OB ↵**, pick the road centerline",
    "**PLAN ↵ ↵** (Current UCS)",
    "**Z ↵ 1/20XP ↵** and pan back to the site; lock the viewport",
    "Reset: **UCS ↵ ↵** (World) then **PLAN ↵ ↵**"],
    "The north arrow must turn with the view."],
  ["PEDIT Multiple - lines into one polyline", [
    "**PE ↵ M ↵**, select the lines **↵**",
    "Convert to polylines? **Y ↵**",
    "**J ↵** fuzz **0.01 ↵**, then **↵** to finish"],
    "LIST afterwards: one closed polyline shows an area."],
  ["LAYMRG - merge one layer into another", [
    "**LAYMRG ↵**, pick an object on the layer to get rid of **↵**",
    "Pick an object on the layer to keep",
    "Read the names, then **Y ↵**"],
    "N ↵ at either prompt lets you pick layers by name."],
  ["'BD - line by bearing and distance (Civil 3D)", [
    "**L ↵**, pick the start point",
    "**'BD ↵**, confirm the start point",
    "Quadrant: 1 NE, 2 SE, 3 SW, 4 NW",
    "Bearing as DD.MMSS (**88.2453** = 88d24'53\"), then the distance",
    "**ESC** ends 'BD, **↵** ends LINE"],
    "Keeps going: enter the next quadrant for the next course."],
  ["-PURGE Regapps - shrink a slow drawing", [
    "**-PU ↵ R ↵**",
    "Names: **↵** (all)",
    "Verify each name? **N ↵**"],
    "Then PURGE ↵ and AUDIT ↵ Y ↵, save."],
];
const RULES = [
  "Survey linework and points sit on real coordinates: never MOVE, SCALE, ROTATE, FLATTEN or EXPLODE them.",
  "Save (or SAVEAS a copy) before PURGE, AUDIT, OVERKILL, MAPCLEAN or LAYDEL.",
  "Lock every viewport once its scale is set.",
  "CTRL+Z undoes almost anything; OOPS brings back only the last erase.",
];

const RED = "C00000";
const t = (text, o = {}) => new TextRun({ text, font: FONT, size: 16, ...o });
const cmd = (text) => new TextRun({ text, font: MONO, size: 15, bold: true });
const border = { top: thin, bottom: thin, left: thin, right: thin };
let KEEP = false;   // set while building a table that must not split
const cell = (children, w, fill) => new TableCell({
  width: { size: w, type: WidthType.DXA }, borders: border,
  shading: fill ? { type: ShadingType.CLEAR, fill, color: "auto" } : undefined,
  margins: { top: 10, bottom: 10, left: 70, right: 70 },
  children: [new Paragraph({ keepNext: KEEP, children })],
});
const useCell = (name, text, w) => new TableCell({ width: { size: w, type: WidthType.DXA }, borders: border,
  margins: { top: 10, bottom: 10, left: 70, right: 70 },
  children: [new Paragraph({ keepNext: KEEP, children: [t(text)] }),
    ...(WARN[name] ? [new Paragraph({ keepNext: KEEP, children: [t("⚠ " + WARN[name], { size: 14, color: RED, bold: true })] })] : [])] });
const titleRow = (title, n, W = FULL) => new TableRow({ tableHeader: true, cantSplit: true, children: [new TableCell({ columnSpan: n,
  width: { size: W, type: WidthType.DXA }, borders: border, shading: { type: ShadingType.CLEAR, fill: ACCENT, color: "auto" },
  margins: { top: 10, bottom: 10, left: 70, right: 70 },
  children: [new Paragraph({ keepNext: true, children: [t(title, { bold: true, color: "FFFFFF", size: 17 })] })] })] });
const headRow = (heads, w) => {
  KEEP = true;
  const cells = heads.map((h, i) => cell([t(h, { bold: true })], w[i], "D9E2F3"));
  KEEP = false;
  return new TableRow({ tableHeader: true, cantSplit: true, children: cells });
};

let STAGES = true;   // course build shows the Stage column
function groupTable(title, rows) {
  if (!STAGES) return threeTable(title, rows);
  const w = [2300, 2200, FULL - 2300 - 2200 - 700, 700];
  return new Table({ width: { size: FULL, type: WidthType.DXA }, columnWidths: w, rows: [
    titleRow(title, 4), headRow(["Command", "Type", "Use it to", "Stage"], w),
    ...rows.map(([c, k, u, st]) => new TableRow({ cantSplit: true, children: [
      cmdCell(c, w[0]), cell([cmd(k)], w[1]), useCell(c, u, w[2]), cell([t(st)], w[3])] }))] });
}
function threeTable(title, rows, heads = ["Command", "Type", "Use it to"], w = [2300, 2200, FULL - 2300 - 2200]) {
  return new Table({ width: { size: FULL, type: WidthType.DXA }, columnWidths: w, rows: [
    titleRow(title, 3), headRow(heads, w),
    ...rows.map(([c, k, u]) => new TableRow({ cantSplit: true, children: [
      cmdCell(c, w[0]), cell([cmd(k)], w[1]), useCell(c, u, w[2])] }))] });
}
function pairTable(title, rows, codeFirst, W = HALF) {
  const w = [Math.round(W * 0.42), W - Math.round(W * 0.42)];
  return new Table({ width: { size: W, type: WidthType.DXA }, columnWidths: w, rows: [titleRow(title, 2, W),
    ...rows.map(([a, b]) => new TableRow({ cantSplit: true, children: [
      cell([codeFirst ? cmd(a) : t(a, { bold: true })], w[0]), cell([t(b)], w[1])] }))] });
}
// **x** in step text = what you type
const stepRuns = (s) => s.split(/(\*\*[^*]+\*\*)/).filter(Boolean)
  .map((p) => (p.startsWith("**") ? cmd(p.slice(2, -2)) : t(p)));
function stepTable([title, steps, tip]) {
  const W = HALF, w = [300, HALF - 300];
  KEEP = true;
  const rows = steps.map((s, i) => new TableRow({ cantSplit: true, children: [
    cell([t(String(i + 1), { bold: true, color: ACCENT })], w[0]), cell(stepRuns(s), w[1])] }));
  KEEP = false;
  return new Table({ width: { size: W, type: WidthType.DXA }, columnWidths: w, rows: [titleRow(title, 2, W),
    ...rows,
    new TableRow({ cantSplit: true, children: [new TableCell({ columnSpan: 2, width: { size: W, type: WidthType.DXA },
      borders: border, shading: { type: ShadingType.CLEAR, fill: "F2F2F2", color: "auto" }, margins: { top: 10, bottom: 10, left: 70, right: 70 },
      children: [new Paragraph({ children: [t("Tip  ", { bold: true, color: "2E7D32" }), t(tip, { italics: true })] })] })] })] });
}
function rulesBox() {
  return new Table({ width: { size: CONTENT, type: WidthType.DXA }, columnWidths: [CONTENT], rows: [new TableRow({ children: [
    new TableCell({ width: { size: CONTENT, type: WidthType.DXA }, shading: { type: ShadingType.CLEAR, fill: "FCE4E4", color: "auto" },
      borders: { top: none, bottom: none, right: none, left: { style: BorderStyle.SINGLE, size: 18, color: RED } },
      margins: { top: 60, bottom: 60, left: 140, right: 140 },
      children: [new Paragraph({ children: [t("⚠ GOLDEN RULES", { bold: true, color: RED, size: 18 })] }),
        ...RULES.map((r) => new Paragraph({ children: [t("•  " + r, { size: 16 })] }))] })] })] });
}

const gap = () => new Paragraph({ spacing: { after: 80 }, children: [] });
const cellNone = (w, children) => new TableCell({ width: { size: w, type: WidthType.DXA },
  borders: { top: none, bottom: none, left: none, right: none }, children });
const twoCol = (left, right) => new Table({
  width: { size: CONTENT, type: WidthType.DXA }, columnWidths: [HALF, GAP, HALF],
  rows: [new TableRow({ cantSplit: true, children: [cellNone(HALF, left), cellNone(GAP, [new Paragraph("")]), cellNone(HALF, right)] })],
});
const join = (...blocks) => blocks.flatMap((b, i) => (i ? [gap(), b] : [b]));
const header = (sub, note) => [
  new Paragraph({ spacing: { after: 0 }, children: [
    new TextRun({ text: "PARAMETRIX  |  SURVEY CAD TRAINING", bold: true, size: 18, color: ACCENT, font: FONT })] }),
  new Paragraph({ spacing: { after: 40 }, children: [new TextRun({ text: sub, bold: true, size: 34, font: FONT })] }),
  new Paragraph({ spacing: { after: 120 }, border: { bottom: { style: BorderStyle.SINGLE, size: 8, color: ACCENT, space: 4 } },
    children: [t(note, { italics: true, color: "595959" })] }),
];

const page = { size: { width: PAGE_W, height: PAGE_H }, margin: { top: 620, bottom: 620, left: LEFT, right: RIGHT } };
const more = (k) => threeTable(...MORE[k]);

function build(course) {
  STAGES = course;
  const name = "SURVEY_CAD_Command_Sheet";
  const docTitle = "Survey CAD Command Sheet";
  const footers = { default: new Footer({ children: [new Paragraph({ alignment: AlignmentType.RIGHT, children: [
    new TextRun({ text: docTitle + "    ", size: 16, color: "808080", font: FONT }),
    new TextRun({ children: [PageNumber.CURRENT], size: 16, color: "808080", font: FONT })] })] }) };
  const sec = (children, extra = {}) => ({ properties: { page, ...extra }, footers, children });
  const legend = "Grey under a command = where its button is (2D Drafting & Annotation workspace; ▾ = drop-down or expanded panel). Red ⚠ = be careful. ↵ = Enter.";
  const basics = [
    ...join(...GROUPS.map((g) => groupTable(...g))), gap(),
    twoCol(join(pairTable("Typing input", INPUT, true), pairTable("Keys", KEYS, false)),
      [pairTable("Snaps (Shift + right-click)", SNAPS, false)]),
  ];
  const moreCmds = [
    ...join(...["viewports", "layers", "select", "edit", "text", "c3d", "files"].map(more),
      threeTable("System variables (type the name, then the value)", SYSVARS, ["Variable", "Set to", "Why"], [2300, 1200, FULL - 3500])),
    gap(), twoCol([pairTable("Snap tricks (type at a point prompt)", TRICKS, true)], [pairTable("More keys", MOREKEYS, false)]),
  ];
  const doc = new Document({
    creator: "Parametrix Survey", title: docTitle,
    styles: { default: { document: { run: { font: FONT, size: 16 } } } },
    sections: [
      sec([...header(course ? "Finish the Drawing - Command Sheet" : "Command Reference - The Basics",
        (course ? "Every command in the course. Stage = where the guide uses it. " : "The everyday drafting commands. ") + legend), ...basics]),
      sec([...header(course ? "Beyond the Course - More Commands" : "More Commands",
        (course ? "Not needed for the course, but you will use them on real jobs. " : "Less common, but you will use them on real jobs. ") +
        "Express Tools come with AutoCAD and Civil 3D. An apostrophe (') runs a command inside another one."), ...moreCmds],
        { type: SectionType.NEXT_PAGE }),
      sec([...header("Step by Step", "The commands with more than one prompt, one step at a time. Bold = what you type."), rulesBox(), gap()],
        { type: SectionType.NEXT_PAGE }),
      sec(join(...STEPS.map(stepTable)), { type: SectionType.CONTINUOUS, column: { count: 2, space: GAP, equalWidth: true } }),
    ],
  });
  return Packer.toBuffer(doc).then((buf) => {
    const f = path.join(OUT, name + ".docx");
    fs.writeFileSync(f, buf);
    console.log("wrote", f);
  });
}

build(false);
