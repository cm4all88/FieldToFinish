"""Field to Finish course: one drawing from the raw point file to the finished base.

Trainee package:
  SURVEY_CAD_START.dxf       bare drawing: empty model, sheet layout with title block
  SURVEY_CAD_POINTS.txt      the crew's point file exactly as delivered
  SURVEY_CAD_RECORD.dxf      record right of way, centerline and surface border (from the plat)
  CHECKPOINT_1..6.pdf        what the drawing should look like after each stage
  SURVEY_CAD_COMPLETED.dxf   the finished base (answer key)

The checkpoint states are built from the same pieces the level scripts tested:
Level 3's field-to-finish processing, Level 4's surface layers and Level 1's
finished base and sheet.
"""
import json
import shutil
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
sys.path.insert(0, str(ROOT / "lib"))

import ezdxf  # noqa: E402
from ezdxf.math import Vec2  # noqa: E402

import base  # noqa: E402
import ftf  # noqa: E402
from geom import bearing, dist, polygon_area  # noqa: E402

L1 = base.load_level(1)
L3 = base.load_level(3)
L4 = base.load_level(4)
L5 = base.load_level(5)
L1.SHEET_SUBTITLE = "FIELD TO FINISH"

OUT = HERE / "output"
CORNERS = [("SW", L1.SW), ("NW", L1.NW), ("NE", L1.NE), ("SE", L1.SE)]
MONUMENTS = {10153: "NW", 10494: "NE", 15000: "SE"}
CONTOURS = ("V-TOPO-CONT-MAJR-E", "V-TOPO-CONT-MINR-E", "V-TOPO-CONT-TEXT-E")


def deed_calls():
    out = []
    for (a, pa), (b, pb) in zip(CORNERS, CORNERS[1:] + CORNERS[:1]):
        out.append({"from": a, "to": b, "bearing": bearing(pa, pb), "dist": round(dist(pa, pb), 2)})
    return out


def add_boundary(doc):
    if "V-PROP-BNDY-E" not in doc.layers:
        doc.layers.add("V-PROP-BNDY-E", color=7).dxf.lineweight = 50
    doc.modelspace().add_lwpolyline([p for _, p in CORNERS], close=True, dxfattribs={"layer": "V-PROP-BNDY-E"})


def add_layers_from_base(doc, layers):
    src = base.read_source()
    for e in src.modelspace():
        if e.dxf.layer in layers and e.dxftype() in ("LWPOLYLINE", "MTEXT", "POLYLINE", "LINE"):
            base.rebuild_entity(e, doc.modelspace(), src, doc)
    for n in layers:
        if n in doc.layers:
            doc.layers.get(n).on()
            doc.layers.get(n).thaw()


def drop_hand_drafting(doc):
    """Stage 2 state: figures only - no pads or ties yet."""
    msp = doc.modelspace()
    for e in list(msp):
        if (e.dxftype() == "LWPOLYLINE" and e.dxf.layer == "V-SURF-CONC-E" and e.closed) or \
           (e.dxftype() == "LINE" and e.dxf.layer == "V-SURF-CURB-E"):
            msp.delete_entity(e)


def build_start():
    doc = ezdxf.new("R2018", setup=False)
    base.common_header(doc)
    base.setup_styles(doc)
    doc.header["$AUNITS"] = 0          # Stage 1 sets surveyor's units
    doc.header["$LUPREC"] = 4
    L1.build_sheet(doc, False)
    return doc


def main():
    OUT.mkdir(exist_ok=True)
    pts = base.read_points()
    fixed = [dict(p, d=L3.FIXES[p["p"]][1]) if p["p"] in L3.FIXES else p for p in pts]
    figs_raw, _ = ftf.build_figures(pts)
    figs_fixed, rep = ftf.build_figures(fixed)
    figs_clean, dropped, jumpers = L3.cleanup(figs_fixed, L3.blank_prefixes())
    P = {p["p"]: p for p in pts}

    # --- trainee files
    build_start().saveas(OUT / "SURVEY_CAD_START.dxf")
    shutil.copyfile(base.SRC_POINTS, OUT / "SURVEY_CAD_POINTS.txt")
    L4.copy_layers({L4.BORDER}, with_record=True).saveas(OUT / "SURVEY_CAD_RECORD.dxf")

    # --- checkpoint states
    states = {}
    c1 = L4.copy_layers({L4.BORDER}, with_record=True)
    add_boundary(c1)
    states[1] = c1
    c2 = L3.build_completed(fixed, figs_fixed)
    drop_hand_drafting(c2)
    add_boundary(c2)
    states[2] = c2
    c3 = L3.build_completed(fixed, figs_clean)
    add_boundary(c3)
    states[3] = c3
    c4 = L3.build_completed(fixed, figs_clean)
    add_boundary(c4)
    add_layers_from_base(c4, set(CONTOURS) | {L4.BORDER})
    states[4] = c4
    final, l1ans = L1.build(True)
    states[5] = final
    for n, d in states.items():
        assert not d.audit().has_errors, n
        d.saveas(OUT / f"_state{n}.dxf")
    final.saveas(OUT / "SURVEY_CAD_COMPLETED.dxf")

    ground = [p for p in pts if L3.first(p) not in L3.NO_SURFACE]
    path = L4.border_path()
    inside = [p for p in ground if path.contains_point((p["e"], p["n"]))]
    S = L5.parse_leaders(base.read_source())
    ans = {
        "deed": deed_calls(),
        "pob": [round(L1.SW.x, 2), round(L1.SW.y, 2)],
        "area_sf": round(polygon_area([p for _, p in CORNERS]), 2),
        "perimeter": round(sum(dist(pa, pb) for (_, pa), (_, pb) in zip(CORNERS, CORNERS[1:] + CORNERS[:1])), 2),
        "monuments": {str(n): {"corner": c, "desc": P[n]["d"],
                               "off": round(dist(Vec2(P[n]["e"], P[n]["n"]), dict(CORNERS)[c]), 2)}
                      for n, c in MONUMENTS.items()},
        "junk_lines": [3], "points": len(pts),
        "groups": {g: sum(1 for p in pts if L3.first(p) in codes) for g, codes in L3.GROUPS.items()},
        "fixes": {str(k): v for k, v in L3.FIXES.items()},
        "figures_raw": len(figs_raw), "figures_fixed": len(figs_fixed), "figures": len(figs_clean),
        "single": rep["single_point_figures"], "dropped": dropped, "jumpers": jumpers,
        "pads": L3.PADS, "ties": L3.TIES,
        "ground": len(ground), "inside": len(inside),
        "z_min": round(min(p["z"] for p in inside), 2), "z_max": round(max(p["z"] for p in inside), 2),
        "no_surface": sorted(L3.NO_SURFACE),
        "ffe": {str(n): round(P[n]["z"], 2) for n in L4.FFE},
        "leaders": {str(n): S[n]["text"] for n in (10012, 10013, 10144) if n in S},
        "l1": l1ans,
    }
    (OUT / "answers.json").write_text(json.dumps(ans, indent=2, ensure_ascii=False))
    print(json.dumps({k: v for k, v in ans.items() if k not in ("l1", "no_surface", "leaders")}, indent=1, ensure_ascii=False)[:2500])


if __name__ == "__main__":
    main()
