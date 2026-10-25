"""
THE LAST BLOCK - the players' hands: one continuous skinned hand + forearm, rigged to the same joints HandRig drives.

Run inside Blender (Text editor > Run, or `blender -b -P blender_build_hands.py -- <out.json>`).
Writes Assets/SecretJenga/Models/Characters/TLB_Hand.json, which the generator turns into Models/TLB_Hand.asset.

Everything is built in HandRig's own space (1 unit = 5 cm, wrist at the origin, fingers +Z, back of the hand +Y,
thumb on -X: a right hand) and in its bind pose: straight fingers fanned a little, thumb at rest, forearm along -Z.
Blender reads these numbers as its own right-handed XYZ, so the hand looks mirrored in the viewport. The same
numbers seen through Unity's left-handed camera are a mirror image too, which already turns Blender's
counter-clockwise front faces into Unity's clockwise ones: the winding is exported unchanged.
HandRig mirrors the mesh for the left hand.

The shapes are fused with a voxel remesh, so the knuckles, the web of the thumb and the wrist are one smooth surface
with nothing poking through. Weights come from Blender's bone heat; Unity computes the bind poses from the rig.
"""
import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals() else ""
OUT = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else os.path.join(HERE, "..", "Models", "Characters", "TLB_Hand.json")

# ---- must match HandRig.cs
FINGER_X = [-0.6, -0.2, 0.2, 0.57]
FINGER_Z = [1.92, 1.96, 1.92, 1.8]
FINGER_LEN = [[0.72, 0.42, 0.34], [0.8, 0.48, 0.36], [0.76, 0.46, 0.35], [0.58, 0.36, 0.3]]
FINGER_R = [0.185, 0.19, 0.18, 0.16]
THUMB_BASE = (-0.78, -0.15, 0.55)
THUMB_LEN = [0.6, 0.46, 0.4]
THUMB_R = 0.27
FOREARM_SKIN = 4.4          # how far back the forearm skin reaches (the sleeve covers the rest)
BIND_SPREAD = 1.5           # HandRig.BindSpread
BONES = ["Wrist"] + [f"F{f}_{j}" for f in range(4) for j in range(3)] + [f"T_{j}" for j in range(3)] + ["Forearm"]

VOXEL = 0.02
TARGET_TRIS = 7000


# ---------------------------------------------------------------- Unity rotation conventions
def euler(x, y, z):
    """Quaternion.Euler(x, y, z) as a matrix: Z first, then X, then Y."""
    r = math.radians
    return (Matrix.Rotation(r(y), 4, "Y") @ Matrix.Rotation(r(x), 4, "X") @ Matrix.Rotation(r(z), 4, "Z"))


def trs(pos, rot):
    return Matrix.Translation(Vector(pos)) @ rot


def rig_pose(curls=(0, 0, 0, 0), spread=BIND_SPREAD, thumb_curl=0.0, thumb_opp=0.0, side=1.0):
    """World matrices of every bone for a pose, exactly as HandRig.ApplyPose places its transforms."""
    w = {"Wrist": Matrix.Identity(4)}
    maxs = [72.0, 92.0, 64.0]
    for f in range(4):
        parent = w["Wrist"]
        pos = (FINGER_X[f] * side, 0.02, FINGER_Z[f])
        sp = (f - 1.5) * spread * 5.0 * side
        for j in range(3):
            m = parent @ trs(pos, euler(curls[f] * maxs[j], sp if j == 0 else 0.0, 0.0))
            w[f"F{f}_{j}"] = m
            parent = m
            pos = (0.0, 0.0, FINGER_LEN[f][j])
    t0 = euler(18 + 26 * thumb_opp, (-48 + 40 * thumb_opp) * side, (-30 - 55 * thumb_opp) * side)
    w["T_0"] = w["Wrist"] @ trs((THUMB_BASE[0] * side, THUMB_BASE[1], THUMB_BASE[2]), t0)
    w["T_1"] = w["T_0"] @ trs((0, 0, THUMB_LEN[0]), euler(thumb_curl * 40, 6 * side, 0))
    w["T_2"] = w["T_1"] @ trs((0, 0, THUMB_LEN[1]), euler(thumb_curl * 55, 0, 0))
    w["Forearm"] = euler(0, 180, 0)
    return w


