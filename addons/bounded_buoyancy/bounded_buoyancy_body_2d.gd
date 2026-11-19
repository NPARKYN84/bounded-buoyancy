@tool
@icon("res://addons/bounded_buoyancy/icon.svg")
class_name BoundedBuoyancyBody2D
extends RigidBody2D
## Bounded Buoyancy for 2D games: buoyancy with the added mass of the submerged part in the body's
## inertia, every physics step, for RigidBody2D.
##
## The same method as BoundedBuoyancyBody, in a side view: buoyancy drives no fall faster than g and
## no rise faster than g/C, the first frame after a release is the classical value, and buoyancy acts
## at the centre of the immersed area, so bodies tip, roll and right themselves. Out of the water the
## body is in air; water nodes can flow and have waves, wind nodes blow, and entries make splashes.
##
## Use: give a RigidBody2D one or more CollisionShape2D children (circle, rectangle, capsule or
## convex polygon) or CollisionPolygon2D children, attach this script and set its density. Then add a
## BoundedBuoyancyWater2D node, or set water_level (the y of a flat surface, in pixels). Lengths and
## speeds are in pixels, as everywhere in Godot 2D; densities are in kg/m³ as in 3D. pixels_per_metre
## and thickness only turn areas into masses in kilograms, so that collisions with other bodies are fair.
##
## Method and benchmarks: Parkyn, N. (2026). Bounded Buoyancy: Added Mass and Two Ceilings for Stable,
## Accurate Buoyancy in Game and Simulation Engines, v1.1. Licence: MIT.

## The body has just touched the water, at `speed` (px/s, through the water), at the point `at` on the surface.
signal water_entered(speed: float, at: Vector2)
## The body has just come wholly out of the water.
signal water_exited(speed: float, at: Vector2)

enum Model {
	BOUNDED, ## Added mass of the submerged part in the inertia (Bounded Buoyancy).
	USUAL,   ## Buoyancy applied to the body's own mass, as most engines do. For comparison.
}

## Which buoyancy model to use. USUAL is there for side-by-side comparisons.
@export var model: Model = Model.BOUNDED
## Density of the body in kg/m³ (water is 1000). It can be changed while running.
@export_range(0.01, 20000.0, 0.01, "or_greater", "suffix:kg/m³") var density: float = 500.0:
	set(value):
		density = maxf(value, 0.001)
		# Set by hand: no longer a listed material.
		if not _from_material and made_of != BoundedBuoyancyMaterials.Kind.CUSTOM \
				and not is_equal_approx(density, BoundedBuoyancyMaterials.density_of(made_of)):
			made_of = BoundedBuoyancyMaterials.Kind.CUSTOM
		if area > 0.0:
			own_mass = density * area * _kg
			if _auto_inertia:
				inertia = _unit_inertia * density
		if Engine.is_editor_hint():
			queue_redraw()   # the waterline preview
			notify_property_list_changed()
			if is_inside_tree():
				update_configuration_warnings()
## What the body is made of: pick one and it takes that material's density (oak 720 kg/m³ floats,
## steel 7850 sinks), so it floats or sinks as the real thing does. Custom: set the density yourself.
## (Setting the density by hand makes it Custom.)
@export var made_of: BoundedBuoyancyMaterials.Kind = BoundedBuoyancyMaterials.Kind.CUSTOM:
	set(value):
		made_of = value
		if value != BoundedBuoyancyMaterials.Kind.CUSTOM:
			_from_material = true
			density = BoundedBuoyancyMaterials.density_of(value)
			_from_material = false
		notify_property_list_changed()
## Added-mass coefficient C of the shape in 2D: 1 for a circle, more for a flat plate moving face-on.
## Buoyancy-driven rising is capped at g/C.
@export_range(0.0, 5.0, 0.01) var added_mass_coefficient: float = 1.0
@export_group("Water")
## Height (y, in pixels) of a flat water surface, used when the scene has no BoundedBuoyancyWater2D.
@export var water_level: float = 0.0
## Density of the water (a BoundedBuoyancyWater2D uses its own).
@export_range(1.0, 20000.0, 1.0, "or_greater", "suffix:kg/m³") var water_density: float = 1000.0
@export_group("Drag and settling")
## Quadratic drag coefficient, in the water and in the air (about 1 for a circle in 2D).
@export_range(0.0, 3.0, 0.01) var drag_coefficient: float = 1.0
## Damping of spin in the water, per second when fully submerged; in air, in proportion to how light
## the body is against the air.
@export_range(0.0, 20.0, 0.01, "or_greater", "suffix:1/s") var angular_drag: float = 3.0
## Turning moves water too: a body that is not round, tipping or turning, pushes water aside, so it
## turns as if heavier than it is (Lamb's added inertia for its shape; nothing for a circle). On by
## default; off turns it on its own inertia alone, as before 2.8.
@export var added_turning := true
## Keep momentum when the added mass grows as the body enters the water.
@export var keep_momentum_on_entry: bool = true
## Wave-making damping: a body bobbing at the surface sends out ripples (here to the left and right)
## that carry its energy away, so it settles: within a few bobs if it is wide and shallow (a raft, a
## boat), slowly if it is deep and narrow, as linear wave theory gives it. It acts on bobbing relative
## to the surface, so riding waves is not held back. 1 is the physics; 0 leaves it out; other values
## scale it. Bounded model only.
@export_range(0.0, 3.0, 0.01) var wave_damping: float = 1.0

