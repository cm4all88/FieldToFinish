# Field-to-Finish Plugin — Parser Core

Parses surveyed point descriptions into drawing instructions. Consumed by the
Civil 3D plugin; contains no Autodesk references and runs without a CAD licence.

## Layout

```
src/FieldCodes/        netstandard2.0 — grammar, rules, parsing. No CAD types.
src/FieldCodes.Cad/    net48;net8.0   — CogoPoint reading, block/label/drip placement.  [not yet written]
config/rules.json      the grammar itself
```

The split matters: `FieldCodes` is the single definition of what a code means.
Nothing downstream should re-implement the grammar.

## Targeting 2024 and 2026

| Civil 3D | Runtime | Plugin TFM |
|---|---|---|
| 2024 | .NET Framework 4.8 | `net48` |
| 2026 | .NET 8 | `net8.0` |

Only the CAD assembly multi-targets. In its csproj, reference the Autodesk
assemblies conditionally and never copy them to output:

```xml
<PropertyGroup>
  <TargetFrameworks>net48;net8.0</TargetFrameworks>
</PropertyGroup>

<ItemGroup Condition="'$(TargetFramework)'=='net48'">
  <Reference Include="AcCoreMgd"><HintPath>$(C3D2024)\AcCoreMgd.dll</HintPath><Private>false</Private></Reference>
  <Reference Include="AcDbMgd"><HintPath>$(C3D2024)\AcDbMgd.dll</HintPath><Private>false</Private></Reference>
  <Reference Include="AcMgd"><HintPath>$(C3D2024)\AcMgd.dll</HintPath><Private>false</Private></Reference>
  <Reference Include="AeccDbMgd"><HintPath>$(C3D2024)\AeccDbMgd.dll</HintPath><Private>false</Private></Reference>
</ItemGroup>
```

`<Private>false</Private>` on every one. Copying Autodesk DLLs into your output
folder is the most common cause of "works on my machine" load failures.

Ship as a `.bundle` under `%ProgramFiles%\Autodesk\ApplicationPlugins\` with a
`PackageContents.xml` declaring both `ComponentEntry` blocks, so one installer
serves both versions and nobody has to NETLOAD.

## Newtonsoft conflict

AutoCAD ships its own `Newtonsoft.Json.dll` and loads it into the process. On
2024 (.NET Framework, shared AppDomain) a version mismatch throws at load. Three
options, in order of preference:

1. Match AutoCAD's shipped version exactly.
2. ILRepack the parser + Newtonsoft into one assembly with `/internalize`.
3. `AssemblyResolve` handler in the plugin's initializer.

On 2026 this is much less likely to bite — .NET 8 gives the plugin its own
`AssemblyLoadContext` — but build for the worse case.

## Design stance

Descriptions arrive already cleaned by the office code audit, so the parser is
strict on purpose:

- Anything unparseable is an **Error** and the point is not drawn.
- Unrecognised modifier tokens are **Errors**, never ignored. Silently dropping
  a token like `DEAD-PROTECTED` is how a tree lands on the wrong sheet.
- No fallback drip radius, no guessed species. A plausible guess in a survey
  drawing outlives everyone's memory of it being a guess.
- **Warnings** mean drawable but suspicious — range outliers, transposed
  trunk/drip, cluster count disagreeing with the stem list. These are what drive
  the review layer.

If errors show up at draw time, that's an audit gap, not a data problem. Report
them loudly rather than recovering gracefully, or people stop running the audit.

## Grammar

Office format: **species, one or more diameters, a literal `.`, then the drip
radius.** More than one diameter means a cluster — no modifier token is needed to
say so.

```
CON 18 . 25                   conifer, 18" trunk, 25 ft drip radius
DEC 12 12 . 14                deciduous cluster, two stems, 14 ft drip radius
CON 8 10 12 18 18 18 24 . 45  seven stems, 45 ft drip radius
```

Species codes are `CON`, `DEC`, `MAP`. The regex captures the species loosely
rather than listing those three as alternatives, so an unrecognised species gives
`Unknown species code 'OAK'` rather than a shrug — the point of the strict parser
is that it says what is wrong.

`trunk` is rounded to `trunkDecimals` for labels. **`trunk.exact` is the unrounded
average and is what anything geometric must use** — `blockScaleFrom` points at it,
because a symbol scaled off a rounded label is drawn at the wrong size.

### What happens to a code with no rule

Four outcomes, and the distinction is the whole point of the report being readable:

| Situation | Result |
|---|---|
| Matches a rule, data is malformed (`CON 18`, no drip) | **Error** — loud |
| Carries data, no rule configured (`XMAG AKA 1013`) | **Unhandled** — summarised with samples |
| Bare code, no data (`PP`, `CB`, `ASPH`) | **Nothing**, silently |
| Linework (`EC B`) or never-draw (`GS`) | **Nothing**, silently |

*"If the note doesn't have anything then it does nothing."* A bare code records that a
thing is there; the point style already shows it and there is no value to label. The
same code carrying data — `PP 1234` — is a different matter and gets reported.

On the first real drawing this took the report from **70 codes across 673 points**
down to **5 descriptions**, all of them survey control awaiting a decision.

### Linework

`lineworkCodes` lists codes whose points define figures rather than features. TBC and
Civil 3D build those into polylines; this tool labels the polylines, so the points
need no rule and are not reported.

Their descriptions carry control codes and chain several figures onto one point —
`BLD B EC B BLD1 B` — none of which is parsed here. Numbered variants (`EC1`, `EC2`)
are separate parallel strings, not repeats.

### Species patterns are generated

A code rule's `match` may contain `{SPECIES}`, which expands to the keys of that
rule's species map when the rules load. The pattern and the map therefore cannot
drift apart.

This matters more than it sounds. The tree rule previously captured species loosely
as `[A-Z]{2,4}` so an unknown species gave a precise error — but that made it swallow
every other code of the same shape, and `PP 1234` parsed as *a tree of unknown
species* rather than a power pole carrying a number. A species outside the map now
surfaces in the unhandled report with its real description attached, which is what
someone actually needs in order to add it.

### Codes this tool does not draw

A survey base map is mostly topo: 746 of the 765 points in the first real drawing
were `EC`, `GS`, `CG`, `TBC`, `SSMH` and the like. Those are listed in
`ignoreCodes` and skipped silently. Anything *not* on that list and *not* matching
a code rule is still a loud error, so a mistyped tree code fails hard.

The list is seeded from one drawing. Review it before relying on it for another job.

Modifiers still apply where present: `CON 18 . 25 CL4 DEAD` and
`... DEAD CL4` produce identical output.

Modifiers apply in **priority order**, not the order they appear in the note, so
`... CL4 DEAD` and `... DEAD CL4` produce identical output. Two modifiers both
setting `labelFormat` at the same priority is a config error and throws at load.

Multi-stem: `TRD 12,14,10 24 CL3` averages the stems per `multiStemAverage` and
retains the individual values in `Fields["trunk.stems"]`.

> Worth confirming against your local ordinance before the next field cycle.
> Stems of 12/14/10 give **12"** arithmetic, **20.98"** quadratic
> (`sqrt(12² + 14² + 10²)` = `sqrt(440)`), and **25"** by largest-plus-half
> (`14 + 0.5 × (12 + 10)`). If the reviewing agency uses a different formula than
> the config does, the average alone can't be recomputed — which is why the raw
> stems are preserved and should be written to XData.
>
> These figures were corrected on first build: this note previously read 20.2"
> and 24", which match neither the code nor its doc comments. `StemAverageTests`
> pins the computed values. If 20.2/24 were the intended *ordinance* numbers then
> the formulas in `FieldCodeParser.Average` are what needs changing, not this note.
> The active config uses `Arithmetic`, where both agreed, so nothing drawn so far
> is affected.

## Installing

Two ways in, both per-user and neither needing admin rights. Civil 3D must be closed
either way: it holds a loaded .NET assembly for the life of the process.

```
deploy\install-dev.ps1                 this machine, from the Debug build
deploy\make-release.ps1 [-Zip]         the folder the office runs, from a Release build
deploy\test-release.ps1                tests that folder without touching a real install
```

`make-release.ps1` writes `deploy\release\FieldToFinish-Setup\`:

```
Install FTF.bat      what a surveyor double-clicks
Uninstall FTF.bat
install.ps1          the work; the batch file stages it locally and calls it
README.txt           three steps, and what to do when something is wrong
version.txt          build date, branch and commit -- printed while installing
Bundle\              PackageContents.xml + Contents\2024\ (the plugin itself)
```

Copy that folder anywhere the office can reach (the U: drive) and point people at
`Install FTF.bat`. It stages the files to `%TEMP%` first, so a network folder and a
network hiccup are both fine, and it refuses to install while Civil 3D is open.

Installing writes one folder -- `%APPDATA%\Autodesk\ApplicationPlugins\FieldToFinish.bundle`
-- and replaces it whole, so a file from an older FTF cannot linger and load. Office
configuration lives in `%APPDATA%\FieldToFinish` and is never touched; see
"Configuration ownership".

### Updating a whole office

Installing records the setup folder it came from, in `source.txt` inside the bundle. At
startup FTF reads that folder's `version.txt` on a background thread -- a slow or absent
network never delays Civil 3D -- and compares the stamp against its own:

```
newer there   the FTF window says an update is waiting; at quit FTF launches that
              folder's install.ps1 with -WaitForCivil3D, which waits for the process
              to end and then replaces the bundle. The next start is current.
