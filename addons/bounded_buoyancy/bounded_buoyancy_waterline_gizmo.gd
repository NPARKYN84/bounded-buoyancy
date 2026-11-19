@tool
extends EditorNode3DGizmoPlugin
## In the editor: where a BoundedBuoyancyBody will float in still water, from its density (or what it is
## Made Of) and its shape as it is placed: a sheet of water at that level round it, see-through, and
## the waterline drawn on it. Nothing is drawn for a body that sinks.

var _sheet: PlaneMesh


func _init() -> void:
	create_material("waterline", Color(0.1, 0.55, 1.0), false, true)
	create_material("sheet", Color(0.2, 0.6, 1.0, 0.28), false, false)
	_sheet = PlaneMesh.new()
	_sheet.size = Vector2.ONE


func _get_gizmo_name() -> String:
	return "Bounded Buoyancy waterline"


func _has_gizmo(node: Node3D) -> bool:
	return node is BoundedBuoyancyBody


func _redraw(gizmo: EditorNode3DGizmo) -> void:
	gizmo.clear()
	var body := gizmo.get_node_3d() as BoundedBuoyancyBody
	if body == null or not body.is_inside_tree():
		return
	var p := body.floating_preview()
	if p.is_empty() or p.sinks:
		return
	var lines := body.waterline_at(p.level)
	if lines.size() >= 2:
		gizmo.add_lines(lines, get_material("waterline", gizmo))
	# The sheet of water: level, round the body's centre, a little wider than the body.
	var xf := body.global_transform
	var centre := xf * body._centroid
	var across := 3.0 * body._bound_radius
	var sheet := Transform3D(Basis().scaled(Vector3(across, 1.0, across)), Vector3(centre.x, p.level, centre.z))
	gizmo.add_mesh(_sheet, get_material("sheet", gizmo), xf.affine_inverse() * sheet)
