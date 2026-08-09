"""
Customer NPCs for the shophouse.

DELIBERATE STYLE CHOICE (read before "improving" this):
these are stylized low-poly figures, not attempts at anatomy. Rounded,
chunky primitives; no fingers; no facial geometry beyond eyes/nose/ears.
Procedural anatomical realism reliably looks worse than a confident
stylized figure, so we commit to the stylized read. Flat shading + a fat
bevel on every part is the whole look: it gives each limb a soft faceted
highlight under URP without a single extra texture.

Two archetypes, and they must be tellable apart ACROSS THE ROOM -- that
is a gameplay requirement, not flavour:
  Customer_A -- office worker: short sleeves, slim trousers, plain silhouette.
  Customer_B -- food critic: wide-brimmed hat, bulky jacket, shoulder bag.
The hat brim and the bag are the at-a-glance cues; everything else is
secondary.

Both stand with origin at the FEET, facing +Y, neutral standing pose.
No rig, no animation -- a NavMeshAgent drives them.

Run headless:
  Blender --background --factory-startup --python art/blender/customer.py
"""

import bpy
import math
import mathutils
import os
import sys

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import lib  # noqa: E402


# --------------------------------------------------------------------------
# Local palette. lib.PALETTE is shared and off-limits to extend from here,
# so clothing/hair colours that only customers need live in this file.
# These are only DEFAULTS -- Unity recolours per material slot for variety.
# --------------------------------------------------------------------------

COLORS = {
    "skin":          (0.72, 0.53, 0.40, 1.0),
    "hair_black":    (0.055, 0.045, 0.042, 1.0),
    "eye_dark":      (0.05, 0.045, 0.05, 1.0),
    "shirt_blue":    (0.30, 0.42, 0.62, 1.0),
    "trousers_navy": (0.135, 0.155, 0.215, 1.0),
    "shoe_dark":     (0.085, 0.075, 0.070, 1.0),
    "jacket_olive":  (0.235, 0.245, 0.170, 1.0),
    "shirt_cream":   (0.80, 0.76, 0.66, 1.0),
    # Kept clearly darker than `skin`: a tan trouser at this value made the
    # legs read as bare skin at distance.
    "trousers_tan":  (0.215, 0.170, 0.115, 1.0),
    "hat_straw":     (0.66, 0.50, 0.26, 1.0),
    "bag_leather":   (0.26, 0.15, 0.09, 1.0),
}


def mat(name, color_key, roughness=0.72, metallic=0.0):
    """Principled BSDF from the local COLORS table (lib.material only knows
    lib.PALETTE keys, and lib.py is shared)."""
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


# --------------------------------------------------------------------------
# Geometry helpers. Everything is a heavily-bevelled box ("rounded box"):
# one primitive, one look, cheap triangles, reads soft from any angle.
# --------------------------------------------------------------------------

def rbox(name, size, loc, material, round_ratio=0.40, segments=2, rot=None):
    """Box with a bevel large enough to read as a soft rounded form."""
    obj = lib.box(name, size, loc, material, bevel=0)
    width = min(size) * round_ratio
    m = obj.modifiers.new("Bevel", "BEVEL")
    m.width = width
    m.segments = segments
    m.limit_method = "ANGLE"
    m.angle_limit = math.radians(40)
    if rot is not None:
        obj.rotation_euler = rot
    return obj


def limb(name, p0, p1, w, d, material, round_ratio=0.44, segments=3):
    """
    A rounded box stretched between two points. The box's long axis is +Z and
    gets rotated onto (p1 - p0), so limbs can be posed by giving joint
    positions instead of hand-computed euler angles -- which is what keeps the
    side-on silhouette from breaking.
    """
    a, b = mathutils.Vector(p0), mathutils.Vector(p1)
    v = b - a
    length = v.length
    obj = rbox(name, (w, d, length), (0, 0, 0), material,
               round_ratio=round_ratio, segments=segments)
    obj.rotation_euler = v.to_track_quat("Z", "Y").to_euler()
    obj.location = (a + b) / 2.0
    return obj


