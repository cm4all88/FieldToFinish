# FieldToFinish: what it does now

Written 2026-11-03, at commit `cab20cd` on `claude/friendly-curie-0inutv` (the DEV branch, not
main). FTF is a Civil 3D 2024 plugin that finishes a survey after Civil 3D has built it.
Civil 3D creates the points, description keys, symbols and coded linework. FTF reads them and
adds what a finished base map needs: labels, tags, schedules, driplines, rotations, gate and fence
annotation, drafting tools, boundary work, easements, exhibits and QC. It never rebuilds the
survey geometry.

**Status in one line.** Everything compiles against the real AutoCAD 2024 API, and about 2,600
unit tests pass. Some areas have been run in Civil 3D (the deploy smoke test, contours, styles,
Right of Way and Lots). Much of the CAD side, including everything added this week, has only been
compiled and unit-tested so far.

---

## 1. Rules everything follows

These hold for every command below:

- **It never guesses.** An unreadable code, an ambiguous gate or an arrow with no clear
  direction goes on the review list. FTF does not pick a plausible answer.
- **It only erases its own output.** Everything FTF draws carries a stamp naming it as FTF's.
  `FTFCLEAN` and every rerun remove only stamped objects. Points, figures, linework and blocks
  FTF did not draw are never erased.
- **Hand work is kept.** A label you moved or reworded stays as you left it on every rerun. A copy
  you made of FTF output (COPY, paste, ARRAY) belongs to you.
- **Reruns don't duplicate.** Each stage replaces only its own earlier output.
- **Source linework is read-only.** Fences, hedges and edges are never exploded, converted or
  edited.
- **Nothing is swapped silently.** A missing text style, multileader style, block or layer is
  named at the end of the command, along with what was used instead.
- **Blocks are opt-in.** No rule inserts a block unless it says `insertBlock: true`, so a symbol
  can't be drawn twice.

---

## 2. Processing the points (FieldToFinish tab)

| Command | What it does |
|---|---|
| `FTF` | Opens the FTF window: status, review pages, settings and standards. It does nothing on its own. |
| `FTFRUN` | Process Drawing: runs every finishing stage in order (symbols, driplines, labels, tags, schedule, draw order). Saved workflows let you choose which stages run. |
| `FTFPOINTS` / `FTFTREES` | Reads every point's field code with the strict parser and places what its rule says. Unreadable points go to the exception report. |
| `FTFDRIP` | Tree driplines, trimmed to the outer edge where canopies overlap. |
| `FTFLABELS` | Point labels with masks and leaders, placed around existing work. Labels come from the crew's notes, not the bare code. A hand-moved label stays put. |
| `FTFTAGS` / `FTFTABLE` | Tree tags (T1, T2...) that keep their numbers across runs, and the tree schedule table. |
| `FTFLABELALL` | Label Everything: every recognised line (with fence heights and gates), every point, then draw order. Lists what it could not label. |
| `FTFRESTYLE` | Updates existing FTF labels, tags and line labels to the current styles, in place. Previews first and keeps hand moves and edits. |
| `FTFORDER` | Puts labels and symbols back above the linework and masks after pasting or a Civil 3D rebuild. |
| `FTFCLEAN` | Removes everything processing created. Points, linework and drafted lines are untouched, and it asks before removing hand-moved labels. |
| `FTFCONTROL` | Control point and found monument table: number, northing, easting, elevation, note. |
| `FTFLEGEND` | A legend of the line types this drawing actually contains. |

**Field-code grammar.**
- Trees are read as species, then trunk sizes, then `.`, then drip radius. `CON 18 . 25` is an
  18-inch conifer with a 25 ft drip; several trunk sizes make a cluster.
- Poles and signs have a notes vocabulary, so `PP 1234 PSE XFMR TEL` labels the same in any word
  order.
- Codes are sorted into four groups:
  - errors (loud);
  - unhandled data, summarised with samples;
  - bare codes (do nothing);
  - linework or never-draw codes (do nothing).

---

## 3. Symbols

