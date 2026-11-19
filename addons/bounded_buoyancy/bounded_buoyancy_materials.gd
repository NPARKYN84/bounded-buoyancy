@tool
class_name BoundedBuoyancyMaterials
extends RefCounted
## Common materials and their densities (kg/m³), for a body's Material setting: pick one and the body
## takes its density, so it floats or sinks as that material does. Typical values for the dry,
## solid material (woods air-dried; a real piece varies by a few per cent, more for wood).

enum Kind {
	CUSTOM,     ## Set the density yourself.
	FOAM,       ## Expanded polystyrene foam.
	BALSA,
	CORK,
	PINE,
	PLYWOOD,
	TEAK,
	OAK,
	ICE,        ## Freshwater ice.
	PLASTIC,    ## Polyethylene.
	EBONY,
	RUBBER,     ## Solid rubber.
	PVC,
	BRICK,
	CONCRETE,
	GLASS,
	STONE,      ## Granite.
	ALUMINIUM,
	IRON,       ## Cast iron.
	STEEL,
	LEAD,
	GOLD,
}

const DENSITY := {
	Kind.FOAM: 30.0,
	Kind.BALSA: 160.0,
	Kind.CORK: 240.0,
	Kind.PINE: 510.0,
	Kind.PLYWOOD: 600.0,
	Kind.TEAK: 660.0,
	Kind.OAK: 720.0,
	Kind.ICE: 917.0,
	Kind.PLASTIC: 950.0,
	Kind.EBONY: 1100.0,
	Kind.RUBBER: 1100.0,
	Kind.PVC: 1380.0,
	Kind.BRICK: 1900.0,
	Kind.CONCRETE: 2400.0,
	Kind.GLASS: 2500.0,
	Kind.STONE: 2700.0,
	Kind.ALUMINIUM: 2700.0,
	Kind.IRON: 7200.0,
	Kind.STEEL: 7850.0,
	Kind.LEAD: 11340.0,
	Kind.GOLD: 19300.0,
}


## The density of a material (kg/m³), or `fallback` for CUSTOM.
static func density_of(kind: Kind, fallback := 500.0) -> float:
	return DENSITY.get(kind, fallback)


## Its name as the inspector shows it ("Oak", "Cast iron" ...).
static func name_of(kind: Kind) -> String:
	return String(Kind.keys()[kind]).capitalize()
