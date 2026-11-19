@tool
extends EditorPlugin
# The node types register themselves through their class_name. The plugin adds the waterline
# preview drawn on bodies in the editor.

var _waterline: EditorNode3DGizmoPlugin


func _enter_tree() -> void:
	_waterline = preload("res://addons/bounded_buoyancy/bounded_buoyancy_waterline_gizmo.gd").new()
	add_node_3d_gizmo_plugin(_waterline)


func _exit_tree() -> void:
	if _waterline:
		remove_node_3d_gizmo_plugin(_waterline)
		_waterline = null


func _enable_plugin() -> void:
	print("Bounded Buoyancy is on: add a BoundedBuoyancyBody, set its Density or what it is Made Of, and it floats. "
		+ "The demo scenes are in addons/bounded_buoyancy/demo. If it helps you, a rating on the Asset Store helps others find it.")
