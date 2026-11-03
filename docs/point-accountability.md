# Point accountability and linework continuity

FTF accounts for every imported survey point from what the drawing actually holds for it, never
from its code alone. It also checks that the linework the crew coded is drawn: gaps, skipped shots
and undrawn strings. What is missing goes on the review list (FTFREVIEW). **Drafting QC Complete
(FTFQC) waits until every required item is drawn, corrected, or closed with a documented reason.**

The checks never write a COGO point or a line of the survey. They write:

- the review list;
- the point ledger in the drawing;
- non-plotted previews of proposed corrections;
- two reports beside the drawing.

Approved corrections are separate objects; the original figures and polylines are never edited,
exploded or joined.

This implements the audit in `docs/audits/point-accountability-linework-continuity.md` with the
decisions recorded there.

## When it runs

| Trigger | What happens |
|---|---|
| `FTFRUN`, the window's Process page, `FTFLABELALL` | It runs when the run finishes, a run that stopped early included. The summary line says how many points are accounted for, how many are for review, and how many continuity items are open. Settings > Point Accountability > Run after processing turns this off. |
| `FTFREVIEWCHECK` (Recheck all in FTFREVIEW) | Runs everything: the code check, continuity, the ledger, the previews and the reports. |
| `FTFQC` | Runs the checks first, so QC is never judged on a stale list. |

Inside FTFRUN the checks are part of the same command, so one `U` undoes the run and its checks
together.

## The point ledger

Every point gets one disposition, with all the evidence behind it. A disposition comes only from
the drawing:

| Disposition | Evidence |
|---|---|
| **Drawn** | FTF objects stamped with the point: label, tag, block, dripline, bench, or a gate label naming the post. A label typed by hand next to the point counts too. |
| **Linework** | A vertex of, or on, a drawn line of its own feature (approved corrections included). |
| **Control** | A `controlCodes` code. When the drawing has a control table, the point must be in it. |
| **Symbol** | Its point style (or its point group's override) draws **the code's own symbol block**. |
| **Surface** | A TIN vertex coincides with it (plan and elevation, 0.01 ft). The evidence says whether a point group in the surface's definition holds it, or the match is by coordinates only. |
| **Excluded** | An ignore entry with a documented reason, or a reviewer's decision with a reason. |
| **Review** | Something it needs is missing, or a decision is open. |
| **Deleted** | It was recorded before and is no longer in the drawing. |

What each kind of point needs:

| The point is | It needs |
|---|---|
| A code with a rule | What the rule plans: label (unless the annotation settings turn it off), tag, block, dripline. If the rule plans nothing, its own symbol. |
| A bare code | Its own symbol: a block FTF placed, or a point style whose marker is the code's symbol block. That block is one named under Settings > Point Accountability > Symbols, the one the orientation standard gives the code, or a block whose name holds the code as a word (`PMX-CB` for `CB`). **A generic marker (a dot, an X, another code's symbol) is not enough** (decision 1). |
| A linework shot | A drawn line of its feature through it, for every figure it is coded onto. |
| A control code | The control table, when there is one. |
| An ignored code | A documented reason (decision 2). An entry that expects a surface (`"expect": "surface"`, as `GS` does) also needs a surface built from it. |
| A code nothing handles | A rule, or the code's review item closed with a reason. |

**Represented in CAD** (something shows it) is reported separately from **drafting complete**
(everything it needs exists or is decided). A hydrant whose label is missing is represented by its
marker, but its drafting is not complete.

The ledger lives in the drawing (key `ACCOUNT`). The first run is the **baseline inventory**. After
that, every point ever recorded stays in it:

- a point that disappears is reported (`point.deleted`) and kept with its last description;
- a point that comes back is marked restored;
- a changed description or position is recorded (`point.sourcechanged`, information only).

Each point keeps its history, and the last 100 runs' numbers are kept with the ledger.

### Review items (source "Points")

| Kind | Meaning |
|---|---|
| `point.missingoutput` | Something its rule, its ignore entry or the control table expects is not in the drawing. |
| `point.missinglinework` | A linework shot on no line of its feature, not already covered by a continuity item. |
| `point.nosymbol` | A bare code (or a rule that plans nothing) without its own symbol. |
| `point.duplicate` | Another point within 0.01 ft in plan. Both points are listed. |
| `point.excludednoreason` | On an ignore list whose entry gives no reason. |
| `point.deleted` | Recorded before, gone now. |
| `point.sourcechanged` | Changed in Civil 3D since the last run (information, not required). |

