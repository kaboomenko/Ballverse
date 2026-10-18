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
ok, why = m01_valid(1.6, 0.6, 0.06, 0.2, 12, 1, 0.0, 12)
check("M01 preset «Невозможный побег» valid", ok, why)
check("M01 preset opening 0.27 m", abs(opening(1.6, 12, 0.06) - 0.274) < 0.005, f"{opening(1.6, 12, 0.06):.3f}")
check("M01 preset gapMin 11.1°", abs(gap_min(1.6, 0.06, 0.2) - 11.1) < 0.05, f"{gap_min(1.6, 0.06, 0.2):.2f}")
# Old preset (gap 12° with defaults) must be rejected: that is the bug the validation prevents.
check("M01 old «Невозможный побег» (defaults + 12°) rejected", not m01_valid(1.2, 0.6, 0.12, 0.35, 12, 1, 0.0, 12)[0])
# Squeeze: the ball touches at most innerRadius + ringSpacing (+ t/2); 360°/s stays within 25 m/s there.
reach = 2.5 + 1.2 + 0.3 / 2
check("M01 squeeze reach ≤ 3.85 m keeps 360°/s ≤ 25 m/s", 2 * math.pi * reach <= 25 + 1e-9, f"{2 * math.pi * reach:.1f} m/s")
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