def mirrored(fn, *args, **kwargs):
    """Build a part at +X and its mirror at -X. Returns both."""
    out = []
    for side in (1, -1):
        out.append(fn(side, *args, **kwargs))
    return out


def flipx(p, side):
    return (p[0] * side, p[1], p[2])


# --------------------------------------------------------------------------
# Shared body construction. Both archetypes share the underlying figure;
# they differ in proportions and in what is layered on top.
# --------------------------------------------------------------------------

def build_body(P, mats):
    """
    P is a proportion dict. Returns (parts, joints) where joints holds the
    world-space attachment points the clothing layers need.
    """
    parts = []

    ankle_z = P["shoe_h"]
    knee_z = P["knee_z"]
    hip_z = P["hip_z"]
    hx = P["stance_x"]

    # --- feet -------------------------------------------------------------
    for side in (1, -1):
        parts.append(rbox(
            "Shoe",
            (P["shoe_w"], P["shoe_l"], P["shoe_h"]),
            (side * hx, P["shoe_fwd"], P["shoe_h"] / 2.0),
            mats["shoes"], round_ratio=0.30))

    # --- legs -------------------------------------------------------------
    for side in (1, -1):
        parts.append(limb("Shin",
                          (side * hx, 0.0, ankle_z),
                          (side * hx, 0.005, knee_z),
                          P["shin_w"], P["shin_d"], mats["trousers"]))
        parts.append(limb("Thigh",
                          (side * hx, 0.005, knee_z),
                          (side * P["hip_x"], 0.0, hip_z),
                          P["thigh_w"], P["thigh_d"], mats["trousers"]))

    # --- pelvis -----------------------------------------------------------
    parts.append(rbox("Hips",
                      (P["hip_w"], P["hip_d"], P["hip_h"]),
                      (0, 0, hip_z),
                      mats["trousers"], round_ratio=0.30))

    # --- torso ------------------------------------------------------------
    # One chest volume plus an abdomen that sits mostly INSIDE it. Two
    # equal slabs stacked leave a hard step at the join that reads as a
    # ledge across the stomach; heavy overlap hides the seam.
    # The waist is pushed BACK so its rear face is flush with the chest's and
    # the whole step happens at the front, like a ribcage over a stomach.
    # Centring it instead leaves a ledge running right around the belly, which
    # reads as a bumbag rather than a body.
    waist_y = -(P["chest_d"] - P["waist_d"]) / 2.0
    waist_z = hip_z + P["hip_h"] / 2.0 + P["waist_h"] / 2.0 - 0.03
    parts.append(rbox("Waist",
                      (P["waist_w"], P["waist_d"], P["waist_h"]),
                      (0, waist_y, waist_z),
                      mats["torso"], round_ratio=0.36, segments=3))

    chest_z = waist_z + P["waist_h"] / 2.0 + P["chest_h"] / 2.0 - 0.055
    parts.append(rbox("Chest",
                      (P["chest_w"], P["chest_d"], P["chest_h"]),
                      (0, 0, chest_z),
                      mats["torso"], round_ratio=0.38, segments=3))

    shoulder_z = chest_z + P["chest_h"] / 2.0 - P["shoulder_drop"]

    # Shoulder caps: without these the chest ends in two square corners and
    # the figure reads as a wardrobe. Rounded blobs at the deltoids are the
    # cheapest fix for the single most unhuman part of the front silhouette.
    for side in (1, -1):
        parts.append(rbox("Shoulder",
                          (P["arm_w"] * 1.35, P["chest_d"] * 0.94,
                           P["arm_w"] * 1.30),
                          (side * (P["chest_w"] / 2.0 - P["arm_w"] * 0.28),
                           0.0, shoulder_z + 0.012),
                          mats.get("shoulder", mats["torso"]),
                          round_ratio=0.46, segments=3))

    # --- neck + head ------------------------------------------------------
    neck_z = chest_z + P["chest_h"] / 2.0 + P["neck_h"] / 2.0 - 0.012
    parts.append(rbox("Neck", (P["neck_w"], P["neck_w"], P["neck_h"]),
                      (0, 0, neck_z), mats["skin"], round_ratio=0.34))

    # round_ratio stays LOW on the head. At 0.44 the bevel ate almost the
    # whole face -- the flat frontal plane shrank to a couple of centimetres
    # and every feature placed on it slid down the curve and disappeared. A
    # face needs a face plane.
    head_z = neck_z + P["neck_h"] / 2.0 + P["head_h"] / 2.0 - 0.022
    parts.append(rbox("Head", (P["head_w"], P["head_d"], P["head_h"]),
                      (0, P["head_fwd"], head_z), mats["skin"],
                      round_ratio=0.22, segments=3))
    # Jaw: narrower and shallower than the skull, set low and forward. A
    # single-box head reads as a brick from every angle; tapering the lower
    # third is what turns it into a face.
    parts.append(rbox("Jaw",
                      (P["head_w"] * 0.80, P["head_d"] * 0.86,
                       P["head_h"] * 0.44),
                      (0, P["head_fwd"] + P["head_d"] * 0.045,
                       head_z - P["head_h"] * 0.28),
                      mats["skin"], round_ratio=0.24, segments=3))

    face_y = P["head_fwd"] + P["head_d"] / 2.0

    # Ears -- two chips. Cheap, but they stop the head reading as a dice.
    for side in (1, -1):
        parts.append(rbox("Ear", (0.020, 0.048, 0.066),
                          (side * (P["head_w"] / 2.0 - 0.002),
                           P["head_fwd"] - 0.012, head_z + 0.006),
                          mats["skin"], round_ratio=0.36))

    # Nose -- a single wedge, and it has to PROJECT. Gives the profile a
    # front, which is most of what makes the SIDE view read as a person
    # rather than a post.
    parts.append(rbox("Nose", (0.034, 0.060, 0.046),
                      (0, face_y - 0.002, head_z - 0.012),
                      mats["skin"], round_ratio=0.34))

    # Eyebrows in HAIR material. Two dark bars do more for "this is a face"
    # at gameplay distance than any amount of modelled brow ridge, and they
    # cost four triangles each.
    for side in (1, -1):
        parts.append(rbox("Eyebrow",
                          (0.052, 0.020, 0.016),
                          (side * 0.048, face_y - 0.002,
                           head_z + P["head_h"] * 0.175),
                          mats["hair"], round_ratio=0.24))

    # Eyes. These MUST sit proud of the face plane -- centred on face_y they
    # end up entirely inside the skull and the character reads as blank.
    for side in (1, -1):
        parts.append(rbox("Eye", (0.044, 0.020, 0.030),
                          (side * 0.048, face_y - 0.004,
                           head_z + P["head_h"] * 0.075),
                          mats["eyes"], round_ratio=0.24))

    # --- arms -------------------------------------------------------------
    # Slight A-pose: hands clear of the hips so the front silhouette has a
    # gap of background either side of the torso.
    sx = P["chest_w"] / 2.0 + P["arm_w"] / 2.0 - 0.012
    joints = {
        "shoulder_z": shoulder_z,
        "shoulder_x": sx,
        "elbow": (sx + P["elbow_out"], 0.005, shoulder_z - P["upper_arm_len"]),
        "wrist": (sx + P["elbow_out"] + P["wrist_out"], 0.006,
                  shoulder_z - P["upper_arm_len"] - P["forearm_len"]),
        "chest_z": chest_z,
        "waist_z": waist_z,
        "waist_y": waist_y,
        "head_z": head_z,
        "face_y": face_y,
        "hip_z": hip_z,
    }

    for side in (1, -1):
        sh = (side * sx, 0.0, shoulder_z)
        el = flipx(joints["elbow"], side)
        wr = flipx(joints["wrist"], side)

        parts.append(limb("UpperArm", sh, el, P["arm_w"], P["arm_w"],
                          mats["upper_arm"]))
        parts.append(limb("Forearm", el, wr, P["arm_w"] * 0.88,
                          P["arm_w"] * 0.88, mats["forearm"]))
        # Hand: a mitten block. No fingers, on purpose.
        hand_dir = mathutils.Vector(wr) - mathutils.Vector(el)
        hand_dir.normalize()
        hand_c = mathutils.Vector(wr) + hand_dir * (P["hand_len"] / 2.0 - 0.01)
        parts.append(rbox("Hand", (P["arm_w"] * 0.86, P["arm_w"] * 0.72,
                                   P["hand_len"]),
                          tuple(hand_c), mats["skin"], round_ratio=0.40))

    return parts, joints


