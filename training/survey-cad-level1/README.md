# Survey CAD Training - Level 1

Plain-AutoCAD survey drafting exercise: one residential lot, 22 exercises, 1" = 30' sheet.

| File | What it is |
|---|---|
| `output/Survey_CAD_Level1_START.dxf` | Trainee start file |
| `output/Survey_CAD_Level1_COMPLETED.dxf` | Answer key (START with every exercise applied) |
| `output/Survey_CAD_Level1_Guide.docx` / `.pdf` | Trainee guide |
| `output/Survey_CAD_Level1_Package.zip` | The four files above, for handing out |

## Rebuilding

Don't hand-edit the DXFs. Change the scripts and rebuild:

```
pip install ezdxf matplotlib pillow
npm install docx
python build_drawings.py     # START/COMPLETED DXF + answers.json
python render_images.py      # PNGs used in the guide
node build_guide.js          # guide .docx
```

`build_drawings.py` computes each COMPLETED result the way the AutoCAD command would
(FILLET R0 = line intersection, EXTEND to the chosen boundary, ROTATE Reference to the
curb, and so on), so the answer key always matches what trainees get.
