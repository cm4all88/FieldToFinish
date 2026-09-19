#!/usr/bin/env python3
"""Turn a recorded survey PDF (or image) into an FTF OCR sidecar (.ocr.json) with tesseract.

The Civil 3D plugin reads documents with the OCR engine Windows ships with. This script does
the same job with tesseract on any machine -- for testing the extraction outside Civil 3D, or
for an office that prefers tesseract -- and writes the DocumentText schema FTFRECORD reads
when its OCR engine is set to Sidecar (Settings > Recorded Surveys).

    pdf-to-sidecar.py PLAT.pdf [--dpi 300] [--rotations 0,90,270,180] [--pages 1,2] [--out DIR]

Each page is rendered, turned clockwise by each rotation, read with tesseract, and every hit is
mapped back onto the unrotated page with the same mathematics as FieldCodes.RecordSurvey.
PageGeometry.Unrotate. Word confidences come from tesseract (0..1). The page images are saved
next to the sidecar so the review window can show them. Nothing here decides what a survey
call is; that is the plugin's job, and it is unit tested.

Requires: pymupdf, pillow, tesseract (the `tesseract` command on PATH).
"""
import argparse, datetime, json, math, os, subprocess, sys, tempfile

try:
    import pymupdf
except ImportError:
    pymupdf = None
from PIL import Image

def rotated_canvas(w, h, deg):
    t = math.radians(deg)
    c, s = abs(math.cos(t)), abs(math.sin(t))
    return math.ceil(round(w * c + h * s, 6)), math.ceil(round(w * s + h * c, 6))

def unrotate(box, deg, cw, ch, pw, ph):
    """Box (x, y, w, h) on a canvas turned clockwise by deg, back to the page. Same maths as PageGeometry.Unrotate."""
    x, y, w, h = box
    t = math.radians(deg)
    cos, sin = math.cos(t), math.sin(t)
    rcx, rcy, pcx, pcy = cw / 2.0, ch / 2.0, pw / 2.0, ph / 2.0
    xs, ys = [], []
    for (cx, cy) in [(x, y), (x + w, y), (x + w, y + h), (x, y + h)]:
        dx, dy = cx - rcx, cy - rcy
        xs.append(pcx + dx * cos + dy * sin)
        ys.append(pcy - dx * sin + dy * cos)
    d = (deg + 180) % 360 - 180
    return {"x": min(xs), "y": min(ys), "w": max(xs) - min(xs), "h": max(ys) - min(ys), "rot": d}

def tesseract_tsv(image_path, psm):
    out = subprocess.run(["tesseract", image_path, "stdout", "--psm", str(psm), "tsv"], capture_output=True, text=True)
    if out.returncode != 0:
        raise RuntimeError(out.stderr.strip())
    rows = [r.split("\t") for r in out.stdout.splitlines()]
    head = rows[0]
    words = []
    for r in rows[1:]:
        if len(r) != len(head):
            continue
        rec = dict(zip(head, r))
        if rec["level"] != "5" or not rec["text"].strip():
            continue
        conf = float(rec["conf"])
        if conf < 0:
            continue
        words.append({"key": (rec["block_num"], rec["par_num"], rec["line_num"]), "text": rec["text"],
                      "x": int(rec["left"]), "y": int(rec["top"]), "w": int(rec["width"]), "h": int(rec["height"]), "conf": conf / 100.0})
    return words

def group_into_lines(words):
    """Sparse-text mode hands back one word per block as often as not; a line-level engine (the
    plugin's Windows OCR) returns whole labels. Join words that sit on one baseline and nearly
    touch, so 'N 88° 37'02" W' is one line here too. Tesseract's own line ids are kept when the
    words share them."""
    words = sorted(words, key=lambda w: (w["y"] + w["h"] / 2.0, w["x"]))
    lines = []
    for w in words:
        cy = w["y"] + w["h"] / 2.0
        placed = False
        for line in lines:
            last = line[-1]
            lcy = last["y"] + last["h"] / 2.0
            h = max(w["h"], last["h"], 1)
            same_row = abs(cy - lcy) <= 0.6 * h
            gap = w["x"] - (last["x"] + last["w"])
            if (w["key"] == last["key"] or (same_row and -0.3 * h <= gap <= 1.4 * h)) and w["x"] >= last["x"]:
                line.append(w)
                placed = True
                break
        if not placed:
            lines.append([w])
    return lines

