// Level 3 guide (Civil 3D field to finish). Run build_drawings.py and render_images.py first.
const fs = require("fs");
const path = require("path");
const G = require("../lib/guide");

const OUT = path.join(__dirname, "output");
const IMG = path.join(OUT, "images");
const A = JSON.parse(fs.readFileSync(path.join(OUT, "answers.json"), "utf8"));
const W = G.CONTENT;

const EX = [
  { part: "Part 1 - Get the data in", pageBreak: true },
  { n: "01", t: "Check the point file before you import it", noImage: true,
    why: "Import problems are cheapest to fix in the text file. Open it first, every time.",
    steps: ["Open ^^SURVEY_CAD_L3_POINTS.txt^^ in Notepad (not Excel; Excel can drop the decimals and damage the descriptions).",
            "Confirm the format: ^^P,N,E,Z,D^^ plus a note field, comma delimited.",
            `Find the bad row: line ${A.junk_lines.join(", ")} has no point number and an elevation of -99999. Delete the whole line and save.`,
            `Point ${A.odd_chars.join(", ")} has a stray character in its note (Windows "…"). Leave it: it only affects the note.`],
    check: `The file has ${A.points} point rows. Numbers run ${A.pt_min}-${A.pt_max}.` },
  { n: "02", t: "Start the drawing from the office template", noImage: true,
    steps: ["New drawing from the ^^Parametrix Civil 3D survey template^^ (ask your lead if you're unsure which). It carries the description keys, the figure prefix database and the PMX-Universal linework code set.",
            "Toolspace > Settings > drawing name > ^^Edit Drawing Settings^^ > Units and Zone: ^^NAD83 Washington State Planes, South Zone, US Foot^^ (WA83-SF). Scale 1\" = 20'.",
            "**SAVEAS** ^^SURVEY_CAD_L3_[INITIALS].dwg^^."],
    check: "The status bar shows 1\" = 20' and the coordinate system reads WA83-SF." },
  { n: "03", t: "Import through a survey database", noImage: true,
    why: "Importing through a survey database is what processes the linework codes into figures.",
    steps: ["Home tab > Create Ground Data > ^^Import Survey Data^^.",
            "Create a new survey database: ^^SURVEY_CAD_L3^^.",
            "Data source: ^^Point File^^. Select the cleaned file. Format: ^^PNEZD (comma delimited)^^.",
            "Check ^^Process linework during import^^, code set ^^PMX-Universal^^. Check ^^Insert figure objects^^ and ^^Insert survey points^^. Finish."],
    check: `Prospector > Point Groups > _All Points shows ${A.points} points. Elevations run ${A.z_min} to ${A.z_max}. The low one is the outfall into the sound; the high one is a control monument off site.` },
  { n: "04", t: "Build point groups", noImage: true,
    steps: ["Prospector > Point Groups > right-click > New. On the Include tab use ^^With raw descriptions matching^^:",
            "^^CONTROL^^: XMAG*,XHT*,XNL*.   ^^MONUMENTS^^: FMON*,FMIC*,FMAG*,FIP*.   ^^TREES^^: CON*,DEC*,MAP*.",
            "^^UTILITIES^^: CB*,CBS*,SSMH*,SDMH*,SDAD*,SSCO*,WVL*,GVL*,WFH*,MW*,PJB*,PP*,PPU*,PPX*,LT*,SN*,SNNP*,UCO*,IDWPP*.",
            "Right-click Point Groups > Properties > set the display order so CONTROL and MONUMENTS draw on top."],
    check: `CONTROL ${A.groups.CONTROL}, MONUMENTS ${A.groups.MONUMENTS}, UTILITIES ${A.groups.UTILITIES}, TREES ${A.groups.TREES}. A single description can match more than one pattern, so watch for overlaps.` },
  { part: "Part 2 - Fix the linework" },
  { n: "05", t: "Read the import results", noImage: true,
    steps: ["Survey tab (Toolspace) > your database > ^^Figures^^. Sort by name.",
            "Open the ^^Event Viewer^^ (or the import log) and read the warnings. Single-point strings can't make a figure, so they show up as warnings."],
    check: `About ${A.figures_raw} figures from the raw codes. Warnings for single-point strings: ${A.single_raw.join(", ")}.` },
  { n: "06", t: "Fix two bad field codes at the source", images: ["06a", "06b"],
    why: "The best fix is in the data, so the next import is right too.",
    steps: [`Point ^^10180^^ reads {{${A.fix["10180"][0]}}}. The extra ^^B^^ restarts RWB1 (so the RWB1 run from 10172 dies as a single point), and ^^RW2^^ is a typo for RWB2. Change it to {{${A.fix["10180"][1]}}}.`,
            `Point ^^15235^^ reads {{${A.fix["15235"][0]}}}. It ends the ^^RWC1^^ wall that began at 15062. Change it to {{${A.fix["15235"][1]}}}.`,
            "Make both edits in the point file (and in the survey database point editor), then re-process the linework (right-click the database > Process Linework)."],
    check: `The RWB1, RWB2 and RWC warnings are gone. Still single: ${A.single.join(", ")}. Leave those: each is a one-shot feature (a building tie, an edge start, the outfall).` },
  { n: "07", t: "Break the jumpers",
    why: "Office-added points in the 65000 series are numbered after everything else, so each one joins the last open string of its code and draws a long line across the site.",
    steps: ["Look for straight figure segments running between unrelated features.",
            ...A.jumpers.map((j) => `^^${j.figure}^^: ${j.from} to ${j.to}, ${j.length}'. Break the figure at the jump and delete the long segment.`),
            "In the survey figure editor use ^^Break^^, or edit the figure and delete the vertex. Don't erase the whole figure."],
    check: `Three jumpers removed. Keep the long ^^LN^^ and ^^FOG^^ stripe lines: those are real stripes shot only at each end.` },
  { n: "08", t: "Figures that aren't lines, and drafting what codes can't", noImage: true,
    steps: [`^^ASPH^^ is a spot shot on asphalt. The office figure prefix database gives it no layer, so its figure zig-zags across the site. Delete the ASPH figure (or turn its prefix off); the points stay for the surface.`,
            `Draft the three concrete landing pads (4 shots each, coded CONC): ${A.pads.map((p) => p.join("-")).join(", ")}. **PL** ↵ Node to node, **C** ↵ to close, layer V-SURF-CONC-E.`,
            `Draw the ${A.ties.length} curb-end ties from flowline to back of curb: ${A.ties.map((t) => t.join("-")).join(", ")}. Layer V-SURF-CURB-E.`],
    check: `Final count: ${A.figures} figures, 3 pads and ${A.ties.length} ties. Compare with SURVEY_CAD_L3_COMPLETED.dxf and with the Level 1 base.` },
  { n: "09", t: "Bring in the record boundary", noImage: true,
    steps: ["**XATTACH** ^^SURVEY_CAD_L3_RECORD.dwg^^ (save the DXF as a DWG first) at 0,0: overlay, relative path.",
            "Check that the found monuments (FIP points) plot near the boundary corners."],
    check: "Point 10494 (FIP) sits 0.64' from the NE corner, the same as Level 1." },
];

