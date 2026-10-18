"""Static check of the numbers quoted in the release-1 mode cards (docs/tz/02_modes.md §2.9, §7.4, §11.4).

Run: python3 docs/dev/checks/spec_geometry_check.py   (exit code 0 = all claims hold)

It recomputes the validation formulas of M01 / M02 / M05 for the defaults and presets named in the spec and
fails if a preset or default is invalid or a quoted figure drifts. The same formulas must be implemented and
unit-tested in Core (spec §8.5 "Валидация параметров режимов"); this script only guards the document.
"""
import math
import sys

FAILS = []


def check(name, ok, detail=""):
    print(("OK   " if ok else "FAIL ") + name + (f"  [{detail}]" if detail else ""))
    if not ok:
        FAILS.append(name)


def deg(x):
    return math.degrees(x)


# --- M01 Ring Escape -------------------------------------------------------------------------------------------
MARGIN = 0.05  # m


def opening(r, gap_deg, t):
    """Free width of a gap whose edges are round caps of radius t/2 (spec §2.9 M01)."""
    return 2 * r * math.sin(math.radians(gap_deg) / 2) - t


def gap_min(r, t, ball):
    return 2 * deg(math.asin((ball + t + MARGIN) / (2 * r)))


def m01_valid(inner, spacing, t, ball, gap, count, shrink, rings, progression="squeeze"):
    if spacing < ball + t + MARGIN - 1e-9:
        return False, "spacing"
    if count * (gap + 20) > 360 + 1e-9:
        return False, "solid arc"
    for i in range(rings):
        g = gap if rings == 1 else gap * (1 - shrink * i / (rings - 1))
        r = inner if progression == "squeeze" else inner + i * spacing
        if opening(r, g, t) < ball + MARGIN - 1e-9:
            return False, f"ring {i} gap {g:.1f} opening {opening(r, g, t):.3f}"
    return True, ""


gm = gap_min(1.2, 0.12, 0.35)
check("M01 default gapMin = 25.0°", abs(gm - 25.0) < 0.05, f"{gm:.2f}")
check("M01 default max shrink ≈ 28 %", abs((1 - gm / 35) - 0.28) < 0.01, f"{1 - gm / 35:.3f}")
check("M01 defaults valid", m01_valid(1.2, 0.6, 0.12, 0.35, 35, 1, 0.0, 12)[0])
check("M01 shrink 50 % at defaults rejected", not m01_valid(1.2, 0.6, 0.12, 0.35, 35, 1, 0.5, 12)[0])
check("M01 gapCount 4 × 120° rejected", not m01_valid(1.2, 0.6, 0.12, 0.35, 120, 4, 0.0, 12)[0])
ok, why = m01_valid(1.6, 0.6, 0.06, 0.2, 30, 1, 0.5, 4)
check("M01 preset «Невозможный побег» (4 rings, 30° shrink 50 %) valid", ok, why)
check("M01 preset last gap 15°, opening 0.36 m", abs(opening(1.6, 15, 0.06) - 0.358) < 0.005, f"{opening(1.6, 15, 0.06):.3f}")
check("M01 preset gapMin 11.1°", abs(gap_min(1.6, 0.06, 0.2) - 11.1) < 0.05, f"{gap_min(1.6, 0.06, 0.2):.2f}")
ok, why = m01_valid(1.2, 0.6, 0.12, 0.35, 100, 2, 0.0, 100)
check("M01 preset «100 колец» (2 × 100°) valid", ok, why)
# Old preset (gap 12° with defaults) must be rejected: that is the bug the validation prevents.
check("M01 old «Невозможный побег» (defaults + 12°) rejected", not m01_valid(1.2, 0.6, 0.12, 0.35, 12, 1, 0.0, 12)[0])
# Speed envelope (QA round 2): a combo can pass ring i+1 before the 0.4 s shift ends and reach ring i+2, so the old
# reach "innerRadius + ringSpacing + t/2" was wrong. The clamp now uses each ring's current radius.
inner_face_i2 = 2.5 + 2 * 1.2 - 0.3 / 2
check("M01 regression: ring i+2 at 4.75 m would move 29.8 m/s at 360°/s", abs(2 * math.pi * inner_face_i2 - 29.8) < 0.05,
      f"{2 * math.pi * inner_face_i2:.1f} m/s")


