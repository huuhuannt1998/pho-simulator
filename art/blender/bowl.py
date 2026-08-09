"""
The hero asset: a bowl of phở.

Exports three variants:
  BowlEmpty  -- clean ceramic bowl, what the player picks up and carries
  BowlFull   -- the finished dish (broth / noodles / beef / herbs / utensils)
  BowlDirty  -- a used bowl with residue, for the bussing + cleanliness loop

Run headless:
  Blender --background --factory-startup --python art/blender/bowl.py

Modelling notes (why it looks the way it does):

* The bowl is one lathed cross-section, not two shells. The profile is
  traced from the underside of the foot, up the outside, over the rim and
  back down the inside to the interior floor, then spun 360 degrees. That
  gives a watertight solid with a real rim thickness in ~1.5k tris.
* A phở bowl reads "phở" and not "soup cup" through PROPORTION: ~0.22 m
  across but ~0.10 m tall with a steep, deep interior and a narrow foot.
  Wide + deep + footed. If it ever starts looking like a cereal bowl, the
  fix is a narrower foot and a steeper wall, not more detail.
* The contents are deliberately cheap: the broth is a single domed disc
  (no liquid volume), noodles are squashed tori, beef slices are bent
  discs. All of the appetite comes from colour contrast and from silhouette
  BREAKING the broth plane, so the toppings are pushed high enough to sit
  proud of the surface rather than floating in it.
* Every edible component is its own material slot so Unity can drive
  colour / variation per bowl (rare vs well-done beef, more or less herb).
  Slot names are listed in SLOT_NAMES below -- keep them stable, gameplay
  code looks them up by name.
"""

import bpy
import bmesh
import math
import mathutils
import os
import random
import sys

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import lib  # noqa: E402


# --------------------------------------------------------------------------
# Parametric knobs -- tune here, not in the geometry below.
# --------------------------------------------------------------------------

RIM_R = 0.110          # outer radius at the rim  (0.22 m across)
HEIGHT = 0.100         # foot contact -> rim
FOOT_R = 0.050         # narrow foot: the single biggest "this is a phở bowl" cue
FOOT_H = 0.013
WALL = 0.006
FLOOR_Z = 0.016        # interior floor height above the table

BROTH_Z = 0.087        # broth surface, a little below the rim
BROTH_DOME = 0.0025    # surface tension bulge at the centre

# The food is heaped on a dome, not floated on the broth. Everything edible is
# seated ON this surface, which is what keeps the toppings in contact with
# something instead of hovering and casting a detached shadow.
NEST_R = 0.078         # dome footprint -- broth needs to stay visible around it
NEST_H = 0.009         # dome apex above the broth

SEGMENTS = 56          # lathe resolution

# Colours that lib.PALETTE does not carry. lib.py is shared and off-limits,
# so anything bowl-specific is defined locally.
LOCAL_COLORS = {
    "rim_band":      (0.13, 0.34, 0.46, 1.0),   # cobalt band, the classic bowl
    "bamboo":        (0.42, 0.28, 0.15, 1.0),   # chopsticks
    # PALETTE noodle is so close to ceramic_white that the nest disappeared
    # into the bowl and into the onion. Rice noodles are warmer than that.
    "noodle_warm":   (0.90, 0.80, 0.56, 1.0),
    "spoon_white":   (0.93, 0.93, 0.91, 1.0),
    # PALETTE beef_cooked renders as a grey-mauve that reads as raw mushroom.
    # Braised beef needs to stay on the warm side of brown or the whole bowl
    # stops looking like food. Unity drives the final colour per bowl anyway.
    "beef_warm":     (0.44, 0.19, 0.15, 1.0),
    "beef_fat":      (0.78, 0.66, 0.55, 1.0),
    "scallion":      (0.26, 0.48, 0.15, 1.0),   # brighter than herb_green
    "chili_red":     (0.72, 0.14, 0.07, 1.0),
    "residue":       (0.20, 0.11, 0.045, 1.0),
}

