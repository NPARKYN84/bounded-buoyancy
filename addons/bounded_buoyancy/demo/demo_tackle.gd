extends RefCounted
## Fish and fishing tackle for the demo, built in code: perch, trout and bass (and mackerel at sea), each with its own
## shape, fins and markings and its tail beating as it swims; a rod with a cork handle, a reel seat and
## whipped guides, and a fixed-spool reel whose handle turns as line is wound in; a stick float with a
## bright tip; a hook baited with a worm; an angler on his seat box, in a waxed jacket, a vest with
## pockets, wellingtons and a bucket hat, whose hands follow the rod and the reel's handle; and an open
## tackle box of lures. For show: the physics is the plugin's own (BoundedBuoyancyFish, -Line, -Body).
##
## The colours of the rod and reel are passed in as a dictionary (ROD_CLASSIC is the default), so that
## other tackle can be drawn the same way.

enum { PERCH, TROUT, BASS, MACKEREL }
const NAMES := ["perch", "trout", "bass", "mackerel"]

## Each kind's shape: its depth (half its height at the deepest, as a share of its length), how far
## back it is deepest (0 the nose, 1 the tail), how much higher its back rises than its belly falls,
## its width as a share of its depth, and its tail's fork (negative: rounded).
const SHAPES := [
	{depth = 0.155, peak = 0.36, hump = 1.35, wide = 0.5, fork = 0.35},
	{depth = 0.115, peak = 0.42, hump = 1.08, wide = 0.58, fork = 0.12},
	{depth = 0.14, peak = 0.38, hump = 1.12, wide = 0.55, fork = -0.08},
	{depth = 0.105, peak = 0.42, hump = 1.0, wide = 0.6, fork = 0.6},
]

const ROD_CLASSIC := {
	blank = Color(0.06, 0.16, 0.12),      # the rod: a deep green, glossy
	whip = Color(0.85, 0.62, 0.18),       # the thread whipping the guides on
	seat = Color(0.20, 0.21, 0.23),       # the reel seat
	reel = Color(0.16, 0.17, 0.19),       # the reel's body
	spool = Color(0.82, 0.66, 0.35),      # its spool
	line = Color(1.0, 0.86, 0.18),        # the line on the spool
	knob = Color(0.05, 0.05, 0.05),       # the handle's knob
}

