@icon("res://addons/bounded_buoyancy/icon_cannonball.svg")
class_name BoundedBuoyancyCannonball
extends BoundedBuoyancyBody
## A ball in fast flight: round shot from a cannon, a musket ball, or any ball fired or thrown hard.
## Give it one SphereShape3D child; BoundedBuoyancyCannon makes them ready to fly.
##
## Drag in the air: a fast ball's drag changes with its speed. Below about two thirds of the speed of
## sound, a cannonball's drag coefficient is about 0.2, well under a slow ball's 0.47, as the air
## round it is turbulent (the drag crisis); it rises through the speed of sound to about 1. The drag
## coefficient here is that of the sphere drag of Miller and Bailey (1979), worked out from 18th and
## 19th century cannon firings, as fitted by Collins, with Morrison's (2013) correlation for the
## Reynolds number. So a 24-pounder carries about a mile at 5 degrees, as the gunners' tables give.
##
## Skipping off the water: a ball that meets the water at a shallow angle skips off it, as round shot
## fired to ricochet across the sea did. The water pushes on the part of the ball under its surface
## that faces into the ball's path, with pressure ½ ρ u², u the ball's speed into the surface there,
## square to it (Birkhoff and others, 1944). Over a ball the push across its path is a lift, and a
## ball skips when it comes down at less than about 18° / √σ, σ its density over the water's: 6.5° for
## iron, 11° for aluminium, 18° for a ball as dense as water. A steel ball 7 mm across skipping at
## 57 m/s leaves the water at the speed and angle Lyu, Yun and Wei (2021) measured. The contact lasts
## much less than a physics step, so a step in which the ball meets the water is worked through in
## short steps while it is in the water, each taking it no more than a fifth of its depth further in
## or out (second order, by the midpoint). Waves tilt the surface, so a ball meets the face of a wave
## more steeply and plunges sooner. The lift comes in between Froude numbers 1 and 2 (a speed of
## √(g d) to 2 √(g d), d the diameter), where the water's push on a moving ball stops being mostly
## buoyancy.
##
## Hitting things: it flies too far in a step for the engine's usual collision tests, so continuous
## collision detection is on and it strikes a hull instead of passing through it; hit is emitted.
## Under the water and once slow, it is an ordinary BoundedBuoyancyBody: an iron ball sinks.

## The ball has struck `body` at the world point `at`, flying at `velocity` (world frame) just before.
signal hit(body: Node, at: Vector3, velocity: Vector3)

## Skip off the water at shallow angles. Off, the water takes the ball as it does any body.
@export var ricochet: bool = true
## Speed of sound in the air, m/s: 343 at 20 °C, 331 at 0 °C.
@export_range(1.0, 2000.0, 0.1, "or_greater", "suffix:m/s") var speed_of_sound: float = 343.0
## Seconds after which the ball removes itself. 0 keeps it.
@export_range(0.0, 3600.0, 0.1, "or_greater", "suffix:s") var lifetime: float = 30.0

## Mach number of the ball's flight through the air this step.
var mach: float = 0.0
## How many times the ball has skipped off the water.
var skips: int = 0
## The ball's velocity as it last left the water (world frame).
var exit_velocity: Vector3 = Vector3.ZERO

const AIR_VISCOSITY := 1.81e-5      # kg/(m s), air at 15 °C
const HIT_SPEED := 2.0              # m/s, slowest strike reported by hit
const SUB_STEP := 0.2               # in the water, each short step moves the ball in or out by this share of its depth
# The sphere drag of Miller and Bailey (1979), as fitted by Collins: C_D = k(M) + t(M) C_Re(s(M) Re),
# k and t cubic Bezier curves through these control points (Mach number, value).
const _SHOCK := [Vector2(0.1, 0.0), Vector2(0.95, 0.0), Vector2(0.55, 0.95), Vector2(1.5, 1.0)]
const _MASK := [Vector2(0.0, 1.1), Vector2(0.85, 1.1), Vector2(0.57, 0.05), Vector2(1.0, 0.0)]
# Four-point Gauss-Legendre rule.
const _GX := [-0.8611363115940526, -0.3399810435848563, 0.3399810435848563, 0.8611363115940526]
const _GW := [0.3478548451374538, 0.6521451548625461, 0.6521451548625461, 0.3478548451374538]

static var _shock_table := PackedFloat32Array()
static var _mask_table := PackedFloat32Array()

var _age := 0.0
var _flight_velocity := Vector3.ZERO
var _touching := {}
var _chord := Vector3.ZERO      # velocity the engine was given for the last worked-through step
var _true_velocity := Vector3.ZERO   # and the ball's real velocity at its end
var _carry := false
var _rate := 0.0


