"""Render the Level 2 images used in the guide."""
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "lib"))
import ezdxf  # noqa: E402
import render  # noqa: E402
import base  # noqa: E402

L2 = base.load_level(2)

OUT = HERE / "output"
IMG = OUT / "images"
WINDOW = ((1124870, 733840), (1125340, 734090))
import json  # noqa: E402

ANS = json.loads((OUT / "answers.json").read_text())


def around(xy, half_w, half_h):
    x, y = xy
    return (x - half_w, y - half_h, x + half_w, y + half_h)


CROPS = {
    "03": around(ANS["crop_layer0"], 30, 20),      # contour labels on layer 0
    "04": around(ANS["crop_style"], 25, 15),       # labels in the wrong style
    "11": (1125110, 733935, 1125150, 733965),      # control block replaces the symbol
}


def sheet_preview(base_file, out):
    """Composite for the image only: the sheet's title block and viewports drawn
    over the base itself (the renderer can't load xrefs)."""
    doc = ezdxf.readfile(base_file)
    pts = {p["p"]: p for p in L2.base.read_points()}
    lay = doc.layouts.new("SHEET")
    lay.page_setup(size=L2.SHEET, margins=(0, 0, 0, 0), units="inch")
    L2.title_block(doc, lay, True)
    for (x1, y1, x2, y2), scale, center in ((L2.VP_OVERALL, 40, L2.L1.VIEW_CENTER), (L2.VP_DETAIL, 10, L2.HOUSE_CENTER)):
        w, h = x2 - x1, y2 - y1
        lay.add_viewport(center=((x1 + x2) / 2, (y1 + y2) / 2), size=(w, h), view_center_point=center,
                         view_height=h * scale)
    L2.control_table(doc, lay, pts)
    render.draw(doc, lay, out, size=(17, 11), dpi=120, paper=True)


def main():
    IMG.mkdir(parents=True, exist_ok=True)
    S = ezdxf.readfile(OUT / f"{L2.BASE_NAME}_START.dxf")
    C = ezdxf.readfile(OUT / f"{L2.BASE_NAME}_COMPLETED.dxf")
    render.crops(S, WINDOW, CROPS, IMG / "ex{name}_before.png", width_in=18, dpi=260)
    render.crops(C, WINDOW, CROPS, IMG / "ex{name}_after.png", width_in=18, dpi=260)
    sheet_preview(OUT / f"{L2.BASE_NAME}_COMPLETED.dxf", IMG / "completed_sheet.png")
    ss = ezdxf.readfile(OUT / "SURVEY_CAD_L2_SHEET_START.dxf")
    render.draw(ss, ss.layouts.get("SHEET"), IMG / "start_sheet.png", size=(17, 11), dpi=90, paper=True)


if __name__ == "__main__":
    main()
