# Manual Civil 3D verification (about 45 minutes)

Do this after `deploy\Run-TraceBaseline.ps1` reports all PASS. It uses a **throw-away drawing and synthetic points**; never a production drawing.

Everything is typed at the Civil 3D command line unless it says otherwise. After each command, look at the command line for error text.

## Before you start Civil 3D
If you have a FieldToFinish installation, run `deploy\Switch-TraceTestMode.ps1 -Begin` first and `-End` when you finish (see `docs\trace\COEXISTENCE.md`). It only renames the folder and proves nothing inside changed.

## 0. Make the test drawing
1. Start Civil 3D 2024. New drawing from **_Autodesk Civil 3D (Imperial) NCS.dwt**.
2. Save it as `<repo>\logs\trace-test\sample.dwg` (any folder you like works; this one keeps things together). Remember the folder: reports are written beside the drawing.
3. Import the points: type `IMPORTPOINTS` (if Civil 3D does not know it: Toolspace, Prospector tab, right-click **Points**, **Create…**, then the *Import Points* button). Choose format **PNEZD (comma delimited)** and the file `tests\Manual\trace-sample-points.csv`. Zoom Extents (`Z`, `E`).
4. You should see 12 points. Check the ribbon has an **FTF** tab and `FTFWHERE` prints a rules path inside `...\ApplicationPlugins\TRACE.bundle\Contents\2024\` and an office folder under `%APPDATA%\TRACE`.
5. Draw one stray `LINE` anywhere and one `TEXT` saying `HAND` (default layer). These are "technician work": they must survive everything below.

**Count objects.** Several steps ask you to count. Type `SELECT`, Enter, `ALL`, Enter. Civil 3D prints `N found`. Press Esc. Call the first count **N0** (points, your line and your text).

## 1. Trees and driplines (workflow 3)
| Do | Expect |
|---|---|
| `FTFTREES` | Command line summarises 5 trees. No error. Unhandled report mentions `XYZ 12`. |
| `FTFDRIP` | Circles around the 5 trees; where trees 1 and 2 overlap the arcs are trimmed (outer outline only). |
| `FTFLABELS` | Labels such as `18" CONIFER`, cluster labels (`CLUSTER OF ... STEMS`) for points 3 and 5, `POLE 1234`, `POLE 1235 (DEAD)`, `STOP SIGN`, `YIELD SIGN`. No label sits on top of another. |
Count: **N1**.

## 2. Re-run safety (workflow 1)
Run `FTFTREES`, `FTFDRIP`, `FTFLABELS` again, in that order. Count again.
**Pass: the count equals N1.** If it is higher, something duplicated: send a screenshot and both numbers.

## 3. Label movement (workflow 2)
1. `MOVE` one label (for example the `POLE 1234` label) about 15 units to the left. Note where it is.
2. Run `FTFLABELS`, then `FTFORDER`.
**Pass:** the moved label is exactly where you left it, no new copy of it appeared, the count equals N1, and other labels still avoid it.

## 4. Cleanup (workflow 1)
Run `FTFCLEAN`. Count again.
**Pass:** the count returns to **N0**, your stray line and your `HAND` text are still there, the 12 points are still there. Then run `FTFCLEAN` once more: nothing changes.
(Then re-run `FTFTREES`, `FTFDRIP`, `FTFLABELS` so step 5 has something to work with.)

## 5. Accounting of points (quality control)
Open the files beside the drawing: `sample.ftf-exceptions.csv` and `sample.ftf-unhandled.csv`.
**Pass:** every one of the 12 points is explained somewhere: drawn, deliberately ignored (`GS`), nothing-to-draw because bare (`PP`), or listed as unhandled (`XYZ 12`). Nothing is silently missing. Write down any point you cannot find an explanation for.

## 6. Line labelling (workflow 4)
1. Copy `tests\Manual\rules.json` into the same folder as `sample.dwg`.
2. Make a layer `C-FENC`. Draw a `PLINE` of three or four segments on it.
3. `FTFLINES`: the fence should be listed as identified (Fence). `FTFLABELLINE`, click the polyline, slide the cursor along it and click to place `CHAIN LINK FENCE`.
4. Run the same label command again on the same line and place another. Check how the second is handled (a second label is allowed if you place one deliberately; nothing should appear by itself).
5. `FTFCLEAN` again; confirm the polyline remains and the label is removed (labels are TRACE-made).
**Pass:** identification works, the label is readable along the line, nothing duplicates by itself.

## 7. Draft lines (workflow 5)
1. `FTFDRAWLINE`. Choose **Boundary**, pick a start point, choose **Bearing**, enter `N45.0000E`, distance `100`. Then add a second course with **Azimuth** `90` distance `50`. Use **Undo** once, then **Finish**.
2. Check each course has a bearing/distance label (for example `N 45°00'00" E 100.00'`) and the label text reads left-to-right.
3. `FTFCLEAN`: the drafted lines must **stay** (they are your work by design).
4. `FTFDRAFTCLEAN`, choose **Annotation**: labels go, lines stay. Run `FTFDRAFTCLEAN` **Everything** and confirm; lines go.
**Pass:** as described; no error text. If it says a layer is missing or asks you to choose one, copy the exact message to me; that is a finding, not your mistake.

## 8. Settings window
Type `FTFSETUP`. Open every tab, change nothing, close. Then change one harmless value (for example a label text height), click Save, reopen, and confirm it stuck. Look for `%APPDATA%\TRACE` containing a settings file afterwards (and **not** `%APPDATA%\FieldToFinish`).

## What to send me
- The zip printed by `Run-TraceBaseline.ps1`.
- The N0 and N1 counts and the count after step 2.
- For each of the 8 sections: **worked**, **did not**, or **partly**, with a screenshot of anything odd.
- For any command error: press F2 in Civil 3D, copy the command-line text, paste it to me.
- `sample.ftf-exceptions.csv` and `sample.ftf-unhandled.csv`.
