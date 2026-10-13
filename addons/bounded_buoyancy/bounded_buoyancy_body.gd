@icon("res://addons/bounded_buoyancy/icon.svg")
class_name BoundedBuoyancyBody
extends RigidBody3D
## Bounded Buoyancy for Godot 4.4+: buoyancy with the added mass of the submerged part
## in the body's inertia, every physics step.
##
## Buoyancy drives no fall faster than g and no rise faster than g/C, for every density (drag and
## collisions act on top); the first frame after a release is the classical value, and momentum is
## kept as the body enters the water. Buoyancy acts at the centre of the immersed volume, so bodies
## of any shape tilt, roll and right themselves: a raft settles flat, a log floats on its side, and a
## boat with a low centre of mass comes back upright.
##
## Method and benchmarks: Parkyn, N. (2026). Bounded Buoyancy: Added Mass and Two Ceilings
## for Stable, Accurate Buoyancy in Game and Simulation Engines, v1.1. Companion paper to
## ValerieX (VXXX) v2.2. Zenodo. Licence: MIT.
##
## Use: give a RigidBody3D one or more CollisionShape3D children (sphere, box, cylinder, capsule or
## convex polygon) and this script, set its density, and set the water level. For any other shape,
## such as a detailed hull, set buoyancy_mesh to a closed mesh. The script sets the body's mass,
## turns off its built-in gravity and damping, and applies weight, buoyancy, drag and the righting
## moment itself. Other forces (collisions, pushes) act as usual and are resisted by the full
## inertia, water included. Drag acts on the body's speed through the water and through the air, so
## a current carries it along and wind pushes it; neither is set moving by the body.
##
## Water: once a scene has any BoundedBuoyancyWater node, the body floats only inside one (riding
## its waves and drifting with its current). Without one, it uses water_level, or the height of the
## water_surface node. Entering or leaving the water emits water_entered or water_exited, and the
## water node draws a splash.
##
## Air: the part of the body out of the water is in air, which the same law covers with air's
## density. A helium balloon rises and a hot-air balloon lifts off when heated (change density
## while running); with air_half_height set, the air thins with height and balloons float at a level.
## A BoundedBuoyancyWind node makes the air move, and a sail (sail_area) lets the wind drive a boat.
##
## Boats: BoundedBuoyancyFoil children (keels, rudders, sails) and BoundedBuoyancyThruster children
## (propellers, fans) are applied after the buoyancy each step, each with a bounded impulse at its
## own point, so a boat holds its course, steers, sails across and into the wind, and drives.

## The body has just touched the water, at `speed` (m/s, through the water) at the world point `at`
## on the surface. Use it for splash sounds.
signal water_entered(speed: float, at: Vector3)
## The body has just come wholly out of the water.
signal water_exited(speed: float, at: Vector3)

enum Model {
	BOUNDED, ## Added mass of the submerged part in the inertia (Bounded Buoyancy).
	USUAL,   ## Buoyancy applied to the body's own mass, as most engines do. For comparison.
}

## Which buoyancy model to use. USUAL is there for side-by-side comparisons.
@export var model: Model = Model.BOUNDED
## Density of the body. The body's own mass is density x volume of its shape. It can be changed
## while running: ballast tanks, or a hot-air balloon whose air is heated (helium balloon about 0.5).
@export_range(0.01, 20000.0, 0.01, "or_greater", "suffix:kg/m³") var density: float = 500.0:
	set(value):
		density = maxf(value, 0.001)
		if volume > 0.0:
			own_mass = density * volume
			if _auto_inertia:
				inertia = _unit_inertia * density
## Added-mass coefficient C of the shape: 0.5 for a sphere, about 1 for a long cylinder
## moving side-on. Buoyancy-driven rising is capped at g/C.
@export_range(0.0, 5.0, 0.01) var added_mass_coefficient: float = 0.5
## Height of a flat water surface, in world units. Used when the scene has no BoundedBuoyancyWater
## and water_surface is not set.
@export var water_level: float = 0.0
## Optional node for the water surface: a BoundedBuoyancyWater (its surface, waves and current are
## used), or any Node3D whose global Y is a flat surface. If set, it takes priority over water nodes.
@export var water_surface: Node3D
## Density of the water (or other liquid). A BoundedBuoyancyWater uses its own density instead.
@export_range(1.0, 20000.0, 1.0, "or_greater", "suffix:kg/m³") var water_density: float = 1000.0
## Quadratic drag coefficient (0.47 for a sphere), in the water and in the air.
@export_range(0.0, 3.0, 0.01) var drag_coefficient: float = 0.47
## Damping of spin in the water, per second when fully submerged; it scales with the submerged
## fraction. It stands in for the energy that rolling and pitching lose to the water. In air it acts in
## proportion to how light the body is against the air, so balloons are steadied and stones are not.
@export_range(0.0, 20.0, 0.01, "or_greater", "suffix:1/s") var angular_drag: float = 3.0
## Keep momentum when the added mass grows as the body enters the water (von Kármán, 1929).
@export var keep_momentum_on_entry: bool = true

