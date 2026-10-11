@tool
@icon("res://addons/bounded_buoyancy/icon_water.svg")
class_name BoundedBuoyancyWater
extends Node3D
## A body of water for BoundedBuoyancyBody, with optional waves.
##
## Place the node where the water's surface should be; the water fills a box of `size` below it.
## Once a scene has any BoundedBuoyancyWater, bodies float only inside one, so ponds, pools and
## tanks can sit at different heights. Scenes without one keep using each body's water_level.
##
## Waves move the surface only: bodies rise, fall and rock with them, and nothing is carried along
## by them. The waves are two travelling sine waves with the speeds of real deep-water waves for
## their length. A current makes the water flow: bodies in it drift with it, as in a river.
##
## Splashes: when a body hits the water fast enough the node throws up a splash, and every entry
## leaves rings spreading out over the surface. They are for show only and do not move bodies.

## Width (X), depth (Y) and length (Z) of the water, in metres. The top face is the surface.
@export var size: Vector3 = Vector3(10.0, 3.0, 10.0):
	set(value):
		size = value.max(Vector3(0.01, 0.01, 0.01))
		_update_surface()
## Density of the water: 1000 kg/m³ for fresh water, about 1025 for sea water.
@export_range(1.0, 20000.0, 1.0, "or_greater", "suffix:kg/m³") var density: float = 1000.0

@export_group("Waves")
## Height of the largest wave above the mean surface, in metres. 0 for still water.
@export_range(0.0, 5.0, 0.001, "or_greater", "suffix:m") var wave_height: float = 0.0:
	set(value):
		wave_height = value
		_update_surface()
## Distance from one crest to the next, in metres.
@export_range(0.05, 200.0, 0.01, "or_greater", "suffix:m") var wave_length: float = 4.0:
	set(value):
		wave_length = maxf(value, 0.01)
		_update_surface()
## Direction the waves travel, in degrees round the vertical (0 = along +X).
@export_range(-180.0, 180.0, 0.1) var wave_direction: float = 0.0:
	set(value):
		wave_direction = value
		_update_surface()

@export_group("Current")
## Velocity of the flowing water in metres per second, in this node's own frame (so turning the node
## turns the river). Bodies are carried along by drag on their speed through the water.
@export var current: Vector3 = Vector3.ZERO:
	set(value):
		current = value
		_update_surface()
## Width of the slower water along each bank, in metres: across the flow, the current falls to zero
## at the sides of the box over this distance. 0 keeps it the same right up to the sides.
@export_range(0.0, 100.0, 0.01, "or_greater", "suffix:m") var current_edge: float = 0.0

@export_group("Splashes")
## Throw up a splash of droplets when a body hits the water fast enough.
@export var splashes: bool = true
## Leave rings spreading over the surface where bodies go in and come out.
@export var ripples: bool = true
## Slowest impact, in metres per second, that throws up a splash (slower ones only leave rings).
@export_range(0.0, 20.0, 0.01, "or_greater", "suffix:m/s") var splash_min_speed: float = 1.5
## Colour of the droplets and rings.
@export var splash_color: Color = Color(0.95, 0.98, 1.0, 0.85)

@export_group("Look")
## Draw a simple translucent surface that moves with the waves. Turn off to use your own.
@export var show_surface: bool = true:
	set(value):
		show_surface = value
		_update_surface()
## Colour and opacity of the drawn surface.
@export var surface_color: Color = Color(0.22, 0.52, 0.82, 0.55):
	set(value):
		surface_color = value
		_update_surface()

## Wave clock in seconds, advanced with the physics step.
var time: float = 0.0

## Every BoundedBuoyancyWater in the scene tree, for bodies to search quickly.
static var instances: Array[Node3D] = []
## The world this water is in; bodies only use water in their own world.
var world: World3D

