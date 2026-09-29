"""Build the Survey CAD Level 1 training drawings.

Writes START.dxf (what the trainee opens) and COMPLETED.dxf (the answer key).
COMPLETED is START with every exercise applied, and each exercise's result is
computed the way the AutoCAD command would compute it (FILLET R0 = line
intersection, TRIM/EXTEND = boundary intersection, ROTATE Reference = align to
the curb, etc.), so the key is what a trainee actually gets.

Also writes answers.json (inverse / area answers quoted in the guide).

Site geometry is authored in a local "lot frame" (x along the street, y into the
lot, SW lot corner at 0,0) and rotated onto an assumed grid so the bearings are
realistic: front line N89d12'40"E, sides N00d47'20"W.
"""
import json
import math
import sys
from pathlib import Path

import ezdxf
from ezdxf.enums import TextEntityAlignment, MTextEntityAlignment
from ezdxf.math import Vec2
from ezdxf.render import mleader

OUT = Path(__file__).parent / "output"

# ---------------------------------------------------------------- geometry
ALPHA_DMS = (0, 47, 20)                     # street / lot rotation, CCW from east
ALPHA = math.radians(ALPHA_DMS[0] + ALPHA_DMS[1] / 60 + ALPHA_DMS[2] / 3600)
ALPHA_DEG = math.degrees(ALPHA)
E0, N0 = 5000.0, 8000.0                     # SW lot corner on the assumed grid
SCALE = 30                                  # 1" = 30'
TXT = 0.08 * SCALE                          # 2.4' standard text
TXT_BIG = 0.10 * SCALE                      # 3.0' street names etc.
TXT_SM = 0.06 * SCALE                       # 1.8' point labels


def T(x, y):
    """Lot frame -> assumed grid (X = Easting, Y = Northing)."""
    c, s = math.cos(ALPHA), math.sin(ALPHA)
    return Vec2(E0 + x * c - y * s, N0 + x * s + y * c)


def rot(deg_local):
    return deg_local + ALPHA_DEG


def bearing(p1, p2):
    d = p2 - p1
    az = math.degrees(math.atan2(d.x, d.y)) % 360
    if az <= 90:
        ns, ew, a = "N", "E", az
    elif az <= 180:
        ns, ew, a = "S", "E", 180 - az
    elif az <= 270:
        ns, ew, a = "S", "W", az - 180
    else:
        ns, ew, a = "N", "W", 360 - az
    tot = round(a * 3600)
    d_, rem = divmod(tot, 3600)
    m_, s_ = divmod(rem, 60)
    return f"{ns}{d_:02d}°{m_:02d}'{s_:02d}\"{ew}"


def seg_intersect(p1, p2, p3, p4):
    """Intersection of the infinite lines p1p2 and p3p4."""
    d1, d2 = p2 - p1, p4 - p3
    den = d1.x * d2.y - d1.y * d2.x
    t = ((p3.x - p1.x) * d2.y - (p3.y - p1.y) * d2.x) / den
    return p1 + d1 * t


# ---------------------------------------------------------------- site data
LOT = [(0, 0), (100, 0), (100, 140), (0, 140)]           # SW, SE, NE, NW
HOUSE = [(22, 20), (46, 20), (46, 25), (80.5, 25), (80.5, 62), (22, 62)]

# pt, x, y, elev, desc  (lot frame)
POINTS = [
    (1, 0, 0, 312.10, "FIR 1/2"),
    (2, 100, 0, 311.62, "FIR 1/2"),
    (3, 100, 140, 318.44, "FIR 1/2"),
    (4, 0, 140, 318.87, "FIR 1/2"),
    (10, -50, -30, 309.95, "MON CASE"),
    (11, 160, -30, 309.41, "MON CASE"),
    (20, 92, -13, 310.58, "CB RIM"),
    (21, 92, -47, 310.41, "CB RIM"),
    (22, 40, -24, 310.12, "SSMH RIM"),
    (23, 78, -8.5, 311.02, "WM"),
    (24, -20, -8.5, 311.40, "FH"),
    (25, -40, -8.5, 311.35, "PP"),
    (26, 150, -8.5, 310.80, "PP"),
    (30, 10, 45, 314.05, "TREE 24 MAPLE"),
    (31, 88, 100, 316.72, "TREE 18 MAPLE"),
    (32, 60, 115, 317.30, "TREE 30 OAK"),
    (33, 120, -8.5, 310.95, "TREE 8 CHERRY"),
    (40, 62, 25, 314.20, "FF"),
    (41, 28, 12, 312.95, "CONC"),
    (50, 6, 30, 313.40, "GS"),
    (51, 70, 10, 312.70, "GS"),
    (52, 90, 40, 313.95, "GS"),
    (53, 10, 90, 316.10, "GS"),
    (54, 40, 100, 316.50, "GS"),
    (55, 80, 128, 318.05, "GS"),
    (56, 20, 128, 318.30, "GS"),
]
PT = {p[0]: p for p in POINTS}