@export_group("Air")
## Density of the air around the body at height 0 (1.2 kg/m³ at room temperature). Balloons rise in
## it; for wood, stone or metal it changes the weight by less than half a percent. 0 leaves air out,
## as the paper's benchmarks do.
@export_range(0.0, 20.0, 0.001, "or_greater", "suffix:kg/m³") var air_density: float = 1.2
## Height over which the air's density halves, so that a balloon rises until it floats where the air
## is as dense as it is. 0 keeps the air the same at every height. Real air halves about every 6 km;
## games can use far less.
@export_range(0.0, 100000.0, 0.1, "or_greater", "suffix:m") var air_half_height: float = 0.0
## Area of a sail or flag for the wind to push on, in square metres, as well as the body itself.
## The sail turns to face the wind it feels, like a sail let out to run before the wind.
@export_range(0.0, 1000.0, 0.01, "or_greater", "suffix:m²") var sail_area: float = 0.0
## Where on the body the wind pushes the sail, in the body's own coordinates. Above the centre of
## mass the push heels the body over; a low centre of mass (ballast) holds it up.
@export var sail_centre: Vector3 = Vector3(0.0, 0.5, 0.0)

@export_group("Shape")
## Optional closed mesh used for buoyancy instead of the collision shapes, in the body's own
## coordinates: for example a simplified, watertight hull of a boat. Leave empty to use the shapes.
@export var buoyancy_mesh: Mesh
## Facets around the curve of cylinders, capsules and spheres that share a body with other
## shapes. The volume is corrected to the exact value, so a few facets are enough.
@export_range(8, 64, 1) var curve_segments: int = 16

## The body's own mass, density x volume (kg).
var own_mass: float = 0.0
## Volume of the body's shape (m³).
var volume: float = 0.0
## Submerged volume this step (m³).
var submerged_volume: float = 0.0
## Added mass of the submerged part this step (kg).
var added_mass: float = 0.0
## Acceleration produced by weight, buoyancy and drag in the last step (m/s²).
var last_acceleration: Vector3 = Vector3.ZERO
## Centre of the submerged volume this step, in world space. The water's buoyancy acts here.
var centre_of_buoyancy: Vector3 = Vector3.ZERO
## Density of the air at the body this step (kg/m³).
var air_density_here: float = 0.0
## Velocity of the wind at the body this step (m/s, world frame).
var wind_here: Vector3 = Vector3.ZERO
## The BoundedBuoyancyWater the body is in this step, if any.
var water: Node3D = null

## Use the native kernel (addons/bounded_buoyancy_native) when it is installed. The results are the
## same either way; the native kernel is many times faster for bodies crossing the surface.
static var use_native := true
static var _kernel: RefCounted = null
static var _kernel_checked := false
static var _g := 9.8
static var _g_frame := -1
const _Water := preload("res://addons/bounded_buoyancy/bounded_buoyancy_water.gd")
const _Wind := preload("res://addons/bounded_buoyancy/bounded_buoyancy_wind.gd")
const _Foil := preload("res://addons/bounded_buoyancy/bounded_buoyancy_foil.gd")
const SAIL_DRAG := 1.2     # drag coefficient of a flat sail facing the wind

var _single_sphere := false
var _radius := 0.0
var _sphere_centre := Vector3.ZERO            # body space
var _verts := PackedVector3Array()            # body-space vertices, each stored once
var _idx := PackedInt32Array()                # three vertex indices per triangle
var _tri_weight := PackedFloat32Array()       # orientation sign x exact/faceted volume, per triangle
var _depth := PackedFloat32Array()            # depth of each vertex below the surface, this step
var _centroid := Vector3.ZERO                 # body space
var _bound_radius := 0.0                      # about _centroid
var _area := 0.0
var _face_area := Vector3.ZERO                # area seen looking along each body axis, for drag
var _prev_added_mass := -1.0
var _auto_inertia := false
var _was_in_water := false
var _first_step := true
var _unit_inertia := Vector3.ZERO             # own rotational inertia per unit density
var _auto_com := false
var _set_up := false
var _warned := false
var _world: World3D
var _world_has_water := false
var _foils: Array = []                        # BoundedBuoyancyFoil nodes under the body
var _thrusters: Array = []                    # BoundedBuoyancyThruster nodes under the body