const GROUP := "bounded_buoyancy_water"
const SECOND_LENGTH := 0.63     # second wave: shorter,
const SECOND_TURN := 35.0       # turned this many degrees,
const SECOND_HEIGHT := 0.4      # and this fraction of the height
const SHADER_CODE := """
shader_type spatial;
render_mode blend_mix, cull_disabled, depth_draw_opaque, specular_schlick_ggx;
uniform vec4 color : source_color;
uniform float amp;
uniform vec2 k1;
uniform vec2 k2;
uniform float w1;
uniform float w2;
uniform float t;
uniform vec2 flow;
varying vec2 pxz;
float h(vec2 p) {
	return amp * sin(dot(k1, p) - w1 * t) + 0.4 * amp * sin(dot(k2, p) - w2 * t + 1.3);
}
void vertex() {
	vec2 p = VERTEX.xz;
	pxz = p;
	VERTEX.y += h(p);
	float e = 0.02;
	vec3 dx = vec3(2.0 * e, h(p + vec2(e, 0.0)) - h(p - vec2(e, 0.0)), 0.0);
	vec3 dz = vec3(0.0, h(p + vec2(0.0, e)) - h(p - vec2(0.0, e)), 2.0 * e);
	NORMAL = normalize(cross(dz, dx));
}
void fragment() {
	float shade = 0.0;
	float speed = length(flow);
	if (speed > 0.0) {
		// Faint streaks carried downstream, so the flow can be seen.
		vec2 dir = flow / speed;
		float along = dot(pxz - flow * t, dir);
		float across = dot(pxz, vec2(-dir.y, dir.x));
		shade = 0.08 * sin(along * 2.5 + 1.5 * sin(across * 1.3)) * sin(along * 0.6 + across * 0.8);
	}
	ALBEDO = color.rgb * (1.0 + shade);
	ALPHA = color.a;
	ROUGHNESS = 0.15;
}
"""

var _mesh_instance: MeshInstance3D
var _material: ShaderMaterial
var _k1 := Vector2.ZERO
var _k2 := Vector2.ZERO
var _w1 := 0.0
var _w2 := 0.0
var _inv := Transform3D.IDENTITY
var _inv_frame := -1
var _params := PackedFloat64Array([0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -1.0])
var _params_frame := -1
var _splashes: Array = []     # [CPUParticles3D, age]
var _ripples: Array = []      # [MeshInstance3D, ShaderMaterial, age, start radius, strength]
var _droplet_mesh: SphereMesh
var _ripple_shader: Shader

const MAX_SPLASHES := 32
const MAX_RIPPLES := 48
const RIPPLE_LIFE := 1.8
const RIPPLE_SPEED := 0.9     # m/s, how fast the rings spread
const RIPPLE_SHADER := """
shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never;
uniform vec4 color : source_color;
uniform float radius;
uniform float width;
uniform float strength;
uniform vec2 origin;
uniform float amp;
uniform vec2 k1;
uniform vec2 k2;
uniform float w1;
uniform float w2;
uniform float t;
varying vec2 lp;
float h(vec2 p) {
	return amp * sin(dot(k1, p) - w1 * t) + 0.4 * amp * sin(dot(k2, p) - w2 * t + 1.3);
}
void vertex() {
	lp = VERTEX.xz;
	VERTEX.y += h(origin + VERTEX.xz) + 0.006;
}
void fragment() {
	float d = length(lp);
	float outer = 1.0 - smoothstep(0.0, width, abs(d - radius));
	float inner = 1.0 - smoothstep(0.0, width, abs(d - 0.62 * radius));
	ALBEDO = color.rgb;
	ALPHA = color.a * strength * max(outer, 0.6 * inner);
}
"""


func _enter_tree() -> void:
	add_to_group(GROUP)
	world = get_world_3d()
	# Advance the wave clock before the bodies use it, whatever the order of the scene tree.
	process_physics_priority = -1000
	if not instances.has(self):
		instances.append(self)


func _exit_tree() -> void:
	instances.erase(self)


func _ready() -> void:
	_update_surface()


func _physics_process(dt: float) -> void:
	if not Engine.is_editor_hint():
		time += dt


func _process(dt: float) -> void:
	if _material:
		_material.set_shader_parameter("t", time)
	_age_effects(dt)


## Height of the water surface (world Y) above or below the world point `p`.
func height_at(p: Vector3) -> float:
	var y := global_position.y
	if wave_height <= 0.0:
		return y
	var l := _inverse() * p
	var xz := Vector2(l.x, l.z)
	return y + wave_height * (sin(_k1.dot(xz) - _w1 * time) + SECOND_HEIGHT * sin(_k2.dot(xz) - _w2 * time + 1.3))


