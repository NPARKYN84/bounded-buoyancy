@tool
extends EditorPlugin
# The BoundedBuoyancyBody node type registers itself through its class_name,
# so the plugin needs no setup of its own.


func _enable_plugin() -> void:
	print("Bounded Buoyancy is on: add a BoundedBuoyancyBody, set its Density or what it is Made Of, and it floats. "
		+ "The demo scenes are in addons/bounded_buoyancy/demo. If it helps you, a rating on the Asset Store helps others find it.")
