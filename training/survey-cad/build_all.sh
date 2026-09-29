#!/usr/bin/env bash
# Rebuild every level: drawings, images, guides, PDFs and packages.
# Needs: python3 with ezdxf, matplotlib, pillow; node with the "docx" package; LibreOffice for PDFs.
set -euo pipefail
cd "$(dirname "$0")"
for n in 1 2 3 4 5; do
  echo "== Level $n"
  python3 level$n/build_drawings.py > /dev/null
  [ -f level$n/render_images.py ] && python3 level$n/render_images.py 2>&1 | grep -v "Ignoring fixed" || true
done
mkdir -p level4/output/images level5/output/images
python3 - <<'PY'
import sys; sys.path.insert(0, "lib")
import ezdxf, render
d = ezdxf.readfile("level4/output/SURVEY_CAD_L4_COMPLETED.dxf")
render.draw(d, d.modelspace(), "level4/output/images/contours.png", ((1124900, 733820), (1125345, 734050)), width_in=16, dpi=120)
for f, tag in (("START", "before"), ("COMPLETED", "after")):
    d = ezdxf.readfile(f"level5/output/SURVEY_CAD_L5_{f}.dxf")
    render.draw(d, d.modelspace(), f"level5/output/images/ex03_{tag}.png", ((1124880, 734020), (1125010, 734110)), width_in=7, dpi=160)
PY
for n in 1 2 3 4 5; do
  node level$n/build_guide.js
  (cd level$n/output && soffice --headless --convert-to pdf SURVEY_CAD_L${n}_Guide.docx > /dev/null 2>&1 || true)
  (cd level$n/output && rm -f SURVEY_CAD_L${n}_Package.zip && zip -q SURVEY_CAD_L${n}_Package.zip *.dxf *.docx *.pdf $( [ $n = 3 ] && echo SURVEY_CAD_L3_POINTS.txt ))
done
