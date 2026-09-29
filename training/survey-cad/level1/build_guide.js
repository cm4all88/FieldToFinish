// Level 1 guide. Run build_drawings.py and render_images.py first.
const fs = require("fs");
const path = require("path");
const G = require("../lib/guide");

const OUT = path.join(__dirname, "output");
const IMG = path.join(OUT, "images");
const A = JSON.parse(fs.readFileSync(path.join(OUT, "answers.json"), "utf8"));
const PTS = Object.fromEntries(fs.readFileSync(path.join(__dirname, "..", "source", "points.txt"), "utf8")
  .split(/\r?\n/).map((l) => l.split(",")).filter((f) => f[0] && f[0].trim())
  .map((f) => [f[0], { n: +f[1], e: +f[2], z: +f[3], d: f[4], note: f.slice(5).join(",") }]));
const typed = (b) => b.replace("°", "d");
const f2 = (v) => Number(v).toFixed(2);
const W = G.CONTENT;

const EX = [
  { part: "Part 1 - Set up the drawing", pageBreak: true },
  { n: "01", t: "Open the drawing and save it as a DWG", noImage: true,
    why: "START is a DXF so it opens in any AutoCAD version. You work in a DWG.",
    steps: ["**OPEN** ↵ and pick ^^SURVEY_CAD_L1_START.dxf^^.",
            "**SAVEAS** ↵. {{Files of type}}: ^^AutoCAD 2018 Drawing (*.dwg)^^. Name it ^^SURVEY_CAD_L1_[YOUR INITIALS].dwg^^.",
            "**Z** ↵ **E** ↵ to zoom to the whole drawing.",
            "The red numbered bubbles are on layer ^^TRAIN-NOTES^^ (no plot). Each number is an exercise in this guide."],
    check: "The title bar shows your .dwg name." },
  { n: "02", t: "Set survey units (UNITS)",
    why: "The coordinates are Washington State Plane South, in US survey feet. AutoCAD's default angles are decimal degrees measured counterclockwise from east, so it won't accept a bearing until you change the units.",
    steps: ["**LIST** ↵ and pick the east boundary line at bubble 02 ↵. Note the angle is a plain decimal number.",
            "**UNITS** ↵. Length: ^^Decimal^^, precision ^^0.00^^. Angle: ^^Surveyor's Units^^, precision ^^N0d00'00\"E^^. Insertion scale: ^^US Survey Feet^^ (or Feet). OK.",
            "**LIST** ↵ the same line again."],
    check: `LIST now reads ${A.east_bearing.replace("°", "d")} and ${f2(A.east_dist)}.`,
    tip: "Leave Direction (base angle) at East. Bearings are measured from north no matter what, and changing the base angle breaks typed coordinates." },
  { n: "03", t: "Object snaps and ID a control point",
    steps: ["**OS** ↵. Check ^^Endpoint, Midpoint, Center, Node, Intersection, Insertion, Perpendicular^^. Leave {{Nearest}} off. OK. (**F3** toggles snaps.)",
            "**ID** ↵. Snap ^^Insertion^^ on the hub & tack symbol for ^^PMX #2001^^ (bubble 03).",
            "Compare with point 2001 in the point list (Appendix)."],
    check: `X = ${f2(A.id_2001[0])}, Y = ${f2(A.id_2001[1])}. In AutoCAD ^^X is the Easting and Y is the Northing^^. The point list is written P,N,E,Z,D, so watch the order.` },
  { n: "04", t: "Freeze the contours while you edit", noImage: true,
    why: "Contours cross everything, and TRIM, EXTEND and snaps grab them. Freezing layers you don't need is a daily habit.",
    steps: ["**LAYFRZ** ↵ and pick a minor contour, a major contour and a contour label ↵.",
            "Or: **LA** ↵ and click the sun icon for ^^V-TOPO-CONT-MAJR-E, V-TOPO-CONT-MINR-E, V-TOPO-CONT-TEXT-E^^.",
            "**LAYISO** ↵, pick a boundary line ↵ to see only the boundary. **LAYUNISO** ↵ to bring everything back."],
    check: "The contours are gone and everything else is still showing. You'll thaw them in Exercise 20.",
    tip: "OFF only hides a layer. FREEZE also removes it from regens, selections and ZOOM EXTENTS." },
  { part: "Part 2 - Check the boundary" },
  { n: "05", t: "Inverse between two control points (DIST)",
    steps: ["**DI** ↵. Snap ^^Insertion^^ on the mag nail for ^^PMX #2000^^ (bubble 05).",
            "Snap ^^Insertion^^ on the hub & tack for ^^PMX #2002^^ (southwest of it).",
            "Record the distance and bearing: ____________________"],
    check: "Compare with the Answer Key. The bearing runs from the first pick to the second." },
  { n: "06", t: "Close the SW boundary corner (FILLET radius 0)",
    why: "At the SW corner (bubble 06) the west line stops 1.5' short and the south line runs 1.2' past.",
    steps: ["**LAYFRZ** anything else that gets in the way, or zoom in close.",
            "**F** ↵ **R** ↵ **0** ↵.",
            "Pick the west line near the corner, then the south line ^^on the part you want to keep^^ (east of the corner)."],
    check: `The corner closes. **ID** ↵ on it reads X = ${f2(A.corners.SW[0])}, Y = ${f2(A.corners.SW[1])}.` },
  { n: "07", t: "Draw the north line by bearing and distance",
    why: "The north boundary line is missing. You'll draw it from the record call and check it closes on the NE corner.",
    steps: ["**LAYMCUR** ↵ and pick a boundary line. V-PROP-BNDY-E becomes current.",
            "**L** ↵. Snap ^^Endpoint^^ on the north end of the west line (the NW corner).",
            `Type **@${f2(A.north_dist)}<${typed(A.north_bearing)}** ↵ ↵.`,
            "**ID** ↵ on the new end. It should land exactly on the north end of the east line.",
            "**DI** ↵ from the new NE corner to the found iron pipe symbol just north of it (point 10494)."],
    check: `The line closes on the east line. The found pipe is ${f2(A.ne_to_fip)}' from the record corner. That's normal: the boundary is drawn from record, and monuments are shown where they were found.` },
  { n: "08", t: "Join the boundary and check the area",
    steps: ["**J** ↵. Pick the four boundary lines ↵. The command line says {{4 objects converted to 1 polyline}}.",
            "**LI** ↵ and pick the boundary (or **AREA** ↵ **O** ↵)."],
    check: `Closed polyline. Area = ${A.area_sf.toLocaleString("en-US", { minimumFractionDigits: 2 })} sq ft (${A.area_ac} ac). Perimeter = ${f2(A.perimeter)}.`,
    tip: "If JOIN leaves pieces, a corner doesn't touch. Go back to Exercise 06 or 07." },
  { part: "Part 3 - Clean up the linework" },
  { n: "09", t: "Fix an object on the wrong layer (LAYMCH)",
    why: "The small retaining wall at the house's NE corner (bubble 09) is on layer 0.",
    steps: ["**LAYMCH** ↵. Pick the wall ↵. Pick any other retaining wall line as the destination.",
            "**CTRL+1**. Layer = V-SURF-WALL-E."],
    check: "The wall shows in the retaining-wall color." },
  { n: "10", t: "Clear property overrides (MATCHPROP)",
    why: "One Harborview curb line (bubble 10) has a red color, dashed linetype and heavy lineweight typed onto it.",
    steps: ["**MA** ↵. Pick a good curb line next to it first (the source).",
            "Pick the red dashed line ↵. **CTRL+1**: Color, Linetype and Lineweight read ByLayer."],
    check: "The curb matches the lines beside it." },
  { n: "11", t: "TRIM an overshoot",
    why: "The bottom edge of the stair-well outline on the house's east side (bubble 11) runs 3' into the house.",
    steps: ["**TR** ↵. Pick the part of the line that is ^^inside^^ the house ↵."],
    check: "The line stops at the house wall." },
  { n: "12", t: "EXTEND to a chosen boundary",
    why: "The thickened asphalt edge near Harborview (bubble 12) stops 4' short of the curb.",
    steps: ["**EX** ↵ **B** ↵ (Boundary edges). Pick the curb line it should reach ↵.",
            "Pick the asphalt edge near its short end ↵."],
    check: "The asphalt edge ends on the curb line.",
    tip: "Without the B option, Quick mode extends to the first thing it hits, which might be the wrong line. That's another reason to freeze the contours." },
  { part: "Part 4 - Symbols" },
  { n: "13", t: "Place a tree at its surveyed coordinates (COPY)",
    why: "Point 10222, a deciduous tree cluster with a 25' drip line (bubble 13), lost its symbol.",
    steps: ["**CO** ↵. Pick any deciduous tree symbol (a scalloped circle) ↵.",
            "Base point: snap ^^Insertion^^ on that tree.",
            `Second point: type **#${f2(A.tree_pt[0])},${f2(A.tree_pt[1])}** ↵ ↵. That's Easting, Northing from the point list. The ^^#^^ makes it an absolute coordinate when Dynamic Input is on.`],
    check: "The tree sits on the leader arrow for the 10\",8\"(2),6\"(2) DEC cluster.",
    tip: "Typing coordinates is X,Y, which is Easting,Northing. Swap them and the tree lands miles away." },
  { n: "14", t: "Align a catch basin (ROTATE Reference)",
    why: "The catch basin for CB #10144 (bubble 14) is 35° off.",
    steps: ["**RO** ↵. Pick the catch basin ↵. Base point: ^^Insertion^^ snap on it.",
            "**R** ↵ (Reference). Snap ^^Endpoint^^ on two corners along one long side of the grate.",
            `Type the new angle as a bearing: **${typed(A.cb_bearing)}** ↵.`],
    check: `The grate lines up with the gutter. In Properties, Rotation reads ${A.cb_bearing}.` },
  { part: "Part 5 - Annotation" },
  { n: "15", t: "Label the north line (TEXT)",
    steps: ["Layer ^^V-PROP-BNDY-TEXT-E^^ current. Text style ^^Survey^^ current.",
            "**DT** ↵ **J** ↵ **BC** ↵. Snap ^^Midpoint^^ of the north line.",
            "Height **1.6** ↵ (0.08\" × 20).",
            `Rotation **${typed(A.north_rot_bearing)}** ↵.`,
            `Type **${A.north_bearing.replace("°", "%%d")} ${f2(A.north_dist)}'** ↵ ↵.`,
            `**M** ↵, pick the text ↵, any base point, then **@1<${typed(A.label_offset_bearing)}** ↵ to move it 1' off the line.`],
    check: "The label reads left to right just outside the line." },
  { n: "16", t: "Make text readable (TORIENT)",
    steps: ["**TORIENT** ↵. Pick HARBORVIEW DR (bubble 16) ↵. Press ↵ to accept {{<Most readable>}}."],
    check: "The street name reads right side up in the same place." },
  { n: "17", t: "Add the HOUSE label (MTEXT)",
    steps: ["Layer ^^V-SURF-BLDG-TEXT-E^^ current.",
            "**MT** ↵. Pick a point in the middle of the house (bubble 17). **J** ↵ **TC** ↵. **H** ↵ **1.6** ↵. **W** ↵ **6** ↵.",
            "Type ^^HOUSE^^. Close the editor. Compare it with the GARAGE label."],
    check: "HOUSE matches the GARAGE label: same layer, style and height." },
  { n: "18", t: "Label a structure (MLEADER)",
    why: "CB #10144 lost its leader. Rebuild it from the field data.",
    steps: ["Annotate tab > Leaders: make multileader style ^^SRV-20^^ current. Layer ^^V-UTIL-STRM-TEXT-E^^ current.",
            "**MLD** ↵. Arrow: ^^Insertion^^ snap on the catch basin (bubble 18). Landing: up and to the right, clear of the street.",
            `Type four lines: {{CB #10144}} / {{RIM=${f2(PTS["10144"].z)}}} / {{IE 8" DIP (W)=73.74}} / {{BOTTOM=73.74}}. Close the editor.`],
    check: "It matches the other CB leaders along Soundview Dr.",
    tip: `RIM is point 10144's elevation (${PTS["10144"].z}, rounded to 0.01). Inverts and bottom come from the crew's measuredowns.` },
  { n: "19", t: "Dimension two setbacks (DIMALIGNED)",
    steps: ["Dim style ^^SRV-20^^ current. Layer ^^V-ANNO-DIMS-E^^ current (create it in LA if it doesn't exist).",
            "**DAL** ↵. ^^Endpoint^^ on the garage's SE corner (bubble 19). Second point: Shift + right-click > ^^Perpendicular^^ > pick the south boundary. Place it about 4' east.",
            "↵ to repeat. ^^Endpoint^^ on the house's NE corner > ^^Perpendicular^^ to the east boundary. Place it about 4' north."],
    check: `The dimensions read ${f2(A.dim_garage)}' and ${f2(A.dim_house)}'.` },
  { part: "Part 6 - Sheet and plot" },
  { n: "20", t: "Thaw the contours and make the viewport", noImage: true,
    steps: ["**LA** ↵. Thaw the three contour layers.",
            "Click the ^^TOPO SHEET^^ layout tab. Layer ^^G-ANNO-VPRT^^ current.",
            "**MV** ↵. Pick the corners of the red dashed box.",
            "Double-click inside. **Z** ↵ **1/20XP** ↵. Pan (hold the wheel) until the site is centered.",
            "Double-click outside. Select the viewport edge, **CTRL+1**: scale 1\" = 20' (or custom 0.05), {{Display locked}} = Yes."],
    check: `In paper space, **DI** ↵ along the north boundary measures ${(A.north_dist / 20).toFixed(2)}".` },
  { n: "21", t: "Plot to PDF", noImage: true,
    steps: ["**PLOT** ↵. Check the page setup: ^^DWG To PDF.pc3^^, ^^ANSI expand D (34 x 22)^^, Layout, 1:1, ^^monochrome.ctb^^.",
            "Preview, then OK and save the PDF."],
    check: "No red bubbles, no dashed box, no viewport frame. All text is readable." },
  { n: "22", t: "Clean up and save", noImage: true,
    steps: ["**LA** ↵. Freeze ^^TRAIN-NOTES^^.",
            "**PU** ↵. Check {{Purge nested items}}, Purge All. TEMP-OLD and OLD-SYMBOL go away.",
            "**AUDIT** ↵ **Y** ↵. **QSAVE** ↵."],
    check: "Compare your drawing with SURVEY_CAD_L1_COMPLETED.dxf." },
];

