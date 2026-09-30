"""Level 1 - AutoCAD basics for survey drafting, on the real training site.

COMPLETED is the rebuilt survey base plus every exercise's result.
START is the same base with each exercise's target undone: a boundary line
deleted, a corner broken, a symbol rotated, a label removed, and so on.
Every COMPLETED result is what the AutoCAD command produces (FILLET R0 = line
intersection, EXTEND to the chosen boundary, etc.), so the key matches.
"""
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "lib"))

import ezdxf  # noqa: E402
from ezdxf.enums import TextEntityAlignment  # noqa: E402
from ezdxf.math import Vec2  # noqa: E402

import base  # noqa: E402
from geom import (bearing, bearing_of_angle, dist, line_intersection, perp_foot,  # noqa: E402
                  point_along, polygon_area, ray_hits_polyline, typed)

OUT = HERE / "output"
TXT = base.TXT
TOL = 0.02

# boundary corners (record geometry from the base)
SW = Vec2(1124980.690, 733878.871)
NW = Vec2(1124986.155, 734003.767)
NE = Vec2(1125235.196, 733996.875)
SE = Vec2(1125267.220, 733870.941)
CORNER_SHORT = 1.5        # Ex 06: west line starts 1.5' short of SW
CORNER_OVER = 1.2         # Ex 06: south line runs 1.2' past SW
CB_ROT_ERR = 35.0         # Ex 14
TRIM_OVER = 3.0           # Ex 11: walk runs 3' into the house
EXT_SHORT = 4.0           # Ex 12: driveway edge stops 4' short of the curb

VIEW_CENTER = Vec2(1125135.0, 733952.0)
SHEET = (34.0, 22.0)
VP_BOX = (0.75, 0.75, 30.25, 21.25)

SHEET_SUBTITLE = "LEVEL 1 - AUTOCAD BASICS"
CONTOUR_LAYERS = ["V-TOPO-CONT-MAJR-E", "V-TOPO-CONT-MINR-E", "V-TOPO-CONT-TEXT-E", "V-TOPO-CONT-TEXT"]


# ---------------------------------------------------------------- finders
def pts(e):
    return [Vec2(p) for p in e.get_points("xy")]


def near(a, b, tol=TOL):
    return (Vec2(a) - Vec2(b)).magnitude < tol


def find_line(msp, layer, a, b):
    for e in msp.query(f'LINE[layer=="{layer}"]'):
        s, t = Vec2(e.dxf.start), Vec2(e.dxf.end)
        if (near(s, a) and near(t, b)) or (near(s, b) and near(t, a)):
            return e
    raise LookupError(f"line {layer} {a} {b}")


def find_pline(msp, layer, first, nverts=None, tol=0.1):
    for e in msp.query(f'LWPOLYLINE[layer=="{layer}"]'):
        p = pts(e)
        if (nverts is None or len(p) == nverts) and (near(p[0], first, tol) or near(p[-1], first, tol)):
            return e
    raise LookupError(f"pline {layer} {first}")


def find_insert(msp, name, at):
    for e in msp.query(f'INSERT[name=="{name}"]'):
        if near(e.dxf.insert, at, 0.01):
            return e
    raise LookupError(f"insert {name} {at}")


def find_mtext(msp, text):
    for e in msp.query("MTEXT"):
        if e.plain_text() == text:
            return e
    raise LookupError(text)


def find_mleader(msp, starts):
    for e in msp.query("MULTILEADER"):
        if e.context.mtext and e.context.mtext.default_content.startswith(starts):
            return e
    raise LookupError(starts)


def on_polyline(msp, pt, exclude_layer="V-SURF-ASPH-E"):
    """Vertices of the polyline whose segment passes through pt (the EXTEND boundary)."""
    pt = Vec2(pt)
    for e in msp.query("LWPOLYLINE"):
        if e.dxf.layer.startswith(("V-TOPO", exclude_layer)):
            continue
        p = pts(e)
        for a, b in zip(p, p[1:]):
            if dist(perp_foot(pt, a, b), pt) < TOL and (pt - a).dot(b - a) >= 0 and (pt - b).dot(a - b) >= 0:
                return p
    raise LookupError(pt)