def bone_length(name):
    if name[0] == "F" and name[1].isdigit():
        f, j = int(name[1]), int(name[3])
        return FINGER_LEN[f][j]
    if name.startswith("T_"):
        return THUMB_LEN[int(name[2])]
    return 1.6 if name == "Wrist" else FOREARM_SKIN


# ---------------------------------------------------------------- shapes
def new_obj(name, bm):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    return ob


def capsule(name, m, length, r0, r1, flat=0.9, rings=10, segs=20):
    """Tapered capsule along local +Z from 0 to length, elliptic section (flat = height / width)."""
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings * 2, radius=1.0)
    for v in bm.verts:
        z = v.co.z
        if z >= 0:
            r = r1
            v.co = Vector((v.co.x * r, v.co.y * r * flat, length + z * r))
        else:
            r = r0
            v.co = Vector((v.co.x * r, v.co.y * r * flat, z * r))
    bmesh.ops.transform(bm, matrix=m, verts=bm.verts)
    return new_obj(name, bm)


def ellipsoid(name, center, radii, rot=Matrix.Identity(4), squareness=1.0):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=32, v_segments=18, radius=1.0)
    for v in bm.verts:
        c = [math.copysign(abs(a) ** squareness, a) for a in v.co]
        v.co = Vector((c[0] * radii[0], c[1] * radii[1], c[2] * radii[2]))
    bmesh.ops.transform(bm, matrix=Matrix.Translation(Vector(center)) @ rot, verts=bm.verts)
    return new_obj(name, bm)


def palm():
    """A boxy, slightly cupped slab: narrower at the wrist, a soft arch across the knuckles."""
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=40, v_segments=24, radius=1.0)
    z0, z1 = -0.45, 2.0
    for v in bm.verts:
        x, y, z = (math.copysign(abs(a) ** e, a) for a, e in zip(v.co, (0.62, 0.7, 0.5)))
        t = (z + 1) * 0.5
        width = 0.5 + 0.26 * t ** 0.8
        thick = 0.21 + 0.04 * t
        px = x * width
        py = y * thick - 0.06 * (px / 0.76) ** 2 + 0.02
        v.co = Vector((px, py, z0 + t * (z1 - z0)))
    return new_obj("Palm", bm)


def forearm():
    bm = bmesh.new()
    segs, rings = 28, 24
    prof = [(0.35, 0.46, 0.22), (0.0, 0.49, 0.26), (-0.6, 0.52, 0.33), (-1.6, 0.6, 0.44), (-2.8, 0.66, 0.52), (-FOREARM_SKIN, 0.66, 0.54)]
    rows = []
    for i in range(rings + 1):
        t = i / rings
        k = t * (len(prof) - 1)
        a, b = prof[int(min(k, len(prof) - 2))], prof[int(min(k, len(prof) - 2)) + 1]
        u = k - int(min(k, len(prof) - 2))
        z, rx, ry = (a[n] + (b[n] - a[n]) * u for n in range(3))
        row = [bm.verts.new((math.cos(s / segs * 2 * math.pi) * rx, math.sin(s / segs * 2 * math.pi) * ry, z)) for s in range(segs)]
        rows.append(row)
    for i in range(rings):
        for s in range(segs):
            bm.faces.new((rows[i][s], rows[i][(s + 1) % segs], rows[i + 1][(s + 1) % segs], rows[i + 1][s]))
    for row, z in ((rows[0], prof[0][0]), (rows[-1], -FOREARM_SKIN)):
        c = bm.verts.new((0, 0, z))
        for s in range(segs):
            bm.faces.new((row[s], row[(s + 1) % segs], c))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return new_obj("Forearm", bm)


