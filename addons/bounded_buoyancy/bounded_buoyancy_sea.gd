@tool
class_name BoundedBuoyancySea
extends RefCounted
## Sea states: how rough the sea is, by the scale sailors and weather services use (the World
## Meteorological Organization's sea state code, the Douglas scale's sea), from calm to phenomenal.
## Each sets the waves of a BoundedBuoyancyWater (or Water2D) through its Sea State setting.
##
## Each state is a band of significant wave height (the mean height, trough to crest, of the highest
## third of the waves; what an observer reports as the height of the sea), and the waves are given the
## middle of it: the highest of them, the main wave with the second on top of it, is that tall, trough
## to crest. Their length is that of a sea the wind has fully raised (Pierson and Moskowitz, 1964:
## significant height 0.21 U²/g, peak period 2π U / 0.877 g, so about 39 times the height from crest
## to crest): a rough sea's waves are 3 m high and over 100 m long, not short and steep.

enum State {
	CUSTOM,     ## Set Wave Height and Wave Length yourself.
	CALM,       ## Calm, glassy: no waves.
	RIPPLED,    ## Calm, rippled: 0 to 0.1 m.
	SMOOTH,     ## Smooth, wavelets: 0.1 to 0.5 m.
	SLIGHT,     ## Slight: 0.5 to 1.25 m.
	MODERATE,   ## Moderate: 1.25 to 2.5 m.
	ROUGH,      ## Rough: 2.5 to 4 m.
	VERY_ROUGH, ## Very rough: 4 to 6 m.
	HIGH,       ## High: 6 to 9 m.
	VERY_HIGH,  ## Very high: 9 to 14 m.
	PHENOMENAL, ## Phenomenal: over 14 m.
}

## Significant wave height of each state (m): the middle of its band.
const HEIGHT := [0.0, 0.0, 0.05, 0.3, 0.875, 1.875, 3.25, 5.0, 7.5, 11.5, 16.0]
## Waves a fully raised sea has, crest to crest, for each metre of significant height.
const LENGTH_PER_HEIGHT := 39.0
## The highest of a water node's waves, trough to crest, over its Wave Height (the main wave's height
## above the mean, with the second wave, 0.4 of it, on top): 2 (1 + 0.4).
const TROUGH_TO_CREST := 2.8


## Significant wave height of `state` (m).
static func significant_height(state: State) -> float:
	return HEIGHT[state]


## Wave Height for `state`: the main wave's height above the mean surface (m).
static func wave_height_of(state: State) -> float:
	return HEIGHT[state] / TROUGH_TO_CREST


## Wave Length for `state` (m), or 0 for a calm sea (keep the length you have).
static func wave_length_of(state: State) -> float:
	return LENGTH_PER_HEIGHT * HEIGHT[state]


## The state's name, as on the scale.
static func name_of(state: State) -> String:
	return ["Custom", "Calm (glassy)", "Calm (rippled)", "Smooth", "Slight", "Moderate", "Rough", "Very rough", "High", "Very high", "Phenomenal"][state]


## The state whose band a significant wave height of `height` m falls in.
static func state_for(height: float) -> State:
	var tops := [0.0, 0.1, 0.5, 1.25, 2.5, 4.0, 6.0, 9.0, 14.0]
	for i in tops.size():
		if height <= tops[i]:
			return (i + 1) as State
	return State.PHENOMENAL
