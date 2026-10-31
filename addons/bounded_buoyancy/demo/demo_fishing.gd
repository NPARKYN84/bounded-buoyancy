extends Node3D
## FISHING: float fishing from a jetty on a lake. Cast the float out; the shot under it cocks it, so
## that only its tip shows. Fish come to the bait and nibble at it, and each nibble dips the float;
## then one takes it and the float slides away under. Strike (sweep the rod back) while it has the
## bait and it is hooked; wait and it drops the bait. A hooked fish runs, veers and dives; the rod
## bends with the pull, and the reel's drag gives line when the pull is more than the drag is set to,
## so a fish that runs hard takes line instead of breaking it. A fish played on a tight line tires;
## let the line go slack and it throws the hook. Reel it in to the jetty to land it.
##   Space   hold to wind up, let go to cast; with a fish on, nothing
##   E       strike (or right mouse button)
##   W/S     reel in / give line (or mouse wheel)
##   +/-     set the drag tighter or looser
##   F       catapult a pouch of loose feed (pellets) out round the float, to draw the fish in
##   A       let the angler fish by himself
##   C       change the camera (following the float, on the jetty, under the water by the hook)
##   R       start again
##   Tab     next scene
## The float, the shot and the fish are ordinary Bounded Buoyancy bodies; the fish (perch, trout and
## bass) are BoundedBuoyancyFish, the loose feed small sinking BoundedBuoyancyBody pellets in the group
## "bounded_buoyancy_feed" (fish smell them from further off than the hook, eat them, then stay round the
## spot keener to bite), the line BoundedBuoyancyLine (0.25 mm nylon, about 3 kg breaking
## strain). The rod is drawn bending with the pull on the line, and its tip moves with it; the angler's
## hands follow the rod and the reel's handle, which turns as line is wound in. The float is drawn
## twice as wide as it is, and the hook and worm half as big again, so that they can be seen. The
## fish, the tackle and the angler are drawn by demo_tackle.gd.

const BW := preload("res://addons/bounded_buoyancy/bounded_buoyancy_water.gd")
const BB := preload("res://addons/bounded_buoyancy/bounded_buoyancy_body.gd")
const LINE := preload("res://addons/bounded_buoyancy/bounded_buoyancy_line.gd")
const FISH := preload("res://addons/bounded_buoyancy/bounded_buoyancy_fish.gd")
const LOOK := preload("res://addons/bounded_buoyancy/demo/demo_look.gd")
const TACKLE := preload("res://addons/bounded_buoyancy/demo/demo_tackle.gd")
const NEXT_SCENE := "res://addons/bounded_buoyancy/demo/demo_submarine.tscn"

const DECK := 0.8               # m: the jetty's deck above the water
const HAND := Vector3(0.45, 1.35, -0.3)   # where the angler holds the rod
const ROD := 3.6                # m long
const ROD_SEGMENTS := 14
const ROD_STIFF := 9.0          # N for each radian the rod bends
const REST_PITCH := 38.0        # degrees: the rod held up while fishing
const STRIKE_PITCH := 66.0      # and swept back to strike
const FLOAT_LONG := 0.25        # m: the float
const FLOAT_WIDE := 0.02
const FLOAT_DENSITY := 280.0
const TIP_SHOWING := 0.025      # m of the float above the water once shotted
const DEPTH := 1.3              # m from the float to the hook
const MAIN := 0.00025           # m: the line's diameter
const BREAKS := 30.0            # N: and its breaking strength
const REEL_SPEED := 0.7         # m/s
const FISH_COUNT := 5
const VIEWS := 3

var _rng := RandomNumberGenerator.new()
var _water: Node3D
var _cam: Camera3D
var _view := 0
var _look_at := Vector3(0, 0, -10)
var _env: Environment
var _sky_fog := 0.0
var _rod_parts: Array = []
var _tip: Node3D
var _float: RigidBody3D
var _hook: RigidBody3D
var _main: Node3D
var _snood: Node3D
var _fish: Array = []
var _pitch := REST_PITCH
var _pitch_to := REST_PITCH
var _bend := 0.0                # radians, smoothed
var _drag := 12.0               # N: the reel's drag
var _casting := false
var _power := 0.0
var _in_flight := false
var _cast_out := false
var _on: Node3D = null          # the hooked fish
var _auto := false
var _auto_wait := 0.0
var _aim := INF                 # where across he aims the next cast (INF: anywhere near straight out)
var _pellets: Array = []        # loose feed in the water, each [body, seconds left]
var _fed_ago := 999.0           # s since he last fed the swim (fishing by himself)
var _tackle_gen := 0            # changes with each new tackle, so a late replacement of old tackle is let be
var _caught := 0
var _caught_kg := 0.0
var _status: Label
var _info: Label
var _bar: ProgressBar
var _drag_mark: ColorRect
var _message := ""
var _message_time := 0.0
var _reel_look: Node3D
var _angler: Dictionary
var _wound := 0.0               # line wound in, for turning the reel's handle
var _last_length := 0.0


