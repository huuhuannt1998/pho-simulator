"""
The kitchen line for Pho Simulator: broth pot, prep counter, ingredient bin,
service pass, and a two-burner range.

These are all "walk up and press E" props, so the design brief is SILHOUETTE
FIRST. Each one has to be identifiable from across the room at standing eye
height (~1.6 m), which is why every asset here carries one big shape gesture
that nothing else in the kitchen shares:

  BrothPot       -- fat tapered drum + ajar lid, the only round mass on the line
  KitchenCounter -- plain slab, deliberately the quiet one
  IngredientBin  -- small open tray with a visible coloured fill
  PassCounter    -- overhead gantry with heat lamps; the only thing with air
                    under a raised shelf
  Stove          -- low box with two black grates cut into a bright top

Style: working commercial kitchen. Stainless everywhere, scuffed, cast iron
where it gets hot.

Run headless:
  Blender --background --factory-startup --python art/blender/kitchen.py
"""

import bpy
import math
import os
import sys

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import lib  # noqa: E402


# --------------------------------------------------------------------------
# Local palette. lib.PALETTE is shared and off-limits for edits, so kitchen
# specific tones live here.
# --------------------------------------------------------------------------

STEEL_BRIGHT = (0.62, 0.64, 0.67, 1.0)   # polished worktop
STEEL_BODY = (0.50, 0.52, 0.55, 1.0)     # cabinet / pot body
STEEL_SCUFF = (0.40, 0.42, 0.44, 1.0)    # legs, shelves, undersides
CAST_IRON = (0.10, 0.10, 0.11, 1.0)      # burner rings, grates
ENAMEL_BLACK = (0.06, 0.06, 0.07, 1.0)   # knobs, burner caps
LAMP_WARM = (0.95, 0.62, 0.22, 1.0)      # heat-lamp tubes at the pass
BRASS = (0.55, 0.42, 0.18, 1.0)          # gas fittings
CONTENTS_NEUTRAL = (0.72, 0.70, 0.62, 1.0)  # placeholder; recoloured in Unity

# Steel look. Kept at a middling metallic so the preview render is readable;
# Unity's URP can push metallic higher with a proper reflection probe.
STEEL_METAL = 0.55
STEEL_ROUGH = 0.38


# --------------------------------------------------------------------------
# Local primitives lib.py does not cover (tapered bodies, rings, domes).
# --------------------------------------------------------------------------

def mat(name, rgba, roughness=0.5, metallic=0.0):
    """Principled material from a literal colour instead of a PALETTE key."""
    m = bpy.data.materials.get(name)
    if m is None:
        m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = rgba
        bsdf.inputs["Roughness"].default_value = roughness
        bsdf.inputs["Metallic"].default_value = metallic
    return m


def steel(name, rgba=STEEL_BODY, roughness=STEEL_ROUGH):
    return mat(name, rgba, roughness, STEEL_METAL)


def taper(name, r_bottom, r_top, height, location=(0, 0, 0), verts=32,
          material=None, bevel=0.004):
    """Truncated cone standing on +Z, origin at its centre."""
    bpy.ops.mesh.primitive_cone_add(
        radius1=r_bottom, radius2=r_top, depth=height,
        vertices=verts, location=location)
    obj = bpy.context.active_object
    obj.name = name
    if bevel:
        lib.add_bevel(obj, bevel)
    if material:
        lib.assign(obj, material)
    return obj


def ring(name, major, minor, location=(0, 0, 0), major_seg=32, minor_seg=8,
         material=None):
    """Torus lying flat in XY -- rolled rims, burner rings, handles."""
    bpy.ops.mesh.primitive_torus_add(
        major_radius=major, minor_radius=minor,
        major_segments=major_seg, minor_segments=minor_seg,
        location=location)
    obj = bpy.context.active_object
    obj.name = name
    if material:
        lib.assign(obj, material)
    return obj


