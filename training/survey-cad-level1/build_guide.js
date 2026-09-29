// Build the Survey CAD Level 1 trainee guide (.docx) from answers.json + rendered images.
// Run build_drawings.py and render_images.py first.
const fs = require("fs");
const path = require("path");
const {
  Document, Packer, Paragraph, TextRun, ImageRun, Table, TableRow, TableCell, WidthType,
  AlignmentType, HeadingLevel, LevelFormat, BorderStyle, ShadingType, PageBreak, Footer,
  PageNumber, TableLayoutType,
} = require("docx");

const OUT = path.join(__dirname, "output");
const IMG = path.join(OUT, "images");
const A = JSON.parse(fs.readFileSync(path.join(OUT, "answers.json"), "utf8"));

const FONT = "Calibri";
const MONO = "Consolas";
const ACCENT = "1F4E79";
const PAGE_W = 12240, MARGIN = 1080, CONTENT = PAGE_W - 2 * MARGIN; // 7.5in content

// ---------------------------------------------------------------- inline markup
// **CMD**  -> bold monospace (what you type)      ^^x^^ -> bold      {{x}} -> italic
function runs(text, base = {}) {
  const out = [];
  const re = /(\*\*[^*]+\*\*|\^\^[^^]+\^\^|\{\{[^}]+\}\})/g;
  let last = 0, m;
  while ((m = re.exec(text))) {
    if (m.index > last) out.push(new TextRun({ text: text.slice(last, m.index), font: FONT, ...base }));
    const t = m[0];
    if (t.startsWith("**")) out.push(new TextRun({ text: t.slice(2, -2), font: MONO, bold: true, color: "000000", ...base, size: 20 }));
    else if (t.startsWith("^^")) out.push(new TextRun({ text: t.slice(2, -2), font: FONT, bold: true, ...base }));
    else out.push(new TextRun({ text: t.slice(2, -2), font: FONT, italics: true, ...base }));
    last = m.index + t.length;
  }
  if (last < text.length) out.push(new TextRun({ text: text.slice(last), font: FONT, ...base }));
  return out;
}
const P = (text, opts = {}) => new Paragraph({ children: runs(text, opts.run || {}), spacing: { after: 100 }, ...opts.para });
const H1 = (t, brk = true) => new Paragraph({ heading: HeadingLevel.HEADING_1, children: [new TextRun({ text: t })],
  pageBreakBefore: brk, keepNext: true, spacing: brk ? undefined : { before: 480, after: 200 } });
const H2 = (t) => new Paragraph({ heading: HeadingLevel.HEADING_2, children: [new TextRun({ text: t })], keepNext: true });
const bullet = (t) => new Paragraph({ numbering: { reference: "bullets", level: 0 }, children: runs(t), spacing: { after: 60 } });

function note(label, text, fill = "EAF1F8") {
  return new Table({
    width: { size: CONTENT, type: WidthType.DXA }, columnWidths: [CONTENT],
    rows: [new TableRow({ children: [new TableCell({
      width: { size: CONTENT, type: WidthType.DXA },
      shading: { type: ShadingType.CLEAR, fill, color: "auto" },
      margins: { top: 80, bottom: 80, left: 140, right: 140 },
      borders: { top: none, bottom: none, right: none, left: { style: BorderStyle.SINGLE, size: 18, color: ACCENT } },
      children: [new Paragraph({ children: [new TextRun({ text: label + "  ", bold: true, font: FONT, color: ACCENT }), ...runs(text)] })],
    })] })],
  });
}
const none = { style: BorderStyle.NONE, size: 0, color: "FFFFFF" };
const thin = { style: BorderStyle.SINGLE, size: 4, color: "BFBFBF" };
const cellBorders = { top: thin, bottom: thin, left: thin, right: thin };

function table(headers, rows, widths, opts = {}) {
  const total = widths.reduce((a, b) => a + b, 0);
  const mk = (cells, head) => new TableRow({
    tableHeader: head,
    children: cells.map((c, i) => new TableCell({
      width: { size: widths[i], type: WidthType.DXA }, borders: cellBorders,
      shading: head ? { type: ShadingType.CLEAR, fill: "D9E2F3", color: "auto" } : undefined,
      margins: { top: 40, bottom: 40, left: 90, right: 90 },
      children: [new Paragraph({ children: runs(String(c), { bold: head, size: opts.size || 20 }) })],
    })),
  });
  return new Table({ width: { size: total, type: WidthType.DXA }, columnWidths: widths,
    rows: [mk(headers, true), ...rows.map((r) => mk(r, false))] });
}

