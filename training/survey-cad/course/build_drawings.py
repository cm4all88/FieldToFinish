"""Finish the Drawing course: a field-to-finish drawing the trainee drafts to a finished base.

START (what comes out of field to finish, before any drafting):
  * survey points with number / elevation / description
  * figure linework drawn from the field codes, with the real problems left in:
    jumpers, the ASPH zig-zag, wall segments lost to two bad codes, no pads or curb ties
  * every symbol placed; storm/sewer symbols not rotated yet
  * record right of way, centerline and lot lines; the boundary with a broken corner,
    a missing line, a short line and an overshoot
  * road names placed but not rotated
  * no pipes, no contours, no labels
COMPLETED: the same drawing finished - clean figures, closed boundary, rotated symbols,
  storm pipes, every label, setbacks and the sheet.

Checkpoint states 1-5 are built with build(stage) so each PDF shows exactly what the
trainee's drawing should look like after that stage.
"""
import json
import math
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
sys.path.insert(0, str(ROOT / "lib"))

import ezdxf  # noqa: E402
from ezdxf.enums import TextEntityAlignment  # noqa: E402
from ezdxf.math import Vec2  # noqa: E402

import base  # noqa: E402
import ftf  # noqa: E402
from geom import bearing, bearing_of_angle, dist, perp_foot, point_along, polygon_area  # noqa: E402

L1 = base.load_level(1)
L3 = base.load_level(3)
L5 = base.load_level(5)
L1.SHEET_SUBTITLE = "FINISH THE DRAWING"

OUT = HERE / "output"
WIN = base.WINDOW
SW, NW, NE, SE = L1.SW, L1.NW, L1.NE, L1.SE
SW_SHORT, SW_OVER = 1.5, 1.2       # FILLET at SW
NE_SHORT = 4.0                     # east line stops short of NE -> EXTEND to the north line
SE_OVER = 3.0                      # south line runs past SE -> TRIM at the east line
ROTATE_LAYERS = {"V-UTIL-STRM-SYMB-E", "V-UTIL-SSWR-SYMB-E"}
STORM_LINES = "V-UTIL-STRM-E"
ROAD = "V-ROAD-TEXT-E"
LABEL_SKIP = {"V-TOPO-CONT-TEXT-E", "V-TOPO-CONT-TEXT"}
EXTRA_FIG_LAYERS = {"TOP": "V-TOPO-SLOP-TOP-E", "TOE": "V-TOPO-SLOP-TOE-E", "RMP": "V-SURF-RAMP-E",
                    "BRK": "V-TOPO-BRKL-E", "INFOL": "V-SURF-INFO-E", "ASPH": "V-FIG-ASPH-E"}
LAYER_COLORS = {"V-SURF-CURB": 4, "V-SURF-ASPH": 8, "V-SURF-CONC": 51, "V-SURF-WALL": 3, "V-SURF-BLDG": 30,
                "V-SURF-FENC": 32, "V-SURF-STAR": 51, "V-CHAN": 2, "V-TOPO": 9, "V-SURF-GRAS": 94,
                "V-SURF-HDRL": 32, "V-SURF-RAMP": 51, "V-SURF-INFO": 9, "V-FIG": 1}
PLANT_LAYER0 = "FCK"                # a fence figure dropped on layer 0 (LAYMCH)
PLANT_OVERRIDE = "CG"               # a flowline with typed-on overrides (MATCHPROP)


# ---------------------------------------------------------------- helpers
def layer_color(name):
    for k, c in LAYER_COLORS.items():
        if name.startswith(k):
            return c
    return 7


def ensure_layer(doc, name, color=None, lt="Continuous"):
    if name not in doc.layers:
        doc.layers.add(name, color=color or layer_color(name), linetype=lt)


def in_win(pt):
    x1, y1, x2, y2 = WIN
    return x1 <= pt[0] <= x2 and y1 <= pt[1] <= y2


