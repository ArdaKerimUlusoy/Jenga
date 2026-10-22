"""
THE LAST BLOCK - builds the kids from Quaternius' CC0 "Universal Animation Library" mannequin.

  * imports UAL1.glb (CC0, https://quaternius.com - mirrored at github.com/tilakpatell/tilakverse-assets)
  * keeps only the clips the game uses (sitting, standing idles, walk, jog, floor play, dance, cheering...)
  * dresses the grey mannequin as a kid: striped t-shirt, shorts, bare knees, white socks, sneakers
    (faces are re-assigned to new materials by the bone that owns them)
  * gives every kid a face (two shiny eyes), a baseball cap AND a mop of hair - the game shows one of the two
  * exports TLB_Kid.fbx for Unity (Generic rig, clips as takes). The head is enlarged at runtime (kid proportions).

Usage:  python blender_build_kid.py <UAL1.glb> <out.fbx> [preview.png]      (with the `bpy` module, Blender 4.x)
   or:  blender --background --python blender_build_kid.py -- <UAL1.glb> <out.fbx>
"""
import sys, math
import bpy
import bmesh
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
SRC, OUT = args[0], args[1]

KEEP = {"Sitting_Idle_Loop", "Sitting_Idle02_Loop", "Sitting_Idle03_Loop", "Sitting_Talking_Loop",
        "Sitting_Nodding_Loop", "Idle_Loop", "Idle_LookAround_Loop", "Idle_Talking_Loop", "Celebration", "Crying",
        "Walk_Loop", "Jog_Fwd_Loop", "GroundSit_Idle_Loop", "Fixing_Kneeling", "Dance_Loop", "Hit_Head", "Interact"}

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)

arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
body = max((o for o in bpy.data.objects if o.type == "MESH"), key=lambda o: len(o.data.polygons))
for o in list(bpy.data.objects):
    if o.type == "MESH" and o != body:
        bpy.data.objects.remove(o, do_unlink=True)
body.name = "Body"

# ---------------------------------------------------------------- animations
def base_name(n):
    return n.split("|")[-1].replace("_" + arm.name, "")

for a in list(bpy.data.actions):
    if base_name(a.name) not in KEEP:
        bpy.data.actions.remove(a)
    else:
        a.name = base_name(a.name)
        a.use_fake_user = True
if arm.animation_data:
    for t in list(arm.animation_data.nla_tracks):
        arm.animation_data.nla_tracks.remove(t)
    arm.animation_data.action = bpy.data.actions.get("Sitting_Idle_Loop")
# no scale keys: the game resizes head / torso / legs at runtime (kid proportions) and must not be overwritten
for a in bpy.data.actions:
    for fc in list(a.fcurves):
        if fc.data_path.endswith(".scale"):
            a.fcurves.remove(fc)
print("kept actions:", sorted(a.name for a in bpy.data.actions))

# ---------------------------------------------------------------- rest-pose measurements (world, metres; the mannequin faces -Y)
arm.data.pose_position = "REST"
bpy.context.view_layer.update()
def bone_world(name):
    b = arm.data.bones[name]
    return arm.matrix_world @ b.head_local, arm.matrix_world @ b.tail_local

head_h, head_t = bone_world("Head")
deps = bpy.context.evaluated_depsgraph_get()
ev = body.evaluated_get(deps)
evm = ev.to_mesh()
verts = [ev.matrix_world @ v.co for v in evm.vertices]
ev.to_mesh_clear()
top = max(v.z for v in verts)
if top < head_h.z + 0.1:
    top = head_h.z + 0.26
skull = [v for v in verts if v.z > head_h.z + 0.02]
face_y = min(v.y for v in skull if abs(v.x) < 0.02 and abs(v.z - (head_h.z + 0.09)) < 0.03)   # front of the face (-Y)
back_y = max(v.y for v in skull)
half_w = max(abs(v.x) for v in skull if abs(v.z - (head_h.z + 0.1)) < 0.03)
head_cy = (face_y + back_y) * 0.5
print("head bone", head_h, "top", top, "face_y", face_y, "back_y", back_y, "half width", half_w)

def mat(name, color, rough=0.7):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1)
    bsdf.inputs["Roughness"].default_value = rough
    return m

