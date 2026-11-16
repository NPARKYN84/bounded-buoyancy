extends MeshInstance3D
## An example of another water system: an ocean drawn by its own shader, as an ocean add-on or a
## shader of your own would be. Bounded Buoyancy knows nothing about it except get_height(), which
## works out the same waves on the CPU from the same numbers the shader uses.
##
## To float bodies on your own ocean: give it a function like get_height() below that returns the
## height of its surface at a world point, add a BoundedBuoyancyWater over it, and set the water's
## surface_source to your ocean's node and surface_method to the function's name. If you can, give
## it get_vertical_acceleration() too and set surface_acceleration_method to its name, so the water's
## motion in the waves pushes the bodies up and down as well; and get_motion(), with
## surface_motion_method set to its name, so they sway to and fro and drift with the waves.

## The waves, each as (height in m, length crest to crest in m, direction in degrees, phase).
@export var waves: Array[Vector4] = [
	Vector4(0.045, 3.1, 10.0, 0.0),
	Vector4(0.030, 1.9, -35.0, 1.7),
	Vector4(0.018, 1.1, 60.0, 4.1),
	Vector4(0.010, 0.7, 150.0, 2.3),
]
## All the wave heights times this: 1 for a moderate sea, 2 for a rough one.
@export_range(0.0, 3.0, 0.01) var roughness: float = 1.0:
	set(value):
		roughness = value
		_send_waves()
## Size of the drawn sea, in metres.
@export var extent := Vector2(24.0, 24.0)

## Wave clock in seconds, advanced with the physics step.
var time := 0.0

var _k: Array[Vector2] = []     # wave vector of each wave (direction x 2 pi / length)
var _w: Array[float] = []       # frequency of each wave
var _a: Array[float] = []       # height of each wave
var _p: Array[float] = []       # phase of each wave
var _material: ShaderMaterial

const SHADER := """
shader_type spatial;
render_mode blend_mix, cull_disabled, depth_draw_opaque, specular_schlick_ggx;
uniform vec4 deep : source_color = vec4(0.05, 0.27, 0.42, 0.92);
uniform vec4 crest : source_color = vec4(0.62, 0.82, 0.88, 0.92);
uniform vec4 k_and_w[4];   // wave vector (x, z), frequency, phase
uniform float amp[4];
uniform float t;
uniform float top;
varying float lift;
float h(vec2 p) {
	float s = 0.0;
	for (int i = 0; i < 4; i++) {
		s += amp[i] * sin(dot(k_and_w[i].xy, p) - k_and_w[i].z * t + k_and_w[i].w);
	}
	return s;
}
void vertex() {
	vec2 p = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz;
	lift = h(p);
	VERTEX.y += lift;
	float e = 0.03;
	vec3 dx = vec3(2.0 * e, h(p + vec2(e, 0.0)) - h(p - vec2(e, 0.0)), 0.0);
	vec3 dz = vec3(0.0, h(p + vec2(0.0, e)) - h(p - vec2(0.0, e)), 2.0 * e);
	NORMAL = normalize(cross(dz, dx));
}
void fragment() {
	float c = smoothstep(0.35, 1.0, lift / max(top, 1e-4));
	vec4 col = mix(deep, crest, c);
	ALBEDO = col.rgb;
	ALPHA = col.a;
	ROUGHNESS = 0.08;
	SPECULAR = 0.6;
}
"""


func _ready() -> void:
	# Move the waves before the bodies measure them in each physics step.
	process_physics_priority = -1000
	var plane := PlaneMesh.new()
	plane.size = extent
	plane.subdivide_width = int(extent.x * 8.0)
	plane.subdivide_depth = int(extent.y * 8.0)
	mesh = plane
	cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	_material = ShaderMaterial.new()
	var sh := Shader.new()
	sh.code = SHADER
	_material.shader = sh
	material_override = _material
	_send_waves()


func _physics_process(dt: float) -> void:
	time += dt


func _process(_dt: float) -> void:
	if _material:
		_material.set_shader_parameter("t", time)


## Height (world Y) of this ocean's surface at the world point `p`: the same sum of waves as the
## shader draws. This is the function Bounded Buoyancy asks.
func get_height(p: Vector3) -> float:
	var s := 0.0
	var xz := Vector2(p.x, p.z)
	for i in _k.size():
		s += _a[i] * sin(_k[i].dot(xz) - _w[i] * time + _p[i])
	return global_position.y + s