def set_pline_end(e, which, new_pt):
    p = list(e.get_points("xyseb"))
    i = 0 if which == "first" else -1
    x, y, *rest = p[i]
    p[i] = (new_pt.x, new_pt.y, *rest)
    e.set_points(p, format="xyseb")


# ---------------------------------------------------------------- build
def build(done):
    doc, _src = base.rebuild_base()
    msp = doc.modelspace()
    ans = {}

    # --- Ex 06/07/08 boundary
    west = find_line(msp, "V-PROP-BNDY-E", SW, NW)
    south = find_line(msp, "V-PROP-BNDY-E", SW, SE)
    north = find_line(msp, "V-PROP-BNDY-E", NW, NE)
    east = find_line(msp, "V-PROP-BNDY-E", NE, SE)
    if done:
        for e in (west, south, north, east):
            msp.delete_entity(e)
        msp.add_lwpolyline([SW, NW, NE, SE], close=True, dxfattribs={"layer": "V-PROP-BNDY-E"})
    else:
        msp.delete_entity(north)
        west.dxf.start, west.dxf.end = point_along(SW, NW, CORNER_SHORT), NW
        south.dxf.start, south.dxf.end = point_along(SW, SE, -CORNER_OVER), SE
    assert near(line_intersection(point_along(SW, NW, CORNER_SHORT), NW,
                                  point_along(SW, SE, -CORNER_OVER), SE), SW, 1e-6)
    ans["north_bearing"] = bearing(NW, NE)
    ans["north_dist"] = round(dist(NW, NE), 2)
    ans["east_bearing"] = bearing(NE, SE)
    ans["east_dist"] = round(dist(NE, SE), 2)
    ans["area_sf"] = round(polygon_area([SW, NW, NE, SE]), 2)
    ans["area_ac"] = round(ans["area_sf"] / 43560, 4)
    ans["perimeter"] = round(dist(SW, NW) + dist(NW, NE) + dist(NE, SE) + dist(SE, SW), 2)
    ans["corners"] = {k: [round(v.x, 3), round(v.y, 3)] for k, v in dict(SW=SW, NW=NW, NE=NE, SE=SE).items()}

    P = {p["p"]: p for p in base.read_points()}
    fip_ne = Vec2(P[10494]["e"], P[10494]["n"])
    ans["ne_to_fip"] = round(dist(NE, fip_ne), 2)
    c2000, c2002, c2001 = (Vec2(P[n]["e"], P[n]["n"]) for n in (2000, 2002, 2001))
    ans["inverse"] = {"bearing": bearing(c2000, c2002), "reverse": bearing(c2002, c2000),
                      "dist": round(dist(c2000, c2002), 2)}
    ans["id_2001"] = [round(c2001.x, 2), round(c2001.y, 2), P[2001]["z"]]

    # --- Ex 09 LAYMCH: retaining wall moved to layer 0
    wall = find_pline(msp, "V-SURF-WALL-E", (1125198.15, 733954.22), 3)
    if not done:
        wall.dxf.layer = "0"

    # --- Ex 10 MATCHPROP: curb line with overrides
    curb = find_pline(msp, "V-SURF-CURB-E", (1125297.7, 733832.6), 13)
    if not done:
        curb.dxf.color = 1
        curb.dxf.linetype = "DASHED2" if "DASHED2" in doc.linetypes else "Continuous"
        curb.dxf.lineweight = 70

    # --- Ex 11 TRIM: walk runs into the house
    walk = find_pline(msp, "V-SURF-CONC-E", (1125195.87, 733927.52), 3)
    wp = pts(walk)
    if not done:
        set_pline_end(walk, "first", point_along(wp[0], wp[1], -TRIM_OVER))

    # --- Ex 12 EXTEND: driveway edge stops short of the curb
    drive = find_pline(msp, "V-SURF-ASPH-E", (1125272.39, 734017.19), 2)
    dp = pts(drive)
    end_i = 0 if near(dp[0], (1125272.39, 734017.19), 0.1) else -1
    far = dp[1] if end_i == 0 else dp[-2]
    curb_edge = on_polyline(msp, dp[end_i])
    short_pt = point_along(dp[end_i], far, EXT_SHORT)
    hit = ray_hits_polyline(short_pt, dp[end_i], curb_edge)
    ans["extend_hit"] = [round(hit.x, 3), round(hit.y, 3)]
    set_pline_end(drive, "first" if end_i == 0 else "last", hit if done else short_pt)

    # --- Ex 13 COPY a tree to typed coordinates (point 10222)
    tree = find_insert(msp, "SSV-DEC", (1125170.861, 733969.169))
    t_pt = Vec2(P[10222]["e"], P[10222]["n"])
    ans["tree_pt"] = [round(t_pt.x, 2), round(t_pt.y, 2)]
    if done:
        tree.dxf.insert = (round(t_pt.x, 2), round(t_pt.y, 2), 0)
    else:
        msp.delete_entity(tree)

    # --- Ex 14 ROTATE Reference: CB #10144
    cb = find_insert(msp, "SSV-CB", (1124977.034, 734057.687))
    good_rot = cb.dxf.rotation
    ans["cb_bearing"] = bearing_of_angle(good_rot)
    if not done:
        cb.dxf.rotation = good_rot + CB_ROT_ERR

    # --- Ex 15 TEXT: label the north line
    mid = (NW + NE) / 2
    up = (NE - NW).orthogonal().normalize()          # left of W->E = north side
    rot = (NE - NW).angle_deg
    ans["label_offset_bearing"] = bearing((0, 0), up)
    if done:
        msp.add_text(f"{bearing(NW, NE)} {dist(NW, NE):.2f}'".replace("\u00b0", "%%d"), height=TXT, rotation=rot,
                     dxfattribs={"layer": "V-PROP-BNDY-TEXT-E", "style": "Survey"}
                     ).set_placement(mid + up * 1.0, align=TextEntityAlignment.BOTTOM_CENTER)
    ans["north_rot_bearing"] = bearing((0, 0), NE - NW)

    # --- Ex 16 TORIENT: HARBORVIEW DR upside down (MC so it flips in place)
    street = find_mtext(msp, "HARBORVIEW DR")
    from ezdxf import bbox
    c = bbox.extents([street]).center
    street.dxf.attachment_point = 5
    street.dxf.insert = c
    r = street.get_rotation()
    street.set_rotation(r if done else r + 180)

    # --- Ex 17 MTEXT: HOUSE label
    house = find_mtext(msp, "HOUSE")
    ans["house_label"] = [round(house.dxf.insert.x, 2), round(house.dxf.insert.y, 2)]
    if not done:
        msp.delete_entity(house)

    # --- Ex 18 MLEADER: CB #10144
    ml = find_mleader(msp, "CB #10144")
    ans["cb_leader"] = ml.context.mtext.default_content
    if not done:
        msp.delete_entity(ml)

    # --- Ex 19 DIMALIGNED setbacks
    garage_c = Vec2(1125180.09, 733877.91)
    house_c = Vec2(1125198.226, 733956.709)
    g_foot = perp_foot(garage_c, SW, SE)
    h_foot = perp_foot(house_c, NE, SE)
    ans["dim_garage"] = round(dist(garage_c, g_foot), 2)
    ans["dim_house"] = round(dist(house_c, h_foot), 2)
    if done:
        base.copy_layer(_src, doc, "V-ANNO-DIMS-E") if "V-ANNO-DIMS-E" in _src.layers else doc.layers.add("V-ANNO-DIMS-E", color=7)
        msp.add_aligned_dim(p1=garage_c, p2=g_foot, distance=4, dimstyle="SRV-20",
                            dxfattribs={"layer": "V-ANNO-DIMS-E"}).render()
        msp.add_aligned_dim(p1=house_c, p2=h_foot, distance=-4, dimstyle="SRV-20",
                            dxfattribs={"layer": "V-ANNO-DIMS-E"}).render()

    # --- layers for the exercises
    if "V-PROP-BNDY-TEXT-E" not in doc.layers:
        doc.layers.add("V-PROP-BNDY-TEXT-E", color=7)
    tn = doc.layers.add("TRAIN-NOTES", color=10)
    tn.dxf.plot = 0
    vp = doc.layers.add("G-ANNO-VPRT", color=8)
    vp.dxf.plot = 0
    if done:
        tn.freeze()
    else:
        doc.layers.add("TEMP-OLD", color=7)                   # purged in Ex 22
        blk = doc.blocks.new("OLD-SYMBOL")
        blk.add_circle((0, 0), 1.0)
        doc.header["$AUNITS"] = 0                            # Ex 02 fixes units
        doc.header["$LUPREC"] = 4
        callouts(msp, ans)
    build_sheet(doc, done)
    doc.header["$CLAYER"] = "0"
    return doc, ans


