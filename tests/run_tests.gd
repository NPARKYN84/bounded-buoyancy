extends SceneTree
## Headless check that the Godot plugin reproduces the paper's benchmarks.
## Run from the project folder:
##   godot --headless --fixed-fps 60 --script res://tests/run_tests.gd
## It prints the first-frame accelerations (paper Table 3) and the release-and-surfacing
## results (paper Table 4), then checks the shape and tilting features (volumes, centre of
## buoyancy and righting), water nodes and waves, and air (balloons, thin air, changing density),
## and ends with PASS or FAIL. The paper's benchmarks leave out air, so B1-B6 set air_density to 0.

const BB := preload("res://addons/bounded_buoyancy/bounded_buoyancy_body.gd")
const BW := preload("res://addons/bounded_buoyancy/bounded_buoyancy_water.gd")
const WIND := preload("res://addons/bounded_buoyancy/bounded_buoyancy_wind.gd")
const BB2 := preload("res://addons/bounded_buoyancy/bounded_buoyancy_body_2d.gd")
const BW2 := preload("res://addons/bounded_buoyancy/bounded_buoyancy_water_2d.gd")
const G := 9.80665
const R := 0.02
const R_TT := 0.0805721899  # table-tennis ball, 2.7 g, 40 mm

var failures := 0


func make_body(r: float, model: int, x: float, y: float) -> RigidBody3D:
	var b := BB.new()
	var cs := CollisionShape3D.new()
	var sh := SphereShape3D.new()
	sh.radius = R
	cs.shape = sh
	b.add_child(cs)
	b.density = r * 1000.0
	b.model = model
	b.air_density = 0.0
	b.collision_layer = 0
	b.collision_mask = 0
	b.freeze = true
	b.position = Vector3(x, y, 0.0)
	root.add_child(b)
	return b


func check(label: String, got: float, want: float, tol: float) -> void:
	var ok: bool = abs(got - want) <= tol
	if not ok:
		failures += 1
	print("  %-44s got %10.3f   expected %10.3f   %s" % [label, got, want, "ok" if ok else "FAIL"])


func _initialize() -> void:
	run.call_deferred()