def add_hair(P, joints, hair_mat, style="short"):
    """Hair as a shell sitting slightly proud of the skull."""
    parts = []
    hz = joints["head_z"]
    hw, hd, hh = P["head_w"], P["head_d"], P["head_h"]
    fwd = P["head_fwd"]

    # Hair is a SHELL on the crown, nothing more. Two rewrites were lost to
    # hair that crept down past the eye line at the silhouette edges, which
    # turns the head into a dark helmet with a letterbox of face in it. Every
    # piece here is kept at or above the brow, and narrower than the skull so
    # it cannot show around the outline.
    parts.append(rbox("Hair_Cap",
                      (hw + 0.016, hd + 0.016, hh * 0.32),
                      (0, fwd - hd * 0.01, hz + hh * 0.345),
                      hair_mat, round_ratio=0.26, segments=3))
    # Back of the head down to the nape -- narrower than the skull so it
    # stays hidden behind the head from the front.
    parts.append(rbox("Hair_Back",
                      (hw - 0.014, hd * 0.30, hh * 0.46),
                      (0, fwd - hd * 0.36, hz + hh * 0.16),
                      hair_mat, round_ratio=0.32, segments=3))
    if style == "short":
        # Fringe: a shallow lip across the brow only.
        parts.append(rbox("Hair_Fringe",
                          (hw + 0.008, 0.042, hh * 0.13),
                          (0, fwd + hd * 0.5 - 0.008, hz + hh * 0.285),
                          hair_mat, round_ratio=0.28))
        # Temple slabs -- upper third only, so the profile is not bald above
        # the ear but the face stays open.
        for side in (1, -1):
            parts.append(rbox("Hair_Temple",
                              (0.012, hd * 0.58, hh * 0.16),
                              (side * (hw / 2.0 + 0.001), fwd - 0.006,
                               hz + hh * 0.26),
                              hair_mat, round_ratio=0.34))
    return parts