func _ready() -> void:
	if Engine.is_editor_hint():
		return
	if not _kernel_checked:
		_kernel_checked = true
		if ClassDB.class_exists(&"BoundedBuoyancyKernel"):
			_kernel = ClassDB.instantiate(&"BoundedBuoyancyKernel")
	# Shapes added after the body joins the scene are picked up on the first physics step.
	_build_shape()
	if volume > 0.0:
		_set_up_body()


## Re-read the body's collision shapes or buoyancy mesh. Call it after adding, removing or resizing
## shapes while the game runs; the mass, centre of mass and inertia follow (unless set by hand).
func rebuild_shape() -> void:
	_build_shape()
	if volume <= 0.0:
		return
	if not _set_up:
		_set_up_body()
		return
	own_mass = density * volume
	if _auto_com:
		center_of_mass = _centroid
	if _auto_inertia:
		_unit_inertia = _own_inertia(center_of_mass) / density
		inertia = _unit_inertia * density


func _set_up_body() -> void:
	_set_up = true
	_world = get_world_3d()
	own_mass = density * volume
	mass = own_mass
	gravity_scale = 0.0
	linear_damp_mode = RigidBody3D.DAMP_MODE_REPLACE
	linear_damp = 0.0
	angular_damp_mode = RigidBody3D.DAMP_MODE_REPLACE
	angular_damp = 0.0
	# Put the centre of mass at the centre of the volume, so both physics engines agree, unless
	# it has been set by hand (a lower centre of mass is how a boat is given ballast).
	if center_of_mass_mode == RigidBody3D.CENTER_OF_MASS_MODE_AUTO or _auto_com:
		_auto_com = true
		center_of_mass_mode = RigidBody3D.CENTER_OF_MASS_MODE_CUSTOM
		center_of_mass = _centroid
	# Fix the rotational inertia at the body's own value. Engines that derive inertia from mass
	# would otherwise scale it with the added mass each step.
	if inertia == Vector3.ZERO:
		_unit_inertia = _own_inertia(center_of_mass) / density
		_auto_inertia = true
		inertia = _unit_inertia * density