function imgSize(file) {
  const b = fs.readFileSync(file);
  return { w: b.readUInt32BE(16), h: b.readUInt32BE(20), data: b };
}
function image(file, widthIn) {
  const { w, h, data } = imgSize(file);
  const W = Math.round(widthIn * 96);
  return new ImageRun({ type: "png", data, transformation: { width: W, height: Math.round((W * h) / w) } });
}
function figure(file, widthIn, caption) {
  const out = [new Paragraph({ alignment: AlignmentType.CENTER, children: [image(file, widthIn)], keepNext: !!caption })];
  if (caption) out.push(new Paragraph({ alignment: AlignmentType.CENTER, spacing: { after: 160 },
    children: [new TextRun({ text: caption, italics: true, size: 18, font: FONT, color: "595959" })] }));
  return out;
}
function beforeAfter(ex) {
  const b = path.join(IMG, `ex${ex}_before.png`), a = path.join(IMG, `ex${ex}_after.png`);
  if (!fs.existsSync(b)) return [];
  const { w, h } = imgSize(b);
  const colW = CONTENT / 2;
  const maxIn = (colW - 200) / 1440;
  const widthIn = Math.min(maxIn, (2.1 * w) / h); // keep tall crops from getting huge
  const cell = (file, label) => new TableCell({
    width: { size: colW, type: WidthType.DXA }, borders: { top: none, bottom: none, left: none, right: none },
    children: [
      new Paragraph({ alignment: AlignmentType.CENTER, children: [image(file, widthIn)] }),
      new Paragraph({ alignment: AlignmentType.CENTER, spacing: { after: 120 },
        children: [new TextRun({ text: label, size: 17, italics: true, color: "595959", font: FONT })] }),
    ],
  });
  return [new Table({ width: { size: CONTENT, type: WidthType.DXA }, columnWidths: [colW, colW],
    rows: [new TableRow({ cantSplit: true, children: [cell(b, "Before (START)"), cell(a, "After (COMPLETED)")] })] })];
}

// ---------------------------------------------------------------- exercises
const inv = A.inverse_1_10;
const pt = Object.fromEntries(A.points.map((p) => [p.pt, p]));
const fmt = (v) => v.toFixed(2);

