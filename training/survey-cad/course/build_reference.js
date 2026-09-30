// Survey CAD Command Reference: AutoCAD and Civil 3D commands for survey drafters, ordered from everyday
// basics to power-drafter tools. Content lives in reference_content.js; this file lays it out (portrait, binder margin).
const fs = require("fs");
const path = require("path");
const {
  Document, Packer, Paragraph, TextRun, Table, TableRow, TableCell, WidthType, BorderStyle,
  ShadingType, AlignmentType, ImageRun, Footer, PageNumber,
} = require("docx");
const C = require("./reference_content");

const OUT = path.join(__dirname, "output");
const FONT = "Calibri", MONO = "Consolas";
const BLUE = "1F4E79", NAVY = "2B2B3C", RED = "C00000", GREEN = "2E7D32", GREY = "7F7F7F", HEAD = "D9E2F3";
const PAGE_W = 12240, PAGE_H = 15840, LEFT = 1260, RIGHT = 620;   // wide left edge for binder holes
const FULL = PAGE_W - LEFT - RIGHT, GAP = 200, HALF = (FULL - GAP) / 2;
const thin = { style: BorderStyle.SINGLE, size: 4, color: "BFBFBF" };
const none = { style: BorderStyle.NONE, size: 0, color: "FFFFFF" };
const border = { top: thin, bottom: thin, left: thin, right: thin };
const noBorder = { top: none, bottom: none, left: none, right: none };
const PAD = { top: 6, bottom: 6, left: 60, right: 60 };

let SIZE = 17;          // body size in half-points; beginner levels read larger than advanced ones
let KEEP = false;       // keepNext on every paragraph while building a block that must not split
const t = (text, o = {}) => new TextRun({ text, font: FONT, size: SIZE, ...o });
const mono = (text, o = {}) => new TextRun({ text, font: MONO, size: SIZE - 1, bold: true, ...o });
// **x** = what you type (bold mono)   !!x!! = warning (red bold)   __x__ = bold
function rich(s, o = {}) {
  return String(s).split(/(\*\*[^*]+\*\*|!![^!]+!!|__[^_]+__)/).filter(Boolean).map((p) => {
    if (p.startsWith("**")) return mono(p.slice(2, -2), o.size ? { size: o.size - 1 } : {});
    if (p.startsWith("!!")) return t(p.slice(2, -2), { ...o, bold: true, color: RED });
    if (p.startsWith("__")) return t(p.slice(2, -2), { ...o, bold: true });
    return t(p, o);
  });
}
const para = (children, o = {}) => new Paragraph({ keepNext: KEEP, spacing: { after: 0 }, children, ...o });
const cell = (children, w, o = {}) => new TableCell({
  width: { size: w, type: WidthType.DXA }, borders: o.borders || border, margins: o.margins || PAD,
  shading: o.fill ? { type: ShadingType.CLEAR, fill: o.fill, color: "auto" } : undefined,
  columnSpan: o.span, children: Array.isArray(children[0]) ? children.map((c) => para(c)) : [para(children)],
});
const table = (w, rows, width = FULL) => new Table({ width: { size: width, type: WidthType.DXA }, columnWidths: w, rows });
const gap = (after = 90) => new Paragraph({ spacing: { after }, children: [] });

function titleRow(title, n, W, fill = BLUE, note) {
  const kids = [t(title, { bold: true, color: "FFFFFF", size: SIZE + 1 })];
  if (note) kids.push(t("   " + note, { color: "DDE6F2", italics: true, size: SIZE - 2 }));
  return new TableRow({ tableHeader: true, cantSplit: true, children: [new TableCell({ columnSpan: n,
    width: { size: W, type: WidthType.DXA }, borders: border, margins: PAD,
    shading: { type: ShadingType.CLEAR, fill, color: "auto" }, children: [new Paragraph({ keepNext: true, children: kids })] })] });
}
function headRow(heads, w) {
  KEEP = true;
  const r = new TableRow({ tableHeader: true, cantSplit: true, children: heads.map((h, i) => cell([t(h, { bold: true, size: SIZE - 1 })], w[i], { fill: HEAD })) });
  KEEP = false;
  return r;
}

