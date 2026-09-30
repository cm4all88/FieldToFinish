"""Checkpoint PDFs: a vector plot of the expected drawing state plus the values to check."""
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "lib"))

import ezdxf  # noqa: E402
import matplotlib  # noqa: E402

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402
from ezdxf.addons.drawing import Frontend, RenderContext  # noqa: E402
from ezdxf.addons.drawing.config import (BackgroundPolicy, ColorPolicy, Configuration,  # noqa: E402
                                         LineweightPolicy)
from ezdxf.addons.drawing.matplotlib import MatplotlibBackend  # noqa: E402

OUT = HERE / "output"
IMG = OUT / "images"
A = json.loads((OUT / "answers.json").read_text())
WINDOW = ((1124880, 733835), (1125345, 734075))


def f2(v):
    return f"{v:,.2f}"


def checks():
    j = A["jumpers"]
    L = A["labels"]
    return {
        1: ("BOUNDARY", [
            "SW corner closed with FILLET R0",
            f"North line drawn: {A['deed'][1]['bearing']}  {f2(A['deed'][1]['dist'])}'",
            "East line extended to the north line",
            "South line trimmed at the east line",
            "Boundary joined into one closed polyline",
            f"Area {f2(A['area_sf'])} sq ft ({A['area_sf'] / 43560:.4f} ac)",
            f"Perimeter {f2(A['perimeter'])}'"]),
        2: ("LINEWORK CLEANUP", [
            *[f"Jumper {x['figure']} {x['from']}-{x['to']} ({x['length']}') erased" for x in j],
            "ASPH zig-zag erased",
            "3 wall pads drafted (V-SURF-WALL-E)",
            f"{len(A['ties'])} curb-end ties drafted",
            "Chain link fence moved off layer 0",
            "Red curb line back to ByLayer"]),
        3: ("STORM AND SEWER", [
            "Catch basins rotated square to the curb",
            "Storm pipes drawn structure to structure",
            f"{len(A['structures'])} CB leaders: RIM, inverts, bottom",
            "SSMH and cleanout leaders with RIM"]),
        4: ("LABELS AND DIMENSIONS", [
            "Road names rotated with the street",
            f"{len(L['control'])} control labels (PMX #, type)",
            f"{len(L['monuments'])} monument labels",
            f"{len(A['trees'])} tree labels (size, type, drip)",
            "Surface callouts: ASPH, CONC, CW, CG, GRASS...",
            f"FFE {', '.join(str(v) for v in A['ffe'].values())}",
            "Boundary bearings and distances",
            f"Setbacks {A['dim_garage']}' and {A['dim_house']}'"]),
        5: ("SHEET AND PLOT", [
            "Viewport 1\" = 20', locked",
            f"North line measures {A['deed'][1]['dist'] / 20:.2f}\" on paper",
            "Point layer frozen",
            "Nothing on layer 0",
            "Plotted with monochrome.ctb"]),
    }


def page(n, doc, layout, window):
    title, items = checks()[n]
    fig = plt.figure(figsize=(17, 11))
    ax = fig.add_axes([0.01, 0.01, 0.74, 0.98] if window else [0.0, 0.0, 0.76, 1.0])
    cfg = Configuration(background_policy=BackgroundPolicy.WHITE, color_policy=ColorPolicy.BLACK,
                        lineweight_policy=LineweightPolicy.RELATIVE, lineweight_scaling=0.5)
    for e in doc.entitydb.values():          # masks preview as black boxes in monochrome
        if e.dxftype() == "WIPEOUT":
            e.dxf.flags = 0
        elif e.dxftype() == "MTEXT" and e.dxf.get("bg_fill", 0):
            e.dxf.bg_fill = 0
    ctx = RenderContext(doc)
    ctx.set_current_layout(layout)
    Frontend(ctx, MatplotlibBackend(ax), config=cfg).draw_layout(layout, finalize=True)
    fig.set_size_inches(17, 11)
    if window:
        (x1, y1), (x2, y2) = window
        ax.set_xlim(x1, x2)
        ax.set_ylim(y1, y2)
    x0 = 0.775
    fig.add_artist(plt.Line2D([0.765, 0.765], [0.02, 0.98], color="black", lw=1))
    fig.text(x0, 0.95, "SURVEY CAD TRAINING", fontsize=11, weight="bold")
    fig.text(x0, 0.915, f"CHECKPOINT {n}", fontsize=22, weight="bold", color="#1F4E79")
    fig.text(x0, 0.885, title, fontsize=13)
    fig.text(x0, 0.84, "Your drawing should match this. Check:", fontsize=10, style="italic")
    y = 0.80
    for it in items:
        fig.text(x0, y, "☐  " + it, fontsize=10, wrap=True)
        y -= 0.034
    fig.text(x0, 0.03, "Zoom in: this PDF is vector.", fontsize=8, color="gray")
    pdf = OUT / f"CHECKPOINT_{n}.pdf"
    fig.savefig(pdf)
    fig.savefig(IMG / f"checkpoint_{n}.png", dpi=70)
    plt.close(fig)


def main():
    IMG.mkdir(parents=True, exist_ok=True)
    for n in (1, 2, 3, 4):
        d = ezdxf.readfile(OUT / f"_state{n}.dxf")
        for lay in ("TRAIN-NOTES", "V-NODE-E"):
            if lay in d.layers:
                d.layers.get(lay).off()
        page(n, d, d.modelspace(), WINDOW)
    d = ezdxf.readfile(OUT / "SURVEY_CAD_COMPLETED.dxf")
    page(5, d, d.layouts.get("TOPO SHEET"), None)


if __name__ == "__main__":
    main()
