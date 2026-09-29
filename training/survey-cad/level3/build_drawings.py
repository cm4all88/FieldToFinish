"""Level 3 - Civil 3D field to finish.

START:  the crew's point file exactly as delivered (junk row and all), plus the
        record boundary/right-of-way linework to xref.
COMPLETED: point blocks and the figures the office code rules produce after
        the bad code is fixed, on the figure layers from config/rules.json.
answers.json: point-group counts, figures by prefix, the problems to find.
"""
import json
import shutil
import sys
from collections import Counter
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
sys.path.insert(0, str(ROOT / "lib"))

import ezdxf  # noqa: E402

import base  # noqa: E402
import ftf  # noqa: E402

OUT = HERE / "output"

# Point groups by the first code in the raw description.
GROUPS = {
    "CONTROL": {"XMAG", "XHT", "XNL"},
    "MONUMENTS": {"FMON", "FMIC", "FMAG", "FIP"},
    "UTILITIES": {"CB", "CBS", "SSMH", "SDMH", "SDAD", "SSCO", "WVL", "GVL", "WFH", "MW", "PJB",
                  "PP", "PPU", "PPX", "LT", "SN", "SNNP", "UCO", "IDWPP"},
    "TREES": {"CON", "DEC", "MAP"},
}
# Codes kept out of the ground surface: notes, trees, above/below-grade items.
NO_SURFACE = {"INFO", "INFOL", "ZK", "BLFF", "POST", "MB", "FIP", "FMON", "FMIC", "FMAG",
              "PP", "PPU", "PPX", "LT", "SN", "SNNP", "MW", "WFH", "PJB", "IDWPP"} | GROUPS["TREES"]
FIXES = {
    10180: ("RWB1 B RW2 B", "RWB1 RWB2 B"),   # stray B restarts RWB1; RW2 is a typo for RWB2
    15235: ("RWC E BLD", "RWC1 E BLD"),       # ends the RWC1 wall started at 15062, not RWC
}
RECORD_LAYERS = ("V-PROP-", "V-ALGN-")
# Drafted by hand in the finished base (not linework codes): CONC pads (4 shots each)
# and the short curb-end ties from flowline (CG) to back of curb (TBC).
PADS = [(10249, 10250, 10251, 10252), (10262, 10263, 10264, 10265), (10275, 10276, 10277, 10278)]
TIES = [(10053, 10050), (10051, 10048), (10046, 10047), (10044, 10042),
        (10071, 10067), (10064, 10062), (10058, 10059), (10073, 10069)]


OFFICE_PTS = 65000          # office-added points; they join the last open string out of sequence
JUMP = 30.0


def cleanup(figs, layers_blank):
    """Drop figures whose prefix draws nothing (blank layer in the figure prefix
    database) and split jumpers: a segment to an office-added point > JUMP feet."""
    import math
    out, dropped, jumpers = [], [], []
    for name, v, closed in figs:
        if name.rstrip("0123456789") in layers_blank:
            dropped.append(name)
            continue
        cur = [v[0]]
        for a, b in zip(v, v[1:]):
            d = math.hypot(a[0] - b[0], a[1] - b[1])
            if d > JUMP and (a[3] >= OFFICE_PTS or b[3] >= OFFICE_PTS):
                jumpers.append({"figure": name, "from": a[3], "to": b[3], "length": round(d, 1)})
                if len(cur) >= 2:
                    out.append((name, cur, False))
                cur = [b]
            else:
                cur.append(b)
        if len(cur) >= 2:
            out.append((name, cur, closed and len(cur) == len(v)))
    return out, dropped, jumpers


def blank_prefixes():
    d = json.loads(ftf.RULES.read_text(encoding="utf-8"))
    return {f["code"] for f in d["lineFeatures"] if not f["layer"]}


def first(p):
    return p["d"].split()[0].rstrip("0123456789") if p["d"].split() else ""


def figure_layers():
    d = json.loads(ftf.RULES.read_text(encoding="utf-8"))
    return {f["code"]: f["layer"] for f in d["lineFeatures"] if f["layer"]}


def fig_layer(name, layers):
    code = name.rstrip("0123456789")
    return layers.get(code, f"V-FIG-{code}-E")


def build_record():
    src = base.read_source()
    doc = ezdxf.new("R2018", setup=False)
    base.common_header(doc)
    base.setup_styles(doc)
    for e in src.modelspace():
        if e.dxf.layer.startswith(RECORD_LAYERS) and e.dxftype() in ("LINE", "ARC", "LWPOLYLINE"):
            base.rebuild_entity(e, doc.modelspace(), src, doc)
    return doc


