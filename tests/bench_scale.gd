extends SceneTree
## Many bodies floating at once: physics time per frame, plugin against plain rigid bodies.
## Run: godot --headless --fixed-fps 60 --script res://tests/bench_scale.gd -- <count> <shape> <waves 0/1>
const BB := preload("res://addons/bounded_buoyancy/bounded_buoyancy_body.gd")
const BW := preload("res://addons/bounded_buoyancy/bounded_buoyancy_water.gd")

func _initialize() -> void:
	run.call_deferred()

func make_shape(kind: String) -> Shape3D:
	match kind:
		"sphere":
			var s := SphereShape3D.new(); s.radius = 0.2; return s
		"box":
			var b := BoxShape3D.new(); b.size = Vector3(0.5, 0.25, 0.35); return b
		"cylinder":
			var c := CylinderShape3D.new(); c.radius = 0.12; c.height = 0.6; return c
		"capsule":
			var p := CapsuleShape3D.new(); p.radius = 0.12; p.height = 0.6; return p
		_:
			var h := ConvexPolygonShape3D.new()
			h.points = PackedVector3Array([Vector3(-0.2, 0.05, -0.32), Vector3(0.2, 0.05, -0.32), Vector3(-0.1, -0.05, -0.28), Vector3(0.1, -0.05, -0.28),
				Vector3(-0.2, 0.05, 0.32), Vector3(0.2, 0.05, 0.32), Vector3(-0.1, -0.05, 0.28), Vector3(0.1, -0.05, 0.28), Vector3(0, 0.05, 0.48)])
			return h

func run() -> void:
	var args := OS.get_cmdline_user_args()
	var n := int(args[0]) if args.size() > 0 else 300
	var kind: String = args[1] if args.size() > 1 else "box"
	var waves := args.size() > 2 and args[2] == "1"
	var plain := args.size() > 3 and args[3] == "plain"
	if args.size() > 3 and args[3] == "gdscript":
		BB.use_native = false
	var side := int(ceil(sqrt(n)))
	if not plain:
		var sea := BW.new()
		sea.size = Vector3(side * 1.5 + 4, 3, side * 1.5 + 4)
		sea.wave_height = 0.08 if waves else 0.0
		sea.wave_length = 3.0
		sea.show_surface = false
		root.add_child(sea)
	var shape := make_shape(kind)
	for i in n:
		var b: RigidBody3D = RigidBody3D.new() if plain else BB.new()
		var cs := CollisionShape3D.new()
		cs.shape = shape
		b.add_child(cs)
		if not plain:
			b.density = 500.0
		b.collision_layer = 0
		b.collision_mask = 0
		b.position = Vector3((i % side) * 1.5 - side * 0.75, 0.0, (i / side) * 1.5 - side * 0.75)
		b.rotation_degrees = Vector3(randf() * 30, randf() * 360, randf() * 30)
		if plain:
			b.gravity_scale = 0.0
		root.add_child(b)
	for k in 60:
		await physics_frame
	var frames := 180
	var t0 := Time.get_ticks_usec()
	for k in frames:
		await physics_frame
	var ms := float(Time.get_ticks_usec() - t0) / frames / 1000.0
	print("%-5s %4d %-9s waves=%s  frame %.2f ms" % ["plain" if plain else "BB", n, kind, waves, ms])
	quit()
