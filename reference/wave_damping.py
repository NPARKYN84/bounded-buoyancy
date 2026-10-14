"""Where Bounded Buoyancy's wave-making damping comes from.

A body bobbing at the surface sends out ripples that carry its energy away (radiation damping). Linear wave theory
gives it exactly for any shape, but only by solving for the flow round the body. This script does that with the
open-source boundary-element solver Capytaine (pip install capytaine) for floating boxes, upright cylinders, spheres
and logs, from deep and narrow to wide and shallow, and for long boxes (towards the side-view, 2D, case). For each it
finds the body's own bobbing frequency (with the real added mass of the water) and the damping ratio there,

    zeta = B / (2 (m + A) omega),

the fraction of critical damping: a bob dies away as exp(-zeta omega t), losing a share 1 - exp(-2 pi zeta) of its
height each swing. It then fits

    zeta = a s^q / (1 + b s^q)

to the results, s being how wide the body is against its draft, worked out from what the plugin knows each step (the
volume under water V and the depth of its centre z_B), for the prism with the same V and z_B: draft d = 2 z_B and
waterplane area A = V / d. In 3D s = A / d^2; in a side view (2D, per metre into the screen) s = beam / draft.

    python3 wave_damping.py          # solve every case (about 15 minutes), write wave_damping.csv, fit
    python3 wave_damping.py --fit    # fit the cases in wave_damping.csv only (no Capytaine needed)
    python3 wave_damping.py --raft   # how far the comparison's raft rises and falls on its waves (under a minute)

Deep water, density 1000 kg/m^3, g 9.81 m/s^2; all results are for bodies floating at rest (own mass = mass of water
displaced), and scale with size (Froude scaling), so one size of each is enough.
"""
import csv
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
CSV = os.path.join(HERE, "wave_damping.csv")
RHO, G = 1000.0, 9.81


def solve_all():
    import warnings
    import logging
    import numpy as np
    import capytaine as cpt
    warnings.filterwarnings("ignore")
    logging.getLogger().setLevel(logging.ERROR)
    solver = cpt.BEMSolver()

    def heave(body, w):
        r = solver.solve(cpt.RadiationProblem(body=body, radiating_dof="Heave", omega=w, water_depth=np.inf, rho=RHO, g=G),
                         keep_details=False)
        return r.added_mass["Heave"], r.radiation_damping["Heave"]

    rows = []

    def case(family, name, mesh_full, length=1.0):
        """length: for long boxes, divide by it to give per metre (side view)."""
        mesh = mesh_full.immersed_part()
        z = mesh.lowest_lid_position(omega_max=30.0)   # a lid inside the body removes irregular frequencies
        if not np.isfinite(z):
            z = -0.001
        body = cpt.FloatingBody(mesh=mesh, lid_mesh=mesh.generate_lid(z=z), dofs=cpt.rigid_body_dofs())
        V, Awp, zb = mesh.volume, mesh.waterplane_area, -mesh.center_of_buoyancy[2]
        own = RHO * V   # floating at rest
        w = math.sqrt(RHO * G * Awp / (1.5 * own))
        for _ in range(12):
            A, B = heave(body, w)
            w2 = math.sqrt(RHO * G * Awp / (own + A))
            if abs(w2 - w) < 1e-3 * w:
                w = w2
                break
            w = 0.5 * (w + w2)
        A, B = heave(body, w)
        d = 2.0 * zb
        s = V / d ** 3 if family != "side view" else V / length / d ** 2
        zeta = B / (2.0 * (own + A) * w)
        rows.append(dict(family=family, case=name, s=s, zeta=zeta, added_mass=A / own, waterplane_ratio=Awp * d / V,
                         faces=mesh.nb_faces))
        print("%-12s %-34s s=%9.3f zeta=%.4f" % (family, name, s, zeta))
        sys.stdout.flush()

    for d in [0.05, 0.1, 0.2, 0.35, 0.5, 0.8, 1.2, 2.0]:
        case("flat", "box 1 x 1, draft %g" % d,
             cpt.mesh_parallelepiped(size=(1, 1, 2 * d), center=(0, 0, 0), resolution=(16, 16, max(4, int(2 * d / 0.06)))))
    for d in [0.05, 0.1, 0.25, 0.5]:
        case("flat", "box 4 x 1, draft %g" % d,
             cpt.mesh_parallelepiped(size=(4, 1, 2 * d), center=(0, 0, 0), resolution=(40, 10, max(4, int(2 * d / 0.06)))))
    for d in [0.1, 0.25, 0.5, 1.0, 2.0]:
        case("flat", "upright cylinder r 0.5, draft %g" % d,
             cpt.mesh_vertical_cylinder(length=2 * d, radius=0.5, center=(0, 0, 0), resolution=(4, 24, max(4, int(2 * d / 0.06)))))
    for dens in [0.1, 0.25, 0.5, 0.75, 0.9]:
        lo, hi = 0.0, 1.0   # fraction x of the diameter under water: x^2 (3 - 2x) = density
        for _ in range(60):
            x = 0.5 * (lo + hi)
            lo, hi = (x, hi) if x * x * (3 - 2 * x) < dens else (lo, x)
        case("round" if dens <= 0.5 else "deep round", "sphere, density %g" % dens,
             cpt.mesh_sphere(radius=1.0, center=(0, 0, 1.0 - 2 * x), resolution=(24, 48)))
    for dens in [0.3, 0.5]:
        lo, hi = 0.0, 2 * math.pi   # angle t the wetted arc spans: (t - sin t) / 2 pi = density
        for _ in range(60):
            t = 0.5 * (lo + hi)
            lo, hi = (t, hi) if (t - math.sin(t)) / (2 * math.pi) < dens else (lo, t)
        keel = 1.0 - math.cos(0.5 * t)
        case("round", "log r 1, length 6, density %g" % dens,
             cpt.mesh_horizontal_cylinder(length=6.0, radius=1.0, center=(0, 0, 1.0 - keel), resolution=(10, 32, 40)))
    # Side view: boxes 1 m wide and 32 m long (16 m for the deepest and shallowest), per metre of length.
    for d, length in [(2.0, 16.0), (1.0, 32.0), (0.5, 32.0), (0.25, 32.0), (0.1, 32.0), (0.05, 16.0)]:
        dx = 0.2
        case("side view", "box 1 wide, %g long, draft %g" % (length, d),
             cpt.mesh_parallelepiped(size=(length, 1.0, 2 * d), center=(0, 0, 0),
                                     resolution=(int(length / dx), 8, max(4, int(2 * d / 0.08)))), length)
    with open(CSV, "w", newline="") as f:
        wr = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
        wr.writeheader()
        wr.writerows(rows)


