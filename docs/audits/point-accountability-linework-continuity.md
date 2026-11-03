# Audit: point accountability and linework continuity

- **Commit audited:** `cab20cd` on `claude/friendly-curie-0inutv`. The branch head is `7ec7e25`, which differs only by `docs/what-ftf-does.md`.
- **Date:** 2026-11-03.
- **Scope:** a read-only audit. No code was changed.
- **References:** paths are relative to the repository root. Line numbers are at `cab20cd`.
- **Verification status:** everything in `src/FieldCodes.Cad` compiles against the real AutoCAD 2024 API. Except where noted, it has not run in Civil 3D.

---

## 0. Summary

| Requirement | Today | Verdict |
|---|---|---|
| 1. Every point has a traceable disposition | Points are classified by **code**, not by **result**. Linework, bare, ignored and recognized codes are treated as fine without checking that anything exists in the drawing. Nothing answers "what did point N produce" or "which points produced nothing". | **Missing.** Some building blocks exist. |
| 2. Interrupted linework | No detection at all. Figure numbers are stripped everywhere, the vertex-to-shot index stores descriptions only, and no linework control code (B/E/C...) is parsed. | **Missing.** Reusable geometry exists. |
| 3. FTFREVIEW / FTFQC integration | The review list, QC Complete gate, zoom/fix/N-A/accept, and stable ids all exist. Missing: a check that runs after FTFRUN, review graphics for source points and proposals, an "approve correction" action, and invalidation of a decision when its source changes. | **Partial.** |
| 4a. Dynamic symbol and linetype legend | Linetypes only, built from what is in the drawing. Point symbols are excluded on purpose. | **Partial.** |
| 4b. Monument descriptions and observation dates | Descriptions are the raw text or typed by hand. No dates anywhere. | **Partial / missing.** |
| 4c. Section/township/range from coordinates | Nothing. STR is typed by hand on exhibits. | **Missing.** |
| 4d. Automatic title block population | Exhibits only, from per-exhibit typed info. Plan sheets have none. There is no project record. | **Partial.** |
| 4e. Asymmetric ROW offsets and annotation | Only in centerline mode. No half-width dimensions. | **Partial.** |
| 4f. Independence from Survey Hub | No reference, dependency or network code anywhere. | **OK.** |
| 5. Real Civil 3D tests | One blocking headless smoke test (FTFVERSION and FTFCONTOUR). No harness creates survey figures. | **Missing** for this scope. |

Problems found along the way (section 7):
- FTFPOINTS counts a point whose block is missing twice.
- The compare smoke harness counts the wrong ownership kinds.
- Project data is committed under `tests/`.
- A personal path is committed in a test script.

---

## 1. How processing, review and tracking work today

### 1.1 FTFRUN

- **Entry point:** `FtfCommand.RunAll` (`src/FieldCodes.Cad/FtfCommand.cs:78-155`).
  1. It reads settings and asks for a preset: Standard, Trees, Annotations, All, or a saved workflow.
  2. It builds a plan (`ProcessPresets.Build`, `src/FieldCodes/Annotation/ProcessPresets.cs:119-231`).
  3. It runs `FtfPipeline.Run` (`src/FieldCodes.Cad/FtfPipeline.cs:149-198`).
  4. It records the run with `FtfStatusService.RecordRun` (`FtfCommand.cs:146`; `FtfStatusService.cs:164-213`).
- **Stages, in order** (`FtfPipeline.cs:62-131`):
  1. FTFPOINTS
  2. FTFORIENT
  3. FTFDRIP
  4. FTFLINELABELS (off by default, and then a silent success)
  5. FTFLABELS
  6. FTFTAGS
  7. FTFTABLE
  8. FTFORDER
- **How each stage runs:** every stage goes through `FtfSession.Run` (`FtfSession.cs:184-204`). That gives one transaction per stage, rolled back on error. A failure adds an `error.command` review item; a success clears it.
- **The run record** is `ProcessingRecord`, stored as `LASTRUN` (`DrawingStore.cs:301-320`). Its counts come from re-classifying codes (`FtfStatusService.Gather`), not from what the stages produced. Each run replaces the last; there is no history.
- **Places to hook a "checks after the run" step:**
  - after `pipeline.Run`, before `RecordRun` (`FtfCommand.cs:143-146`);
  - the window's `FtfMainForm.RunPlan` (`Ui/FtfMainForm.cs:1082`) and `RunStages` (`:2839`), which both reach `RecordSessionRun` (`:2891`).
  - FTFLABELALL (`LabelAllCommand.cs:63-136`) uses neither the pipeline nor `RecordRun`, so it needs its own call.

### 1.2 What is decided about each point

**Parser** (`FieldCodeParser.Parse`, `src/FieldCodes/FieldCodeParser.cs:49-152`; flags in `Models.cs:70-221`):

| Outcome | How | Treated as |
|---|---|---|
| Blank | Error `EMPTY` (`:57-61`) | Error, reported |
| `Ignored` | ignore list matched **before** rules (`:71-76`); or linework code / user-ignored code after no rule matched (`:94-102`) | "Silent by design" (`Models.cs:95-99`) |
| `NoContent` | bare code with no data and not in `symbolLabels` (`:104-126`) | "Nothing to draw and nothing to report... Silent by design" (`Models.cs:101-110`) |
| `Unhandled` | data, but no rule matched (`:132-135`) | Unhandled summary |
| Recognized | rule matched; then resolved and checked (`:138-149`) | Processed |

