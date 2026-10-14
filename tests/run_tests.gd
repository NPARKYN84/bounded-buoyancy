extends SceneTree
## Headless check that the Godot plugin reproduces the paper's benchmarks.
## Run from the project folder:
##   godot --headless --fixed-fps 60 --script res://tests/run_tests.gd
## It prints the first-frame accelerations (paper Table 3) and the release-and-surfacing
## results (paper Table 4), then checks the shape and tilting features (volumes, centre of
## buoyancy and righting), water nodes and waves, air (balloons, thin air, changing density), fast
## bodies, rivers, wind, splashes, 2D, another water system's surface, and foils and propellers
## (keels, rudders, sails), and ends with PASS or FAIL. The paper's benchmarks leave out air, so B1-B6 set air_density to 0.

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
		var paper_ball := make_body(c[1], BB.Model.BOUNDED, i * 0.2, -0.5)
		paper_ball.wave_damping = 0.0   # the paper's model: no wave-making damping
		runs.append([c, paper_ball, -1.0, -1e9])
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
	deep.size = Vector3(10, 60, 30)   # long enough for the skimmer to slow down in
	deep.position = Vector3(200, 0, 10)
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
	check("box skimming at 25 m/s: stopped within 15 m (z, m)", skimmer.global_position.z, 6.0, 6.0)
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

	# B13: the surface of another water system. A stand-in "ocean" gives the same waves as a water
	# node 40 m away, so bodies on it must move exactly as their twins on the water node's own waves.
	# Both twins cut the waterline in GDScript, so that only the surfaces differ: the native kernel
	# works in single precision, and its last-digit differences grow as the capsule tumbles (the
	# wave-making damping, which reads how fast the surface under a body rises, feels them most).
	print("B13 Other water systems")
	var native_was := BB.use_native
	BB.use_native = false
	var own := BW.new()
	own.size = Vector3(20, 4, 20)
	own.position = Vector3(0, 0, 500)
	own.wave_height = 0.08
	own.wave_length = 4.0
	own.wave_direction = 20.0
	own.show_surface = false
	root.add_child(own)
	var ocean_script := GDScript.new()
	ocean_script.source_code = "extends Node3D\nvar target: Node3D\nvar offset := Vector3.ZERO\nvar calls := 0\n" \
		+ "func get_height(p: Vector3) -> float:\n\tcalls += 1\n\treturn target.height_at(p - offset)\n" \
		+ "func get_acceleration(p: Vector3, across := 0.0, along := 0.0) -> float:\n\treturn target.vertical_acceleration_at(p - offset, across, along)\n"
	ocean_script.reload()
	var ocean := Node3D.new()
	ocean.set_script(ocean_script)
	ocean.set("target", own)
	ocean.set("offset", Vector3(40, 0, 0))
	root.add_child(ocean)
	var outside_water := BW.new()
	outside_water.size = Vector3(20, 4, 20)
	outside_water.position = Vector3(40, 0, 500)
	outside_water.surface_source = ocean
	outside_water.surface_method = &"get_height"
	outside_water.surface_max_wave = 0.12
	outside_water.surface_acceleration_method = &"get_acceleration"
	root.add_child(outside_water)
	var twins := []
	var ki := 0
	for sh in [_box(Vector3(0.6, 0.15, 0.4)), _boat_hull(), _sphere(0.15), _capsule(0.1, 0.5)]:
		var at := Vector3(-4.0 + 2.5 * ki, 0.05, 500.0 + 1.5 * ki)
		twins.append([make_shape_body(sh, 450.0, at, 15.0), make_shape_body(sh, 450.0, at + Vector3(40, 0, 0), 15.0)])
		ki += 1
	await physics_frame
	for e in twins:
		e[0].freeze = false
		e[1].freeze = false
	t = 0.0
	var worst_y := 0.0
	var worst_tilt := 0.0
	var range_lo := 1e9
	var range_hi := -1e9
	while t < 4.0:
		await physics_frame
		t += dt
		for e in twins:
			worst_y = maxf(worst_y, absf(e[0].global_position.y - e[1].global_position.y))
			worst_tilt = maxf(worst_tilt, absf(tilt_deg(e[0]) - tilt_deg(e[1])))
		if t > 2.0:
			range_lo = minf(range_lo, twins[0][1].global_position.y)
			range_hi = maxf(range_hi, twins[0][1].global_position.y)
	check("same waves from another system: height (mm)", 1000.0 * worst_y, 0.0, 0.5)
	# The capsule stands up, falls over and tumbles at up to 300 degrees a second, so a thousandth of a
	# second's difference in its fall (single-precision positions, 40 m apart) shows as tenths of a degree.
	check("same waves from another system: tilt (deg)", worst_tilt, 0.0, 0.5)
	BB.use_native = native_was
	check("raft on the other system's waves rises and falls (cm)", 100.0 * (range_hi - range_lo), 20.0, 12.0)
	for e in twins:
		e[0].queue_free()
		e[1].queue_free()
	# A body far above the outside_water surface asks for one height a step (at its centre), not one per vertex.
	var aloft := make_shape_body(_box(Vector3(0.4, 0.4, 0.4)), 450.0, Vector3(40, 3.0, 503), 0.0)
	await physics_frame
	aloft.freeze = false
	await physics_frame
	var before: int = ocean.get("calls")
	for n in 10:
		await physics_frame
	check("body well above it: heights asked per step", (int(ocean.get("calls")) - before) / 10.0, 1.0, 0.0)
	aloft.queue_free()
	# A flat surface 0.25 m up, set from code: a cube at 85% of water's density floats on it.
	var flat := BW.new()
	flat.size = Vector3(10, 4, 10)
	flat.position = Vector3(80, 0, 500)
	flat.height_function = func(_p: Vector3) -> float: return 0.25
	flat.surface_max_wave = 0.0
	root.add_child(flat)
	# A source without the method: the water keeps its own (flat) surface.
	var wrong := BW.new()
	wrong.size = Vector3(10, 4, 10)
	wrong.position = Vector3(100, 0, 500)
	var plain := Node.new()
	root.add_child(plain)
	wrong.surface_source = plain
	wrong.show_surface = false
	root.add_child(wrong)
	var on_flat := make_shape_body(_box(Vector3(0.2, 0.2, 0.2)), 850.0, Vector3(80, 0.3, 500), 0.0)
	var on_wrong := make_shape_body(_box(Vector3(0.2, 0.2, 0.2)), 850.0, Vector3(100, 0.05, 500), 0.0)
	await physics_frame
	on_flat.freeze = false
	on_wrong.freeze = false
	var flat_sum := 0.0
	var wrong_sum := 0.0
	var m := 0
	t = 0.0
	while t < 10.0:
		await physics_frame
		t += dt
		if t > 7.0:
			flat_sum += on_flat.global_position.y
			wrong_sum += on_wrong.global_position.y
			m += 1
	check("cube on a flat surface set from code (cm)", 100.0 * flat_sum / m, 25.0 + 100.0 * 0.1 * (1.0 - 2.0 * 0.85), 0.5)
	check("source without the method: own surface kept (cm)", 100.0 * wrong_sum / m, 100.0 * 0.1 * (1.0 - 2.0 * 0.85), 0.5)

	# B14: foils and propellers. Keels, rudders and sails are BoundedBuoyancyFoil nodes, propellers
	# BoundedBuoyancyThruster nodes, under the body; each gives a bounded impulse at its own point.
	print("B14 Foils and propellers (keels, rudders, sails, propellers)")
	for old in WIND.instances.duplicate():
		old.queue_free()
	await physics_frame
	var sea14 := BW.new()
	sea14.size = Vector3(600, 8, 600)
	sea14.position = Vector3(0, 0, 1500)
	sea14.show_surface = false
	sea14.splashes = false
	sea14.ripples = false
	root.add_child(sea14)
	# A neutrally buoyant ball (no drag) carrying a foil at its centre, carried through the water at
	# 2 m/s with the flow meeting the foil at 10 and 90 deg: the force square to the foil is
	# ½ ρ A q² C_N(α), bounded by the ball's inertia over the step.
	var k_ar2: float = FOIL.lift_slope(2.0)
	check("lift slope of a long wing / 2 pi", FOIL.lift_slope(1e6) / TAU, 1.0, 1e-4)
	for deg in [10.0, 90.0]:
		var ball := make_shape_body(_sphere(0.5), 1000.0, Vector3(-100, -2.0, 1500), 0.0)
		ball.drag_coefficient = 0.0
		var plate: Node3D = FOIL.new()
		plate.area = 0.2
		plate.aspect_ratio = 2.0
		ball.add_child(plate)
		await physics_frame
		# Unfrozen first: Jolt keeps no velocity set on a frozen body.
		ball.freeze = false
		await physics_frame
		var a := deg_to_rad(deg)
		ball.linear_velocity = Vector3(-sin(a), 0, -cos(a)) * 2.0
		await physics_frame
		var un := 2.0 * sin(a)
		var uc := 2.0 * cos(a)
		var cn := 0.5 * 1000.0 * 0.2 * minf(k_ar2 * uc + 1.2 * un, 1.4 * 4.0 / un)
		var want := cn * un / (1.0 + cn / 60.0 / ball.mass)
		check("plate at %d deg: force square to it (N)" % deg, plate.force.x, want, 0.002 * want)
		check("plate at %d deg: angle of attack (deg)" % deg, absf(plate.angle_of_attack), deg, 0.01)
		ball.queue_free()
	# Bounded: a 0.1 m cube14 at water's density with a 10 m² foil, moving across it at 10 m/s.
	var cube14 := make_shape_body(_box(Vector3(0.1, 0.1, 0.1)), 1000.0, Vector3(-90, -4.0, 1500), 0.0)   # deep enough for the whole 4.5 m span
	cube14.drag_coefficient = 0.0
	var huge: Node3D = FOIL.new()
	huge.area = 10.0
	cube14.add_child(huge)
	await physics_frame
	cube14.freeze = false
	await physics_frame
	cube14.linear_velocity = Vector3(10, 0, 0)
	await physics_frame
	var c_huge := 0.5 * 1000.0 * 10.0 * 1.2 * 10.0
	print("  (applied explicitly the first step would leave %.0f m/s)" % (10.0 - c_huge / 60.0 / cube14.mass * 10.0))
	check("1.5 kg cube, 10 m² foil, 10 m/s across: speed after a step", cube14.linear_velocity.x, 10.0 / (1.0 + c_huge / 60.0 / cube14.mass), 1e-3)
	cube14.queue_free()

	# A keel: going ahead at 3 m/s and slipping sideways at 0.5 m/s, a keeled motorboat loses the slip
	# within a second and keeps its speed ahead; without the keel the slip lasts.
	var keeled := make_motorboat(Vector3(-60, 0, 1400), 0.0, true, false, false)
	var bare := make_motorboat(Vector3(-50, 0, 1400), 0.0, false, false, false)
	await physics_frame
	keeled.freeze = false
	bare.freeze = false
	await _wait(3.0)
	for b in [keeled, bare]:
		b.linear_velocity = b.global_transform.basis * Vector3(0.5, 0, -3)
		b.angular_velocity = Vector3.ZERO
	await _wait(1.0)
	var slip_k: float = (keeled.global_transform.basis.inverse() * keeled.linear_velocity).x
	var slip_b: float = (bare.global_transform.basis.inverse() * bare.linear_velocity).x
	var ahead_k: float = -(keeled.global_transform.basis.inverse() * keeled.linear_velocity).z
	var ahead_b: float = -(bare.global_transform.basis.inverse() * bare.linear_velocity).z
	print("  after 1 s: with a keel %.3f m/s sideways, %.2f ahead; without %.3f sideways, %.2f ahead" % [slip_k, ahead_k, slip_b, ahead_b])
	check("keel: sideways slip after 1 s, of the slip without one", absf(slip_k) / absf(slip_b), 0.0, 0.3)
	check("keel: speed ahead kept, of the speed without one", ahead_k / ahead_b, 1.0, 0.15)

	# Directional stability: a fin behind the centre of mass straightens the course; ahead of it, the
	# boat turns round.
	var fin_aft := make_motorboat(Vector3(-20, 0, 1400), 0.0, false, false, false)
	add_foil(fin_aft, Vector3(0, -0.4, 0.9), 0.15, 1.5)
	var fin_fore := make_motorboat(Vector3(0, 0, 1400), 0.0, false, false, false)
	add_foil(fin_fore, Vector3(0, -0.4, -0.9), 0.15, 1.5)
	await physics_frame
	fin_aft.freeze = false
	fin_fore.freeze = false
	await _wait(3.0)
	for b in [fin_aft, fin_fore]:
		b.linear_velocity = b.global_transform.basis * Vector3(0, 0, -3)
		b.angular_velocity = Vector3(0, 0.4, 0)
	await _wait(6.0)
	check("fin behind the centre of mass: spin after 6 s (deg/s)", rad_to_deg(fin_aft.angular_velocity.y), 0.0, 2.0)
	check("fin behind: course turned by no more than (deg)", absf(_yaw(fin_aft)), 15.0, 15.0)
	check("fin ahead: boat turned round, more than 60 deg", 1.0 if absf(_yaw(fin_fore)) > 60.0 else 0.0, 1.0, 0.0)

	# A propeller's own top speed: with nothing else in the way, throttle x max_speed, ahead and
	# astern, without overshooting.
	var full := make_motorboat(Vector3(40, 0, 1700), 0.0, false, false, true)
	var half_t := make_motorboat(Vector3(50, 0, 1700), 0.0, false, false, true)
	var astern := make_motorboat(Vector3(60, 0, 1500), 0.0, false, false, true)
	for b in [full, half_t, astern]:
		b.drag_coefficient = 0.0
		b.keep_momentum_on_entry = false
	_thruster(full).throttle = 1.0
	_thruster(half_t).throttle = 0.5
	_thruster(astern).throttle = -1.0
	await physics_frame
	for b in [full, half_t, astern]:
		b.freeze = false
	var fastest := 0.0
	var t14 := 0.0
	while t14 < 60.0:
		await physics_frame
		t14 += dt
		fastest = maxf(fastest, -full.linear_velocity.z)
	check("nothing in the way, full throttle: top speed (m/s)", -full.linear_velocity.z, 5.0, 0.03)
	check("nothing in the way, half throttle: top speed (m/s)", -half_t.linear_velocity.z, 2.5, 0.03)
	check("nothing in the way, full astern: top speed (m/s)", astern.linear_velocity.z, 5.0, 0.03)
	check("no overshoot on the way up: fastest seen (m/s)", fastest, 5.0, 0.03)

	# The rudder: hard over either way at full throttle, the motorboat turns in a steady circle, to
	# starboard for a positive angle; the two circles are mirror images.
	var right := make_motorboat(Vector3(150, 0, 1400), 0.0)
	var left := make_motorboat(Vector3(180, 0, 1400), 0.0)
	_rudder(right).angle = 25.0
	_rudder(left).angle = -25.0
	_thruster(right).throttle = 1.0
	_thruster(left).throttle = 1.0
	await physics_frame
	right.freeze = false
	left.freeze = false
	await _wait(30.0)
	var r1 := right.angular_velocity.y
	await _wait(5.0)
	var r2 := right.angular_velocity.y
	check("rudder +25 deg: turns to starboard (clockwise)", 1.0 if right.angular_velocity.y < 0.0 else 0.0, 1.0, 0.0)
	check("rudder -25 deg: the mirror image (yaw rate ratio)", left.angular_velocity.y / -right.angular_velocity.y, 1.0, 0.02)
	check("steady turn: yaw rate change over 5 s (%)", 100.0 * absf(r2 - r1) / absf(r1), 0.0, 3.0)
	check("turning circle radius, boat lengths (2.4 m)", right.linear_velocity.length() / absf(right.angular_velocity.y) / 2.4, 3.5, 3.0)

	# A boat at anchor in a stream14 (pinned at its bow) swings to lie along the current, bow upstream.
	var stream14 := BW.new()
	stream14.size = Vector3(80, 6, 40)
	stream14.position = Vector3(0, 0, 2000)
	stream14.current = Vector3(1.0, 0, 0)
	stream14.show_surface = false
	stream14.splashes = false
	stream14.ripples = false
	root.add_child(stream14)
	var moored := make_motorboat(Vector3(0, 0, 2000), 90.0, true, true, false)
	await physics_frame
	var pin := PinJoint3D.new()
	pin.position = moored.global_transform * Vector3(0, 0.1, -1.4)
	root.add_child(pin)
	pin.node_a = pin.get_path_to(moored)
	moored.freeze = false
	await _wait(60.0)
	var bow := -moored.global_transform.basis.z
	check("boat at anchor in a 1 m/s river: bow upstream (deg from -x)", rad_to_deg(Vector2(bow.x, bow.z).angle_to(Vector2(-1, 0))), 0.0, 10.0)

	# Sailing: a dinghy with a centreboard, a rudder and a self-trimming sail, steered by an autopilot,
	# makes ground towards the wind 45 deg off it.
	var breeze14 := WIND.new()
	breeze14.velocity = Vector3(0, 0, 6)
	breeze14.gust_strength = 0.0
	breeze14.direction_swing = 0.0
	root.add_child(breeze14)
	var dinghy := make_dinghy(Vector3(-200, 0, 1700), -45.0)
	var course := Vector3(sin(deg_to_rad(45.0)), 0, -cos(deg_to_rad(45.0)))
	await physics_frame
	dinghy.freeze = false
	var start := Vector3.ZERO
	var heel14 := 0.0
	t14 = 0.0
	while t14 < 90.0:
		_rudder(dinghy).angle = _autopilot(dinghy, course)
		await physics_frame
		t14 += dt
		if absf(t14 - 30.0) < 0.5 * dt:
			start = dinghy.global_position
		if t14 > 30.0:
			heel14 = maxf(heel14, tilt_deg(dinghy))
	var made := dinghy.global_position - start
	print("  dinghy close-hauled: %.2f m/s through the water, %.2f m/s towards the wind, heeling up to %.1f deg" % [made.length() / 60.0, -made.z / 60.0, heel14])
	check("dinghy 45 deg off the wind: towards the wind, at least 0.5 (m/s)", 1.0 if -made.z / 60.0 >= 0.5 else 0.0, 1.0, 0.0)
	check("dinghy: stays upright (deg)", heel14, 12.0, 18.0)
	check("dinghy: holds its course (deg)", absf(rad_to_deg(Vector2(-dinghy.global_transform.basis.z.x, -dinghy.global_transform.basis.z.z).angle_to(Vector2(course.x, course.z)))), 0.0, 8.0)
	breeze14.queue_free()

	# B15: wave-making damping. A body bobbing at the surface sends out ripples that carry its energy
	# away, so it settles; a body riding waves is not held back.
	print("B15 Wave-making damping (bobbing settles)")
	# Damping ratios against the exact ones of linear wave theory (Capytaine; reference/wave_damping.csv).
	for e in [["cube floating half under", 4.0, 0.0401, true], ["box 1 x 1, draft 0.1 (a raft)", 100.0, 0.1597, true],
			["upright post, draft 4 x its width", 0.194, 0.0044, true], ["ball floating half under", 4.972, 0.0848, false],
			["log floating half under", 15.536, 0.1464, false]]:
		check("%s: ratio / exact" % e[0], BB.wave_damping_ratio(e[1]) / e[2], 1.15 if e[3] else 0.675, 0.35 if e[3] else 0.075)
	for e in [[0.5, 0.0270], [1.0, 0.0514], [4.0, 0.1508], [20.0, 0.2894]]:
		check("side view, beam %s x draft: ratio / exact" % e[0], BB2.wave_damping_ratio(e[0]) / e[1], 1.0, 0.03)
	var still15 := BW.new()
	still15.size = Vector3(40, 5, 40)
	still15.position = Vector3(0, 0, 2900)
	still15.show_surface = false
	still15.splashes = false
	still15.ripples = false
	root.add_child(still15)
	# Let go from rest half under, a box starts exactly as without the damping.
	var half15 := make_shape_body(_box(Vector3(0.2, 0.2, 0.2)), 250.0, Vector3(-15, 0, 2890), 0.0)
	# Let go 3 cm below where they float, with and without the damping.
	var crate15 := make_shape_body(_box(Vector3(0.4, 0.4, 0.4)), 500.0, Vector3(-10, -0.03, 2900), 0.0)
	crate15.drag_coefficient = 0.0        # wave-making damping alone, to compare with its damping ratio
	crate15.keep_momentum_on_entry = false
	var ball15 := make_shape_body(_sphere(0.1), 500.0, Vector3(-7, -0.03, 2900), 0.0)
	var ball15_0 := make_shape_body(_sphere(0.1), 500.0, Vector3(-4, -0.03, 2900), 0.0)
	ball15_0.wave_damping = 0.0
	var raft15 := make_shape_body(_box(Vector3(1.0, 0.2, 1.0)), 300.0, Vector3(-1, 0.01, 2900), 0.0)
	var post15 := make_shape_body(_box(Vector3(0.2, 1.0, 0.2)), 800.0, Vector3(7, -0.33, 2900), 0.0)
	post15.center_of_mass_mode = RigidBody3D.CENTER_OF_MASS_MODE_CUSTOM
	post15.center_of_mass = Vector3(0, -0.4, 0)
	var sunk15 := make_shape_body(_sphere(0.1), 2000.0, Vector3(10, -2.0, 2900), 0.0)
	var set15 := [half15, crate15, ball15, ball15_0, raft15, post15, sunk15]
	await physics_frame
	for b in set15:
		b.freeze = false
	await physics_frame
	check("box at a quarter of water's density, half under: first frame (m/s^2)", half15.linear_velocity.y / dt, G * 0.5, 0.01)
	check("  with the damping on", 1.0 if half15.heave_damping > 0.0 else 0.0, 1.0, 0.0)
	var crate_y: Array = []
	var post_y: Array = []
	var sunk_damped := false
	# The largest distance from the floating height over a second (a whole bob or more), from 3 s and 9 s.
	var ball3 := 0.0
	var ball03 := 0.0
	var raft3 := 0.0
	var ball9 := 0.0
	var ball09 := 0.0
	var t15 := dt
	while t15 < 10.0:
		await physics_frame
		t15 += dt
		crate_y.append(crate15.global_position.y)
		post_y.append(post15.global_position.y)
		sunk_damped = sunk_damped or sunk15.heave_damping != 0.0
		if t15 > 3.0 and t15 <= 4.0:
			ball3 = maxf(ball3, absf(ball15.global_position.y))
			ball03 = maxf(ball03, absf(ball15_0.global_position.y))
			raft3 = maxf(raft3, absf(raft15.global_position.y - 0.04))
		if t15 > 9.0:
			ball9 = maxf(ball9, absf(ball15.global_position.y))
			ball09 = maxf(ball09, absf(ball15_0.global_position.y))
	var crests := _crests(crate_y, 0.0)
	var seen: float = log(crests[0] / crests[2]) / (4.0 * PI) if crests.size() >= 3 else 0.0
	check("crate bobbing, no drag: damping ratio seen / formula", seen / BB.wave_damping_ratio(4.0), 1.0, 0.1)
	print("  wooden ball let go 3 cm low, bob from 3 s to 4 s: %.1f mm with wave damping, %.1f mm without; from 9 s: %.2f mm, %.1f mm" % [1000.0 * ball3, 1000.0 * ball03, 1000.0 * ball9, 1000.0 * ball09])
	check("wooden ball let go 3 cm low: bob from 3 s to 4 s (mm)", 1000.0 * ball3, 2.5, 2.5)
	check("  without wave damping, at least 3 times that", 1.0 if ball03 > 3.0 * ball3 else 0.0, 1.0, 0.0)
	check("  with it, settled after 9 s (mm)", 1000.0 * ball9, 0.0, 0.5)
	check("  without, still bobbing after 9 s (mm, > 2)", 1.0 if 1000.0 * ball09 > 2.0 else 0.0, 1.0, 0.0)
	check("raft let go 3 cm low: settled after 3 s (mm)", 1000.0 * raft3, 0.0, 0.5)
	# Damped only while at the surface (rising out, plunging in, still).
	check("at the surface for: rising out (s)", BB.at_surface(0.05, 1.0, 10.0, dt), 0.01, 1e-9)
	check("at the surface for: plunging in (s)", BB.at_surface(0.05, 1.0, -10.0, dt), 0.01, 1e-9)
	check("at the surface for: plunging in, 3 times as much out (s)", BB.at_surface(0.05, 3.0, -10.0, dt), dt, 1e-9)
	var post_crests := _crests(post_y, -0.3)
	check("deep post: still bobbing after 10 s (last crest / first)", post_crests[-1] / post_crests[0] if post_crests.size() >= 3 else 0.0, 0.75, 0.25)
	check("body wholly under water: no wave damping", 1.0 if sunk_damped else 0.0, 0.0, 0.0)
	for b in set15:
		b.queue_free()

	# Riding long waves the raft is not held back: the damping acts on its bobbing relative to the surface.
	var sea15 := BW.new()
	sea15.size = Vector3(80, 5, 80)
	sea15.position = Vector3(0, 0, 3100)
	sea15.wave_height = 0.15
	sea15.wave_length = 12.0
	sea15.wave_direction = 0.0
	sea15.show_surface = false
	sea15.splashes = false
	sea15.ripples = false
	root.add_child(sea15)
	var rider15 := make_shape_body(_box(Vector3(0.6, 0.15, 0.4)), 400.0, Vector3(0, 0.015, 3100), 0.0)
	await physics_frame
	rider15.freeze = false
	var lo15 := 1e9
	var hi15 := -1e9
	var slo15 := 1e9
	var shi15 := -1e9
	t15 = dt
	while t15 < 20.0:
		await physics_frame
		t15 += dt
		if t15 > 5.0:
			var y15 := rider15.global_position.y
			var s15: float = sea15.height_at(rider15.global_transform * rider15.center_of_mass)
			lo15 = minf(lo15, y15)
			hi15 = maxf(hi15, y15)
			slo15 = minf(slo15, s15)
			shi15 = maxf(shi15, s15)
	check("raft on long waves follows the surface (rise and fall / surface's)", (hi15 - lo15) / (shi15 - slo15), 1.0, 0.03)
	rider15.queue_free()

	# Moved into water 10 cm higher, by a teleport or by gliding across into it, a body is not kicked by
	# the jump in the surface: it moves as its twin without wave damping, both having been at rest.
	var low15 := BW.new()
	low15.size = Vector3(20, 5, 20)
	low15.position = Vector3(-10, 0, 3300)
	var high15 := BW.new()
	high15.size = Vector3(20, 5, 20)
	high15.position = Vector3(10, 0.1, 3300)
	for w15 in [low15, high15]:
		w15.show_surface = false
		w15.splashes = false
		w15.ripples = false
		root.add_child(w15)
	var twins15 := []
	for k in 4:
		var tw := make_shape_body(_box(Vector3(0.4, 0.4, 0.4)), 500.0, Vector3(-15, 0, 3292 + 4 * k), 0.0)
		tw.drag_coefficient = 0.0
		tw.angular_drag = 0.0
		if k % 2 == 1:
			tw.wave_damping = 0.0
		twins15.append(tw)
	await physics_frame
	for b in twins15:
		b.freeze = false
	await _wait(0.5)
	# Twins 0 and 1 are teleported 25 m into the higher water; twins 2 and 3 glide across into it at 2 m/s.
	for k in [0, 1]:
		twins15[k].global_position += Vector3(25, 0, 0)
	for k in [2, 3]:
		twins15[k].global_position = Vector3(-0.5, 0, twins15[k].global_position.z)
		twins15[k].linear_velocity = Vector3(2, 0, 0)
	# Over the step into the new water and the two after it (later the damped twin rightly bobs less).
	var tele_worst := 0.0
	var glide_worst := 0.0
	var crossed_at := -1
	for n in 30:
		await physics_frame
		if n < 3:
			tele_worst = maxf(tele_worst, absf(twins15[0].linear_velocity.y - twins15[1].linear_velocity.y))
		if crossed_at < 0 and twins15[2].global_position.x > 2.0 * dt + 1e-6:
			crossed_at = n
		if crossed_at >= 0 and n < crossed_at + 3:
			glide_worst = maxf(glide_worst, absf(twins15[2].linear_velocity.y - twins15[3].linear_velocity.y))
	check("teleported into water 10 cm higher: as its twin without (m/s)", tele_worst, 0.0, 0.005)
	check("glided into water 10 cm higher: as its twin while crossing (m/s)", glide_worst, 0.0, 0.005)
	check("  (it did cross)", 1.0 if crossed_at >= 0 else 0.0, 1.0, 0.0)
	for b in twins15:
		b.queue_free()

	# Water rising steadily (a lock filling, speeding up smoothly to 0.2 m/s over 2 s) carries a raft up at the same
	# depth as a raft without wave damping: the damping acts on its speed relative to the surface, not on its own
	# speed (which would hold it about 8 mm deeper). Then the water is lifted 15 cm in one step: the raft gets no
	# kick from the jump, beyond its twin's.
	var lock15 := BW.new()
	lock15.size = Vector3(20, 6, 20)
	lock15.position = Vector3(0, 0, 3500)
	lock15.show_surface = false
	lock15.splashes = false
	lock15.ripples = false
	root.add_child(lock15)
	var lock_rafts := [make_shape_body(_box(Vector3(1.0, 0.2, 1.0)), 300.0, Vector3(-3, 0.04, 3500), 0.0),
		make_shape_body(_box(Vector3(1.0, 0.2, 1.0)), 300.0, Vector3(3, 0.04, 3500), 0.0)]
	lock_rafts[1].wave_damping = 0.0
	var lock2d := BW2.new()
	lock2d.size = Vector2(1000, 600)
	lock2d.position = Vector2(8000, 30000)   # clear of the other 2D water (a 2D water holds everything above it)
	lock2d.show_water = false
	root.add_child(lock2d)
	# 2D rafts 120 x 30 px at 400 kg/m³: draft 12 px, so the centre 3 px above the surface (y down).
	var lock_rafts2d := [make_body_2d(_rect2(Vector2(120, 30)), 400.0, Vector2(7800, 29997), 0.0),
		make_body_2d(_rect2(Vector2(120, 30)), 400.0, Vector2(8200, 29997), 0.0)]
	lock_rafts2d[1].wave_damping = 0.0
	await physics_frame
	for b in lock_rafts + lock_rafts2d:
		b.freeze = false
	var lock_sum := [0.0, 0.0, 0.0, 0.0]
	var lock_n := 0
	t15 = 0.0
	while t15 < 6.0:
		await physics_frame
		t15 += dt
		var speed15 := 0.1 * (1.0 - cos(PI * t15 / 2.0)) if t15 < 2.0 else 0.2   # speeding up smoothly
		lock15.position.y += speed15 * dt
		lock2d.position.y -= 100.0 * speed15 * dt
		if t15 > 5.0:
			for k in 2:
				lock_sum[k] += lock_rafts[k].global_position.y - lock15.position.y - 0.04
				lock_sum[2 + k] += -(lock_rafts2d[k].global_position.y - lock2d.position.y) - 3.0
			lock_n += 1
	print("  raft in water rising at 0.2 m/s: %.2f mm from its usual depth with wave damping, %.2f mm without; 2D: %.3f px, %.3f px" % [1000.0 * lock_sum[0] / lock_n, 1000.0 * lock_sum[1] / lock_n, lock_sum[2] / lock_n, lock_sum[3] / lock_n])
	check("raft in water rising at 0.2 m/s: depth, less a raft's without (mm)", 1000.0 * (lock_sum[0] - lock_sum[1]) / lock_n, 0.0, 0.5)
	check("2D raft in water rising at 20 px/s: depth, less a raft's without (px)", (lock_sum[2] - lock_sum[3]) / lock_n, 0.0, 0.05)
	# Slow the water smoothly to a stop, let the rafts settle, then lift it 15 cm (and the 2D water 15 px) in one
	# step: over that step and the two after it, each raft's speed changes as its twin's does.
	t15 = 0.0
	while t15 < 2.0:
		await physics_frame
		t15 += dt
		var slowing := 0.1 * (1.0 + cos(PI * t15 / 2.0))
		lock15.position.y += slowing * dt
		lock2d.position.y -= 100.0 * slowing * dt
	await _wait(4.0)
	var before15: float = lock_rafts[0].linear_velocity.y - lock_rafts[1].linear_velocity.y
	var before2d: float = lock_rafts2d[0].linear_velocity.y - lock_rafts2d[1].linear_velocity.y
	lock15.position.y += 0.15
	lock2d.position.y -= 15.0
	var lift_worst := 0.0
	var lift_worst2d := 0.0
	for n in 3:
		await physics_frame
		lift_worst = maxf(lift_worst, absf(lock_rafts[0].linear_velocity.y - lock_rafts[1].linear_velocity.y - before15))
		lift_worst2d = maxf(lift_worst2d, absf(lock_rafts2d[0].linear_velocity.y - lock_rafts2d[1].linear_velocity.y - before2d))
	check("water lifted 15 cm in a step: as its twin without (m/s)", lift_worst, 0.0, 0.01)
	check("2D water lifted 15 px in a step: as its twin without (px/s)", lift_worst2d, 0.0, 1.0)
	for b in lock_rafts + lock_rafts2d:
		b.queue_free()

	# Side view: a 2D crate bobs down at its damping ratio; a plank settles; without the damping the crate bobs on.
	var tank15 := BW2.new()
	tank15.size = Vector2(6000, 1000)
	tank15.position = Vector2(0, 20000)
	tank15.show_water = false
	root.add_child(tank15)
	var crate2d := make_body_2d(_rect2(Vector2(40, 40)), 500.0, Vector2(-1000, 20003), 0.0)
	crate2d.drag_coefficient = 0.0
	crate2d.keep_momentum_on_entry = false
	var crate2d_0 := make_body_2d(_rect2(Vector2(40, 40)), 500.0, Vector2(-500, 20003), 0.0)
	crate2d_0.wave_damping = 0.0
	var plank15 := make_body_2d(_rect2(Vector2(160, 20)), 400.0, Vector2(0, 20000 - 2 + 3), 0.0)
	await physics_frame
	for b in [crate2d, crate2d_0, plank15]:
		b.freeze = false
	var ys2d: Array = []
	t15 = dt
	while t15 < 10.0:
		await physics_frame
		t15 += dt
		ys2d.append(-(crate2d.global_position.y - 20000.0))   # up positive
	var crests2d := _crests(ys2d, 0.0)
	var seen2d: float = log(crests2d[0] / crests2d[2]) / (4.0 * PI) if crests2d.size() >= 3 else 0.0
	check("2D crate bobbing, no drag: damping ratio seen / formula", seen2d / BB2.wave_damping_ratio(2.0), 1.0, 0.1)
	check("2D plank let go 3 px low: settled after 10 s (px)", absf(plank15.global_position.y - 19998.0), 0.0, 0.05)
	check("2D crate without wave damping still bobbing after 10 s (px, > 0.05)", 1.0 if absf(crate2d_0.global_position.y - 20000.0) > 0.05 else 0.0, 1.0, 0.0)
	for b in [crate2d, crate2d_0, plank15]:
		b.queue_free()

	# B16: the water's push in waves. Water accelerating up and down in the waves pushes bodies with it.
	print("B16 The water's push in waves")
	# The water's upward acceleration is the second time derivative of the surface's height: for a water
	# node's own waves, for the 2D water's, and for the ocean example's (the template for linking an ocean).
	var sea16 := BW.new()
	sea16.size = Vector3(100, 20, 100)
	sea16.position = Vector3(3, 0.5, 3998)
	sea16.rotation_degrees = Vector3(0, 30, 0)
	sea16.wave_height = 0.3
	sea16.wave_length = 8.0
	sea16.wave_direction = 20.0
	sea16.show_surface = false
	sea16.splashes = false
	sea16.ripples = false
	root.add_child(sea16)
	var sea16_2d := BW2.new()
	sea16_2d.size = Vector2(10000, 2000)
	sea16_2d.position = Vector2(400, 40050)
	sea16_2d.wave_height = 30.0
	sea16_2d.wave_length = 800.0
	sea16_2d.show_water = false
	root.add_child(sea16_2d)
	var ocean16: Node3D = load("res://addons/bounded_buoyancy/demo/ocean_example.gd").new()
	ocean16.position = Vector3(0, 0.2, 0)
	root.add_child(ocean16)
	await physics_frame
	var worst16 := [0.0, 0.0, 0.0]
	var h16 := 1e-3
	for p16 in [Vector3(1, 0, 4000), Vector3(-7, 0, 4004.5), Vector3(12, 0, 3989)]:
		for t16 in [0.3, 2.1, 5.7]:
			var f1 := func(time_now: float) -> float:
				sea16.time = time_now
				return sea16.height_at(p16)
			var fd1: float = (f1.call(t16 + h16) - 2.0 * f1.call(t16) + f1.call(t16 - h16)) / (h16 * h16)
			sea16.time = t16
			worst16[0] = maxf(worst16[0], absf(sea16.vertical_acceleration_at(Vector3(p16.x, sea16.height_at(p16), p16.z)) - fd1))
			var f2 := func(time_now: float) -> float:
				sea16_2d.time = time_now
				return sea16_2d.height_at(p16.x * 100.0)
			var fd2: float = (f2.call(t16 + h16) - 2.0 * f2.call(t16) + f2.call(t16 - h16)) / (h16 * h16)
			sea16_2d.time = t16
			worst16[1] = maxf(worst16[1], absf(sea16_2d.vertical_acceleration_at(Vector2(p16.x * 100.0, sea16_2d.height_at(p16.x * 100.0))) - fd2))
			var f3 := func(time_now: float) -> float:
				ocean16.time = time_now
				return ocean16.get_height(p16)
			var fd3: float = (f3.call(t16 + h16) - 2.0 * f3.call(t16) + f3.call(t16 - h16)) / (h16 * h16)
			ocean16.time = t16
			worst16[2] = maxf(worst16[2], absf(ocean16.get_vertical_acceleration(Vector3(p16.x, ocean16.get_height(p16), p16.z)) - fd3))
	check("water's acceleration at the surface = surface height's 2nd derivative (m/s^2)", worst16[0], 0.0, 1e-3)
	check("  2D (px/s^2)", worst16[1], 0.0, 0.1)
	check("  the ocean example (m/s^2)", worst16[2], 0.0, 1e-3)
	check("  50 m down, gone: water node, 2D, ocean example", absf(sea16.vertical_acceleration_at(Vector3(1, -49.5, 4000)))
		+ absf(sea16_2d.vertical_acceleration_at(Vector2(100, 40050 + 5000))) / 100.0 + absf(ocean16.get_vertical_acceleration(Vector3(1, -49.8, 4000))), 0.0, 1e-6)
	ocean16.queue_free()
	# The same in a side view: a 2D ball as dense as water, 100 px (1 m) under 12 m waves 20 px high.
	sea16_2d.position = Vector2(0, 40050)
	sea16_2d.wave_height = 20.0
	sea16_2d.wave_length = 1200.0
	sea16_2d.time = 0.0
	var deep2d := make_body_2d(_circle2(10), 1000.0, Vector2(0, 40150), 0.0)
	deep2d.drag_coefficient = 0.0
	await physics_frame
	deep2d.freeze = false
	var g2d := float(ProjectSettings.get_setting("physics/2d/default_gravity"))
	var k2a := TAU / 1200.0
	var k2b := TAU / (1200.0 * 0.63)
	var w2a := sqrt(g2d * k2a)
	var w2b := sqrt(g2d * k2b)
	var t0_2d := sea16_2d.time + 0.5 * dt
	deep2d.linear_velocity = Vector2(0, 20.0 * (-w2a * cos(-w2a * t0_2d) * exp(-k2a * 100.0) - 0.4 * w2b * cos(-w2b * t0_2d + 1.3) * exp(-k2b * 100.0)))
	var ts2d := []
	var ys2d16 := []
	var water2d := []
	t15 = 0.0
	while t15 < 20.0:
		await physics_frame
		t15 += dt
		ts2d.append(t15)
		ys2d16.append(deep2d.global_position.y)
		water2d.append(20.0 * (sin(-w2a * sea16_2d.time) * exp(-k2a * 100.0) + 0.4 * sin(-w2b * sea16_2d.time + 1.3) * exp(-k2b * 100.0)))
	var fit2d := _follows(ts2d, ys2d16, water2d)
	check("2D ball as dense as water under waves: rises and falls, of the water there", fit2d.x, 1.0, 0.05)
	check("  in step with it (correlation)", fit2d.y, 1.0, 0.01)
	deep2d.queue_free()
	sea16_2d.queue_free()
	# A ball as dense as the water, 1 m under 12 m waves, moves up and down with the water round it, as
	# far as the water there does (each wave's height x exp(-k x 1 m)). Before, it stayed put. It is let
	# go moving with the water there; any difference left stays with it as a steady drift (nothing here
	# slows it), so the straight line that best fits its height is taken out first.
	sea16.time = 0.0
	sea16.wave_height = 0.2
	sea16.wave_length = 12.0
	sea16.wave_direction = 0.0
	sea16.rotation_degrees = Vector3.ZERO
	var deep16 := make_shape_body(_sphere(0.1), 1000.0, Vector3(3, -0.5, 3998), 0.0)
	deep16.drag_coefficient = 0.0
	await physics_frame
	deep16.freeze = false
	var k16a := TAU / 12.0
	var k16b := TAU / (12.0 * 0.63)
	var t0_16 := sea16.time + 0.5 * dt
	deep16.linear_velocity = Vector3(0, -0.2 * (sqrt(G * k16a) * cos(-sqrt(G * k16a) * t0_16) * exp(-k16a)
		+ 0.4 * sqrt(G * k16b) * cos(-sqrt(G * k16b) * t0_16 + 1.3) * exp(-k16b)), 0)
	var ts16 := []
	var ys16 := []
	var water16 := []
	t15 = 0.0
	while t15 < 20.0:
		await physics_frame
		t15 += dt
		ts16.append(t15)
		ys16.append(deep16.global_position.y)
		var wa := sqrt(G * k16a)
		var wb := sqrt(G * k16b)
		water16.append(0.2 * (sin(-wa * sea16.time) * exp(-k16a) + 0.4 * sin(-wb * sea16.time + 1.3) * exp(-k16b)))
	var fit16 := _follows(ts16, ys16, water16)
	print("  ball as dense as water, 1 m under 12 m waves: rises and falls %.2f times as far as the water there, correlation %.4f" % [fit16.x, fit16.y])
	check("ball as dense as water under waves: rises and falls, of the water there", fit16.x, 1.0, 0.05)
	check("  in step with it (correlation)", fit16.y, 1.0, 0.01)
	deep16.queue_free()
	# Waves too steep to be real (1 m high, 2 m long): the water's acceleration is held to half of g.
	sea16.wave_height = 1.0
	sea16.wave_length = 2.0
	var steep16 := make_shape_body(_box(Vector3(0.4, 0.4, 0.4)), 500.0, Vector3(3, 0, 3998), 0.0)
	await physics_frame
	steep16.freeze = false
	var most16 := 0.0
	for n in 300:
		await physics_frame
		most16 = maxf(most16, absf(steep16.wave_acceleration))
	check("waves too steep to be real: the water's acceleration (g)", most16 / G, 0.5, 0.01)
	steep16.queue_free()
	var usual16 := make_shape_body(_box(Vector3(0.4, 0.4, 0.4)), 500.0, Vector3(3, 0, 3998), 0.0)
	usual16.model = BB.Model.USUAL
	await physics_frame
	usual16.freeze = false
	var usual_push := 0.0
	for n in 30:
		await physics_frame
		usual_push = maxf(usual_push, absf(usual16.wave_acceleration))
	check("  the usual model is not pushed (m/s^2)", usual_push, 0.0, 0.0)
	usual16.queue_free()

	# B17: flooding. Compartments fill through their holes by the orifice law; the water inside is
	# weight carried at its own centre, which stays level as the body leans.
	print("B17 Flooding")
	var sea17 := BW.new()
	sea17.size = Vector3(200, 200, 200)
	sea17.position = Vector3(0, 0, 6000)
	sea17.show_surface = false
	sea17.splashes = false
	sea17.ripples = false
	root.add_child(sea17)
	# The free surface: water 0.4 m deep in a 2 m wide, 4 m long tank turned 10 degrees moves across
	# the tank by i tan(angle) / V and up by i tan²(angle) / 2V (i = l b³ / 12), as for any wall-sided tank.
	var tank17: Node3D = COMPARTMENT.new()
	tank17.size = Vector3(2, 1, 4)
	var tilt17 := deg_to_rad(10.0)
	var xf17 := Transform3D(Basis(Vector3(0, 0, 1), tilt17), Vector3(1, 2, 3))
	var v17 := 2.0 * 0.4 * 4.0
	var upright17: Array = COMPARTMENT.level_for(tank17.corners(Transform3D(Basis(), Vector3(1, 2, 3))), v17)
	var leaning17: Array = COMPARTMENT.level_for(tank17.corners(xf17), v17)
	var shift17: Vector3 = xf17.affine_inverse() * leaning17[1] - (upright17[1] - Vector3(1, 2, 3))
	var i17 := 4.0 * 8.0 / 12.0
	check("free surface: water moves across the tank (mm)", 1000.0 * absf(shift17.x), 1000.0 * i17 * tan(tilt17) / v17, 0.01)
	check("free surface: and up it (mm)", 1000.0 * shift17.y, 1000.0 * i17 * tan(tilt17) * tan(tilt17) / (2.0 * v17), 0.01)
	check("upright: the water's surface 0.4 m above the floor (mm)", 1000.0 * (upright17[0] - 1.5), 400.0, 0.01)
	# A mirrored tank (scale -1 across, as a port tank copied from a starboard one) holds water the same.
	var mirrored17: Array = COMPARTMENT.level_for(tank17.corners(Transform3D(Basis().scaled(Vector3(-1, 1, 1)), Vector3(1, 2, 3))), v17)
	check("  mirrored: the same (mm)", 1000.0 * (mirrored17[0] - 1.5), 400.0, 0.01)
	tank17.free()

	# The sinking-bowl water clock: a box 1 m square and 0.5 m tall floating at draft d0 = 0.15 m, open to
	# a hole of area a in its floor. As water comes in the box sinks, and the head at the hole stays d0,
	# so it fills at the steady rate C a √(2 g d0) and goes under at t = S (H - d0) / (C a √(2 g d0)).
	# Water loose across the whole floor takes away all the stability the box's width gives it (ρ i / Δ
	# equals the waterplane's I / V), so the box is ballasted low to float upright, as the textbook's does.
	var g17 := float(ProjectSettings.get_setting("physics/3d/default_gravity"))
	var bowl17 := make_shape_body(_box(Vector3(1, 0.5, 1)), 300.0, Vector3(0, 0.1, 6000), 0.0)
	bowl17.center_of_mass = Vector3(0, -0.24, 0)
	var hold17: Node3D = COMPARTMENT.new()
	hold17.size = Vector3(1, 0.5, 1)
	hold17.holes.append(Vector4(0, -0.25, 0, 0.005))
	bowl17.add_child(hold17)
	var expect17 := 1.0 * (0.5 - 0.15) / (0.6 * 0.005 * sqrt(2.0 * g17 * 0.15))
	await physics_frame
	bowl17.freeze = false
	var under17 := -1.0
	var half17 := -1.0
	t = 0.0
	while t < expect17 * 1.3 and under17 < 0.0:
		await physics_frame
		t += dt
		var top: float = bowl17.global_position.y + 0.25
		if half17 < 0.0 and top <= 0.35 - 0.175:
			half17 = t
		if top <= 0.0:
			under17 = t
	check("sinking bowl: goes under at the water clock's time (s)", under17, expect17, 0.03 * expect17)
	check("  and sinks steadily: half way at half the time (s)", half17, 0.5 * expect17, 0.03 * expect17)
	await _wait(15.0)
	# Full, it weighs 650 kg against 500 kg of water displaced: it sinks at about its terminal speed
	# (a little less, as it leans and shows the water more of itself).
	check("  then sinks at about its terminal speed (m/s)", -bowl17.linear_velocity.y, sqrt(2.0 * 150.0 * g17 / (1000.0 * 0.47 * 1.0)), 0.25)
	check("  and fills: the compartment holds 0.5 m³ (m³)", hold17.flood_volume, 0.5, 0.001)
	# Without the ballast the same box lolls over as it floods.
	var loose17 := make_shape_body(_box(Vector3(1, 0.5, 1)), 300.0, Vector3(4, 0.1, 6000), 0.0)
	var loosehold17: Node3D = COMPARTMENT.new()
	loosehold17.size = Vector3(1, 0.5, 1)
	loosehold17.flood_volume = 0.06
	loose17.add_child(loosehold17)
	await physics_frame
	loose17.freeze = false
	loose17.apply_torque_impulse(Vector3(0, 0, 0.5))
	await _wait(15.0)
	check("  unballasted, it lolls over as it floods (deg)", tilt_deg(loose17), 20.0, 18.0)
	loose17.queue_free()
	bowl17.queue_free()

	# Lost buoyancy: a barge 2 m wide and 6 m long at draft 0.4 m with its middle 2 m open to the sea
	# settles at draft 0.4 x 6 / (6 - 2) = 0.6 m, level.
	var barge17 := make_shape_body(_box(Vector3(2, 1, 6)), 400.0, Vector3(10, 0.1, 6000), 0.0)
	var mid17: Node3D = COMPARTMENT.new()
	mid17.size = Vector3(2, 1, 2)
	mid17.holes.append(Vector4(0, -0.5, 0, 0.2))
	barge17.add_child(mid17)
	await physics_frame
	barge17.freeze = false
	await _wait(40.0)
	check("lost buoyancy: barge settles at the draft of the textbook (cm)", 100.0 * (0.5 - barge17.global_position.y), 60.0, 0.5)
	check("  level (deg)", tilt_deg(barge17), 0.0, 0.2)
	barge17.queue_free()

	# Slack water: a barge 2 m wide, 1 m deep and 4 m long at half water's density, with 0.2 m of water
	# loose across its whole floor, has GM 0.44 m as if the water were solid but 0.476 m less for its
	# free surface (ρ i / Δ): it lolls over. With a bulkhead down the middle the loss is a quarter, and
	# it stays upright. (A bulkhead across it too: water loose along its whole length takes away all of
	# its stability end to end, just as water across its whole width does side to side.)
	var slack17 := make_shape_body(_box(Vector3(2, 1, 4)), 500.0, Vector3(20, -0.2, 6000), 1.0)
	var one17: Node3D = COMPARTMENT.new()
	one17.size = Vector3(2, 1, 4)
	one17.flood_volume = 1.6
	slack17.add_child(one17)
	var split17 := make_shape_body(_box(Vector3(2, 1, 4)), 500.0, Vector3(30, -0.2, 6000), 1.0)
	for side in [-0.5, 0.5]:
		for end in [-1.0, 1.0]:
			var quarter: Node3D = COMPARTMENT.new()
			quarter.size = Vector3(1, 1, 2)
			quarter.position = Vector3(side, 0, end)
			quarter.flood_volume = 0.4
			split17.add_child(quarter)
	await physics_frame
	slack17.freeze = false
	split17.freeze = false
	await _wait(25.0)
	check("free surface: a slack tank across the barge makes it loll (deg)", tilt_deg(slack17), 15.0, 12.0)
	check("  bulkheads down the middle and across keep it upright (deg)", tilt_deg(split17), 0.0, 0.5)
	slack17.queue_free()
	split17.queue_free()

	# Side damage: a hole in the starboard (+x) compartment below the waterline lists the ship to starboard.
	var ship17 := make_shape_body(_box(Vector3(3, 1.2, 8)), 350.0, Vector3(40, 0.18, 6000), 0.0)
	for side in [-1.0, 1.0]:
		var sidec: Node3D = COMPARTMENT.new()
		sidec.size = Vector3(1.4, 1.2, 3)
		sidec.position = Vector3(0.75 * side, 0, 1.5)
		sidec.name = "Starboard" if side > 0.0 else "Port"
		ship17.add_child(sidec)
	await physics_frame
	ship17.freeze = false
	var dry17: Vector3 = ship17.inertia
	var starboard17: Node3D = ship17.get_node("Starboard")
	starboard17.add_hole(ship17.to_global(Vector3(1.5, -0.4, 1.5)), 0.02)
	await _wait(30.0)
	var heel17 := rad_to_deg(asin(clampf(-ship17.global_transform.basis.x.normalized().y, -1.0, 1.0)))
	check("side damage: lists to the holed side (deg, + to starboard)", heel17, 10.0, 9.0)
	check("  the port compartment stays dry (m³)", (ship17.get_node("Port") as Node3D).flood_volume, 0.0, 0.0)
	# Patch the hole and pump it dry: back upright, its centre of mass and inertia as before.
	starboard17.patch_holes()
	starboard17.pump_rate = 1.0
	await _wait(20.0)
	check("  patched and pumped dry: upright again (deg)", tilt_deg(ship17), 0.0, 0.5)
	check("  its inertia back to its own (%)", 100.0 * (ship17.inertia - dry17).length() / dry17.length(), 0.0, 0.001)
	check("  no water left (kg)", ship17.flood_mass, 0.0, 0.0)
	ship17.queue_free()

	# Taken out while flooded, a compartment takes its water's inertia with it.
	var tub17 := make_shape_body(_box(Vector3(2, 1, 2)), 300.0, Vector3(50, 0.0, 6000), 0.0)
	var tubhold17: Node3D = COMPARTMENT.new()
	tubhold17.size = Vector3(1.8, 0.8, 1.8)
	tubhold17.flood_volume = 0.3
	tub17.add_child(tubhold17)
	await physics_frame
	var tubdry17: Vector3 = tub17.inertia
	tub17.freeze = false
	await _wait(0.5)
	# 300 kg of water lying 9.26 cm deep on a floor 40 cm below the centre of mass adds m d² about
	# the two level axes.
	var tubwater17 := 300.0 * pow(0.4 - 0.5 * 0.3 / (1.8 * 1.8), 2.0)
	check("compartment removed while flooded: had water's inertia (%)", 100.0 * (tub17.inertia - tubdry17).length() / tubdry17.length(), 100.0 * sqrt(2.0) * tubwater17 / tubdry17.length(), 0.05)
	tubhold17.queue_free()
	await _wait(0.1)
	check("  its inertia back to its own (%)", 100.0 * (tub17.inertia - tubdry17).length() / tubdry17.length(), 0.0, 0.001)
	tub17.queue_free()

	# A body let go with water already in it keeps its speed: the water is its own, not let in.
	var cask17 := make_shape_body(_box(Vector3(1, 1, 1)), 300.0, Vector3(60, 20, 6000), 0.0)
	var caskhold17: Node3D = COMPARTMENT.new()
	caskhold17.size = Vector3(0.9, 0.9, 0.9)
	caskhold17.flood_volume = 0.5
	cask17.add_child(caskhold17)
	await physics_frame
	cask17.freeze = false
	cask17.linear_velocity = Vector3(5, 0, 0)
	await physics_frame
	await physics_frame
	check("let go with water in it: keeps its speed (m/s)", cask17.linear_velocity.x, 5.0, 0.001)
	cask17.queue_free()
	sea17.queue_free()

	# B18: cannons. Round shot in flight, its recoil, striking a plank, and skipping off the water.
	print("B18 Cannons")
	var sea18 := BW.new()
	sea18.size = Vector3(6000, 300, 6000)
	sea18.position = Vector3(0, 0, 20000)
	sea18.show_surface = false
	sea18.splashes = false
	sea18.ripples = false
	root.add_child(sea18)
	var g18 := float(ProjectSettings.get_setting("physics/3d/default_gravity"))
	# In a vacuum (air density 0): 100 m/s at 45 degrees comes back to the muzzle's height v² / g away.
	var vac18 := _gun18(Vector3(-2500, 30, 20000), Vector3(1, 1, 0), 100.0)
	var vball18: RigidBody3D = vac18.fire()
	vball18.air_density = 0.0
	var vrange18 := await _back_to_height(vball18, 30.0, 20.0)
	check("in a vacuum: range at 45 degrees, v² / g (m)", vrange18 - (-2500.0), 100.0 * 100.0 / g18, 2.0)
	vball18.queue_free()
	vac18.queue_free()

	# A 24-pounder at 5 degrees from 3 m above the sea, 488 m/s (1600 ft/s): it first grazes the water
	# where the same drag law worked through in steps of 0.1 ms puts it, and about a mile off, as the
	# gunners' range tables give for a long 24- or 32-pounder at 5 degrees.
	var gun18 := _gun18(Vector3(-2500, 3, 20000), Vector3(cos(deg_to_rad(5.0)), sin(deg_to_rad(5.0)), 0), 488.0)
	var shot18: RigidBody3D = gun18.fire()
	var graze18 := [INF, 0.0]
	shot18.water_entered.connect(func(_s: float, at: Vector3) -> void:
		if graze18[0] == INF:
			graze18[0] = at.x + 2500.0
			graze18[1] = shot18.get("_age"))
	t = 0.0
	while graze18[0] == INF and t < 15.0:
		await physics_frame
		t += dt
	var ref18 := _flight18(488.0, 5.0, 3.0, gun18.shot_diameter, gun18.shot_mass, g18)
	check("24-pounder at 5 degrees: first graze (m)", graze18[0], ref18[0], 0.005 * ref18[0])
	check("  time of flight (s)", graze18[1], ref18[1], 0.02)
	check("  about a mile, as the range tables give (m)", graze18[0], 1609.0, 240.0)
	var cd18 := BALL.drag_coefficient_at(0.5 * 343.0, gun18.shot_diameter, 1.2, 343.0)
	var cd18b := BALL.drag_coefficient_at(1.45 * 343.0, gun18.shot_diameter, 1.2, 343.0)
	check("  its drag coefficient at Mach 0.5, past the drag crisis", cd18, 0.2, 0.05)
	check("  and at Mach 1.45", cd18b, 1.0, 0.05)
	shot18.queue_free()
	gun18.queue_free()

	# Recoil: a 24-pounder on the deck of a 7.2 t barge fires to starboard: the barge takes the shot's
	# momentum and the powder gas's (at half the shot's speed) at the muzzle, so it moves off to port
	# and rolls away from the shot.
	var barge18 := make_shape_body(_box(Vector3(2, 1.5, 6)), 400.0, Vector3(-2000, 0.15, 20100), 0.0)
	barge18.wave_damping = 0.0
	var deck18: Node3D = CANNON.new()
	deck18.spread = 0.0
	deck18.shot_parent = root
	barge18.add_child(deck18)
	deck18.position = Vector3(1.1, 0.8, 0)
	deck18.rotation_degrees = Vector3(0, -90, 0)   # its -Z to +X: starboard
	await _wait(1.0)
	barge18.freeze = false
	await _wait(2.0)
	var state18 := PhysicsServer3D.body_get_direct_state(barge18.get_rid())
	var mass18 := barge18.mass
	var v18 := state18.linear_velocity
	var w18 := state18.angular_velocity
	var fired18: RigidBody3D = deck18.fire()
	var leaves18 := fired18.linear_velocity - v18
	var push18: float = deck18.recoil_momentum()
	check("recoil: barge takes the shot's and the gas's momentum (kg m/s)", -(state18.linear_velocity.x - v18.x) * mass18, push18, 0.001 * push18)
	check("  and its turn, 0.8 m above the centre of mass (kg m²/s)", (state18.angular_velocity.z - w18.z) * barge18.inertia.z, 0.8 * push18, 0.001 * push18)
	check("  the shot leaves at the muzzle speed (m/s)", leaves18.length(), 488.0, 0.01)
	await physics_frame
	check("  the barge moves off to port (m/s)", barge18.linear_velocity.x, -push18 / mass18, 0.01 * push18 / mass18)
	fired18.queue_free()
	barge18.queue_free()

	# Continuous collision: at 488 m/s the shot flies 8 m a step, but it strikes a 10 cm plank 30 m off
	# instead of passing through it.
	var plank18 := StaticBody3D.new()
	var pshape18 := CollisionShape3D.new()
	pshape18.shape = _box(Vector3(0.1, 4, 4))
	plank18.add_child(pshape18)
	plank18.position = Vector3(-1470, 20, 20200)
	root.add_child(plank18)
	var ccd18 := _gun18(Vector3(-1500, 20, 20200), Vector3(1, 0, 0), 488.0)
	var struck18 := [null]
	var ball18: RigidBody3D = ccd18.fire()
	ball18.air_density = 0.0
	ball18.hit.connect(func(body: Node, _at: Vector3, _v: Vector3) -> void: struck18[0] = body)
	await _wait(0.5)
	check("a shot at 488 m/s strikes a 10 cm plank (1 = struck)", 1.0 if struck18[0] == plank18 else 0.0, 1.0, 0.0)
	check("  and does not pass through it (1 = this side)", 1.0 if ball18.global_position.x < plank18.position.x else 0.0, 1.0, 0.0)
	ball18.queue_free()
	ccd18.queue_free()
	plank18.queue_free()

	# Skipping: a ball meeting the water at a shallow angle skips off it if it comes down at less than
	# 18° / √σ, σ its density over the water's (Birkhoff and others, 1944: 1 / √(10 σ) radians). The
	# angle found by halving: for iron round shot, aluminium and a ball a little denser than water, at 200 m/s.
	for sigma in [7.42, 2.7, 1.2]:
		var birkhoff := rad_to_deg(1.0 / sqrt(10.0 * sigma))
		var found := await _critical18(sigma, 200.0, birkhoff)
		check("skips below 18°/√σ: σ = %.2f (deg)" % sigma, found, birkhoff, 0.05 * birkhoff)
	# A steel ball 7 mm across skipping at 57 m/s leaves the water as Lyu, Yun and Wei (2021) measured:
	# [speed in, angle in, speed out, angle out].
	for case in [[58.58, 1.04, 56.81, 0.94], [58.41, 2.60, 50.86, 2.68], [58.66, 3.69, 47.42, 3.26], [56.69, 4.53, 38.75, 4.32]]:
		var out: Array = await _bounce18(7778.6, 0.0035, case[0], case[1])
		check("steel ball in at %.2f deg: speed out / speed in" % case[1], out[0], case[2] / case[0], 0.12)
		check("  and its angle out (deg)", out[1], case[3], 0.35)
	# Gravity aside, the speed does not matter: the push and the turn it gives both go as the speed squared.
	var slow18 := await _critical18(7.42, 200.0, 6.6)
	var fast18 := await _critical18(7.42, 400.0, 6.6)
	check("  the same at 200 and 400 m/s (deg)", fast18, slow18, 0.02 * slow18)

	# Ricochet fire: a 24-pounder fired level from 3 m comes down at about 1 degree, well under iron's
	# 6.5 degrees, and skips on across the water, each skip with a splash, until it slows and sinks.
	var level18 := _gun18(Vector3(-2500, 3, 20300), Vector3(1, 0, 0), 488.0)
	var skipper18: RigidBody3D = level18.fire()
	var splashes18 := [0, INF]
	skipper18.water_entered.connect(func(_s: float, at: Vector3) -> void:
		splashes18[0] += 1
		splashes18[1] = minf(splashes18[1], at.x + 2500.0))
	await _wait(25.0)
	var first18: float = splashes18[1]
	check("ricochet fire: fired level, it skips on (at least 5 skips)", mini(skipper18.skips, 10), 10.0, 5.0)
	check("  a splash at every touch (splashes - skips)", splashes18[0] - skipper18.skips, 0.5, 0.5)
	check("  and carries far past its first graze (at least 3 times as far)", minf((skipper18.global_position.x + 2500.0) / first18, 5.0), 5.0, 2.0)
	skipper18.queue_free()
	level18.queue_free()
	sea18.queue_free()

	# B19: damage. Shot through planking (BoundedBuoyancyHull): the wood resists it with σ + ρ v²
	# (Poncelet), oak ρ 750 kg/m³ and σ 15 MPa.
	print("B19 Damage")
	# Into solid oak: a 32-pounder fired from 400 and from 1000 yards goes 42 and 31.5 inches in
	# (Elvin, Handbook of the 32-pounder, 1805). The shot flies there through the air.
	for case in [[400.0, 0.4, 42.0], [1000.0, 1.3, 31.5]]:
		var yards: float = case[0] * 0.9144
		var wall19 := StaticBody3D.new()
		var ws19 := CollisionShape3D.new()
		ws19.shape = _box(Vector3(4, 400, 400))
		wall19.add_child(ws19)
		var oak19: Node = HULL.new()
		oak19.thickness = 3.0
		oak19.splinters = 0
		wall19.add_child(oak19)
		wall19.position = Vector3(-1000 + yards + 2.0, 0, 40000)
		root.add_child(wall19)
		var gun19 := _gun18(Vector3(-1000, 10, 40000), Vector3(cos(deg_to_rad(case[1])), sin(deg_to_rad(case[1])), 0), 488.0)
		gun19.gun = CANNON.Gun.POUNDER_32
		gun19.muzzle_speed = 488.0
		var depth19 := [NAN, 0.0]
		var shot19: RigidBody3D = gun19.fire()
		oak19.struck.connect(func(ball: RigidBody3D, _at: Vector3, through: bool, _out: float) -> void:
			depth19[0] = ball.penetration
			depth19[1] = 1.0 if through else 0.0)
		await _wait(5.0)
		check("32-pounder from %d yards: into solid oak (in)" % int(case[0]), depth19[0] / 0.0254, case[2], 0.04 * case[2])
		wall19.queue_free()
		gun19.queue_free()

	# A ship with two side compartments and 10 cm planking, shot from 8 m off its starboard side, down
	# through it: in above the waterline, out of the far side below it. Two holes, each as big as the
	# shot; the ship takes all the momentum the shot loses (no splinters to share it, for this one); the sea comes in
	# through the hole below the waterline only. Then a second shot, to throw splinters, which float.
	var sea19 := BW.new()
	sea19.size = Vector3(400, 200, 400)
	sea19.position = Vector3(0, 0, 30000)
	sea19.show_surface = false
	sea19.splashes = false
	sea19.ripples = false
	root.add_child(sea19)
	var ship19 := make_shape_body(_box(Vector3(3, 1.2, 8)), 350.0, Vector3(0, 0.18, 30000), 0.0)
	ship19.collision_layer = 1
	ship19.collision_mask = 1
	var plank19: Node = HULL.new()
	plank19.thickness = 0.1
	plank19.splinters = 0
	ship19.add_child(plank19)
	for side in [-1.0, 1.0]:
		var room: Node3D = COMPARTMENT.new()
		room.size = Vector3(1.4, 1.2, 3)
		room.position = Vector3(0.75 * side, 0, 0)
		room.name = "Starboard" if side > 0.0 else "Port"
		ship19.add_child(room)
	await physics_frame
	ship19.freeze = false
	await _wait(2.0)
	var holes19 := []
	plank19.holed.connect(func(at: Vector3, area: float, room: Node) -> void: holes19.append([at, area, room]))
	var p0 := ship19.global_position
	var aim19 := ship19.to_global(Vector3(-1.5, -0.3, 0)) - Vector3(8.0, 2.5, 0) - p0
	var fire19 := _gun18(p0 + Vector3(8.0, 2.5, 0), aim19, 400.0)
	var state19 := PhysicsServer3D.body_get_direct_state(ship19.get_rid())
	var before19 := state19.linear_velocity * ship19.mass
	var ball19: RigidBody3D = fire19.fire()
	var dir19 := aim19.normalized()
	var speeds19 := []
	var in19 := [Vector3.ZERO]
	ball19.hit.connect(func(body: Node, _at: Vector3, v: Vector3) -> void:
		if body == ship19 and in19[0] == Vector3.ZERO:
			in19[0] = v)
	plank19.struck.connect(func(_ball: RigidBody3D, _at: Vector3, through: bool, out: float) -> void:
		if through:
			speeds19.append(out))
	await _wait(0.1)
	var out19: float = speeds19[-1] if speeds19.size() == 2 else NAN
	# Across: the water does not push the ship back that way within a tenth of a second.
	var lost19: float = fire19.shot_mass * (in19[0].x - out19 * dir19.x)
	var gained19: float = (state19.linear_velocity * ship19.mass - before19).x
	# What Poncelet's law gives for the two sides, 10 cm each, met at the shot's angle off square.
	var area_shot: float = PI * pow(0.5 * float(fire19.shot_diameter), 2.0)
	var cos19 := absf(dir19.x)
	var mid19: float = HULL.speed_after(fire19.shot_mass, area_shot, in19[0].length(), 0.1 / cos19, 750.0, 15e6)
	var far19: float = HULL.speed_after(fire19.shot_mass, area_shot, mid19, 0.1 / cos19, 750.0, 15e6)
	check("shot through a ship: two holes", holes19.size(), 2.0, 0.0)
	var area19: float = holes19[0][1] if holes19.size() > 0 else 0.0
	var shot_d19: float = fire19.shot_diameter
	check("  each as big as the shot (cm²)", 1e4 * area19, 1e4 * PI * pow(0.5 * shot_d19, 2.0), 0.01)
	check("  one in each compartment (1 = yes)", 1.0 if holes19.size() == 2 and holes19[0][2] != holes19[1][2] else 0.0, 1.0, 0.0)
	check("  the ship takes the momentum the shot loses (%)", 100.0 * gained19 / lost19, 100.0, 3.0)
	check("  out of the far side at the speed Poncelet's law gives (m/s)", out19, far19, 0.01 * far19)
	await _wait(8.0)
	var port19: Node3D = ship19.get_node("Port")
	var starboard19: Node3D = ship19.get_node("Starboard")
	check("  the sea comes in below the waterline (m³)", port19.flood_volume, 0.5, 0.49)
	check("  and not above it (m³)", starboard19.flood_volume, 0.0, 0.0)
	plank19.splinters = 4
	var again19: RigidBody3D = fire19.fire()
	if again19 == null:
		fire19.reload_time = 0.0
		await physics_frame
		again19 = fire19.fire()
	await _wait(0.1)
	var through19: Array = []
	for n in root.get_children():
		if n.name.begins_with("Splinter"):
			through19.append(n)
	await _wait(8.0)
	var floating19 := 0
	for sp in through19:
		if is_instance_valid(sp) and absf(sp.global_position.y - sea19.height_at(sp.global_position)) < 0.1:
			floating19 += 1
	check("  splinters thrown off (8: four in, four out)", through19.size(), 8.0, 0.0)
	check("  at least the four thrown off above the water float (of 8)", mini(floating19, 4), 4.0, 0.0)
	# A spent shot lodges in the planking: the ship takes all its momentum, and no hole is made.
	var spent19 := _gun18(ship19.global_position + Vector3(5.0, 0.3, 0), Vector3(-1, 0, 0), 30.0)
	before19 = state19.linear_velocity * ship19.mass
	var holes_before19 := holes19.size()
	var slow19: RigidBody3D = spent19.fire()
	slow19.air_density = 0.0
	var lodged19 := [false]
	plank19.struck.connect(func(_ball: RigidBody3D, _at: Vector3, through: bool, _out: float) -> void: lodged19[0] = not through)
	await _wait(0.5)
	check("a spent shot (30 m/s) lodges (1 = yes)", 1.0 if lodged19[0] and not is_instance_valid(slow19) else 0.0, 1.0, 0.0)
	check("  making no hole", holes19.size() - holes_before19, 0.0, 0.0)
	# A ship of two boxes side by side, touching (as a convex decomposition is): shot goes in through one
	# side and out of the other, not stopping at the face they share; the same two holes and speed out
	# as through one box.
	var ship19c := BB.new()
	for side in [-0.75, 0.75]:
		var part19c := CollisionShape3D.new()
		part19c.shape = _box(Vector3(1.5, 1.2, 8))
		part19c.position = Vector3(side, 0, 0)
		ship19c.add_child(part19c)
	ship19c.density = 350.0
	ship19c.air_density = 0.0
	_place(ship19c, Vector3(0, 0.18, 30120), 0.0)
	ship19c.collision_layer = 1
	ship19c.collision_mask = 1
	var plank19c: Node = HULL.new()
	plank19c.thickness = 0.1
	plank19c.splinters = 0
	ship19c.add_child(plank19c)
	await physics_frame
	ship19c.freeze = false
	await _wait(2.0)
	var holes19c := []
	var out19c := []
	plank19c.holed.connect(func(at: Vector3, _area: float, _room: Node) -> void: holes19c.append(at))
	plank19c.struck.connect(func(_ball: RigidBody3D, _at: Vector3, through: bool, out: float) -> void: out19c.append(out if through else 0.0))
	var level19c := _gun18(ship19c.global_position + Vector3(8.0, 0.4, 0), Vector3(-1, 0, 0), 400.0)
	var in19c := [0.0]
	var ball19c: RigidBody3D = level19c.fire()
	ball19c.hit.connect(func(_body: Node, _at: Vector3, v: Vector3) -> void:
		if in19c[0] == 0.0:
			in19c[0] = v.length())
	await _wait(0.1)
	var two19c: float = HULL.speed_after(level19c.shot_mass, area_shot, HULL.speed_after(level19c.shot_mass, area_shot, in19c[0], 0.1, 750.0, 15e6), 0.1, 750.0, 15e6)
	check("a ship of two boxes touching: two holes, through both", holes19c.size(), 2.0, 0.0)
	check("  the second in its far side (m from the first)", (holes19c[1] - holes19c[0]).length() if holes19c.size() == 2 else 0.0, 3.0, 0.05)
	check("  out at the speed two planks give (m/s)", out19c[-1] if out19c.size() == 2 else 0.0, two19c, 0.01 * two19c)
	ship19c.queue_free()
	level19c.queue_free()
	# Shot that meets the sea on its way to a ship. Fired low, at 4°, to land 3 m short of its side, it
	# skips, and strikes the side just above the water, not 14 cm under it where its straight path goes. Fired
	# steeply, at 20°, to land 1.5 m short, it plunges in and strikes below the waterline, slowed by the
	# water as much as the drag of a ball wholly under it would over that way (it is under for most of it).
	var ship19b := make_shape_body(_box(Vector3(3, 3, 8)), 500.0, Vector3(0, 0, 30060), 0.0)   # 1.5 m deep
	ship19b.collision_layer = 1
	ship19b.collision_mask = 1
	var plank19b: Node = HULL.new()
	plank19b.thickness = 0.1
	plank19b.splinters = 0
	ship19b.add_child(plank19b)
	await physics_frame
	ship19b.freeze = false
	await _wait(2.0)
	var wet19 := []
	for case in [[4.0, 3.0], [20.0, 1.5]]:
		var deg: float = case[0]
		var short: float = case[1]
		var touch: float = ship19b.global_position.x + 1.5 + short   # where the ball's bottom meets the water
		var dir := Vector3(-cos(deg_to_rad(deg)), -sin(deg_to_rad(deg)), 0)
		var gun := _gun18(Vector3(touch + 10.0, 5.0, ship19b.global_position.z), dir, 400.0)
		var a19: float = 0.5 * float(gun.shot_diameter)
		gun.global_position = Vector3(touch + 10.0, sea19.global_position.y + a19 + 10.0 * tan(deg_to_rad(deg)), ship19b.global_position.z)
		var got := [NAN, NAN, 0.0]
		var ball: RigidBody3D = gun.fire()
		ball.hit.connect(func(body: Node, at: Vector3, v: Vector3) -> void:
			if body == ship19b and is_nan(got[0]):
				got[0] = at.y - sea19.global_position.y
				got[1] = v.length())
		ball.water_entered.connect(func(_speed: float, _at: Vector3) -> void: got[2] += 1.0)
		await _wait(0.3)
		gun.queue_free()
		var m19: float = ball.own_mass if is_instance_valid(ball) else gun.shot_mass
		var vol19 := 4.0 / 3.0 * PI * a19 * a19 * a19
		var k19 := 0.5 * 1000.0 * 0.47 * PI * a19 * a19 / (m19 + 0.5 * 1000.0 * vol19)
		wet19.append([got[0], got[1], got[2], 400.0 * exp(-k19 * short / cos(deg_to_rad(deg))), a19, short * tan(deg_to_rad(deg)) - a19])
	check("shot meeting the sea 3 m short at 4°: it touches the water first (1 = yes)", minf(wet19[0][2], 1.0), 1.0, 0.0)
	check("  and strikes the side above the water as it skips up (cm)", 100.0 * wet19[0][0], 10.0, 10.0)
	check("shot meeting the sea 1.5 m short at 20°: strikes under the water (cm)", 100.0 * wet19[1][0], -100.0 * wet19[1][5], 15.0)
	check("  slowed by the water (m/s)", wet19[1][1], wet19[1][3], 0.05 * wet19[1][3])
	ship19b.queue_free()
	ship19.queue_free()
	fire19.queue_free()
	spent19.queue_free()
	for sp in through19:
		if is_instance_valid(sp):
			sp.queue_free()
	sea19.queue_free()

	print("PASS" if failures == 0 else "FAIL (%d checks)" % failures)
	quit(1 if failures else 0)


