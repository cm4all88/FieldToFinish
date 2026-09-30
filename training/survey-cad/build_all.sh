#!/usr/bin/env bash
# Build the Field to Finish course: drawings, checkpoint PDFs, guide, package.
# Needs: python3 with ezdxf, matplotlib, pillow; node with the "docx" package; LibreOffice for the guide PDF.
set -euo pipefail
cd "$(dirname "$0")"
python3 course/build_drawings.py > /dev/null
python3 course/render_checkpoints.py 2>&1 | grep -v "Ignoring fixed" || true
node course/build_guide.js
node course/check_inventory.js
node course/build_reference.js
cd course/output
soffice --headless --convert-to pdf SURVEY_CAD_Guide.docx SURVEY_CAD_Command_Reference.docx > /dev/null 2>&1 || true
# Second pass: put each section's start page on the reference's cover sheet.
node -e 'const C=require("../reference_content");console.log(JSON.stringify(C.LEVELS.map(L=>[L.n,L.title])))' > section_titles.json
python3 - <<'PY'
import json, pymupdf as fitz
titles = json.load(open("section_titles.json"))
doc = fitz.open("SURVEY_CAD_Command_Reference.pdf")
pages = {}
for n, title in titles:
    for i in range(1, len(doc)):          # skip the cover, which lists every title
        if title in doc[i].get_text():
            pages[n] = i + 1
            break
json.dump(pages, open("section_pages.json", "w"))
PY
rm -f section_titles.json
(cd .. && node build_reference.js > /dev/null)
soffice --headless --convert-to pdf SURVEY_CAD_Command_Reference.docx > /dev/null 2>&1 || true
python3 - <<'PY'
# AutoCAD rejects a DXF containing a bare ^ (DXF escape character) or non-ASCII text.
import glob, sys
bad = []
for f in glob.glob("*.dxf"):
    L = open(f, encoding="utf-8").read().split("\n")
    for i in range(1, len(L), 2):
        if "^" in L[i] or any(ord(c) > 126 for c in L[i]):
            bad.append((f, i + 1, L[i][:40]))
if bad:
    sys.exit(f"DXF values AutoCAD may reject: {bad[:10]}")
PY
rm -f _state*.dxf SURVEY_CAD_Package.zip
zip -q SURVEY_CAD_Package.zip SURVEY_CAD_START.dxf SURVEY_CAD_POINTS.txt \
    SURVEY_CAD_COMPLETED.dxf SURVEY_CAD_Guide.docx SURVEY_CAD_Guide.pdf \
    SURVEY_CAD_Command_Reference.pdf CHECKPOINT_*.pdf
echo "built course/output/SURVEY_CAD_Package.zip"