const EX = [
  { part: "Part 1 - Set up the drawing" },
  { n: "01", t: "Open the drawing and save it as a DWG",
    why: "The START file is a DXF so it opens in any AutoCAD version. You work in a DWG.",
    steps: ["**OPEN** ↵ and pick ^^Survey_CAD_Level1_START.dxf^^.",
            "**SAVEAS** ↵. Set {{Files of type}} to ^^AutoCAD 2018 Drawing (*.dwg)^^ and name it ^^SURVEY_CAD_L1_[YOUR INITIALS].dwg^^.",
            "**Z** ↵ **E** ↵ to zoom to the whole site.",
            "The red numbered bubbles are on layer ^^TRAIN-NOTES^^. Each number is an exercise in this guide. The layer is set to not plot."],
    check: "The title bar shows your .dwg file name." },
  { n: "02", t: "Set survey units (UNITS)",
    why: "Out of the box AutoCAD measures angles counterclockwise from east in decimal degrees. Surveyors work in bearings. Until you change the units, AutoCAD will not accept a bearing like N89d12'40\"E.",
    steps: ["**LIST** ↵ and pick the front lot line at bubble 02 ↵. Note {{Angle in XY Plane}} = ^^0.7889^^ and a length with four decimals.",
            "**UNITS** ↵. Length: Type ^^Decimal^^, Precision ^^0.00^^.",
            "Angle: Type ^^Surveyor's Units^^, Precision ^^N0d00'00\"E^^. Leave {{Clockwise}} unchecked and do not change {{Direction}}.",
            "Insertion scale: ^^Feet^^. Click OK.",
            "**LIST** ↵ the same line again."],
    check: `LIST now reports ${A.front_bearing.replace("°", "d")} and 100.00, the same as the label on the drawing.`,
    tip: "Bearings are always measured from north, so leave the base angle (Direction) at East. Changing it breaks coordinate and bearing input." },
  { n: "03", t: "Object snaps and ID a monument",
    why: "Everything in survey CAD is drawn from something: a point, a monument, a line end. Running snaps make that automatic.",
    steps: ["**OS** ↵. Check ^^Endpoint, Midpoint, Center, Node, Intersection, Insertion, Perpendicular^^. Leave {{Nearest}} off. Make sure {{Object Snap On (F3)}} is checked. OK.",
            "**ID** ↵. Hover the found iron rod at bubble 03 until the ^^Insertion^^ snap marker shows, then pick.",
            "For a one-time snap, hold ^^Shift + right-click^^ at any prompt and choose from the menu."],
    check: `ID reports X = ${fmt(A.pt1[0])}  Y = ${fmt(A.pt1[1])}. In AutoCAD ^^X is the Easting and Y is the Northing^^. Compare note 2 on the sheet.` },
  { n: "04", t: "Thaw the survey points layer",
    why: "The survey points (number, elevation, description) are on V-NODE, which is frozen. You need them for the rest of the exercises.",
    steps: ["**LA** ↵ to open the Layer Properties Manager.",
            "Find ^^V-NODE^^. The snowflake icon means it is frozen. Click it to thaw. Close the palette.",
            "**LAYISO** ↵, pick a lot line ↵. Only the boundary layer is shown. **LAYUNISO** ↵ to bring everything back.",
            "In the Layer Properties Manager, note that ^^TRAIN-NOTES^^ and ^^G-ANNO-VPRT^^ have the no-plot printer icon."],
    check: "26 survey points show, each with point number, elevation and description.",
    tip: "OFF hides a layer. FREEZE also skips it during regens and ZOOM EXTENTS. Use freeze for layers you won't need for a while." },
  { part: "Part 2 - Check the survey" },
  { n: "05", t: "Inverse between two points (DIST)",
    why: "An inverse (bearing and distance between two known points) is the most common check in survey CAD.",
    steps: ["**DI** ↵ (DIST). Snap ^^Node^^ on point 1 (the SW lot corner rod).",
            "Snap ^^Node^^ on point 10, the centerline monument at bubble 05.",
            "Read {{Distance}} and {{Angle in XY Plane}} on the command line. Record them: ^^__________^^"],
    check: "Compare with the Answer Key. The direction is from the first pick to the second pick. Reverse the picks and you get the opposite bearing." },
  { n: "06", t: "Draw the rear lot line by bearing and distance",
    why: "The rear line was not drawn. You'll draw it from the deed call and prove it closes on the found rod.",
    steps: ["**LAYMCUR** ↵ and pick any lot line. V-PROP-LINE becomes current.",
            "**L** ↵. Snap ^^Endpoint^^ on the east lot line at the NE rod (point 3).",
            `Type **@100<${A.rear_bearing.replace("°", "d")}** ↵ ↵  (degrees = {{d}}, no spaces).`,
            "**ID** ↵ and snap the new line's west endpoint."],
    check: `The line ends exactly on the NW rod (point 4): X = ${fmt(A.nw_end[0])}, Y = ${fmt(A.nw_end[1])}.`,
    tip: "If AutoCAD says \"Invalid point\", UNITS is not set to Surveyor's (Exercise 02)." },
  { n: "07", t: "Join the boundary and check the area",
    steps: ["**J** ↵ (JOIN). Pick all four lot lines ↵. The command line says {{4 objects converted to 1 polyline}}.",
            "**LI** ↵ (LIST) and pick the boundary.",
            "Or: **AREA** ↵ **O** ↵ and pick the boundary."],
    check: `Closed polyline, Area = ${A.area_sf.toLocaleString("en-US", { minimumFractionDigits: 2 })} sq ft (${A.area_ac} ac), Perimeter = ${fmt(A.perimeter)}.`,
    tip: "If JOIN leaves separate objects, the ends do not touch. Redo Exercise 06 with snaps on." },
  { part: "Part 3 - Clean up the linework" },
  { n: "08", t: "Fix a fence on the wrong layer (LAYMCH)",
    why: "The west fence (bubble 08) came in on layer 0. It shows white with no fence linetype and won't follow the layer standard.",
    steps: ["**LAYMCH** ↵. Pick the west fence ↵.",
            "Pick any correct fence line (for example the rear fence).",
            "**CTRL+1**. Confirm Layer = V-SITE-FENC."],
    check: "The west fence now shows the ---X--- fence linetype." },
  { n: "09", t: "Remove property overrides (MATCHPROP)",
    why: "East of the lot, the front-of-walk line (bubble 09) has a red color, dashed linetype and heavy lineweight typed directly onto it. Overrides like that break the standard and plot wrong.",
    steps: ["**MA** ↵. Pick the good front-of-walk line west of the lot first (the source).",
            "Pick the red dashed line ↵.",
            "**CTRL+1**. Color, Linetype and Lineweight all read ByLayer."],
    check: "The line matches the rest of the sidewalk." },
  { n: "10", t: "Close a corner with FILLET radius 0",
    why: "Field linework rarely closes perfectly. At the NE fence corner (bubble 10) the rear fence stops 1.7' short and the east fence runs 1.6' past.",
    steps: ["**F** ↵ **R** ↵ **0** ↵.",
            "Pick the rear fence near the corner.",
            "Pick the east fence ^^on the part you want to keep^^ (just south of the corner)."],
    check: "Zoom in close. The two fences meet in one clean corner with no gap or overshoot." },
  { n: "11", t: "TRIM an overshoot",
    why: "The patio's east edge (bubble 11) runs 3.6' into the house.",
    steps: ["**TR** ↵.",
            "Pick the part of the patio line that is ^^inside^^ the house ↵."],
    check: "The patio edge stops on the house wall.",
    tip: "AutoCAD 2021 and later trim in Quick mode: every object is a cutting edge, so just pick what to remove." },
  { n: "12", t: "EXTEND to a chosen boundary",
    why: "The driveway's east edge (bubble 12) stops 4.8' short. Concrete driveways run to the sidewalk, not to the property line.",
    steps: ["**EX** ↵ then **B** ↵ (Boundary edges).",
            "Pick the ^^back-of-walk^^ line (the upper sidewalk line) ↵.",
            "Pick the driveway's east edge near its south end ↵."],
    check: "Both driveway edges end on the back of walk.",
    tip: "Without the B option, Quick mode extends to the first thing it hits, which is the front lot line. On AutoCAD 2020 and earlier, EXTEND asks for boundaries first by default." },
  { part: "Part 4 - Symbols" },
  { n: "13", t: "INSERT a symbol at a survey point",
    why: `Point 31 (${pt[31].desc}) at bubble 13 has no tree symbol.`,
    steps: ["**LAYMCUR** ↵ and pick an existing tree symbol. V-SITE-VEGE becomes current.",
            "**-INSERT** ↵ **TREE-DECID** ↵.",
            "Snap ^^Node^^ on point 31. Press ↵ three times to accept X scale 1, Y scale = X and rotation 0.",
            "(Ribbon: Insert > Insert > TREE-DECID does the same thing.)"],
    check: "The tree is centered on point 31 and on layer V-SITE-VEGE.",
    tip: "The blocks in this file are drawn on layer 0 with ByLayer properties, so a symbol takes on the layer you insert it on." },
  { n: "14", t: "ROTATE with Reference to align a catch basin",
    why: "The catch basin at point 20 (bubble 14) is 35° off the curb. Rotate with Reference lines it up exactly without working out any angle.",
    steps: ["**RO** ↵. Pick the catch basin ↵. Base point: ^^Insertion^^ snap on the catch basin.",
            "**R** ↵ (Reference). Snap ^^Endpoint^^ on one corner of a long side of the grate, then the other corner of the same side.",
            "**P** ↵ (Points). Pick two points along the face-of-curb line, west point first. Use Shift + right-click > {{Nearest}}."],
    check: `The grate is parallel to the curb. In Properties (CTRL+1) Rotation reads ${A.front_bearing.replace("°", "d")}.` },
  { part: "Part 5 - Annotation" },
  { n: "15", t: "Label a lot line with bearing and distance (TEXT)",
    steps: ["**LAYMCUR** ↵ and pick an existing bearing label. V-PROP-TEXT becomes current. Confirm text style ^^SURVEY^^ is current (Annotate tab).",
            "**DT** ↵ (TEXT) **J** ↵ **BC** ↵. Snap ^^Midpoint^^ of the rear lot line.",
            "Height **2.4** ↵. That is 0.08\" plotted text × 30.",
            `Rotation **${A.front_bearing.replace("°", "d")}** ↵. The text is rotated so it reads left to right.`,
            `Type **${A.rear_bearing.replace("°", "%%d")} 100.00'** ↵ ↵.  ({{%%d}} makes the ° symbol.)`,
            `**M** ↵, pick the text ↵, pick any base point, then type **@1<${A.side_bearing.replace("°", "d")}** ↵ to move the label 1' off the line.`],
    check: "The label sits just outside the rear line and matches the other three." },
  { n: "16", t: "Make text readable (TORIENT)",
    why: "The street name (bubble 16) is upside down.",
    steps: ["**TORIENT** ↵. Pick MAPLE STREET ↵.",
            "Press ↵ to accept {{<Most readable>}}."],
    check: "MAPLE STREET reads right side up in the same place. TORIENT fixes a whole selection at once and keeps each text's insertion point. Don't use MOVE and ROTATE for this." },
  { n: "17", t: "Label the building (MTEXT)",
    steps: ["Set ^^V-ANNO-TEXT^^ current from the layer dropdown.",
            "**MT** ↵. At {{Specify first corner}}: Shift + right-click > {{Mid Between 2 Points}}. Pick the house's upper-left corner, then the lower-right corner of the main house (not the garage).",
            `**J** ↵ **MC** ↵.   **R** ↵ **${A.front_bearing.replace("°", "d")}** ↵.   **H** ↵ **2.4** ↵.   **W** ↵ **40** ↵.`,
            `Type three lines: {{1-STORY WOOD FRAME}} / {{HOUSE}} / {{FF=${fmt(pt[40].z)}}}. Read the FF elevation from point 40. Close the editor.`],
    check: "The label is centered in the house and aligned with it." },
  { n: "18", t: "Label a structure with a multileader (MLEADER)",
    why: "Structures get rim and invert callouts. Use the SSMH leader in the street as your pattern.",
    steps: ["On the Annotate tab > Leaders, set multileader style ^^SRV-30^^ current. Keep V-ANNO-TEXT current.",
            "**MLD** ↵ (MLEADER). Arrow: Shift + right-click > {{Nearest}} on the edge of the catch basin at bubble 18.",
            "Landing: pick up and to the right, in the front yard of LOT 9.",
            `Type {{CB TYPE 1}} / {{RIM=${fmt(pt[20].z)}}} / {{IE 12" S=307.18}}. Close the editor.`,
            "Adjust the text and landing with grips if needed."],
    check: "The leader matches the SSMH example: same text height, arrow and landing.",
    tip: `RIM comes from point 20. The invert comes from the field notes: 12" pipe south, measured 3.40' down from rim (${fmt(pt[20].z)} - 3.40 = 307.18).` },
  { n: "19", t: "Dimension building setbacks (DIMALIGNED)",
    why: "Setbacks are shown as the perpendicular distance from the building to the lot line.",
    steps: ["Set dimension style ^^SRV-30^^ current (Annotate tab) and layer ^^V-ANNO-DIMS^^ current.",
            "**DAL** ↵ (DIMALIGNED). Snap ^^Endpoint^^ on the garage's front-left corner (bubble 19).",
            "Second point: Shift + right-click > {{Perpendicular}}, then pick the front lot line. Place the dimension about 4' to the left.",
            "↵ to repeat. ^^Endpoint^^ on the house's front-right corner > ^^Perpendicular^^ to the east lot line. Place it about 5' up."],
    check: "The dimensions read 20.00' and 19.50'.",
    tip: "The SRV-30 dim style has DIMSCALE 30, so its 0.08\" text plots at the same size as your 2.4' notes." },
  { part: "Part 6 - Sheet and plot" },
  { n: "20", t: "Create a scaled viewport",
    steps: ["Click the ^^TOPO SHEET^^ layout tab. Set ^^G-ANNO-VPRT^^ current.",
            "**MV** ↵ (MVIEW). Pick the corners of the red dashed box.",
            "Double-click inside the viewport. **Z** ↵ **1/30XP** ↵.",
            "Pan with the wheel held down (don't roll it) until the site is centered.",
            "Double-click outside the viewport. Select its edge, **CTRL+1**: confirm the scale is 1\" = 30' ({{Standard scale}} 1\" = 30', or {{Custom scale}} 0.0333) and set {{Display locked}} = Yes."],
    check: "In paper space, **DI** ↵ along the front lot line measures 3.33 (inches). The graphic scale bar agrees." },
  { n: "21", t: "Plot to PDF",
    steps: ["**PLOT** ↵ (CTRL+P). The page setup is already filled in: ^^DWG To PDF.pc3^^, ^^ANSI B (17 x 11)^^, Plot area ^^Layout^^, Scale ^^1:1^^, Plot style table ^^monochrome.ctb^^.",
            "Click ^^Preview^^. Press ESC to close the preview, then click OK and save the PDF."],
    check: "The PDF shows no red bubbles, no dashed box and no viewport frame. Fences show ---X---, pipes show ---SD--- / ---SS--- / ---W---, and all text is readable." },
  { n: "22", t: "Clean up and save",
    steps: ["**LA** ↵. Freeze ^^TRAIN-NOTES^^.",
            "**PU** ↵ (PURGE). Check {{Purge nested items}} and click {{Purge All}}. This removes the unused layer TEMP-OLD and block OLD-TREE.",
            "**AUDIT** ↵ **Y** ↵.",
            "**QSAVE** ↵."],
    check: "AUDIT ends with no errors left unfixed. Your file matches the COMPLETED answer key." },
];