const FOIL := preload("res://addons/bounded_buoyancy/bounded_buoyancy_foil.gd")
const COMPARTMENT := preload("res://addons/bounded_buoyancy/bounded_buoyancy_compartment.gd")
const THRUSTER := preload("res://addons/bounded_buoyancy/bounded_buoyancy_thruster.gd")


const CANNON := preload("res://addons/bounded_buoyancy/bounded_buoyancy_cannon.gd")
const BALL := preload("res://addons/bounded_buoyancy/bounded_buoyancy_cannonball.gd")
const HULL := preload("res://addons/bounded_buoyancy/bounded_buoyancy_hull.gd")


## A 24-pounder with no scatter at `at`, pointing along `dir`, firing at `speed`; shots last.
func _gun18(at: Vector3, dir: Vector3, speed: float) -> Node3D:
	var gun: Node3D = CANNON.new()
	gun.spread = 0.0
	gun.muzzle_speed = speed
	gun.shot_lifetime = 0.0
	gun.shot_parent = root
	root.add_child(gun)
	gun.global_transform = Transform3D(Basis.looking_at(dir.normalized(), Vector3.UP if absf(dir.normalized().y) < 0.99 else Vector3.BACK), at)
	return gun


## Where `ball` comes back down through the height `y` (world X, interpolated between steps).
func _back_to_height(ball: RigidBody3D, y: float, seconds: float) -> float:
	var prev := ball.global_position
	var t := 0.0
	while t < seconds:
		await physics_frame
		t += 1.0 / 60.0
		var now := ball.global_position
		if prev.y >= y and now.y < y and now.y < prev.y:
			return lerpf(prev.x, now.x, (prev.y - y) / (prev.y - now.y))
		prev = now
	return NAN


