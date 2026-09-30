// Field to Finish course guide: one drawing, six stages, a checkpoint PDF after each.
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
const L1 = A.l1;
const excl = A.no_surface.map((c) => c + "*").join(",");
const ld = (n) => (A.leaders[n] || "").split("\\P").join(" / ");

function checkpoint(n, lines) {
  return [
    G.note(`CHECKPOINT ${n}`, `Open ^^CHECKPOINT_${n}.pdf^^ and compare it with your drawing. Tick off each item on the right side of the PDF before you go on. Save a copy of your drawing now (^^..._STAGE${n}.dwg^^) so you can come back to it.`, "E8F3EA"),
    new Paragraph({ spacing: { after: 100 } }),
    ...G.figure(path.join(IMG, `checkpoint_${n}.png`), 6.4, `CHECKPOINT_${n}.pdf`),
    G.table(["Check", "Expected"], lines, [3600, W - 3600], { size: 18 }),
  ];
}

const S1 = [
  { part: "Stage 1 - Set up the drawing and the record boundary", pageBreak: true },
  { n: "1.1", t: "Open the bare drawing", noImage: true,
    steps: ["Civil 3D: **OPEN** ↵ ^^SURVEY_CAD_START.dxf^^. Model space is empty. The ^^TOPO SHEET^^ layout already has the title block.",
            "**SAVEAS** ↵ ^^SURVEY_CAD_[YOUR INITIALS].dwg^^. This is the one drawing you'll finish."],
    check: "The title bar shows your .dwg." },
  { n: "1.2", t: "Units, coordinate system, scale", noImage: true,
    steps: ["**UNITS** ↵. Length ^^Decimal 0.00^^. Angle ^^Surveyor's Units, N0d00'00\"E^^. Insertion scale ^^US Survey Feet^^.",
            "Toolspace > Settings > right-click the drawing > ^^Edit Drawing Settings^^ > Units and Zone: ^^NAD83 Washington State Planes, South Zone, US Foot^^ (WA83-SF).",
            "Status bar: annotation scale ^^1\" = 20'^^."],
    check: "Coordinates and bearings read the way the survey is written." },
  { n: "1.3", t: "Bring in the office standards", noImage: true,
    why: "The START drawing is bare on purpose. Office standards come from the Parametrix survey template, not from a copy of an old job.",
    steps: ["Manage tab > Styles panel > ^^Import^^. Pick the Parametrix Civil 3D survey template. Import everything (point styles, label styles, description key sets, layers).",
            "Ask your lead which template, description key set and figure prefix database the office uses."],
    check: "Prospector > Description Key Sets and Settings > Point Styles are populated." },
  { n: "1.4", t: "Draw the parcel from the deed calls", noImage: true,
    why: "The record boundary comes from the deed, not from the field shots. You'll compare the found monuments with it in Stage 2.",
    steps: ["Layer ^^V-PROP-BNDY-E^^ current (create it if the template didn't: color white, lineweight 0.50).",
            `**L** ↵. Start at the point of beginning (the SW corner) by typing **#${f2(A.pob[0])},${f2(A.pob[1])}** ↵. That's Easting,Northing.`,
            ...A.deed.map((c) => `**@${f2(c.dist)}<${typed(c.bearing)}** ↵   (${c.from} to ${c.to})`),
            "↵ to end. The last course should land back on the start point."],
    check: "Closure: **DI** ↵ from the last endpoint to the start point reads 0.00." },
  { n: "1.5", t: "Join and check the area", noImage: true,
    steps: ["**J** ↵ pick the four lines ↵. **LI** ↵ the result (or **AREA** ↵ **O** ↵)."],
    check: `Closed polyline, ${Number(A.area_sf).toLocaleString("en-US", { minimumFractionDigits: 2 })} sq ft (${(A.area_sf / 43560).toFixed(4)} ac), perimeter ${f2(A.perimeter)}'.` },
  { n: "1.6", t: "Attach the record data", noImage: true,
    steps: ["Open ^^SURVEY_CAD_RECORD.dxf^^ and **SAVEAS** it as a DWG in your folder.",
            "In your drawing: **XATTACH** ↵ that DWG. Overlay, relative path, insert at **0,0**, scale 1, rotation 0.",
            "It holds the right-of-way lines, the road centerline and the surface border you'll use in Stage 4."],
    check: "The ROW lines meet your parcel's west and east lines." },
];

