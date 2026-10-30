# Pictures of the demo scenes for the store page and the README.
# Run (with a display or xvfb-run): godot --path . --rendering-driver vulkan --audio-driver Dummy --script res://tests/shots.gd -- <out_dir> [scene ...]
extends SceneTree

var out := "user://"

func _initialize() -> void:
	var scenes: Array = []
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--") or a.ends_with("/") or a.begins_with("/"):
			if not a.begins_with("--"):
				out = a
		else:
			scenes.append(a)
	if scenes.is_empty():
		scenes = ["demo_battle", "demo_flood", "demo_fishing", "demo_ocean", "demo_cannons", "demo_boats", "demo_sky", "demo_waves"]
	DisplayServer.window_set_size(Vector2i(1280, 720))
	root.size = Vector2i(1280, 720)
	for name in scenes:
		change_scene_to_file("res://addons/bounded_buoyancy/demo/%s.tscn" % name)
		for i in 300:
			await physics_frame
		# Clean pictures: the demo's text and controls hidden.
		for c in current_scene.find_children("*", "CanvasLayer", true, false):
			c.visible = false
		for c in current_scene.find_children("*", "Control", true, false):
			c.visible = false
		await process_frame
		await RenderingServer.frame_post_draw
		root.get_viewport().get_texture().get_image().save_png("%s/%s.png" % [out, name])
		print("saved ", name)
	quit()