## The flight of a ball `d` across of mass `m` fired at `v0` at `deg` degrees from `h` above the water,
## through still air of 1.2 kg/m³, worked through in steps of 0.1 ms (second order) with the
## cannonball's drag law, its air buoyancy and the air it takes along: [range, time] to the moment its
## bottom touches the water.
func _flight18(v0: float, deg: float, h: float, d: float, m: float, g: float) -> Array:
	var rho := 1.2
	var vol := PI / 6.0 * d * d * d
	var mi := m + 0.5 * rho * vol
	var area := PI * 0.25 * d * d
	var a := 0.5 * d
	var p := Vector2(0.0, h)
	var v := Vector2(cos(deg_to_rad(deg)), sin(deg_to_rad(deg))) * v0
	var step := 1e-4
	var t := 0.0
	var up := (rho * vol - m) * g / mi
	while p.y - a > 0.0:
		var s := v.length()
		var acc := -0.5 * BALL.drag_coefficient_at(s, d, rho, 343.0) * rho * area * s / mi * v + Vector2(0.0, up)
		var vm := v + acc * 0.5 * step
		var sm := vm.length()
		var accm := -0.5 * BALL.drag_coefficient_at(sm, d, rho, 343.0) * rho * area * sm / mi * vm + Vector2(0.0, up)
		var prev := p
		p += vm * step
		v += accm * step
		t += step
		if p.y - a <= 0.0:
			var f := (prev.y - a) / (prev.y - p.y)
			return [prev.x + f * (p.x - prev.x), t - step + f * step]
	return [p.x, t]


