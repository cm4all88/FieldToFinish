"""Render drawings to PNG for the guides.

Model space renders on AutoCAD's dark background; sheets render as a
monochrome plot. Close-ups are cropped out of one high-resolution render so
every crop shares the same scale.
"""
import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402
from ezdxf.addons.drawing import Frontend, RenderContext  # noqa: E402
from ezdxf.addons.drawing.config import (BackgroundPolicy, ColorPolicy, Configuration,  # noqa: E402
                                         LineweightPolicy)
from ezdxf.addons.drawing.matplotlib import MatplotlibBackend  # noqa: E402
from PIL import Image  # noqa: E402

CAD_BG = "#212830"
Image.MAX_IMAGE_PIXELS = None


def draw(doc, layout, path, window=None, width_in=12.0, dpi=150, paper=False, size=None):
    if window:
        (x1, y1), (x2, y2) = window
        size = size or (width_in, width_in * (y2 - y1) / (x2 - x1))
    size = size or (width_in, width_in * 0.65)
    fig = plt.figure(figsize=size, dpi=dpi)
    ax = fig.add_axes([0, 0, 1, 1])
    ctx = RenderContext(doc)
    ctx.set_current_layout(layout)
    if paper:
        # masks/wipeouts preview as black boxes in monochrome; AutoCAD plots them white
        for e in list(doc.entitydb.values()):
            if e.dxftype() == "WIPEOUT":
                e.dxf.flags = 0
            elif e.dxftype() == "MTEXT" and e.dxf.get("bg_fill", 0):
                e.dxf.bg_fill = 0
        cfg = Configuration(background_policy=BackgroundPolicy.WHITE, color_policy=ColorPolicy.BLACK,
                            lineweight_policy=LineweightPolicy.ABSOLUTE, lineweight_scaling=1.0)
        bg = "white"
    else:
        cfg = Configuration(background_policy=BackgroundPolicy.CUSTOM, custom_bg_color=CAD_BG,
                            lineweight_policy=LineweightPolicy.RELATIVE, lineweight_scaling=0.6)
        bg = CAD_BG
    Frontend(ctx, MatplotlibBackend(ax), config=cfg).draw_layout(layout, finalize=True)
    fig.set_size_inches(size)           # finalize() shrinks the figure; put it back
    if window:
        (x1, y1), (x2, y2) = window
        ax.set_xlim(x1, x2)
        ax.set_ylim(y1, y2)
    fig.savefig(path, facecolor=bg, dpi=dpi)
    to_px = ax.transData.transform
    k = dpi / fig.dpi
    height = fig.bbox.height
    plt.close(fig)
    return lambda x, y: (k * to_px((x, y))[0], k * (height - to_px((x, y))[1]))


def crops(doc, window, crops_dict, out_pattern, width_in=16.0, dpi=250, big_path=None):
    """Render `window` once and cut each crop (world boxes) out of it."""
    big_path = big_path or str(out_pattern).replace("{name}", "_big")
    px = draw(doc, doc.modelspace(), big_path, window, width_in=width_in, dpi=dpi)
    im = Image.open(big_path)
    for name, (x1, y1, x2, y2) in crops_dict.items():
        (l, t), (r, b) = px(x1, y2), px(x2, y1)
        c = im.crop((round(l), round(t), round(r), round(b)))
        c.thumbnail((1100, 1100))
        c.save(str(out_pattern).replace("{name}", name))
    im.close()
    import os
    os.remove(big_path)