@export_group("Air")
## Density of the air (1.2 kg/m³). 0 leaves air out.
@export_range(0.0, 20.0, 0.001, "or_greater", "suffix:kg/m³") var air_density: float = 1.2
## Height in pixels over which the air's density halves, measured up from y = 0. 0 keeps it the same.
@export_range(0.0, 1000000.0, 1.0, "or_greater", "suffix:px") var air_half_height: float = 0.0
## Length of a sail for the wind to push on, in pixels (0 for none).
@export_range(0.0, 10000.0, 0.1, "or_greater", "suffix:px") var sail_size: float = 0.0
## Where the wind pushes the sail, in the body's own coordinates.
@export var sail_centre: Vector2 = Vector2(0.0, -40.0)

@export_group("Scale")
## Pixels per metre, used only to turn areas into kilograms (Godot's 2D gravity of 980 px/s² is 9.8 m/s² at 100).
@export_range(1.0, 10000.0, 0.1, "or_greater") var pixels_per_metre: float = 100.0
## Depth of the body into the screen in metres, used only to turn areas into kilograms.
@export_range(0.001, 100.0, 0.001, "or_greater", "suffix:m") var thickness: float = 0.2

## The body's own mass (kg).
var own_mass: float = 0.0
## Area of the body's shape (px²).
var area: float = 0.0
## Area under water this step (px²).
var submerged_area: float = 0.0
## Added mass of the water and air taken along this step (kg).
var added_mass: float = 0.0
## Mass of water in the body's compartments (BoundedBuoyancyCompartment2D), kg: weight it carries.
var flood_mass: float = 0.0
## Acceleration from weight, buoyancy and drag in the last step (px/s²).
var last_acceleration: Vector2 = Vector2.ZERO
## Centre of the area under water this step, in global coordinates.
var centre_of_buoyancy: Vector2 = Vector2.ZERO
## The BoundedBuoyancyWater2D the body is in this step, if any.
var water: Node2D = null
## Velocity of the wind at the body this step (px/s).
var wind_here: Vector2 = Vector2.ZERO
## Wave-making damping of bobbing this step, N per m/s (0 when the body is not at the surface).
var heave_damping: float = 0.0
## Acceleration of the water in the waves at the body this step, along +y (downwards, px/s²), which pushes it.
var wave_acceleration: float = 0.0

const _Water2D := preload("res://addons/bounded_buoyancy/bounded_buoyancy_water_2d.gd")
const _Wind2D := preload("res://addons/bounded_buoyancy/bounded_buoyancy_wind_2d.gd")
const SAIL_DRAG := 1.2
const TELEPORT_DISTANCE := 0.1   # a body this far from where its velocity would have carried it, or water whose level moves this far in a step, has been moved (m)

var _kg := 0.0                          # kilograms per px² of area at unit density
var _circle := false
var _radius := 0.0
var _circle_centre := Vector2.ZERO
var _polys: Array[PackedVector2Array] = []
var _poly_scale := PackedFloat32Array()  # exact / polygon area, per polygon
var _centroid := Vector2.ZERO
var _bound_radius := 0.0
var _box := Vector2.ZERO                 # bounding box size, body space
var _fill := 1.0
var _auto_inertia := false
var _from_material := false                 # density being set from Made Of
var _unit_inertia := 0.0
var _turning_added := 0.0               # the water's share in turning (engine units), eased
var _compartments: Array = []           # BoundedBuoyancyCompartment2D under it
var _flood_inertia := 0.0               # the flood water's share of the inertia (engine units), this step
var _flood_torque := 0.0                # the flood water's weight about the centre of mass, this step
var _prev_flood := 0.0
var _extra_on := false                  # the inertia includes it
var _dry_inertia := 0.0                 # the inertia without it, when set by hand
var _wet_inertia := 0.0                 # the inertia as last set with it
var _prev_added_mass := -1.0
var _was_in_water := false
var _first_step := true
var _auto_com := false
var _set_up := false
var _warned := false
var _world: World2D
var _world_has_water := false
var _prev_surface := NAN                 # height of the surface under the body last step
var _prev_water: Node2D = null           # the water it was in then
var _prev_level := 0.0                   # that water's level
var _prev_com := Vector2.ZERO            # where its centre of mass was
var _prev_frame := -1                    # and which physics frame that was


func _notification(what: int) -> void:
	if what == NOTIFICATION_TRANSFORM_CHANGED and Engine.is_editor_hint() and not _check_queued:
		_check_queued = true
		(func() -> void:
			_check_queued = false
			if is_inside_tree():
				update_configuration_warnings()).call_deferred()


var _check_queued := false


func _ready() -> void:
	if Engine.is_editor_hint():
		set_notify_transform(true)
		# In the editor: keep the warning on the node up to date as shapes are added and taken away.
		child_entered_tree.connect(func(_n: Node) -> void: update_configuration_warnings.call_deferred())
		child_exiting_tree.connect(func(_n: Node) -> void: update_configuration_warnings.call_deferred())
		return
	_build_shape()
	if area > 0.0:
		_set_up_body()