func _ready() -> void:
	super()
	if Engine.is_editor_hint():
		return
	continuous_cd = true
	contact_monitor = true
	max_contacts_reported = maxi(max_contacts_reported, 4)


func _physics_process(dt: float) -> void:
	if Engine.is_editor_hint():
		return
	_age += dt
	if lifetime > 0.0 and _age >= lifetime:
		queue_free()
		return
	if _carry:
		# The engine carried the ball along the chord of its last worked-through step; unless something
		# has struck it since, it goes on with its real velocity.
		_carry = false
		if (linear_velocity - _chord).length() <= 1e-4 * maxf(_chord.length(), 1.0):
			linear_velocity = _true_velocity
	_flight_velocity = linear_velocity
	if ricochet and _set_up and not freeze and model == Model.BOUNDED and _single_sphere and _skip_step(dt):
		return
	super(dt)


func _integrate_forces(state: PhysicsDirectBodyState3D) -> void:
	var now := {}
	for i in state.get_contact_count():
		var other := state.get_contact_collider_object(i)
		if other == null:
			continue
		now[other] = true
		if not _touching.has(other) and _flight_velocity.length() >= HIT_SPEED:
			hit.emit(other, state.get_contact_collider_position(i), _flight_velocity)
	_touching = now


func _air_drag_coefficient(speed: float) -> float:
	if not _single_sphere:
		return drag_coefficient   # a fast ball's drag is for a ball
	mach = speed / maxf(speed_of_sound, 1.0)
	return drag_coefficient_at(speed, 2.0 * _radius, air_density_here, speed_of_sound)


## Drag coefficient in air of density `air_density` of a ball `diameter` metres across flying at
## `speed`, the speed of sound being `sound_speed`: the sphere drag of Miller and Bailey (1979), as
## fitted by Collins (arc.id.au), C_D = k(M) + t(M) C_Re(s(M) Re), with Morrison's (2013) correlation
## for C_Re. About 0.2 for a cannonball below Mach 0.6, 0.8 at Mach 1, 1 above Mach 1.4.
static func drag_coefficient_at(speed: float, diameter: float, air_density: float, sound_speed := 343.0) -> float:
	if _shock_table.is_empty():
		_build_tables()
	var m := clampf(speed / maxf(sound_speed, 1.0), 0.2, 1.5)
	var f := (m - 0.2) / 0.005
	var i := mini(int(f), _shock_table.size() - 2)
	f -= i
	var k := lerpf(_shock_table[i], _shock_table[i + 1], f)
	var t := lerpf(_mask_table[i], _mask_table[i + 1], f)
	var s := 0.78 + 0.22 * atan(-12.0 * (m - 0.23))
	var re := maxf(speed * diameter * maxf(air_density, 1e-6) / AIR_VISCOSITY, 1.0)
	return k + t * morrison_drag(s * re)


## Drag coefficient of a smooth sphere at the Reynolds number `re`, from creeping flow through the drag
## crisis (Morrison, 2013): 0.47 at 10^5, about 0.1 just past the crisis at 5 x 10^5.
static func morrison_drag(re: float) -> float:
	return 24.0 / re + 2.6 * (re / 5.0) / (1.0 + pow(re / 5.0, 1.52)) \
		+ 0.411 * pow(re / 2.63e5, -7.94) / (1.0 + pow(re / 2.63e5, -8.0)) \
		+ 0.25 * (re / 1.0e6) / (1.0 + re / 1.0e6)


static func _build_tables() -> void:
	# k(M) and t(M) every 0.005 from Mach 0.2 to 1.5, from their Bezier curves.
	var n := 261
	_shock_table.resize(n)
	_mask_table.resize(n)
	for j in n:
		var m := 0.2 + 0.005 * j
		_shock_table[j] = 1.0 if m >= 1.5 else _bezier_at(_SHOCK, m)
		_mask_table[j] = 0.0 if m > 1.0 else _bezier_at(_MASK, m)


## The value of the cubic Bezier curve with control points `p` (x, value) at x, its x rising along it.
static func _bezier_at(p: Array, x: float) -> float:
	var lo := 0.0
	var hi := 1.0
	for n in 40:
		var u := 0.5 * (lo + hi)
		var w := 1.0 - u
		if w * w * w * p[0].x + 3.0 * w * w * u * p[1].x + 3.0 * w * u * u * p[2].x + u * u * u * p[3].x < x:
			lo = u
		else:
			hi = u
	var u := 0.5 * (lo + hi)
	var w := 1.0 - u
	return w * w * w * p[0].y + 3.0 * w * w * u * p[1].y + 3.0 * w * u * u * p[2].y + u * u * u * p[3].y