func _physics_process(dt: float) -> void:
	if Engine.is_editor_hint():
		return
	if not _set_up:
		_build_shape()
		if volume <= 0.0:
			if not _warned:
				_warned = true
				push_warning("BoundedBuoyancyBody '%s': needs a CollisionShape3D child (sphere, box, cylinder, capsule or convex polygon) or a closed buoyancy_mesh." % name)
			return
		_set_up_body()
	if freeze:
		_prev_added_mass = -1.0
		last_acceleration = Vector3.ZERO
		return
	var frame := Engine.get_physics_frames()
	if frame != _g_frame:
		_g = float(ProjectSettings.get_setting("physics/3d/default_gravity"))
		_g_frame = frame
	var g := _g
	var xf := global_transform
	var cw := xf * _centroid
	var rho := water_density
	var flow := Vector3.ZERO
	water = _find_water(cw)
	if water:
		rho = water.density
		flow = water.current_at(cw)
		_immerse(xf, water.height_at(cw), water)
	elif water_surface or not _world_has_water:
		_immerse(xf, water_surface.global_position.y if water_surface else water_level)
	else:
		submerged_volume = 0.0
		centre_of_buoyancy = cw
	# The rest of the body is in air, thinning with height if air_half_height is set.
	air_density_here = air_density
	if air_half_height > 0.0:
		air_density_here *= pow(0.5, maxf(cw.y / air_half_height, -10.0))
	var air_volume := volume - submerged_volume
	var displaced := rho * submerged_volume + air_density_here * air_volume   # mass of water and air displaced
	added_mass = added_mass_coefficient * displaced
	if _prev_added_mass < 0.0:
		_prev_added_mass = added_mass
	var in_water := submerged_volume > 0.0
	if _first_step:
		_was_in_water = in_water
		_first_step = false
	elif in_water != _was_in_water:
		_was_in_water = in_water
		_report_crossing(in_water, cw, flow)

	var inertial_mass := own_mass + added_mass if model == Model.BOUNDED else own_mass
	var v := linear_velocity
	if model == Model.BOUNDED and keep_momentum_on_entry and added_mass > _prev_added_mass:
		# The water taken along starts with the water's own velocity, so momentum is shared
		# relative to the water.
		v = flow + (v - flow) * ((own_mass + _prev_added_mass) / inertial_mass)

	# Density-state drive (weight minus buoyancy), then drag applied implicitly, as engines
	# apply damping. Drag acts on the body's speed through each medium: through the water (which
	# may be flowing) on the part under water, and through the air on the rest. With still water
	# and air this reproduces the reference update of the paper step for step.
	var drive := Vector3.UP * (displaced - own_mass) * g
	var wind := _Wind.total_at(cw, _world) if not _Wind.instances.is_empty() else Vector3.ZERO
	wind_here = wind
	var through_water := v - flow
	var through_air := v - wind
	var c_water := 0.5 * drag_coefficient * _drag_area(through_water, xf.basis) * rho * submerged_volume / volume \
		* through_water.length() * dt / inertial_mass
	var c_air := 0.5 * drag_coefficient * _drag_area(through_air, xf.basis) * air_density_here * air_volume / volume \
		* through_air.length() * dt / inertial_mass
	var c_sail := 0.5 * SAIL_DRAG * sail_area * air_density_here * through_air.length() * dt / inertial_mass
	var v_target := (v + drive / inertial_mass * dt + c_water * flow + (c_air + c_sail) * wind) / (1.0 + c_water + c_air + c_sail)
	var sail_target := v_target
	# Foils and thrusters, each a bounded impulse at its own point, after the buoyancy and drag.
	var turn := Vector3.ZERO
	if not (_foils.is_empty() and _thrusters.is_empty()):
		var r := _apply_appendages(v_target, xf, inertial_mass, rho, flow, wind, dt)
		v_target = r[0]
		turn = r[1]

	# The engine integrates F / mass over this step, so this force lands exactly on v_target.
	mass = inertial_mass
	var dv := v_target - linear_velocity
	apply_central_force(inertial_mass * dv / dt)
	last_acceleration = (v_target - v) / dt

	# The sail's push acts at the sail's centre, so it heels the body as well as driving it.
	if c_sail > 0.0:
		var push := c_sail * inertial_mass / dt * (wind - sail_target)
		apply_torque((xf * sail_centre - xf * center_of_mass).cross(push))
	# Righting moment: the lift of the water and of the air act at the centres of the parts they
	# displace, the weight at the centre of mass.
	if displaced > 0.0:
		var centre := ((rho - air_density_here) * submerged_volume * centre_of_buoyancy + air_density_here * volume * cw) / displaced
		var arm := centre - xf * center_of_mass
		if arm.x * arm.x + arm.z * arm.z > 1e-14:
			apply_torque(arm.cross(Vector3.UP * displaced * g))
	if turn != Vector3.ZERO:
		apply_torque(turn / dt)
	# Spin is damped by the water, and by the air in proportion to how light the body is against it.
	var damp := angular_drag * (submerged_volume + air_volume * minf(1.0, air_density_here / density)) / volume
	if absf(damp - angular_damp) > 1e-6:
		angular_damp = damp
	_prev_added_mass = added_mass


func _add_appendage(node: Node) -> void:
	var list := _foils if node is _Foil else _thrusters
	if not list.has(node):
		list.append(node)


func _remove_appendage(node: Node) -> void:
	_foils.erase(node)
	_thrusters.erase(node)