const FISH_SHADER := """
shader_type spatial;
render_mode cull_disabled;
uniform float long = 0.35;
uniform float beat = 0.0;
uniform int kind = 0;
varying vec2 at;        // body: along (0 nose, 1 tail) and up (-1 belly, 1 back); fin: along it and out
varying vec3 fin;       // x: 1 on a fin; y: which fin; z: 1 on a spiny one
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
// Distance to the nearest of the spots scattered over a grid (a share `density` of its cells
// have one), and that spot's own random number.
vec2 spots(vec2 q, float density) {
	vec2 c = floor(q);
	vec2 best = vec2(9.0, 0.0);
	for (int i = -1; i <= 1; i++) {
		for (int j = -1; j <= 1; j++) {
			vec2 cc = c + vec2(float(i), float(j));
			float h = hash(cc);
			if (h < density) {
				vec2 centre = cc + 0.5 + 0.5 * (vec2(hash(cc + 7.1), hash(cc + 3.3)) - 0.5);
				float d = length(q - centre);
				if (d < best.x) { best = vec2(d, hash(cc + 1.7)); }
			}
		}
	}
	return best;
}
void vertex() {
	at = UV;
	fin = COLOR.rgb;
	float t = UV2.x;
	VERTEX.x += sin(TIME * (6.0 + 10.0 * beat) - t * 4.0) * (0.03 + 0.07 * beat) * long * t * t;
}
void fragment() {
	vec3 c;
	float t = at.x;
	float up = at.y;
	if (fin.x > 0.5) {
		// Fins: coloured by kind, with rays, darker toward the edge.
		vec3 top, tail, low, side;
		if (kind == 0) { top = vec3(0.22, 0.26, 0.16); tail = vec3(0.70, 0.40, 0.12); low = vec3(1.0, 0.30, 0.04); side = vec3(0.95, 0.55, 0.15); }
		else if (kind == 1) { top = vec3(0.36, 0.28, 0.14); tail = vec3(0.34, 0.26, 0.13); low = vec3(0.90, 0.58, 0.25); side = vec3(0.80, 0.52, 0.24); }
		else if (kind == 2) { top = vec3(0.16, 0.24, 0.11); tail = vec3(0.18, 0.26, 0.12); low = vec3(0.45, 0.52, 0.30); side = vec3(0.50, 0.55, 0.32); }
		else { top = vec3(0.10, 0.20, 0.24); tail = vec3(0.12, 0.22, 0.26); low = vec3(0.75, 0.78, 0.80); side = vec3(0.45, 0.52, 0.56); }
		c = fin.y < 0.1 ? top : (fin.y < 0.35 ? tail : (fin.y < 0.6 ? low : side));
		c *= 0.82 + 0.18 * sin(t * 70.0);
		c *= mix(1.0, 0.72, up);
		// The perch's black spot at the back of its spiny fin; the trout's spotted back fin.
		if (kind == 0 && fin.z > 0.5) { c = mix(c, vec3(0.03), smoothstep(0.7, 0.85, t) * smoothstep(0.15, 0.45, up)); }
		if (kind == 1 && fin.y < 0.1) { c = mix(c, vec3(0.05), smoothstep(0.33, 0.22, spots(vec2(t * 7.0, up * 3.0), 0.6).x)); }
		// The leading edge of the lower fins white on a trout, as in life.
		if (kind == 1 && fin.y > 0.35 && fin.y < 0.6) { c = mix(c, vec3(0.95), smoothstep(0.12, 0.0, t)); }
	} else {
		vec3 back, flank, belly;
		if (kind == 0) { back = vec3(0.10, 0.18, 0.05); flank = vec3(0.60, 0.62, 0.12); belly = vec3(0.96, 0.92, 0.78); }
		else if (kind == 1) { back = vec3(0.20, 0.17, 0.07); flank = vec3(0.72, 0.50, 0.18); belly = vec3(0.98, 0.86, 0.50); }
		else if (kind == 2) { back = vec3(0.07, 0.16, 0.06); flank = vec3(0.36, 0.50, 0.20); belly = vec3(0.96, 0.96, 0.90); }
		else { back = vec3(0.05, 0.30, 0.34); flank = vec3(0.70, 0.76, 0.80); belly = vec3(0.97, 0.97, 0.98); }
		c = mix(flank, back, smoothstep(-0.05, 0.65, up));
		c = mix(c, belly, smoothstep(-0.2, -0.6, up));
		if (kind == 0) {
			// Six or seven bold dark bars across the back, fading toward the belly.
			float b = 0.5 + 0.5 * cos(6.2832 * (t * 6.4 - 0.25));
			float bar = smoothstep(0.5, 0.8, b) * smoothstep(0.2, 0.26, t) * smoothstep(0.9, 0.84, t) * smoothstep(-0.55, -0.1, up);
			c = mix(c, vec3(0.06, 0.09, 0.04), bar * 0.88);
		} else if (kind == 1) {
			// Black spots over the back and upper flank; red ones with pale rings along the middle.
			vec2 k = spots(vec2(t * 13.0, up * 2.6), 0.6);
			c = mix(c, vec3(0.04, 0.03, 0.02), smoothstep(0.3, 0.22, k.x) * smoothstep(-0.3, 0.05, up) * smoothstep(0.1, 0.18, t));
			vec2 r = spots(vec2(t * 9.0 + 0.5, up * 2.2 + 0.3), 0.5);
			float mid = smoothstep(0.5, 0.3, abs(up + 0.05)) * smoothstep(0.2, 0.28, t) * smoothstep(0.9, 0.8, t);
			c = mix(c, vec3(0.98, 0.92, 0.80), (smoothstep(0.42, 0.36, r.x) - smoothstep(0.28, 0.24, r.x)) * mid);
			c = mix(c, vec3(0.85, 0.08, 0.04), smoothstep(0.28, 0.22, r.x) * mid);
		} else if (kind == 3) {
			// Wavy black stripes across the blue-green back.
			float w = sin(t * 70.0 + 3.0 * sin(up * 9.0 + t * 20.0));
			c = mix(c, vec3(0.02, 0.05, 0.06), smoothstep(0.45, 0.8, w) * smoothstep(0.1, 0.35, up) * smoothstep(0.12, 0.2, t) * smoothstep(0.9, 0.82, t));
		} else {
			// A dark ragged band of blotches from the eye to the tail, and mottling above it.
			float y0 = 0.06 + 0.06 * sin(t * 47.0) + 0.03 * sin(t * 131.0);
			float band = smoothstep(0.24, 0.12, abs(up - y0)) * smoothstep(0.1, 0.16, t) * (0.75 + 0.25 * sin(t * 83.0));
			c = mix(c, vec3(0.06, 0.10, 0.05), band * 0.9);
			vec2 k = spots(vec2(t * 14.0, up * 3.0), 0.5);
			c = mix(c, vec3(0.10, 0.16, 0.07), smoothstep(0.35, 0.2, k.x) * smoothstep(0.2, 0.5, up) * 0.6);
		}
		// The lateral line, the edge of the gill cover, and the mouth.
		c = mix(c, c * 1.35, 0.5 * smoothstep(0.03, 0.0, abs(up - 0.22 + 0.1 * t)) * step(0.22, t));
		float gill = smoothstep(0.014, 0.0, abs(t - (0.21 + 0.035 * up * up))) * step(-0.75, up) * step(up, 0.65);
		c *= 1.0 - 0.45 * gill;
		float gape = kind == 2 ? 0.13 : 0.07;
		c *= 1.0 - 0.7 * smoothstep(0.04, 0.0, abs(up + 0.12 - 0.6 * t)) * step(t, gape);
		// Scales.
		c *= 0.94 + 0.06 * sin(t * 260.0 + up * 22.0) * sin(t * 260.0 - up * 22.0);
	}
	ALBEDO = c;
	ROUGHNESS = 0.45;
	SPECULAR = 0.35;
}
"""


# --- The fish -------------------------------------------------------------------------------------