// ---------------------------------------------------------------- document
const numbering = [{ reference: "bullets", levels: [{ level: 0, format: LevelFormat.BULLET, text: "•", alignment: AlignmentType.LEFT,
  style: { paragraph: { indent: { left: 360, hanging: 260 } } } }] }];
for (const e of EX) if (e.n) numbering.push({ reference: `steps-${e.n}`, levels: [{ level: 0, format: LevelFormat.DECIMAL, text: "%1.",
  alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 400, hanging: 300 } } } }] });

const body = [];

// cover
body.push(
  new Paragraph({ spacing: { before: 600, after: 60 }, children: [new TextRun({ text: "PARAMETRIX  |  SURVEY", bold: true, size: 22, color: ACCENT, font: FONT })] }),
  new Paragraph({ spacing: { after: 60 }, children: [new TextRun({ text: "Survey CAD Training", bold: true, size: 56, font: FONT })] }),
  new Paragraph({ spacing: { after: 300 }, border: { bottom: { style: BorderStyle.SINGLE, size: 12, color: ACCENT, space: 6 } },
    children: [new TextRun({ text: "Level 1 - AutoCAD Basics for Survey Drafting", size: 32, font: FONT, color: "404040" })] }),
  P("You will finish a small topographic survey of one residential lot in plain AutoCAD (no Civil 3D). You will set survey units, check the boundary, clean up field linework, place symbols, annotate, and plot a 1\" = 30' sheet."),
  table(["Item", ""], [
    ["Start file", "^^Survey_CAD_Level1_START.dxf^^"],
    ["Answer key", "^^Survey_CAD_Level1_COMPLETED.dxf^^ (open it to compare, don't copy from it)"],
    ["Software", "AutoCAD 2018 or later (plain AutoCAD or Civil 3D)"],
    ["Time", "About 2 to 3 hours"],
    ["Site", "LOT 8, BLOCK 3, CEDAR PARK ADDITION: a fictitious site on an assumed grid"],
  ], [1900, CONTENT - 1900]),
  new Paragraph({ spacing: { before: 200 } }),
  table(["Notation", "Meaning"], [
    ["↵", "Press Enter (or Space)"],
    ["**TEXT IN THIS FONT**", "Type it exactly"],
    ["Pick", "Click in the drawing"],
    ["Endpoint, Node, Insertion ...", "Object snap to use (running snap, or Shift + right-click for a one-time snap)"],
    ["CTRL+1", "Properties palette"],
  ], [2600, CONTENT - 2600]),
  new Paragraph({ spacing: { before: 200 } }),
  new Paragraph({ heading: HeadingLevel.HEADING_2, pageBreakBefore: true, children: [new TextRun("The drawing you will finish")] }),
  P("The START drawing with the survey points turned on. Each red bubble marks where an exercise happens."),
  ...figure(path.join(IMG, "start_model.png"), 6.6, "The START drawing (survey points shown). Red bubbles = exercise numbers."),
);