| Command | What it does |
|---|---|
| `FTFORIENT` | Turns the symbols Civil 3D drew to face the way the features do: signs, hydrants, light arms, signal poles, valves, pavement arrows and words. Each decision is ranked: measured direction first, then a relationship to another feature, then road alignment, then inference. Anything unsure goes to review. It writes a QC layer (not plotted) and a CSV of every decision with its evidence. A marker turned by hand is kept. |
| `FTFORIENTRESET` | Puts every marker FTFORIENT turned back as it was, and removes the QC graphics. |

**Pavement arrows.**
- A written direction of travel is honoured: `ASTO N` and `ASTO S`, or NB, SOUTHBOUND, NE and
  so on.
- The arrow lines up with the nearest road lane and follows curves. At an intersection it picks
  the approach street.
- A crew direction that conflicts with the road too much goes to review.
- The block's own zero is taken into account.
- The settings are on the Symbols and Rotations page.

---

## 4. Line labels and line features (Survey Drafting tab)

| Command | What it does |
|---|---|
| `FTFLABELLINE` (`FTFL`) | The main way to label a line. Click it, slide the live preview along it (the cursor picks the spot and side), click to place. The feature is identified from the figure name, then the Trimble name, then the shots it was drawn through, then the layer. If it can't tell, it asks. Fence heights show live, and the gates on that fence are labelled too. |
| `FTFLINELABELS` | The optional automatic pass over the whole drawing. Labels are spaced along long runs and never placed beside a label you placed by hand. |
| `FTFLABELBETWEEN` (`FTFB`) | A label centred between two picked edges, or between two sides of one line (a U-shaped hedge gets `HEDGE` between its legs, not at the bottom). Works on 3D polylines and survey figures. Everything is measured in plan, and the edges are only read. `W` switches the wording and `T` lets you type your own. |
| `FTFLABELDRIVEWAY` (`FTFDWY`) | A between-edges label built from the edge layer's surface type: `ASPH DWY`, `CONC DWY`. |
| `FTFLABELSTAIRS` (`FTFS`) | Select the tread lines; the label counts the steps. |
| `FTFBENCH` | A bench outline and its size label (`BENCH 3'x1.5'`), built from the two end shots and the depth in their notes. |

**Fence heights.**
- `FNC B 3.5` labels `3.5' FENCE`, and the height carries along the fence until a shot records a
  new one (`5' FENCE`).
- Line codes (B, E, C...) and figure numbers are skipped.
- A number that can't be a height is put on the review list.
- Wording, decimals and the highest height believed are settings.

**Gates.**
- `FTFGATES` pairs gate posts (FGP plus your aliases), but only where a fence supports the pair:
  - both posts on one fence, next to each other; or
  - the fence stopping at both posts.
- Width rules:

  | Width between posts | Label |
  |---|---|
  | under 4.5 ft | review |
  | 4.5 ft to just under 5 ft | `GATE` |
  | 5 to 25 ft | `6' GATE`, `12' GATE`... |
  | over 25 ft | review |

- The width is the distance between post centres, and FTF says so. A width the crew wrote on a
  post (`FGP 12`) is used only when the posts agree with it.
- Ambiguous pairs, unpaired posts and posts away from any fence go to review.
- `FTFGATE` lets you pick two posts to confirm or change a pair. Reruns never move or remove that
  label.

**Also.**
- `LEFT`/`RIGHT` written in the source coding is honoured; it can't be recovered once TBC
  flattens it away.
- Lettered linetypes (`---W---`) are left to the legend.
- The label layer comes from the drawing's own layer table and is never invented.

---

## 5. Review and QC