def ball(name, radius, location=(0, 0, 0), segments=16, rings_=10, material=None):
    bpy.ops.mesh.primitive_uv_sphere_add(
        radius=radius, segments=segments, ring_count=rings_, location=location)
    obj = bpy.context.active_object
    obj.name = name
    if material:
        lib.assign(obj, material)
    return obj


def transform_apply(obj, rotation=(0, 0, 0), translation=None, scale=None):
    """
    Rotate (radians, XYZ), optionally scale, optionally translate, baking the
    result into the mesh. `translation=None` means "leave it where it is" --
    passing (0,0,0) would yank the object back to the world origin, which is
    almost never what a caller wants.
    """
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    obj.rotation_euler = rotation
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    if scale is not None:
        obj.scale = scale
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if translation is not None:
        obj.location = translation
        bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)
    return obj


def report_size(obj):
    bpy.context.view_layer.update()
    vs = [obj.matrix_world @ v.co for v in obj.data.vertices]
    xs, ys, zs = [v.x for v in vs], [v.y for v in vs], [v.z for v in vs]
    print("ART_SIZE %-16s  X %.3f  Y %.3f  Z %.3f  (z from %.3f to %.3f)" % (
        obj.name, max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs),
        min(zs), max(zs)))


# --------------------------------------------------------------------------
# Shared sub-assemblies.
# --------------------------------------------------------------------------

def gas_burner(cx, cy, base_z, radius=0.15, iron=None, body=None, grate=True):
    """
    A gas burner ring: shallow drip pan, black burner head, and (optionally)
    a cast-iron pot grate over it. Used both under the broth pot and on the
    stove top, so the two read as the same kitchen.
    """
    parts = []
    parts.append(lib.cylinder("BurnerPan", radius * 1.30, 0.018,
                              (cx, cy, base_z + 0.009), verts=24, mat=body))
    parts.append(lib.cylinder("BurnerBody", radius * 0.72, 0.030,
                              (cx, cy, base_z + 0.033), verts=20, mat=iron))
    parts.append(ring("BurnerRing", radius * 0.78, 0.020,
                      (cx, cy, base_z + 0.050), major_seg=24, minor_seg=6,
                      material=iron))
    parts.append(lib.cylinder("BurnerCap", radius * 0.46, 0.020,
                              (cx, cy, base_z + 0.062), verts=20, mat=iron))

    if grate:
        # Four radial fingers -- the classic star grate. Cheap and it reads.
        for i in range(4):
            a = math.radians(45 + i * 90)
            arm = lib.box("Grate", (radius * 1.25, 0.026, 0.016),
                          (0, 0, 0), iron)
            transform_apply(
                arm, rotation=(0, 0, a),
                translation=(cx + math.cos(a) * radius * 0.72,
                             cy + math.sin(a) * radius * 0.72,
                             base_z + 0.082))
            parts.append(arm)
        parts.append(ring("GrateRing", radius * 1.02, 0.014,
                          (cx, cy, base_z + 0.082), major_seg=20, minor_seg=6,
                          material=iron))
    return parts