**Classifiers built on the parser:**
- **`CodeDiagnostics.Classify`** (`Review/CodeDiagnostics.cs:235-326`) sorts every point into Error / NeedsConfiguration / Informational / None.
  - Linework, rule-ignored, user-ignored and bare codes are all **Informational, "Nothing to do"** (`:248-273`).
  - A data-carrying `Unhandled` point whose code is a line feature is also reclassified as Linework (`:279`).
  - "Processed" means "a rule matched". The text is "finishing is planned" (`:316-325`), with no check of output.
- **`FeatureReviewBuilder.Build`** (`Review/FeatureReview.cs:130-188`, the dry run) maps those to OutsideScope / NothingToDo / NotConfigured / Error / Ok.
  - It computes **planned outputs** per point with `PlannedActions` (`:203-236`): block, rotation, dripline, label or leader, tag or table.
  - It is in memory only and never compared with what was drawn.
- **`AnnotationPlan.Build`** (`Annotation/LabelPlan.cs:54-139`) gives per-point Create / Update / Skip / Flag with a reason, as a preview. It skips Ignored, NoContent and Unhandled (`:73`).

**Files written beside the drawing:**

| File | Written by | Contents |
|---|---|---|
| `<dwg>.ftf-exceptions.csv` | FTFPOINTS only | errors, warnings, missing blocks |
| `<dwg>.ftf-unhandled.csv` | FTFPOINTS only | one row per code |
| `<dwg>.ftf-orientation.csv` | FTFORIENT | one row per decision |
| `<dwg>.ftf-linework.csv` | FTFLINES | no handles, no coordinates |

**What reaches the review list** (`ReviewStore.CheckCodes`, `ReviewCommands.cs:78-122`, through FTFREVIEWCHECK only):
- unknown codes (one item per code)
- unreadable descriptions and invalid values (one item per point)
- unrecognised words after a linework code

`ReviewChecks.Codes` drops everything else (`Review/ReviewChecks.cs:55`: `if (issue.Category != CodeIssueCategory.Error) continue;`).

**Every place a bare or linework code currently counts as fine:**
- `FieldCodeParser.cs:67-126`
- `TreeCommands.cs:68` (`Ignored || NoContent` → `ignored++; continue`) and `:127`
- `CodeDiagnostics.cs:248-273, 279`, `SkippedIntentionally` (`:159-163`)
- `FeatureReview.cs:142-162, 184-185`
- `LabelPlan.cs:73`
- `ReviewChecks.cs:55`
- `FtfStatusService.cs:28-29`
- `ProcessingRecord.cs:63, 141-144`
- the Overview's "Skipped on purpose" (`FtfMainForm.cs:2709-2715`)

### 1.3 Tracing output back to points

Every FTF object carries a stamp (`Ownership.cs:250-307`). Its "point number" slot holds:

| Output | Slot holds | Where |
|---|---|---|
| Block, dripline, label, mask, tag | the real point number | `TreeCommands.cs:354`, `DripCommands.cs:168,183`, `LabelCommands.cs:174,186,378`, `MaskSync.cs:74`, `TagCommands.cs:152,162` |
| FTFORIENT QC graphics | the point number | `OrientationCommands.cs:350-367` |
| FTFORIENT rotation record | `FTF_ORIENT` XData on the COGO point itself | `OrientationCommands.cs:297-326`; written only when a marker turns |
| Bench | `"<low>-<high>"` point numbers | `BenchCommands.cs:133...` |
| Line labels | the **feature code**, not a point | `LineworkCommands.cs:294-295` |
| Gate labels | the fence code; the posts are in the tag `gate:A-B` | `LineworkCommands.Gates.cs:26-31` |
| Control table, legend, tables, spots | `"control"`, `"legend"`, `""`, the elevation | not per point |
| Utility pipes and labels | a GUID; the dip project maps it to the point | `Utilities/UtilityModel.cs:187,216-224` |

- **No query exists for "which objects did point N produce" or "which points produced nothing".** The closest joins are:
  - `AnnotationScan.Read` (sets of labelled and tagged point numbers, `AnnotationScan.cs:50-54`);
  - `InsertedBlocks.FollowMarkers` (`InsertedBlocks.cs:71-79`).
- The tag register (`DrawingStore.cs:254-278`) means "ever tagged", not "tagged now".
- FTF never writes a point's description, location or user-defined properties. FTFPOINTS and FTFORIENT write only `MarkerRotation` and `LabelRotation`.
- `ParsedPoint.RawDescription` can hold the description combined with the note (`FieldCodeParser.cs:175,192`), so some reports show combined text rather than the stored raw description.

### 1.4 Surfaces, control, ignore lists, duplicates

- **Surfaces:**
  - FTFSPOT uses `TinSurface.FindElevationAtXY`.
  - FTFCONTOUR uses `CivilDocument.GetSurfaceIds` and `TinSurface.GetTriangles(false)` (`ContourCommands.cs:598-617, 686-700`). This has run in the Civil 3D smoke test.
  - No surface definition, point group or point file is read anywhere. **"Was this point used in a surface" is never asked.**
