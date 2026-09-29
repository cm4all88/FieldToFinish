"""Render the START/COMPLETED drawings to PNGs used in the guide.

Full views of model space and the sheet, plus before/after crops for each
exercise. Crops are defined in the lot frame (see build_drawings.T).
"""
import sys
from pathlib import Path

import ezdxf
import matplotlib
from PIL import Image

matplotlib.use("Agg")
import matplotlib.pyplot as plt
from ezdxf.addons.drawing import Frontend, RenderContext
from ezdxf.addons.drawing.config import BackgroundPolicy, ColorPolicy, Configuration, LineweightPolicy
from ezdxf.addons.drawing.matplotlib import MatplotlibBackend

from build_drawings import OUT, T

IMG = OUT / "images"

# exercise -> (x1, y1, x2, y2) in the lot frame
CROPS = {
    "02": (40, -16, 120, 16),
    "06": (-15, 105, 115, 160),
    "08": (-30, 45, 40, 150),
    "09": (60, -20, 180, 15),
    "10": (84, 124, 114, 150),
    "11": (15, 45, 85, 90),
    "12": (5, -18, 75, 28),
    "13": (60, 80, 120, 125),
    "14": (65, -32, 120, 2),
    "16": (-30, -34, 55, -8),
    "17": (10, 12, 95, 72),
    "18": (65, -30, 150, 32),
    "19": (-5, -10, 110, 50),
}


CAD_BG = "#212830"   # AutoCAD's default dark model-space background


def draw(doc, layout, path, window=None, size=(12, 8), dpi=150, freeze=(), paper=False):
    for name in freeze:
        if name in doc.layers:
            doc.layers.get(name).freeze()
    fig = plt.figure(figsize=size, dpi=dpi)
    ax = fig.add_axes([0, 0, 1, 1])
    ctx = RenderContext(doc)
    ctx.set_current_layout(layout)
    if paper:  # as plotted with monochrome.ctb
        cfg = Configuration(background_policy=BackgroundPolicy.WHITE, color_policy=ColorPolicy.BLACK,
                            lineweight_policy=LineweightPolicy.ABSOLUTE, lineweight_scaling=1.0)
        bg = "white"
    else:
        cfg = Configuration(background_policy=BackgroundPolicy.CUSTOM, custom_bg_color=CAD_BG,
                            lineweight_policy=LineweightPolicy.RELATIVE, lineweight_scaling=0.6)
        bg = CAD_BG
    Frontend(ctx, MatplotlibBackend(ax), config=cfg).draw_layout(layout, finalize=True)
    fig.set_size_inches(size)       # finalize() shrinks the figure; put it back
    if window:
        (x1, y1), (x2, y2) = window
        ax.set_xlim(x1, x2)
        ax.set_ylim(y1, y2)
    fig.savefig(path, facecolor=bg, dpi=dpi)
    to_px = ax.transData.transform          # world -> display pixels (origin bottom-left)
    k = dpi / fig.dpi                       # display pixels -> saved-image pixels
    height = fig.bbox.height
    plt.close(fig)
    return lambda x, y: (k * to_px((x, y))[0], k * (height - to_px((x, y))[1]))


def crop_window(box):
    x1, y1, x2, y2 = box
    pts = [T(x1, y1), T(x2, y1), T(x2, y2), T(x1, y2)]
    xs, ys = [p.x for p in pts], [p.y for p in pts]
    return (min(xs), min(ys)), (max(xs), max(ys))


def main():
    IMG.mkdir(parents=True, exist_ok=True)
    start = ezdxf.readfile(OUT / "Survey_CAD_Level1_START.dxf")
    done = ezdxf.readfile(OUT / "Survey_CAD_Level1_COMPLETED.dxf")
    # show the points in START renders too (they are frozen in the file itself)
    start.layers.get("V-NODE").thaw()

    full = crop_window((-75, -82, 180, 160))
    draw(start, start.layouts.get("TOPO SHEET"), IMG / "start_sheet.png", size=(12.75, 8.25), paper=True)
    draw(done, done.layouts.get("TOPO SHEET"), IMG / "completed_sheet.png", size=(12.75, 8.25), paper=True)
    for doc, tag in ((start, "before"), (done, "after")):
        name = "start" if tag == "before" else "completed"
        draw(doc, doc.modelspace(), IMG / f"{name}_model.png", full, size=(13, 12.3))
        big = OUT / f"_{name}_big.png"
        px = draw(doc, doc.modelspace(), big, full, size=(13, 12.3), dpi=300)
        im = Image.open(big)
        for ex, box in CROPS.items():
            (gx1, gy1), (gx2, gy2) = crop_window(box)
            (l, t), (r, b) = px(gx1, gy2), px(gx2, gy1)
            c = im.crop((round(l), round(t), round(r), round(b)))
            c.thumbnail((1100, 1100))
            c.save(IMG / f"ex{ex}_{tag}.png")
        big.unlink()


if __name__ == "__main__":
    main()