def counter_frame(width, depth, height, top_t, leg, shelf_z, materials,
                  shelf_inset=0.06, backsplash=0.0):
    """
    The common commercial-counter chassis: worktop slab, perimeter apron,
    four tube legs with levelling feet, and an undershelf. Both the prep
    counter and the pass are built on this so they sit as a matched set.
    """
    bright, body, scuff = materials
    parts = []

    top_z = height - top_t / 2.0
    parts.append(lib.box("Top", (width, depth, top_t), (0, 0, top_z), bright))

    # Turned-down edge: commercial tops are folded, not slabs.
    lip_z = height - top_t - 0.020
    for sy in (-1, 1):
        parts.append(lib.box("EdgeLip", (width, 0.020, 0.045),
                             (0, sy * (depth / 2 - 0.010), lip_z), scuff))
    for sx in (-1, 1):
        parts.append(lib.box("EdgeLip", (0.020, depth - 0.04, 0.045),
                             (sx * (width / 2 - 0.010), 0, lip_z), scuff))

    if backsplash:
        parts.append(lib.box(
            "Backsplash", (width, 0.022, backsplash),
            (0, depth / 2 - 0.011, height + backsplash / 2.0), bright))

    # Legs, inset from the corners, with a levelling foot at the bottom.
    lx = width / 2 - 0.075
    ly = depth / 2 - 0.075
    leg_h = height - top_t - 0.030
    for sx in (-1, 1):
        for sy in (-1, 1):
            parts.append(lib.cylinder(
                "Leg", leg / 2.0, leg_h,
                (sx * lx, sy * ly, 0.030 + leg_h / 2.0), verts=12, mat=scuff))
            parts.append(lib.cylinder(
                "Foot", leg * 0.42, 0.030,
                (sx * lx, sy * ly, 0.015), verts=12, mat=body))

    # Undershelf plus its own turned-down edge.
    sw = width - shelf_inset * 2
    sd = depth - shelf_inset * 2
    parts.append(lib.box("Shelf", (sw, sd, 0.022), (0, 0, shelf_z), body))
    for sy in (-1, 1):
        parts.append(lib.box("ShelfLip", (sw, 0.016, 0.030),
                             (0, sy * (sd / 2 - 0.008), shelf_z - 0.024), scuff))

    return parts


# --------------------------------------------------------------------------
# 1. Broth pot -- the hero.
# --------------------------------------------------------------------------

POT_BURNER_H = 0.205      # floor to the top of the burner grate
POT_R_BOTTOM = 0.225
POT_R_TOP = 0.260
POT_BODY_H = 0.495
POT_HANDLE_REACH = 0.095


