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
Install or Update FieldToFinish.bat   what a surveyor double-clicks
Install FTF.bat      the older name; does the same
Restore previous FieldToFinish.bat, Check FTF.bat, Repair FTF.bat, Uninstall FTF.bat
install.ps1          the work; the batch file stages it locally and calls it
check.ps1, ftf-common.ps1   the diagnostic report, and the checks both share
README.txt           three steps, and what to do when something is wrong
version.txt          build date, branch and commit -- printed while installing
Bundle\              PackageContents.xml + Contents\2024\ (the plugin itself)
```

`UPDATEFTF -Publish` publishes it, after every check passed, as the approved release
on the server, and people double-click `Install or Update FieldToFinish.bat` there. It stages the
files to `%TEMP%` first, so a network folder and a network hiccup are both fine, verifies every
file, and refuses to install while Civil 3D is open. The whole process, the folder layout and
rollback are in [docs/network-deployment.md](docs/network-deployment.md).

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

Civil 3D builds FTF's four ribbon tabs at startup, one per kind of work:
**FieldToFinish** (process, points and symbols, labels, trees, utility dips, settings,
reports), **Survey Drafting** (spot elevations, contours, line labels, drafting, sheets),
**Boundary** (recorded surveys, calculations and closure, right of way and lots, record
comparison) and **Easements** (create, legal descriptions, exhibits, closure
verification). Every command keeps its name; the full table is in `docs/ribbon.md`,
built from `FieldCodes.Commands.RibbonLayout`, which tests check against the registered
commands.

## Build status

Everything compiles. `FieldCodes` and `FieldCodes.Cad` (net48, against the local
Civil 3D 2024) build clean; 114 unit tests pass.

```bash
dotnet test FieldToFinish.sln
```

### Verified against a real drawing

A real project base drawing (identifiers withheld), 765 CogoPoints:

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

### Reversing modifications — PARTLY SOLVED

FTF does two different things:

| | Reversible? |
|---|---|
| **Entities FTF creates** (labels, driplines, tags, masks, the schedule) | Yes — XData ownership, `FTFCLEAN` erases them |
| **Marker rotations set by `FTFORIENT`** | Yes — the rotation before FTF's first change is kept on the point (XData `FTF_ORIENT`), `FTFORIENTRESET` puts it back, and a marker turned by hand since is kept, not overwritten |
| **Marker rotations set by `FTFPOINTS` from a field azimuth (`SN 135`)** | **No** — the prior value is not recorded |

Re-running `FTFPOINTS` is safe because the rotation is idempotent: the same value is
written again. But nothing can put a marker it turned back. The same record `FTFORIENT`
keeps would solve it; until then, `FTFORIENT` (which also reads `SN 135` as a measured
direction, and turns it to the same value) is the reversible way to orient signs.

## Layers: the PMX template

FTF draws to the PMX layer template, read from a completed PMX Civil 3D drawing (588 layers):
`V-<GROUP>-<FEATURE>-E`, `-E` meaning existing, each family with `-PNTS-E`, `-SYMB-E` and
`-TEXT-E` companions (`V-UTIL-POWR-E`, `V-UTIL-POWR-SYMB-E`, `V-UTIL-POWR-TEXT-E`).

| What FTF draws | Layer |
|---|---|
| Pole, junction box, guy anchor labels | `V-UTIL-POWR-TEXT-E` |
| Light pole labels | `V-UTIL-LITG-TEXT-E` |
| Hydrant, water valve / gas valve labels | `V-UTIL-WATR-TEXT-E` / `V-UTIL-NGAS-TEXT-E` |
| Catch basin, area drain / manhole, vault labels | `V-UTIL-STRM-TEXT-E` / `V-UTIL-STRC-TEXT-E` |
| Sign / mailbox / post labels | `V-SURF-SIGN-TEXT-E` / `V-SURF-MBOX-TEXT-E` / `V-SURF-POST-TEXT-E` |
| Control labels | `V-CTRL-TEXT-E` |
| Tree labels / drip lines | `V-SURF-VEGE-TEXT-E` / `V-SURF-VEGE-E` |
| Bench outline / label | `V-SURF-LAND-E` / `V-SURF-LAND-TEXT-E` |
| Line labels | the line's own family text layer when the drawing has it, else `V-TEXT-E` |
| Tag numbers | the feature's own label layer |
| Tree schedule, control table | `V-TEXT-TABL` |
| Contours / spot elevations | `V-TOPO-CONT-E`, `V-TOPO-CONT-TEXT-E` / `V-TOPO-SPOT-PNTS-E`, `V-TOPO-SPOT-TEXT-E` |

Draw order and label placement use the same template. **Protected** (labels never cover):
`V-UTIL-*`, `V-SIGL-*`, `V-CTRL-*`, `V-SURF-SIGN-*`, `V-SURF-*-SYMB-E`, `V-PROP-*-SYMB*`.
**Maskable** (labels may sit on and mask): `V-TOPO-CONT-*`, `V-TOPO-MAJR-E`, `V-TOPO-MINR-E`,
`V-SURF-VEGE-E`, `V-TINN-*`, `V-ROAD-*`.

Earlier builds used names the template does not have (`V-UTIL-POWR-TEXT`, `V-SIGN-TEXT`,
`V-SITE-TEXT`, `V-TREE-TEXT`, `V-TREE-DRIP`, `V-CTRL-TEXT`, `V-LINE-TEXT`, `V-TREE-TAG`,
`V-TREE-TABL`, and the old draw-order lists). A settings file or a rules file saved by an
earlier build is upgraded as it loads (`FieldCodes.Settings.PmxLayers`): only those exact
names, and a draw-order list only when it is still exactly the old default, so anything the
office chose itself is kept. In a drawing labelled by an earlier build, the next FTFLABELALL
(or FTFLABELS, FTFTAGS, FTFDRIP) redraws its labels on the new layers; labels moved by hand are
kept where they are, on their old layer, and the emptied old layers can be purged. Easement,
exhibit, record-survey and sheet layers are unchanged.

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

## Installing and updating: UPDATEFTF

`C:\dev\UPDATEFTF` builds the approved `integration` branch, tests it, installs it where Civil 3D
loads it at startup, and proves it in Civil 3D before keeping it; type `FTFVERSION` in Civil 3D to
see which build is running. See [DEPLOY.md](DEPLOY.md).

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
| `FTFLABELALL` | Label Everything: every recognised line (gates included), every point, then draw order; lists what it could not label | yes |
| `FTFLABELBETWEEN` (`FTFB`) | A label centred between two picked edges, or two sides of one line (a U-shaped hedge); any linework, 3D polylines included; the edges are only read | yes |
| `FTFLABELDRIVEWAY` (`FTFDWY`) | Between-edges label for a driveway: the first edge's layer type plus DWY (`ASPH DWY`) | yes |
| `FTFGATES` | Pairs gate posts (FGP) along the fences and labels each gate between its posts with its width (`6' GATE`); unsure pairs go to the review list. See "Gates" | yes (labels moved or reworded by hand are kept; never duplicates) |
| `FTFGATE` | Pick two gate posts: confirms or changes a pair, labels the gate between them | yes (replaces its own) |
| `FTFREVIEW` | The review list: everything FTF could not decide, with zoom, fix, settings, recheck, not applicable and accept. See "Review list and Drafting QC" | n/a |
| `FTFREVIEWCHECK` | Rechecks everything the review list is built from: codes, linework continuity and point accountability. Writes only the review list, the point ledger, the non-plotted proposal previews and the reports | yes |
| `-FTFREVIEW` | The review list on the command line: List, Approve a proposed correction, NotApplicable / Accept with a reason, Reopen, Zoom (for scripts and the keyboard) | n/a |
| `FTFQC` | Marks the drawing Drafting QC Complete, refused while required review items are open | yes |
| `FTFBENCH` | A bench's outline and size label from its two end shots and the depth in their notes | yes (replaces) |
| `FTFVERSION` | Which FTF build is running, where it was loaded from, what is installed, and whether it is the latest approved build (see DEPLOY.md) | n/a |
| `FTFORIENT` | Turns the symbols Civil 3D already drew (COGO markers) to face the way the features do; Preview, Apply or All. See "Orienting symbols" | yes (and kept: a marker turned by hand is not overwritten) |
| `FTFORIENTRESET` | Puts every marker `FTFORIENT` turned back as it was and removes its QC graphics | yes |
| `FTFORIENTPOINT` | One symbol's decision: the rotation now and before FTF, the direction FTFORIENT would set, the reference line and its distance, the evidence and confidence; then Zoom, Preview, Apply, Override, Recheck or Reset. The review list's Fix runs it | yes (Apply and Override are kept by later FTFORIENT runs; Recheck hands the point back) |
| `FTFWHAT` | What a selected object is and what made it: a Civil 3D object (point, style, label style, figure), an FTF object and the command that made it, a copy of FTF output, or neither. For a text, multileader or block, its height or scale and what that comes to on paper at the annotation scale (oversized annotation shows here). With nothing selected, counts the whole drawing by owner and says which annotation scale FTF draws at and how tall 0.08 in text comes out. Changes nothing | n/a |
| `FTFRESTYLE` | Brings FTF's existing labels, tags and line labels up to the current text and leader styles, in place; Preview first, Apply to change. See "CAD standards and styles" | yes |
| `FTFLAYERUPDATE` | Lists every layer FTF uses whose properties differ from its layer standard, then asks before changing anything | yes |
| `FTFCHECKSTANDARDS` | Checks every layer, linetype, text style, multileader style and block the settings name against the drawing, with fixes on request | n/a |
| `FTFCONTOUR` | Contour polylines at exact elevations off a TIN surface (one, a list, or a run), optionally clipped to a boundary and labelled; the surface is only read | yes (replaces its own at the same surface and elevation) |
| `FTFRECORD` | Recorded plat / Record of Survey in, reviewed calls, reconstructed geometry out — see "Recorded surveys" | yes (FTFRECORDREBUILD) |
| `FTFRECORDCHECK` | Drawing versus the record; flags only | yes |
| `FTFRECORDLABEL` | Places or refreshes the record labels | yes |
| `FTFRECORDSOURCE` | Where a reconstructed line came from | n/a |
| `FTFRECORDREBUILD` | Reopen the review, rebuild after edits | yes |
| `FTFCOMPARE` | Survey-vs-consultant comparison window: what they already show goes nonplot, what they lack keeps plotting. See "FTFCOMPARE" below | yes |
| `FTFCOMPARERUN` / `FTFCOMPAREAPPLY` / `FTFCOMPARERESTORE` / `FTFCOMPAREQC` / `FTFCOMPARESAVE` / `FTFCOMPAREBATCH` | The same workflow from the command line, step by step | yes |
| `FTFLOTS` | Divides a platted block into lots: lot lines with bearings/distances, alley, setbacks, easements, numbers and areas, drainage arrows (see below) | n/a |
| `FTFROW` | Right of way from picked monuments: lines, opposite line, centerline, width dimensions, monument notes — runs, blocks, intersections (see below) | n/a |

