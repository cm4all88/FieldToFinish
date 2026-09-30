// One-page command sheet for the Finish the Drawing course.
const fs = require("fs");
const path = require("path");
const {
  Document, Packer, Paragraph, TextRun, Table, TableRow, TableCell, WidthType, BorderStyle,
  ShadingType, PageOrientation, AlignmentType,
} = require("docx");

const OUT = path.join(__dirname, "output");
const FONT = "Calibri", MONO = "Consolas", ACCENT = "1F4E79";
const PAGE_W = 15840, PAGE_H = 12240, MARGIN = 540;
const CONTENT = PAGE_W - 2 * MARGIN;
const GAP = 240;
const COL = (CONTENT - GAP) / 2;
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

const t = (text, o = {}) => new TextRun({ text, font: FONT, size: 16, ...o });
const cmd = (text) => new TextRun({ text, font: MONO, size: 15, bold: true });
const cell = (children, w, head, fill) => new TableCell({
  width: { size: w, type: WidthType.DXA }, borders: { top: thin, bottom: thin, left: thin, right: thin },
  shading: fill ? { type: ShadingType.CLEAR, fill, color: "auto" } : undefined,
  margins: { top: 20, bottom: 20, left: 70, right: 70 },
  children: [new Paragraph({ children })],
});

function groupTable(title, rows) {
  const w = [1500, 1800, COL - 1500 - 1800 - 780, 780];
  const head = new TableRow({ tableHeader: true, children: [
    cell([t(title, { bold: true, color: "FFFFFF" })], w[0] + w[1] + w[2] + w[3], true, ACCENT),
  ].map((c, i) => new TableCell({ ...c.options, columnSpan: 4, width: { size: COL, type: WidthType.DXA },
    borders: { top: thin, bottom: thin, left: thin, right: thin }, shading: { type: ShadingType.CLEAR, fill: ACCENT, color: "auto" },
    margins: { top: 20, bottom: 20, left: 70, right: 70 },
    children: [new Paragraph({ children: [t(title, { bold: true, color: "FFFFFF", size: 17 })] })] })) });
  const sub = new TableRow({ children: [
    cell([t("Command", { bold: true })], w[0], true, "D9E2F3"), cell([t("Type", { bold: true })], w[1], true, "D9E2F3"),
    cell([t("Use it to", { bold: true })], w[2], true, "D9E2F3"), cell([t("Stage", { bold: true })], w[3], true, "D9E2F3")] });
  const body = rows.map(([c, k, u, s]) => new TableRow({ children: [
    cell([t(c, { bold: true })], w[0]), cell([cmd(k)], w[1]), cell([t(u)], w[2]), cell([t(s)], w[3])] }));
  return new Table({ width: { size: COL, type: WidthType.DXA }, columnWidths: w, rows: [head, sub, ...body] });
}

function pairTable(title, rows, codeFirst) {
  const w = [Math.round(COL * 0.42), COL - Math.round(COL * 0.42)];
  const head = new TableRow({ children: [new TableCell({ columnSpan: 2, width: { size: COL, type: WidthType.DXA },
    borders: { top: thin, bottom: thin, left: thin, right: thin }, shading: { type: ShadingType.CLEAR, fill: ACCENT, color: "auto" },
    margins: { top: 20, bottom: 20, left: 70, right: 70 },
    children: [new Paragraph({ children: [t(title, { bold: true, color: "FFFFFF", size: 17 })] })] })] });
  return new Table({ width: { size: COL, type: WidthType.DXA }, columnWidths: w, rows: [head,
    ...rows.map(([a, b]) => new TableRow({ children: [cell([codeFirst ? cmd(a) : t(a, { bold: true })], w[0]), cell([t(b)], w[1])] }))] });
}

const gap = () => new Paragraph({ spacing: { after: 60 }, children: [] });
const left = [groupTable(...GROUPS[0]), gap(), groupTable(...GROUPS[1]), gap(), groupTable(...GROUPS[2])];
const right = [groupTable(...GROUPS[3]), gap(), groupTable(...GROUPS[4]), gap(),
  pairTable("Typing input", INPUT, true), gap(), pairTable("Snaps (Shift + right-click)", SNAPS, false), gap(),
  pairTable("Keys", KEYS, false)];

const layout = new Table({
  width: { size: CONTENT, type: WidthType.DXA }, columnWidths: [COL, GAP, COL],
  rows: [new TableRow({ children: [
    new TableCell({ width: { size: COL, type: WidthType.DXA }, borders: { top: none, bottom: none, left: none, right: none }, children: left }),
    new TableCell({ width: { size: GAP, type: WidthType.DXA }, borders: { top: none, bottom: none, left: none, right: none }, children: [new Paragraph("")] }),
    new TableCell({ width: { size: COL, type: WidthType.DXA }, borders: { top: none, bottom: none, left: none, right: none }, children: right }),
  ] })],
});

const doc = new Document({
  creator: "Parametrix Survey", title: "Survey CAD Command Sheet",
  styles: { default: { document: { run: { font: FONT, size: 16 } } } },
  sections: [{
    properties: { page: { size: { width: PAGE_H, height: PAGE_W, orientation: PageOrientation.LANDSCAPE },
      margin: { top: 500, bottom: 400, left: MARGIN, right: MARGIN } } },
    children: [
      new Paragraph({ spacing: { after: 40 }, children: [
        new TextRun({ text: "PARAMETRIX  |  SURVEY CAD TRAINING", bold: true, size: 18, color: ACCENT, font: FONT }),
        new TextRun({ text: "     Finish the Drawing - Command Sheet", bold: true, size: 30, font: FONT })] }),
      new Paragraph({ spacing: { after: 100 }, border: { bottom: { style: BorderStyle.SINGLE, size: 8, color: ACCENT, space: 4 } },
        children: [t("Every command used in the course. Stage = where the guide uses it (0 = getting started). ↵ = Enter.", { italics: true, color: "595959" })] }),
      layout,
    ],
  }],
});

Packer.toBuffer(doc).then((buf) => {
  const f = path.join(OUT, "SURVEY_CAD_Command_Sheet.docx");
  fs.writeFileSync(f, buf);
  console.log("wrote", f);
});