CB_START_ROT = 35.0          # Ex 14: catch basin is inserted 35 deg off the curb
FENCE_REAR_SHORT = 97.3      # Ex 10: rear fence stops short of the corner
FENCE_EAST_LONG = 140.6      # Ex 10: east fence overshoots the corner
PATIO_OVERSHOOT = 58.4       # Ex 11: patio edge runs into the house
DRIVE_SHORT = 4.3            # Ex 12: driveway edge stops short of back of walk

# ---------------------------------------------------------------- layers
# name, color, linetype, lineweight (1/100 mm), plot
LAYERS = [
    ("V-PROP-LINE", 6, "Continuous", 50, True),
    ("V-PROP-RWAY", 6, "Continuous", 35, True),
    ("V-PROP-ADJN", 8, "DASHED2", 18, True),
    ("V-PROP-CNTR", 2, "CENTER2", 18, True),
    ("V-PROP-TEXT", 7, "Continuous", 25, True),
    ("V-CTRL-MONU", 1, "Continuous", 25, True),
    ("V-NODE", 140, "Continuous", 13, True),
    ("V-ROAD-CURB", 4, "Continuous", 35, True),
    ("V-ROAD-SWLK", 51, "Continuous", 25, True),
    ("V-SITE-CONC", 51, "Continuous", 25, True),
    ("V-SITE-FENC", 32, "FENCE", 25, True),
    ("V-SITE-VEGE", 3, "Continuous", 25, True),
    ("V-BLDG-OTLN", 30, "Continuous", 50, True),
    ("V-STRM-STRC", 150, "Continuous", 35, True),
    ("V-STRM-PIPE", 150, "STORM", 25, True),
    ("V-SSWR-STRC", 34, "Continuous", 35, True),
    ("V-SSWR-PIPE", 34, "SANITARY", 25, True),
    ("V-WATR-STRC", 5, "Continuous", 35, True),
    ("V-WATR-PIPE", 5, "WATER", 25, True),
    ("V-POWR-POLE", 212, "Continuous", 35, True),
    ("V-POWR-OVHD", 212, "OVERHEAD", 25, True),
    ("V-ANNO-TEXT", 7, "Continuous", 25, True),
    ("V-ANNO-DIMS", 7, "Continuous", 18, True),
    ("V-ANNO-TTLB", 7, "Continuous", 35, True),
    ("G-ANNO-VPRT", 8, "Continuous", 13, False),
    ("TRAIN-NOTES", 10, "Continuous", 25, False),
    ("TEMP-OLD", 7, "Continuous", 25, True),          # unused, removed by PURGE (Ex 22)
]

# Linetype patterns are in plotted inches; LTSCALE = 30 and PSLTSCALE = 0.
SIMPLE_LT = {
    "DASHED2": ("Dashed (.5x) __ __ __", [0.375, 0.25, -0.125]),
    "CENTER2": ("Center (.5x) ___ _ ___", [1.125, 0.75, -0.125, 0.125, -0.125]),
}


def complex_lt(label, dash=0.40, gap_before=0.06, char_w=0.05):
    w = char_w * len(label)
    gap_after = w + 0.02 - 0.0  # room for the text plus a small gap
    pattern = (f'A,{dash},-{gap_before},["{label}",STANDARD,S=.06,U=0.0,'
               f'X={-(gap_before - 0.02):.3f},Y=-.03],-{gap_after:.3f}')
    return pattern, dash + gap_before + gap_after


