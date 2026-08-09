"""
Shopfront props and signage for the phở shophouse.

  RestaurantSign -- standing shop sign; the player interacts with it to open
                    and close for the day. Board face is its own material slot
                    so a painted/lettered texture can be dropped on later
                    without touching the frame.
  MenuBoard      -- chalkboard easel; the upgrade station the player buys from.
  BowlStack      -- a short stack of clean empty bowls for the service shelf.
  Crate          -- slatted ingredient delivery crate.
  CeilingLamp    -- bare bulb under a conical shade.

Style: warm, worn, Vietnamese street food. Painted steel and battered timber,
nothing pristine. lib.PALETTE where it fits; extra colours are defined locally
because lib.py is shared.

Origins sit where the thing is PLACED: floor contact for everything except
CeilingLamp, which hangs, so its origin is at the top of its cord.

Run headless:
  Blender --background --factory-startup --python art/blender/props.py
"""

import bpy
import math
import os
import sys

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import lib  # noqa: E402


COLORS = {
    "sign_red":      (0.48, 0.11, 0.10, 1.0),
    "sign_gold":     (0.72, 0.55, 0.20, 1.0),
    "post_steel":    (0.30, 0.31, 0.30, 1.0),
    "chalk_board":   (0.085, 0.105, 0.090, 1.0),
    "chalk_frame":   (0.34, 0.21, 0.11, 1.0),
    "bowl_white":    (0.90, 0.89, 0.86, 1.0),
    "bowl_blue":     (0.22, 0.32, 0.48, 1.0),
    "crate_wood":    (0.44, 0.30, 0.16, 1.0),
    "crate_dark":    (0.175, 0.105, 0.052, 1.0),
    "lamp_shade":    (0.42, 0.15, 0.10, 1.0),
    "lamp_bulb":     (1.0, 0.93, 0.74, 1.0),
    "lamp_flex":     (0.10, 0.09, 0.09, 1.0),
    "brass":         (0.62, 0.48, 0.22, 1.0),
}


def mat(name, color_key, roughness=0.7, metallic=0.0):
    """Principled BSDF from the local COLORS table."""
    m = bpy.data.materials.get(name)
    if m is None:
        m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = COLORS[color_key]
        bsdf.inputs["Roughness"].default_value = roughness
        bsdf.inputs["Metallic"].default_value = metallic
    return m


def cone(name, r_bottom, r_top, height, location, verts=24, material=None,
         bevel=0.004):
    """Truncated cone standing on +Z, origin at its centre."""
    bpy.ops.mesh.primitive_cone_add(radius1=r_bottom, radius2=r_top,
                                    depth=height, vertices=verts,
                                    location=location)
    obj = bpy.context.active_object
    obj.name = name
    if bevel:
        lib.add_bevel(obj, bevel)
    if material:
        lib.assign(obj, material)
    return obj


def ring(name, radius, thickness=0.007, segments=20, material=None):
    """
    A torus lying flat in XY -- used for bowl lips. A solid disc at the rim
    caps the bowl and reads as a lid; a ring reads as a glazed edge and you
    can still see into the bowl.
    """
    bpy.ops.mesh.primitive_torus_add(major_radius=radius,
                                     minor_radius=thickness,
                                     major_segments=segments,
                                     minor_segments=4,
                                     location=(0, 0, 0))
    obj = bpy.context.active_object
    obj.name = name
    if material:
        lib.assign(obj, material)
    return obj


def rot_x(p, angle):
    """Rotate a local offset about +X (used to place parts on a tilted panel)."""
    c, s = math.cos(angle), math.sin(angle)
    return (p[0], p[1] * c - p[2] * s, p[1] * s + p[2] * c)


def on_panel(obj, local, angle, centre):
    """Place `obj` at a local offset on a panel tilted `angle` about X."""
    r = rot_x(local, angle)
    obj.rotation_euler = (angle, 0, 0)
    obj.location = (centre[0] + r[0], centre[1] + r[1], centre[2] + r[2])
    return obj


def set_origin_to(obj, point):
    """
    Origin at an arbitrary point. lib.set_origin_to_floor is right for things
    that stand on the ground; a ceiling lamp is placed by its FIXING, so its
    origin belongs at the top of the cord instead.
    """
    bpy.context.view_layer.update()
    bpy.context.scene.cursor.location = point
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    bpy.context.scene.cursor.location = (0, 0, 0)
    obj.location = (0, 0, 0)
    return obj