## A fish `long` metres long of kind PERCH, TROUT or BASS, nose toward -Z: its body, its fins and its
## eyes. Its material's "beat" (0 to 1) is how hard its tail beats.
static func fish_mesh(kind: int, long: float) -> MeshInstance3D:
	var s: Dictionary = SHAPES[kind]
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var rings := 24
	var around := 18
	var pts: Array = []
	for i in rings + 1:
		var t := float(i) / rings
		var row: Array = []
		for j in around:
			var a := TAU * j / around
			row.append(_surface(s, long, t, sin(a), cos(a)))
		pts.append(row)
	var nose := Vector3(0, -0.02 * long, -0.5 * long - 0.01 * long)
	var tail_end := Vector3(0, 0, 0.5 * long + 0.005)
	for i in rings:
		for j in around:
			var j1 := (j + 1) % around
			var ta := float(i) / rings
			var tb := float(i + 1) / rings
			var ua := sin(TAU * j / around)
			var ub := sin(TAU * j1 / around)
			var q := [[pts[i][j], ta, ua], [pts[i + 1][j], tb, ua], [pts[i][j1], ta, ub], [pts[i][j1], ta, ub], [pts[i + 1][j], tb, ua], [pts[i + 1][j1], tb, ub]]
			for v in q:
				_body_vertex(st, v[0], v[1], v[2])
	for j in around:
		var j1 := (j + 1) % around
		var ua := sin(TAU * j / around)
		var ub := sin(TAU * j1 / around)
		_body_vertex(st, nose, 0.0, 0.0)
		_body_vertex(st, pts[0][j], 0.0, ua)
		_body_vertex(st, pts[0][j1], 0.0, ub)
		_body_vertex(st, pts[rings][j], 1.0, ua)
		_body_vertex(st, tail_end, 1.0, 0.0)
		_body_vertex(st, pts[rings][j1], 1.0, ub)
	_fins(st, kind, s, long)
	st.generate_normals()
	var m := MeshInstance3D.new()
	m.mesh = st.commit()
	var sh := Shader.new()
	sh.code = FISH_SHADER
	var mat := ShaderMaterial.new()
	mat.shader = sh
	mat.set_shader_parameter("long", long)
	mat.set_shader_parameter("kind", kind)
	m.material_override = mat
	_eyes(m, kind, s, long)
	return m


## The body's half-height above (up > 0) or below, and its half-width, `t` of the way back.
static func _radius(s: Dictionary, long: float, t: float, up: float) -> Vector2:
	var peak: float = s.peak
	var u := 0.5 * t / peak if t < peak else 0.5 + 0.5 * (t - peak) / (1.0 - peak)
	var g := pow(sin(PI * clampf(u * 0.9 + 0.05, 0.0, 1.0)), 0.62)
	var d: float = s.depth * long * g
	var h: float = d * (s.hump if up > 0.0 else 0.88)
	return Vector2(h, d * s.wide)


static func _surface(s: Dictionary, long: float, t: float, up: float, side: float) -> Vector3:
	var r := _radius(s, long, t, up)
	return Vector3(side * r.y, up * r.x, (t - 0.5) * long)


static func _body_vertex(st: SurfaceTool, p: Vector3, t: float, up: float) -> void:
	st.set_color(Color(0, 0, 0, 1))
	st.set_uv(Vector2(t, up))
	st.set_uv2(Vector2(t, 0))
	st.add_vertex(p)


## A fin as a strip from `base` (points on the body) to `tip`, `t` of the way back along the body at
## each; `which` picks its colour (0 a back fin, 0.25 the tail, 0.5 a lower fin, 0.75 a side fin).
static func _fin(st: SurfaceTool, base: Array, tip: Array, t: Array, which: float, spiny := false) -> void:
	var n := base.size()
	for k in n - 1:
		var a := float(k) / (n - 1)
		var b := float(k + 1) / (n - 1)
		var quad := [[base[k], a, 0.0, t[k]], [tip[k], a, 1.0, t[k]], [base[k + 1], b, 0.0, t[k + 1]],
			[base[k + 1], b, 0.0, t[k + 1]], [tip[k], a, 1.0, t[k]], [tip[k + 1], b, 1.0, t[k + 1]]]
		for v in quad:
			st.set_color(Color(1.0, which, 1.0 if spiny else 0.0, 1.0))
			st.set_uv(Vector2(v[1], v[2]))
			st.set_uv2(Vector2(v[3], 1.0))
			st.add_vertex(v[0])


## A fin along the back (or under the belly, `below`) from t0 to t1, `high` at its tallest (as a share
## of the length), raked back; a spiny one has a ragged edge.
static func _ridge_fin(st: SurfaceTool, s: Dictionary, long: float, t0: float, t1: float, high: float, below: bool, which: float, spiny := false) -> void:
	var base := []
	var tip := []
	var ts := []
	var n := 12
	for k in n:
		var a := float(k) / (n - 1)
		var t := lerpf(t0, t1, a)
		var y := _radius(s, long, t, -1.0 if below else 1.0).x
		var p := Vector3(0, -y if below else y, (t - 0.5) * long)
		var h := high * long * pow(sin(PI * clampf(a * 0.85 + 0.12, 0.0, 1.0)), 0.7)
		if spiny:
			h *= 1.0 if k % 2 == 0 else 0.78
			h *= lerpf(1.1, 0.6, a)
		var out := Vector3(0, -h if below else h, 0.35 * h)
		base.append(p - Vector3(0, 0.003 * (-1.0 if below else 1.0), 0))
		tip.append(p + out)
		ts.append(t)
	_fin(st, base, tip, ts, which, spiny)


