"""
Shophouse interior shell -- the room the whole game happens inside.

Reference shape: a Vietnamese street-food "nha ong" (tube house) shopfront.
Narrow-and-deep footprint, high ceiling, painted plaster with a darker
wainscot band, tiled floor with a border, exposed cross beams, and a front
elevation that is almost entirely a roll-up shutter opening onto the
pavement.

Coordinate contract (IMPORTANT, differs from furniture assets):
  * Interior floor SURFACE is z = 0. Room is centred on x = 0, y = 0.
  * -Y is the STREET side (the open shutter front). +Y is the back of the
    shop -- the natural place for the kitchen, so the cook -> serve walk runs
    down the long axis.
  * Every exported piece keeps its origin at world (0, 0, 0) rather than at
    its own floor-contact centroid. Architecture pieces must stay in a single
    shared frame: SceneBuilder instantiates all three at the SAME transform
    and they assemble correctly, with the walkable floor exactly at that
    transform's Y. (lib.set_origin_to_floor would give each piece a
    different origin and they would drift apart.)

Run headless:
  Blender --background --factory-startup --python art/blender/shophouse.py
"""

import bpy
import math
import os
import sys

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import lib  # noqa: E402


# --------------------------------------------------------------------------
# Local palette. lib.py is shared and off-limits, so extra colours are
# registered into the runtime palette dict from here. Keys are prefixed
# "sh_" so they cannot collide with another asset's additions.
# --------------------------------------------------------------------------

LOCAL_COLOURS = {
    "sh_floor_tile":   (0.235, 0.212, 0.180, 1.0),   # grey-cream terrazzo
    "sh_floor_border": (0.190, 0.092, 0.070, 1.0),   # red-brown border tile
    "sh_wall_lower":   (0.105, 0.190, 0.185, 1.0),   # muted, grubby teal
    "sh_wall_upper":   (0.430, 0.375, 0.300, 1.0),   # warm plaster
    "sh_ceiling":      (0.300, 0.270, 0.230, 1.0),   # dimmer than the walls
    "sh_trim":         (0.360, 0.325, 0.270, 1.0),   # chair rail / cornice
    "sh_skirt":        (0.105, 0.058, 0.044, 1.0),   # near-black brown
    "sh_shutter":      (0.115, 0.125, 0.135, 1.0),   # galvanised steel
    "sh_concrete":     (0.175, 0.168, 0.155, 1.0),   # pavement
    "sh_pilaster":     (0.400, 0.345, 0.272, 1.0),   # slightly off the wall
    "sh_grout":        (0.130, 0.118, 0.100, 1.0),   # floor joint lines
    "sh_soot":         (0.105, 0.092, 0.082, 1.0),   # smoke-blacked stove wall
}
lib.PALETTE.update(LOCAL_COLOURS)


# --------------------------------------------------------------------------
# Parametric knobs. Tune these, not the geometry below.
# --------------------------------------------------------------------------

IW = 6.4            # interior width  (X)  -- see main() for the proportions note
ID = 10.5           # interior depth  (Y)
IH = 3.3            # interior height (Z), floor surface to ceiling underside

WT = 0.15           # wall thickness
FLOOR_T = 0.14      # floor slab thickness
CEIL_T = 0.16       # ceiling slab thickness

WAINSCOT_H = 1.05   # top of the darker lower-wall band
SKIRT_H = 0.16      # skirting board height
SKIRT_P = 0.030     # how far skirting/rails stand proud of the wall
RAIL_H = 0.055      # chair-rail strip height
CORNICE_H = 0.11    # cornice band height

OPEN_W = 4.9        # shutter opening width
LINTEL_Z = 2.55     # underside of the lintel over the opening
SILL_H = 0.06       # low sill you step over on the way in

BEAM_W = 0.20       # ceiling beam cross-section
BEAM_D = 0.26
BEAM_YS = (-3.4, -1.15, 1.15, 3.4)

ALCOVE_W = 3.3      # stove alcove in the back wall: width
ALCOVE_H = 2.35     # ...height
ALCOVE_D = 0.48     # ...how far it recesses back

PICRAIL_Z = 2.15    # high picture rail -- breaks up the blank upper wall
PARAPET_H = 0.65    # facade parapet standing above the roof slab

PIL_W = 0.38        # pilaster width along the wall
PIL_P = 0.09        # how far pilasters stand proud of the side walls
PIL_YS = (-3.4, -1.15, 1.15, 3.4)   # aligned with the beams overhead