# ---------------------------------------------------------------- outfit: re-assign faces by owning bone
mats = [mat("M_Skin", (0.86, 0.66, 0.52), 0.55), mat("M_Shirt", (0.25, 0.48, 0.80), 0.85), mat("M_Stripe", (0.95, 0.93, 0.88), 0.85),
        mat("M_Shorts", (0.24, 0.30, 0.44), 0.9), mat("M_Socks", (0.95, 0.95, 0.93), 0.9), mat("M_Shoes", (0.82, 0.18, 0.15), 0.45)]
SKIN, SHIRT, STRIPE, SHORTS, SOCKS, SHOES = range(6)
me = body.data
me.materials.clear()
for m_ in mats:
    me.materials.append(m_)
vg = {g.index: g.name for g in body.vertex_groups}
def owner(poly):
    votes = {}
    for vi in poly.vertices:
        v = me.vertices[vi]
        if v.groups:
            g = max(v.groups, key=lambda g: g.weight)
            votes[vg[g.group]] = votes.get(vg[g.group], 0) + 1
    return max(votes, key=votes.get) if votes else ""
FINGERS = ("index", "middle", "ring", "pinky", "thumb", "hand")
_ev = body.evaluated_get(bpy.context.evaluated_depsgraph_get())
_evm = _ev.to_mesh()
_mw = _ev.matrix_world
centers = [_mw @ p.center for p in _evm.polygons]
_ev.to_mesh_clear()
for poly in me.polygons:
    b = owner(poly)
    c = centers[poly.index]
    if b in ("Head", "neck_01") or b.startswith(FINGERS) or b.startswith("lowerarm"):
        idx = SKIN
    elif b.startswith(("foot", "ball")):
        idx = SHOES
    elif b.startswith("calf"):
        idx = SOCKS if c.z < 0.19 else SKIN          # bare knees, white socks
    elif b.startswith("thigh") or b == "pelvis":
        idx = SHORTS
    elif b.startswith(("spine", "clavicle", "upperarm")):
        idx = SHIRT
    else:
        idx = SHIRT
    poly.material_index = idx
# the mannequin's own egg-shaped skull goes: the round kid head (below) replaces it
head_polys = [p.index for p in me.polygons if owner(p) == "Head"]
bm_ = bmesh.new()
bm_.from_mesh(me)
bm_.faces.ensure_lookup_table()
bmesh.ops.delete(bm_, geom=[bm_.faces[i] for i in head_polys], context="FACES")
bm_.to_mesh(me)
bm_.free()
print("outfit assigned, removed", len(head_polys), "skull faces")

def lathe_obj(name, profile, segments, material, center, scale_xy=(1.0, 1.0), cap_top=True):
    """profile: (radius, z) list, revolved around Z."""
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    rings = []
    for r, z in profile:
        ring = []
        for i in range(segments):
            a = 2 * math.pi * i / segments
            ring.append(bm.verts.new((r * math.cos(a) * scale_xy[0], r * math.sin(a) * scale_xy[1], z)))
        rings.append(ring)
    for k in range(len(rings) - 1):
        for i in range(segments):
            j = (i + 1) % segments
            bm.faces.new((rings[k][i], rings[k][j], rings[k + 1][j], rings[k + 1][i]))
    if cap_top and profile[-1][0] > 0:
        bm.faces.new(list(reversed(rings[-1])))
    bm.normal_update()
    bm.to_mesh(mesh)
    bm.free()
    ob = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(ob)
    ob.location = center
    mesh.materials.append(material)
    for p in mesh.polygons:
        p.use_smooth = True
    return ob

def ellipsoid(name, radii, center, material, seg=16):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=seg // 2, radius=1.0, location=center)
    ob = bpy.context.active_object
    ob.name = name
    ob.scale = radii
    bpy.ops.object.transform_apply(scale=True)
    ob.data.materials.append(material)
    for p in ob.data.polygons:
        p.use_smooth = True
    return ob

