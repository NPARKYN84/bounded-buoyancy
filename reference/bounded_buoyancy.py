#!/usr/bin/env python3
"""
bounded_buoyancy.py -- reference implementation and benchmarks for
  Parkyn, N. (2026). Bounded Buoyancy: Added Mass and Two Ceilings for Stable, Accurate Buoyancy
  in Game and Simulation Engines, v1.1. Companion paper to
  Parkyn, N. (2026). ValerieX (VXXX): The Density-State Drive -- a single-drive account of weight
  and buoyancy, with two predictions the two-force account does not make, v2.2, with Supplementary Information. Zenodo.

Usage:  python3 bounded_buoyancy.py            # print every benchmark number
        python3 bounded_buoyancy.py --figures  # also write the figures

A sphere of radius R and density rho_o moves vertically through a water surface at z = 0 (z up).
Three per-step update rules, all advanced with semi-implicit (symplectic) Euler at a fixed game
timestep, as Unity/PhysX, Godot and Bullet do:
  NAIVE    a = F / (rho_o V)                              -- buoyancy as a force on the body's own mass
  EXPLICIT a = (F - m_a * a_prev) / (rho_o V)             -- added mass as a lagged force
  BOUNDED  a = F / (rho_o V + m_a),  m_a = C rho_m V_sub  -- added mass in the inertia (ValerieX)
           with momentum kept when m_a grows (water entry; von Karman 1929)
Weight and buoyancy act explicitly; quadratic drag on the submerged part is applied implicitly in all
three, as engines apply damping. The FINE-STEP REFERENCE is the BOUNDED physics integrated at dt = 1e-5 s;
it measures the error of the 60 Hz update, not physical accuracy. The NAIVE model is also run at the fine step,
so that its discretisation error and its model error (no added mass, no entry rule) can be told apart.
"""
import sys
import numpy as np

g = 9.80665
RHO_W = 1000.0
C_SPHERE = 0.5
CD = 0.47
R_TT = 2.7e-3 / (4.0 / 3.0 * np.pi * 0.02**3) / RHO_W   # table-tennis ball, 2.7 g, 40 mm: r = 0.0806


def cap_volume(R, z):
    """Submerged volume and waterplane radius^2 of a sphere (centre height z) cut by the plane z = 0."""
    h = np.clip(R - z, 0.0, 2 * R)
    return np.pi * h**2 * (3 * R - h) / 3.0, h


def step(model, R, rho_o, z, v, a_prev, ma_prev, dt, C=C_SPHERE, rho_m=RHO_W, cd=CD):
    """One fixed-timestep update. Works on scalars or NumPy arrays.
    Weight and buoyancy are explicit; drag is applied implicitly (v_new = v_trial / (1 + k|v| dt / M)),
    as engines apply damping, so that every model gets the same stable drag treatment."""
    V = 4.0 / 3.0 * np.pi * R**3
    m = rho_o * V
    Vs, h = cap_volume(R, z)
    ma = C * rho_m * Vs
    Fb = -m * g + rho_m * Vs * g                                  # density-state drive (weight + buoyancy)
    kd = 0.5 * rho_m * cd * np.pi * R**2 * (Vs / V)                # quadratic drag on the submerged part
    if model == "naive":
        M = m; acc = Fb / m
    elif model == "explicit":
        M = m; acc = (Fb - ma * a_prev) / m
    elif model == "bounded":
        M = m + ma
        v = np.where(ma > ma_prev, v * (m + ma_prev) / M, v)     # keep momentum on water entry
        acc = Fb / M
    else:
        raise ValueError(model)
    v_new = (v + acc * dt) / (1.0 + kd * np.abs(v) * dt / M)
    a = (v_new - v) / dt
    z = z + v_new * dt
    return z, v_new, a, ma


def run(model, R, rho_o, z0, T, dt, v0=0.0):
    n = int(round(T / dt))
    z = np.empty(n + 1); v = np.empty(n + 1); a = np.empty(n)
    z[0], v[0] = z0, v0
    ap, map_ = 0.0, C_SPHERE * RHO_W * cap_volume(R, z0)[0]
    for i in range(n):
        z[i + 1], v[i + 1], ap, map_ = step(model, R, rho_o, z[i], v[i], ap, map_, dt)
        a[i] = ap
        if not np.isfinite(z[i + 1]) or abs(z[i + 1]) > 1e4:
            z[i + 1:] = np.nan; v[i + 1:] = np.nan; a[i:] = np.nan
            break
    return np.arange(n + 1) * dt, z, v, a


