#include "bb_kernel.h"

#include <godot_cpp/core/class_db.hpp>

#include <cmath>
#include <cstdlib>

using namespace godot;

namespace {

struct V3 {
	double x, y, z;
};
inline V3 sub(const V3 &a, const V3 &b) { return { a.x - b.x, a.y - b.y, a.z - b.z }; }
inline V3 add3(const V3 &a, const V3 &b, const V3 &c) { return { a.x + b.x + c.x, a.y + b.y + c.y, a.z + b.z + c.z }; }
inline V3 lerp(const V3 &a, const V3 &b, double t) { return { a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t }; }
inline double triple(const V3 &a, const V3 &b, const V3 &c) {
	return a.x * (b.y * c.z - b.z * c.y) + a.y * (b.z * c.x - b.x * c.z) + a.z * (b.x * c.y - b.y * c.x);
}

// Sum of tetrahedra (p, triangle part below the surface). Vertices are taken relative to p.
Vector4 clip_core(const Vector3 *verts, const int32_t *idx, int64_t n_idx, const float *weights,
		const double *depth, const Vector3 &pv) {
	const V3 p = { pv.x, pv.y, pv.z };
	double vol = 0.0;
	V3 m = { 0.0, 0.0, 0.0 };
	for (int64_t i = 0, t = 0; i + 2 < n_idx; i += 3, ++t) {
		const int32_t ia = idx[i], ib = idx[i + 1], ic = idx[i + 2];
		double da = depth[ia], db = depth[ib], dc = depth[ic];
		const int under = int(da > 0.0) + int(db > 0.0) + int(dc > 0.0);
		if (under == 0) {
			continue;
		}
		V3 a = sub({ verts[ia].x, verts[ia].y, verts[ia].z }, p);
		V3 b = sub({ verts[ib].x, verts[ib].y, verts[ib].z }, p);
		V3 c = sub({ verts[ic].x, verts[ic].y, verts[ic].z }, p);
		const double w = weights[t];
		if (under == 3) {
			const double t3 = w * triple(a, b, c) / 6.0;
			const V3 s = add3(a, b, c);
			vol += t3;
			m.x += t3 * s.x;
			m.y += t3 * s.y;
			m.z += t3 * s.z;
			continue;
		}
		// Turn the triangle, keeping its winding, so that the odd vertex out comes first.
		if ((under == 1 && db > 0.0) || (under == 2 && db <= 0.0)) {
			V3 tv = a; a = b; b = c; c = tv;
			double td = da; da = db; db = dc; dc = td;
		} else if ((under == 1 && dc > 0.0) || (under == 2 && dc <= 0.0)) {
			V3 tv = c; c = b; b = a; a = tv;
			double td = dc; dc = db; db = da; da = td;
		}
		const V3 ab = lerp(a, b, da / (da - db));
		const V3 ca = lerp(c, a, dc / (dc - da));
		if (under == 1) {
			const double t1 = w * triple(a, ab, ca) / 6.0;
			const V3 s = add3(a, ab, ca);
			vol += t1;
			m.x += t1 * s.x;
			m.y += t1 * s.y;
			m.z += t1 * s.z;
		} else {
			const double t2a = w * triple(ab, b, c) / 6.0;
			const double t2b = w * triple(ab, c, ca) / 6.0;
			const V3 s1 = add3(ab, b, c);
			const V3 s2 = add3(ab, c, ca);
			vol += t2a + t2b;
			m.x += t2a * s1.x + t2b * s2.x;
			m.y += t2a * s1.y + t2b * s2.y;
			m.z += t2a * s1.z + t2b * s2.z;
		}
	}
	// Back to the plugin's convention: the sum of volume x (p + a + b + c) with absolute vertices.
	return Vector4(vol, m.x + 4.0 * p.x * vol, m.y + 4.0 * p.y * vol, m.z + 4.0 * p.z * vol);
}

// Per-thread scratch space for the depth of each vertex, grown as needed (plain C allocation, so
// the library needs nothing newer from the C++ runtime than the oldest supported systems have).
struct DepthBuffer {
	double *data = nullptr;
	int64_t size = 0;
	~DepthBuffer() { std::free(data); }
	double *get(int64_t n) {
		if (n > size) {
			double *grown = static_cast<double *>(std::realloc(data, sizeof(double) * n));
			if (grown == nullptr) {
				return nullptr;
			}
			data = grown;
			size = n;
		}
		return data;
	}
};
thread_local DepthBuffer t_depth;

} // namespace

Vector4 BoundedBuoyancyKernel::clip_flat(const PackedVector3Array &verts, const PackedInt32Array &idx,
		const PackedFloat32Array &weights, const Vector3 &up, double h, const Vector3 &p) const {
	const int64_t nv = verts.size();
	if (nv == 0 || weights.size() * 3 < idx.size()) {
		return Vector4();
	}
	double *depth = t_depth.get(nv);
	if (depth == nullptr) {
		return Vector4();
	}
	const Vector3 *v = verts.ptr();
	const double ux = up.x, uy = up.y, uz = up.z;
	for (int64_t j = 0; j < nv; ++j) {
		depth[j] = h - (ux * v[j].x + uy * v[j].y + uz * v[j].z);
	}
	return clip_core(v, idx.ptr(), idx.size(), weights.ptr(), depth, p);
}

Vector4 BoundedBuoyancyKernel::clip_waves(const PackedVector3Array &verts, const PackedInt32Array &idx,
		const PackedFloat32Array &weights, const Transform3D &body, const Transform3D &water_inv,
		const PackedFloat64Array &wave, const Vector3 &p) const {
	const int64_t nv = verts.size();
	if (nv == 0 || wave.size() < 11 || weights.size() * 3 < idx.size()) {
		return Vector4();
	}
	const double *wv = wave.ptr();
	const double water_y = wv[0], amp = wv[1], k1x = wv[2], k1z = wv[3], k2x = wv[4], k2z = wv[5];
	const double w1 = wv[6], w2 = wv[7], h2 = wv[8], phase2 = wv[9], time = wv[10];
	double *depth = t_depth.get(nv);
	if (depth == nullptr) {
		return Vector4();
	}
	const Vector3 *v = verts.ptr();
	for (int64_t j = 0; j < nv; ++j) {
		const Vector3 wp = body.xform(v[j]);
		const Vector3 l = water_inv.xform(wp);
		const double height = water_y + amp * (std::sin(k1x * l.x + k1z * l.z - w1 * time) +
				h2 * std::sin(k2x * l.x + k2z * l.z - w2 * time + phase2));
		depth[j] = height - wp.y;
	}
	return clip_core(v, idx.ptr(), idx.size(), weights.ptr(), depth, p);
}

void BoundedBuoyancyKernel::_bind_methods() {
	ClassDB::bind_method(D_METHOD("clip_flat", "verts", "idx", "weights", "up", "h", "p"), &BoundedBuoyancyKernel::clip_flat);
	ClassDB::bind_method(D_METHOD("clip_waves", "verts", "idx", "weights", "body", "water_inv", "wave", "p"), &BoundedBuoyancyKernel::clip_waves);
	ClassDB::bind_method(D_METHOD("version"), &BoundedBuoyancyKernel::version);
}