// Button pictures are optional: course/icons/<NAME>.png (screenshots from your own install, never committed).
const ICONS = path.join(__dirname, "icons");
function cmdCell(name, ribbon, w) {
  const f = path.join(ICONS, name.replace(/[ /']+/g, "_") + ".png");
  const icon = fs.existsSync(f) ? [new ImageRun({ type: "png", data: fs.readFileSync(f), transformation: { width: 16, height: 16 } }), t(" ")] : [];
  return cell([...icon, t(name, { bold: true })], w);
}
// Typed input in bold mono; menu paths and mouse actions in plain italics.
const TYPED = /↵|^CTRL|^Shift|^F\d|^'|^%%|^[A-Z0-9 /._#@<'"+-]+$/;
const typeRuns = (k) => (!k ? [t("")] : TYPED.test(k) && !k.includes("›") ? [mono(k)] : [t(k, { italics: true, color: "404040" })]);
function whatCell(what, warn, w, ribbon) {
  const lines = [[...rich(what), ...(ribbon ? [t("   " + ribbon, { size: SIZE - 4, color: GREY })] : [])]];
  if (warn) lines.push([t("⚠ " + warn, { size: SIZE - 2, bold: true, color: RED })]);
  return cell(lines, w);
}
const AMBER = "B45F06";
const SURVEY = {   // "Survey use" column: what the command may do to coordinate-controlled survey geometry
  safe: ["Safe", GREEN], care: ["With care", AMBER], derived: ["Derived geometry only", "2E75B6"],
  draft: ["Drafted items only", AMBER], never: ["Never", RED],
};

// ---- block renderers --------------------------------------------------------------------------------
// cmds: rows [command, type, what, {ribbon, warn, survey}]
function cmdsBlock(b) {
  const withSurvey = b.rows.some((r) => r[3] && r[3].survey);
  const w = withSurvey ? [2050, 1950, FULL - 2050 - 1950 - 1250, 1250] : [2050, 1950, FULL - 4000];
  const heads = withSurvey ? ["Command", "Type", "What it does", "Survey use"] : ["Command", "Type", "What it does"];
  return table(w, [titleRow(b.title, w.length, FULL, b.fill, b.note), headRow(b.heads || heads, w),
    ...b.rows.map(([c, k, what, o = {}]) => new TableRow({ cantSplit: true, children: [
      cmdCell(c, o.ribbon, w[0]), cell(typeRuns(k), w[1]), whatCell(what, o.warn, w[2], o.ribbon),
      ...(withSurvey ? [cell([t(...(o.survey ? [SURVEY[o.survey][0], { bold: true, color: SURVEY[o.survey][1], size: SIZE - 2 }] : [""]))], w[3])] : [])] }))]);
}
// pairs: rows [a, b]; a in mono when b.code
function pairsBlock(b, W = FULL) {
  const a = Math.round(W * (b.split || 0.34));
  const w = [a, W - a];
  return table(w, [titleRow(b.title, 2, W, b.fill, b.note), ...(b.heads ? [headRow(b.heads, w)] : []),
    ...b.rows.map(([x, y]) => new TableRow({ cantSplit: true, children: [
      cell(b.code ? [mono(x)] : rich(`__${x}__`), w[0]), cell(rich(y), w[1])] }))], W);
}
// three-column generic with rich text: rows [a, b, c]
function tripleBlock(b) {
  const w = b.widths || [2300, 2300, FULL - 4600];
  return table(w, [titleRow(b.title, 3, FULL, b.fill, b.note), headRow(b.heads, w),
    ...b.rows.map((r) => new TableRow({ cantSplit: true, children: r.map((x, i) => cell(rich(i === 0 ? `__${x}__` : x), w[i])) }))]);
}
function sysvarBlock(b) {
  const w = [1900, 1150, FULL - 1900 - 1150 - 2600, 2600];
  return table(w, [titleRow(b.title, 4, FULL, b.fill, b.note), headRow(["Variable", "Autodesk default", "What it controls", "Parametrix / training note"], w),
    ...b.rows.map(([v, d, what, note, warn]) => new TableRow({ cantSplit: true, children: [
      cell([mono(v)], w[0]), cell(rich(d), w[1]), whatCell(what, warn, w[2]), cell(rich(note || ""), w[3])] }))]);
}
// diagnose: rows [problem, [checks...]]
function diagBlock(b) {
  const w = [2900, FULL - 2900];
  return table(w, [titleRow(b.title, 2, FULL, b.fill, b.note), headRow(["Problem", "Check"], w),
    ...b.rows.map(([p, checks]) => new TableRow({ cantSplit: true, children: [
      cell(rich(`__${p}__`), w[0]),
      cell(checks.flatMap((c, i) => [...(i ? [t("   ")] : []), t(`${i + 1} `, { bold: true, color: BLUE }), ...rich(c)]), w[1])] }))]);
}
function box(b) {   // concept (blue), warn (red), aid (green)
  const [fill, bar, head] = { concept: ["EAF1F8", BLUE, BLUE], warn: ["FCE4E4", RED, RED], aid: ["E8F3E8", GREEN, GREEN] }[b.kind || "concept"];
  const W = b.width || FULL;
  KEEP = true;
  const kids = [para([t(b.title, { bold: true, color: head, size: SIZE + 1 })])];
  for (const l of b.lines) kids.push(para(l.startsWith("- ") ? [t("•  ", { bold: true, color: bar }), ...rich(l.slice(2))] : rich(l)));
  KEEP = false;
  return table([W], [new TableRow({ cantSplit: true, children: [new TableCell({ width: { size: W, type: WidthType.DXA },
    shading: { type: ShadingType.CLEAR, fill, color: "auto" }, margins: { top: 50, bottom: 50, left: 140, right: 120 },
    borders: { top: none, bottom: none, right: none, left: { style: BorderStyle.SINGLE, size: 18, color: bar } }, children: kids })] })], W);
}
// exercise: {title, steps[], check, tip}
function exercise(e, W = HALF) {
  const w = [300, W - 300];
  KEEP = true;
  const rows = e.steps.map((s, i) => new TableRow({ cantSplit: true, children: [
    cell([t(String(i + 1), { bold: true, color: BLUE })], w[0]), cell(rich(s), w[1])] }));
  const extra = [];
  if (e.check) extra.push(new TableRow({ cantSplit: true, children: [cell([t("✓ Check  ", { bold: true, color: GREEN }), ...rich(e.check)], W, { span: 2, fill: "F2F7F2" })] }));
  if (e.tip) extra.push(new TableRow({ cantSplit: true, children: [cell([t("Tip  ", { bold: true, color: BLUE }), ...rich(e.tip, { italics: true })], W, { span: 2, fill: "F2F2F2" })] }));
  KEEP = false;
  return table(w, [titleRow("HOW TO  " + e.title, 2, W, "385D8A"), ...rows, ...extra], W);
}
const twoCol = (left, right) => table([HALF, GAP, HALF], [new TableRow({ cantSplit: true, children: [
  cell(left, HALF, { borders: noBorder, margins: { top: 0, bottom: 0, left: 0, right: 0 } }),
  cell([t("")], GAP, { borders: noBorder }),
  cell(right, HALF, { borders: noBorder, margins: { top: 0, bottom: 0, left: 0, right: 0 } })] })]);
function exercisesBlock(b) {
  const out = [];
  for (let i = 0; i < b.items.length; i += 2) {
    const a = b.items[i], c = b.items[i + 1];
    if (!c && b.items.length === 1 && a.wide) { out.push(exercise(a, FULL)); break; }
    out.push(new Table({ width: { size: FULL, type: WidthType.DXA }, columnWidths: [HALF, GAP, HALF], rows: [new TableRow({ cantSplit: true, children: [
      new TableCell({ width: { size: HALF, type: WidthType.DXA }, borders: noBorder, margins: { top: 0, bottom: 0, left: 0, right: 0 }, children: [exercise(a)] }),
      new TableCell({ width: { size: GAP, type: WidthType.DXA }, borders: noBorder, children: [new Paragraph("")] }),
      new TableCell({ width: { size: HALF, type: WidthType.DXA }, borders: noBorder, margins: { top: 0, bottom: 0, left: 0, right: 0 }, children: c ? [exercise(c)] : [new Paragraph("")] })] })] }));
    out.push(gap(60));
  }
  return out;
}
function sideBySide(b) {   // two small blocks next to each other
  const r = (x) => (x.type === "pairs" ? pairsBlock(x, HALF) : box({ ...x, width: HALF }));
  return new Table({ width: { size: FULL, type: WidthType.DXA }, columnWidths: [HALF, GAP, HALF], rows: [new TableRow({ cantSplit: true, children: [
    new TableCell({ width: { size: HALF, type: WidthType.DXA }, borders: noBorder, margins: { top: 0, bottom: 0, left: 0, right: 0 }, children: [r(b.left)] }),
    new TableCell({ width: { size: GAP, type: WidthType.DXA }, borders: noBorder, children: [new Paragraph("")] }),
    new TableCell({ width: { size: HALF, type: WidthType.DXA }, borders: noBorder, margins: { top: 0, bottom: 0, left: 0, right: 0 }, children: [r(b.right)] })] })] });
}

function sectionBar(S) {
  const fill = S.advanced ? NAVY : BLUE;
  const kids = [new TextRun({ text: ` ${S.n}   `, font: FONT, size: 30, bold: true, color: S.advanced ? "F4B183" : "9DC3E6" }),
    new TextRun({ text: S.title, font: FONT, size: 26, bold: true, color: "FFFFFF" })];
  if (S.tag) kids.push(new TextRun({ text: "   " + S.tag, font: FONT, size: 15, bold: true, color: S.advanced ? "F4B183" : "BDD7EE" }));
  if (S.sub) kids.push(new TextRun({ text: "    " + S.sub, font: FONT, size: 17, italics: true, color: "E6ECF5" }));
  const edge = { style: BorderStyle.SINGLE, size: 1, color: fill, space: 4 };
  return [new Paragraph({ keepNext: true, keepLines: true, spacing: { before: 60, after: 100 },
    shading: { type: ShadingType.CLEAR, fill, color: "auto" }, border: { top: edge, bottom: edge, left: edge, right: edge },
    children: kids })];
}

function renderBlock(b) {
  switch (b.type) {
    case "cmds": return [cmdsBlock(b)];
    case "pairs": return [pairsBlock(b)];
    case "triple": return [tripleBlock(b)];
    case "sysvars": return [sysvarBlock(b)];
    case "diag": return [diagBlock(b)];
    case "box": return [box(b)];
    case "side": return [sideBySide(b)];
    case "exercises": case "how": return exercisesBlock(b);
    default: throw new Error("unknown block " + b.type);
  }
}

function cover() {
  SIZE = 16;
  const out = [
    new Paragraph({ spacing: { after: 0 }, children: [new TextRun({ text: "PARAMETRIX  |  SURVEY CAD", bold: true, size: 20, color: BLUE, font: FONT })] }),
    new Paragraph({ spacing: { after: 0 }, children: [new TextRun({ text: C.TITLE, bold: true, size: 40, font: FONT })] }),
    new Paragraph({ spacing: { after: 140 }, border: { bottom: { style: BorderStyle.SINGLE, size: 10, color: BLUE, space: 4 } },
      children: [new TextRun({ text: C.SUBTITLE, italics: true, size: 21, color: "404040", font: FONT })] }),
  ];
  for (const b of C.COVER) { out.push(...renderBlock(b)); out.push(gap(70)); }
  return out;
}

function build() {
  const body = cover();
  for (const L of C.LEVELS) {
    SIZE = L.size || 15;
    if (L.newPage) body.push(new Paragraph({ pageBreakBefore: true, spacing: { after: 0 }, children: [] }));
    body.push(...sectionBar(L));
    for (const b of L.blocks) { body.push(...renderBlock(b)); body.push(gap(70)); }
  }
  body.pop();   // no spacer after the last table: it would spill onto an empty page
  {
  }
  const footers = { default: new Footer({ children: [new Paragraph({ alignment: AlignmentType.RIGHT, children: [
    new TextRun({ text: C.FOOTER + "    ", size: 15, color: "808080", font: FONT }),
    new TextRun({ children: [PageNumber.CURRENT], size: 15, color: "808080", font: FONT })] })] }) };
  const doc = new Document({
    creator: "Parametrix Survey", title: C.TITLE,
    styles: { default: { document: { run: { font: FONT, size: 17 } } } },
    sections: [{ properties: { page: { size: { width: PAGE_W, height: PAGE_H }, margin: { top: 600, bottom: 600, left: LEFT, right: RIGHT } } },
      footers, children: body }],
  });
  return Packer.toBuffer(doc).then((buf) => {
    const f = path.join(OUT, C.FILE + ".docx");
    fs.mkdirSync(OUT, { recursive: true });
    fs.writeFileSync(f, buf);
    console.log("wrote", f);
  });
}
build();