# ---------------------------------------------------------------- a round kid head over the mannequin's egg-shaped skull
HR = (0.118, 0.123, 0.128)                         # x, y (depth), z radii of the new head
hc = Vector((head_t.x, head_cy, top - HR[2] + 0.002))
skin_mat = mats[SKIN]
headball = ellipsoid("HeadBall", HR, hc, skin_mat, 32)
ears = [ellipsoid("Ear", (0.018, 0.03, 0.036), hc + Vector((sx * (HR[0] - 0.004), 0.004, -0.012)), skin_mat, 12) for sx in (-1, 1)]
face_y = hc.y - HR[1]
top = hc.z + HR[2]
rx, ry = HR[0] + 0.006, HR[1] + 0.006
center_xy = Vector((hc.x, hc.y, 0.0))

# ---------------------------------------------------------------- baseball cap: dome + forward brim
cap_mat = mat("M_Cap", (0.85, 0.20, 0.18), 0.6)
brim_mat = mat("M_CapBrim", (0.95, 0.95, 0.92), 0.6)
cz = top - 0.075
dome_prof = [(1.0, 0.0), (0.98, 0.025), (0.9, 0.05), (0.72, 0.072), (0.42, 0.087), (0.0, 0.092)]
dome = lathe_obj("CapDome", [(r * 1.0, z) for r, z in dome_prof], 36, cap_mat, Vector((center_xy.x, center_xy.y, cz)), (rx, ry))
# rescale z of the lathe: profile radii are unit and scaled in xy above
mesh = bpy.data.meshes.new("CapBrim")
bm = bmesh.new()
seg, rings = 24, []
for k, ext in enumerate([0.0, 0.5, 1.0]):
    ring = []
    for i in range(seg + 1):
        a = math.pi + math.pi * i / seg          # front half (-Y side)
        x = math.cos(a) * rx * (1.0 - 0.15 * ext)
        y = math.sin(a) * (ry + 0.085 * ext * (0.35 + 0.65 * abs(math.sin(a))))
        z = 0.004 - 0.012 * ext * ext
        ring.append(bm.verts.new((x, y, z)))
    rings.append(ring)
for k in range(len(rings) - 1):
    for i in range(seg):
        bm.faces.new((rings[k][i], rings[k][i + 1], rings[k + 1][i + 1], rings[k + 1][i]))
bm.normal_update()
bm.to_mesh(mesh)
bm.free()
brim = bpy.data.objects.new("CapBrim", mesh)
bpy.context.collection.objects.link(brim)
brim.location = Vector((center_xy.x, center_xy.y, cz + 0.004))
mesh.materials.append(brim_mat)
mod = brim.modifiers.new("Thick", "SOLIDIFY")
mod.thickness = 0.008
bpy.context.view_layer.objects.active = brim
bpy.ops.object.modifier_apply(modifier="Thick")
button = ellipsoid("CapButton", (0.012, 0.012, 0.008), Vector((center_xy.x, center_xy.y, cz + 0.092)), cap_mat, 10)

# ---------------------------------------------------------------- hair: a soft mop over the top and back of the head
hair_mat = mat("M_Hair", (0.25, 0.15, 0.08), 0.75)
hz = top - 0.13
hair_prof = [(1.02, 0.0), (1.06, 0.04), (1.06, 0.08), (0.98, 0.11), (0.8, 0.128), (0.5, 0.14), (0.0, 0.145)]
hair = lathe_obj("Hair", hair_prof, 36, hair_mat, Vector((center_xy.x, center_xy.y + 0.012, hz)), (rx, ry), True)
# fringe: open the front of the mop a little lower over the forehead by pulling the front ring down
for v in hair.data.vertices:
    if v.co.y < -ry * 0.55 and v.co.z < 0.05:
        v.co.z += 0.045          # the front of the bottom ring sits higher: forehead stays free

# ---------------------------------------------------------------- face: two shiny eyes (+ a tiny highlight)
eye_mat = mat("M_Eyes", (0.03, 0.025, 0.02), 0.15)
glint_mat = mat("M_EyeGlint", (1.0, 1.0, 1.0), 0.2)
ez = hc.z - 0.005
eyes = []
for sx in (-1, 1):
    ex = sx * 0.042
    ey = hc.y - HR[1] * math.sqrt(max(0.0, 1 - (ex / HR[0]) ** 2 - ((ez - hc.z) / HR[2]) ** 2)) + 0.004
    e = ellipsoid("Eye", (0.016, 0.009, 0.021), Vector((ex, ey, ez)), eye_mat, 14)
    g = ellipsoid("Glint", (0.005, 0.004, 0.005), Vector((ex + 0.005, ey - 0.008, ez + 0.008)), glint_mat, 8)
    eyes += [e, g]