## The foils and thrusters for one step, after the buoyancy and drag that brought the body to
## velocity `v`: each gives an impulse at its own point, worked out with the motion at the end of
## the step against the inertia the body has there, so it stops the flow across a foil, or brings a
## propeller up to its speed, but never overshoots. One after another, each seeing the motion the
## ones before it left. Returns [new velocity, angular impulse about the centre of mass].
func _apply_appendages(v: Vector3, xf: Transform3D, m: float, rho: float, flow: Vector3, wind: Vector3, dt: float) -> Array:
	var w := angular_velocity
	var com := xf * center_of_mass
	var axes := xf.basis.orthonormalized()
	var angular := Vector3.ZERO
	var has_water := water != null or water_surface != null or not _world_has_water
	var level: float = water_surface.global_position.y if water_surface else water_level
	for f in _foils:
		f.force = Vector3.ZERO
		f.immersion = 0.0
		f.angle_of_attack = 0.0
		if f.area <= 0.0 or not f.is_inside_tree():
			continue
		var rest: Basis = f.rest_basis()
		var s := rest.y
		var pivot: Vector3 = f.global_position
		var in_water: bool = f.medium == _Foil.Medium.WATER
		if f.auto_trim:
			# A sail swings away from the wind it feels at its pivot.
			var air := (flow if in_water else wind) - (v + w.cross(pivot - com))
			f.angle = _Foil.trim(rest.z, s, air, f.trim_angle_of_attack, f.max_trim)
		var c: Vector3 = _turn(rest.z, s, f.angle)
		var p: Vector3 = pivot + c * float(f.effort_offset)
		var half: float = 0.5 * (float(f.span_length()) * absf(s.y) + float(f.chord_length()) * absf(c.y))
		var wet: float = _wet_fraction(p, half, has_water, level)
		var frac := wet if in_water else 1.0 - wet
		var density := rho if in_water else air_density_here
		f.immersion = frac
		if frac <= 0.0 or density <= 0.0:
			continue
		var medium := flow if in_water else wind
		var r: Vector3 = p - com
		var u := v + w.cross(r) - medium          # the foil's velocity through its medium
		var up := u - s * s.dot(u)                 # the part across the span
		var q := up.length()
		if q < 1e-9:
			continue
		var n := s.cross(c)
		var un := up.dot(n)
		var uc := up.dot(c)
		var aoa := rad_to_deg(atan2(-un, -uc))
		f.angle_of_attack = aoa - 180.0 if aoa > 90.0 else (aoa + 180.0 if aoa < -90.0 else aoa)
		var a: float = f.area * frac
		# Square to the faces: ½ ρ A (k |uc| + 1.2 |un|) un, no more than ½ ρ A 1.4 q² (stall).
		var coeff: float = _Foil.lift_slope(f.aspect_ratio) * absf(uc) + _Foil.SQUARE_ON * absf(un)
		if absf(un) > 1e-12:
			coeff = minf(coeff, _Foil.MAX_NORMAL * q * q / absf(un))
		var jn := _impulse(n, 0.5 * density * a * coeff, un, 0.0, -INF, INF, r, m, axes, dt)
		v += jn / m
		w += _inverse_inertia(r.cross(jn), axes)
		angular += r.cross(jn)
		# Friction along the chord, with the flow left after the push across it.
		var u2 := v + w.cross(r) - medium
		var jc := _impulse(c, 0.5 * density * a * _Foil.FRICTION * q, u2.dot(c), 0.0, -INF, INF, r, m, axes, dt)
		v += jc / m
		w += _inverse_inertia(r.cross(jc), axes)
		angular += r.cross(jc)
		f.force = (jn + jc) / dt
	for t in _thrusters:
		t.force = Vector3.ZERO
		t.immersion = 0.0
		if not t.is_inside_tree():
			continue
		var at: Vector3 = t.global_position
		var wet: float = _wet_fraction(at, 0.5 * maxf(t.diameter, 1e-3), has_water, level)
		var in_water: bool = t.medium == 0
		var frac := wet if in_water else 1.0 - wet
		t.immersion = frac
		var throttle: float = clampf(t.throttle, -1.0, 1.0)
		var full: float = t.max_thrust * frac
		if full <= 0.0 or throttle == 0.0 or t.max_speed <= 0.0:
			continue
		var d: Vector3 = -t.global_transform.basis.z.normalized()
		var r: Vector3 = at - com
		var ua := (v + w.cross(r) - (flow if in_water else wind)).dot(d)
		# Thrust full x (throttle - ua / max_speed): a pull towards throttle x max_speed, kept between
		# nothing and full x throttle.
		var j := _impulse(d, full / t.max_speed, ua, throttle * t.max_speed,
			minf(0.0, throttle) * full * dt, maxf(0.0, throttle) * full * dt, r, m, axes, dt)
		v += j / m
		w += _inverse_inertia(r.cross(j), axes)
		angular += r.cross(j)
		t.force = j / dt
	return [v, angular]


## Fraction of a part `half` metres tall either side of `p` that is under the water.
func _wet_fraction(p: Vector3, half: float, has_water: bool, level: float) -> float:
	if not has_water:
		return 0.0
	var depth: float = (water.height_at(p) if water else level) - p.y
	if half < 1e-6:
		return 1.0 if depth > 0.0 else 0.0
	return clampf(0.5 + 0.5 * depth / half, 0.0, 1.0)


## The impulse along unit `d` at `r` (from the centre of mass) that a pull of `coeff` newtons per
## m/s towards the speed `target` gives over dt, the speed along d there being `speed` now: worked
## out with the speed at the end of the step, against the body's inertia at that point, so it never
## carries the point past the target. Kept between lo and hi.
func _impulse(d: Vector3, coeff: float, speed: float, target: float, lo: float, hi: float, r: Vector3, m: float, axes: Basis, dt: float) -> Vector3:
	if coeff <= 0.0:
		return Vector3.ZERO
	var rd := r.cross(d)
	var inv := 1.0 / m + rd.dot(_inverse_inertia(rd, axes))
	var k := coeff * dt
	return d * clampf(k * (target - speed) / (1.0 + k * inv), lo, hi)


