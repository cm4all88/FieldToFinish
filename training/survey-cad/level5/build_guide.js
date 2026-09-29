// Level 5 guide (Civil 3D utilities and finishing). Run build_drawings.py first.
const fs = require("fs");
const path = require("path");
const G = require("../lib/guide");

const OUT = path.join(__dirname, "output");
const IMG = path.join(OUT, "images");
const A = JSON.parse(fs.readFileSync(path.join(OUT, "answers.json"), "utf8"));
const W = G.CONTENT;
const cl = A.centerlines[0];
const f2 = (v) => (v == null ? "-" : Number(v).toFixed(2));

const EX = [
  { part: "Part 1 - Storm pipe network", pageBreak: true },
  { n: "01", t: "Read the structure data", noImage: true,
    why: "Every storm structure was opened and measured: rim, each pipe's size, material, direction and invert, and the bottom. That's what you build the network from.",
    steps: ["Open ^^SURVEY_CAD_L5_START.dxf^^ (structures, curbs, right of way, centerline; no pipes or labels) and xref it into your Level 4 drawing, or work in it directly in Civil 3D.",
            "Use the Structure Table at the end of this guide. It comes from the crew's measuredowns.",
            "Find the problem in the data before you model it."],
    check: A.findings.join(" ") },
  { n: "02", t: "Create the structures", noImage: true,
    steps: ["Home > Pipe Network > ^^Pipe Network Creation Tools^^. Name ^^STRM-EX^^. Use the office ^^Storm^^ parts list. Surface ^^EG^^ (Level 4).",
            "Structure: catch basin (use a manhole for SDMH #10004). Place ^^Structures only^^, snapping ^^Node^^ to each trunk point, south to north: " + A.structures.map((s) => s.name.replace(" (SQUARE BEEHIVE GRATE)", "")).join(", ") + ".",
            "For each structure, Properties: ^^Rim^^ = measured rim, ^^Sump depth^^ = rim minus BOTTOM (or set the bottom elevation directly)."],
    check: "11 structures. Their rims match the leaders, not the surface. A rim is measured, and the surface is interpolated." },
  { n: "03", t: "Connect the trunk and set the measured inverts", noImage: true,
    steps: ["Pipes only: connect each structure to the next one north. Size and material from the Pipe Table.",
            "For each pipe, Pipe Properties: ^^Start Invert^^ = upstream (N) invert, ^^End Invert^^ = downstream (S) invert. Type them in; don't let Civil 3D apply design rules.",
            "At CB #10014 the pipe grows to 20\". At CB #10005 there's a 2.8' drop inside the structure."],
    check: `Trunk length ${f2(A.trunk_length)}'. Every slope is positive (flow goes north to the Sound).` },
  { n: "04", t: "Label and check against the field leaders", noImage: true,
    steps: ["Annotate > Add Labels > Pipe Network: pipe labels showing size, material, length and slope; structure labels showing rim and inverts.",
            "**XATTACH** ^^SURVEY_CAD_L5_COMPLETED.dwg^^ and compare your structure labels with the finished base's leaders."],
    check: "Every rim and invert matches, including the corrected CB #10016 outlet direction." },
  { part: "Part 2 - Alignment" },
  { n: "05", t: "Centerline alignment from objects", noImage: true,
    steps: ["Home > Alignment > ^^Create Alignment from Objects^^. Select the centerline pieces (layer V-ALGN-CNTR-E) from the south end.",
            "Name ^^SOUNDVIEW-HARBORVIEW CL^^. Start station ^^10+00^^. Uncheck {{Add curves between tangents}} (the curve is already drawn).",
            "Add station labels at 50' and 100'."],
    check: `Length ${f2(cl.length)}' (ends at station ${(10 + cl.length / 100).toFixed(4).replace(/(\d+)\.(\d{2})(\d+)/, "$1+$2.$3")}). ${cl.pieces} pieces, one curve, R = ${cl.radii.join(", ")}'.` },
  { part: "Part 3 - Deliver the base" },
  { n: "06", t: "Final QA", noImage: true,
    steps: ["Run the Level 2 QA checks again (layer 0, text style, duplicates, elevation).",
            "Point groups draw in the right order. The surface builds with no warnings. The pipe network shows no errors in the Event Viewer.",
            "**PURGE** and **AUDIT**. Xrefs: relative paths, no unresolved references."],
    check: "Clean Event Viewer. AUDIT: no errors left unfixed." },
  { n: "07", t: "Export for people without Civil 3D", noImage: true,
    why: "Most clients and consultants don't run Civil 3D. They get a plain AutoCAD copy, the same kind of file you started Level 1 from.",
    steps: ["**-EXPORTTOAUTOCAD** ↵ ↵. It writes a copy with ^^ACAD-^^ in front of the name. Points become blocks and text; surfaces and networks become plain linework.",
            "Open the ACAD- copy and check it the way you did in Level 1: layers, symbol sizes, labels.",
            "**ETRANSMIT** ↵ to zip the DWG, xrefs and the PDF for delivery."],
    check: "The ACAD- copy opens in plain AutoCAD with no proxy warnings and looks like the Civil 3D base." },
];