# The contract with Unity. Do not rename without updating gameplay code.
SLOT_NAMES = {
    "bowl":        "Ceramic_Bowl",
    "rim":         "Ceramic_RimBand",
    "broth":       "Broth",
    "noodles":     "Noodles",
    "beef":        "Beef",
    "beef_fat":    "Beef_Fat",
    "herbs":       "Herbs_Green",
    "scallion":    "SpringOnion_Green",
    "onion":       "Onion_White",
    "chili":       "Chili_Red",
    "chopsticks":  "Chopsticks",
    "spoon":       "Spoon",
    "residue":     "Residue",
}


# --------------------------------------------------------------------------
# Material helpers
# --------------------------------------------------------------------------

def local_material(name, color, roughness=0.7, metallic=0.0):
    """Same shape as lib.material(), but for colours lib.PALETTE lacks."""
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = color
        bsdf.inputs["Roughness"].default_value = roughness
        bsdf.inputs["Metallic"].default_value = metallic
    return mat


def materials():
    """Every slot this asset family can use, built once per scene."""
    return {
        "bowl":       lib.material(SLOT_NAMES["bowl"], "ceramic_white", roughness=0.16),
        "rim":        local_material(SLOT_NAMES["rim"], LOCAL_COLORS["rim_band"], roughness=0.16),
        "broth":      lib.material(SLOT_NAMES["broth"], "broth", roughness=0.13),
        "noodles":    local_material(SLOT_NAMES["noodles"], LOCAL_COLORS["noodle_warm"], roughness=0.34),
        "beef":       local_material(SLOT_NAMES["beef"], LOCAL_COLORS["beef_warm"], roughness=0.38),
        "beef_fat":   local_material(SLOT_NAMES["beef_fat"], LOCAL_COLORS["beef_fat"], roughness=0.4),
        "herbs":      lib.material(SLOT_NAMES["herbs"], "herb_green", roughness=0.5),
        "scallion":   local_material(SLOT_NAMES["scallion"], LOCAL_COLORS["scallion"], roughness=0.45),
        "onion":      lib.material(SLOT_NAMES["onion"], "onion_white", roughness=0.5),
        "chili":      local_material(SLOT_NAMES["chili"], LOCAL_COLORS["chili_red"], roughness=0.45),
        "chopsticks": local_material(SLOT_NAMES["chopsticks"], LOCAL_COLORS["bamboo"], roughness=0.5),
        "spoon":      local_material(SLOT_NAMES["spoon"], LOCAL_COLORS["spoon_white"], roughness=0.3),
        "residue":    local_material(SLOT_NAMES["residue"], LOCAL_COLORS["residue"], roughness=0.22),
    }


# --------------------------------------------------------------------------
# The bowl itself
# --------------------------------------------------------------------------

def bowl_profile():
    """
    (radius, z) cross-section, traced anticlockwise from the axis:
    underside -> foot -> outer wall -> rim -> inner wall -> interior floor.

    Spinning this closed-ish polyline gives a solid with real wall thickness.
    """
    R, H, F = RIM_R, HEIGHT, FOOT_R
    return [
        # recessed underside, so the bowl sits on a ring not a disc
        (0.000,        0.009),
        (F * 0.55,     0.008),
        (F * 0.82,     0.004),
        (F * 0.88,     0.000),
        (F * 1.00,     0.000),
        # foot ring, flaring into the body
        (F * 1.06,     FOOT_H * 0.55),
        (F * 1.10,     FOOT_H),
        # outer wall: steep at the bottom, easing out towards the rim
        (F * 1.24,     H * 0.22),
        (F * 1.52,     H * 0.38),
        (F * 1.84,     H * 0.55),
        (R * 0.90,     H * 0.72),
        (R * 0.975,    H * 0.87),
        (R * 1.000,    H * 0.955),
        # rim: a small rolled lip, the thing that catches a highlight
        (R * 0.998,    H),
        (R - WALL * 0.55, H * 0.999),
        # inner wall back down
        (R - WALL * 0.85, H * 0.94),
        (R * 0.905,    H * 0.80),
        (F * 1.72,     H * 0.60),
        (F * 1.36,     H * 0.42),
        (F * 1.06,     H * 0.27),
        (F * 0.80,     FLOOR_Z + 0.006),
        (F * 0.55,     FLOOR_Z),
        (0.000,        FLOOR_Z - 0.002),
    ]