## Where the body would float in still water as it is placed now, its tilt kept (it may then tip
## over). Works in the editor, and is what the waterline drawn on a body there comes from. Returns {}
## if it has no shape, else {"sinks", "fraction": share of it under water, "level": the waterline's
## height (global y, pixels, with the body where it is now), "draft": how deep it floats below that
## (m), "water_density"}. Air is left out.
func floating_preview() -> Dictionary:
	_build_shape()
	if area <= 0.0:
		return {}
	var rho := water_density
	for w in _Water2D.instances:
		if is_instance_valid(w) and w.is_inside_tree() and w.world == get_world_2d():
			rho = w.density
			break
	var xf := global_transform
	var top := INF
	var bottom := -INF
	if _circle:
		var c := xf * _circle_centre
		top = c.y - _radius
		bottom = c.y + _radius
	else:
		for poly in _polys:
			for q in poly:
				var y := (xf * q).y
				top = minf(top, y)
				bottom = maxf(bottom, y)
	var ppm := pixels_per_metre
	var fraction := density / rho
	if fraction >= 1.0:
		return {"sinks": true, "fraction": 1.0, "level": top, "draft": (bottom - top) / ppm, "water_density": rho}
	# The area under a level grows as the level rises (y falls): find the one that holds the body's
	# weight. The flat Water Level is borrowed for the measuring and put back.
	var keep := [water_level, submerged_area, centre_of_buoyancy]
	var want := fraction * area
	var lo := top
	var hi := bottom
	for i in 40:
		var mid := 0.5 * (lo + hi)
		water_level = mid
		_immerse(xf, null)
		if submerged_area < want:
			hi = mid
		else:
			lo = mid
	water_level = keep[0]
	submerged_area = keep[1]
	centre_of_buoyancy = keep[2]
	var level := 0.5 * (lo + hi)
	return {"sinks": false, "fraction": fraction, "level": level, "draft": (bottom - level) / ppm, "water_density": rho}


func _get_property_list() -> Array[Dictionary]:
	if not Engine.is_editor_hint():
		return []
	return [{"name": "Floating preview", "type": TYPE_NIL, "usage": PROPERTY_USAGE_GROUP, "hint_string": "floating_"},
		{"name": "floating_preview_text", "type": TYPE_STRING, "usage": PROPERTY_USAGE_EDITOR | PROPERTY_USAGE_READ_ONLY}]


func _get(property: StringName) -> Variant:
	if property == &"floating_preview_text":
		var p := floating_preview()
		if p.is_empty():
			return "Add a shape to see where it floats."
		if p.sinks:
			return "Sinks: denser than the water (%.0f kg/m³)." % p.water_density
		return "Floats %.0f%% under, %.2f m deep, as placed." % [100.0 * p.fraction, p.draft]
	return null


var _preview_clock := 0.0


func _process(dt: float) -> void:
	if not Engine.is_editor_hint():
		set_process(false)
		return
	# In the editor: redraw the waterline now and then, as shapes are moved and resized.
	_preview_clock += dt
	if _preview_clock >= 0.25:
		_preview_clock = 0.0
		queue_redraw()


func _draw() -> void:
	if not Engine.is_editor_hint():
		return
	var p := floating_preview()
	if p.is_empty():
		return
	var font := ThemeDB.fallback_font
	var inv := global_transform.affine_inverse()
	if p.sinks:
		draw_string(font, inv * (global_transform * _centroid) + Vector2(-20, 5), "sinks", HORIZONTAL_ALIGNMENT_LEFT, -1, 14, Color(0.9, 0.3, 0.2))
		return
	var level: float = p.level
	# Where the level crosses the shape's outline.
	var x0 := INF
	var x1 := -INF
	var xf := global_transform
	if _circle:
		var c := xf * _circle_centre
		var q := _radius * _radius - (level - c.y) * (level - c.y)
		if q > 0.0:
			x0 = c.x - sqrt(q)
			x1 = c.x + sqrt(q)
	else:
		for poly in _polys:
			for j in poly.size():
				var a := xf * poly[j]
				var b := xf * poly[(j + 1) % poly.size()]
				if (a.y < level) != (b.y < level):
					var x := a.x + (b.x - a.x) * (level - a.y) / (b.y - a.y)
					x0 = minf(x0, x)
					x1 = maxf(x1, x)
	if x0 >= x1:
		return
	var colour := Color(0.15, 0.6, 1.0, 0.9)
	draw_dashed_line(inv * Vector2(x0, level), inv * Vector2(x1, level), colour, 2.0, 6.0)
	draw_string(font, inv * Vector2(x1 + 6.0, level + 5.0), "%.0f%% under" % (100.0 * p.fraction), HORIZONTAL_ALIGNMENT_LEFT, -1, 14, colour)


var _confined_mine := 0.0   # the area this body pushed aside last step in confined water (px²)


## In confined water, the level this body sees includes the rise it makes itself, found here so that
## what it pushes aside and the rise it makes agree at the end of the step (secant steps), as in
## BoundedBuoyancyBody: a body nearly filling a tank neither lags its own rise nor chases it.
func _confined_immerse(xf: Transform2D, w: Node2D) -> void:
	var width: float = w.size.x
	if width <= 0.0:
		return
	var keep := water_level
	var surface: float = w.height_at(xf.origin.x)
	var f := func(d: float) -> float:
		water_level = surface - d   # raised by d (y points down)
		_immerse(xf, null)
		return d - (submerged_area - _confined_mine) / width
	var d0 := 0.0
	var f0: float = f.call(d0)
	var d1 := -f0
	for i in 6:
		var f1: float = f.call(d1)
		if absf(f1) < 1e-7 or absf(f1 - f0) < 1e-12:
			break
		var d2 := d1 - f1 * (d1 - d0) / (f1 - f0)
		d0 = d1
		f0 = f1
		d1 = d2
	water_level = surface - d1
	_immerse(xf, null)
	water_level = keep


