@tool
@icon("res://addons/bounded_buoyancy/icon_cannonball.svg")
class_name BoundedBuoyancyCannonball2D
extends BoundedBuoyancyBody2D
## A ball in fast flight in a side view: round shot from a BoundedBuoyancyCannon2D, or any ball fired
## or thrown hard. As BoundedBuoyancyCannonball in 3D, with the same physics: it is a ball, though the
## side view shows it as a circle. Give it one CircleShape2D child; BoundedBuoyancyCannon2D makes them
## ready to fly.
##
## Drag in the air: a fast ball's drag changes with its speed, about 0.2 for a cannonball below two
## thirds of the speed of sound and rising to about 1 above it (Miller and Bailey's sphere drag, from
## 18th and 19th century cannon firings), so shot carries as far as the gunners' tables give.
##
## Skipping off the water: a ball that meets the water at a shallow angle skips off it. The water pushes
## on the part of the ball under its surface that faces into its path, with pressure ½ ρ u², u its speed
## into the surface there (Birkhoff and others, 1944), and a ball skips when it comes down at less than
## about 18° / √σ, σ its density over the water's: 6.5° for iron. The contact lasts much less than a
## physics step, so a step in which the ball meets the water is worked through in short steps.
##
## Hitting things: a body with a BoundedBuoyancyHull2D (its planking) is struck by the ball's own
## reckoning: the ball goes in through the planking and out of the far side if it has the speed,
## holing the nearest compartment each time, and otherwise lodges in the wood; the body takes the
## momentum it loses. Anything else it strikes as the engine meets any collision (continuous collision
## detection is on). Under the water and once slow, it is an ordinary BoundedBuoyancyBody2D: iron sinks.

## The ball has struck `body` at the global point `at`, flying at `velocity` (px/s) just before.
signal hit(body: Node, at: Vector2, velocity: Vector2)

## Skip off the water at shallow angles. Off, the water takes the ball as it does any body.
@export var ricochet: bool = true
## Speed of sound in the air, m/s: 343 at 20 °C.
@export_range(1.0, 2000.0, 0.1, "or_greater", "suffix:m/s") var speed_of_sound: float = 343.0
## Seconds after which the ball removes itself. 0 keeps it.
@export_range(0.0, 3600.0, 0.1, "or_greater", "suffix:s") var lifetime: float = 30.0

## Mach number of the ball's flight through the air this step.
var mach: float = 0.0
## How many times the ball has skipped off the water.
var skips: int = 0
## The ball's velocity as it last left the water (px/s).
var exit_velocity: Vector2 = Vector2.ZERO

const _Ball3D := preload("res://addons/bounded_buoyancy/bounded_buoyancy_cannonball.gd")
const _HullPlanks := preload("res://addons/bounded_buoyancy/bounded_buoyancy_hull_2d.gd")
const _Hull3D := preload("res://addons/bounded_buoyancy/bounded_buoyancy_hull.gd")
const SUB_STEP := 0.2   # in the water, each short step moves the ball in or out by this share of its depth
const M_UP := Vector2(0.0, 1.0)   # up, in the ball's own reckoning (metres, y up)

var _age := 0.0
var _carry := false
var _chord := Vector2.ZERO
var _true_velocity := Vector2.ZERO
var _passing := {}    # bodies it is going through or has just left: seconds left
var _wet := false
var _rate := 0.0


func _ready() -> void:
	super()
	if Engine.is_editor_hint():
		return
	continuous_cd = RigidBody2D.CCD_MODE_CAST_SHAPE
	contact_monitor = true
	max_contacts_reported = 4
	body_entered.connect(func(other: Node) -> void:
		if linear_velocity.length() >= 2.0 * pixels_per_metre:
			hit.emit(other, global_position, linear_velocity))


## Go through `body` without striking it for `seconds` (the gun's own ship, as the shot leaves).
func pass_through(body: PhysicsBody2D, seconds: float) -> void:
	add_collision_exception_with(body)
	_passing[body] = seconds