func _ready() -> void:
	_rng.seed = 11
	LOOK.sea_and_sky(self, Vector2(-24.0, -150.0), 0.004)
	_env = (get_child(0) as WorldEnvironment).environment
	_env.fog_light_color = Color(0.70, 0.78, 0.82)
	_sky_fog = _env.fog_density
	_water = BW.new()
	_water.size = Vector3(400, 12, 400)
	_water.wave_height = 0.012
	_water.wave_length = 2.5
	_water.wave_direction = 35.0
	_water.splash_min_speed = 0.6
	_water.surface_color = Color(0.10, 0.24, 0.20, 0.72)
	_water.surface_detail = 0.6
	_water.foam = 0.2
	add_child(_water)
	_scenery()
	_make_rod()
	_cam = Camera3D.new()
	_cam.fov = 60.0
	_cam.near = 0.03
	add_child(_cam)
	_hud()
	_reset()
	if "--auto" in OS.get_cmdline_user_args():
		_auto = true


func _reset() -> void:
	_tackle_gen += 1
	for f in _fish:
		if is_instance_valid(f):
			f.queue_free()
	_fish.clear()
	for n in [_float, _hook]:
		if n and is_instance_valid(n):
			n.queue_free()
	if _main and is_instance_valid(_main):
		_main.queue_free()
	_on = null
	_in_flight = false
	_cast_out = false
	_casting = false
	for p in _pellets:
		if is_instance_valid(p[0]):
			p[0].queue_free()
	_pellets.clear()
	_pitch = REST_PITCH
	_pitch_to = REST_PITCH
	_bend = 0.0
	_place_rod()
	_make_tackle()
	for i in FISH_COUNT:
		_add_fish()
	_say("Hold Space and let go to cast.", 4.0)


# --- The tackle -------------------------------------------------------------------------------

## The float (a stick float, shotted down to its tip), the hook with its shot under it, and the line
## from the rod's tip to the float, hanging from the rod ready to cast.
func _make_tackle() -> void:
	var tip := _tip.global_position
	_float = BB.new()
	var cs := CollisionShape3D.new()
	var cyl := CylinderShape3D.new()
	cyl.radius = FLOAT_WIDE * 0.5
	cyl.height = FLOAT_LONG
	cs.shape = cyl
	_float.add_child(cs)
	_float.density = FLOAT_DENSITY
	_float.collision_layer = 0
	_float.collision_mask = 0
	_float.angular_drag = 6.0
	_float.position = tip + Vector3(0, -1.0, 0)
	_float.add_child(TACKLE.float_mesh(FLOAT_LONG, FLOAT_WIDE))
	add_child(_float)
	# The shot that cocks it: enough that it floats with only TIP_SHOWING out, less its own weight.
	var area := PI * FLOAT_WIDE * FLOAT_WIDE * 0.25
	var spare := 1000.0 * area * (FLOAT_LONG - TIP_SHOWING) - FLOAT_DENSITY * area * FLOAT_LONG
	var lead := spare / (11340.0 - 1000.0)
	var r := pow(lead * 3.0 / (4.0 * PI), 1.0 / 3.0)
	_hook = BB.new()
	var hs := CollisionShape3D.new()
	var sph := SphereShape3D.new()
	sph.radius = r
	hs.shape = sph
	_hook.add_child(hs)
	_hook.density = 11340.0
	_hook.collision_layer = 0
	_hook.collision_mask = 0
	_hook.position = _float.position + Vector3(0, -0.5 * FLOAT_LONG - DEPTH, 0)
	_hook.add_to_group(&"bounded_buoyancy_bait")
	TACKLE.ball(_hook, Vector3.ZERO, r * 1.6, TACKLE.mat(Color(0.35, 0.36, 0.38), 0.4, 0.6))
	TACKLE.hook_and_worm(_hook, r * 1.6)
	add_child(_hook)
	_snood = LINE.new()
	_snood.position = Vector3(0, -0.5 * FLOAT_LONG, 0)
	_snood.body = _hook
	_snood.length = DEPTH
	_snood.diameter = 0.0002
	_snood.breaking_strength = 25.0
	_snood.colour = Color(0.85, 0.88, 0.85, 0.7)
	_float.add_child(_snood)
	_main = LINE.new()
	_main.body = _float
	_main.end_offset = Vector3(0, 0.5 * FLOAT_LONG, 0)
	_main.length = 1.0
	_main.diameter = MAIN
	_main.damping = 0.5
	_main.breaking_strength = BREAKS
	_main.slip_tension = _drag
	_main.colour = Color(1.0, 0.86, 0.18, 0.95)   # a bright line, to be seen
	for l in [_main, _snood]:
		l.snapped.connect(func(_t: float) -> void:
			_say("Snap! The %s broke." % ("line" if l == _main else "hook length"), 4.0)
			var gen := _tackle_gen
			get_tree().create_timer(3.0, false, true).timeout.connect(func() -> void:
				if gen == _tackle_gen:
					_new_tackle()))
	_tip.add_child(_main)


# --- The fish ---------------------------------------------------------------------------------

