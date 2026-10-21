"""
THE LAST BLOCK - builds the saloon patron character from Quaternius' CC0 "Universal Animation Library" mannequin.

  * imports UAL1.glb (CC0, https://quaternius.com - mirrored at github.com/tilakpatell/tilakverse-assets)
  * keeps only the seated / idle clips the game uses (keeps the file small)
  * repaints the grey mannequin as a saloon patron: skin, shirt, open vest, trousers, tall boots
    (faces are re-assigned to new materials by the bone that owns them)
  * models a cowboy hat and a neckerchief and parents them to the Head / neck bones
  * exports TLB_Cowboy.fbx for Unity (Generic rig, clips as takes)

Usage:  python blender_build_cowboy.py <UAL1.glb> <out.fbx>      (with the `bpy` module, Blender 4.x)
   or:  blender --background --python blender_build_cowboy.py -- <UAL1.glb> <out.fbx>
"""
import sys, math
import bpy
import bmesh
from mathutils import Vector, Matrix

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
SRC, OUT = args[0], args[1]

KEEP = {"Sitting_Idle_Loop", "Sitting_Idle02_Loop", "Sitting_Idle03_Loop", "Sitting_Talking_Loop",
        "Sitting_Nodding_Loop", "Idle_Loop", "Celebration", "Crying"}

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)

arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
# the file also contains a stray Icosphere - the character is the skinned mesh under the armature
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
print("kept actions:", [a.name for a in bpy.data.actions])

# ---------------------------------------------------------------- rest-pose bone positions (world)
arm.data.pose_position = "REST"   # build + parent accessories against the rest pose
bpy.context.view_layer.update()
def bone_world(name):
    b = arm.data.bones[name]
    return arm.matrix_world @ b.head_local, arm.matrix_world @ b.tail_local

head_h, head_t = bone_world("Head")
neck_h, _ = bone_world("neck_01")
# top of the skull: evaluate the deformed mesh in rest pose (vertex coords alone are in bind space)
deps = bpy.context.evaluated_depsgraph_get()
ev = body.evaluated_get(deps)
top = max((ev.matrix_world @ v.co).z for v in ev.to_mesh().vertices)
ev.to_mesh_clear()
if top < head_h.z + 0.1:          # fallback: mannequin skull is ~26 cm above the head bone
    top = head_h.z + 0.26
print("head bone", head_h, head_t, "neck", neck_h, "top", top)

def mat(name, color, rough=0.7):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1)
    bsdf.inputs["Roughness"].default_value = rough
    return m

# ---------------------------------------------------------------- outfit: re-assign faces by owning bone
skin_mat = mat("M_Skin", (0.72, 0.52, 0.40), 0.6)
shirt_mat = mat("M_Shirt", (0.80, 0.74, 0.62), 0.85)
vest_mat = mat("M_Vest", (0.20, 0.13, 0.08), 0.75)
pants_mat = mat("M_Pants", (0.22, 0.24, 0.30), 0.9)
boots_mat = mat("M_Boots", (0.16, 0.09, 0.05), 0.5)
me = body.data
me.materials.clear()
for m_ in (skin_mat, shirt_mat, vest_mat, pants_mat, boots_mat):
    me.materials.append(m_)
SKIN, SHIRT, VEST, PANTS, BOOTS = range(5)
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
# polygon centres/normals in world space, rest pose (mesh data may be stored in another bind space)
_ev = body.evaluated_get(bpy.context.evaluated_depsgraph_get())
_evm = _ev.to_mesh()
_mw = _ev.matrix_world
_n3 = _mw.to_3x3().inverted().transposed()
centers = [(_mw @ p.center, (_n3 @ p.normal).normalized()) for p in _evm.polygons]
_ev.to_mesh_clear()
for poly in me.polygons:
    b = owner(poly)
    c, n = centers[poly.index]
    if b in ("Head", "neck_01") or b.startswith(FINGERS):
        idx = SKIN
    elif b.startswith(("foot", "ball")) or (b.startswith("calf") and c.z < 0.36):
        idx = BOOTS
    elif b.startswith(("thigh", "calf")) or b == "pelvis":
        idx = PANTS
    elif b.startswith("spine") and n.y < -0.25 and abs(c.x) > 0.045 and c.z > 0.95:
        idx = VEST          # open vest over the chest
    elif b.startswith("spine") and n.y > 0.2:
        idx = VEST          # vest back
    else:
        idx = SHIRT
    poly.material_index = idx
print("outfit assigned")

hat_mat = mat("M_Hat", (0.33, 0.22, 0.13), 0.8)
band_mat = mat("M_HatBand", (0.12, 0.08, 0.05), 0.6)
scarf_mat = mat("M_Neckerchief", (0.55, 0.12, 0.10), 0.85)

