extends SceneTree
## Times the immersed-volume calculation per body per step, for each kind of shape, partly submerged.
const BB := preload("res://addons/bounded_buoyancy/bounded_buoyancy_body.gd")

func body(shape: Shape3D) -> RigidBody3D:
	var b := BB.new()
	var cs := CollisionShape3D.new()
	cs.shape = shape
	b.add_child(cs)
	b.density = 500.0
	b.freeze = true
	b.collision_layer = 0
	b.collision_mask = 0
	b.rotation_degrees = Vector3(10, 0, 20)
	root.add_child(b)
	return b

func _initialize() -> void:
	run.call_deferred()

func run() -> void:
	var sph := SphereShape3D.new(); sph.radius = 0.1
	var box := BoxShape3D.new(); box.size = Vector3(0.3, 0.1, 0.2)
	var cyl := CylinderShape3D.new(); cyl.radius = 0.05; cyl.height = 0.3
	var cap := CapsuleShape3D.new(); cap.radius = 0.05; cap.height = 0.3
	var hull := ConvexPolygonShape3D.new()
	hull.points = PackedVector3Array([Vector3(-0.2, 0.05, -0.2), Vector3(0.2, 0.05, -0.2), Vector3(-0.1, -0.05, -0.2), Vector3(0.1, -0.05, -0.2),
		Vector3(-0.2, 0.05, 0.2), Vector3(0.2, 0.05, 0.2), Vector3(-0.1, -0.05, 0.2), Vector3(0.1, -0.05, 0.2)])
	var cases := [["sphere", sph], ["box", box], ["cylinder", cyl], ["capsule", cap], ["convex hull (boat)", hull]]
	var bodies := []
	for c in cases:
		bodies.append([c[0], body(c[1])])
	await physics_frame
	for e in bodies:
		var b: RigidBody3D = e[1]
		var xf := b.global_transform
		var n := 2000
		var t0 := Time.get_ticks_usec()
		for k in n:
			b._immerse(xf, 0.0)
		var us := float(Time.get_ticks_usec() - t0) / n
		print("  %-20s %3d triangles   %6.1f µs per body per step   (V_sub/V = %.3f)" % [e[0], b._idx.size() / 3, us, b.submerged_volume / b.volume])
	var sea: Node3D = preload("res://addons/bounded_buoyancy/bounded_buoyancy_water.gd").new()
	sea.wave_height = 0.05
	sea.show_surface = false
	root.add_child(sea)
	print("  with waves:")
	for e in bodies:
		var b: RigidBody3D = e[1]
		var xf := b.global_transform
		var n := 2000
		var t0 := Time.get_ticks_usec()
		for k in n:
			b._immerse(xf, sea.height_at(xf.origin), sea)
		print("  %-20s %6.1f µs per body per step" % [e[0], float(Time.get_ticks_usec() - t0) / n])
	quit()