def build_skin(pose):
    parts = [palm(), forearm()]
    for f in range(4):
        for j in range(3):
            m = pose[f"F{f}_{j}"]
            r0 = FINGER_R[f] * (1 - 0.06 * j)
            r1 = FINGER_R[f] * (1 - 0.06 * (j + 1)) * (0.96 if j == 2 else 1.0)
            ln = FINGER_LEN[f][j] - (0.04 if j == 2 else 0.0)
            parts.append(capsule(f"F{f}_{j}", m, ln, r0 * 1.04, r1, 0.86))
            if j > 0:
                # the middle and end joints are a little thicker than the bone between them
                c = m @ Vector((0, 0.01, 0))
                parts.append(ellipsoid(f"Joint{f}{j}", c, (r0 * 1.1, r0 * 0.95, r0 * 0.9)))
        tip = pose[f"F{f}_2"] @ Vector((0, -0.04, FINGER_LEN[f][2] * 0.55))
        rt = FINGER_R[f] * 0.86
        parts.append(ellipsoid(f"Pad{f}", tip, (rt * 0.95, rt * 0.8, FINGER_LEN[f][2] * 0.42)))
        # knuckle on the back of the hand
        k = pose[f"F{f}_0"].to_translation()
        parts.append(ellipsoid(f"Knuckle{f}", (k.x, k.y + 0.02, k.z - 0.14), (FINGER_R[f] * 1.0, FINGER_R[f] * 0.7, FINGER_R[f] * 1.0)))
    for j in range(3):
        r0 = THUMB_R * (1 - 0.07 * j)
        r1 = THUMB_R * (1 - 0.07 * (j + 1)) * (0.96 if j == 2 else 1.0)
        ln = THUMB_LEN[j] - (0.04 if j == 2 else 0.0)
        parts.append(capsule(f"T_{j}", pose[f"T_{j}"], ln, r0 * (1.25 if j == 0 else 1.0), r1, 0.88))
    # thumb ball and the outer heel of the palm
    parts.append(ellipsoid("Thenar", (-0.44, -0.08, 0.74), (0.34, 0.21, 0.64), euler(0, -18, 0)))
    parts.append(ellipsoid("Hypothenar", (0.42, -0.06, 0.88), (0.26, 0.15, 0.78), euler(0, 6, 0)))

    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    ob = bpy.context.active_object
    ob.name = "HandSkin"

    rm = ob.modifiers.new("Fuse", "REMESH")
    rm.mode = "VOXEL"
    rm.voxel_size = VOXEL
    rm.adaptivity = 0.0
    bpy.ops.object.modifier_apply(modifier=rm.name)
    sm = ob.modifiers.new("Soften", "CORRECTIVE_SMOOTH")
    sm.factor = 0.6
    sm.iterations = 20
    sm.smooth_type = "SIMPLE"
    sm.use_only_smooth = True
    bpy.ops.object.modifier_apply(modifier=sm.name)
    retopo(ob, pose)
    for p in ob.data.polygons:
        p.use_smooth = True
    return ob


def digits_ok(ob, pose):
    """Every fingertip and the thumb tip still has geometry (a remesh can swallow a thin finger)."""
    tips = [pose[f"F{f}_2"] @ Vector((0, 0, FINGER_LEN[f][2] * 0.6)) for f in range(4)]
    tips.append(pose["T_2"] @ Vector((0, 0, THUMB_LEN[2] * 0.6)))
    return all(any((v.co - t).length < FINGER_R[min(i, 3)] * 1.4 for v in ob.data.vertices) for i, t in enumerate(tips))


