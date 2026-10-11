# Changelog

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
