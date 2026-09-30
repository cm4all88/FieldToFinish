// Finish the Drawing guide: a field-to-finish drawing drafted to a finished base.
// Run build_drawings.py and render_checkpoints.py first.
const fs = require("fs");
const path = require("path");
const { Paragraph } = require("docx");
const G = require("../lib/guide");

const OUT = path.join(__dirname, "output");
const IMG = path.join(OUT, "images");
const A = JSON.parse(fs.readFileSync(path.join(OUT, "answers.json"), "utf8"));
const W = G.CONTENT;
const f2 = (v) => Number(v).toFixed(2);
const typed = (b) => b.replace("°", "d");
const N = A.deed[1];
const L = A.labels;
const lines = (t) => t.split("\\P").join(" / ");
// labels read left to right, so a line that runs west is labeled in the reverse direction
const readable = (b) => (b.endsWith("W") ? b.replace(/^N/, "s").replace(/^S/, "N").replace(/^s/, "S").replace(/W$/, "E") : b);
const CODE = { XMAG: "MAG NAIL", XNL: "MAG NAIL", XHT: "HUB & TACK" };

function checkpoint(n, rows) {
  return [
    G.note(`CHECKPOINT ${n}`, `Open ^^CHECKPOINT_${n}.pdf^^ and compare it with your drawing. Tick every item on the right of the PDF before you go on. **SAVEAS** a copy (^^..._STAGE${n}.dwg^^) so you can come back to it.`, "E8F3EA"),
    new Paragraph({ spacing: { after: 100 } }),
    ...G.figure(path.join(IMG, `checkpoint_${n}.png`), 6.4, `CHECKPOINT_${n}.pdf`),
    G.table(["Check", "Expected"], rows, [3400, W - 3400], { size: 18 }),
  ];
}

const S0 = [
  { part: "Getting started", pageBreak: true },
  { n: "0.1", t: "Open the drawing", noImage: true,
    steps: ["**OPEN** ↵ ^^SURVEY_CAD_START.dxf^^. **SAVEAS** ↵ ^^SURVEY_CAD_[YOUR INITIALS].dwg^^. This is the one drawing you'll finish.",
            "**Z** ↵ **E** ↵. You're looking at the job right after field to finish: the points, the linework drawn from the field codes, the symbols, and the record lines. Nothing is labeled yet."],
    check: "Your title bar shows the .dwg name." },
  { n: "0.2", t: "Units and snaps", noImage: true,
    steps: ["**UNITS** ↵. Length ^^Decimal 0.00^^. Angle ^^Surveyor's Units^^, precision ^^N0d00'00\"E^^. OK.",
            "**OS** ↵. Turn on ^^Endpoint, Midpoint, Center, Node, Intersection, Insertion, Perpendicular^^. ^^Node^^ snaps to a survey point; ^^Insertion^^ snaps to a symbol."],
    check: "**LIST** ↵ on any boundary line shows a bearing, not a decimal angle." },
  { n: "0.3", t: "Read the points", noImage: true,
    why: "Every point shows its number, its elevation and the crew's description. The description is how you know what something is: CB is a catch basin, CON 18 . 25 is an 18\" conifer with a 25' drip line, BLFF is a building finished floor.",
    steps: ["Zoom into the house. Find a BLFF point and an FIP (found iron pipe).",
            "**ID** ↵, snap ^^Node^^ on any point. X is the Easting and Y is the Northing.",
            "When the points get in the way, freeze layer ^^V-NODE-E^^ (**LAYFRZ** ↵ pick a point). Thaw it again when you need the data."],
    check: "You can freeze and thaw the points." },
];