# a small smile: an arc of tube on the front of the head
mouth_mat = mat("M_Mouth", (0.35, 0.10, 0.08), 0.5)
cu = bpy.data.curves.new("Smile", "CURVE")
cu.dimensions = "3D"
cu.bevel_depth = 0.0042
cu.bevel_resolution = 2
sp = cu.splines.new("POLY")
pts = []
for i in range(9):
    t = -1 + 2 * i / 8
    x = t * 0.03
    z = hc.z - 0.055 + 0.012 * t * t
    y = hc.y - HR[1] * math.sqrt(max(0.0, 1 - (x / HR[0]) ** 2 - ((z - hc.z) / HR[2]) ** 2)) + 0.002
    pts.append((x, y, z))
sp.points.add(len(pts) - 1)
for i, p in enumerate(pts):
    sp.points[i].co = (*p, 1)
smile = bpy.data.objects.new("Smile", cu)
bpy.context.collection.objects.link(smile)
bpy.context.view_layer.objects.active = smile
smile.select_set(True)
bpy.ops.object.convert(target="MESH")
smile = bpy.context.active_object
smile.data.materials.clear()
smile.data.materials.append(mouth_mat)
eyes += [smile]

# ---------------------------------------------------------------- join + parent accessories to the head bone
def parent_to_bone(ob, bone):
    mw = ob.matrix_world.copy()
    ob.parent = arm
    ob.parent_type = "BONE"
    ob.parent_bone = bone
    bpy.context.view_layer.update()
    ob.matrix_world = mw

def join(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    objs[0].name = name
    return objs[0]

cap = join([dome, brim, button], "KidCap")
eyes_ob = join([headball] + ears + eyes, "KidFace")
hair.name = "KidHair"
for o in (cap, hair, eyes_ob):
    parent_to_bone(o, "Head")
# plug the top of the neck (the old skull faces are gone) - it follows the neck, not the enlarged head
neck_h2, neck_t2 = bone_world("neck_01")
plug = ellipsoid("KidNeck", (0.058, 0.06, 0.07), Vector((neck_t2.x, neck_t2.y + 0.004, neck_t2.z - 0.02)), mats[SKIN], 16)
parent_to_bone(plug, "neck_01")
arm.data.pose_position = "POSE"
bpy.context.view_layer.update()

# ---------------------------------------------------------------- export
bpy.ops.object.select_all(action="DESELECT")
for o in (arm, body, cap, hair, eyes_ob, plug):
    o.select_set(True)
bpy.ops.export_scene.fbx(
    filepath=OUT, use_selection=True, object_types={"ARMATURE", "MESH"},
    apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
    add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=1.0, mesh_smooth_type="FACE", use_armature_deform_only=False)
print("exported", OUT)

# optional preview render: python blender_build_kid.py in.glb out.fbx preview.png
if len(args) > 2:
    scn = bpy.context.scene
    # the same kid proportions the game applies (PlayerAvatars.KidProportions)
    for bn, k in (("spine_01", 0.88), ("Head", 1.5), ("thigh_l", 0.8), ("thigh_r", 0.8)):
        arm.pose.bones[bn].scale = (k, k, k)
    act = bpy.data.actions.get(args[3] if len(args) > 3 else "Idle_Loop")
    arm.animation_data.action = act
    scn.frame_set(int(act.frame_range[0]) + 5)
    if len(args) > 4 and args[4] == "hair":
        cap.hide_render = True
    else:
        hair.hide_render = True
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    bpy.context.collection.objects.link(cam)
    cam.location = (0.9, -2.6, 1.45)
    cam.rotation_euler = (Vector((0, 0, 1.1)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    scn.camera = cam
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    bpy.context.collection.objects.link(sun)
    sun.rotation_euler = (0.8, 0.2, 0.3)
    sun.data.energy = 4
    w = bpy.data.worlds.new("W"); scn.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.55, 0.6, 1)
    scn.render.engine = "CYCLES"
    scn.cycles.samples = 16
    scn.render.resolution_x = scn.render.resolution_y = 420
    scn.render.filepath = args[2]
    bpy.ops.render.render(write_still=True)
    print("preview", args[2])
