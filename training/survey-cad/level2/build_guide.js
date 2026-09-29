// Level 2 guide. Run build_drawings.py and render_images.py first.
const fs = require("fs");
const path = require("path");
const G = require("../lib/guide");

const OUT = path.join(__dirname, "output");
const IMG = path.join(OUT, "images");
const A = JSON.parse(fs.readFileSync(path.join(OUT, "answers.json"), "utf8"));
const W = G.CONTENT;
const BASE = "SURVEY_CAD_L2_BASE";

const EX = [
  { part: "Part 1 - QA/QC the base", pageBreak: true },
  { n: "01", t: "Open the base and save it as a DWG", noImage: true,
    steps: [`**OPEN** ↵ ^^${BASE}_START.dxf^^. **SAVEAS** ↵ ^^${BASE}.dwg^^. Use exactly this name: the sheet xrefs it in Part 4.`,
            "**AUDIT** ↵ **Y** ↵. Then **-PURGE** ↵ **R** ↵ ↵ **N** ↵ to purge unused registered applications, which Civil 3D exports leave behind."],
    check: "AUDIT reports no errors left unfixed." },
  { n: "02", t: "Why QA before drafting", noImage: true,
    why: "Every base that comes out of processing has problems you can't see by looking: objects on the wrong layer, wrong text styles, duplicates, stray elevations. Exercises 03-06 find each one with a query instead of your eyes, so the count is exact.",
    steps: ["Keep a tally as you go. Each exercise tells you how many you should find."],
    check: "You know the four checks: layer 0, text style, duplicates, elevation." },
  { n: "03", t: "Find objects on layer 0 (QSELECT)", noImage: true,
    steps: ["**QSELECT** ↵. Apply to: ^^Entire drawing^^. Object type: ^^Multiple^^. Property: ^^Layer^^. Operator: ^^= Equals^^. Value: ^^0^^. OK.",
            "Look at what's selected (**CTRL+1**): they're all contour elevation labels.",
            "In Properties set Layer = ^^V-TOPO-CONT-TEXT-E^^. ESC. Run the same QSELECT again."],
    check: `The first QSELECT finds ^^${A.defect_layer0}^^ objects. The second finds none.` },
  { n: "04", t: "Find labels in the wrong text style",
    steps: ["**QSELECT** ↵. Object type: ^^MText^^. Property: ^^Style^^. = ^^Standard^^. OK.",
            "**CTRL+1**: Style = ^^Survey^^."],
    check: `^^${A.defect_style}^^ labels found. They switch from Arial to the romans Survey font.` },
  { n: "05", t: "Remove duplicates - controlled OVERKILL", noImage: true,
    why: "Processing sometimes draws the same line twice. It plots fine but doubles your edits and breaks JOIN. Never run OVERKILL on a whole base; limit it to one layer at a time.",
    steps: ["**LAYISO** ↵, pick a curb line ↵. Only V-SURF-CURB-E shows.",
            "**OVERKILL** ↵. Select everything shown ↵. In the dialog: Tolerance ^^0.000001^^. ^^Uncheck^^ {{Optimize segments within polylines}} and {{Combine co-linear objects}}. OK.",
            "**LAYUNISO** ↵."],
    check: `The command line reports ^^${A.defect_dupes} duplicate(s) deleted^^.` },
  { n: "06", t: "Find 2D linework floating at an elevation", noImage: true,
    why: "Plan linework should sit at elevation 0. Lines left at an elevation snap to the wrong Z and throw off distances. Contours are supposed to have elevations, so the search must exclude them.",
    steps: ["**QSELECT** ↵. Object type: ^^Polyline^^. Property: ^^Layer^^ = ^^V-SURF-CONC-E^^. OK.",
            "**QSELECT** ↵ again. Apply to: ^^Current selection^^. Property: ^^Elevation^^. Operator: ^^<> Not Equal^^. Value: ^^0^^. OK.",
            "**CTRL+1**: Elevation = ^^0^^."],
    check: `^^${A.defect_elev}^^ walk polylines found and set to 0.`,
    tip: "Don't use FLATTEN on a survey base. It also flattens the 3D breaklines the surface is built from." },
  { part: "Part 2 - Annotation standards" },
  { n: "07", t: "Make the Survey text annotative", noImage: true,
    why: "The base's text is 1.6' tall, which only works at 1\" = 20'. Annotative text is set to a paper height (0.08\") and AutoCAD sizes it for each scale.",
    steps: ["Status bar: set {{Annotation Scale}} to ^^1\" = 20'^^.",
            "**STYLE** ↵. Pick ^^Survey^^. Check ^^Annotative^^. Paper Text Height ^^0.08^^. Apply.",
            "**QSELECT** ↵. Multiple, Property ^^Style^^ = ^^Survey^^. **CTRL+1**: Annotative = ^^Yes^^."],
    check: "Nothing moves or changes size at 1\" = 20'." },
  { n: "08", t: "Add the 1\" = 40' scale", noImage: true,
    steps: ["Select all the annotative text (**QSELECT** Annotative = Yes).",
            "**OBJECTSCALE** ↵ ↵. Add ^^1\" = 40'^^. OK.",
            "Switch the status-bar annotation scale to 1\" = 40', then back to 1\" = 20'."],
    check: "At 1\" = 40' the labels double in model size (3.2') and still read 0.08\" on paper. The 1\" = 20' labels come back when you switch back." },
  { n: "09", t: "Annotative leaders and dimensions", noImage: true,
    steps: ["**MLEADERSTYLE** ↵. Modify ^^SRV-20^^: Leader Structure tab, check ^^Annotative^^.",
            "**DIMSTYLE** ↵. Modify ^^SRV-20^^: Fit tab, check ^^Annotative^^.",
            "Select the leaders and dimensions, **CTRL+1** Annotative = Yes, then **OBJECTSCALE** add 1\" = 40'."],
    check: "At 1\" = 40' the leaders and the two setback dimensions scale like the text." },
  { n: "10", t: "Linetype scale settings", noImage: true,
    steps: ["**LTSCALE** ↵ **1** ↵.   **PSLTSCALE** ↵ **1** ↵.   **MSLTSCALE** ↵ **1** ↵.   **REGEN** ↵.",
            "Look at the storm line (---SD---) at annotation scale 1\" = 20', then 1\" = 40'."],
    check: "The SD spacing looks the same on screen at both scales. Linetypes now follow the annotation scale." },
  { part: "Part 3 - Blocks with attributes" },
  { n: "11", t: "Build an attributed control-point block",
    why: "A block with attributes carries data: point number, coordinates, elevation. That data can fill a table automatically.",
    steps: ["In an empty spot, set layer ^^0^^ current. **POLYGON** ↵ **3** ↵ pick a center, **I** ↵ **1.38** ↵. **C** ↵ (CIRCLE) at the same center, radius **0.3** ↵.",
            "**ATTDEF** ↵ five times, all with ^^Invisible^^ checked, height 1.6, style Survey: tags ^^PT^^, ^^DESC^^, ^^NORTHING^^, ^^EASTING^^, ^^ELEV^^.",
            "**B** ↵ (BLOCK). Name ^^CTRL-PT^^. Base point: the center. Select the triangle, circle and 5 attributes. ^^Delete^^ the originals. OK.",
            "Replace the control symbols: for PMX #2000-#2003, **I** ↵ CTRL-PT with ^^Insertion^^ snap on the old symbol, fill in the attributes from the point list, then erase the old symbol. Put the blocks on V-CTRL-PMX_-SYMB-E."],
    check: "**EATTEDIT** on each block shows its point number, N, E, elevation and description." },
  { part: "Part 4 - Xrefs and sheets" },
  { n: "12", t: "Xref the base into the sheet", noImage: true,
    steps: ["**QSAVE** the base. **OPEN** ^^SURVEY_CAD_L2_SHEET_START.dxf^^ and **SAVEAS** ^^SURVEY_CAD_L2_SHEET.dwg^^ in the ^^same folder^^.",
            "Model tab. Layer ^^G-XREF^^ (create it). **XATTACH** ↵ pick ^^" + BASE + ".dwg^^. Reference type: ^^Overlay^^. Path: ^^Relative^^. Insertion **0,0**, scale **1**, rotation **0**. Uncheck {{Specify On-screen}}.",
            "**ID** ↵ on PMX #2001."],
    check: "ID reads the same coordinates as in the base. The xref sits at 0,0 on the state plane grid, so every coordinate matches." },
  { n: "13", t: "Overall viewport at 1\" = 40'", noImage: true,
    steps: ["^^SHEET^^ layout. Layer ^^G-ANNO-VPRT^^ current. **MV** ↵ pick the OVERALL box.",
            "Double-click in. **Z** ↵ **1/40XP** ↵. Pan to center the site. Set the viewport's annotation scale to 1\" = 40'. Double-click out, **CTRL+1**, {{Display locked}} = Yes."],
    check: "Labels read 0.08\" on paper, because you added the 1\" = 40' scale in Exercise 08." },
  { n: "14", t: "House detail at 1\" = 10' with viewport freezes", noImage: true,
    steps: ["**MV** ↵ pick the DETAIL box. Inside: **Z** ↵ **1/10XP** ↵, center on the house, lock it.",
            "With the detail viewport active, **LA** ↵ and click ^^VP Freeze^^ for the xref's contour layers (^^" + BASE + "|V-TOPO-CONT-*^^).",
            "In the base, add scale 1\" = 10' to the house, garage and FFE labels (**OBJECTSCALE**), **QSAVE**, then in the sheet **XREF** palette > Reload."],
    check: "Contours show in the overall view but not in the detail. The house labels show at the right size in both." },
  { n: "15", t: "Control table from the block attributes (DATAEXTRACTION)", noImage: true,
    steps: ["**DX** ↵. Create a new extraction. Add drawings: ^^" + BASE + ".dwg^^.",
            "Objects: ^^CTRL-PT^^ only. Properties: the five attributes only.",
            "Output: ^^AutoCAD table^^. Insert it in the CONTROL TABLE box on the sheet. Title {{SURVEY CONTROL}}."],
    check: "Four rows. The values match the Answer Key." },
  { n: "16", t: "Layer states", noImage: true,
    steps: ["**LAYERSTATE** ↵. New: ^^PLOT^^ with TRAIN-NOTES frozen. New: ^^WORKING^^ with TRAIN-NOTES thawed.",
            "Restore each one and watch the drawing change."],
    check: "Restoring PLOT hides every training object." },
  { n: "17", t: "Plot and wrap up", noImage: true,
    steps: ["Restore layer state ^^PLOT^^. **PLOT** ↵: DWG To PDF, ANSI expand D, 1:1, monochrome.ctb.",
            "**QSAVE** both files. Compare with the COMPLETED files."],
    check: "Both viewports and the table plot. Text reads 0.08\" in both viewports." },
];