## The water's push on a ball of unit radius moving with unit velocity (uh along the level, uy up),
## its centre `c` radii above the surface: the integral of (u·n)² n over the part of the ball under the
## surface that faces into its path (u·n > 0), as Vector2(along uh's direction, up). The force is
## -½ ρ U² a² times this. The part's edge, z = -c, and the edges of the facing side, at z = ±uh, split
## the integral over height z; round the ball (angle φ) it is worked out exactly.
static func newton_push(uh: float, uy: float, c: float) -> Vector2:
	var lo := -1.0
	var hi := minf(-c, 1.0)
	if hi <= lo:
		return Vector2.ZERO
	var cuts := PackedFloat64Array([lo])
	if -uh > lo and -uh < hi:
		cuts.append(-uh)
	if uh > lo and uh < hi and uh > -uh:
		cuts.append(uh)
	cuts.append(hi)
	var along := 0.0
	var up := 0.0
	for j in cuts.size() - 1:
		var mid := 0.5 * (cuts[j] + cuts[j + 1])
		var half := 0.5 * (cuts[j + 1] - cuts[j])
		for q in 4:
			var z: float = mid + half * _GX[q]
			var s := sqrt(maxf(1.0 - z * z, 0.0))
			var a := uh * s
			var b := uy * z
			var p0 := PI
			if a < 1e-12:
				if b <= 0.0:
					continue
			else:
				var r := -b / a
				if r >= 1.0:
					continue
				if r > -1.0:
					p0 = acos(r)
			var sp := sin(p0)
			var cp := cos(p0)
			var j0 := a * a * (p0 + sp * cp) + 4.0 * a * b * sp + 2.0 * b * b * p0
			var j1 := a * a * 2.0 * (sp - sp * sp * sp / 3.0) + 2.0 * a * b * (p0 + sp * cp) + 2.0 * b * b * sp
			along += _GW[q] * half * s * j1
			up += _GW[q] * half * z * j0
	return Vector2(along, up)


## A step in which a fast ball meets the water, worked through in short steps. Returns false, leaving
## the step to BoundedBuoyancyBody, when the ball is slow (Froude number below 1), wholly under the
## water, out of the water all step, or where there is no water.
func _skip_step(dt: float) -> bool:
	var frame := Engine.get_physics_frames()
	if frame != _g_frame:
		_g = float(ProjectSettings.get_setting("physics/3d/default_gravity"))
		_g_frame = frame
	var g := _g
	var xf := global_transform
	var c := xf * _sphere_centre
	var a := _radius
	water = _find_water(c)
	if water == null and not (water_surface or not _world_has_water):
		return false
	var flow: Vector3 = water.current_at(c) if water else Vector3.ZERO
	var v := linear_velocity
	if (v - flow).length_squared() < 2.0 * g * a:
		return false
	var depth := _surface_at(c) - (c.y - a)
	if depth >= 2.0 * a:
		return false
	if depth <= 0.0:
		# Out of the water: does it reach it this step? Its path's lowest point is at one end of it;
		# over waves, the surface between may reach as high as the crests.
		var end := c + v * dt + Vector3.DOWN * (0.5 * g * dt * dt)
		var lowest := minf(c.y, end.y) - a
		if water and water.is_wavy():
			if lowest > water.global_position.y + water.max_wave():
				return false
		elif end.y - a > _surface_at(end):
			return false
	_through_the_water(dt, c, v, flow, g)
	return true