const S1 = [
  { part: "Stage 1 - Fix the boundary" },
  { n: "1.1", t: "Close the SW corner (FILLET)", noImage: true,
    why: "At the SW corner the west line stops short and the south line runs past.",
    steps: ["**F** ↵ **R** ↵ **0** ↵.", "Pick the west line near the corner, then the south line on the part you keep (east of the corner)."],
    check: "The corner closes cleanly." },
  { n: "1.2", t: "Draw the missing north line by bearing", noImage: true,
    steps: ["**LAYMCUR** ↵ pick a boundary line.",
            `**L** ↵, snap ^^Endpoint^^ on the north end of the west line, then type **@${f2(N.dist)}<${typed(N.bearing)}** ↵ ↵.`],
    check: "The line ends at the NE corner, above the east line." },
  { n: "1.3", t: "EXTEND the east line", noImage: true,
    steps: ["The east line stops short of the NE corner. **EX** ↵ **B** ↵ pick the new north line ↵, then pick the east line near its north end ↵."],
    check: "The east line meets the north line." },
  { n: "1.4", t: "TRIM the south line", noImage: true,
    steps: ["The south line runs past the SE corner. **TR** ↵ pick the part that sticks out past the east line ↵."],
    check: "The south line stops at the east line." },
  { n: "1.5", t: "Join and check the area", noImage: true,
    steps: ["**J** ↵ pick the four boundary lines ↵. **LI** ↵ the boundary.",
            "**DI** ↵ from each corner to the found iron pipe (FIP) next to it."],
    check: `Area ${Number(A.area_sf).toLocaleString("en-US", { minimumFractionDigits: 2 })} sq ft, perimeter ${f2(A.perimeter)}'. The pipes sit 0.45' (NW), 0.64' (NE) and 0.85' (SE) from the record corners. That's normal; don't move the boundary.` },
];

const S2 = [
  { part: "Stage 2 - Clean up the field-to-finish linework" },
  { n: "2.1", t: "Erase the jumpers", noImage: true,
    why: "Points 65000 and up were added in the office after the field work. Because they're numbered last, field to finish tied each one onto the last open line of its code and drew a long line across the site.",
    steps: [...A.jumpers.map((j) => `The ^^${j.figure}^^ line from point ${j.from} to ${j.to} (${j.length}'). Pick it and **E** ↵.`)],
    check: "No straight lines cut across the lot between unrelated features." },
  { n: "2.2", t: "Erase the ASPH zig-zag", noImage: true,
    steps: ["ASPH points are single elevation shots on pavement, not a line. Field to finish connected them anyway into one long zig-zag on V-SURF-ASPH-E. Erase it. Keep the points."],
    check: "The zig-zag is gone; the ASPH points are still there." },
  { n: "2.3", t: "Draft what the codes couldn't", noImage: true,
    steps: [`Three concrete wall footings were shot as 4 corners each (CONC): ${A.pads.map((p) => p.join("-")).join(", ")}. Layer ^^V-SURF-WALL-E^^. **PL** ↵ ^^Node^^ to each corner in order, **C** ↵ to close.`,
            `Eight curb-end ties, flowline (CG) to back of curb (TBC), along Soundview: ${A.ties.map((t) => t.join("-")).join(", ")}. Layer ^^V-SURF-CURB-E^^. **PL** ↵ Node to Node ↵.`],
    check: "3 footings and 8 ties." },
  { n: "2.4", t: "Fix a layer and an override", noImage: true,
    steps: ["The chain link fence along the south line is on layer 0. **LAYMCH** ↵ pick it ↵, then pick a line on ^^V-SURF-FENC-CHNL-E^^ (or type N to pick the layer by name).",
            "One Harborview curb line is red and heavy. **MA** ↵ pick a good curb line, then the red one ↵."],
    check: "Fence on V-SURF-FENC-CHNL-E; the curb is ByLayer." },
];

const S3 = [
  { part: "Stage 3 - Storm and sewer" },
  { n: "3.1", t: "Rotate the catch basins", noImage: true,
    why: "The storm symbols came in at 0°. A catch basin grate is drawn square to the curb.",
    steps: ["For each rectangular catch basin (CB, CBS): **RO** ↵ pick it ↵, base point ^^Insertion^^.",
            "**R** ↵ (Reference): snap ^^Endpoint^^ on two corners along one long side of the grate.",
            "**P** ↵ (Points): pick two points along the gutter line next to it (Shift + right-click > Nearest)."],
    check: `CB #10144 reads ${A.cb_rot["10144"]} in Properties. Round symbols (manholes, drains) don't need rotating.` },
  { n: "3.2", t: "Draw the storm pipes", noImage: true,
    steps: ["Layer ^^V-UTIL-STRM-E^^ (linetype shows ---SD---).",
            "Use the measuredowns table below. Each invert gives a pipe's size and the direction it leaves the structure. **PL** ↵ from the structure's ^^Insertion^^ point to the next structure in that direction. A pipe that leaves the site runs to the edge of the drawing in its direction.",
            "Compare with CHECKPOINT_3 before labeling."],
    check: "Every invert in the table has a pipe." },
  { n: "3.3", t: "Label the structures (MLEADER)", noImage: true,
    steps: ["Multileader style ^^SRV-20^^, layer ^^V-UTIL-STRM-TEXT-E^^. **MLD** ↵ arrow on the structure, landing clear of the street, then type the lines from the table (Enter between lines).",
            `Sewer, layer ^^V-UTIL-SSWR-TEXT-E^^: ${A.ssmh.map((s) => `{{${s.d} / RIM=${f2(s.z)}}} at point ${s.p}`).join("; ")}.`],
    check: `${A.structures.length} storm leaders and 2 sewer leaders.` },
];