## Warnings shown on the node in the editor: what it needs to float.
func _get_configuration_warnings() -> PackedStringArray:
	var w := PackedStringArray()
	var usable := 0
	var other := 0
	for c in get_children():
		if c is CollisionShape2D and c.shape != null and not c.disabled:
			if c.shape is RectangleShape2D or c.shape is CircleShape2D or c.shape is CapsuleShape2D or c.shape is ConvexPolygonShape2D:
				usable += 1
			else:
				other += 1
		elif c is CollisionPolygon2D and not c.disabled and c.polygon.size() >= 3:
			usable += 1
	if usable == 0 and other == 0:
		w.append("Add a CollisionShape2D (rectangle, circle, capsule or convex polygon) or a CollisionPolygon2D child: the body floats by the shape of what it is.")
	elif other > 0:
		w.append("Only rectangles, circles, capsules and convex polygons (and CollisionPolygon2D) are floated: other shapes are left out.")
	if w.is_empty() and is_inside_tree():
		w.append_array(behaviour_check())
	return w


## Will it behave? As BoundedBuoyancyBody's: a size that is probably a slip of scale, a body not over
## any water, a boat (a body with its own centre of mass, a sail or compartments) that will capsize, and
## a body that bobs faster than the physics steps can follow. Shown as warnings on the node in the editor.
func behaviour_check() -> PackedStringArray:
	var out := PackedStringArray()
	var p := floating_preview()
	if p.is_empty():
		return out
	var ppm := pixels_per_metre
	var size := _box * global_transform.get_scale().abs() / ppm
	var biggest := maxf(size.x, size.y)
	var smallest := minf(size.x, size.y)
	if biggest > 500.0:
		out.append("It is %.0f m across at %.0f pixels a metre: is that the size you meant? Check Pixels Per Metre." % [biggest, ppm])
	elif smallest < 0.002:
		out.append("It is only %.1f mm across at its thinnest at %.0f pixels a metre: is that the size you meant? Check Pixels Per Metre." % [1000.0 * smallest, ppm])
	var here := global_position
	var any := false
	var over := false
	for wn in _Water2D.instances:
		if is_instance_valid(wn) and wn.is_inside_tree() and wn.world == get_world_2d():
			any = true
			if wn.contains(here):
				over = true
				break
	if any and not over:
		out.append("It is not over any BoundedBuoyancyWater2D: once the game runs it will fall, as nothing holds it up. Move it over the water, or the water under it.")
	if p.sinks:
		return out
	var ticks := float(ProjectSettings.get_setting("physics/common/physics_ticks_per_second", 60))
	var g := float(ProjectSettings.get_setting("physics/2d/default_gravity", 980.0)) / ppm
	var rho: float = p.water_density
	var level: float = p.level
	var xf := global_transform
	var e := maxf(0.01 * float(p.draft) * ppm, 1e-4)
	var waterline := (_area_below(xf, level - e) - _area_below(xf, level + e)) / (2.0 * e) / ppm   # m
	if waterline > 0.0:
		var under := float(p.fraction) * area / (ppm * ppm)
		var m := (density * area / (ppm * ppm) + added_mass_coefficient * rho * under) * thickness
		var period := TAU * sqrt(m / (rho * g * waterline * thickness))
		if period < 5.0 / ticks:
			var need := 60
			while float(need) * period < 5.0 and need < 1920:
				need *= 2
			out.append("It bobs %.0f times a second, too fast for %d physics steps a second to follow smoothly: it may jitter. Set Physics Ticks Per Second to %d or more, or make it bigger." % [1.0 / period, int(ticks), need])
	if _is_boat_2d():
		var com := center_of_mass if center_of_mass_mode == RigidBody2D.CENTER_OF_MASS_MODE_CUSTOM and not _auto_com else _centroid
		var gm := _metacentric_height_2d(xf, level, com) / ppm
		if gm < 0.0:
			out.append("It will capsize: as placed, its centre of mass is %.2f m too high for it to stay upright. Lower its Center of Mass (ballast) by at least that, or make it wider." % -gm)
	return out


func _is_boat_2d() -> bool:
	return (center_of_mass_mode == RigidBody2D.CENTER_OF_MASS_MODE_CUSTOM and not _auto_com) or sail_size > 0.0 \
		or not find_children("*", "BoundedBuoyancyCompartment2D", true, false).is_empty()


## Area (px²) of the body at `xf` below the global height `level` (y down), and its centre.
func _area_below(xf: Transform2D, level: float) -> float:
	var keep := [water_level, submerged_area, centre_of_buoyancy]
	water_level = level
	_immerse(xf, null)
	var a := submerged_area
	water_level = keep[0]
	submerged_area = keep[1]
	centre_of_buoyancy = keep[2]
	return a


## The metacentric height (px) for heeling 3° either way, with the centre of mass at `com` (body space),
## floating as at `level`. Negative: it capsizes.
func _metacentric_height_2d(xf: Transform2D, level: float, com: Vector2) -> float:
	var target := _area_below(xf, level)
	var g_world := xf * com
	var worst := INF
	var keep := [water_level, submerged_area, centre_of_buoyancy]
	for sgn in [-1.0, 1.0]:
		var phi: float = deg_to_rad(3.0) * float(sgn)
		# Turned by phi about the centre of mass.
		var xf1 := Transform2D(0.0, g_world) * Transform2D(phi, Vector2.ZERO) * Transform2D(0.0, -g_world) * xf
		var lo := level - _bound_radius - 100.0
		var hi := level + _bound_radius + 100.0
		for i in 50:
			var mid := 0.5 * (lo + hi)
			if _area_below(xf1, mid) < target:
				hi = mid   # more under as the level rises (y falls)
			else:
				lo = mid
		water_level = 0.5 * (lo + hi)
		_immerse(xf1, null)
		var b := centre_of_buoyancy
		# Buoyancy (upwards: -y) at b; its turning about the centre of mass, against the heel.
		var torque := (b - g_world).cross(Vector2(0.0, -1.0))
		var gm := -torque / sin(phi)
		worst = minf(worst, gm)
	water_level = keep[0]
	submerged_area = keep[1]
	centre_of_buoyancy = keep[2]
	return worst