def build_broth_pot():
    body = steel("Pot_Steel", STEEL_BODY, 0.34)
    bright = steel("Pot_Steel_Bright", STEEL_BRIGHT, 0.28)
    scuff = steel("Pot_Steel_Scuff", STEEL_SCUFF, 0.48)
    iron = mat("Pot_CastIron", CAST_IRON, 0.72, 0.25)

    parts = []

    # --- Burner stand: a squat ring stand, not a flat plate, so the pot
    # clearly sits ON something rather than floating.
    parts.append(lib.cylinder("StandBase", 0.300, 0.028, (0, 0, 0.014),
                              verts=24, mat=scuff))
    for i in range(3):
        a = math.radians(90 + i * 120)
        parts.append(lib.box(
            "StandLeg", (0.034, 0.034, 0.115),
            (math.cos(a) * 0.235, math.sin(a) * 0.235, 0.0855), scuff))
    parts.append(ring("StandRim", 0.255, 0.022, (0, 0, 0.150),
                      major_seg=28, minor_seg=6, material=scuff))
    parts.extend(gas_burner(0, 0, 0.118, radius=0.155, iron=iron, body=scuff))

    # Gas pipe stub -- tiny, but it says "this is plumbed in".
    brass = mat("Pot_Brass", BRASS, 0.45, 0.7)
    pipe = lib.cylinder("GasPipe", 0.014, 0.26, (0, 0, 0), verts=10, mat=brass)
    transform_apply(pipe, rotation=(math.radians(90), 0, 0),
                    translation=(0, -0.22, 0.100))
    parts.append(pipe)
    parts.append(lib.box("GasValve", (0.05, 0.045, 0.05), (0, -0.32, 0.100), iron))

    # --- Pot body: tapered drum, widest at the rim.
    body_cz = POT_BURNER_H + POT_BODY_H / 2.0
    parts.append(taper("PotBody", POT_R_BOTTOM, POT_R_TOP, POT_BODY_H,
                       (0, 0, body_cz), verts=32, material=body))

    # A brighter band low on the body reads as a fire-scorched pot bottom and
    # breaks up an otherwise dead cylinder.
    parts.append(taper("PotScorch", POT_R_BOTTOM + 0.004, POT_R_BOTTOM + 0.010,
                       0.075, (0, 0, POT_BURNER_H + 0.038), verts=32,
                       material=scuff))

    # --- Broth surface, sunk just inside the rim.
    #
    # The pot body is a closed cone, so rather than hollowing it out (which
    # would leave back-facing interior walls that URP culls) the liquid is a
    # disc laid on the cone's cap and ringed by the rolled rim. Reads as a
    # full pot from every angle a player can get to, and stays watertight.
    broth = mat("Pot_Broth", lib.PALETTE["broth"], 0.22, 0.0)
    body_top = POT_BURNER_H + POT_BODY_H
    parts.append(lib.cylinder("Broth", POT_R_TOP - 0.020, 0.016,
                              (0, 0, body_top + 0.005), verts=32, mat=broth,
                              bevel=0.002))

    # Scallion and onion floating in it. Four tiny meshes, but the colour
    # break is what turns a flat brown disc into "soup".
    scallion = lib.material("Pot_Scallion", "herb_green", roughness=0.65)
    onion = lib.material("Pot_Onion", "onion_white", roughness=0.6)
    for (bx, by, br, m) in ((-0.080, 0.055, 0.030, scallion),
                            (0.090, -0.020, 0.024, onion),
                            (0.010, -0.095, 0.021, scallion),
                            (-0.030, -0.015, 0.027, onion)):
        parts.append(lib.cylinder("BrothBit", br, 0.014, (bx, by, body_top + 0.014),
                                  verts=8, mat=m, bevel=0.002))

    # --- Rolled rim at the top: the single detail that makes it read "pot".
    rim_z = body_top + 0.006
    parts.append(ring("PotRim", POT_R_TOP, 0.021, (0, 0, rim_z),
                      major_seg=32, minor_seg=8, material=bright))

    # --- Two side handles: a bracket loop each side, welded high on the body.
    hz = rim_z - 0.105
    for sx in (-1, 1):
        r_at = POT_R_BOTTOM + (POT_R_TOP - POT_R_BOTTOM) * (
            (hz - POT_BURNER_H) / POT_BODY_H)
        x0 = sx * (r_at - 0.010)
        x1 = sx * (r_at + POT_HANDLE_REACH)
        for sy in (-1, 1):
            parts.append(lib.box(
                "HandleArm", (abs(x1 - x0), 0.026, 0.026),
                ((x0 + x1) / 2.0, sy * 0.058, hz), bright))
        parts.append(lib.box("HandleBar", (0.026, 0.142, 0.030),
                             (x1, 0, hz), bright))

    # --- Lid, deliberately ajar: tipped and slid off-centre so you read the
    # pot as open, and so the silhouette is asymmetric (much easier to
    # recognise at a distance than a closed drum).
    lid_parts = [
        lib.cylinder("LidDisc", POT_R_TOP + 0.008, 0.018, (0, 0, 0),
                     verts=32, mat=bright),
        taper("LidDome", POT_R_TOP * 0.72, POT_R_TOP * 0.26, 0.038,
              (0, 0, 0.028), verts=28, material=bright),
        lib.cylinder("LidKnobStem", 0.018, 0.030, (0, 0, 0.060), verts=12,
                     mat=iron),
        ball("LidKnob", 0.033, (0, 0, 0.086), material=iron),
    ]
    lid = lib.join(lid_parts, "PotLid")
    transform_apply(lid, rotation=(0, math.radians(-16), math.radians(9)),
                    translation=(0.165, 0.040, rim_z + 0.068))
    parts.append(lid)

    return lib.join(parts, "BrothPot")


# --------------------------------------------------------------------------
# 2. Prep counter.
# --------------------------------------------------------------------------

CTR_W, CTR_D, CTR_H = 1.60, 0.70, 0.90


