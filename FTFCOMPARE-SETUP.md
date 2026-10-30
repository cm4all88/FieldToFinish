# FTFCOMPARE: build, install and test on Civil 3D 2024

Short version, with Civil 3D closed. Into a new folder:

```
git clone -b claude/kind-hamilton-apylzj https://github.com/cm4all88/FieldToFinish.git C:\dev\FieldToFinish-compare
cd C:\dev\FieldToFinish-compare
powershell -ExecutionPolicy Bypass -File deploy\build-install-test.ps1
```

Or in a clone you already have (the branch is on GitHub but a clone made earlier has
not seen it until it fetches):

```
cd C:\dev\FieldToFinish
git fetch origin
git checkout claude/kind-hamilton-apylzj
git pull origin claude/kind-hamilton-apylzj
powershell -ExecutionPolicy Bypass -File deploy\build-install-test.ps1
```

The script ends with `Smoke test passed` or `FAILED:` and the reason. Then start
Civil 3D, open a **copy** of one project drawing, and type `FTFCOMPARE` (step 5).

Status: the comparison engine is unit tested (1,066 tests). The plugin has built against
the real Civil 3D 2024 API and run the whole smoke sequence headlessly: matched linework,
the clipped xref, the block twin, every "still plots" case, restore, audit and the
deliverable passed; the one failure (COGO point numbers read as elevations, which sent
every point to review) is fixed in the current commit and awaits a re-run. Nothing below
reports it as verified until step 4 passes on your machine and step 6 checks out on a
real drawing.

## Framework compatibility (checked before anything is installed)

Civil 3D 2024 hosts plugins on .NET Framework 4.8; Civil 3D 2025 and later use .NET 8.
What the build produces, straight from the project files:

| Assembly | Target | Where it is set |
|---|---|---|
| `FieldCodes.Cad.dll`, the plugin Civil 3D loads | `net48` (.NET Framework 4.8) | `src\FieldCodes.Cad\FieldCodes.Cad.csproj` |
| `FieldCodes.dll`, the comparison engine | `netstandard2.0` (loads under .NET Framework 4.8) | `src\FieldCodes\FieldCodes.csproj` |
| `Newtonsoft.Json.dll` 13.0.3 | ships beside the plugin | same |
| unit tests | `net8.0`, never loaded by Civil 3D | `tests\FieldCodes.Tests` |

The plugin references AcCoreMgd, AcDbMgd, AcMgd, AdWindows, AecBaseMgd and
`C3D\AeccDbMgd` from the Civil 3D 2024 install folder (`-p:C3D2024`), each with
`Private=false`, so no Autodesk DLL is ever copied; the build errors out if they are not
there. The .NET 8 SDK is only the compiler: `dotnet build` produces .NET Framework 4.8
output for the plugin, with the framework reference assemblies coming from the
`Microsoft.NETFramework.ReferenceAssemblies` package, so Visual Studio is not needed.
The bundle manifest restricts loading to AutoCAD R24.3, which is Civil 3D 2024. There
are no stub or substitute Autodesk assemblies anywhere in the repository.

The script refuses to install if any of this is off: the `AcDbMgd.dll` in the Civil 3D
folder must be release 24.x (2025 and later are R25+ and are refused), the .NET Framework
4.8 runtime must be present, and after the build it reads the target framework out of
each built DLL and stops on anything but `net48` for the plugin and `netstandard2.0` for
the engine, or on an Autodesk DLL in the output.

## 1. What to get from GitHub

Repository `cm4all88/FieldToFinish`, branch `claude/kind-hamilton-apylzj`.

```
git clone -b claude/kind-hamilton-apylzj https://github.com/cm4all88/FieldToFinish.git C:\dev\FieldToFinish-compare
```

