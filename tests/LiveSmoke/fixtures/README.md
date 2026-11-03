# Survey figure fixture

FTF has to read Civil 3D **survey figures** correctly before the point ledger and continuity work
can rely on them. Survey figures can only be made by a Civil 3D survey database import, which
cannot run headless. So this fixture is built **once, by hand, in Civil 3D**. After that,
`tests\LiveSmoke\run-figures.ps1` checks every FTF build against it, headless.

| File | What it is |
|---|---|
| `figures-synthetic.csv` | 30 synthetic points, PNEZD comma delimited, invented local coordinates (northing and easting 5000 to 5600). No project data. |
| `figures-synthetic.dwg` | **Not in the repository.** You build it from the CSV. It lives at `C:\dev\FTF-fixtures\figures-synthetic.dwg`. A saved drawing carries the saving user's name and survey database path, and this repository is public. `.gitignore` refuses a `.dwg` here. |

## What the points cover

| Points | Coding | What it checks |
|---|---|---|
| 1001-1004 | `FNC1 B` ... `FNC1 E` | An open figure with a figure number (FNC1 is not FNC). |
| 1005-1006 | `FNC1 B`, `FNC1 E` | The same figure name begun again after it ended. The fixture records what Civil 3D does with it. |
| 1007-1008 | `FNC B`, `FNC E` | An unnumbered figure of the same code. |
| 1010-1015 | `EP1`/`EP2`, alternating | Two strings shot interleaved. |
| 1020-1023 | `BLD1 B` ... `BLD1 C` | A figure closed with the close command. |
| 1030-1034 | `EP3 B`, `EP3 P`, `EP3`, `EP3 T`, `EP3 E` | A three-point curve (begin curve P, end curve T) between tangents. |
| 1040-1042 | `TOP1 B`, `TOP1 E TOE1 B`, `TOE1 E` | One shot on two figures, ending one and beginning the other. |
| 1050-1051 | `EC1 B`, `EC1 E` | EC as a figure code (edge of concrete), never "end curve". |
| 1060-1061 | `PP`, `WV` | Points with no linework. |

## Build it (once, about 10 minutes)

1. In Civil 3D 2024, start a new drawing from `_Autodesk Civil 3D (Imperial) NCS.dwt`. Save it
   as `C:\dev\FTF-fixtures\figures-synthetic.dwg`. Create the folder if needed.
2. **Toolspace > Survey** tab: right-click **Survey Databases > New local survey database**. Name
   it `FTF-SYNTHETIC-FIGURES`.
3. Check the linework code set you will use: **Toolspace > Survey > Linework Code Sets**. Use the
   office set that crews code to. Its begin, end, close, begin curve and end curve codes should
   match `lineworkCommands` in `config\rules.json`: B, E, C, P, T. If they differ, use the office
   set as it is. The check then shows exactly where FTF and Civil 3D read the coding differently,
   which is what it is for. Note which set you used.
4. **Home > Import Survey Data**:
   - *Specify Database*: `FTF-SYNTHETIC-FIGURES`.
   - *Specify Data Source*: **Point File**. Choose `tests\LiveSmoke\fixtures\figures-synthetic.csv`,
     format **PNEZD (comma delimited)**.
   - *Specify Network*: create a new network, for example `SYNTHETIC`.
   - *Import Options*: tick **Process linework during import** and choose the linework code set.
     Tick **Insert figure objects** and **Insert survey points**. Leave the rest at their defaults.
   - **Finish**.
5. Check that figures appear in the drawing and under the database's **Figures**. Do not edit,
   move or rename anything; the fixture must be exactly what the import made.
6. Save and close the drawing. Close Civil 3D.

## Run the check

From the repository, with Civil 3D closed and an FTF build installed (UPDATEFTF):

```
.\tests\LiveSmoke\run-figures.ps1
```

- **Exit 0**: every check passed.
- **Exit 1**: a check failed. The console lists them.
- **Exit 2**: the fixture does not exist yet, so nothing was verified.

The run works on a copy in `%LOCALAPPDATA%\FieldToFinish\figures\<time>\`. The original fixture
is never opened for writing; its hash is checked before and after.

Please send back the console output and `figures-manifest.txt` from that folder. The manifest
records what Civil 3D made of each coding case: figure names, layers, closure, arcs, every
vertex and the shots at it, and any figure Civil 3D renamed. The continuity engine (Step 3) is
built to match it, not to guesses.

## What it proves, and what it does not

It proves, in Civil 3D 2024, against the real `SurveyFigure` API:

- each figure's `Name` and its points from `GetPoints(AllPoints)`;
- that FTF's vertex reader returns every one of those points, in order;
- that FTF's source-point reader finds the same shots at every vertex as an independent search,
  and that every vertex of a figure without arcs is at a shot;
- that FTF's linework parser agrees with Civil 3D's import:
  - every shot coded onto a figure lies on a figure of that name;
  - every figure is named for a figure its own shots are coded onto;
  - a figure coded closed is closed;
- that FTFLINES lists each figure under its full name (figure number kept), with its source,
  vertex count and shots, and writes the same inventory twice;
- that nothing changes: no figure, no point, nothing FTF-owned created.

It does not prove anything about figures made a different way (a field book import, figures
drawn from objects, edited figures), or about a different linework code set than the one used to
build it.