# --------------------------------------------------------------------------
# Archetype A -- office worker.
# --------------------------------------------------------------------------

PROPS_A = {
    "shoe_w": 0.100, "shoe_l": 0.240, "shoe_h": 0.062, "shoe_fwd": 0.030,
    "stance_x": 0.105, "hip_x": 0.098,
    "knee_z": 0.485, "hip_z": 0.955,
    "shin_w": 0.110, "shin_d": 0.118,
    "thigh_w": 0.144, "thigh_d": 0.155,
    "hip_w": 0.262, "hip_d": 0.198, "hip_h": 0.112,
    "waist_w": 0.330, "waist_d": 0.206, "waist_h": 0.160,
    "chest_w": 0.372, "chest_d": 0.238, "chest_h": 0.285,
    "shoulder_drop": 0.062,
    "neck_w": 0.098, "neck_h": 0.070,
    "head_w": 0.200, "head_d": 0.225, "head_h": 0.268, "head_fwd": 0.004,
    "arm_w": 0.094,
    "upper_arm_len": 0.278, "forearm_len": 0.255,
    "elbow_out": 0.040, "wrist_out": 0.014,
    "hand_len": 0.118,
}


def build_customer_a():
    mats = {
        "skin": mat("Skin", "skin", roughness=0.66),
        "hair": mat("Hair", "hair_black", roughness=0.58),
        "eyes": mat("Eyes", "eye_dark", roughness=0.35),
        "shirt": mat("Shirt", "shirt_blue", roughness=0.78),
        "trousers": mat("Trousers", "trousers_navy", roughness=0.80),
        "shoes": mat("Shoes", "shoe_dark", roughness=0.45),
    }
    build_mats = dict(mats)
    build_mats["torso"] = mats["shirt"]
    # Short sleeves: upper arm is shirt, forearm is bare skin. Free contrast.
    build_mats["upper_arm"] = mats["shirt"]
    build_mats["forearm"] = mats["skin"]

    parts, joints = build_body(PROPS_A, build_mats)
    P = PROPS_A

    # Collar -- a low band around the neck base. Small, but it is the thing
    # that says "shirt" rather than "torso-coloured box".
    parts.append(rbox("Collar",
                      (P["chest_w"] * 0.52, P["chest_d"] * 0.78, 0.045),
                      (0, 0.004, joints["chest_z"] + P["chest_h"] / 2.0 - 0.012),
                      mats["shirt"], round_ratio=0.30))

    # Sleeve cuff -- a shallow lip where fabric ends and arm begins. Keep it
    # small: a fat cuff turns the elbow into a knuckle.
    for side in (1, -1):
        el = flipx(joints["elbow"], side)
        parts.append(rbox("Cuff",
                          (P["arm_w"] * 1.06, P["arm_w"] * 1.06, 0.022),
                          (el[0], el[1], el[2] + 0.016),
                          mats["shirt"], round_ratio=0.40))

    # Belt -- separates shirt from trousers at a glance. Flush with the hips,
    # not proud of them, or it reads as a tool belt.
    parts.append(rbox("Belt",
                      (P["waist_w"] + 0.010, P["waist_d"] + 0.010, 0.032),
                      (0, joints["waist_y"],
                       joints["waist_z"] - P["waist_h"] / 2.0 - 0.004),
                      mats["shoes"], round_ratio=0.34))

    parts += add_hair(P, joints, mats["hair"], style="short")
    return lib.join(parts, "Customer_A")


