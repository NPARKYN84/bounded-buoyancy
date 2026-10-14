# Bounded Buoyancy for Unity

**Stable, accurate buoyancy for Unity 2022.3 LTS and Unity 6, in 3D and 2D.** Light objects stop launching out of the water, floats settle after a few bobs as real ones do, crates, rafts and boats of any collider shape tilt and right themselves, ride the waves (their own, or HDRP's Water System's) and drift down rivers, boats with keels, rudders, propellers and sails steer and sail up into the wind, balloons rise and float, wind fills sails, things splash as they hit the water, and hundreds of bodies can cross the surface at once. It is the Unity version of the Bounded Buoyancy plugin for Godot, with the same physics and the same benchmarks.

Most engines apply buoyancy as a force on the body's own mass. For anything lighter than water that has no upper limit: released under water, an air bubble accelerates at 815g in its first frame and moves 2.2 m in one 60 Hz step. Real light spheres and bubbles start at no more than about 2g, because the water around them has to be set moving too. That water is the *added mass* of classical hydrodynamics.

Bounded Buoyancy puts the added mass of the submerged part into the body's inertia every physics step:

```
M = ρ_body · V  +  C · ρ_water · V_submerged
```

Buoyancy then drives no fall faster than g and no rise faster than g/C (2g for a sphere), for every density, and the first frame after a release is the classical value. Drag and collisions act on top, as usual.