def setup_doc(doc):
    h = doc.header
    h["$INSUNITS"] = 2          # feet
    h["$MEASUREMENT"] = 0       # imperial
    h["$LUNITS"] = 2            # decimal
    h["$LUPREC"] = 4            # START: 4 places  (Ex 02 fixes to 2)
    h["$AUNITS"] = 0            # START: decimal degrees (Ex 02 fixes to surveyor)
    h["$AUPREC"] = 4
    h["$ANGBASE"] = 0.0
    h["$ANGDIR"] = 0
    h["$LTSCALE"] = float(SCALE)
    h["$PSLTSCALE"] = 0
    h["$TEXTSIZE"] = TXT
    h["$DIMSCALE"] = float(SCALE)
    h["$PDMODE"] = 0

    doc.styles.new("SURVEY", dxfattribs={"font": "romans.shx", "height": 0, "width": 1.0})
    st = doc.styles.get("Standard")
    st.dxf.font = "romans.shx"

    for name, (desc, pat) in SIMPLE_LT.items():
        doc.linetypes.add(name, pattern=pat, description=desc)
    for name, label in [("FENCE", "X"), ("STORM", "SD"), ("SANITARY", "SS"),
                        ("WATER", "W"), ("OVERHEAD", "OHP")]:
        pattern, length = complex_lt(label)
        doc.linetypes.add(name, pattern=pattern, length=length,
                          description=f"{name.title()} ----{label}----{label}----")

    for name, color, lt, lw, plot in LAYERS:
        lay = doc.layers.add(name, color=color, linetype=lt)
        lay.dxf.lineweight = lw
        lay.dxf.plot = 1 if plot else 0

    ds = doc.dimstyles.new("SRV-30")
    for k, v in dict(dimscale=SCALE, dimtxt=0.08, dimasz=0.08, dimexo=0.05, dimexe=0.06,
                     dimgap=0.03, dimtad=1, dimtih=0, dimtoh=0, dimdec=2, dimlunit=2,
                     dimzin=0, dimtxsty="SURVEY", dimpost="<>'", dimclrd=256, dimclre=256,
                     dimclrt=256, dimtofl=1, dimatfit=3, dimlfac=1.0).items():
        ds.set_dxf_attrib(k, v)
    doc.header["$DIMSTYLE"] = "SRV-30"

    mls = doc.mleader_styles.duplicate_entry("Standard", "SRV-30")
    mls.dxf.char_height = TXT
    mls.dxf.arrow_head_size = TXT_SM
    mls.dxf.landing_gap_size = 0.8
    mls.dxf.dogleg_length = 3.0
    mls.dxf.text_style_handle = doc.styles.get("SURVEY").dxf.handle

    doc.header["$TEXTSTYLE"] = "SURVEY"
    make_blocks(doc)


# ---------------------------------------------------------------- blocks
def make_blocks(doc):
    """All block geometry is on layer 0 / ByLayer so the INSERT layer controls it."""
    b = doc.blocks.new("SRV-PT", base_point=(0, 0))
    b.add_point((0, 0))
    b.add_line((-0.5, -0.5), (0.5, 0.5))
    b.add_line((-0.5, 0.5), (0.5, -0.5))
    for tag, y in (("PNT", 1.1), ("ELEV", -0.9), ("DESC", -3.3)):
        b.add_attdef(tag, (1.1, y), dxfattribs={"height": TXT_SM, "style": "SURVEY"})

    b = doc.blocks.new("FIR", base_point=(0, 0))          # found iron rod
    b.add_circle((0, 0), 0.9)
    b.add_circle((0, 0), 0.35)

    b = doc.blocks.new("MON-CASE", base_point=(0, 0))     # monument in case
    b.add_circle((0, 0), 1.6)
    b.add_lwpolyline([(-0.8, -0.8), (0.8, -0.8), (0.8, 0.8), (-0.8, 0.8)], close=True)
    b.add_point((0, 0))

    b = doc.blocks.new("TREE-DECID", base_point=(0, 0))   # deciduous tree, 8' canopy
    n, r = 10, 4.0
    pts = []
    for i in range(n):
        a = 2 * math.pi * i / n
        pts.append((r * math.cos(a), r * math.sin(a), 0, 0, -0.35))
    b.add_lwpolyline(pts, format="xyseb", close=True)
    b.add_circle((0, 0), 0.6)

    b = doc.blocks.new("CB", base_point=(0, 0))            # catch basin 3' x 2'
    b.add_lwpolyline([(-1.5, -1), (1.5, -1), (1.5, 1), (-1.5, 1)], close=True)
    for x in (-0.75, 0, 0.75):
        b.add_line((x, -0.7), (x, 0.7))

    b = doc.blocks.new("SSMH", base_point=(0, 0))          # sanitary manhole
    b.add_circle((0, 0), 2.0)
    b.add_text("S", height=2.0, dxfattribs={"style": "SURVEY"}).set_placement(
        (0, 0), align=TextEntityAlignment.MIDDLE_CENTER)

    b = doc.blocks.new("WM", base_point=(0, 0))            # water meter
    b.add_lwpolyline([(-1.2, -0.8), (1.2, -0.8), (1.2, 0.8), (-1.2, 0.8)], close=True)
    b.add_text("W", height=1.1, dxfattribs={"style": "SURVEY"}).set_placement(
        (0, 0), align=TextEntityAlignment.MIDDLE_CENTER)

    b = doc.blocks.new("FH", base_point=(0, 0))            # fire hydrant
    b.add_circle((0, 0), 1.0)
    b.add_line((-2.0, 0), (-1.0, 0))
    b.add_line((1.0, 0), (2.0, 0))
    b.add_line((0, 1.0), (0, 2.0))

    b = doc.blocks.new("PP", base_point=(0, 0))            # power pole
    b.add_circle((0, 0), 1.0)
    b.add_line((-1.6, 0), (1.6, 0))
    b.add_line((0, -1.6), (0, 1.6))

    b = doc.blocks.new("OLD-TREE", base_point=(0, 0))      # unused, removed by PURGE
    b.add_circle((0, 0), 3.0)

    b = doc.blocks.new("NORTH", base_point=(0, 0))         # paper-space inches
    b.add_circle((0, 0), 0.35)
    b.add_solid([(0, 0.55), (-0.14, -0.2), (0, -0.05), (0.14, -0.2)])
    b.add_line((0, -0.35), (0, 0.55))
    b.add_text("N", height=0.16, dxfattribs={"style": "SURVEY"}).set_placement(
        (0, 0.72), align=TextEntityAlignment.BOTTOM_CENTER)