const body = [];
body.push(...G.cover({ level: 2, title: "Production Drafting",
  intro: "Level 2 takes the finished Level 1 base and makes it production ready. You QA it with queries, move it onto annotative standards, build an attributed control block, and set up a separate sheet that xrefs the base with two scales and a control table.",
  info: [["Start files", `${BASE}_START.dxf, SURVEY_CAD_L2_SHEET_START.dxf`],
         ["Answer key", `${BASE}_COMPLETED.dxf, SURVEY_CAD_L2_SHEET_COMPLETED.dxf (xrefs ${BASE}_COMPLETED.dwg)`],
         ["Before you start", "Finish Level 1"], ["Time", "About 3 hours"],
         ["Scales", "Overall 1\" = 40', house detail 1\" = 10'"]] }));
body.push(G.P(""));
body.push(G.note("NOTE", "Some Level 2 results live in settings a DXF can't carry from here: annotation scale lists on objects, layer states and viewport layer freezes. For those, use each exercise's Check and the Answer Key. To open the COMPLETED sheet, first save the COMPLETED base as " + BASE + "_COMPLETED.dwg in the same folder."));
body.push(new (require("docx").Paragraph)({ spacing: { after: 200 } }));
body.push(...G.figure(path.join(IMG, "completed_sheet.png"), 6.9, "The finished Level 2 sheet (preview): overall at 1\" = 40', house detail at 1\" = 10', control table"));
body.push(...G.exerciseBlocks(EX, IMG));