## The body's own inverse inertia tensor, about the centre of mass, applied to `a` (world frame).
func _inverse_inertia(a: Vector3, axes: Basis) -> Vector3:
	var l := axes.transposed() * a
	return axes * Vector3(l.x / inertia.x if inertia.x > 0.0 else 0.0, l.y / inertia.y if inertia.y > 0.0 else 0.0,
		l.z / inertia.z if inertia.z > 0.0 else 0.0)


## `v` turned by `degrees` about the unit `axis` (right-handed).
static func _turn(v: Vector3, axis: Vector3, degrees: float) -> Vector3:
	var a := deg_to_rad(degrees)
	return v * cos(a) + axis.cross(v) * sin(a) + axis * (axis.dot(v) * (1.0 - cos(a)))


## Emit water_entered or water_exited, and ask the water for a splash.
func _report_crossing(entering: bool, cw: Vector3, flow: Vector3) -> void:
	var speed := (linear_velocity - flow).length()
	var surface: float = water.height_at(cw) if water else (water_surface.global_position.y if water_surface else water_level)
	var at := Vector3(cw.x, surface, cw.z)
	if entering:
		water_entered.emit(speed, at)
	else:
		water_exited.emit(speed, at)
	if water and water.has_method(&"splash"):
		var across := 2.0 * sqrt(maxf(_face_area.y, 1e-6) / PI)
		water.splash(at, speed if entering else 0.4 * speed, across)


## Area the body shows to a flow along `along` (world frame): the faces of its bounding box, each
## seen at its angle to the flow, times how much of the box the shape fills.
func _drag_area(along: Vector3, basis: Basis) -> float:
	var n := along.length()
	if _single_sphere or n < 1e-9:
		return _area
	var d := (basis.transposed() * along).abs() / n
	return d.dot(_face_area)


## The water the body is in: water_surface if it is a BoundedBuoyancyWater, otherwise the first
## BoundedBuoyancyWater in the body's world that contains `at`.
func _find_water(at: Vector3) -> Node3D:
	_world_has_water = false
	if water_surface:
		return water_surface if water_surface is _Water else null
	for w in _Water.instances:
		if w.world != _world:
			continue
		_world_has_water = true
		if w.contains(at):
			return w
	return null