STEP_DROP = 0.16    # how far the pavement sits below the shop floor

# Derived
HX = IW / 2.0       # 3.5  -- interior wall inner faces at +/- HX
HY = ID / 2.0       # 4.5  -- front (street) inner face at -HY
OX = HX + WT        # outer face X
OY = HY + WT        # outer face Y


# --------------------------------------------------------------------------
# Helpers local to this asset
# --------------------------------------------------------------------------

def origin_to_world_zero(obj):
    """Park the object's origin at world (0,0,0) -- see module docstring."""
    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    obj.location = (0.0, 0.0, 0.0)
    return obj


def report_bounds(obj):
    bpy.context.view_layer.update()
    cos = [obj.matrix_world @ v.co for v in obj.data.vertices]
    lo = (min(c.x for c in cos), min(c.y for c in cos), min(c.z for c in cos))
    hi = (max(c.x for c in cos), max(c.y for c in cos), max(c.z for c in cos))
    print(
        f"ART_BOUNDS {obj.name}  "
        f"min=({lo[0]:.2f},{lo[1]:.2f},{lo[2]:.2f}) "
        f"max=({hi[0]:.2f},{hi[1]:.2f},{hi[2]:.2f}) "
        f"size=({hi[0]-lo[0]:.2f},{hi[1]-lo[1]:.2f},{hi[2]-lo[2]:.2f}) "
        f"tris~{len(obj.data.polygons)}"
    )


def clear_preview_rig():
    """render_preview adds a sun + camera every call; stacking them blows the
    exposure out. Wipe lights/cameras before each shot."""
    for o in list(bpy.data.objects):
        if o.type in {"LIGHT", "CAMERA"}:
            bpy.data.objects.remove(o, do_unlink=True)


def add_interior_lights():
    """A roofed room is pitch black under render_preview's single outdoor sun.
    Hang practical lights inside so the previews show what the PLAYER sees.
    Preview-only -- Unity does its own lighting."""
    for y, power in ((-3.4, 130.0), (-1.15, 130.0), (1.15, 130.0), (3.4, 110.0)):
        bpy.ops.object.light_add(type="POINT", location=(0.0, y, IH - 0.42))
        lamp = bpy.context.active_object
        lamp.data.energy = power
        lamp.data.shadow_soft_size = 0.35
        lamp.data.color = (1.0, 0.86, 0.68)

    # A practical over the stove recess, so the alcove reads as a lit-from-
    # within cooking nook rather than a black rectangle at the end of the room.
    bpy.ops.object.light_add(type="POINT", location=(0.0, HY + 0.10, ALCOVE_H - 0.20))
    hood = bpy.context.active_object
    hood.data.energy = 55.0
    hood.data.shadow_soft_size = 0.30
    hood.data.color = (1.0, 0.82, 0.60)

    # Daylight spilling in through the shutter opening from the street.
    bpy.ops.object.light_add(type="AREA", location=(0.0, -HY - 0.9, 1.6))
    sky = bpy.context.active_object
    sky.data.energy = 420.0
    sky.data.shape = "RECTANGLE"
    sky.data.size = OPEN_W
    sky.data.size_y = 2.4
    sky.data.color = (0.78, 0.85, 1.0)
    sky.rotation_euler = (math.radians(90.0), 0.0, 0.0)


def set_look(exposure=-1.1):
    """render_preview does not touch colour management, and Blender's default
    AgX view transform desaturates hard when the scene is over-lit. Pin it
    explicitly so the previews report the palette honestly."""
    vs = bpy.context.scene.view_settings
    for tf in ("AgX", "Filmic", "Standard"):
        try:
            vs.view_transform = tf
            break
        except TypeError:
            continue
    for look in ("AgX - Medium High Contrast", "Medium High Contrast", "None"):
        try:
            vs.look = look
            break
        except TypeError:
            continue
    vs.exposure = exposure