// primer
body.push(H1("Survey CAD basics"));
body.push(H2("Coordinates"));
body.push(P("AutoCAD's ^^X is the Easting^^ and ^^Y is the Northing^^. Point lists are usually written P,N,E,Z,D, so watch the order when you type or import coordinates. This site uses an assumed grid with the SW lot corner at N 8000.00, E 5000.00."));
body.push(H2("Angles and bearings"));
body.push(P("AutoCAD's default angle is 0° = east, measured counterclockwise. A bearing is measured from north or south toward east or west. After you set ^^Surveyor's Units^^ (Exercise 02), you type bearings directly:"));
body.push(table(["You want", "Type"], [
  ["100 ft at N 45°30'15\" E from the last point", "**@100<N45d30'15\"E**"],
  ["The ° symbol in TEXT", "**%%d**"],
  ["Bearing as a text/block rotation", "**N89d12'40\"E**"],
], [4200, CONTENT - 4200]));
body.push(H2("Plot scale, text height and linetypes"));
body.push(P("This drawing is drafted for one plot scale: ^^1\" = 30'^^. Model-space size = plotted size × 30."));
body.push(table(["Use", "Plotted height", "Model height"], [
  ["Point labels", "0.06\"", "1.8'"],
  ["Standard notes, bearings, dimensions", "0.08\"", "2.4'"],
  ["Street names, lot numbers", "0.10\"", "3.0'"],
], [3600, 1900, CONTENT - 5500]));
body.push(P("The linetypes are defined in plotted inches with ^^LTSCALE = 30^^ and ^^PSLTSCALE = 0^^, so they look the same in model space and in the 1\" = 30' viewport. Level 2 covers annotative scaling for sheets with several scales."));
body.push(H2("Layers"));
body.push(P("Layer names follow the NCS pattern Discipline-Major-Minor (V = survey). {{Replace them with the Parametrix CAD standard if it differs.}}"));
body.push(table(["Layer", "Contents"], [
  ["V-PROP-LINE / -RWAY / -CNTR / -ADJN", "Boundary, right-of-way, centerline, adjoiner lines"],
  ["V-PROP-TEXT", "Bearings, distances, lot and block labels"],
  ["V-CTRL-MONU", "Monuments and found rods"],
  ["V-NODE", "Survey point blocks (number, elevation, description)"],
  ["V-ROAD-CURB / -SWLK", "Curb and gutter, sidewalk"],
  ["V-SITE-CONC / -FENC / -VEGE", "Concrete flatwork, fences, trees"],
  ["V-BLDG-OTLN", "Building footprint"],
  ["V-STRM / V-SSWR / V-WATR / V-POWR -STRC, -PIPE ...", "Utility structures and lines"],
  ["V-ANNO-TEXT / -DIMS / -TTLB", "Notes and leaders, dimensions, title block"],
  ["G-ANNO-VPRT", "Viewports (no plot)"],
  ["TRAIN-NOTES", "Training bubbles (no plot, frozen at the end)"],
], [3700, CONTENT - 3700], { size: 18 }));

