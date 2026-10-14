# Changelog

## [2.4.0] - 2026-10-14

- Flooding: **Bounded Buoyancy Compartment** on child objects of a body: boxes that fill with water through their **Holes** (orifice law) and drain through holes above the water, with **Pump Rate**, `AddHole(worldPoint, area)` and `PatchHoles()`. The water lies level as the body leans and is weight carried at its own centre, applied as weight plus its turning moment about the centre of mass, and in the inertia tensor as weight at its centre (worked out again when it changes by 1%, and back to the body's own once dry). Bodies report `FloodMass`. In the core, `Compartment3D` and `BodyCore3D.Compartments`, `FloodMass` and `FloodInertia`.
- Tests: a B17 play-mode test (a list toward the damaged side, upright again and the inertia back once pumped dry) and two edit-mode tests (the free-surface shift, the orifice law); the .NET checks add the Godot plugin's B17, with the flood water's inertia in the turning, and agree with it to the printed digits except the sinking speed of the filled bowl (2.41 m/s against 2.43 in Godot Physics, which depends on how it leans as it goes down). `Sym3.FromPrincipal` and `Sym3 +` in the core.

## [2.3.0] - 2026-10-14

- Wave-making damping: a body bobbing at the surface sends out ripples that carry its energy away, so floats now settle after a few bobs, as real ones do. It acts on bobbing relative to the surface under the body (the Bounded Buoyancy Water's waves, or HDRP's), so riding waves is not held back, and on the speed at the start of each step, so the first frame after a release is unchanged. How much depends on the shape, from linear wave theory: a raft or a boat loses about 70% of each bob, a cube or a ball floating half under about 27%, a deep post 3%. The damping ratios were found exactly with the open-source solver Capytaine for 30 floating shapes and fitted with one curve (`reference/wave_damping.py` in the repository): in the simulation, 0.8 to 1.5 times the exact value for flat-bottomed bodies, about 0.5 to 0.7 times for balls and logs floating at least half out of the water, and within about 2% in 2D.
- The water's push in waves: water accelerating up and down in the waves now pushes on everything in it, (1 + C) ρ V a, a the water's upward acceleration at the body averaged over its footprint. So a small raft rises and falls as far as the water does instead of a little further (99% on waves 20 times its length, against 105% before; linear wave theory gives 100%), and a body under the water moves up and down with the water round it. Held to half of g, as real waves never accelerate faster. Bounded model only; still water is unchanged. HDRP gives heights only, so on HDRP water bodies are buoyed by the surface alone, as before; a link to another water system can supply the water's acceleration by overriding the new `BoundedBuoyancySurface.VerticalAccelerationAt` (and `VerticalAccelerationOver` to average a footprint in one go), and `BoundedBuoyancyWater.VerticalAccelerationAt` gives it for any point. In the core: `WaterSurface.VerticalAccelerationAt`, `WaveSet.VerticalAcceleration`, `ISurfaceMotion`, `WaveSet.MeanHeight`, `BodyCore3D.WaveAcceleration`, `BuoyancyShape3D.Size`, and the same in 2D.
- New setting **Wave Damping** on Bounded Buoyancy Body and Body 2D (1, the physics; 0 leaves it out). Scenes saved with an earlier version get 1. In the core: `BodyCore3D.WaveDamping`, `HeaveDamping`, `WaveDampingRatio`, `WaveDampingCoefficient` and `AtSurface`, and the same in `BodyCore2D`; `WaterSample.Owner` names the water a sample came from; `Immersion3D.Immerse` can return the height of the surface over the body, so the damping asks other water systems for nothing more.
- A body is damped only while it is at the surface, so one leaping out of the water or plunging in is damped only for the part of the step it is there; one teleported, moving into water at another height, or in water moved up or down at once, gets no kick from the jump in the surface; water rising or falling steadily carries a body as before; and one re-enabled starts afresh.
- Tests: a B15 play-mode test (floats settling, a raft still riding waves) and two edit-mode tests; the .NET checks add 47. The paper's benchmarks run with Wave Damping 0, so their numbers are unchanged.

## [2.2.0] - 2026-10-13

- Boats: **Bounded Buoyancy Foil** (keels, centreboards, rudders, fins, sails, wings) and **Bounded Buoyancy Thruster** (propellers, fans) components on child objects of a body. A foil is a flat plate whose force square to its faces is ½ ρ A q² C_N(α), with a lift slope for its aspect ratio (Helmbold) and a stall limit; a rudder turns with **Angle** (positive swings the trailing edge to +X, as in Godot), and a sail with **Auto Trim** swings itself away from the wind it feels. A thruster pushes along its +Z, less the faster it goes, giving a boat a top speed of its own. Both are bounded like the buoyancy: worked out with the motion at the end of the step against the inertia the body has at their point, so a foil stops the flow across it but never throws the body back, and a propeller never overshoots its speed.
- The engine-free core has them as `Foil3D`, `Thruster3D` and `Appendages.Apply`; the body applies them after its buoyancy each step, as a velocity change and an angular impulse.
- Demo sample: a BOATS scene, a motorboat to drive (W/S and A/D, or the sliders) and a dinghy that beats up into the wind in tacks on its own.
- Tests: three B14 play-mode tests (the bounded foil force; a keel, the rudder both ways and a propeller; a dinghy sailing upwind) and two edit-mode tests; the .NET checks add 52, including the Unity layout of a boat turning the same way as the Godot layout.
- No change to the buoyancy itself or to the paper's benchmarks.

## [2.1.1] - 2026-10-13

- Bounded Buoyancy HDRP Water: each body's heights are now found in one Burst job (HDRP's `WaterSimulationSearchJob`) instead of one managed search per vertex, so a detailed hull or a fleet costs little more than a sphere; and with **Use HDRP Current** (on by default) the Water Surface's own current carries the bodies (its speed in km/h along its orientation, the ripples current for a pool, turned with the surface's object), in place of the Bounded Buoyancy Water's Current.
- BoundedBuoyancySurface has an optional `FlowAt`, and the core an `ISurfaceFlow`, so any linked water system can supply its current.
- Checks: the search job compiled against the HDRP 14, 15, 16 and 17 API; a core check that a source's current carries a ball over the volume's own.

