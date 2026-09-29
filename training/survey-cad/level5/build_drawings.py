"""Level 5 - Civil 3D utilities and finishing.

Parses the storm structure leaders in the finished base (rim, inverts, bottom),
builds the Soundview 18" storm trunk as a structure/pipe table (lengths and
slopes from measured inverts), and measures the road centerlines for the
alignment exercises.

START:     the storm corridor record (structures as points/symbols, no labels)
           and the centerline linework.
COMPLETED: the finished base's storm linework, symbols and leaders plus centerlines.
"""
import json
import math
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
sys.path.insert(0, str(ROOT / "lib"))

import ezdxf  # noqa: E402
from ezdxf.math import Vec2  # noqa: E402

import base  # noqa: E402

OUT = HERE / "output"
CORRIDOR = (1124850.0, 733100.0, 1125450.0, 734800.0)
TRUNK = [10152, 10019, 10018, 10017, 10016, 10014, 10013, 10011, 10006, 10005, 10004]   # south -> north
STORM_LAYERS = ("V-UTIL-STRM-E", "V-UTIL-STRM-SYMB-E")
LABEL_LAYER = "V-UTIL-STRM-TEXT-E"
KEEP = ("V-PROP-RWAY-E", "V-ALGN-CNTR-E", "V-SURF-CURB-E")


def parse_leaders(src):
    out = {}
    for e in src.modelspace().query("MULTILEADER"):
        if e.dxf.layer != LABEL_LAYER or not e.context.mtext:
            continue
        lines = e.context.mtext.default_content.split("\\P")
        m = re.match(r"(CB|SDMH)\s*#(\d+)", lines[0])
        if not m:
            continue
        s = {"type": m.group(1), "name": lines[0].strip(), "ie": [], "text": e.context.mtext.default_content}
        for ln in lines[1:]:
            if ln.startswith("RIM"):
                s["rim"] = ln.split("=")[1]
            elif ln.startswith("BOTTOM"):
                s["bottom"] = float(ln.split("=")[1])
            elif ln.startswith("IE"):
                mm = re.match(r'IE\s+(\d+)"\s+(\w+)\s+\((\w+)\)=([\d.]+)', ln)
                if mm:
                    s["ie"].append({"size": int(mm.group(1)), "mat": mm.group(2), "dir": mm.group(3),
                                    "z": float(mm.group(4))})
        out[int(m.group(2))] = s
    return out


def trunk_table(S, P):
    """Pipes south -> north. Out = the structure's (N) invert (lowest if the
    leader repeats a direction); in = the next structure's (S) invert."""
    findings = []
    pipes = []

    def ie(n, d):
        vals = [x for x in S[n]["ie"] if x["dir"] == d and x["size"] >= 18]
        return vals

    for a, b in zip(TRUNK, TRUNK[1:]):
        out = ie(a, "N")
        if not out:
            ss = ie(a, "S")
            if len(ss) == 2:        # direction written twice: the lower one is the outlet
                out = [min(ss, key=lambda x: x["z"])]
                findings.append(f"{S[a]['name']}: both 18\" inverts are labeled (S); the lower "
                                f"({out[0]['z']:.2f}) is the outlet and should read (N).")
        inn = ie(b, "S")
        pa, pb = Vec2(P[a]["e"], P[a]["n"]), Vec2(P[b]["e"], P[b]["n"])
        L = (pb - pa).magnitude
        zo, zi = out[0]["z"], (max(inn, key=lambda x: x["z"])["z"] if inn else None)
        pipes.append({"from": S[a]["name"], "to": S[b]["name"], "size": out[0]["size"], "mat": out[0]["mat"],
                      "length": round(L, 2), "ie_out": zo, "ie_in": zi,
                      "slope_pct": round((zo - zi) / L * 100, 2) if zi is not None else None})
    return pipes, findings


