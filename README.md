# Episode Waypoint Color (GTA IV Complete Edition)

ScriptHookDotNet script that replaces player waypoints with episode-colored
script blips and requests a GPS route for each:
GTA IV = steel blue, TLAD = blood red, TBOGT = hot pink.

Install: install ScriptHookDotNet, then copy `EpisodeWaypointColor.cs` to `<GTA IV>/scripts/`.
On first launch it creates `EpisodeWaypointColor.ini` beside the script:

```
[General]
UpdateIntervalMs=50
Debug=false
ArrivalRadius=15
RemoveOnArrival=true
RouteEnabled=true
RainbowMode=false
RainbowCycleMs=2500
WaypointIcon=Misc_Waypoint
WaypointName=Waypoint
SelectionRadius=10
[Icons]
GTAIV=Default
TLAD=Default
TBOGT=Default
[Colors]
Preset=Episode
HueStepDegrees=18
GTAIV=4FA3E0FF
TLAD=C8102EFF
TBOGT=FF2FB9FF
```
Colors are `RRGGBBAA` hex. Settings reload approximately once per second.
The ini is auto-created on first start, and missing keys are appended to existing
files automatically (no deletion needed).
Legacy `Overlay`, `HideOriginal`, and `OverlayIcon` settings are no longer used.

Placing a waypoint at a new location adds a marker without removing earlier markers.
Placing a native waypoint within `SelectionRadius` horizontal metres (1-100) of an
existing marker removes the nearest one instead. This is a proximity-based toggle,
not a direct hook into the map's selection event: it requires the game to create a
native waypoint when you click. Click behaviour and zoom-dependent accuracy need
in-game verification.

The script prepares a copy before deleting only the detected native blip, then
applies its episode icon and route. It does not use the global waypoint switch-off,
which could interfere with other markers. Initial configuration failures remove the
copy and report an error; the destination may already have been transferred if a
later engine call fails.

The marker and route are intended to share the episode color; there is no separate
route-color setting. **Multiple GPS routes are experimental:** the script calls
`SET_ROUTE` for every saved waypoint, but GTA IV may display only one route or
override custom route colours. The API offers no independent per-route colour
setter. Markers can coexist even if simultaneous GPS routes are unsupported.

Each marker stays until selected again or you arrive within
`ArrivalRadius` metres (1-100, default 15). Arrival uses horizontal distance because
map-selected waypoint height may not match road height. Arrival removal is suspended
while paused. Detection runs on the next script tick, not directly inside the map's
click handler; an existing ini may retain a slower `UpdateIntervalMs`.

`RemoveOnArrival`, `RouteEnabled`, `WaypointIcon`, `WaypointName`, and all color values
are editable mid-session.

### Episode icons and color presets

Set `[Icons]` keys `GTAIV`, `TLAD`, and `TBOGT` to a `BlipIcon` enum name, such as
`Misc_Waypoint`, `Misc_Destination`, or `Misc_Destination1`. `Default` inherits
`[General] WaypointIcon`, preserving existing configurations. Icon changes apply to
all saved markers immediately after settings reload. `WaypointName` remains the
shared map label, defaulting to `Waypoint`.

`[Colors] Preset` accepts:

- `Episode`: uses your `GTAIV`, `TLAD`, and `TBOGT` ini colors.
- `Original`: uses the green preset index (8), not an engine-default color query.
- `HighContrast`: uses bright yellow.
- `Rainbow`: continuously cycles the base color.

The earliest remaining waypoint gets the exact base color. Later markers receive
alternating positive/negative hue offsets using `HueStepDegrees` (1-60, default 18).
After any removal, colors are reassigned in placement order, so the new first marker
gets the base color. White, grey, and black bases gain a subtle tint on later markers.
For preset-index colors, later markers use approximate RGB equivalents; the first
retains the exact index. Many markers can eventually repeat hues.

`RainbowMode=true` makes the waypoint color cycle continuously. `RainbowCycleMs`
controls how long one full cycle takes (250-60000). When rainbow mode is on, episode
color keys are ignored until it is turned off.

This is not an exact replacement for the engine's waypoint bookkeeping. Native
map-click cancellation, mission route priority, taxi destination selection, other
mods that read waypoints, and script reload cleanup need in-game verification.
The mod does not recolor or delete mission objective blips.

Troubleshooting: set `Debug=true`, place a waypoint, and check the console (`~`) and
`EpisodeWaypointColor.log` beside the script. Debug records additions, map-selection
and arrival removals, current count, episode, preset, configured icon/color/route,
settings reloads, and error stack traces. It does not log every rainbow frame.
A color of 1-2 digits (e.g. `TLAD=9`) uses a preset index instead (0 white, 1 dark red, 3 cyan, 5 yellow, 7 purple, 9 red, 13 turquoise...).

In-game checks: add three waypoints in each episode, select the middle one to remove
it, then remove the first and confirm the remaining marker gets the base color.
Check requested routes and their colors, arrival removal, each preset, episode
icons, and ini edits during play. Repeat during a mission. Route rendering and
map-selection interception cannot be verified outside the game.