## [2.1.0] - 2026-10-13

- Other water systems: Bounded Buoyancy Water has a new **Surface Source**. Bodies then float on that system's surface, each vertex of their shape measured against it, with this package's added mass, righting and drag; the water still sets the size, density, current and splashes, and stops drawing its own surface.
- **Bounded Buoyancy HDRP Water**: the link to the Water System of the High Definition Render Pipeline. Add it to an unscaled object at the water's height, pick the Water Surface and turn on its Script Interactions; HDRP draws the water and Bounded Buoyancy does the physics. It is compiled only when HDRP 14 (Unity 2022.3) or newer is installed, and uses `FindWaterSurfaceHeight` in HDRP 14 and `ProjectPointOnWaterSurface` (with deformers, optionally) from HDRP 15.
- **BoundedBuoyancySurface**: a base class to link any other water system (override `HeightAt`, and `HeightsAt` for many points at once). In the engine-free core, `ISurfaceHeights`.
- Bodies near wavy water now check the surface over every part of them when the waves are short and steep compared with the body; before, a crest could reach a body that was counted as clear of the water. The benchmark results are unchanged.
- Tests: a new play-mode test (B13) and two edit-mode tests of another water system's surface; the .NET checks add five, and compile the HDRP link against the HDRP 14 and HDRP 17 API.

## [2.0.1] - 2026-10-12

No changes to the Unity package's code or settings. The release archived on Zenodo now includes the Unity package and its checks.

## [2.0.0] - 2026-10-11

First Unity release, with the features of Bounded Buoyancy 2.0.0 for Godot.

- Bounded Buoyancy Body for Rigidbody: the added mass of the submerged part in the inertia every physics step, so buoyancy drives no fall faster than g and no rise faster than g/C; momentum kept on entry; a Usual model for comparison.
- Any collider shape: box (scaled, even skewed), sphere, capsule and convex mesh, on the object or its children, or a closed buoyancy mesh. Buoyancy acts at the centre of the part under water, so bodies tilt, roll and right themselves. Centre of mass at the centre of the volume (with an offset for ballast), and inertia with its principal axes, worked out from the shape.
- Bounded Buoyancy Water: water volumes at any height, with waves, a current (slower along the banks), splashes, rings and a drawn surface that works in every render pipeline.
- Air: balloons and hot-air balloons, air that thins with height, and density changed while running.
- Bounded Buoyancy Wind: wind with gusts that travel downwind, limited to a box if wanted; sails that heel a boat.
- Water Entered and Water Exited events with the speed and the point on the surface.
- 2D: Bounded Buoyancy Body 2D, Water 2D and Wind 2D for Rigidbody2D, with circle, box, capsule and polygon colliders.
- Unity 2022.3 LTS and Unity 6 (linearVelocity, linearDamping and angularDamping in Unity 6).
- Play-mode tests of the paper's benchmarks in Unity's physics, edit-mode tests of the engine-free core, and a demo sample with eight 3D scenes and a 2D scene, opened from Tools > Bounded Buoyancy.