const body = [];
body.push(...G.cover({ level: 5, title: "Civil 3D Utilities and Finishing",
  intro: "The last level models the Soundview Dr storm trunk from the measured structure data, builds the road centerline alignment, and delivers the base: QA, export for people without Civil 3D, and eTransmit.",
  info: [["Start file", "SURVEY_CAD_L5_START.dxf (structures and centerline, no pipes or labels)"],
         ["Answer key", "SURVEY_CAD_L5_COMPLETED.dxf + the tables in this guide"],
         ["Software", "Civil 3D 2019 or later"], ["Before you start", "Finish Level 4 (the EG surface)"], ["Time", "About 3 hours"]] }));
body.push(new (require("docx").Paragraph)({ pageBreakBefore: true }));
body.push(G.H2("The storm trunk"));
body.push(G.P(`Eleven structures along Soundview Dr, from CB #10152 at the south end down to SDMH #10004, then out to the Sound (point ${A.outfall.p}, {{${A.outfall.note}}}, elevation ${A.outfall.z}).`));
body.push(...G.beforeAfter(IMG, "03", ["START: structures only", "COMPLETED: pipes and measured leaders"]));
body.push(...G.exerciseBlocks(EX, IMG));

body.push(G.H1("Structure table (from the field measuredowns)"));
body.push(G.table(["Structure", "Rim", "Inverts", "Bottom"],
  A.structures.map((s) => [s.name.replace(" (SQUARE BEEHIVE GRATE)", ""), s.rim, s.ie.join("; "), f2(s.bottom)]),
  [1500, 900, W - 3300, 900], { size: 17 }));
body.push(G.H1("Answer key"));
body.push(G.H2("Pipe table"));
body.push(G.table(["From", "To", "Pipe", "Length", "IE out", "IE in", "Slope"],
  A.pipes.map((p) => [p.from, p.to, `${p.size}" ${p.mat}`, f2(p.length), f2(p.ie_out), f2(p.ie_in), `${f2(p.slope_pct)}%`]),
  [1300, 1400, 1200, 1000, 1000, 1000, W - 6900], { size: 17 }));
body.push(G.table(["Item", "Result"], [
  ["Data problem", A.findings.join(" ")],
  ["Trunk length", `${f2(A.trunk_length)}' (10 pipes)`],
  ["Centerline", `${f2(cl.length)}', ${cl.pieces} pieces, curve R = ${cl.radii.join(", ")}'`],
], [2000, W - 2000]));
G.buildGuide({ outFile: path.join(OUT, "SURVEY_CAD_L5_Guide.docx"), level: 5, title: "Utilities and Finishing", body, exercises: EX });