## Submerged volume and its centre for the body at `xf`, with the water surface at height `surface`
## over the body's centre. With a wavy `wavy_water`, each vertex is measured against the surface above it.
func _immerse(xf: Transform3D, surface: float, wavy_water: Node3D = null) -> void:
	# swell bounds how far the surface anywhere under the body can be from its height over the
	# centre: twice the most it rises or falls from its mean.
	var swell := 0.0
	var outside := Callable()   # another water system's surface, if the water uses one
	if wavy_water and wavy_water.is_wavy():
		swell = 2.0 * wavy_water.max_wave()
		outside = wavy_water.outside_height()
	else:
		wavy_water = null
	if _single_sphere:
		# Exact spherical cap, which is the same whichever way the sphere has turned. The sphere's
		# centre is the body's centroid, so `surface` is already the height over it.
		var c := xf * _sphere_centre
		var h := clampf(_radius + surface - c.y, 0.0, 2.0 * _radius)
		submerged_volume = PI * h * h * (3.0 * _radius - h) / 3.0
		var drop := 0.0 if h <= 0.0 else 3.0 * pow(2.0 * _radius - h, 2) / (4.0 * (3.0 * _radius - h))
		centre_of_buoyancy = c - Vector3.UP * drop
		return
	var cw := xf * _centroid
	if cw.y - _bound_radius >= surface + swell:
		submerged_volume = 0.0
		centre_of_buoyancy = cw
		return
	if cw.y + _bound_radius <= surface - swell:
		submerged_volume = volume
		centre_of_buoyancy = cw
		return
	# Work in the body's own coordinates: only the depth of each vertex needs the transform.
	# Each triangle is cut at the surface, and the part below makes tetrahedra with a point p on
	# the surface. The cut face lies in the surface plane, so it adds nothing: the sum is the exact
	# volume below the surface, and its first moment gives the centre of buoyancy.
	var up := Vector3(xf.basis.x.y, xf.basis.y.y, xf.basis.z.y)   # world height per unit of each body axis
	var h := surface - xf.origin.y
	var p := _centroid + up * ((h - up.dot(_centroid)) / up.length_squared())
	if _kernel and use_native and not outside.is_valid():
		var r: Vector4 = _kernel.clip_waves(_verts, _idx, _tri_weight, xf, wavy_water.inverse_transform(), wavy_water.wave_params(), p) \
			if wavy_water else _kernel.clip_flat(_verts, _idx, _tri_weight, up, h, p)
		submerged_volume = clampf(r.x, 0.0, volume)
		centre_of_buoyancy = xf * (Vector3(r.y, r.z, r.w) / (4.0 * r.x)) if r.x > 1e-12 else cw
		return
	if outside.is_valid():
		# Another water system's surface: ask it for the height over each vertex.
		for j in _verts.size():
			var wp := xf * _verts[j]
			_depth[j] = float(outside.call(wp)) - wp.y
	elif wavy_water:
		# Waves: measure each vertex against the surface above it. The cut face then follows the
		# waves, which is accurate while the waves are long compared with the body.
		for j in _verts.size():
			var wp := xf * _verts[j]
			_depth[j] = wavy_water.height_at(wp) - wp.y
	else:
		for j in _verts.size():
			_depth[j] = h - up.dot(_verts[j])
	var vol := 0.0
	var moment := Vector3.ZERO
	var nt := _idx.size()
	var i := 0
	while i < nt:
		var ia := _idx[i]
		var ib := _idx[i + 1]
		var ic := _idx[i + 2]
		var w := _tri_weight[i / 3]
		i += 3
		var da := _depth[ia]
		var db := _depth[ib]
		var dc := _depth[ic]
		var under := int(da > 0.0) + int(db > 0.0) + int(dc > 0.0)
		if under == 0:
			continue
		var a := _verts[ia]
		var b := _verts[ib]
		var c := _verts[ic]
		if under == 3:
			var t3 := w * (a - p).dot((b - p).cross(c - p)) / 6.0
			vol += t3
			moment += t3 * (p + a + b + c)
			continue
		# Turn the triangle (keeping its winding) so that the odd vertex out comes first.
		if (under == 1 and db > 0.0) or (under == 2 and db <= 0.0):
			var tv := a; a = b; b = c; c = tv
			var td := da; da = db; db = dc; dc = td
		elif (under == 1 and dc > 0.0) or (under == 2 and dc <= 0.0):
			var tv := c; c = b; b = a; a = tv
			var td := dc; dc = db; db = da; da = td
		var ab := a + (b - a) * (da / (da - db))
		var ca := c + (a - c) * (dc / (dc - da))
		if under == 1:
			var t1 := w * (a - p).dot((ab - p).cross(ca - p)) / 6.0
			vol += t1
			moment += t1 * (p + a + ab + ca)
		else:
			var t2a := w * (ab - p).dot((b - p).cross(c - p)) / 6.0
			var t2b := w * (ab - p).dot((c - p).cross(ca - p)) / 6.0
			vol += t2a + t2b
			moment += t2a * (p + ab + b + c) + t2b * (p + ab + c + ca)
	submerged_volume = clampf(vol, 0.0, volume)
	centre_of_buoyancy = xf * (moment / (4.0 * vol)) if vol > 1e-12 else cw


