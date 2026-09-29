"""Level 4 - Civil 3D surfaces.

START:     the surface border polyline (and the Level 3 drawing the trainee made).
COMPLETED: the finished base's real surface products - 1' contours, contour
           labels, TIN breaklines and the surface border - plus FFE labels.
answers.json: point-group counts, what the boundary removes, elevation range,
           the INFO stump shot, FFE values.
"""
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
sys.path.insert(0, str(ROOT / "lib"))

import ezdxf  # noqa: E402
from matplotlib.path import Path as MPath  # noqa: E402

import base  # noqa: E402

L3 = base.load_level(3)
OUT = HERE / "output"
BORDER = "V-TOPO-SURFACE-BORDER-E"
SURFACE_LAYERS = ("V-TOPO-CONT-MAJR-E", "V-TOPO-CONT-MINR-E", "V-TOPO-CONT-TEXT-E", "V-TINN-BRKL-E", BORDER)
FFE = {10220: "FFE=65.39", 10448: "FFE=65.45", 10436: "FFE=64.62"}   # BLFF shots labeled in the base


def copy_layers(layers, with_record=True):
    src = base.read_source()
    doc = ezdxf.new("R2018", setup=False)
    base.common_header(doc)
    base.setup_styles(doc)
    msp = doc.modelspace()
    for e in src.modelspace():
        keep = e.dxf.layer in layers or (with_record and e.dxf.layer.startswith(L3.RECORD_LAYERS))
        if keep and e.dxftype() in ("LINE", "ARC", "LWPOLYLINE", "POLYLINE", "MTEXT"):
            base.rebuild_entity(e, msp, src, doc)
    for n in layers:
        if n in doc.layers:
            doc.layers.get(n).on()
            doc.layers.get(n).thaw()
    return doc


def border_path():
    src = base.read_source()
    b = [e for e in src.modelspace() if e.dxf.layer == BORDER][0]
    return MPath([(v.dxf.location.x, v.dxf.location.y) for v in b.vertices])


def main():
    OUT.mkdir(exist_ok=True)
    start = copy_layers({BORDER}, with_record=False)
    start.saveas(OUT / "SURVEY_CAD_L4_BORDER.dxf")
    done = copy_layers(set(SURFACE_LAYERS))
    P = {p["p"]: p for p in base.read_points()}
    if "V-TOPO-TEXT-E" not in done.layers:
        done.layers.add("V-TOPO-TEXT-E", color=3)
    for n, label in FFE.items():
        done.modelspace().add_mtext(label, dxfattribs={
            "layer": "V-TOPO-TEXT-E", "style": "Survey", "char_height": base.TXT,
            "insert": (P[n]["e"] + 1.0, P[n]["n"] + 0.8), "attachment_point": 7})
    assert not done.audit().has_errors
    done.saveas(OUT / "SURVEY_CAD_L4_COMPLETED.dxf")

    pts = base.read_points()
    ground = [p for p in pts if L3.first(p) not in L3.NO_SURFACE]
    path = border_path()
    inside = [p for p in ground if path.contains_point((p["e"], p["n"]))]
    cont = [e for e in done.modelspace().query("LWPOLYLINE") if e.dxf.layer.startswith("V-TOPO-CONT")]
    elev = sorted({round(e.dxf.elevation, 2) for e in cont})
    ans = {
        "ground": len(ground), "inside": len(inside), "outside": len(ground) - len(inside),
        "z_min": round(min(p["z"] for p in inside), 2), "z_max": round(max(p["z"] for p in inside), 2),
        "contour_min": elev[0], "contour_max": elev[-1], "contours": len(cont),
        "majors": sum(1 for e in cont if e.dxf.layer.endswith("MAJR-E")),
        "breaklines": sum(1 for e in done.modelspace() if e.dxf.layer == "V-TINN-BRKL-E"),
        "stump": {"p": 10421, "z": P[10421]["z"], "note": P[10421]["note"]},
        "far": [n for n in (1001, 1002) if n in P],
        "ffe": {str(n): [round(P[n]["z"], 2), P[n]["d"]] for n in FFE},
        "no_surface": sorted(L3.NO_SURFACE),
    }
    (OUT / "answers.json").write_text(json.dumps(ans, indent=2))
    print(json.dumps(ans, indent=2))


if __name__ == "__main__":
    main()