## A fish somewhere out from the jetty, a perch, a trout or a bass: 20 to 50 cm long, as strong as
## its size; trout dash hardest, bass pull hardest.
func _add_fish() -> void:
	var f: RigidBody3D = FISH.new()
	var kind := _rng.randi_range(0, 2)
	var long := _rng.randf_range(0.2, 0.34) if kind == TACKLE.PERCH else _rng.randf_range(0.3, 0.5)
	var cs := CollisionShape3D.new()
	var cap := CapsuleShape3D.new()
	cap.radius = 0.11 * long
	cap.height = long
	cs.shape = cap
	cs.rotation_degrees = Vector3(90, 0, 0)
	f.add_child(cs)
	f.collision_layer = 0
	f.collision_mask = 0
	# Out where the float lands, so that one finds the bait before long.
	var at := Vector3(_rng.randf_range(-6, 6), -_rng.randf_range(1.0, 2.5), _rng.randf_range(-20, -9))
	f.position = at
	f.rotation.y = _rng.randf() * TAU
	f.mouth = Vector3(0, 0, -0.5 * long)
	f.cruise_speed = 1.0 * long
	f.burst_speed = (8.5 if kind == TACKLE.TROUT else 7.0) * long
	var kg := 1000.0 * PI * pow(0.11 * long, 2) * long * 0.85
	f.max_thrust = (10.0 if kind == TACKLE.BASS else 8.0) * kg
	f.endurance = (6.0 if kind == TACKLE.TROUT else 8.0) + 10.0 * kg
	f.set_meta(&"kind", kind)
	f.bite_pull = 0.08
	f.appetite = 0.08
	f.min_depth = 0.6
	f.max_depth = 3.0
	f.home_range = 6.0
	f.sense_range = 5.0
	f.random_seed = _rng.randi() | 1
	var look := TACKLE.fish_mesh(kind, long)
	f.add_child(look)
	f.set_meta(&"look", look.material_override)
	add_child(f)
	f.took_bait.connect(func(_b: Node3D) -> void:
		if not _on:
			_say("The float's going under! Strike! (E)", 2.0))
	f.nibbled.connect(func(_b: Node3D) -> void:
		if not _on and _message_time <= 0.0:
			_say("A nibble...", 1.5))
	f.dropped_bait.connect(func(_b: Node3D) -> void:
		_say("Missed it: it dropped the bait.", 2.5))
	f.hooked.connect(func() -> void:
		_on = f
		_say("Fish on! Keep the line tight; reel when it tires.", 3.0))
	f.tired.connect(func() -> void:
		if _on == f:
			_say("It's tiring: reel in!", 2.5))
	f.escaped.connect(func() -> void:
		if _on == f:
			_on = null
			_pitch_to = REST_PITCH
			_say("It threw the hook." if not _main.broken else "Gone, with the float.", 3.0))
	_fish.append(f)


# --- The rod ----------------------------------------------------------------------------------

func _make_rod() -> void:
	for i in ROD_SEGMENTS:
		var m := MeshInstance3D.new()
		var cm := CylinderMesh.new()
		var t0 := float(i) / ROD_SEGMENTS
		var t1 := float(i + 1) / ROD_SEGMENTS
		cm.bottom_radius = _rod_radius(t0 * ROD)
		cm.top_radius = _rod_radius(t1 * ROD)
		cm.height = ROD / ROD_SEGMENTS
		cm.radial_segments = 12
		cm.rings = 1
		m.mesh = cm
		add_child(m)
		_rod_parts.append(m)
	_tip = Node3D.new()
	add_child(_tip)
	_reel_look = TACKLE.dress_rod(_rod_parts, ROD / ROD_SEGMENTS, _rod_radius)
	# The angler on his seat box, just behind the rod's butt.
	_angler = TACKLE.angler(self, Vector3(HAND.x - 0.12, DECK + 0.38, HAND.z + 0.18))
	TACKLE.tackle_box(self, Vector3(-0.55, DECK + 0.025, -0.15), 0.35)


## The rod's radius `along` metres from the butt: a thick cork handle, then the blank tapering to the tip.
func _rod_radius(along: float) -> float:
	if along < 0.52:
		return 0.0135
	return lerpf(0.0085, 0.0018, (along - 0.52) / (ROD - 0.52))


## Lay the rod out from the angler's hands, at its pitch, bent toward the line by the pull on it:
## the tip bends most. Its tip is where the line is made fast.
func _place_rod() -> void:
	var dir := Vector3(0, sin(deg_to_rad(_pitch)), -cos(deg_to_rad(_pitch)))
	var pull := Vector3.DOWN
	if _main and is_instance_valid(_main) and _main.body and is_instance_valid(_main.body):
		var e: Array = _main.ends()
		pull = (e[1] - e[0]).normalized()
	var total := 0.0
	for i in ROD_SEGMENTS:
		total += pow(i + 1, 2)
	var p := HAND
	var d := dir
	var room := d.angle_to(pull)
	var bend := minf(_bend, room)
	var axis := d.cross(pull)
	if axis.length() < 1e-5:
		axis = Vector3.RIGHT
	axis = axis.normalized()
	var piece := ROD / ROD_SEGMENTS
	for i in ROD_SEGMENTS:
		d = d.rotated(axis, bend * pow(i + 1, 2) / total)
		var m: MeshInstance3D = _rod_parts[i]
		var mid := p + d * (0.5 * piece)
		var x := Vector3.UP.cross(d) if absf(d.y) < 0.99 else Vector3.RIGHT
		x = x.normalized()
		m.global_transform = Transform3D(Basis(x, d, x.cross(d)).orthonormalized(), mid)
		p += d * piece
	_tip.global_position = p
	# The handle turns as line is wound in; his left hand on its knob, his right on the rod above the reel.
	(_reel_look.get_node("Handle") as Node3D).rotation.x = -_wound * 18.0
	(_reel_look.get_node("Rotor") as Node3D).rotation.y = -_wound * 90.0
	var seat: Node3D = _rod_parts[1]
	var right_hand := seat.global_transform * Vector3(0.0, 0.41 - 1.5 * ROD / ROD_SEGMENTS, -0.01)
	TACKLE.pose_arms(_angler, (_reel_look.get_node("Handle/Knob") as Node3D).global_position, right_hand)


