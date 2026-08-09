"""
Shared helpers for every Phở Simulator art script.

DESIGN RULES (read before adding an asset):

* 1 Blender unit == 1 metre == 1 Unity unit. Never scale on export.
* +Z is up in Blender; the glTF exporter converts to Unity's +Y up. Author
  in Blender's convention and let the exporter do it -- do NOT pre-rotate.
* Every asset's origin sits where the object is naturally PLACED: floor
  contact for furniture, so SceneBuilder can drop a model at a world
  position with no magic Y offset.
* Style target (GDD section 40): stylized realism. That means READABLE
  SILHOUETTES and chunky, slightly-oversized proportions -- not high poly
  counts. The "realism" comes from Unity's URP lighting and materials, not
  from geometry density. Keep meshes cheap.
* Bevel everything. A 2-3mm bevel on every hard edge is the single highest
  value-per-triangle trick there is: it catches a specular highlight along
  each edge, which is most of what separates "3D model" from "programmer
  cube" under real lighting.
"""

import bpy
import bmesh
import math
import os
import sys


# --------------------------------------------------------------------------
# Palette -- one place, so every asset agrees.
# Values are linear-ish sRGB picked for a warm Vietnamese shophouse:
# aged wood, painted plaster, stainless steel, terracotta.
# --------------------------------------------------------------------------

PALETTE = {
    "wood_dark":      (0.16, 0.09, 0.05, 1.0),
    "wood_mid":       (0.35, 0.20, 0.10, 1.0),
    "wood_light":     (0.55, 0.36, 0.20, 1.0),
    "plaster_warm":   (0.78, 0.72, 0.62, 1.0),
    "plaster_teal":   (0.24, 0.42, 0.42, 1.0),
    "terracotta":     (0.45, 0.20, 0.13, 1.0),
    "steel":          (0.55, 0.57, 0.60, 1.0),
    "steel_dark":     (0.22, 0.23, 0.25, 1.0),
    "ceramic_white":  (0.90, 0.89, 0.86, 1.0),
    "broth":          (0.42, 0.22, 0.08, 1.0),
    "noodle":         (0.92, 0.88, 0.76, 1.0),
    "beef_raw":       (0.60, 0.16, 0.18, 1.0),
    "beef_cooked":    (0.38, 0.22, 0.18, 1.0),
    "herb_green":     (0.18, 0.38, 0.14, 1.0),
    "onion_white":    (0.88, 0.86, 0.78, 1.0),
    "cloth_red":      (0.45, 0.11, 0.11, 1.0),
    "skin":           (0.72, 0.53, 0.40, 1.0),
    "shirt_blue":     (0.20, 0.28, 0.45, 1.0),
    "shirt_grey":     (0.35, 0.35, 0.38, 1.0),
}


def reset_scene():
    """Factory-fresh scene: no default cube, camera, or light."""
    bpy.ops.wm.read_factory_settings(use_empty=True)


def material(name, color_key, roughness=0.7, metallic=0.0):
    """
    Principled BSDF material. glTF exports base color / metallic / roughness
    natively, so these survive into Unity's URP with no manual setup.
    """
    mat = bpy.data.materials.get(name)
    if mat is None:
        mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = PALETTE[color_key]
        bsdf.inputs["Roughness"].default_value = roughness
        bsdf.inputs["Metallic"].default_value = metallic
    return mat


def assign(obj, mat):
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    return obj


def box(name, size, location=(0, 0, 0), mat=None, bevel=0.004):
    """
    Axis-aligned box. `size` is FULL extent (x, y, z), `location` is the
    centre. Applies a small bevel -- see the module docstring for why that
    matters more than poly count.
    """
    bpy.ops.mesh.primitive_cube_add(size=1, location=location)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (size[0], size[1], size[2])
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        add_bevel(obj, bevel)
    if mat:
        assign(obj, mat)
    return obj


