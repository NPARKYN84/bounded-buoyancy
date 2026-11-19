@tool
@icon("res://addons/bounded_buoyancy/icon_line.svg")
class_name BoundedBuoyancyLine2D
extends Node2D
## A line in a side view: a mooring rope, an anchor cable, a tow line, a fishing line. It pulls but
## never pushes. As BoundedBuoyancyLine in 3D.
##
## Put the node where the line is made fast (a bollard, a buoy, an anchor on the sea bed, a boat's
## bow) and set body to what is on the other end, made fast at end_offset in that body's own frame
## (pixels). If the node is under a RigidBody2D, that body is pulled too (a boat towing another);
## otherwise the node holds fast.
##
## The line is elastic: stretched by x beyond its length L it pulls with T = E A x / L (Hooke's law).
## Shorter than its length it hangs slack and pulls nothing, so a moored boat rides freely until it
## drifts to the end of its cable. Like the buoyancy, the pull is bounded: each step it is worked out
## with the stretch and the speed at the end of the step, against the inertia of what is on each end
## (water taken along and water inside included), so a stiff line neither shakes nor flings anything.
## A line pulled harder than breaking_strength breaks; one with a slip_tension runs out instead.

## Emitted when the line breaks, with the pull that broke it (N).
signal snapped(tension: float)

## What is on the other end. Leave empty for a line made fast to nothing.
@export var body: RigidBody2D:
	set(value):
		body = value
		queue_redraw()
## Where the line is made fast on body, in body's own frame (pixels).
@export var end_offset: Vector2 = Vector2.ZERO
## Length of the line unstretched, in metres. Shorter heaves it in, longer pays it out.
@export_range(0.0, 10000.0, 0.001, "or_greater", "suffix:m") var length: float = 5.0:
	set(value):
		length = maxf(value, 0.0)
		queue_redraw()
## Diameter of the line, in metres: a fishing line 0.0003, a mooring rope 0.02, an anchor chain's link
## about 0.02.
@export_range(0.00001, 1.0, 0.00001, "or_greater", "suffix:m") var diameter: float = 0.02
## Stiffness of its material (Young's modulus), in GPa: nylon 2 to 4, polyester rope about 5 (laid
## rope stretches more), steel wire and chain 200.
@export_range(0.0001, 300.0, 0.0001, "or_greater", "suffix:GPa") var stiffness: float = 2.5
## Damping of the line's stretch: 0 springs back for ever, 1 settles without springing back.
@export_range(0.0, 2.0, 0.01) var damping: float = 0.5
## The pull at which the line runs out, in newtons (a reel's drag, a winch's clutch). 0: it never does.
@export_range(0.0, 10000000.0, 0.1, "or_greater", "suffix:N") var slip_tension: float = 0.0
## The pull at which the line breaks, in newtons. 0 never breaks.
@export_range(0.0, 10000000.0, 0.1, "or_greater", "suffix:N") var breaking_strength: float = 0.0
## Pixels a metre: the body's own, if it is a BoundedBuoyancyBody2D.
@export_range(1.0, 10000.0, 0.1, "or_greater") var pixels_per_metre: float = 100.0

@export_group("Look")
## Show the line.
@export var visible_line: bool = true
## Its colour.
@export var colour: Color = Color(0.25, 0.22, 0.18, 0.9)
## Its width on screen, in pixels.
@export var width: float = 2.0

## Pull along the line over the last physics step, in newtons.
var tension: float = 0.0
## How far the line is stretched beyond its length, in metres (negative while slack).
var stretch: float = 0.0
## Distance between its ends, in metres.
var span: float = 0.0
## The line has broken.
var broken: bool = false

const SEGMENTS := 24

var _holder: RigidBody2D = null
var _g := 9.8


func _enter_tree() -> void:
	_holder = null
	var p := get_parent()
	while p:
		if p is RigidBody2D:
			_holder = p
			break
		if p is PhysicsBody2D:
			break
		p = p.get_parent()


func _ready() -> void:
	top_level = false
	_g = float(ProjectSettings.get_setting("physics/2d/default_gravity"))


func _get_configuration_warnings() -> PackedStringArray:
	if body == null:
		return PackedStringArray(["Set Body to what is on the other end of the line."])
	return PackedStringArray()


## Where the line is made fast at this end and at the far end (global, pixels).
func ends() -> Array[Vector2]:
	var far := global_position
	if body and is_instance_valid(body):
		far = body.global_transform * end_offset
	return [global_position, far]


## Spring constant of the whole line, N/m: E A / L.
func spring_constant() -> float:
	return stiffness * 1e9 * PI * diameter * diameter * 0.25 / maxf(length, 1e-4)