def build_counter():
    bright = steel("Ctr_Bright", STEEL_BRIGHT, 0.30)
    body = steel("Ctr_Body", STEEL_BODY, 0.40)
    scuff = steel("Ctr_Scuff", STEEL_SCUFF, 0.50)

    parts = counter_frame(
        CTR_W, CTR_D, CTR_H, top_t=0.040, leg=0.050, shelf_z=0.255,
        materials=(bright, body, scuff), backsplash=0.085)

    # A pair of stacked prep trays on the undershelf: cheap storytelling, and
    # it stops the shelf reading as an empty void from across the room.
    for i, sx in enumerate((-1, 1)):
        for k in range(2):
            parts.append(lib.box(
                "StoredTray", (0.30, 0.24, 0.055),
                (sx * 0.42, 0.02, 0.295 + k * 0.058), scuff))

    return lib.join(parts, "KitchenCounter")


# --------------------------------------------------------------------------
# 3. Ingredient bin -- sits ON a counter, recoloured per station in Unity.
#
# NOTE FOR UNITY: the fill volume uses its own material slot named
# "BinContents". Everything else is on "Bin_Steel". Swap the base colour on
# "BinContents" per station (herbs green, onion white, beef red, ...).
# --------------------------------------------------------------------------

BIN_W, BIN_D, BIN_H = 0.320, 0.265, 0.175
BIN_WALL = 0.012
BIN_FILL = 0.78            # how full, as a fraction of interior depth


def build_ingredient_bin():
    bin_steel = steel("Bin_Steel", STEEL_BODY, 0.36)
    bin_bright = steel("Bin_Steel", STEEL_BODY, 0.36)  # same slot on purpose
    contents = mat("BinContents", CONTENTS_NEUTRAL, 0.78, 0.0)

    parts = []

    # Pan body: an open tray whose walls flare outward, like a gastronorm.
    # Built as four leaning wall slabs + a floor, which keeps it one cheap
    # object with a genuinely open top.
    flare = math.radians(7)
    inner_w = BIN_W - 0.075
    inner_d = BIN_D - 0.075

    parts.append(lib.box("BinFloor", (inner_w, inner_d, BIN_WALL),
                         (0, 0, 0.020 + BIN_WALL / 2.0), bin_steel))

    wall_h = BIN_H - 0.020
    for sy in (-1, 1):
        w = lib.box("BinWallY", (BIN_W - 0.02, BIN_WALL, wall_h), (0, 0, 0),
                    bin_steel)
        transform_apply(
            w, rotation=(sy * flare, 0, 0),
            translation=(0, sy * (inner_d / 2.0 + 0.018), 0.020 + wall_h / 2.0))
        parts.append(w)
    for sx in (-1, 1):
        w = lib.box("BinWallX", (BIN_WALL, BIN_D - 0.02, wall_h), (0, 0, 0),
                    bin_steel)
        transform_apply(
            w, rotation=(0, -sx * flare, 0),
            translation=(sx * (inner_w / 2.0 + 0.018), 0, 0.020 + wall_h / 2.0))
        parts.append(w)

    # Rolled lip around the open top -- the read that says "container", and it
    # gives the rim a highlight so the opening is legible from above.
    lip_z = BIN_H
    lw, ld = BIN_W, BIN_D
    for sy in (-1, 1):
        parts.append(lib.box("BinLip", (lw, 0.024, 0.014),
                             (0, sy * (ld / 2 - 0.012), lip_z), bin_bright))
    for sx in (-1, 1):
        parts.append(lib.box("BinLip", (0.024, ld - 0.048, 0.014),
                             (sx * (lw / 2 - 0.012), 0, lip_z), bin_bright))

    # Small foot rail so the bin does not sit dead flat on the counter.
    parts.append(lib.box("BinFoot", (inner_w + 0.02, inner_d + 0.02, 0.020),
                         (0, 0, 0.010), bin_bright))

    # --- Contents: ONE simple filled volume, slightly heaped, on its own
    # material slot. Kept as a separate mesh island so recolouring in Unity
    # only touches this.
    base_z = 0.020 + BIN_WALL
    fill_top = base_z + (wall_h - BIN_WALL) * BIN_FILL
    fill_h = fill_top - base_z

    # A 4-sided "cone" is a tapered box; rotating it 45 degrees squares it up
    # with the pan, and the slight taper matches the pan's flared walls.
    # The body of the pile: fills the pan almost wall to wall, so the coloured
    # volume -- not the steel -- is what the player's eye lands on.
    fill = taper("Fill", inner_w * 0.700, inner_w * 0.760, fill_h,
                 (0, 0, base_z + fill_h / 2.0), verts=4,
                 material=contents, bevel=0.005)
    transform_apply(fill, rotation=(0, 0, math.radians(45)),
                    scale=(1.0, inner_d / inner_w, 1.0))

    # A squashed dome crowning it, cresting just over the rim. A heaped pile
    # reads as "prepped ingredients"; a flat slab reads as poured concrete,
    # and a stepped block reads as a lump of butter -- both were tried.
    dome = ball("FillDome", inner_w * 0.560, (0, 0, fill_top),
                segments=18, rings_=10, material=contents)
    transform_apply(dome, scale=(1.0, inner_d / inner_w, 0.32))

    # Lumps riding proud on the crown so the surface has grain at close range.
    lumps = []
    for (hx, hy, hr) in ((-0.072, 0.036, 0.026), (0.070, -0.030, 0.024),
                         (0.014, 0.052, 0.022), (-0.028, -0.046, 0.021),
                         (0.052, 0.040, 0.019), (0.028, 0.004, 0.026),
                         (-0.038, 0.006, 0.023)):
        # Follow the dome's curve so they sit ON the pile, not in a plane.
        r2 = (hx / (inner_w * 0.56)) ** 2 + (hy / (inner_d * 0.56)) ** 2
        hz = fill_top + inner_w * 0.560 * 0.32 * math.sqrt(max(0.0, 1.0 - r2))
        lumps.append(ball("FillLump", hr, (hx, hy, hz - hr * 0.45),
                          segments=10, rings_=7, material=contents))

    contents_obj = lib.join([fill, dome] + lumps, "BinContents")

    body = lib.join(parts, "BinBody")
    return lib.join([body, contents_obj], "IngredientBin")