# --- Playing ----------------------------------------------------------------------------------

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and not event.echo:
		if event.keycode == KEY_SPACE:
			if event.pressed and not _cast_out and not _in_flight:
				_casting = true
				_power = 0.0
			elif not event.pressed and _casting:
				_cast()
		elif event.pressed and event.keycode == KEY_E:
			_strike()
		elif event.pressed and (event.keycode == KEY_PLUS or event.keycode == KEY_EQUAL or event.keycode == KEY_KP_ADD):
			_drag = minf(_drag + 2.0, 40.0)
			_main.slip_tension = _drag
		elif event.pressed and (event.keycode == KEY_MINUS or event.keycode == KEY_KP_SUBTRACT):
			_drag = maxf(_drag - 2.0, 2.0)
			_main.slip_tension = _drag
		elif event.pressed and event.keycode == KEY_F:
			_feed_swim()
		elif event.pressed and event.keycode == KEY_A:
			_auto = not _auto
			_say("The angler fishes by itself." if _auto else "Over to you.", 2.0)
		elif event.pressed and event.keycode == KEY_C:
			_view = (_view + 1) % VIEWS
		elif event.pressed and event.keycode == KEY_R:
			_reset()
		elif event.pressed and event.keycode == KEY_TAB:
			get_tree().change_scene_to_file(NEXT_SCENE)
	elif event is InputEventMouseButton and event.pressed:
		if event.button_index == MOUSE_BUTTON_RIGHT:
			_strike()
		elif event.button_index == MOUSE_BUTTON_WHEEL_UP:
			_reel(-0.15)
		elif event.button_index == MOUSE_BUTTON_WHEEL_DOWN:
			_reel(0.15)


## Cast: the rod comes forward and the float and its shot fly out, the line running off the reel.
func _cast() -> void:
	_casting = false
	if _main.broken:
		return
	var speed := lerpf(6.0, 15.0, _power)
	var across := _aim if _aim != INF else _rng.randf_range(-0.12, 0.12)
	_aim = INF
	var aim := Vector3(across, 0.55, -1.0).normalized()
	_float.linear_velocity = aim * speed
	_hook.linear_velocity = aim * speed * 0.97
	_pitch = 15.0
	_pitch_to = REST_PITCH
	_in_flight = true


## Strike: sweep the rod back, which snatches the line tight.
func _strike() -> void:
	if not _cast_out or _main.broken:
		return
	_pitch_to = STRIKE_PITCH
	get_tree().create_timer(0.6, false, true).timeout.connect(func() -> void:
		_pitch_to = REST_PITCH + (20.0 if _on else 0.0))


func _reel(metres: float) -> void:
	if _cast_out and not _main.broken:
		_main.length = maxf(_main.length + metres, 1.0)


func _physics_process(dt: float) -> void:
	if _casting:
		_power = minf(_power + dt, 1.0)
		_pitch_to = REST_PITCH + 45.0 * _power
	_pitch = move_toward(_pitch, _pitch_to, (300.0 if _pitch_to > _pitch else 260.0) * dt)
	# The rod bends with the pull on it, and springs back.
	var want := minf((_main.tension if is_instance_valid(_main) else 0.0) / ROD_STIFF, 1.4)
	_bend = lerpf(_bend, want, 1.0 - exp(-dt / 0.06))
	_place_rod()
	if not is_instance_valid(_main) or not is_instance_valid(_float):
		return
	var e: Array = _main.ends()
	var span: float = (e[1] - e[0]).length()
	if _in_flight:
		# The bail is open: line runs off the reel as fast as the float pulls it.
		_main.length = maxf(_main.length, span + 2.0)
		if _float.submerged_volume > 0.0:
			_in_flight = false
			_cast_out = true
			_main.length = span + 0.3
	elif _cast_out and not _main.broken:
		if Input.is_key_pressed(KEY_W):
			_main.length = maxf(_main.length - REEL_SPEED * dt, 1.0)
		elif Input.is_key_pressed(KEY_S):
			_main.length += REEL_SPEED * dt
		_wound += maxf(_last_length - _main.length, 0.0)
		# Reeled right in: the float is back at the rod, ready to cast again.
		if _main.length <= 1.05 and not _on:
			_cast_out = false
	_last_length = _main.length
	if _main.broken and _on == null:
		_cast_out = false
	_land()
	_age_pellets(dt)
	_fed_ago += dt
	if _auto:
		_angle(dt)
	_hud_update(dt)


## A hooked fish brought to the jetty, tired and near the surface, is landed (with a net on a pole).
func _land() -> void:
	if _on == null or not is_instance_valid(_on):
		_on = null
		return
	var fp: Vector3 = _on.global_position
	var near := Vector2(fp.x - HAND.x, fp.z - HAND.z).length()
	if near < 4.2 and fp.y > -0.8 and _on.state == FISH.State.TIRED:
		var kg: float = _on.own_mass
		_caught += 1
		_caught_kg += kg
		_say("Landed a %s! %.2f kg. Hold Space to cast again." % [TACKLE.NAMES[_on.get_meta(&"kind", 0)], kg], 5.0)
		_auto_wait = 4.0
		var f := _on
		_on = null
		_fish.erase(f)
		f.queue_free()
		_add_fish()
		_new_tackle()


## New tackle, hanging from the rod.
func _new_tackle() -> void:
	_tackle_gen += 1
	_pitch_to = REST_PITCH
	for n in [_float, _hook, _main]:
		if n and is_instance_valid(n):
			n.queue_free()
	_cast_out = false
	_in_flight = false
	_make_tackle.call_deferred()