## Pay out (positive) or heave in (negative) `metres` of line.
func pay_out(metres: float) -> void:
	length = maxf(length + metres, 0.0)


func _ppm() -> float:
	if body and is_instance_valid(body) and &"pixels_per_metre" in body:
		return body.pixels_per_metre
	return pixels_per_metre


func _physics_process(dt: float) -> void:
	if Engine.is_editor_hint():
		queue_redraw()
		return
	queue_redraw()
	tension = 0.0
	if broken or body == null or not is_instance_valid(body) or dt <= 0.0:
		return
	var ppm := _ppm()
	var e := ends()
	var a: Vector2 = e[0]
	var b: Vector2 = e[1]
	var d := b - a
	span = d.length() / ppm
	stretch = span - length
	if span < 1e-6:
		return
	var n := d.normalized()   # from this end toward the far end
	var a_moves := _moves(_holder)
	var b_moves := _moves(body)
	if not (a_moves or b_moves):
		return
	# Speed (m/s) at which the ends draw apart now, and how that would change over the step without the
	# line, so that a weight hanging still stretches it by just m g / k.
	var u := 0.0
	var w := 0.0   # 1 / inertia along the line (1/kg), both ends together
	if b_moves:
		u += (_point_velocity(body, b) + _other_acceleration(body) * dt).dot(n) / ppm
		w += _inverse_mass_at(body, b, n)
	if a_moves:
		u -= (_point_velocity(_holder, a) + _other_acceleration(_holder) * dt).dot(n) / ppm
		w += _inverse_mass_at(_holder, a, n)
	if w <= 0.0:
		return
	# Implicit (backward Euler) spring and damper: the pull J (an impulse, N s) is the one the stretch
	# and the speed at the end of the step give. It never pushes.
	var k := spring_constant()
	var c := 2.0 * damping * sqrt(k / w)
	var j := dt * (k * stretch + (k * dt + c) * u) / (1.0 + dt * (k * dt + c) * w)
	if j <= 0.0:
		return
	if slip_tension > 0.0 and j > slip_tension * dt:
		j = slip_tension * dt
		var stretched := stretch + (u - j * w) * dt
		length += maxf(stretched - slip_tension / k, 0.0)
	tension = j / dt
	if breaking_strength > 0.0 and tension > breaking_strength:
		broken = true
		snapped.emit(tension)
		tension = 0.0
		return
	var pull := n * tension * ppm   # engine units: kg px/s²
	if b_moves:
		body.apply_force(-pull, b - body.global_position)
	if a_moves:
		_holder.apply_force(pull, a - _holder.global_position)


static func _moves(rb: RigidBody2D) -> bool:
	return rb != null and is_instance_valid(rb) and not rb.freeze and rb.mass > 0.0


static func _point_velocity(rb: RigidBody2D, at: Vector2) -> Vector2:
	var state := PhysicsServer2D.body_get_direct_state(rb.get_rid())
	if state:
		return state.get_velocity_at_local_position(at - state.transform.origin)
	return rb.linear_velocity


func _other_acceleration(rb: RigidBody2D) -> Vector2:
	if &"last_acceleration" in rb:
		return rb.last_acceleration
	return Vector2.DOWN * _g * rb.gravity_scale


## 1 / the inertia `rb` puts up against a pull along `n` at `at`: its mass and its turning.
static func _inverse_mass_at(rb: RigidBody2D, at: Vector2, n: Vector2) -> float:
	var inv := 1.0 / rb.mass
	var state := PhysicsServer2D.body_get_direct_state(rb.get_rid())
	if state:
		var r := at - state.transform * state.center_of_mass_local
		var rn := r.cross(n)
		inv += rn * rn * state.inverse_inertia
	return inv


func _draw() -> void:
	if not visible_line or broken or body == null or not is_instance_valid(body):
		return
	var e := ends()
	var a: Vector2 = to_local(e[0])
	var b: Vector2 = to_local(e[1])
	var ppm := _ppm()
	var dd := a.distance_to(b) / ppm
	# A slack line hangs in a curve as long as the line: a parabola of sag s has length about
	# D + 8 s² / 3 D.
	var sag := sqrt(3.0 * dd * maxf(length - dd, 0.0) / 8.0) * ppm if dd > 1e-4 else 0.0
	var pts := PackedVector2Array()
	for i in SEGMENTS + 1:
		var t := float(i) / SEGMENTS
		pts.append(a.lerp(b, t) + Vector2.DOWN * (4.0 * sag * t * (1.0 - t)))
	draw_polyline(pts, colour, width, true)