# --------------------------------------------------------------------------
# 4. Service pass -- counter plus overhead warming gantry.
# --------------------------------------------------------------------------

PASS_W, PASS_D, PASS_H = 1.40, 0.66, 0.92
PASS_SHELF_Z = 1.34        # underside of the warming shelf
PASS_RAIL_Z = 1.55


def build_pass_counter():
    bright = steel("Pass_Bright", STEEL_BRIGHT, 0.28)
    body = steel("Pass_Body", STEEL_BODY, 0.40)
    scuff = steel("Pass_Scuff", STEEL_SCUFF, 0.50)
    iron = mat("Pass_Iron", ENAMEL_BLACK, 0.6, 0.2)
    lamp = mat("Pass_HeatLamp", LAMP_WARM, 0.25, 0.0)
    lamp.node_tree.nodes["Principled BSDF"].inputs["Emission Color"].default_value = LAMP_WARM
    lamp.node_tree.nodes["Principled BSDF"].inputs["Emission Strength"].default_value = 2.5

    parts = counter_frame(
        PASS_W, PASS_D, PASS_H, top_t=0.042, leg=0.050, shelf_z=0.250,
        materials=(bright, body, scuff))

    # --- The gantry. Two uprights at the ends carry a heated shelf and, above
    # it, a ticket rail. This overhead mass is the whole point: it is the only
    # thing on the line with daylight between a shelf and a worktop.
    px = PASS_W / 2 - 0.085
    py = PASS_D / 2 - 0.14
    post_h = PASS_RAIL_Z - PASS_H
    for sx in (-1, 1):
        parts.append(lib.box(
            "GantryPost", (0.048, 0.048, post_h),
            (sx * px, py, PASS_H + post_h / 2.0), scuff))
        # Bracket kicking forward to hold the shelf.
        parts.append(lib.box(
            "ShelfBracket", (0.036, 0.34, 0.030),
            (sx * px, py - 0.17, PASS_SHELF_Z - 0.016), scuff))

    shelf_d = 0.40
    shelf_y = py - 0.17
    parts.append(lib.box("WarmShelf", (PASS_W - 0.10, shelf_d, 0.030),
                         (0, shelf_y, PASS_SHELF_Z), bright))
    for sy in (-1, 1):
        parts.append(lib.box(
            "WarmShelfLip", (PASS_W - 0.10, 0.018, 0.038),
            (0, shelf_y + sy * (shelf_d / 2 - 0.009), PASS_SHELF_Z - 0.032),
            scuff))

    # Heat lamps slung under the shelf -- warm tubes in black housings.
    for sx in (-1, 1):
        parts.append(lib.box(
            "LampHousing", (0.44, 0.075, 0.048),
            (sx * 0.33, shelf_y, PASS_SHELF_Z - 0.055), iron))
        tube = lib.cylinder("LampTube", 0.020, 0.40, (0, 0, 0), verts=12,
                            mat=lamp)
        transform_apply(tube, rotation=(0, math.radians(90), 0),
                        translation=(sx * 0.33, shelf_y,
                                     PASS_SHELF_Z - 0.086))
        parts.append(tube)

    # Ticket rail across the top, with a few dockets clipped to it.
    rail = lib.cylinder("TicketRail", 0.014, PASS_W - 0.14, (0, 0, 0),
                        verts=12, mat=bright)
    transform_apply(rail, rotation=(0, math.radians(90), 0),
                    translation=(0, py, PASS_RAIL_Z))
    parts.append(rail)
    paper = mat("Pass_Ticket", (0.88, 0.86, 0.80, 1.0), 0.9, 0.0)
    for tx in (-0.42, -0.16, 0.20, 0.47):
        parts.append(lib.box("Ticket", (0.085, 0.004, 0.115),
                             (tx, py - 0.006, PASS_RAIL_Z - 0.062), paper))

    return lib.join(parts, "PassCounter")