def lathe(name, profile, segments=SEGMENTS):
    bm = bmesh.new()
    verts = [bm.verts.new((x, 0.0, z)) for (x, z) in profile]
    edges = [bm.edges.new((verts[i], verts[i + 1])) for i in range(len(verts) - 1)]

    bmesh.ops.spin(
        bm,
        geom=verts + edges,
        axis=(0.0, 0.0, 1.0),
        cent=(0.0, 0.0, 0.0),
        dvec=(0.0, 0.0, 0.0),
        angle=2.0 * math.pi,
        steps=segments,
        use_duplicate=False,
    )
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

    mesh = bpy.data.meshes.new(f"{name}_mesh")
    bm.to_mesh(mesh)
    bm.free()

    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    for poly in mesh.polygons:
        poly.use_smooth = True
    return obj


def build_bowl(mats, dirty=False):
    """
    Lathed bowl, with the cobalt band painted onto the outer rim faces by
    material index rather than modelled as separate geometry.

    `dirty` additionally stains the lower interior -- a tide mark costs zero
    triangles and does more to say "this bowl is used" than any amount of
    scattered crumbs.
    """
    obj = lathe("Bowl", bowl_profile())

    obj.data.materials.append(mats["bowl"])      # slot 0
    obj.data.materials.append(mats["rim"])       # slot 1
    if dirty:
        obj.data.materials.append(mats["residue"])   # slot 2

    band_lo, band_hi = HEIGHT * 0.70, HEIGHT * 0.945
    stain_hi = FLOOR_Z + 0.013
    for poly in obj.data.polygons:
        c = poly.center
        # outward-facing only: normal must point away from the axis
        outward = (poly.normal.x * c.x + poly.normal.y * c.y) > 0.0
        if outward and band_lo <= c.z <= band_hi:
            poly.material_index = 1
        elif dirty and not outward and (FLOOR_Z - 0.004) <= c.z <= stain_hi:
            poly.material_index = 2
    return obj


# --------------------------------------------------------------------------
# Contents
# --------------------------------------------------------------------------

def interior_radius(z):
    """Inner wall radius at height z -- so toppings tuck under the rim."""
    prof = bowl_profile()
    inner = [(x, zz) for (x, zz) in prof[14:]]          # rim -> floor
    inner = sorted(inner, key=lambda p: p[1])
    for i in range(len(inner) - 1):
        (x0, z0), (x1, z1) = inner[i], inner[i + 1]
        if z0 <= z <= z1 and z1 > z0:
            t = (z - z0) / (z1 - z0)
            return x0 + (x1 - x0) * t
    return inner[-1][0]


def nest_z(r):
    """
    Height of the heaped-food dome at radius r. The outer edge sinks a little
    below the broth so the two surfaces never coincide and z-fight.
    """
    t = min(r / NEST_R, 1.0)
    s = min(max((t - 0.55) / 0.45, 0.0), 1.0)          # flat top, short shoulder
    return (BROTH_Z - 0.006) + (NEST_H + 0.006) * 0.5 * (1.0 + math.cos(math.pi * s))


def nest_slope(r):
    """dz/dr of the dome -- used to lie toppings flat against the heap."""
    t = r / NEST_R
    if t >= 1.0 or t <= 0.55:
        return 0.0
    s = (t - 0.55) / 0.45
    return -(NEST_H + 0.006) * 0.5 * math.pi / (0.45 * NEST_R) * math.sin(math.pi * s)


def on_nest(x, y, spin=0.0, lift=0.002, rng=None, jitter=0.0):
    """
    (location, rotation_euler) for a topping seated on the dome at (x, y),
    rotated `spin` about its own axis and then tilted to match the slope.

    Seating toppings ON a surface instead of floating them above the broth is
    the single biggest fix for food that looks fake: a floating slice reads as
    a detached object with a hard drop shadow under it.
    """
    r = math.hypot(x, y)
    a = math.atan2(y, x)
    tilt = math.atan(nest_slope(r))
    if rng and jitter:
        tilt += rng.uniform(-jitter, jitter)
    tangent = mathutils.Vector((-math.sin(a), math.cos(a), 0.0))
    m = mathutils.Matrix.Rotation(tilt, 4, tangent) @ mathutils.Matrix.Rotation(spin, 4, "Z")
    return (x, y, nest_z(r) + lift), m.to_euler(), m