def z_equilibrium(R, r):
    """Centre height of a floating sphere (r < 1): solve V_sub/V = r by bisection (vectorised)."""
    lo = np.zeros_like(R); hi = 2 * R
    for _ in range(60):
        h = 0.5 * (lo + hi)
        f = h**2 * (3 * R - h) / (4 * R**3) - r
        lo = np.where(f < 0, h, lo); hi = np.where(f >= 0, h, hi)
    return R - 0.5 * (lo + hi)


def surfacing_metrics(t, z):
    if np.any(~np.isfinite(z)):
        return np.nan, np.nan
    k = np.argmax(z >= 0)
    return (t[k] if z[k] >= 0 else np.nan), np.nanmax(z)


def bob_period(t, z, zeq):
    d = z - zeq
    s = np.where(np.diff(np.sign(d)) != 0)[0]
    if len(s) < 3:
        return np.nan
    return 2 * np.mean(np.diff(t[s][:7]))


def main(figures=False):
    dt = 1 / 60
    L = "-" * 92
    print(L); print("B1  First-frame acceleration of a body released from rest, fully submerged (m/s^2; up +)"); print(L)
    for lab, r in [("air bubble (r = 0.001225)", 0.001225), ("foam sphere (r = 0.03)", 0.03), ("table-tennis ball (r = 0.081)", R_TT),
                   ("cork (r = 0.25)", 0.25), ("ice (r = 0.917)", 0.917), ("rock (r = 2.7)", 2.7)]:
        out = []
        for model in ["naive", "explicit", "bounded"]:
            _, z, v, a = run(model, 0.02, r * RHO_W, -0.5, 2 * dt, dt)
            out.append(a[0])
        a_ref = g * (1 - r) / (r + C_SPHERE)
        print(f"  {lab:30s} naive {out[0]:9.1f} ({out[0]/g:7.1f} g) | explicit {out[1]:8.1f} | bounded {out[2]:7.2f} ({out[2]/g:5.2f} g) | classical {a_ref:7.2f}")
        if r < 0.01:
            print(f"  {'':30s} naive displacement in the first frame: {0.5*abs(out[0])*dt**2*100:.0f} cm (semi-implicit Euler moves {abs(out[0])*dt**2*100:.0f} cm)")

    print(L); print("B2  Release from 0.5 m depth, R = 2 cm, 60 Hz: time to surface (s) and peak centre height above surface (cm)"); print(L)
    print(f"  {'body':14s} {'fine-step ref':>22s} {'bounded':>22s} {'naive':>22s} {'naive fine-step':>22s} {'explicit':>14s}")
    b2 = {}
    for lab, r in [("foam", 0.03), ("table-tennis", R_TT), ("cork", 0.25), ("pine", 0.5), ("ice", 0.917)]:
        row = {}
        for model, ddt in [("bounded", 1e-5), ("bounded", dt), ("naive", dt), ("naive", 1e-5), ("explicit", dt)]:
            t, z, v, a = run(model, 0.02, r * RHO_W, -0.5, 3.0, ddt)
            row[(model, ddt)] = surfacing_metrics(t, z)
        b2[lab] = row
        ref = row[("bounded", 1e-5)]; bo = row[("bounded", dt)]; na = row[("naive", dt)]; nf = row[("naive", 1e-5)]; ex = row[("explicit", dt)]
        fmt = lambda p: f"{p[0]:6.3f} s {100*p[1]:6.1f} cm" if np.isfinite(p[0]) else "      diverges     "
        print(f"  {lab:14s} {fmt(ref):>22s} {fmt(bo):>22s} {fmt(na):>22s} {fmt(nf):>22s} {('diverges' if not np.isfinite(ex[0]) else fmt(ex)):>14s}")

    print(L); print("B3  Floating sphere released from rest with its lowest point at the surface, R = 10 cm, 60 Hz, 10 s"); print(L)
    print("  r       draft(mm)  period ref / bounded / naive / naive fine-step (s)   RMS bob at 8-10 s (mm): ref / bounded / naive / naive fine-step")
    for r in [0.002, 0.005, 0.01, 0.05, 0.2, 0.5]:
        R = 0.1; zeq = float(z_equilibrium(np.array([R]), np.array([r]))[0])
        res = {}
        for model, ddt in [("bounded", 1e-5), ("bounded", dt), ("naive", dt), ("naive", 1e-5)]:
            t, z, v, a = run(model, R, r * RHO_W, R, 10.0, ddt)
            last = t >= 8.0
            res[model, ddt] = (bob_period(t, z, zeq), 1000 * np.sqrt(np.nanmean((z[last] - zeq)**2)))
        pr, pb, pn, pf = res["bounded", 1e-5][0], res["bounded", dt][0], res["naive", dt][0], res["naive", 1e-5][0]
        print(f"  {r:<6} {1000*(R-zeq):8.1f}   {pr:6.3f} / {pb:6.3f} / {pn:6.3f} / {pf:6.3f}               "
              f"{res['bounded', 1e-5][1]:6.3f} / {res['bounded', dt][1]:6.3f} / {res['naive', dt][1]:6.3f} / {res['naive', 1e-5][1]:6.3f}")
    print(L); print("B3b Shallow float r = 0.002: heave added mass of a disc of the same waterline radius (high-frequency, free surface)"); print(L)
    R = 0.1; r = 0.002; zeq = float(z_equilibrium(np.array([R]), np.array([r]))[0]); h = R - zeq
    aw = np.sqrt(2 * R * h - h * h); Vsub = r * 4 / 3 * np.pi * R**3; k = RHO_W * g * np.pi * aw**2; m = r * RHO_W * 4 / 3 * np.pi * R**3
    ma_sub = C_SPHERE * RHO_W * Vsub; ma_disc = 4 / 3 * RHO_W * aw**3
    T = lambda M: 2 * np.pi * np.sqrt(M / k)
    print(f"  waterline radius {100*aw:.2f} cm; added mass: submerged-body {1000*ma_sub:.1f} g, disc {1000*ma_disc:.1f} g (x{ma_disc/ma_sub:.1f})")
    print(f"  linear periods: usual {T(m):.3f} s, bounded {T(m+ma_sub):.3f} s, with the disc value {T(m+ma_disc):.3f} s (x{T(m+ma_disc)/T(m):.1f} the usual)")

    print(L); print("B4  Shallow floaters: smallest stable draft at 60 Hz (linear analysis, semi-implicit Euler, omega*dt < 2)"); print(L)
    print(f"  analytic, naive   : omega^2 = g A_w / V_sub ~ 2g/h          -> draft h > g dt^2 / 2       = {g*dt**2/2*1000:.2f} mm")
    print(f"  analytic, bounded : omega^2 = g A_w / ((1+C) V_sub)          -> draft h > g dt^2 / (2(1+C)) = {g*dt**2/(2*(1+C_SPHERE))*1000:.2f} mm")
    if figures:
        draw(b2)