## The catapult: a pouch of loose feed, a dozen 10 mm soft pellets, fired out to land round the float (or
## where he means to cast). Each is a little sinking body: it flies, splashes in, and sinks slowly through
## the water, where the fish smell it from further off than the hook. Slow-sinking feed, as anglers
## loose-feed with: hard pellets (1150 kg/m³) dropped through the fish's depths in 20 s, before most of
## them had found it.
const PELLETS := 12
const PELLET_RADIUS := 0.005
const PELLET_DENSITY := 1050.0
const PELLET_LIFE := 60.0
const LONG_AIM := 0.016        # aim this much further for each metre of the throw,
const LONG_AIM_MORE := 0.0007  # and more again on a long throw, where the air has longer at it

func _feed_swim() -> void:
	var at: Vector3
	if _cast_out and is_instance_valid(_float):
		at = _float.global_position
	else:
		var power: float = SPOTS[_spot][0]
		at = HAND + Vector3(SPOTS[_spot][1], 0.0, -1.0).normalized() * lerpf(5.0, 16.0, power)
	at.y = _water.height_at(at)
	var from := HAND + Vector3(0.1, 0.25, -0.2)
	var g := float(ProjectSettings.get_setting("physics/3d/default_gravity"))
	for i in PELLETS:
		var where := at + Vector3(_rng.randf_range(-1.0, 1.0), 0.0, _rng.randf_range(-1.0, 1.0))
		# Aimed long, as an angler learns to with a catapult, the more the further he throws: the air takes
		# a light pellet's way, and aimed true it fell three metres short of a float fourteen metres out.
		var d := where - from
		var throw := Vector2(d.x, d.z).length()
		var long_aim := 1.0 + throw * (LONG_AIM + LONG_AIM_MORE * throw)
		d.x *= long_aim
		d.z *= long_aim
		var t := 0.8 + 0.035 * Vector2(d.x, d.z).length()   # a lob, as a catapult throws
		var b := BB.new()
		var cs := CollisionShape3D.new()
		var sph := SphereShape3D.new()
		sph.radius = PELLET_RADIUS
		cs.shape = sph
		b.add_child(cs)
		b.density = PELLET_DENSITY
		b.collision_layer = 0
		b.collision_mask = 0
		b.position = from
		b.linear_velocity = Vector3(d.x / t, (d.y + 0.5 * g * t * t) / t, d.z / t)
		TACKLE.ball(b, Vector3.ZERO, PELLET_RADIUS * 2.5, TACKLE.mat(Color(0.55, 0.34, 0.16), 0.8, 0.0))   # drawn bigger, to be seen
		b.add_to_group(&"bounded_buoyancy_feed")
		add_child(b)
		_pellets.append([b, PELLET_LIFE, false])
	_fed_ago = 0.0
	_say("Feeding the swim: a pouch of pellets round the float.", 2.0)


## Pellets not eaten sink out of reach and are gone after a while.
func _age_pellets(dt: float) -> void:
	for i in range(_pellets.size() - 1, -1, -1):
		var p: Array = _pellets[i]
		p[1] -= dt
		if not is_instance_valid(p[0]):
			_pellets.remove_at(i)
			continue
		var at: Vector3 = p[0].global_position
		if not p[2] and at.y < _water.height_at(at):
			p[2] = true                  # in: the water makes its own small plop and ring for it
		if p[1] <= 0.0:
			p[0].queue_free()
			_pellets.remove_at(i)


## The angler fishing by himself, as a good angler fishes: by what he can see and feel, never by what the
## fish is up to. He casts to one of a few spots, lets the float settle, and winds in the slack so he is
## in touch with it. A dip, even right under,
## is a fish nibbling: he waits. The float sliding away across the water is a fish making off with the
## bait, or the float pulled under and kept there: he strikes, a moment later, as a person reacts. With a fish on he plays it by the pull: lets the
## drag give line while it pulls hard, reels while the pull eases, and reels steadily as it comes in
## tired. He sets the drag firmer if a fish takes a lot of line, and lighter as it comes close, where a
## last lunge on a short line could snap it. He feeds the swim with his catapult, a pouch of pellets round
## the float little and often, to draw fish in and keep them keen. With nothing doing for a while he tries
## another spot; with fish about (nibbles), he stays.
const SPOTS := [[0.55, 0.0], [0.8, -0.09], [0.7, 0.1], [0.95, 0.04], [0.45, -0.05]]   # [cast power, aim across]
const MOVING := 0.2          # m/s: the float going off across the water faster than this is being taken
const SEEN_FOR := 0.15       # s it must keep going before he is sure (a nibble stirs it, never this fast)
const HELD_UNDER := 0.9      # s: or the float pulled under and kept under this long (a nibble lets it up)
const PATIENCE := 50.0       # s without a sign of a fish before he tries another spot
const SETTLE := 3.0          # s for the float and line to settle after a cast or a strike before he reads it
const FEED_EVERY := 40.0     # s: he catapults a pouch of feed round the float, little and often

var _spot := 0
var _settled := 0.0           # s since the float came to rest out there
var _moving := 0.0            # s the float has been going away
var _under := 0.0             # s the float has been under, running
var _react := -1.0            # s until he strikes, once he has seen it go
var _quiet := 0.0             # s since the last sign of a fish
var _line_going := 0.0        # s a hooked fish has been taking line


