extends Node3D
## CANNONS: a ship fires broadsides at another across the water. Fired low, round shot skips off the
## water, as gunners' ricochet fire did, splashing at every touch; fired higher it plunges in. Each gun
## kicks the ship back as it fires, so a broadside heels it. Shot that reaches the other ship strikes
## its hull (continuous collision: it flies 8 m a step) and rocks it.
##   Space   fire a broadside (it fires by itself every few seconds too)
##   Up/Down raise or lower the guns
##   S       slow motion
##   R       run again
##   Tab     next scene
## Everything here is ordinary Bounded Buoyancy bodies, BoundedBuoyancyCannon nodes on the ship, and
## the BoundedBuoyancyCannonball shot they fire. The smoke and the tall splashes are for show, and the
## shot is drawn three times its size so that it can be seen. Run with -- --slow to start slowed down.

const BB := preload("res://addons/bounded_buoyancy/bounded_buoyancy_body.gd")
const BW := preload("res://addons/bounded_buoyancy/bounded_buoyancy_water.gd")
const CANNON := preload("res://addons/bounded_buoyancy/bounded_buoyancy_cannon.gd")
const NEXT_SCENE := "res://addons/bounded_buoyancy/demo/demo_2d.tscn"

const INK := Color(0.12, 0.14, 0.18)
const HULL := Color(0.42, 0.27, 0.16)
const ENEMY := Color(0.30, 0.22, 0.18)
const DECK := Color(0.78, 0.66, 0.46)
const GAP := 90.0           # m between the ships
const GUNS := 5
const EVERY := 6.0          # s between broadsides fired by themselves

var _ship: RigidBody3D
var _enemy: RigidBody3D
var _guns: Array = []
var _elevation := -2.0
var _t := 0.0
var _next := 0.8
var _ripple: Array = []     # guns still to fire in this broadside, [gun, time]
var _shots: Array = []
var _hits := 0
var _status: Label
var _smoke_mesh: SphereMesh
var _smoke_mat: StandardMaterial3D
var _spray_mat: StandardMaterial3D


func _ready() -> void:
	var env := WorldEnvironment.new()
	var e := Environment.new()
	e.background_mode = Environment.BG_COLOR
	e.background_color = Color(0.82, 0.9, 0.97)
	e.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	e.ambient_light_color = Color(1, 1, 1)
	e.ambient_light_energy = 0.55
	env.environment = e
	add_child(env)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-45, -30, 0)
	add_child(sun)
	var water := BW.new()
	water.size = Vector3(900, 400, 900)
	water.wave_height = 0.04
	water.wave_length = 9.0
	water.wave_direction = 20.0
	water.splash_min_speed = 1.0
	water.surface_color = Color(0.13, 0.33, 0.52, 0.85)
	add_child(water)
	var cam := Camera3D.new()
	cam.fov = 55.0
	add_child(cam)
	cam.look_at_from_position(Vector3(-25, 14, 25), Vector3(GAP * 0.45, 0.0, -4), Vector3.UP)
	if "--slow" in OS.get_cmdline_user_args():
		Engine.time_scale = 0.25
	var layer := CanvasLayer.new()
	add_child(layer)
	var title := Label.new()
	title.text = "Broadside: round shot skips off the water at shallow angles, and plunges in at steep ones"
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
	keys.text = "Space: fire     Up/Down: raise or lower the guns     S: slow motion     R: run again     Tab: next scene"
	keys.anchor_top = 1.0
	keys.anchor_bottom = 1.0
	keys.offset_left = 24
	keys.offset_top = -38
	keys.add_theme_color_override("font_color", Color(0.9, 0.94, 1.0))
	keys.add_theme_font_size_override("font_size", 16)
	layer.add_child(keys)
	_smoke_mesh = SphereMesh.new()
	_smoke_mesh.radius = 0.5
	_smoke_mesh.height = 1.0
	_smoke_mesh.radial_segments = 8
	_smoke_mesh.rings = 4
	_smoke_mat = StandardMaterial3D.new()
	_smoke_mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	_smoke_mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	_smoke_mat.albedo_color = Color(0.62, 0.62, 0.64, 0.55)
	_smoke_mat.render_priority = 10   # over the water's surface, which is drawn see-through too
	_spray_mat = StandardMaterial3D.new()
	_spray_mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	_spray_mat.albedo_color = Color(0.96, 0.98, 1.0)
	_reset()