def cylinder(name, radius, height, location=(0, 0, 0), verts=24, mat=None, bevel=0.003):
    """Cylinder standing on +Z, origin at its CENTRE (not base)."""
    bpy.ops.mesh.primitive_cylinder_add(radius=radius, depth=height, vertices=verts, location=location)
    obj = bpy.context.active_object
    obj.name = name
    if bevel:
        add_bevel(obj, bevel)
    if mat:
        assign(obj, mat)
    return obj


def add_bevel(obj, width=0.004, segments=2):
    mod = obj.modifiers.new("Bevel", "BEVEL")
    mod.width = width
    mod.segments = segments
    mod.limit_method = "ANGLE"
    mod.angle_limit = math.radians(40)
    mod.harden_normals = False
    return obj


def shade_smooth_by_angle(obj, degrees=35):
    """
    Smooth shading limited by angle -- rounded forms read as round, hard
    edges stay hard.

    API NOTE (this function was silently a NO-OP and every curved asset in
    the project shipped visibly faceted because of it): Blender REMOVED
    `Mesh.use_auto_smooth` / `auto_smooth_angle` in 4.1, and there is no
    "SMOOTH_BY_ANGLE" entry in the modifier type enum, so both branches of
    the previous implementation fell through without error. The supported
    path on 4.1+ (verified present on 5.1.1) is the operator
    `bpy.ops.object.shade_smooth_by_angle`, which attaches the stock
    Smooth by Angle geometry-nodes modifier.

    Fails soft: shading is cosmetic, so a future API change should degrade
    to flat shading, never break an asset build.
    """
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj

    try:
        bpy.ops.object.shade_smooth_by_angle(angle=math.radians(degrees))
        return obj
    except (AttributeError, TypeError, RuntimeError):
        pass

    try:
        bpy.ops.object.shade_auto_smooth(angle=math.radians(degrees))
        return obj
    except (AttributeError, TypeError, RuntimeError):
        pass

    for poly in obj.data.polygons:
        poly.use_smooth = True
    return obj


def join(objects, name):
    """Join a list of meshes into one object (first is the target)."""
    objects = [o for o in objects if o is not None]
    if not objects:
        return None
    bpy.ops.object.select_all(action="DESELECT")
    for o in objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    if len(objects) > 1:
        bpy.ops.object.join()
    obj = bpy.context.active_object
    obj.name = name
    return obj


def set_origin_to_floor(obj):
    """
    Move the object's origin to (centre-x, centre-y, min-z) -- i.e. where it
    rests on the ground. See the module docstring: this is what lets
    SceneBuilder place a model at a world position with no Y fudge factor.
    """
    bpy.context.view_layer.update()
    min_z = min((obj.matrix_world @ v.co).z for v in obj.data.vertices)
    xs = [(obj.matrix_world @ v.co).x for v in obj.data.vertices]
    ys = [(obj.matrix_world @ v.co).y for v in obj.data.vertices]
    centre = ((min(xs) + max(xs)) / 2.0, (min(ys) + max(ys)) / 2.0, min_z)

    bpy.context.scene.cursor.location = centre
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    bpy.context.scene.cursor.location = (0, 0, 0)
    obj.location = (0, 0, 0)
    return obj


def export_glb(obj_or_objects, out_path):
    """Export the given object(s) as a single .glb, +Y up for Unity."""
    objs = obj_or_objects if isinstance(obj_or_objects, (list, tuple)) else [obj_or_objects]
    objs = [o for o in objs if o is not None]

    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]

    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=out_path,
        export_format="GLB",
        use_selection=True,
        export_yup=True,
        export_apply=True,          # bake modifiers (the bevels) into the mesh
        export_materials="EXPORT",
        export_cameras=False,
        export_lights=False,
    )
    print(f"ART_EXPORTED {out_path}")