## Labels from field notes: poles and signs

A crew rarely shoots a pole as just `PP 1234`. It is `PP 1234 PSE XFMR TEL CATV`,
or the same words in another order. A rule can carry a **notes vocabulary**
(`notes` in `rules.json`): each term turns what the crew writes into the label's
wording and puts it in a named group, and the label format reads the groups in a
fixed order. The order the notes were typed in never changes the label.

```
PP 1234 PSE XFMR TEL CATV      POLE 1234 PSE
PP CATV PSE TEL 1234 XFMR      W/ XFMR, TEL, CATV        (the same label)
PP 45FT WD 1234 SL             POLE 1234 WOOD 45'
                               W/ LUMINAIRE
PP 1234 XFMR DEAD              POLE 1234 (DEAD)
                               W/ XFMR                    (modifiers still compose)
SN 135 SPD 25 MPH WP           SPEED LIMIT 25 SIGN (WOOD POST)
SN 90 NO PARKING ANYTIME       NO PARKING ANYTIME SIGN
SN 270 DEAD END                DEAD END SIGN              (wording, not the DEAD modifier)
```

- **Terms** are regexes over whole words, tried in the order listed. A term may
  span words (`NO\s+PARK(?:ING)?`) and capture values for its wording
  (`SPEED\s+(?<mph>[0-9]+)` → `SPEED LIMIT {mph}`). The rule's own terms are read
  before the global modifiers, so DEAD/REM/PROT still work on a pole while a
  sign's DEAD END stays wording.
- **Reading order.** Within a group, values read in the vocabulary's order
  (`Vocabulary`, the default) or as written (`AsShot`, for sign wording); the
  group's `joiner` goes between them. `{notes}` is every group in order.
- **Unknown words are never dropped.** `Flag` (the pole) labels them as written on
  the last line and lists the point in the exception report so the word can be
  added to the vocabulary; `Keep` (the sign, whose wording is free text) labels
  them quietly; `Refuse` makes the point an error, as an unknown modifier is.
- **The format reads top to bottom.** Text in `[square brackets]` appears only when a
  placeholder inside it has a value, `\P` starts a new line, and a line left empty
  is dropped, so a pole with no attachments has no `W/` line. A modifier's suffix
  goes on the first line. A plain one-line format expands exactly as before.
  FTFSETUP > Point Features shows the format one line per row, and its test box
  shows the label any description would get.

The shipped pole and `SN` sign vocabularies are a **starter set** (owners, poles'
material and height, transformer, TEL, CATV, fiber, luminaire, guy; NO PARKING,
SPEED LIMIT, YIELD, DEAD END, ONE WAY, posts). Confirm them against the office
code sheet and extend them in `rules.json`. Any other rule (light poles, junction
boxes) takes a vocabulary the same way, with no code change.

**Notes in the data collector's note field.** When the crew shoots the pole as just
`PP` and writes `1234 PSE XFMR` in the note field, the point's raw description is
only the code; the note arrives in a Civil 3D point property or the Full
Description, depending on how the points were imported. FTF reads it from there and
treats it as if it had been typed after the code, so the pole still gets
`POLE 1234 PSE / W/ XFMR` rather than `PP`. Where it looks (Settings > Point Labels >
Read notes from):

- **Blank (automatic):** a point property whose name says note -- Note, Notes, Field
  Note, Comment, Remarks, Memo, Description, Description 1/2 -- then the Full
  Description when it adds to the code (`PP 1234 PSE`), or when it differs from point
  to point. A Full Description that reads the same on every point of a code (`POWER
  POLE` on every PP) is a description key's wording, not a note, and is not read; nor
  is one built from the code's own `$` parameters.
- **Full Description**, **None**, or a point property's name: that place only.