static func _fins(st: SurfaceTool, kind: int, s: Dictionary, long: float) -> void:
	# The tail: a fan from the narrow end of the body, forked or rounded.
	var tb := 0.955
	var hb := _radius(s, long, tb, 1.0).x
	var zb := (tb - 0.5) * long
	var base := []
	var tip := []
	var ts := []
	var n := 13
	var span := 0.15 * long if kind == PERCH else (0.17 * long if kind == MACKEREL else 0.14 * long)
	for k in n:
		var u := 1.0 - 2.0 * k / (n - 1)
		var reach: float = 0.17 * long * (1.0 - s.fork * (1.0 - absf(u)))
		base.append(Vector3(0, u * hb, zb))
		tip.append(Vector3(0, u * span, zb + reach))
		ts.append(1.0)
	_fin(st, base, tip, ts, 0.25)
	match kind:
		PERCH:
			_ridge_fin(st, s, long, 0.24, 0.5, 0.13, false, 0.0, true)
			_ridge_fin(st, s, long, 0.53, 0.73, 0.09, false, 0.0)
		TROUT:
			_ridge_fin(st, s, long, 0.36, 0.53, 0.10, false, 0.0)
			_ridge_fin(st, s, long, 0.76, 0.82, 0.035, false, 0.0)
		MACKEREL:
			_ridge_fin(st, s, long, 0.3, 0.42, 0.09, false, 0.0, true)
			_ridge_fin(st, s, long, 0.55, 0.65, 0.06, false, 0.0)
		BASS:
			_ridge_fin(st, s, long, 0.28, 0.5, 0.095, false, 0.0, true)
			_ridge_fin(st, s, long, 0.5, 0.71, 0.11, false, 0.0)
	_ridge_fin(st, s, long, 0.62, 0.76, 0.085 if kind != MACKEREL else 0.05, true, 0.5, kind != TROUT and kind != MACKEREL)
	# The paired fins: pelvic fins under the belly, pectoral fins behind the gills, angled back.
	for side in [-1.0, 1.0]:
		for f in [[0.33, 0.41, -0.85, Vector3(0.05, -0.07, 0.07), 0.5], [0.22, 0.27, -0.35, Vector3(0.07, -0.025, 0.1), 0.75]]:
			var b2 := []
			var t2 := []
			var p2 := []
			for k in 3:
				var t: float = lerpf(f[0], f[1], k / 2.0)
				var up: float = f[2]
				var r := _radius(s, long, t, up)
				var p := Vector3(side * r.y * sqrt(1.0 - up * up) * 0.95, up * r.x, (t - 0.5) * long)
				b2.append(p)
				var o: Vector3 = f[3]
				p2.append(p + Vector3(side * o.x, o.y, o.z) * long * (1.0 - 0.3 * absf(k - 1.0)))
				t2.append(t)
			_fin(st, b2, p2, t2, f[4])


## Two eyes, a gold ring round a black pupil, a little way back from the nose and high on the head.
static func _eyes(parent: Node3D, kind: int, s: Dictionary, long: float) -> void:
	var t := 0.085 if kind != BASS else 0.095
	var r := _radius(s, long, t, 1.0)
	var re := 0.03 * long
	var iris := StandardMaterial3D.new()
	iris.albedo_color = Color(0.78, 0.66, 0.40) if kind == TROUT else (Color(0.8, 0.82, 0.85) if kind == MACKEREL else Color(0.85, 0.62, 0.15))
	iris.metallic = 0.5
	iris.roughness = 0.25
	var pupil := StandardMaterial3D.new()
	pupil.albedo_color = Color(0.01, 0.01, 0.01)
	pupil.roughness = 0.05
	pupil.metallic_specular = 1.0
	for side in [-1.0, 1.0]:
		var at := Vector3(side * (r.y * 0.92 - 0.25 * re), 0.3 * r.x, (t - 0.5) * long)
		for e in [[re, iris, Vector3.ZERO], [re * 0.6, pupil, Vector3(side * re * 0.55, 0, 0)]]:
			var mi := MeshInstance3D.new()
			var sm := SphereMesh.new()
			sm.radius = e[0]
			sm.height = 2.0 * e[0]
			sm.radial_segments = 12
			sm.rings = 6
			mi.mesh = sm
			mi.material_override = e[1]
			mi.position = at + e[2]
			parent.add_child(mi)


# --- Shapes ---------------------------------------------------------------------------------------

static func mat(c: Color, rough := 0.8, metal := 0.0) -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	m.albedo_color = c
	m.roughness = rough
	m.metallic = metal
	return m


## A cylinder of radius `r` from `a` to `b` (in `parent`'s space).
static func rod_between(parent: Node3D, a: Vector3, b: Vector3, r: float, m: Material, sides := 10) -> MeshInstance3D:
	var mi := MeshInstance3D.new()
	var cm := CylinderMesh.new()
	cm.top_radius = r
	cm.bottom_radius = r
	cm.height = maxf(a.distance_to(b), 0.0005)
	cm.radial_segments = sides
	cm.rings = 1
	mi.mesh = cm
	mi.material_override = m
	parent.add_child(mi)
	place_between(mi, a, b)
	return mi


## A capsule of radius `r` from `a` to `b`.
static func limb(parent: Node3D, a: Vector3, b: Vector3, r: float, m: Material) -> MeshInstance3D:
	var mi := MeshInstance3D.new()
	var cm := CapsuleMesh.new()
	cm.radius = r
	cm.height = a.distance_to(b) + 2.0 * r
	cm.radial_segments = 12
	cm.rings = 4
	mi.mesh = cm
	mi.material_override = m
	parent.add_child(mi)
	place_between(mi, a, b)
	return mi


## Turn and move `mi` (whose length is along its Y) to lie from `a` to `b`.
static func place_between(mi: Node3D, a: Vector3, b: Vector3) -> void:
	var d := b - a
	if d.length() < 1e-6:
		d = Vector3.UP
	d = d.normalized()
	var x := (Vector3.FORWARD if absf(d.z) < 0.9 else Vector3.RIGHT).cross(d).normalized()
	mi.transform = Transform3D(Basis(x, d, x.cross(d)), 0.5 * (a + b))


static func ball(parent: Node3D, at: Vector3, r: float, m: Material, squash := Vector3.ONE) -> MeshInstance3D:
	var mi := MeshInstance3D.new()
	var sm := SphereMesh.new()
	sm.radius = r
	sm.height = 2.0 * r
	sm.radial_segments = 14
	sm.rings = 7
	mi.mesh = sm
	mi.material_override = m
	mi.position = at
	mi.scale = squash
	parent.add_child(mi)
	return mi


