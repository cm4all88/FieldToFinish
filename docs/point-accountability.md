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

## The drafting standard (Settings > Point Features > Drafting Standard)

What a point needs on the plan comes from one drafting standard per feature. Point accountability,
Label All, FTFLABELS and FTFORIENT all read it, so a feature the standard draws no label for is never
labelled by Label All and never reported for a missing label. The engine has no list of its own.

A point's requirement is built up in this order:

1. the default row (`family: "*"`);
2. its Label All family's row;
3. a row whose description pattern matches, only when no rule or list knows its code (so
   "HOUSE NUMBER 4735 SIGN" is a sign);
4. its own code's row;
5. any exception that fits, by sign category or by a pattern on the description or the crew's note.

Each row can set:

| Field | Values |
|---|---|
| `class` | Feature, Surface, Control, Sign, Utility, Structure, Vegetation, Note |
| `disposition` | Drafted, SurfaceOnly, ControlCheck, Symbol, PointStyle, Label, SymbolAndLabel, PointOnly, Review |
| `symbol` | `None`; `PointStyle` (any visible marker, or one of `symbolStyle`); `Specific` (the code's own symbol block, or one of `symbolStyle`) |
| `label` | `AsPlanned` (what its rule writes, where Label All labels its family); `None`; `Required`; `PointLabel` (its own Civil 3D label counts, where FTF's label would say no more than the description: "SN STOP" still gets "STOP SIGN") |
| `surface`, `rotation`, `review` | yes / no |
| `layer`, `symbolStyle`, `labelStyle`, `note` | text; styles are comma separated, `*` and `?` allowed |

With `review` on, every such point is listed for a person to look at. `rotation` has three states:

- `true`: turning the symbol is part of the deliverable;
- `false`: FTFORIENT turns it only by a direction the crew recorded, never an assumed one;
- not stated (the default row states nothing): FTFORIENT's own rules decide, as before.

The built-in PMX standard, used when the rules file has no `featureStandard`:

| Feature | Standard |
|---|---|
| Any code | Its point style shows it; the label its rule writes, where Label All labels its family. |
| `GS` ground shot | Surface only: no symbol, no label, never a drafting item. |
| `ZK` control check shot | Control/Check: no symbol, no label; in the control check report. |
| `WFH`, `FH` fire hydrant | Its own symbol, correctly rotated; no text label. |
| Signs (`SN*`, `SIGN`, the Signs family, and a description with the word SIGN) | Their own symbol, rotated, labelled by their point label. Address / house number, private and other descriptive signs: their point style is enough, and nothing turns them without a recorded direction. |
| Utilities, storm and sewer, trees, control families | Their point style. |

Sign categories come from the sign's code, then its words, in this order:

1. address / house number;
2. private;
3. street name;
4. regulatory;
5. warning;
6. other descriptive.

The description is never changed. A private sign is not assumed to face a road.

The Drafting Standard editor lists every row in force. A built-in row keeps its codes, family and
pattern; switch it off and add your own instead. A row's exceptions are a grid below it: each
applies by sign category and/or a pattern on the description or note, then names the fields it
changes. Save Rules writes only what differs from the built-in standard. An office file that changes nothing gets no section, and later corrections to the
built-in standard still reach it.

```json
"featureStandard": {
  "requirements": [
    { "name": "Cleanout", "codes": ["SSCO"], "label": "Required" },
    { "name": "Water meter", "codes": ["WMT"], "symbol": "Specific", "symbolStyle": "PMX WATER*" },
    { "codes": ["SN*", "SIGN"], "family": "Signs", "match": "\\bSIGNS?\\b", "enabled": false }
  ],
  "signCategories": [ { "category": "Address", "codes": [], "words": ["HOUSE NUMBER", "LOT NUMBER"] } ]
}
```

A standard that cannot work (a bad family, a pattern that is not a regular expression, two default
rows) is refused when the rules load, with the reason.

## The point ledger

Every point gets one disposition, with all the evidence behind it. A disposition comes only from
the drawing:

| Disposition | Evidence |
|---|---|
| **Drawn** | FTF objects stamped with the point: label, tag, block, dripline, bench, or a gate label naming the post. A label typed by hand next to the point counts too. |
| **Linework** | A vertex of, or on, a drawn line of its own feature (approved corrections included). |
| **Control** | A `controlCodes` code (the control table, when the drawing has one), or a control check shot. |
| **Symbol** | Its point style (or its point group's override) draws **the code's own symbol block**, or a style the standard names. |
| **PointStyle** | Its Civil 3D point style shows it, as its standard has it; or its own Civil 3D label does (`label: PointLabel`). |
| **Surface** | Designated surface data (its standard, or an ignore entry that expects a surface). Membership is checked only when asked (below). |
| **PointOnly** | The standard draws nothing for it: the survey point is the record. |
| **Excluded** | An ignore entry, or a reviewer's decision with a reason. |
| **Unverified** | The drawing shows it (a visible marker, a point label), but FTF could not read enough to confirm it is what its standard requires. Neither accounted for nor missing (below). |
| **Review** | A required deliverable is missing, or a required decision is open. Warnings never hold a point. |
| **Deleted** | It was recorded before and is no longer in the drawing. |

What each kind of point needs:

| The point is | It needs |
|---|---|
| A code with a rule | What the rule plans (block, dripline, tag), then what its standard asks: a label as planned or as required, a symbol by point style or its own. |
| A bare code | What its standard asks. By default its point style is enough (`WMT`, `GYA`, `BOL`, `SSCO`). A code the office labels with its code (`symbolLabels`, as `PP`) needs that label, as Label All draws it. Only a standard with `symbol: Specific` needs the code's own symbol block. |
| A linework shot | A drawn line of its feature through it, for every figure it is coded onto. |
| A control code | The control table, when there is one. |
| A ground shot, a control check shot | Nothing on the plan. Never a required item. |
| An ignored code the standard does not name | Nothing. An entry without a reason is a warning to document it. |
| A code nothing handles | A rule, or the code's review item closed with a reason. A description the standard names by its words is not "a code nothing handles". |

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

Severity follows importance:

- **Required action**: a missing or incorrect deliverable. It holds the point for review and blocks
  Drafting QC Complete.
- **Warning**: a possible deficiency. It is listed and does not hold the point.
- **Information**: something accounted for that is worth knowing.

| Kind | Severity | Meaning |
|---|---|---|
| `point.missingoutput` | Required | A label, tag, block, dripline or control-table entry its rule or standard requires is not in the drawing. |
| `point.nosymbol` | Required | Its standard requires a symbol (its own, or a point style it names), and the marker is not it. |
| `point.missinglinework` | Warning, required | A linework shot on no line of its feature, not already covered by a continuity item. |
| `point.review` | Warning, required | Its standard asks for every such point to be reviewed. |
| `point.symbolunverified` | Warning, required | A required symbol FTF could not confirm: the point shows a marker, but its point style's type or block could not be read. One item per code, listing its points. |
| `point.labelunverified` | Warning, required | A point label the standard lets stand in, whose visibility (or style name) FTF could not read. One item per code. |
| `point.duplicate` | Warning | Another point within 0.01 ft in plan. Both points are listed. |
| `point.excludednoreason` | Warning | On an ignore list whose entry gives no reason. |
| `point.surfaceexcluded` | Warning | (Surface check on) In a surface's definition but left out of its triangulation. |
| `point.surfaceunverified` | Information | (Surface check on) Surface data no surface can be shown to hold; one item per code, listing its points. |
| `point.deleted` | Required | Recorded before, gone now. |
| `point.sourcechanged` | Information | Changed in Civil 3D since the last run. |

A run raises at most one item per point and kind. An item a run no longer finds resolves by itself
with its history kept. This is how false positives from an earlier build go away. A person's
decision stays while the point it was made on is unchanged.

### Missing, or not verified

A symbol or label is **missing** only when what FTF read shows it is not there:

- the point style draws no marker, or draws a generic marker or another code's block;
- the point's own label is turned off, its style draws no label, or it has no label style;
- the standard requires a real label (`label: Required`) and none is drawn.

When a read fails, the point is **not verified**: a Civil 3D member that cannot be read, or a
style name, marker type, symbol block or label style that comes back unread. The item says which
read failed. It is required, so QC waits for a person: they check the points once per code and
accept with a note, or fix them. It resolves by itself once a later read works. A read that then
shows the symbol is wrong is missing, so nothing is dismissed by a failed read.

The ledger's `Unverified` column lists what each point is waiting on. The run's summary counts
"shown but not verified" apart from "for review".

### Surface data and the optional surface check

A ground shot is accounted for as surface data whether or not a surface uses it: missing surface
evidence is not a drafting error, and the point and its description are never changed. Settings >
Point Accountability > **Verify surface membership** (off by default) also checks each one against
the drawing's TIN surfaces. The ledger's `Surface` column records the result:

- **designated**: the check is off;
- **verified**: a TIN vertex at the point, or in a point group the surface is built from;
- **excluded**: in the definition but not triangulated (a warning);
- **not verifiable**: information only.

### Control check shots

A `ZK` shot is accounted for as a control check, with no symbol or label. When there are any, the
check writes `<drawing>.ftf-control-checks.csv`: each check shot paired with the nearest control
point within Settings > Point Accountability > Control check: control point within (2 ft by
default), with ΔN, ΔE, horizontal and ΔZ, or a note that none is near.

### Orientation

FTFORIENT reads the same standard. A feature whose standard says `rotation: false` (an address,
private or other sign) is turned only by a direction the crew recorded: a measured
azimuth, `FACES N`, or a shot panel. It is never turned toward the nearest road. The report says
"left as surveyed (no recorded direction; its standard turns it only by one)".

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
without a reason is listed as a warning, so the reason gets written down (Settings > Point
Accountability > Ignored codes need a reason). An ignore entry only says nothing is drawn: a code its
drafting standard names (GS, ZK) is accounted for by the standard. The shipped `ZK` is documented as a
control check shot. `CONS` has no reason, because its meaning is the office's to document.

## Reports

| File | Contents |
|---|---|
| `<drawing>.ftf-points.csv` | Every point ever recorded. Columns: raw description as stored, XYZ, how FTF read it, class, the standard that applied, what it needs, surface status, disposition, represented, drafting complete, expected, found, missing, unverified, evidence, issues, decision, first and last seen, deleted. Ends with the run's numbers, warnings counted apart. |
| `<drawing>.ftf-control-checks.csv` | Each control check shot, the control point it checks, and the differences (written when there are check shots). |
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
| Drafting standard | Unit tests (`FeatureStandardTests`, `RuleDocumentFeatureStandardTests`) cover: GS with and without a surface; ZK; house number signs; hydrants with their symbol and no label; bare codes shown by an acceptable point style; missing required symbols and labels; configuration changes and back; earlier false positives resolving while decisions are kept; Label All and FTFORIENT reading the same standard. |
| Drafting standard in Civil 3D 2024 | The accountability harness seeds a bare hydrant, ZK shots, a house number sign with its label on (1301) and one with it turned off (1302), and a hydrant whose point style draws its symbol block (1401). STANDARD / RESTORED add a standard to the rules beside the drawing and take it away; SURFACE / SURFACEOFF turn the surface check on and off. What the harness set up is read back from Civil 3D's own typed API and compared with the ledger. If the harness cannot set something up, that check is a NOTE, not a pass. |
| 65d1c05 in Civil 3D | Passed everything but HY and SG. Both came from the plugin's readers, not the standard: the point style's name and the label style were read by reflection with `Type.GetProperty(name)`. The style name came back null, which only an exception in that read can cause, and the exception was swallowed: every style read as "?", and the label as "could not be read". The only exception that call raises for a valid name is `AmbiguousMatchException`, thrown for a member a class declares again; the message itself was not logged. The engine then called the hydrant's symbol and the sign's label missing. Fixed: typed reads (`StyleBase.Name`, `PointStyle.MarkerType`, `PointStyle.MarkerSymbolName`, `CogoPoint.LabelStyleId`), `PropertyReader` for the rest, the reason kept, and "not verified" kept apart from "missing" (`PropertyReaderTests`, `UnverifiedEvidenceTests`). **The fix is not yet run in Civil 3D.** |
| Settings > Point Features > Drafting Standard, Settings > Point Accountability | Compiled against AutoCAD 2024 (`tests/RealApiCompileCheck`). **Untested as UI.** The document they edit is unit tested. |
| FTFORIENT and the standard | Unit tested only: FTFORIENT is not in FTFRUN, so the headless harness does not run it. |
| Plugin | Compiles against the real AutoCAD 2024 API (`tests/RealApiCompileCheck`). Civil 3D types are named through aliases; Civil 3D members and names use stand-ins on Linux and are checked against Civil 3D itself only by the Windows build (fb3d1b2 failed there on an ambiguous `DBObject`, now fixed and covered by the checks). |
| Civil 3D 2024, headless | `tests/LiveSmoke/AccountabilityTestHarness.cs`, a **required** stage of the deploy smoke test (UPDATEFTF puts the previous build back if it fails). Covers the audit's scenarios A1, A2, A4, A6-A11, R1-R4 and R6: FTFRUN twice, approve, recheck, source change, QC refused then marked, erased point and correction. **Not yet run in Civil 3D at the time of writing**: at 68804df the identity check before it stopped (below), so it never started. |
| Point identity, headless | `tests/LiveSmoke/IdentityTestHarness.cs`, required. IDVERIFY READER compares the reader's points with Civil 3D's own COGO point collection (number, handle, raw description, XYZ), and the field-notes path (FTFLINES's) with the plain one. At 68804df it stopped with no output: it looked `SourcePointReader.Read` up by name, the new `Read(db, tr, notes)` overload made that ambiguous (AmbiguousMatchException), and nothing caught it. Fixed by naming every plugin method by its exact signature (`HarnessContractTests`). **Not yet re-run in Civil 3D.** |
| Fence heights in field notes | A fence height written in the crew's note (Settings > Point Labels > Read notes from) is read as one written in the description; identification still uses the raw description. Unit tested; not run in Civil 3D. |
| Not covered headless | The FTFREVIEW window itself (buttons, multi-select): needs an `acad.exe` UI run (scenario U1). Survey figures: the figure fixture (`tests/LiveSmoke/fixtures/README.md`). Undo of a run and its checks together (R5). Surface membership through a point-group definition (the harness builds its TIN from vertices; the definition path is read by reflection and reported as a NOTE). |