const body = [];
body.push(...G.cover({ level: 1, title: "AutoCAD Basics for Survey Drafting",
  intro: "You'll finish a real topographic survey base in plain AutoCAD: set survey units, check the boundary, clean up linework, place symbols, annotate, and plot a 1\" = 20' sheet. The site is a real survey (NAD 83 Washington South, NAVD 88), used for training only.",
  info: [["Start file", "SURVEY_CAD_L1_START.dxf"], ["Answer key", "SURVEY_CAD_L1_COMPLETED.dxf"],
         ["Software", "AutoCAD 2018 or later, or Civil 3D used as plain AutoCAD"], ["Time", "About 2 to 3 hours"],
         ["Scale", "1\" = 20' on an ANSI expand D (34 x 22) sheet"]] }));
body.push(new (require("docx").Paragraph)({ pageBreakBefore: true }));
body.push(G.H2("The drawing you will finish"));
body.push(G.P("START with the red exercise bubbles. The contours are frozen for most of the exercises."));
body.push(...G.figure(path.join(IMG, "start_model.png"), 6.9, "SURVEY_CAD_L1_START.dxf"));

body.push(G.H1("Survey CAD basics"));
body.push(G.H2("Coordinates and angles"));
body.push(G.P("AutoCAD's ^^X is the Easting^^ and ^^Y is the Northing^^. Point files are usually P,N,E,Z,D, so the order flips when you type coordinates. After ^^Surveyor's Units^^ are set (Exercise 02), you type bearings directly:"));
body.push(G.table(["You want", "Type"], [
  ["A point 100' at N 45°30'15\" E from the last point", "**@100<N45d30'15\"E**"],
  ["An absolute coordinate (Dynamic Input on)", "**#1125170.86,733969.17**"],
  ["The ° symbol in text", "**%%d**"],
], [4300, W - 4300]));
body.push(G.H2("Scale and text"));
body.push(G.P("The base is drafted for ^^1\" = 20'^^. Model-space size = plotted size × 20. Standard text is 0.08\" plotted, which is ^^1.6'^^ in model space. Linetypes are defined in plotted inches with ^^LTSCALE = 20^^ and ^^PSLTSCALE = 0^^. Level 2 moves this onto annotative scaling."));
body.push(G.H2("Layers"));
body.push(G.P("The base uses the Parametrix survey layers: V- (survey), then the feature (PROP, SURF, UTIL, TOPO, CTRL), a minor part, and -E for existing. For example V-SURF-CURB-E, V-UTIL-STRM-SYMB-E, V-TOPO-CONT-MINR-E. Symbols are SSV- blocks."));