def chains(lines):
    """Group centerline pieces into connected runs; return total length per run."""
    segs = []
    for e in lines:
        if e.dxftype() == "LINE":
            segs.append((Vec2(e.dxf.start), Vec2(e.dxf.end), e.dxf.start.distance(e.dxf.end), None))
        elif e.dxftype() == "ARC":
            ang = (e.dxf.end_angle - e.dxf.start_angle) % 360
            segs.append((Vec2(e.start_point), Vec2(e.end_point), math.radians(ang) * e.dxf.radius, e.dxf.radius))
        elif e.dxftype() == "LWPOLYLINE":
            p = [Vec2(x) for x in e.get_points("xy")]
            segs.append((p[0], p[-1], sum((b - a).magnitude for a, b in zip(p, p[1:])), None))
    groups = []
    for s in segs:
        hit = [g for g in groups if any(near(s[0], t[0]) or near(s[0], t[1]) or near(s[1], t[0]) or near(s[1], t[1]) for t in g)]
        merged = [s]
        for g in hit:
            merged += g
            groups.remove(g)
        groups.append(merged)
    return [{"length": round(sum(x[2] for x in g), 2), "pieces": len(g),
             "radii": sorted({round(x[3], 2) for x in g if x[3]}),
             "south": min((min(x[0].y, x[1].y) for x in g))} for g in groups]


def near(a, b, tol=0.05):
    return (a - b).magnitude < tol


def build(done, src):
    doc = ezdxf.new("R2018", setup=False)
    base.common_header(doc)
    base.setup_styles(doc)
    base.make_mleader_style(doc)
    msp = doc.modelspace()
    for e in src.modelspace():
        lay = e.dxf.layer
        if not base.in_window(e, CORRIDOR):
            continue
        if e.dxftype() == "MULTILEADER" and lay == LABEL_LAYER:
            if done:
                base.rebuild_mleader(e, msp, src, doc)
        elif e.dxftype() == "INSERT" and e.dxf.name.startswith("*") and lay in STORM_LAYERS:
            for v in e.virtual_entities():
                n = base.rebuild_entity(v, msp, src, doc, layer=lay if v.dxf.layer == "0" else v.dxf.layer)
                if n is not None and n.dxftype() == "INSERT":
                    base.fix_symbol_scale(n)
        elif lay in STORM_LAYERS + KEEP and e.dxftype() in ("LINE", "ARC", "LWPOLYLINE"):
            if lay == "V-UTIL-STRM-E" and not done:
                continue                      # START: structures only, the trainee builds the pipes
            base.rebuild_entity(e, msp, src, doc)
    return doc


def main():
    OUT.mkdir(exist_ok=True)
    src = base.read_source()
    P = {p["p"]: p for p in base.read_points()}
    S = parse_leaders(src)
    pipes, findings = trunk_table(S, P)
    cl = chains([e for e in src.modelspace() if e.dxf.layer == "V-ALGN-CNTR-E"])
    start, done = build(False, src), build(True, src)
    for d in (start, done):
        assert not d.audit().has_errors
    start.saveas(OUT / "SURVEY_CAD_L5_START.dxf")
    done.saveas(OUT / "SURVEY_CAD_L5_COMPLETED.dxf")
    structs = [{"no": n, "name": S[n]["name"], "rim": S[n]["rim"], "bottom": S[n].get("bottom"),
                "point_z": round(P[n]["z"], 2), "n": P[n]["n"], "e": P[n]["e"],
                "ie": [f"{x['size']}\" {x['mat']} ({x['dir']}) {x['z']:.2f}" for x in S[n]["ie"]]} for n in TRUNK]
    ans = {"structures": structs, "pipes": pipes, "findings": findings,
           "trunk_length": round(sum(p["length"] for p in pipes), 2),
           "centerlines": sorted(cl, key=lambda c: -c["length"]),
           "outfall": {"p": 10003, "z": P[10003]["z"], "note": P[10003]["note"]},
           "all_labeled": sorted(S)}
    (OUT / "answers.json").write_text(json.dumps(ans, indent=2))
    print(json.dumps({k: v for k, v in ans.items() if k != "structures"}, indent=2))


if __name__ == "__main__":
    main()