# --------------------------------------------------------------------------
# 5. Two-burner range.
# --------------------------------------------------------------------------

STOVE_W, STOVE_D, STOVE_H = 0.86, 0.70, 0.86


def build_stove():
    bright = steel("Stove_Bright", STEEL_BRIGHT, 0.30)
    body = steel("Stove_Body", STEEL_BODY, 0.42)
    scuff = steel("Stove_Scuff", STEEL_SCUFF, 0.50)
    iron = mat("Stove_Iron", CAST_IRON, 0.72, 0.25)
    knob = mat("Stove_Knob", ENAMEL_BLACK, 0.45, 0.1)

    parts = []

    plinth_h = 0.09
    cab_h = STOVE_H - 0.045 - plinth_h

    # Plinth + cabinet body, recessed at the bottom so it does not read as a
    # solid block sitting in the floor.
    parts.append(lib.box("Plinth", (STOVE_W - 0.10, STOVE_D - 0.10, plinth_h),
                         (0, 0, plinth_h / 2.0), scuff))
    parts.append(lib.box("Body", (STOVE_W, STOVE_D, cab_h),
                         (0, 0, plinth_h + cab_h / 2.0), body))

    # Front door with a handle bar -- gives the front face a readable centre.
    parts.append(lib.box("Door", (STOVE_W - 0.09, 0.020, cab_h - 0.13),
                         (0, -STOVE_D / 2 + 0.005, plinth_h + cab_h / 2.0 - 0.02),
                         bright))
    handle = lib.cylinder("DoorHandle", 0.016, STOVE_W - 0.24, (0, 0, 0),
                          verts=10, mat=scuff)
    transform_apply(handle, rotation=(0, math.radians(90), 0),
                    translation=(0, -STOVE_D / 2 - 0.030,
                                 plinth_h + cab_h - 0.16))
    parts.append(handle)
    for sx in (-1, 1):
        parts.append(lib.box("HandleStud", (0.028, 0.055, 0.028),
                             (sx * (STOVE_W - 0.24) / 2, -STOVE_D / 2 - 0.012,
                              plinth_h + cab_h - 0.16), scuff))

    # Cooktop: a bright plate with a raised rear splash, so the two black
    # burners sit as dark holes in a light field. That contrast is what makes
    # the stove legible at eye height.
    top_z = STOVE_H - 0.022
    parts.append(lib.box("Cooktop", (STOVE_W, STOVE_D, 0.044),
                         (0, 0, top_z), bright))
    parts.append(lib.box("Splash", (STOVE_W, 0.024, 0.11),
                         (0, STOVE_D / 2 - 0.012, STOVE_H + 0.055), bright))

    # Control panel strip along the front with two knobs.
    parts.append(lib.box("ControlStrip", (STOVE_W - 0.06, 0.030, 0.085),
                         (0, -STOVE_D / 2 - 0.012, STOVE_H - 0.075), scuff))
    for sx in (-1, 1):
        k = lib.cylinder("Knob", 0.032, 0.045, (0, 0, 0), verts=14, mat=knob)
        transform_apply(k, rotation=(math.radians(90), 0, 0),
                        translation=(sx * 0.20, -STOVE_D / 2 - 0.040,
                                     STOVE_H - 0.075))
        parts.append(k)
        parts.append(lib.box("KnobPointer", (0.010, 0.030, 0.040),
                             (sx * 0.20, -STOVE_D / 2 - 0.058,
                              STOVE_H - 0.055), bright))

    # Two burners, front-back offset slightly so the top does not look like a
    # domino tile.
    for sx, dy in ((-1, 0.035), (1, -0.035)):
        parts.extend(gas_burner(sx * 0.205, dy, STOVE_H,
                                radius=0.150, iron=iron, body=scuff))

    return lib.join(parts, "Stove")