If `C:\dev\FieldToFinish` already exists from earlier work, either use the new folder
above or update that clone with `git fetch origin` followed by
`git checkout claude/kind-hamilton-apylzj` (a checkout before the fetch fails with
"pathspec did not match", because the clone does not know the branch yet). Or download
the branch as a ZIP from GitHub and extract it; any folder works, the scripts find their
own paths. Everything FTFCOMPARE needs is in that branch: the
solution, `config\compare-profiles\Default.json`, the scripts under `deploy\` and
`tests\LiveSmoke\`.

Prerequisites on the machine: Civil 3D 2024 (it brings the .NET Framework 4.8 runtime),
the .NET 8 SDK x64 (https://dotnet.microsoft.com/download; the compiler only), and
PowerShell 5.1 or later, which Windows has. The .NET Framework 4.8 C# compiler used for
the smoke harness is part of Windows. Git is optional: the branch can be downloaded as
a ZIP instead.

## 2. Compile against the installed Civil 3D assemblies

```
powershell -ExecutionPolicy Bypass -File deploy\build-install-test.ps1 -SkipInstall -SkipSmoke
```

This runs `dotnet build FieldToFinish.sln` with `C3D2024` pointing at
`C:\Program Files\Autodesk\AutoCAD 2024` (pass `-C3D2024 "<folder with acad.exe>"` if
Civil 3D is installed elsewhere), then the unit tests. The csproj references the Autodesk
assemblies from that folder and never copies them.

If the build fails, the script prints the first errors with file and line. An error
inside `src\FieldCodes.Cad\Compare` means an Autodesk API member differs from what the
code expects; the README section "FTFCOMPARE > Verified and not verified" lists, in
order of likelihood, the members to look at (point style display, block twin cloning,
the xref clip transform, label text). Send me the error lines and I will correct them.

## 3. Install or load the commands

Close Civil 3D, then:

```
powershell -ExecutionPolicy Bypass -File deploy\build-install-test.ps1 -SkipSmoke
```

The install step copies the bundle to
`%APPDATA%\Autodesk\ApplicationPlugins\FieldToFinish.bundle` (per user, no admin
rights). Civil 3D loads it on startup: the FTF ribbon gets a **Compare** panel and these
commands exist on the command line:

| Command | Does |
|---|---|
| `FTFCOMPARE` | The review window |
| `FTFCOMPARERUN` | Run the comparison (All / Select / Window) |
| `FTFCOMPAREAPPLY` | Make the approved duplicates nonplot (verified one by one) |
| `FTFCOMPARERESTORE` | Put everything back |
| `FTFCOMPAREAUDIT` | Read-only: what the comparison holds in this drawing; "CLEAN" after a restore |
| `FTFCOMPAREQC` | The final checks |
| `FTFCOMPARESAVE` | Optional: a deliverable copy in the delivery folder |
| `FTFCOMPAREDECIDE` | A decision by handle, `Confident`, or `Unresolved` |
| `FTFCOMPAREPROFILE` / `FTFCOMPARESOURCE` / `FTFCOMPAREAREA` | Profile, consultant source, comparison limits |

To load without installing (for a quick try), start Civil 3D and type
`NETLOAD`, then pick `src\FieldCodes.Cad\bin\Debug\net48\FieldCodes.Cad.dll`. Either
way, rebuilding needs Civil 3D closed: a loaded assembly is never released.

## 4. Run the smoke test

```
powershell -ExecutionPolicy Bypass -File deploy\build-install-test.ps1
```

(or `tests\LiveSmoke\run-compare.ps1` for the smoke test without installing; it refuses
if an older bundle is installed, because accoreconsole would load that instead). Civil 3D
must be closed for every step of the script. The script:

1. builds and runs the unit tests;
2. installs the bundle;
3. compiles `tests\LiveSmoke\CompareTestHarness.cs` (two standalone commands that read
   the drawing by hand, so they check what is really there, not what the plugin thinks);
4. copies the stock NCS template twice into `tests\LiveSmoke\compare\`, writes a
   settings file beside the seed drawing so the run does not depend on your personal
   settings, and generates the accoreconsole scripts with the real paths;
5. run 1, in accoreconsole with the C3D product: `COMPARESEEDCONSULTANT` draws the
   consultant's base map (a curb, half of a second curb, a fence, one pole, a storm
   manhole with a rim label) and Civil 3D saves it as `compare-consultant.dwg`;
   run 2, in the seed drawing: `COMPARESEED` attaches that file as a clipped xref and
   draws our survey (two curbs, a third curb they lack, a fragmented fence, a symbol
   block whose definition draws on its own layer, four COGO points, one with a point
   style that fixes its marker layer, rim and invert labels),
   then `FTFCOMPARERUN`, `FTFCOMPAREAPPLY`, `COMPAREVERIFY Applied`, `FTFCOMPAREQC`,
   `FTFCOMPARERUN` again, `FTFCOMPAREAPPLY`, `COMPAREVERIFY Applied`,
   `FTFCOMPARERESTORE`, `COMPAREVERIFY Restored`, `FTFCOMPAREAUDIT`, `FTFCOMPAREAPPLY`,
   `COMPAREVERIFY Applied`, `FTFCOMPAREDECIDE Unresolved`, `FTFCOMPAREAPPLY`,
   `FTFCOMPARESAVE`, `SAVEAS`;
6. judges the log: every `[VERIFY PASS]` / `[VERIFY FAIL]` line, three
   `COMPAREVERIFY: 0 failure(s)`, the audit reporting CLEAN after the restore, no
   "NOT changed" or command errors, and the deliverable
   `tests\LiveSmoke\compare\compare-out\seedcompare-ACC.dwg`.

One thing in the log is expected to look alarming and is not: the first `FTFCOMPAREQC`
reports `Final QC: FAILED` with a `[FAIL] DISCREPANCIES` line, because the seeded rim
label conflict has not been decided yet. The script knows that; it checks the last QC,
after `FTFCOMPAREDECIDE Unresolved`, through the deliverable being written.

After the comparison passes, the script runs the FTFTRIM scenario the same way: a
site drawing with its limits of survey on a no-plot layer, attached to an aerial host;
`FTFTRIM`, `TRIMVERIFY Applied`, `FTFTRIMRESTORE`, `TRIMVERIFY Restored`, `FTFTRIM`
again. It checks that the aerial inside the limits is on `-NP` layers, that the two
crossing curbs are cut at the limits with their outside parts plotting, that the
off-by-0.9-ft seams are clouded while the aligned ones are not, that our xref is
untouched, and that restore removes everything.

The result is one line: `Smoke test passed: ...` or `FAILED: ...` with the failing
checks, the log path (`tests\LiveSmoke\compare\compare_out.log`), the log's last lines
and `compare-progress.txt`, a step-by-step marker file every harness step and every
plugin command writes before it runs, so a run that dies without output still says
where (accoreconsole's exit code is on the log's last line). What each check
proves is in the `COMPAREVERIFY` section of `CompareTestHarness.cs`: matched curb,
fence and points nonplot; the half-represented curb nonplot with a split copy of the
rest plotting; the fence beyond the xref clip still plotting; the symbol block pointed
at a nonplot twin; the fixed-layer point on the nonplot point style with its label
hidden; the missing curb, pole and handhole untouched; the conflicting rim label and the
invert label they lack still plotting; and after restore, every one of those back, no
split copies, no twins, the original point style and label visibility back.

Until this passes, do not use FTFCOMPARE on a project drawing.

## 5. Open a project drawing and run the comparison

Work in a **copy** of the survey drawing. Any of these are fine; the plugin does not
require a particular one:

- the working copy stays your comparison drawing (apply, review, deliver from it);
- after applying, `SAVEAS` to a separate comparison drawing: the applied record and the
  decisions travel inside the DWG, and the result and report files follow the new name;
- reopen a comparison drawing later and run again: the earlier changes are restored
  first, the comparison re-runs, your decisions are re-applied where the feature is
  unchanged, and `FTFCOMPAREAPPLY` applies the new plan.

Steps:

1. Open the copy. The consultant data is either attached as an xref (the default) or a
   separate file: `FTFCOMPARESOURCE`, choose `File` and pick it. Nothing is attached to
   your drawing in File mode.
2. Optional: `FTFCOMPAREAREA` to pick the comparison limits (a polygon, or a closed
   polyline). Without it, the consultant data's own extent plus 10 ft is the limit:
   survey beyond it is never called missing, and a line of ours that crosses that edge is
   sent to review rather than cut (the profile's `boundary.outsideLimitsAction`). When
   the consultant data is clipped or covers less than the survey, set an area, or set
   `boundary.useConsultantExtentWhenNoBoundary` to false in the profile to compare the
   whole drawing (the smoke test does the latter).
3. `FTFCOMPARE`, then **Run comparison**. On the command line, read:
   - the summary counts (matched / missing / partial / discrepancies / need review /
     not evaluated / excluded);
   - "Consultant layers the profile does not map": those layers were not compared, and
     anything of ours beside them is sent to review rather than called missing;
   - any xref the reader could not load or that is hidden (frozen / off / no-plot
     insert layer): the data is then INCOMPLETE and nothing is called missing.
   Out of tolerance: a counterpart found but more than 0.5 ft away horizontally, or
   more than 0.5 ft different vertically (their rim may be a block attribute or a label
   beside the symbol), is a discrepancy. Ours keeps plotting, gets a revision cloud and a
   note on layer `PMX-COMP-REVIEW`, and is listed in the CSV with the same note. The
   marks are redrawn on every run and erased by `FTFCOMPARERESTORE`.
   Layering: a matched object goes to its own layer's name plus `-NP` (for example
   `V-UTIL-POWR-POLE-NP`), created no-plot if missing; an existing `-NP` layer is forced
   no-plot. The profile's `nonplotLayers.mode` = `Fixed` uses the `PMX-COMP-NP-*` set
   instead.
4. Map the consultant's layers: `FTFCOMPAREPROFILE`, `SaveAs` with the consultant's
   name, then `Edit` (opens the JSON). Under each category's `"consultant"` block, add
   their layer wildcards and block names; save; run again. The profile is reused for
   every site from that consultant.
5. Review. The window opens on the "Needs attention" filter: discrepancies, review
   items, not-evaluated objects and partial lines. Click a row: the drawing zooms, ours
   is highlighted magenta and theirs cyan. Decide with the buttons (Accept match, Keep
   plotting, Make nonplot, Accept partial, Exclude). "Accept all confident" takes the
   obvious matches; "Keep all unresolved plotting" settles the rest conservatively.
6. **Apply approved changes**. Watch the command line for `NOT changed` lines: those
   are objects the plugin refused to change because they would still have plotted or
   their plot state could not be determined (a point group overriding a point style, for
   example). They are listed in the window with the reason and block the final QC until
   you decide them.
7. `FTFCOMPAREQC`. Every line is PASS, or it tells you what is left. `FTFCOMPARESAVE`
   is optional: it writes `<name>-ACC.dwg` plus the CSV and QC text to the delivery
   folder (Settings > Survey Comparison, or the profile), and refuses while a blocking
   check fails. You can equally `QSAVE` / `SAVEAS` the working drawing yourself.

## 6. How to tell whether the results are correct

Do this on the first real site before trusting it:

1. **Counts make sense.** On a site the consultant mapped well, most linework and
   structures should be Matched; Missing should be the features you know they lack.
   Hundreds of Not Evaluated means unmapped layers (step 5.4), not missing data.
2. **Spot-check each status in the window.** Pick five Matched rows: zoom, confirm the
   cyan consultant geometry really is the same feature (not a parallel curb or a storm
   structure matched to a sanitary one). Pick five Missing rows: confirm the consultant
   shows nothing there. Open the Partial rows: the uncovered stations are listed with
   lengths; after Apply the copies on the original layer are exactly the parts the
   consultant lacks (no sliver, no gap).
3. **The report.** `<name>.ftf-compare.csv` beside the drawing has one row per survey
   object: status, consultant handle, horizontal and vertical difference, original and
   final plotting layer, decision, notes. Sort by Status; read every Discrepancy note.
4. **Plot preview, the decisive check.** After Apply, PLOT > Preview (or publish to PDF)
   the working drawing, and preview the untouched copy beside it. On the comparison
   preview: nothing on the `-NP` layers appears; the matched COGO points' markers
   and labels are gone; the split copies of partial lines appear; every Missing feature
   appears; the consultant's content is unchanged.
5. **`FTFCOMPAREQC` all PASS**, including DUPLICATES-NONPLOT (every matched duplicate
   really does not plot: layer, visibility, block content, point style and label
   resolved) and PLOT-VERIFIED (nothing undeterminable).
6. **Nothing of theirs changed.** The consultant file's modified date is unchanged;
   their xref layers and geometry are as before.

If any of these is wrong, do not deliver; restore (step 7), save the CSV and the log,
and send me the handle(s) and what you saw.

## 7a. Trim the aerial base for the ACC merge (FTFTRIM)

Open a copy of the aerial base drawing from ACC. Attach each finished site drawing as an
xref (or have one file ready). Make sure each site drawing has its limits of survey as a
closed polyline on a limits layer (`V-SURV-LIMITS` or anything matching
`trim.limitsLayers` in the profile); plot state does not matter. Then:

```
FTFTRIM
  Our survey comes from [Xrefs/File] <Xrefs>:   Enter
  Limits of survey [Layer/Select] <Layer>:      Enter   (or Select, and pick the polylines)
