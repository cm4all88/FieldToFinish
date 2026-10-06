# Field-to-Finish Plugin — Parser Core

Parses surveyed point descriptions into drawing instructions. Consumed by the
Civil 3D plugin; contains no Autodesk references and runs without a CAD licence.

## Layout

```
src/FieldCodes/        netstandard2.0 — grammar, rules, parsing. No CAD types.
src/FieldCodes.Cad/    net48;net8.0   — CogoPoint reading, block/label/drip placement.  [not yet written]
config/rules.json      the grammar itself
src/CrewUpload/        netstandard2.0 — crew upload rules: project lookup, sorting, naming, copy checks.
src/CrewUpload.App/    net48 WinExe   — CrewUpload.exe, the crews' drag-and-drop upload window.
config/job-folders.json  the project folder and file naming standard CrewUpload follows
```

The split matters: `FieldCodes` is the single definition of what a code means.
Nothing downstream should re-implement the grammar.

## Crew Upload

`CrewUpload.exe` is a separate Windows app (no Civil 3D needed) for getting a crew's download into
the right project folder with the right names.

A crew download is one folder per crew, day, project and kind of work, named
`{date}-{crew}-{client}-{task}-{work type}`.

**Project setup (PM).** The app never works out a project's folders from the company folder structure.
Once a project's folders exist, a PM registers it: client-task (`1800-119`) and the project's base
Survey folder, picked in a folder browser, for example

```
\\parametrix.com\pmx\PSO\Projects\Clients\1800-HDR\554-1800-119 TDLE Phase 3\99Svcs\Survey
```

A pick on a mapped drive is stored as its UNC path; a path that is not UNC is refused. The PM can change
the location later, or mark a finished project **inactive** -- it disappears from the crews' list and
an upload to it is refused, but the registration and its full history stay (projects are never
deleted; *Reactivate* opens one again). Project keys are always `####-###`: `1800-11` is read as
`1800-011`, and `1800-011` and `1800-119` are different projects.

**The registry** is one file in its own shared configuration folder, never beside the exe:

```
\\parametrix.com\pmx\PSO\Shared\Divisions\00Survey\FLD\CrewUpload\Config\
    project-registry.json              the live list (registryFile in job-folders.json, a permanent UNC path)
    project-registry.backup-1.json     the version before the live one ... backup-5 the oldest (registryBackups)
```

- **Permissions are the security boundary.** PMs/admins need Modify on the folder (a save creates,
  renames and deletes files there); crews need Read. `deploy\crewupload-config-folder.ps1` makes the
  folder and adds both grants. PMs open setup with `--setup` (their own shortcut), and the optional
  `projectManagers` list in job-folders.json is a second check -- a convenience that keeps the screen
  out of the way, not protection. A PM without Modify gets "You do not have permission" on saving.
- **Saves never write over the live file.** A save takes a writers' lock (`project-registry.lock`,
  deleted on close even if the PC crashes), re-reads the live file, writes the complete new registry to
  a temporary file in the same folder, flushes it to disk, reads it back and checks it, and only then
  atomically replaces the live file, which becomes backup-1 (older backups roll up to backup-5). A write
  that does not read back is thrown away and the live file is untouched.
- **Two PMs at once.** Every save bumps the file's revision. If another PM saved after this PM opened
  setup, changes to *other* projects are merged in and kept; a change to the *same* project stops the
  save, says who changed it, and refreshes the screen so the PM can look before trying again.
- **On the share.** Every file step of a save (temp write, read-back, backup copy, the replace, each
  backup roll) is retried for about 8 seconds through sharing violations, antivirus locks and brief SMB
  errors. The live file is never deleted or renamed away: the backup is a *copy* taken first, and the
  replace runs without a backup argument, so a replace that fails leaves the live file exactly as it was.
  A backup roll that fails after the replace is a warning, not a failed save (the previous version is
  kept as `project-registry.backup-new-*.json`).
- **The lock** is `project-registry.lock`, held open exclusively by the saving PM (so the file server
  stops anyone else opening, deleting or replacing it), with `project-registry.lock.owner.json` saying
  who holds it. A leftover lock file that nobody holds is taken over and logged. A lock that is still
  *held* is never broken -- including one held by a PC that crashed, whose handle the server keeps until
  it drops the connection; after 15 s the PM is told who holds it and since when, and after 2 minutes how
  IT can close it (Computer Management > Shared Folders > Open Files).