func _angle(dt: float) -> void:
	_auto_wait -= dt
	if not _cast_out and not _in_flight and not _casting and _auto_wait <= 0.0:
		# Cast to the spot he is fishing, giving the line a moment to settle before he watches it.
		_casting = true
		_power = 0.0
		_aim = SPOTS[_spot][1]
		_auto_wait = 1.0
		_settled = 0.0
		_moving = 0.0
		_react = -1.0
		var power: float = SPOTS[_spot][0]
		get_tree().create_timer(0.3 + 0.7 * power, false, true).timeout.connect(func() -> void:
			if _casting:
				_power = power
				_cast())
		return
	if not _cast_out:
		return
	if _on == null:
		_watch(dt)
		return
	_play(dt)


## Watching the float.
func _watch(dt: float) -> void:
	var f := _float
	var fp := f.global_position
	# Nothing for a long while, or the float drifted in close: he winds in (moving the float himself, so
	# he is not watching it for bites), and next time tries another spot.
	if _quiet > PATIENCE or Vector2(fp.x - HAND.x, fp.z - HAND.z).length() < 4.0:
		_main.length = maxf(_main.length - 2.0 * REEL_SPEED * dt, 1.0)
		if _main.length <= 1.05:
			_spot = (_spot + 1) % SPOTS.size()
			_quiet = 0.0
		return
	var tip := fp + f.global_transform.basis.y * (0.5 * FLOAT_LONG)
	# How fast the float goes off: away from him or across, not back towards him (as it does while the
	# line settles after a cast or a strike, which is no bite).
	var v := Vector3(f.linear_velocity.x, 0.0, f.linear_velocity.z)
	var to_him := Vector3(HAND.x - fp.x, 0.0, HAND.z - fp.z).normalized()
	var towards := v.dot(to_him)
	var speed := (v - to_him * towards).length() if towards > 0.0 else v.length()
	_settled += dt
	_quiet += dt
	var under: bool = tip.y < _water.height_at(tip) - 0.005
	_under = _under + dt if under else 0.0
	if _settled > SETTLE and under:
		_quiet = 0.0                 # a dip: a fish is about
	# In touch with the float: once it has settled he winds in the slack, so that a strike pulls the hook
	# home and does not only straighten loose line.
	if _settled > SETTLE:
		var e: Array = _main.ends()
		var span: float = (e[1] - e[0]).length()
		if _main.length > span + 0.4:
			_main.length = maxf(span + 0.3, _main.length - REEL_SPEED * dt)
	if _settled > SETTLE and _fed_ago > FEED_EVERY and _react < 0.0:
		_feed_swim()
	if _react >= 0.0:
		_react -= dt
		if _react < 0.0:
			_strike()
			_settled = 0.0           # the strike jerks the float about: let it settle before watching again
		return
	if _settled > SETTLE and _pitch_to == REST_PITCH:
		_moving = _moving + dt if speed > MOVING else 0.0
		if _moving >= SEEN_FOR or _under >= HELD_UNDER:
			_react = _rng.randf_range(0.2, 0.5)   # a person's reaction
			_moving = 0.0
			_under = 0.0
			_quiet = 0.0


## Playing a hooked fish by the pull on the line.
func _play(dt: float) -> void:
	var pull: float = _main.tension
	var out: float = _main.length
	if pull >= 0.85 * _drag:
		_line_going += dt            # running: the drag gives it line, he holds the rod up and waits
	else:
		_line_going = 0.0
		var rate := REEL_SPEED if pull < 0.5 * _drag else 0.5 * REEL_SPEED
		_main.length = maxf(out - rate * dt, 1.0)
	# The drag: firmer if it keeps taking line far out, lighter once it is close in.
	var want := _drag
	if _line_going > 3.0 and out > 20.0:
		want = minf(_drag + 2.0, 0.6 * BREAKS)
		_line_going = 0.0
	elif out < 8.0:
		want = minf(_drag, 0.35 * BREAKS)
	if want != _drag:
		_drag = want
		_main.slip_tension = _drag


# --- The view and the words --------------------------------------------------------------------

func _process(dt: float) -> void:
	# The fish beat their tails as hard as they swim.
	for f in _fish:
		if is_instance_valid(f):
			f.get_meta(&"look").set_shader_parameter("beat", f.effort)
	if not is_instance_valid(_float):
		return
	var target: Vector3 = _float.global_position
	if _on and is_instance_valid(_on):
		target = _on.global_position.lerp(_float.global_position, 0.4)
	elif not _cast_out:
		target = Vector3(0, 0, -12)
	_look_at = _look_at.lerp(target, 1.0 - exp(-dt / 0.8))
	var under := false
	var waiting := _cast_out and _on == null and not _in_flight
	if _view == 0 and waiting:
		# Close to the float, from the jetty's side, to see it dip.
		var fp: Vector3 = _float.global_position
		var back := Vector3(fp.x - HAND.x, 0.0, fp.z - HAND.z).normalized()
		var from := fp - back * 1.9 + Vector3(0.5, 0.45, 0.0)
		_cam.global_position = _cam.global_position.lerp(from, 1.0 - exp(-dt / 0.6))
		_cam.look_at(fp + Vector3(0, 0.05, 0), Vector3.UP)
	elif _view <= 1:
		var from := Vector3(-0.5, 2.6, 2.6)
		_cam.global_position = _cam.global_position.lerp(from, 1.0 - exp(-dt / 0.3)) if _view == 0 else from
		_cam.look_at(_look_at.lerp(Vector3(_look_at.x, 0.0, _look_at.z), 0.5), Vector3.UP)
	else:
		# Under the water, a little way from the hook, looking at it.
		var h: Vector3 = (_hook.global_position if is_instance_valid(_hook) else _look_at)
		if _on and is_instance_valid(_on):
			h = _on.global_position
		var from := h + Vector3(1.4, 0.15, 1.2)
		from.y = minf(from.y, -0.25)
		_cam.global_position = _cam.global_position.lerp(from, 1.0 - exp(-dt / 0.5))
		_cam.look_at(h, Vector3.UP)
		under = true
	# Under the water: the light is green and the far side lost in it.
	under = under and _cam.global_position.y < _water.height_at(_cam.global_position)
	_env.fog_density = 0.07 if under else _sky_fog
	_env.fog_sky_affect = 1.0 if under else 0.0
	_env.fog_aerial_perspective = 0.0 if under else 0.4
	_env.fog_light_color = Color(0.20, 0.40, 0.36) if under else Color(0.70, 0.78, 0.82)


