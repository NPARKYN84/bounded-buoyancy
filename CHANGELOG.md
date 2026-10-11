# Changelog

## 2.0.0

- Unity: a Unity version of the plugin, for Unity 2022.3 LTS and Unity 6, in 3D and 2D, with the same physics, settings and benchmarks, is in `unity/` (a Unity package; its own changelog is in `unity/com.parkyn.bounded-buoyancy`).

- Rivers and currents: water nodes have a **Current** (and **Current Edge** for slower water by the banks). Drag acts on each body's speed through the water, so bodies drift with the stream; momentum on entry is shared relative to the moving water.
- Wind: a new **BoundedBuoyancyWind** node, everywhere or in a box, with gusts that travel downwind and swing the direction. It pushes on the part of each body out of the water. Bodies can have a **Sail Area** pushing at a **Sail Centre**, so boats run before the wind and heel.
- Splashes: water nodes throw up droplets when a body hits fast enough and leave rings spreading over the surface (riding the waves). Bodies emit **water_entered** and **water_exited** for sounds.
- 2D: `BoundedBuoyancyBody2D`, `BoundedBuoyancyWater2D` and `BoundedBuoyancyWind2D`, with the same physics for `RigidBody2D` in pixel units.
- Drag now uses the area a body shows to the flow, from its bounding box at the angle of the flow, instead of always its area seen from above.
- Shapes added after a body joins the scene are picked up on its first physics step, and `rebuild_shape()` re-reads them at any time. Convex shapes no longer depend on the shape's debug-fill setting.
- Water and wind nodes now update before the bodies each frame, so the native kernel and GDScript agree whatever the order of the scene tree, and bodies only use water and wind in their own world (viewport).
- The default density is now 500 kg/m³ (wood) rather than a table-tennis ball. Zero or negative densities, wave lengths and gust periods set from code are clamped instead of giving NaN.
- The Linux native kernel now loads on systems from about 2021 on (for example Ubuntu 22.04).
- Demo: three new scenes, a river, a windy lake with sailing boats, and the 2D version (click to drop things in).
- Tests: new checks of rivers, wind and gusts, sails, splashes and signals, and the 2D version; all pass in Godot Physics and Jolt, with and without the native kernel.

## 1.3.0

- Speed: an optional native kernel (`addons/bounded_buoyancy_native`) does the waterline cut in compiled code, with the same results as GDScript. Builds for Windows and Linux are included; the source and a GitHub workflow that builds Windows, Linux and macOS are in `native/`. 300 bodies riding waves take 2 to 4 ms per frame, against 2 to 44 ms in GDScript, depending on the shape (2.5 to 4.5 ms with the extra work of version 2.0).
- Less work per body in GDScript too: water nodes are kept in a list, gravity is read once per frame, and torque and spin damping are only set when they change.
- Demo: a sixth scene with 300 crates, barrels, balls and buoys on waves, showing the buoyancy time per frame (N switches the native kernel on and off).
- Tests: new checks of fast bodies (a stone dropped 50 m, a ball fired into the water at 30 m/s, a box skimming at 25 m/s); `-- --gdscript` runs the whole suite without the native kernel. The air checks now build their balloons at the stated 10 cm size (the results did not depend on it).

## 1.2.0

- Air: the part of a body out of the water is now in air (**Air Density**, 1.2 kg/m³), with its lift, added mass and drag, so helium balloons rise and float. For wood, stone or metal this changes the weight by less than half a percent. Set Air Density to 0 for the 1.1 behaviour.
- New **Air Half Height**: the air thins with height, so balloons that keep their volume rise until they float at a level.
- **Density** can be changed while the game runs, and the body's own mass and rotational inertia follow: hot-air balloons, ballast tanks.
- Demo: a fifth scene with two foil helium balloons and a hot-air balloon (B for the burner, G for a gust).
- Tests: new air checks (balloons, a stone, a body half in water and half in air, a balloon floating where the air thins, a hot-air balloon righting itself, density changed while running), passing in Godot Physics and Jolt. The paper's benchmarks run with air left out, as in the paper, and are unchanged. The raised-pool check now uses a cube dense enough to float flat: a cube at half water's density floats on an edge, as real ones do, which made the old check depend on rounding.

## 1.1.0

- Buoyancy now acts at the centre of the immersed volume, so bodies tilt, roll and right themselves: rafts settle flat, tall blocks tip onto their side, and boats with a low centre of mass come back upright.
- New shapes: cylinder, capsule and convex polygon (including Godot's *Create Convex Collision Sibling*), alone or several in one body. Boxes are exact at any angle.
- New **Buoyancy Mesh** setting for any closed mesh, such as a detailed hull.
- New **Angular Drag** setting (default 3 per second when fully submerged) for the water's damping of rolling and pitching.
- The centre of mass is placed at the centre of the volume unless set by hand, so both physics engines agree; a lower custom centre of mass acts as ballast.
- New **BoundedBuoyancyWater** node: place water as a box in the scene, at any height and density. Once a scene has one, bodies float only inside water nodes.
- Waves: give a water node a wave height and length, and bodies ride and rock on the surface they sit on. The node draws a matching surface, or you can use your own.
- Demo: a third scene with a raft, a tall block and a boat (T tips them over), and a fourth with five bodies on waves (W for calm or rough).
- Tests: new checks of volumes, centres of buoyancy, righting, water nodes, sea water and waves, passing in Godot Physics and Jolt. The paper's benchmarks are unchanged.

## 1.0.0

- First release: the added mass of the submerged part in the inertia, the two ceilings g and g/C, the momentum rule on water entry, a side-by-side demo and the paper's benchmarks as tests.