## The angle (degrees below level) under which a ball of relative density `sigma`, 14 cm across,
## meeting still water at `speed`, skips; found by halving between half and one and a half of `guess`.
func _critical18(sigma: float, speed: float, guess: float) -> float:
	var lo := 0.5 * guess
	var hi := 1.5 * guess
	for n in 12:
		var mid := 0.5 * (lo + hi)
		if await _skips18(sigma, speed, mid):
			lo = mid
		else:
			hi = mid
	return 0.5 * (lo + hi)


## A ball `a` in radius of `density`, its bottom a tenth of a millimetre above the sea of B18 and
## coming down to meet it at `speed`, `deg` below level.
func _ball18(density: float, a: float, speed: float, deg: float) -> RigidBody3D:
	var ball := BALL.new()
	var cs := CollisionShape3D.new()
	cs.shape = _sphere(a)
	ball.add_child(cs)
	ball.density = density
	ball.lifetime = 0.0
	ball.collision_layer = 0
	ball.collision_mask = 0
	var dir := Vector3(cos(deg_to_rad(deg)), -sin(deg_to_rad(deg)), 0.0)
	ball.position = Vector3(1500, a + 1e-4, 20000)
	ball.linear_velocity = dir * speed
	root.add_child(ball)
	return ball


## [speed out / speed in, angle out (deg)] of a ball of `density` and radius `a` meeting the water at
## `speed`, `deg` below level, in still air.
func _bounce18(density: float, a: float, speed: float, deg: float) -> Array:
	var ball := _ball18(density, a, speed, deg)
	ball.air_density = 0.0
	for n in 120:
		await physics_frame
		if ball.skips > 0:
			break
	var v: Vector3 = ball.exit_velocity
	ball.queue_free()
	return [v.length() / speed, rad_to_deg(atan2(v.y, v.x))]