- **Control:**
  - `controlCodes` (`config/rules.json:84-92`) is used only by FTFCONTROL (first-token match, `SpotCommands.cs:232-346`).
  - The parser and classifiers do not know about control codes. A bare `FIP` is "Bare code, nothing to do", and `FIP 5/8` with no rule is "Needs configuration".
- **Ignore lists:**
  - `ignoreCodes`, `lineworkCodes`, the user's ignored codes (`Settings/DiagnosticsSettings.cs:31-32, 157-193`) and `ignoreLineNames` are **plain string lists**.
  - None records a reason, a person or a date.
  - By contrast, a review item marked not applicable requires a reason (`ReviewRegister.MarkNotApplicable`).
- **Duplicates:** nothing detects duplicate point locations. Civil 3D itself enforces unique point numbers.

### 1.5 Linework identification

- **`LineworkCatalog`** (`src/FieldCodes/Linework/LineworkReview.cs`) identifies a line by figure name, then Trimble name, then layer.
  - **Figure numbers are thrown away:** `FindByFigureName` calls `StripTrailingDigits` (`:151, :439`), so FNC1, FNC2 and FNC are one code.
  - `FindInDescription` (`:195-212`) strips digits too.
- **`LineworkCommands.StagedCandidates`** (`src/FieldCodes.Cad/LineworkCommands.cs:1331-1363`) adds **point notes**: the shots the line was drawn through, matched by `NotesAtVertices` → `BuildNoteIndex` (`:1471-1511`). The match key is `Bucket` (`:1692`), X and Y rounded to 0.01.
  - The index holds **description strings only**: no point number, handle or Z.
  - Only `HeightReviewItems` (`:1597-1620`) builds a second index (bucket → COGO point) to recover identity.
  - Neighbouring buckets are never checked, so a shot and a vertex 0.001 apart on either side of a rounding boundary miss each other.
  - Lines, arcs and splines contribute only their two ends.
- **`FtfLineworkService.Scan`** (FTFLINES, the legend, the Lines page) does **not** use point notes. It can therefore identify a line differently from the line label pass and the gate pass.
- **Civil 3D Survey API:** none. `Autodesk.Civil.Survey` is never referenced; there is no survey database, figure prefix database or figure-to-point reference.
  - On `SurveyFigure` and `FeatureLine`, the only members used are a type test, `.Name`, `GetPoints(FeatureLinePointType.AllPoints)` and inherited `Curve` members.
  - None of these has run in Civil 3D.
- **Linework control codes:** `rules.json:17` says the PMX codes (B U E C P T G RT X CIR OC HO VO SO RPN CPN) are "Recorded for reference; NOT parsed here".
  - `LineHeights.LineCodes` / `ParameterCodes` (`Linework/LineHeights.cs:33-42`) are skip lists for reading heights, not a parser.
  - Tokens that collide: `EC` (a line command and the edge-of-concrete code), `R` (a parameter code and RIGHT), `L` (LEFT).
- **Real data (the reference drawing, kept outside the repo):**
  - Linework is 2D polylines only: no survey figures, feature lines or surfaces.
  - The crew interleaves strings. In extraction order the shots run `FNC1 B` … (many other codes) … `FNC1 E`, then `FNC1 B` again 4 shots later.
  - Fence heights there are in the **field notes** (`6.0 WOOD`, `6.5 TALL`), not the description.
  - So continuity checking **must work on plain polylines matched to their shots**, not only on figures.

### 1.6 The review list and QC (FTFREVIEW, FTFQC)

- **`ReviewRegister`** (`src/FieldCodes/Review/ReviewRegister.cs`) holds the items; it is stored in the drawing under key `REVIEW` (`DrawingStore.cs:282-299`).
  - Id = kind + subject (`ReviewKinds.New`).
  - `Report(source, items, complete)` (`:132`) opens new items, keeps not-applicable and accepted decisions, and resolves items no longer found.
  - `MarkNotApplicable` requires a reason (`:208`); `Accept` records who and when (`:222`).
  - `MarkQcComplete` is refused while required items are open (`:262`).
- **Catalog of kinds and their required flag:** `ReviewKinds` (`Review/ReviewKinds.cs`). Settings are on Settings > Review and QC.
- **The window:** `Ui/ReviewForm.cs` — Zoom (`:222`; zooms and selects the stored handles), Fix (`:230`), Its settings (`:243`), Recheck (`:255`), Not applicable (`:283`), Accept (`:296`), Reopen (`:303`).
- **What feeds it today:**
  - gates (`LineworkCommands.Gates.cs:330`)
  - FTFORIENT (`OrientationCommands.cs:136-137`)
  - the line label pass: fence heights and unidentified lines (`LineworkCommands.cs:317-319`)
  - FTFREVIEWCHECK: codes
  - every `FtfSession.Run`: command errors

