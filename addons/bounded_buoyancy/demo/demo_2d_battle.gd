extends Node2D
## 2D BATTLE: two ships in a side view trade fire with 12-pounders. Each shot flies with a real
## cannonball's drag, skips if it meets the water at a shallow angle, and goes through the planking it
## strikes (BoundedBuoyancyHull2D), holing the compartment behind it. Holes below the waterline let the
## sea in (BoundedBuoyancyCompartment2D): the ships settle, list toward the flooded end, and sink. The
## recoil of every gun pushes its ship back. Each rides to an anchor on the sea bed by a cable
## (BoundedBuoyancyLine2D) that hangs slack until the recoil and the shot push her to the end of it.
##   F       fire both now
##   P       pumps on the teal ship
##   Space   run again
##   Tab     next scene

const BB2 := preload("res://addons/bounded_buoyancy/bounded_buoyancy_body_2d.gd")
const BW2 := preload("res://addons/bounded_buoyancy/bounded_buoyancy_water_2d.gd")
const NEXT_SCENE := "res://addons/bounded_buoyancy/demo/demo_materials.tscn"
const INK := Color(0.12, 0.14, 0.18)
const TEAL := Color(0.16, 0.62, 0.56)
const ORANGE := Color(0.93, 0.5, 0.15)
const HULL := Color(0.30, 0.24, 0.18)
const APART := 950.0      # px from the middle to each ship (9.5 m)

var _water: Node2D
var _ships: Array = []    # [body, gun, compartments, flag colour, name, label]
var _tracers: Array = []  # [ball, Line2D]
var _status: Label
var _t := 0.0
var _rng := RandomNumberGenerator.new()
var _cables: Array = []


func _ready() -> void:
	RenderingServer.set_default_clear_color(Color(0.84, 0.91, 0.97))
	var cam := Camera2D.new()
	cam.position = Vector2(0, 40)
	cam.zoom = Vector2(0.36, 0.36)
	add_child(cam)
	_water = BW2.new()
	_water.size = Vector2(12000, 760)
	_water.wave_height = 4.0
	_water.wave_length = 500.0
	_water.water_color = Color(0.22, 0.48, 0.72, 0.65)
	add_child(_water)
	# The sea bed, 7 m down, for a sunk ship to come to rest on.
	var bed := StaticBody2D.new()
	var bs := CollisionShape2D.new()
	var br := RectangleShape2D.new()
	br.size = Vector2(12000, 100)
	bs.shape = br
	bed.add_child(bs)
	var sand := Polygon2D.new()
	sand.polygon = PackedVector2Array([Vector2(-6000, -50), Vector2(6000, -50), Vector2(6000, 50), Vector2(-6000, 50)])
	sand.color = Color(0.62, 0.56, 0.42)
	bed.add_child(sand)
	bed.position = Vector2(0, 750)
	add_child(bed)
	var layer := CanvasLayer.new()
	add_child(layer)
	var title := Label.new()
	title.text = "2D battle: 12-pounders, shot that goes through planking, holds that flood"
	title.position = Vector2(24, 14)
	title.add_theme_color_override("font_color", INK)
	title.add_theme_font_size_override("font_size", 20)
	layer.add_child(title)
	_status = Label.new()
	_status.position = Vector2(24, 44)
	_status.add_theme_color_override("font_color", INK.lightened(0.2))
	_status.add_theme_font_size_override("font_size", 16)
	layer.add_child(_status)
	var keys := Label.new()
	keys.text = "F: fire both now     P: pumps on the teal ship     Space: run again     Tab: next scene"
	keys.anchor_top = 1.0
	keys.anchor_bottom = 1.0
	keys.offset_left = 24
	keys.offset_top = -38
	keys.add_theme_color_override("font_color", INK.lightened(0.35))
	keys.add_theme_color_override("font_outline_color", Color(1, 1, 1, 0.75))
	keys.add_theme_constant_override("outline_size", 6)
	keys.add_theme_font_size_override("font_size", 16)
	layer.add_child(keys)
	_reset()


