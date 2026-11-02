# Episode Waypoint Color (GTA IV Complete Edition)

ScriptHookDotNet script that recolors the waypoint by episode:
GTA IV = steel blue, TLAD = blood red, TBOGT = hot pink.

Install: install ScriptHookDotNet, then copy `EpisodeWaypointColor.cs` to `<GTA IV>/scripts/`.
On first launch it creates `EpisodeWaypointColor.ini` beside the script:

```
[General]
UpdateIntervalMs=250
Debug=false
ReplaceWaypoint=true
[Colors]
GTAIV=4FA3E0FF
TLAD=C8102EFF
TBOGT=FF2FB9FF
```
Colors are `RRGGBBAA` hex. Edit the file and restart the game (or reload scripts) to apply. Delete it to regenerate defaults.

Untested in-game; if custom colors don't show, the native may only accept preset indexes.

Troubleshooting: set `Debug=true`, place a waypoint, and check the console (`~`). It prints whether the waypoint was found and the color set vs. read back.
A color of 1-2 digits (e.g. `TLAD=9`) uses a preset index instead (0 white, 1 dark red, 3 cyan, 5 yellow, 7 purple, 9 red, 13 turquoise...).

`ReplaceWaypoint=true` (experimental): GTA IV has no native to re-place a waypoint, so the mod deletes the game's waypoint blip and puts a recolored waypoint-icon blip at the same spot. It may not give the in-game GPS route; set `false` to only recolor the real waypoint.