# --------------------------------------------------------------------------
# RestaurantSign -- the open/close interactable.
# --------------------------------------------------------------------------

SIGN_HEIGHT = 2.30      # top of the board
BOARD_W = 1.10
BOARD_H = 0.62
BOARD_T = 0.05
BOARD_Y = 0.095      # board stands PROUD of the post, not on top of it
POST_R = 0.045


def build_sign():
    steel = mat("Sign_Post", "post_steel", roughness=0.55, metallic=0.35)
    frame = mat("Sign_Frame", "sign_red", roughness=0.62)
    face = mat("Sign_Face", "sign_gold", roughness=0.70)
    trim = mat("Sign_Trim", "brass", roughness=0.42, metallic=0.60)

    parts = []
    board_cz = SIGN_HEIGHT - BOARD_H / 2.0

    # Post and foot. A single post reads as a lollipop, so the base gets a
    # plate and the post a collar -- cheap, and it makes the thing look bolted
    # to the pavement instead of pushed into it.
    parts.append(lib.cylinder("Sign_PostShaft", POST_R, SIGN_HEIGHT - 0.12,
                              (0, 0, (SIGN_HEIGHT - 0.12) / 2.0),
                              verts=16, mat=steel, bevel=0.005))
    parts.append(lib.box("Sign_Foot", (0.36, 0.36, 0.045), (0, 0, 0.0225),
                         steel, bevel=0.006))
    parts.append(lib.cylinder("Sign_Collar", POST_R + 0.018, 0.055,
                              (0, 0, 0.072), verts=16, mat=steel, bevel=0.005))

    # Board carcass, then the face plate proud of it on BOTH sides so the sign
    # is legible from either approach.
    parts.append(lib.box("Sign_Board", (BOARD_W, BOARD_T, BOARD_H),
                         (0, BOARD_Y, board_cz), frame, bevel=0.008))
    for sy in (1, -1):
        parts.append(lib.box("Sign_FacePlate",
                             (BOARD_W - 0.10, 0.012, BOARD_H - 0.10),
                             (0, BOARD_Y + sy * (BOARD_T / 2.0 + 0.004),
                              board_cz),
                             face, bevel=0.004))

    # Pediment + underskirt: the stepped top and bottom edges of a Vietnamese
    # painted shop board.
    parts.append(lib.box("Sign_Pediment", (BOARD_W + 0.09, BOARD_T + 0.03, 0.06),
                         (0, BOARD_Y, SIGN_HEIGHT + 0.02), frame, bevel=0.006))
    parts.append(lib.box("Sign_Skirt", (BOARD_W + 0.05, BOARD_T + 0.02, 0.04),
                         (0, BOARD_Y, board_cz - BOARD_H / 2.0 - 0.01),
                         frame, bevel=0.006))

    # Brackets tying board to post -- two angled struts.
    for sx in (1, -1):
        # Bracket runs back from the board to the post, and a diagonal strut
        # braces it -- the board is cantilevered forward of the mast.
        parts.append(lib.box("Sign_Bracket", (0.030, BOARD_Y + 0.06, 0.024),
                             (sx * 0.15, BOARD_Y / 2.0,
                              board_cz - BOARD_H / 2.0 - 0.03),
                             trim, bevel=0.004))
        strut = lib.box("Sign_Strut", (0.024, 0.020, 0.26),
                        (sx * 0.15, BOARD_Y * 0.55,
                         board_cz - BOARD_H / 2.0 - 0.16),
                        trim, bevel=0.004)
        strut.rotation_euler = (math.radians(-32), 0, 0)
        parts.append(strut)

    # Two little hanging lanterns. Pure flavour, four boxes, big payoff on a
    # street frontage.
    for sx in (1, -1):
        parts.append(lib.cylinder("Sign_Lantern", 0.055, 0.13,
                                  (sx * (BOARD_W / 2.0 - 0.02), BOARD_Y,
                                   board_cz - BOARD_H / 2.0 - 0.14),
                                  verts=12, mat=frame, bevel=0.006))
        parts.append(lib.box("Sign_LanternCap", (0.075, 0.075, 0.018),
                             (sx * (BOARD_W / 2.0 - 0.02), BOARD_Y,
                              board_cz - BOARD_H / 2.0 - 0.072),
                             trim, bevel=0.003))

    return lib.join(parts, "RestaurantSign")


