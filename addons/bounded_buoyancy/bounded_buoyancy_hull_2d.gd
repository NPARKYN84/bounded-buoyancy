@tool
@icon("res://addons/bounded_buoyancy/icon_hull.svg")
class_name BoundedBuoyancyHull2D
extends Node
## The planking of a ship in a side view (or of anything shot can strike), as BoundedBuoyancyHull in
## 3D: how thick it is and what it is made of. Put it directly under the body (a BoundedBuoyancyBody2D,
## or any CollisionObject2D).
##
## Shot (BoundedBuoyancyCannonball2D) that strikes the body is resisted by the wood with a pressure
## σ + ρ v² on its cross-section, v its speed through it: the wood's strength σ and its density ρ
## (Poncelet's law of penetration), so a 32-pounder goes 42 inches into solid oak from 400 yards, as
## the gunners' handbook gives. Shot with the speed goes in through the planking where it strikes and
## out through the planking on the far side, slower, punching a hole as big as itself each time;
## through a hole below the waterline the sea comes in (the hole goes in the nearest
## BoundedBuoyancyCompartment2D of the body), so a ship holed there floods, settles and sinks. Shot
## without the speed lodges in the wood. The body takes the momentum the shot loses. A shot meeting
## the planking at a glancing angle (more than about 78° off square) glances off.

## Shot has struck the planking at `at` (global): `through` if it went through, leaving at `speed_out`
## (m/s; 0 if it lodged).
signal struck(ball: RigidBody2D, at: Vector2, through: bool, speed_out: float)
## A hole of `area` m² has been made at `at` (global), in `compartment` (null if the body has none).
signal holed(at: Vector2, area: float, compartment: Node)

## Thickness of the planking, in metres. A frigate's side was about 0.4 m of oak at the waterline, a
## ship of the line's 0.6 m; a small boat's a few centimetres.
@export_range(0.001, 5.0, 0.001, "or_greater", "suffix:m") var thickness: float = 0.2
## Density of the wood, kg/m³: oak 750. It resists fast shot (ρ v²).
@export_range(1.0, 20000.0, 1.0, "or_greater", "suffix:kg/m³") var density: float = 750.0
## Strength of the wood against shot, in MPa: oak 15. It stops slow shot.
@export_range(0.0, 1000.0, 0.01, "or_greater", "suffix:MPa") var strength: float = 15.0
## Size of a shot hole, as a multiple of the shot's own cross-section.
@export_range(0.0, 10.0, 0.01, "or_greater") var hole_size: float = 1.0

const _Hull3D := preload("res://addons/bounded_buoyancy/bounded_buoyancy_hull.gd")
const META := &"bounded_buoyancy_hull_2d"


func _enter_tree() -> void:
	if get_parent():
		get_parent().set_meta(META, self)


func _exit_tree() -> void:
	if get_parent() and get_parent().get_meta(META, null) == self:
		get_parent().remove_meta(META)


## The planking of `body`, if it has any.
static func of(body: Object) -> Node:
	return body.get_meta(META, null) if body and body.has_meta(META) else null


## The speed of shot (mass kg, radius m) after going through this planking, met at `cos_in` (the cosine
## of its angle off square) at `speed` (m/s). 0 if it lodges.
func speed_through(mass: float, radius: float, speed: float, cos_in: float) -> float:
	var path := thickness / maxf(cos_in, _Hull3D.GLANCE)
	return _Hull3D.speed_after(mass, PI * radius * radius, speed, path, density, strength * 1e6)


## Make a hole of `area` m² at `at` (global) in the body's nearest compartment, if it has any; emits
## holed. Returns the compartment.
func make_hole(at: Vector2, area: float) -> Node:
	var body := get_parent()
	var best: Node2D = null
	var best_d := INF
	if body and "_compartments" in body:
		for c in body._compartments:
			if not is_instance_valid(c) or not c.is_inside_tree():
				continue
			var l: Vector2 = c.to_local(at)
			var h: Vector2 = 0.5 * c.size
			var d := (l - l.clamp(-h, h)).length()
			if d < best_d:
				best_d = d
				best = c
	if best:
		best.add_hole(at, area)
	holed.emit(at, area, best)
	return best
