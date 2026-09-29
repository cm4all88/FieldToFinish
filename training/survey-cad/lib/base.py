"""Rebuild the survey base into a clean DXF.

The source is a Civil 3D base run through EXPORTTOAUTOCAD and converted from
DWG with LibreDWG. That file opens in ezdxf but carries broken extension
dictionaries and Civil 3D leftovers, so nothing is copied over directly: every
entity, block, layer, linetype and text style is recreated from its geometry in
a fresh R2018 document. Anonymous blocks from the export are flattened so the
symbols become ordinary named SSV-* block references.
"""
import math
from pathlib import Path

import ezdxf
from ezdxf import bbox
from ezdxf.math import Vec2, Vec3
from ezdxf.render import mleader

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "source"
SRC_DXF = SOURCE / "base_acad.dxf"      # EXPORTTOAUTOCAD copy, converted to DXF
SRC_POINTS = SOURCE / "points.txt"      # PNEZD export of the COGO points

# Model-space window kept for the training site (drops the long storm corridor
# and the far-off control monuments).
WINDOW = (1124820.0, 733780.0, 1125440.0, 734120.0)

SCALE = 20                       # 1" = 20'
TXT = 0.08 * SCALE               # 1.6' - matches the base's Survey text
DROP_LAYERS = {"G-BORD-XLIT", "G-BORD-TEXT-HLIN", "V-TEXT"}   # base's own sheet notes

# Complex linetypes rebuilt without shape files. Patterns are plotted inches,
# used with LTSCALE = 20 and PSLTSCALE = 0.
TEXT_LT = {
    "STORM": ("SD", 1.30),
    "EOPA": ("/", 1.00),
    "FENCELINK": ("o", 0.20),
    "ROCK_WALL": ("^", 0.15),
}


def load_level(n):
    """Import levelN/build_drawings.py under a unique module name."""
    import importlib.util
    name = f"level{n}_build"
    if name in __import__("sys").modules:
        return __import__("sys").modules[name]
    spec = importlib.util.spec_from_file_location(name, ROOT / f"level{n}" / "build_drawings.py")
    mod = importlib.util.module_from_spec(spec)
    __import__("sys").modules[name] = mod
    spec.loader.exec_module(mod)
    return mod


def read_source():
    return ezdxf.readfile(SRC_DXF)


def read_points():
    """PNEZD rows as dicts; skips blank placeholder rows."""
    pts = []
    for line in SRC_POINTS.read_text(encoding="utf-8", errors="replace").splitlines():
        f = line.split(",")
        if len(f) < 5 or not f[0].strip():
            continue
        pts.append({"p": int(f[0]), "n": float(f[1]), "e": float(f[2]), "z": float(f[3]),
                    "d": f[4].strip(), "note": ",".join(f[5:]).strip()})
    return pts


def in_window(e, win=WINDOW):
    b = bbox.extents([e])
    if not b.has_data:
        return False
    x1, y1, x2, y2 = win
    return not (b.extmax.x < x1 or b.extmin.x > x2 or b.extmax.y < y1 or b.extmin.y > y2)


# ---------------------------------------------------------------- resources
def _pattern(lt):
    tags = lt.pattern_tags.tags if lt.pattern_tags else []
    return [t.value for t in tags if t.code == 49]


def copy_linetype(src, dst, name):
    if name in dst.linetypes or name.upper() in ("BYLAYER", "BYBLOCK", "CONTINUOUS"):
        return
    lt = src.linetypes.get(name)
    if name in TEXT_LT:
        label, dash = TEXT_LT[name]
        w = 0.05 * len(label)
        pat = (f'A,{dash},-0.05,["{label}",STANDARD,S=.06,U=0.0,X=-0.03,Y=-.03],-{w + 0.03:.3f}')
        dst.linetypes.add(name, pattern=pat, length=dash + 0.08 + w,
                          description=lt.dxf.description if lt else name)
        return
    pat = _pattern(lt) if lt else []
    if not pat:
        dst.linetypes.add(name, pattern=[0.0], description="Continuous")
        return
    dst.linetypes.add(name, pattern=[sum(abs(v) for v in pat)] + pat,
                      description=lt.dxf.description)


def copy_layer(src, dst, name):
    if name in dst.layers:
        return
    s = src.layers.get(name) if name in src.layers else None
    lt = s.dxf.linetype if s else "Continuous"
    copy_linetype(src, dst, lt)
    lay = dst.layers.add(name, color=(abs(s.dxf.color) if s else 7) or 7, linetype=lt)
    if s:
        lay.dxf.lineweight = s.dxf.get("lineweight", -3)
        lay.dxf.plot = s.dxf.get("plot", 1)
        if not s.is_on():
            lay.off()
        if s.is_frozen():
            lay.freeze()


def setup_styles(dst):
    dst.styles.new("Survey", dxfattribs={"font": "romans.shx", "width": 0.75})
    dst.styles.get("Standard").dxf.font = "romans.shx"
    dst.header["$TEXTSTYLE"] = "Survey"
    dst.header["$TEXTSIZE"] = TXT