# --------------------------------------------------------------------------
# MenuBoard -- the upgrade station.
# --------------------------------------------------------------------------

def build_menu_board():
    """
    A-frame sandwich board. The first attempt was a single panel on an easel:
    it read as a plank on sticks from every angle, and the slate faced -Y so
    the "front" view showed its back. An A-frame is simpler, unmistakably a
    street menu, and has a silhouette from any approach direction.
    """
    wood = lib.material("Menu_Frame", "wood_mid", roughness=0.66)
    wood_dark = lib.material("Menu_Frame_Dark", "wood_dark", roughness=0.72)
    board = mat("Menu_Board", "chalk_board", roughness=0.92)
    chalk = mat("Menu_Chalk", "bowl_white", roughness=0.95)

    parts = []
    panel_w, panel_h, panel_t = 0.62, 0.94, 0.038
    lean = math.radians(11)             # each panel leans back from vertical
    hinge_z = 1.02
    # Panel centre: measured down the tilted panel from the hinge.
    half = panel_h / 2.0
    cz = hinge_z - half * math.cos(lean)
    cy = half * math.sin(lean)

    for sy in (1, -1):
        ang = sy * lean
        centre = (0.0, sy * cy, cz)

        # Slate first, then the frame rails around it, so the dark face is
        # framed rather than floating.
        parts.append(on_panel(
            lib.box("Menu_Slate", (panel_w, panel_t, panel_h),
                    (0, 0, 0), board, bevel=0.005),
            (0, 0, 0), ang, centre))

        rail = 0.052
        for lz in (half - rail / 2.0, -half + rail / 2.0):
            parts.append(on_panel(
                lib.box("Menu_Rail", (panel_w + 0.040, panel_t + 0.022, rail),
                        (0, 0, 0), wood, bevel=0.005),
                (0, 0, lz), ang, centre))
        for lx in (panel_w / 2.0 - rail / 2.0, -panel_w / 2.0 + rail / 2.0):
            parts.append(on_panel(
                lib.box("Menu_Stile", (rail, panel_t + 0.022, panel_h),
                        (0, 0, 0), wood, bevel=0.005),
                (lx, 0, 0), ang, centre))

        # Chalked price lines -- reads as a written menu with no texture.
        for i, (lw, lz) in enumerate(((0.40, 0.28), (0.32, 0.16),
                                      (0.36, 0.02), (0.26, -0.12),
                                      (0.34, -0.26))):
            parts.append(on_panel(
                lib.box(f"Menu_Line{i}", (lw, 0.010, 0.018),
                        (0, 0, 0), chalk, bevel=0.002),
                (-0.05, sy * (panel_t / 2.0 + 0.004), lz), ang, centre))

        # Feet, so it does not knife-edge into the floor.
        parts.append(lib.box("Menu_Foot", (panel_w + 0.030, 0.070, 0.026),
                             (0, sy * (cy + half * math.sin(lean)), 0.013),
                             wood_dark, bevel=0.004))

    # Hinge cap across the top, and a chain stopping the two halves splaying.
    parts.append(lib.box("Menu_Hinge", (panel_w + 0.055, 0.135, 0.045),
                         (0, 0, hinge_z + 0.010), wood_dark, bevel=0.007))
    parts.append(lib.box("Menu_Tie", (0.026, cy * 2.0, 0.014),
                         (0, 0, 0.30), wood_dark, bevel=0.003))

    # Chalk nub resting on the near foot.
    parts.append(lib.cylinder("Menu_ChalkStick", 0.011, 0.055,
                              (0.20, cy + half * math.sin(lean), 0.036),
                              verts=8, mat=chalk, bevel=0.002))

    return lib.join(parts, "MenuBoard")


# --------------------------------------------------------------------------
# BowlStack -- clean empties on the service shelf.
# --------------------------------------------------------------------------

