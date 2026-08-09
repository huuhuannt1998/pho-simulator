"""
Modular mall/terrace pieces, so the shop can GROW.

WHY THIS EXISTS SEPARATELY FROM shophouse.py: that script builds one
complete, self-contained interior and the current scene depends on it.
Rewriting it into modules would risk regressing a room that already works
and looks right. These are additive pieces for the expansion system; the
single-unit shell stays exactly as it is until the mall layout replaces it.

THE MODULE IDEA
Each unit is a bay with NO side walls of its own. Walls live BETWEEN units
as separate objects, so a row of N units needs N+1 dividers:

    |   unit 1   |   unit 2   |   unit 3   |
    ^            ^            ^            ^
    end wall   divider     divider      end wall

That split is what makes "buy the shop next door" cheap at runtime: when a
player buys unit 2, the divider between 1 and 2 is swapped from the solid
variant to the doorway variant and the two rooms become one space. No mesh
rebuilding, no re-baking the room -- just toggling which of two objects is
active, which is also trivial to replicate over the network (one bool per
divider) compared with streaming new geometry to every client.

Units butt together exactly UNIT_W apart on X, all sharing the world-origin
frame that shophouse.py established (interior floor surface at z=0), so the
scene builder can place unit N at x = N * UNIT_W with no other maths.

Run headless:
  Blender --background --factory-startup --python art/blender/mall.py
"""

import bpy
import os
import sys

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import lib  # noqa: E402


lib.PALETTE.update({
    "mall_floor":  (0.62, 0.60, 0.56, 1.0),
    "mall_grout":  (0.44, 0.43, 0.40, 1.0),
    "mall_wall":   (0.74, 0.70, 0.62, 1.0),
    "mall_lower":  (0.26, 0.40, 0.40, 1.0),
    "mall_skirt":  (0.34, 0.17, 0.12, 1.0),
    "mall_ceil":   (0.80, 0.78, 0.74, 1.0),
    "mall_beam":   (0.30, 0.18, 0.10, 1.0),
    "mall_trim":   (0.86, 0.84, 0.79, 1.0),
})

UNIT_W = 6.4        # bay width on X -- units sit exactly this far apart
UNIT_D = 10.5       # depth on Y, matching the existing shophouse
UNIT_H = 3.3        # floor surface to ceiling underside
WT = 0.15           # divider thickness
FLOOR_T = 0.14
CEIL_T = 0.16
WAINSCOT_H = 1.05
SKIRT_H = 0.16

OPEN_W = 4.9        # shopfront opening
LINTEL_Z = 2.55

DOOR_W = 1.6        # knock-through between two owned units
DOOR_H = 2.25


def _mats():
    return {
        "floor": lib.material("Mall_Floor", "mall_floor", roughness=0.45),
        "grout": lib.material("Mall_Grout", "mall_grout", roughness=0.7),
        "wall": lib.material("Mall_Wall", "mall_wall", roughness=0.85),
        "lower": lib.material("Mall_Lower", "mall_lower", roughness=0.6),
        "skirt": lib.material("Mall_Skirt", "mall_skirt", roughness=0.7),
        "ceil": lib.material("Mall_Ceiling", "mall_ceil", roughness=0.9),
        "beam": lib.material("Mall_Beam", "mall_beam", roughness=0.75),
        "trim": lib.material("Mall_Trim", "mall_trim", roughness=0.6),
    }


