"""
Secret Jenga - Blender model builder (optional).

The Unity generator already creates every mesh procedurally (and exports OBJ copies to
Assets/SecretJenga/Models/OBJ). Use this script if you want to refine the models in Blender:
it rebuilds the Jenga block, the table and the pendant lamp with bevels, UVs and simple
materials, then exports FBX + GLB files that Unity can import directly.

Usage (Blender 3.6 - 4.x):
    blender --background --python blender_build_models.py -- <output_folder>
or open Blender > Scripting tab > open this file > Run Script (exports next to the .blend).

Units: 1 Blender unit = 1 Unity unit. Block = 1.5 x 0.3 x 0.5 (L x H x W), pivot at center.
"""
import bpy
import bmesh
import math
import os
import sys

argv = sys.argv
out_dir = argv[argv.index("--") + 1] if "--" in argv else os.path.join(os.path.dirname(bpy.data.filepath or os.getcwd()), "SecretJengaModels")
os.makedirs(out_dir, exist_ok=True)


def reset_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in (bpy.data.meshes, bpy.data.materials):
        for item in list(block):
            if item.users == 0:
                block.remove(item)


def make_material(name, base_color, roughness=0.5, metallic=0.0, emission=None):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*base_color, 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    if emission is not None:
        key = "Emission Color" if "Emission Color" in bsdf.inputs else "Emission"
        bsdf.inputs[key].default_value = (*emission, 1.0)
        if "Emission Strength" in bsdf.inputs:
            bsdf.inputs["Emission Strength"].default_value = 5.0
    return mat


def wood_material(name, light, dark, scale=6.0):
    """Procedural wood grain (wave texture) - bake it if you need image textures."""
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nodes, links = nt.nodes, nt.links
    for n in list(nodes):
        if n.type not in ("OUTPUT_MATERIAL", "BSDF_PRINCIPLED"):
            nodes.remove(n)
    bsdf = nodes.get("Principled BSDF")
    coord = nodes.new("ShaderNodeTexCoord")
    mapping = nodes.new("ShaderNodeMapping")
    mapping.inputs["Scale"].default_value = (1.0, scale, scale)
    wave = nodes.new("ShaderNodeTexWave")
    wave.wave_type = "RINGS"
    wave.inputs["Scale"].default_value = 2.0
    wave.inputs["Distortion"].default_value = 6.0
    wave.inputs["Detail"].default_value = 4.0
    ramp = nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (*dark, 1)
    ramp.color_ramp.elements[1].color = (*light, 1)
    links.new(coord.outputs["Object"], mapping.inputs["Vector"])
    links.new(mapping.outputs["Vector"], wave.inputs["Vector"])
    links.new(wave.outputs["Fac"], ramp.inputs["Fac"])
    links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.55
    return mat


def beveled_box(name, size, bevel, segments=2, material=None):
    bpy.ops.mesh.primitive_cube_add(size=1.0)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    mod = obj.modifiers.new("Bevel", "BEVEL")
    mod.width = bevel
    mod.segments = segments
    mod.limit_method = "ANGLE"
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.ops.object.shade_smooth()
    if hasattr(obj.data, "use_auto_smooth"):
        obj.data.use_auto_smooth = True
        obj.data.auto_smooth_angle = math.radians(40)
    # box-projected UVs, grain along X
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.cube_project(cube_size=max(size))
    bpy.ops.object.mode_set(mode="OBJECT")
    if material:
        obj.data.materials.append(material)
    return obj


def lathe(name, profile, segments=32, material=None):
    """profile: list of (radius, height); revolved around Z (Blender up)."""
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    rings = []
    for r, z in profile:
        ring = []
        for i in range(segments):
            a = 2 * math.pi * i / segments
            ring.append(bm.verts.new((r * math.cos(a), r * math.sin(a), z)))
        rings.append(ring)
    for k in range(len(rings) - 1):
        for i in range(segments):
            j = (i + 1) % segments
            bm.faces.new((rings[k][i], rings[k][j], rings[k + 1][j], rings[k + 1][i]))
    bm.normal_update()
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.shade_smooth()
    if material:
        mesh.materials.append(material)
    return obj


def export(objs, filename):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    fbx = os.path.join(out_dir, filename + ".fbx")
    glb = os.path.join(out_dir, filename + ".glb")
    bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, apply_unit_scale=True, axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE")
    bpy.ops.export_scene.gltf(filepath=glb, use_selection=True, export_format="GLB")
    print("exported", fbx, glb)


reset_scene()
maple = wood_material("SJ_Maple", (0.93, 0.80, 0.60), (0.70, 0.52, 0.33))
walnut = wood_material("SJ_Walnut", (0.36, 0.22, 0.13), (0.16, 0.09, 0.05), 3.0)
shade_mat = make_material("SJ_LampShade", (0.12, 0.30, 0.22), 0.3, 0.4)
bulb_mat = make_material("SJ_Bulb", (1.0, 0.9, 0.7), 0.1, 0.0, emission=(1.0, 0.75, 0.45))

# Jenga block: Blender Z-up -> size (X=length, Y=width, Z=height)
block = beveled_box("SJ_JengaBlock", (1.5, 0.5, 0.3), 0.035, 2, maple)
export([block], "SJ_JengaBlock")

reset_scene()
walnut = wood_material("SJ_Walnut", (0.36, 0.22, 0.13), (0.16, 0.09, 0.05), 3.0)
top = beveled_box("TableTop", (14.0, 9.5, 0.5), 0.12, 3, walnut)
top.location = (0, 0, -0.25)
leg_profile = [(0.20, -11.0), (0.26, -10.85), (0.24, -10.6), (0.20, -10.2), (0.22, -8.0), (0.26, -6.0), (0.32, -5.6),
               (0.36, -5.3), (0.32, -5.0), (0.28, -4.6), (0.30, -2.6), (0.38, -2.2), (0.40, -1.9), (0.36, -1.6), (0.40, -0.5)]
legs = []
for x, y in [(6.1, 3.9), (-6.1, 3.9), (6.1, -3.9), (-6.1, -3.9)]:
    leg = lathe("TableLeg", leg_profile, 24, walnut)
    leg.location = (x, y, 0)
    legs.append(leg)
aprons = []
for (x, y, sx, sy) in [(0, 3.95, 12.2, 0.18), (0, -3.95, 12.2, 0.18), (6.15, 0, 0.18, 7.8), (-6.15, 0, 0.18, 7.8)]:
    a = beveled_box("Apron", (sx, sy, 0.9), 0.04, 2, walnut)
    a.location = (x, y, -0.95)
    aprons.append(a)
export([top] + legs + aprons, "SJ_Table")

reset_scene()
shade_mat = make_material("SJ_LampShade", (0.12, 0.30, 0.22), 0.3, 0.4)
bulb_mat = make_material("SJ_Bulb", (1.0, 0.9, 0.7), 0.1, 0.0, emission=(1.0, 0.75, 0.45))
shade = lathe("LampShade", [(1.6, -0.9), (1.62, -0.85), (1.2, -0.35), (0.45, 0.1), (0.18, 0.3), (0.18, 0.5)], 40, shade_mat)
bpy.ops.mesh.primitive_uv_sphere_add(radius=0.28, location=(0, 0, -0.45))
bulb = bpy.context.active_object
bulb.name = "Bulb"
bulb.data.materials.append(bulb_mat)
export([shade, bulb], "SJ_PendantLamp")

blend_path = os.path.join(out_dir, "SecretJenga_Models.blend")
bpy.ops.wm.save_as_mainfile(filepath=blend_path)
print("Saved", blend_path)