# ---------------------------------------------------------------- helpers
def line(msp, a, b, layer, **kw):
    return msp.add_line(T(*a), T(*b), dxfattribs={"layer": layer, **kw})


def pline(msp, pts, layer, close=False):
    return msp.add_lwpolyline([T(*p) for p in pts], close=close, dxfattribs={"layer": layer})


def insert(msp, name, xy, layer, rot_local=0.0, scale=1.0):
    return msp.add_blockref(name, T(*xy), dxfattribs={
        "layer": layer, "rotation": rot(rot_local), "xscale": scale, "yscale": scale})


def text(msp, s, xy, layer, h=TXT, rot_local=0.0, align=TextEntityAlignment.MIDDLE_CENTER):
    return msp.add_text(s, height=h, rotation=rot(rot_local),
                        dxfattribs={"layer": layer, "style": "SURVEY"}).set_placement(T(*xy), align=align)


def mleader_text(msp, content, arrow_xy, text_xy, layer):
    ml = msp.add_multileader_mtext("SRV-30", dxfattribs={"layer": layer})
    ml.set_content(content, char_height=TXT, alignment=mleader.TextAlignment.left)
    ml.add_leader_line(mleader.ConnectionSide.left, [T(*arrow_xy)])
    ml.build(insert=T(*text_xy))
    return ml