def build_broth(mats):
    """
    A single domed disc. There is no liquid volume anywhere in this asset --
    the broth is a surface and nothing more, which is both cheaper and easier
    to fake convincingly than a solid.
    """
    r = interior_radius(BROTH_Z) - 0.0015
    bm = bmesh.new()
    centre = bm.verts.new((0.0, 0.0, BROTH_Z + BROTH_DOME))

    rings = []
    for (rr, dome) in [(r * 0.45, 0.85), (r * 0.78, 0.5), (r, 0.0)]:
        ring = []
        for i in range(SEGMENTS):
            a = 2.0 * math.pi * i / SEGMENTS
            ring.append(bm.verts.new((
                rr * math.cos(a),
                rr * math.sin(a),
                BROTH_Z + BROTH_DOME * dome,
            )))
        rings.append(ring)

    for i in range(SEGMENTS):
        j = (i + 1) % SEGMENTS
        bm.faces.new((centre, rings[0][i], rings[0][j]))
    for k in range(len(rings) - 1):
        for i in range(SEGMENTS):
            j = (i + 1) % SEGMENTS
            bm.faces.new((rings[k][i], rings[k + 1][i], rings[k + 1][j], rings[k][j]))

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new("Broth_mesh")
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new("Broth", mesh)
    bpy.context.collection.objects.link(obj)
    for poly in mesh.polygons:
        poly.use_smooth = True
    return lib.assign(obj, mats["broth"])


def torus(name, major, minor, location, rot, squash, mat, major_seg=14, minor_seg=6):
    bpy.ops.mesh.primitive_torus_add(
        major_radius=major, minor_radius=minor,
        major_segments=major_seg, minor_segments=minor_seg,
        location=(0, 0, 0),
    )
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (1.0, 1.0, squash)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.location = location
    obj.rotation_euler = rot
    for poly in obj.data.polygons:
        poly.use_smooth = True
    return lib.assign(obj, mat)