**Gaps against requirement 3:**
1. **Nothing runs after FTFRUN.** FTFREVIEWCHECK is manual. Gates run only in the line label pass (off by default) or Label All.
2. **An item has only a point (X, Y) and up to 50 handles.** There is no geometry: no proposed connector, no source-point overlay, and the evidence is a single reason string. Zoom selects the handles; it draws nothing.
3. **There is no "approve correction" action.** Accept means "accepted as drawn".
4. **Decisions are not invalidated when the source changes.** A not-applicable decision on an item persists even if the geometry behind it changes.
   - FTFCOMPARE already solves this: `ReviewDecisionSet.ApplyTo` (`Compare/CompareResults.cs:279-350`) re-applies a decision only when `CompareFeature.Fingerprint` (`Compare/CompareModel.cs:306`) is unchanged, and reports it as stale otherwise. **Reuse that pattern.**
5. **Group ids are unstable for per-point decisions.** `code.unknown` is one item per code, and `gate.ambiguous` is keyed by its member list (`Linework/Gates.cs:312`). A per-point disposition needs per-point ids, while the window groups them for review.

### 1.7 Reusable pieces

| Need | Reuse |
|---|---|
| Shot ↔ vertex identity | `Bucket` + `BuildNoteIndex` + the bucket→COGO map in `HeightReviewItems` (unify into one index that carries point number, handle and raw description) |
| Feature identity | `StagedCandidates`, `LineworkCatalog`, `PointNotes.Consensus`, `LineHeights.TokensAfter` / `CodeOf` |
| Ends, gaps, alignment | `GateFinder.Closest` / `FencesAt` (`Linework/Gates.cs:550-590`); `CurveChain` Graph / NearMiss / Nearest / Continues (`Easements/CurveChain.cs`); `Geometry2` (`Compare/Geometry2.cs`); `SeamMatcher.Check` (`Compare/Trim.cs:399-475`, "does a line carry on where another stops"); `LinearMatcher.RunsStraight` (`Compare/LinearMatcher.cs:371`); `OrientLine` ends and directions; `SpatialGrid` |
| Never bridging a gate | `GateFinder` results (a gate is exactly two fence ends with FGP posts) |
| Planned outputs per point | `FeatureReviewBuilder.PlannedActions`, `AnnotationPlan.Build` |
| Surface membership | `TinSurface.GetTriangles(false)` vertices, as FTFCONTOUR reads them |
| Point style draws a marker | `CogoPointPlotting.Resolve` (`Compare/CogoPointPlotting.cs:39-76`; `GetDisplayStylePlan(Marker).Visible` and point-group overrides) |
| Non-plotted review graphics | FTFORIENT `DrawQc` / `NonPlotLayer` (`OrientationCommands.cs:333-460`) |
| Transient preview | `CompareHighlighter` (`Compare/CompareHighlighter.cs`) |
| Manual work survives reruns | `WasMovedByHand`, `ContentsHashOf`, `IsUserCopy`, the `manual` / `gatefix:` tags, `ReviewDecisionSet` + fingerprint |
| Next free ownership kind | **53** (`Ownership.cs:152`, OrientQc = 52; numbers 35-38 must never be reused) |

---

## 2. Proposed design: point accountability

### 2.1 Disposition, with evidence

Each point gets **one disposition and the evidence behind it**. More than one kind of evidence may apply; all are kept, and the strongest names the disposition.

| Disposition | Evidence required (checked in the drawing, never assumed from the code) |
|---|---|
| **Drawn** | FTF objects stamped with this point number: label, tag, block, dripline, bench, gate label (post in the tag), orientation record. Their handles are listed. |
| **Linework** | The point is a vertex of a line identified as **its own** feature (same code or alias; figure number checked where it survives). The line's handle and figure name are listed. |
| **Symbol** | Civil 3D's point style draws a visible marker for it (`CogoPointPlotting.Resolve`, including point-group overrides). The style name is listed. |
| **Surface** | A TIN vertex coincides with the point (XY 0.01, Z 0.01, from `GetTriangles`). The surface name is listed. |
| **Control** | The code is in `controlCodes`. The control table entry is listed when there is one. |
| **Excluded** | The code is on an ignore list **that gives a reason**, or a technician excluded the point in FTFREVIEW (reason, who, when). |
| **Review** | None of the above, or only partly. |

Review sub-kinds, each with a stable id `kind:<point number>`:

| Kind | Meaning |
|---|---|
| `point.unknowncode` | No rule and no exclusion. |
| `point.error` | The description cannot be read. |
| `point.missingoutput` | A planned output (`PlannedActions` / `AnnotationPlan`) does not exist: a label expected but not drawn, a block missing. Exclusions in the annotation policy count as "not expected". |
| `point.missinglinework` | A linework code that is on no line of its code. |
| `point.nosymbol` | A bare code whose point style draws nothing. |
| `point.duplicate` | Another point within 0.01 ft in XY. Both are listed with their codes. |
| `point.excludednoreason` | On an ignore list that gives no reason. |
| `point.sourcechanged` | Its description or location changed since it was last audited (information). |

**Never treated as accounted for without evidence:**
- unknown codes (Review until there is a rule or a documented exclusion);
- bare codes (need Symbol);
- linework codes (need Linework);
- recognized codes (need Drawn, or Symbol when the rule plans no output).

### 2.2 The point ledger, stored in the drawing