func run() -> void:
	var engine_name: String = ProjectSettings.get_setting("physics/3d/physics_engine")
	print("Bounded Buoyancy plugin check, Godot %s, physics engine: %s" % [Engine.get_version_info().string, engine_name])
	# Run with "-- --gdscript" to check the GDScript path when the native kernel is installed.
	if "--gdscript" in OS.get_cmdline_user_args():
		BB.use_native = false
	var native := ClassDB.class_exists(&"BoundedBuoyancyKernel") and BB.use_native
	print("Waterline cut: %s" % ("native kernel" if native else "GDScript"))
	var dt := 1.0 / 60.0

	# B1: first-frame acceleration, released from rest 0.5 m under water.
	print("B1  First-frame acceleration (m/s^2, up +)")
	var cases := [["air bubble", 0.001225], ["foam sphere", 0.03], ["table-tennis ball", R_TT],
		["cork", 0.25], ["ice", 0.917], ["rock", 2.7]]
	var bodies := []
	var i := 0
	for c in cases:
		bodies.append([c, make_body(c[1], BB.Model.USUAL, i * 0.2, -0.5), make_body(c[1], BB.Model.BOUNDED, i * 0.2 + 0.1, -0.5)])
		i += 1
	await physics_frame
	for e in bodies:
		e[1].freeze = false
		e[2].freeze = false
	# The bodies take their first step in this physics tick; read it at the start of the next.
	await physics_frame
	for j in bodies.size():
		var e = bodies[j]
		var r: float = e[0][1]
		# Measured from the engine's own velocity after one step, so this also checks that the
		# engine honoured the new mass and force in the same step.
		check("%s, usual" % e[0][0], e[1].linear_velocity.y / dt, G * (1.0 - r) / r, max(0.002 * abs(G * (1.0 - r) / r), 0.01))
		check("%s, bounded" % e[0][0], e[2].linear_velocity.y / dt, G * (1.0 - r) / (r + 0.5), 0.01)
	for e in bodies:
		e[1].queue_free()
		e[2].queue_free()
	await physics_frame

	# A box (upright cube, 10 cm, half the density of water), bounded model, C = 0.5.
	var box := BB.new()
	var bcs := CollisionShape3D.new()
	var bsh := BoxShape3D.new()
	bsh.size = Vector3(0.1, 0.1, 0.1)
	bcs.shape = bsh
	box.add_child(bcs)
	box.density = 500.0
	box.air_density = 0.0
	box.collision_layer = 0
	box.collision_mask = 0
	box.freeze = true
	box.position = Vector3(0, -0.5, 0)
	root.add_child(box)
	await physics_frame
	box.freeze = false
	await physics_frame
	check("box (upright cube), bounded", box.linear_velocity.y / dt, G * 0.5 / 1.0, 0.01)
	box.queue_free()
	await physics_frame

	# B2: release from 0.5 m depth at 60 Hz, bounded model: time to surface and peak height.
	print("B2  Release from 0.5 m, bounded model at 60 Hz (paper Table 4)")
	var b2 := [["foam", 0.03, 0.517, 6.7], ["table-tennis", R_TT, 0.533, 6.1], ["cork", 0.25, 0.617, 4.9], ["pine", 0.5, 0.783, 3.3]]
	var runs := []
	i = 0
	for c in b2:
		runs.append([c, make_body(c[1], BB.Model.BOUNDED, i * 0.2, -0.5), -1.0, -1e9])
		i += 1
	await physics_frame
	for e in runs:
		e[1].freeze = false
	var steps := 0
	while steps < 180:
		await physics_frame
		for e in runs:
			var y: float = e[1].global_position.y
			if e[2] < 0.0 and y >= 0.0:
				e[2] = (steps + 1) * dt  # steps + 1 physics steps have run at this point
			e[3] = max(e[3], y)
		steps += 1
	for e in runs:
		check("%s, time to surface (s)" % e[0][0], e[2], e[0][2], 0.001)
		check("%s, peak height (cm)" % e[0][0], 100.0 * e[3], e[0][3], 0.25)

	# B5: shapes and tilting (features of the plugin beyond the paper's vertical benchmarks).
	print("B5  Shapes, centre of buoyancy and righting")
	var vbox := make_shape_body(_box(Vector3(0.2, 0.1, 0.3)), 500.0, Vector3.ZERO, 45.0)
	var vcyl := make_shape_body(_cylinder(0.05, 0.3), 500.0, Vector3(1, -1, 0), 0.0)
	var vcylh := make_shape_body(_cylinder(0.05, 0.3), 500.0, Vector3(2, 0, 0), 90.0)
	var vcap := make_shape_body(_capsule(0.05, 0.3), 500.0, Vector3(3, -1, 0), 0.0)
	var wedge := ConvexPolygonShape3D.new()
	wedge.points = PackedVector3Array([Vector3(-0.1, 0, -0.1), Vector3(0.1, 0, -0.1), Vector3(0, 0.1, -0.1),
		Vector3(-0.1, 0, 0.1), Vector3(0.1, 0, 0.1), Vector3(0, 0.1, 0.1)])
	var vhull := make_shape_body(wedge, 500.0, Vector3(4, -1, 0), 0.0)
	var vmesh := BB.new()
	var bm := BoxMesh.new()
	bm.size = Vector3(0.2, 0.1, 0.3)
	vmesh.buoyancy_mesh = bm
	vmesh.density = 500.0
	_place(vmesh, Vector3(5, 0, 0), 45.0)
	await physics_frame
	for b in [vbox, vcyl, vcylh, vcap, vhull, vmesh]:
		b._immerse(b.global_transform, 0.0)
	check("box at 45 deg, centre at surface: V_sub/V", vbox.submerged_volume / vbox.volume, 0.5, 1e-6)
	check("cylinder volume (cm^3)", vcyl.volume * 1e6, PI * 0.05 * 0.05 * 0.3 * 1e6, 0.01)
	check("cylinder fully under: V_sub/V", vcyl.submerged_volume / vcyl.volume, 1.0, 1e-9)
	check("cylinder on its side, half under: V_sub/V", vcylh.submerged_volume / vcylh.volume, 0.5, 1e-6)
	check("capsule volume (cm^3)", vcap.volume * 1e6, (PI * 0.0025 * 0.2 + 4.0 / 3.0 * PI * 0.000125) * 1e6, 0.01)
	check("convex wedge volume (cm^3)", vhull.volume * 1e6, 0.5 * 0.2 * 0.1 * 0.2 * 1e6, 0.01)
	check("buoyancy mesh = box shape: V_sub/V", vmesh.submerged_volume / vmesh.volume, 0.5, 1e-6)
	check("box at 45 deg: centre of buoyancy below centre (cm)", -100.0 * (vbox.centre_of_buoyancy.y - vbox.global_position.y), 100.0 * _half_box_cb_depth(), 0.01)
	for b in [vbox, vcyl, vcylh, vcap, vhull, vmesh]:
		b.queue_free()

	# Floating and righting, run in the engine at 60 Hz with no collisions.
	var level := make_shape_body(_box(Vector3(0.2, 0.2, 0.2)), 500.0, Vector3(0, 0, 0), 0.0)
	var raft := make_shape_body(_box(Vector3(0.6, 0.15, 0.4)), 400.0, Vector3(2, 0.0, 0), 30.0)
	var tall := make_shape_body(_box(Vector3(0.1, 0.4, 0.1)), 600.0, Vector3(4, 0.04, 0), 2.0)
	var hull := ConvexPolygonShape3D.new()
	hull.points = PackedVector3Array([Vector3(-0.25, 0.06, -0.3), Vector3(0.25, 0.06, -0.3), Vector3(-0.12, -0.06, -0.3), Vector3(0.12, -0.06, -0.3),
		Vector3(-0.25, 0.06, 0.3), Vector3(0.25, 0.06, 0.3), Vector3(-0.12, -0.06, 0.3), Vector3(0.12, -0.06, 0.3)])
	var boat := BB.new()
	var hcs := CollisionShape3D.new()
	hcs.shape = hull
	boat.add_child(hcs)
	boat.density = 300.0
	boat.center_of_mass_mode = RigidBody3D.CENTER_OF_MASS_MODE_CUSTOM
	boat.center_of_mass = Vector3(0, -0.05, 0)
	_place(boat, Vector3(6, 0.0, 0), 60.0)
	await physics_frame
	for b in [level, raft, tall, boat]:
		b.freeze = false
	var t := 0.0
	while t < 8.0:
		await physics_frame
		t += dt
	check("cube at half density stays level: drift (mm)", 1000.0 * abs(level.global_position.y), 0.0, 1.0)
	check("raft dropped at 30 deg: tilt after 8 s (deg)", tilt_deg(raft), 0.0, 2.0)
	check("tall block, upright: tilt after 8 s (deg)", tilt_deg(tall), 90.0, 5.0)
	check("boat with low centre of mass, from 60 deg (deg)", tilt_deg(boat), 0.0, 3.0)
	for b in [level, raft, tall, boat]:
		b.queue_free()
	await physics_frame

	# B6: water nodes. Once a scene has one, bodies float only inside water nodes.
	print("B6  Water nodes and waves")
	var pond := BW.new()
	pond.size = Vector3(4, 2, 4)
	pond.show_surface = false
	root.add_child(pond)
	var pool := BW.new()
	pool.size = Vector3(2, 1, 2)
	pool.position = Vector3(10, 1.0, 0)
	pool.show_surface = false
	root.add_child(pool)
	var sea := BW.new()
	sea.size = Vector3(4, 2, 4)
	sea.position = Vector3(20, 0, 0)
	sea.density = 1025.0
	sea.show_surface = false
	root.add_child(sea)
	var swell := BW.new()
	swell.size = Vector3(20, 3, 20)
	swell.position = Vector3(50, 0, 0)
	swell.wave_height = 0.05
	swell.wave_length = 3.0
	swell.show_surface = false
	root.add_child(swell)
	var tt := make_body(R_TT, BB.Model.BOUNDED, 0.0, -0.5)
	var dry := make_body(R_TT, BB.Model.BOUNDED, 30.0, -0.5)
	# 85% of water's density: a cube this dense floats flat (one at half density tips onto an edge).
	var cube := make_shape_body(_box(Vector3(0.2, 0.2, 0.2)), 850.0, Vector3(10, 1.0, 0), 0.0)
	var salt := make_shape_body(_box(Vector3(0.2, 0.2, 0.2)), 900.0, Vector3(20, -0.07, 0), 0.0)
	var rider := make_shape_body(_box(Vector3(0.6, 0.15, 0.4)), 400.0, Vector3(50, 0.0, 0), 0.0)
	await physics_frame
	for b in [tt, dry, cube, salt, rider]:
		b.freeze = false
	await physics_frame
	check("ball outside any water: falls at g (m/s^2)", dry.linear_velocity.y / dt, -G, 0.01)
	var surfaced := -1.0
	var lowest := 1e9
	var highest := -1e9
	var max_tilt := 0.0
	var cube_sum := 0.0
	var cube_n := 0
	t = dt
	while t < 10.0:
		await physics_frame
		t += dt
		if surfaced < 0.0 and tt.global_position.y >= 0.0:
			surfaced = t
		if t > 7.0:
			cube_sum += cube.global_position.y
			cube_n += 1
		if t > 3.0:
			lowest = minf(lowest, rider.global_position.y)
			highest = maxf(highest, rider.global_position.y)
			max_tilt = maxf(max_tilt, tilt_deg(rider))
	check("table-tennis ball in a water node: time to surface (s)", surfaced, 0.533, 0.001)
	# Mean over the last 3 s: a cube this dense bobs about once a second, and only slowly stops.
	check("cube in a raised pool bobs about its level (cm)", 100.0 * cube_sum / cube_n, 100.0 + 100.0 * 0.1 * (1.0 - 2.0 * 0.85), 0.5)
	check("cube in sea water floats at its sea-water level (cm)", 100.0 * salt.global_position.y, 100.0 * 0.1 * (1.0 - 2.0 * 900.0 / 1025.0), 0.15)
	check("raft on 5 cm waves: rises and falls (cm)", 100.0 * (highest - lowest), 10.0, 6.0)
	check("raft on 5 cm waves: rocks but stays upright (deg)", max_tilt, 8.0, 7.5)
	for b in [tt, dry, cube, salt, rider]:
		b.queue_free()
	await physics_frame

	# B7: air. The same law with air's density, outside the water nodes above.
	print("B7  Air: balloons, thin air and changing density")
	var bal_u := make_air_ball(0.45, BB.Model.USUAL, Vector3(100, 5, 0), 0.0)
	var bal_b := make_air_ball(0.45, BB.Model.BOUNDED, Vector3(102, 5, 0), 0.0)
	var stone := make_air_ball(2700.0, BB.Model.BOUNDED, Vector3(104, 5, 0), 0.0)
	var high := make_air_ball(0.6, BB.Model.BOUNDED, Vector3(106, 2, 0), 10.0)
	var half := make_shape_body(_box(Vector3(0.1, 0.1, 0.1)), 300.0, Vector3(1.5, 0.0, 1.5), 0.0)
	half.air_density = 1.2
	var ballast := make_shape_body(_box(Vector3(0.1, 0.1, 0.1)), 2000.0, Vector3(-1.5, -0.5, -1.5), 0.0)
	var hot := make_shape_body(_sphere(1.0), 1.0, Vector3(110, 10.0 * log(1.2) / log(2.0), 0), 30.0)
	hot.air_density = 1.2
	hot.air_half_height = 10.0
	hot.center_of_mass_mode = RigidBody3D.CENTER_OF_MASS_MODE_CUSTOM
	hot.center_of_mass = Vector3(0, -0.6, 0)
	await physics_frame
	ballast.density = 500.0
	for b in [bal_u, bal_b, stone, high, half, ballast, hot]:
		b.freeze = false
	await physics_frame
	check("helium balloon (0.45 kg/m^3) in air, usual", bal_u.linear_velocity.y / dt, G * (1.2 - 0.45) / 0.45, 0.02)
	check("helium balloon (0.45 kg/m^3) in air, bounded", bal_b.linear_velocity.y / dt, G * (1.2 - 0.45) / (0.45 + 0.6), 0.002)
	check("stone in air: just under g (m/s^2)", -stone.linear_velocity.y / dt, G * (2700.0 - 1.2) / (2700.0 + 0.6), 0.002)
	check("cube half in water, half in air", half.linear_velocity.y / dt, G * (0.5006 - 0.3) / (0.3 + 0.5 * 0.5006), 0.002)
	check("density changed while running: acceleration", ballast.linear_velocity.y / dt, G * 0.5, 0.002)
	check("density changed while running: own mass (kg)", ballast.own_mass, 0.5, 1e-6)
	check("density changed while running: inertia (x 10^4)", 1e4 * ballast.inertia.x, 1e4 * 0.5 * 0.02 / 12.0, 0.001)
	t = dt
	while t < 120.0:
		await physics_frame
		t += dt
	check("balloon (0.6) in air halving every 10 m: floats at (m)", high.global_position.y, 10.0, 0.3)
	check("hot-air balloon tipped 30 deg, low centre of mass: rights (deg)", tilt_deg(hot), 0.0, 2.0)
	for b in [bal_u, bal_b, stone, high, half, ballast, hot]:
		b.queue_free()
	await physics_frame

	# B8: fast bodies. Water 60 m deep, so nothing falls out of the bottom.
	print("B8  Fast bodies")
	var deep := BW.new()
	deep.size = Vector3(10, 60, 10)
	deep.position = Vector3(200, 0, 0)
	deep.show_surface = false
	root.add_child(deep)
	var falling := make_air_ball(2700.0, BB.Model.BOUNDED, Vector3(199, 50.0, 0), 0.0)
	var fired := make_shape_body(_sphere(0.1), 100.0, Vector3(201, 0.5, 0), 0.0)
	var skimmer := make_shape_body(_box(Vector3(0.4, 0.1, 0.3)), 300.0, Vector3(198, 0.0, -3), 0.0)
	await physics_frame
	for b in [falling, fired, skimmer]:
		b.freeze = false
	fired.linear_velocity = Vector3(0, -30, 0)
	skimmer.linear_velocity = Vector3(0, 0, 25)
	var entry_speed := 0.0
	var worst := 0.0
	t = dt
	while t < 20.0:
		await physics_frame
		t += dt
		if entry_speed == 0.0 and falling.global_position.y < 0.3:
			entry_speed = -falling.linear_velocity.y
		for b in [falling, fired, skimmer]:
			worst = maxf(worst, b.linear_velocity.length())
	var v_stone := sqrt(2.0 * 1700.0 * (4.0 / 3.0 * PI * 0.001) * G / (1000.0 * 0.47 * PI * 0.01))
	# Falling 50 m through air with drag: v^2 = (F/k)(1 - exp(-2 k h / M)), M including the air's added mass.
	var vol := 4.0 / 3.0 * PI * 0.001
	var f_air := (2700.0 - 1.2) * vol * G
	var k_air := 0.5 * 1.2 * 0.47 * PI * 0.01
	var v_hit := sqrt(f_air / k_air * (1.0 - exp(-2.0 * k_air * 49.7 / ((2700.0 + 0.6) * vol))))
	check("stone dropped 50 m: hits the water at (m/s)", entry_speed, v_hit, 0.5)
	check("then sinks at its terminal speed in water (m/s)", -falling.linear_velocity.y, v_stone, 0.1)
	check("light ball fired down at 30 m/s: floats again (cm)", 100.0 * fired.global_position.y, 100.0 * (0.1 - 2.0 * 0.1 * _cap_fraction(0.1)), 1.0)
	check("box skimming at 25 m/s: slowed, still level (deg)", tilt_deg(skimmer), 0.0, 1.0)
	check("fastest speed seen: the stone's, as it hits (m/s)", worst, v_hit, 0.5)
	for b in [falling, fired, skimmer]:
		b.queue_free()

	# B9: flowing water. A river 120 m long and 10 m wide, flowing at 1.5 m/s, slower along the banks;
	# and the same river turned a quarter turn, so that it flows the other way across the world.
	print("B9  Flowing water")
	var river := BW.new()
	river.size = Vector3(120, 3, 10)
	river.position = Vector3(300, 0, 0)
	river.current = Vector3(1.5, 0, 0)
	river.current_edge = 2.0
	river.show_surface = false
	root.add_child(river)
	var turned := BW.new()
	turned.size = Vector3(120, 3, 10)
	turned.position = Vector3(400, 0, 0)
	turned.rotation_degrees = Vector3(0, 90, 0)
	turned.current = Vector3(1.5, 0, 0)
	turned.show_surface = false
	root.add_child(turned)
	var mid := make_shape_body(_sphere(0.1), 500.0, Vector3(242, 0, 0), 0.0)
	var bank := make_shape_body(_sphere(0.1), 500.0, Vector3(242, 0, 4.5), 0.0)
	var other := make_shape_body(_sphere(0.1), 500.0, Vector3(400, 0, 58), 0.0)
	var drifter := make_shape_body(_box(Vector3(0.6, 0.15, 0.4)), 400.0, Vector3(242, 0, -1.0), 0.0)
	await physics_frame
	for b in [mid, bank, other, drifter]:
		b.freeze = false
	# Quadratic drag on the speed through the water closes the gap as 1/t: after a minute a ball is
	# within 1% of the current.
	t = dt
	while t < 60.0:
		await physics_frame
		t += dt
	check("ball mid-river, after 60 s: drifts with the current (m/s)", mid.linear_velocity.x, 1.5, 0.02)
	check("ball 0.5 m from the bank drifts slower (m/s)", bank.linear_velocity.x, 1.5 * 0.25, 0.02)
	check("river turned a quarter turn: flows along -z (m/s)", other.linear_velocity.z, -1.5, 0.02)
	check("raft drifts with the current (m/s)", drifter.linear_velocity.x, 1.5, 0.1)
	check("raft stays level in the current (deg)", tilt_deg(drifter), 0.0, 1.0)
	for b in [mid, bank, other, drifter]:
		b.queue_free()

	# B10: wind. A steady 3 m/s wind over a lake at x = 500 only (a box of wind), and a gusty wind
	# measured at a fixed point.
	print("B10 Wind")
	var lake := BW.new()
	lake.size = Vector3(200, 3, 60)
	lake.position = Vector3(560, 0, 0)
	lake.show_surface = false
	root.add_child(lake)
	var breeze := WIND.new()
	breeze.velocity = Vector3(3, 0, 0)
	breeze.gust_strength = 0.0
	breeze.direction_swing = 0.0
	breeze.everywhere = false
	breeze.size = Vector3(200, 40, 60)
	breeze.position = Vector3(560, 0, 0)
	root.add_child(breeze)
	var drifting := make_air_ball(0.6, BB.Model.BOUNDED, Vector3(470, 10.0, 0), 10.0)
	var still := make_air_ball(0.6, BB.Model.BOUNDED, Vector3(300, 10.0, 30), 10.0)
	var sailer := make_shape_body(_boat_hull(), 300.0, Vector3(470, 0.05, -10), 0.0)
	sailer.sail_area = 0.5
	sailer.sail_centre = Vector3(0, 0.6, 0)
	sailer.center_of_mass_mode = RigidBody3D.CENTER_OF_MASS_MODE_CUSTOM
	sailer.center_of_mass = Vector3(0, -0.04, 0)
	sailer.air_density = 1.2
	var crate := make_shape_body(_box(Vector3(0.4, 0.4, 0.4)), 500.0, Vector3(470, 0.0, 10), 0.0)
	crate.air_density = 1.2
	await physics_frame
	for b in [drifting, still, sailer, crate]:
		b.freeze = false
	var heel := 0.0
	t = dt
	while t < 30.0:
		await physics_frame
		t += dt
		if t > 10.0:
			heel = maxf(heel, tilt_deg(sailer))
	check("balloon in a 3 m/s wind drifts with it (m/s)", drifting.linear_velocity.x, 3.0, 0.06)
	check("balloon outside the wind's box stays put (m/s)", still.linear_velocity.length(), 0.0, 0.01)
	check("boat with a sail runs before the wind (m/s)", sailer.linear_velocity.x, 0.75, 0.6)
	check("crate with no sail barely moves (m/s)", crate.linear_velocity.x, 0.1, 0.1)
	check("boat heels a little in a light wind, stays up (deg)", heel, 1.0, 0.9)
	var gusty := WIND.new()
	gusty.velocity = Vector3(5, 0, 0)
	gusty.gust_strength = 0.3
	gusty.everywhere = false
	gusty.position = Vector3(0, 500, 0)
	root.add_child(gusty)
	var lo := 1e9
	var hi := 0.0
	for k in 6000:
		gusty.time = k * 0.05
		var w: Vector3 = gusty.velocity_at(Vector3(0, 500, 0))
		lo = minf(lo, w.length())
		hi = maxf(hi, w.length())
	check("gusts: slowest wind (m/s)", lo, 5.0 * 0.7, 0.25)
	check("gusts: strongest wind (m/s)", hi, 5.0 * 1.3, 0.25)

	# B11: splashes. A ball dropped onto a pond (bottom 1 m above the surface), and a cork pushed up
	# out of the water from below.
	print("B11 Splashes and signals")
	var splash_pond := BW.new()
	splash_pond.size = Vector3(10, 4, 10)
	splash_pond.position = Vector3(700, 0, 0)
	root.add_child(splash_pond)
	var dropped := make_shape_body(_sphere(0.1), 800.0, Vector3(700, 1.1, 0), 0.0)
	var popper := make_shape_body(_sphere(0.1), 100.0, Vector3(702, -1.0, 0), 0.0)
	var entries: Array = []
	var exits: Array = []
	dropped.water_entered.connect(func(speed: float, at: Vector3): entries.append([speed, at]))
	popper.water_exited.connect(func(speed: float, at: Vector3): exits.append([speed, at]))
	await physics_frame
	dropped.freeze = false
	popper.freeze = false
	popper.linear_velocity = Vector3(0, 6, 0)
	var effects_seen := 0
	t = dt
	while t < 5.0:
		await physics_frame
		t += dt
		effects_seen = maxi(effects_seen, splash_pond.get_child_count(true))
	check("ball dropped 1 m: water_entered once", entries.size(), 1, 0)
	check("impact speed reported (m/s)", entries[0][0] if entries.size() > 0 else 0.0, sqrt(2.0 * G * 1.0), 0.2)
	check("impact point on the surface (cm)", 100.0 * (entries[0][1].y if entries.size() > 0 else 1.0), 0.0, 0.5)
	check("cork shot up: water_exited reported", float(exits.size() >= 1), 1.0, 0.0)
	check("splash and rings drawn (effects at once)", float(effects_seen >= 2), 1.0, 0.0)

	# B12: the 2D version. Water surface at y = 0 (y points down), gravity 980 px/s^2.
	print("B12 2D version")
	var g2 := float(ProjectSettings.get_setting("physics/2d/default_gravity"))
	var tank := BW2.new()
	tank.size = Vector2(4000, 3000)
	tank.show_water = false
	root.add_child(tank)
	var flume := BW2.new()
	flume.size = Vector2(12000, 1000)
	flume.position = Vector2(12000, 0)
	flume.current = Vector2(150, 0)
	flume.show_water = false
	root.add_child(flume)
	var swell2 := BW2.new()
	swell2.size = Vector2(4000, 1000)
	swell2.position = Vector2(30000, 0)
	swell2.wave_height = 20.0
	swell2.wave_length = 600.0
	swell2.show_water = false
	root.add_child(swell2)
	var c_usual := make_body_2d(_circle2(20), 250.0, Vector2(-600, 500), 0.0)
	c_usual.model = BB2.Model.USUAL
	var c_bound := make_body_2d(_circle2(20), 250.0, Vector2(-500, 500), 0.0)
	var balloon2 := make_body_2d(_circle2(20), 0.45, Vector2(5000, -3000), 0.0)
	balloon2.air_density = 1.2
	var plank := make_body_2d(_rect2(Vector2(200, 50)), 500.0, Vector2(0, -20), 0.0)
	var boat2 := make_body_2d(_boat2(), 300.0, Vector2(600, -10), 40.0)
	boat2.center_of_mass_mode = RigidBody2D.CENTER_OF_MASS_MODE_CUSTOM
	boat2.center_of_mass = Vector2(0, 12)
	var log2 := make_body_2d(_capsule2(20, 100), 600.0, Vector2(-1000, -400), 0.0)
	var drift2 := make_body_2d(_circle2(20), 500.0, Vector2(6200, 0), 0.0)
	var raft2 := make_body_2d(_rect2(Vector2(120, 30)), 400.0, Vector2(30000, 0), 0.0)
	var drop2 := make_body_2d(_circle2(20), 800.0, Vector2(1200, -120), 0.0)
	var hits: Array = []
	drop2.water_entered.connect(func(speed: float, _at: Vector2): hits.append(speed))
	await physics_frame
	for b in [c_usual, c_bound, balloon2, plank, boat2, log2, drift2, raft2, drop2]:
		b.freeze = false
	await physics_frame
	check("2D circle (C = 1), usual: first frame (px/s^2)", -c_usual.linear_velocity.y / dt, g2 * 750.0 / 250.0, 0.5)
	check("2D circle (C = 1), bounded: first frame (px/s^2)", -c_bound.linear_velocity.y / dt, g2 * 750.0 / 1250.0, 0.1)
	check("2D balloon in air: first frame (px/s^2)", -balloon2.linear_velocity.y / dt, g2 * 0.75 / 1.65, 0.1)
	check("2D capsule area (px^2)", log2.area, 40.0 * 60.0 + PI * 400.0, 0.01)
	var plank_sum := 0.0
	var plank_n := 0
	var raft_lo := 1e9
	var raft_hi := -1e9
	t = dt
	while t < 30.0:
		await physics_frame
		t += dt
		if t > 7.0 and t < 10.0:
			plank_sum += plank.global_position.y
			plank_n += 1
		if t > 5.0:
			raft_lo = minf(raft_lo, raft2.global_position.y)
			raft_hi = maxf(raft_hi, raft2.global_position.y)
	check("2D plank at half density floats with its centre at the surface (px)", plank_sum / plank_n, 0.0, 1.0)
	check("2D boat tipped 40 deg, low centre of mass: rights (deg)", absf(rad_to_deg(boat2.rotation)), 0.0, 3.0)
	check("2D ball in a 150 px/s current drifts with it (px/s)", drift2.linear_velocity.x, 150.0, 5.0)
	check("2D raft on 20 px waves rises and falls (px)", raft_hi - raft_lo, 50.0, 30.0)
	check("2D ball dropped 100 px: water_entered speed (px/s)", hits[0] if hits.size() > 0 else 0.0, sqrt(2.0 * g2 * 100.0), 8.0)

	print("PASS" if failures == 0 else "FAIL (%d checks)" % failures)
	quit(1 if failures else 0)