// exercises
let first = true;
for (const e of EX) {
  if (e.part) { body.push(H1(e.part, e.part.startsWith("Part 1"))); first = true; continue; }
  body.push(new Paragraph({ heading: HeadingLevel.HEADING_2, keepNext: true, spacing: { before: first ? 120 : 360 },
    children: [new TextRun({ text: `${e.n}   ${e.t}` })] }));
  first = false;
  if (e.why) body.push(P(e.why, { run: { color: "404040" }, para: { keepNext: true } }));
  for (const s of e.steps) body.push(new Paragraph({ numbering: { reference: `steps-${e.n}`, level: 0 }, children: runs(s), spacing: { after: 60 }, keepNext: true }));
  body.push(new Paragraph({ spacing: { before: 80, after: 80 }, keepNext: true, children: [new TextRun({ text: "✓ Check:  ", bold: true, color: "2E7D32", font: FONT }), ...runs(e.check)] }));
  if (e.tip) { body.push(note("TIP", e.tip)); body.push(new Paragraph({ spacing: { after: 60 } })); }
  body.push(...beforeAfter(e.n));
}

// completed sheet
body.push(H1("The finished sheet"));
body.push(...figure(path.join(IMG, "completed_sheet.png"), 7.2, "TOPO SHEET plotted with monochrome.ctb (from the COMPLETED answer key)"));

