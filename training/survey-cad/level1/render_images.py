"""Render the Level 1 images used in the guide."""
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "lib"))
import ezdxf  # noqa: E402
import render  # noqa: E402

OUT = HERE / "output"
IMG = OUT / "images"
WINDOW = ((1124870, 733840), (1125340, 734090))

# exercise -> world box (x1, y1, x2, y2)
CROPS = {
    "02": (1125215, 733915, 1125295, 733965),
    "03": (1125195, 733945, 1125250, 733980),
    "05": (1125050, 733890, 1125150, 733965),
    "06": (1124962, 733866, 1125002, 733894),
    "07": (1125140, 733975, 1125255, 734030),
    "09": (1125183, 733938, 1125228, 733966),
    "10": (1125230, 733955, 1125295, 734005),
    "11": (1125184, 733918, 1125210, 733938),
    "12": (1125256, 734004, 1125286, 734026),
    "13": (1125135, 733950, 1125200, 733992),
    "14": (1124955, 734042, 1124998, 734070),
    "15": (1125060, 733985, 1125165, 734015),
    "16": (1125255, 733862, 1125335, 733955),
    "17": (1125155, 733912, 1125208, 733966),
    "18": (1124955, 734040, 1125045, 734090),
    "19": (1125145, 733858, 1125262, 733972),
}


def main():
    IMG.mkdir(parents=True, exist_ok=True)
    S = ezdxf.readfile(OUT / "SURVEY_CAD_L1_START.dxf")
    C = ezdxf.readfile(OUT / "SURVEY_CAD_L1_COMPLETED.dxf")
    for d in (S, C):          # contours are frozen during most exercises; show them anyway
        for n in ("V-TOPO-CONT-MAJR-E", "V-TOPO-CONT-MINR-E"):
            if n in d.layers:
                d.layers.get(n).thaw()
    render.draw(S, S.modelspace(), IMG / "start_model.png", WINDOW, width_in=16, dpi=130)
    render.crops(S, WINDOW, CROPS, IMG / "ex{name}_before.png", width_in=18, dpi=260)
    C.layers.get("TRAIN-NOTES").thaw()
    render.crops(C, WINDOW, CROPS, IMG / "ex{name}_after.png", width_in=18, dpi=260)
    C2 = ezdxf.readfile(OUT / "SURVEY_CAD_L1_COMPLETED.dxf")
    render.draw(C2, C2.layouts.get("TOPO SHEET"), IMG / "completed_sheet.png", size=(17, 11), dpi=120, paper=True)


if __name__ == "__main__":
    main()