## Re-read the body's collision shapes. Call it after adding, removing or resizing shapes while the
## game runs; the mass, centre of mass and inertia follow (unless set by hand).
func rebuild_shape() -> void:
	_build_shape()
	if area <= 0.0:
		return
	if not _set_up:
		_set_up_body()
		return
	own_mass = density * area * _kg
	if _auto_com:
		center_of_mass = _centroid
	if _auto_inertia:
		_unit_inertia = _own_inertia(center_of_mass) / density
		inertia = _unit_inertia * density


func _set_up_body() -> void:
	_set_up = true
	_world = get_world_2d()
	_kg = thickness / (pixels_per_metre * pixels_per_metre)
	own_mass = density * area * _kg
	mass = own_mass
	gravity_scale = 0.0
	linear_damp_mode = RigidBody2D.DAMP_MODE_REPLACE
	linear_damp = 0.0
	angular_damp_mode = RigidBody2D.DAMP_MODE_REPLACE
	angular_damp = 0.0
	if center_of_mass_mode == RigidBody2D.CENTER_OF_MASS_MODE_AUTO or _auto_com:
		_auto_com = true
		center_of_mass_mode = RigidBody2D.CENTER_OF_MASS_MODE_CUSTOM
		center_of_mass = _centroid
	if inertia == 0.0:
		_unit_inertia = _own_inertia(center_of_mass) / density
		_auto_inertia = true
		inertia = _unit_inertia * density


