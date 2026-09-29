"""Level 2 - Production drafting: QA/QC, standards, attributes, xrefs and sheets.

Two drawings per state:
  BASE  - the survey base (Level 1's finished base). START has planted QA
          defects; COMPLETED has them fixed plus the control-point blocks.
  SHEET - a sheet file that xrefs the base. START is the title block only;
          COMPLETED has the xref, two scaled viewports and a control table.

Things a DXF written here cannot carry (annotation scale lists on objects,
layer states, per-viewport layer freezes) are checked in the guide instead.
"""
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
sys.path.insert(0, str(ROOT / "lib"))

import ezdxf  # noqa: E402
from ezdxf.enums import TextEntityAlignment  # noqa: E402
from ezdxf.math import Vec2  # noqa: E402

import base  # noqa: E402

L1 = base.load_level(1)

OUT = HERE / "output"
BASE_NAME = "SURVEY_CAD_L2_BASE"
TXT = base.TXT
CONTROL = [2000, 2001, 2002, 2003]
SHEET = (34.0, 22.0)
VP_OVERALL = (0.75, 7.25, 20.25, 21.25)     # 1" = 40'
VP_DETAIL = (20.75, 7.25, 30.25, 21.25)     # 1" = 10'
HOUSE_CENTER = Vec2(1125180.0, 733927.0)
DESC = {"XMAG": "MAG NAIL", "XNL": "MAG NAIL", "XHT": "HUB & TACK"}


def desc(p):
    return DESC.get(p["d"].split(" AKA")[0].strip(), p["d"])


def l1_base():
    """Level 1 finished base without its sheet or training bubbles."""
    doc, _ = L1.build(True)
    doc.layouts.new("Layout1")
    doc.layouts.delete("TOPO SHEET")
    for name in ("TRAIN-NOTES",):
        if name in doc.layers:
            doc.layers.remove(name) if not any(e.dxf.layer == name for e in doc.modelspace()) else None
    doc.header["$AUNITS"] = 4
    doc.header["$LUPREC"] = 2
    return doc


def plant_defects(doc, ans):
    msp = doc.modelspace()
    doc.styles.get("Standard").dxf.font = "arial.ttf"
    # A) contour labels dropped onto layer 0
    labels = [e for e in msp.query('MTEXT[layer=="V-TOPO-CONT-TEXT-E"]')]
    a = labels[::4]
    for e in a:
        e.dxf.layer = "0"
    # B) labels in the wrong text style
    txt = [e for e in msp.query("MTEXT") if e.dxf.layer in ("V-SURF-CONC-TEXT-E", "V-SURF-ASPH-TEXT-E")]
    b = txt[::3][:8]
    for e in b:
        e.dxf.style = "Standard"
    # C) duplicate linework (copied in place)
    curbs = [e for e in msp.query('LWPOLYLINE[layer=="V-SURF-CURB-E"]')]
    c = curbs[::2][:10]
    for e in c:
        msp.add_entity(e.copy())
    # D) walk linework left at an elevation
    conc = [e for e in msp.query('LWPOLYLINE[layer=="V-SURF-CONC-E"]')]
    d = conc[::3][:6]
    for i, e in enumerate(d):
        e.dxf.elevation = 64.0 + i * 0.25
    ans.update(defect_layer0=len(a), defect_style=len(b), defect_dupes=len(c), defect_elev=len(d))
    ans["crop_layer0"] = list(map(float, a[0].dxf.insert.vec2))
    ans["crop_style"] = list(map(float, b[0].dxf.insert.vec2))


def ctrl_block(doc):
    if "CTRL-PT" in doc.blocks:
        return
    b = doc.blocks.new("CTRL-PT", base_point=(0, 0))
    r = 1.2
    b.add_lwpolyline([(0, r * 1.15), (-r, -r * 0.58), (r, -r * 0.58)], close=True)
    b.add_circle((0, 0), 0.3)
    A = {"height": TXT, "style": "Survey"}
    # all invisible: the existing leaders already label the points; these feed the table
    for i, tag in enumerate(("PT", "DESC", "NORTHING", "EASTING", "ELEV")):
        b.add_attdef(tag, (1.8, -i * 2.0), dxfattribs={**A, "flags": 1})
    for e in b.query("ATTDEF"):
        e.dxf.prompt = e.dxf.tag.title()