# --------------------------------------------------------------------------
# Archetype B -- food critic. Shorter, stockier, and carrying enough
# silhouette furniture (brim, bag, coat hem) to be unmistakable at range.
# --------------------------------------------------------------------------

PROPS_B = {
    "shoe_w": 0.108, "shoe_l": 0.250, "shoe_h": 0.070, "shoe_fwd": 0.032,
    "stance_x": 0.118, "hip_x": 0.108,
    "knee_z": 0.432, "hip_z": 0.848,
    "shin_w": 0.122, "shin_d": 0.130,
    "thigh_w": 0.158, "thigh_d": 0.168,
    "hip_w": 0.292, "hip_d": 0.214, "hip_h": 0.118,
    "waist_w": 0.352, "waist_d": 0.230, "waist_h": 0.160,
    "chest_w": 0.400, "chest_d": 0.258, "chest_h": 0.265,
    "shoulder_drop": 0.058,
    "neck_w": 0.106, "neck_h": 0.064,
    "head_w": 0.204, "head_d": 0.228, "head_h": 0.272, "head_fwd": 0.004,
    "arm_w": 0.108,
    "upper_arm_len": 0.264, "forearm_len": 0.244,
    "elbow_out": 0.046, "wrist_out": 0.016,
    "hand_len": 0.120,
}


