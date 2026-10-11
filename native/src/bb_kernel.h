// Bounded Buoyancy native kernel: the waterline cut of a closed triangle mesh, in C++.
// Optional speed-up for the GDScript plugin; the plugin gives the same results without it.
#pragma once

#include <godot_cpp/classes/ref_counted.hpp>
#include <godot_cpp/variant/packed_float32_array.hpp>
#include <godot_cpp/variant/packed_float64_array.hpp>
#include <godot_cpp/variant/packed_int32_array.hpp>
#include <godot_cpp/variant/packed_vector3_array.hpp>
#include <godot_cpp/variant/transform3d.hpp>
#include <godot_cpp/variant/vector3.hpp>
#include <godot_cpp/variant/vector4.hpp>

namespace godot {

class BoundedBuoyancyKernel : public RefCounted {
	GDCLASS(BoundedBuoyancyKernel, RefCounted)

protected:
	static void _bind_methods();

public:
	// Volume below a flat surface and its first moment, in body space. Each vertex j lies at depth
	// h - up.dot(verts[j]) below the surface. Returns (volume, moment), where the moment is the sum of
	// tetrahedron volume x (p + a + b + c), so the centre of buoyancy is moment / (4 volume).
	Vector4 clip_flat(const PackedVector3Array &verts, const PackedInt32Array &idx, const PackedFloat32Array &weights,
			const Vector3 &up, double h, const Vector3 &p) const;

	// The same with each vertex measured against a wavy surface. wave = [water_y, amplitude, k1x, k1z,
	// k2x, k2z, w1, w2, second_height, second_phase, time]; water_inv maps world points into the water's frame.
	Vector4 clip_waves(const PackedVector3Array &verts, const PackedInt32Array &idx, const PackedFloat32Array &weights,
			const Transform3D &body, const Transform3D &water_inv, const PackedFloat64Array &wave, const Vector3 &p) const;

	int version() const { return 1; }
};

} // namespace godot