def preview(path, lens=24.0, sky=0.55, **kw):
    """lib.render_preview is tuned for small props on a dark backdrop: a 50 mm
    lens and a near-black world. Neither works for an interior -- 50 mm sees
    almost nothing of a room, and a black world turns the shutter opening
    into a solid black rectangle instead of a street.

    So: let lib build its standard rig and render, then widen the lens, lift
    the world, and re-render over the top. The rig stays lib's."""
    clear_preview_rig()
    add_interior_lights()
    set_look()
    lib.render_preview(path, resolution=880, **kw)

    cam = bpy.context.scene.camera
    if cam:
        cam.data.lens = lens
    world = bpy.context.scene.world
    if world and world.use_nodes:
        bg = world.node_tree.nodes.get("Background")
        if bg:
            bg.inputs[0].default_value = (sky * 0.86, sky * 0.92, sky, 1.0)
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print(f"ART_PREVIEW {path}")


# --------------------------------------------------------------------------
# The shell: floor, three solid walls, open front elevation, ceiling.
# --------------------------------------------------------------------------

def build_shell():
    m_tile = lib.material("Sh_FloorTile", "sh_floor_tile", roughness=0.35)
    m_border = lib.material("Sh_FloorBorder", "sh_floor_border", roughness=0.45)
    m_lower = lib.material("Sh_WallLower", "sh_wall_lower", roughness=0.55)
    m_upper = lib.material("Sh_WallUpper", "sh_wall_upper", roughness=0.85)
    m_ceil = lib.material("Sh_Ceiling", "sh_ceiling", roughness=0.9)
    m_trim = lib.material("Sh_Trim", "sh_trim", roughness=0.5)
    m_skirt = lib.material("Sh_Skirt", "sh_skirt", roughness=0.5)
    m_beam = lib.material("Sh_Beam", "wood_dark", roughness=0.7)

    parts = []

    # ---- Floor -----------------------------------------------------------
    # Slab in border colour, then a slightly proud tile field inset from the
    # walls. The 5 mm lip reads as tile laid over screed and avoids any
    # coplanar z-fighting.
    parts.append(lib.box("FloorSlab", (IW + 2 * WT, ID + 2 * WT, FLOOR_T),
                         (0, 0, -FLOOR_T / 2.0), m_border, bevel=0.006))
    fw, fd = IW - 1.10, ID - 1.10
    parts.append(lib.box("FloorField", (fw, fd, 0.03),
                         (0, 0, -0.010), m_tile, bevel=0.005))

    # Joint lines across the tile field. Without them the floor is a single
    # flat sheet with no sense of scale -- these six thin strips are the
    # cheapest possible fix and they also give the eye something to follow
    # down the long axis.
    m_grout = lib.material("Sh_Grout", "sh_grout", roughness=0.6)
    for i in range(1, 5):
        gy = -fd / 2.0 + fd * i / 5.0
        parts.append(lib.box("FloorJoint", (fw, 0.026, 0.03),
                             (0, gy, -0.008), m_grout, bevel=0.004))
    for sx in (-1, 1):
        parts.append(lib.box("FloorJoint", (0.026, fd, 0.03),
                             (sx * fw / 3.0, 0, -0.008), m_grout, bevel=0.004))

    # ---- Side + back walls, split into a lower and upper band -------------
    upper_h = IH - WAINSCOT_H
    upper_z = WAINSCOT_H + upper_h / 2.0

    for sx in (-1, 1):
        x = sx * (HX + WT / 2.0)
        parts.append(lib.box("WallSideLow", (WT, ID + 2 * WT, WAINSCOT_H),
                             (x, 0, WAINSCOT_H / 2.0), m_lower))
        parts.append(lib.box("WallSideUp", (WT, ID + 2 * WT, upper_h),
                             (x, 0, upper_z), m_upper))

    # Back wall, built around a shallow stove alcove. A pho shop's back wall
    # is never blank -- it is the cooking recess, and from the doorway it is
    # the single biggest thing in frame, so it has to carry some depth. The
    # alcove is a recess with a solid back panel, NOT a hole: nothing is
    # see-through and the wall stays solid as specified.
    y_back = HY + WT / 2.0
    ax = ALCOVE_W / 2.0
    side_w = (IW - ALCOVE_W) / 2.0

    for sx in (-1, 1):
        bx = sx * (HX - side_w / 2.0)
        parts.append(lib.box("WallBackLow", (side_w, WT, WAINSCOT_H),
                             (bx, y_back, WAINSCOT_H / 2.0), m_lower))
        parts.append(lib.box("WallBackUp", (side_w, WT, upper_h),
                             (bx, y_back, upper_z), m_upper))

    # Header over the alcove.
    hdr_h = IH - ALCOVE_H
    parts.append(lib.box("AlcoveHeader", (ALCOVE_W, WT, hdr_h),
                         (0, y_back, ALCOVE_H + hdr_h / 2.0), m_upper))

    # Recess lining: one flat soot-black box per face. Years of stove smoke
    # blacken the tile behind a pho burner, and a dark rectangle at the end
    # of a long pale room is what actually makes the recess READ as a recess
    # from the doorway -- at 0.5 m deep a same-coloured recess is invisible.
    m_soot = lib.material("Sh_Soot", "sh_soot", roughness=0.42)
    y_panel = HY + ALCOVE_D + WT / 2.0
    parts.append(lib.box("AlcoveBack", (ALCOVE_W, WT, ALCOVE_H),
                         (0, y_panel, ALCOVE_H / 2.0), m_soot))
    for sx in (-1, 1):
        parts.append(lib.box("AlcoveReveal", (WT, ALCOVE_D, ALCOVE_H),
                             (sx * (ax + WT / 2.0), HY + ALCOVE_D / 2.0,
                              ALCOVE_H / 2.0), m_soot))
    parts.append(lib.box("AlcoveSoffit", (ALCOVE_W + 2 * WT, ALCOVE_D, WT),
                         (0, HY + ALCOVE_D / 2.0, ALCOVE_H + WT / 2.0), m_soot))
    parts.append(lib.box("AlcoveFloor", (ALCOVE_W, ALCOVE_D, FLOOR_T),
                         (0, HY + ALCOVE_D / 2.0, -FLOOR_T / 2.0), m_border))
    # Bright lintel lip across the top of the recess, so the dark panel is
    # framed rather than reading as a hole punched in the wall.
    parts.append(lib.box("AlcoveLip", (ALCOVE_W + 0.10, 0.07, 0.09),
                         (0, HY - 0.02, ALCOVE_H + 0.03), m_trim, bevel=0.006))

    # ---- Front (street) elevation: two narrow piers, lintel, low sill -----
    pier_w = (IW - OPEN_W) / 2.0            # 0.8 m each
    y_front = -(HY + WT / 2.0)
    for sx in (-1, 1):
        px = sx * (HX - pier_w / 2.0)
        parts.append(lib.box("PierLow", (pier_w, WT, WAINSCOT_H),
                             (px, y_front, WAINSCOT_H / 2.0), m_lower))
        parts.append(lib.box("PierUp", (pier_w, WT, upper_h),
                             (px, y_front, upper_z), m_upper))

    lintel_h = IH - LINTEL_Z
    parts.append(lib.box("Lintel", (OPEN_W, WT, lintel_h),
                         (0, y_front, LINTEL_Z + lintel_h / 2.0), m_upper))
    parts.append(lib.box("Sill", (OPEN_W, WT + 0.06, SILL_H),
                         (0, y_front, SILL_H / 2.0), m_skirt, bevel=0.008))

    # Parapet: a shophouse facade always carries past the roof line. Without
    # it the building reads as a squat single-storey shed from outside.
    parts.append(lib.box("Parapet", (IW + 2 * WT, WT, PARAPET_H),
                         (0, y_front, IH + CEIL_T + PARAPET_H / 2.0),
                         m_upper, bevel=0.008))
    parts.append(lib.box("ParapetCap", (IW + 2 * WT + 0.04, WT + 0.08, 0.09),
                         (0, y_front, IH + CEIL_T + PARAPET_H + 0.045),
                         m_trim, bevel=0.008))

    # ---- Skirting + chair rail: cheap, and they anchor the walls ----------
    def band(name, size, loc, mat):
        parts.append(lib.box(name, size, loc, mat, bevel=0.005))

    for sx in (-1, 1):
        x = sx * (HX - SKIRT_P / 2.0)
        band("Skirt", (SKIRT_P, ID, SKIRT_H), (x, 0, SKIRT_H / 2.0), m_skirt)
        band("Rail", (SKIRT_P, ID, RAIL_H), (x, 0, WAINSCOT_H), m_trim)
        # High picture rail: 2.2 m of unbroken plaster above the wainscot is
        # the emptiest surface in the room; one more line fixes it.
        band("PicRail", (SKIRT_P * 0.7, ID, 0.04), (x, 0, PICRAIL_Z), m_trim)
        band("Cornice", (SKIRT_P + 0.02, ID, CORNICE_H),
             (sx * (HX - (SKIRT_P + 0.02) / 2.0), 0, IH - CORNICE_H / 2.0), m_trim)

    # Back wall bands run only over the solid segments either side of the
    # alcove; the alcove gets its own skirting on the recessed panel.
    y_bk = HY - SKIRT_P / 2.0
    for sx in (-1, 1):
        bx = sx * (HX - side_w / 2.0)
        band("Skirt", (side_w, SKIRT_P, SKIRT_H), (bx, y_bk, SKIRT_H / 2.0), m_skirt)
        band("Rail", (side_w, SKIRT_P, RAIL_H), (bx, y_bk, WAINSCOT_H), m_trim)
    band("Cornice", (IW, SKIRT_P + 0.02, CORNICE_H),
         (0, HY - (SKIRT_P + 0.02) / 2.0, IH - CORNICE_H / 2.0), m_trim)

    # Skirt/rail returns on the inner face of the front piers.
    y_fr = -(HY - SKIRT_P / 2.0)
    for sx in (-1, 1):
        px = sx * (HX - pier_w / 2.0)
        band("Skirt", (pier_w, SKIRT_P, SKIRT_H), (px, y_fr, SKIRT_H / 2.0), m_skirt)
        band("Rail", (pier_w, SKIRT_P, RAIL_H), (px, y_fr, WAINSCOT_H), m_trim)

    # ---- Pilasters: shallow piers on the long walls -----------------------
    # A 10.5 m blank wall is the single most "empty warehouse" thing in the
    # room. Four shallow piers per side, aligned under the beams, give it a
    # structural rhythm and a shadow every 2.25 m for almost no triangles.
    m_pil = lib.material("Sh_Pilaster", "sh_pilaster", roughness=0.85)
    for sx in (-1, 1):
        px = sx * (HX - PIL_P / 2.0)
        for py in PIL_YS:
            parts.append(lib.box("Pilaster", (PIL_P, PIL_W, IH - CORNICE_H),
                                 (px, py, (IH - CORNICE_H) / 2.0), m_pil,
                                 bevel=0.008))
            # Small corbel where the pilaster meets the beam above.
            parts.append(lib.box("Corbel", (PIL_P + 0.05, PIL_W + 0.10, 0.10),
                                 (sx * (HX - (PIL_P + 0.05) / 2.0), py,
                                  IH - CORNICE_H - 0.05), m_trim, bevel=0.006))

    # ---- Ceiling + exposed cross beams -----------------------------------
    parts.append(lib.box("Ceiling", (IW + 2 * WT, ID + 2 * WT, CEIL_T),
                         (0, 0, IH + CEIL_T / 2.0), m_ceil, bevel=0.006))
    for by in BEAM_YS:
        parts.append(lib.box("Beam", (IW, BEAM_W, BEAM_D),
                             (0, by, IH - BEAM_D / 2.0), m_beam, bevel=0.008))

    return lib.join(parts, "ShophouseShell")