| Command | What it does |
|---|---|
| `FTFREVIEW` | The review list: one place for everything that needs a drafter. Each row shows the point or entity, feature, issue, why, what to do, severity, whether it's required, and status. You can zoom to it and select it, run its fix command, open its settings, recheck it, mark it not applicable (a reason is required), accept it, or reopen it. Stored in the drawing. |
| `FTFREVIEWCHECK` | Rechecks the codes (read-only): unknown codes, blank or unreadable descriptions, invalid values, and unknown words after a linework code. |
| `FTFQC` | Marks the drawing Drafting QC Complete. It is refused while required items are open, and a new required item takes the mark off again. Saving and drafting are never blocked. |
| FTF window pages | Unknown Codes (errors, needs configuration, informational), Overview, Reports (CSV exports), Lines (why each line is or isn't labelled), Standards (where every value comes from). |
| `FTFLINES` / `FTFBLOCKS` / `FTFWHERE` / `FTFLINEMETA` | Read-only reports: linework inventory, block check, where the rules file was found, and line metadata. |

What feeds the review list:
- gates;
- fence heights;
- unlabelled lines;
- FTFORIENT reviews and failures;
- the code checks;
- commands that didn't finish.

Codes you told FTF to ignore and normal Civil 3D linework never appear on it. Which kinds of item
are "required" is a table on Settings > Review and QC.

---

## 6. Storm and sewer dips

| Command | What it does |
|---|---|
| `FTFDIP` | The Dip Builder window: structures from field dips, with pipes, connections, inverts, labels and QC in one place. Edits made while the drawing waits on a pick are queued, not refused, and there is a Cancel prompt button. |
| `FTFDIPCHECK` | Finds structures whose surveyed point moved after they were drafted, and offers to rebuild each one. |
| `FTFDIPPDF` | Field-book style PDF notes: measured depths in black, calculated inverts in red, a sketch per structure. An issued file is never overwritten (`_R1`, `_R2`...). |

---

## 7. Survey drafting and sheets

| Command | What it does |
|---|---|
| `FTFSPOT` | Click spot elevations off a TIN surface; each value moves clear of the ones already placed. |
| `FTFCONTOUR` | Contours at the exact elevations you type, taken off a TIN surface. They can be clipped to a boundary and labelled. The surface is never changed. |
| `FTFDRAWLINE` | Boundary, right-of-way, section and easement lines drafted course by course from bearings and distances, annotated to the office standard. |
| `FTFDRAFTCLEAN` | Removes drafted annotation. The lines themselves go only after you confirm. |
| `FTFTILDE` / `FTFROWDIM` | Break mark across a line; right-of-way width written between the centerline and the R/W line. |
| `FTFSHEETPLAN` / `FTFSHEETMAKE` / `FTFSHEETS` / `FTFKEYMAP` / `FTFMATCHLINE` | Sheets: proposes the fewest sheets with match lines, makes one locked-viewport layout per sheet (never overwriting), draws sheets from existing layouts, adds a key map, and draws match lines. |

---

## 8. Boundary

| Command | What it does |
|---|---|
| `FTFRECORD` (+ `REBUILD`, `LABEL`, `SOURCE`, `CHECK`) | Load a recorded plat or Record of Survey, review the extracted calls, and rebuild the geometry. Then: record labels, where a line came from, and the drawing checked against the record (flags only). |
| `FTFROW` | Pick the monuments; it draws the R/W lines, the opposite line, the centerline, width dimensions and monument notes. Verified in Civil 3D (99 UI checks). |
| `FTFLOTS` | Divides a platted block into lots: lot lines, alley, setbacks, easements, numbers, areas and drainage arrows. Verified in Civil 3D. |
| `FTFCOMPARE` (+ `RUN`, `APPLY`, `QC`, `SAVE`, `BATCH`, `RESTORE`) | Compares our survey with the consultant's base map. What they already show goes nonplot and what they lack keeps plotting. Discrepancies are clouded. The final QC blocks the deliverable on a real problem, and everything can be restored. |
| `FTFTRIM` / `FTFTRIMRESTORE` | Trims the aerial base inside our limits and flags the seams. Can be undone. |

---

## 9. Easements and exhibits

| Command | What it does |
|---|---|
| `STRIPEASEMENT` / `PORTIONEASEMENT` / `CONSTRUCTIONAREA` | A strip of a given width trimmed to lot lines; part of a lot ("west 10 ft of the south 50 ft"); or a metes-and-bounds area. |
| `FTFEASEMENTEXCLUDE` / `FTFEASEMENTCOMPONENT` | EXCEPT and TOGETHER WITH, then rebuild. |
| `FTFEASEMENTLEGAL` / `FTFEASEMENTEXPORT` | A DRAFT legal description in the office's Exhibit A pattern; the ordered courses, width and area. |
| `FTFEXHIBIT` (+ `REBUILD`, `GROUP`, `QA`, `PREVIEW`, `STAMP`) | The exhibit sheet with its viewport, annotation and tables. Rebuilding keeps your edits. Also: grouped exhibits, a pre-plot QA, a PDF preview, and the stamp. |
| `FTFEASEMENTINSPECT` / `FTFEASEMENTCHECK` / `FTFEXHIBITINSPECT` | Independent closure traverse, a check against the source survey, and where every exhibit item came from. |

---

## 10. Settings and standards

- **`FTFSETUP`**: every command's settings, grouped by workflow. Pages register themselves, so a
  new command's page just appears.
- **Five settings levels**: Built-in < Office < My defaults < Profile < Project, merged value by
  value. Every value shows where it comes from.
- **`FTFPROFILE`**: chooses the drafting profile a drawing uses.
- **Layers**: every layer FTF draws on, mapped to the project's layer with colour, linetype and
  plot settings.
  - `FTFLAYERUPDATE` fixes layer properties after asking.
  - `FTFCHECKSTANDARDS` checks every named layer, style and block and offers fixes.
- **Text and leader styles**: named looks, assigned per feature, family, rule or code, with a
  tester that shows where each value comes from.
- **Point Features**: one screen per feature (label, layer, leader, tag, style, preview).
- **Line Labels**: per-feature wording and layer, fence height wording, and automatic pass
  spacing.
- **Gates**: post codes, how close counts as on the fence, ambiguity margin, width limits,
  wording, rounding, decimals, label layer.
- **Review and QC**: which item kinds are required, whether QC Complete is blocked, reviewer name.
- **Symbols and Rotations**: the symbol standard FTFORIENT uses, plus the pavement marking options.
- **Workflows and Commands**: saved processing workflows, every command listed with its ribbon
  place and settings pages, and your own "My Commands" ribbon panel.
- **Ribbon**: four tabs (FieldToFinish, Survey Drafting, Boundary, Easements). Tests check it
  against the commands the plugin actually registers.

---

## 11. Installing and updating

- **`UPDATEFTF -Dev -Ref <branch>`**: builds, validates and installs a DEV build. It rolls back if
  the build fails, so a failed build never replaces the installed version.
- **`install-dev.ps1` / `make-release.ps1` / `test-release.ps1`**: per-user install and the
  office setup folder.
- **`FTFUPDATE`**: a newer office copy installs itself when Civil 3D closes. An older office copy
  never pulls a machine back.
- **`FTFVERSION`**: which build is running, where it came from, and whether it is the latest
  approved build.
- **Deploy smoke tests**: run in Civil 3D on install, including a style check and a contour check.

---

## Lessons learned

1. **Building on Linux missed a Windows compile error.** The Linux check uses stand-ins for the
   AutoCAD libraries, and those stand-ins lacked AutoCAD's `Font` type. So an ambiguous `Font`
   (AutoCAD's vs System.Drawing's) compiled here and broke your Windows build. Fix: every change
   now also compiles against the real AutoCAD 2024 NuGet assemblies, and a test flags bare type
   names that clash between Autodesk and System namespaces. Use the real API wherever you can get
   it; stand-ins hide problems.
2. **Find the actual cause before fixing.** Three bugs this week looked different from what they
   were:
   - **Dip Builder** "won't let me change it": a drawing prompt was still waiting, and the window
     refused edits instead of queuing them.
   - **Both arrows pointing the same way**: the crew's written N/S was never read, and keep-right
     inference gave both arrows one direction.
   - **3D polyline "rejected"**: the second pick on the same object was refused as "the same line
     twice", and the preview measured a flattened cursor against a line hundreds of feet up.
3. **Measure in plan.** Anything on 3D polylines or survey figures should project along Z
   (`GetClosestPointTo(p, Vector3d.ZAxis, ...)`) or work from XY vertices. Elevation otherwise
   quietly pulls points to the wrong place.
4. **Distance alone is not evidence.** Two gate posts 6 ft apart may be on two different fences.
   Requiring fence evidence, and flagging rather than guessing when two pairings are plausible,
   keeps wrong labels off the plan. The same goes for widths: post spacing is not a measured clear
   opening, and the label shouldn't pretend it is.
5. **Check claims against the real drawing.** In my copy of the reference drawing, points 10075 and 10076 are stripe
   shots, not `ASTO N`/`ASTO S`. The code sheet has HGL for hedges, but the rules had no hedge
   feature and no layer for it. Real data keeps showing that the description and the drawing
   differ.
6. **A review list beats command-line messages.** Problems printed to the command line get lost.
   Stable IDs mean a drafter's "not applicable, gate removed" survives reruns. An item the check
   stops finding closes itself.
7. **Tests that read the source keep promises honest.** The ribbon, settings registry, ownership,
   "never opened for write" and similar rules are enforced by tests that read the plugin source,
   since Civil 3D can't run here. They caught missing settings pages and ribbon entries this week.
8. **Parallel work needs clean seams.** Splitting gate code into its own file, and giving the
   Label Between fix to a separate helper working on a disjoint part of `LineworkCommands.cs`,
   merged with no conflicts.
9. **Docs drift.** The README's "Build status" still says 114 tests and "nothing has run". The
   real numbers are about 2,600 tests and several areas verified in Civil 3D. Status sections need
   updating with each release.
10. **Known noise in the Linux test run**:
    - 4 path tests fail because they expect Windows paths.
    - One easement test with a 10-second timeout occasionally fails when the machine is loaded.
    - Neither is a real failure, but both make the "all green" signal fuzzy.

---

## Ideas

In rough order of value:

1. **Civil 3D smoke tests for this week's work.** Extend `tests/LiveSmoke` the way FTFROW and
   FTFLOTS were verified: seed fence figures with FGP posts, a U-shaped Polyline3d hedge and two
   `ASTO` arrows. Then run FTFGATES, FTFLABELBETWEEN, FTFORIENT and FTFREVIEW twice each and check
   the second run changes nothing.
2. **Read the figure prefix database directly.** Import `parametrix.fdb_xdef` instead of
   hand-copying layers into `rules.json`. That would give HGL its real layer and stop the rules
   drifting from the office standard.
3. **Read the drawing's description keys as the code dictionary.** `FGP = GATE POST`,
   `HGL = HEDGE LINE`: FTF could suggest gate post codes, line features and labels from the keys
   already in the drawing, for the drafter to confirm.
4. **Build the Survey Production Checklist on the review list.** The QC Complete mark and the
   required items are ready. A checklist page could add the manual steps (title block, stamp,
   north arrow, plot check) beside the automatic ones.
5. **Run the code recheck automatically** at the end of FTFRUN and Label All, so the review list
   is always current without remembering FTFREVIEWCHECK.
6. **Export the review list** to CSV or PDF as a QC record, with who closed what and why.
7. **Double gates.** Three posts in a row, where the middle one is a centre post, could become
   `12' DOUBLE GATE` when the crew codes the centre post (say `FGPC`) instead of being flagged as
   ambiguous.
8. **Clear-opening width.** An optional post diameter setting could subtract post size from
   centre-to-centre spacing, labelled as an estimate, for offices that want clear widths.
9. **Feed failed point labels into the review list.** FTFLABELS failures still only reach the
   exception report.
10. **Continuous integration.** Run the unit tests and both compile checks (stand-in and real API)
    on every push in GitHub Actions. Make the 4 path tests work on any operating system so a red
    result always means something.
11. **Refresh the README status sections** (Build status, Needs verifying) to match what has
    actually been verified.