Codes nothing handles and unreadable descriptions stay on the code check's items
(`code.unknown:<code>`, one per code; `code.unreadable` and `code.invalid`, one per point). The
ledger links its points to those items, so one decision on the code accounts for all its points.

## Linework continuity

The shots are read in collection order (point number order) as the strings the crew coded. Each
figure is followed separately (interleaved strings are normal). The parser handles:

- B begin, E end, C close;
- curves P to T (a curve shot need not be a vertex);
- point on curve (OC);
- recall (RPN) and connect (CPN);
- a shot on several figures;
- figure numbers (FNC1 is not FNC2).

Each consecutive pair of shots in a string must be joined by a drawn line of the feature.

| Finding | Kind | When |
|---|---|---|
| Gap in a coded string | `line.gap` | The string is coded continuous but drawn as two lines with a gap. Probable, unless gate posts at both ends make it intentional or another feature crossing the gap makes it possible. |
| Interrupted collection | `line.gap` | The same figure was begun again after it stopped. The two parts are weighed on several kinds of evidence: the same figure id; whether the first part was ended with E; how many shots came between, and what they were; the gap against the strings' median shot spacing; whether the line turns across the gap; crossings; gate posts. Probable needs a re-begin with nothing against it. Possible is reported too. **Consecutive point numbers alone are never enough.** |
| Missing intermediate vertex | `line.missingvertex` | The line goes straight past a shot coded onto it. |
| Missing segment | `line.missingsegment` | Coded shots with no line between them, or a whole string not drawn. |

Never proposed:

- strings with different figure numbers;
- different features;
- a gap longer than the feature's limit;
- a gate opening (posts at both ends). Gate openings are intentional breaks: listed in the continuity report, not review items.

A gap crossed by an **identified** feature (a line FTF names from its shots, figure name or layer: a
driveway edge, a road, a wall) is intentional when the feature's row says "crossing means
intentional". A line nothing identifies (a contour, a parcel line) only weakens the case: the gap is
still possible, and still for review.

**Probable and possible gaps both need review** (decision 5).

Every item carries its evidence, its confidence and a fingerprint of the shots and line ends it was
found on.

### Limits per feature (decision 4)

Settings > Point Accountability holds one row per feature family (fences, walls, curbs and
flowlines, pavement edges, buildings, striping), plus a defaults row. Each row sets:

- the spacing factor;
- the longest gap;
- the shots allowed between the two parts;
- the sharpest turn;
- whether a crossing or gate posts make a gap intentional.

**The shipped values (3 × median spacing, at most 50 ft, 25 shots, 30°, buildings 95°) are initial
testing values, not proven standards.** Tune them against real drawings.

## Proposals and corrections (decision 3)

For every open continuity item, FTF previews a proposed correction: a dashed connector and a note on
`FTF-QC-CONTINUITY`. FTF makes that layer non-plotting and redraws the previews on every run.

**Approve correction** (FTFREVIEW, or `-FTFREVIEW` > Approve) draws the correction as a separate
line on the source line's layer:

- a 3D polyline when the elevations differ;
- stamped as an FTF correction (`linefix:<item id>`), with a hash of its geometry;
- the item becomes **Corrected**, recording who and when.

The survey linework is never changed, so a later run still finds the gap in it. The decision holds
while the source is unchanged. After that:

| What happens | What FTF does |
|---|---|
| A rerun | Keeps the correction and never draws a second one. |
| The correction is edited by hand | Keeps it, and never draws over it. |
| The correction is erased | Reopens the item. |
| The source changes (a shot moves, a line end moves, the gap no longer exists) | Reopens the item with "source changed". The correction is kept for the drafter to check or erase. |

FTFCLEAN never erases corrections.

## Decisions, history and change detection (decision 5)

Every review item keeps a history:

- opened;
- found again;
- resolved;
- not applicable;
- accepted;
- corrected;
- source changed;
- correction erased;
- reopened.