- **Troubleshooting.** Every save failure and every retry is logged with the operation attempted, the
  path, the user, the machine and the Windows error (`win32=32` sharing violation, `5` access denied,
  `64` network name gone ...) to `registry-errors.log` beside the registry and to
  `%LOCALAPPDATA%\FieldToFinish\CrewUpload\registry-errors.log` on the PC (in case the share is the problem).
- **Crew reads** retry a briefly locked or mid-swap file a few times before reporting a problem. If the
  live file is damaged (hand-edited, say), the newest good backup is read instead and PM saves are
  blocked until someone restores it.

**Settings** (bottom of the window) shows where the app looks for the project list. A PM started with
`--setup` can point it at another folder there (a mapped-drive pick is stored as its UNC path); it is
saved to the job-folders.json the app was started with.

**Project numbers** are prefix-client-task, 3-4-3 (`554-1800-119`), with the client in the middle;
projects are filed by client first. PMs register the full number. Crews may type the full number or just
client-task (`1800-119`, as in their download names): one matching registered project is used, several
are offered as a short list of registered projects to choose from. Names keep using client-task
(`20261005-JBB-1800-119-TOPO`, `1800-119-20261005-0412.jpg`). The PM's folder picker opens in the
project's client folder.

**Crew uploads.** The crew types client-task, or drops a named download, and that is all they choose.
An unregistered project stops with "The PM needs to set the project location"; there is no browsing for
a destination. Uploads land in the one place the app writes:

```
[Survey]\02Field\01FLD_DR_FN_DCfile\Unprocessed\       unprocessedFolder, made inside the registered Survey folder
    20261005-JBB-1800-119-TOPO\                       the crew's folder, kept as they named it
        20261005-JBB-1800-119-TOPO.job                job files: the download's name
        20261005-JBB-1800-119-TOPO-FN.pdf             field notes
        20261005-JBB-1800-119-TOPO-ASB.pdf            as-built notes
        Photos\1800-119-20261005-0412.jpg             client-task-date-camera number (IMG_0412)
        upload-manifest.csv                           original name -> final name, per upload
```

Moving a download from Unprocessed to its processed location stays an office job. The app refuses to
write anywhere outside Unprocessed, and does nothing if the registered Survey folder has gone.

1. **Phase.** A project with a phase (141) has it in the Phase box beside the project number, or typed
   as `1800-119-141`. It follows client-task in every name (`20261005-JBB-1800-119-141-TOPO`,
   `1800-119-141-20261005-0412.jpg`) and goes to the same registered project. A download folder named
   with a phase fills the box in.
1. **Visit.** Crew initials, field date and work type (TOPO, LINEOUT, STAKE, BNDY, CTRL, ASBLT, ESMT)
   make the download folder's name. Dropping a folder already named that way fills them in.
2. **Drop.** Four boxes -- Job files, Field notes, As-built notes, Photos. Whatever lands on a box is
   that type, files or whole folders. Crew initials inside a download name never decide a type.
3. **Check.** Each file shows its type, final name and status before anything is copied. A dropped
   download keeps its own folder and subfolders; downloads are never flattened together.
4. **Upload.** Files are copied, never moved; nothing is deleted from FLD_Download and nothing is
   overwritten.
   - Same name and byte size already there: already uploaded, not copied.
   - Same name, different size: a **conflict**. It is not copied until the crew chooses *keep both*
     (the new file gets `-2`) or *skip* (right-click a row, or answer when uploading). Only then are
     files hashed, to find out whether the file is already there under another name.
   - Two different files in one batch that would get the same name are numbered `-2`, `-3`.
   - After copying, every file is checked again -- the original still there, the copy there at the
     same byte size -- and only then does the app say the upload is complete.
   - Every file, including conflicts and skips, gets a row in the download folder's `upload-manifest.csv`.