The note is read only where the code takes notes: a bare code (PP, SN, CB...) or a
rule with a notes vocabulary. A tree's grammar has no place for a remark, so trees
are never changed by one. A bare code the office labels with its code (CB, SSMH,
WFH...) is labelled with its note when no rule reads it. A sign may be shot without
its azimuth (`SN STOP`, or `SN` with `STOP` in the note): it is labelled `STOP SIGN`
and the marker is left as Civil 3D placed it. `FTFLABELS` says how many notes it read
and from where.

## Benches: FTFBENCH

A bench is shot as two points, one at the middle of each end, with its depth (and
often its height) in the notes. `FTFBENCH` takes the two points, or the two-point
line Civil 3D drew through them, and draws the outline: the shot-to-shot line
offset half the depth to each side, ends closed, on `bench.layer`. The label says
its size, `BENCH 3'x1.5'` (`bench.labelFormat`; `{length}`, `{depth}`, `{height}` in
feet, `[ ]` and `\P` as above).

- The depth is read from either end's notes: `D1.5`, `D 1.5`, `D=1.5`, `DEPTH 1.5`,
  `1.5D`, `1.5' DEEP`, `18IN D`; the height the same way with `H`, `HT`, `HEIGHT`,
  `HIGH`. Inches are converted. Two ends that disagree, or a number with no D or H
  beside it, are shown and the depth is asked for, never guessed.
- The length is what was shot; only the label rounds it. A depth larger than the
  length, or ends more than 40 ft apart, is drawn but flagged as worth checking.
- The label goes beside the bench by default (at plotted sizes the text is often
  taller than the bench is deep), or wherever you click.
- The outline and label are deliberate drafting: `FTFCLEAN` leaves them alone,
  `FTFDRAFTCLEAN` manages them with the drafted lines, and running `FTFBENCH` again
  on the same two shots replaces that bench rather than drawing a second.
- The outline goes on `V-SURF-LAND-E` and the label on `V-SURF-LAND-TEXT-E` (PMX keeps
  benches with landscape features); `bench.layer` / `bench.labelLayer` change them.

## Between two edges: FTFLABELBETWEEN

`FTFLABELBETWEEN` (`FTFB`) labels the space between two edges: a walk between its
edges, a hedge between its sides. Pick the first edge, then the second, where you want
the label; the preview starts centred between the two picked places, slides along the
space between them as you move the cursor, and a click places it.

- **Two lines, or two parts of one line.** The second pick may be on the same object as
  the first: a hedge drawn as one 3D polyline up one side, across and down the other is
  picked on its two long sides and gets `HEDGE` centred between them. Each side is the
  run of the line around its pick, up to the bend between them (the bottom of the U, the
  crown of a horseshoe), so the label rides between the sides and is never put at the
  middle of the whole polyline, which is on the bottom of the U. A closed outline works
  the same way. Two picks on one straight run of a line (one segment, or segments in
  line) have nothing between them and are refused.
- **Any linework:** lines, 2D polylines, 3D polylines at any elevation, arcs and bulges,
  survey figures and feature lines, U shapes, horseshoes, sides that are not parallel
  and sides of many segments. Everything is measured in plan (X and Y): the label sits
  midway between the nearest point of each side under the cursor, reading along their
  averaged direction (sides drawn in opposite directions, as a U's always are, are
  turned to agree) and never upside down. The lines' elevations never move it.
- **The wording** is the first edge's feature's between-wording (`ASPHALT` between
  asphalt edges), `W` cycling its alternates. An edge no rule identifies, or a feature
  with no wording, asks what it should say; `T` types different wording at any time.
  Layer, text style, height, rotation and mask come from Settings > Line Labels, as for
  `FTFLABELLINE`; the label is kept by the line-label pass like any hand-placed one.
- **The source is never modified.** Both edges are only read; nothing is exploded,
  converted, moved or redrawn. FTF adds one MTEXT label and its mask.

## Driveways: FTFLABELDRIVEWAY

`FTFLABELDRIVEWAY` (`FTFDWY`) is `FTFLABELBETWEEN` for a driveway: pick its two
edges (or two sides of one line), slide the label along the midline, click. The wording is the first edge's
**surface type read from its layer** with DWY after it: an edge on `V-SURF-ASPH-E`
labels `ASPH DWY`, on `V-SURF-CONC-E` `CONC DWY`, on `V-SURF-GRVL-E` `GRVL DWY`. The
type is the layer's last field once a one-letter status field (`-E`) is set aside,
so the edge need not be a configured line feature. `W` switches to the spelled-out
word where the feature has one (`ASPHALT DWY` for an EP edge). The wording is
Settings > Line Labels > Driveway wording (`{type} DWY`); the label layer is the one
`FTFLABELBETWEEN` would use.

## Label Everything: FTFLABELALL

Once the points are imported and Civil 3D has built the linework, the **Label
Everything** button (`FTFLABELALL`) labels the drawing in one go -- but only the
feature families ticked in **Settings > Label All** (trees, utilities, storm and
sewer structures, signs, control, pavement markings, other points, line features).
Storm and sewer structures are off by default: their dips are recorded and labelled
by the Dip Builder. `FTFLABELALL > Preview` changes nothing and lists how many labels
(and tree tags) would be created, updated, skipped or flagged, by family and reason.

- **Trees get one annotation, never two** (Settings > Trees): description only
  (`18" FIR`, the default), tag only (`T12`, the description goes in the schedule), or
  none. Label All tags trees instead of labelling them when trees are set to tags.
  The tree schedule is a separate switch (Settings > Schedule); with descriptions it
  lists trees by point number.
- **Dip measurements are never label text.** A crew note such as `12 RCP N 6.41`,
  `MD 6.25`, `BOT 7.82` or `WL 6.9` is not used; the point is labelled from its code.
- **Points marked `NOLABEL`** (after the code or in the note) are left alone.
- **Hand labels are respected.** Text someone typed near a point that says the same
  thing (`18in Fir` for FTF's `18" FIR`) means FTF does not label it again; labels FTF
  placed and someone moved stay where they were put; a copy someone made of an FTF
  label is theirs and is never replaced or erased.
- A run replaces and erases only labels in the families it covers.

1. Every recognised line (when Line features is ticked), by the whole-drawing line
   pass, at or above the minimum length in Settings > Line Labels. It runs even when
   the automatic pass is off in Settings > Line Labels; that switch only governs
   `FTFRUN`. A line is identified by figure name, then TrimbleName XData, then the
   notes of the survey points it was drawn through, then its layer, the same order
   `FTFLABELLINE` uses. Two shots noted with a span marker (`FGP`) along a line get
   a `GATE` label between them.
2. Every point (`FTFLABELS`), steering around the line labels just placed, worded
   from the crew's notes even when they are in the note field (see *Labels from field
   notes* above).
3. Draw order (`FTFORDER`): all of FTF's own text on top, its masks under it, then
   one REGEN so the screen shows that order (a drawing changed from code keeps showing
   the old stacking until a regen, which made new masks look as if they covered their
   labels).

**Leader arrows** are sized at plot scale: Settings > Point Labels > Arrow size
(0.06 plotted by default, times the drawing scale), for point labels and tag leaders.
Before, the arrow was the multileader style's size in drawing units -- a speck at
1"=20'. Turn "Size leader arrowheads here" off to leave it to the style; an annotative
multileader is always left to its style.