# ---------------------------------------------------------------- model space
def build_model(doc, done):
    msp = doc.modelspace()

    # --- boundary (Ex 06/07)
    if done:
        pline(msp, LOT, "V-PROP-LINE", close=True)
    else:
        line(msp, LOT[0], LOT[1], "V-PROP-LINE")   # front
        line(msp, LOT[1], LOT[2], "V-PROP-LINE")   # east
        line(msp, LOT[3], LOT[0], "V-PROP-LINE")   # west  (rear line is missing)

    # right of way / centerline / adjoiners (ROW broken at the lot so nothing overlaps)
    line(msp, (-70, 0), (0, 0), "V-PROP-RWAY")
    line(msp, (100, 0), (175, 0), "V-PROP-RWAY")
    line(msp, (-70, -60), (175, -60), "V-PROP-RWAY")
    line(msp, (-70, -30), (175, -30), "V-PROP-CNTR")
    line(msp, (-70, 140), (0, 140), "V-PROP-ADJN")
    line(msp, (100, 140), (175, 140), "V-PROP-ADJN")
    line(msp, (0, 140), (0, 150), "V-PROP-ADJN")
    line(msp, (100, 140), (100, 150), "V-PROP-ADJN")

    # boundary labels
    front_b = bearing(T(*LOT[0]), T(*LOT[1]))
    side_b = bearing(T(*LOT[0]), T(*LOT[3]))
    text(msp, f"{front_b} 100.00'", (72, 1.2), "V-PROP-TEXT", align=TextEntityAlignment.BOTTOM_CENTER)
    text(msp, f"{side_b} 140.00'", (103.6, 70), "V-PROP-TEXT", rot_local=90,
         align=TextEntityAlignment.BOTTOM_CENTER)
    text(msp, f"{side_b} 140.00'", (-1.2, 70), "V-PROP-TEXT", rot_local=90,
         align=TextEntityAlignment.BOTTOM_CENTER)
    if done:  # Ex 15: BC at midpoint, rotation N89d12'40"E, then MOVE @1<N00d47'20"W
        text(msp, f"{bearing(T(*LOT[2]), T(*LOT[3]))} 100.00'", (50, 141.0), "V-PROP-TEXT",
             align=TextEntityAlignment.BOTTOM_CENTER)
    text(msp, "LOT 8", (30, 112), "V-PROP-TEXT", h=TXT_BIG)
    text(msp, "BLOCK 3", (30, 107.5), "V-PROP-TEXT")
    text(msp, "LOT 7", (-35, 75), "V-PROP-TEXT", h=TXT_BIG)
    text(msp, "LOT 9", (138, 75), "V-PROP-TEXT", h=TXT_BIG)

    # monuments
    for p in (1, 2, 3, 4):
        insert(msp, "FIR", PT[p][1:3], "V-CTRL-MONU")
    for p in (10, 11):
        insert(msp, "MON-CASE", PT[p][1:3], "V-CTRL-MONU")

    # --- street: north side
    line(msp, (-70, -0.5), (175, -0.5), "V-ROAD-SWLK")            # back of walk
    line(msp, (-70, -5.5), (100, -5.5), "V-ROAD-SWLK")            # front of walk (west piece)
    if done:  # Ex 09 MATCHPROP clears the overrides
        line(msp, (100, -5.5), (175, -5.5), "V-ROAD-SWLK")
    else:
        line(msp, (100, -5.5), (175, -5.5), "V-ROAD-SWLK", color=1, linetype="DASHED2", lineweight=70)
    for y in (-11.5, -12.0, -13.5):                                # back / face of curb, lip of gutter
        line(msp, (-70, y), (175, y), "V-ROAD-CURB")
    # south side
    for y in (-46.5, -48.0, -48.5):
        line(msp, (-70, y), (175, y), "V-ROAD-CURB")
    line(msp, (-70, -54.5), (175, -54.5), "V-ROAD-SWLK")
    line(msp, (-70, -59.5), (175, -59.5), "V-ROAD-SWLK")

    # --- building, porch, walks, driveway, patio
    pline(msp, HOUSE, "V-BLDG-OTLN", close=True)
    pline(msp, [(58, 25), (58, 17), (66, 17), (66, 25)], "V-SITE-CONC")   # stoop
    line(msp, (46, 21), (58, 21), "V-SITE-CONC")                          # front walk
    line(msp, (44, 17), (58, 17), "V-SITE-CONC")
    line(msp, (24, 20), (24, -0.5), "V-SITE-CONC")                        # driveway west edge
    # Ex 12 EXTEND (boundary = back of walk)
    line(msp, (44, 20), (44, -0.5 if done else DRIVE_SHORT), "V-SITE-CONC")
    line(msp, (24, -5.5), (22, -11.5), "V-SITE-CONC")                     # approach flares
    line(msp, (44, -5.5), (46, -11.5), "V-SITE-CONC")
    line(msp, (30, 62), (30, 74), "V-SITE-CONC")                          # patio
    line(msp, (30, 74), (52, 74), "V-SITE-CONC")
    line(msp, (52, 74), (52, 62 if done else PATIO_OVERSHOOT), "V-SITE-CONC")   # Ex 11 TRIM
    text(msp, "CONC PATIO", (41, 68), "V-ANNO-TEXT", h=TXT_SM)
    text(msp, "CONC DRWY", (34, 5), "V-ANNO-TEXT", h=TXT_SM)

    # --- fence
    line(msp, (1, 55), (22, 55), "V-SITE-FENC")
    line(msp, (80.5, 55), (99, 55), "V-SITE-FENC")
    line(msp, (1, 55), (1, 139), "V-SITE-FENC" if done else "0")          # Ex 08 LAYMCH
    if done:  # Ex 10 FILLET R0 -> intersection of the two runs
        corner = seg_intersect(T(1, 139), T(FENCE_REAR_SHORT, 139), T(99, 55), T(99, FENCE_EAST_LONG))
        msp.add_line(T(1, 139), corner, dxfattribs={"layer": "V-SITE-FENC"})
        msp.add_line(T(99, 55), corner, dxfattribs={"layer": "V-SITE-FENC"})
    else:
        line(msp, (1, 139), (FENCE_REAR_SHORT, 139), "V-SITE-FENC")
        line(msp, (99, 55), (99, FENCE_EAST_LONG), "V-SITE-FENC")

    # --- vegetation
    for p in (30, 32, 33):
        insert(msp, "TREE-DECID", PT[p][1:3], "V-SITE-VEGE")
    if done:  # Ex 13 INSERT at the node of point 31
        insert(msp, "TREE-DECID", PT[31][1:3], "V-SITE-VEGE", rot_local=-ALPHA_DEG)

    # --- utilities
    cb_rot = 0.0 if done else CB_START_ROT                                # Ex 14 ROTATE Reference
    insert(msp, "CB", PT[20][1:3], "V-STRM-STRC", rot_local=cb_rot)
    insert(msp, "CB", PT[21][1:3], "V-STRM-STRC")
    line(msp, (92, -14.0), (92, -46.0), "V-STRM-PIPE")
    insert(msp, "SSMH", PT[22][1:3], "V-SSWR-STRC", rot_local=-ALPHA_DEG)
    line(msp, (-70, -24), (38, -24), "V-SSWR-PIPE")
    line(msp, (42, -24), (175, -24), "V-SSWR-PIPE")
    line(msp, (-70, -36), (175, -36), "V-WATR-PIPE")
    line(msp, (78, -36), (78, -9.3), "V-WATR-PIPE")
    insert(msp, "WM", PT[23][1:3], "V-WATR-STRC")
    insert(msp, "FH", PT[24][1:3], "V-WATR-STRC")
    insert(msp, "PP", PT[25][1:3], "V-POWR-POLE")
    insert(msp, "PP", PT[26][1:3], "V-POWR-POLE")
    line(msp, (-39, -8.5), (149, -8.5), "V-POWR-OVHD")

    # --- annotation
    street = text(msp, "MAPLE STREET", (10, -20.5), "V-ANNO-TEXT", h=TXT_BIG,
                  rot_local=0 if done else 180)                          # Ex 16 TORIENT
    text(msp, "(60' PUBLIC R/W)", (130, -41.5), "V-ANNO-TEXT")
    mleader_text(msp, "SSMH\\PRIM=310.12\\PIE 8\" E=301.40\\PIE 8\" W=301.52",
                 (40 + 0.8, -24 - 1.8), (50, -66), "V-ANNO-TEXT")        # example leader
    if done:
        # Ex 17 MTEXT, MC, centered between house corners, rotated with the street
        c = (T(22, 62) + T(80.5, 25)) / 2
        msp.add_mtext("1-STORY WOOD FRAME\\PHOUSE\\PFF=314.20", dxfattribs={
            "layer": "V-ANNO-TEXT", "style": "SURVEY", "char_height": TXT, "width": 40.0,
            "attachment_point": 5, "insert": c, "rotation": ALPHA_DEG})
        # Ex 18 MLEADER on the rotated catch basin
        mleader_text(msp, "CB TYPE 1\\PRIM=310.58\\PIE 12\" S=307.18",
                     (93.2, -12.3), (112, 16), "V-ANNO-TEXT")
        # Ex 19 DIMALIGNED setbacks
        p1, p2 = T(22, 20), T(22, 0)
        msp.add_aligned_dim(p1=p1, p2=p2, distance=-4, dimstyle="SRV-30",
                            dxfattribs={"layer": "V-ANNO-DIMS"}).render()
        p1, p2 = T(80.5, 25), T(100, 25)
        msp.add_aligned_dim(p1=p1, p2=p2, distance=5, dimstyle="SRV-30",
                            dxfattribs={"layer": "V-ANNO-DIMS"}).render()

    # --- survey points (V-NODE is frozen in START; Ex 04 thaws it)
    for n, x, y, z, d in POINTS:
        ref = msp.add_blockref("SRV-PT", T(x, y), dxfattribs={"layer": "V-NODE"})
        ref.add_auto_attribs({"PNT": str(n), "ELEV": f"{z:.2f}", "DESC": d})

    if not done:
        build_callouts(msp)
    return street