def curve_marks(points):
    """(point number, figure name) -> 'P' or 'T' from the raw descriptions."""
    lw, _ = ftf.load_rules()
    marks = {}
    for p in points:
        toks = p["d"].upper().split()
        for i, t in enumerate(toks):
            b, n = ftf.split_code(t)
            if b in lw:
                j = i + 1
                while j < len(toks) and toks[j] in ftf.CONTROLS:
                    if toks[j] in ("P", "T"):
                        marks[(p["p"], b + n)] = toks[j]
                    j += 1
    return marks


def circle3(a, b, c):
    ax, ay, bx, by, cx, cy = a.x, a.y, b.x, b.y, c.x, c.y
    d = 2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by))
    if abs(d) < 1e-9:
        return None
    ux = ((ax * ax + ay * ay) * (by - cy) + (bx * bx + by * by) * (cy - ay) + (cx * cx + cy * cy) * (ay - by)) / d
    uy = ((ax * ax + ay * ay) * (cx - bx) + (bx * bx + by * by) * (ax - cx) + (cx * cx + cy * cy) * (bx - ax)) / d
    return Vec2(ux, uy)


def figure_points(name, verts, marks):
    """Vertices with bulges: P ... T runs become arcs through P, the middle shot and T."""
    xy = [Vec2(v[0], v[1]) for v in verts]
    bulge = [0.0] * len(verts)
    i = 0
    while i < len(verts):
        if marks.get((verts[i][3], name)) == "P":
            j = next((k for k in range(i + 1, len(verts)) if marks.get((verts[k][3], name)) == "T"), None)
            if j and j - i >= 2:
                m = (i + j) // 2
                c = circle3(xy[i], xy[m], xy[j])
                if c:
                    ccw = (xy[m] - xy[i]).x * (xy[j] - xy[m]).y - (xy[m] - xy[i]).y * (xy[j] - xy[m]).x > 0
                    for k in range(i, j):
                        a0 = math.atan2(xy[k].y - c.y, xy[k].x - c.x)
                        a1 = math.atan2(xy[k + 1].y - c.y, xy[k + 1].x - c.x)
                        da = (a1 - a0) % (2 * math.pi) if ccw else -((a0 - a1) % (2 * math.pi))
                        bulge[k] = math.tan(da / 4)
                i = j
                continue
        i += 1
    return [(p.x, p.y, 0, 0, b) for p, b in zip(xy, bulge)]


def fig_layer(name, office):
    code = name.rstrip("0123456789")
    return office.get(code) or EXTRA_FIG_LAYERS.get(code, f"V-FIG-{code}-E")


# ---------------------------------------------------------------- build
def verts(e):
    if e.dxftype() == "LWPOLYLINE":
        return [Vec2(x) for x in e.get_points("xy")]
    if e.dxftype() == "LINE":
        return [Vec2(e.dxf.start), Vec2(e.dxf.end)]
    return []


def matches(e, pts, n=None):
    v = verts(e)
    return v and (n is None or len(v) == n) and all(any((a - q).magnitude < 0.05 for a in v) for q in pts)


