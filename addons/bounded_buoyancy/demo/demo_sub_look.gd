extends RefCounted
## The submarine of the SUBMARINE scene, drawn in code: a hull with a rounded bow, a long straight
## middle and a stern tapering to the propeller, painted light grey, red along the bottom with a black
## band between; a streamlined sail (the fin) with planes on its sides, a bridge, a periscope and masts
## and navigation lights; a row of lit round windows along each side; a searchlight in the bow; four
## control surfaces at the stern, the upper and lower ones turning with the rudder; and a seven-bladed
## bronze propeller that turns with the throttle. For show: the physics is the scene's capsule.
##
## make() puts it on `parent`, the bow toward -Z, and returns its moving parts.

const HULL_SHADER := """
shader_type spatial;
uniform float long = 22.0;
uniform float radius = 1.6;
varying vec3 p;
void vertex() { p = VERTEX; }
void fragment() {
	float up = p.y / radius;
	vec3 grey = vec3(0.50, 0.55, 0.60);
	vec3 red = vec3(0.52, 0.15, 0.10);
	vec3 c = up > -0.4 ? grey : red;
	// The black band where the two paints meet.
	c = mix(c, vec3(0.04), step(abs(up + 0.32), 0.08));
	// Plating: rings every two metres and a seam along each side.
	float ring = smoothstep(0.03, 0.0, abs(fract(p.z / 2.0) - 0.5) - 0.47);
	c *= 1.0 - 0.18 * ring;
	c *= 1.0 - 0.15 * smoothstep(0.02, 0.0, abs(up - 0.55));
	// Free-flooding holes along the casing, above the band.
	float holes = step(abs(up - 0.25), 0.04) * step(0.5, fract(p.z * 1.5)) * step(abs(p.z), long * 0.32);
	c = mix(c, vec3(0.03), holes * 0.85);
	// White depth marks at the bow and stern.
	float mark = step(abs(abs(p.z) - long * 0.42), 0.05) * step(-0.9, up) * step(up, 0.0) * step(0.5, fract(up * 6.0));
	c = mix(c, vec3(0.95), mark);
	ALBEDO = c;
	ROUGHNESS = 0.5;
	METALLIC = 0.25;
}
"""


static func _mat(c: Color, rough := 0.5, metal := 0.2, glow := 0.0) -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	m.albedo_color = c
	m.roughness = rough
	m.metallic = metal
	if glow > 0.0:
		m.emission_enabled = true
		m.emission = c
		m.emission_energy_multiplier = glow
	return m


## The hull's radius `u` metres back from the bow.
static func hull_radius(u: float, long: float, radius: float) -> float:
	var bow := 3.4
	var stern := 7.0
	if u < bow:
		var a := 1.0 - u / bow
		return radius * sqrt(maxf(1.0 - a * a, 0.0))
	if u > long - stern:
		var a := (u - (long - stern)) / stern
		return radius * (1.0 - 0.84 * pow(a, 1.5))
	return radius


