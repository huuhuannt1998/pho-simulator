"""Parametric phở bowl generator.

Run headless:
    blender --background --python art/blender/scripts/bowl.py -- --variant regular

Or run all variants via `make art` (see art/blender/Makefile).

Design intent (GDD sect. 40): stylized realism, warm readable silhouette.
This script produces a clean lathed bowl profile (foot -> body -> rim lip)
plus a simple two-tone ceramic material. It intentionally does NOT try to
model food contents — broth/noodles/toppings are separate meshes assembled
at runtime in Unity so recipe contents can vary per-bowl.

Parametric knobs live in VARIANTS below. Add a variant instead of hand-editing
geometry so regenerating art is a data change, not a remodel.
"""

import bpy
import bmesh
import math
import os
import sys

# ---------------------------------------------------------------------------
# Variants: (name, rim_radius_m, height_m, wall_thickness_m)
# ---------------------------------------------------------------------------
VARIANTS = {
    "small":   dict(rim_radius=0.075, height=0.055, wall=0.004),
    "regular": dict(rim_radius=0.090, height=0.065, wall=0.005),
    "large":   dict(rim_radius=0.105, height=0.075, wall=0.006),
    "special": dict(rim_radius=0.115, height=0.080, wall=0.006),  # Pho Dac Biet
}

OUT_DIR = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "out")


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block_collection in (bpy.data.meshes, bpy.data.materials, bpy.data.images):
        for block in list(block_collection):
            if block.users == 0:
                block_collection.remove(block)


def build_bowl_profile(rim_radius, height, wall, foot_radius_ratio=0.45, lip_flare=1.12):
    """Return a list of (x, z) profile points, foot -> rim, for lathing.

    x = radius from center axis, z = height from base.
    A flared lip (lip_flare > 1) is what makes a bowl read as a bowl rather
    than a bucket, and matters more to silhouette than polycount.
    """
    foot_r = rim_radius * foot_radius_ratio
    body_r = rim_radius
    lip_r = rim_radius * lip_flare

    return [
        (0.0,               0.0),
        (foot_r * 0.9,      0.0),
        (foot_r,            height * 0.06),
        (foot_r * 1.05,     height * 0.12),
        (body_r * 0.55,     height * 0.45),
        (body_r * 0.85,     height * 0.75),
        (body_r,            height * 0.92),
        (lip_r,             height * 0.98),
        (lip_r * 0.97,      height),
    ], wall


def lathe_profile(name, profile_points, wall, segments=48):
    """Build a bowl mesh by lathing an outer profile and a thinned inner profile."""
    bm = bmesh.new()

    def ring(points, z_offset=0.0, x_scale=1.0):
        verts_by_row = []
        for (x, z) in points:
            row = []
            for i in range(segments):
                angle = 2.0 * math.pi * i / segments
                vx = x * x_scale * math.cos(angle)
                vy = x * x_scale * math.sin(angle)
                row.append(bm.verts.new((vx, vy, z + z_offset)))
            verts_by_row.append(row)
        return verts_by_row

    outer_rows = ring(profile_points)
    inner_points = [(max(x - wall, 0.0), z) for (x, z) in profile_points]
    inner_rows = ring(inner_points, z_offset=0.0)

    def stitch(rows, flip=False):
        for r in range(len(rows) - 1):
            a, b = rows[r], rows[r + 1]
            for i in range(segments):
                i2 = (i + 1) % segments
                v1, v2, v3, v4 = a[i], a[i2], b[i2], b[i]
                face = (v1, v4, v3, v2) if flip else (v1, v2, v3, v4)
                try:
                    bm.faces.new(face)
                except ValueError:
                    pass  # duplicate face at pinched foot vertex, safe to skip

    stitch(outer_rows, flip=False)
    stitch(inner_rows, flip=True)

    # Cap the rim: connect outer top ring to inner top ring.
    top_outer, top_inner = outer_rows[-1], inner_rows[-1]
    for i in range(segments):
        i2 = (i + 1) % segments
        try:
            bm.faces.new((top_outer[i], top_outer[i2], top_inner[i2], top_inner[i]))
        except ValueError:
            pass

    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

    mesh = bpy.data.meshes.new(f"{name}_mesh")
    bm.to_mesh(mesh)
    bm.free()

    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)

    mod = obj.modifiers.new(name="Smooth", type="SUBSURF")
    mod.levels = 1
    mod.render_levels = 2

    return obj


def make_ceramic_material(name, base_color=(0.92, 0.90, 0.85, 1.0), glaze_tint=(0.55, 0.62, 0.60, 1.0)):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = base_color
        bsdf.inputs["Roughness"].default_value = 0.25
        if "Specular IOR Level" in bsdf.inputs:
            bsdf.inputs["Specular IOR Level"].default_value = 0.6
    return mat


def export_glb(obj, filepath):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.gltf(
        filepath=filepath,
        use_selection=True,
        export_format="GLB",
        export_apply=True,
    )


def build_variant(variant_name):
    if variant_name not in VARIANTS:
        raise ValueError(f"Unknown bowl variant '{variant_name}'. Options: {list(VARIANTS)}")

    params = VARIANTS[variant_name]
    clear_scene()

    profile, wall = build_bowl_profile(
        rim_radius=params["rim_radius"],
        height=params["height"],
        wall=params["wall"],
    )
    obj = lathe_profile(f"pho_bowl_{variant_name}", profile, wall)
    obj.data.materials.append(make_ceramic_material(f"mat_bowl_{variant_name}"))

    os.makedirs(OUT_DIR, exist_ok=True)
    out_path = os.path.join(OUT_DIR, f"pho_bowl_{variant_name}.glb")
    export_glb(obj, out_path)
    print(f"[bowl.py] wrote {out_path}")
    return out_path


def parse_args():
    argv = sys.argv
    if "--" in argv:
        argv = argv[argv.index("--") + 1:]
    else:
        argv = []
    variant = "regular"
    if "--variant" in argv:
        variant = argv[argv.index("--variant") + 1]
    return variant


if __name__ == "__main__":
    requested = parse_args()
    if requested == "all":
        for name in VARIANTS:
            build_variant(name)
    else:
        build_variant(requested)
