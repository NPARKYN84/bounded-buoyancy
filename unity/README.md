# Bounded Buoyancy for Unity

The Unity version of the plugin, for Unity 2022.3 LTS and Unity 6: the same physics, settings and benchmarks as the
Godot plugin at the top of this repository, in 3D and 2D.

- `com.parkyn.bounded-buoyancy/`: the package. Install it in Unity from *Window → Package Manager → + → Add package
  from git URL…* with `https://github.com/NPARKYN84/bounded-buoyancy.git?path=/unity/com.parkyn.bounded-buoyancy`.
  Its README explains everything else, starting with a one-minute check.
- `checks/`: plain-.NET checks that run without Unity (the physics against the benchmarks, the edit-mode tests, and
  compiling against the Unity 2022.3 and Unity 6 API, and the HDRP 14 and 17 water API for the HDRP link). GitHub runs
  them on every change to this folder.