const S2 = [
  { part: "Stage 2 - Field to finish" },
  { n: "2.1", t: "Check the point file before you import", noImage: true,
    steps: ["Open ^^SURVEY_CAD_POINTS.txt^^ in Notepad (not Excel). Format: P,N,E,Z,D plus a note.",
            `Line ${A.junk_lines[0]} has no point number and an elevation of -99999. Delete the line and save.`],
    check: `${A.points} point rows are left.` },
  { n: "2.2", t: "Import through a survey database", noImage: true,
    steps: ["Home > Create Ground Data > ^^Import Survey Data^^. New survey database ^^SURVEY_CAD^^.",
            "Point File, format ^^PNEZD (comma delimited)^^. Check ^^Process linework during import^^ (code set PMX-Universal), ^^Insert figure objects^^ and ^^Insert survey points^^."],
    check: `_All Points = ${A.points}. Figures appear, and some of them are wrong. You'll fix those next.` },
  { n: "2.3", t: "Point groups", noImage: true,
    steps: ["New groups with ^^raw descriptions matching^^:",
            "CONTROL: XMAG*,XHT*,XNL*.  MONUMENTS: FMON*,FMIC*,FMAG*,FIP*.  TREES: CON*,DEC*,MAP*.",
            "UTILITIES: CB*,CBS*,SSMH*,SDMH*,SDAD*,SSCO*,WVL*,GVL*,WFH*,MW*,PJB*,PP*,PPU*,PPX*,LT*,SN*,SNNP*,UCO*,IDWPP*."],
    check: Object.entries(A.groups).map(([k, v]) => `${k} ${v}`).join(", ") + "." },
  { n: "2.4", t: "Fix two bad field codes", noImage: true,
    steps: [`Point ^^10180^^: {{${A.fixes["10180"][0]}}}. The extra B restarts RWB1, and RW2 is a typo for RWB2. Make it {{${A.fixes["10180"][1]}}}.`,
            `Point ^^15235^^: {{${A.fixes["15235"][0]}}} ends the RWC1 wall, not RWC. Make it {{${A.fixes["15235"][1]}}}.`,
            "Edit both in the point file and in the survey database, then right-click the database > ^^Process Linework^^."],
    check: `${A.figures_fixed} figures. Single-point strings left: ${A.single.join(", ")} (those are fine).` },
  { n: "2.5", t: "Compare the found monuments with the record corners", noImage: true,
    steps: ["**DI** ↵ from each boundary corner to the nearby found iron pipe (FIP) point."],
    check: Object.entries(A.monuments).map(([k, v]) => `FIP ${k} is ${f2(v.off)}' from the ${v.corner} corner`).join("; ") + ". Record your values; the boundary stays on record." },
];

const S3 = [
  { part: "Stage 3 - Clean up the linework" },
  { n: "3.1", t: "Break the jumpers", noImage: true,
    why: "Office-added points in the 65000 series are numbered last, so each one joins the last open string of its code and draws a long line across the site.",
    steps: [...A.jumpers.map((j) => `^^${j.figure}^^: ${j.from} to ${j.to} (${j.length}'). Break the figure at the jump and delete the long segment.`),
            "Keep the long LN and FOG stripes. Those are real lines, shot only at each end."],
    check: "No figure segment crosses open ground between unrelated features." },
  { n: "3.2", t: "Remove the ASPH figure", noImage: true,
    steps: ["ASPH shots are spot elevations on asphalt, and the office prefix database gives ASPH no layer. Erase the ASPH figure. Keep the points; they go in the surface."],
    check: `${A.figures} figures.` },
  { n: "3.3", t: "Draft what the codes can't", noImage: true,
    steps: [`Three concrete landing pads, 4 shots each (CONC): ${A.pads.map((p) => p.join("-")).join(", ")}. **PL** ↵ Node to node, **C** ↵. Layer V-SURF-CONC-E.`,
            `${A.ties.length} curb-end ties, flowline to back of curb: ${A.ties.map((t) => t.join("-")).join(", ")}. **L** ↵ Node to node. Layer V-SURF-CURB-E.`],
    check: "3 pads and 8 ties." },
];