## Whether a ball of relative density `sigma` meeting the water at `speed`, `deg` below level, skips.
func _skips18(sigma: float, speed: float, deg: float) -> bool:
	var ball := _ball18(1000.0 * sigma, 0.07, speed, deg)
	var skipped := false
	for n in 90:
		await physics_frame
		if ball.skips > 0:
			skipped = true
			break
		if ball.submerged_volume >= 0.999 * ball.volume:
			break
	ball.queue_free()
	return skipped


## Wait `seconds` of physics.
func _wait(seconds: float) -> void:
	var n := roundi(seconds * 60.0)
	for i in n:
		await physics_frame


## A small open motorboat, 2.4 m long, pointing `yaw_deg` anticlockwise from -z, ballasted low,
## with a keel, a rudder (its second foil) and a propeller as asked.
func make_motorboat(at: Vector3, yaw_deg: float, keel := true, rudder := true, prop := true) -> RigidBody3D:
	var hull := ConvexPolygonShape3D.new()
	hull.points = PackedVector3Array([Vector3(-0.45, 0.2, -1.1), Vector3(0.45, 0.2, -1.1), Vector3(-0.3, -0.2, -0.9), Vector3(0.3, -0.2, -0.9),
		Vector3(-0.45, 0.2, 1.2), Vector3(0.45, 0.2, 1.2), Vector3(-0.3, -0.2, 1.1), Vector3(0.3, -0.2, 1.1), Vector3(0, 0.2, -1.6), Vector3(0, -0.1, -1.4)])
	var b := make_shape_body(hull, 250.0, at, 0.0)
	b.rotation_degrees = Vector3(0, yaw_deg, 0)
	b.center_of_mass_mode = RigidBody3D.CENTER_OF_MASS_MODE_CUSTOM
	b.center_of_mass = Vector3(0, -0.1008, -0.0445)   # the hull's centroid, 12 cm lower (as in the .NET checks)
	if keel:
		add_foil(b, Vector3(0, -0.4, 0), 0.15, 1.5)
	if rudder:
		add_foil(b, Vector3(0, -0.3, 1.15), 0.06, 2.0, 0, 0.04)
	if prop:
		var t: Node3D = THRUSTER.new()
		t.max_thrust = 400.0
		t.max_speed = 5.0
		t.diameter = 0.25
		t.position = Vector3(0, -0.3, 1.1)
		b.add_child(t)
	return b