# --------------------------------------------------------------------------
# Roll-up shutter, exported separately so it can be animated open/closed.
# --------------------------------------------------------------------------

def build_shutter():
    m_steel = lib.material("Sh_Shutter", "sh_shutter", roughness=0.45, metallic=0.75)
    m_box = lib.material("Sh_ShutterBox", "steel_dark", roughness=0.55, metallic=0.4)

    y = -HY + 0.16
    parts = [lib.box("ShutterHousing", (OPEN_W + 0.10, 0.30, 0.30),
                     (0, y, LINTEL_Z - 0.15), m_box, bevel=0.008)]

    # The rolled-up drum peeking out of the housing.
    drum = lib.cylinder("ShutterRoll", 0.135, OPEN_W - 0.06,
                        (0, y - 0.02, LINTEL_Z - 0.15), verts=16, mat=m_steel)
    drum.rotation_euler = (0.0, math.radians(90.0), 0.0)
    bpy.ops.object.select_all(action="DESELECT")
    drum.select_set(True)
    bpy.context.view_layer.objects.active = drum
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    parts.append(drum)

    # A few slats hanging out of the roll -- the tell that it is a shutter.
    for i, z in enumerate((LINTEL_Z - 0.34, LINTEL_Z - 0.40, LINTEL_Z - 0.46)):
        parts.append(lib.box(f"Slat{i}", (OPEN_W - 0.10, 0.045, 0.052),
                             (0, y - 0.03, z), m_steel, bevel=0.006))

    # Side guide channels the shutter would run down.
    for sx in (-1, 1):
        parts.append(lib.box("ShutterGuide", (0.07, 0.11, LINTEL_Z),
                             (sx * (OPEN_W / 2.0 - 0.035), y, LINTEL_Z / 2.0),
                             m_box, bevel=0.005))

    return lib.join(parts, "ShophouseShutter")