def build(stage, data):
    """stage 0 = START; 1 boundary, 2 linework, 3 utilities, 4 labels, 5 sheet (= COMPLETED).
    Linework is the finished base; START puts back the field-to-finish problems."""
    pts, raw, jumpers = data["pts"], data["raw"], data["jumpers"]
    P = {p["p"]: Vec2(p["e"], p["n"]) for p in pts}
    doc, src = base.rebuild_base()
    msp = doc.modelspace()
    if stage == 0:
        doc.header["$AUNITS"] = 0
        doc.header["$LUPREC"] = 4
    pads = [[P[n] for n in pad] for pad in L3.PADS]
    ties = [[P[a], P[b]] for a, b in L3.TIES]
    fence_done = curb_done = False
    for e in list(msp):
        lay, t = e.dxf.layer, e.dxftype()
        drop = False
        if lay.startswith(("V-TOPO-CONT", "V-TINN", "V-TOPO-SURFACE")):
            drop = True                                             # surface comes in a later course
        elif lay == "V-PROP-BNDY-E":
            drop = True                                             # re-added below per stage
        elif lay == STORM_LINES and stage < 3:
            drop = True
        elif t == "MULTILEADER":
            keep = stage >= 4 or (stage >= 3 and lay == "V-UTIL-STRM-TEXT-E")
            drop = not keep
        elif t in ("MTEXT", "TEXT") and lay != ROAD:
            drop = stage < 4
        elif stage < 2 and (any(matches(e, pad, 4) for pad in pads) or any(matches(e, tie, 2) for tie in ties)):
            drop = True
        if drop:
            msp.delete_entity(e)
            continue
        if t == "MTEXT" and lay == ROAD:
            from ezdxf import bbox
            c = bbox.extents([e]).center
            r = e.get_rotation()
            e.dxf.attachment_point = 5
            e.dxf.insert = c
            e.set_rotation(r if stage >= 4 else 0.0)
        elif t == "INSERT" and lay in ROTATE_LAYERS and stage < 3:
            e.dxf.rotation = 0.0
        elif stage < 2 and t == "LWPOLYLINE" and lay == "V-SURF-FENC-CHNL-E" and len(e) > 5 and not fence_done:
            e.dxf.layer, fence_done = "0", True                     # LAYMCH
        elif stage < 2 and t == "LWPOLYLINE" and lay == "V-SURF-CURB-E" and len(e) > 10 and not curb_done:
            e.dxf.color, e.dxf.lineweight, curb_done = 1, 70, True  # MATCHPROP

    # field-to-finish problems still in START
    if stage < 2:
        for j in jumpers:
            lay = {"TBC": "V-SURF-CURB-E", "BLD": "V-SURF-BLDG-E", "EC": "V-SURF-CONC-E"}[j["figure"]]
            msp.add_lwpolyline([P[j["from"]], P[j["to"]]], dxfattribs={"layer": lay})
        zig = next(v for n, v, c in raw if n == "ASPH")
        msp.add_lwpolyline([(x[0], x[1]) for x in zig], dxfattribs={"layer": "V-SURF-ASPH-E"})

    # points
    b = doc.blocks.new("SRV-PT")
    b.add_point((0, 0))
    b.add_line((-0.4, -0.4), (0.4, 0.4))
    b.add_line((-0.4, 0.4), (0.4, -0.4))
    for tag, y in (("PNT", 0.9), ("ELEV", -0.7), ("DESC", -2.6)):
        b.add_attdef(tag, (0.9, y), dxfattribs={"height": 1.2, "style": "Survey"})
    ensure_layer(doc, "V-NODE-E", 140)
    for p in pts:
        if in_win((p["e"], p["n"])):
            ref = msp.add_blockref("SRV-PT", (p["e"], p["n"], p["z"]), dxfattribs={"layer": "V-NODE-E"})
            ref.add_auto_attribs({"PNT": str(p["p"]), "ELEV": f"{p['z']:.2f}", "DESC": p["d"]})
    if stage >= 4:
        doc.layers.get("V-NODE-E").freeze()

    # boundary
    ensure_layer(doc, "V-PROP-BNDY-E", 7)
    if stage >= 1:
        msp.add_lwpolyline([SW, NW, NE, SE], close=True, dxfattribs={"layer": "V-PROP-BNDY-E"})
    else:
        a = {"layer": "V-PROP-BNDY-E"}
        msp.add_line(point_along(SW, NW, SW_SHORT), NW, dxfattribs=a)                              # west
        msp.add_line(SE, point_along(NE, SE, NE_SHORT), dxfattribs=a)                              # east, short at NE
        msp.add_line(point_along(SW, SE, -SW_OVER), point_along(SE, SW, -SE_OVER), dxfattribs=a)   # south, long both ends

    if stage >= 4:
        # sewer structure labels (the base leaves them off; the course adds them)
        Pz = {p["p"]: p for p in pts}
        for n, txt in ((15040, "SSMH"), (15026, "SSCO")):
            ml = msp.add_multileader_mtext("SRV-20", dxfattribs={"layer": "V-UTIL-SSWR-TEXT-E"})
            ensure_layer(doc, "V-UTIL-SSWR-TEXT-E", 7)
            ml.set_content(f"{txt}\\PRIM={Pz[n]['z']:.2f}", char_height=base.TXT)
            from ezdxf.render import mleader
            ml.add_leader_line(mleader.ConnectionSide.left, [P[n]])
            ml.build(insert=P[n] + Vec2(8, 6))
        ensure_layer(doc, "V-PROP-BNDY-TEXT-E", 7)
        corners = [SW, NW, NE, SE]
        center = sum(corners, Vec2()) / 4
        for a, c in zip(corners, corners[1:] + corners[:1]):
            if (c - a).x < 0 or (abs((c - a).x) < 1e-9 and (c - a).y < 0):
                a, c = c, a
            mid, left = (a + c) / 2, (c - a).orthogonal().normalize()
            outside_left = (mid + left - center).magnitude > (mid - center).magnitude
            label = f"{bearing(a, c)} {dist(a, c):.2f}'".replace("\u00b0", "%%d")
            msp.add_text(label, height=base.TXT, rotation=(c - a).angle_deg,
                         dxfattribs={"layer": "V-PROP-BNDY-TEXT-E", "style": "Survey"}).set_placement(
                mid + (left if outside_left else -left) * 1.0,
                align=TextEntityAlignment.BOTTOM_CENTER if outside_left else TextEntityAlignment.TOP_CENTER)
        ensure_layer(doc, "V-ANNO-DIMS-E", 7)
        g, h = Vec2(1125180.09, 733877.91), Vec2(1125198.226, 733956.709)
        msp.add_aligned_dim(p1=g, p2=perp_foot(g, SW, SE), distance=4, dimstyle="SRV-20",
                            dxfattribs={"layer": "V-ANNO-DIMS-E"}).render()
        msp.add_aligned_dim(p1=h, p2=perp_foot(h, NE, SE), distance=-4, dimstyle="SRV-20",
                            dxfattribs={"layer": "V-ANNO-DIMS-E"}).render()

    # sheet and view
    tn = doc.layers.add("TRAIN-NOTES", color=10)
    tn.dxf.plot = 0
    doc.layers.add("G-ANNO-VPRT", color=8).dxf.plot = 0
    L1.build_sheet(doc, stage >= 5)
    if stage >= 5:
        tn.freeze()
    lo, hi = Vec2(1124870, 733840), Vec2(1125340, 734090)
    doc.header["$EXTMIN"] = (lo.x, lo.y, 0)
    doc.header["$EXTMAX"] = (hi.x, hi.y, 0)
    doc.set_modelspace_vport(height=(hi.y - lo.y) * 1.1, center=((lo.x + hi.x) / 2, (lo.y + hi.y) / 2))
    doc.header["$CLAYER"] = "0"
    return doc