func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo:
		if event.keycode == KEY_SPACE:
			_reset()
		elif event.keycode == KEY_TAB:
			get_tree().change_scene_to_file(NEXT_SCENE)
		elif event.keycode == KEY_F:
			for s in _ships:
				s[1].reload_left = 0.0
				_fire(s)
		elif event.keycode == KEY_P and not _ships.is_empty():
			for c in _ships[0][2]:
				c.patch_holes()
				c.pump_rate = 0.4


func _physics_process(dt: float) -> void:
	_t += dt
	if _t > 1.5:
		for s in _ships:
			if s[1].is_loaded():
				_fire(s)
	for i in range(_tracers.size() - 1, -1, -1):
		var line: Line2D = _tracers[i][1]
		if not is_instance_valid(_tracers[i][0]):
			line.queue_free()
			_tracers.remove_at(i)
			continue
		line.add_point(_tracers[i][0].global_position)
		while line.get_point_count() > 6:
			line.remove_point(0)
	var lines := PackedStringArray()
	for s in _ships:
		var body: RigidBody2D = s[0]
		var water := 0.0
		for c in s[2]:
			water += c.flood_volume
		var heel := rad_to_deg(body.rotation)
		var state := "afloat"
		if body.global_position.y > 120.0:
			state = "sunk"
		elif body.to_global(Vector2(0, -80)).y > _water.height_at(body.global_position.x):
			state = "going down"   # her deck is under
		elif absf(heel) > 60.0:
			state = "capsized"
		s[5].text = "%s\n%.1f t of water in, %s" % [s[4], water * 1.0, state]
		s[5].position = Vector2(body.global_position.x - 160.0, minf(body.global_position.y - 330.0, -330.0))
		lines.append("%s: %.1f t of water in, trim %+.1f°, %s" % [s[4], water, heel, state])
	_status.text = "   ".join(lines)


## Aim the ship's gun just under the other ship's waterline, and fire.
func _fire(s: Array) -> void:
	var body: RigidBody2D = s[0]
	if body.global_position.y > 120.0:
		return
	var other: RigidBody2D = _ships[1][0] if s == _ships[0] else _ships[0][0]
	var gun: Node2D = s[1]
	# At the waterline where she floats now, so the holes are below it however she has settled.
	var aim := Vector2(other.global_position.x + _rng.randf_range(-300.0, 300.0), 35.0)
	gun.global_rotation = (aim - gun.global_position).angle()
	var ball: RigidBody2D = gun.fire()
	if ball == null:
		return
	var line := Line2D.new()
	line.width = 5.0
	line.default_color = Color(0.15, 0.15, 0.17, 0.5)
	add_child(line)
	_tracers.append([ball, line])


func _reset() -> void:
	for s in _ships:
		s[0].queue_free()
		s[5].queue_free()
	_ships.clear()
	for c in _cables:
		c.queue_free()
	_cables.clear()
	for t in _tracers:
		if is_instance_valid(t[0]):
			t[0].queue_free()
		t[1].queue_free()
	_tracers.clear()
	_t = 0.0
	_rng.seed = 11
	_ships.append(_ship(Vector2(-APART, -30), 1.0, TEAL, "Teal"))
	_ships.append(_ship(Vector2(APART, -30), -1.0, ORANGE, "Orange"))
	_ships[1][1].reload_left = 1.2   # not quite together
	# Each anchored by the bow, toward the other ship, so the recoil and the shot pull her cable taut;
	# a little slack in it.
	for i in 2:
		var side := -1.0 if i == 0 else 1.0
		var cable := BoundedBuoyancyLine2D.new()
		cable.position = Vector2(side * (APART - 700.0), 700.0)
		cable.body = _ships[i][0]
		cable.end_offset = Vector2(-side * 380.0, -40.0)   # her bow, at the deck
		var reach: float = cable.global_position.distance_to(_ships[i][0].to_global(cable.end_offset))
		cable.length = reach / 100.0 + 0.3
		cable.diameter = 0.03
		cable.width = 3.0
		add_child(cable)
		_cables.append(cable)
		var anchor := Polygon2D.new()
		anchor.polygon = PackedVector2Array([Vector2(-30, 0), Vector2(30, 0), Vector2(0, -50)])
		anchor.color = Color(0.2, 0.2, 0.22)
		cable.add_child(anchor)


