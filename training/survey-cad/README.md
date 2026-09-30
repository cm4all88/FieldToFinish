# Survey CAD Training - Field to Finish

One drawing, six stages. Trainees start from a bare drawing and the crew's raw point
file and finish a real topographic survey base in Civil 3D. After each stage a
checkpoint PDF shows what their drawing should look like and the values to check.

| Stage | Trainee does |
|---|---|
| 1 | Units, coordinate system, office styles; draw the parcel from deed calls; closure, area |
| 2 | Clean the point file; survey database import; point groups; fix two bad field codes |
| 3 | Break jumpers, remove the ASPH figure, draft pads and curb ties |
| 4 | GROUND point group, breaklines, boundary, contours |
| 5 | Street and surface labels, structure/monument leaders, FFEs, setbacks, boundary labels |
| 6 | QA, sheet at 1" = 20', plot, EXPORTTOAUTOCAD, ETRANSMIT |

Package (`course/output/SURVEY_CAD_Package.zip`): `SURVEY_CAD_START.dxf` (bare),
`SURVEY_CAD_POINTS.txt` (as delivered), `SURVEY_CAD_RECORD.dxf` (ROW, centerline, surface
border), `CHECKPOINT_1-6.pdf`, `SURVEY_CAD_Guide.docx/.pdf`, `SURVEY_CAD_COMPLETED.dxf`.

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