## Velocity of the water at the world point `p` (world frame), slowing towards the banks if current_edge is set.
func current_at(p: Vector3) -> Vector3:
	if current == Vector3.ZERO:
		return Vector3.ZERO
	var f := 1.0
	if current_edge > 0.0:
		var flat := Vector2(current.x, current.z)
		if flat.length_squared() > 0.0:
			var across := Vector2(-flat.y, flat.x).normalized()
			var l := _inverse() * p
			var half := 0.5 * (absf(across.x) * size.x + absf(across.y) * size.z)
			f = clampf((half - absf(across.dot(Vector2(l.x, l.z)))) / current_edge, 0.0, 1.0)
	return global_transform.basis * (current * f)


## Throw up a splash and rings at the world point `at`, for a body of size `size` (metres across)
## hitting the water at `speed`. Bodies call this themselves; call it to make a splash of your own.
func splash(at: Vector3, speed: float, size: float) -> void:
	if not is_inside_tree() or Engine.is_editor_hint():
		return
	size = clampf(size, 0.02, 20.0)
	var l := to_local(at)
	if ripples and speed > 0.05:
		_add_ripple(l, size, clampf(speed / 3.0, 0.25, 1.0))
	if splashes and speed >= splash_min_speed:
		_add_splash(l, speed, size)


func _add_splash(l: Vector3, speed: float, size: float) -> void:
	if _splashes.size() >= MAX_SPLASHES:
		var old: Array = _splashes.pop_front()
		old[0].queue_free()
	if _droplet_mesh == null:
		_droplet_mesh = SphereMesh.new()
		_droplet_mesh.radius = 0.5
		_droplet_mesh.height = 1.0
		_droplet_mesh.radial_segments = 6
		_droplet_mesh.rings = 3
	var p := CPUParticles3D.new()
	var mat := StandardMaterial3D.new()
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	mat.albedo_color = splash_color
	p.mesh = _droplet_mesh
	p.material_override = mat
	p.one_shot = true
	p.explosiveness = 0.95
	p.lifetime = clampf(0.25 * sqrt(speed), 0.5, 1.4)
	p.amount = clampi(int(14.0 * speed * sqrt(size)), 16, 120)
	p.emission_shape = CPUParticles3D.EMISSION_SHAPE_RING
	p.emission_ring_axis = Vector3.UP
	p.emission_ring_radius = 0.45 * size
	p.emission_ring_inner_radius = 0.3 * size
	p.emission_ring_height = 0.0
	p.direction = Vector3.UP
	p.spread = 28.0
	var up := minf(0.45 * speed, 7.0)
	p.initial_velocity_min = 0.5 * up
	p.initial_velocity_max = up
	p.gravity = Vector3(0, -float(ProjectSettings.get_setting("physics/3d/default_gravity")), 0)
	var drop := clampf(size, 0.1, 3.0)
	p.scale_amount_min = 0.04 + 0.04 * drop
	p.scale_amount_max = 0.08 + 0.08 * drop
	p.position = Vector3(l.x, l.y + 0.02, l.z)
	add_child(p, false, Node.INTERNAL_MODE_BACK)
	p.emitting = true
	_splashes.append([p, 0.0])


func _add_ripple(l: Vector3, size: float, strength: float) -> void:
	if _ripples.size() >= MAX_RIPPLES:
		var old: Array = _ripples.pop_front()
		old[0].queue_free()
	if _ripple_shader == null:
		_ripple_shader = Shader.new()
		_ripple_shader.code = RIPPLE_SHADER
	var reach := 0.5 * size + RIPPLE_SPEED * RIPPLE_LIFE
	var plane := PlaneMesh.new()
	plane.size = Vector2(2.0 * reach, 2.0 * reach)
	plane.subdivide_width = 12
	plane.subdivide_depth = 12
	var mat := ShaderMaterial.new()
	mat.shader = _ripple_shader
	mat.set_shader_parameter("color", splash_color)
	mat.set_shader_parameter("width", clampf(0.06 * size, 0.02, 0.3))
	mat.set_shader_parameter("origin", Vector2(l.x, l.z))
	mat.set_shader_parameter("amp", wave_height)
	mat.set_shader_parameter("k1", _k1)
	mat.set_shader_parameter("k2", _k2)
	mat.set_shader_parameter("w1", _w1)
	mat.set_shader_parameter("w2", _w2)
	var mi := MeshInstance3D.new()
	mi.mesh = plane
	mi.material_override = mat
	mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	mi.position = Vector3(l.x, 0.0, l.z)
	add_child(mi, false, Node.INTERNAL_MODE_BACK)
	_ripples.append([mi, mat, 0.0, 0.5 * size, strength])