## A ship 8 m long, 1.6 m deep and 3 m in the beam, three holds, a 12-pounder on deck facing `facing`.
func _ship(at: Vector2, facing: float, flag: Color, caption: String) -> Array:
	var b := BB2.new()
	b.thickness = 3.0
	b.density = 450.0
	b.center_of_mass_mode = RigidBody2D.CENTER_OF_MASS_MODE_CUSTOM
	b.center_of_mass = Vector2(0, 30)
	var pts := PackedVector2Array([Vector2(-400, -80), Vector2(400, -80), Vector2(330, 80), Vector2(-350, 80)])
	if facing < 0.0:
		for i in pts.size():
			pts[i].x = -pts[i].x
		pts.reverse()
	var cs := CollisionShape2D.new()
	var shape := ConvexPolygonShape2D.new()
	shape.points = pts
	cs.shape = shape
	b.add_child(cs)
	var look := Polygon2D.new()
	look.polygon = pts
	look.color = HULL
	b.add_child(look)
	var stripe := Line2D.new()
	stripe.points = PackedVector2Array([Vector2(-395, -60), Vector2(395, -60)])
	stripe.width = 10.0
	stripe.default_color = flag.darkened(0.1)
	b.add_child(stripe)
	var mast := Line2D.new()
	mast.points = PackedVector2Array([Vector2(-40 * facing, -80), Vector2(-40 * facing, -300)])
	mast.width = 8.0
	mast.default_color = INK.lightened(0.15)
	b.add_child(mast)
	var pennant := Polygon2D.new()
	pennant.polygon = PackedVector2Array([Vector2(-40 * facing, -300), Vector2(-40 * facing - 90 * facing, -280), Vector2(-40 * facing, -260)])
	pennant.color = flag
	b.add_child(pennant)
	var planks := BoundedBuoyancyHull2D.new()
	planks.thickness = 0.12
	planks.hole_size = 4.0   # ragged: splinters torn out round each shot hole
	b.add_child(planks)
	var holds := []
	for k in 3:
		var c := BoundedBuoyancyCompartment2D.new()
		c.size = Vector2(240, 120)
		c.position = Vector2((float(k) - 1.0) * 245.0, 12.0)
		c.water_colour = Color(0.2, 0.45, 0.75, 0.75)
		c.hatch_area = 0.6   # a hatch on deck over each hold: once the deck goes under, the sea pours in
		b.add_child(c)
		holds.append(c)
	var gun := BoundedBuoyancyCannon2D.new()
	gun.gun = BoundedBuoyancyCannon.Gun.POUNDER_12
	gun.reload_time = 2.5
	gun.spread = 0.4
	gun.shot_lifetime = 6.0
	gun.position = Vector2(300 * facing, -95)
	gun.rotation = 0.0 if facing > 0.0 else PI
	gun.seed_spread(7 if facing > 0.0 else 9)
	b.add_child(gun)
	var barrel := Polygon2D.new()
	barrel.polygon = PackedVector2Array([Vector2(-70, -9), Vector2(0, -7), Vector2(0, 7), Vector2(-70, 9)])
	barrel.color = Color(0.1, 0.1, 0.11)
	gun.add_child(barrel)
	b.position = at
	add_child(b)
	var label := Label.new()
	label.add_theme_color_override("font_color", INK)
	label.add_theme_color_override("font_outline_color", Color(1, 1, 1, 0.8))
	label.add_theme_constant_override("outline_size", 8)
	label.add_theme_font_size_override("font_size", 30)
	add_child(label)
	return [b, gun, holds, flag, caption, label]
