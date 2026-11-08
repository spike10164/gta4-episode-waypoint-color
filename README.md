# Episode Waypoint Color (GTA IV Complete Edition)

ScriptHookDotNet script that replaces the player's waypoint with an episode-colored
script blip and enables its GPS route:
GTA IV = steel blue, TLAD = blood red, TBOGT = hot pink.

Install: install ScriptHookDotNet, then copy `EpisodeWaypointColor.cs` to `<GTA IV>/scripts/`.
On first launch it creates `EpisodeWaypointColor.ini` beside the script:

```
[General]
UpdateIntervalMs=50
Debug=false
ArrivalRadius=15
[Colors]
GTAIV=4FA3E0FF
TLAD=C8102EFF
TBOGT=FF2FB9FF
```
Colors are `RRGGBBAA` hex. Settings reload approximately once per second.
Missing settings are added automatically without deleting your existing ini.
Legacy `Overlay`, `HideOriginal`, and `OverlayIcon` settings are no longer used.

When a native waypoint is detected, the script creates its own marker at the same
spot, enables `SET_ROUTE` on it, then removes the native waypoint using
`SWITCH_OFF_WAYPOINT`. A moved waypoint replaces the copy (blip positions cannot be
changed in this API). The copy is fully configured before the native waypoint is
removed, and any failure deletes the copy so the original is never lost.

The marker and route are intended to share the episode color; there is no separate
route-color setting. **In-game verification is still required:** the engine may
override route colors or only accept preset color indexes for routing.

The replacement stays until another waypoint is placed or you arrive within
`ArrivalRadius` metres (1-100, default 15). Arrival uses horizontal distance because
map-selected waypoint height may not match road height. Arrival removal is suspended
while paused. Detection runs on the next script tick, not directly inside the map's
click handler; an existing ini may retain a slower `UpdateIntervalMs`.

This is not an exact replacement for the engine's waypoint bookkeeping. Native
map-click cancellation, mission route priority, taxi destination selection, other
mods that read waypoints, and script reload cleanup need in-game verification.
The mod does not recolor or delete mission objective blips.

Troubleshooting: set `Debug=true`, place a waypoint, and check the console (`~`).
A color of 1-2 digits (e.g. `TLAD=9`) uses a preset index instead (0 white, 1 dark red, 3 cyan, 5 yellow, 7 purple, 9 red, 13 turquoise...).

In-game checks: place and move a waypoint in each episode, confirm a single marker
and route, drive into the arrival radius, then repeat during a mission and after
editing the ini. Check both custom hex colors and preset indexes.
