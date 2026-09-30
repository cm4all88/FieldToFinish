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
    m = A["monuments"]
    j = A["jumpers"]
    return {
        1: ("SETUP AND RECORD BOUNDARY", [
            "Units: surveyor's bearings, 0.00 ft",
            f"POB (SW corner): E {f2(A['pob'][0])}, N {f2(A['pob'][1])}",
            *[f"{c['from']}-{c['to']}: {c['bearing']}  {f2(c['dist'])}'" for c in A["deed"]],
            "Closure: last course ends on the POB",
            f"Area {f2(A['area_sf'])} sq ft ({A['area_sf'] / 43560:.4f} ac)",
            f"Perimeter {f2(A['perimeter'])}'",
            "Record xref: right of way, centerline, surface border"]),
        2: ("FIELD TO FINISH", [
            f"Point file: delete bad line {A['junk_lines'][0]} (Z = -99999)",
            f"{A['points']} points imported",
            *[f"Group {k}: {v}" for k, v in A["groups"].items()],
            *[f"Fix {k}: {v[0]} -> {v[1]}" for k, v in A["fixes"].items()],
            f"Figures after fixes: {A['figures_fixed']}",
            *[f"FIP {k} is {v['off']}' from the {v['corner']} corner" for k, v in m.items()],
            "Jumpers still showing (fixed in Stage 3)"]),
        3: ("LINEWORK CLEANUP", [
            *[f"Jumper {x['figure']} {x['from']}-{x['to']} ({x['length']}') removed" for x in j],
            "ASPH figure deleted (spot shots, not a line)",
            "3 CONC pads drafted", f"{len(A['ties'])} curb-end ties drafted",
            f"Figures: {A['figures']}",
            f"Single-point strings left: {', '.join(A['single'])} (OK)"]),
        4: ("SURFACE", [
            f"GROUND point group: {A['ground']} points",
            f"Inside the border: {A['inside']}",
            f"Elevations {A['z_min']} to {A['z_max']}",
            "Contours: 1' minor, 5' major, 45 to 91",
            "Point 10421 (stump shot on top) kept out",
            "Off-site control 1001/1002 kept out"]),
        5: ("ANNOTATION AND UTILITIES", [
            "Street names read right side up",
            "Structure leaders: RIM, inverts, bottom",
            f"FFE {', '.join(str(v) for v in A['ffe'].values())}",
            f"Setbacks: garage {A['l1']['dim_garage']}', house {A['l1']['dim_house']}'",
            f"North line label: {A['l1']['north_bearing']} {A['l1']['north_dist']}'",
            "Trees, monuments and control labeled"]),
        6: ("SHEET AND DELIVERY", [
            "Viewport 1\" = 20', locked",
            f"North line measures {A['l1']['north_dist'] / 20:.2f}\" on paper",
            "No objects on layer 0; one text style",
            "PURGE and AUDIT clean",
            "Plotted with monochrome.ctb",
            "Exported ACAD copy opens in plain AutoCAD"]),
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
    for n in (1, 2, 3, 4, 5):
        d = ezdxf.readfile(OUT / f"_state{n}.dxf")
        if "TRAIN-NOTES" in d.layers:
            d.layers.get("TRAIN-NOTES").freeze()
        page(n, d, d.modelspace(), WINDOW)
    d = ezdxf.readfile(OUT / "SURVEY_CAD_COMPLETED.dxf")
    page(6, d, d.layouts.get("TOPO SHEET"), None)


if __name__ == "__main__":
    main()