def retopo(ob, pose):
    """Even quads (clean shading, rounder bends). QuadriFlow can drop a thin finger, so every result is checked;
    more faces are tried before falling back to a plain decimate, which never loses a finger."""
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    dense = ob.data.copy()
    for faces in (TARGET_TRIS // 2, TARGET_TRIS, TARGET_TRIS * 3 // 2):
        try:
            bpy.ops.object.quadriflow_remesh(target_faces=faces, seed=7, use_mesh_symmetry=False, use_preserve_sharp=False, smooth_normals=True)
        except RuntimeError as e:
            print("quadriflow failed:", e)
        if len(ob.data.polygons) != len(dense.polygons) and digits_ok(ob, pose):
            print(f"quadriflow kept all digits at {faces} faces")
            return
        print(f"quadriflow at {faces} faces lost a digit; retrying")
        ob.data = dense.copy()
    dm = ob.modifiers.new("Budget", "DECIMATE")
    dm.ratio = min(1.0, TARGET_TRIS / max(1, sum(len(p.vertices) - 2 for p in ob.data.polygons)))
    bpy.ops.object.modifier_apply(modifier=dm.name)
    if not digits_ok(ob, pose):
        raise RuntimeError("the hand lost a finger even after a plain decimate")


def build_sleeve():
    """Rolled-up shirt sleeve over the back of the forearm: a turned-in lip, a thick cuff, soft folds above it."""
    bm = bmesh.new()
    segs = 32
    flat = 0.82
    # (z, radius, folds) from the cuff's inner lip up to the elbow, where the upper arm takes over
    prof = [(-2.7, 0.7, 0), (-2.56, 0.76, 0), (-2.6, 0.84, 0), (-2.78, 0.88, 0), (-3.12, 0.88, 0), (-3.28, 0.83, 0),
            (-3.4, 0.79, 0.4), (-4.2, 0.82, 1), (-5.2, 0.85, 1), (-6.2, 0.88, 0.7), (-6.9, 0.9, 0.3)]
    rows = []
    for z, r, fold in prof:
        row = []
        for k in range(segs):
            a = k / segs * 2 * math.pi
            wob = 1 + fold * (0.035 * math.sin(3 * a + z * 2.3) + 0.02 * math.sin(5 * a - z * 3.1))
            row.append(bm.verts.new((math.cos(a) * r * wob, math.sin(a) * r * flat * wob, z)))
        rows.append(row)
    for i in range(len(rows) - 1):
        for k in range(segs):
            bm.faces.new((rows[i][k], rows[i][(k + 1) % segs], rows[i + 1][(k + 1) % segs], rows[i + 1][k]))
    for row in (rows[0], rows[-1]):
        c = bm.verts.new((0, 0, row[0].co.z))
        for k in range(segs):
            bm.faces.new((row[k], row[(k + 1) % segs], c))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = new_obj("Sleeve", bm)
    sub = ob.modifiers.new("Soft", "SUBSURF")
    sub.levels = 1
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.modifier_apply(modifier=sub.name)
    ob.vertex_groups.new(name="Forearm").add(list(range(len(ob.data.vertices))), 1.0, "REPLACE")
    for p in ob.data.polygons:
        p.use_smooth = True
    return ob


def build_armature(pose):
    arm = bpy.data.armatures.new("HandRig")
    ob = bpy.data.objects.new("HandRig", arm)
    bpy.context.collection.objects.link(ob)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode="EDIT")
    eb = {}
    for name in BONES:
        b = arm.edit_bones.new(name)
        m = pose[name]
        head = m.to_translation()
        b.head = head
        b.tail = head + (m.to_3x3() @ Vector((0, 0, bone_length(name))))
        b.roll = 0
        eb[name] = b
    for f in range(4):
        eb[f"F{f}_0"].parent = eb["Wrist"]
        eb[f"F{f}_1"].parent = eb[f"F{f}_0"]
        eb[f"F{f}_2"].parent = eb[f"F{f}_1"]
    eb["T_0"].parent = eb["Wrist"]
    eb["T_1"].parent = eb["T_0"]
    eb["T_2"].parent = eb["T_1"]
    bpy.ops.object.mode_set(mode="OBJECT")
    return ob