const S4 = [
  { part: "Stage 4 - Surface" },
  { n: "4.1", t: "GROUND point group", noImage: true,
    steps: ["New group ^^GROUND^^: include all points; exclude raw descriptions matching:", `{{${excl}}}`],
    check: `GROUND = ${A.ground}.` },
  { n: "4.2", t: "Build EG", noImage: true,
    steps: ["Create Surface: TIN ^^EG^^. Definition > Point Groups: ^^GROUND^^.",
            "Breaklines (Standard): curb, edge of pavement, wall top and bottom, top and toe, concrete edges, stairs. Leave out stripes and fences.",
            "Boundaries: ^^Outer^^, pick the surface border in the record xref (bind or copy it in first)."],
    check: `${A.inside} points inside the border. Elevations ${A.z_min} to ${A.z_max}.` },
  { n: "4.3", t: "Contours", noImage: true,
    steps: ["Style: 1' minor, 5' major, smoothing off. Label the majors about every 200'."],
    check: "Contours run 45 to 91." },
];

const S5 = [
  { part: "Stage 5 - Annotation and utilities" },
  { n: "5.1", t: "Surface and street labels", noImage: true,
    steps: ["MTEXT, style Survey, 0.08\" (1.6'): ^^SOUNDVIEW DR^^ and ^^HARBORVIEW DR^^ (4.0' high, rotated with the street). Use **TORIENT** ↵ on anything upside down.",
            "Surface callouts: ASPH, CONC, CW, GRASS, CONC PATIO, GARAGE, HOUSE. Put each on its V-SURF-...-TEXT-E layer."],
    check: "Every label reads left to right or bottom to top." },
  { n: "5.2", t: "Structure and monument leaders", noImage: true,
    steps: ["Multileader style SRV-20. One leader per storm structure, with the rim from the point and the inverts and bottom from the measuredowns. For example:",
            `{{${ld("10144")}}}`, `{{${ld("10012")}}}`,
            "Monuments: FOUND IRON PIPE with the height above grade and the date found. Control: PMX # and the type."],
    check: "Compare with CHECKPOINT_5." },
  { n: "5.3", t: "FFEs, setbacks, boundary labels", noImage: true,
    steps: [`FFE labels from the BLFF points: ${Object.values(A.ffe).map((v) => "FFE=" + f2(v)).join(", ")}.`,
            "**DAL** ↵ garage SE corner, Perpendicular to the south line. House NE corner, Perpendicular to the east line.",
            `Label the boundary: **DT** ↵ J BC at each midpoint, for example ${L1.north_bearing} ${f2(L1.north_dist)}' on the north line.`],
    check: `Setbacks ${f2(L1.dim_garage)}' and ${f2(L1.dim_house)}'.` },
];

const S6 = [
  { part: "Stage 6 - QA, sheet and delivery" },
  { n: "6.1", t: "QA", noImage: true,
    steps: ["**QSELECT** Layer = 0: expect nothing. **QSELECT** MText Style ≠ Survey: expect nothing.",
            "Controlled **OVERKILL** on one layer at a time if anything was drawn twice. Never on the whole drawing.",
            "**PURGE** ↵ and **AUDIT** ↵ **Y** ↵. The Event Viewer is clean."],
    check: "Zero objects on layer 0. AUDIT has no errors left unfixed." },
  { n: "6.2", t: "Sheet and plot", noImage: true,
    steps: ["TOPO SHEET layout. Layer G-ANNO-VPRT. **MV** ↵ pick the dashed box. **Z** ↵ **1/20XP** ↵, center, lock.",
            "Freeze TRAIN-NOTES. **PLOT**: DWG To PDF, ANSI expand D, 1:1, monochrome.ctb."],
    check: `The north line measures ${(L1.north_dist / 20).toFixed(2)}" on paper.` },
  { n: "6.3", t: "Deliver", noImage: true,
    steps: ["**-EXPORTTOAUTOCAD** ↵ ↵ for people without Civil 3D. Open the ACAD- copy and check it.",
            "**ETRANSMIT** ↵: DWG, xrefs, PDF. Compare your final drawing with SURVEY_CAD_COMPLETED.dxf."],
    check: "Your drawing matches CHECKPOINT_6 and the COMPLETED file." },
];