## Fraction of a sphere's diameter under water when it floats at density ratio `r`.
func _cap_fraction(r: float) -> float:
	var lo := 0.0
	var hi := 1.0
	for i in 60:
		var x := 0.5 * (lo + hi)
		if x * x * (3.0 - 2.0 * x) < r:
			lo = x
		else:
			hi = x
	return 0.5 * (lo + hi)


func tilt_deg(b: Node3D) -> float:
	return rad_to_deg(acos(clampf(b.global_transform.basis.y.normalized().dot(Vector3.UP), -1.0, 1.0)))


func _place(b: RigidBody3D, at: Vector3, roll_deg: float) -> void:
	b.collision_layer = 0
	b.collision_mask = 0
	b.freeze = true
	b.position = at
	b.rotation_degrees = Vector3(0, 0, roll_deg)
	root.add_child(b)


func make_shape_body(shape: Shape3D, dens: float, at: Vector3, roll_deg: float) -> RigidBody3D:
	var b := BB.new()
	var cs := CollisionShape3D.new()
	cs.shape = shape
	b.add_child(cs)
	b.density = dens
	b.air_density = 0.0
	_place(b, at, roll_deg)
	return b


## A sphere of radius 0.1 m in air (1.2 kg/m³), thinning with height if half_height > 0.
func make_air_ball(dens: float, model: int, at: Vector3, half_height: float) -> RigidBody3D:
	var b := make_shape_body(_sphere(0.1), dens, at, 0.0)
	b.model = model
	b.air_density = 1.2
	b.air_half_height = half_height
	return b