def build_bowl_stack(count=4):
    """
    Nested empties. Only the top bowl is ever seen inside, so only the top
    bowl is built hollow (open cone + solidify); the ones below are solid
    cones, which is invisible from outside and a third of the triangles.
    """
    white = mat("Bowl_Ceramic", "bowl_white", roughness=0.30)
    rim = mat("Bowl_Rim", "bowl_blue", roughness=0.32)

    parts = []
    pitch = 0.044           # how far each bowl rises above the one below
    foot_r, rim_r = 0.052, 0.098
    body_h = 0.076

    for i in range(count):
        z = 0.014 + i * pitch

        # EVERY bowl is an open cone + solidify. Building the lower ones as
        # solid cones was cheaper but their flat top caps sat inside the bowl
        # above and z-fought with its wall, which showed up as a ring of
        # shimmering spikes around each lip.
        bpy.ops.mesh.primitive_cone_add(
            radius1=foot_r, radius2=rim_r, depth=body_h, vertices=18,
            end_fill_type="NOTHING", location=(0, 0, z + body_h / 2.0))
        wall = bpy.context.active_object
        wall.name = f"Bowl{i}"
        sol = wall.modifiers.new("Solidify", "SOLIDIFY")
        sol.thickness = 0.007
        sol.offset = 0.0
        lib.assign(wall, white)
        parts.append(wall)

        if i == 0:
            # Footring + a floor, so the bottom of the stack is closed.
            parts.append(lib.cylinder("Bowl_Foot", foot_r + 0.005, 0.018,
                                      (0, 0, 0.009), verts=20, mat=white,
                                      bevel=0.002))
        if i == count - 1:
            parts.append(lib.cylinder("Bowl_TopFloor", foot_r + 0.008, 0.010,
                                      (0, 0, z + 0.018), verts=20, mat=white,
                                      bevel=0.002))

        # Blue lip. A RING, not a disc -- as a disc it capped every bowl and
        # the stack read as a pile of plates with lids.
        lip = ring(f"Bowl_Lip{i}", rim_r + 0.001, thickness=0.006,
                   segments=18, material=rim)
        lip.location = (0, 0, z + body_h - 0.003)
        parts.append(lip)

    return lib.join(parts, "BowlStack")


# --------------------------------------------------------------------------
# Crate -- ingredient delivery.
# --------------------------------------------------------------------------

CRATE = (0.52, 0.40, 0.34)      # outer w, d, h


def build_crate():
    wood = mat("Crate_Wood", "crate_wood", roughness=0.80)
    wood_dark = mat("Crate_Post", "crate_dark", roughness=0.82)

    w, d, h = CRATE
    parts = []
    post = 0.042
    slat_t = 0.016

    # Corner posts.
    for sx in (1, -1):
        for sy in (1, -1):
            parts.append(lib.box("Crate_Post",
                                 (post, post, h),
                                 (sx * (w / 2 - post / 2),
                                  sy * (d / 2 - post / 2), h / 2),
                                 wood_dark, bevel=0.004))

    # Slats: three bands per side, with gaps. The gaps are the whole point --
    # a closed box is a box, a slatted one is a crate.
    for i, z in enumerate((0.055, 0.165, 0.275)):
        band_h = 0.075 if i < 2 else 0.062
        for sy in (1, -1):
            parts.append(lib.box("Crate_SlatY",
                                 (w - post * 1.6, slat_t, band_h),
                                 (0, sy * (d / 2 - slat_t / 2), z),
                                 wood, bevel=0.004))
        for sx in (1, -1):
            parts.append(lib.box("Crate_SlatX",
                                 (slat_t, d - post * 1.6, band_h),
                                 (sx * (w / 2 - slat_t / 2), 0, z),
                                 wood, bevel=0.004))

    # Floor boards.
    for i, x in enumerate((-0.16, 0.0, 0.16)):
        parts.append(lib.box(f"Crate_Floor{i}", (0.14, d - post * 1.8, 0.018),
                             (x, 0, 0.024), wood, bevel=0.003))

    # Top rail, so the open mouth has a lip.
    for sy in (1, -1):
        parts.append(lib.box("Crate_RailY", (w, 0.026, 0.028),
                             (0, sy * (d / 2 - 0.013), h - 0.014),
                             wood_dark, bevel=0.004))
    for sx in (1, -1):
        parts.append(lib.box("Crate_RailX", (0.026, d - 0.052, 0.028),
                             (sx * (w / 2 - 0.013), 0, h - 0.014),
                             wood_dark, bevel=0.004))

    return lib.join(parts, "Crate")


# --------------------------------------------------------------------------
# CeilingLamp -- hangs, so it is the one asset whose origin is at the TOP.
# --------------------------------------------------------------------------

DROP = 0.55     # cord length from ceiling to the shade