def build_customer_b():
    mats = {
        "skin": mat("Skin", "skin", roughness=0.66),
        "hair": mat("Hair", "hair_black", roughness=0.58),
        "eyes": mat("Eyes", "eye_dark", roughness=0.35),
        "shirt": mat("Shirt", "shirt_cream", roughness=0.80),
        "trousers": mat("Trousers", "trousers_tan", roughness=0.80),
        "shoes": mat("Shoes", "shoe_dark", roughness=0.45),
        "jacket": mat("Jacket", "jacket_olive", roughness=0.82),
        "hat": mat("Hat", "hat_straw", roughness=0.85),
        "bag": mat("Bag", "bag_leather", roughness=0.55),
    }
    build_mats = dict(mats)
    build_mats["torso"] = mats["shirt"]
    build_mats["upper_arm"] = mats["jacket"]
    build_mats["forearm"] = mats["jacket"]
    # Deltoid caps must be COAT, not shirt -- as shirt they punched two cream
    # blobs out through the coat shoulders in profile.
    build_mats["shoulder"] = mats["jacket"]

    parts, joints = build_body(PROPS_B, build_mats)
    P = PROPS_B

    chest_top = joints["chest_z"] + P["chest_h"] / 2.0
    hem_z = joints["hip_z"] - 0.045

    # Jacket body: an open coat. Built as back slab + two front panels with a
    # cream gap between them, so the shirt shows through as a vertical stripe.
    jacket_h = chest_top - hem_z
    jacket_cz = (chest_top + hem_z) / 2.0

    # The coat must stay INSIDE the shoulder line. The first pass wrapped the
    # sides too, which merged coat and arms into one slab and wiped out the
    # arm silhouette -- the figure stopped reading as a person at all.
    parts.append(rbox("Jacket_Back",
                      (P["chest_w"] + 0.016, P["chest_d"] * 0.44, jacket_h),
                      (0, -(P["chest_d"] / 2.0) * 0.60, jacket_cz),
                      mats["jacket"], round_ratio=0.30, segments=3))
    for side in (1, -1):
        parts.append(rbox("Jacket_Front",
                          (P["chest_w"] * 0.44, P["chest_d"] * 0.42, jacket_h),
                          (side * (P["chest_w"] * 0.275),
                           (P["chest_d"] / 2.0) * 0.68, jacket_cz),
                          mats["jacket"], round_ratio=0.30, segments=3))
        # Lapel -- a slanted flap at the chest opening.
        parts.append(rbox("Jacket_Lapel",
                          (P["chest_w"] * 0.19, 0.028, P["chest_h"] * 0.50),
                          (side * (P["chest_w"] * 0.150),
                           (P["chest_d"] / 2.0) * 0.88,
                           chest_top - P["chest_h"] * 0.30),
                          mats["jacket"], round_ratio=0.32,
                          rot=(0, side * math.radians(10), 0)))
    # Hem lip -- a hard horizontal line low on the body. Strong at distance.
    parts.append(rbox("Jacket_Hem",
                      (P["chest_w"] + 0.028, P["chest_d"] + 0.024, 0.044),
                      (0, 0, hem_z + 0.020),
                      mats["jacket"], round_ratio=0.34, segments=3))
    # Shoulders/yoke, so the coat sits ON the figure rather than around it.
    parts.append(rbox("Jacket_Yoke",
                      (P["chest_w"] + 0.030, P["chest_d"] + 0.020, 0.105),
                      (0, 0, chest_top - 0.030),
                      mats["jacket"], round_ratio=0.40, segments=3))
    # Stand collar. Without it the cream shirt shows as a ring around the
    # neck and reads as a surgical collar rather than a shirt under a coat.
    parts.append(rbox("Jacket_Collar",
                      (P["neck_w"] + 0.075, P["chest_d"] * 0.72, 0.062),
                      (0, -0.006, chest_top + 0.022),
                      mats["jacket"], round_ratio=0.38, segments=3))

    # --- hat: the single strongest read at distance ----------------------
    # Sat too low first time and turned the whole face into a black void, so
    # the brim now rides above the brow with the crown clear of the skull.
    head_top = joints["head_z"] + P["head_h"] / 2.0
    brim_z = head_top - 0.010
    parts.append(lib.cylinder("Hat_Brim", 0.208, 0.018,
                              (0, P["head_fwd"] + 0.006, brim_z), verts=24,
                              mat=mats["hat"], bevel=0.006))
    parts.append(lib.cylinder("Hat_Crown", 0.113, 0.125,
                              (0, P["head_fwd"] + 0.006, brim_z + 0.064),
                              verts=20, mat=mats["hat"], bevel=0.012))
    parts.append(lib.cylinder("Hat_Band", 0.120, 0.028,
                              (0, P["head_fwd"] + 0.006, brim_z + 0.024),
                              verts=20, mat=mats["bag"], bevel=0.004))

    # --- shoulder bag: second silhouette cue, and it breaks the symmetry --
    # Hangs OUTBOARD of the coat and forward of the hip so it clears the body
    # outline from the front as well as the side. Buried inside the coat on
    # the first pass and was effectively invisible.
    strap_top = (0.058, -0.010, chest_top - 0.020)
    bag_c = (-0.185, 0.175, joints["hip_z"] - 0.030)
    parts.append(limb("Bag_Strap", strap_top,
                      (bag_c[0] + 0.020, bag_c[1] - 0.070, bag_c[2] + 0.115),
                      0.044, 0.026, mats["bag"], round_ratio=0.34))
    parts.append(rbox("Bag_Body", (0.115, 0.230, 0.205),
                      bag_c, mats["bag"], round_ratio=0.30, segments=3))
    parts.append(rbox("Bag_Flap", (0.124, 0.238, 0.078),
                      (bag_c[0], bag_c[1], bag_c[2] + 0.086),
                      mats["bag"], round_ratio=0.34, segments=3))

    parts += add_hair(P, joints, mats["hair"], style="cropped")
    return lib.join(parts, "Customer_B")