# --------------------------------------------------------------------------
# Threshold step + pavement apron. Separate so the level can skip it if the
# shop is placed against an existing street mesh.
# --------------------------------------------------------------------------

def build_threshold():
    m_conc = lib.material("Sh_Concrete", "sh_concrete", roughness=0.9)
    m_border = lib.material("Sh_FloorBorder", "sh_floor_border", roughness=0.45)

    parts = []

    # Pavement slab: top sits STEP_DROP below the shop floor. Kept modest --
    # the street proper is somebody else's asset, this is just enough ground
    # for the step to land on.
    pav_d = 1.5
    parts.append(lib.box("Pavement", (IW + 2 * WT + 0.9, pav_d, 0.20),
                         (0, -OY - pav_d / 2.0 + 0.02, -STEP_DROP - 0.10),
                         m_conc, bevel=0.008))

    # The step you climb to get in, flush with the shop floor at the top,
    # with a thin tiled nosing so it matches the floor border inside.
    step_d = 0.38
    step_y = -OY - step_d / 2.0 + 0.01
    parts.append(lib.box("Step", (OPEN_W + 0.4, step_d, STEP_DROP),
                         (0, step_y, -STEP_DROP / 2.0), m_conc, bevel=0.010))
    parts.append(lib.box("StepNosing", (OPEN_W + 0.4, step_d + 0.02, 0.020),
                         (0, step_y, -0.008), m_border, bevel=0.004))

    return lib.join(parts, "ShophouseThreshold")