def build_ceiling_lamp():
    flex = mat("Lamp_Flex", "lamp_flex", roughness=0.55)
    shade = mat("Lamp_Shade", "lamp_shade", roughness=0.45)
    bulb = mat("Lamp_Bulb", "lamp_bulb", roughness=0.15)
    brass = mat("Lamp_Fitting", "brass", roughness=0.35, metallic=0.75)

    parts = []
    # Built downward from z = 0, which is the ceiling plane.
    parts.append(lib.cylinder("Lamp_Rose", 0.055, 0.022, (0, 0, -0.011),
                              verts=14, mat=brass, bevel=0.003))
    parts.append(lib.cylinder("Lamp_Cord", 0.007, DROP, (0, 0, -DROP / 2.0),
                              verts=8, mat=flex, bevel=0.002))

    shade_h = 0.145
    shade_top = -DROP
    parts.append(cone("Lamp_Shade", 0.185, 0.055, shade_h,
                      (0, 0, shade_top - shade_h / 2.0), verts=24,
                      material=shade, bevel=0.005))
    # Inner face of the shade, brighter -- reads as a lit interior even
    # before Unity puts a real light in it.
    parts.append(cone("Lamp_ShadeIn", 0.168, 0.050, shade_h - 0.014,
                      (0, 0, shade_top - shade_h / 2.0 - 0.006), verts=24,
                      material=bulb, bevel=0.003))
    parts.append(lib.cylinder("Lamp_Holder", 0.026, 0.048,
                              (0, 0, shade_top - 0.030), verts=12,
                              mat=brass, bevel=0.003))
    parts.append(cone("Lamp_Bulb", 0.030, 0.016, 0.070,
                      (0, 0, shade_top - shade_h - 0.010), verts=16,
                      material=bulb, bevel=0.008))

    return lib.join(parts, "CeilingLamp")


# --------------------------------------------------------------------------
# Preview + export.
# --------------------------------------------------------------------------

def clear_preview_rig():
    for obj in list(bpy.data.objects):
        if obj.type in {"LIGHT", "CAMERA"}:
            bpy.data.objects.remove(obj, do_unlink=True)


def add_key_light(azimuth, energy=3.2, elevation=48.0):
    """Sun from the camera's side; lib's fixed sun leaves the camera-facing
    side in shadow. Preview only -- deleted before the next view."""
    bpy.ops.object.light_add(type="SUN", location=(0, 0, 4))
    sun = bpy.context.active_object
    sun.data.energy = energy
    sun.rotation_euler = (math.radians(elevation), 0.0,
                          math.radians(azimuth - 18.0))
    return sun


def emit(obj, asset_name, focus, distance, floor_origin=True,
         origin_point=None, views=(("_front", 180.0, 14.0),
                                   ("_34", 142.0, 26.0))):
    if floor_origin:
        lib.set_origin_to_floor(obj)
    else:
        set_origin_to(obj, origin_point)

    glb, png = lib.out_paths(asset_name)
    lib.shade_smooth_by_angle(obj, 35)
    lib.export_glb(obj, glb)
    # FBX is the format Unity imports natively -- see lib.export_fbx. This
    # script builds its own export call rather than using lib.finish, so it
    # must emit both formats explicitly.
    lib.export_fbx(obj, glb[:-4] + ".fbx")
    d = obj.dimensions
    print(f"ART_DIMS {asset_name} w={d.x:.3f} d={d.y:.3f} h={d.z:.3f}")
    print(f"ART_SLOTS {asset_name} " +
          ",".join(s.material.name for s in obj.material_slots if s.material))

    base = png[:-4]
    for suffix, az, el in views:
        clear_preview_rig()
        add_key_light(az)
        lib.render_preview(f"{base}{suffix}.png", focus=focus,
                           distance=distance, azimuth=az, elevation=el,
                           resolution=560)


def main():
    lib.reset_scene()
    emit(build_sign(), "RestaurantSign", focus=(0, 0, 1.35), distance=4.2)

    lib.reset_scene()
    emit(build_menu_board(), "MenuBoard", focus=(0, 0, 0.85), distance=2.9)

    lib.reset_scene()
    emit(build_bowl_stack(), "BowlStack", focus=(0, 0, 0.10), distance=0.72)

    lib.reset_scene()
    emit(build_crate(), "Crate", focus=(0, 0, 0.17), distance=1.30)

    lib.reset_scene()
    # Hangs from the ceiling: origin at the top of the cord, not the floor.
    emit(build_ceiling_lamp(), "CeilingLamp", focus=(0, 0, -0.42),
         distance=1.55, floor_origin=False, origin_point=(0, 0, 0))


if __name__ == "__main__":
    main()