## Upward acceleration (m/s²) of the water at the world point `p`. The water goes round in circles as
## the waves pass, so what is at p now is the water whose circle's centre is p less how far the waves
## have carried it (mean_place); for each wave, its acceleration is -height x frequency² x the sine of
## the phase there, dying away as exp(k z) with z that centre's depth. Bounded Buoyancy asks this too
## (surface_acceleration_method), so bodies are pushed up and down by the water's motion.
func get_vertical_acceleration(p: Vector3) -> float:
	var m := mean_place(p)
	var s := 0.0
	for i in _k.size():
		s -= _w[i] * _w[i] * _a[i] * sin(m[1][i]) * exp(_k[i].length() * float(m[0].y))
	return s


## The water's motion at the world point `p`: [velocity, acceleration], the acceleration level (its
## upward part is get_vertical_acceleration's). For each wave the water goes forwards under a crest and
## back under a trough, height x frequency x the sine of the phase, and up and down, -height x frequency
## x its cosine, dying away as exp(k z); and it creeps the way the wave runs at Stokes's drift, height² x
## frequency x wave number x exp(2 k z). Bounded Buoyancy asks this too (surface_motion_method), so
## bodies sway to and fro with the waves and drift with them. (`across` and `along`, the width of the
## body asking, are not used here.)
func get_motion(p: Vector3, _across := 0.0, _along := 0.0) -> Array:
	var m := mean_place(p)
	var z: float = m[0].y
	var vel := Vector3.ZERO
	var acc := Vector3.ZERO
	for i in _k.size():
		var n := _k[i].length()
		var d := _k[i] / n
		var e := _a[i] * exp(n * z)
		var ph: float = m[1][i]
		var along := e * _w[i] * sin(ph) + e * e * _w[i] * n
		vel += Vector3(d.x * along, -e * _w[i] * cos(ph), d.y * along)
		var push := -e * _w[i] * _w[i] * cos(ph)
		acc += Vector3(d.x * push, 0.0, d.y * push)
	return [vel, acc]


## Where the water now at the world point `p` sits on average: [the centre of its circle as Vector3(x,
## depth below the mean level (no more than 0), z), each wave's phase there]. Found by putting the
## answer back in a few times.
func mean_place(p: Vector3) -> Array:
	var y := p.y - global_position.y
	var c := Vector2(p.x, p.z)
	var z := minf(y, 0.0)
	var phases := PackedFloat64Array()
	phases.resize(_k.size())
	for n in 10:
		var shift := Vector2.ZERO
		var lift := 0.0
		for i in _k.size():
			var kn := _k[i].length()
			phases[i] = _k[i].dot(c) - _w[i] * time + _p[i]
			var e := _a[i] * exp(kn * z)
			shift += _k[i] / kn * e * cos(phases[i])
			lift += e * sin(phases[i])
		c = Vector2(p.x, p.z) - shift
		z = minf(y - lift, 0.0)
	for i in _k.size():
		phases[i] = _k[i].dot(c) - _w[i] * time + _p[i]
	return [Vector3(c.x, z, c.y), phases]


## The most the surface rises above (or falls below) its average height.
func max_wave() -> float:
	var s := 0.0
	for a in _a:
		s += a
	return s


func _send_waves() -> void:
	var g := float(ProjectSettings.get_setting("physics/3d/default_gravity"))
	_k.clear()
	_w.clear()
	_a.clear()
	_p.clear()
	var kw := PackedVector4Array()
	var amps := PackedFloat32Array()
	for v in waves.slice(0, 4):
		var n := TAU / maxf(v.y, 0.01)
		var dir := Vector2(cos(deg_to_rad(v.z)), sin(deg_to_rad(v.z)))
		_k.append(dir * n)
		_w.append(sqrt(g * n))   # deep-water waves: frequency = sqrt(g x wave number)
		_a.append(v.x * roughness)
		_p.append(v.w)
		kw.append(Vector4(dir.x * n, dir.y * n, sqrt(g * n), v.w))
		amps.append(v.x * roughness)
	while kw.size() < 4:
		kw.append(Vector4.ZERO)
		amps.append(0.0)
	if _material:
		_material.set_shader_parameter("k_and_w", kw)
		_material.set_shader_parameter("amp", amps)
		_material.set_shader_parameter("top", max_wave())