def place_control(doc, pts):
    ctrl_block(doc)
    if "V-CTRL-TABL-E" not in doc.layers:
        doc.layers.add("V-CTRL-TABL-E", color=7)
    msp = doc.modelspace()
    for n in CONTROL:
        p = pts[n]
        for old in msp.query("INSERT"):          # the new block replaces the old control symbol
            if old.dxf.name.startswith("SSV-X") and Vec2(old.dxf.insert).isclose(Vec2(p["e"], p["n"]), abs_tol=0.01):
                msp.delete_entity(old)
        ref = msp.add_blockref("CTRL-PT", (p["e"], p["n"]), dxfattribs={"layer": "V-CTRL-PMX_-SYMB-E"})
        ref.add_auto_attribs({"PT": f"PMX #{n}", "DESC": desc(p),
                              "NORTHING": f"{p['n']:.3f}", "EASTING": f"{p['e']:.3f}", "ELEV": f"{p['z']:.2f}"})


def build_base(done):
    doc = l1_base()
    ans = {}
    if done:
        pts = {p["p"]: p for p in base.read_points()}
        place_control(doc, pts)
        # counts come from the START build
    else:
        plant_defects(doc, ans)
        ctrl_block(doc)                       # the trainee builds this in Ex 10; START has none
        doc.blocks.delete_block("CTRL-PT", safe=False)
    return doc, ans


# ---------------------------------------------------------------- sheet file
def title_block(doc, lay, done):
    W, H = SHEET
    if "G-ANNO-TTLB" not in doc.layers:
        doc.layers.add("G-ANNO-TTLB", color=7)
    if "Survey" not in doc.styles:
        doc.styles.new("Survey", dxfattribs={"font": "romans.shx", "width": 0.75})
    A = {"layer": "G-ANNO-TTLB", "style": "Survey"}

    def t(s, x, y, h, al=TextEntityAlignment.LEFT):
        lay.add_text(s, height=h, dxfattribs=A).set_placement((x, y), align=al)

    lay.add_lwpolyline([(0.5, 0.5), (W - 0.5, 0.5), (W - 0.5, H - 0.5), (0.5, H - 0.5)], close=True,
                       dxfattribs={"layer": "G-ANNO-TTLB", "const_width": 0.03})
    xs = 30.5
    lay.add_line((xs, 0.5), (xs, H - 0.5), dxfattribs={"layer": "G-ANNO-TTLB"})
    x0 = xs + 0.2
    t("PARAMETRIX", x0, H - 1.1, 0.26)
    t("SURVEY CAD TRAINING", x0, H - 1.45, 0.12)
    lay.add_line((xs, H - 1.7), (W - 0.5, H - 1.7), dxfattribs={"layer": "G-ANNO-TTLB"})
    t("TOPOGRAPHIC SURVEY", x0, H - 2.15, 0.15)
    t("TRAINING SITE", x0, H - 2.45, 0.12)
    t("LEVEL 2 - PRODUCTION", x0, H - 2.7, 0.10)
    t("OVERALL 1\" = 40'", x0, H - 3.1, 0.09)
    t("HOUSE DETAIL 1\" = 10'", x0, H - 3.3, 0.09)
    lay.add_line((xs, 1.3), (W - 0.5, 1.3), dxfattribs={"layer": "G-ANNO-TTLB"})
    t("SHEET 1 OF 1", x0, 0.7, 0.09)
    for (x1, y1, x2, y2), label in ((VP_OVERALL, "OVERALL  1\" = 40'"), (VP_DETAIL, "HOUSE DETAIL  1\" = 10'")):
        t(label, (x1 + x2) / 2, y1 - 0.35, 0.16, TextEntityAlignment.TOP_CENTER)


def control_table(doc, lay, pts):
    """Paper-space control table (what TABLE produces, drawn as lines/text)."""
    A = {"layer": "G-ANNO-TTLB", "style": "Survey"}
    cols = [("POINT", 1.4), ("NORTHING", 1.9), ("EASTING", 1.9), ("ELEV", 1.2), ("DESCRIPTION", 2.2)]
    x0, y0, rh = 1.0, 5.9, 0.32
    widths = [w for _, w in cols]
    total = sum(widths)
    rows = [[f"PMX #{n}", f"{pts[n]['n']:.3f}", f"{pts[n]['e']:.3f}", f"{pts[n]['z']:.2f}", desc(pts[n])]
            for n in CONTROL]
    lay.add_text("SURVEY CONTROL", height=0.16, dxfattribs=A).set_placement(
        (x0 + total / 2, y0 + 0.15), align=TextEntityAlignment.BOTTOM_CENTER)
    n = len(rows) + 1
    for i in range(n + 1):
        lay.add_line((x0, y0 - i * rh), (x0 + total, y0 - i * rh), dxfattribs={"layer": "G-ANNO-TTLB"})
    x = x0
    for w in [0] + widths:
        x += w
        lay.add_line((x, y0), (x, y0 - n * rh), dxfattribs={"layer": "G-ANNO-TTLB"})
    lay.add_line((x0, y0), (x0, y0 - n * rh), dxfattribs={"layer": "G-ANNO-TTLB"})
    for r, row in enumerate([[c for c, _ in cols]] + rows):
        x = x0
        for (c, w), val in zip(cols, row):
            lay.add_text(val, height=0.1, dxfattribs=A).set_placement(
                (x + w / 2, y0 - r * rh - rh / 2), align=TextEntityAlignment.MIDDLE_CENTER)
            x += w
    return rows