// checklist
body.push(H1("Completion checklist"));
const checks = [
  ["02", "UNITS: Surveyor's, 0.00; LIST shows bearings"], ["03", "Running snaps set; ID of point 1 = 5000.00, 8000.00"],
  ["04", "V-NODE thawed"], ["05", "Inverse recorded"], ["06", "Rear line drawn by bearing; closes on point 4"],
  ["07", "Boundary is one closed polyline, 14,000.00 sq ft"], ["08", "West fence on V-SITE-FENC"],
  ["09", "Sidewalk overrides removed (all ByLayer)"], ["10", "NE fence corner closed"], ["11", "Patio edge trimmed at the house"],
  ["12", "Driveway edge extended to back of walk"], ["13", "Tree inserted at point 31"], ["14", "Catch basin parallel to the curb"],
  ["15", "Rear line labeled"], ["16", "MAPLE STREET reads right side up"], ["17", "Building MTEXT centered, with FF"],
  ["18", "Catch basin MLEADER with RIM and IE"], ["19", "Setback dims 20.00' and 19.50'"],
  ["20", "Viewport 1\" = 30', centered, locked, on G-ANNO-VPRT"], ["21", "PDF plotted, nothing non-plot showing"],
  ["22", "TRAIN-NOTES frozen, purged, audited, saved"],
];
body.push(table(["☐", "Ex", "Item"], checks.map(([n, t]) => ["☐", n, t]), [500, 700, CONTENT - 1200]));