func _physics_process(dt: float) -> void:
	if Engine.is_editor_hint():
		return
	if not _set_up:
		_build_shape()
		if area <= 0.0:
			if not _warned:
				_warned = true
				push_warning("BoundedBuoyancyBody2D '%s': needs a CollisionShape2D (circle, rectangle, capsule, convex polygon) or a CollisionPolygon2D." % name)
			return
		_set_up_body()
	if freeze:
		_prev_added_mass = -1.0
		_prev_flood = 0.0
		_prev_surface = NAN
		last_acceleration = Vector2.ZERO
		return
	var g := float(ProjectSettings.get_setting("physics/2d/default_gravity"))
	var xf := global_transform
	var cw := xf * _centroid
	var rho := water_density
	var flow := Vector2.ZERO
	water = _find_water(cw)
	if water:
		rho = water.density
		flow = water.current_at(cw)
		_immerse(xf, water)
		if water.confined and water.wave_height <= 0.0:
			_confined_immerse(xf, water)
	elif not _world_has_water:
		_immerse(xf, null)
	else:
		submerged_area = 0.0
		centre_of_buoyancy = cw
	var rho_air := air_density
	if air_half_height > 0.0:
		rho_air *= pow(0.5, maxf(-cw.y / air_half_height, -10.0))
	var air_area := area - submerged_area
	var displaced := (rho * submerged_area + rho_air * air_area) * _kg   # kg of water and air displaced
	added_mass = added_mass_coefficient * displaced
	# Water in the compartments: weight the body carries, at the water's own centre.
	flood_mass = _flood(xf, rho, g, dt) if not _compartments.is_empty() else 0.0
	if water and water.confined:
		water.displace(submerged_area - flood_mass / rho * pixels_per_metre * pixels_per_metre / thickness)   # confined water rises with what it holds
		_confined_mine = submerged_area
	else:
		_confined_mine = 0.0
	if _compartments.is_empty():
		_flood_inertia = 0.0
		_flood_torque = 0.0
	if _prev_added_mass < 0.0:
		_prev_added_mass = added_mass
		_prev_flood = flood_mass
	var in_water := submerged_area > 0.0
	if _first_step:
		_was_in_water = in_water
		_first_step = false
	elif in_water != _was_in_water:
		_was_in_water = in_water
		_report_crossing(in_water, cw, flow)

	var carried := own_mass + flood_mass   # what the body weighs: itself and the water inside it
	var inertial_mass := carried + added_mass if model == Model.BOUNDED else carried
	var v := linear_velocity
	if model == Model.BOUNDED and keep_momentum_on_entry:
		# Water taken along, or let in, starts with the water's own velocity, so momentum is shared
		# relative to the water.
		var grown := maxf(added_mass - _prev_added_mass, 0.0) + maxf(flood_mass - _prev_flood, 0.0)
		if grown > 0.0:
			v = flow + (v - flow) * ((inertial_mass - grown) / inertial_mass)

	# Density-state drive (weight minus buoyancy; y points down), then drag on the speed through the
	# water and through the air, applied implicitly.
	var drive := Vector2(0.0, (carried - displaced) * g)
	# The water's own motion in waves pushes the body with it, as in BoundedBuoyancyBody, averaged
	# across the body (two Gauss points across its bounding box).
	wave_acceleration = 0.0
	if model == Model.BOUNDED and submerged_area > 0.0 and water:
		var a: float = water.vertical_acceleration_at(centre_of_buoyancy, absf(xf.x.x) * _box.x + absf(xf.y.x) * _box.y)
		wave_acceleration = clampf(a, -0.5 * g, 0.5 * g)
		drive += Vector2(0.0, (1.0 + added_mass_coefficient) * rho * submerged_area * _kg * wave_acceleration)
	var wind := _Wind2D.total_at(cw, _world) if not _Wind2D.instances.is_empty() else Vector2.ZERO
	wind_here = wind
	var through_water := v - flow
	var through_air := v - wind
	var c_water := 0.5 * drag_coefficient * _drag_length(through_water, xf) * rho * _kg * (submerged_area / area) \
		* through_water.length() * dt / inertial_mass
	var c_air := 0.5 * drag_coefficient * _drag_length(through_air, xf) * rho_air * _kg * (air_area / area) \
		* through_air.length() * dt / inertial_mass
	var c_sail := 0.5 * SAIL_DRAG * sail_size * rho_air * _kg * through_air.length() * dt / inertial_mass
	var v_target := (v + drive / inertial_mass * dt + c_water * flow + (c_air + c_sail) * wind) / (1.0 + c_water + c_air + c_sail)
	var sail_target := v_target

	# Wave-making damping of bobbing relative to the surface under the body, as in BoundedBuoyancyBody.
	heave_damping = 0.0
	if model == Model.BOUNDED and wave_damping > 0.0 and submerged_area > 0.0 and submerged_area < area * (1.0 - 1e-9):
		var surface_here := _surface_at(cw.x, water)
		var level_now: float = water.global_position.y if water else water_level
		var com_now := xf * center_of_mass
		var frame := Engine.get_physics_frames()
		var jump := TELEPORT_DISTANCE * pixels_per_metre
		var follows := not is_nan(_prev_surface) and water == _prev_water and frame == _prev_frame + 1 \
			and absf(level_now - _prev_level) < jump \
			and (com_now - _prev_com - linear_velocity * dt).length_squared() < jump * jump
		var surface_rate := (surface_here - _prev_surface) / dt if follows else 0.0
		_prev_surface = surface_here
		_prev_water = water
		_prev_level = level_now
		_prev_com = com_now
		_prev_frame = frame
		var depth := centre_of_buoyancy.y - surface_here   # y points down
		if depth > 1e-9 and g > 0.0 and rho > 0.0:
			# The rectangle with the same area under water and the same depth of its centre; its own
			# bobbing frequency ω = √(ρ g beam thickness / m), in metres and kilograms.
			var draft := 2.0 * depth
			var beam := submerged_area / draft
			var omega := sqrt(rho * g * beam * _kg / inertial_mass)
			heave_damping = wave_damping * 2.0 * wave_damping_ratio(beam / draft) * inertial_mass * omega
			# Damped for as long as it is at the surface this step: until it leaves the water or goes
			# under (rising is -y here).
			var rel := v.y - surface_rate
			var time := _at_surface(depth, (area - submerged_area) / submerged_area, -rel, dt)
			v_target.y -= (1.0 - exp(-heave_damping * time / inertial_mass)) * rel
	else:
		_prev_surface = NAN

	mass = inertial_mass
	apply_central_force(inertial_mass * (v_target - linear_velocity) / dt)
	last_acceleration = (v_target - v) / dt

	var com := xf * center_of_mass
	if c_sail > 0.0:
		var push := c_sail * inertial_mass / dt * (wind - sail_target)
		apply_torque((xf * sail_centre - com).cross(push))
	# Righting moment: buoyancy of the water and the air at the centres of the parts they displace.
	if displaced > 0.0:
		var centre := ((rho - rho_air) * submerged_area * centre_of_buoyancy + rho_air * area * cw) * _kg / displaced
		apply_torque((centre - com).cross(Vector2(0.0, -displaced * g)))
	if flood_mass > 0.0:
		apply_torque(_flood_torque)
	var damp := angular_drag * (submerged_area + air_area * minf(1.0, rho_air / density)) / area
	if absf(damp - angular_damp) > 1e-6:
		angular_damp = damp
	# Turning moves water too: the water's share in it, added to the inertia (eased in over about half
	# a second, so that it does not flicker with each wave).
	var turning_now := _added_turning_inertia(xf, rho) if model == Model.BOUNDED and submerged_area > 0.0 and added_turning else 0.0
	_turning_added = turning_now if turning_now == 0.0 else lerpf(_turning_added, turning_now, 1.0 - exp(-dt / 0.5))
	_set_extra_inertia(_turning_added + _flood_inertia)
	_prev_added_mass = added_mass
	_prev_flood = flood_mass


## The compartments for one step: moves water through their holes and pumps, and puts the water's
## weight on the body, as BoundedBuoyancyBody does: weight carried at the water's own centre, which
## stays level in its compartment as the body leans, applied as a weight on the body plus its turning
## moment about the body's centre of mass. The water does not turn with the body, so it adds to the
## inertia only as weight at its centre. Returns the water's mass (kg).
func _flood(xf: Transform2D, rho: float, g: float, dt: float) -> float:
	var w: Node2D = water
	var have_water := _world_has_water
	var level := water_level
	var surface := func(p: Vector2) -> float:
		var here: Node2D = w if w and w.contains(p) else (_find_water(p) if have_water else null)
		if here:
			return here.height_at(p.x)
		return NAN if have_water else level
	var com := xf * center_of_mass
	var total := 0.0
	var torque := 0.0
	var spin := 0.0
	for comp in _compartments:
		if not is_instance_valid(comp) or not comp.is_inside_tree():
			continue
		var r: Array = comp.flood_step(comp.global_transform, surface, rho, g, dt, pixels_per_metre, thickness)
		var m: float = r[0]
		if m <= 0.0:
			continue
		var arm: Vector2 = r[1] - com
		total += m
		torque += arm.cross(Vector2(0.0, m * g))
		spin += m * arm.length_squared()
	_flood_torque = torque
	_flood_inertia = spin
	return total


func _add_compartment(c: Node) -> void:
	if not _compartments.has(c):
		_compartments.append(c)


func _remove_compartment(c: Node) -> void:
	_compartments.erase(c)


