"""Field-to-finish figure builder for the training answer keys.

Implements the office linework rules recorded in config/rules.json
(PMX-Universal.f2f_xdef): descriptions are space delimited; a linework code
(from lineworkCodes, with an optional trailing number that makes it a separate
string) starts or continues a figure; control codes after it act on that
figure: B begin, E end, C close, P begin curve, T end curve. Auto-begin is on.
Points are processed in point-number order, the way Civil 3D does.

Curves (P ... T) are kept as straight vertices here. The answer key uses the
figures for counts and vertex lists, not arc geometry.
"""
import json
import re
from pathlib import Path

RULES = Path(__file__).resolve().parents[3] / "config" / "rules.json"
CONTROLS = {"B", "E", "C", "P", "T", "U"}


def load_rules():
    d = json.loads(RULES.read_text(encoding="utf-8"))
    return set(d["lineworkCodes"]), set(d["ignoreCodes"])


def split_code(token):
    m = re.fullmatch(r"([A-Z]+)(\d*)", token)
    return (m.group(1), m.group(2)) if m else (None, None)


def build_figures(points):
    """points: dicts with p, n, e, z, d. Returns (figures, report)."""
    lw, ignore = load_rules()
    open_figs = {}          # figure name -> list of vertices
    done = []               # (name, vertices, closed)
    report = {"linework_points": 0, "unknown_codes": {}, "curve_points": 0}
    for p in sorted(points, key=lambda q: q["p"]):
        toks = p["d"].upper().split()
        i = 0
        used = False
        while i < len(toks):
            base, num = split_code(toks[i])
            i += 1
            if base is None or base not in lw:
                if base and base not in ignore and base not in lw and not toks[i - 1].replace(".", "").isdigit():
                    report["unknown_codes"][base] = report["unknown_codes"].get(base, 0) + 1
                continue
            name = base + num
            ctrls = []
            while i < len(toks) and toks[i] in CONTROLS:
                ctrls.append(toks[i])
                i += 1
            vtx = (p["e"], p["n"], p["z"], p["p"])
            if "B" in ctrls and name in open_figs:
                done.append((name, open_figs.pop(name), False))
            open_figs.setdefault(name, []).append(vtx)
            used = True
            if "P" in ctrls or "T" in ctrls:
                report["curve_points"] += 1
            if "C" in ctrls:
                done.append((name, open_figs.pop(name), True))
            elif "E" in ctrls:
                done.append((name, open_figs.pop(name), False))
        report["linework_points"] += used
    for name, v in open_figs.items():
        done.append((name, v, False))
    figs = [(n, v, c) for n, v, c in done if len(v) >= 2]
    report["single_point_figures"] = sorted({n for n, v, c in done if len(v) < 2})
    return figs, report