static func box(parent: Node3D, at: Vector3, size: Vector3, m: Material, rot := Vector3.ZERO) -> MeshInstance3D:
	var mi := MeshInstance3D.new()
	var bm := BoxMesh.new()
	bm.size = size
	mi.mesh = bm
	mi.material_override = m
	mi.position = at
	mi.rotation = rot
	parent.add_child(mi)
	return mi


static func ring(parent: Node3D, at: Vector3, r: float, thick: float, m: Material) -> MeshInstance3D:
	var mi := MeshInstance3D.new()
	var tm := TorusMesh.new()
	tm.inner_radius = r - thick
	tm.outer_radius = r
	tm.rings = 16
	tm.ring_segments = 6
	mi.mesh = tm
	mi.material_override = m
	mi.position = at
	parent.add_child(mi)
	return mi


# --- The rod and reel ---------------------------------------------------------------------------

## Dress a rod made of `parts`, each a piece `piece` long with its length along its Y and the side
## away from the angler's line of sight (below the rod) toward its +Z: cork on the first two pieces, a
## rubber butt cap, the reel seat with the reel on it, and guides whipped on along the blank to the tip
## ring. Returns the reel; its "Handle" and "Rotor" turn as line is wound in.
static func dress_rod(parts: Array, piece: float, radius: Callable, colours := ROD_CLASSIC) -> Node3D:
	var n := parts.size()
	var cork := ShaderMaterial.new()
	var sh := Shader.new()
	sh.code = """
shader_type spatial;
varying vec3 p;
void vertex() { p = VERTEX; }
float hash(vec3 q) { return fract(sin(dot(q, vec3(12.9898, 78.233, 37.719))) * 43758.5453); }
void fragment() {
	vec3 c = vec3(0.70, 0.54, 0.36);
	float speck = hash(floor(p * 900.0));
	c *= 0.86 + 0.18 * speck;
	c *= 0.92 + 0.08 * step(0.5, fract(p.y * 55.0));
	ALBEDO = c;
	ROUGHNESS = 0.95;
}
"""
	cork.shader = sh
	var blank := mat(colours.blank, 0.22, 0.35)
	var whip := mat(colours.whip, 0.4, 0.5)
	var steel := mat(Color(0.75, 0.77, 0.80), 0.18, 0.95)
	var black := mat(Color(0.03, 0.03, 0.03), 0.6)
	for i in n:
		(parts[i] as MeshInstance3D).material_override = cork if i < 2 else blank
	var butt: Node3D = parts[0]
	rod_between(butt, Vector3(0, -0.5 * piece - 0.015, 0), Vector3(0, -0.5 * piece + 0.01, 0), radius.call(0.0) * 1.25, black, 14)
	# The reel seat, a little way up the handle, with the cork above and below it.
	var seat_at := 0.33 - 1.5 * piece
	var seat_piece: Node3D = parts[1]
	var r1: float = radius.call(0.33)
	rod_between(seat_piece, Vector3(0, seat_at - 0.06, 0), Vector3(0, seat_at + 0.06, 0), r1 * 1.18, mat(colours.seat, 0.25, 0.8), 14)
	for e in [-0.065, 0.065]:
		ring(seat_piece, Vector3(0, seat_at + e, 0), r1 * 1.32, r1 * 0.3, mat(colours.whip, 0.3, 0.8))
	# The guides: rings on short legs under the blank, smaller toward the tip, each whipped on.
	var guides := [[3, 0.019], [5, 0.014], [7, 0.011], [9, 0.009], [11, 0.007], [12, 0.006], [13, 0.005]]
	for g in guides:
		var i: int = g[0]
		if i >= n:
			continue
		var at := 0.5 * piece - 0.03 if i < n - 1 else 0.5 * piece - 0.004
		var rb: float = radius.call((i + 0.5) * piece)
		var gr: float = g[1]
		var part: Node3D = parts[i]
		var stand := rb + 0.004 + gr if i < n - 1 else rb + gr * 0.6
		rod_between(part, Vector3(0, at - 0.018, 0), Vector3(0, at + 0.012, 0), rb * 1.45, whip, 10)
		rod_between(part, Vector3(0, at, rb * 0.5), Vector3(0, at, stand - gr), 0.0012, steel, 6)
		ring(part, Vector3(0, at, stand), gr, 0.0018, steel)
	return reel(seat_piece, Vector3(0, seat_at, r1 * 1.18), colours)


