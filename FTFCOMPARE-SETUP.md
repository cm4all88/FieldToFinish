# FTFCOMPARE: build, install and test on Civil 3D 2024

Short version: clone the repository, run `deploy\build-install-test.ps1`, read its
result, then open a copy of one project drawing and run `FTFCOMPARE`.

Status: the comparison engine is unit tested (1,051 tests). The Civil 3D side compiles
against the 2024 API surface but **has not yet run inside Civil 3D**. Nothing below
reports it as verified until step 4 passes on your machine and step 6 checks out on a
real drawing.

## 1. What to get from GitHub

Repository `cm4all88/FieldToFinish`, branch `claude/kind-hamilton-apylzj`.

```
git clone https://github.com/cm4all88/FieldToFinish.git C:\dev\FieldToFinish
cd C:\dev\FieldToFinish
git checkout claude/kind-hamilton-apylzj
```

(Or download the branch as a ZIP from GitHub and extract it; any folder works, the
scripts find their own paths.) Everything FTFCOMPARE needs is in that branch: the
solution, `config\compare-profiles\Default.json`, the scripts under `deploy\` and
`tests\LiveSmoke\`.

Prerequisites on the machine: Civil 3D 2024, the .NET 8 SDK
(https://dotnet.microsoft.com/download), PowerShell 5.1 or later. The .NET Framework
4.8 C# compiler used for the smoke harness is part of Windows.

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
4. copies the stock NCS template to `tests\LiveSmoke\compare\seedcompare.dwg`, writes a
   settings file beside it so the run does not depend on your personal settings, and
   generates the accoreconsole script with the real paths;
5. runs, in accoreconsole with the C3D product: `COMPARESEED` (a consultant drawing
   attached as a clipped xref, with a curb, half of a second curb, a fence, one pole, a
   storm manhole with a rim label; and our survey: two curbs, a third curb they lack, a
   fragmented fence, a symbol block whose definition draws on its own layer, four COGO
   points, one with a point style that fixes its marker layer, rim and invert labels),
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

The result is one line: `Smoke test passed: ...` or `FAILED: ...` with the failing
checks and the log path (`tests\LiveSmoke\compare\compare_out.log`). What each check
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
   polyline). Without it, the consultant data's own extent plus 10 ft is the limit, and
   survey beyond it is never called missing.
3. `FTFCOMPARE`, then **Run comparison**. On the command line, read:
   - the summary counts (matched / missing / partial / discrepancies / need review /
     not evaluated / excluded);
   - "Consultant layers the profile does not map": those layers were not compared, and
     anything of ours beside them is sent to review rather than called missing;
   - any xref the reader could not load or that is hidden (frozen / off / no-plot
     insert layer): the data is then INCOMPLETE and nothing is called missing.
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
   preview: nothing on the `PMX-COMP-NP-*` layers appears; the matched COGO points' markers
   and labels are gone; the split copies of partial lines appear; every Missing feature
   appears; the consultant's content is unchanged.
5. **`FTFCOMPAREQC` all PASS**, including DUPLICATES-NONPLOT (every matched duplicate
   really does not plot: layer, visibility, block content, point style and label
   resolved) and PLOT-VERIFIED (nothing undeterminable).
6. **Nothing of theirs changed.** The consultant file's modified date is unchanged;
   their xref layers and geometry are as before.

If any of these is wrong, do not deliver; restore (step 7), save the CSV and the log,
and send me the handle(s) and what you saw.

## 7. Restore the drawing if something is wrong

```
FTFCOMPARERESTORE
FTFCOMPAREAUDIT
```

Restore puts every recorded object back exactly: layer, visibility, block definition,
point style, label visibility; erases the split copies (and any copy no record owns);
erases the nonplot twin block definitions nothing uses. Decisions are kept for the next
run. The audit then prints what is left; it must say
`CLEAN: nothing from the comparison is in effect in this drawing`. If it does not, the
lines above it say what remains (objects still on a `PMX-COMP-NP-*` layer, split copies,
twins, restyled points); send me those lines.

Because the original survey drawing and the consultant's files are never written,
the fallback is always to discard the working copy and copy the original again.