func _say(text: String, seconds: float) -> void:
	_message = text
	_message_time = seconds


func _hud() -> void:
	var layer := CanvasLayer.new()
	add_child(layer)
	var title := Label.new()
	title.text = "Float fishing: cast, wait for a bite, strike, play the fish and land it"
	title.position = Vector2(24, 14)
	_outline(title, Color(1, 1, 1))
	title.add_theme_font_size_override("font_size", 20)
	layer.add_child(title)
	_status = Label.new()
	_status.position = Vector2(24, 44)
	_outline(_status, Color(1.0, 0.92, 0.6))
	_status.add_theme_font_size_override("font_size", 18)
	layer.add_child(_status)
	_info = Label.new()
	_info.position = Vector2(24, 72)
	_outline(_info, Color(0.93, 0.95, 0.98))
	_info.add_theme_font_size_override("font_size", 15)
	layer.add_child(_info)
	_bar = ProgressBar.new()
	_bar.position = Vector2(24, 100)
	_bar.size = Vector2(260, 12)
	_bar.max_value = BREAKS
	_bar.show_percentage = false
	layer.add_child(_bar)
	_drag_mark = ColorRect.new()
	_drag_mark.size = Vector2(3, 18)
	_drag_mark.color = Color(1, 0.85, 0.3)
	layer.add_child(_drag_mark)
	var keys := Label.new()
	keys.text = "Space: hold and let go to cast     E: strike     W/S: reel in / give line     +/-: drag     F: catapult feed     A: fish by itself     C: camera     R: start again     Tab: next scene"
	keys.anchor_top = 1.0
	keys.anchor_bottom = 1.0
	keys.offset_left = 24
	keys.offset_top = -38
	_outline(keys, Color(0.9, 0.94, 1.0))
	keys.add_theme_font_size_override("font_size", 15)
	layer.add_child(keys)


func _hud_update(dt: float) -> void:
	_message_time -= dt
	_status.text = _message if _message_time > 0.0 else ("Winding up... %d%%" % roundi(_power * 100.0) if _casting else "")
	var t: float = _main.tension if is_instance_valid(_main) else 0.0
	_bar.value = t
	_drag_mark.position = Vector2(24 + 260.0 * _drag / BREAKS - 1.0, 97)
	var fish_text := ""
	if _on and is_instance_valid(_on):
		fish_text = "     fish: %s, strength %d%%" % ["running" if _on.state == FISH.State.HOOKED else "tired", roundi(_on.stamina * 100.0)]
	_info.text = "Pull on the line %4.1f N (breaks at %d N), drag %d N     line out %.1f m%s     caught %d (%.2f kg)" % [t, int(BREAKS), int(_drag), _main.length if is_instance_valid(_main) else 0.0, fish_text, _caught, _caught_kg]


func _outline(label: Label, colour: Color) -> void:
	label.add_theme_color_override("font_color", colour)
	label.add_theme_color_override("font_outline_color", Color(0.05, 0.08, 0.12, 0.85))
	label.add_theme_constant_override("outline_size", 5)


# --- The lake and its shore -------------------------------------------------------------------