## A fixed-spool reel hung below the rod at `at` (in the rod piece's space): its foot, the gearbox,
## the rotor with the bail arm, the spool full of line, and the handle on the left (+X) with its knob.
static func reel(parent: Node3D, at: Vector3, colours := ROD_CLASSIC) -> Node3D:
	var r := Node3D.new()
	r.name = "Reel"
	r.position = at
	r.scale = Vector3.ONE * 1.3   # a little larger than life, to be seen
	parent.add_child(r)
	var body := mat(colours.reel, 0.3, 0.75)
	var gold := mat(colours.spool, 0.25, 0.85)
	var line := mat(colours.line, 0.6)
	var steel := mat(Color(0.78, 0.80, 0.83), 0.15, 0.95)
	# The foot along the seat, and the stem down to the gearbox.
	box(r, Vector3(0, 0, 0.003), Vector3(0.012, 0.07, 0.005), body)
	box(r, Vector3(0, -0.005, 0.03), Vector3(0.01, 0.022, 0.05), body)
	# The gearbox, round, across the rod.
	var gb := rod_between(r, Vector3(-0.018, -0.012, 0.075), Vector3(0.02, -0.012, 0.075), 0.03, body, 20)
	gb.name = "Gearbox"
	# The rotor and the spool in front of it, along the rod.
	var rotor := Node3D.new()
	rotor.name = "Rotor"
	rotor.position = Vector3(0, 0.0, 0.075)
	r.add_child(rotor)
	rod_between(rotor, Vector3(0, 0.016, 0), Vector3(0, 0.05, 0), 0.027, body, 20)
	# The bail arm: a wire hoop over the spool's front, on its two arms.
	var bail := ring(rotor, Vector3(0, 0.07, 0), 0.031, 0.0022, steel)
	bail.rotation = Vector3(0, 0, deg_to_rad(18))
	rod_between(rotor, Vector3(0.027, 0.045, 0), Vector3(0.031, 0.07, 0.006), 0.0025, steel, 6)
	rod_between(rotor, Vector3(-0.027, 0.045, 0), Vector3(-0.031, 0.07, -0.006), 0.0025, steel, 6)
	rod_between(r, Vector3(0, 0.05, 0.075), Vector3(0, 0.088, 0.075), 0.025, gold, 20)
	rod_between(r, Vector3(0, 0.055, 0.075), Vector3(0, 0.083, 0.075), 0.0262, line, 20)
	rod_between(r, Vector3(0, 0.088, 0.075), Vector3(0, 0.094, 0.075), 0.012, gold, 14)
	# The handle: an arm turning about the gearbox's axle, a knob on the end of it.
	var handle := Node3D.new()
	handle.name = "Handle"
	handle.position = Vector3(0.022, -0.012, 0.075)
	r.add_child(handle)
	rod_between(handle, Vector3(0, 0, 0), Vector3(0.012, 0, 0), 0.006, body, 10)
	rod_between(handle, Vector3(0.012, 0, 0), Vector3(0.014, 0, 0.06), 0.004, body, 8)
	var knob := limb(handle, Vector3(0.016, 0, 0.06), Vector3(0.04, 0, 0.06), 0.008, mat(colours.knob, 0.5))
	knob.name = "Knob"
	return r


# --- Float and bait -----------------------------------------------------------------------------

## A stick float `long` metres long, drawn twice as wide as `wide` so that it can be seen: an olive
## body with a gold band, a white band under a black ring, and a bright orange-red tip that glows.
static func float_mesh(long: float, wide: float) -> MeshInstance3D:
	var m := MeshInstance3D.new()
	var cm := CylinderMesh.new()
	cm.top_radius = wide
	cm.bottom_radius = wide * 0.45
	cm.height = long
	cm.radial_segments = 14
	cm.rings = 6
	m.mesh = cm
	var sh := Shader.new()
	sh.code = """
shader_type spatial;
uniform float long = 0.25;
varying float y;
void vertex() { y = VERTEX.y / long + 0.5; }
void fragment() {
	vec3 c = vec3(0.14, 0.20, 0.10);
	if (y > 0.84) { c = vec3(1.0, 0.24, 0.02); EMISSION = c * 1.6; }
	else if (y > 0.81) { c = vec3(0.02); }
	else if (y > 0.70) { c = vec3(0.97, 0.97, 0.94); }
	else if (abs(y - 0.42) < 0.015) { c = vec3(0.85, 0.65, 0.2); }
	else if (y < 0.06) { c = vec3(0.5, 0.35, 0.2); }
	ALBEDO = c;
	ROUGHNESS = 0.35;
	SPECULAR = 0.7;
}
"""
	var mt := ShaderMaterial.new()
	mt.shader = sh
	mt.set_shader_parameter("long", long)
	m.material_override = mt
	return m


## The hook under the shot (centre at the origin, radius `shot`): a bronze hook with a red worm
## threaded on it, hanging a little below, drawn half as big again as it is so that it can be seen.
static func hook_and_worm(parent: Node3D, shot: float) -> void:
	var bronze := mat(Color(0.55, 0.38, 0.16), 0.3, 0.9)
	var worm := mat(Color(0.72, 0.22, 0.24), 0.45)
	var band := mat(Color(0.85, 0.42, 0.40), 0.45)
	var top := Vector3(0, -shot - 0.03, 0)
	var k := 1.5
	var w := 0.0012 * k
	# The shank down from the eye, the bend, and the point back up.
	var eye := ring(parent, top, 0.002 * k, 0.0008 * k, bronze)
	eye.rotation = Vector3(PI * 0.5, 0, 0)
	limb(parent, top, top + Vector3(0, -0.022, 0) * k, w, bronze)
	var c := top + Vector3(0.006, -0.022, 0) * k
	var prev := top + Vector3(0, -0.022, 0) * k
	for i in range(1, 8):
		var a := PI + PI * i / 7.0
		var p := c + Vector3(cos(a), sin(a), 0) * 0.006 * k
		limb(parent, prev, p, w, bronze)
		prev = p
	limb(parent, prev, prev + Vector3(-0.001, 0.008, 0) * k, w * 0.8, bronze)
	# The worm: looped on the bend, both ends hanging and curling.
	for end in [-1.0, 1.0]:
		var q: Vector3 = c + Vector3(0, -0.006 * k, 0)
		for i in 9:
			var a: float = end * (0.4 + 0.5 * i) + 0.3 * sin(i * 1.3)
			var nq: Vector3 = q + Vector3(sin(a) * 0.006, -0.0065, cos(a) * 0.004) * k
			limb(parent, q, nq, 0.0028 * k * (1.0 - 0.04 * i), band if i == 3 else worm)
			q = nq


# --- The angler -----------------------------------------------------------------------------------

