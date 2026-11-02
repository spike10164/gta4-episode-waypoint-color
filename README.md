# Episode Waypoint Color (GTA IV Complete Edition)

ScriptHookDotNet script that recolors the waypoint by episode:
GTA IV = steel blue, TLAD = blood red, TBOGT = hot pink.

Install: install ScriptHookDotNet, then copy `EpisodeWaypointColor.cs` to `<GTA IV>/scripts/`.
On first launch it creates `EpisodeWaypointColor.ini` beside the script:

```
[General]
UpdateIntervalMs=250
Debug=false
Overlay=true
HideOriginal=true
OverlayIcon=Misc_Destination
[Colors]
GTAIV=4FA3E0FF
TLAD=C8102EFF
TBOGT=FF2FB9FF
```
Colors are `RRGGBBAA` hex. Edit the file and restart the game (or reload scripts) to apply. Delete it to regenerate defaults.

Untested in-game; if custom colors don't show, the native may only accept preset indexes.

Troubleshooting: set `Debug=true`, place a waypoint, and check the console (`~`). It prints whether the waypoint was found and the color set vs. read back.
A color of 1-2 digits (e.g. `TLAD=9`) uses a preset index instead (0 white, 1 dark red, 3 cyan, 5 yellow, 7 purple, 9 red, 13 turquoise...).

`Overlay=true` keeps the game's real waypoint (so the GPS route and removal still work) and draws a recolored blip on top using `OverlayIcon` (any `BlipIcon` name). It must differ from `Misc_Waypoint`, otherwise the mod can't tell its own blip from the real one. `HideOriginal` tries to hide the real blip on the map (`MapOnly`). The GPS route line is drawn by the game and stays green. `Overlay=false` just recolors the real waypoint.