def skin_to(arm, ob):
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")


def blend_wrist(ob, z_hand=0.55, z_arm=-0.65):
    """Bone heat switches from forearm to hand almost on one ring; spread it over the wrist so a bend creases softly."""
    wrist, arm = ob.vertex_groups["Wrist"], ob.vertex_groups["Forearm"]
    for v in ob.data.vertices:
        z = v.co.z
        if z > z_hand + 0.2:
            continue
        own = {ob.vertex_groups[g.group].name: g.weight for g in v.groups if g.weight > 1e-4}
        other = sum(w for n, w in own.items() if n not in ("Wrist", "Forearm"))
        share = 1.0 - min(1.0, other)
        t = min(1.0, max(0.0, (z - z_arm) / (z_hand - z_arm)))
        t = t * t * (3 - 2 * t)
        wrist.add([v.index], share * t, "REPLACE")
        arm.add([v.index], share * (1 - t), "REPLACE")


# ---------------------------------------------------------------- export
def export(skin, sleeve, path):
    """Flat arrays (Unity's JsonUtility cannot read nested ones): 4 bone slots per vertex, indices into BONES.
    triangles = skin (submesh 0), clothTriangles = sleeve (submesh 1)."""
    d = {"bones": BONES, "positions": [], "normals": [], "uvs": [], "boneIndices": [], "boneWeights": [],
         "triangles": [], "clothTriangles": []}
    count = 0
    for ob, key in ((skin, "triangles"), (sleeve, "clothTriangles")):
        me = ob.data
        me.calc_loop_triangles()
        uv = me.uv_layers.active.data if me.uv_layers.active else None
        groups = {g.index: g.name for g in ob.vertex_groups}
        remap = {}
        for lt in me.loop_triangles:
            for li in lt.loops:
                loop = me.loops[li]
                v = me.vertices[loop.vertex_index]
                n = v.normal if lt.use_smooth else loop.normal
                u = tuple(round(c, 4) for c in uv[li].uv) if uv else (0.0, 0.0)
                k = (loop.vertex_index, u, tuple(round(c, 3) for c in n))
                if k not in remap:
                    ws = sorted(((g.weight, BONES.index(groups[g.group])) for g in v.groups
                                 if groups.get(g.group) in BONES and g.weight > 1e-4), reverse=True)[:4]
                    total = sum(w for w, _ in ws) or 1.0
                    ws += [(0.0, 0)] * (4 - len(ws))
                    remap[k] = count
                    count += 1
                    d["positions"] += [round(c, 5) for c in v.co]
                    d["normals"] += [round(c, 4) for c in n]
                    d["uvs"] += list(u)
                    d["boneIndices"] += [bi for _, bi in ws]
                    d["boneWeights"] += [round(w / total, 4) for w, _ in ws]
                d[key].append(remap[k])
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w") as fh:
        json.dump(d, fh, separators=(",", ":"))
    print(f"wrote {path}: {count} vertices, {len(d['triangles']) // 3} skin + {len(d['clothTriangles']) // 3} sleeve triangles")


# ---------------------------------------------------------------- main
def build(path=OUT):
    scene = bpy.data.scenes.get("TLB_Hands") or bpy.data.scenes.new("TLB_Hands")
    bpy.context.window.scene = scene
    for o in list(scene.collection.all_objects):
        bpy.data.objects.remove(o, do_unlink=True)
    bpy.context.view_layer.update()

    pose = rig_pose()
    skin = build_skin(pose)
    bpy.ops.object.select_all(action="DESELECT")
    skin.select_set(True)
    bpy.context.view_layer.objects.active = skin
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.01)
    bpy.ops.object.mode_set(mode="OBJECT")
    sleeve = build_sleeve()
    arm = build_armature(pose)
    skin_to(arm, skin)
    blend_wrist(skin)
    export(skin, sleeve, path)
    return skin, sleeve, arm


if __name__ == "__main__":
    build()
