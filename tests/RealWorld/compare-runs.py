"""Compares production-drawing runs with a baseline, case by case.

    python compare-runs.py <baseline folder> <runs folder> [out.md]

Reads each case's report (RWREPORT): QA counts, easement areas, tie/route/boundary courses, hatches, review items.
Geometry is compared course by course; a difference over 0.001 ft is listed.
"""
import math
import os
import re
import sys

COURSE = re.compile(r'^\s+(tie|tiecourse|route|boundary) (Line|Arc) ([\d.\-]+),([\d.\-]+) -> ([\d.\-]+),([\d.\-]+) len ([\d.]+)')


def parse(path):
    data = {'easements': {}, 'exhibits': {}, 'order': []}
    if not os.path.exists(path):
        return data
    current = None
    kind = None
    for raw in open(path, encoding='utf-8-sig'):
        line = raw.rstrip('\n')
        m = re.match(r'^EASEMENT (.+?) area ([\d.]+) display ([\d.]+)', line)
        if m:
            title = m.group(1)
            n = 2
            while title in data['easements']:
                title = m.group(1) + ' #' + str(n)
                n += 1
            current = {'area': float(m.group(2)), 'display': float(m.group(3)), 'courses': {'tie': [], 'tiecourse': [], 'route': [], 'boundary': []}, 'hatch': [], 'lotlines': None}
            data['easements'][title] = current
            kind = 'E'
            continue
        m = re.match(r'^EXHIBIT (.+?): 1" = ([\d.]+)\' rotation ([\-\d.]+) profile (.+?) items (\d+) QA: (.*)$', line)
        if m:
            current = {'scale': float(m.group(2)), 'rotation': float(m.group(3)), 'items': int(m.group(5)), 'qa': m.group(6), 'review': [], 'keys': [], 'north': None}
            data['exhibits'][m.group(1)] = current
            kind = 'X'
            continue
        if current is None:
            continue
        if kind == 'E':
            c = COURSE.match(line)
            if c:
                current['courses'][c.group(1)].append((c.group(2), float(c.group(3)), float(c.group(4)), float(c.group(5)), float(c.group(6)), float(c.group(7))))
                continue
            h = re.match(r'^\s+hatch \S+ (\S+) scale ([\d.]+) spacing ([\d.]+)', line)
            if h:
                current['hatch'].append((h.group(1), float(h.group(2)), float(h.group(3))))
                continue
            l = re.match(r'^\s+lot lines (\d+)', line)
            if l:
                current['lotlines'] = int(l.group(1))
        else:
            r = re.match(r'^\s+review (\w*):? ?(.*)$', line)
            if r:
                current['review'].append((r.group(1), r.group(2)))
                continue
            i = re.match(r'^\s+item (\S+) \[', line)
            if i:
                current['keys'].append(i.group(1))
                continue
            n = re.match(r'^\s+north arrow checked: (\w+)', line)
            if n:
                current['north'] = n.group(1)
    return data


def qa_counts(qa):
    m = re.match(r'(\d+) Errors, (\d+) Review Items, (\d+) Informational', qa or '')
    return (int(m.group(1)), int(m.group(2)), int(m.group(3))) if m else (None, None, None)


def course_diff(a, b):
    if len(a) != len(b):
        return 'course count %d -> %d' % (len(a), len(b))
    worst = 0.0
    kinds = []
    for x, y in zip(a, b):
        if x[0] != y[0]:
            kinds.append('%s->%s' % (x[0], y[0]))
        worst = max(worst, math.hypot(x[1] - y[1], x[2] - y[2]), math.hypot(x[3] - y[3], x[4] - y[4]))
    parts = []
    if kinds:
        parts.append('kinds ' + ', '.join(kinds))
    if worst > 0.001:
        parts.append('max point shift %.3f ft' % worst)
    return '; '.join(parts) if parts else 'identical'


def generic(key):
    return re.sub(r':[0-9a-f]{32}', ':<id>', key)


def main():
    base_root, runs_root = sys.argv[1], sys.argv[2]
    out = []
    cases = sorted(set(os.listdir(runs_root)) | set(os.listdir(base_root)))
    for case in cases:
        new_path = os.path.join(runs_root, case, 'report.txt')
        if not os.path.exists(new_path):
            continue
        base_geo = os.path.join(base_root, case, 'geometry.txt')
        base = parse(base_geo if os.path.exists(base_geo) else os.path.join(base_root, case, 'report.txt'))
        new = parse(new_path)
        out.append('## ' + case)
        for name, x in new['exhibits'].items():
            bx = base['exhibits'].get(name)
            be, br, bi = qa_counts(bx['qa']) if bx else (None, None, None)
            ne, nr, ni = qa_counts(x['qa'])
            out.append('- QA %s: errors %s -> %s, review %s -> %s, info %s -> %s; scale 1"=%s\' -> %s\'; north checked %s' %
                       (name, be, ne, br, nr, bi, ni, bx['scale'] if bx else '-', x['scale'], x['north']))
            if bx:
                old_msgs = set(generic(m) for _, m in bx['review'])
                new_msgs = set(generic(m) for _, m in x['review'])
                gone = sorted(old_msgs - new_msgs)
                added = sorted(new_msgs - old_msgs)
                for m in gone:
                    out.append('  - review no longer: ' + m[:220])
                for m in added:
                    out.append('  - review new: ' + m[:220])
                old_keys = set(generic(k) for k in bx['keys'])
                new_keys = set(generic(k) for k in x['keys'])
                if old_keys != new_keys:
                    out.append('  - sheet items added: %s; removed: %s' % (sorted(new_keys - old_keys), sorted(old_keys - new_keys)))
        for title, e in new['easements'].items():
            b = base['easements'].get(title)
            if not b:
                out.append('- easement %s: new (area %.2f)' % (title, e['area']))
                continue
            out.append('- easement %s: area %.2f -> %.2f (%+.3f sq ft)' % (title, b['area'], e['area'], e['area'] - b['area']))
            for part in ('tie', 'tiecourse', 'route', 'boundary'):
                if b['courses'][part] or e['courses'][part]:
                    d = course_diff(b['courses'][part], e['courses'][part])
                    if d != 'identical' or part in ('route', 'boundary'):
                        out.append('  - %s: %s' % (part, d))
            scale = next(iter(new['exhibits'].values()))['scale'] if new['exhibits'] else None
            for h in e['hatch']:
                out.append('  - hatch %s scale %.4f, lines print %.4f" apart at 1"=%s\'' % (h[0], h[1], h[2] / scale if scale else float('nan'), scale))
            if e['lotlines']:
                out.append('  - lot built from %d separate lines' % e['lotlines'])
        out.append('')
    text = '\n'.join(out)
    if len(sys.argv) > 3:
        open(sys.argv[3], 'w', encoding='utf-8').write(text)
    print(text)


if __name__ == '__main__':
    main()