same          nothing happens.
older there   nothing happens: an office copy that was rolled back does not drag
              machines back with it. Rolling back is a deliberate re-install.
unreadable    nothing happens, quietly -- including when the drive is unreachable.
```

So publishing a build reaches a surveyor the next time they open Civil 3D after closing
it once. A loaded assembly cannot be replaced underneath a running session, which is the
whole reason the swap waits for the close. `install-dev.ps1` deliberately removes
`source.txt` and `version.txt`: a build installed from the repository is nobody's office
copy and must not be replaced by one. `FTFUPDATE` reports what a machine found; the
decision itself is `FieldCodes.Deploy.OfficeBuild`, with unit tests.

Civil 3D builds FTF's two ribbon tabs at startup: **FTF** for the drawing being
finished (Label, Finish, Dips, Review, Sheets, Clean up) and **FTF Boundary** for the
record work written from it (Record, Easements, Exhibits).

## Build status

Everything compiles. `FieldCodes` and `FieldCodes.Cad` (net48, against the local
Civil 3D 2024) build clean; 114 unit tests pass.

```bash
dotnet test FieldToFinish.sln
```

### Verified against a real drawing

`553-2750-051-SV-BASE` (Gig Harbor SW Park), 765 CogoPoints:

```
ignored (topo)  : 746
parsed OK       : 19
errors          : 0
```

That run is what corrected the grammar — the sample `TR<letter>` rule this file
originally documented matched none of the 765 real descriptions.

### What is tested

`FieldCodes` has no Autodesk dependency, so anything that can live there is unit
tested rather than left for a drawing. That deliberately includes two algorithms
that would otherwise have been CAD-only:

| Area | Where | Covered |
|---|---|---|
| Grammar, modifiers, rotation, errors | `FieldCodeParser` | yes |
| Layer classification / banding / obstacle class | `LayerClassifier` | yes |
| Drip-line arc trimming, spatial bucketing | `Geometry/DripLineTrimmer` | yes |
| Label candidate search, obstacle classes | `Geometry/LabelPlacer` | yes |
| Exception-report CSV formatting | `Reporting/ExceptionReport` | yes |

### What is NOT tested — needs a drawing

Every file in `FieldCodes.Cad` is marked `UNTESTED` in its header. It compiles
against the real Autodesk assemblies, so the API surface is right, but no line of
it has ever executed. See "Needs verifying" below.

## Where FTF sits

**FTF is not a replacement for Civil 3D's Field to Finish.** Civil 3D creates the
survey geometry; FTF is the post-processing and presentation layer that polishes that
geometry into the office standard.

```
raw field coding
  → Civil 3D survey / description keys / linework
    → existing points + symbols + linework
      → FTF polishing
        → finished survey base map
```

| Civil 3D owns | FTF owns |
|---|---|
| Survey points | Interpreting extra information in the description |
| Description keys | Rotating existing symbols |
| Survey symbols and blocks | Modifiers and presentation-layer changes |
| Coded survey linework | Labels, conflict avoidance, leaders |
| | Tags, schedules, tree driplines |
| | Draw order, cleanup, problem-code review |

So FTF does **not** build fence geometry, building polygons or curb linework — Civil
3D already produces those. A fence may eventually need FTF *polish*, meaning
inspecting the line Civil 3D drew, not drawing another one.

### Block insertion is opt in

A code rule naming a `block` inserts nothing. `insertBlock` must be set to `true`
deliberately, for a symbol Civil 3D genuinely cannot provide. Without that gate, one
added key would put a second symbol on top of every point a rule matched, and
duplicated symbols look almost right — which is how they reach a plan set.

No shipped rule sets it. `BlockInsertionSafeguardTests` asserts that, and that a
modifier cannot smuggle a block in either.

### Reversing modifications — OPEN PRODUCTION REQUIREMENT

FTF does two different things, and only one of them is currently reversible:

| | Reversible? |
|---|---|
| **Entities FTF creates** (labels, driplines, tags, masks, the schedule) | Yes — XData ownership, `FTFCLEAN` erases them |
| **Properties FTF modifies on Civil 3D objects** (`MarkerRotation` today) | **No** — there is no entity to erase |

Re-running is safe because a modification is idempotent: the same rotation is written
again. But `FTFCLEAN` cannot put a rotated marker back, because it never recorded what
the value was before FTF touched it.

Before this goes to production, modifications need a reversible strategy — most likely
recording the prior value alongside the ownership stamp so cleanup can restore it,
and deciding what happens when someone has since changed it by hand. **Not solved.
Recorded so it is not forgotten.**

## Who draws the tree symbol

**Civil 3D does.** Description keys match the raw description and apply a point
style that draws the symbol; the plugin deliberately places no block for trees.

This was settled by running against the real drawing: the tree rule originally
asked for `TREE-CONIFER` and friends, which do not exist in the drawing, because
the symbols were already coming from point styles. Inserting blocks would have put
a second symbol on top of every tree.

So the plugin does only what point styles cannot:

- drip lines, trimmed to the outer envelope
- labels with collision avoidance, masks and leaders
- draw-order banding

Two consequences that are easy to get wrong:

- A `CogoPoint` **is** the tree symbol, so label placement treats CogoPoints as
  **hard obstacles** and draw-order banding puts them in the **symbol** band. Both
  are decided by entity type, not by layer — a point group can sit on any layer.
- No modifier may reintroduce a block, or the doubling comes back through the side
  door. The `cluster` modifier's `block` is commented out for that reason.

To hand the symbol back to the plugin, restore `block` / `blockLayer` /
`blockScaleFrom` on the tree rule (see the `//block` note in `rules.json`) **and**
turn the symbol off in the point style.

## Commands

| Command | Does | Re-runnable |
|---|---|---|
| `FTFTREES` | Parses every CogoPoint, places symbol blocks, writes the exception report | yes |
| `FTFDRIP` | Draws drip lines, trimmed to the outer envelope | yes |
| `FTFLABELS` | Places labels, masks and leaders | yes |
| `FTFORDER` | Re-applies draw-order banding | yes |
| `FTFCLEAN` | Deletes everything the automatic pipeline owns — drafted survey lines are deliberately excluded | yes |
| `FTFSETUP` | The control centre — every command's settings, in tabs | n/a |
| `FTFWHERE` | Reports where `rules.json` is being looked for and what loaded | n/a |
| `FTFBLOCKS` | Lists the drawing's blocks and checks them against the rules | n/a |
| `FTFDRAWLINE` | Drafts cadastral / record lines with bearing/distance annotation — the separate drafting world, see below | n/a |
| `FTFDRAFTCLEAN` | Cleanup for the drafted world: annotation by default, geometry only on explicit confirmation | n/a |
| `FTFRECORD` | Recorded plat / Record of Survey in, reviewed calls, reconstructed geometry out — see "Recorded surveys" | yes (FTFRECORDREBUILD) |
| `FTFRECORDCHECK` | Drawing versus the record; flags only | yes |
| `FTFRECORDLABEL` | Places or refreshes the record labels | yes |
| `FTFRECORDSOURCE` | Where a reconstructed line came from | n/a |
| `FTFRECORDREBUILD` | Reopen the review, rebuild after edits | yes |
| `FTFCOMPARE` | Survey-vs-consultant comparison window: what they already show goes nonplot, what they lack keeps plotting. See "FTFCOMPARE" below | yes |
| `FTFCOMPARERUN` / `FTFCOMPAREAPPLY` / `FTFCOMPARERESTORE` / `FTFCOMPAREQC` / `FTFCOMPARESAVE` / `FTFCOMPAREBATCH` | The same workflow from the command line, step by step | yes |
| `FTFLOTS` | Divides a platted block into lots: lot lines with bearings/distances, alley, setbacks, easements, numbers and areas, drainage arrows (see below) | n/a |
| `FTFROW` | Right of way from picked monuments: lines, opposite line, centerline, width dimensions, monument notes — runs, blocks, intersections (see below) | n/a |