# ---------------------------------------------------------------- callouts
# exercise: (target in lot frame, bubble in lot frame)
CALLOUTS = {
    2: ((88, 0), (88, 10)),
    7: ((0, 95), (-18, 95)),
    3: ((0, 0), (-14, 12)),
    5: ((-50, -30), (-62, -16)),
    6: ((50, 140), (50, 152)),
    8: ((1, 120), (-12, 128)),
    9: ((140, -5.5), (150, 8)),
    10: ((99, 139), (112, 150)),
    11: ((52, 60), (62, 80)),
    12: ((44, 2), (56, 8)),
    13: ((88, 100), (100, 112)),
    14: ((92, -13), (80, -18)),
    16: ((-9, -20.5), (-22, -20.5)),
    17: ((51, 43), (66, 50)),
    15: ((50, 141), (30, 152)),
    18: ((93.5, -12.5), (114, 14)),
    19: ((22, 10), (12, 16)),
}


def build_callouts(msp):
    r = 3.0
    for n, (tgt, bub) in sorted(CALLOUTS.items()):
        t, b = T(*tgt), T(*bub)
        d = (t - b)
        if d.magnitude > r:
            msp.add_line(b + d.normalize(r), t, dxfattribs={"layer": "TRAIN-NOTES"})
            msp.add_circle(t, 0.6, dxfattribs={"layer": "TRAIN-NOTES"})
        msp.add_circle(b, r, dxfattribs={"layer": "TRAIN-NOTES"})
        msp.add_text(f"{n:02d}", height=2.6, dxfattribs={"layer": "TRAIN-NOTES", "style": "SURVEY"}
                     ).set_placement(b, align=TextEntityAlignment.MIDDLE_CENTER)


# ---------------------------------------------------------------- paper space
VP_BOX = (0.75, 0.75, 13.25, 10.25)
VIEW_CENTER_LOCAL = (52, 36)