# --------------------------------------------------------------------------
# Build + look at it.
# --------------------------------------------------------------------------

def eye_elevation(focus_z, distance, eye=1.60):
    """Camera elevation that puts the lens at standing eye height."""
    s = max(-0.95, min(0.95, (eye - focus_z) / distance))
    return math.degrees(math.asin(s))


def emit(build_fn, name, focus, distance, azimuth=40.0, elevation=35.0,
         eye_distance=None, eye=1.60):
    """
    Build, export, and render TWO previews: the usual 3/4 hero shot, and a
    second one taken from a standing player's eye line a few metres back,
    because "can I tell what that is from across the kitchen" is the actual
    acceptance test for these props.

    `eye` is the camera height. For props that live on a 0.9 m counter, pass
    the height of the eye ABOVE that counter instead.
    """
    lib.reset_scene()
    obj = build_fn()
    report_size(obj)
    lib.finish(obj, name, focus=focus, distance=distance,
               azimuth=azimuth, elevation=elevation)

    lib.reset_scene()
    obj = build_fn()
    lib.set_origin_to_floor(obj)
    d = eye_distance if eye_distance else distance * 1.8
    _, png = lib.out_paths(name + "_eye")
    lib.render_preview(png, focus=focus, distance=d, azimuth=azimuth - 22.0,
                       elevation=eye_elevation(focus[2], d, eye))


def main():
    emit(build_broth_pot, "BrothPot", focus=(0, 0, 0.48), distance=1.9,
         eye_distance=3.2)
    emit(build_counter, "KitchenCounter", focus=(0, 0, 0.50), distance=3.1,
         eye_distance=4.4)
    # The bin sits on a 0.9 m counter, so its "eye" view is a player looking
    # down onto it from 0.7 m above the worktop.
    emit(build_ingredient_bin, "IngredientBin", focus=(0, 0, 0.09),
         distance=0.80, eye_distance=1.1, eye=0.70)
    emit(build_pass_counter, "PassCounter", focus=(0, 0, 0.85), distance=3.4,
         eye_distance=4.6)
    emit(build_stove, "Stove", focus=(0, 0, 0.50), distance=2.4,
         eye_distance=3.6)


if __name__ == "__main__":
    main()