- **Model:** `src/FieldCodes/Review/PointLedger.cs` (new, library, unit-tested). For each point number:
  - handle;
  - raw description **as stored** (taken from `CogoPoint.RawDescription`, not the combined parser text);
  - XYZ;
  - disposition and evidence;
  - first and last audited.
- **Storage:** a new `DrawingStore` key `ACCOUNT` (JSON, about 1,100 rows for the reference drawing).
- **Snapshots** let FTF show a later edit to a point (`point.sourcechanged`) and keep the text as the crew shot it.
- **Collector:** CAD, one read-only pass. Points, parse results, stamps grouped by point number, the line-vertex identity index, TIN vertex sets, the control list, ignore reasons and register decisions.
- **Counts:** shown on the Overview and in `LASTRUN`, for example "1,115 points: 1,080 accounted for (by disposition), 20 excluded with reasons, 15 for review". These replace "Skipped on purpose".
- **Ignore lists get reasons** without breaking old files:
  - `ignoreCodes` accepts `"GS"` or `{ "code": "GS", "reason": "ground shot, surface only" }`;
  - the user's ignored codes keep reason, person and date;
  - a code without a reason produces `point.excludednoreason` (configurable as required or not).

### 2.3 Review integration

- **Source** "Points".
- **The window** groups items by kind and code: one row per group, expandable to points. Not applicable can be set **in bulk** on a selection with one reason.
- **Decisions are stored with a fingerprint** of the point (raw description + XYZ). If the point changes later, the decision reopens with "source changed since your decision".

---

## 3. Proposed design: linework continuity

### 3.1 Inputs (one read-only collector)

- **Lines:** every identified line (figure, feature line, 2D or 3D polyline). For each: handle, feature code, figure name **with its number** where it survives, closed or not, and plan vertices. Each vertex carries **the shot at it** (point number, raw description, handle) from the unified index.
- **Shots:** every linework-coded point, including shots on no line.
- **Linework codes per shot:** a new parser `LineworkCoding.Parse` (library), configured from the PMX control codes.
  - It splits chained descriptions (`TOP B TOP1 B`, `BLD B EC B BLD1 B`) into (figure + number, controls).
  - It resolves the `EC` / `R` / `L` collisions by position: a command follows a figure code; a feature code starts a new figure.

### 3.2 Candidates and evidence

**Candidates:**
- an open end of line A paired with an open end of line B of the same feature family, within a search distance;
- an open end and a same-family shot that is on no line.

**Evidence is recorded for every candidate. No single feature decides.**

| Evidence | For a missing connection | Against (intentional gap) |
|---|---|---|
| String identity | Same figure id (FNC1 = FNC1) | Different numbers (FNC1 vs FNC2 are parallel strings). Different family rejects outright. |
| Field coding | A's end shot not coded E; B's start shot coded B (the crew re-began) | A's end coded E (explicit end); a note such as GAP, OPEN, GATE |
| Sequence | A's last and B's first shots close in point order; the shots between are **other codes** (the unrelated feature), which are listed | Long sequence gap; the same family shot on another string in between |
| Gap | Short compared with the strings' median shot spacing | Over the limit for the feature |
| Alignment | A's end direction, the gap and B's start direction agree (deflection and lateral offset small); Z continuous on 3D | Reversal or a sharp turn |
| Crossings | Nothing crosses the gap | A driveway, road edge, building or wall crosses it; gate posts at both ends (`GateFinder`) |

**Never inferred:**
- Consecutive point numbers with matching codes are **never** enough. Sequence counts only together with identity and geometry.
- A gate opening is never proposed as a missing connection.

**Result classes:**
- **Probable** — an item with a proposed connector.
- **Possible** — an item with the evidence, no proposal.
- **Intentional** — informational, with the reason ("gate posts at both ends", "explicit E and driveway crossing").

Kinds and ids:
- `line.gap` (required by default);
- `line.stray` (a linework shot on no line, linked to its point item);
- id `line.gap:<A handle>:<start|end>|<B handle>:<start|end>`.

### 3.3 Proposals and approved corrections

- **Proposals:**
  - a new ownership kind **53 `LineGapProposal`**: a dashed connector plus a short note ("FNC 103→105, 8.0', re-begun after 104 PP");
  - on a non-plotted QC layer, following the FTFORIENT QC pattern;
  - redrawn on every run.
- **Approve correction** (FTFREVIEW):
  - creates kind **54 `LineCorrection`**: a separate connector on the feature's layer and linetype, with Z kept for 3D;
  - tagged `linefix:<key>` and carrying a **fingerprint** of both ends (handles, end coordinates, point numbers);
  - the item resolves, recording who and when;
  - **the original figures and polylines are never edited, exploded or joined.**
- **Reruns:**
  - a correction is never erased by a rerun or FTFCLEAN (kinds of 19 and above are already outside FTFCLEAN, `TreeCommands.cs:199-201`);
  - if its fingerprint no longer matches — the figure was rebuilt, or the gap now closes by itself — it is reported "check: source changed" and is not deleted;
  - a correction moved or edited by hand is kept (the `WasMovedByHand` / `ContentsHash` patterns).