## A sailing dinghy, 3.6 m long, pointing `yaw_deg` anticlockwise from -z: a ballasted hull, a
## centreboard, a rudder and an 8 m² self-trimming sail on its mast.
func make_dinghy(at: Vector3, yaw_deg: float) -> RigidBody3D:
	var hull := ConvexPolygonShape3D.new()
	hull.points = PackedVector3Array([Vector3(-0.7, 0.25, -1.4), Vector3(0.7, 0.25, -1.4), Vector3(-0.45, -0.15, -1.2), Vector3(0.45, -0.15, -1.2),
		Vector3(-0.7, 0.25, 1.7), Vector3(0.7, 0.25, 1.7), Vector3(-0.45, -0.15, 1.6), Vector3(0.45, -0.15, 1.6), Vector3(0, 0.25, -1.9), Vector3(0, -0.05, -1.7)])
	var b := make_shape_body(hull, 160.0, at, 0.0)
	b.air_density = 1.2
	b.rotation_degrees = Vector3(0, yaw_deg, 0)
	b.center_of_mass_mode = RigidBody3D.CENTER_OF_MASS_MODE_CUSTOM
	b.center_of_mass = Vector3(0, -0.3811, 0.0562)   # the hull's centroid, 45 cm lower
	add_foil(b, Vector3(0, -0.6, -0.1), 0.5, 2.0)
	add_foil(b, Vector3(0, -0.35, 1.65), 0.12, 2.5, 0, 0.06)
	var sail := add_foil(b, Vector3(0, 2.1, -0.6), 8.0, 3.0, 1, 0.55)
	sail.auto_trim = true
	return b


