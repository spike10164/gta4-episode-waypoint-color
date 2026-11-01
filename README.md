# Episode Waypoint Color (GTA IV Complete Edition)

ScriptHookDotNet script that recolors the waypoint by episode:
GTA IV = steel blue, TLAD = blood red, TBOGT = hot pink.

Install: install ScriptHookDotNet, then copy `EpisodeWaypointColor.cs` to `<GTA IV>/scripts/`.
On first launch it creates `EpisodeWaypointColor.ini` beside the script:

```
[General]
UpdateIntervalMs=500
[Colors]
GTAIV=4FA3E0FF
TLAD=C8102EFF
TBOGT=FF2FB9FF
```
Colors are `RRGGBBAA` hex. Edit the file and restart the game (or reload scripts) to apply. Delete it to regenerate defaults.

Untested in-game; if custom colors don't show, the native may only accept preset indexes.