func _physics_process(dt: float) -> void:
	if Engine.is_editor_hint():
		return
	_age += dt
	if lifetime > 0.0 and _age >= lifetime:
		queue_free()
		return
	for b in _passing.keys():
		_passing[b] -= dt
		if _passing[b] <= 0.0:
			_passing.erase(b)
			if is_instance_valid(b):
				remove_collision_exception_with(b)
	if _carry:
		# The engine carried the ball along the chord of its last worked-through step; unless something
		# has struck it since, it goes on with its real velocity.
		_carry = false
		if (linear_velocity - _chord).length() <= 1e-3 * maxf(_chord.length(), 1.0):
			linear_velocity = _true_velocity
	if not _set_up or freeze or not _circle or model != Model.BOUNDED:
		super(dt)
		return
	var ppm := pixels_per_metre
	var g := float(ProjectSettings.get_setting("physics/2d/default_gravity")) / ppm
	var a := _radius / ppm
	var v := Vector2(linear_velocity.x, -linear_velocity.y) / ppm
	if v.length_squared() < 2.0 * g * a:
		super(dt)   # slow: an ordinary body
		return
	_fast_step(dt, ppm, g, a)


# The ball's world, in metres with y up, against the engine's pixels with y down.
func _m(p: Vector2, ppm: float) -> Vector2:
	return Vector2(p.x, -p.y) / ppm


func _px(p: Vector2, ppm: float) -> Vector2:
	return Vector2(p.x, -p.y) * ppm


## The surface's height (m, y up) over the point `x` (m), the water's density and current (m/s): [level,
## rho, flow, found]. With no water there, level is -INF.
func _water_at(x: Vector2, ppm: float) -> Array:
	var p := _px(x, ppm)
	var w := _find_water(p)
	if w:
		var cur: Vector2 = w.current_at(p)
		return [-w.height_at(p.x) / ppm, w.density, Vector2(cur.x, -cur.y) / ppm, true]
	if not _world_has_water:
		return [-water_level / ppm, water_density, Vector2.ZERO, true]
	return [-INF, water_density, Vector2.ZERO, false]


## The surface's normal (m-space, up) over `x`: the waves tilt it.
func _normal_at(x: Vector2, ppm: float) -> Vector2:
	var h := 0.05
	var l0: float = _water_at(x - Vector2(h, 0.0), ppm)[0]
	var l1: float = _water_at(x + Vector2(h, 0.0), ppm)[0]
	if is_inf(l0) or is_inf(l1):
		return M_UP
	return Vector2(-(l1 - l0) / (2.0 * h), 1.0).normalized()


