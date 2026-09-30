# Survey CAD Training - Finish the Drawing

A drafting course for new survey drafters. Trainees get a drawing right after field to
finish (points in, linework drawn from the field codes, symbols placed, nothing labeled)
and finish it into the real survey base. A checkpoint PDF after each stage shows what
their drawing should look like and the values to check.

| Stage | Trainee does |
|---|---|
| 0 | Open, units, snaps, reading point descriptions |
| 1 | Boundary: FILLET a corner, draw the missing line by bearing, EXTEND, TRIM, JOIN, area |
| 2 | Linework: erase jumpers and the ASPH zig-zag, draft wall footings and curb ties, LAYMCH, MATCHPROP |
| 3 | Storm and sewer: rotate catch basins to the curb, draw pipes from measuredowns, structure leaders |
| 4 | Labels: rotate road names, control, monuments, trees, surface callouts, FFEs, boundary labels, setbacks |
| 5 | QA, viewport at 1" = 20', plot |

Package (`course/output/SURVEY_CAD_Package.zip`): `SURVEY_CAD_START.dxf`, `CHECKPOINT_1-5.pdf`,
`SURVEY_CAD_Guide.docx/.pdf`, `SURVEY_CAD_Drafter_Reference.pdf` (12-page progressive training path and desk reference, Levels 1-9, portrait for a binder; content in `course/reference_content.js`, verification notes in `course/REFERENCE_SOURCES.md`, old-sheet inventory in `course/REFERENCE_INVENTORY.md`; drop button screenshots in `course/icons/NAME.png` to show them), `SURVEY_CAD_COMPLETED.dxf`, and the raw `SURVEY_CAD_POINTS.txt` for reference.

START is the finished base with the finishing work taken back out and the real
field-to-finish problems put in (jumpers from out-of-sequence office points, the ASPH
zig-zag, missing footings and ties). COMPLETED is the finished base with every label and
no contours, since the surface is a later course.

## Source data (not in the repo)

`source/` is git-ignored because it holds a real client survey. Put two files there:

- `source/base_acad.dxf`: the finished base after Civil 3D `-EXPORTTOAUTOCAD`, converted to DXF
  (LibreDWG: `dwg2dxf -y -o base_acad.dxf ACAD-base.dwg`).
- `source/points.txt`: the PNEZD point export as delivered by the crew.

Then run `./build_all.sh`.

## Code

- `course/`: builds the trainee files, the checkpoint states and PDFs, and the guide.
- `lib/base.py` rebuilds the exported base into a clean DXF from geometry alone. The Civil 3D
  export carries broken dictionaries and garbled symbol scales, and neither is copied over.
- `lib/ftf.py` processes descriptions into figures with the office linework rules in
  `config/rules.json` (PMX-Universal).
- `level1/`, `level3/`, `level4/`, `level5/build_drawings.py` are the tested building blocks
  the course uses: the finished base and sheet, field-to-finish fixes and cleanup, surface
  layers, and structure leader parsing.
- `build_all.sh` fails the build if any DXF value holds a `^` or a non-ASCII character.
  AutoCAD's DXF reader rejects the whole file for a bare `^` ("Error in APPID Table").