body.push(...G.exerciseBlocks(EX, IMG));

body.push(G.H1("The finished sheet"));
body.push(...G.figure(path.join(IMG, "completed_sheet.png"), 7.2, "TOPO SHEET from the COMPLETED answer key (monochrome preview)"));

body.push(G.H1("Answer key"));
body.push(G.table(["Ex", "Result"], [
  ["02", `East line: ${A.east_bearing}, ${f2(A.east_dist)}'`],
  ["03", `PMX #2001: X ${f2(A.id_2001[0])}, Y ${f2(A.id_2001[1])}`],
  ["05", `PMX #2000 to #2002: ${A.inverse.bearing}, ${f2(A.inverse.dist)}'  (reverse ${A.inverse.reverse})`],
  ["06", `SW corner: X ${f2(A.corners.SW[0])}, Y ${f2(A.corners.SW[1])}`],
  ["07", `North line ${A.north_bearing} ${f2(A.north_dist)}' closes on NE corner X ${f2(A.corners.NE[0])}, Y ${f2(A.corners.NE[1])}. Found pipe 10494 is ${f2(A.ne_to_fip)}' away.`],
  ["08", `Area ${A.area_sf.toLocaleString("en-US", { minimumFractionDigits: 2 })} sq ft = ${A.area_ac} ac; perimeter ${f2(A.perimeter)}'`],
  ["14", `CB #10144 rotation ${A.cb_bearing}`],
  ["19", `Garage to south line ${f2(A.dim_garage)}'; house to east line ${f2(A.dim_house)}'`],
], [700, W - 700]));
body.push(G.H2("Points used in Level 1 (PNEZD)"));
const used = ["1009", "2000", "2001", "2002", "2003", "10144", "10153", "10222", "10494", "15000"];
body.push(G.table(["Pt", "Northing", "Easting", "Elev", "Description"],
  used.filter((p) => PTS[p]).map((p) => [p, PTS[p].n.toFixed(3), PTS[p].e.toFixed(3), PTS[p].z.toFixed(2), PTS[p].d]),
  [800, 1800, 1800, 1100, W - 5500], { size: 18 }));