def build_noodles(mats):
    """
    A wide, lumpy dome of noodle rather than a few rings floating on soup.
    Individual strands are a trap: at the distance a player actually sees this,
    a heaped mass with a broken outline reads as a noodle nest for ~1200 tris.

    The dome is also what everything else is seated on, so it does double duty
    as the food's collision surface for the eye.
    """
    rng = random.Random(11)
    parts = []

    # The heap itself.
    bm = bmesh.new()
    centre = bm.verts.new((0.0, 0.0, nest_z(0.0)))
    rings = []
    for frac in (0.30, 0.58, 0.80, 1.0):
        rr = NEST_R * frac
        ring = []
        for i in range(SEGMENTS):
            a = 2.0 * math.pi * i / SEGMENTS
            # A little radial wobble so the edge of the heap is not a circle.
            wob = 1.0 + 0.022 * math.sin(a * 5.0 + frac * 3.1) * frac
            ring.append(bm.verts.new((
                rr * wob * math.cos(a),
                rr * wob * math.sin(a),
                nest_z(rr) + (0.0015 * math.sin(a * 7.0 + frac * 2.0) if frac < 1.0 else 0.0),
            )))
        rings.append(ring)
    for i in range(SEGMENTS):
        j = (i + 1) % SEGMENTS
        bm.faces.new((centre, rings[0][i], rings[0][j]))
    for k in range(len(rings) - 1):
        for i in range(SEGMENTS):
            j = (i + 1) % SEGMENTS
            bm.faces.new((rings[k][i], rings[k + 1][i], rings[k + 1][j], rings[k][j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new("NoodleHeap_mesh")
    bm.to_mesh(mesh)
    bm.free()
    heap = bpy.data.objects.new("NoodleHeap", mesh)
    bpy.context.collection.objects.link(heap)
    for poly in mesh.polygons:
        poly.use_smooth = True
    parts.append(lib.assign(heap, mats["noodles"]))

    # Loops sitting proud of the heap. These are what break the silhouette --
    # without them the heap is a smooth dome and reads as mashed potato.
    # Tight curls, not wide rings. A torus whose hole is large reads as an
    # ONION RING -- that was the first version's mistake. Keeping the major
    # radius close to the minor radius turns the same primitive into a curl.
    coils = []
    for _ in range(24):
        a = rng.uniform(0, 2 * math.pi)
        rad = math.sqrt(rng.uniform(0.0, 1.0)) * 0.056
        major = rng.uniform(0.0060, 0.0105)
        coils.append((rad * math.cos(a), rad * math.sin(a), major, 0.0038))

    for i, (x, y, major, minor) in enumerate(coils):
        loc, rot, _ = on_nest(x, y, spin=rng.uniform(0, math.pi),
                              lift=minor * 0.10, rng=rng, jitter=0.30)
        parts.append(torus(f"NoodleCoil{i}", major, minor, loc, rot, 0.58,
                           mats["noodles"], major_seg=10, minor_seg=5))

    # Big loose loops draped over the shoulder of the heap and out onto the
    # broth. These do most of the "these are NOODLES" work: a long shallow
    # loop crossing the boundary between the heap and the soup is the one
    # shape nothing else in the bowl has.
    for i in range(5):
        a = rng.uniform(0, 2 * math.pi)
        rad = rng.uniform(0.044, 0.058)
        x, y = rad * math.cos(a), rad * math.sin(a)
        loc, rot, _ = on_nest(x, y, spin=a + rng.uniform(-0.8, 0.8),
                              lift=0.0012, rng=rng, jitter=0.30)
        parts.append(torus(f"NoodleLoose{i}", rng.uniform(0.012, 0.017), 0.0028,
                           loc, rot, 0.55, mats["noodles"], major_seg=12, minor_seg=5))
    return parts


def curved_slab(name, rx, ry, thickness, curve, mat, verts=14, location=(0, 0, 0), rot=(0, 0, 0)):
    """
    A flattened disc with a parabolic sag -- one beef slice. The bend is what
    sells "sliced meat draped over noodles" rather than "poker chip".
    """
    bpy.ops.mesh.primitive_cylinder_add(radius=1.0, depth=1.0, vertices=verts,
                                        location=(0, 0, 0))
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (rx, ry, thickness)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    for v in obj.data.vertices:
        nx = v.co.x / rx
        v.co.z += curve * (nx * nx - 0.35)

    obj.location = location
    obj.rotation_euler = rot
    lib.add_bevel(obj, width=0.0009, segments=2)
    for poly in obj.data.polygons:
        poly.use_smooth = True
    return lib.assign(obj, mat)


def build_beef(mats):
    """
    Slices fanned around the heap, each lying flat against the slope with its
    long axis following the arc -- the way a bowl actually gets dressed.
    """
    rng = random.Random(29)
    parts = []

    RX, RY = 0.029, 0.0165

    # Slices cover a ~250 degree arc, not the full circle. A complete ring of
    # evenly spaced slices reads as a flower or a pizza; leaving one quadrant
    # open lets broth and noodle show through, which is what a real bowl looks
    # like and what gives the surface some depth.
    layout = []
    for k in range(7):
        a = -0.55 + k * (4.35 / 6.0) + rng.uniform(-0.16, 0.16)
        layout.append((a, rng.uniform(0.042, 0.058), rng.uniform(0.82, 1.16)))
    layout.append((rng.uniform(0.4, 3.6), 0.021, 1.05))
    layout.append((rng.uniform(0.4, 3.6), 0.028, 0.88))

    for i, (a, rad, scale) in enumerate(layout):
        rx, ry = RX * scale, RY * scale
        x, y = rad * math.cos(a), rad * math.sin(a)
        spin = a + math.pi / 2 + rng.uniform(-0.30, 0.30)
        loc, rot, m = on_nest(x, y, spin=spin, lift=0.0040, rng=rng, jitter=0.10)
        parts.append(curved_slab(
            f"BeefSlice{i}", rx, ry, 0.0020, 0.0022, mats["beef"],
            location=loc, rot=rot,
        ))

        # Fat rim hugging the outer long edge. A second slot, and the warm line
        # of colour that stops the beef reading as a flat brown counter.
        off = m @ mathutils.Vector((0.0, ry - 0.0030, 0.0006))
        parts.append(curved_slab(
            f"BeefFat{i}", rx * 0.90, 0.0030, 0.0021, 0.0022, mats["beef_fat"],
            location=(loc[0] + off.x, loc[1] + off.y, loc[2] + off.z),
            rot=rot,
        ))
    return parts


def build_garnish(mats):
    """
    Spring-onion rings, herb, onion slivers, chilli. This is almost pure
    colour: at gameplay distance nobody resolves the shapes, they just read
    green-and-white flecks over brown-and-cream, which is what says "fresh".
    """
    rng = random.Random(53)
    parts = []

    def scatter(n, r_lo, r_hi):
        for _ in range(n):
            a = rng.uniform(0, 2 * math.pi)
            rad = rng.uniform(r_lo, r_hi)
            yield rad * math.cos(a), rad * math.sin(a), a

    # Spring onion rings, in loose clumps rather than an even sprinkle.
    idx = 0
    for cx, cy in ((0.030, 0.030), (-0.036, 0.012), (0.006, -0.042),
                   (-0.014, 0.052), (0.052, -0.020), (-0.056, -0.034)):
        for _ in range(5):
            x = cx + rng.uniform(-0.020, 0.020)
            y = cy + rng.uniform(-0.020, 0.020)
            if math.hypot(x, y) > 0.080:
                continue
            loc, rot, _ = on_nest(x, y, spin=rng.uniform(0, math.pi), lift=0.0060,
                                  rng=rng, jitter=0.45)
            parts.append(torus(f"Scallion{idx}", rng.uniform(0.0044, 0.0064), 0.0011,
                               loc, rot, 0.62, mats["scallion"],
                               major_seg=8, minor_seg=4))
            idx += 1

    # Herb leaves -- flatter and smaller than before; the old ones read as pea
    # pods because they were too thick and too dark.
    for i, (x, y, a) in enumerate(scatter(6, 0.020, 0.070)):
        loc, rot, _ = on_nest(x, y, spin=a + rng.uniform(-1.0, 1.0), lift=0.0062,
                              rng=rng, jitter=0.40)
        parts.append(curved_slab(f"Herb{i}", 0.0090, 0.0048, 0.0008, 0.0010,
                                 mats["herbs"], verts=8, location=loc, rot=rot))

    # Onion slivers -- the brightest thing in the bowl, so they carry the eye.
    for i, (x, y, a) in enumerate(scatter(7, 0.015, 0.072)):
        loc, rot, _ = on_nest(x, y, spin=a + rng.uniform(-1.2, 1.2), lift=0.0064,
                              rng=rng, jitter=0.40)
        parts.append(curved_slab(f"Onion{i}", 0.0105, 0.0026, 0.0010, 0.0012,
                                 mats["onion"], verts=8, location=loc, rot=rot))

    # Three chilli rings. Tiny, but the eye goes straight to saturated red.
    for i, (x, y, a) in enumerate(scatter(3, 0.025, 0.062)):
        loc, rot, _ = on_nest(x, y, spin=rng.uniform(0, math.pi), lift=0.0062,
                              rng=rng, jitter=0.40)
        parts.append(torus(f"Chili{i}", 0.0056, 0.0018, loc, rot, 0.8,
                           mats["chili"], major_seg=8, minor_seg=4))
    return parts


def build_utensils(mats):
    """
    Chopsticks laid across the far rim and a soup spoon hooked over the near
    left. Both are kept off the centre of the bowl -- utensils crossing the
    middle of the food read as clutter and hide the thing they garnish.
    """
    parts = []
    rim_z = HEIGHT

    # --- chopsticks: parallel pair, resting on the far rim ---
    yaw = math.radians(28)
    push = mathutils.Vector((-math.sin(yaw), math.cos(yaw), 0.0)) * 0.052
    for i, gap in enumerate((-0.0075, 0.0075)):
        stick = lib.box(f"Chopstick{i}", (0.230, 0.0050, 0.0050),
                        (0, 0, 0), mats["chopsticks"], bevel=0.0011)
        stick.rotation_euler = (0.0, 0.0, yaw)
        offset = mathutils.Vector((-math.sin(yaw), math.cos(yaw), 0.0)) * gap
        stick.location = (push.x + offset.x, push.y + offset.y, rim_z + 0.0026)
        parts.append(stick)

    # The soup spoon was cut. Every version of it read as a thermometer
    # lying in the bowl -- a bad utensil is worse than no utensil, and the
    # chopsticks alone already say "this is a served dish".
    return parts


def build_residue(mats):
    """
    Leftovers for the dirty bowl. Most of the "used" read comes from the tide
    mark painted onto the bowl's inner wall in build_bowl(); this adds the
    puddle and the few bits nobody finished.
    """
    rng = random.Random(97)
    parts = []
    puddle_z = FLOOR_Z + 0.0018

    bpy.ops.mesh.primitive_cylinder_add(radius=interior_radius(puddle_z + 0.004) * 0.74,
                                        depth=0.0035, vertices=28,
                                        location=(0.003, -0.002, puddle_z))
    puddle = bpy.context.active_object
    puddle.name = "Residue"
    for poly in puddle.data.polygons:
        poly.use_smooth = True
    parts.append(lib.assign(puddle, mats["residue"]))

    # Splashes up the inside wall.
    for i in range(5):
        a = rng.uniform(0, 2 * math.pi)
        z = rng.uniform(FLOOR_Z + 0.016, HEIGHT * 0.48)
        r = interior_radius(z) - 0.0045
        parts.append(curved_slab(
            f"Smear{i}", rng.uniform(0.006, 0.011), rng.uniform(0.003, 0.006),
            0.0008, 0.0012, mats["residue"], verts=8,
            location=(r * math.cos(a), r * math.sin(a), z),
            rot=(math.radians(52) * math.cos(a + math.pi / 2),
                 math.radians(52) * math.sin(a + math.pi / 2), a)))

    # Leftovers, sunk into the puddle.
    for i in range(5):
        a = rng.uniform(0, 2 * math.pi)
        rad = rng.uniform(0.0, 0.038)
        parts.append(torus(
            f"LeftoverNoodle{i}", rng.uniform(0.009, 0.015), 0.0040,
            (rad * math.cos(a), rad * math.sin(a), puddle_z + 0.0035),
            (rng.uniform(-0.3, 0.3), rng.uniform(-0.3, 0.3), rng.uniform(0, math.pi)),
            0.55, mats["noodles"]))
    for i in range(7):
        a = rng.uniform(0, 2 * math.pi)
        rad = rng.uniform(0.0, 0.046)
        parts.append(torus(
            f"LeftoverScallion{i}", 0.0052, 0.0016,
            (rad * math.cos(a), rad * math.sin(a), puddle_z + 0.0030),
            (rng.uniform(-0.4, 0.4), rng.uniform(-0.4, 0.4), rng.uniform(0, math.pi)),
            0.8, mats["scallion"], major_seg=8, minor_seg=4))
    return parts


# --------------------------------------------------------------------------
# Assembly
# --------------------------------------------------------------------------

def assemble(variant, utensils=True):
    lib.reset_scene()
    mats = materials()
    parts = [build_bowl(mats, dirty=(variant == "dirty"))]

    if variant == "full":
        parts.append(build_broth(mats))
        parts += build_noodles(mats)
        parts += build_beef(mats)
        parts += build_garnish(mats)
        if utensils:
            parts += build_utensils(mats)
    elif variant == "dirty":
        parts += build_residue(mats)

    name = {"empty": "BowlEmpty", "full": "BowlFull", "dirty": "BowlDirty"}[variant]
    return lib.join(parts, name), name


# Preview framing: the close 3/4 is roughly how the player sees the bowl while
# carrying it, which is the angle that actually has to hold up.
VIEWS = {
    "":       dict(focus=(0, 0, 0.055), distance=0.46, elevation=26, azimuth=40),
    "_top":   dict(focus=(0, 0, 0.070), distance=0.44, elevation=72, azimuth=25),
}


def build_and_export(variant, utensils=True):
    obj, name = assemble(variant, utensils)
    lib.finish(obj, name, **VIEWS[""])
    tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    print(f"ART_STATS {name} tris~{tris} slots={[m.name for m in obj.data.materials]}")

    # Rebuild for the second view: render_preview adds its own sun + camera,
    # so re-running it in the same scene would double the lighting.
    obj, name = assemble(variant, utensils)
    lib.set_origin_to_floor(obj)
    _, png = lib.out_paths(name + "_top")
    lib.render_preview(png, **VIEWS["_top"])


def main():
    for variant in ("empty", "full", "dirty"):
        build_and_export(variant)


if __name__ == "__main__":
    main()