def build_completed(pts, figs):
    doc = build_record()
    msp = doc.modelspace()
    b = doc.blocks.new("SRV-PT")
    b.add_point((0, 0))
    b.add_line((-0.4, -0.4), (0.4, 0.4))
    b.add_line((-0.4, 0.4), (0.4, -0.4))
    for tag, y in (("PNT", 0.9), ("ELEV", -0.7), ("DESC", -2.6)):
        b.add_attdef(tag, (0.9, y), dxfattribs={"height": 1.2, "style": "Survey"})
    doc.layers.add("V-NODE-E", color=140)
    for p in pts:
        ref = msp.add_blockref("SRV-PT", (p["e"], p["n"], p["z"]), dxfattribs={"layer": "V-NODE-E"})
        ref.add_auto_attribs({"PNT": str(p["p"]), "ELEV": f"{p['z']:.2f}", "DESC": p["d"]})
    layers = figure_layers()
    for name, verts, closed in figs:
        lay = fig_layer(name, layers)
        if lay not in doc.layers:
            doc.layers.add(lay, color=3)
        pl = msp.add_polyline3d([(e, n, z) for e, n, z, _ in verts], dxfattribs={"layer": lay})
        if closed:
            pl.close(True)
    P = {p["p"]: p for p in pts}
    for lay in ("V-SURF-CONC-E", "V-SURF-CURB-E"):
        if lay not in doc.layers:
            doc.layers.add(lay, color=2)
    for pad in PADS:
        msp.add_lwpolyline([(P[n]["e"], P[n]["n"]) for n in pad], close=True, dxfattribs={"layer": "V-SURF-CONC-E"})
    for a, b2 in TIES:
        msp.add_line((P[a]["e"], P[a]["n"]), (P[b2]["e"], P[b2]["n"]), dxfattribs={"layer": "V-SURF-CURB-E"})
    return doc


def main():
    OUT.mkdir(exist_ok=True)
    raw_lines = base.SRC_POINTS.read_bytes().decode("cp1252").splitlines()
    junk = [i + 1 for i, l in enumerate(raw_lines) if l.split(",")[0].strip() == ""]
    pts = base.read_points()
    fixed = [dict(p, d=FIXES[p["p"]][1]) if p["p"] in FIXES else p for p in pts]
    figs_raw, rep_raw = ftf.build_figures(pts)
    figs_proc, rep = ftf.build_figures(fixed)
    figs, dropped, jumpers = cleanup(figs_proc, blank_prefixes())

    shutil.copyfile(base.SRC_POINTS, OUT / "SURVEY_CAD_L3_POINTS.txt")
    rec = build_record()
    rec.saveas(OUT / "SURVEY_CAD_L3_RECORD.dxf")
    done = build_completed(fixed, figs)
    assert not done.audit().has_errors
    done.saveas(OUT / "SURVEY_CAD_L3_COMPLETED.dxf")

    groups = {g: sum(1 for p in pts if first(p) in codes) for g, codes in GROUPS.items()}
    groups["SURFACE"] = sum(1 for p in pts if first(p) not in NO_SURFACE)
    ans = {
        "file_lines": len(raw_lines), "junk_lines": junk, "points": len(pts),
        "pt_min": min(p["p"] for p in pts), "pt_max": max(p["p"] for p in pts),
        "z_min": min(p["z"] for p in pts), "z_max": max(p["z"] for p in pts),
        "groups": groups,
        "figures_raw": len(figs_raw), "figures_fixed": len(figs_proc), "figures": len(figs),
        "dropped": dropped, "jumpers": jumpers,
        "by_prefix": dict(Counter(n.rstrip("0123456789") for n, _, _ in figs).most_common()),
        "single_raw": rep_raw["single_point_figures"], "single": rep["single_point_figures"],
        "bad_codes": sorted(k for k in rep_raw["unknown_codes"] if k in ("RW",)),
        "fix": {str(k): v for k, v in FIXES.items()},
        "curve_points": rep["curve_points"],
        "odd_chars": [p["p"] for p in pts if any(ord(c) > 126 for c in p["d"] + p["note"])],
        "aka": [p["p"] for p in pts if "AKA" in p["d"]],
        "pads": PADS, "ties": TIES,
    }
    (OUT / "answers.json").write_text(json.dumps(ans, indent=2))
    print(json.dumps(ans, indent=2))


if __name__ == "__main__":
    main()