body.push(G.H1("Answer key"));
body.push(G.table(["Ex", "Result"], [
  ["03", `${A.defect_layer0} contour labels on layer 0`],
  ["04", `${A.defect_style} labels in style Standard`],
  ["05", `${A.defect_dupes} duplicate curb polylines deleted`],
  ["06", `${A.defect_elev} walk polylines at an elevation`],
  ["13 / 14", `Overall 1" = ${A.overall_scale}'; detail 1" = ${A.detail_scale}'`],
], [1000, W - 1000]));
body.push(G.H2("Survey control table"));
body.push(G.table(["Point", "Northing", "Easting", "Elev", "Description"], A.control, [1500, 1900, 1900, 1100, W - 6400]));
body.push(G.H1("Commands used"));
body.push(G.table(["Command", "Used for"], [
  ["QSELECT", "Find objects by property"], ["OVERKILL", "Delete duplicates (controlled)"], ["-PURGE R / AUDIT", "Clean exports"],
  ["STYLE / MLEADERSTYLE / DIMSTYLE", "Annotative standards"], ["OBJECTSCALE", "Add annotation scales"],
  ["LTSCALE / PSLTSCALE / MSLTSCALE", "Linetype scaling"], ["ATTDEF / BLOCK / EATTEDIT", "Attributed blocks"],
  ["XATTACH / XREF", "Xref the base"], ["MVIEW / VP Freeze", "Scaled viewports"], ["DATAEXTRACTION", "Tables from attributes"],
  ["LAYERSTATE", "Saved layer setups"],
], [4200, W - 4200]));

G.buildGuide({ outFile: path.join(OUT, "SURVEY_CAD_L2_Guide.docx"), level: 2, title: "Production Drafting", body, exercises: EX });