## An angler sitting on a seat box whose top is at `seat`, facing -Z: wellingtons, dark trousers, an
## olive waxed jacket under a sand-coloured vest with pockets, a grey beard and a bucket hat. Returns
## what pose_arms needs to move his arms.
static func angler(parent: Node3D, seat: Vector3) -> Dictionary:
	var up := Vector3.UP
	var f := Vector3.FORWARD
	var s := Vector3.RIGHT
	var jacket := mat(Color(0.22, 0.27, 0.16), 0.55, 0.05)
	var vest := mat(Color(0.62, 0.55, 0.38), 0.9)
	var pocket := mat(Color(0.52, 0.46, 0.31), 0.9)
	var trousers := mat(Color(0.14, 0.15, 0.17), 0.9)
	var welly := mat(Color(0.10, 0.22, 0.12), 0.35)
	var skin := mat(Color(0.86, 0.64, 0.50), 0.6)
	var beard := mat(Color(0.78, 0.77, 0.74), 0.95)
	var hat := mat(Color(0.58, 0.53, 0.38), 0.95)
	var boxm := mat(Color(0.55, 0.58, 0.55), 0.5)
	var trim := mat(Color(0.14, 0.15, 0.16), 0.5)
	# The seat box: drawers in front, a footplate.
	var b := seat - up * 0.19
	box(parent, b, Vector3(0.42, 0.38, 0.34), boxm)
	for k in 3:
		box(parent, b + Vector3(0, 0.11 - 0.11 * k, -0.181), Vector3(0.42, 0.006, 0.004), trim)
		box(parent, b + Vector3(0, 0.08 - 0.11 * k, -0.183), Vector3(0.08, 0.02, 0.01), trim)
	box(parent, seat + up * 0.008, Vector3(0.44, 0.016, 0.34), mat(Color(0.18, 0.20, 0.22), 0.8))
	var hips := seat + up * 0.1 + f * 0.02
	for side in [-1.0, 1.0]:
		var hip: Vector3 = hips + s * 0.1 * side
		var knee: Vector3 = hips + f * 0.42 + s * 0.13 * side + up * 0.04
		var ankle: Vector3 = knee - up * 0.42 + f * 0.04
		limb(parent, hip, knee, 0.08, trousers)
		limb(parent, knee, ankle, 0.066, trousers)
		limb(parent, ankle - up * 0.03, knee - up * 0.08, 0.072, welly)
		limb(parent, ankle - up * 0.04, ankle - up * 0.05 + f * 0.17, 0.055, welly)
	var chest := hips + up * 0.5 + f * 0.1
	limb(parent, hips + up * 0.05, chest, 0.17, jacket)
	limb(parent, hips + up * 0.2 + f * 0.04, chest - up * 0.03, 0.178, vest)
	# Pockets on the vest's front.
	for side in [-1.0, 1.0]:
		for k in 2:
			var at: Vector3 = hips + up * (0.3 + 0.13 * k) + f * (0.22 + 0.025 * k) + s * 0.08 * side
			box(parent, at, Vector3(0.09, 0.08, 0.025), pocket, Vector3(-0.2, 0, 0))
	ring(parent, chest + up * 0.08 + f * 0.01, 0.075, 0.03, jacket)
	var head := chest + up * 0.24 + f * 0.04
	ball(parent, head, 0.105, skin)
	ball(parent, head + f * 0.05 - up * 0.045, 0.085, beard, Vector3(1.0, 0.9, 0.85))
	ball(parent, head + f * 0.1 + up * 0.0, 0.02, skin)
	var brim := MeshInstance3D.new()
	var bm := CylinderMesh.new()
	bm.top_radius = 0.125
	bm.bottom_radius = 0.17
	bm.height = 0.04
	brim.mesh = bm
	brim.material_override = hat
	brim.position = head + up * 0.07
	brim.rotation = Vector3(0.12, 0, 0)
	parent.add_child(brim)
	rod_between(parent, head + up * 0.06, head + up * 0.17, 0.112, hat, 18)
	rod_between(parent, head + up * 0.09, head + up * 0.11, 0.116, mat(Color(0.25, 0.22, 0.16), 0.9), 18)
	var arms := []
	for side in [-1.0, 1.0]:
		var shoulder: Vector3 = chest + s * 0.19 * side - up * 0.04
		ball(parent, shoulder, 0.06, jacket)
		var upper := limb(parent, shoulder, shoulder - up * 0.3, 0.052, jacket)
		var fore := limb(parent, shoulder, shoulder - up * 0.28, 0.046, jacket)
		var cuff := limb(parent, shoulder, shoulder - up * 0.02, 0.049, mat(Color(0.12, 0.14, 0.09), 0.6))
		var hand := ball(parent, shoulder, 0.04, skin, Vector3(1.0, 1.15, 1.0))
		arms.append({shoulder = shoulder, side = side, upper = upper, fore = fore, cuff = cuff, hand = hand})
	return {arms = arms}


## Put the angler's hands at `left` and `right` (world space), bending his elbows out and down.
static func pose_arms(a: Dictionary, left: Vector3, right: Vector3) -> void:
	var l1 := 0.3
	var l2 := 0.28
	for arm in a.arms:
		var sh: Vector3 = arm.shoulder
		var hand: Vector3 = left if arm.side < 0.0 else right
		var to := hand - sh
		var d := clampf(to.length(), 0.05, l1 + l2 - 0.001)
		var dir := to.normalized()
		var x := (l1 * l1 - l2 * l2 + d * d) / (2.0 * d)
		var h := sqrt(maxf(l1 * l1 - x * x, 0.0))
		var pole: Vector3 = Vector3(arm.side * 0.6, -1.0, 0.2)
		pole = (pole - dir * pole.dot(dir)).normalized()
		var elbow := sh + dir * x + pole * h
		var wrist := elbow + (hand - elbow).normalized() * l2
		place_between(arm.upper, sh, elbow)
		place_between(arm.fore, elbow, wrist)
		place_between(arm.cuff, wrist - (wrist - elbow).normalized() * 0.04, wrist - (wrist - elbow).normalized() * 0.01)
		arm.hand.position = wrist + (wrist - elbow).normalized() * 0.02