def m01_omega_cap(r_mid, t):
    """|ω| cap in rad/s for a ring whose midline is at r_mid now (spec §2.9 M01 «Скорость колец»)."""
    return min(2 * math.pi, 25 / (r_mid + t / 2))


worst = max(m01_omega_cap(r / 100, t) * (r / 100 + t / 2) for r in range(30, 1201) for t in (0.04, 0.12, 0.3))
check("M01 per-ring clamp: wall speed ≤ 25 m/s on every radius up to 12 m", worst <= 25 + 1e-9, f"{worst:.2f} m/s")
check("M01 effective maxSpeed ≤ 30 m/s", worst + 5 <= 30 + 1e-9)
check("M01 ring at 12 m allows 119°/s", round(deg(m01_omega_cap(12, 0.12))) == 119, f"{deg(m01_omega_cap(12, 0.12)):.1f}")


# Growth (QA round 2): the cap must use the narrowest remaining ring, not the current one.
def d_grow(inner, spacing, t, gap, shrink, rings, cur=0, progression="squeeze"):
    ws = []
    for j in range(cur, rings):
        g = gap if rings == 1 else gap * (1 - shrink * j / (rings - 1))
        r = inner if progression == "squeeze" else inner + j * spacing
        ws.append(opening(r, g, t) - MARGIN)
    return min(spacing - t - MARGIN, min(ws))


shrink_max = math.floor((1 - gm / 35) * 100) / 100
old_cap = min(0.6 - 0.12 - MARGIN, opening(1.2, 35, 0.12) - MARGIN)
last_open = opening(1.2, 35 * (1 - shrink_max), 0.12)
check("M01 regression: old rule 4 lets ball grow to 0.430 m", abs(old_cap - 0.430) < 0.0005, f"{old_cap:.4f}")
check("M01 regression: last ring (25.2°) opening 0.404 m < 0.430 m", abs(last_open - 0.404) < 0.0005 and last_open < old_cap,
      f"{last_open:.4f}")
check("M01 regression: 3 hits of growOnHit 10 % reach the old cap", math.ceil(math.log(old_cap / 0.35) / math.log(1.1)) == 3)
dg = d_grow(1.2, 0.6, 0.12, 35, shrink_max, 12)
check("M01 d_grow = 0.354 m and the grown ball passes the last ring", abs(dg - 0.354) < 0.0006 and dg <= last_open - MARGIN + 1e-12,
      f"{dg:.4f}")
for prog in ("squeeze", "static"):
    for cur in range(12):
        cap = d_grow(1.2, 0.6, 0.12, 35, shrink_max, 12, cur, prog)
        later = [opening(1.2 if prog == "squeeze" else 1.2 + j * 0.6, 35 * (1 - shrink_max * j / 11), 0.12) for j in range(cur, 12)]
        if any(cap + MARGIN > w + 1e-12 for w in later):
            check(f"M01 d_grow passes every later ring ({prog}, cur {cur})", False)
check("M01 d_grow passes every later ring (squeeze and static, every current ring)", True)


def m01_moves(gravity_g, launch, e):
    return gravity_g >= 0.2 or (launch >= 2 and e >= 0.95)


check("M01 motion rule: defaults pass", m01_moves(1.0, 6, 1.0))
check("M01 motion rule: gravity 0 + launch 0 rejected", not m01_moves(0.0, 0, 1.0))
check("M01 motion rule: gravity 0 + e 0.8 rejected", not m01_moves(0.0, 6, 0.8))
# Camera-zoom cap: outer ring ≤ 12 m.
check("M01 camera-zoom ringCount at defaults = 19", math.floor((12 - 1.2) / 0.6) + 1 == 19)


# --- M02 Geometry Bounce ---------------------------------------------------------------------------------------
PHI_MAX = 0.55


def a_in(n, R, t):
    a = R * math.cos(math.pi / n) - t / 2
    return n * a * a * math.tan(math.pi / n)