```

It reports the counts: aerial objects inside (now on their `-NP` layers), split at the
edge (outside part plotting), not trimmed (hatches, dimensions: check by hand), and the
seams: aligned, discrepancies, no continuation, for review. Every seam that is not
aligned has a cloud and a note at the cut; `<name>.ftf-seams.csv` lists them all with
offset, elevation difference and direction, `<name>.ftf-trim.csv` lists every trimmed
object. Nothing of ours is changed; fix the aerial to ours by hand where a seam is off.
`FTFTRIMRESTORE` puts the aerial back exactly and removes the pieces and marks; running
`FTFTRIM` again restores first.

## 7. Restore the drawing if something is wrong

```
FTFCOMPARERESTORE
FTFCOMPAREAUDIT
```

Restore puts every recorded object back exactly: layer, visibility, block definition,
point style, label visibility; erases the split copies (and any copy no record owns);
erases the nonplot twin block definitions nothing uses; erases the revision clouds and
notes. Decisions are kept for the next
run. The audit then prints what is left; it must say
`CLEAN: nothing from the comparison is in effect in this drawing`. If it does not, the
lines above it say what remains (the applied record, split copies, twins, restyled
points; objects on `-NP` layers are listed but may be ones placed there by hand); send me
those lines.

Because the original survey drawing and the consultant's files are never written,
the fallback is always to discard the working copy and copy the original again.