## Line labelling: the primary workflow is interactive

The finished survey shows line annotation is context-dependent drafting, so the
surveyor decides which lines get labels and where. `FTFLABELLINE` is the primary
workflow:

1. Click a line (Line, Polyline, Arc, SurveyFigure, FeatureLine).
2. FTF identifies it -- figure name, then TrimbleName XData, then **the raw
   notes of the survey points the line was drawn through**, then layer -- and
   resolves the configured label standard. No confident identity, or candidates
   that disagree: it says exactly what it found and places nothing.

   Point notes are the strongest evidence surviving a TBC export: linework is
   drawn THROUGH the shot points, so a CogoPoint coinciding exactly with a
   vertex (0.01-unit bucket -- coincidence is evidence, proximity would be
   guessing) created that vertex, and its note still says what the flattened
   layer erased: "FOG B" vs "LNDY B" on the same stripe layer, wall subtypes,
   even "ASPH L" directional intent. The notes must AGREE (set intersection,
   chained shots like "BLD B EC B" narrowing against plain "EC B" ones);
   disagreement is reported, never chosen from.
3. A live preview (an EntityJig around the real DBText) tracks the cursor along
   the line: the cursor picks the spot ALONG the feature and WHICH side; within
   half the configured side offset of the line the label snaps onto the line,
   beyond that it offsets to the cursor's side at the full standard distance.
   Text, layer, style, height, offset and mask all come from the standard --
   never from the mouse. Curves use the local tangent at the placement spot, and
   the readability flip can never move the label off the chosen side (the jig and
   the engine share one placement calculation, `LineLabelPlanner.PlaceAt`).
4. One click places label + mask, stamped FTF-owned with the manual mark.

**Label layers resolve from the drawing's own layer table** (one resolver shared
by FTFLABELLINE, the bulk pass and the review, so they cannot disagree):

1. The feature rule's explicit `labelLayer` -- always wins.
2. The office-standard text layer derived from the source layer's name, most
   specific first, used ONLY if that exact layer exists: `V-CHAN-STRP-E ->
   V-CHAN-STRP-TEXT-E`; `V-SURF-FENC-CHNL-E -> V-SURF-FENC-TEXT-E` (no CHNL text
   layer exists); `V-ALGN-CNTR-E -> V-ALGN-TEXT`. The pattern was read from the
   real 177-layer table of 553-2750-051-SV-BASE, not invented.
3. The family's single text layer, when exactly one exists.
4. The configured default -- and only then. Several plausible family layers
   (V-SURF-HDRL-E has nine surface text layers and no derivable one): FTF
   refuses to pick, uses the default, and reports the candidates in Feature
   Review and the command line. The resolver can only return a layer that exists
   or one explicitly configured; it is structurally incapable of inventing a
   layer name that then gets created.

`FTFLINELABELS` (the whole-drawing bulk pass) remains available as a diagnostic
and is now OPT-IN (`lineLabels.enabled` defaults to false; existing settings
files keep whatever they have). The bulk pass never deletes a manually placed
label -- manual drafting wins -- and `FTFCLEAN` still removes everything FTF
owns. Every label-and-mask pair displays as source geometry, then mask, then
text, enforced at creation with `DrawOrderTable.MoveBelow`.

## Directional line labels: the TBC export limitation

Directional field intent is supported by FTF, but it can only be applied when the
source coding survives into the Civil 3D drawing. TBC-exported plain polylines that
retain only a layer cannot recover LEFT/RIGHT intent.

FTF reads `LEFT`/`L` and `RIGHT`/`R` from the modifier position of the source line
coding (a figure or feature line named `ASPH LEFT`, `RWC1 L`), relative to the
line's own direction. When no coding survives, the fallback chain is:

```
source directional modifier (when available)
  -> the feature rule's configured placement
    -> the global default placement
      -> On Line
```

No side is ever guessed, and no side is ever inferred spatially from nearby points
-- that would be a separate capability, deliberately designed and validated first.
`FTFLINEMETA` is the read-only probe that inspects surviving polyline metadata
(XData, extension dictionaries, hyperlinks, registered applications) before
concluding the coding is gone.