func _through_the_water(dt: float, start: Vector3, v_start: Vector3, flow: Vector3, g: float) -> void:
	var a := _radius
	var vol := volume
	var m := own_mass
	var rho: float = water.density if water else water_density
	air_density_here = air_density
	if air_half_height > 0.0:
		air_density_here *= pow(0.5, maxf(start.y / air_half_height, -10.0))
	var air := air_density_here
	var wind := _Wind.total_at(start, _world) if not _Wind.instances.is_empty() else Vector3.ZERO
	wind_here = wind
	var slope := _wave_slope()
	var x := start
	var v := v_start
	var t := 0.0
	var wet := _was_in_water   # as the last step left it, so each touch is reported once
	var prev_added := _prev_added_mass   # -1 on a first step: nothing to share then
	var wet_time := 0.0
	var n := 0
	while t < dt - 1e-12 and n < 5000:
		n += 1
		var rest := dt - t
		var level := _surface_at(x)
		if level - (x.y - a) <= 0.0:
			if wet:
				wet = false
				skips += 1
				exit_velocity = v
				_cross(false, Vector3(x.x, level, x.z), (v - flow).length())
			# Clear of the water: fly on for as long as it surely stays clear, or to the end of the step.
			var clear := (x.y - a) - level
			var closing := maxf(-v.y, 0.0) + slope * Vector2(v.x, v.z).length() + g * rest
			var h := rest if closing <= 0.0 else minf(rest, maxf(clear, 0.05 * a) / closing)
			var mi_air := m + added_mass_coefficient * air * vol
			var ua := v - wind
			var ca := 0.5 * _air_drag_coefficient(ua.length()) * _area * air * ua.length() * h / mi_air
			v = (v + Vector3.UP * ((air * vol - m) * g / mi_air * h) + ca * wind) / (1.0 + ca)
			x += v * h
			t += h
			prev_added = added_mass_coefficient * air * vol
			continue
		if not wet:
			wet = true
			_cross(true, Vector3(x.x, level, x.z), (v - flow).length())
		# Depth measured square to the surface, which waves tilt: a ball meets the face of a wave steeply.
		var normal := _surface_normal(x)
		var dd := clampf(a - (x.y - level) * normal.y, 0.0, 2.0 * a)
		var under := PI * dd * dd * (3.0 * a - dd) / 3.0
		var added := added_mass_coefficient * (rho * under + air * (vol - under))
		if keep_momentum_on_entry and prev_added >= 0.0 and added > prev_added:
			# Water taken along starts with the water's own velocity.
			v = flow + (v - flow) * ((m + prev_added) / (m + added))
		prev_added = added
		if dd >= 2.0 * a:
			# Wholly under, the push across its path is nothing (the ball is round): the rest of the
			# step as BoundedBuoyancyBody takes it, its drag worked out with the speed at the end.
			var mi := m + added
			var u := v - flow
			var cw := 0.5 * drag_coefficient * _area * rho * u.length() * rest / mi
			v = (v + Vector3.UP * ((rho * vol - m) * g / mi * rest) + cw * flow) / (1.0 + cw)
			x += v * rest
			wet_time += rest
			t = dt
			break
		# A short step, second order (midpoint): short enough that the ball moves into or out of the
		# water by no more than a fifth of how deep it is, and goes no more than ten radii.
		var acc := _water_accel(x, v, level, normal, dd, flow, wind, rho, air, g)
		var speed := (v - flow).length()
		var ref := maxf(dd, 0.05 * a)
		var h := minf(rest, 10.0 * a / maxf(speed, 1e-6))
		h = minf(h, SUB_STEP * ref / maxf(absf((v - flow).dot(normal)), 1e-6))
		h = minf(h, sqrt(2.0 * SUB_STEP * ref / maxf(absf(acc.dot(normal)), 1e-9)))
		h = minf(h, 0.5 / maxf(_rate, 1e-9))
		var xm := x + v * (0.5 * h)
		var vm := v + acc * (0.5 * h)
		var lm := _surface_at(xm)
		var nm := _surface_normal(xm)
		var ddm := clampf(a - (xm.y - lm) * nm.y, 0.0, 2.0 * a)
		var accm := _water_accel(xm, vm, lm, nm, ddm, flow, wind, rho, air, g)
		x += vm * h
		v += accm * h
		t += h
		wet_time += h * under / vol
	# Where the step leaves it: a touch, or a skip, in its last moment counts too.
	var level_end := _surface_at(x)
	if level_end - (x.y - a) > 0.0 and not wet:
		_cross(true, Vector3(x.x, level_end, x.z), (v - flow).length())
	elif level_end - (x.y - a) <= 0.0 and wet:
		skips += 1
		exit_velocity = v
		_cross(false, Vector3(x.x, level_end, x.z), (v - flow).length())
	var d_end := clampf(level_end - (x.y - a), 0.0, 2.0 * a)
	submerged_volume = PI * d_end * d_end * (3.0 * a - d_end) / 3.0
	var sunk := 3.0 * (2.0 * a - d_end) * (2.0 * a - d_end) / (4.0 * (3.0 * a - d_end))   # cap's centre below the ball's
	centre_of_buoyancy = x + Vector3.DOWN * sunk if submerged_volume > 0.0 else x
	added_mass = added_mass_coefficient * (rho * submerged_volume + air * (vol - submerged_volume))
	var mi_end := m + added_mass
	mass = mi_end
	# The engine moves the ball on by the velocity it ends the step with, so it is given the chord of the
	# step's path, from where the ball was to where the short steps leave it; the real velocity at the end
	# is handed back at the start of the next step. No teleport, and continuous collision detection
	# sweeps the chord.
	var chord := (x - start) / dt
	apply_central_force(mi_end * (chord - v_start) / dt)
	_chord = chord
	_true_velocity = v
	_carry = true
	last_acceleration = (v - v_start) / dt
	heave_damping = 0.0
	wave_acceleration = 0.0
	_prev_added_mass = added_mass
	_prev_surface = NAN
	_was_in_water = submerged_volume > 0.0
	_first_step = false
	var wet_share := wet_time / dt
	var damp := angular_drag * (wet_share + (1.0 - wet_share) * minf(1.0, air / density))
	if absf(damp - angular_damp) > 1e-6:
		angular_damp = damp