def common_header(dst):
    h = dst.header
    h["$INSUNITS"] = 2
    h["$MEASUREMENT"] = 0
    h["$LUNITS"] = 2
    h["$LUPREC"] = 2
    h["$AUNITS"] = 4
    h["$AUPREC"] = 4
    h["$ANGBASE"] = 0.0
    h["$ANGDIR"] = 0
    h["$LTSCALE"] = float(SCALE)
    h["$PSLTSCALE"] = 0
    h["$PDMODE"] = 0


# ---------------------------------------------------------------- entities
def _attribs(e, layer=None, dst_layers=None):
    a = {"layer": layer or e.dxf.layer}
    for k in ("color", "linetype", "lineweight", "ltscale"):
        if e.dxf.hasattr(k):
            a[k] = e.dxf.get(k)
    return a


def rebuild_entity(e, layout, src, dst, layer=None):
    """Recreate one entity in `layout`. Returns the new entity or None."""
    t = e.dxftype()
    lay = layer or e.dxf.layer
    if lay == "0" and layer:
        lay = layer
    copy_layer(src, dst, lay)
    a = _attribs(e, lay)
    if "linetype" in a:
        copy_linetype(src, dst, a["linetype"])
    if t == "LINE":
        return layout.add_line(e.dxf.start, e.dxf.end, dxfattribs=a)
    if t == "ARC":
        return layout.add_arc(e.dxf.center, e.dxf.radius, e.dxf.start_angle, e.dxf.end_angle, dxfattribs=a)
    if t == "CIRCLE":
        return layout.add_circle(e.dxf.center, e.dxf.radius, dxfattribs=a)
    if t == "LWPOLYLINE":
        a["elevation"] = e.dxf.get("elevation", 0.0)
        if e.dxf.hasattr("const_width"):
            a["const_width"] = e.dxf.const_width
        return layout.add_lwpolyline(list(e.get_points("xyseb")), format="xyseb", close=e.closed, dxfattribs=a)
    if t == "POLYLINE" and e.is_3d_polyline:
        p = layout.add_polyline3d([v.dxf.location for v in e.vertices], dxfattribs=a)
        if e.is_closed:
            p.close(True)
        return p
    if t == "SPLINE":
        s = layout.add_spline(dxfattribs=a)
        s.dxf.degree = e.dxf.degree
        if len(e.control_points):
            s.control_points = list(e.control_points)
            s.knots = list(e.knots)
            if len(e.weights):
                s.weights = list(e.weights)
        elif len(e.fit_points):
            s.fit_points = list(e.fit_points)
        else:
            layout.delete_entity(s)
            return None
        return s
    if t == "SOLID":
        return layout.add_solid([e.dxf.vtx0, e.dxf.vtx1, e.dxf.vtx2, e.dxf.vtx3], dxfattribs=a)
    if t == "TEXT":
        a.update(style="Survey", height=e.dxf.height, rotation=e.dxf.get("rotation", 0),
                 halign=e.dxf.get("halign", 0), valign=e.dxf.get("valign", 0),
                 insert=e.dxf.insert, align_point=e.dxf.get("align_point", e.dxf.insert),
                 width=e.dxf.get("width", 1.0))
        return layout.add_text(e.dxf.text, dxfattribs=a)
    if t == "MTEXT":
        a.update(style="Survey", char_height=e.dxf.char_height, insert=e.dxf.insert,
                 attachment_point=e.dxf.get("attachment_point", 1), width=e.dxf.get("width", 0),
                 rotation=e.get_rotation())
        m = layout.add_mtext(e.text, dxfattribs=a)
        if e.dxf.get("bg_fill", 0):
            m.set_bg_color(None, scale=1.5, text_frame=False) if False else m.set_bg_color("canvas", scale=1.3)
        return m
    if t == "HATCH":
        h = layout.add_hatch(color=a.get("color", 256), dxfattribs={"layer": lay})
        h.set_solid_fill(color=a.get("color", 256))
        for p in e.paths:
            if type(p).__name__ == "PolylinePath":
                h.paths.add_polyline_path([(v[0], v[1], v[2]) for v in p.vertices], is_closed=p.is_closed)
            else:
                ep = h.paths.add_edge_path()
                for ed in p.edges:
                    n = type(ed).__name__
                    if n == "LineEdge":
                        ep.add_line(ed.start, ed.end)
                    elif n == "ArcEdge":
                        ep.add_arc(ed.center, ed.radius, ed.start_angle, ed.end_angle, ed.ccw)
        return h
    if t == "WIPEOUT":
        pts = [Vec3(v) for v in e.boundary_path_wcs()]
        return layout.add_wipeout([(p.x, p.y) for p in pts], dxfattribs={"layer": lay})
    if t == "INSERT":
        name = e.dxf.name
        copy_block(src, dst, name)
        return layout.add_blockref(name, e.dxf.insert, dxfattribs={
            **a, "rotation": e.dxf.get("rotation", 0), "xscale": e.dxf.get("xscale", 1),
            "yscale": e.dxf.get("yscale", 1), "zscale": e.dxf.get("zscale", 1)})
    return None