func add_foil(b: Node3D, at: Vector3, area: float, ar: float, medium := 0, effort := 0.0) -> Node3D:
	var f: Node3D = FOIL.new()
	f.area = area
	f.aspect_ratio = ar
	f.medium = medium
	f.effort_offset = effort
	f.position = at
	b.add_child(f)
	return f


## The body's second foil (its rudder).
func _rudder(b: Node3D) -> Node3D:
	var n := 0
	for c in b.get_children():
		if c is FOIL:
			n += 1
			if n == 2:
				return c
	return null


func _thruster(b: Node3D) -> Node3D:
	for c in b.get_children():
		if c is THRUSTER:
			return c
	return null


## Heading of the body's bow, in degrees anticlockwise from -z.
func _yaw(b: Node3D) -> float:
	var f := -b.global_transform.basis.z
	return rad_to_deg(atan2(-f.x, -f.z))


## Rudder angle steering `b` towards the horizontal direction `course` (a positive angle turns it to
## starboard, clockwise).
func _autopilot(b: RigidBody3D, course: Vector3) -> float:
	var f := -b.global_transform.basis.z
	var error := rad_to_deg(atan2(f.cross(course).y, f.x * course.x + f.z * course.z))
	return clampf(-1.5 * error + 25.0 * rad_to_deg(b.angular_velocity.y) / 60.0, -30.0, 30.0)


