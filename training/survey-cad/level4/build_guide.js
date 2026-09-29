// Level 4 guide (Civil 3D surfaces). Run build_drawings.py first.
const fs = require("fs");
const path = require("path");
const G = require("../lib/guide");

const OUT = path.join(__dirname, "output");
const IMG = path.join(OUT, "images");
const A = JSON.parse(fs.readFileSync(path.join(OUT, "answers.json"), "utf8"));
const W = G.CONTENT;
const excl = A.no_surface.map((c) => c + "*").join(",");

const EX = [
  { part: "Part 1 - Build the surface", pageBreak: true },
  { n: "01", t: "Open your Level 3 drawing", noImage: true,
    steps: ["Open your finished Level 3 drawing (points, figures, record xref). **SAVEAS** ^^SURVEY_CAD_L4_[INITIALS].dwg^^.",
            "**INSERT** ^^SURVEY_CAD_L4_BORDER.dxf^^ (save it as a DWG first) at 0,0 and explode it once, or copy the border polyline in. Layer V-TOPO-SURFACE-BORDER-E."],
    check: "The border polyline wraps the site and both street frontages." },
  { n: "02", t: "See what goes wrong with _All Points", noImage: true,
    why: "The fastest way to learn why point groups matter is to build a bad surface once.",
    steps: ["Prospector > Surfaces > Create Surface: TIN, name ^^EG-TEST^^, style ^^Border & Triangles & Points^^.",
            "Definition > Point Groups > Add > ^^_All Points^^. **Z** ↵ **E** ↵."],
    check: `The triangles reach miles off site to control points ${A.far.join(" and ")}. On the lot, point ${A.stump.p} ({{${A.stump.note}}}) makes a spike to ${A.stump.z}. Delete EG-TEST.` },
  { n: "03", t: "Build the GROUND point group", noImage: true,
    steps: ["New point group ^^GROUND^^. Include tab: ^^Include all points^^.",
            `Exclude tab: ^^With raw descriptions matching^^: ${excl}`,
            "Those are notes, trees, finished floors, above- or below-grade items and monuments. None of them are ground."],
    check: `GROUND = ${A.ground} points.` },
  { n: "04", t: "Create EG from GROUND, breaklines and a boundary", noImage: true,
    steps: ["Create Surface: TIN, name ^^EG^^, style ^^Contours 1' and 5' (Background)^^ or your office style.",
            "Definition > Point Groups > Add ^^GROUND^^.",
            "Definition > Breaklines > Add: ^^Standard^^. Select the survey figures for curb, edge of pavement, walls (top and bottom), top and toe of slope, concrete edges and stairs. Leave out stripes (LN, FOG, LNDY) and fences (FCK).",
            "Definition > Boundaries > Add: ^^Outer^^, ^^Non-destructive^^ unchecked, pick the border polyline."],
    check: `The surface stops at the border. ${A.outside} of the GROUND points sit outside the border and drop out; ${A.inside} are used.` },
  { n: "05", t: "Check the surface statistics", noImage: true,
    steps: ["Surface Properties > Statistics > General."],
    check: `Minimum elevation ≈ ${A.z_min}, maximum ≈ ${A.z_max}. Anything far outside that means a bad point got in.` },
  { n: "06", t: "Read the Event Viewer", noImage: true,
    steps: ["Open the Event Viewer (Prospector > surface > right-click > Rebuild; warnings appear in the Panorama).",
            "Crossing breaklines are the usual warning. A wall top and bottom shot on the same line can cross. Fix it by nudging the wall-top figure 0.1' toward the high side, or drop the offending vertex."],
    check: "No crossing-breakline warnings after the fix." },
  { part: "Part 2 - Contours and labels" },
  { n: "07", t: "Contours at 1' and 5'", noImage: true,
    steps: ["Surface style: minor ^^1'^^, major ^^5'^^. Smoothing: ^^off^^ (smoothing moves contours off the data).",
            "Contour labels: Annotate > Add Labels > Surface > ^^Contour - Multiple at Interval^^, majors only, about every 200'."],
    check: `Contours run ${A.contour_min} to ${A.contour_max}.` },
  { n: "08", t: "Compare with the finished base", noImage: true,
    steps: ["**XATTACH** ^^SURVEY_CAD_L4_COMPLETED.dwg^^ (save the DXF as a DWG first) on a layer you can color red.",
            "Zoom through the site. Your contours should sit on the red ones. Where they don't, look for a missing breakline, a missing point or a boundary difference."],
    check: "Contours match within a few tenths everywhere except right at the boundary edge." },
  { n: "09", t: "Finished-floor labels", noImage: true,
    steps: ["The BLFF points are finished floors (they're kept out of the surface).",
            ...Object.entries(A.ffe).map(([p, v]) => `Point ${p} (${v[1]}): label it {{FFE=${v[0].toFixed(2)}}} next to the door, layer V-TOPO-TEXT-E, 0.08\" text.`)],
    check: "Three FFE labels matching the answer key." },
];

const body = [];
body.push(...G.cover({ level: 4, title: "Civil 3D Surfaces",
  intro: "You'll build the existing-ground surface from the Level 3 data: the right point group, breaklines from the figures, a boundary, and 1' contours. Then you'll check it against the contours in the finished base.",
  info: [["Start files", "Your Level 3 drawing + SURVEY_CAD_L4_BORDER.dxf"],
         ["Answer key", "SURVEY_CAD_L4_COMPLETED.dxf: the finished base's contours, labels, breaklines, border and FFE labels"],
         ["Software", "Civil 3D 2019 or later"], ["Before you start", "Finish Level 3"], ["Time", "About 2 hours"]] }));
body.push(new (require("docx").Paragraph)({ pageBreakBefore: true }));
body.push(...G.figure(path.join(IMG, "contours.png"), 6.9, "Answer key: 1' contours inside the surface border, breaklines, FFE labels"));
body.push(...G.exerciseBlocks(EX, IMG));
body.push(G.H1("Answer key"));
body.push(G.table(["Item", "Result"], [
  ["GROUND group", `${A.ground} points`],
  ["Inside border", `${A.inside} used, ${A.outside} outside`],
  ["Elevations", `${A.z_min} to ${A.z_max}`],
  ["Contours", `${A.contour_min} to ${A.contour_max}; ${A.contours} polylines (${A.majors} major)`],
  ["Breaklines in the base", `${A.breaklines}`],
  ["Bad points", `${A.far.join(", ")} (off-site control); ${A.stump.p} (stump shot on top, coded INFO)`],
  ["FFE", Object.values(A.ffe).map((v) => v[0].toFixed(2)).join(", ")],
], [2200, W - 2200]));
G.buildGuide({ outFile: path.join(OUT, "SURVEY_CAD_L4_Guide.docx"), level: 4, title: "Surfaces", body, exercises: EX });