# --------------------------------------------------------------------------
# Preview: front AND side. A figure that reads from the front and is broken
# from the side is the classic failure, so both get rendered every run.
# --------------------------------------------------------------------------

def clear_preview_rig():
    for obj in list(bpy.data.objects):
        if obj.type in {"LIGHT", "CAMERA"}:
            bpy.data.objects.remove(obj, do_unlink=True)


def add_key_light(azimuth, energy=3.2, elevation=48.0):
    """
    Sun shining FROM the camera's side. lib.render_preview installs one fixed
    sun off at -Y, which leaves whichever side the camera is on in shadow --
    fine for a table, useless for judging a face. Preview lighting only; it is
    deleted before the next view and never exported.
    """
    bpy.ops.object.light_add(type="SUN", location=(0, 0, 4))
    sun = bpy.context.active_object
    sun.data.energy = energy
    sun.rotation_euler = (math.radians(elevation), 0.0,
                          math.radians(azimuth - 18.0))
    return sun


def preview_views(asset_name, focus, distance):
    _, png = lib.out_paths(asset_name)
    base = png[:-4]
    # NB azimuth 0 puts the camera at -Y looking towards +Y, i.e. it renders
    # the BACK of a character built facing +Y. Front is azimuth 180. Getting
    # this backwards means "reviewing" a figure whose face you never see.
    for suffix, az, el in (("_front", 180.0, 8.0),
                           ("_side", 90.0, 8.0),
                           ("_34", 142.0, 16.0),
                           ("_back", 0.0, 8.0)):
        clear_preview_rig()
        add_key_light(az)
        lib.render_preview(f"{base}{suffix}.png", focus=focus,
                           distance=distance, azimuth=az, elevation=el,
                           resolution=560)


def emit(obj, asset_name, focus, distance):
    lib.set_origin_to_floor(obj)
    glb, _ = lib.out_paths(asset_name)
    lib.shade_smooth_by_angle(obj, 35)
    lib.export_glb(obj, glb)
    # FBX is the format Unity imports natively -- see lib.export_fbx. This
    # script builds its own export call rather than using lib.finish, so it
    # must emit both formats explicitly.
    lib.export_fbx(obj, glb[:-4] + ".fbx")

    dims = obj.dimensions
    print(f"ART_DIMS {asset_name} w={dims.x:.3f} d={dims.y:.3f} h={dims.z:.3f} "
          f"tris~{len(obj.data.polygons)}")
    print(f"ART_SLOTS {asset_name} " +
          ",".join(s.material.name for s in obj.material_slots if s.material))

    preview_views(asset_name, focus, distance)


def main():
    lib.reset_scene()
    a = build_customer_a()
    emit(a, "Customer_A", focus=(0, 0, 0.92), distance=3.6)

    lib.reset_scene()
    b = build_customer_b()
    emit(b, "Customer_B", focus=(0, 0, 0.92), distance=3.6)


if __name__ == "__main__":
    main()