// answer key
body.push(H1("Answer key"));
body.push(table(["Ex", "Result"], [
  ["02", `Front lot line: before 0.7889 (decimal degrees); after ${A.front_bearing}, 100.00`],
  ["03", `Point 1: X = ${fmt(A.pt1[0])}, Y = ${fmt(A.pt1[1])}`],
  ["05", `Point 1 to point 10: ${inv.bearing}, ${fmt(inv.distance)}'   (reverse: ${inv.reverse})`],
  ["06", `Rear line ${A.rear_bearing} 100.00' ends at X = ${fmt(A.nw_end[0])}, Y = ${fmt(A.nw_end[1])} (point 4)`],
  ["07", `Area ${A.area_sf.toLocaleString("en-US", { minimumFractionDigits: 2 })} sq ft = ${A.area_ac} ac; perimeter ${fmt(A.perimeter)}'`],
  ["14", `Catch basin rotation: ${A.front_bearing} (was 35° off the curb)`],
  ["19", "Garage to front line 20.00'; house to east line 19.50'"],
  ["20", "100' front line = 3.33\" on the sheet"],
], [700, CONTENT - 700]));

// point list
body.push(H2("Field point list (PNEZD)"));
body.push(table(["Pt", "Northing", "Easting", "Elev", "Description"],
  A.points.map((p) => [p.pt, fmt(p.n), fmt(p.e), fmt(p.z), p.desc]), [700, 1700, 1700, 1300, CONTENT - 5400], { size: 18 }));

// command reference
body.push(H1("Commands used"));
body.push(table(["Command", "Alias", "Used for"], [
  ["UNITS", "UN", "Surveyor's angle units, precision"], ["OSNAP", "OS", "Running object snaps"],
  ["ID", "", "Coordinate of a point"], ["DIST", "DI", "Inverse: bearing and distance"],
  ["LAYER / LAYISO / LAYUNISO", "LA", "Layer control"], ["LAYMCUR / LAYMCH", "", "Make an object's layer current / move objects to a layer"],
  ["LINE", "L", "Lines by bearing and distance"], ["JOIN", "J", "Lines into one polyline"],
  ["LIST / AREA", "LI", "Length, bearing, area, perimeter"], ["MATCHPROP", "MA", "Copy properties / clear overrides"],
  ["FILLET", "F", "Close corners (R = 0)"], ["TRIM / EXTEND", "TR / EX", "Clean overshoots and undershoots"],
  ["INSERT / -INSERT", "I", "Place symbols"], ["ROTATE (Reference)", "RO", "Align a symbol to linework"],
  ["TEXT / MTEXT", "DT / MT", "Labels and notes"], ["TORIENT", "", "Make text readable"],
  ["MLEADER", "MLD", "Leader callouts"], ["DIMALIGNED", "DAL", "Setback dimensions"],
  ["MVIEW", "MV", "Viewports"], ["PLOT", "CTRL+P", "PDF output"],
  ["PURGE / AUDIT", "PU", "Drawing cleanup"],
], [2800, 1300, CONTENT - 4100]));

body.push(H2("For the instructor"));
body.push(P("The drawings and this guide are generated by scripts in the training folder: ^^build_drawings.py^^, ^^render_images.py^^ and ^^build_guide.js^^. The COMPLETED file is built by applying each exercise's exact result to START, so the answer key matches what the commands produce. To change the site or add an exercise, edit the scripts and rebuild; don't hand-edit the DXFs."));

const doc = new Document({
  creator: "Parametrix Survey", title: "Survey CAD Training - Level 1",
  styles: {
    default: { document: { run: { font: FONT, size: 21 } } },
    paragraphStyles: [
      { id: "Heading1", name: "Heading 1", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { size: 34, bold: true, color: ACCENT, font: FONT }, paragraph: { spacing: { before: 0, after: 200 }, outlineLevel: 0 } },
      { id: "Heading2", name: "Heading 2", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { size: 25, bold: true, color: "1F1F1F", font: FONT },
        paragraph: { spacing: { before: 280, after: 100 }, outlineLevel: 1,
          border: { bottom: { style: BorderStyle.SINGLE, size: 4, color: "BFBFBF", space: 2 } } } },
    ],
  },
  numbering: { config: numbering },
  sections: [{
    properties: { page: { size: { width: PAGE_W, height: 15840 }, margin: { top: 1000, bottom: 1000, left: MARGIN, right: MARGIN } } },
    footers: { default: new Footer({ children: [new Paragraph({ alignment: AlignmentType.RIGHT, children: [
      new TextRun({ text: "Survey CAD Training - Level 1    ", size: 16, color: "808080", font: FONT }),
      new TextRun({ children: [PageNumber.CURRENT], size: 16, color: "808080", font: FONT })] })] }) },
    children: body,
  }],
});

Packer.toBuffer(doc).then((buf) => {
  const f = path.join(OUT, "Survey_CAD_Level1_Guide.docx");
  fs.writeFileSync(f, buf);
  console.log("wrote", f);
});