const S4 = [
  { part: "Stage 4 - Labels and dimensions" },
  { n: "4.1", t: "Rotate the road names", noImage: true,
    steps: ["SOUNDVIEW DR and HARBORVIEW DR came in flat. **RO** ↵ pick the name ↵, base point: its middle (Insertion snap).",
            "**R** ↵ **0** ↵ then **P** ↵ and pick two points along the road centerline.",
            "If it ends up upside down: **TORIENT** ↵ pick it ↵ ↵."],
    check: "Both names run along their streets and read left to right (or bottom to top)." },
  { n: "4.2", t: "Label the control points", noImage: true,
    steps: ["Layer ^^V-CTRL-PMX_-TEXT-E^^. One MLEADER per control point: {{PMX #[number]}} / {{[type]}}.",
            "Types from the description: XMAG and XNL = MAG NAIL, XHT = HUB & TACK."],
    check: A.control.map((c) => `PMX #${c.p} ${CODE[c.d] || c.d}`).join("; ") + "." },
  { n: "4.3", t: "Label the found monuments", noImage: true,
    steps: ["Layer ^^V-CTRL-MONU-TEXT-E^^. Text from the crew's field notes:", ...L.monuments.map((m) => `{{${m}}}`)],
    check: "4 monument leaders." },
  { n: "4.4", t: "Label the trees", noImage: true,
    why: "Tree descriptions are: type, trunk size(s), a dot, drip radius. CON = conifer (label CFR); DEC and MAP = deciduous (label DEC).",
    steps: ["Layer ^^V-SURF-VEGE-TEXT-E^^. One trunk: {{CON 18 . 25}} becomes {{18\" CFR / 25' DRIP}}.",
            "More than one trunk is a cluster: {{CON 22 18 . 28}} becomes {{CLUSTER / 22\"&18\" CFR / 28' DRIP}}. Repeated sizes get a count: 18\"(3).",
            "Label every tree. The full answer list is in the appendix."],
    check: `${A.trees.length} tree leaders.` },
  { n: "4.5", t: "Surface callouts and FFEs", noImage: true,
    steps: ["Small MTEXT callouts (style Survey, 1.6') on the matching -TEXT-E layer: ASPH, CONC, CW (concrete walk), CG (curb & gutter), GRASS, CONC PATIO, GARAGE, HOUSE, CONC RETWALL, 4' CHAIN LINK FENCE, THICKENED ASPH EDGE and the rest. Use CHECKPOINT_4 for what goes where.",
            `FFE labels from the BLFF points, layer ^^V-TOPO-TEXT-E^^: ${Object.values(A.ffe).map((v) => "FFE=" + f2(v)).join(", ")}.`],
    check: "Every paved, concrete and grass area is called out." },
  { n: "4.6", t: "Boundary labels and setbacks", noImage: true,
    steps: ["Layer ^^V-PROP-BNDY-TEXT-E^^. **DT** ↵ **J** ↵ **BC** ↵ at each line's midpoint, height 1.6, rotated with the line, reading left to right. Type %%d for the degree sign.",
            ...A.deed.map((c) => `${c.from}-${c.to}: {{${readable(c.bearing)} ${f2(c.dist)}'}}`),
            "Layer ^^V-ANNO-DIMS-E^^, dim style SRV-20. **DAL** ↵ from the garage SE corner Perpendicular to the south line, and from the house NE corner Perpendicular to the east line."],
    check: `Setbacks ${f2(A.dim_garage)}' and ${f2(A.dim_house)}'.` },
];