# --------------------------------------------------------------------------

def main():
    lib.reset_scene()

    shell = build_shell()
    origin_to_world_zero(shell)
    report_bounds(shell)

    shutter = build_shutter()
    origin_to_world_zero(shutter)
    report_bounds(shutter)

    threshold = build_threshold()
    origin_to_world_zero(threshold)
    report_bounds(threshold)

    for obj, name in ((shell, "ShophouseShell"),
                      (shutter, "ShophouseShutter"),
                      (threshold, "ShophouseThreshold")):
        glb, _ = lib.out_paths(name)
        # Smoothing is normally applied by lib.finish, which this script
        # deliberately bypasses to preserve the shared world-origin
        # coordinate contract documented at the top of this file -- so apply
        # it explicitly here rather than shipping faceted architecture.
        lib.shade_smooth_by_angle(obj, 35)
        lib.export_glb(obj, glb)
        # FBX is the format Unity actually imports (see lib.export_fbx).
        lib.export_fbx(obj, glb[:-4] + ".fbx")

    _, png_dir = lib.out_paths("x")
    prev = os.path.dirname(png_dir)

    # azimuth 0 puts the camera at -Y (street side) looking toward +Y, i.e.
    # INTO the shop. azimuth 180 puts it deep in the shop looking back out.
    shots = (
        # 1. Player's eye, standing just inside the doorway at ~1.65 m,
        #    looking down the length of the shop. THE view that matters.
        #    Off the centre line on purpose -- a dead-symmetrical shot hides
        #    the side wall, the pilasters and every depth cue in the room.
        ("ShophouseInterior.png", 21.0,
         dict(focus=(0.0, 2.6, 1.50), distance=7.6, elevation=1.0, azimuth=14.0)),
        # 2. From the cook's position at the back, looking out at the street.
        #    Sky is deliberately over-bright here: the opening has no street
        #    geometry behind it, and a blown-out doorway reads as daylight
        #    whereas a mid-grey one reads as a closed shutter.
        ("ShophouseFromBack.png", 24.0,
         dict(focus=(0.0, -1.0, 1.50), distance=5.5, elevation=3.0,
              azimuth=186.0, sky=2.6)),
        # 3. Exterior 3/4 of the street elevation.
        ("ShophouseExterior.png", 30.0,
         dict(focus=(0.0, -2.4, 1.4), distance=11.5, elevation=13.0, azimuth=30.0)),
    )
    for fname, lens, kw in shots:
        preview(os.path.join(prev, fname), lens=lens, **kw)


if __name__ == "__main__":
    main()