## Sets the body's inertia to its own plus `extra`.
func _set_extra_inertia(extra: float) -> void:
	if _extra_on and inertia != _wet_inertia:
		_dry_inertia = inertia   # set from elsewhere meanwhile: the body's own inertia now
	if extra <= 0.0:
		if _extra_on:
			_extra_on = false
			inertia = _unit_inertia * density if _auto_inertia else _dry_inertia
		return
	if not _extra_on:
		_extra_on = true
		_dry_inertia = inertia
	var i := (_unit_inertia * density if _auto_inertia else _dry_inertia) + extra
	if absf(i - inertia) > 0.01 * i:   # set when it has changed by 1%
		inertia = i
	_wet_inertia = inertia


## The water's share in the body's turning (engine units, kg px²). Turning a body that is not round
## pushes water aside, as moving it does; a circle moves none. It is Lamb's for an elliptic cylinder
## turning in water, π/8 ρ (a² − b²)² per metre of thickness, a and b half its width and height. At the
## surface the water above is missing and the surface acts as a mirror, so the height is twice the
## draft and the share is halved.
func _added_turning_inertia(xf: Transform2D, rho: float) -> float:
	if _circle or _box == Vector2.ZERO or area <= 0.0:
		return 0.0
	var share := clampf(submerged_area / area, 0.0, 1.0)
	var ppm := pixels_per_metre
	var h := _box * 0.5 / ppm
	var floating := share < 0.999
	if floating:
		# The body's axis nearest upright (y is down in 2D): its height under water, doubled by the mirror.
		if absf(xf.y.normalized().y) >= absf(xf.x.normalized().y):
			h.y *= minf(2.0 * share, 1.0)
		else:
			h.x *= minf(2.0 * share, 1.0)
	var d := h.x * h.x - h.y * h.y
	return PI / 8.0 * rho * d * d * thickness * (0.5 if floating else 1.0) * ppm * ppm


## Damping ratio of bobbing from the waves a body makes in a side view, where the waves it sends out
## run only left and right (a long body, like a strip of a ship's hull): ζ = 0.0591 r / (1 + 0.15 r),
## r its beam over its draft. About 0.03 for a deep, narrow body (r = 0.5), 0.05 for a square floating
## half under (1), 0.15 for a raft (4) and 0.3 for a plank lying flat (20). Fitted within 2% to the
## exact results of linear wave theory for long floating boxes, computed with the boundary-element
## solver Capytaine (reference/wave_damping.py).
static func wave_damping_ratio(beam_to_draft: float) -> float:
	if not (beam_to_draft > 0.0):
		return 0.0
	return 0.0591 * beam_to_draft / (1.0 + 0.15 * beam_to_draft)


## How long in a step of `dt` the body stays at the surface, moving up at `rise` (as in BoundedBuoyancyBody).
static func _at_surface(depth: float, dry_over_wet: float, rise: float, dt: float) -> float:
	if rise > 0.0:
		return minf(dt, 2.0 * depth / rise)
	if rise < 0.0:
		return minf(dt, 2.0 * depth * dry_over_wet / -rise)
	return dt


func _find_water(at: Vector2) -> Node2D:
	_world_has_water = false
	for w in _Water2D.instances:
		if w.world != _world:
			continue
		_world_has_water = true
		if w.contains(at):
			return w
	return null


func _surface_at(x: float, w: Node2D) -> float:
	return w.height_at(x) if w else water_level


## Area under water and its centre (global), for the body at `xf`.
func _immerse(xf: Transform2D, w: Node2D) -> void:
	if _circle:
		var c := xf * _circle_centre
		var q := _surface_at(c.x, w) - c.y          # distance from the centre down to the surface
		if q >= _radius:
			submerged_area = 0.0
			centre_of_buoyancy = c
		elif q <= -_radius:
			submerged_area = area
			centre_of_buoyancy = c
		else:
			var s := sqrt(_radius * _radius - q * q)
			submerged_area = _radius * _radius * acos(q / _radius) - q * s
			centre_of_buoyancy = c + Vector2(0.0, (2.0 / 3.0) * s * s * s / submerged_area)
		return
	var cw := xf * _centroid
	# swell bounds how far the surface anywhere under the body can be from its height over the
	# centre: twice the most it rises or falls from its mean.
	var swell: float = 2.0 * w.max_wave() if w else 0.0
	var top := _surface_at(cw.x, w)
	if cw.y + _bound_radius <= top - swell:
		submerged_area = 0.0
		centre_of_buoyancy = cw
		return
	if cw.y - _bound_radius >= top + swell:
		submerged_area = area
		centre_of_buoyancy = cw
		return
	var total := 0.0
	var moment := Vector2.ZERO
	for k in _polys.size():
		var poly := _polys[k]
		var n := poly.size()
		var pts := PackedVector2Array()
		var depth := PackedFloat32Array()
		pts.resize(n)
		depth.resize(n)
		for j in n:
			var p := xf * poly[j]
			pts[j] = p
			depth[j] = p.y - _surface_at(p.x, w)   # positive below the surface
		var clipped := PackedVector2Array()
		for j in n:
			var a := pts[j]
			var b := pts[(j + 1) % n]
			var da := depth[j]
			var db := depth[(j + 1) % n]
			if da > 0.0:
				clipped.append(a)
			if (da > 0.0) != (db > 0.0):
				clipped.append(a + (b - a) * (da / (da - db)))
		var m := clipped.size()
		if m < 3:
			continue
		var a2 := 0.0
		var cm := Vector2.ZERO
		var o := clipped[0]
		for j in range(1, m - 1):
			var e1 := clipped[j] - o
			var e2 := clipped[j + 1] - o
			var t := 0.5 * e1.cross(e2)
			a2 += t
			cm += t * (o + clipped[j] + clipped[j + 1]) / 3.0
		var s := _poly_scale[k]
		total += absf(a2) * s
		moment += cm * signf(a2) * s
	submerged_area = clampf(total, 0.0, area)
	centre_of_buoyancy = moment / total if total > 1e-9 else cw