**What the inspection actually found (553-2750-051-SV-BASE, 2026-08):** 49 of 398
line entities carry `TrimbleName` XData -- the TBC feature *name* ("Edge of
Pavement", "Top of Slope", "FL ASPH CURB", "Building"), not the raw field coding,
and never a LEFT/RIGHT token. No other metadata survives: no extension
dictionaries on linework, no object data, no hyperlinks, no handles back to source
survey objects. So on this drawing the limitation above stands for directional
intent -- but TrimbleName is now a first-class identification source, between the
figure name and the layer in precedence, matched exactly (case-insensitive against
a configured code, name or label) and never fuzzily: "Edge of Pavement" identifies
EP, "Edge of Conc" identifies nothing and falls through to the layer. Should a
future export ever preserve raw coding ("ASPH L") in TrimbleName, the code match
AND the directional modifier fire with no FTF change at all.

**UPSTREAM IMPROVEMENT (recorded):** the TBC export used for
553-2750-051-SV-BASE flattened figure identity -- wall/curb subtypes collapsed to
generic layers and figure names were lost entirely; only TrimbleName XData
survived, on a minority of entities and without directional tokens. Exporting
Civil 3D survey figures (or preserving figure names / raw source coding -- ideally
in TrimbleName XData, which FTF already reads) would let FTF recover per-line
identity, subtype labels (RWC vs RWT, CG vs TBC) and LEFT/RIGHT intent in future
drawings. Until then, TrimbleName and layer identification with generic labels are
the ceiling.

## Survey line drafting: FTFDRAWLINE — the other FTF world

The pipeline rule stands: Civil 3D creates the survey topo geometry and FTF only
polishes it. `FTFDRAWLINE` is the one deliberate exception, and it is a separate
world with its own command, settings, ownership kinds and cleanup — the normal
`FTF` processing never creates these lines.

It exists for the small set of cadastral / record lines an office drafts by hand:
Boundary, Right of Way, Centerline, Section Line, Quarter Section, Easement, Lot
Line, Property Line (the catalog is configurable). Here the user is explicitly
asking FTF to create geometry, so FTF may own and manage it.

The workflow is continuous course entry, built for legal-description work:

```
FTFDRAWLINE
  → Boundary                      (line type → configured layer/linetype/annotation)
  → start point (pick or COGO point number)
  → Bearing → N 42 18 36 E → 184.27      line + "N 42°18'36" E 184.27'" annotation
  → next course starts at that endpoint  (Bearing/Azimuth/Pick/COGO/Undo/Finish)
```

- **One construction engine, many standards.** No per-type drawing code; the type
  only resolves layer, linetype, annotation layer, text style/height, offset,
  placement, mask, and bearing/distance formatting (`Drafting Lines` settings page,
  stored in `ftf-settings.json` — behaviour, not grammar, so not in `rules.json`).
- **The drawing's layer table is the authority.** A configured layer or linetype
  the drawing does not have is refused with an explanation; FTF never creates an
  office layer from a guessed name. An unconfigured annotation layer resolves
  through the same `LabelLayerResolver` the line labels use (existing office text
  layer in the line layer's family, else — visibly — the line's own layer).
- **Annotation is computed from the drawn geometry**, never echoed from what was
  typed; if they ever differ, the geometry is the truth. Text follows the course
  with the standard readability flip, which changes only the text rotation — the
  reported bearing never changes. Parsing is strict (`SurveyDirection`, unit
  tested): angle entry is packed D.MMSS, the Civil 3D convention — `N45.2536E`
  is 45°25'36" E, `.5` pads to 50 minutes — with symbol/space/dash DMS forms
  also accepted. Decimal degrees deliberately have no dot form (the digits
  collide with D.MMSS), and a packed entry reading as 60+ minutes or seconds
  ("N42.75E") is refused loudly rather than reinterpreted.
- **Undo Last Course** removes the most recent line and its annotation together.
- **Ownership**: drafted entities carry their own kinds (`DraftLine`,
  `DraftAnnotation`, `DraftMask`). `FTFCLEAN` — the undo button for a bad pipeline
  run — deliberately skips them and says so: drafted geometry is the user's work,
  not run output. `FTFDRAFTCLEAN` removes drafting annotation by default and
  erases the geometry only after an explicit Yes.

Annotation type is per line type: `None`, `Bearing + Distance` (combined or
stacked), `Bearing Only`, `Distance Only`, or `Feature Text` (a configured
literal such as SECTION LINE — never derived from the type name). Direction
format is per type too: quadrant bearing or whole-circle azimuth. `CurveData`
is reserved for the curve milestone.

**Verified headless (2026-08-20, accoreconsole against the stock NCS template):**
`tests\LiveSmoke\run-livesmoke.ps1` drives the whole straight-line milestone —
seed layers + COGO points, Boundary via Bearing/Azimuth/Pick/COGO courses,
Undo Last Course, a FeatureText Centerline from a COGO start — then checks the
stamped result: correct endpoints from each construction method, annotation
recomputed from geometry (typed azimuth 90 came back as `N 90°00'00" E`; the
picked course reported `S 13°38'19" E 124.21'` with the text rotation flipped
readable and the bearing unchanged), texts and masks on the derived
`V-PROP-BNDY-TEXT-E`, `FTFCLEAN` removing zero drafted entities, and
`FTFDRAFTCLEAN` Annotation/Everything behaving per the ownership design. The
log is `tests\LiveSmoke\draft_out.log`. Still needing interactive Civil 3D:
the Drafting Lines settings page itself (WinForms, never shown), wipeout
print/frame behavior, and on-screen pick UX with osnaps.

Not built yet, in order: tangent curves (previous course supplies the incoming
tangent; Radius + Arc Length / Delta / Chord, Left/Right required), then
non-tangent curves (refused unless the inputs uniquely define the curve), then
curve annotation standards and optionally a curve table. Milestone 1 is straight
courses: Pick Endpoint, Bearing+Distance, Azimuth+Distance, COGO point start/end,
continuous entry, computed annotation, undo.

## Recorded surveys: FTFRECORD — reconstruction from a recorded plat or Record of Survey

`FTFRECORD` takes a recorded plat, short plat, Record of Survey, boundary line
adjustment, large lot subdivision or easement exhibit (PDF, TIFF, JPG, PNG) and
rebuilds it as Civil 3D geometry **from its written calls**. It is not a tracing
tool: the picture assists interpretation (which text is where, which courses meet),
but every line and arc is traversed from a bearing and distance or from curve
elements that a person has seen and approved.

```
FTFRECORD
  → choose the document
  → read it (Windows OCR at several page rotations, or a .ocr.json sidecar)
  → extract calls, curves, lots, monuments, record references, notes — each with a confidence
  → propose the course order and figures from where the labels sit on the page
  → REVIEW: every call in a table against the page image; approve, edit, take an alternative, reorder
  → check the office standard against this drawing (missing layer/style/block = withheld, reported)
  → Build: traverse each figure, draw once per shared line, label, place monuments
  → QC report on the command line and beside the drawing
```

| Command | Does |
|---|---|
| `FTFRECORD` | Upload, extract, order, review, build, QC |
| `FTFRECORDCHECK` | Drawing versus record: bearing/distance/curve mismatches, closure per lot, shared boundaries, duplicates, missing lines/labels/monuments, layer and style against the standard. Flags only. |
| `FTFRECORDLABEL` | Places or refreshes the bearing/distance, curve, lot and record-vs-measured labels (hand-moved labels are kept and routed around) |
| `FTFRECORDSOURCE` | Click a reconstructed object: document, recording number, sheet, call, record source, record and measured values, confidence, spot on the page |
| `FTFRECORDREBUILD` | Reopens the review for a stored record and rebuilds after edits; warns first when the geometry was hand-edited |

### What the reader does and does not know

- **Windows OCR** (`Windows.Media.Ocr`, `Windows.Data.Pdf`, through the Windows 10 SDK
  contracts) reads the page; nothing is installed and nothing leaves the machine. Survey
  annotation runs at every angle, so each page is read at the rotations in
  Settings > Recorded Surveys (0, 90, 270, 180 by default) and the hits are mapped back
  onto the page and merged. Large sheets are read in overlapping tiles.
- Windows OCR gives **no per-word confidence**. FTF's confidence is honest about it: a
  line read once is 0.90, the same text read again in another pass is 0.97, and every
  repaired look-alike character (O for 0, l for 1, S for 5, B for 8) lowers it. A spot
  read *differently* in two passes keeps both readings and is flagged.
- **Sidecar**: any OCR tool can write the document's text in the `DocumentText` schema as
  `<document>.ocr.json` and FTFRECORD reads that instead (Settings > Recorded Surveys >
  OCR engine = Sidecar). `tests\RealWorld\pdf-to-sidecar.py` does it with tesseract, and
  is how the extraction is exercised outside Civil 3D.
- **Lines are put back together before anything is read.** An OCR pass breaks a printed
  line at every wide gap; the pieces are rejoined in the text's own frame (a sideways
  sheet's text runs down the page), by baseline and gap. A legal description that wraps
  ("thence S 88°" / "26'42\" E along said line 634.31'") is joined into its paragraph the
  same way, whichever way the sheet was scanned, so a call reads across the line break. In
  that running text only the number that follows the bearing (with a foot mark or unit,
  or directly after it) is the course's distance; section, lot and page numbers are not.
- **What could not be read is still shown.** A bearing with the right shape but an
  impossible value (81 seconds, 96 degrees) is listed as a course with no bearing and the
  reason, at its spot on the page, for the reviewer to type in; plain words that happen
  to start with N or S are not bearings. OCR look-alikes are repaired only inside a number
  (`634.3/'` is 634.31', `§` is a 5, `NB7°OS W` is N 87°05' W) and every repair lowers the
  confidence; `NE 1/4` and `1/2" REBAR` are fractions, not distances.

### What is never guessed

- A bearing over 90°, 60+ minutes or seconds, a chord longer than the diameter: refused
  with the reason, shown in the review, never folded into a plausible value.
- A bearing with no distance nearby stays incomplete and cannot be approved until a
  person enters the distance. Nothing is invented to complete a course.
- A curve needs two of R, Δ, L, CH, T; the rest are **calculated** and every stated
  element is checked against the solution. A curve with no chord bearing, no previous
  course to be tangent to and no radial bearing is refused, not drawn on a guessed side.
- A figure that does not close is drawn as written, with the misclosure vector and
  precision reported. Alternative readings that *would* close it are listed as
  suggestions. Nothing is adjusted.
- Record versus measured: `R1: N 89°42'18" E 1320.45'` and `M: N 89°42'21" E 1320.38'`
  are one course with two attributes (plus R2, R3 … each tied to its AFN / volume-page
  reference). Geometry is built from the measured value where the document gives one
  (or always from the record: Settings > Recorded Surveys > build from). A measured
  bearing is never mixed with a record distance. Both values are labelled and kept on
  the entity.
- Every value carries its basis — Recorded, Measured, Calculated, Inferred, Entered —
  and the label suffix and metadata say which. Inferred graphical information is never
  presented as a call.
- The office standard maps survey entities (Boundary, Lot Line, Right of Way,
  Centerline, Easement, Section Line, Quarter Section, Tie Line, Adjoiner) to layers,
  linetypes, Civil 3D general line/curve label styles, text styles, and monument blocks.
  Every name is checked against the open drawing: a missing layer withholds those
  courses, a missing style withholds those labels, a missing block lists the monument
  but does not draw it. Nothing is substituted for a look-alike or created from a
  guessed name (creating a missing layer is an explicit setting, still reported). The
  shipped defaults are FTF's generic layer names and **no** label style or block names;
  the PMX profiles carry the section with the office text style `Survey`, which was
  measured from the office template, and nothing that was not.

### Review

The review table shows Course (figure, order, id), Type, Bearing, Distance, Curve data,
Record source and basis, Object type, Confidence, Status and Flags. Selecting a row
outlines its source text on the page image. Calls under the threshold (0.85), with an
alternative reading, with disagreeing curve elements or incomplete are *NeedsReview*
and stop the build until approved, edited (strictly parsed) or rejected. "Order courses
from the page" chains the labels into lots by walking the sharpest right turn at each
corner (the planar-face rule), so it never wanders off along a neighbour's line; a lot
line labelled once serves both lots. Picking the start point of one lot places every
lot connected to it through shared lines so they meet at their common corners.

### Metadata and QC

Every reconstructed entity is stamped (project id, call id) and carries an `FTF_RECORD`
Xrecord with the document, recording number, sheet, survey type, lot/block, object type,
basis, record source and reference, measured and record bearing/distance, curve
elements, confidence, page and box on the document, and the build time. The whole
project (calls, review edits with who and when, build handles, closures) is stored in the
drawing and, by default, beside it as `<drawing>.<project>.ftfrecord.json`.

`FTFRECORDCHECK` reads the stamped geometry back and reports, in the spec's form:

```
Course K1: MATCH
Course K3: CAD 125.37 / Record 125.73 — REVIEW
Curve K8: Radius MATCH / Arc differs 0.02' / Delta MATCH / Chord MATCH — REVIEW
Lot 7 closure: 0.018' (1:30,000)
Shared boundary Lot 7 / Lot 8: MATCH
Nothing was adjusted. Discrepancies are for the surveyor to resolve.
```

### What is tested, what is not

`FieldCodes/RecordSurvey` has no Autodesk dependency and carries 190+ tests: bearing
spellings, DMS and distance units (feet, metres, chains, rods), curve reconciliation
from every pair of elements, placement, traverse and closure, misclosure suggestions,
record-versus-measured storage, shared lot lines and their discrepancies, OCR ambiguity
(second-pass alternatives, digit confusions, look-alike repair), standards mapping and
missing-style behaviour, no silent invention, source metadata round trips, the review
gate and edits, label text and collision-free placement, and QC verdicts. Two synthetic
Washington fixtures (a King County short plat with a curve table and a shared lot line;
a Pierce County Record of Survey with R1/R2/M calls) run end to end in
`tests\FieldCodes.Tests\Samples`. No project document is in the repository.

`FieldCodes.Cad\RecordCommands.cs`, `RecordDocumentReader.cs`, `RecordDrafter.cs`,
`Ui\RecordReviewForm.cs` and `Setup\RecordSurveyPage.cs` are, like the rest of the CAD
layer, **UNTESTED against a drawing**. In particular: the Windows OCR / PDF contracts
package loading inside Civil 3D 2024, `GeneralSegmentLabel.Create` for the Civil 3D
labels, and the review window. Headless (accoreconsole) runs take a sidecar path and a
start point from the command line and approve everything above the threshold, so a
LiveSmoke script can drive the whole build.

The Civil 3D side (`RecordCommands`, `RecordDrafter`, `RecordDocumentReader`, the review
window and the settings page) is compiled by `tests\CadCompileCheck` against stand-ins for
the AutoCAD, Civil 3D and Windows Runtime types, with the plugin's own C# 7.3 and
warnings-as-errors settings: `dotnet build tests/CadCompileCheck` on any machine. That
catches what a compiler catches and nothing more; the plugin remains UNTESTED against a
running Civil 3D until it is loaded there.

This is a production aid for a licensed surveyor. It makes no boundary determination;
the surveyor interprets the controlling evidence.

## Right of way from monuments: FTFROW

`FTFROW` is drafted in the same world as `FTFDRAWLINE` (its output is stamped
`DraftLine` / `DraftAnnotation`, so `FTFCLEAN` leaves it alone and `FTFDRAFTCLEAN`
manages it) but it works from picked **monuments** instead of typed courses. Pick
the monuments (survey points snap), fill in the popup, and it draws:

- the right-of-way line between the monuments, with bearing and distance;
- the **opposite right-of-way line** and the **centerline**, offset to the road
  side and drawn on their own layers;
- the right-of-way **width**, square across, labelled in the office wording;
- each monument's **note** (found / set) and, for an **offset monument**, the
  monument where it actually sits, a tie to the corner it marks, and the offset
  note ("5.00' OFFSET N 45°00'00" E TO CORNER" -- direction from the monument TO the corner).

Three modes:

| Mode | You pick | Each side is |
|---|---|---|
| **Run** | the monuments along one road frontage | offset by the road's width to the side the road is on, or to both sides when the monuments are on the centerline |
| **Block** | the corner monuments around a platted block, in order | a frontage (offset outward by *its own* road's width), a line across a road, or plain (no road). A corner can take a radius: a tangent arc with R, L, Δ and chord |
| **Intersection** | the corner monuments around the intersection | a line across a road. The distance between the monuments is checked against the record right-of-way width and a miss is reported |

Where it is configured: **FTF Settings > Right of Way**. The right-of-way *lines*
take the layer, linetype and bearing/distance format of the Drafting Lines type
named there, so a line from `FTFROW` and one from `FTFDRAWLINE` cannot disagree.
Centerline, monument, dimension, monument-note and road-name layers are set on the
page; a layer that is unset or missing from the drawing is refused with an
explanation, never created. Note wording ("FND {desc}", "{width}' R/W", the
offset note) is the office's to set; the shipped defaults are common platting
practice, not any one office's standard.

What it will not do, by design: it draws only straight sides between monuments
(a curved right-of-way frontage is two monuments and a chord for now), and
a corner radius treats the picked monument as the point of intersection. Where a
square dimension would land beyond the join of two offset lines (the inside of
a turn) it is skipped and reported instead of being forced. The geometry and
wording are in `FieldCodes.Row` and unit tested (`RightOfWayTests`).

**Verified headless (2026-10-24, accoreconsole against the stock NCS template):**
`tests\LiveSmoke\run-rowlots.ps1` drives all three modes and `FTFLOTS` — see
[Platted blocks](#platted-blocks-ftflots) below for what it checks.

## Platted blocks: FTFLOTS

`FTFLOTS` divides a block into lots. Pick the block's corners (survey points snap; or pick a closed
polyline of straight sides), then the popup asks for what a plat needs:

- **which side the lots front**, how many lots and how wide each is. Frontages must add up to that
  side; if they miss it you choose whether the first or last lot takes the difference or the tool
  refuses. A corner-lot allowance prefills wider end lots;
- **depth** -- blank runs the lots to the back of the block; a number leaves the rest undivided
  and says how much;
- **an alley** -- none, along the back (behind one row), or between two rows. A second row of lots
  can front another side, numbered on round the block;
- **where each lot drains** -- toward the front, rear, left, right, both ways (a ridge) or no arrow,
  for the whole block with any lot overridden, and the slope. Left and right are as seen standing on
  the front line looking into the lot;
- **building setbacks and utility easements** -- front, side, corner-lot street side and rear,
  drawn as an envelope inside each lot, with the plat notes written from the numbers used;
- **minimum lot area and frontage**, flagged lot by lot; and rounded block corners.

Lot lines are cut **square to the front**. Each lot is the block cut by half-planes, so a skewed or
trapezoidal block ends its end lots on the block's own sides and the lot areas still add up to the
block (a check that is run and reported). A line two lots share is one line, labelled with bearing and
distance; a line on the block's own boundary carries its distance only, because the right-of-way
drawing (`FTFROW`) already owns that boundary's bearings and is not drawn again unless the setting asks.

Configured at **FTF Settings > Platted Blocks**: lot lines and the alley take the layer and annotation
of Drafting Lines types (Lot Line, Right of Way); the setback, easement, lot-text, drainage and notes
layers are set there and never created from a name. The numbers the popup starts from are the office's;
the shipped defaults are zero, not an ordinance.

Not done: lines other than straight sides for the block (rounded corners excepted), a front that
bends at an angle point (divide it as separate fronts), cul-de-sac or flag lots, and corner sight
triangles. The geometry and wording are in `FieldCodes.Lots` and unit tested (`LotTests`).

## Verified headless: FTFROW and FTFLOTS

**2026-10-24, accoreconsole against the stock NCS template.**
`tests\LiveSmoke\run-rowlots.ps1` seeds the office layers and the monument and corner survey
points, then drives both commands through everything described above and checks what is
actually stamped in the drawing:

- **the refusal first.** One configured layer is renamed out of the drawing and `FTFROW`
  refuses before it asks anything — *layer "V-SURV-MONU-E" (set for monument symbols) is not
  in this drawing. FTF never creates office layers from a configured name.*
- **Run** — three monuments along one frontage, 60' to the right, the last an offset monument
  5.00' at N45E. The line through the corners landed on the monument-line layer, the opposite
  right-of-way line 60' south of it on the Right of Way type's layer, the centerline 30' south
  of that on the centerline layer, `60' R/W` dimensions with ticks at both ends only (the
  *Ends* policy), `FND MON IN CASE` / `SET REBAR AND CAP` notes, and the offset monument's
  cross, tie to its corner and `5.00' OFFSET N 45°00'00" E TO CORNER`.
- **Block** — a 200x100 block, 60' and 40' frontages, two plain sides, one 25' rounded corner.
  The arc came out tangent (centre 25' in from the corner both ways, `R=25.00' L=39.27'
  Δ=90°00'00" CH=N 45°00'00" E 35.36'`) with the two sides shortened to 175' and 75'.
- **Intersection** — a 100' ring of crossings, one given as 99.5' record width, which reported
  *the monuments are 100.00' apart across the road but the record right of way is 99.5'*.
- **FTFLOTS** — the same block as four 50' x 80' lots with a 20' rear alley: lot lines square
  to the front, the shared lines labelled with bearing and distance and the block's own
  boundary with its distance only, setback and easement envelopes per lot, `LOT n` / `4,000 SF`,
  drainage arrows (lot 3 overridden to the rear), `20' ALLEY`, `BLOCK A`, and the plat notes
  written from the numbers used.
- **ownership.** `FTFCLEAN` left all 156 objects alone; `FTFDRAFTCLEAN` Annotation removed all
  77 annotation entities and touched none of the 79 lines.

The log is `tests\LiveSmoke\rowlots\rowlots_out.log` and the drawing `outrowlots.dwg`.
`accoreconsole` has no window, so there the popups ask their questions on the command line
instead — the same questions in the same order, parsed as strictly, which is what the script
answers.

## Verified in Civil 3D: the FTFROW and FTFLOTS windows

**2026-10-24, full Civil 3D 2024, unattended.**
Because the headless run bypasses both popups, `Ui\RowForm.cs`, `Ui\LotForm.cs` and the two
settings pages had never been constructed. `tests\LiveSmoke\rowlots-ui\run-rowlots-ui.ps1`
runs them: it stages the harness in the trusted bundle folder, launches Civil 3D on a seeded
drawing, and drives the real windows through their own controls — 99 checks, all passing.

- **the Right of Way window** in all three modes: the monument grid carries the survey points
  the picks snapped to; choosing "on the centerline" swaps the width field out for the two
  offsets and back; a block shows the sides grid with the distance between each pair of picks
  and offers a corner radius, an intersection offers none and starts its sides as crossings.
- **it refuses on screen.** A width of "sixty" keeps the window open and says *The
  right-of-way width is not a number.*; Cancel draws nothing.
- **the Platted Block window**: 200' divides four ways to 50' each, the second row of lots
  appears and goes again with its box, and a per-lot drainage override reaches the drawing.
- **each window's work matches the headless run exactly** — 29, 35, 24 and 68 objects on the
  same layers, with the same wording.
- **both settings pages** build, fill themselves, offer only layers that are really in the
  drawing (46 of them, from the template), save back what they show, and report a text height
  of zero rather than taking it.
- **layout at 100%, 125% and 150% text**, off-screen copies of both windows checked for
  clipped controls.

The log is `tests\LiveSmoke\rowlots-ui\rowlots-ui.log`, with a screenshot of each window
beside it. Known and left alone: the Platted Block window's row of lot fields runs wider than
the window (1479px of content in 979px), so the body scrolls sideways to reach the lot depth
and the close-the-frontages choice. Every field is reachable; the row is set to wrap and does
not, because an AutoSize flow panel grows instead of folding.

Still needing a person: pick UX with osnaps, and the settings sections other than these two.

## Configuration ownership

Rules resolve through three levels; the highest existing one wins:

```
Factory defaults          ->  Office configuration        ->  Drawing override
(in the bundle;               (%APPDATA%\FieldToFinish\       (rules.json beside
 replaced by every deploy)     rules.json; survives            the drawing;
                               every update)                   per-job)
```

The factory file belongs to the installer and the installer only ever writes
inside the bundle — so **updating or redeploying FTF cannot overwrite user
configuration, by construction**. The rule editor never writes the factory file:
the first save creates the office configuration; a drawing override, when active,
is edited in place. The Point Features page states which level is active.

**Restore Factory Defaults** is an explicit button that copies the shipped rules
over the office configuration (previous version kept as `.bak`), after
confirmation, and only after the factory content itself validates — a damaged
factory file can never destroy a working office configuration. Reload means
reload; it never restores.

## Two files, two owners

| File | Holds | Written by |
|---|---|---|
| `config/rules.json` | **Grammar.** Codes, species, modifiers, ignore list, layers per rule, validation ranges. | People, deliberately. **Never by the plugin.** |
| `ftf-settings.json` | **Behaviour.** How the commands act — text style, sizes, offsets, masking, report location. | `FTFSETUP` |

They are separate because `rules.json` ships with the plugin and is replaced on
every build. Settings a user edits have to live somewhere a rebuild cannot reach.

Settings are searched for in this order, first hit wins:

1. beside the drawing — per-job overrides
2. `%APPDATA%\FieldToFinish\` — this user, every drawing
3. the plugin folder — shipped defaults

With no settings file anywhere, the legacy keys still in `rules.json` seed one, so
upgrading does not change how anyone's drawings come out. Those keys are marked
`SETTINGS-MOVED` and have no effect once a settings file exists.

## FTFSETUP

One window for the whole suite, seven tabs: General, Trees, Drip Lines, Labels,
Draw Order, Cleanup & Re-run, and a read-only Field Codes tab. Each tab names the
commands it affects. The status strip shows the Civil 3D build, annotation scale,
whether the rules loaded, and which settings file is in use.

Dropdowns are populated from the open drawing — text styles, layers — so nobody
types a layer name slightly wrong. Text styles with a **fixed height** are flagged,
because AutoCAD ignores a height set in code for those.

The Field Codes tab is deliberately read-only, with a live "try a description" box.
Grammar stays a considered file edit: it is what decides whether a mistyped code
fails loudly.

**Adding a tab for a new command** is one `ISettingsSection` in `FieldCodes` and one
`SetupPage` in `FieldCodes.Cad`, registered in one list. `SetupForm` knows nothing
about any individual page.

## Label collision boxes are measured, not estimated

There is no character-width factor any more. The old estimate — `chars × height ×
0.6` — is only right for one font; with a wider style the reserved boxes cleared
each other while the drawn glyphs overlapped. That is exactly what showed up in the
first Civil 3D test.

Now the text entity is created with the configured style and height, added to the
drawing, and its `GeometricExtents` measured. The offset between the insertion
point and the extents' lower-left corner is recorded, and the final position is set
so the **extents** land where the placer decided, not the insertion point.
`LabelPlacer` is unchanged — it always took width and height as inputs; it is just
fed the truth now.

Every command deletes what it owns before drawing, so running one twice is a
no-op rather than a duplicate. Ownership is XData under `FTF_FIELDTOFINISH` —
nothing is ever deleted by layer or block name.

## Needs verifying

Nothing below has run. In rough order of how badly a mistake would hurt:

1. **`FTFTREES` end to end** on a real point group — that `CogoPoint.RawDescription`
   reads what the surveyor typed, and that `PointNumber` ordering is what you want.
2. **Re-run safety.** Run every command twice and confirm the entity count is
   identical the second time. This is the whole premise of the XData design.
3. **Drip trimming visually.** The maths is unit tested, but confirm arc direction
   and that `Arc(centre, normal, radius, start, end)` produces the arc you expect
   rather than its complement.
4. **Draw-order banding.** `DrawOrderTable.MoveToTop` is called once per band,
   ascending. Confirm labels really finish on top and that masks do not hide the
   linework they sit above.
5. **Label boxes at plot scale.** `EstimateTextWidth` uses a character-width factor
   rather than measuring the string; check the boxes are not badly over- or
   under-sized at your usual annotation scale, and tune `characterWidthFactor`.
6. **The hand-moved-label rule.** Move a label, re-run `FTFLABELS`, confirm it stays
   put and that the others route around it. `movedTolerance` is in drawing units.
7. **net8.0-windows / Civil 3D 2026.** No 2026 install was available. The csproj
   drops that target with a warning when it cannot find one, and the 2026 HintPaths
   assume the 2024 layout (three assemblies in the install root, `AeccDbMgd` under
   `C3D\`). The TFM is `net8.0-windows`, not plain `net8.0`, because the settings
   dialog is WinForms. Confirm both before trusting a 2026 build.

9. **`FTFSETUP` dialog.** Shown via `Application.ShowModalDialog` so AutoCAD owns
   the modal loop. Confirm it is modal to the editor, that a bad value is refused
   without writing, and that the `.bak` is created.
8. **Wipeout masks.** `Wipeout.SetFrom` needs a closed boundary; confirm the frame
   is invisible and the mask does not print.

Newtonsoft is not a problem on 2024: the shipped `Newtonsoft.Json.dll` next to
`acad.exe` is **13.0.3**, which is exactly what `FieldCodes.csproj` pins, so
option 1 above already holds. Re-check if either side moves.

## FTFCOMPARE: survey comparison for ACC deliverables

We receive a consultant's existing-conditions base map and we have our own survey of
the same ground. The deliverable plots THEIR information plus only what OUR survey adds.
`FTFCOMPARE` decides, feature by feature, which of our objects the consultant already
shows (those stop plotting), which they lack (those keep plotting), which lines they show
only in part (the represented stations stop plotting, the rest keeps plotting), and
which it cannot decide (those are shown for review, untouched). It never redraws survey
geometry, never edits the consultant's data or any shared block definition, verifies
every change it makes, and reverses everything from its own record.

### Workflow

```
open a WORKING COPY of the survey drawing (the original is never written)
  FTFCOMPARESOURCE   consultant data = the drawing's xrefs (default) or a separate file
  FTFCOMPAREPROFILE  pick / save the consultant's mapping profile (Default to start)
  FTFCOMPAREAREA     optional: the comparison limits (a picked polygon or closed polyline)
  FTFCOMPARE         the window: Run comparison -> review the exceptions -> Apply approved changes
  FTFCOMPAREQC       the final checks; FTFCOMPARESAVE refuses while a blocking one fails
  FTFCOMPARESAVE     writes <name>-ACC.dwg + CSV + QC text to the delivery folder
```

Each command is also usable without the window. `FTFCOMPARERUN All|Select|Window`
compares everything, a selection, or a crossing window. `FTFCOMPAREDECIDE <handle>
AcceptMatch|KeepPlotting|MakeNonplot|AcceptPartial|Exclude|None` (or `Confident`) records a
decision from scripts. `FTFCOMPAREBATCH <manifest.json>` processes many sites (kept simple
on purpose; prove the single-site workflow first).

### Compile, load and test on one real drawing

See `FTFCOMPARE-SETUP.md` at the repository root: what to pull, how to build against the
installed Civil 3D 2024 assemblies, install, run the smoke test, run the comparison on a
project drawing, judge the results, and restore. `deploy\build-install-test.ps1` does
the build, the unit tests, the install and the smoke test in one run and stops at the
first failure with the reason. Framework split: the plugin targets .NET Framework 4.8
(Civil 3D 2024's runtime), the engine netstandard2.0; the .NET 8 SDK is only the
compiler, and the script verifies the built output and the host release before
installing.

### How it decides

The engine lives in `FieldCodes.Compare` (no Autodesk types; 110 unit tests) and works on
geometry the CAD reader extracts first; the drawing is modified only afterwards, through
`CompareLayerManager`, from the approved plan.

| Step | What happens |
|---|---|
| Read | Model space of the working copy; the consultant's xrefs (nested references followed, every transform applied) or a separate file. Only what is SHOWN counts: an xref insert that is invisible or on a frozen / off / no-plot layer makes the data incomplete; content on layers frozen, off or no-plot in the drawing (the `XREF\|LAYER` records) is left out and counted; a clipped xref is cut to its clip boundary (inverted clips honoured; the boundary follows an insert moved after clipping), so linework outside the clip is split off and cannot cover ours. Survey objects that do not plot as the drawing stands are left out too. |
| Classify | Every object is classified from code (`SDMH 12` -> STORM-MH), block name, key words and layer, per the profile. Agreeing evidence raises confidence; disagreement (storm code on a sanitary layer) marks a conflict that sends any match to review. No evidence = NOT EVALUATED, reported with the unmapped layer, never "missing". |
| Points | Nearest consultant feature of the SAME category within the category tolerance (poles 2 ft, monuments 1 ft ... per profile). Same system but another type (CB where we shot an MH) = review. Another system (sanitary vs storm) is never a candidate. Real elevations on both sides that differ beyond tolerance = DISCREPANCY. |
| Linework | Our line is sampled at every vertex and at a step that shrinks for short lines (1 ft, down to 0.25 ft); a sample is covered when a same-category consultant line runs within the offset tolerance (1 ft) and within 15 degrees of our direction. Every change of coverage between two samples is bisected to 0.1 ft, and each interval carries its end POINTS. A gap in their drafting is bridged only where our line runs straight across it; an uncovered stretch shorter than 3 ft is absorbed only while it stays within two tolerances of their line (a near miss); a short stretch that swings farther away, a jog, a return, a stub, keeps plotting however short. Arcs are chorded at no more than 10 degrees per chord, so tight returns compare correctly. 97 % covered with no uncovered stretch = matched; otherwise PARTIAL with the uncovered stations, or MISSING. |
| Annotation | Each label belongs to the nearest feature within 15 ft (or its attribute block). Facts are parsed: RIM, IE with direction, pipe size, material, structure id; otherwise the normalised wording with synonyms. A fact the consultant also shows at the matched feature = duplicate; a fact they lack = keeps plotting; a different value = DISCREPANCY. Civil 3D labels (pipe, structure, note, spot...) are read through their text components; one whose text cannot be read is OPAQUE: it proves a label exists there, so our annotation beside it goes to review, never to nonplot. Our own unreadable labels are NOT EVALUATED and keep plotting. |
| Limits | A polygon from `FTFCOMPAREAREA`, else the consultant data's own extent plus 10 ft. Survey beyond the limits is "outside limits", never missing; what to do with it is a profile setting (Review by default). |
| Incomplete sources | An unloaded, unresolved or hidden xref makes the consultant data INCOMPLETE: nothing is called missing (NOT EVALUATED instead) and the final QC blocks the deliverable. |

Automatic acceptance needs confidence >= 0.75 (profile `autoAcceptConfidence`): clean
geometry and unconflicted classification. A match whose offset is above 60 % of the
tolerance is shown for review as a possible parallel feature.

### Discrepancies: clouded in the drawing, listed in the report

A counterpart found within the 2 ft search radius but more than 0.5 ft away
horizontally (`tolerances.pointDiscrepancyFeet`), or differing by more than 0.5 ft
vertically (`tolerances.elevationFeet`; their rim can come from a block attribute or a
label beside the symbol), is a discrepancy: ours keeps plotting, the comparison draws a
revision cloud around it and a note beside it ("FTFCOMPARE DISCREPANCY <handle>
(<category>): 0.95 ft from the consultant Handhole, more than the 0.50 ft tolerance")
on the plotting layer `PMX-COMP-REVIEW` (`review.*` in the profile), and the CSV report
lists it with the same note. A category can loosen that distance with its own
`pointDiscrepancyFeet` (trees, say), never beyond its search radius. The marks are
stamped as the comparison's own, redrawn on every run and erased by
`FTFCOMPARERESTORE`; the final QC warns that they will plot.

### What changes in the drawing, how it is verified, and how it comes back

- Nonplot layers. Default (`nonplotLayers.mode` = `Suffix`): each object goes to its own
  layer's name plus `-NP` (`V-UTIL-POWR-POLE` to `V-UTIL-POWR-POLE-NP`), created no-plot
  with the source layer's colour, linetype and lineweight where missing; an existing
  `-NP` layer is forced no-plot, thawed and on. Alternative (`Fixed`): five named layers
  by kind, `PMX-COMP-NP-POINT`, `-LINE`, `-BLOCK`, `-TEXT`, `-UTILITY`, colour 8.
- Every change is checked the moment it is made with the same resolver the final QC
  uses (`PlotVisibility`: visibility, layer, block content layer by layer with layer 0
  following the insert, attributes, point style and label). An object that would still
  plot, or whose state cannot be determined, is put back on the spot, reported as
  "NOT changed" with the reason, shown in the window, and blocks the final QC until a
  person decides. Only verified changes are recorded.
- A plain object moves to its nonplot layer. Block attributes move with their block.
- A COGO point moves to the nonplot layer AND its label is hidden on the point
  (`IsLabelVisible`), because a label style may fix its own layer. If the point style
  fixes the marker's layer (anything but 0), the point gets the comparison's own point
  style `PMX-COMP-NP`, whose marker and label sit on layer 0 (so they follow the point's
  nonplot layer) in suffix mode, or on the point nonplot layer in fixed mode; with
  `nonplotLayers.restyleFixedMarkerPoints` false nothing is restyled and such a point is
  reported as NOT changed for a person to handle. A point whose primary point
  group overrides the point style is refused (the group would win): remove the override
  or exclude the point.
- A block whose definition draws on its own layers is pointed at a nonplot twin
  definition `PMX-COMP-NP$<name>` (a deep clone with every entity on a nonplot layer: its
  own layer's `-NP` twin in suffix mode, the block nonplot layer in fixed mode; layer 0
  stays 0 and follows the insert; nested blocks twinned too). The shared definition is never edited, so other inserts are
  untouched. A dynamic block in that situation is hidden instead (its displayed geometry
  lives in an anonymous definition AutoCAD manages).
- A partly represented line moves to nonplot INTACT and the uncovered stations are copied
  back onto its original layer with `Curve.GetSplitCurves` at the closest points to the
  interval's recorded end points (arcs stay arcs, elevations stay, colour / linetype /
  lineweight / plot style / transparency copied, layer the original's). Each piece is
  checked against the expected end points. Closed polylines and Civil 3D figures get a 3D
  polyline sampled along the real curve and the record says so (QC warns).
- `FTFCOMPARERESTORE` puts every recorded object back (layer, visibility, block
  definition, point style, label visibility), erases the split copies plus any copy no
  record owns, and erases twin definitions nothing references. Decisions are kept.
  Re-running the comparison restores first, so nothing accumulates; applying twice changes
  nothing the second time (a change already recorded with the same stations is skipped).
- The deliverable is always a new file: `FTFCOMPARESAVE` refuses a path equal to the
  working drawing, the consultant file or any xref. Source files are only ever read.
- The applied record, the decisions, the profile choice and the source live IN the
  drawing (named object dictionary `FTF_FIELDTOFINISH\COMPARE`); the full result is
  `<name>.ftf-compare-result.json` beside it, the report `<name>.ftf-compare.csv`, and
  decisions are mirrored to `<name>.ftf-compare-decisions.json`. A decision is re-applied
  only while the feature's fingerprint (geometry, layer, description) is unchanged.

### Profiles

`%APPDATA%\FieldToFinish\compare-profiles\<name>.json`; the factory `Default.json` ships
with the plugin (`config\compare-profiles`, generated from `CompareProfile.CreateDefault()`
and pinned by `ShippedProfileTests`). A `ftf-compare-profile.json` beside a drawing wins
for that job. The survey side of every category also takes the layers from `rules.json`'s
`lineFeatures`. The consultant side is deliberately broad wildcards: the first run lists
every unmapped consultant layer; `FTFCOMPAREPROFILE SaveAs` makes the consultant's copy
and `Edit` opens the JSON. Once mapped, every site from that consultant reuses it.

### Final QC (FTFCOMPARESAVE refuses on a blocking failure)

SOURCES (consultant data complete), DISCREPANCIES, REVIEW, NOT-EVALUATED (profile decides
whether each blocks), NONPLOT-LAYERS (exist, do not plot), MISSING-PLOT (every feature
meant for delivery, and every split copy, REALLY plots), APPLIED (every planned change
made and verified; refused ones listed with their reason), DUPLICATES-NONPLOT (every
matched duplicate really does not plot: layer, visibility, block content, point style
and label resolved), PLOT-VERIFIED (no object whose plot state could not be determined),
PREVIOUS-RUNS (no split copies from an earlier run), XREFS (all resolve), OUTPUT
(delivery folder writable). Non-blocking warnings list sampled split copies and hidden
dynamic blocks.

### FTFTRIM: trim the aerial base inside our limits and flag the seams

For the ACC merge the base drawing on the site is aerial; our finished survey replaces it
inside each site's limits of survey. In the aerial drawing (a working copy), attach the
finished site drawing(s) as xrefs (or point at one file), then `FTFTRIM`:

- Our limits of survey are the closed polylines on a limits layer (`trim.limitsLayers`,
  `V-SURV-LIMIT*` and the like) found inside the survey xrefs, transformed by their
  insert, or in the aerial drawing itself; or pick them (`Select`). Read regardless of
  plot state, since limits usually sit on a no-plot layer.
- Inside a footprint every aerial object goes to its own layer plus `-NP` (same rule and
  record as the comparison). An aerial line crossing the limit is cut exactly at the
  edge: the original goes nonplot intact and the outside part is copied back plotting
  (`GetSplitCurves`, verified against the expected end points). An outside remnant
  shorter than `trim.minPieceFeet` goes nonplot with the rest and the report says so.
  Hatches, dimensions and unreadable objects inside a footprint are not trimmed and are
  listed as "check by hand". Nothing is erased.
- At every cut the seam is checked: our line of the same feature type within
  `trim.seamSearchFeet` (2 ft), its offset at the cut, the elevation difference when
  both sides carry real elevations, and the direction. Beyond 0.5 ft horizontally or
  vertically it is a discrepancy; no continuation, an unknown feature type on either
  side or a direction difference above 20 degrees sends it to review. Every seam that is
  not aligned gets a revision cloud and a note on the review layer; every seam is listed
  in `<name>.ftf-seams.csv`, every trimmed object in `<name>.ftf-trim.csv`.
- `FTFTRIMRESTORE` puts every aerial object back exactly, erases the outside pieces and
  the marks. Running `FTFTRIM` again restores first, so nothing accumulates. Our survey
  is never written: the fix for a bad seam is to adjust the aerial to ours, by hand for
  now (an assisted "move the aerial end to our end" pass is the next step).

The trim's split copies and marks are stamped apart from the comparison's, so each
tool's restore takes only its own.

### Verified and not verified

Verified here (`dotnet test`: 1,051 tests, 1,047 passing on the Linux build machine
where the 4 pre-existing Windows-path tests cannot run; 110 cover the comparison):
every scenario of the first version plus coverage boundaries refined to 0.1 ft with end
points, a 2 ft jog and a 2.5 ft end stub kept while near misses are absorbed, drafting
gaps bridged only when our line runs straight across, 1.5 ft lines decided, tight returns
across two chordings, a crossing line not covering the crossing, polygon clipping of
polylines (inside, inverted, multiple pieces) and clipped consultant geometry unable to
cover ours, unreadable consultant labels sending our annotation to review, unreadable
survey labels not evaluated, QC failing on real plot state (a point style or block
definition that still plots) and on undeterminable state, split copies that must plot,
the applied record for every method, interval end points in the plan, and identical
plans on repeated runs.

NOT verified: nothing in `FieldCodes.Cad\Compare` has run in Civil 3D. The code compiles
against a stub of every AutoCAD and Civil 3D member it uses, which does not prove the
members behave as expected. Run the steps above. What to watch, in order of consequence:

1. `CogoPointPlotting.cs`: `PointStyle.GetDisplayStylePlan(PointDisplayStyleType.Marker)`
   and `DisplayStyle.Layer / Visible` are the members used to read where a point style
   puts its marker and to build the `PMX-COMP-NP` style; `CogoPoint.StyleId`,
   `LabelStyleId`, `IsLabelVisible` are used directly; the point-group override is read by
   reflection (`PrimaryPointGroupId`, `IsPointStyleOverridden`, `PointStyleId`) and a
   missing member reads as "could not determine", which blocks. The smoke test seeds a
   fixed-layer style and checks the nonplot style is assigned and restored.
2. `BlockTwins.cs`: `Database.DeepCloneObjects` into the twin definition and
   `BlockReference.BlockTableRecord` reassignment; the smoke test checks the twin is used
   and purged.
3. `CompareDrawingReader.ReadClip`: the spatial filter boundary through
   `ClipSpaceToWorldCoordinateSystemTransform` and `OriginalInverseBlockTransform`. The
   smoke test clips the xref at x = 1150 and checks the fence beyond it keeps plotting;
   an xref clipped and then MOVED is not seeded, check one by hand.
4. Civil 3D label text through `GetTextComponentIds` / `GetTextComponentText`
   (reflection); if the text comes back with unresolved fields the label is opaque and
   our annotation beside it goes to review, which is the safe outcome.
5. `GetSplitCurves` pieces are checked against the expected end points; a mismatch falls
   back to a sampled polyline and the QC warns.

Known limits of this version: hatches and dimensions are listed as not evaluated;
curved pipes in pipe networks are read as straight; a COGO point whose point group
overrides its style is refused rather than changed; model space only (layout viewports
with per-viewport frozen layers are not evaluated); the batch runner applies only
confident results and leaves every review item for the window.
