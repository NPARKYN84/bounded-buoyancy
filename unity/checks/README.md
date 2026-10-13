# Checks for the Unity package

These run without Unity, with the .NET 8 SDK. None of this folder is part of the package itself.

- `CoreTests`: the package's engine-free physics core (`Runtime/Core`) run against every benchmark the Godot
  plugin passes, with a small fixed-step integrator standing in for the physics engine. `dotnet run -c Release`
  prints each check and ends with PASS.
- `EditModeRunner`: runs the package's edit-mode tests (`Tests/Editor`) in plain .NET. `dotnet run -c Release`.
- `Split`: compiles the runtime, the HDRP link, the play-mode tests, the edit-mode tests, the demo sample and its
  editor menu, each as its own assembly as Unity builds them, against minimal stand-ins for the Unity API, once for
  Unity 2022.3 (with HDRP 14) and once for Unity 6 (with HDRP 17), with warnings as errors.
  `python3 make.py 2022_3 && python3 make.py 6000_0`, then `dotnet build` in `2022_3/HDRP`, `2022_3/SamplesEditor`,
  `2022_3/Tests`, `2022_3/EditorTests` and the same under `6000_0`.
- `StubCompile`: the stand-ins (`UnityStubs.cs`, `SampleStubs.cs`, `TestStubs.cs`, and `HDRPStubs.cs` for the HDRP
  link). They declare only the members the package uses, with the names, types and namespaces of Unity's published
  C# reference source (github.com/Unity-Technologies/UnityCsReference, 2022.3 and 6000.0) and HDRP's published source
  (github.com/Unity-Technologies/Graphics, 2022.3 and 6000.0); they contain no Unity code. `u2022_3` and
  `u6000_0` compile everything as one assembly, a quicker check.
- `Compare`: a head-to-head of four ways of floating things on the same integrator, in the same water, through
  the same tests: the usual method (buoyancy as a force on the body's own mass), the usual method with damping in
  the water, pontoons with damping (spheres approximating the shape, the method of Unreal's Water plugin and many
  assets), and Bounded Buoyancy. The other methods are written from their published descriptions, not taken from
  any engine's code; their damping is tuned once, on a wooden float, and used for everything. `dotnet run -c Release`
  prints the tables and writes `compare_results.csv` and a trace of the table-tennis ball's height.
- `make_meta.py`: writes the `.meta` files Unity needs when the package is installed from git; the GUIDs come from
  each file's path, so they do not change from one run to the next.

`nuget.config` clears the package sources: nothing is downloaded.

These checks cannot run Unity's physics engines. The package's play-mode tests do that, in Unity: see the package
README, "Try it in a minute".
