# Episode Waypoint Color (GTA IV Complete Edition)

ScriptHookDotNet script that replaces the player's waypoint with an episode-coloured
script blip and GPS route:
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
SplitRouteBlip=true
RainbowMode=false
RainbowCycleMs=2500
WaypointIcon=Misc_Destination
WaypointName=Waypoint
Persist=false
PersistPerEpisode=true
SelectionRadius=75
[Icons]
GTAIV=Default
TLAD=Default
TBOGT=Default
[Colors]
Preset=Episode
GTAIV=4FA3E0FF
TLAD=C8102EFF
TBOGT=FF2FB9FF
```
Colours are `RRGGBBAA` hex; 1-2 digits (e.g. `TLAD=9`) use a preset index instead
(0 white, 1 dark red, 3 cyan, 5 yellow, 7 purple, 9 red, 13 turquoise...).
Settings reload about once per second during play. Missing keys are appended to
existing ini files automatically; existing values are never overwritten.

### How it works

There is one waypoint at a time. When you place a waypoint, the script creates its
own blip there (icon, name, colour and route set immediately) and removes the game's
waypoint (`SWITCH_OFF_WAYPOINT`, then direct removal as a fallback). Placing a new
waypoint moves it. Placing one within `SelectionRadius` metres (1-250, default 75) of
the current waypoint clears it instead, because map clicks on a blip often land
50+ m away. It is also removed when you arrive within `ArrivalRadius` metres
(1-100, default 15, horizontal distance) if `RemoveOnArrival=true`.

GTA IV has no way to disable its built-in waypoint, and scripts do not run while the
pause menu/map is open, so the replacement happens as soon as you return to the game.
The engine calculates a script blip's GPS route shortly after it is created, so the
route can appear a moment later than the native one.

### Icons and route

The game draws the icon of whichever blip has the GPS route in its fixed colour. With
`SplitRouteBlip=true` (default) the visible icon has no route; a second, zero-size blip at
the same spot carries the route, coloured the same so the route line matches. Set it to
`false` to put the route on the visible icon again (icon colour is then ignored).

The default icon is `Misc_Destination`; `[Icons]` `GTAIV`/`TLAD`/`TBOGT` accept any
`BlipIcon` enum name (e.g. `Misc_Waypoint`, `Misc_Destination1`, `Misc_Objective`);
`Default` uses `[General] WaypointIcon`.

### Colour presets

- `Episode`: your `GTAIV`, `TLAD` and `TBOGT` colours.
- `Original`: green preset index (8).
- `HighContrast`: bright yellow.
- `Rainbow`: cycles continuously (same as `RainbowMode=true`; `RainbowCycleMs` 250-60000).

### Persistence

`Persist=true` saves the waypoint position to `EpisodeWaypointColor_Waypoints_<Episode>.txt`
(or `EpisodeWaypointColor_Waypoints.txt` with `PersistPerEpisode=false`) and restores it
about 3 seconds after the player appears on the next launch or script reload. The file
is deleted when you clear or reach the waypoint. Engine removals (save load, mission
cleanup) do not erase it. Persistence is per installation, not per save game.

### Troubleshooting

Set `Debug=true` and check the console (`~`) and `EpisodeWaypointColor.log` beside the
script. It logs placements, removals (with the distance to the current waypoint),
configured icon/colour/route, settings reloads and errors.