| Released from rest under water (60 Hz) | Usual engine method | Bounded Buoyancy |
| --- | ---: | ---: |
| Air bubble | 815g | 1.99g |
| Foam ball (3% of water's density) | 32.3g | 1.83g |
| Table-tennis ball | 11.4g | 1.58g |
| Cork | 3.0g | 1.00g |

## Try it in a minute

1. **Install.** In Unity, open *Window → Package Manager*, click **+**, and choose *Add package from git URL…* with
   `https://github.com/NPARKYN84/bounded-buoyancy.git?path=/unity/com.parkyn.bounded-buoyancy`
   or *Add package from disk…* and pick this folder's `package.json`.
2. **See it.** In the package's page in the Package Manager, open *Samples* and import **Demo**. Then choose *Tools → Bounded Buoyancy → Play 3D Demo* (or *Play 2D Demo*). It opens a new scene with the demo in it and presses Play. The buttons on the left switch between eight scenes: release, float, tilt, waves, sky, many, river and wind.
3. **Check it.** Add `"testables": ["com.parkyn.bounded-buoyancy"]` to your project's `Packages/manifest.json`, open *Window → General → Test Runner*, and press *Run All* on the **PlayMode** tab (about a minute: long runs go at 20 times real speed) and the **EditMode** tab (a second). The play-mode tests run the paper's benchmarks in Unity's own physics engine and should all pass.

The drawn water, splashes and demo use Unity's built-in Sprites/Default shader, which renders in the Built-in and Universal render pipelines. In HDRP it renders too dark (it has no exposure handling), so give the water an HDRP material of your own (**Surface Material**, **Effects Material**), or let HDRP's Water System draw the water (below); the physics is the same in every pipeline.

## Use (3D)

1. Give a GameObject a **Rigidbody** and colliders: box, sphere, capsule or convex mesh, on it or on its children (several make one shape).
2. Add **Bounded Buoyancy Body** and set its **Density** (kg/m³; water is 1000).
3. Add a GameObject with **Bounded Buoyancy Water** where the middle of the water's surface should be, and set its **Size**: the water fills that box below it. (Without any water in the scene, bodies use their **Water Level**, a flat surface at that height.)

Press Play. The component sets the Rigidbody's mass, turns off its gravity and damping, and applies weight, buoyancy, drag and the righting moment itself, every physics step. Collisions, your own forces and joints act as usual and are resisted by the full inertia, water included. Disable the component and the Rigidbody is handed back as it was: it falls and damps as before.

Bodies step themselves in FixedUpdate, where Unity runs physics by default. If you set *Physics → Simulation Mode* to Update, they are stepped each frame for you. If you set it to Script, call `BoundedBuoyancySimulation.Step(dt)` (and `Step2D(dt)` for 2D) just before your own `Physics.Simulate(dt)`.

To give a boat ballast, set **Centre Of Mass Offset** below zero: the lower it is, the harder the boat rights itself. For a detailed hull, set **Buoyancy Mesh** to a simple closed mesh with Read/Write enabled. If you add or change colliders while the game runs, call `RebuildShape()`.

### Bounded Buoyancy Body

| Setting | Meaning | Default |
| --- | --- | --- |
| Model | Bounded, or Usual (buoyancy on the body's own mass, as most engines do) for comparisons | Bounded |
| Density | kg/m³; set `Density` from a script while running (ballast tanks, a hot-air balloon's burner) | 500 (wood) |
| Added Mass Coefficient | C for the shape: 0.5 sphere, about 1 long cylinder side-on | 0.5 |
| Water Level / Water Surface | Flat surface used when the scene has no water volume; Water Surface (a water volume, or any object whose Y is the surface) takes priority | 0 / none |
| Water Density | kg/m³ for Water Level (a water volume uses its own) | 1000 |
| Drag Coefficient | Quadratic drag, in the water and in the air | 0.47 |
| Angular Drag | Damping of rolling and pitching in the water, per second when fully under; in air, in proportion to how light the body is against the air | 3 |
| Keep Momentum On Entry | The water set moving takes its share of momentum as the body enters | on |
| Wave Damping | The energy bobbing sends out as ripples, from linear wave theory (see below): 1 is the physics, 0 leaves it out | 1 |
| Air Density / Air Half Height | Air at height 0 (0 leaves air out), and the height over which it halves (0: the same at every height) | 1.2 / 0 |
| Sail Area / Sail Centre | Area for the wind to push on, m², and where it pushes, in the body's coordinates | 0 / (0, 0.5, 0) |
| Buoyancy Mesh | Optional closed mesh used instead of the colliders, in the object's local space | none |
| Curve Segments | Facets round capsules, and spheres that share a body (volumes are corrected to the exact value) | 16 |
| Set Centre Of Mass / Offset | Put the centre of mass at the centre of the volume, plus the offset; off keeps the Rigidbody's own | on / 0 |
| Set Inertia | Work out the rotational inertia (with its principal axes) from the shape; off keeps the Rigidbody's own | on |
| Water Entered / Water Exited | Events with the speed through the water (m/s) and the point on the surface, for sounds | |

Read from scripts: `OwnMass`, `Volume`, `SubmergedVolume`, `AddedMass`, `CentreOfBuoyancy`, `LastAcceleration`, `AirDensityHere`, `WindHere`, `Water`, and `Core` for everything else (`Core.HeaveDamping` is the wave-making damping this step).

### Settling: the ripples a body makes

A body bobbing at the surface pushes the water out in rings, and the rings carry its energy away; that is why a real float settles after a few bobs. **Wave Damping** takes that energy away too. It acts on the body's bobbing relative to the surface under it, so a body riding waves (the Bounded Buoyancy Water's, or HDRP's) still rises and falls with them, and a body let go from rest starts exactly as before. A wide, shallow body settles in a bob or two (each bob about 70% lower than the last for a raft or a boat, 50% for a log, 27% for a cube or a ball floating half under); a deep, narrow one bobs on (3% for a spar buoy). The amounts come from linear wave theory, solved exactly with the open-source solver Capytaine for 30 floating boxes, posts, balls and logs and fitted with one curve (`reference/wave_damping.py` in the repository). In the simulation that gives 0.8 to 1.5 times the exact damping for flat-bottomed bodies, about 0.5 to 0.7 times for balls and logs floating at least half out of the water, more for heavy balls floating mostly under, and within about 2% in 2D. Set it to 0 to leave it out, as the paper's benchmarks do.

### Bounded Buoyancy Water

**Size** and **Density** (1000 fresh, about 1025 sea); **Wave Height**, **Wave Length** and **Wave Direction** (two travelling sine waves with deep-water speeds); **Current** (in the object's own frame, so turning it turns the river) and **Current Edge** (slower water along the banks); **Splashes**, **Ripples**, **Splash Min Speed** and **Splash Color**; **Show Surface**, **Surface Color**, and optional **Surface Material** and **Effects Material**. Turn the object about Y to turn the waves and the current; keep its scale at 1 and set the size instead. Once a scene has any water volume, bodies float only inside one, so a pond, a pool and the sea can sit at different heights. The water moving up and down in the waves pushes on everything in it, so a small raft rises and falls as far as the water does (99% on waves 20 times its length, where exact wave theory gives 100%), and a ball as dense as water drifting under the surface moves up and down with the water round it.

From scripts: `HeightAt(point)`, `CurrentAt(point)`, `Contains(point)`, `MaxWave`, `WaveTime`, and `Splash(point, speed, width)` to throw up a splash of your own.

### HDRP Water System, and other water systems

Keep the ocean, river or pool that HDRP's Water System draws, and let Bounded Buoyancy do the floating: the part of each body under HDRP's waves, the added mass, righting and drag.

1. On the HDRP **Water Surface**, turn on **Script Interactions**, so HDRP can answer height requests from scripts. In Unity 2022.3, turn on **Script Interactions** in the Water section of your HDRP asset as well.
2. Add **Bounded Buoyancy HDRP Water** to an empty, unscaled object at the water's height (HDRP sizes some water surfaces by their object's scale, so not to a scaled Water Surface's object). It adds a **Bounded Buoyancy Water**, links itself as that water's **Surface Source**, and picks up the Water Surface (on the same object, a parent, or the first in the scene). It sizes the water to match a quad surface, or 1 km across for an infinite ocean; set **Size** to cover where bodies should float.
3. Set **Max Wave** to at least the height of the highest crest above the water's average level (3 m to start). For bodies further than twice that from the surface, HDRP is asked only for the height over their centre.

Bodies with a Bounded Buoyancy Body then float on HDRP's waves, each vertex of their shape measured against the surface above it. Each body asks for all its vertices at once, and the heights are found in one Burst job on the worker threads, the way Unity's own water samples do, so a hull of hundreds of vertices costs little more than a sphere. With **Use HDRP Current** on (the default), the Water Surface's own current carries the bodies: its Current speed (in km/h) along its orientation, the large current of an ocean or river and the ripples current of a pool, turned with the surface's object; turn it off to use the Bounded Buoyancy Water's **Current** instead. The Bounded Buoyancy Water still sets the size and the density, and stops drawing its own surface. HDRP gives heights only, not how its water moves, so on HDRP water bodies are buoyed by the surface alone: a small raft rises and falls 5 to 15% further than the water. **Search Error** and **Search Steps** set how exactly HDRP finds each height (0.01 m and 8 steps, as in Unity's own samples), and in Unity 2023.1 and later **Include Deformation** counts the water's deformation (its deformers, or its water decals in Unity 6). The link is compiled only when HDRP 14 (Unity 2022.3) or newer is in the project; without HDRP the package needs nothing from it.

Unity's HDRP water samples include a Buoyancy script of their own. It treats every object as a sphere of a radius you set, measured at one point, applies buoyancy on the object's own mass, and holds it down with a "surface tension" damping and a cap on its speed. Here the collider's own shape is cut at HDRP's surface, so a raft, a boat and a log float, tilt and right themselves as their shapes do, and the added mass keeps light objects from launching without any damping or speed cap.

Any other water system can be linked the same way: derive from **BoundedBuoyancySurface**, override `HeightAt(Vector3 worldPoint)` to return the height of its surface there (and `HeightsAt` if it can answer many points at once), add it next to a Bounded Buoyancy Water and set the water's **Surface Source** to it. If the system knows how its water moves, override `VerticalAccelerationAt` too, to return the water's upward acceleration at a point in the water, and its push carries the bodies as the water volume's own waves do (for waves made of sines, each wave's part is −height × ω² × sin(phase) × e^(k z), z the point's height above the average level, no more than 0).

### Bounded Buoyancy Wind

**Velocity** (in the object's own frame: turn it to aim the wind), **Gust Strength**, **Gust Period** and **Direction Swing**; gusts travel downwind, so nearby bodies feel each one in turn. Turn off **Everywhere** to fill only a box of **Size** round the object, such as a gap between buildings or a fan. The wind pushes on the part of each body out of the water, so balloons drift with it, a body with a sail runs before it and heels, and a crate barely moves.

### Boats: Bounded Buoyancy Foil and Bounded Buoyancy Thruster

Add **Bounded Buoyancy Foil** components on child objects of a body for its keel, centreboard, rudder, fins or sails, and **Bounded Buoyancy Thruster** for a propeller or a fan. A foil is a thin flat plate (span up its Y axis, leading edge forward along +Z, faces along X); the flow past it makes a force square to its faces, lift and pressure drag together, at its own point, so a keel stops a boat sliding sideways, a rudder turns it, and a sail with a keel drives it across the wind and up into it. A thruster pushes along its +Z, less the faster it goes, so it gives a boat a top speed of its own and pushes astern when reversed. Like the buoyancy, their forces are bounded: each is worked out with the flow at the end of the step against the inertia the body has there, so a foil can stop the flow across it but never throw the body back, and a propeller brings the boat up to its speed and no further, however large or light.

| Foil setting | Meaning | Default |
| --- | --- | --- |
| Medium | Water (keel, rudder, fin: acts on its part under the water) or Air (sail, wing: acts above it, with the wind) | Water |
| Area / Aspect Ratio | Area of one face, m², and span² / area (1 to 2 keel, 2 to 3 rudder, 3 to 5 sail) | 0.1 / 2 |
| Angle | Degrees about its span: a rudder's angle (`Angle` from scripts). Positive swings the trailing edge to +X, turning a boat with the rudder at its stern to starboard | 0 |
| Effort Offset | From the object (a mast, a rudder stock) to where the force acts, along the chord, m | 0 |
| Auto Trim / Trim Angle Of Attack / Max Trim | A sail swings itself away from the wind it feels, keeping that angle to it, no further than Max Trim; running dead downwind it stays on its side until the wind is 10° round on the other (by the lee), so it does not flick across | off / 15° / 85° |

The force square to a foil's faces is ½ ρ A q² C_N(α), with C_N(α) = k sin α cos α + 1.2 sin² α, no more than 1.4 (a stalled plate), k the lift slope of a wing of its aspect ratio (Helmbold). A thruster has **Max Thrust** (N), **Max Speed** (m/s, where full throttle pushes no more), **Throttle** (-1 to 1), **Diameter** and **Medium**. From scripts: `Force` and `Immersion` (and a foil's `AngleOfAttack`), and the body's `Foils` and `Thrusters`. A foil must be on its own object under the body (it turns its object), and remembers where it was placed; if you turn that object by script while the game runs, call `Replace()`. Foils and thrusters can be added, moved to another boat or enabled in any order.

For hulls: a streamlined hull has much less drag than the default Drag Coefficient (a sphere's 0.47); 0.1 to 0.2 suits a boat. Turn **Keep Momentum On Entry** off for boats on waves: it is meant for bodies plunging into the water, and a hull dipping into each wave would otherwise lose speed every time. The demo's BOATS scene has a motorboat to drive and a dinghy that beats up into the wind in tacks on its own.

### Flooding: Bounded Buoyancy Compartment

Put **Bounded Buoyancy Compartment** on child objects of a body for the spaces inside it that can flood: holds, bow and stern sections, tanks. Each is a box (**Size**, centred on its object) with **Holes**, each (x, y, z, area) in the object's own coordinates, set in the inspector or made where something hits: `compartment.AddHole(worldPoint, area)`. Water pours in through holes below the waterline by the orifice law, Q = C A √(2 g Δh), and drains out through holes above it; inside it lies level whichever way the ship leans, and its weight acts at its own centre. So a holed ship settles lower, lists toward its flooded side, and sinks or capsizes once it has taken in more water than its reserve of buoyancy will carry. Loose water costs it stability (the free-surface effect), and bulkheads limit both, as in real ships. **Pump Rate** (m³/s) takes water out again, and `PatchHoles()` closes them. **Discharge Coefficient** is 0.6 for a sharp-edged hole, and **Flood Volume** is the water inside now (set it to start part full).

The body carries the water as weight at its centre (the added-weight method), with its turning moment about the centre of mass, and adds it to the inertia tensor as weight at its centre; once pumped dry the body is back to its own. Compartments report `FillFraction`, `Inflow`, `WaterLevel` and `WaterCentre`, and the body its `FloodMass`.

### Cannons: Bounded Buoyancy Cannon and Bounded Buoyancy Cannonball

Put **Bounded Buoyancy Cannon** on an object at a gun's muzzle, pointing the way it fires (its forward, +Z), and call `Fire()`. Choose the **Gun**, a 6- to 32-pounder, and the shot's size and weight are set from the British round shot of Muller's *Treatise of Artillery* (1768); the muzzle speed is a long gun's with its full charge, 488 m/s (1600 ft/s). On a ship (any Rigidbody above it), the shot leaves with the ship's velocity at the muzzle added, and the ship takes the recoil there: the shot's momentum and the powder gas's (**Charge Mass**, leaving at half the shot's speed on average). So a broadside heels the ship as well as pushing it back. **Reload Time**, **Spread** (degrees of scatter), **Shot Lifetime**, an optional **Shot Prefab** and **Shot Parent**, and the `fired` event.

The shot is a **Bounded Buoyancy Cannonball**, a Bounded Buoyancy Body for any fast ball (give it one Sphere Collider). Its air drag changes with its speed as a cannonball's does: about 0.2 below two thirds of the speed of sound, rising through the sound barrier to about 1 (Miller and Bailey, 1979), so a 24-pounder first grazes the water about a mile off at 5°. Meeting the water at a shallow angle it skips, as gunners' ricochet fire did: the water's push on the part of it under the surface lifts it out again if it came down at less than about 18° / √σ, σ its density over the water's (Birkhoff and others, 1944), 6.5° for iron. The contact lasts a fraction of a physics step, so that step is worked through in short steps and the Rigidbody carried along their chord; on waves, a ball meeting the face of a wave plunges in sooner. Continuous collision detection is on, so it strikes a hull instead of passing through it, and its `hit` event gives what it struck, where, and its velocity just before. It reports `Skips`, `ExitVelocity` and `Mach`; **Ricochet**, **Speed Of Sound** and **Lifetime** are its settings.

### Damage: Bounded Buoyancy Hull

Put **Bounded Buoyancy Hull** on a ship (the object with its Rigidbody) for its planking: **Thickness** (m), and the wood's **Density** and **Strength** (oak, 750 kg/m³ and 15 MPa, by default). Round shot that strikes the ship then holes it. The wood resists shot with a pressure σ + ρ v² on its cross-section, v its speed through the wood (Poncelet's law of penetration), so with oak a 32-pounder goes 42 inches into solid oak from 400 yards and 31.5 inches from 1000 yards, as the gunners' handbook gives (Elvin, 1805). Shot with the speed to go through does, slower, punching a hole as big as itself (**Hole Size** times) in the ship's nearest Bounded Buoyancy Compartment; it flies on across the ship and out of the far side if it still has the speed, holing that side too, and otherwise lodges in it (the cannonball's `Penetration` says how far). The wood punched out flies off as splinters that float (**Splinters**, **Splinter Lifetime**, **Splinter Color**), and the ship takes the momentum the shot loses, less what the splinters carry off. Shot that meets the sea on its way to a ship skips off it or plunges in first, so shot fired low holes a ship at and above its waterline: the water comes in at the lowest holes, the ship settles, more of them go under, and in the end she lists and sinks. Shot meeting the planking more than about 78° off square glances off. The ship's colliders are taken as its outside, and as solid: shot goes in through the planking where it strikes them and out where its path leaves them; colliders that touch or overlap (a ship of several boxes) count as one, and a part beyond a gap along the shot's path (a mast clear of the hull) is not struck by the same shot. Shot passes anything lighter than itself in its way, such as splinters. The `struck` event gives the shot, where, whether it went through and its speed out; `holed` gives where, the hole's area and its compartment.

## 2D

**Bounded Buoyancy Body 2D**, **Bounded Buoyancy Water 2D** and **Bounded Buoyancy Wind 2D** do the same in a side view for **Rigidbody2D**: circle, box (rounded corners too), capsule and polygon colliders; tipping and righting; waves, currents, air, wind, sails and splashes. Lengths are in metres with y up, as in Unity 2D. **Thickness** (depth into the screen, 0.2 m) turns areas into kilograms, so collisions with other bodies are fair; it does not change how anything floats. A circle's added-mass coefficient in 2D is 1, so a light ball rises no faster than g. The 2D water is drawn in front of sprites (**Sorting Order** 10), so bodies look under water.

## How it is checked

- **The physics, in plain C#.** All the physics lives in an engine-free core (`Runtime/Core`), with no Unity types. The repository's `unity/checks/CoreTests` runs it in .NET with a small integrator standing in for the engine, against every benchmark the Godot plugin passes: 238 checks, all passing. First-frame accelerations agree with the formula to the printed digits (an air bubble's 7995.62 m/s² usual against 19.54 m/s² bounded); the release-and-surfacing times are 0.517, 0.533, 0.617 and 0.783 s, as in the paper's Table 4; a ball in a 1.5 m/s river drifts at 1.486 m/s after a minute; a helium balloon starts at 16.34 m/s² usual against 7.00 m/s² bounded.
- **In Unity.** The package's 23 play-mode tests run the same benchmarks in Unity's physics engines (B1 to B19: first frames, surfacing, every collider type, righting, water volumes and waves, air and balloons, density changed while running, fast bodies, rivers, wind and sails, splashes and events, 2D, another water system's surface, foils and propellers: the bounded foil force, a keel against leeway, turning circles both ways, a propeller's top speed and a dinghy sailing upwind, floats settling while a raft still rides waves, a ship holed on one side listing to it and coming upright when pumped dry, a cannon's recoil, its shot stopped by a plank at 488 m/s, and an iron ball skipping at 4° and plunging at 9°, and shot into solid oak, through a ship (of one box and of two) and out of the far side, lodging when spent, and meeting the sea short of a ship's side), plus a check that Unity measures a Rigidbody2D's inertia about its centre of mass, as the 2D body assumes. The 20 edit-mode tests check the core in a second.
- **Flooding.** In the .NET checks, with the same numbers as the Godot plugin: a box with a hole in its floor sinks at the steady rate of the sinking-bowl water clock and goes under at the time it gives (68.07 s against 68.02 s); a barge with its middle third open to the sea settles at the draft of the lost-buoyancy method (60.0 cm); loose water moves across a leaning tank by exactly i tan θ / V; a slack tank across a barge's whole floor makes it loll, and bulkheads down the middle and across keep it upright.
- **Cannons.** In the .NET checks, with the same numbers as the Godot plugin: a 24-pounder at 5° first grazes the water where the same drag law worked through in steps of 0.1 ms puts it (to 0.4%), 1.72 km off, about the mile of the range tables; balls of relative density 7.42, 2.7 and 1.2 skip below 6.66°, 10.90° and 15.85°, against Birkhoff's 6.65°, 11.03° and 16.54°, the same at 200 and 400 m/s; and a 7 mm steel ball meeting the water at 57 m/s and 1.04° to 4.53° leaves it at the speed and angle Lyu, Yun and Wei (2021) measured (speed out 0.98 to 0.79 of the speed in, against 0.97 to 0.68; angle out within 0.25° of theirs).
- **Damage.** In the .NET checks: a 32-pounder fired from 400 and from 1000 yards goes 41.9 and 31.3 inches into solid oak, against Elvin's 42 and 31.5, as in Godot; the speed shot keeps through planking matches m v dv = −A (σ + ρ v²) dx worked through in steps of 10 µm; a 24-pounder that lands 3 m short of a ship's side at 4° skips and strikes 9.6 cm above the water, as in Godot, and one that lands 1.5 m short at 20° plunges in and strikes 43 cm under it, slowed from 400 to 239 m/s, close to the 242 m/s the drag of a ball wholly under the water gives over that way (Godot: 43 to 44 cm and 237 to 239 m/s).
- **Boats.** In the .NET checks a keeled motorboat slipping sideways at 0.5 m/s keeps 0.04 m/s of the slip after a second, against 0.36 m/s without the keel; with its rudder hard over it turns in a steady circle about three boat lengths across, the same either way; a propeller with nothing else in the way settles at exactly throttle x Max Speed; and a dinghy steered 45° off a 6 m/s wind gains 0.76 m/s towards it, heeling less than 8°. The same boat laid out as the Unity components lay it out turns the same way at the same rate as the Godot layout, mirrored.
- **Wave-making damping.** In the .NET checks the damping is checked against the exact values for boxes, posts, balls, logs and side-view sections; a crate's bobs die away at the rate it gives (to about 1%); a wooden ball let go 3 cm below its floating height still bobs 3 mm either way after 3 s and under a fifth of a millimetre after 9 s, against 12 mm and 6 mm without wave damping; a raft on long waves rises and falls as far as the surface, and one in water rising at 0.2 m/s rides at the same depth as without wave damping; a body teleported, gliding into water at another height, or in water lifted 15 cm at once, gets no kick from the jump in the surface; and a table-tennis ball leaping out of the water pops up 4.1, 3.9 and 3.6 cm above its floating height at 60, 120 and 480 steps a second.
- **The water's push in waves.** The water's upward acceleration matches the second time derivative of the surface's height (3D and 2D) and dies away with depth; a ball as dense as water 1 m under 12 m waves rises and falls as far as the water there, in step with it; and waves too steep to be real push no harder than half of g.
- **Other water systems.** A stand-in for another system that gives a copy of a water volume's waves must move bodies exactly as the waves themselves do; in the .NET checks the twins agree to within a millionth of a millimetre and a millionth of a degree. A body well clear of the surface asks for one height a step.
- **The Unity API.** Every Unity call was checked against Unity's published C# reference source for 2022.3 and Unity 6, and every HDRP call against HDRP's published source for HDRP 14 (Unity 2022.3) and 17 (Unity 6), the height-search job against HDRP 15 and 16 as well. The runtime, the HDRP link, tests and sample compile against both, each as its own assembly as Unity builds them, with warnings as errors (`unity/checks/Split`). The HDRP link has not yet been run inside an HDRP project. In Unity 6 it uses `linearVelocity`, `linearDamping` and `angularDamping`; in 2022.3, `velocity`, `drag` and `angularDrag`.

## Speed

The buoyancy work for 300 bodies riding waves, measured in plain .NET 8 on a 2.1 GHz server processor, takes about 0.3 ms per physics step for spheres, 0.4 to 0.9 ms for boxes and convex hulls, and 2 to 3.5 ms for capsules (which have the most facets; lower **Curve Segments** to make them cheaper). Unity's Mono and IL2CPP runtimes are usually somewhat slower than .NET 8, and Unity's own physics step comes on top. Nothing is allocated per step.

## Limits

- Submerged volumes are exact for every shape (capsules, and spheres in compound bodies, to within the facets, corrected to the exact total). Overlapping colliders in one body are counted twice, and a buoyancy mesh must be closed.
- A mesh collider counts as the convex hull of its points, as Unity's physics treats a dynamic body's mesh. Its mesh (or the buoyancy mesh) must have Read/Write enabled for builds; otherwise its bounding box is used, with a warning.
- One added-mass value per body, which is exact for spheres and for the vertical motion of other shapes. Rotation uses the body's own inertia; the water's share in rolling and pitching is stood in for by Angular Drag.
- Waves move the surface only. The energy a body's bobbing sends out as ripples is taken away (Wave Damping), but the ripples themselves do not move other bodies, a moving boat makes no wake to slow it, and splashes are drawn, not simulated. Wave damping acts on bobbing up and down; rolling and pitching are slowed by Angular Drag. It is worked out for deep water, from the volume under water and the depth of its centre. The volume under a wave is accurate while the waves are long compared with the body.
- Only the water's up-and-down motion in waves pushes bodies; its to-and-fro motion does not, so things floating in waves do not sway back and forth with them, or drift slowly the way the waves run. A body that is a good part of a wavelength long rises and falls a little less than linear wave theory gives (a raft on waves five times its length, 93% against 98%). On HDRP water, and other linked systems that do not give the water's acceleration, bodies are buoyed by the surface alone and a small raft rises and falls 5 to 15% further than the water.
- Wave damping acts in the steps that find a body partly under water, so how high a light ball shooting out of the water pops up depends a little on the time step. It reads how fast the surface rises from its height a step apart; a water system that moves its waves only when a frame is drawn, rather than every physics step, would give it an uneven reading (not yet tried with HDRP; lower Wave Damping if bodies on HDRP water look jittery).
- From another water system the height of the surface is taken, and its current if the link supplies one (HDRP's does, as the surface's uniform current: a current map's local bends are not read): near the surface, one request per vertex of the body's shape each step, plus one at its centre (just one for a lone sphere). With HDRP these go in one Burst job per body; for other systems, keep big fleets to simple colliders or a low-poly Buoyancy Mesh. 2D water has no other-system link yet.
- A body's own drag acts at its centre, so a current or wind turns it only through its foils (a keel aft of the centre of mass weathervanes a boat; a boat already drifting with the water has nothing flowing past it and does not turn). Sail Area always faces the wind it feels; for sailing across or into the wind, use a sail foil and a keel.
- Foils are flat plates: no camber, and no lift from the hull itself (a hull with no keel slides sideways freely). The lift slope allows for the foil's aspect ratio, but not for one foil's wake meeting another. Propellers do not turn the boat by their torque. Foils and thrusters are 3D only.
- Compartments are boxes, taken to be vented (air gets out as water comes in), and their water moves at once to lie level: it does not slosh. They should lie inside the body's shape. The 2D bodies have no compartments yet, and the demo sample has no flooding scene yet (the Godot plugin's demo has).
- Only a ball (one Sphere Collider) skips; the water's push is Birkhoff's, on the part under the surface as it stood before the ball met it, and the splash and the hollow it opens are drawn, not simulated. On calm water a ball fired low skips on for kilometres; on waves it plunges after a few skips. Planking is one thickness and one wood for the whole body, and a hole is as big as the shot (or Hole Size times), not torn larger. No cannons or damage in 2D, and no cannon scene in the demo sample yet (the Godot plugin's demo has one).
- Gravity is read from Physics.gravity (Physics2D.gravity in 2D), straight down. Water volumes should be turned only about the vertical, and water and wind kept at scale 1 (set their sizes instead).
- Floating bodies apply a small force every step, so they do not go to sleep.

## Differences from the Godot version

The physics and the settings are the same. In Unity the centre of mass is moved with **Centre Of Mass Offset** (from the centre of the volume) rather than set directly; the inertia is worked out with its principal axes, not just its diagonal; 2D is in metres with y up; water and wind ignore their objects' scale; there is no cylinder (Unity has no cylinder collider; use a convex mesh or a buoyancy mesh); and C# needs no native kernel.

## Cite

Parkyn, N. (2026). *Bounded Buoyancy: Added Mass and Two Ceilings for Stable, Accurate Buoyancy in Game and Simulation Engines*, v1.1. Companion paper to *ValerieX (VXXX): The Density-State Drive*, v2.2. Zenodo. https://doi.org/10.5281/zenodo.23304509

To cite the plugin itself: Parkyn, N. (2026). *Bounded Buoyancy: stable, accurate buoyancy for Godot and Unity* [Software]. Zenodo. https://doi.org/10.5281/zenodo.23305636 (this DOI always leads to the latest version).

## Licence

MIT. Free for any use, including commercial, as long as the licence notice stays with the code.