func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo:
		if event.keycode == KEY_SPACE:
			_broadside()
		elif event.keycode == KEY_UP:
			_elevation = minf(_elevation + 1.0, 10.0)
			_aim()
		elif event.keycode == KEY_DOWN:
			_elevation = maxf(_elevation - 1.0, -2.0)
			_aim()
		elif event.keycode == KEY_S:
			Engine.time_scale = 0.2 if Engine.time_scale > 0.5 else 1.0
		elif event.keycode == KEY_R:
			_reset()
		elif event.keycode == KEY_TAB:
			Engine.time_scale = 1.0
			get_tree().change_scene_to_file(NEXT_SCENE)


func _physics_process(dt: float) -> void:
	_t += dt
	if _t >= _next:
		_next = _t + EVERY
		_broadside()
	for r in _ripple.duplicate():
		if _t >= r[1]:
			_ripple.erase(r)
			r[0].fire()
	var skips := 0
	var shown := 0
	for s in _shots.duplicate():
		if not is_instance_valid(s):
			_shots.erase(s)
			continue
		skips = maxi(skips, s.skips)
		shown += 1
	var heel := rad_to_deg(asin(clampf(_ship.global_transform.basis.x.normalized().y, -1.0, 1.0)))
	var heel_enemy := rad_to_deg(asin(clampf(_enemy.global_transform.basis.x.normalized().y, -1.0, 1.0)))
	_status.text = "Guns at %+.0f°, 12-pounders at 488 m/s.   Most skips by one shot: %d.   Hits: %d.   Heel: this ship %+.1f°, the other %+.1f°" % [
		_elevation, skips, _hits, heel, heel_enemy]


func _reset() -> void:
	for n in [_ship, _enemy]:
		if n and is_instance_valid(n):
			n.queue_free()
	for s in _shots:
		if is_instance_valid(s):
			s.queue_free()
	_shots.clear()
	_ripple.clear()
	_guns.clear()
	_hits = 0
	_t = 0.0
	_next = 0.8
	_ship = _make_ship(Vector3(0, 0, 0), HULL, true)
	_enemy = _make_ship(Vector3(GAP, 0, 0), ENEMY, false)
	_aim()


## A ship 14 m long and 4 m wide with high sides, along Z, ballasted low; with guns on its
## starboard (+X) side if armed.
func _make_ship(at: Vector3, colour: Color, armed: bool) -> RigidBody3D:
	var b := BB.new()
	var hull := ConvexPolygonShape3D.new()
	hull.points = PackedVector3Array([
		Vector3(-2.0, 2.2, -6.0), Vector3(2.0, 2.2, -6.0), Vector3(-2.0, 2.2, 6.0), Vector3(2.0, 2.2, 6.0), Vector3(0, 2.2, -8.0),
		Vector3(-1.6, -1.0, -5.5), Vector3(1.6, -1.0, -5.5), Vector3(-1.6, -1.0, 5.6), Vector3(1.6, -1.0, 5.6), Vector3(0, -0.6, -7.4)])
	var cs := CollisionShape3D.new()
	cs.shape = hull
	b.add_child(cs)
	var mi := MeshInstance3D.new()
	mi.mesh = hull.get_debug_mesh()
	var hm := StandardMaterial3D.new()
	hm.albedo_color = colour
	hm.roughness = 0.9
	mi.material_override = hm
	b.add_child(mi)
	var deck := MeshInstance3D.new()
	var dm := BoxMesh.new()
	dm.size = Vector3(3.95, 0.05, 11.9)
	deck.mesh = dm
	deck.position = Vector3(0, 2.2, 0)
	var deck_mat := StandardMaterial3D.new()
	deck_mat.albedo_color = DECK
	deck.material_override = deck_mat
	b.add_child(deck)
	var mast := MeshInstance3D.new()
	var mm := CylinderMesh.new()
	mm.top_radius = 0.12
	mm.bottom_radius = 0.16
	mm.height = 9.0
	mast.mesh = mm
	mast.position = Vector3(0, 6.7, -1.0)
	mast.material_override = hm
	b.add_child(mast)
	b.density = 330.0
	b.angular_drag = 1.5
	b.position = at
	add_child(b)
	b.center_of_mass = Vector3(0, -0.3, 0)   # ballast low in the hold
	if armed:
		var barrel := CylinderMesh.new()
		barrel.top_radius = 0.09
		barrel.bottom_radius = 0.13
		barrel.height = 1.8
		var iron := StandardMaterial3D.new()
		iron.albedo_color = Color(0.1, 0.1, 0.11)
		iron.metallic = 0.6
		for i in GUNS:
			var gun: Node3D = CANNON.new()
			gun.gun = CANNON.Gun.POUNDER_12
			gun.spread = 0.4
			gun.shot_lifetime = 12.0
			gun.shot_parent = self
			gun.position = Vector3(2.1, 1.5, -4.0 + 2.0 * i)
			b.add_child(gun)
			var look := MeshInstance3D.new()
			look.mesh = barrel
			look.material_override = iron
			look.rotation_degrees = Vector3(90, 0, 0)
			look.position = Vector3(0, 0, 0.7)
			gun.add_child(look)
			gun.fired.connect(_on_fired.bind(gun))
			_guns.append(gun)
	return b