const body = [];
body.push(...G.cover({ level: 3, title: "Civil 3D Field to Finish",
  intro: "You'll take the crew's raw point file, clean it, import it through a survey database with the office linework codes, find and fix the field-code problems, and finish the linework the codes can't draw. Every problem in this level is real. They all came out of this survey.",
  info: [["Start files", "SURVEY_CAD_L3_POINTS.txt (as delivered), SURVEY_CAD_L3_RECORD.dxf"],
         ["Answer key", "SURVEY_CAD_L3_COMPLETED.dxf (points, figures, pads, ties)"],
         ["Software", "Civil 3D 2019 or later with the Parametrix survey template"],
         ["Before you start", "Finish Levels 1 and 2"], ["Time", "About 3 hours"]] }));
body.push(G.H2("Linework codes (office code set PMX-Universal)", { pageBreakBefore: true }));
body.push(G.P("Descriptions are space delimited: a code, then control codes that act on it. A trailing number makes a separate string (EC1 and EC2 are two figures). Auto-begin is on."));
body.push(G.table(["Control", "Meaning", "Example from this survey"], [
  ["B", "Begin a new string", "{{TBC EP B}} - continue TBC, start a new EP"],
  ["E", "End the string after this point", "{{RWC E BLD}}"],
  ["C", "Close back to the first point", ""],
  ["P / T", "Begin / end a curve", "{{EP5 P}} ... {{EP5 T}}"],
  ["(none)", "Continue the open string", "{{EC}}"],
], [1200, 3200, W - 4400]));
body.push(G.P("Codes that never draw: ^^GS^^ (ground shot), ^^INFO^^, ^^INFOL^^, ^^NOTE^^, ^^ZK^^, ^^CONS^^. Numbers after a tree code are trunk sizes and the drip radius (e.g. {{CON 18 . 25}}), not linework."));
body.push(...G.figure(path.join(IMG, "figures.png"), 6.6, "The finished figures (answer key)"));
body.push(...G.exerciseBlocks(EX, IMG));

body.push(G.H1("Answer key"));
body.push(G.table(["Item", "Result"], [
  ["Bad row", `Line ${A.junk_lines.join(", ")} (no number, Z = -99999)`],
  ["Points", `${A.points} (${A.pt_min}-${A.pt_max}); Z ${A.z_min} to ${A.z_max}`],
  ["Point groups", Object.entries(A.groups).filter(([k]) => k !== "SURFACE").map(([k, v]) => `${k} ${v}`).join(", ")],
  ["Figures", `${A.figures_raw} raw; ${A.figures_fixed} after the code fixes; ${A.figures} after dropping ASPH and splitting jumpers`],
  ["Code fixes", Object.entries(A.fix).map(([k, v]) => `${k}: ${v[0]} → ${v[1]}`).join("; ")],
  ["Jumpers", A.jumpers.map((j) => `${j.figure} ${j.from}-${j.to} (${j.length}')`).join("; ")],
  ["Still single (OK)", A.single.join(", ")],
  ["Hand drafted", `3 CONC pads, ${A.ties.length} curb ties`],
], [2000, W - 2000]));
body.push(G.H2("Figures by prefix (answer key)"));
body.push(G.table(["Prefix", "Figures", "Prefix", "Figures"], (() => {
  const e = Object.entries(A.by_prefix).filter(([k]) => k !== "ASPH"); const rows = [];
  for (let i = 0; i < e.length; i += 2) rows.push([e[i][0], e[i][1], e[i + 1] ? e[i + 1][0] : "", e[i + 1] ? e[i + 1][1] : ""]);
  return rows;
})(), [W / 4, W / 4, W / 4, W / 4]));

G.buildGuide({ outFile: path.join(OUT, "SURVEY_CAD_L3_Guide.docx"), level: 3, title: "Field to Finish", body, exercises: EX });