static func make(parent: Node3D, long: float, radius: float) -> Dictionary:
	var root := Node3D.new()
	root.name = "Look"
	parent.add_child(root)
	# The hull, turned on a lathe.
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var rings := 80
	var around := 36
	var pts := []
	for i in rings + 1:
		var u := long * i / rings
		var r := hull_radius(u, long, radius)
		var row := []
		for j in around:
			var a := TAU * j / around
			row.append(Vector3(cos(a) * r, sin(a) * r, u - 0.5 * long))
		pts.append(row)
	for i in rings:
		for j in around:
			var j1 := (j + 1) % around
			for v in [pts[i][j], pts[i][j1], pts[i + 1][j], pts[i][j1], pts[i + 1][j1], pts[i + 1][j]]:
				st.add_vertex(v)
	var tail := Vector3(0, 0, 0.5 * long + 0.05)
	for j in around:
		st.add_vertex(pts[rings][j])
		st.add_vertex(pts[rings][(j + 1) % around])
		st.add_vertex(tail)
	st.generate_normals()
	var hull := MeshInstance3D.new()
	hull.mesh = st.commit()
	var sh := Shader.new()
	sh.code = HULL_SHADER
	var hm := ShaderMaterial.new()
	hm.shader = sh
	hm.set_shader_parameter("long", long)
	hm.set_shader_parameter("radius", radius)
	hull.material_override = hm
	root.add_child(hull)
	var grey := _mat(Color(0.50, 0.55, 0.60), 0.5, 0.25)
	var dark := _mat(Color(0.05, 0.06, 0.07), 0.3, 0.4)
	var bronze := _mat(Color(0.80, 0.55, 0.25), 0.25, 0.9)
	# The sail: an elliptic fin, its top flat, with the bridge windows at its front.
	var sail_at := Vector3(0, radius + 1.0, -0.2 * long)
	var sail := MeshInstance3D.new()
	var sc := CylinderMesh.new()
	sc.top_radius = 1.0
	sc.bottom_radius = 1.0
	sc.height = 2.9
	sc.radial_segments = 32
	sail.mesh = sc
	sail.material_override = grey
	sail.position = sail_at + Vector3(0, -0.15, 0)
	sail.scale = Vector3(0.48, 1.0, 2.1)
	root.add_child(sail)
	for side in [-1.0, 1.0]:
		_box(root, sail_at + Vector3(side * 0.36, 0.95, -1.55), Vector3(0.06, 0.22, 0.5), dark, Vector3(0, side * 0.5, 0))
		# The planes on the sail's sides.
		_box(root, sail_at + Vector3(side * 1.25, 0.3, -0.6), Vector3(1.6, 0.1, 0.9), grey)
		# Navigation lights: red to port, green to starboard.
		var lamp := MeshInstance3D.new()
		var lm := SphereMesh.new()
		lm.radius = 0.09
		lm.height = 0.18
		lamp.mesh = lm
		lamp.material_override = _mat(Color(1.0, 0.1, 0.05) if side < 0.0 else Color(0.1, 1.0, 0.3), 0.3, 0.0, 4.0)
		lamp.position = sail_at + Vector3(side * 0.5, 1.2, -1.2)
		root.add_child(lamp)
	for m in [[0.0, -0.4, 2.0, 0.09], [0.0, 0.2, 1.6, 0.13], [0.0, 0.7, 1.2, 0.07]]:
		var mast := MeshInstance3D.new()
		var mc := CylinderMesh.new()
		mc.top_radius = m[3]
		mc.bottom_radius = m[3]
		mc.height = m[2]
		mast.mesh = mc
		mast.material_override = dark
		mast.position = sail_at + Vector3(m[0], 1.3 + 0.5 * m[2], m[1])
		root.add_child(mast)
	_box(root, sail_at + Vector3(0, 1.3 + 2.0, -0.4), Vector3(0.14, 0.18, 0.3), dark)
	# A hatch on the deck fore and aft of the sail.
	for z in [-0.38 * long, 0.12 * long]:
		var hatch := MeshInstance3D.new()
		var hc := CylinderMesh.new()
		hc.top_radius = 0.38
		hc.bottom_radius = 0.42
		hc.height = 0.12
		hatch.mesh = hc
		hatch.material_override = grey
		hatch.position = Vector3(0, radius + 0.02, z)
		root.add_child(hatch)
	# Round windows along both sides, lit from within, each in a dark rim.
	var glow := _mat(Color(1.0, 0.82, 0.45), 0.3, 0.0, 3.0)
	for side in [-1.0, 1.0]:
		for k in 7:
			var z := -0.28 * long + k * 1.6
			var a := deg_to_rad(28.0)
			var r := hull_radius(z + 0.5 * long, long, radius)
			var at := Vector3(side * cos(a) * r, sin(a) * r, z)
			var n := Vector3(side * cos(a), sin(a), 0)
			var rim := MeshInstance3D.new()
			var rc := CylinderMesh.new()
			rc.top_radius = 0.24
			rc.bottom_radius = 0.24
			rc.height = 0.08
			rim.mesh = rc
			rim.material_override = dark
			root.add_child(rim)
			_face(rim, at, n)
			var pane := MeshInstance3D.new()
			var pc := CylinderMesh.new()
			pc.top_radius = 0.17
			pc.bottom_radius = 0.17
			pc.height = 0.1
			pane.mesh = pc
			pane.material_override = glow
			root.add_child(pane)
			_face(pane, at + n * 0.01, n)
	# A searchlight in the bow, lighting the water ahead.
	var lens := MeshInstance3D.new()
	var lc := SphereMesh.new()
	lc.radius = 0.28
	lc.height = 0.56
	lens.mesh = lc
	lens.material_override = _mat(Color(1.0, 0.95, 0.8), 0.2, 0.0, 5.0)
	lens.position = Vector3(0, -0.4, -0.5 * long + 0.25)
	lens.scale = Vector3(1, 1, 0.5)
	root.add_child(lens)
	var beam := SpotLight3D.new()
	beam.light_color = Color(1.0, 0.95, 0.85)
	beam.light_energy = 6.0
	beam.spot_range = 45.0
	beam.spot_angle = 22.0
	beam.spot_attenuation = 0.8
	beam.position = Vector3(0, -0.4, -0.5 * long - 0.2)
	beam.rotation = Vector3(deg_to_rad(-6.0), 0, 0)
	root.add_child(beam)
	# Control surfaces at the stern: the upper and lower ones are the rudder, and turn with it.
	var fins_z := 0.5 * long - 2.4
	var rudders := []
	for k in 4:
		var a := PI * 0.5 * k
		var out := Vector3(cos(a), sin(a), 0)
		var pivot := Node3D.new()
		pivot.position = Vector3(0, 0, fins_z)
		root.add_child(pivot)
		var r0 := hull_radius(fins_z + 0.5 * long, long, radius)
		var fin := _box(pivot, out * (r0 + 0.6), Vector3(0.12, 1.3, 1.3), grey)
		fin.rotation = Vector3(0, 0, a - PI * 0.5)
		if k % 2 == 1:
			rudders.append(pivot)
	# The propeller: a cone of a hub and seven broad blades, set at an angle and swept back.
	var prop := Node3D.new()
	prop.name = "Propeller"
	prop.position = Vector3(0, 0, 0.5 * long - 0.05)
	root.add_child(prop)
	var hub := MeshInstance3D.new()
	var hc2 := CylinderMesh.new()
	hc2.top_radius = 0.3
	hc2.bottom_radius = 0.06
	hc2.height = 0.8
	hub.mesh = hc2
	hub.material_override = bronze
	hub.rotation = Vector3(PI * 0.5, 0, 0)
	hub.position = Vector3(0, 0, 0.35)
	prop.add_child(hub)
	for k in 7:
		var blade := Node3D.new()
		blade.rotation = Vector3(0, 0, TAU * k / 7.0)
		prop.add_child(blade)
		var b := _box(blade, Vector3(0, 0.5, 0.25), Vector3(0.36, 0.72, 0.04), bronze)
		b.rotation = Vector3(0, deg_to_rad(50.0), deg_to_rad(-15.0))
	return {prop = prop, rudders = rudders}


static func _box(parent: Node3D, at: Vector3, size: Vector3, m: Material, rot := Vector3.ZERO) -> MeshInstance3D:
	var mi := MeshInstance3D.new()
	var bm := BoxMesh.new()
	bm.size = size
	mi.mesh = bm
	mi.material_override = m
	mi.position = at
	mi.rotation = rot
	parent.add_child(mi)
	return mi


## Set `mi` (a cylinder, its axis along its Y) at `at`, its axis along `n`.
static func _face(mi: Node3D, at: Vector3, n: Vector3) -> void:
	var y := n.normalized()
	var x := Vector3.FORWARD.cross(y).normalized()
	mi.transform = Transform3D(Basis(x, y, x.cross(y)), at)
