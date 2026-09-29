"""Render the Level 3 images: raw vs fixed figures at the two bad codes, and the result."""
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "lib"))
import ezdxf  # noqa: E402
import base  # noqa: E402
import ftf  # noqa: E402
import render  # noqa: E402

L3 = base.load_level(3)
OUT = HERE / "output"
IMG = OUT / "images"
WINDOW = ((1124870, 733840), (1125340, 734090))
CROPS = {
    "06a": (1125125, 733975, 1125205, 734005),     # 10180 RWB1/RWB2
    "06b": (1125185, 733940, 1125215, 733965),     # 15235 RWC1
    "07": (1125140, 733870, 1125240, 734010),      # jumpers from office points
}


def main():
    IMG.mkdir(parents=True, exist_ok=True)
    pts = base.read_points()
    raw, _ = ftf.build_figures(pts)
    before = L3.build_completed(pts, raw)
    for e in list(before.modelspace()):          # before = no hand-drafted pads/ties
        if e.dxftype() in ("LINE", "LWPOLYLINE") and e.dxf.layer in ("V-SURF-CONC-E", "V-SURF-CURB-E"):
            before.modelspace().delete_entity(e)
    after = ezdxf.readfile(OUT / "SURVEY_CAD_L3_COMPLETED.dxf")
    for d in (before,):
        d.layers.get("V-NODE-E").off()
    render.crops(before, WINDOW, CROPS, IMG / "ex{name}_before.png", width_in=18, dpi=260)
    after.layers.get("V-NODE-E").off()
    render.crops(after, WINDOW, CROPS, IMG / "ex{name}_after.png", width_in=18, dpi=260)
    render.draw(after, after.modelspace(), IMG / "figures.png", WINDOW, width_in=16, dpi=120)


if __name__ == "__main__":
    main()