func _report_crossing(entering: bool, cw: Vector2, flow: Vector2) -> void:
	var speed := (linear_velocity - flow).length()
	var at := Vector2(cw.x, _surface_at(cw.x, water))
	if entering:
		water_entered.emit(speed, at)
	else:
		water_exited.emit(speed, at)
	if water and water.has_method(&"splash"):
		water.splash(at, speed if entering else 0.4 * speed, _box.x)


## Length the body shows to a flow along `along`: its bounding box seen at that angle, times its fill.
func _drag_length(along: Vector2, xf: Transform2D) -> float:
	if _circle:
		return 2.0 * _radius
	var n := along.length()
	if n < 1e-9:
		return _box.x * _fill
	var d := (xf.basis_xform_inv(along) / n).abs()
	return (d.x * _box.y + d.y * _box.x) * _fill


func _build_shape() -> void:
	_polys.clear()
	_poly_scale = PackedFloat32Array()
	_circle = false
	area = 0.0
	var parts := []
	for child in get_children():
		if child is CollisionShape2D and child.shape != null and not child.disabled:
			parts.append([child.shape, child.transform])
		elif child is CollisionPolygon2D and not child.disabled and child.polygon.size() >= 3:
			parts.append([child.polygon, child.transform])
	if parts.size() == 1 and parts[0][0] is CircleShape2D:
		var xf: Transform2D = parts[0][1]
		_circle = true
		_radius = parts[0][0].radius * xf.get_scale().x
		_circle_centre = xf.origin
		area = PI * _radius * _radius
		_centroid = _circle_centre
		_bound_radius = _radius
		_box = Vector2(2.0 * _radius, 2.0 * _radius)
		_fill = PI / 4.0
		return
	var moment := Vector2.ZERO
	for part in parts:
		var pts := PackedVector2Array()
		var exact := -1.0
		var shape = part[0]
		if shape is RectangleShape2D:
			var h: Vector2 = shape.size * 0.5
			pts = PackedVector2Array([Vector2(-h.x, -h.y), Vector2(h.x, -h.y), Vector2(h.x, h.y), Vector2(-h.x, h.y)])
		elif shape is CircleShape2D:
			for i in 24:
				pts.append(Vector2.from_angle(TAU * i / 24.0) * shape.radius)
			exact = PI * shape.radius * shape.radius
		elif shape is CapsuleShape2D:
			var r: float = shape.radius
			var half := maxf(0.5 * shape.height - r, 0.0)
			for i in 13:
				pts.append(Vector2(0.0, -half) + Vector2.from_angle(PI + PI * i / 12.0) * r)
			for i in 13:
				pts.append(Vector2(0.0, half) + Vector2.from_angle(PI * i / 12.0) * r)
			exact = 2.0 * r * 2.0 * half + PI * r * r
		elif shape is ConvexPolygonShape2D:
			pts = shape.points
		elif shape is PackedVector2Array:
			pts = shape
		else:
			push_warning("BoundedBuoyancyBody2D '%s': %s is not supported." % [name, shape.get_class()])
			continue
		var xf: Transform2D = part[1]
		var placed := PackedVector2Array()
		for p in pts:
			placed.append(xf * p)
		var a := _signed_area(placed)
		if absf(a) < 1e-9:
			continue
		var s := (exact * absf(xf.determinant()) / absf(a)) if exact > 0.0 else 1.0
		_polys.append(placed)
		_poly_scale.append(s)
		area += absf(a) * s
		moment += _polygon_centroid(placed) * absf(a) * s
	if area <= 0.0:
		return
	_centroid = moment / area
	var lo := Vector2(INF, INF)
	var hi := Vector2(-INF, -INF)
	_bound_radius = 0.0
	for poly in _polys:
		for p in poly:
			lo = lo.min(p)
			hi = hi.max(p)
			_bound_radius = maxf(_bound_radius, p.distance_to(_centroid))
	_box = hi - lo
	_fill = clampf(area / (_box.x * _box.y), 0.3, 1.0)


func _signed_area(p: PackedVector2Array) -> float:
	var a := 0.0
	for i in p.size():
		a += p[i].cross(p[(i + 1) % p.size()])
	return 0.5 * a


func _polygon_centroid(p: PackedVector2Array) -> Vector2:
	var a := 0.0
	var c := Vector2.ZERO
	for i in p.size():
		var q := p[(i + 1) % p.size()]
		var cr := p[i].cross(q)
		a += cr
		c += (p[i] + q) * cr
	return c / (3.0 * a)


## Rotational inertia of the body's own mass about `com` (kg·px²).
func _own_inertia(com: Vector2) -> float:
	if _circle:
		var d := _circle_centre - com
		return own_mass * (0.5 * _radius * _radius + d.length_squared())
	var j := 0.0
	for k in _polys.size():
		var p := _polys[k]
		var jk := 0.0
		for i in p.size():
			var a := p[i] - com
			var b := p[(i + 1) % p.size()] - com
			var cr := a.cross(b)
			jk += cr * (a.dot(a) + a.dot(b) + b.dot(b))
		j += absf(jk) / 12.0 * _poly_scale[k]
	return density * _kg * j