func _scenery() -> void:
	var wood := StandardMaterial3D.new()
	wood.albedo_color = Color(0.45, 0.34, 0.24)
	wood.roughness = 0.95
	var old_wood := StandardMaterial3D.new()
	old_wood.albedo_color = Color(0.30, 0.24, 0.18)
	old_wood.roughness = 1.0
	# The jetty: planks across, on posts.
	for i in 40:
		var plank := MeshInstance3D.new()
		var bm := BoxMesh.new()
		bm.size = Vector3(2.4, 0.05, 0.28)
		plank.mesh = bm
		plank.material_override = wood if i % 3 else old_wood
		plank.position = Vector3(0, DECK, 0.6 - i * 0.3)
		plank.position.z = -0.8 + i * 0.3
		plank.rotation.y = _rng.randf_range(-0.015, 0.015)
		add_child(plank)
	for z in [-0.8, 2.2, 5.2, 8.2, 11.2]:
		for x in [-1.1, 1.1]:
			var post := MeshInstance3D.new()
			var cm := CylinderMesh.new()
			cm.top_radius = 0.09
			cm.bottom_radius = 0.1
			cm.height = 4.0
			post.mesh = cm
			post.material_override = old_wood
			post.position = Vector3(x, DECK - 2.0 + 0.3, z)
			add_child(post)
	# The shore behind, and the far shore: grass, low hills, trees.
	var grass := StandardMaterial3D.new()
	grass.albedo_color = Color(0.26, 0.38, 0.18)
	grass.roughness = 1.0
	var bank := MeshInstance3D.new()
	var pm := PlaneMesh.new()
	pm.size = Vector2(400, 200)
	bank.mesh = pm
	bank.material_override = grass
	bank.position = Vector3(0, 0.25, 112)
	add_child(bank)
	var tree_look := StandardMaterial3D.new()
	tree_look.albedo_color = Color(0.13, 0.24, 0.12)
	tree_look.roughness = 1.0
	var hill_look := StandardMaterial3D.new()
	hill_look.albedo_color = Color(0.22, 0.32, 0.18)
	hill_look.roughness = 1.0
	for i in 26:
		var a := lerpf(-1.2, 1.2, i / 25.0)
		var hill := MeshInstance3D.new()
		var sm := SphereMesh.new()
		sm.radius = _rng.randf_range(30, 55)
		sm.height = sm.radius * 2.0
		hill.mesh = sm
		hill.material_override = hill_look
		hill.position = Vector3(sin(a) * 190.0, -sm.radius * 0.72, -cos(a) * 190.0)
		add_child(hill)
	for i in 160:
		var a := _rng.randf_range(-1.3, 1.3)
		var r := _rng.randf_range(150.0, 185.0)
		var tree := MeshInstance3D.new()
		var cm := CylinderMesh.new()
		var h := _rng.randf_range(8.0, 16.0)
		cm.top_radius = 0.0
		cm.bottom_radius = h * 0.28
		cm.height = h
		cm.radial_segments = 7
		tree.mesh = cm
		tree.material_override = tree_look
		tree.position = Vector3(sin(a) * r, h * 0.5 + _rng.randf_range(-1.0, 6.0), -cos(a) * r)
		add_child(tree)
	# Reeds by the near bank.
	var reed_look := StandardMaterial3D.new()
	reed_look.albedo_color = Color(0.42, 0.48, 0.22)
	for i in 140:
		var reed := MeshInstance3D.new()
		var cm := CylinderMesh.new()
		cm.top_radius = 0.004
		cm.bottom_radius = 0.012
		cm.height = _rng.randf_range(1.0, 2.0)
		cm.radial_segments = 4
		reed.mesh = cm
		reed.material_override = reed_look
		var x := _rng.randf_range(2.0, 14.0) * (1.0 if _rng.randf() < 0.5 else -1.0)
		reed.position = Vector3(x, cm.height * 0.5 - 0.3, _rng.randf_range(6.0, 12.0))
		reed.rotation = Vector3(_rng.randf_range(-0.12, 0.12), 0, _rng.randf_range(-0.12, 0.12))
		add_child(reed)
	# The lake bed, stones on it, and clumps of weed swaying.
	var bed := MeshInstance3D.new()
	var bp := PlaneMesh.new()
	bp.size = Vector2(120, 120)
	bed.mesh = bp
	bed.material_override = TACKLE.mat(Color(0.46, 0.41, 0.28), 1.0)
	bed.position = Vector3(0, -4.5, -20)
	add_child(bed)
	var stone := TACKLE.mat(Color(0.42, 0.42, 0.38), 0.9)
	for i in 90:
		var r := _rng.randf_range(0.08, 0.35)
		TACKLE.ball(self, Vector3(_rng.randf_range(-16, 16), -4.5 + 0.3 * r, _rng.randf_range(-30, -2)), r, stone,
			Vector3(_rng.randf_range(0.8, 1.4), _rng.randf_range(0.4, 0.7), _rng.randf_range(0.8, 1.4)))
	var sh := Shader.new()
	sh.code = """
shader_type spatial;
render_mode cull_disabled;
uniform vec3 colour : source_color;
varying float h;
void vertex() {
	h = UV.y;
	float sway = (1.0 - UV.y);
	VERTEX.x += sin(TIME * 0.9 + NODE_POSITION_WORLD.x * 0.7 + VERTEX.y * 1.5) * 0.12 * sway * sway;
	VERTEX.z += cos(TIME * 0.7 + NODE_POSITION_WORLD.z * 0.5) * 0.06 * sway * sway;
}
void fragment() {
	ALBEDO = colour * mix(0.55, 1.15, 1.0 - h);
	ROUGHNESS = 0.8;
}
"""
	var greens := [Color(0.22, 0.48, 0.16), Color(0.38, 0.52, 0.14), Color(0.16, 0.38, 0.20)]
	for i in 70:
		var at := Vector3(_rng.randf_range(-16, 16), -4.5, _rng.randf_range(-30, -3))
		var m := ShaderMaterial.new()
		m.shader = sh
		m.set_shader_parameter("colour", greens[i % 3])
		for k in _rng.randi_range(4, 8):
			var blade := MeshInstance3D.new()
			var qm := QuadMesh.new()
			var h := _rng.randf_range(0.8, 2.6)
			qm.size = Vector2(_rng.randf_range(0.04, 0.08), h)
			qm.subdivide_depth = 6
			blade.mesh = qm
			blade.material_override = m
			blade.position = at + Vector3(_rng.randf_range(-0.25, 0.25), 0.5 * h, _rng.randf_range(-0.25, 0.25))
			blade.rotation = Vector3(_rng.randf_range(-0.15, 0.15), _rng.randf() * TAU, _rng.randf_range(-0.15, 0.15))
			add_child(blade)