hexa = a_in(6, 3.6, 0.12)
tri = a_in(3, 3.6, 0.12)
check("M02 hexagon A_in = 32.4 m²", abs(hexa - 32.4) < 0.05, f"{hexa:.2f}")
check("M02 triangle A_in = 15.7 m²", abs(tri - 15.7) < 0.05, f"{tri:.2f}")
n_hex = math.floor(PHI_MAX * hexa / (math.pi * 0.25 ** 2))
check("M02 hexagon holds 90 balls of 0.5 m", n_hex == 90, str(n_hex))
d200 = 2 * math.sqrt(PHI_MAX * hexa / (200 * math.pi))
check("M02 200 balls ≤ 0.34 m in hexagon", abs(d200 - 0.34) < 0.005, f"{d200:.3f}")
d200t = 2 * math.sqrt(PHI_MAX * tri / (200 * math.pi))
check("M02 200 balls ≤ 0.23 m with evolution (triangle)", abs(d200t - 0.23) < 0.006, f"{d200t:.3f}")
phi128 = 128 * math.pi * 0.15 ** 2 / hexa
check("M02 preset «Размножение» 128 × 0.3 m: φ ≤ 0.28", phi128 <= 0.28 and phi128 <= PHI_MAX, f"{phi128:.3f}")
phi30 = 30 * math.pi * 0.25 ** 2 / a_in(3, 3.6, 0.12)
check("M02 preset «Треугольник-хаос» 30 × 0.5 m within φmax", phi30 <= PHI_MAX, f"{phi30:.3f}")
w_cap = deg(25 / 4.2)
check("M02 triangle R 4.2 m: ω cap 341°/s", abs(w_cap - 341) < 0.5, f"{w_cap:.1f}")
check("M02 evolution circumradius ≤ 8.6 m", 2 * 1.02 * 4.2 <= 8.6 + 1e-9, f"{2 * 1.02 * 4.2:.2f}")
check("M02 evolution shrink speed ≈ 2.2 m/s", abs(0.52 * 4.2 - 2.2) < 0.05)
# Evolution (QA round 2): balls meet the new figure's walls beyond R, so the clamp uses the current circumradius Rc(t).
rc0 = 2.04 * 4.2
check("M02 regression: old clamp (R̂ = R) — corner of new figure at 51 m/s", abs(math.radians(w_cap) * rc0 - 51.0) < 0.1,
      f"{math.radians(w_cap) * rc0:.1f} m/s")
check("M02 evolution clamp at appearance = 167°/s", abs(deg(25 / rc0) - 167) < 0.5, f"{deg(25 / rc0):.1f}")
worst = max(min(2 * math.pi, 25 / (R * (2.04 - 1.04 * k / 100))) * R * (2.04 - 1.04 * k / 100)
            for R in (2.0, 3.6, 4.2) for k in range(101))
check("M02 evolution: wall speed ≤ 25 m/s during the whole morph", worst <= 25 + 1e-9, f"{worst:.2f}")
check("M02 evolution corner radial speed 1.04 R/s ≤ 5 m/s", 1.04 * 4.2 <= 5, f"{1.04 * 4.2:.2f}")
pulse_rad = 0.08 * 4.2 * 2 * math.pi / (3 / 2)
check("M02 pulse radial speed ≤ 1.5 m/s", pulse_rad <= 1.5, f"{pulse_rad:.2f}")
# Smallest ball (QA round 2): sizeSpread is capped so that the smallest diameter stays ≥ 0.1 m (radius ≥ 0.05 m, §6.5).


def spread_cap(ball):
    return min(1.0, max(0.0, 2 * (1 - 0.1 / ball)))


check("M02 regression: sizeSpread 100 % at 0.1 m used to give 0.05 m", abs(0.1 * (1 - 0.5) - 0.05) < 1e-12)
check("M02 sizeSpread cap: 0 at ballSize 0.1 m", spread_cap(0.1) == 0.0)
check("M02 d_min ≥ 0.1 m for every ballSize", all(b / 100 * (1 - spread_cap(b / 100) / 2) >= 0.1 - 1e-12 for b in range(10, 151)))
n_small = math.floor(PHI_MAX * a_in(3, 2.0, 0.3) / (math.pi * 0.05 ** 2))
check("M02 balls of 0.1 m in the smallest triangle (R 2.0, wall 0.3) = 262 ≥ tier 200", n_small == 262, str(n_small))

# --- M05 Helix Drop --------------------------------------------------------------------------------------------
R_PATH, T_PLAT = 1.5, 0.15