def fit():
    import numpy as np
    from scipy.optimize import least_squares
    with open(CSV) as f:
        rows = list(csv.DictReader(f))

    def model(p, s):
        a, b, q = p
        x = s ** q
        return a * x / (1.0 + b * x)

    # 3D: flat- and round-bottomed bodies together (the deep, narrow-waisted spheres left out); side view: q = 1.
    for label, families, q_free in [("3D", ("flat", "round"), True), ("side view", ("side view",), False)]:
        use = [r for r in rows if r["family"] in families]
        s = np.array([float(r["s"]) for r in use])
        z = np.array([float(r["zeta"]) for r in use])
        if q_free:
            p = least_squares(lambda p: np.log(model(p, s)) - np.log(z), [0.02, 0.08, 0.9], bounds=([1e-5, 0, 0.1], [1, 10, 2])).x
        else:
            p = least_squares(lambda p: np.log(model([p[0], p[1], 1.0], s)) - np.log(z), [0.05, 0.15], bounds=([1e-5, 0], [1, 10])).x
            p = [p[0], p[1], 1.0]
        p = [float("%.3g" % v) for v in p]   # as the plugin has them
        print("%s: zeta = %g s^%g / (1 + %g s^%g)" % (label, p[0], p[2], p[1], p[2]))
        for r in rows:
            if (label == "3D" and r["family"] != "side view") or (label != "3D" and r["family"] == "side view"):
                print("   %-12s %-34s s=%9.3f exact %.4f fit %.4f (x %.2f)" % (r["family"], r["case"], float(r["s"]),
                      float(r["zeta"]), model(p, float(r["s"])), model(p, float(r["s"])) / float(r["zeta"])))


def raft():
    """The raft of unity/checks/Compare (60 x 40 cm, 15 cm deep, 400 kg/m^3, so 6 cm under) on that test's waves: the
    heave response (how far it rises and falls, of the wave's own rise and fall) to each of the two waves that make up
    the surface (for 3 m waves, 3 m long head on and 1.89 m long 35 degrees round; the same for 12 m waves), free to
    heave, pitch, roll and the rest."""
    import warnings
    import logging
    import numpy as np
    import xarray as xr
    import capytaine as cpt
    warnings.filterwarnings("ignore")
    logging.getLogger().setLevel(logging.ERROR)
    full = cpt.mesh_parallelepiped(size=(0.6, 0.4, 0.15), center=(0, 0, 0.015), resolution=(30, 20, 8))
    mesh = full.immersed_part()
    m = 400.0 * 0.6 * 0.4 * 0.15
    body = cpt.FloatingBody(mesh=mesh, lid_mesh=mesh.generate_lid(z=-0.001), dofs=cpt.rigid_body_dofs(rotation_center=(0, 0, 0.015)),
                            center_of_mass=(0, 0, 0.015), mass=m)
    body.inertia_matrix = body.add_dofs_labels_to_matrix(np.diag([m, m, m, m * (0.4 ** 2 + 0.15 ** 2) / 12,
                                                                  m * (0.6 ** 2 + 0.15 ** 2) / 12, m * (0.6 ** 2 + 0.4 ** 2) / 12]))
    body.hydrostatic_stiffness = body.compute_hydrostatic_stiffness(rho=RHO, g=G)
    for length, heading in [(3.0, 0.0), (3.0 * 0.63, 35.0), (12.0, 0.0), (12.0 * 0.63, 35.0)]:
        w = math.sqrt(G * 2.0 * math.pi / length)
        test = xr.Dataset(coords={"omega": [w], "wave_direction": [math.radians(heading)], "radiating_dof": list(body.dofs),
                                  "water_depth": [np.inf], "rho": [RHO], "g": [G]})
        rao = cpt.post_pro.rao(cpt.BEMSolver().fill_dataset(test, body, hydrostatics=True))
        print("raft on a %.2f m wave, %g degrees round: rises and falls %.2f times as far as the wave" %
              (length, heading, abs(complex(rao.sel(radiating_dof="Heave").values.squeeze()))))


if __name__ == "__main__":
    if "--raft" in sys.argv:
        raft()
        sys.exit(0)
    if "--fit" not in sys.argv:
        solve_all()
    fit()