body.push(G.H1("Commands used"));
body.push(G.table(["Command", "Alias", "Used for"], [
  ["UNITS", "UN", "Surveyor's angles, precision"], ["OSNAP", "OS", "Running snaps"], ["ID / DIST", "DI", "Coordinates, inverse"],
  ["LAYFRZ / LAYISO / LAYMCUR / LAYMCH", "", "Layer control"], ["LINE", "L", "Lines by bearing and distance"],
  ["FILLET", "F", "Close corners (R = 0)"], ["JOIN / LIST / AREA", "J / LI", "Boundary polyline, area"],
  ["MATCHPROP", "MA", "Clear overrides"], ["TRIM / EXTEND", "TR / EX", "Overshoots and undershoots"],
  ["COPY", "CO", "Place a symbol at coordinates"], ["ROTATE (Reference)", "RO", "Align a symbol"],
  ["TEXT / MTEXT / TORIENT", "DT / MT", "Labels"], ["MLEADER", "MLD", "Structure callouts"],
  ["DIMALIGNED", "DAL", "Setbacks"], ["MVIEW / PLOT", "MV", "Sheet"], ["PURGE / AUDIT", "PU", "Cleanup"],
], [3400, 1300, W - 4700]));

G.buildGuide({ outFile: path.join(OUT, "SURVEY_CAD_L1_Guide.docx"), level: 1, title: "AutoCAD Basics", body, exercises: EX });
