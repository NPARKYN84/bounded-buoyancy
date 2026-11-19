@tool
@icon("res://addons/bounded_buoyancy/icon_cannon.svg")
class_name BoundedBuoyancyCannon2D
extends Node2D
## A muzzle-loading cannon that fires round shot, in a side view. As BoundedBuoyancyCannon in 3D.
##
## Place it at the gun's muzzle, pointing the way it fires (its +x); turn it up for elevation. fire()
## sends out a BoundedBuoyancyCannonball2D, which flies with a fast ball's drag, skips off the water
## when it comes down at a shallow angle, and goes through or lodges in planking it strikes (a body with
## a BoundedBuoyancyHull2D).
##
## On a ship (any RigidBody2D above it in the tree), the shot leaves with the ship's own velocity at
## the muzzle added, and the ship takes the recoil at the muzzle: the shot's momentum and the powder
## gas's at half the shot's speed. Choose a gun and the shot is set from Muller's Treatise of
## Artillery (1768), as in 3D.

## A shot has just left the muzzle.
signal fired(ball: RigidBody2D)

const _Gun3D := preload("res://addons/bounded_buoyancy/bounded_buoyancy_cannon.gd")
const _Ball2D := preload("res://addons/bounded_buoyancy/bounded_buoyancy_cannonball_2d.gd")

## Size of gun: sets the shot's diameter and mass, and a full charge.
@export var gun: _Gun3D.Gun = _Gun3D.Gun.POUNDER_24:
	set(value):
		gun = value
		if _Gun3D.SHOT.has(value):
			var s: Array = _Gun3D.SHOT[value]
			shot_mass = s[0] * _Gun3D.POUND
			shot_diameter = s[1] * _Gun3D.INCH
			charge_mass = shot_mass / 3.0
@export_group("Shot")
## Diameter of the shot, in metres.
@export_range(0.005, 1.0, 0.0001, "or_greater", "suffix:m") var shot_diameter: float = 5.55 * _Gun3D.INCH
## Mass of the shot, in kg.
@export_range(0.001, 1000.0, 0.001, "or_greater", "suffix:kg") var shot_mass: float = 24.0 * _Gun3D.POUND
## Speed of the shot leaving the muzzle, m/s.
@export_range(1.0, 2000.0, 0.1, "or_greater", "suffix:m/s") var muzzle_speed: float = 488.0
## Mass of the powder charge, kg: a third of the shot's for a full charge.
@export_range(0.0, 500.0, 0.001, "or_greater", "suffix:kg") var charge_mass: float = 8.0 * _Gun3D.POUND
@export_group("Firing")
## Seconds to reload after firing.
@export_range(0.0, 600.0, 0.01, "or_greater", "suffix:s") var reload_time: float = 3.0
## Scatter of the shot about where the gun points, in degrees.
@export_range(0.0, 10.0, 0.01, "suffix:°") var spread: float = 0.3
## Push the ship back as the gun fires.
@export var recoil: bool = true
@export_group("Shots in the scene")
## Seconds after which a shot removes itself. 0 keeps them.
@export_range(0.0, 3600.0, 0.1, "or_greater", "suffix:s") var shot_lifetime: float = 30.0
## Where shots are added. Empty: beside the ship, or in the current scene.
@export var shot_parent: Node
## Colour the shot is drawn in.
@export var shot_color: Color = Color(0.13, 0.13, 0.14)
## Pixels a metre, for the shot's size on screen: the ship's if it is a BoundedBuoyancyBody2D.
@export_range(1.0, 10000.0, 0.1, "or_greater") var pixels_per_metre: float = 100.0

## Seconds until the gun is loaded again.
var reload_left: float = 0.0
## The last shot fired.
var last_shot: RigidBody2D = null

var _rng := RandomNumberGenerator.new()


func _physics_process(dt: float) -> void:
	if Engine.is_editor_hint():
		return
	if reload_left > 0.0:
		reload_left = maxf(reload_left - dt, 0.0)


func _draw() -> void:
	if Engine.is_editor_hint():
		var ppm := _ppm()
		var r := 0.5 * shot_diameter * ppm
		draw_rect(Rect2(Vector2(-6.0 * r, -1.6 * r), Vector2(6.0 * r, 3.2 * r)), Color(0.2, 0.2, 0.22), false, 1.0)
		draw_line(Vector2.ZERO, Vector2(4.0 * r, 0.0), Color(1.0, 0.4, 0.2), 1.0)


## Whether the gun is loaded and can fire.
func is_loaded() -> bool:
	return reload_left <= 0.0


## Seed the scatter, for shots that fall the same way every run.
func seed_spread(value: int) -> void:
	_rng.seed = value


## Momentum the recoil gives the ship, in kg m/s: the shot's, and the powder gas's at half its speed.
func recoil_momentum() -> float:
	return (shot_mass + 0.5 * charge_mass) * muzzle_speed


## Fire, if loaded. Returns the shot, or null.
func fire() -> RigidBody2D:
	if reload_left > 0.0 or not is_inside_tree() or Engine.is_editor_hint():
		return null
	reload_left = reload_time
	var ppm := _ppm()
	var forward := global_transform.x.normalized()
	if spread > 0.0:
		forward = forward.rotated(deg_to_rad(spread) * _rng.randfn(0.0, 0.5))
	var muzzle := global_position
	var ship := _ship()
	var carried := Vector2.ZERO
	if ship:
		var state := PhysicsServer2D.body_get_direct_state(ship.get_rid())
		var off := muzzle - ship.global_position
		carried = ship.linear_velocity + Vector2(-off.y, off.x) * ship.angular_velocity
		if state:
			carried = state.get_velocity_at_local_position(off)
	var ball := _make_ball(ppm)
	var parent := shot_parent
	if parent == null:
		parent = ship.get_parent() if ship else get_tree().current_scene
	if parent == null:
		parent = get_tree().root
	parent.add_child(ball)
	ball.global_position = muzzle
	ball.linear_velocity = carried + forward * muzzle_speed * ppm
	if ship:
		ball.pass_through(ship, 0.25)
		if recoil:
			ship.apply_impulse(-forward * recoil_momentum() * ppm, muzzle - ship.global_position)
	last_shot = ball
	fired.emit(ball)
	return ball


func _ppm() -> float:
	var s := _ship()
	if s and "pixels_per_metre" in s:
		return s.pixels_per_metre
	return pixels_per_metre


## The ship the gun is on: the nearest RigidBody2D above it.
func _ship() -> RigidBody2D:
	var p := get_parent()
	while p:
		if p is RigidBody2D:
			return p
		p = p.get_parent()
	return null


func _make_ball(ppm: float) -> RigidBody2D:
	var r := 0.5 * shot_diameter
	var ball := _Ball2D.new()
	ball.name = "Shot"
	ball.pixels_per_metre = ppm
	# A disk 4r/3 deep has a ball's volume, so the ball's mass and buoyancy come out right.
	ball.thickness = 4.0 * r / 3.0
	ball.density = shot_mass / (PI / 6.0 * shot_diameter * shot_diameter * shot_diameter)
	var cs := CollisionShape2D.new()
	var circle := CircleShape2D.new()
	circle.radius = r * ppm
	cs.shape = circle
	ball.add_child(cs)
	var dot := _Dot.new()
	dot.radius = r * ppm
	dot.colour = shot_color
	ball.add_child(dot)
	ball.lifetime = shot_lifetime
	return ball


class _Dot extends Node2D:
	var radius := 1.0
	var colour := Color.BLACK

	func _draw() -> void:
		draw_circle(Vector2.ZERO, maxf(radius, 1.0), colour)