func _build_shape() -> void:
	_verts = PackedVector3Array()
	_idx = PackedInt32Array()
	_tri_weight = PackedFloat32Array()
	_single_sphere = false
	volume = 0.0
	var shapes := []
	for child in get_children():
		if child is CollisionShape3D and child.shape != null and not child.disabled:
			shapes.append([child.shape, child.transform])
	if buoyancy_mesh == null and shapes.size() == 1 and shapes[0][0] is SphereShape3D:
		var xf: Transform3D = shapes[0][1]
		_single_sphere = true
		_radius = shapes[0][0].radius * xf.basis.get_scale().x
		_sphere_centre = xf.origin
		volume = 4.0 / 3.0 * PI * pow(_radius, 3)
		_centroid = _sphere_centre
		_bound_radius = _radius
		_area = PI * _radius * _radius
		_face_area = Vector3.ONE * _area
		return
	if buoyancy_mesh != null:
		_add_faces(buoyancy_mesh.get_faces(), Transform3D.IDENTITY, -1.0)
	else:
		for s in shapes:
			var faces := PackedVector3Array()
			var exact := -1.0
			var shape: Shape3D = s[0]
			if shape is BoxShape3D:
				var m := BoxMesh.new()
				m.size = shape.size
				faces = m.get_faces()
				exact = shape.size.x * shape.size.y * shape.size.z
			elif shape is CylinderShape3D:
				var m := CylinderMesh.new()
				m.top_radius = shape.radius
				m.bottom_radius = shape.radius
				m.height = shape.height
				m.radial_segments = curve_segments
				m.rings = 0
				faces = m.get_faces()
				exact = PI * shape.radius * shape.radius * shape.height
			elif shape is CapsuleShape3D:
				var m := CapsuleMesh.new()
				m.radius = shape.radius
				m.height = shape.height
				m.radial_segments = curve_segments
				m.rings = 1
				faces = m.get_faces()
				exact = PI * shape.radius * shape.radius * (shape.height - 2.0 * shape.radius) + 4.0 / 3.0 * PI * pow(shape.radius, 3)
			elif shape is SphereShape3D:
				var m := SphereMesh.new()
				m.radius = shape.radius
				m.height = 2.0 * shape.radius
				m.radial_segments = curve_segments
				m.rings = maxi(4, curve_segments / 4)
				faces = m.get_faces()
				exact = 4.0 / 3.0 * PI * pow(shape.radius, 3)
			elif shape is ConvexPolygonShape3D:
				# A fresh copy, so that the shape's own debug settings cannot turn its faces into outlines.
				var hull := ConvexPolygonShape3D.new()
				hull.points = shape.points
				faces = hull.get_debug_mesh().get_faces()
			else:
				push_warning("BoundedBuoyancyBody '%s': %s is not supported; set buoyancy_mesh instead." % [name, shape.get_class()])
				continue
			_add_faces(faces, s[1], exact)
	if _idx.is_empty() or volume <= 0.0:
		volume = 0.0
		return
	_depth.resize(_verts.size())
	var moment := Vector3.ZERO
	for i in range(0, _idx.size(), 3):
		var a := _verts[_idx[i]]
		var b := _verts[_idx[i + 1]]
		var c := _verts[_idx[i + 2]]
		var t := _tri_weight[i / 3] * a.dot(b.cross(c)) / 6.0
		moment += t * (a + b + c) * 0.25
	_centroid = moment / volume
	var lo := _verts[0]
	var hi := _verts[0]
	_bound_radius = 0.0
	for v in _verts:
		lo = lo.min(v)
		hi = hi.max(v)
		_bound_radius = maxf(_bound_radius, v.distance_to(_centroid))
	var size := hi - lo
	var fill := clampf(volume / (size.x * size.y * size.z), 0.3, 1.0)
	_face_area = Vector3(size.y * size.z, size.x * size.z, size.x * size.y) * fill
	_area = _face_area.y


## Adds a closed set of triangles (shape coordinates) placed by `xf`. With `exact` > 0 the faceted
## volume is scaled to that value; the sign corrects for the winding of the triangles.
func _add_faces(faces: PackedVector3Array, xf: Transform3D, exact: float) -> void:
	var faceted := 0.0
	for i in range(0, faces.size() - 2, 3):
		faceted += faces[i].dot(faces[i + 1].cross(faces[i + 2])) / 6.0
	if absf(faceted) < 1e-12:
		push_warning("BoundedBuoyancyBody '%s': a buoyancy shape has no volume; is the mesh closed?" % name)
		return
	var det := xf.basis.determinant()
	var w := signf(faceted * det) * (exact / absf(faceted) if exact > 0.0 else 1.0)
	var index := {}
	for v in faces:
		var key: Vector3 = xf * v
		if not index.has(key):
			index[key] = _verts.size()
			_verts.append(key)
	for i in range(0, faces.size() - 2, 3):
		_idx.append(index[xf * faces[i]])
		_idx.append(index[xf * faces[i + 1]])
		_idx.append(index[xf * faces[i + 2]])
		_tri_weight.append(w)
	volume += absf(faceted) * absf(det) * absf(w)


## Principal moments of inertia of the body's own mass about `com` (body axes).
func _own_inertia(com: Vector3) -> Vector3:
	if _single_sphere:
		var d := _sphere_centre - com
		var i0 := 0.4 * own_mass * _radius * _radius
		return Vector3(i0 + own_mass * (d.y * d.y + d.z * d.z), i0 + own_mass * (d.x * d.x + d.z * d.z), i0 + own_mass * (d.x * d.x + d.y * d.y))
	var sxx := 0.0
	var syy := 0.0
	var szz := 0.0
	for i in range(0, _idx.size(), 3):
		var a := _verts[_idx[i]] - com
		var b := _verts[_idx[i + 1]] - com
		var c := _verts[_idx[i + 2]] - com
		var v := _tri_weight[i / 3] * a.dot(b.cross(c)) / 6.0
		sxx += v / 10.0 * (a.x * a.x + b.x * b.x + c.x * c.x + a.x * b.x + a.x * c.x + b.x * c.x)
		syy += v / 10.0 * (a.y * a.y + b.y * b.y + c.y * c.y + a.y * b.y + a.y * c.y + b.y * c.y)
		szz += v / 10.0 * (a.z * a.z + b.z * b.z + c.z * c.z + a.z * b.z + a.z * c.z + b.z * c.z)
	return density * Vector3(syy + szz, sxx + szz, sxx + syy)