**New field notes** and **New as-built notes** start a document from `config\templates\`, already named
for the visit and in its download folder; **Type field notes** saves typed notes the same way.

The window follows the Parametrix Brand Guide (Nov 2023): the primary logo on a white header, charcoal
and white with Parametrix Red kept to the Upload button and problems, the red spacer arrow under the
title, the ix formation in the bottom-left corner, and Rockwell / Franklin Gothic (the guide's Office
stand-ins for Klinic Slab and Franklin Gothic URW). Each drop box carries its type's colour from the
secondary palette, repeated as a stripe in the Type column. Logo files and colours are in
`config\branding\` and the `branding` section of the config.

Everything -- the registry location and backups, PM list, Unprocessed folder, work types, upload types, codes, keywords and name
patterns -- is in `config\job-folders.json`, copied beside the exe. The app refuses to start with a
config that does not validate and says what is wrong.

```
dotnet build src\CrewUpload.App -c Release      # -> src\CrewUpload.App\bin\Release\net48\
dotnet test  tests\CrewUpload.Tests
dotnet test  tests\CrewUpload.Schedule.Tests
CrewUpload.exe [--config path\job-folders.json] [project number] [files or folders...]
```

### Daily reports

**Daily report...** fills in Form 03-SV-125-GW (job, date, project/task/subtask, crew, hours, data
file, control file, equipment, vehicle and mileage, comments, safety observations and precautions,
extras, plus work type, work order and optional weather). It opens with what the upload screen
already shows; everything is editable. **Submit** gives the report a ReportID (`DR-20260507-JBB-3f9a1c2e`)
and files:

| Copy | Where |
|---|---|
| Project | beside the crew's download: `[Survey]\02Field\01FLD_DR_FN_DCfile\Unprocessed\<download>\<download>-DR.pdf` |
| Admin | `dailyReport.adminFolder\yyyy\yyyy-MM\<download>-DR.pdf` |
| Crew | `dailyReport.localFolder` on the PC |
| Record | `dailyReport.adminFolder\Records\yyyy\yyyy-MM\<ReportID>.json` -- the structured record |

Nothing is overwritten (a second report the same day is `-2`). If the admin folder is unreachable the
record waits on the PC and is sent later (**My reports...** shows each report and whether it has been
recorded). The vehicle, equipment and safety lists are in the `dailyReport` section of
`job-folders.json`. Admins (with `--setup`) get **Scheduled vs reported...** in Project setup: reported
crews, missing reports and reports for unscheduled work, from the records -- never from file names.

### Survey Schedule integration (optional)

Crew Upload and the Survey Schedule stay separate programs. The integration is
`CrewUpload.Schedule.dll` beside the exe; Crew Upload loads it at run time only when
`features.scheduleIntegration` is on, and has no compile-time link to it. **Delete the DLL (or switch the
feature off) and Crew Upload is exactly the standalone tool**; the schedule never depends on Crew Upload.

- **Reads** `schedule.folder` (`pso-master.json`, `pm-*.json`, `pso-requests.json`, `pso-overrides.json`)
  read-only and assembles it the way the app's `assembleState()` does. Pending requests are left out.
  If it cannot be read: "Schedule unavailable. Report can still be entered manually."
- **Project link.** In Project setup the PM links a registered project to its schedule project
  (stored as the schedule's permanent id). Projects carrying exactly the full 3-4-3 number are
  suggested; nothing is linked automatically.
- **Crew & work types** (Project setup): crew initials (`JBB` = `jeff_bearson`, shown by name), optional
  Windows sign-in, and which schedule activities mean which work type. Kept in `crew-settings.json`
  beside the project list, written as safely as the registry. Empty until a PM fills it in. Mapping is
  exact only: an activity that is not exactly a listed name leaves the work type blank for the crew;
  free-text comments are never searched. The work type codes themselves are the editable list in
  `job-folders.json` (the shipped list is a placeholder).
- **Prefill** (`features.schedulePrefill`): the TODAY card shows what the person is scheduled on with
  **Daily Report / Upload Files / Open Project**; the report offers each scheduled project plus *Not
  listed / Enter manually*. The schedule only gives a starting point; the report records what happened.
- **Same reading as the Schedule.** `ScheduleAssembler.cs` is a port of the Schedule's `assembleState()`;
  `tests\schedule-compat` runs the Schedule's own code (extracted from its HTML) against it on every
  test run where Node.js is installed. A multi-day entry covers every day of its range, as the board
  draws it. `ScheduleCheck.exe` prints what Crew Upload sees on given dates, read-only.
- **Status** (`features.scheduleReportStatus`, off by default): marks the reported entries in
  `pso-progress.json` the way the app's own end-of-day reports do. The PM files are never written.

| Switch | Default | Off means |
|---|---|---|
| `features.scheduleIntegration` | on | no schedule at all (standalone) |
| `features.schedulePrefill` | on | no TODAY card, no prefill |
| `features.dailyReports` | on | no Daily report button |
| `features.scheduleReportStatus` | off | the schedule's progress file is never written |

Before relying on it, run the Windows smoke test: `deploy\SMOKE-TEST.md`.

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