def read_rotated(page_png, deg, cw, ch, pw, ph, psm, workdir):
    img = Image.open(page_png)
    if deg % 360 != 0:
        # PIL rotates counter-clockwise for a positive angle; the plugin turns the page clockwise.
        canvas = img.rotate(-deg, expand=True, fillcolor="white")
    else:
        canvas = img
    path = os.path.join(workdir, "rot%03d.png" % (deg % 360))
    canvas.save(path)
    cw2, ch2 = canvas.size
    words = tesseract_tsv(path, psm)
    result = []
    for ws in group_into_lines(words):
        ws.sort(key=lambda w: w["x"])
        x0 = min(w["x"] for w in ws); y0 = min(w["y"] for w in ws)
        x1 = max(w["x"] + w["w"] for w in ws); y1 = max(w["y"] + w["h"] for w in ws)
        line = {"text": " ".join(w["text"] for w in ws),
                "box": unrotate((x0, y0, x1 - x0, y1 - y0), deg, cw2, ch2, pw, ph),
                "conf": min(w["conf"] for w in ws),
                "words": [{"text": w["text"], "box": unrotate((w["x"], w["y"], w["w"], w["h"]), deg, cw2, ch2, pw, ph), "conf": w["conf"]} for w in ws],
                "pass": float(deg)}
        result.append(line)
    return result

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("document")
    ap.add_argument("--dpi", type=int, default=300)
    ap.add_argument("--rotations", default="0,90,270,180")
    ap.add_argument("--pages", default="")
    ap.add_argument("--psm", type=int, default=11, help="tesseract page segmentation mode; 11 = sparse text, right for a plat")
    ap.add_argument("--out", default="")
    args = ap.parse_args()

    doc = args.document
    out = args.out or os.path.dirname(os.path.abspath(doc))
    os.makedirs(out, exist_ok=True)
    stem = os.path.splitext(os.path.basename(doc))[0]
    rotations = [float(r) for r in args.rotations.split(",") if r.strip()]
    wanted = {int(p) for p in args.pages.split(",") if p.strip()}

    page_images = []
    if doc.lower().endswith(".pdf"):
        if pymupdf is None:
            sys.exit("pymupdf is needed for PDF input: pip install pymupdf")
        pdf = pymupdf.open(doc)
        for i, page in enumerate(pdf):
            if wanted and (i + 1) not in wanted:
                continue
            pix = page.get_pixmap(dpi=args.dpi)
            path = os.path.join(out, "%s-page-%03d.png" % (stem, i + 1))
            pix.save(path)
            page_images.append((i + 1, path, pix.width, pix.height))
    else:
        img = Image.open(doc)
        path = os.path.join(out, "%s-page-001.png" % stem)
        img.convert("RGB").save(path)
        page_images.append((1, path, img.width, img.height))

    text = {"schema": "ftf-record-ocr-1", "document": os.path.abspath(doc), "reader": "tesseract %s dpi, rotations %s, psm %d" % (args.dpi, args.rotations, args.psm),
            "readUtc": datetime.datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%SZ"), "pages": [], "notes": []}
    with tempfile.TemporaryDirectory() as work:
        for number, path, pw, ph in page_images:
            lines = []
            for deg in rotations:
                cw, ch = rotated_canvas(pw, ph, deg)
                try:
                    hits = read_rotated(path, deg, cw, ch, pw, ph, args.psm, work)
                except Exception as ex:
                    text["notes"].append("page %d at %g degrees: %s" % (number, deg, ex))
                    continue
                print("page %d rotation %g: %d lines" % (number, deg, len(hits)), file=sys.stderr)
                lines.extend(hits)
            text["pages"].append({"number": number, "widthPx": pw, "heightPx": ph, "dpi": args.dpi, "lines": lines, "image": os.path.basename(path)})

    sidecar = os.path.join(out, stem + ".ocr.json")
    with open(sidecar, "w") as f:
        json.dump(text, f, indent=1)
    print(sidecar)

if __name__ == "__main__":
    main()