def build_sheet(doc, done):
    lay = doc.layouts.new("TOPO SHEET")
    lay.page_setup(size=(17, 11), margins=(0, 0, 0, 0), units="inch",
                   name="ANSI_B_(17.00_x_11.00_Inches)", device="DWG To PDF.pc3")
    lay.dxf_layout.dxf.current_style_sheet = "monochrome.ctb"
    if "Layout1" in doc.layouts:
        doc.layouts.delete("Layout1")
    A = {"layer": "V-ANNO-TTLB", "style": "SURVEY"}

    lay.add_lwpolyline([(0.5, 0.5), (16.5, 0.5), (16.5, 10.5), (0.5, 10.5)], close=True,
                       dxfattribs={"layer": "V-ANNO-TTLB", "const_width": 0.02})
    lay.add_line((13.5, 0.5), (13.5, 10.5), dxfattribs={"layer": "V-ANNO-TTLB"})

    def t(s, x, y, h, al=TextEntityAlignment.LEFT):
        lay.add_text(s, height=h, dxfattribs=A).set_placement((x, y), align=al)

    x0 = 13.65
    t("PARAMETRIX", x0, 10.1, 0.20)
    t("SURVEY CAD TRAINING - LEVEL 1", x0, 9.85, 0.09)
    lay.add_line((13.5, 9.7), (16.5, 9.7), dxfattribs={"layer": "V-ANNO-TTLB"})
    t("TOPOGRAPHIC SURVEY", x0, 9.4, 0.14)
    t("LOT 8, BLOCK 3", x0, 9.18, 0.09)
    t("CEDAR PARK ADDITION", x0, 9.0, 0.09)
    t("(FICTITIOUS SITE - TRAINING ONLY)", x0, 8.82, 0.07)
    lay.add_line((13.5, 8.65), (16.5, 8.65), dxfattribs={"layer": "V-ANNO-TTLB"})

    t("LEGEND", x0, 8.4, 0.10)
    legend = [("FIR", "FOUND 1/2\" IRON ROD", 1.0), ("MON-CASE", "MONUMENT IN CASE", 1.0),
              ("SRV-PT", "SURVEY POINT", 1.0), ("TREE-DECID", "DECIDUOUS TREE", 0.6),
              ("CB", "CATCH BASIN", 1.0), ("SSMH", "SANITARY MANHOLE", 1.0),
              ("WM", "WATER METER", 1.0), ("FH", "FIRE HYDRANT", 1.0), ("PP", "POWER POLE", 1.0)]
    y = 8.15
    for blk, label, s in legend:
        ref = lay.add_blockref(blk, (x0 + 0.2, y + 0.03), dxfattribs={
            "layer": "V-ANNO-TTLB", "xscale": s * 2 / SCALE, "yscale": s * 2 / SCALE})
        if blk == "SRV-PT":
            ref.add_auto_attribs({"PNT": "", "ELEV": "", "DESC": ""})
        t(label, x0 + 0.5, y, 0.07)
        y -= 0.24
    for lt, label in [("FENCE", "FENCE"), ("STORM", "STORM DRAIN"), ("SANITARY", "SANITARY SEWER"),
                      ("WATER", "WATER LINE"), ("OVERHEAD", "OVERHEAD POWER")]:
        lay.add_line((x0, y + 0.03), (x0 + 0.4, y + 0.03),
                     dxfattribs={"layer": "V-ANNO-TTLB", "linetype": lt, "ltscale": 1 / SCALE})
        t(label, x0 + 0.5, y, 0.07)
        y -= 0.24
    lay.add_line((13.5, y + 0.05), (16.5, y + 0.05), dxfattribs={"layer": "V-ANNO-TTLB"})

    notes = ["NOTES",
             "1. BASIS OF BEARINGS: N89°12'40\"E ALONG",
             "   THE NORTH R/W OF MAPLE STREET.",
             "2. COORDINATES: ASSUMED LOCAL GRID,",
             "   SW LOT CORNER = N 8000.00, E 5000.00.",
             "3. ELEVATIONS: ASSUMED DATUM.",
             "4. UTILITIES SHOWN FROM SURFACE",
             "   EVIDENCE ONLY."]
    y -= 0.2
    for i, s in enumerate(notes):
        t(s, x0, y, 0.10 if i == 0 else 0.065)
        y -= 0.16

    lay.add_blockref("NORTH", (14.2, 1.95), dxfattribs={"layer": "V-ANNO-TTLB"})
    # graphic scale: 0-15-30-60 ft = 0-0.5-1-2 in
    sx, sy = 14.7, 1.8
    for i, (a, b) in enumerate([(0, 0.5), (0.5, 1.0), (1.0, 2.0)]):
        pts = [(sx + a * 0.8, sy), (sx + b * 0.8, sy), (sx + b * 0.8, sy + 0.06), (sx + a * 0.8, sy + 0.06)]
        if i % 2 == 0:
            lay.add_solid([pts[0], pts[1], pts[3], pts[2]], dxfattribs={"layer": "V-ANNO-TTLB"})
        lay.add_lwpolyline(pts, close=True, dxfattribs={"layer": "V-ANNO-TTLB"})
    for v, lbl in [(0, "0"), (0.5, "15"), (1.0, "30"), (2.0, "60")]:
        t(lbl, sx + v * 0.8, sy + 0.12, 0.06, TextEntityAlignment.BOTTOM_CENTER)
    t("SCALE: 1\" = 30'", sx + 0.8, sy - 0.18, 0.08, TextEntityAlignment.TOP_CENTER)

    lay.add_line((13.5, 1.2), (16.5, 1.2), dxfattribs={"layer": "V-ANNO-TTLB"})
    t("DRAWN: ___   DATE: ________", x0, 0.95, 0.07)
    t("SHEET 1 OF 1", x0, 0.68, 0.08)

    x1, y1, x2, y2 = VP_BOX
    if done:  # Ex 20
        w, h = x2 - x1, y2 - y1
        vc = T(*VIEW_CENTER_LOCAL)
        vp = lay.add_viewport(center=((x1 + x2) / 2, (y1 + y2) / 2), size=(w, h),
                              view_center_point=vc, view_height=h * SCALE,
                              dxfattribs={"layer": "G-ANNO-VPRT"})
        vp.dxf.flags = vp.dxf.flags | 16384            # Display Locked
    else:
        lay.add_lwpolyline([(x1, y1), (x2, y1), (x2, y2), (x1, y2)], close=True,
                           dxfattribs={"layer": "TRAIN-NOTES", "linetype": "DASHED2", "ltscale": 1 / SCALE})
        lay.add_text("20  MVIEW: PICK THE CORNERS OF THIS DASHED BOX", height=0.14,
                     dxfattribs={"layer": "TRAIN-NOTES", "style": "SURVEY"}
                     ).set_placement((x1 + 0.2, y2 - 0.35))
    return lay