def lathe_obj(name, profile, segments, material, center, scale_xy=(1.0, 1.0)):
    """profile: (radius, z) list, revolved around Z."""
    me = bpy.data.meshes.new(name)
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
    # caps
    if profile[-1][0] > 0:
        bm.faces.new(list(reversed(rings[-1])))
    bm.normal_update()
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    ob.location = center
    me.materials.append(material)
    for p in me.polygons:
        p.use_smooth = True
    return ob

# ---------------------------------------------------------------- cowboy hat (crown with a pinched top + upturned brim)
brim_z = top - 0.085
hat_center = Vector((head_t.x, head_t.y + 0.005, brim_z))
crown = lathe_obj("HatCrown", [(0.105, 0.0), (0.108, 0.03), (0.104, 0.09), (0.095, 0.13), (0.07, 0.15), (0.0, 0.135)], 40, hat_mat, hat_center, (1.0, 1.12))
band = lathe_obj("HatBand", [(0.109, 0.004), (0.112, 0.012), (0.112, 0.03), (0.109, 0.036)], 40, band_mat, hat_center, (1.0, 1.12))
# brim: flat ring whose sides curl up (Western style)
me = bpy.data.meshes.new("HatBrim")
bm = bmesh.new()
seg, rings = 48, []
for k, r in enumerate([0.10, 0.14, 0.18, 0.205]):
    ring = []
    for i in range(seg):
        a = 2 * math.pi * i / seg
        x, y = math.cos(a) * r * 1.0, math.sin(a) * r * 1.18
        side = abs(math.cos(a))            # curl up at the left / right sides
        z = (side ** 2) * (r - 0.10) * 0.55 + (0.004 if k else 0.0)
        ring.append(bm.verts.new((x, y, z)))
    rings.append(ring)
for k in range(len(rings) - 1):
    for i in range(seg):
        j = (i + 1) % seg
        bm.faces.new((rings[k][i], rings[k][j], rings[k + 1][j], rings[k + 1][i]))
bm.normal_update()
bm.to_mesh(me)
bm.free()
brim = bpy.data.objects.new("HatBrim", me)
bpy.context.collection.objects.link(brim)
brim.location = hat_center
me.materials.append(hat_mat)
mod = brim.modifiers.new("Thick", "SOLIDIFY")
mod.thickness = 0.008
bpy.context.view_layer.objects.active = brim
bpy.ops.object.modifier_apply(modifier="Thick")
for p in me.polygons:
    p.use_smooth = True

# ---------------------------------------------------------------- neckerchief
nz = neck_h.z + 0.02
scarf = lathe_obj("Neckerchief", [(0.075, -0.03), (0.082, 0.0), (0.08, 0.035), (0.07, 0.05)], 32, scarf_mat, Vector((neck_h.x, neck_h.y + 0.01, nz)), (1.1, 1.0))

# ---------------------------------------------------------------- join accessories, parent to bones
def parent_to_bone(ob, bone):
    mw = ob.matrix_world.copy()
    ob.parent = arm
    ob.parent_type = "BONE"
    ob.parent_bone = bone
    bpy.context.view_layer.update()
    ob.matrix_world = mw

bpy.ops.object.select_all(action="DESELECT")
for o in (crown, band, brim):
    o.select_set(True)
bpy.context.view_layer.objects.active = crown
bpy.ops.object.join()
crown.name = "CowboyHat"
parent_to_bone(crown, "Head")
parent_to_bone(scarf, "neck_01")
arm.data.pose_position = "POSE"
bpy.context.view_layer.update()

# ---------------------------------------------------------------- export
bpy.ops.object.select_all(action="DESELECT")
for o in (arm, body, crown, scarf):
    o.select_set(True)
bpy.ops.export_scene.fbx(
    filepath=OUT, use_selection=True, object_types={"ARMATURE", "MESH"},
    apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
    add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=1.0, mesh_smooth_type="FACE", use_armature_deform_only=False)
print("exported", OUT)

# optional preview render: python blender_build_cowboy.py in.glb out.fbx preview.png
if len(args) > 2:
    scn = bpy.context.scene
    act = bpy.data.actions.get("Sitting_Idle_Loop")
    arm.animation_data.action = act
    scn.frame_set(int(act.frame_range[0]) + 5)
    cam_data = bpy.data.cameras.new("Cam")
    cam = bpy.data.objects.new("Cam", cam_data)
    bpy.context.collection.objects.link(cam)
    cam.location = (1.6, -2.4, 1.4)
    direction = Vector((0, 0, 0.9)) - cam.location
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    scn.camera = cam
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    bpy.context.collection.objects.link(sun)
    sun.rotation_euler = (0.8, 0.2, 0.6)
    sun.data.energy = 4
    scn.render.engine = "CYCLES"
    scn.cycles.samples = 16
    scn.render.resolution_x = scn.render.resolution_y = 420
    scn.render.filepath = args[2]
    bpy.ops.render.render(write_still=True)
    print("preview", args[2])