- **No correction needed:** not applicable with a reason. It is stored with the candidate's fingerprint, so it reopens only if the ends change.
- **Correcting the source instead:** the item always suggests it (for example, a CPN / continue code in the field book). FTF records the drafting correction; it does not touch the survey database.

### 3.4 Settings

Per feature family, with defaults:
- search distance;
- sequence window;
- maximum deflection;
- whether a crossing feature implies intentional.

Fences, curbs, flowlines, pavement edges and walls each get a row.

---

## 4. Running the checks after FTFRUN

- **One shared step** (`PostRunChecks`, CAD) runs:
  1. the code check (FTFREVIEWCHECK's body);
  2. point accountability;
  3. linework continuity;
  4. gates when enabled.
- **Called from:**
  - `FtfCommand.RunAll` after `pipeline.Run` (`FtfCommand.cs:143-146`);
  - the window's `RunPlan` / `RunStages`;
  - the end of FTFLABELALL.
- It also runs after a run that stopped early, and the summary says so.
- **It writes only:** the review list, the point ledger and QC graphics (kind 53). It never writes points or linework.
- **Undo:** it runs inside the same undo group as the run, so one `U` undoes both.
- **Summary at the end of FTFRUN:** "Accounted for 1,080 / 1,115; 15 for review; 2 possible missing connections (FTFREVIEW)".

---

## 5. The other capabilities (requirement 4)

| Capability | Exists | Missing | Extend, don't duplicate |
|---|---|---|---|
| **Legend** | FTFLEGEND (`SpotCommands.FtfLegend`, `:446-563`; `Linework/LegendBuilder.cs:21-45`): one sample line per feature present, ByLayer, refreshed in place (kind Legend). Exhibit legend is separate (easements only). | Point symbols (excluded on purpose, `SpotCommands.cs:452-453, 559-561`); linetype fidelity (figure-style linetypes, sample length for lettered types); legend settings (title, columns, order, exclusions, extra rows); no unit test of `LegendBuilder`. | Add a symbols part to `LegendBuilder` (codes present → block or point-style marker → meaning from `CodeRule.Description` / orientation `SymbolRule.Meaning`), a Legend settings section, and the same refresh path. |
| **Monument descriptions** | FTFCONTROL (`SpotCommands.cs:225-444`): PT, N, E, ELEV, DESCRIPTION (raw, verbatim) + CSV with lat/long. ROW monument notes are typed (`RowCommands.cs:179-184`). The record survey classifies OCR monument text but does not label it. | A code → monument vocabulary (FIP → FOUND IRON PIPE, size, cap/LS); status and description columns; pre-filling ROW and record notes from the point. | `controlCodes` entries become objects with description and status (backward compatible); FTFCONTROL columns; RowForm pre-fill. |
| **Observation dates** | None on points. A typed date exists only for dip field notes (`FieldNotesModel.cs:23-53`). | Any source. | Options to decide: a user-defined property (readable today through `CogoNotes` reflection), the survey database (API not verified), or a per-point override. |
| **Section/township/range** | Typed only (exhibit Location field, `ExhibitCommands.cs:228`). | Coordinate system read, PLSS data, aliquot lookup, formatting, feeding exhibits and title blocks. | A project-level STR field now; an optional **offline** PLSS lookup later (a BLM CadNSDI extract for the office's counties). No network service, so it stays independent of Survey Hub. |
| **Title blocks** | Exhibits: `InsertSheetBlock` (`ExhibitCommands.cs:605-666`) with attribute map and guards (`ExhibitSettings.cs:526-570`), keeps hand edits. | Plan sheets (FTFSHEETMAKE makes a layout and viewport only); a drawing-level project record; sheet x of y; `TitleBlock()` (`ExhibitCommands.cs:1491-1542`) ignores the attribute map and guards (inconsistent). | A `PROJECT` record in `DrawingStore` + a Project page; FTFSHEETMAKE uses the same `InsertSheetBlock` path; fix `TitleBlock()`. |
| **Asymmetric ROW** | Centerline runs take separate left and right widths (`RowBuilder.cs:155-162`; `RowForm.cs:162-181`; tested). | Monument-line runs and block/intersection mode (one width, centerline at half: `RowBuilder.cs:143, 290`); offset monument lines; per-course widths and tapers; half-width dimensions with `{left}` / `{right}` / `{total}`; a stored, re-editable ROW job. | Extend `RowJob` / `RowBuilder`; reuse the easement `WidthSpec` and `WidthDimensions` (`Easements/EasementModel.cs:41-49`, `Exhibits/ExhibitChecks.cs:25-29`). |
| **Setup / project info** | Settings levels and profiles (behaviour only). Project-like fields are scattered: `ExhibitInfo`, `FieldNotesHeader`, `LegalInputs`. | One project record (number, name, client, surveyor or firm, county, STR, coordinate system, field dates, crew) used by exhibits, sheets, legal descriptions and dip notes. | As in the title-block row. |
| **Survey Hub** | No reference in the code, docs, config, deploy scripts or git history. No HTTP code. | None. | Keep it so: no network calls; any PLSS data stays a local file. |

---

## 6. Implementation sequence (after this audit is reviewed)

Each step is one reviewable change. Each passes the unit tests, both compile checks and its live Civil 3D test before the next starts.

| # | Step | Main files |
|---|---|---|
| 0 | **Groundwork and fixes.** Fix FTFPOINTS double counting. Put `FtfCommandTrace` in `FtfSession.Run`, so any command is checkable live. Fix the compare harness's kind literals. Decide the committed project data (section 7). | `TreeCommands.cs`, `FtfSession.cs`, `tests/LiveSmoke/CompareTestHarness.cs` |
| 1 | **Shot identity index** (point number, handle, raw description, XYZ per vertex). Replace the description-only index and check neighbouring buckets. FTFLINES uses point notes like the other passes. **Linework code parser.** | `LineworkCommands.cs` (`BuildNoteIndex`, `NotesAtVertices`), `FtfLineworkService.cs`, new `Linework/LineworkCoding.cs` |
| 2 | **Point ledger and dispositions**: library model, CAD collector, `ACCOUNT` store, ignore reasons, Overview and `LASTRUN` counts, "Points" review source. | new `Review/PointLedger.cs`, new `PointAccountCommands.cs`, `DrawingStore.cs`, `Config.cs`, `DiagnosticsSettings.cs`, `FtfStatusService.cs`, `ProcessingRecord.cs`, `ReviewKinds.cs` |
| 3 | **Continuity engine**: library candidates and evidence, CAD collector, proposals (kind 53), `line.gap` / `line.stray` items, settings per feature. | new `Linework/LineContinuity.cs`, new `LineContinuityCommands.cs`, `Ownership.cs`, `Settings/Sections.cs`, a settings page |
| 4 | **Review window**: geometry and evidence on items, transient overlay of source points and proposal, Approve correction (kind 54), bulk not-applicable, grouping, decision fingerprints and reopen-on-change. | `Review/ReviewRegister.cs`, `Ui/ReviewForm.cs`, `ReviewCommands.cs` |
| 5 | **Run after FTFRUN**, the window and Label All; summary line; one undo group. | `FtfCommand.cs`, `Ui/FtfMainForm.cs`, `LabelAllCommand.cs`, new `PostRunChecks.cs` |
| 6 | **Live Civil 3D tests** (section 8): blocking core in `smoke-test.ps1`; developer suite; survey-figure fixture. | `tests/LiveSmoke/*`, `deploy/smoke-test.ps1`, `DeploymentContractTests.cs` |
| 7 | **Capability gaps**, each separate: project record and title blocks → legend symbols → monument vocabulary and control table → asymmetric ROW → STR (manual first, offline lookup later). | as in section 5 |

Steps 1-5 are the accountability and continuity work. Step 7 can be reordered or deferred independently.

---

## 7. Problems found that are outside the plan

1. **FTFPOINTS counts a point twice.** A point whose block is missing is counted as recognized (`TreeCommands.cs:83`) and then as skipped (`:107`). Separately, the `BLOCK` error is added only at run time, so the Overview's `Gather` counts these points as processed.
2. **The compare smoke harness counts the wrong objects.** `tests/LiveSmoke/CompareTestHarness.cs:307-308, 511-512` count ownership kinds 35-38. Those are now FTFRECORD kinds; compare and trim are 45-48 (`Ownership.cs`). The harness is developer-only (`deploy/build-install-test.ps1`), not the UPDATEFTF gate.
3. **Project data is committed** (since the baseline commit `d8db9db`). It conflicts with the rule against confidential project documents in the repository:
   - `tests/RealWorld/cases/*.txt`: state-plane coordinates, a client name, parcel numbers and an STR description, for example `c6-svba-heavy.txt:32-43`;
   - parcel numbers in `tests/LiveSmoke/runcleanup.scr:86-96`;
   - a personal path in `tests/LiveSmoke/ui/run-ui.scr:1`.

   Not changed; **your decision** (replace with synthetic values going forward; whether history matters).
4. **FTFLINES can disagree with the label passes.** It identifies lines without point notes, so a line can be named differently there than by FTFLINELABELS and the gate pass.
5. **Fence heights from field notes are missing.** They are read only from the raw description, but in the reference drawing they are in the field notes.

---

## 8. Civil 3D 2024 tests required

### 8.1 How (based on the existing harnesses)

- **Headless, blocking:** a new `tests/LiveSmoke/AccountabilityTestHarness.cs` following the `ContourTestHarness` protocol:
  - C# 5;
  - result file named by an environment variable;
  - `BEGIN` / `STAGE … PASS|FAIL` / `RESULT` / `COMPLETE`;
  - ownership kinds read from `FtfEntityKind`, never literals;
  - `RequireRanOnce` through `FtfCommandTrace`;
  - run by a new `Invoke-AccountabilityCheck` in `deploy/smoke-test.ps1` that **exits 1 on failure** (unlike the style check), so UPDATEFTF rolls back;
  - pinned in `DeploymentContractTests`.
- **Seed data:** points with `CogoPoints.Add` plus a point number; linework as `Polyline` and `Polyline3d` built through the shots. This is the TBC-export reality of the reference drawing. TIN surfaces are seeded with `TinSurface.Create` + `AddVertices`, as the contour harness does.
- **Survey figures:** no harness can create them, and nothing shows a survey database can be created headless. Plan:
  1. Build one **synthetic** fixture drawing in Civil 3D from a synthetic field book: invented coordinates, no client data, a README stating its origin. Commit it as `tests/LiveSmoke/fixtures/figures-synthetic.dwg`.
  2. Check `SurveyFigure.Name`, `GetPoints(AllPoints)`, figure numbers and that figures are untouched.
  3. If the Survey API can build the fixture by script, prove that first in a separate spike.
- **Window tests:** an `acad.exe /b` harness like `RowLotsUiTestHarness` drives `ReviewForm`. This is developer-run, 10-15 minutes.
- **Real drawing:** the reference drawing and others under `C:\dev\FTF-RealWorld` (never committed) through `tests/RealWorld/run-real.ps1`. The report records only counts and false-positive notes.

### 8.2 Scenarios and what each must prove

| # | Scenario (synthetic) | Must prove |
|---|---|---|
| A1 | **Interrupted fence**: 101 `FNC B`, 102, 103 `FNC`, 104 `PP` (unrelated), 105 `FNC B`, 106, 107 `FNC E`; two polylines, collinear, 8 ft gap | One `line.gap` (103↔105, evidence: re-begun, 104 between, aligned). One kind-53 proposal on a non-plotted layer. Both polylines unchanged (handles, vertex count, coordinates). |
| A2 | **Gate gap**: fence ends at two `FGP` posts 6 ft apart | No `line.gap`; one `6' GATE` label. |
| A3 | **Intentional gap**: fence ends either side of a driveway's edge lines, ends coded E then B | Classified intentional (or possible, per the settings decided); no proposal. |
| A4 | **Parallel strings**: `FNC1` and `FNC2` ends 3 ft apart | No proposal between different strings. |
| A5 | **Consecutive-number trap**: `FNC` shots numbered 201-206 forming two fences 40 ft apart and not aligned | No proposal. |
| A6 | **Duplicates**: two points at the same XY (same code; different code) | `point.duplicate` for each pair; nothing drawn twice. |
| A7 | **Unknown and bare codes**: `XYZQ 12`; bare `CB` with a marker style; bare `CB` with a style that draws nothing | `point.unknowncode`; Symbol (accounted); `point.nosymbol`. |
| A8 | **Missing linework**: `EP` shots 301-305, no polyline | `point.missinglinework` × 5 and `line.stray`. |
| A9 | **Surface and exclusions**: `GS` shots in a TIN; one `GS` outside it; ignore entry with and without a reason | Surface (accounted); Excluded with reason; `point.excludednoreason`. |
| A10 | **Control**: `FIP` with and without FTFCONTROL | Control (accounted), with the table entry when present. |
| A11 | **Missing output**: a tree rule that plans a label, with labels for its category turned off; and one where the label is deleted by hand after a run | Not expected (accounted) vs `point.missingoutput`. |
| R1 | **Rerun safety**: FTFRUN twice | Identical counts of every FTF kind; COGO points unchanged (number, raw description, XYZ, style); linework unchanged; same review item ids; nothing duplicated. |
| R2 | **Approved correction**: approve A1's proposal, then FTFRUN twice | Exactly one kind-54 correction; item resolved; proposal gone. A moved correction is kept. Editing a source polyline flags the correction "source changed" and does not delete it. |
| R3 | **Decisions survive**: N/A on A3 and A7, rerun | Still N/A. Moving A3's end 2 ft reopens it with "source changed". |
| R4 | **Source edit**: change a point's raw description between runs | `point.sourcechanged`; FTF wrote nothing to the point. |
| R5 | **Undo**: `U` after FTFRUN | The run and its checks undo together. |
| R6 | **QC gate**: FTFQC Complete with a required item open, then after resolving it | Refused, then marked. |
| F1 | **Survey figures (fixture)**: interrupted `FNC1` figure pair; `FNC1` / `FNC2` | As A1 and A4 on real `SurveyFigure` objects; figure numbers read; figures untouched. |
| U1 | **Window (acad.exe)**: zoom, overlay, approve, bulk N/A with reason, QC Complete | Every action through the real `ReviewForm` controls. |

---

## 9. Decisions needed before implementation

1. **Bare codes.** May a bare code count as accounted for when Civil 3D's point style draws its marker (Symbol), or must every bare code be confirmed once per code?
2. **Ignore reasons.** OK to let `ignoreCodes` and the user's ignored codes carry a reason (old files still load)? Should a missing reason be required for QC Complete?
3. **Form of an approved correction.** Options:
   - a separate connector on the feature layer (proposed);
   - a combined copy of both lines;
   - a review record only, with the fix made in the field book.
4. **Thresholds.** The default search distance, sequence window and deflection per feature family. Proposed: 3× median shot spacing (at most 50 ft), 25 shots, 30°.
5. **Required for QC.** Which new kinds are required by default. Proposed: all point kinds except `sourcechanged`; `line.gap` probable and possible.
6. **Committed project data** (section 7 item 3): replace it with synthetic data going forward? Rewrite history or not?
7. **Survey-figure fixture.** OK to build it by hand once in Civil 3D from a synthetic field book?
8. **Observation dates.** Where do your crews' dates live: a user-defined property, the survey database, or the field book name (the reference drawing's field books are named by date and crew)?