# ---------------------------------------------------------------- callouts
def callouts(msp, ans):
    items = {
        "02": (point_along(NE, SE, 60), (22, 6)),
        "03": ((1125223.153, 733963.975), (4, -14)),
        "05": ((1125129.208, 733951.874), (-16, -10)),
        "06": (SW, (-12, -14)),
        "07": ((NW + NE) / 2, (0, 16)),
        "08": ((1125020.0, 733940.0), (0, 0)),
        "09": ((1125200.9, 733954.2), (18, -8)),
        "10": ((1125258.0, 733985.8), (20, 6)),
        "11": ((1125194.4, 733927.5), (12, -12)),
        "12": ((1125270.5, 734012.0), (-16, 12)),
        "13": (ans["tree_pt"], (-22, 14)),
        "14": ((1124977.034, 734057.687), (14, -12)),
        "15": ((NW + NE) / 2 + Vec2(40, 0), (0, 16)),
        "16": ((1125293.6, 733907.9), (-26, -8)),
        "17": ((1125180.4, 733941.6), (-12, 10)),
        "18": ((1124978.5, 734058.0), (18, 10)),
        "19": ((1125180.09, 733875.5), (10, -16)),
    }
    r = 3.0
    for n, (tgt, off) in items.items():
        t = Vec2(tgt)
        b = t + Vec2(off)
        if Vec2(off).magnitude > r:
            msp.add_line(b + (t - b).normalize(r), t, dxfattribs={"layer": "TRAIN-NOTES"})
            msp.add_circle(t, 0.6, dxfattribs={"layer": "TRAIN-NOTES"})
        msp.add_circle(b, r, dxfattribs={"layer": "TRAIN-NOTES"})
        msp.add_text(n, height=2.6, dxfattribs={"layer": "TRAIN-NOTES", "style": "Survey"}
                     ).set_placement(b, align=TextEntityAlignment.MIDDLE_CENTER)


