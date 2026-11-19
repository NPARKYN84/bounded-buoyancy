extends Node3D
## LOCK: confined water. A canal lock's chamber, 4 m wide and 14 m long, is water that cannot get
## away (the water node's Confined). A boat floats in it and a crate is dropped in: each raises the
## level by just the water it pushes aside. Then the paddles open and the chamber fills (add_water),
## and the boat rises with it, as boats are lifted through a real lock; then it empties again.
##   F       fill the lock (or stop)
##   E       empty it (or stop)
##   C       drop another crate in
##   Space   run again
##   Tab     next scene

const LOOK := preload("res://addons/bounded_buoyancy/demo/demo_look.gd")
const BB := preload("res://addons/bounded_buoyancy/bounded_buoyancy_body.gd")
const BW := preload("res://addons/bounded_buoyancy/bounded_buoyancy_water.gd")
const NEXT_SCENE := "res://addons/bounded_buoyancy/demo/demo.tscn"

const INK := Color(0.12, 0.14, 0.18)
const WIDTH := 4.0
const LENGTH := 14.0
const FLOW := 5.0        # m³/s through the paddles (a real lock takes minutes: this is quicker to watch)
const TOP := 1.8         # how far the chamber fills above where it starts (m)

var _water: Node3D
var _start_y := 0.0
var _bodies: Array = []
var _flow := 0.0          # +1 filling, -1 emptying, 0 still
var _t := 0.0
var _status: Label


func _ready() -> void:
	LOOK.sea_and_sky(self, Vector2(-40.0, -30.0))
	_water = BW.new()
	_water.size = Vector3(WIDTH, 6.0, LENGTH)
	_water.confined = true
	_water.surface_color = Color(0.12, 0.30, 0.30, 0.8)
	_water.fixed_density = true
	_water.density = 1000.0
	add_child(_water)
	_start_y = _water.global_position.y
	_build_lock()
	var cam := Camera3D.new()
	cam.fov = 55.0
	cam.position = Vector3(7.5, 5.5, 10.5)
	add_child(cam)
	cam.look_at(Vector3(0.0, 0.0, 0.0))
	var layer := CanvasLayer.new()
	add_child(layer)
	var title := Label.new()
	title.text = "A lock: confined water. What floats in it raises it; fill the chamber and the boat rises with it."
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
	keys.text = "F: fill     E: empty     C: drop a crate in     Space: run again     Tab: next scene"
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
			_flow = 0.0 if _flow > 0.0 else 1.0
		elif event.keycode == KEY_E:
			_flow = 0.0 if _flow < 0.0 else -1.0
		elif event.keycode == KEY_C:
			_crate(Vector3(randf_range(-1.0, 1.0), _water.global_position.y + 2.5, randf_range(3.0, 5.5)))


func _physics_process(dt: float) -> void:
	_t += dt
	# By itself: a crate goes in at 3 s, the paddles open at 6 s and the chamber fills, then empties.
	if _t >= 3.0 and _t - dt < 3.0:
		_crate(Vector3(0.4, _water.global_position.y + 2.5, 4.5))
	if _t >= 6.0 and _t - dt < 6.0:
		_flow = 1.0
	var filled: float = _water.global_position.y - _start_y - _water.level_rise
	if _flow > 0.0 and filled >= TOP:
		_flow = 0.0
		if _t < 40.0:
			get_tree().create_timer(4.0).timeout.connect(func() -> void: _flow = -1.0)
	elif _flow < 0.0 and filled <= 0.0:
		_flow = 0.0
	if _flow != 0.0:
		_water.add_water(_flow * FLOW * dt)
	var boat: RigidBody3D = _bodies[0]
	_status.text = "Water %.3f m up: %.0f mm from what floats in it, %.2f m run in.   The boat's deck %.2f m up.   %s" % [
		_water.global_position.y - _start_y, 1000.0 * _water.level_rise, filled, boat.global_position.y + 0.45,
		"Filling" if _flow > 0.0 else ("Emptying" if _flow < 0.0 else "Paddles shut")]


func _reset() -> void:
	for b in _bodies:
		b.queue_free()
	_bodies.clear()
	_water.global_position.y = _start_y
	_water.level_rise = 0.0
	_flow = 0.0
	_t = 0.0
	_bodies.append(_boat())


## A small boat 6 m long and 1.8 m wide, a shallow box with a cabin.
func _boat() -> RigidBody3D:
	var b := BB.new()
	var hull := ConvexPolygonShape3D.new()
	hull.points = PackedVector3Array([
		Vector3(-0.9, 0.35, -3.0), Vector3(0.9, 0.35, -3.0), Vector3(-0.9, 0.35, 2.7), Vector3(0.9, 0.35, 2.7), Vector3(0, 0.35, 3.1),
		Vector3(-0.8, -0.35, -2.9), Vector3(0.8, -0.35, -2.9), Vector3(-0.8, -0.35, 2.6), Vector3(0.8, -0.35, 2.6)])
	var cs := CollisionShape3D.new()
	cs.shape = hull
	b.add_child(cs)
	var mi := MeshInstance3D.new()
	mi.mesh = hull.get_debug_mesh()
	mi.material_override = _paint(Color(0.15, 0.32, 0.22))
	b.add_child(mi)
	var cabin := MeshInstance3D.new()
	var cm := BoxMesh.new()
	cm.size = Vector3(1.4, 0.7, 2.6)
	cabin.mesh = cm
	cabin.position = Vector3(0, 0.7, -0.6)
	cabin.material_override = _paint(Color(0.78, 0.18, 0.15))
	b.add_child(cabin)
	b.density = 300.0
	b.position = Vector3(0, 0.1, -1.5)
	add_child(b)
	return b


func _crate(at: Vector3) -> void:
	var b := BB.new()
	var cs := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = Vector3(0.8, 0.8, 0.8)
	cs.shape = box
	b.add_child(cs)
	var mi := MeshInstance3D.new()
	var m := BoxMesh.new()
	m.size = box.size
	mi.mesh = m
	mi.material_override = _paint(Color(0.62, 0.45, 0.28))
	b.add_child(mi)
	b.made_of = BoundedBuoyancyMaterials.Kind.OAK
	b.position = at
	add_child(b)
	_bodies.append(b)


## The chamber: a floor, two side walls and two gates, in stone and timber.
func _build_lock() -> void:
	var stone := _paint(Color(0.55, 0.53, 0.50))
	var timber := _paint(Color(0.35, 0.25, 0.17))
	var floor_y := -4.0
	_wall(Vector3(0, floor_y - 0.25, 0), Vector3(WIDTH + 2.0, 0.5, LENGTH + 2.0), stone)
	for side in [-1.0, 1.0]:
		_wall(Vector3(side * (0.5 * WIDTH + 0.5), 0.0, 0), Vector3(1.0, 8.5, LENGTH + 2.0), stone)
		_wall(Vector3(0, 0.0, side * (0.5 * LENGTH + 0.3)), Vector3(WIDTH, 8.5, 0.6), timber)


func _wall(at: Vector3, size: Vector3, paint: Material) -> void:
	var body := StaticBody3D.new()
	var mi := MeshInstance3D.new()
	var m := BoxMesh.new()
	m.size = size
	mi.mesh = m
	mi.material_override = paint
	body.add_child(mi)
	var cs := CollisionShape3D.new()
	var s := BoxShape3D.new()
	s.size = size
	cs.shape = s
	body.add_child(cs)
	body.position = at
	add_child(body)


func _paint(colour: Color) -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	m.albedo_color = colour
	m.roughness = 0.85
	return m