const S5 = [
  { part: "Stage 5 - Sheet and plot" },
  { n: "5.1", t: "QA", noImage: true,
    steps: ["Freeze ^^V-NODE-E^^ (the finished base doesn't show point labels).",
            "**QSELECT** Layer = 0: nothing should be selected. **PURGE** ↵ and **AUDIT** ↵ **Y** ↵."],
    check: "Nothing on layer 0." },
  { n: "5.2", t: "Viewport and plot", noImage: true,
    steps: ["TOPO SHEET layout. Layer ^^G-ANNO-VPRT^^. **MV** ↵ pick the dashed box. Double-click inside, **Z** ↵ **1/20XP** ↵, center the site, double-click outside, lock the viewport (CTRL+1 > Display locked = Yes).",
            "Freeze TRAIN-NOTES. **PLOT**: DWG To PDF, ANSI expand D, 1:1, monochrome.ctb."],
    check: `In paper space the north line measures ${(N.dist / 20).toFixed(2)}".` },
];

const body = [];
body.push(...G.cover({ level: "1-5", title: "Finish the Drawing",
  intro: "Field to finish has run. The points are in, the linework is drawn from the field codes, and the symbols are placed. Your job is what a survey drafter does next: fix the boundary, clean up what the codes got wrong, draw the storm lines, label everything, and plot the sheet. Five stages. After each one, a checkpoint PDF shows what your drawing should look like.",
  info: [["You start with", "SURVEY_CAD_START.dxf"], ["Checkpoints", "CHECKPOINT_1.pdf to CHECKPOINT_5.pdf"],
         ["Answer key", "SURVEY_CAD_COMPLETED.dxf"], ["Reference", "SURVEY_CAD_POINTS.txt (the raw point file)"],
         ["Software", "AutoCAD or Civil 3D, 2018 or later"], ["Time", "About a day"]] }));
body.push(G.H2("Typing coordinates and bearings", { pageBreakBefore: true }));
body.push(G.table(["You want", "Type"], [
  ["A distance and bearing from the last point", `**@${f2(N.dist)}<${typed(N.bearing)}**`],
  ["The ° symbol in text", "**%%d**"],
  ["An absolute coordinate (Easting,Northing)", "**#1125170.86,733969.17**"],
], [4300, W - 4300]));
body.push(G.P("Work the stages in order and check each checkpoint before you move on. If you get stuck, zoom into the checkpoint PDF (it's vector) or open SURVEY_CAD_COMPLETED.dxf."));

const cps = {
  1: [["North line", `${N.bearing}  ${f2(N.dist)}'`], ["Area", `${f2(A.area_sf)} sq ft`], ["Perimeter", `${f2(A.perimeter)}'`]],
  2: [["Jumpers erased", A.jumpers.length], ["ASPH zig-zag", "erased"], ["Footings / ties", `3 / ${A.ties.length}`], ["Layer 0", "empty"]],
  3: [["CBs", "square to the curb"], ["Storm leaders", A.structures.length], ["Sewer leaders", 2]],
  4: [["Control", L.control.length], ["Monuments", L.monuments.length], ["Trees", A.trees.length], ["FFE", Object.values(A.ffe).map(f2).join(", ")], ["Setbacks", `${f2(A.dim_garage)}', ${f2(A.dim_house)}'`]],
  5: [["Viewport", "1\" = 20', locked"], ["North line on paper", `${(N.dist / 20).toFixed(2)}"`]],
};
const allEx = [...S0];
body.push(...G.exerciseBlocks(S0, IMG));
for (const [ex, n] of [[S1, 1], [S2, 2], [S3, 3], [S4, 4], [S5, 5]]) {
  body.push(...G.exerciseBlocks(ex, IMG));
  if (n === 3) {
    body.push(G.H2("Storm measuredowns (crew field notes)"));
    body.push(G.table(["Structure", "Label text"], A.structures.map((s) => [`#${s.no}`, lines(s.text)]), [1400, W - 1400], { size: 18 }));
  }
  body.push(...checkpoint(n, cps[n]));
  allEx.push(...ex);
}
body.push(G.H1("Appendix - tree labels"));
body.push(G.table(["Point", "Description", "Label"], A.trees.map((t) => [t.p, t.desc, lines(t.label)]), [1000, 3000, W - 4000], { size: 18 }));
G.buildGuide({ outFile: path.join(OUT, "SURVEY_CAD_Guide.docx"), level: "1-5", title: "Finish the Drawing", body, exercises: allEx });