func _aim() -> void:
	for gun in _guns:
		gun.rotation_degrees = Vector3(_elevation, -90.0, 0.0)


func _broadside() -> void:
	for i in _guns.size():
		_ripple.append([_guns[i], _t + 0.15 * i])


func _on_fired(ball: RigidBody3D, gun: Node3D) -> void:
	_shots.append(ball)
	ball.hit.connect(func(body: Node, _at: Vector3, _v: Vector3) -> void:
		if body == _enemy:
			_hits += 1)
	ball.water_entered.connect(_plume)
	for child in ball.get_children():
		if child is MeshInstance3D:
			child.scale = Vector3.ONE * 3.0
	# Smoke and a flash, for show.
	var smoke := CPUParticles3D.new()
	smoke.mesh = _smoke_mesh
	smoke.material_override = _smoke_mat
	smoke.one_shot = true
	smoke.explosiveness = 0.9
	smoke.amount = 18
	smoke.lifetime = 3.0
	smoke.direction = -gun.global_transform.basis.z
	smoke.spread = 25.0
	smoke.initial_velocity_min = 2.0
	smoke.initial_velocity_max = 6.0
	smoke.damping_min = 2.0
	smoke.damping_max = 3.0
	smoke.gravity = Vector3(0, 0.3, 0)
	smoke.scale_amount_min = 1.0
	smoke.scale_amount_max = 2.4
	smoke.local_coords = false
	smoke.position = gun.global_position
	add_child(smoke)
	smoke.emitting = true
	var flash := OmniLight3D.new()
	flash.light_color = Color(1.0, 0.75, 0.4)
	flash.light_energy = 6.0
	flash.omni_range = 6.0
	flash.position = gun.global_position
	add_child(flash)
	get_tree().create_timer(0.08, false, true).timeout.connect(flash.queue_free)
	get_tree().create_timer(3.2, false, true).timeout.connect(smoke.queue_free)


## A column of spray where shot meets the water, taller the faster it hits. For show.
func _plume(speed: float, at: Vector3) -> void:
	var p := CPUParticles3D.new()
	p.mesh = _smoke_mesh
	p.material_override = _spray_mat
	p.one_shot = true
	p.explosiveness = 0.85
	p.amount = 60
	p.lifetime = 1.8
	p.direction = Vector3.UP
	p.spread = 12.0
	var up := clampf(0.04 * speed, 3.0, 14.0)
	p.initial_velocity_min = 0.4 * up
	p.initial_velocity_max = up
	p.gravity = Vector3(0, -9.8, 0)
	p.scale_amount_min = 0.3
	p.scale_amount_max = 0.8
	p.emission_shape = CPUParticles3D.EMISSION_SHAPE_SPHERE
	p.emission_sphere_radius = 0.3
	p.local_coords = false
	p.position = at   # this scene's root is at the world's origin
	add_child(p)
	p.emitting = true
	get_tree().create_timer(2.0, false, true).timeout.connect(p.queue_free)