**Lettered and symbol linetypes are left to the legend.** A line whose linetype
draws its own letter or symbol -- water `---W---`, power `---P---`, a fence's x's --
already says what it is, so the line pass gives it no text label. FTF reads this from
the linetype's definition (its text and shape elements), from the line's own linetype
or its layer's when ByLayer, never from the name. Gate labels along such a line are
still placed, `FTFLABELLINE` still labels one you click (with a note), and the summary
lists them by linetype (`Left to the legend: WATER (W) 12, POWER (P) 4`). Turn it off
under Settings > Line Labels. **Fences are the exception:** every fence code carries
`"alwaysLabel": true` in `rules.json`, so a fence is labelled whether or not its
linetype draws x's. Any other line feature can be given the same flag. A survey figure whose style gives it a lettered linetype
of its own, rather than by layer, is not seen. `FTFLEGEND` itself lists only the line
features configured in `rules.json`, so an unconfigured water or power line is in the
office legend, not FTF's.

Labels placed with `FTFLABELLINE`, or moved by hand, are kept. Running it again
after more linework arrives replaces only its own labels. It ends by listing every
identified line it could not label, grouped: a feature with no wording configured
(`GRL Guardrail: no label configured (4 lines)`), or a layer two features share and
nothing on the line tells apart (`V-SURF-FENC-E: could be FNC (FENCE) or FHW (HOG
WIRE FENCE)`). Label those with `FTFLABELLINE`, which asks which it is.

**Fences.** Only chain link (`FCK`) used to have a wording, so every other fence
was refused. The shipped rules now label `FNC` FENCE, `FHW` HOG WIRE FENCE, `FBD`
WOOD FENCE, `FRL` RAIL FENCE and `FBW` BARBED WIRE FENCE; confirm the wordings
against the office standard. `FNC`/`FHW` share `V-SURF-FENC-E` and `FBD`/`FRL` share
`V-SURF-FENC-WOOD-E`: a survey figure names itself (`FNC3`), a plain polyline is
named by its shots' notes, and only when neither says does `FTFLABELLINE` ask.

## Settings, layers and processing

### Settings, grouped by workflow

The FTF window's Settings page is grouped the way the work goes:

| Group | Pages |
|---|---|
| FieldToFinish | General, **Layers**, Point Features, Symbols and Rotations (read only), Trees, Point Labels, Line Labels, **Label All**, Tags, Schedule, Driplines, Drawing Order |
| Survey Drafting | Spot Elevations, Contours, Drafting Lines, Sheets, Utility Dips |
| Boundary and Easements | Right of Way, Platted Blocks, Recorded Surveys, Survey Comparison, Strip Easements, Easement Exhibits |
| Administration | Profiles, Diagnostics (codes you marked ignored), Advanced Settings |

Every page shows "Unsaved changes" as soon as something is edited; **Save** writes to the
level chosen at the bottom (my settings, this drawing's folder, or the drawing's
profile -- never the office file), **Discard Changes** reads the settings back, and
**Restore section defaults** resets one page. **Profiles** shows which settings file
is in force for the drawing, selects or saves a project/client profile, and imports
or exports a whole configuration (an import loads into the window; nothing is written
until Save). Read-only pages and cells are greyed.

**Point Features** is two panels: the feature list (search by code or name, filter by
Label All family or disabled, sortable) beside the selected rule's editor. Edits are
staged as you type, validated and previewed against a sample description, and **Save
Rules** writes the rules file (comments and unknown properties kept, `.bak` written,
never the factory file). Each rule's Label All family can be set there (an optional
`"category"` in `rules.json`). **Line Labels** lists every line feature with its label
text, label layer and side, saved to the rules file the same way.

### Layers

**Settings > Layers** lists every layer FTF draws on -- from the rules (each feature's
label, dripline and block layer, symbol labels, line label layers, bench, orientation
QC) and from every settings section (found by reflection, so a new tool's layer fields
appear on their own) -- as *Feature | Object Type | Current Layer | New Layer | Source |
Status*, with search and filters. **New Layer** sends a layer to the project's own: pick
from the drawing or type a name. It is stored as the existing `layerMappings` (older
files load unchanged) and is applied by **every** command at the one place layers are
created (`CadUtil.EnsureLayer`, through `ProjectLayers`): labels, driplines, tags, the
schedule, spot elevations, contours, line labels, drafting, orientation QC and the
production tools. Before, only the dip and record tools read the mappings. A mapped
layer is classified as the layer it replaces for masks and draw order. **Create
Missing** makes the selected layers that are not in the drawing yet, each taking the
colour and lineweight of its family; Status says which exist, which are mapped, which
will be created and which have a near-namesake in the drawing. A change for one feature
only is made on that feature's own page (**Edit Feature Layer** goes there).

### Processing presets

The Process page (and `FTFRUN`, which asks) offers:

| Preset | Runs |
|---|---|
| Standard Topographic Finishing | point finishing, driplines, line labels (if selected), labels for the selected families except storm/sewer dips; tags only for trees set to Tag only; no schedule |
| Trees and Vegetation | driplines and the tree annotation the settings choose; the schedule if it is on |
| Annotations Only | line labels, labels, tags and the schedule; driplines and symbol rotations untouched |
| Custom | the stages and families you tick (Orient Symbols is available here) |

Stages always run in order; draw order is added after anything that draws annotation
and is not run otherwise; a schedule without a tags stage uses the tags already in the
drawing. **Preview** counts what a run would create, update, skip or flag; after a run
the page shows each stage's outcome. Label and tag stages replace and erase only the
families of the preset running, so a trees-only run never touches a pole's label.

### Review, diagnostics, field notes

- **Unknown Codes** sorts every point into *Errors* (blank or unreadable descriptions,
  invalid values), *Needs Configuration* (data with no rule) and *Informational*
  (Civil 3D linework, never-draw codes, codes you marked ignored, bare codes), with
  counts and filters. **Configure Rule** opens Point Features at the rule (or offers a
  starter rule for a code nothing handles); **Mark Ignored** stores the code in the
  settings (`"diagnostics": { "ignoredCodes": [...] }`). **Overview** separates what is in
  the drawing (points, eligible, processed, errors, unconfigured, skipped, FTF objects by
  kind, last processed and its outcome, kept in the drawing) from this session's
  activity; **Reports** lists every error and warning with point, code and reason, and
  keeps the CSV exports. **Lines** gives each line a status and lists the settings that
  decide its label, with a link to its row on Line Labels. **Standards** says where each
  value is defined and opens the page that edits it; "planned" items are marked not
  available. Every list sorts by column. Details: `docs/diagnostics.md`.
- **Dip Builder > Save Field Notes PDF** (`FTFDIPPDF`): the completed dips as
  field-book notes on US Letter -- measured depths in black, calculated inverts in red,
  bottom and water elevations labelled as such, a plan sketch per structure (inferred
  directions dashed and marked), warnings included, page numbers and project on every
  page. Saved under `Survey\Field Notes\Utility Dips` (configurable) as
  `<project>_Utility-Dip-Field-Notes_<date>.pdf`; an issued file is never overwritten
  (`_R1`, `_R2`...). Optional automatic save when a structure is labelled. Written
  directly, no printer driver. Details: `docs/dip-field-notes.md`.

### Review list and Drafting QC (FTFREVIEW, FTFQC)

Everything FTF could not decide is kept in one list inside the drawing, so nothing that
needs a drafter is lost in command-line history:

| Raised by | Items |
|---|---|
| FTFREVIEWCHECK (Recheck codes) | codes nothing handles (one item per code, listing its points); blank or unreadable descriptions and invalid values (per point); words after a linework code that are not a line command, a number or a known code (`FNC CL`) |
| Line labels (FTFLINELABELS, Label All) | fence heights that cannot be read (`FNC B 35`); lines identified but not labelled |
| Gates | unpaired posts, posts that could pair more than one way, widths out of range, recorded widths that disagree, posts away from every fence |
| FTFORIENT | pavement markings whose direction could not be decided, symbols needing review or below the confidence to apply, points that could not be turned |
| Any FTF command | a command that did not finish (closed when it next finishes) |
| Point accountability (after FTFRUN, the window, FTFLABELALL; FTFREVIEWCHECK; FTFQC) | points missing what they need (a label, a block, their own symbol, a line, a surface, the control table), duplicates, exclusions without a reason, points deleted since they were first recorded; see `docs/point-accountability.md` |
| Linework continuity (the same) | gaps in coded strings, interrupted strings begun again, lines that skip a coded shot, coded shots with no line, each with its evidence and a proposed correction |

Every point is accounted for from what the drawing holds for it (the point ledger,
`docs/point-accountability.md`): an ignored code needs a documented reason, a bare code its
own symbol, a linework shot a drawn line of its feature. Approving a proposed correction
draws a separate line on the feature's layer; the survey linework is never changed. A
decision reopens when the point or line it was made on changes.

Each item shows the point number or entity, the feature, the issue, why, what to do,
its severity, whether it is required, and its status. In FTFREVIEW: **Zoom to** (double
click) zooms and selects what it is about; **Fix** runs the command that corrects it
(FTFGATE, FTFORIENT...); **Its settings** opens the page that governs it; **Recheck**
reruns the check that raised it; **Not applicable** closes it with your reason;
**Accept** records that you reviewed it as drawn; **Reopen** puts it back. Reruns keep
your decisions; an item the check no longer finds is resolved automatically and
reopens if it comes back. Who and when are recorded (Settings > Review and QC >
Reviewer name, else your Windows sign-in).

**Drafting QC Complete** (`FTFQC`, or the button in FTFREVIEW) is refused while any
required item is open, and a new required item takes the mark off again. Which kinds
are required is a table on Settings > Review and QC; the refusal can be turned off
there. Nothing here blocks saving or drafting. This is the hook for the planned Survey
Production Checklist: its Drafting QC Complete step reads this mark.

### What reprocessing can and cannot remove

- FTF erases only entities carrying its own stamp. Civil 3D points, survey figures,
  symbols, linework and anything else not drawn by FTF are never erased.
- Every stamp records the handle of the entity it was written on. A copy of FTF output
  (COPY, paste, ARRAY) has a new handle and is therefore the user's: never re-placed,
  replaced or erased, and counted as a hand label.
- Labels moved by hand are kept by every label run. `FTFCLEAN` asks before removing
  them (kept by default) and reports copies it left alone.
- Settings > General > "Ask before FTFCLEAN or Process Drawing removes earlier FTF
  output" now does what it says; before, nothing read it.
- Tag numbers are kept in a register in the drawing, so a tree whose tag is switched off
  and on again gets its old number back.

## CAD standards and styles: one configuration

Everything that decides how FTF's output looks is set in Settings, in one layered
configuration, and the drawing commands read it through one resolver each. Details are in
the docs named below.

| Area | Where | What it controls | Doc |
|---|---|---|---|
| Settings levels | footer of the Settings window; Administration > Where Values Come From | Built-in < Office < My defaults < Profile < Project, merged value by value. Each value shows the level it comes from and the values it hides; Inherit removes one. Saves write only differences. Office saves need confirmation and the office PIN once one is set. | `docs/settings-levels.md` |
| Layers | FieldToFinish > Layers | Every layer FTF draws on (labels, symbols, linework labels, trees, dips, leaders, spots, contours, pavement, control, tables, easements...): the mapping to a project layer and its colour, linetype, lineweight, plot and description. Applied when FTF creates a layer. `FTFLAYERUPDATE` previews and then updates existing layers FTF uses; `FTFCHECKSTANDARDS` checks every layer, linetype, style and block the settings name and offers fixes. | `docs/layer-standards.md` |
| Text and leader styles | FieldToFinish > Text Styles, Leader Styles, Style Assignments | Named text looks (text style, height, width, oblique, rotation, justification, colour, mask, prefix, suffix, decimals, annotative) and multileader looks (multileader style, arrowhead and size, straight or spline, colour, linetype, lineweight, landing, gap, attachment, maximum length, annotative), assigned to every feature, a family, a rule or one code, separately for point labels, tags and line labels. A tester shows every value a label will get and where it comes from. | `docs/annotation-styles.md` |
| One feature, one screen | FieldToFinish > Point Features | Per feature: label text, label layer, leader use, tag prefix, dripline layer, **and** label on/off, label text style, leader style, offset, tag style, the effective look with a preview, and a link to the feature's symbol and block. Line features get a text style column on Line Labels. | `docs/annotation-styles.md` |
| Symbols and blocks | FieldToFinish > Symbols and Rotations | The symbol standard FTFORIENT turns markers by (block, native zero, period, two-sided), editable, with a block preview; blocks FTF inserts (opt-in per rule): block, layer, scale, insertion point, rotation method, attributes. | `docs/symbols-and-blocks.md` |
| Commands and workflows | Administration > Commands; FieldToFinish > Workflows | Every command, its settings pages and ribbon place; your own ribbon panel ("My Commands"); saved processing workflows that appear in Process Drawing and FTFRUN. Pages register themselves, so a new command's settings appear without editing the window. | `docs/settings-registry.md` |

**Existing drawings.** New output takes the current standards on its own. For what is
already drawn there are separate, preview-first commands: `FTFRESTYLE` (FTF's labels, tags
and line labels, in place, hand-moved positions and hand-edited wording kept) and
`FTFLAYERUPDATE` (layer properties only). Neither touches anything FTF did not draw.

**Nothing is swapped silently.** A text style, multileader style, arrowhead block or linetype
the settings name but the drawing lacks is listed by name at the end of the command, with
what was used instead, and shown in red on the settings pages and in Check Standards.

**Civil 3D's own point label styles** (the label a COGO point draws through its point label
style) are not multileaders. FTF does not pretend its leader settings apply to them; edit
those in Civil 3D's Settings tab.

**Checked in Civil 3D on deploy.** Besides the existing smoke test, `deploy/smoke-test.ps1`
runs a separate style check (`tests/LiveSmoke/StyleTestHarness.cs`): it labels four COGO
points under test styles and checks the real MTEXT and MLEADER properties, then FTFRESTYLE
Preview and Apply. UPDATEFTF prints `style check: PASS` or `STYLE CHECK FAILED`; while the
style system is new, a failure there does not undo the install.

**Office settings survive updates.** The Office level is `ftf-settings.json` beside the
installed DLL; the installer now carries it into each new version.

## Contours at an exact elevation: FTFCONTOUR

`FTFCONTOUR` draws the contour at any elevation you name, straight off an existing TIN
surface, without touching the surface:

```
Command: FTFCONTOUR
Select a TIN surface or [Name] <EG>:            (click it, Enter for the last one, N to pick by name)
Elevation(s) on "EG" in feet -- 12.6, or 12.6, 13, 13.5, or 10 to 14 by 0.5 [Boundary=none / Labels=off]: 12.60
FTFCONTOUR: surface "EG" (1,234 points, 2,401 triangles, elevations 10.12 to 18.4).
  12.60  3 contours (1 closed, 2 open), 412.37 ft, 386 vertices.
  Accuracy: 12 vertices read back from the surface; all on their contour elevation (largest difference 0.0000 ft).
  Surface untouched: definition, triangles, elevation range, style and build state read the same before and after.
  3 contour polylines on layer V-TOPO-CONT-E. One U undoes the lot.
```

