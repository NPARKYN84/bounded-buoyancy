@tool
@icon("res://addons/bounded_buoyancy/icon_compartment.svg")
class_name BoundedBuoyancyCompartment2D
extends Node2D
## A space inside a BoundedBuoyancyBody2D that can flood, in a side view: a ship's hold, a tank, the
## inside of a boat. As BoundedBuoyancyCompartment in 3D.
##
## Add it under the body and set its size: a rectangle centred on this node, inside the body's shape,
## as deep (into the screen) as the body's thickness. Give it holes (in the inspector, or from code
## with add_hole(), say where a cannonball hit) and water pours in through those below the waterline,
## faster the deeper they are, by the orifice law Q = C A √(2 g Δh), Δh the difference between the
## water's depth outside and inside at the hole; holes above the water let flood water drain out again.
## The water inside stays level whichever way the body leans. Its weight acts at its own centre, which
## moves to the low side as the body leans (the free-surface effect), so a holed boat settles lower,
## lists toward its flooded end, and sinks or capsizes when it has taken in more than its reserve of
## buoyancy will carry. The body's hull keeps its buoyancy: the flood water is weight it carries, as in
## the added-weight method of naval architecture.
##
## Pumps (pump_rate) take water out again. Set flood_volume from code to start it part full, or to
## fill or empty it at once. The compartment is taken to be vented: air gets out as water comes in.

## Width and height of the space, in pixels. Keep it inside the body's shape.
@export var size: Vector2 = Vector2(100.0, 50.0):
	set(value):
		size = value.max(Vector2(0.1, 0.1))
		queue_redraw()
## The holes, each as (x, y, area): its position in this compartment's own coordinates (pixels), on or
## inside the rectangle, and its area in m² (a cannonball of 15 cm makes about 0.018 m²).
@export var holes: Array[Vector3] = []:
	set(value):
		holes = value
		queue_redraw()
## Area of a hatch in the top of the space, in m² (0: closed). Once the hatch goes under, the sea
## pours in through it as through a hole, as it does into a real ship whose deck goes under: that is
## what sinks a ship holed only at one end.
@export_range(0.0, 100.0, 0.001, "or_greater", "suffix:m²") var hatch_area: float = 0.0
## Discharge coefficient of the holes: 0.6 for a sharp-edged hole, up to about 0.95 for a smooth one.
@export_range(0.1, 1.0, 0.01) var discharge_coefficient: float = 0.6
## Water the pumps take out, in m³ per second, while there is water to take.
@export_range(0.0, 100.0, 0.001, "or_greater", "suffix:m³/s") var pump_rate: float = 0.0
## Water in the compartment now, in m³.
@export_range(0.0, 100000.0, 0.001, "or_greater", "suffix:m³") var flood_volume: float = 0.0:
	set(value):
		flood_volume = maxf(value, 0.0)
## Draw the water inside (and the outline in the editor).
@export var show_water := true
## Colour of the water drawn inside.
@export var water_colour := Color(0.2, 0.45, 0.75, 0.6)

## Volume of the space (m³): its area times the body's thickness.
var capacity: float:
	get:
		return _area_m2() * _thickness()
## Fraction of the space full of water, 0 to 1.
var fill_fraction: float:
	get:
		var full := capacity
		return clampf(flood_volume / full, 0.0, 1.0) if full > 0.0 else 0.0
## Flow of water in through the holes over the last step, m³/s (negative when it drains out).
var inflow: float = 0.0
## Height of the water's surface inside (global y, pixels) over the last step.
var water_level: float = 0.0
## Centre of the water inside (global, pixels) over the last step.
var water_centre: Vector2 = Vector2.ZERO

var _body: Node = null
var _shown_level := NAN


func _enter_tree() -> void:
	if Engine.is_editor_hint():
		return
	var p := get_parent()
	while p:
		if p.has_method(&"_add_compartment"):
			_body = p
			p._add_compartment(self)
			return
		p = p.get_parent()


func _exit_tree() -> void:
	if _body and is_instance_valid(_body):
		_body._remove_compartment(self)
	_body = null