def export_fbx(obj_or_objects, out_path):
    """
    Export as FBX -- this is the format UNITY ACTUALLY CONSUMES.

    Why both formats: Unity does not import .glb natively (it needs the
    gltfast package), whereas FBX is a first-class native importer with zero
    package dependencies. Rather than take a package-resolution risk on the
    critical path, the pipeline writes FBX for Unity and keeps GLB as the
    portable interchange/preview format.

    apply_scale_options='FBX_SCALE_UNITS' + global_scale=1 keeps 1 Blender
    metre == 1 Unity unit, and axis_forward/axis_up convert Blender's +Z-up
    to Unity's +Y-up so models arrive upright with no rotation fixup.
    """
    objs = obj_or_objects if isinstance(obj_or_objects, (list, tuple)) else [obj_or_objects]
    objs = [o for o in objs if o is not None]

    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]

    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=out_path,
        use_selection=True,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        global_scale=1.0,
        axis_forward="-Z",
        axis_up="Y",
        object_types={"MESH"},
        use_mesh_modifiers=True,   # bake the bevels
        mesh_smooth_type="FACE",
        bake_space_transform=False,
        path_mode="COPY",
        embed_textures=False,
    )
    print(f"ART_EXPORTED_FBX {out_path}")


def render_preview(out_path, focus=(0, 0, 1.0), distance=6.0, elevation=35.0, azimuth=40.0, resolution=640):
    """
    Render a quick 3/4 preview so the asset can actually be LOOKED at rather
    than assumed correct. Preview only -- never part of the game build.
    """
    scene = bpy.context.scene

    # Sun + soft world fill, so shape reads without being blown out.
    bpy.ops.object.light_add(type="SUN", location=(4, -6, 8))
    sun = bpy.context.active_object
    sun.data.energy = 4.0
    sun.rotation_euler = (math.radians(50), 0, math.radians(35))

    world = bpy.data.worlds.new("PreviewWorld") if scene.world is None else scene.world
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.05, 0.06, 0.08, 1.0)
        bg.inputs[1].default_value = 1.0

    az, el = math.radians(azimuth), math.radians(elevation)
    cam_loc = (
        focus[0] + distance * math.cos(el) * math.sin(az),
        focus[1] - distance * math.cos(el) * math.cos(az),
        focus[2] + distance * math.sin(el),
    )
    bpy.ops.object.camera_add(location=cam_loc)
    cam = bpy.context.active_object
    scene.camera = cam

    # Aim the camera at the focus point.
    direction = (focus[0] - cam_loc[0], focus[1] - cam_loc[1], focus[2] - cam_loc[2])
    import mathutils
    rot = mathutils.Vector(direction).to_track_quat("-Z", "Y").to_euler()
    cam.rotation_euler = rot

    for engine in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "CYCLES"):
        try:
            scene.render.engine = engine
            break
        except TypeError:
            continue

    scene.render.resolution_x = resolution
    scene.render.resolution_y = resolution
    scene.render.film_transparent = False
    scene.render.filepath = out_path
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    bpy.ops.render.render(write_still=True)
    print(f"ART_PREVIEW {out_path}")


def out_paths(asset_name):
    """
    (glb_path, preview_path) for an asset, derived from the repo root so the
    scripts work regardless of the CWD Blender was launched from.
    """
    here = os.path.dirname(os.path.abspath(__file__))
    repo = os.path.abspath(os.path.join(here, "..", ".."))
    glb = os.path.join(repo, "Assets", "Art", "Generated", f"{asset_name}.glb")
    preview = os.path.join(repo, "art", "previews", f"{asset_name}.png")
    return glb, preview


def finish(obj, asset_name, preview=True, smooth_degrees=35, **preview_kwargs):
    """
    Standard tail end of every asset script: smooth shading, floor origin,
    export both formats, render a preview.

    Writes FBX (what Unity imports) AND GLB (portable interchange) -- see
    export_fbx's docstring for why both.

    Smoothing is applied HERE, centrally, rather than left to each asset
    script to remember -- angle-limited at 35 degrees, so cylinders and
    domes read as round while box edges (90 degrees) stay crisp. Pass
    smooth_degrees=None to opt out for a deliberately faceted asset.
    """
    if smooth_degrees:
        shade_smooth_by_angle(obj, smooth_degrees)
    set_origin_to_floor(obj)
    glb, png = out_paths(asset_name)
    export_glb(obj, glb)
    export_fbx(obj, glb[:-4] + ".fbx")
    if preview:
        render_preview(png, **preview_kwargs)