func _age_effects(dt: float) -> void:
	var i := 0
	while i < _splashes.size():
		var e: Array = _splashes[i]
		e[1] += dt
		if e[1] > e[0].lifetime + 0.2:
			e[0].queue_free()
			_splashes.remove_at(i)
		else:
			i += 1
	i = 0
	while i < _ripples.size():
		var r: Array = _ripples[i]
		r[2] += dt
		if r[2] >= RIPPLE_LIFE:
			r[0].queue_free()
			_ripples.remove_at(i)
			continue
		var mat: ShaderMaterial = r[1]
		mat.set_shader_parameter("radius", r[3] + RIPPLE_SPEED * r[2])
		mat.set_shader_parameter("strength", r[4] * (1.0 - r[2] / RIPPLE_LIFE))
		mat.set_shader_parameter("t", time)
		i += 1


## The most the surface rises or falls from its mean height.
func max_wave() -> float:
	return wave_height * (1.0 + SECOND_HEIGHT)


## Whether the world point `p` lies over or in the water: inside it horizontally and above its floor.
func contains(p: Vector3) -> bool:
	var l := _inverse() * p
	return absf(l.x) <= 0.5 * size.x and absf(l.z) <= 0.5 * size.z and l.y >= -size.y


## The waves as numbers, for the native kernel: [surface height, height, k1 (x, z), k2 (x, z),
## w1, w2, second height, second phase, time], and the map from world points to the water's frame.
func wave_params() -> PackedFloat64Array:
	var frame := Engine.get_physics_frames()
	if frame != _params_frame or _params[10] != time:
		_params = PackedFloat64Array([global_position.y, wave_height, _k1.x, _k1.y, _k2.x, _k2.y, _w1, _w2, SECOND_HEIGHT, 1.3, time])
		_params_frame = frame
	return _params


func inverse_transform() -> Transform3D:
	return _inverse()


func _inverse() -> Transform3D:
	var frame := Engine.get_physics_frames()
	if frame != _inv_frame:
		_inv = global_transform.affine_inverse()
		_inv_frame = frame
	return _inv


## Wave numbers and frequencies, worked out once whenever a wave setting changes.
func _update_waves() -> void:
	var g := float(ProjectSettings.get_setting("physics/3d/default_gravity"))
	var a1 := deg_to_rad(wave_direction)
	var a2 := deg_to_rad(wave_direction + SECOND_TURN)
	var n1 := TAU / wave_length
	var n2 := TAU / (wave_length * SECOND_LENGTH)
	_k1 = Vector2(cos(a1), sin(a1)) * n1
	_k2 = Vector2(cos(a2), sin(a2)) * n2
	_w1 = sqrt(g * n1)   # deep-water waves: frequency = sqrt(g x wave number)
	_w2 = sqrt(g * n2)


func _update_surface() -> void:
	_update_waves()
	if not is_inside_tree():
		return
	if not show_surface:
		if _mesh_instance:
			_mesh_instance.queue_free()
			_mesh_instance = null
			_material = null
		return
	if _mesh_instance == null:
		_mesh_instance = MeshInstance3D.new()
		_mesh_instance.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		add_child(_mesh_instance, false, Node.INTERNAL_MODE_BACK)
		_material = ShaderMaterial.new()
		var sh := Shader.new()
		sh.code = SHADER_CODE
		_material.shader = sh
	var plane := PlaneMesh.new()
	plane.size = Vector2(size.x, size.z)
	var cells := 1 if wave_height <= 0.0 else clampi(int(maxf(size.x, size.z) / (wave_length / 16.0)), 1, 256)
	plane.subdivide_width = cells
	plane.subdivide_depth = cells
	plane.material = _material
	_mesh_instance.mesh = plane
	_material.set_shader_parameter("color", surface_color)
	_material.set_shader_parameter("amp", wave_height)
	_material.set_shader_parameter("k1", _k1)
	_material.set_shader_parameter("k2", _k2)
	_material.set_shader_parameter("w1", _w1)
	_material.set_shader_parameter("w2", _w2)
	_material.set_shader_parameter("t", time)
	_material.set_shader_parameter("flow", Vector2(current.x, current.z))