# ---------------------------------------------------------------- build
def build(done):
    doc = ezdxf.new("R2018", setup=False)
    setup_doc(doc)
    build_model(doc, done)
    build_sheet(doc, done)
    if done:
        h = doc.header
        h["$AUNITS"] = 4            # Ex 02 surveyor's units
        h["$AUPREC"] = 4            # N 45d30'15" E
        h["$LUPREC"] = 2
        h["$OSMODE"] = 1 + 2 + 4 + 8 + 32 + 64 + 128       # Ex 03 END MID CEN NOD INT INS PER
        doc.layers.get("TRAIN-NOTES").freeze()           # Ex 22
        doc.layers.remove("TEMP-OLD")                    # Ex 22 PURGE
        doc.blocks.delete_block("OLD-TREE", safe=False)
        h["$CLAYER"] = "0"
    else:
        doc.layers.get("V-NODE").freeze()                # Ex 04 thaws it
        h = doc.header
        h["$OSMODE"] = 0
    doc.header["$EXTMIN"] = (T(-75, -85).x, T(-75, -85).y, 0)
    doc.header["$EXTMAX"] = (T(180, 160).x, T(180, 160).y, 0)
    return doc


def answers():
    p1, p10 = T(*PT[1][1:3]), T(*PT[10][1:3])
    lot = [T(*p) for p in LOT]
    area = 0.5 * abs(sum(lot[i].x * lot[i - 1].y - lot[i - 1].x * lot[i].y for i in range(4)))
    return {
        "pt1": [round(p1.x, 2), round(p1.y, 2)],
        "pt3": [round(T(*LOT[2]).x, 2), round(T(*LOT[2]).y, 2)],
        "pt4": [round(T(*LOT[3]).x, 2), round(T(*LOT[3]).y, 2)],
        "inverse_1_10": {"bearing": bearing(p1, p10), "reverse": bearing(p10, p1),
                         "distance": round(p1.distance(p10), 2)},
        "front_bearing": bearing(lot[0], lot[1]),
        "side_bearing": bearing(lot[0], lot[3]),
        "rear_bearing": bearing(lot[2], lot[3]),
        "area_sf": round(area, 2), "area_ac": round(area / 43560, 4), "perimeter": 480.0,
        "points": [{"pt": n, "n": round(T(x, y).y, 2), "e": round(T(x, y).x, 2), "z": z, "desc": d}
                   for n, x, y, z, d in POINTS],
        "nw_end": [round(T(*LOT[3]).x, 2), round(T(*LOT[3]).y, 2)],
        "alpha_deg": ALPHA_DEG, "cb_start_rot": CB_START_ROT + ALPHA_DEG,
    }


if __name__ == "__main__":
    OUT.mkdir(exist_ok=True)
    build(False).saveas(OUT / "Survey_CAD_Level1_START.dxf")
    build(True).saveas(OUT / "Survey_CAD_Level1_COMPLETED.dxf")
    (OUT / "answers.json").write_text(json.dumps(answers(), indent=2, ensure_ascii=False))
    print(json.dumps(answers(), indent=2, ensure_ascii=False))
