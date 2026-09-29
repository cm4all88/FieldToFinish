# Survey CAD Training (Levels 1-5)

A five-level survey CAD course built on one real topographic survey. Each level has a
START drawing, a COMPLETED answer key and a trainee guide (.docx and .pdf) with
before/after pictures. Everything is generated from the source survey, so the answer keys
always match what the commands produce.

| Level | Software | Topic |
|---|---|---|
| 1 | AutoCAD | Units, snaps, layers, boundary checks, cleanup, symbols, annotation, sheet |
| 2 | AutoCAD | QA/QC with QSELECT/OVERKILL, annotative standards, attributed blocks, xrefs, multi-scale sheet |
| 3 | Civil 3D | Field to finish: clean the point file, survey database import, point groups, fix field codes, jumpers |
| 4 | Civil 3D | EG surface: point group, breaklines, boundary, contours, compare with the finished base |
| 5 | Civil 3D | Storm pipe network from measured structures, centerline alignment, QA and delivery |

## Source data (not in the repo)

`source/` is git-ignored because it holds a real client survey. To rebuild, put two files there:

- `source/base_acad.dxf`: the survey base after Civil 3D `-EXPORTTOAUTOCAD`, converted to DXF
  (LibreDWG `dwg2dxf` works: `dwg2dxf -y -o base_acad.dxf ACAD-base.dwg`).
- `source/points.txt`: the PNEZD point export, as delivered by the crew.

Then run `./build_all.sh`. Outputs land in `levelN/output/` (also git-ignored).

## How it works

- `lib/base.py` rebuilds the exported base into a clean DXF from geometry alone: entities, SSV-
  symbol blocks, layers, linetypes and multileaders. The Civil 3D export carries broken
  dictionaries, and a straight copy would bring them along. It also restores the point-symbol
  scale that the DWG conversion garbles.
- `lib/ftf.py` processes point descriptions into figures using the office linework rules in
  `config/rules.json` (PMX-Universal: B/E/C/P/T, trailing-number strings, never-draw codes).
- Each `levelN/build_drawings.py` writes START/COMPLETED plus `answers.json`. The guide
  (`build_guide.js`, using `lib/guide.js`) reads its numbers from that file.
- The Civil 3D levels (3-5) can't ship Civil 3D objects in a DXF. Their COMPLETED files hold the
  reference geometry (figures, contours, storm linework), and the guides carry the check values.
