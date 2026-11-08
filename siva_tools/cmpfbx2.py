# Compare two export folders' FBX models by content (not bytes): per Geometry the arrays + layer layout, Model/Material names, connections shape.
import sys, os, numpy as np
sys.path.insert(0, sys.argv[1]); import fbx
A, B = sys.argv[2], sys.argv[3]
ARR = ('Vertices', 'PolygonVertexIndex', 'Normals', 'NormalsW', 'Tangents', 'TangentsW', 'Colors', 'UV', 'Materials')
def summary(path):
    r = fbx.parse(path); objs = r.get('Objects'); out = []
    gs = r.get('GlobalSettings').get('Properties70')
    axes = tuple(tuple(p.props[4:]) for p in gs.children if p.props[0] in ('UpAxis', 'FrontAxis', 'CoordAxis', 'UnitScaleFactor'))
    for c in objs.children:
        if c.name == 'Geometry':
            g = {'name': c.props[1].split('\x00')[0]}
            for e in c.children:
                if e.name in ARR: g[e.name] = np.asarray(e.props[0])
                if e.name.startswith('LayerElement'):
                    key = (e.name, e.props[0])
                    g[key] = {x.name: (np.asarray(x.props[0]) if x.name in ARR else x.props[0]) for x in e.children}
                if e.name == 'Layer':
                    g[('Layer', e.props[0])] = sorted((le.get('Type').props[0], le.get('TypedIndex').props[0]) for le in e.all('LayerElement'))
            out.append(('G', g['name'], g))
        elif c.name in ('Model', 'Material'):
            out.append((c.name, c.props[1].split('\x00')[0], None))
    return axes, sorted(out, key=lambda t: (t[0], t[1]))
def same(a, b):
    if isinstance(a, np.ndarray) or isinstance(b, np.ndarray):
        a = np.asarray(a); b = np.asarray(b)
        return a.shape == b.shape and np.array_equal(a.astype(np.float64), b.astype(np.float64))
    if isinstance(a, dict) and isinstance(b, dict):
        return a.keys() == b.keys() and all(same(a[k], b[k]) for k in a)
    return a == b
files = sorted(os.path.relpath(os.path.join(d, f), A) for d, _, fs in os.walk(A) for f in fs if f.endswith('.fbx'))
bad = []; missing = []
for f in files:
    pb = os.path.join(B, f)
    if not os.path.exists(pb): missing.append(f); continue
    ax_a, sa = summary(os.path.join(A, f)); ax_b, sb = summary(pb)
    ok = ax_a == ax_b and len(sa) == len(sb) and all(x[0] == y[0] and x[1] == y[1] and (x[2] is None or same(x[2], y[2])) for x, y in zip(sa, sb))
    if not ok:
        bad.append(f)
        if len(bad) <= 3:
            for x, y in zip(sa, sb):
                if x[2] is not None and y[2] is not None and not same(x[2], y[2]):
                    ks = [k for k in set(x[2]) | set(y[2]) if not same(x[2].get(k), y[2].get(k))]
                    print('DIFF', f, x[1], ks[:6]); break
            else: print('DIFF', f, 'object list / axes', ax_a == ax_b, len(sa), len(sb))
extra = [f for f in (os.path.relpath(os.path.join(d, x), B) for d, _, fs in os.walk(B) for x in fs if x.endswith('.fbx')) if not os.path.exists(os.path.join(A, f))]
print(f"compared {len(files)}  different {len(bad)}  missing in new {len(missing)}  only in new {len(extra)}")