func _get_configuration_warnings() -> PackedStringArray:
	var p := get_parent()
	while p:
		if p is BoundedBuoyancyBody2D:
			return PackedStringArray()
		p = p.get_parent()
	return PackedStringArray(["Put it under a BoundedBuoyancyBody2D: it is a space inside that body."])


## Make a hole of `area` m² at the global point `at` (moved onto the rectangle if it lies outside it).
## Returns its index in holes.
func add_hole(at: Vector2, area: float) -> int:
	var l := to_local(at)
	var h := 0.5 * size
	l = l.clamp(-h, h)
	holes.append(Vector3(l.x, l.y, maxf(area, 0.0)))
	queue_redraw()
	return holes.size() - 1


## Close every hole.
func patch_holes() -> void:
	holes.clear()
	queue_redraw()


## Global positions of the rectangle's four corners, for the compartment at `xf` (global), in order
## round it.
func corners(xf: Transform2D) -> PackedVector2Array:
	var h := 0.5 * size
	return PackedVector2Array([xf * Vector2(-h.x, -h.y), xf * Vector2(h.x, -h.y), xf * Vector2(h.x, h.y), xf * Vector2(-h.x, h.y)])


## Area (px²) of the polygon `c` below the global height `level` (y greater than it: y points down),
## and its centre: Vector3(area, centre x, centre y).
static func below(c: PackedVector2Array, level: float) -> Vector3:
	var poly := PackedVector2Array()
	var n := c.size()
	for i in n:
		var a := c[i]
		var b := c[(i + 1) % n]
		var a_in := a.y >= level
		var b_in := b.y >= level
		if a_in:
			poly.append(a)
		if a_in != b_in:
			poly.append(a + (b - a) * ((level - a.y) / (b.y - a.y)))
	if poly.size() < 3:
		return Vector3.ZERO
	var area := 0.0
	var m := Vector2.ZERO
	for i in poly.size():
		var p := poly[i]
		var q := poly[(i + 1) % poly.size()]
		var cr := p.x * q.y - q.x * p.y
		area += cr
		m += (p + q) * cr
	if absf(area) < 1e-12:
		return Vector3.ZERO
	var centre := m / (3.0 * area)
	return Vector3(absf(0.5 * area), centre.x, centre.y)


## The height (global y) of the water's surface when `area` px² of water lies level in the polygon
## `c`, and the water's centre: [level, centre].
static func level_for(c: PackedVector2Array, area: float) -> Array:
	var top := INF
	var bottom := -INF
	var mid := Vector2.ZERO
	for p in c:
		top = minf(top, p.y)
		bottom = maxf(bottom, p.y)
		mid += p
	mid /= c.size()
	if area <= 0.0:
		return [bottom, Vector2(mid.x, bottom)]
	var full := below(c, top - 1.0).x
	if area >= full:
		return [top, mid]
	# The area below a level grows steadily as the level rises (y falls): find it by the Illinois
	# method (regula falsi that halves the stale end).
	var a := bottom
	var fa := -area
	var b := top
	var fb := full - area
	var side := 0
	var level := 0.5 * (top + bottom)
	var r := Vector3.ZERO
	for n in 60:
		level = (a * fb - b * fa) / (fb - fa)
		r = below(c, level)
		var f := r.x - area
		if absf(f) <= 1e-10 * full or absf(b - a) <= 1e-9 * (bottom - top):
			break
		if f * fb > 0.0:
			b = level
			fb = f
			if side == -1:
				fa *= 0.5
			side = -1
		else:
			a = level
			fa = f
			if side == 1:
				fb *= 0.5
			side = 1
	var centre := Vector2(r.y, r.z) if r.x > 1e-12 else Vector2(mid.x, level)
	return [level, centre]