func make_body_2d(shape: Shape2D, dens: float, at: Vector2, rot_deg: float) -> RigidBody2D:
	var b := BB2.new()
	var cs := CollisionShape2D.new()
	cs.shape = shape
	b.add_child(cs)
	b.density = dens
	b.air_density = 0.0
	b.collision_layer = 0
	b.collision_mask = 0
	b.freeze = true
	b.position = at
	b.rotation_degrees = rot_deg
	root.add_child(b)
	return b


func _circle2(r: float) -> CircleShape2D:
	var s := CircleShape2D.new()
	s.radius = r
	return s


func _rect2(size: Vector2) -> RectangleShape2D:
	var s := RectangleShape2D.new()
	s.size = size
	return s


func _capsule2(r: float, h: float) -> CapsuleShape2D:
	var s := CapsuleShape2D.new()
	s.radius = r
	s.height = h
	return s


func _boat2() -> ConvexPolygonShape2D:
	var s := ConvexPolygonShape2D.new()
	s.points = PackedVector2Array([Vector2(-60, -15), Vector2(60, -15), Vector2(40, 15), Vector2(-40, 15)])
	return s


## A small boat's hull, 0.9 m long, as a convex polygon.
func _boat_hull() -> ConvexPolygonShape3D:
	var h := ConvexPolygonShape3D.new()
	h.points = PackedVector3Array([Vector3(-0.45, 0.12, -0.25), Vector3(0.45, 0.12, -0.25), Vector3(-0.35, -0.12, -0.15), Vector3(0.35, -0.12, -0.15),
		Vector3(-0.45, 0.12, 0.25), Vector3(0.45, 0.12, 0.25), Vector3(-0.35, -0.12, 0.15), Vector3(0.35, -0.12, 0.15)])
	return h