func _fast_step(dt: float, ppm: float, g: float, a: float) -> void:
	var start := _m(global_position, ppm)
	var x := start
	var v := Vector2(linear_velocity.x, -linear_velocity.y) / ppm
	var m := own_mass
	var vol := 4.0 / 3.0 * PI * a * a * a
	var area := PI * a * a
	var air := air_density
	var t := 0.0
	var n := 0
	while t < dt - 1e-12 and n < 5000:
		n += 1
		var rest := dt - t
		var w := _water_at(x, ppm)
		var level: float = w[0]
		var rho: float = w[1]
		var flow: Vector2 = w[2]
		var dd := clampf(level - (x.y - a), 0.0, 2.0 * a) if not is_inf(level) else 0.0
		var h: float
		var x1: Vector2
		var v1: Vector2
		if dd <= 0.0 or not ricochet:
			if _wet:
				_wet = false
				skips += 1
				exit_velocity = _px(v, ppm)
				water_exited.emit((v - flow).length() * ppm, _px(Vector2(x.x, level), ppm))
			# Clear of the water: fly on for as long as it surely stays clear, or to the end of the step.
			var clear := (x.y - a) - level if not is_inf(level) else INF
			var closing := maxf(-v.y, 0.0) + g * rest
			h = rest if closing <= 0.0 or is_inf(clear) else minf(rest, maxf(clear, 0.05 * a) / closing)
			h = minf(h, 20.0 * a / maxf(v.length(), 1e-6))
			var mi := m + added_mass_coefficient * air * vol
			var s := v.length()
			mach = s / maxf(speed_of_sound, 1.0)
			var ca := 0.5 * _Ball3D.drag_coefficient_at(s, 2.0 * a, air, speed_of_sound) * area * air * s * h / mi
			v1 = (v + M_UP * ((air * vol - m) * g / mi * h)) / (1.0 + ca)
			x1 = x + v1 * h
		elif dd >= 2.0 * a:
			# Wholly under: no push across its path (the ball is round), its drag as a body's.
			var mi := m + added_mass_coefficient * rho * vol
			var u := v - flow
			h = minf(rest, 4.0 * a / maxf(u.length(), 1e-6))
			var cw := 0.5 * drag_coefficient * area * rho * u.length() * h / mi
			v1 = (v + M_UP * ((rho * vol - m) * g / mi * h) + cw * flow) / (1.0 + cw)
			x1 = x + v1 * h
		else:
			if not _wet:
				_wet = true
				water_entered.emit((v - flow).length() * ppm, _px(Vector2(x.x, level), ppm))
			# A short step, second order (midpoint): short enough that the ball moves into or out of the
			# water by no more than a fifth of how deep it is, and goes no more than ten radii.
			var normal := _normal_at(x, ppm)
			var acc := _accel(v, dd, normal, flow, rho, air, g, a, m, vol, area)
			var speed := (v - flow).length()
			var ref := maxf(dd, 0.05 * a)
			h = minf(rest, 10.0 * a / maxf(speed, 1e-6))
			h = minf(h, SUB_STEP * ref / maxf(absf((v - flow).dot(normal)), 1e-6))
			h = minf(h, sqrt(2.0 * SUB_STEP * ref / maxf(absf(acc.dot(normal)), 1e-9)))
			h = minf(h, 0.5 / maxf(_rate, 1e-9))
			var xm := x + v * (0.5 * h)
			var vm := v + acc * (0.5 * h)
			var wm := _water_at(xm, ppm)
			var ddm := clampf(float(wm[0]) - (xm.y - a), 0.0, 2.0 * a) if not is_inf(wm[0]) else 0.0
			var accm := _accel(vm, ddm, _normal_at(xm, ppm), flow, rho, air, g, a, m, vol, area)
			x1 = x + vm * h
			v1 = v + accm * h
		# Planking on the way?
		var struck := _strike(x, x1, v, ppm, a, m)
		if not struck.is_empty():
			if struck[0] == null:
				return   # lodged
			x = struck[0]
			v = struck[1]
			t += h * float(struck[2])
			continue
		x = x1
		v = v1
		t += h
	# The engine moves the ball on by the velocity it ends the step with, so it is given the chord of the
	# step's path; the real velocity is handed back at the start of the next step.
	var chord := _px((x - start) / dt, ppm)
	linear_velocity = chord
	angular_velocity *= 0.98
	_chord = chord
	_true_velocity = _px(v, ppm)
	_carry = true
	last_acceleration = (_true_velocity - Vector2(chord.x, chord.y)) / dt
	var d_end := 0.0
	var w_end := _water_at(x, ppm)
	if not is_inf(w_end[0]):
		d_end = clampf(float(w_end[0]) - (x.y - a), 0.0, 2.0 * a)
	submerged_area = area * ppm * ppm * (d_end / (2.0 * a))
	_prev_added_mass = -1.0
	_was_in_water = d_end > 0.0


## Acceleration of the ball (m/s², y up) moving at `v`, `dd` deep in water whose surface has `normal`:
## weight, buoyancy, the water's push on the part under it and the air's drag on the rest, against its
## mass and the water and air it takes along, as BoundedBuoyancyCannonball.
func _accel(v: Vector2, dd: float, normal: Vector2, flow: Vector2, rho: float, air: float, g: float, a: float, m: float, vol: float, area: float) -> Vector2:
	var under := PI * dd * dd * (3.0 * a - dd) / 3.0
	var displaced := rho * under + air * (vol - under)
	var mi := m + added_mass_coefficient * displaced
	var acc := M_UP * ((displaced - m) * g / mi)
	var u := v - flow
	var speed := u.length()
	var drag_area := area * under / vol
	if dd > 0.0 and speed > 1e-9:
		var fast := smoothstep(1.0, 2.0, speed / sqrt(2.0 * g * a))
		if fast > 0.0:
			var into := u.dot(normal)
			var skim := u - normal * into
			var skim_speed := skim.length()
			var along := skim / skim_speed if skim_speed > 1e-12 else Vector2.ZERO
			var p: Vector2 = _Ball3D.newton_push(skim_speed / speed, into / speed, 1.0 - dd / a)
			var push := (along * p.x + normal * p.y) * (-0.5 * rho * speed * speed * a * a)
			var dir := u / speed
			var back := -push.dot(dir)
			acc += (push + dir * back) * (fast / mi)
			drag_area = lerpf(drag_area, back / (0.25 * rho * speed * speed), fast)
	var rate_water := 0.5 * drag_coefficient * drag_area * rho * speed / mi
	var air_speed := v.length()
	var rate_air := 0.5 * _Ball3D.drag_coefficient_at(air_speed, 2.0 * a, air, speed_of_sound) * area * air * (vol - under) / vol * air_speed / mi
	_rate = rate_water + rate_air
	return acc - u * rate_water - v * rate_air


