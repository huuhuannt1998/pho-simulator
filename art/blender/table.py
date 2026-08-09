"""
Dining table + stools for the shophouse.

Parametric on purpose (see the plan's art pipeline note): "make the tables
bigger" is a one-line diff here, not a remodel.

Reference shape: the low, square, slightly-battered wooden tables of a
Vietnamese street-food shophouse -- squat proportions, thick apron, chunky
square legs with a stretcher near the floor.

Run headless:
  Blender --background --factory-startup --python art/blender/table.py
"""

import bpy
import os
import sys

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import lib  # noqa: E402


# Parametric knobs -- tune these, not the geometry below.
TOP_W = 0.80          # table top width/depth (square)
TOP_T = 0.045         # top thickness
HEIGHT = 0.74         # floor to table-top surface
LEG = 0.055           # square leg cross-section
APRON_H = 0.07        # skirt under the top
INSET = 0.075         # how far legs sit in from the top's edge
STRETCHER_Z = 0.16    # height of the low cross-brace


def build_table():
    wood = lib.material("Wood_Table", "wood_mid", roughness=0.62)
    wood_dark = lib.material("Wood_Table_Dark", "wood_dark", roughness=0.7)

    parts = []

    # Top.
    top_z = HEIGHT - TOP_T / 2.0
    parts.append(lib.box("Top", (TOP_W, TOP_W, TOP_T), (0, 0, top_z), wood))

    # Apron: four rails just under the top, inset slightly. Reads as a real
    # piece of furniture rather than a slab on sticks.
    apron_z = HEIGHT - TOP_T - APRON_H / 2.0
    span = TOP_W - INSET * 2.0 + LEG
    for sx, sy in ((0, 1), (0, -1), (1, 0), (-1, 0)):
        if sx:
            size = (0.03, span, APRON_H)
            loc = (sx * (TOP_W / 2 - INSET), 0, apron_z)
        else:
            size = (span, 0.03, APRON_H)
            loc = (0, sy * (TOP_W / 2 - INSET), apron_z)
        parts.append(lib.box("Apron", size, loc, wood_dark))

    # Legs.
    leg_off = TOP_W / 2 - INSET
    for sx in (-1, 1):
        for sy in (-1, 1):
            parts.append(lib.box(
                "Leg", (LEG, LEG, HEIGHT - TOP_T),
                (sx * leg_off, sy * leg_off, (HEIGHT - TOP_T) / 2.0),
                wood_dark))

    # Low stretchers between legs on two opposite sides -- the detail that
    # makes it read "cheap sturdy cafe table" instead of "IKEA slab".
    for sy in (-1, 1):
        parts.append(lib.box(
            "Stretcher", (leg_off * 2, 0.028, 0.028),
            (0, sy * leg_off, STRETCHER_Z), wood_dark))

    return lib.join(parts, "DiningTable")


def build_stool():
    """Backless round stool -- the classic plastic/wood shophouse seat."""
    wood = lib.material("Wood_Stool", "wood_light", roughness=0.6)
    wood_dark = lib.material("Wood_Stool_Dark", "wood_dark", roughness=0.7)

    seat_h = 0.44
    parts = [lib.cylinder("Seat", 0.16, 0.035, (0, 0, seat_h - 0.0175), verts=20, mat=wood)]

    for sx in (-1, 1):
        for sy in (-1, 1):
            parts.append(lib.box(
                "StoolLeg", (0.032, 0.032, seat_h - 0.035),
                (sx * 0.105, sy * 0.105, (seat_h - 0.035) / 2.0),
                wood_dark))

    parts.append(lib.cylinder("StoolRing", 0.115, 0.022, (0, 0, 0.14), verts=16, mat=wood_dark))
    return lib.join(parts, "Stool")


def main():
    lib.reset_scene()
    table = build_table()
    lib.finish(table, "DiningTable", focus=(0, 0, 0.4), distance=2.4)

    lib.reset_scene()
    stool = build_stool()
    lib.finish(stool, "Stool", focus=(0, 0, 0.25), distance=1.5)


if __name__ == "__main__":
    main()
