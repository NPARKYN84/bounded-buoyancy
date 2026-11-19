extends Node3D
## MADE OF: the same block in nine materials, dropped into the sea. Each is set by one dropdown,
## Made Of, which sets its density; how deep each floats, or that it sinks, comes from that alone.
## Foam rides high, cork floats with three quarters out, oak a quarter, ice with only a twelfth showing
## (the tip of the iceberg), and ebony, concrete and steel go to the bottom.
##   B       blocks or balls
##   Space   run again
##   Tab     next scene

const LOOK := preload("res://addons/bounded_buoyancy/demo/demo_look.gd")
const BB := preload("res://addons/bounded_buoyancy/bounded_buoyancy_body.gd")
const BW := preload("res://addons/bounded_buoyancy/bounded_buoyancy_water.gd")
const MAT := preload("res://addons/bounded_buoyancy/bounded_buoyancy_materials.gd")
const NEXT_SCENE := "res://addons/bounded_buoyancy/demo/demo.tscn"

const INK := Color(0.12, 0.14, 0.18)
const SIZE := 0.6        # m, the blocks' side and the balls' diameter
const DEPTH := 0.66      # m of water over the sea bed: what sinks rests just under the surface, in sight
const KINDS := [
	[MAT.Kind.FOAM, Color(0.95, 0.93, 0.85)],
	[MAT.Kind.CORK, Color(0.72, 0.52, 0.33)],
	[MAT.Kind.PINE, Color(0.90, 0.76, 0.52)],
	[MAT.Kind.OAK, Color(0.62, 0.45, 0.28)],
	[MAT.Kind.ICE, Color(0.80, 0.92, 0.98)],
	[MAT.Kind.PLASTIC, Color(0.93, 0.5, 0.15)],
	[MAT.Kind.EBONY, Color(0.16, 0.12, 0.10)],
	[MAT.Kind.CONCRETE, Color(0.62, 0.62, 0.60)],
	[MAT.Kind.STEEL, Color(0.55, 0.58, 0.62)],
]

var _bodies: Array = []   # [body, label, kind]
var _balls := false
var _t := 0.0
var _water: Node3D


func _ready() -> void:
	LOOK.sea_and_sky(self, Vector2(-32.0, -30.0))
	var water := BW.new()
	water.size = Vector3(3000.0, DEPTH, 3000.0)
	water.surface_color = Color(0.10, 0.32, 0.40, 0.4)
	water.wave_height = 0.015
	water.wave_length = 6.0
	water.wave_direction = 80.0
	add_child(water)
	_water = water
	# The sea bed, so what sinks comes to rest where it can be seen.
	var bed := StaticBody3D.new()
	var bs := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = Vector3(40.0, 1.0, 20.0)
	bs.shape = box
	bed.add_child(bs)
	var sand := MeshInstance3D.new()
	var sm := BoxMesh.new()
	sm.size = box.size
	sand.mesh = sm
	sand.material_override = _paint(Color(0.76, 0.68, 0.52))
	bed.add_child(sand)
	bed.position = Vector3(0.0, -DEPTH - 0.5, 0.0)
	add_child(bed)
	var cam := Camera3D.new()
	cam.fov = 60.0
	cam.position = Vector3(0.0, 1.3, 7.8)
	cam.rotation_degrees = Vector3(-9, 0, 0)
	add_child(cam)
	var layer := CanvasLayer.new()
	add_child(layer)
	var title := Label.new()
	title.text = "Made Of: the same block in nine materials. One dropdown sets each; the floating comes from that alone."
	title.position = Vector2(24, 14)
	title.add_theme_color_override("font_color", INK)
	title.add_theme_font_size_override("font_size", 20)
	layer.add_child(title)
	var keys := Label.new()
	keys.text = "B: blocks or balls     Space: run again     Tab: next scene"
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
		elif event.keycode == KEY_B:
			_balls = not _balls
			_reset()
		elif event.keycode == KEY_TAB:
			get_tree().change_scene_to_file(NEXT_SCENE)


func _physics_process(dt: float) -> void:
	_t += dt
	for i in _bodies.size():
		var row: Array = _bodies[i]
		var b: RigidBody3D = row[0]
		var label: Label3D = row[1]
		var kind: int = row[2]
		var dens := MAT.density_of(kind)
		var state := ""
		if dens > _water.density:
			state = "sinks"
		elif b.volume > 0.0:
			state = "%.0f%% under" % (100.0 * b.submerged_volume / b.volume)
		label.text = "%s\n%.0f kg/m³\n%s" % [MAT.name_of(kind), dens, state]
		var top := 0.75 + 0.5 * float(i % 2)   # every other one higher, so neighbours do not overlap
		label.global_position = Vector3(b.global_position.x, top, b.global_position.z)


func _reset() -> void:
	for row in _bodies:
		row[0].queue_free()
		row[1].queue_free()
	_bodies.clear()
	_t = 0.0
	var n := KINDS.size()
	for i in n:
		var x := (float(i) - 0.5 * float(n - 1)) * 1.25
		_bodies.append(_drop(KINDS[i][0], KINDS[i][1], Vector3(x, 1.2 + 0.15 * float(i % 3), 0.0)))


func _drop(kind: int, colour: Color, at: Vector3) -> Array:
	var b := BB.new()
	var cs := CollisionShape3D.new()
	var mi := MeshInstance3D.new()
	if _balls:
		var s := SphereShape3D.new()
		s.radius = 0.5 * SIZE
		cs.shape = s
		var m := SphereMesh.new()
		m.radius = 0.5 * SIZE
		m.height = SIZE
		mi.mesh = m
	else:
		var s := BoxShape3D.new()
		s.size = Vector3.ONE * SIZE
		cs.shape = s
		var m := BoxMesh.new()
		m.size = s.size
		mi.mesh = m
	var paint := _paint(colour)
	if kind == MAT.Kind.ICE:
		paint.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
		paint.albedo_color.a = 0.92
		paint.roughness = 0.1
	elif kind == MAT.Kind.STEEL:
		paint.metallic = 0.8
		paint.roughness = 0.35
	mi.material_override = paint
	b.add_child(cs)
	b.add_child(mi)
	b.made_of = kind
	b.position = at
	b.rotation_degrees = Vector3(0.0, 8.0 * float(kind % 3) - 8.0, 4.0 * float(kind % 2))
	add_child(b)
	var label := Label3D.new()
	label.font_size = 34
	label.outline_size = 12
	label.modulate = INK
	label.outline_modulate = Color(1, 1, 1, 0.8)
	label.pixel_size = 0.0055
	label.no_depth_test = true
	label.render_priority = 10
	label.outline_render_priority = 9
	label.billboard = BaseMaterial3D.BILLBOARD_ENABLED
	add_child(label)
	return [b, label, kind]


func _paint(colour: Color) -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	m.albedo_color = colour
	m.roughness = 0.8
	return m