## One physics step of `dt` for the compartment at `xf` (global), on a body whose surroundings have
## water whose surface is at `surface.call(p)` (global y, pixels) for a global point p (NAN where there
## is none), of density `rho`, under gravity `g` (px/s²), at `ppm` pixels a metre, with the body
## `thickness` m deep. Moves water through the holes and the pumps, and returns the water's mass (kg)
## and centre (global): [mass, centre].
func flood_step(xf: Transform2D, surface: Callable, rho: float, g: float, dt: float, ppm: float, thickness: float) -> Array:
	var c := corners(xf)
	var px2_per_m3 := ppm * ppm / thickness     # px² of the rectangle per m³ of water
	var full_area := absf(size.x * size.y * xf.determinant())
	var full := full_area / px2_per_m3
	flood_volume = minf(flood_volume, full)
	var before := flood_volume
	var state := level_for(c, flood_volume * px2_per_m3)
	var level: float = state[0]
	var vol := flood_volume
	var g_m := g / ppm
	var openings := holes
	if hatch_area > 0.0:
		openings = holes.duplicate()
		openings.append(Vector3(0.0, -0.5 * size.y, hatch_area))   # the top is at -y: y points down
	for hole in openings:
		if hole.z <= 0.0:
			continue
		var at := xf * Vector2(hole.x, hole.y)
		var outside: float = surface.call(at)
		var d_out := maxf(at.y - outside, 0.0) / ppm if not is_nan(outside) else 0.0
		var d_in := maxf(at.y - level, 0.0) / ppm if before > 0.0 else 0.0
		var head := d_out - d_in
		if absf(head) < 1e-9:
			continue
		var q := discharge_coefficient * hole.z * sqrt(2.0 * g_m * absf(head)) * dt
		# Never past the level at which the flow through this hole would stop: the water outside when
		# filling; the water outside, or the hole itself, when draining.
		if head > 0.0:
			var stop := below(c, outside).x / px2_per_m3
			vol = maxf(vol, minf(vol + q, stop))
		else:
			var stop_level := minf(outside, at.y) if not is_nan(outside) else at.y
			var stop := below(c, stop_level).x / px2_per_m3
			vol = minf(vol, maxf(vol - q, stop))
	if pump_rate > 0.0:
		vol -= pump_rate * dt
	flood_volume = clampf(vol, 0.0, full)
	inflow = (flood_volume - before) / dt if dt > 0.0 else 0.0
	if flood_volume != before:
		state = level_for(c, flood_volume * px2_per_m3)
	water_level = state[0]
	water_centre = state[1]
	if show_water and (flood_volume > 0.0 or not is_nan(_shown_level)):
		queue_redraw()
	return [rho * flood_volume, water_centre]


func _draw() -> void:
	var h := 0.5 * size
	if Engine.is_editor_hint():
		draw_rect(Rect2(-h, size), Color(0.3, 0.6, 1.0, 0.8), false, 1.0)
		for hole in holes:
			draw_circle(Vector2(hole.x, hole.y), 3.0, Color(1.0, 0.3, 0.2))
		return
	if not show_water or flood_volume <= 0.0:
		_shown_level = NAN
		return
	# The water inside: the rectangle below the level, drawn in this node's own coordinates.
	var inv := global_transform.affine_inverse()
	var c := corners(global_transform)
	var poly := PackedVector2Array()
	for i in 4:
		var a := c[i]
		var b := c[(i + 1) % 4]
		if a.y >= water_level:
			poly.append(inv * a)
		if (a.y >= water_level) != (b.y >= water_level):
			poly.append(inv * (a + (b - a) * ((water_level - a.y) / (b.y - a.y))))
	# A sliver of water (the level at a corner) has no area to draw.
	if poly.size() >= 3 and not Geometry2D.triangulate_polygon(poly).is_empty():
		draw_colored_polygon(poly, water_colour)
	for hole in holes:
		draw_circle(Vector2(hole.x, hole.y), 2.5, Color(0.1, 0.1, 0.1))
	_shown_level = water_level


func _area_m2() -> float:
	var ppm := 100.0
	if _body and is_instance_valid(_body):
		ppm = _body.pixels_per_metre
	var s := global_scale if is_inside_tree() else Vector2.ONE
	return absf(size.x * s.x * size.y * s.y) / (ppm * ppm)


func _thickness() -> float:
	return _body.thickness if _body and is_instance_valid(_body) else 0.2