# --- The tackle box -------------------------------------------------------------------------------

## An open tackle box at `at`, turned `yaw` about the vertical: a tray of lures (spoons, spinners,
## plugs), spare floats and hooks, the lid open behind it.
static func tackle_box(parent: Node3D, at: Vector3, yaw: float) -> Node3D:
	var root := Node3D.new()
	root.position = at
	root.rotation.y = yaw
	parent.add_child(root)
	var shell := mat(Color(0.16, 0.42, 0.30), 0.45)
	var tray := mat(Color(0.82, 0.84, 0.80), 0.5)
	var silver := mat(Color(0.85, 0.86, 0.88), 0.12, 1.0)
	var gold := mat(Color(0.92, 0.70, 0.25), 0.15, 1.0)
	var red := mat(Color(0.85, 0.08, 0.06), 0.4)
	var yellow := mat(Color(1.0, 0.85, 0.05), 0.4)
	var lime := mat(Color(0.65, 0.95, 0.10), 0.35)
	var orange := mat(Color(1.0, 0.42, 0.05), 0.35)
	var black := mat(Color(0.02, 0.02, 0.02), 0.3)
	var clear := mat(Color(0.85, 0.9, 0.95), 0.1)
	var W := 0.44
	var D := 0.26
	var H := 0.12
	box(root, Vector3(0, H * 0.5, 0), Vector3(W, H, D), shell)
	box(root, Vector3(0, H - 0.004, 0), Vector3(W - 0.02, 0.008, D - 0.02), tray)
	for k in 4:
		box(root, Vector3(-W * 0.5 + 0.01 + (W - 0.02) * (k + 1) / 5.0, H + 0.008, 0), Vector3(0.004, 0.02, D - 0.02), tray)
	box(root, Vector3(0, H + 0.008, 0), Vector3(W - 0.02, 0.02, 0.004), tray)
	# The lid, open past upright.
	var lid := Node3D.new()
	lid.position = Vector3(0, H, -D * 0.5)
	lid.rotation.x = deg_to_rad(-105)
	root.add_child(lid)
	box(lid, Vector3(0, 0.012, D * 0.5), Vector3(W, 0.024, D), shell)
	box(lid, Vector3(0, 0.026, D * 0.5), Vector3(0.1, 0.006, 0.03), mat(Color(0.9, 0.9, 0.85), 0.4))
	var y := H + 0.008
	var cell := (W - 0.02) / 5.0
	var cx := func(k: int) -> float: return -W * 0.5 + 0.01 + cell * (k + 0.5)
	# Spoons: silver and gold, one with a red stripe.
	for j in 2:
		var z := -D * 0.25 + D * 0.5 * j
		ball(root, Vector3(cx.call(0), y + 0.006, z), 0.02, silver if j == 0 else gold, Vector3(0.55, 0.2, 1.3))
		if j == 0:
			box(root, Vector3(cx.call(0), y + 0.011, z), Vector3(0.008, 0.002, 0.04), red)
	# Spinners: a blade, beads on the wire.
	for j in 2:
		var z := -D * 0.25 + D * 0.5 * j
		var o := Vector3(cx.call(1), y + 0.008, z)
		rod_between(root, o + Vector3(0, 0, -0.035), o + Vector3(0, 0, 0.035), 0.0012, silver, 6)
		var blade := ball(root, o + Vector3(0.008, 0.002, -0.015), 0.016, gold if j == 0 else silver, Vector3(0.6, 0.15, 1.0))
		blade.rotation.y = 0.3
		for b in 3:
			ball(root, o + Vector3(0, 0, 0.004 + 0.008 * b), 0.004, red if b != 1 else yellow)
	# Plugs: fat bodies in bright colours, a dark eye and a clear lip.
	var plugs := [[lime, orange], [silver, black], [yellow, red]]
	for j in 3:
		var z := -D * 0.22 + D * 0.44 * j
		var o := Vector3(cx.call(2), y + 0.012, z) if j < 2 else Vector3(cx.call(3), y + 0.012, -0.02)
		ball(root, o, 0.03, plugs[j][0], Vector3(0.4, 0.42, 1.0))
		ball(root, o + Vector3(0, -0.004, 0.004), 0.024, plugs[j][1], Vector3(0.3, 0.3, 0.8))
		for side in [-1.0, 1.0]:
			ball(root, o + Vector3(side * 0.011, 0.004, -0.019), 0.004, black)
		box(root, o + Vector3(0, -0.006, -0.034), Vector3(0.012, 0.002, 0.014), clear, Vector3(0.6, 0, 0))
	# Spare floats, and a few hooks.
	for j in 3:
		var x: float = cx.call(4) - 0.025 + 0.025 * j
		var a := Vector3(x, y + 0.006, -D * 0.4)
		var b := Vector3(x, y + 0.006, D * 0.3)
		rod_between(root, a, b, 0.005, mat(Color(0.14, 0.2, 0.1), 0.4), 8)
		rod_between(root, a, a + Vector3(0, 0, 0.03), 0.0052, orange, 8)
	for j in 4:
		var o := Vector3(cx.call(3) - 0.02 + 0.013 * j, y + 0.004, D * 0.3)
		rod_between(root, o, o + Vector3(0, 0, -0.025), 0.0009, gold, 5)
	return root
