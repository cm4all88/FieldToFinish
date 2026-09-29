"""Small survey geometry helpers shared by the level builders."""
import math

from ezdxf.math import Vec2


def bearing(p1, p2, sep=""):
    """Quadrant bearing from p1 to p2, e.g. N89°12'40"E (sep=" " -> N 89°12'40" E)."""
    d = Vec2(p2) - Vec2(p1)
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
    dd, rem = divmod(tot, 3600)
    mm, ss = divmod(rem, 60)
    return f"{ns}{sep}{dd:02d}°{mm:02d}'{ss:02d}\"{sep}{ew}"


def bearing_of_angle(deg_ccw_from_east):
    """Bearing text for an AutoCAD angle (0 = east, CCW)."""
    a = math.radians(deg_ccw_from_east)
    return bearing((0, 0), (math.cos(a), math.sin(a)))


def typed(b):
    """Bearing as typed at the AutoCAD command line: N89d12'40"E."""
    return b.replace("°", "d").replace(" ", "")


def dist(p1, p2):
    return (Vec2(p2) - Vec2(p1)).magnitude


def line_intersection(p1, p2, p3, p4):
    p1, p2, p3, p4 = map(Vec2, (p1, p2, p3, p4))
    d1, d2 = p2 - p1, p4 - p3
    den = d1.x * d2.y - d1.y * d2.x
    t = ((p3.x - p1.x) * d2.y - (p3.y - p1.y) * d2.x) / den
    return p1 + d1 * t


def perp_foot(p, a, b):
    p, a, b = map(Vec2, (p, a, b))
    ab = b - a
    return a + ab * ((p - a).dot(ab) / ab.dot(ab))


def point_along(a, b, d):
    """Point d feet from a toward b (negative = behind a)."""
    a, b = Vec2(a), Vec2(b)
    return a + (b - a).normalize(d) if d >= 0 else a - (b - a).normalize(-d)


def ray_hits_polyline(start, direction_pt, pts):
    """First intersection of the ray start->direction_pt (beyond start) with polyline pts."""
    s, dpt = Vec2(start), Vec2(direction_pt)
    best = None
    for a, b in zip(pts, pts[1:]):
        a, b = Vec2(a), Vec2(b)
        d1, d2 = dpt - s, b - a
        den = d1.x * d2.y - d1.y * d2.x
        if abs(den) < 1e-12:
            continue
        t = ((a.x - s.x) * d2.y - (a.y - s.y) * d2.x) / den
        u = ((a.x - s.x) * d1.y - (a.y - s.y) * d1.x) / den
        if t > 1e-9 and -1e-9 <= u <= 1 + 1e-9 and (best is None or t < best[0]):
            best = (t, s + d1 * t)
    return best[1] if best else None


def polygon_area(pts):
    return 0.5 * abs(sum(pts[i][0] * pts[i - 1][1] - pts[i - 1][0] * pts[i][1] for i in range(len(pts))))
