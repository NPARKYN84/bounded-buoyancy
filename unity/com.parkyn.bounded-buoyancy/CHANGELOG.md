# Changelog

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