## Does the ball's path from `x0` to `x1` (m) strike planking? If not, []. If it does: [where it goes on
## from (m), its velocity (m/s), the share of the short step used], or [null] if it lodged.
func _strike(x0: Vector2, x1: Vector2, v: Vector2, ppm: float, a: float, m: float) -> Array:
	var space := get_world_2d().direct_space_state
	var q := PhysicsRayQueryParameters2D.create(_px(x0, ppm), _px(x1, ppm), collision_mask)
	var skip: Array[RID] = [get_rid()]
	for b in _passing.keys():
		if is_instance_valid(b):
			skip.append(b.get_rid())
	q.exclude = skip
	var r := space.intersect_ray(q)
	if r.is_empty():
		return []
	var body: Object = r.collider
	var hull: Node = _HullPlanks.of(body)
	var s := v.length()
	if hull == null or s < _Hull3D.STRIKE_SPEED:
		return []   # the engine meets it as it does any collision
	var dir := v / s
	var n: Vector2 = r.normal
	var nm := Vector2(n.x, -n.y)
	var cos_in := -dir.dot(nm)
	if cos_in < _Hull3D.GLANCE:
		return []   # glancing: the engine's collision turns it aside
	var at: Vector2 = r.position
	var share := clampf((_m(at, ppm) - x0).length() / maxf((x1 - x0).length(), 1e-9), 0.0, 1.0)
	hit.emit(body, at, _px(v, ppm))
	var hole: float = PI * a * a * hull.hole_size
	var s1: float = hull.speed_through(m, a, s, cos_in)
	hull.make_hole(at, hole)
	var push_on := func(p: Vector2, momentum: Vector2) -> void:
		if body is RigidBody2D:
			body.apply_impulse(_px(momentum, ppm), p - body.global_position)
	if s1 <= 0.0:
		hull.struck.emit(self, at, false, 0.0)
		push_on.call(at, v * m)
		queue_free()
		return [null]
	# Through the ship and out of the far side, where its path leaves the body's shape.
	var far := at + _px(dir, ppm).normalized() * 1e5
	var q2 := PhysicsRayQueryParameters2D.create(far, at, collision_mask)
	q2.exclude = skip
	var r2 := space.intersect_ray(q2)
	var out_at: Vector2 = r2.position if not r2.is_empty() and r2.collider == body else at
	var cos_out := cos_in
	if not r2.is_empty() and r2.collider == body:
		cos_out = maxf(dir.dot(Vector2(r2.normal.x, -r2.normal.y)), _Hull3D.GLANCE)   # the far side faces along the path
	var s2: float = hull.speed_through(m, a, s1, cos_out)
	if s2 <= 0.0:
		hull.struck.emit(self, at, true, s1)
		hull.struck.emit(self, out_at, false, 0.0)
		push_on.call(out_at, v * m)
		queue_free()
		return [null]
	if out_at != at:
		hull.make_hole(out_at, hole)
	hull.struck.emit(self, at, true, s2)
	push_on.call(at, dir * (s - s2) * m)
	if body is PhysicsBody2D:
		pass_through(body, 0.2)
	return [_m(out_at, ppm) + dir * (a * 1.05), dir * s2, share]