## How a body's height `ys` (at times `ts`) follows the water's rise `water`, once the straight line
## that best fits its height (a steady drift) is taken out: (rise and fall over the water's, correlation).
func _follows(ts: Array, ys: Array, water: Array) -> Vector2:
	var n := ts.size()
	var tm := 0.0
	var ym := 0.0
	var wm := 0.0
	for i in n:
		tm += ts[i] / n
		ym += ys[i] / n
		wm += water[i] / n
	var stt := 0.0
	var sty := 0.0
	for i in n:
		stt += (ts[i] - tm) * (ts[i] - tm)
		sty += (ts[i] - tm) * (ys[i] - ym)
	var lo := 1e18
	var hi := -1e18
	var wlo := 1e18
	var whi := -1e18
	var cov := 0.0
	var vb := 0.0
	var vw := 0.0
	for i in n:
		var b: float = ys[i] - ym - sty / stt * (ts[i] - tm)
		lo = minf(lo, b)
		hi = maxf(hi, b)
		wlo = minf(wlo, water[i])
		whi = maxf(whi, water[i])
		cov += b * (water[i] - wm)
		vb += b * b
		vw += (water[i] - wm) * (water[i] - wm)
	return Vector2((hi - lo) / (whi - wlo), cov / sqrt(vb * vw))


## Heights of the successive crests of a bob about `rest`, from a list of heights.
func _crests(ys: Array, rest: float) -> Array:
	var out := []
	for k in range(1, ys.size() - 1):
		if ys[k] > ys[k - 1] and ys[k] >= ys[k + 1] and ys[k] > rest:
			out.append(ys[k] - rest)
	return out


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
	for ki in 4:
		var a: Vector2 = rot[ki]
		var b: Vector2 = rot[(ki + 1) % 4]
		if a.y <= 0.0:
			poly.append(a)
		if (a.y < 0.0 and b.y > 0.0) or (a.y > 0.0 and b.y < 0.0):
			poly.append(a + (b - a) * (a.y / (a.y - b.y)))
	var area := 0.0
	var cy := 0.0
	for ki in poly.size():
		var p: Vector2 = poly[ki]
		var q: Vector2 = poly[(ki + 1) % poly.size()]
		var cr := p.x * q.y - q.x * p.y
		area += cr
		cy += (p.y + q.y) * cr
	return -cy / (3.0 * area)