def foot(ball):
    return 2 * deg(math.asin(ball / 2 / R_PATH))


def h_max(spacing, ball):
    return spacing - T_PLAT - ball - 0.1


check("M05 footprint 0.5 m = 19.2°", abs(foot(0.5) - 19.2) < 0.05, f"{foot(0.5):.2f}")
check("M05 footprint 0.8 m = 30.9°", abs(foot(0.8) - 30.9) < 0.05, f"{foot(0.8):.2f}")
check("M05 1-sector gap allowed only up to 14 sectors (ball 0.5)", math.floor(360 / (foot(0.5) + 6)) == 14)
check("M05 default gap 2 of 12 sectors passable", 2 * 360 / 12 >= foot(0.5) + 6)
check("M05 «Безумная» preset (1 of 12) passable", 360 / 12 >= foot(0.5) + 6)
check("M05 default h_max = 1.05 m", abs(h_max(1.8, 0.5) - 1.05) < 1e-9)
check("M05 default bounceHeight 0.9 ≤ h_max", 0.9 <= h_max(1.8, 0.5))
check("M05 spacing ≥ ball + T + 0.4 gives h_max ≥ 0.3", abs(h_max(0.5 + T_PLAT + 0.4, 0.5) - 0.3) < 1e-9)


# Transit (QA round 2): the ball needs t_tr to fall through T + ballSize; the platform may turn more than the slack.
G = 9.81


def t_transit(ball, v0, g=G):
    return (math.sqrt(v0 * v0 + 2 * g * (T_PLAT + ball)) - v0) / g


t_def = t_transit(0.5, math.sqrt(2 * G * 0.9))
check("M05 transit at defaults 0.13 s", abs(t_def - 0.134) < 0.001, f"{t_def:.3f}")
t_max = t_transit(0.8, 0.0, 0.5 * G)
check("M05 longest transit 0.62 s (ball 0.8, v0 0, 0.5 g)", abs(t_max - 0.62) < 0.005, f"{t_max:.3f}")
check("M05 «Безумная» 240°/s turns 32° during transit vs 10.8° slack (transit rule needed)",
      abs(240 * t_def - 32.1) < 0.1 and abs(30 - foot(0.5) - 10.8) < 0.05, f"{240 * t_def:.1f} / {30 - foot(0.5):.1f}")
t_big = t_transit(0.8, math.sqrt(2 * G * 0.3))
check("M05 ball 0.8 m, bounce 0.3 m, 70°/s: 18° during transit vs 6° slack", abs(70 * t_big - 18.0) < 0.1, f"{70 * t_big:.1f}")
check("M05 6° window at 100°/s = 0.06 s ≈ 4 ticks", abs(6 / 100 - 0.06) < 1e-12 and round(0.06 * 60) == 4)
check("M05 gravity 0.5–2 g keeps v_min > 0", math.sqrt(2 * 0.5 * G * 0.3) > 0)


def pos_range(extent):
    return max(16, 2 ** math.ceil(math.log2(extent + 4)))


for count, spacing, want in ((30, 1.8, 64), (100, 3.0, 512), (100, 1.2, 128), (5, 1.2, 16)):
    pr = pos_range(count * spacing)
    step = pr / 32768
    px = 12 * spacing / 1920  # 12 platforms over a 1080×1920 frame
    check(f"M05 PositionRange {count}×{spacing} = {want} m, step ≤ 1 px", pr == want and step <= px,
          f"range {pr}, step {step * 1000:.2f} mm, px {px * 1000:.2f} mm")

# --- Roadmap §11.4 ---------------------------------------------------------------------------------------------
lo, hi = 3 + 3 + 5 + 3, 4 + 4 + 6 + 4
check("Roadmap П2–П5 = 14–18 weeks", (lo, hi) == (14, 18))
check("Roadmap ≈ 3–4 months", 3 <= lo / 4.345 and hi / 4.345 <= 4.2, f"{lo / 4.345:.2f}–{hi / 4.345:.2f}")
check("Wave 3 at 11–14 weeks ≈ 2.5–3.2 months", (3 + 3 + 5, 4 + 4 + 6) == (11, 14))

print(f"\n{len(FAILS)} failed")
sys.exit(1 if FAILS else 0)
