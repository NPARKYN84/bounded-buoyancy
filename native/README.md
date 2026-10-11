# Building the native kernel

The kernel is a small GDExtension (`src/`) built with godot-cpp. The plugin works without it; it only makes the waterline cut faster.

1. Install Python 3, SCons (`pip install scons`) and a C++ compiler (Visual Studio or MinGW on Windows, Xcode on macOS, GCC or Clang on Linux).
2. In this folder, fetch godot-cpp for Godot 4.4:

       git clone --depth 1 --branch godot-4.4-stable https://github.com/godotengine/godot-cpp.git

3. Build for your platform, for example:

       scons platform=windows target=template_release
       scons platform=linux target=template_release
       scons platform=macos target=template_release arch=universal

   The library is written to `../addons/bounded_buoyancy_native/bin/`.
4. For a platform not yet listed in `addons/bounded_buoyancy_native/bounded_buoyancy_native.gdextension`, add a line for it, for example:

       macos = "res://addons/bounded_buoyancy_native/bin/libbounded_buoyancy.macos.template_release.universal.dylib"

The GitHub workflow in `.github/workflows/build_native.yml` does all of this for Windows, Linux and macOS whenever the native source changes (or when run by hand from the Actions tab), and attaches the libraries to the run. It also runs the plugin's checks on every push.