# ---------------------------------------------------------------- sheet
def build_sheet(doc, done):
    lay = doc.layouts.new("TOPO SHEET")
    lay.page_setup(size=SHEET, margins=(0, 0, 0, 0), units="inch",
                   name="ARCH_expand_D_(24.00_x_36.00_Inches)" if False else "ANSI_expand_D_(34.00_x_22.00_Inches)",
                   device="DWG To PDF.pc3")
    lay.dxf_layout.dxf.current_style_sheet = "monochrome.ctb"
    if "Layout1" in doc.layouts:
        doc.layouts.delete("Layout1")
    if "G-ANNO-TTLB" not in doc.layers:
        doc.layers.add("G-ANNO-TTLB", color=7)
    A = {"layer": "G-ANNO-TTLB", "style": "Survey"}
    W, H = SHEET

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
    t(SHEET_SUBTITLE, x0, H - 2.7, 0.10)
    t("FOR TRAINING ONLY - NOT FOR", x0, H - 3.0, 0.08)
    t("DESIGN OR CONSTRUCTION", x0, H - 3.18, 0.08)
    lay.add_line((xs, H - 3.4), (W - 0.5, H - 3.4), dxfattribs={"layer": "G-ANNO-TTLB"})
    notes = ["NOTES",
             "1. HORIZONTAL DATUM: NAD 1983(2011),",
             "   WASHINGTON STATE PLANE SOUTH ZONE,",
             "   U.S. SURVEY FEET.",
             "2. VERTICAL DATUM: NAVD 88.",
             "3. CONTOUR INTERVAL = 1 FOOT.",
             "4. UTILITIES SHOWN FROM SURFACE",
             "   EVIDENCE AND MEASUREDOWNS."]
    y = H - 3.8
    for i, s in enumerate(notes):
        t(s, x0, y, 0.12 if i == 0 else 0.085)
        y -= 0.22
    # north arrow + graphic scale (0-20-40-80 ft = 0-1-2-4 in)
    nb = doc.blocks.new("TRAIN-NORTH") if "TRAIN-NORTH" not in doc.blocks else doc.blocks.get("TRAIN-NORTH")
    if not len(nb):
        nb.add_circle((0, 0), 0.4)
        nb.add_solid([(0, 0.62), (-0.16, -0.22), (0, -0.06), (0.16, -0.22)])
        nb.add_text("N", height=0.18, dxfattribs={"style": "Survey"}).set_placement(
            (0, 0.8), align=TextEntityAlignment.BOTTOM_CENTER)
    lay.add_blockref("TRAIN-NORTH", (xs + 0.8, 3.2), dxfattribs={"layer": "G-ANNO-TTLB"})
    sx, sy = xs + 0.3, 2.0
    for i, (a, b) in enumerate([(0, 0.5), (0.5, 1.0), (1.0, 2.0)]):
        p = [(sx + a, sy), (sx + b, sy), (sx + b, sy + 0.07), (sx + a, sy + 0.07)]
        if i % 2 == 0:
            lay.add_solid([p[0], p[1], p[3], p[2]], dxfattribs={"layer": "G-ANNO-TTLB"})
        lay.add_lwpolyline(p, close=True, dxfattribs={"layer": "G-ANNO-TTLB"})
    for v, lbl in [(0, "0"), (0.5, "10"), (1.0, "20"), (2.0, "40")]:
        t(lbl, sx + v, sy + 0.14, 0.07, TextEntityAlignment.BOTTOM_CENTER)
    t("SCALE: 1\" = 20'", sx + 1.0, sy - 0.2, 0.09, TextEntityAlignment.TOP_CENTER)
    lay.add_line((xs, 1.3), (W - 0.5, 1.3), dxfattribs={"layer": "G-ANNO-TTLB"})
    t("DRAWN: ____   DATE: ________", x0, 1.0, 0.08)
    t("SHEET 1 OF 1", x0, 0.7, 0.09)

    x1, y1, x2, y2 = VP_BOX
    if done:
        w, h = x2 - x1, y2 - y1
        v = lay.add_viewport(center=((x1 + x2) / 2, (y1 + y2) / 2), size=(w, h),
                             view_center_point=VIEW_CENTER, view_height=h * base.SCALE,
                             dxfattribs={"layer": "G-ANNO-VPRT"})
        v.dxf.flags = v.dxf.flags | 16384
    else:
        lay.add_lwpolyline([(x1, y1), (x2, y1), (x2, y2), (x1, y2)], close=True,
                           dxfattribs={"layer": "TRAIN-NOTES", "linetype": "DASHED2", "ltscale": 1 / base.SCALE}
                           if "DASHED2" in doc.linetypes else {"layer": "TRAIN-NOTES"})
        lay.add_text("20  MVIEW: PICK THE CORNERS OF THIS DASHED BOX", height=0.2,
                     dxfattribs={"layer": "TRAIN-NOTES", "style": "Survey"}).set_placement((x1 + 0.3, y2 - 0.5))


def main():
    OUT.mkdir(exist_ok=True)
    start, ans = build(False)
    done, _ = build(True)
    for d in (start, done):
        a = d.audit()
        assert not a.has_errors, [str(e.message) for e in a.errors]
    start.saveas(OUT / "SURVEY_CAD_L1_START.dxf")
    done.saveas(OUT / "SURVEY_CAD_L1_COMPLETED.dxf")
    (OUT / "answers.json").write_text(json.dumps(ans, indent=2, ensure_ascii=False))
    print(json.dumps(ans, indent=2, ensure_ascii=False))


if __name__ == "__main__":
    main()