Each event records who and when. A person's decision (not applicable, accepted, corrected) is
stored with the fingerprint of the source it was made on. When a later run finds the source changed,
the decision is reopened with the old decision quoted, never carried over silently.

Point and linework items need a documented disposition. Not applicable always needs a reason, and
Accept needs a note for these kinds. In FTFREVIEW you can select several items (or **Select same
kind** for every item of a kind and code) and close them with one reason.

## Ignore reasons (decision 2)

In `rules.json` `ignoreCodes`, and in Settings > Diagnostics, an entry is either a code (as before)
or an object. Older files still load:

```json
"ignoreCodes": [
  { "code": "GS", "reason": "Ground shot: surface data for the TIN, nothing drafted.", "expect": "surface" },
  "ZK"
]
```

Marking a code ignored on Unknown Codes now asks for the reason, and records who and when. An entry
without a reason puts its points on the review list (Settings > Point Accountability > Ignored codes
need a reason). The shipped `ZK` and `CONS` have no reason, because their meaning is the office's to
document.

## Reports

| File | Contents |
|---|---|
| `<drawing>.ftf-points.csv` | Every point ever recorded. Columns: raw description as stored, XYZ, how FTF read it, disposition, represented, drafting complete, expected, found, missing, evidence, issues, decision, first and last seen, deleted. Ends with the run's numbers. |
| `<drawing>.ftf-continuity.csv` | Every continuity finding: intentional breaks included, with their evidence, status and decision. |

The Overview's **Accounted for** row shows the last run's numbers; click it to open FTFREVIEW. The
last processing record carries them too.

## Commands

No new processing command. The new command is `-FTFREVIEW`, the command-line form of FTFREVIEW
(AutoCAD's dash convention), for scripts and the keyboard:

- `List` [id prefix];
- `Approve` id;
- `NotApplicable` ids + reason;
- `Accept` ids + note;
- `Reopen` ids;
- `Zoom` id.

A trailing `*` names every open id that starts so (`point.duplicate:*`); commas separate several.

## Verification

| What | Status |
|---|---|
| Library: continuity, ledger, decisions, ignore notes, reports | Unit tests (`LineContinuityTests`, `PointAccountingTests`, `ReviewDecisionTests`, `IgnoreReasonTests`, `AccountabilityHarnessExpectationTests`). |
| Plugin | Compiles against the real AutoCAD 2024 API (`tests/RealApiCompileCheck`). Civil 3D types are named through aliases; Civil 3D members and names use stand-ins on Linux and are checked against Civil 3D itself only by the Windows build (fb3d1b2 failed there on an ambiguous `DBObject`, now fixed and covered by the checks). |
| Civil 3D 2024, headless | `tests/LiveSmoke/AccountabilityTestHarness.cs`, a **required** stage of the deploy smoke test (UPDATEFTF puts the previous build back if it fails). Covers the audit's scenarios A1, A2, A4, A6-A11, R1-R4 and R6: FTFRUN twice, approve, recheck, source change, QC refused then marked, erased point and correction. **Not yet run in Civil 3D at the time of writing**: at 68804df the identity check before it stopped (below), so it never started. |
| Point identity, headless | `tests/LiveSmoke/IdentityTestHarness.cs`, required. IDVERIFY READER compares the reader's points with Civil 3D's own COGO point collection (number, handle, raw description, XYZ), and the field-notes path (FTFLINES's) with the plain one. At 68804df it stopped with no output: it looked `SourcePointReader.Read` up by name, the new `Read(db, tr, notes)` overload made that ambiguous (AmbiguousMatchException), and nothing caught it. Fixed by naming every plugin method by its exact signature (`HarnessContractTests`). **Not yet re-run in Civil 3D.** |
| Fence heights in field notes | A fence height written in the crew's note (Settings > Point Labels > Read notes from) is read as one written in the description; identification still uses the raw description. Unit tested; not run in Civil 3D. |
| Not covered headless | The FTFREVIEW window itself (buttons, multi-select): needs an `acad.exe` UI run (scenario U1). Survey figures: the figure fixture (`tests/LiveSmoke/fixtures/README.md`). Undo of a run and its checks together (R5). Surface membership through a point-group definition (the harness builds its TIN from vertices; the definition path is read by reflection and reported as a NOTE). |