def build_unit():
    """One bay: floor, ceiling, back wall, shopfront. No side walls."""
    m = _mats()
    parts = []

    # Floor slab, sitting just below z=0 so the walking surface is exactly 0.
    parts.append(lib.box("Floor", (UNIT_W, UNIT_D, FLOOR_T),
                         (0, 0, -FLOOR_T / 2), m["floor"]))

    # Ceiling.
    parts.append(lib.box("Ceiling", (UNIT_W, UNIT_D, CEIL_T),
                         (0, 0, UNIT_H + CEIL_T / 2), m["ceil"]))

    # Back wall (+Y is the back; the street is -Y, matching shophouse.py).
    back_y = UNIT_D / 2 - WT / 2
    parts.append(lib.box("BackWall", (UNIT_W, WT, UNIT_H),
                         (0, back_y, UNIT_H / 2), m["wall"]))
    parts.append(lib.box("BackLower", (UNIT_W, 0.03, WAINSCOT_H),
                         (0, back_y - WT / 2 - 0.015, WAINSCOT_H / 2), m["lower"]))
    parts.append(lib.box("BackSkirt", (UNIT_W, 0.05, SKIRT_H),
                         (0, back_y - WT / 2 - 0.025, SKIRT_H / 2), m["skirt"]))

    # Shopfront: a lintel over a wide opening, with narrow piers either side.
    front_y = -UNIT_D / 2 + WT / 2
    pier_w = (UNIT_W - OPEN_W) / 2
    for sx in (-1, 1):
        parts.append(lib.box("FrontPier", (pier_w, WT, UNIT_H),
                             (sx * (OPEN_W + pier_w) / 2, front_y, UNIT_H / 2), m["wall"]))
    parts.append(lib.box("Lintel", (OPEN_W, WT, UNIT_H - LINTEL_Z),
                         (0, front_y, LINTEL_Z + (UNIT_H - LINTEL_Z) / 2), m["wall"]))

    # Two ceiling beams so the bay reads as built, not extruded.
    for by in (-UNIT_D / 4, UNIT_D / 4):
        parts.append(lib.box("Beam", (UNIT_W, 0.22, 0.26),
                             (0, by, UNIT_H - 0.13), m["beam"]))

    return lib.join(parts, "MallUnit")


def _divider(with_door):
    """
    The wall between two bays. Built as four boxes around the opening when
    it has a doorway, rather than as a boolean cut -- booleans on a wall
    this simple cost more than they're worth and are a common source of
    non-manifold geometry.
    """
    m = _mats()
    parts = []

    if not with_door:
        parts.append(lib.box("Divider", (WT, UNIT_D, UNIT_H), (0, 0, UNIT_H / 2), m["wall"]))
    else:
        side = (UNIT_D - DOOR_W) / 2
        for sy in (-1, 1):
            parts.append(lib.box("DividerSide", (WT, side, UNIT_H),
                                 (0, sy * (DOOR_W + side) / 2, UNIT_H / 2), m["wall"]))
        parts.append(lib.box("DividerHead", (WT, DOOR_W, UNIT_H - DOOR_H),
                             (0, 0, DOOR_H + (UNIT_H - DOOR_H) / 2), m["wall"]))
        # Reveal around the opening, so the knock-through reads as finished
        # work rather than a hole someone smashed.
        for sy in (-1, 1):
            parts.append(lib.box("DoorReveal", (WT + 0.04, 0.06, DOOR_H),
                                 (0, sy * DOOR_W / 2, DOOR_H / 2), m["trim"]))
        parts.append(lib.box("DoorHead", (WT + 0.04, DOOR_W, 0.06),
                             (0, 0, DOOR_H), m["trim"]))

    # Wainscot + skirting on both faces, so either side looks finished.
    for sx in (-1, 1):
        x = sx * (WT / 2 + 0.015)
        parts.append(lib.box("DivLower", (0.03, UNIT_D, WAINSCOT_H), (x, 0, WAINSCOT_H / 2), m["lower"]))
        parts.append(lib.box("DivSkirt", (0.05, UNIT_D, SKIRT_H), (x, 0, SKIRT_H / 2), m["skirt"]))

    return lib.join(parts, "MallDivider" + ("Door" if with_door else "Solid"))


def main():
    lib.reset_scene()
    unit = build_unit()
    lib.finish(unit, "MallUnit", focus=(0, 1.5, 1.5), distance=13.0, elevation=8.0, azimuth=18.0)

    lib.reset_scene()
    lib.finish(_divider(False), "MallDividerSolid", focus=(0, 0, 1.5), distance=13.0, elevation=12.0, azimuth=55.0)

    lib.reset_scene()
    lib.finish(_divider(True), "MallDividerDoor", focus=(0, 0, 1.5), distance=13.0, elevation=12.0, azimuth=55.0)


if __name__ == "__main__":
    main()
