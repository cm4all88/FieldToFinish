// Shared builder for the Survey CAD training guides (.docx).
// Each level's build_guide.js supplies its content as data and calls buildGuide().
const fs = require("fs");
const path = require("path");
const {
  Document, Packer, Paragraph, TextRun, ImageRun, Table, TableRow, TableCell, WidthType,
  AlignmentType, HeadingLevel, LevelFormat, BorderStyle, ShadingType, Footer, PageNumber,
} = require("docx");

const FONT = "Calibri";
const MONO = "Consolas";
const ACCENT = "1F4E79";
const PAGE_W = 12240, MARGIN = 1080, CONTENT = PAGE_W - 2 * MARGIN;
const none = { style: BorderStyle.NONE, size: 0, color: "FFFFFF" };
const thin = { style: BorderStyle.SINGLE, size: 4, color: "BFBFBF" };

// **CMD** -> bold monospace (what you type)   ^^x^^ -> bold   {{x}} -> italic
function runs(text, base = {}) {
  const out = [];
  const re = /(\*\*[^*]+\*\*|\^\^[^^]+\^\^|\{\{[^}]+\}\})/g;
  let last = 0, m;
  text = String(text);
  while ((m = re.exec(text))) {
    if (m.index > last) out.push(new TextRun({ text: text.slice(last, m.index), font: FONT, ...base }));
    const t = m[0];
    if (t.startsWith("**")) out.push(new TextRun({ text: t.slice(2, -2), font: MONO, bold: true, ...base, size: 20 }));
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
const H2 = (t, opts = {}) => new Paragraph({ heading: HeadingLevel.HEADING_2, children: [new TextRun({ text: t })], keepNext: true, ...opts });
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

function table(headers, rows, widths, opts = {}) {
  const total = widths.reduce((a, b) => a + b, 0);
  const mk = (cells, head) => new TableRow({
    tableHeader: head,
    children: cells.map((c, i) => new TableCell({
      width: { size: widths[i], type: WidthType.DXA }, borders: { top: thin, bottom: thin, left: thin, right: thin },
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
function beforeAfter(imgDir, ex, labels = ["Before (START)", "After (COMPLETED)"]) {
  const b = path.join(imgDir, `ex${ex}_before.png`), a = path.join(imgDir, `ex${ex}_after.png`);
  if (!fs.existsSync(b)) return [];
  const { w, h } = imgSize(b);
  const colW = CONTENT / 2;
  const widthIn = Math.min((colW - 200) / 1440, (2.3 * w) / h);
  const cell = (file, label) => new TableCell({
    width: { size: colW, type: WidthType.DXA }, borders: { top: none, bottom: none, left: none, right: none },
    children: [
      new Paragraph({ alignment: AlignmentType.CENTER, children: [image(file, widthIn)] }),
      new Paragraph({ alignment: AlignmentType.CENTER, spacing: { after: 120 },
        children: [new TextRun({ text: label, size: 17, italics: true, color: "595959", font: FONT })] }),
    ],
  });
  return [new Table({ width: { size: CONTENT, type: WidthType.DXA }, columnWidths: [colW, colW],
    rows: [new TableRow({ cantSplit: true, children: [cell(b, labels[0]), cell(a, labels[1])] })] })];
}

// exercises: [{part}] or [{n, t, why, steps[], check, tip, noImage}]
function exerciseBlocks(exercises, imgDir) {
  const body = [];
  let first = true;
  for (const e of exercises) {
    if (e.part) { body.push(H1(e.part, !!e.pageBreak)); first = true; continue; }
    body.push(new Paragraph({ heading: HeadingLevel.HEADING_2, keepNext: true, spacing: { before: first ? 120 : 360 },
      children: [new TextRun({ text: `${e.n}   ${e.t}` })] }));
    first = false;
    if (e.why) body.push(P(e.why, { run: { color: "404040" }, para: { keepNext: true } }));
    for (const s of e.steps) body.push(new Paragraph({ numbering: { reference: `steps-${e.n}`, level: 0 }, children: runs(s), spacing: { after: 60 }, keepNext: true }));
    body.push(new Paragraph({ spacing: { before: 80, after: 80 }, keepNext: true,
      children: [new TextRun({ text: "✓ Check:  ", bold: true, color: "2E7D32", font: FONT }), ...runs(e.check)] }));
    if (e.tip) { body.push(note("TIP", e.tip)); body.push(new Paragraph({ spacing: { after: 60 } })); }
    if (e.images) for (const id of e.images) body.push(...beforeAfter(imgDir, id));
    else if (!e.noImage) body.push(...beforeAfter(imgDir, e.n));
  }
  return body;
}

function numberingFor(exercises) {
  const cfg = [{ reference: "bullets", levels: [{ level: 0, format: LevelFormat.BULLET, text: "•", alignment: AlignmentType.LEFT,
    style: { paragraph: { indent: { left: 360, hanging: 260 } } } }] }];
  for (const e of exercises) if (e.n) cfg.push({ reference: `steps-${e.n}`, levels: [{ level: 0, format: LevelFormat.DECIMAL, text: "%1.",
    alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 400, hanging: 300 } } } }] });
  return cfg;
}

function cover({ level, title, intro, info, notation = true }) {
  const out = [
    new Paragraph({ spacing: { before: 600, after: 60 }, children: [new TextRun({ text: "PARAMETRIX  |  SURVEY", bold: true, size: 22, color: ACCENT, font: FONT })] }),
    new Paragraph({ spacing: { after: 60 }, children: [new TextRun({ text: "Survey CAD Training", bold: true, size: 56, font: FONT })] }),
    new Paragraph({ spacing: { after: 300 }, border: { bottom: { style: BorderStyle.SINGLE, size: 12, color: ACCENT, space: 6 } },
      children: [new TextRun({ text: /^\d/.test(String(level)) && !String(level).includes("-") ? `Level ${level} - ${title}` : title, size: 32, font: FONT, color: "404040" })] }),
    P(intro),
    table(["Item", ""], info, [2000, CONTENT - 2000]),
  ];
  if (notation) out.push(new Paragraph({ spacing: { before: 200 } }), table(["Notation", "Meaning"], [
    ["↵", "Press Enter (or Space)"],
    ["**TEXT IN THIS FONT**", "Type it exactly"],
    ["Pick", "Click in the drawing"],
    ["Endpoint, Node, Insertion ...", "Object snap to use (running snap, or Shift + right-click for a one-time snap)"],
    ["CTRL+1", "Properties palette"],
  ], [2600, CONTENT - 2600]));
  return out;
}

function buildGuide({ outFile, level, title, body, exercises }) {
  const doc = new Document({
    creator: "Parametrix Survey", title: `Survey CAD Training - Level ${level}`,
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
    numbering: { config: numberingFor(exercises || []) },
    sections: [{
      properties: { page: { size: { width: PAGE_W, height: 15840 }, margin: { top: 1000, bottom: 1000, left: MARGIN, right: MARGIN } } },
      footers: { default: new Footer({ children: [new Paragraph({ alignment: AlignmentType.RIGHT, children: [
        new TextRun({ text: String(level).includes("-") ? "Survey CAD Training - Finish the Drawing    " : `Survey CAD Training - Level ${level}    `, size: 16, color: "808080", font: FONT }),
        new TextRun({ children: [PageNumber.CURRENT], size: 16, color: "808080", font: FONT })] })] }) },
      children: body,
    }],
  });
  return Packer.toBuffer(doc).then((buf) => { fs.writeFileSync(outFile, buf); console.log("wrote", outFile); });
}

module.exports = { runs, P, H1, H2, bullet, note, table, figure, image, beforeAfter, exerciseBlocks, cover, buildGuide, CONTENT };