## Acceleration of a ball at `x` moving at `v`, `dd` deep in water whose surface there is at `level`
## with normal `normal`: weight, buoyancy, the water's push on the part under it and the air's drag on
## the rest, against the ball's mass and the water and air it takes along. Leaves in _rate how fast
## drag takes away its speed (1/s).
func _water_accel(x: Vector3, v: Vector3, level: float, normal: Vector3, dd: float, flow: Vector3, wind: Vector3, rho: float, air: float, g: float) -> Vector3:
	var a := _radius
	var vol := volume
	var under := PI * dd * dd * (3.0 * a - dd) / 3.0
	var displaced := rho * under + air * (vol - under)
	var mi := own_mass + added_mass_coefficient * displaced
	var acc := Vector3.UP * ((displaced - own_mass) * g / mi)
	var u := v - flow
	var speed := u.length()
	# Drag in the water: as BoundedBuoyancyBody's, the area times the share of the ball under water.
	var drag_area := _area * under / vol
	if dd > 0.0 and speed > 1e-9:
		var fast := smoothstep(1.0, 2.0, speed / sqrt(2.0 * g * a))
		if fast > 0.0:
			# The water's push on the part under the surface that faces into the ball's path: across the
			# path, a lift; along it, a drag, scaled so that wholly under it is the ball's own drag.
			var into := u.dot(normal)
			var skim := u - normal * into
			var skim_speed := skim.length()
			var along := skim / skim_speed if skim_speed > 1e-12 else Vector3.ZERO
			var p := newton_push(skim_speed / speed, into / speed, 1.0 - dd / a)
			var push := (along * p.x + normal * p.y) * (-0.5 * rho * speed * speed * a * a)
			var dir := u / speed
			var back := -push.dot(dir)
			acc += (push + dir * back) * (fast / mi)
			drag_area = lerpf(drag_area, back / (0.25 * rho * speed * speed), fast)
	var rate_water := 0.5 * drag_coefficient * drag_area * rho * speed / mi
	var ua := v - wind
	var air_speed := ua.length()
	var rate_air := 0.5 * _air_drag_coefficient(air_speed) * _area * air * (vol - under) / vol * air_speed / mi
	_rate = rate_water + rate_air
	return acc - u * rate_water - ua * rate_air


## Height of the water's surface over the world point `p`.
func _surface_at(p: Vector3) -> float:
	if water:
		return water.height_at(p)
	return water_surface.global_position.y if water_surface else water_level


## The surface's normal under the world point `p`, from its heights a radius either side.
func _surface_normal(p: Vector3) -> Vector3:
	if water == null or not water.is_wavy():
		return Vector3.UP
	var e := maxf(_radius, 0.02)
	var dx := _surface_at(p + Vector3(e, 0.0, 0.0)) - _surface_at(p - Vector3(e, 0.0, 0.0))
	var dz := _surface_at(p + Vector3(0.0, 0.0, e)) - _surface_at(p - Vector3(0.0, 0.0, e))
	return Vector3(-dx, 2.0 * e, -dz).normalized()


## How steeply the surface can slope: for this water's waves, k H for each wave.
func _wave_slope() -> float:
	if water == null or not water.is_wavy():
		return 0.0
	if water.outside_height().is_valid():
		return 0.3
	return TAU * water.wave_height / water.wave_length * (1.0 + _Water.SECOND_HEIGHT / _Water.SECOND_LENGTH)


## Emit water_entered or water_exited at `at` on the surface, and ask the water for a splash.
func _cross(entering: bool, at: Vector3, speed: float) -> void:
	if entering:
		water_entered.emit(speed, at)
	else:
		water_exited.emit(speed, at)
	if water and water.has_method(&"splash"):
		water.splash(at, speed if entering else 0.4 * speed, 2.0 * _radius)