def draw(b2):
    import os; os.makedirs("figures", exist_ok=True)
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    NAVY, ORANGE, TEAL, GREY, INK = "#1F3864", "#C8641E", "#2A9D8F", "#8C8C8C", "#262626"
    plt.rcParams.update({"font.family": "DejaVu Sans", "font.size": 9, "axes.edgecolor": "#7F7F7F", "axes.linewidth": 0.8,
                         "axes.titlesize": 10, "axes.titleweight": "bold", "axes.titlecolor": NAVY, "legend.frameon": False})
    # Figure: first-frame (release) acceleration against density ratio
    fig, ax = plt.subplots(figsize=(7.0, 3.9), dpi=220)
    r = np.logspace(-3, np.log10(0.999), 400)
    ax.plot(r, (1 - r) / r, color=ORANGE, lw=2, ls="--", label="buoyancy on the body’s own mass")
    ax.plot(r, (1 - r) / (r + 0.5), color=TEAL, lw=2.2, label="bounded buoyancy, sphere (C = 0.5)")
    ax.plot(r, (1 - r) / (r + 1.0), color=NAVY, lw=1.6, ls="-.", label="bounded buoyancy, side-on cylinder (C = 1)")
    ax.axhline(2, color=TEAL, lw=0.8, ls=":"); ax.axhline(1, color=NAVY, lw=0.8, ls=":")
    ax.text(0.0012, 2.25, "ceiling g/C = 2g (sphere)", fontsize=7.6, color=TEAL)
    ax.text(0.0012, 0.66, "ceiling g/C = g (side-on cylinder)", fontsize=7.6, color=NAVY)
    for lab, rr in [("air bubble", 0.001225), ("foam", 0.03), ("table-tennis ball", R_TT), ("cork", 0.25)]:
        ax.plot(rr, (1 - rr) / rr, "o", ms=5, color=ORANGE, mec="white", mew=1)
        ax.annotate(f"{lab}: {(1-rr)/rr:.0f}g", xy=(rr, (1 - rr) / rr), xytext=(6, 2), textcoords="offset points", fontsize=7.4, color=INK)
    ax.set_xscale("log"); ax.set_yscale("log"); ax.set_xlim(1e-3, 1); ax.set_ylim(0.05, 2000)
    ax.set_xlabel("density ratio r = ρₒ/ρ_water"); ax.set_ylabel("upward acceleration at release, in g")
    ax.set_title("Release from rest under water: buoyancy on the body’s own mass has no ceiling")
    ax.grid(True, which="major", color="#E6E6E6", lw=0.6); ax.legend(loc="upper right", fontsize=7.6)
    fig.tight_layout(); fig.savefig("figures/fig_release_accel.png"); plt.close(fig)

    # Figure: floating sphere dropped from touching the surface
    fig, ax = plt.subplots(figsize=(7.0, 3.3), dpi=220)
    R, rr = 0.1, 0.05; zeq = float(z_equilibrium(np.array([R]), np.array([rr]))[0])
    for model, ddt, col, ls, lw, name in [("bounded", 1e-5, INK, "-", 1.2, "fine-step reference"), ("bounded", 1 / 60, TEAL, "-", 1.8, "bounded, 60 Hz"),
                                          ("naive", 1 / 60, ORANGE, "--", 1.8, "buoyancy on own mass, 60 Hz")]:
        t, z, v, a = run(model, R, rr * RHO_W, R, 3.0, ddt)
        ax.plot(t, (z - zeq) * 1000, color=col, ls=ls, lw=lw, label=name)
    ax.axhline(0, color=GREY, lw=0.7)
    ax.set_xlim(0, 3); ax.set_xlabel("time (s)"); ax.set_ylabel("height above floating level (mm)")
    ax.set_title("A light float (R = 10 cm, r = 0.05) released at the surface", fontsize=9.5, loc="left")
    ax.grid(True, color="#E6E6E6", lw=0.6); ax.legend(loc="upper right", fontsize=7.4)
    fig.tight_layout(); fig.savefig("figures/fig_float.png"); plt.close(fig)

    # Figure: release and surfacing trajectories (table-tennis ball and foam)
    fig, axs = plt.subplots(1, 2, figsize=(7.4, 3.4), dpi=220, sharey=True)
    for ax, (lab, r) in zip(axs, [("table-tennis ball, r = 0.081", R_TT), ("foam sphere, r = 0.03", 0.03)]):
        for model, ddt, col, ls, name in [("bounded", 1e-5, INK, "-", "fine-step reference"), ("bounded", 1 / 60, TEAL, "-", "bounded, 60 Hz"),
                                          ("naive", 1 / 60, ORANGE, "--", "buoyancy on own mass, 60 Hz"),
                                          ("naive", 1e-5, GREY, ":", "buoyancy on own mass, fine step")]:
            t, z, v, a = run(model, 0.02, r * RHO_W, -0.5, 1.5, ddt)
            ax.plot(t, z * 100, color=col, ls=ls, lw=1.8 if ddt > 1e-4 else (1.6 if model == "naive" else 1.2), label=name)
        ax.axhline(0, color="#7FB3D5", lw=1); ax.text(0.03, 1.2, "water surface", ha="left", fontsize=7.2, color="#2E86C1")
        ax.set_title(lab, fontsize=9.5, loc="left"); ax.set_xlabel("time after release (s)"); ax.set_xlim(0, 1.5)
        ax.grid(True, color="#E6E6E6", lw=0.6)
    axs[0].set_ylabel("height of centre (cm)"); axs[0].set_ylim(-55, 15)
    axs[0].legend(loc="lower right", fontsize=7.2)
    fig.tight_layout(); fig.savefig("figures/fig_release.png"); plt.close(fig)
    print("figures written")


if __name__ == "__main__":
    main(figures="--figures" in sys.argv)