- **Elevations.** One (`12.6`), a list separated by commas, semicolons or spaces
  (`12.6, 13.0, 13.5`), or a run (`10 to 14 by 0.5`, which counts in decimal so it lands
  on 14 exactly). Each value is the exact double its text names -- `12.60` is drawn at
  12.6, never rounded to a display precision -- in drawing units (the prompt says which).
  The same value typed twice (`13, 13.0`) is drawn once. Anything unreadable is refused
  and asked again; nothing is drawn from a half-understood list.
- **What is drawn.** Every piece where the surface crosses the elevation: disconnected
  pieces, open pieces that run off the surface or into a hidden area, and closed loops
  around hills and hollows, each its own lightweight polyline with **Elevation** set to
  the value typed, on the contour layer (colour, linetype and lineweight ByLayer).
- **Where it comes from.** Civil 3D's own extraction,
  `TinSurface.ExtractContoursAt(double elevation)` (the `ITerrainSurface` member; the
  no-smoothing overload), which intersects the built TIN at that elevation -- the
  surface's actual intersection, not a position interpolated between the contours its
  style displays. Civil 3D adds its result to the drawing as polylines; FTF reads each
  one exactly (vertices, bulges, closure), erases it, and draws its own in its place. If
  that call fails, FTF traces the same intersection from the surface's visible triangles
  (`GetTriangles(false)`) and says so.
- **The surface is only read.** It is opened for read for the whole command, so AutoCAD
  itself would refuse any change. Nothing is added to its definition; no triangle,
  breakline, boundary, point, style or contour interval is touched; it is **never
  rebuilt**. An out-of-date surface is reported and contoured as last built -- rebuild it
  yourself first if its definition changed. (Regenerating the display, `REGEN`, is not a
  rebuild and is never needed.) Its point and triangle counts, elevation range and mean,
  style and out-of-date flag are read before and after; if anything differs, the whole
  command is rolled back and says why.