def main():
    OUT.mkdir(exist_ok=True)
    src = base.read_source()
    pts = base.read_points()
    fixed = [dict(p, d=L3.FIXES[p["p"]][1]) if p["p"] in L3.FIXES else p for p in pts]
    raw, _ = ftf.build_figures(pts)
    ff, rep = ftf.build_figures(fixed)
    clean, dropped, jumpers = L3.cleanup(ff, L3.blank_prefixes())
    office = {k: v for k, v in L3.figure_layers().items()}
    data = {"pts": pts, "raw": raw, "jumpers": jumpers}
    import shutil
    shutil.copyfile(base.SRC_POINTS, OUT / "SURVEY_CAD_POINTS.txt")
    for stage in range(6):
        d = build(stage, data)
        a = d.audit()
        assert not a.has_errors, (stage, [str(x.message) for x in a.errors][:5])
        name = {0: "SURVEY_CAD_START.dxf", 5: "SURVEY_CAD_COMPLETED.dxf"}.get(stage, f"_state{stage}.dxf")
        d.saveas(OUT / name)

    P = {p["p"]: p for p in pts}
    S = L5.parse_leaders(src)
    in_site = sorted(n for n in S if in_win((P[n]["e"], P[n]["n"])))
    corners = [("SW", SW), ("NW", NW), ("NE", NE), ("SE", SE)]
    ans = {
        "deed": [{"from": a, "to": c, "bearing": bearing(pa, pc), "dist": round(dist(pa, pc), 2)}
                 for (a, pa), (c, pc) in zip(corners, corners[1:] + corners[:1])],
        "area_sf": round(polygon_area([SW, NW, NE, SE]), 2),
        "perimeter": round(sum(dist(pa, pc) for (_, pa), (_, pc) in zip(corners, corners[1:] + corners[:1])), 2),
        "defects": {"sw_short": SW_SHORT, "sw_over": SW_OVER, "ne_short": NE_SHORT, "se_over": SE_OVER},
        "fixes": {str(k): v for k, v in L3.FIXES.items()}, "jumpers": jumpers, "dropped": dropped,
        "pads": L3.PADS, "ties": L3.TIES, "single": rep["single_point_figures"],
        "structures": [{"no": n, "text": S[n]["text"], "rim_pt": round(P[n]["z"], 2)} for n in in_site],
        "ssmh": [{"p": n, "d": P[n]["d"], "z": round(P[n]["z"], 2)} for n in (15040, 15026) if n in P],
        "control": [{"p": n, "d": P[n]["d"].split(" AKA")[0]} for n in (1009, 2000, 2001, 2002, 2003, 2500)],
        "cb_rot": {"10144": bearing_of_angle(-5.611851331204784)},
        "dim_garage": round(dist(Vec2(1125180.09, 733877.91), perp_foot((1125180.09, 733877.91), SW, SE)), 2),
        "dim_house": round(dist(Vec2(1125198.226, 733956.709), perp_foot((1125198.226, 733956.709), NE, SE)), 2),
        "ffe": {str(n): round(P[n]["z"], 2) for n in (10220, 10448, 10436)},
    }
    trees = []
    for e in src.modelspace().query("MULTILEADER"):
        if e.dxf.layer == "V-SURF-VEGE-TEXT-E" and base.in_window(e):
            a = Vec2(e.context.leaders[0].lines[0].vertices[0])
            near = min((p for p in pts if p["d"].split()[0] in ("CON", "DEC", "MAP", "INFO")),
                       key=lambda p: (Vec2(p["e"], p["n"]) - a).magnitude)
            trees.append({"p": near["p"], "desc": near["d"], "label": e.context.mtext.default_content})
    ans["trees"] = sorted(trees, key=lambda t: t["p"])
    labels = {"control": [], "monuments": [], "surface": [], "ffe": [], "road": []}
    for e in src.modelspace():
        if not base.in_window(e):
            continue
        lay = e.dxf.layer
        if e.dxftype() == "MULTILEADER" and e.context.mtext:
            txt = e.context.mtext.default_content.replace("\\P", " / ")
            if lay == "V-CTRL-PMX_-TEXT-E":
                labels["control"].append(txt)
            elif lay == "V-CTRL-MONU-TEXT-E":
                labels["monuments"].append(txt)
            elif lay not in ("V-SURF-VEGE-TEXT-E", "V-UTIL-STRM-TEXT-E"):
                labels["surface"].append(txt)
        elif e.dxftype() == "MTEXT":
            txt = e.plain_text().replace("\n", " / ")
            key = {"V-ROAD-TEXT-E": "road", "V-TOPO-TEXT-E": "ffe"}.get(lay, "surface")
            if not lay.startswith("V-TOPO-CONT"):
                labels[key].append(txt)
    ans["labels"] = {k: sorted(v) for k, v in labels.items()}
    (OUT / "answers.json").write_text(json.dumps(ans, indent=2, ensure_ascii=False))
    print(len(ans["structures"]), "structures;", len(trees), "tree labels;", jumpers)


if __name__ == "__main__":
    main()