func _sphere(r: float) -> SphereShape3D:
	var s := SphereShape3D.new()
	s.radius = r
	return s


func _box(size: Vector3) -> BoxShape3D:
	var s := BoxShape3D.new()
	s.size = size
	return s


func _cylinder(r: float, h: float) -> CylinderShape3D:
	var s := CylinderShape3D.new()
	s.radius = r
	s.height = h
	return s


func _capsule(r: float, h: float) -> CapsuleShape3D:
	var s := CapsuleShape3D.new()
	s.radius = r
	s.height = h
	return s


## Depth of the centre of buoyancy below the centre for a 0.2 x 0.1 box turned 45 degrees with its
## centre at the surface: the lower half is a quadrilateral whose centroid is found by triangles.
func _half_box_cb_depth() -> float:
	var c := cos(PI / 4.0)
	var hx := 0.1
	var hy := 0.05
	var pts := [Vector2(-hx, -hy), Vector2(hx, -hy), Vector2(hx, hy), Vector2(-hx, hy)]
	var rot: Array[Vector2] = []
	for p in pts:
		rot.append(Vector2(p.x * c - p.y * c, p.x * c + p.y * c))
	# Clip the rotated rectangle to y <= 0 and take the centroid of the polygon.
	var poly: Array[Vector2] = []
	for k in 4:
		var a: Vector2 = rot[k]
		var b: Vector2 = rot[(k + 1) % 4]
		if a.y <= 0.0:
			poly.append(a)
		if (a.y < 0.0 and b.y > 0.0) or (a.y > 0.0 and b.y < 0.0):
			poly.append(a + (b - a) * (a.y / (a.y - b.y)))
	var area := 0.0
	var cy := 0.0
	for k in poly.size():
		var p: Vector2 = poly[k]
		var q: Vector2 = poly[(k + 1) % poly.size()]
		var cr := p.x * q.y - q.x * p.y
		area += cr
		cy += (p.y + q.y) * cr
	return -cy / (3.0 * area)