- **Elevations with no contour** are reported, not errors: below the lowest point, above
  the highest (with the surface's own figure), touching only at a single point, crossed
  only inside a hidden area, or crossed only outside the boundary.
- **Boundary (`B`).** Pick a closed polyline (arcs followed to 0.0001) or a circle; the
  contours are cut exactly where they cross it -- the cut points lie on the original
  contour segments, so clipping changes where a contour stops, never where it runs.
  `B` then `N` goes back to the whole surface.
- **Labels (`L`).** Elevation text centred on the contour, along it and never upside
  down, one per piece or one per *label spacing* along long pieces, slid off sharp
  bends, with a mask so the contour breaks for the text. The text is the elevation as
  typed (or to a fixed number of decimals, in settings). Pieces too short to carry the
  text are left bare.
- **No duplicates.** Each contour is stamped with the surface and exact elevation. Running
  it again at the same elevation on the same surface replaces FTF's earlier contours and
  labels; with a boundary, only inside it (an earlier contour that crosses the boundary
  is trimmed back to its outside part, so old and new meet without overlapping).
  Contours at other elevations, from other surfaces, or drawn by hand are never touched.
  `FTFCLEAN` leaves contours alone; erase them like any polyline.
- **Undo.** Everything is one transaction in one command: one `U` removes it all.
- **Several surfaces.** Click the one you want, or `N` lists every TIN surface by
  name (useful when its style hides it). The last one used is the default.

Settings > Contours: contour layer (`V-TOPO-CONT-E`) and label layer
(`V-TOPO-CONT-TEXT-E`), both through the project's layer mappings like the production
layers; labels on by default or not; label height (0.08 plotted), spacing (6" plotted,
0 = one per piece), decimals (-1 = as typed); masks; and the read-back accuracy check.

**What still needs Civil 3D to confirm** (none of this has run in the product yet): that
`ExtractContoursAt` behaves as documented on the office's surfaces -- that it adds
lightweight polylines to model space which open and erase within the command's
transaction, and returns them unsmoothed; that `GetGeneralProperties`,
`GetTinProperties`, `IsOutOfDate` and `StyleId` read the same before and after (the
"Surface untouched" line); that `GetTriangles(false)` and `TinSurfaceVertex.Location`
exist as used by the fallback; that a data-shortcut (reference) surface can be
contoured; that one `U` removes a multi-elevation run; and the labels, masks and layer
prompts on a real drawing. The elevation parsing, the tracer, clipping, label spacing,
the before/after comparison and the report are unit tested
(`tests/FieldCodes.Tests/ContourTests.cs`), and a source guard fails the build's tests
if the command ever calls a surface-editing API.

## Orienting symbols: FTFORIENT

Civil 3D draws every survey symbol at rotation zero unless something turns it. `FTFORIENT`
turns the ones that mean a direction — signs, hydrant nozzles, light and signal arms, valve
pipe axes, pavement arrows and words — from what was surveyed around them.

**What it changes:** each COGO point's `MarkerRotation`, and for painted words the label
rotation too. Nothing else. No block is inserted, no point moves, no elevation or line is
touched, and nothing is drawn as utility linework. The rotation before FTF's first change
is kept on the point (XData `FTF_ORIENT`), so `FTFORIENTRESET` puts every symbol back.

**Run it**

1. `UPDATEFTF` (the approved build), open the drawing.
2. `FTFORIENT` → `Preview`. Nothing is rotated; QC arrows for every decision are drawn
   (non-plotting, starting beside each symbol so they are not mistaken for it) and the report is
   written. Look at the red rings first: those are the review list.
3. `FTFORIENT` → `Apply` (the default). Turns every decision at or above the office's
   confidence (0.6 and Inferred or stronger, `rules.json` → `orientation`).
   A marker someone already turned by hand is **kept** (listed as "kept as turned by
   hand"); a measured direction still replaces it. `All` replaces those too.
   After Apply, QC graphics are drawn only for what needs review (Settings > Symbols and
   Rotations > Rotation evidence and review), sized by the drawing's annotation scale.
4. `FTFORIENTPOINT` (or Fix on the review list) explains one point and lets you Apply the
   suggestion, Override it with your own direction, or Recheck it. A direction you set or
   accept is never changed by a later FTFORIENT.
5. `FTFORIENTRESET` puts everything back and removes the QC.

The ribbon has both on the Finish panel (Orient Symbols, Reset Orient).

### How it decides

Strongest evidence first; a stronger tier always wins, and only *measured* features are
used to orient others, so one wrong guess cannot set off a chain of confident wrong ones.

| Tier | From | Examples |
|---|---|---|
| **Measured** | What the crew recorded | `SN 135` (the office sign rule's azimuth), `AZ 135`, `FACES NE`, `NOZ E`, `STEAMER 270`, `ARM W`, `ARMS N S`, a valve `N-S`; a shot sign panel line; in the description or the crew's note |
| **Relationship** | Surveyed features around it | the stop bar a stop sign stands beside; the intersection approach a sign is on the right of; a side road (edges only) a stop sign stands on; a luminaire shot at the end of an arm; a hydrant and its valve tee; a measured arrow in the lane beside a sign |
| **Alignment** | Surveyed road or utility linework | traffic keeps right of a two-way centre line (0.7), a lane or fog line (0.6) or a pavement edge with no centre line (0.55); a one-way line drawn in its direction (0.85); a valve on a surveyed main (0.85). Less when the line had to be followed past its end, or is over 30 ft away |
| **Inferred** | A feature rule the surroundings support | a hydrant's pumper nozzle faces the nearest road (0.7 within 15 ft, 0.6 to 40 ft; the report's Source says "fallback rule"); a lone valve between a hydrant and the road; two valves near a hydrant (0.55: suggested, not turned) |
| **Review** | Two answers equally good, or nothing to go on | left alone, ringed red, listed with a suggestion where there is one |

Per feature:

- **Signs** face the traffic they are meant for. A sign on the right of a two-way road faces
  the drivers coming toward it. At an intersection the approach is chosen by where the sign
  stands (about a lane and a shoulder off the centre line, before the junction), not by the
  nearest road — at a corner both are near. **Stop signs** take a stop bar first, then an
  approach, then a side road drawn by its edges only. A stop sign with none of those is
  **not** faced at through traffic: it goes to review with that as the suggestion. Several
  signs on one post, street-name blades (read from both sides) and signs between two roads
  that would face different ways all go to review.
- **Side roads with no centre line** (common on rural surveys, where only the main road's
  stripe is shot): a point much nearer an edge that runs *across* the main centre line than
  to the centre line itself belongs to that side road.
- **Lights**: a round light is never rotated. An arm points to the luminaire shot if there is
  one within 40 ft, or the direction the crew recorded (`ARM W`). With neither, the arm is **not**
  invented from the pavement: it is suggested toward the road and listed for review (the
  street-light fallback is Review; an office can set it to Apply).
- **Signal poles**: the mast arm spans the intersection approach it stands beside; unsure, or
  nothing but the road to go on → review.
- **Hydrants**: a recorded nozzle direction (`NOZ E`, `STEAMER 270`) first; otherwise the pumper
  nozzle faces the street (fire engine access) as a documented fallback (set Hydrant to Review to
  have each confirmed). Never from the lateral: an underground lateral does not show which way the
  visible nozzle was turned. Two streets at similar distance in different directions → review.
- **Water valves** (axis symbols: 90° and 270° are the same): on a surveyed main, along it.
  Three valves at a hydrant are read as a tee — two on the main, one on the lateral
  toward the hydrant — scored on how well they line up. The probable main and lateral are
  put on the review list and in the report, never drawn as utility linework or delivered; they
  are drawn (dashed, with a short tag, on the non-plotting `FTF-QC-ORIENT-INFERRED`) only in
  Preview or when the office turns that on. Two valves are not an assembly: their probable
  arrangement is suggested, not applied. Anything else → review.
- **Gas valves**: on a surveyed gas main, along it; several in a line along the road, along
  the road (0.6); otherwise review. (No hydrant tee: that is water only.)
- **Arrows and painted words**: the direction of travel in their lane — stop bar, inbound
  intersection approach, one-way line, or keep-right of the road's middle.

### Roads that are only partly surveyed

A centre line is not required. A feature takes its road's direction from the road lines near
it, ranked by how far each kind is trusted (centre lines, then lane and fog lines, then pavement
edges, curbs and flowlines, then edges of concrete as support only), how near it is, and how long
it is (a short driveway edge says less about the road than the long edge behind it). Fences,
property lines and utilities are never road lines.

- **Local tangent:** the direction is taken over a set length of line where the feature stands
  (20 ft), so a curve gives its tangent there and a small jog in a shot edge does not swing it.
- **Lines that stop short:** followed past their end along their last stretch, at most 25 ft,
  with less confidence the further they go; further than that they are not used. A feature in a
  gap, where the line resumes beyond it, is treated as if the line ran through. Nothing is ever
  extended in the drawing.
- **Disagreement:** road lines near a feature that run more than 15 degrees apart send it to
  review with both suggestions, instead of one being picked.
- All of these are settings (Symbols and Rotations > Rotation evidence and review), as are the
  trust in each kind of line, intersection handling, each kind's fallback (Apply, Review or
  Leave), how hand rotations are kept, and the QC graphics. The decision CSV lists, per point,
  the reference used and its distance, the source of the direction, the proposed rotation,
  whether it was applied and whether it needs review.
- `docs/orientation-review-2026-11.md` has the reasoning, before and after.

### Pavement arrows with a written direction of travel

`ASTO N` and `ASTO S` (STRAIGHT ARROW N / S) used to point the same way: with no stop bar or
intersection, both arrows took the keep-right lane direction of the road beside them. Now a
direction the crew writes after an arrow or pavement-word code is the direction of travel:
`N`, `S`, `E`, `W`, `NE`...`NNW`, `NB`/`SB`/`EB`/`WB`, `S/B`, `SOUTHBOUND`, `SOUTH BOUND`, read
from the raw description, the full description (`STRAIGHT ARROW S`) and the note. Straight,
left, right and combined arrows (`ASTO`, `ALTO`, `ARTO`, `ASLT`, `ASRT`, `ASLR`) all work this way.

- **Which way:** the written direction. **Exact angle:** the road line beside the arrow whose
  direction is closest to what was written. The angle is taken where the arrow stands, so a curve
  is followed. At an intersection this picks the street the arrow is on, not the cross street,
  even when the cross street is nearer.
- **No road line within reach:** the arrow points the written compass direction on the drawing's
  grid. North is +Y in world coordinates, whatever way the view is turned.
- **Road too far off what was written** (an `E` arrow on a north-south road): listed for review,
  never guessed.
- **Precedence:** a measured azimuth (`AZ 197`, `POINTS SSW`) still wins. When stop-bar or
  keep-right reasoning disagrees with the crew, the crew's direction is used and the report says so.
- **The block's own zero is taken off:** a PMX arrow travels north at rotation 0. A different
  block's zero is set per code on Settings > Symbols and Rotations (Override for Code).
- **Re-running:** FTFORIENT only changes the marker rotation. Nothing is inserted and no point
  moves. A marker someone turned by hand is kept; `FTFORIENT All` overrides that.
- **Settings > Symbols and Rotations:**
  - read a written direction of travel (on);
  - follow the lane's road line (on);
  - road line within 40 ft;
  - review beyond 60 degrees.

### The symbols' own zero (PMX)

A marker rotation turns the block about its insertion point, so the rotation that points a
symbol is the direction's AutoCAD angle **minus the direction the block itself points at
zero**. Read from the blocks in a completed PMX drawing:

| Block | At rotation 0 | Reference | Repeats every |
|---|---|---|---|
| `SSV-SN*` (signs) | post at the insertion point, panel to its north: faces north | 90° | 360° |
| `SSV-WFH` (hydrant) | nozzles to the east | 0° | 360° |
| `SSV-WVL` (water valve) | bowtie on a north–south pipe | 90° | 180° |
| `SSV-GV` (gas valve) | pipe east–west — **assumed, confirm against the block** | 0° | 180° |
| `SSV-LTARM`, `PPL*` (arm) | arm to the north | 90° | 360° |
| `SSV-LT` (round light), `SSV-WVT` (a letter W) | symmetric | — | never rotated |
| `SSV-TSP` (signal pole) | **not inspected; assumed arm north** | 90° | 360° |
| `SSV-AS*`/`AL*`/`AR*` (arrows) | tip at the insertion point, travelling north | 90° | 360° |
| `BART` (painted word) | label reads with north as up; label rotated with the marker | 90° | 360° |

Each is a row in `rules.json` → `orientation` → `symbols` (left out, the built-in PMX list
applies; name the list to replace it).

**This fixes FTFPOINTS' sign rotation.** It used to set a sign's marker to the azimuth's
AutoCAD angle as is, which is right only for a block drawn facing east. PMX signs face
north at zero, so every `SN <azimuth>` sign was 90° off: `SN 135` faced 45°. It now takes
the block's zero off (`SN 135` → marker 225°, facing 135°), for any code the orientation
standard knows; other codes and rules that give a plain AutoCAD angle are unchanged.

### QC and the report

- `FTF-QC-ORIENT` (not plotted): an arrow per decision, coloured by tier (green measured,
  cyan relationship, blue alignment, yellow inferred, red review), both-way arrows for
  axis symbols, a grey line to what it was oriented by, the tier and confidence as text,
  each intersection found with its inbound approaches.
- `FTF-QC-ORIENT-INFERRED` (not plotted): the probable main and hydrant lateral read from a
  valve tee, labelled as inferred.
- `<drawing>.ftf-orientation.csv` beside the drawing (or in the report folder): point, code,
  kind, tier, confidence, facing azimuth, the alternative where there was one, rotation
  before and after, outcome (rotated, already right, kept as turned by hand, left — not
  sure enough, review, symmetric), what it was associated with, every piece of evidence,
  and the review reason.

### Checked on a real drawing

Run (outside Civil 3D, through the same engine) on a completed PMX rural topo — 1,115
points, the main road's double yellow, edges of asphalt, no centre lines on the side roads —
with the symbol points identified from the drawing's own point database:

- 24 directional symbols (20 signs, 1 hydrant, 3 valves); 11 would be turned at the default
  threshold, 7 left (edge-only roads at 0.55), 6 for review.
- Every sign along the striped road faced its oncoming traffic; the side-road stop sign faced
  the side road's traffic, and the side road's other sign followed the side road, not the
  main road's stripe 60 ft away. Both came from adding the side-road rule after the first
  run had faced them at the main road.
- Stop signs with no stop bar, approach or side road found (3) went to review rather than
  being faced at through traffic. The hydrant nozzle turned to the road 7 ft away. The three
  valves had no main surveyed near them (one stands right beside the hydrant) and went to
  review.

The drawing has no recorded marker rotations to compare with: this checks that decisions
are sensible, not that they match the drafter's.

### Limits

- **Point style orientation reference is assumed World.** A style set to View or Object
  turns markers relative to the view; check one symbol after the first Apply.
- Civil 3D Alignment objects are not read — only lines (centre lines, stripes, edges, stop
  bars, crosswalks, mains) by layer or survey figure name (`rules.json` → `orientation` →
  `lines`). An alignment's polyline on `V-ALGN-CNTR-E` is read.
- Two-way roads are assumed to keep right. One-way roads must be drawn as one-way lines
  (`V-ROAD-ONEWAY*`) in their direction, or a measured arrow must be nearby.
- Edge-only roads stay at 0.55 — below the 0.6 default — because a one-way road would break
  keep-right. Lower `applyMinimumConfidence` to 0.55 to turn them.
- Nothing is learnt from a drawing's existing rotations, and nothing comes from aerials or
  point clouds: survey linework and points only.
- The gas valve axis and the signal pole block's zero are assumptions until checked against
  the blocks (above).

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
   real 177-layer table of a project base drawing, not invented.
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

### Fence heights written on the shots (FNC B 3.5)

A crew writes a fence's height after its code: `FNC B 3.5` is the fence code, `B`
(begin line) and a 3.5 ft height; `FNC B 5` starts a 5 ft fence. FTF reads the
height off the shots the fence was drawn through (the same vertex-to-shot match as
point notes above) and carries it along the fence until a later shot records a
different one, so a fence that steps from 3.5 to 5 ft gets `3.5' FENCE` labels up
to that shot and `5' FENCE` after it.

- The line codes (B, E, C, CLS, BC, EC, PC, PT, OC and the rest) are skipped, and a
  number fused to the code (`FNC1`) is a figure number, not a height. Numbers that
  follow `R`, `OFF` and other parameter codes are theirs, not heights.
- A number that cannot be a fence height (over the configured highest height, or
  under 0.5 ft), or two different heights on one shot, is reported on the command
  line as `Check: ...` and never used.
- Any feature whose name or label says FENCE reads heights; `"readHeight": true` on
  a line feature in `rules.json` turns it on for others (a wall), `false` turns it off.
- `FTFLABELLINE` shows the height in force where the cursor is while you drag; the
  automatic pass and Label All write each label with the height at its own spot.
- Settings > Line Labels > Fence heights: the wording (`{height}' {label}`, empty to
  leave heights out), decimals (-1 keeps what the crew wrote) and the highest height
  believed (20 ft by default).

### Gates (FTFGATES, FTFGATE)

Two shots coded as gate posts (`FGP`, plus any aliases on Settings > Gates) become one
gate, labelled between the posts along the opening with its width: a 6.0 ft opening is
`6' GATE`, 4.8 ft is `GATE`, 12 ft `12' GATE`. The label uses the line label text style,
layer, height and mask of the fence it is in. The posts and fences are only read.

- **Pairing needs a fence, never distance alone.** Both posts on one fence line and next
  to each other along it (chain link, wood or any fence, bends included), or the fence
  stopping at both posts (the opening between two fence ends), pair them. A fence at
  only one post is weaker evidence but still pairs. Posts on two different fences
  (parallel runs) or either side of a corner are not one gate. Posts away from every
  fence are proposed in the review list, not labelled, unless Settings > Gates allows it.
- **Ambiguous pairs are flagged, not guessed.** A post with two plausible partners
  (three posts in a row, a double gate) is left unlabelled and listed for review.
- **Widths** (Settings > Gates): under 4.5 ft review (or a narrow-gate label if you set
  one), 4.5 to under 5 ft the plain `GATE`, 5 to 25 ft `{width}' GATE`, over 25 ft review.
  Rounding (1 ft by default, or 0.5...) and decimals are settings. The width is the
  distance between the post shots, centre to centre, and the review list and command
  line say so: it is not a measured clear opening. A width the crew wrote on a post
  (`FGP 12`) is used when the posts agree with it, and reviewed when they do not. No
  width is ever replaced by a standard gate size.
- **Reruns** (FTFGATES, the automatic line label pass, Label All, FTFLABELLINE on that
  fence) leave a gate label alone when nothing changed, keep one you moved or reworded,
  replace one whose posts changed, and never place a second one.
- **FTFGATE**: pick two posts to confirm a pair FTF was unsure of, or to change one it
  made. Accept the label, type your own, or cancel; a width FTF would not label on its
  own (too wide, recorded width disagrees) is shown with its reason first. The label is
  yours: reruns never move or remove it, its posts are left out of automatic pairing,
  and the review items for those posts are resolved.
- The older span-marker rule (two marked shots on one line) no longer labels gate posts
  while gate finding is on, so nothing is labelled twice.

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

**What the inspection actually found (a real project base drawing, 2026-08):** 49 of 398
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
That drawing's TBC export flattened figure identity -- wall/curb subtypes collapsed to
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
window and the settings page), along with the line labelling, driveway and bench commands
and two setup pages, is compiled by `tests\CadCompileCheck` against stand-ins for
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