def build_sheet(done, pts):
    doc = ezdxf.new("R2018", setup=False)
    base.common_header(doc)
    doc.styles.new("Survey", dxfattribs={"font": "romans.shx", "width": 0.75})
    lay = doc.layouts.new("SHEET")
    lay.page_setup(size=SHEET, margins=(0, 0, 0, 0), units="inch",
                   name="ANSI_expand_D_(34.00_x_22.00_Inches)", device="DWG To PDF.pc3")
    lay.dxf_layout.dxf.current_style_sheet = "monochrome.ctb"
    doc.layouts.delete("Layout1")
    title_block(doc, lay, done)
    vpl = doc.layers.add("G-ANNO-VPRT", color=8)
    vpl.dxf.plot = 0
    rows = None
    if done:
        doc.layers.add("G-XREF", color=7)
        doc.add_xref_def(f"{BASE_NAME}_COMPLETED.dwg", BASE_NAME)
        doc.modelspace().add_blockref(BASE_NAME, (0, 0), dxfattribs={"layer": "G-XREF"})
        for (x1, y1, x2, y2), scale, center in ((VP_OVERALL, 40, L1.VIEW_CENTER), (VP_DETAIL, 10, HOUSE_CENTER)):
            w, h = x2 - x1, y2 - y1
            v = lay.add_viewport(center=((x1 + x2) / 2, (y1 + y2) / 2), size=(w, h), view_center_point=center,
                                 view_height=h * scale, dxfattribs={"layer": "G-ANNO-VPRT"})
            v.dxf.flags = v.dxf.flags | 16384
        rows = control_table(doc, lay, pts)
    else:
        tn = doc.layers.add("TRAIN-NOTES", color=10)
        tn.dxf.plot = 0
        for (x1, y1, x2, y2), label in ((VP_OVERALL, "OVERALL VIEWPORT"), (VP_DETAIL, "DETAIL VIEWPORT")):
            lay.add_lwpolyline([(x1, y1), (x2, y1), (x2, y2), (x1, y2)], close=True, dxfattribs={"layer": "TRAIN-NOTES"})
            lay.add_text(label, height=0.2, dxfattribs={"layer": "TRAIN-NOTES", "style": "Survey"}).set_placement(
                (x1 + 0.3, y2 - 0.5))
        lay.add_lwpolyline([(1.0, 2.0), (9.6, 2.0), (9.6, 6.3), (1.0, 6.3)], close=True, dxfattribs={"layer": "TRAIN-NOTES"})
        lay.add_text("CONTROL TABLE", height=0.2, dxfattribs={"layer": "TRAIN-NOTES", "style": "Survey"}).set_placement((1.3, 5.8))
    return doc, rows


def main():
    OUT.mkdir(exist_ok=True)
    pts = {p["p"]: p for p in base.read_points()}
    bs, ans = build_base(False)
    bc, _ = build_base(True)
    ss, _ = build_sheet(False, pts)
    sc, rows = build_sheet(True, pts)
    for d in (bs, bc, ss, sc):
        a = d.audit()
        assert not a.has_errors, [str(e.message) for e in a.errors]
    bs.saveas(OUT / f"{BASE_NAME}_START.dxf")
    bc.saveas(OUT / f"{BASE_NAME}_COMPLETED.dxf")
    ss.saveas(OUT / "SURVEY_CAD_L2_SHEET_START.dxf")
    sc.saveas(OUT / "SURVEY_CAD_L2_SHEET_COMPLETED.dxf")
    ans["control"] = rows
    ans["overall_scale"] = 40
    ans["detail_scale"] = 10
    (OUT / "answers.json").write_text(json.dumps(ans, indent=2, ensure_ascii=False))
    print(json.dumps(ans, indent=2, ensure_ascii=False))


if __name__ == "__main__":
    main()