const body = [];
body.push(...G.cover({ level: "1-6", title: "Field to Finish, One Drawing",
  intro: "You'll take one bare drawing and a crew's raw point file all the way to a finished topographic survey base, the same way a real job runs. Six stages. After each one there's a checkpoint PDF showing what your drawing should look like and the numbers to check.",
  info: [["You start with", "SURVEY_CAD_START.dxf (bare), SURVEY_CAD_POINTS.txt (as delivered), SURVEY_CAD_RECORD.dxf (ROW, centerline, surface border)"],
         ["Checkpoints", "CHECKPOINT_1.pdf to CHECKPOINT_6.pdf"],
         ["Answer key", "SURVEY_CAD_COMPLETED.dxf"],
         ["Software", "Civil 3D 2019 or later with the Parametrix survey template"],
         ["Time", "About 2 days, one or two stages per session"]] }));
body.push(G.H2("How to use this", { pageBreakBefore: true }));
body.push(G.table(["Stage", "You do", "Skills"], [
  ["1", "Set up; draw the parcel from deed calls", "UNITS, coordinate system, typed coordinates and bearings, JOIN, AREA, XATTACH"],
  ["2", "Field to finish", "Point file QA, survey database import, point groups, fixing field codes"],
  ["3", "Linework cleanup", "Breaking figures, node snaps, PLINE/LINE drafting, layers"],
  ["4", "Surface", "Point groups, breaklines, boundaries, contours"],
  ["5", "Annotation", "MTEXT, TORIENT, MLEADER, DIMALIGNED, TEXT"],
  ["6", "QA, sheet, delivery", "QSELECT, PURGE/AUDIT, MVIEW, PLOT, EXPORTTOAUTOCAD, ETRANSMIT"],
], [900, 3600, W - 4500], { size: 18 }));
body.push(G.P("Work in order. Don't start a stage until your drawing matches the previous checkpoint. If you get stuck, compare against the checkpoint PDF (zoom in: it's vector) or the COMPLETED file."));
body.push(G.H2("Typing coordinates and bearings"));
body.push(G.table(["You want", "Type"], [
  ["An absolute coordinate (Easting,Northing)", "**#1124980.69,733878.87**"],
  ["A distance and bearing from the last point", "**@125.02<N02d30'20\"E**"],
  ["The ° symbol in text", "**%%d**"],
], [4300, W - 4300]));

const stages = [[S1, 1], [S2, 2], [S3, 3], [S4, 4], [S5, 5], [S6, 6]];
const cps = {
  1: A.deed.map((c) => [`${c.from}-${c.to}`, `${c.bearing}  ${f2(c.dist)}'`]).concat([["Area", `${f2(A.area_sf)} sq ft`], ["Perimeter", `${f2(A.perimeter)}'`]]),
  2: [["Points", A.points], ["Figures after fixes", A.figures_fixed], ...Object.entries(A.monuments).map(([k, v]) => [`FIP ${k}`, `${f2(v.off)}' from ${v.corner}`])],
  3: [["Figures", A.figures], ["Jumpers removed", A.jumpers.length], ["Pads / ties", `3 / ${A.ties.length}`]],
  4: [["GROUND", A.ground], ["Inside border", A.inside], ["Elevations", `${A.z_min} to ${A.z_max}`], ["Contours", "45 to 91"]],
  5: [["FFE", Object.values(A.ffe).map(f2).join(", ")], ["Setbacks", `${f2(L1.dim_garage)}', ${f2(L1.dim_house)}'`], ["North label", `${L1.north_bearing} ${f2(L1.north_dist)}'`]],
  6: [["Viewport", "1\" = 20', locked"], ["North line on paper", `${(L1.north_dist / 20).toFixed(2)}"`], ["Layer 0", "empty"]],
};
const allEx = [];
for (const [ex, n] of stages) {
  body.push(...G.exerciseBlocks(ex, IMG));
  body.push(...checkpoint(n, cps[n]));
  allEx.push(...ex);
}
G.buildGuide({ outFile: path.join(OUT, "SURVEY_CAD_Guide.docx"), level: "1-6", title: "Field to Finish", body, exercises: allEx });