def copy_block(src, dst, name):
    if name in dst.blocks:
        return
    sb = src.blocks.get(name)
    db = dst.blocks.new(name, base_point=sb.block.dxf.base_point)
    for e in sb:
        rebuild_entity(e, db, src, dst)


def rebuild_mleader(e, layout, src, dst, style="SRV-20"):
    """Recreate a text multileader. The dogleg vector points from the landing
    toward the text, so a leader whose dogleg points -X attaches to the text's
    right side (text sits left of the landing)."""
    c = e.context
    if not c.mtext or not c.leaders:
        return None
    copy_layer(src, dst, e.dxf.layer)
    ml = layout.add_multileader_mtext(style, dxfattribs={"layer": e.dxf.layer})
    text_left_of_landing = c.leaders[0].dogleg_vector.x < 0
    ml.set_content(c.mtext.default_content, char_height=c.char_height,
                   alignment=mleader.TextAlignment.right if text_left_of_landing else mleader.TextAlignment.left)
    for ld in c.leaders:
        side = mleader.ConnectionSide.right if ld.dogleg_vector.x < 0 else mleader.ConnectionSide.left
        ml.add_leader_line(side, [v for line in ld.lines for v in line.vertices])
    ml.build(insert=Vec2(c.mtext.insert))
    # The base's leaders don't all share one gap/dogleg, so copy them exactly.
    new = ml.multileader.context
    new.landing_gap_size = c.landing_gap_size
    for nl, sl in zip(new.leaders, c.leaders):
        nl.last_leader_point = Vec3(sl.last_leader_point)
        nl.dogleg_length = sl.dogleg_length
        nl.dogleg_vector = Vec3(sl.dogleg_vector)
        for nline, sline in zip(nl.lines, sl.lines):
            nline.vertices = [Vec3(v) for v in sline.vertices]
    new.mtext.insert = Vec3(c.mtext.insert)
    new.mtext.width = c.mtext.width
    new.arrow_head_size = c.arrow_head_size
    new.scale = c.scale
    for k in ("leader_line_color", "text_color", "arrow_head_size"):
        if e.dxf.hasattr(k):
            ml.multileader.dxf.set(k, e.dxf.get(k))
    new.mtext.alignment = c.mtext.alignment
    ml.multileader.dxf.text_attachment_point = e.dxf.get("text_attachment_point", 1)
    return ml.multileader


def make_mleader_style(dst, name="SRV-20"):
    if name in dst.mleader_styles:
        return
    s = dst.mleader_styles.duplicate_entry("Standard", name)
    s.dxf.char_height = TXT
    s.dxf.arrow_head_size = TXT
    s.dxf.landing_gap_size = 1.8                  # matches the base leaders
    s.dxf.dogleg_length = 1.2
    s.dxf.text_style_handle = dst.styles.get("Survey").dxf.handle


def make_dimstyle(dst, name="SRV-20"):
    if name in dst.dimstyles:
        return
    ds = dst.dimstyles.new(name)
    for k, v in dict(dimscale=SCALE, dimtxt=0.08, dimasz=0.08, dimexo=0.05, dimexe=0.06,
                     dimgap=0.03, dimtad=1, dimtih=0, dimtoh=0, dimdec=2, dimlunit=2, dimzin=0,
                     dimtxsty="Survey", dimpost="<>'", dimtofl=1, dimatfit=3).items():
        ds.set_dxf_attrib(k, v)
    dst.header["$DIMSTYLE"] = name


# Point-symbol blocks are drawn in plotted inches (a CB is 0.08" x 0.10") and
# Civil 3D scales them by the annotation scale. The DWG conversion garbles the
# nested scale (values like 4.9e-15), so every point symbol is set to the sheet
# scale. Tree symbols are drawn to canopy size and keep their exported scale.
TREE_BLOCKS = {"SSV-CON", "SSV-DEC"}


def fix_symbol_scale(ins):
    if ins.dxf.name.startswith("SSV-") and ins.dxf.name not in TREE_BLOCKS:
        ins.dxf.xscale = ins.dxf.yscale = ins.dxf.zscale = float(SCALE)


def rebuild_base(win=WINDOW):
    """Return (dst_doc, src_doc) with the windowed model space rebuilt."""
    src = read_source()
    dst = ezdxf.new("R2018", setup=False)
    common_header(dst)
    setup_styles(dst)
    make_mleader_style(dst)
    make_dimstyle(dst)
    msp = dst.modelspace()
    for e in src.modelspace():
        if e.dxf.layer in DROP_LAYERS or not in_window(e, win):
            continue
        t = e.dxftype()
        if t == "MULTILEADER":
            rebuild_mleader(e, msp, src, dst)
        elif t == "INSERT" and e.dxf.name.startswith("*"):
            for v in e.virtual_entities():
                n = rebuild_entity(v, msp, src, dst, layer=e.dxf.layer if v.dxf.layer == "0" else v.dxf.layer)
                if n is not None and n.dxftype() == "INSERT":
                    fix_symbol_scale(n)
        else:
            rebuild_entity(e, msp, src, dst)
    return dst, src
