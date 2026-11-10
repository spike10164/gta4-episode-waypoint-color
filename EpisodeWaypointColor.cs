using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GTA;

namespace EpisodeWaypointColor {

   // Recolors the player's waypoint blip to match the current episode:
   //   GTA IV -> steel blue, TLAD -> blood red, TBOGT -> hot pink.
   // Written in C# 2.0 style (no var, lambdas or string interpolation).
   public class EpisodeWaypointColor : Script {

      // Defaults, packed as RRGGBBAA hex (the format CHANGE_BLIP_COLOUR accepts for custom colors).
      private const string DefaultGTAIV = "4FA3E0FF";
      private const string DefaultTLAD = "C8102EFF";
      private const string DefaultTBOGT = "FF2FB9FF";

      private int colorGTAIV;
      private int colorTLAD;
      private int colorTBOGT;
      private bool debug = false;
      private class SavedWaypoint {
         public Blip Blip;
         // invisible blip at the same spot that carries the GPS route (null when not split)
         public Blip RouteBlip;
         public Vector3 Position;
         public int Color;
         public bool Configured;
         public BlipIcon Icon;
         public string Name;
         public bool Route;
         public bool RouteApplied;
      }
      private SavedWaypoint marker = null;
      private float arrivalRadius = 15.0f;
      private bool removeOnArrival = true;
      private bool routeEnabled = true;
      private bool splitRoute = true;
      private bool rainbowMode = false;
      private int rainbowCycleMs = 2500;
      private BlipIcon waypointIcon = BlipIcon.Misc_Destination;
      private BlipIcon iconGTAIV;
      private BlipIcon iconTLAD;
      private BlipIcon iconTBOGT;
      private string colorPreset = "Episode";
      private float selectionRadius = 75.0f;
      private string logPath;
      private string persistPath;
      private bool persistPerEpisode = true;
      private bool persist = false;
      private bool restored = false;
      private bool dirty = false;
      private int playerSeenTick = 0;
      private int retryAfterTick = 0;
      private string waypointName = "Waypoint";
      private int lastSettingsCheck = 0;
      private DateTime settingsWriteTime;
      private int lastErrorTick = -10000;

      public EpisodeWaypointColor() {
         logPath = Path.ChangeExtension(Settings.Filename, ".log");
         string oldPersistPath = Path.ChangeExtension(Settings.Filename, ".waypoints");
         persistPath = GetPersistPath();
         try {
            if (File.Exists(oldPersistPath) && !File.Exists(persistPath)) File.Move(oldPersistPath, persistPath);
         } catch (IOException) {
         } catch (UnauthorizedAccessException) {
         }
         LoadSettings();
         // the waypoint blip is recreated whenever the player sets a new one, so poll
         this.Tick += new EventHandler(this.EpisodeWaypointColor_Tick);
      }

      private bool IsSectionHeader(string line, string sectionName) {
         if (line == null) return false;
         string t = line.Trim();
         if (!t.StartsWith("[") || !t.EndsWith("]")) return false;
         string found = t.Substring(1, t.Length - 2).Trim();
         return string.Compare(found, sectionName, true, CultureInfo.InvariantCulture) == 0;
      }

      private bool IsAnySectionHeader(string line) {
         if (line == null) return false;
         string t = line.Trim();
         return t.StartsWith("[") && t.EndsWith("]");
      }

      private bool IniHasKey(List<string> lines, string sectionName, string keyName) {
         bool inSection = false;
         int i;
         for (i = 0; i < lines.Count; i++) {
            string line = lines[i];
            if (IsAnySectionHeader(line)) {
               inSection = IsSectionHeader(line, sectionName);
               continue;
            }
            if (!inSection) continue;
            if (line == null) continue;
            string t = line.Trim();
            if (t.Length == 0 || t.StartsWith(";") || t.StartsWith("#")) continue;
            int eq = t.IndexOf('=');
            if (eq <= 0) continue;
            string key = t.Substring(0, eq).Trim();
            if (string.Compare(key, keyName, true, CultureInfo.InvariantCulture) == 0) return true;
         }
         return false;
      }

      private int FindSectionLine(List<string> lines, string sectionName) {
         int i;
         for (i = 0; i < lines.Count; i++) {
            if (IsSectionHeader(lines[i], sectionName)) return i;
         }
         return -1;
      }

      private bool EnsureIniKey(List<string> lines, string section, string key, string value, string comment) {
         if (IniHasKey(lines, section, key)) return false;
         int sectionLine = FindSectionLine(lines, section);
         if (sectionLine < 0) {
            if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0) lines.Add("");
            lines.Add("[" + section + "]");
            if (comment != null && comment.Length > 0) lines.Add(comment);
            lines.Add(key + "=" + value);
            return true;
         }
         int insertAt = sectionLine + 1;
         while (insertAt < lines.Count && !IsAnySectionHeader(lines[insertAt])) insertAt++;
         if (comment != null && comment.Length > 0) lines.Insert(insertAt++, comment);
         lines.Insert(insertAt, key + "=" + value);
         return true;
      }

      private void MergeMissingKeys() {
         string path = Settings.Filename;
         List<string> lines = new List<string>();
         if (File.Exists(path)) {
            lines.AddRange(File.ReadAllLines(path));
         } else {
            lines.Add("; EpisodeWaypointColor settings");
            lines.Add("; This file is reloaded during play, and missing keys are appended automatically.");
         }

         bool changed = false;
         changed |= EnsureIniKey(lines, "General", "UpdateIntervalMs", "50", "; Tick interval in milliseconds (10-1000).");
         changed |= EnsureIniKey(lines, "General", "Debug", "false", "; Print debug messages to the in-game console.");
         changed |= EnsureIniKey(lines, "General", "ArrivalRadius", "15", "; Remove fake waypoint when within this horizontal distance in meters (1-100).");
         changed |= EnsureIniKey(lines, "General", "RemoveOnArrival", "true", "; If false, the waypoint remains until selected again or removed by the engine.");
         changed |= EnsureIniKey(lines, "General", "RouteEnabled", "true", "; If true, enables GPS route on the fake waypoint.");
         changed |= EnsureIniKey(lines, "General", "SplitRouteBlip", "true", "; If true, the GPS route is carried by a separate invisible blip, because the game draws a routed blip's icon in its fixed colour.");
         changed |= EnsureIniKey(lines, "General", "RainbowMode", "false", "; If true, colors cycle continuously instead of using per-episode colors.");
         changed |= EnsureIniKey(lines, "General", "RainbowCycleMs", "2500", "; Full rainbow cycle duration in milliseconds (250-60000).");
         changed |= EnsureIniKey(lines, "General", "WaypointIcon", "Misc_Destination", "; Any BlipIcon enum name. Misc_Waypoint is drawn by the game in its own colour, so it ignores the colours below.");
         changed |= EnsureIniKey(lines, "General", "WaypointName", "Waypoint", "; Label shown on map/legend.");
         changed |= EnsureIniKey(lines, "General", "Persist", "false", "; If true, the waypoint is saved to EpisodeWaypointColor_Waypoints.txt and restored on the next launch or script reload.");
         changed |= EnsureIniKey(lines, "General", "PersistPerEpisode", "true", "; If true, each episode keeps its own saved waypoint; if false, one shared file is used.");
         changed |= EnsureIniKey(lines, "General", "SelectionRadius", "75", "; Placing a waypoint within this many horizontal meters of the current one removes it instead (1-250). Map clicks are imprecise, so keep this generous.");
         changed |= EnsureIniKey(lines, "Icons", "GTAIV", "Default", "; BlipIcon enum name, or Default to use General/WaypointIcon.");
         changed |= EnsureIniKey(lines, "Icons", "TLAD", "Default", null);
         changed |= EnsureIniKey(lines, "Icons", "TBOGT", "Default", null);
         changed |= EnsureIniKey(lines, "Colors", "Preset", "Episode", "; Episode (your colors below), Original, HighContrast, or Rainbow.");
         changed |= EnsureIniKey(lines, "Colors", "GTAIV", DefaultGTAIV, "; Episode color (RRGGBBAA hex or short preset index).");
         changed |= EnsureIniKey(lines, "Colors", "TLAD", DefaultTLAD, null);
         changed |= EnsureIniKey(lines, "Colors", "TBOGT", DefaultTBOGT, null);

         if (changed) File.WriteAllLines(path, lines.ToArray());
      }

      // Creates EpisodeWaypointColor.ini next to the script on first launch.
      private void LoadSettings() {
         bool wasPersisting = persist;
         try {
            settingsWriteTime = File.GetLastWriteTimeUtc(Settings.Filename);
            MergeMissingKeys();
            Settings.Load();
            settingsWriteTime = File.GetLastWriteTimeUtc(Settings.Filename);
         } catch (Exception ex) {
            Game.Console.Print("EpisodeWaypointColor could not create ini: " + ex.Message);
         }
         colorGTAIV = ReadColor("GTAIV", DefaultGTAIV);
         colorTLAD = ReadColor("TLAD", DefaultTLAD);
         colorTBOGT = ReadColor("TBOGT", DefaultTBOGT);
         debug = Settings.GetValueBool("Debug", "General", false);
         this.Interval = Math.Max(10, Math.Min(1000, Settings.GetValueInteger("UpdateIntervalMs", "General", 50)));
         arrivalRadius = Settings.GetValueFloat("ArrivalRadius", "General", 15.0f);
         removeOnArrival = Settings.GetValueBool("RemoveOnArrival", "General", true);
         routeEnabled = Settings.GetValueBool("RouteEnabled", "General", true);
         bool wasSplit = splitRoute;
         splitRoute = Settings.GetValueBool("SplitRouteBlip", "General", true);
         if (marker != null && splitRoute != wasSplit) marker.RouteApplied = false;
         rainbowMode = Settings.GetValueBool("RainbowMode", "General", false);
         rainbowCycleMs = Settings.GetValueInteger("RainbowCycleMs", "General", 2500);
         waypointName = Settings.GetValueString("WaypointName", "General", "Waypoint");
         if (waypointName == null || waypointName.Trim().Length == 0) waypointName = "Waypoint";
         waypointIcon = ReadIcon("General", "WaypointIcon", BlipIcon.Misc_Destination);
         iconGTAIV = ReadIcon("Icons", "GTAIV", waypointIcon);
         iconTLAD = ReadIcon("Icons", "TLAD", waypointIcon);
         iconTBOGT = ReadIcon("Icons", "TBOGT", waypointIcon);
         colorPreset = Settings.GetValueString("Preset", "Colors", "Episode").Trim();
         if (!PresetIs("Episode") && !PresetIs("Original") && !PresetIs("HighContrast") && !PresetIs("Rainbow")) {
            Game.Console.Print("EpisodeWaypointColor: invalid Preset; using Episode");
            colorPreset = "Episode";
         }
         selectionRadius = ReadRange("General", "SelectionRadius", 75.0f, 1.0f, 250.0f);
         persist = Settings.GetValueBool("Persist", "General", false);
         bool wasPerEpisode = persistPerEpisode;
         persistPerEpisode = Settings.GetValueBool("PersistPerEpisode", "General", true);
         if (persistPerEpisode != wasPerEpisode && restored) dirty = true;
         // turning persistence on mid-session saves the current markers right away
         if (persist && !wasPersisting && restored) dirty = true;
         if (marker != null) marker.Configured = false;
         if (float.IsNaN(arrivalRadius) || float.IsInfinity(arrivalRadius) || arrivalRadius < 1.0f || arrivalRadius > 100.0f) {
            Game.Console.Print("EpisodeWaypointColor: ArrivalRadius must be 1-100 metres; using 15");
            arrivalRadius = 15.0f;
         }
         if (rainbowCycleMs < 250 || rainbowCycleMs > 60000) {
            Game.Console.Print("EpisodeWaypointColor: RainbowCycleMs must be 250-60000; using 2500");
            rainbowCycleMs = 2500;
         }
      }

      private float ReadRange(string section, string key, float fallback, float min, float max) {
         float value = Settings.GetValueFloat(key, section, fallback);
         if (Finite(value) && value >= min && value <= max) return value;
         Game.Console.Print("EpisodeWaypointColor: invalid " + key + "; using " + fallback);
         return fallback;
      }

      private BlipIcon ReadIcon(string section, string key, BlipIcon fallback) {
         string iconName = Settings.GetValueString(key, section, "Default").Trim();
         if (string.Compare(iconName, "Default", true, CultureInfo.InvariantCulture) == 0) return fallback;
         try {
            BlipIcon icon = (BlipIcon)Enum.Parse(typeof(BlipIcon), iconName, true);
            if (Enum.IsDefined(typeof(BlipIcon), icon)) return icon;
         } catch (ArgumentException) {
         } catch (OverflowException) {
         }
         Game.Console.Print("EpisodeWaypointColor: invalid icon '" + iconName + "' for " + section + "/" + key + "; using " + fallback);
         return fallback;
      }

      private void ReloadSettings() {
         int now = Environment.TickCount;
         if (now - lastSettingsCheck < 1000) return;
         lastSettingsCheck = now;
         DateTime writeTime = File.GetLastWriteTimeUtc(Settings.Filename);
         if (writeTime == settingsWriteTime) return;
         LoadSettings();
         Debug("settings reloaded");
      }

      private int ReadColor(string name, string defaultHex) {
         string hex = Settings.GetValueString(name, "Colors", defaultHex);
         if (hex == null) hex = defaultHex;
         hex = hex.Trim();
         if (hex.StartsWith("0x") || hex.StartsWith("0X")) hex = hex.Substring(2);
         int value;
         if (hex.Length == 8 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)) {
            return value;
         }
         // a short number (0-14) is treated as a preset BlipColor index
         if (hex.Length <= 2 && int.TryParse(hex, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 0 && value <= 14) {
            return value;
         }
         Game.Console.Print("EpisodeWaypointColor: invalid color '" + hex + "' for " + name + ", using default");
         return int.Parse(defaultHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
      }

      private int GetEpisodeColor() {
         if (rainbowMode || PresetIs("Rainbow")) return GetRainbowColor(Environment.TickCount);
         if (PresetIs("Original")) return 8;
         if (PresetIs("HighContrast")) return PackRgba(255, 255, 0, 255);
         switch (Game.CurrentEpisode) {
            case GameEpisode.TLAD:
               return colorTLAD;
            case GameEpisode.TBOGT:
               return colorTBOGT;
            default:
               return colorGTAIV;
         }
      }

      private void Debug(string text) {
         if (!debug) return;
         Game.Console.Print("EpisodeWaypointColor: " + text);
         try { File.AppendAllText(logPath, DateTime.Now.ToString("s") + " " + text + Environment.NewLine); }
         catch (IOException ex) { Game.Console.Print("EpisodeWaypointColor could not write log: " + ex.Message); }
         catch (UnauthorizedAccessException ex) { Game.Console.Print("EpisodeWaypointColor could not write log: " + ex.Message); }
      }

      private bool PresetIs(string name) {
         return string.Compare(colorPreset, name, true, CultureInfo.InvariantCulture) == 0;
      }

      private BlipIcon GetEpisodeIcon() {
         switch (Game.CurrentEpisode) {
            case GameEpisode.TLAD: return iconTLAD;
            case GameEpisode.TBOGT: return iconTBOGT;
            default: return iconGTAIV;
         }
      }

      private int ClampByte(int v) {
         if (v < 0) return 0;
         if (v > 255) return 255;
         return v;
      }

      private int PackRgba(int r, int g, int b, int a) {
         return ((ClampByte(r) & 255) << 24) | ((ClampByte(g) & 255) << 16) | ((ClampByte(b) & 255) << 8) | (ClampByte(a) & 255);
      }

      private int GetRainbowColor(int nowTick) {
         int phaseTick = nowTick % rainbowCycleMs;
         if (phaseTick < 0) phaseTick += rainbowCycleMs;
         return HsvColor(phaseTick / (float)rainbowCycleMs, 1.0f, 1.0f, 255);
      }

      private int HsvColor(float hue, float saturation, float value, int alpha) {
         hue -= (float)Math.Floor(hue);
         float h = hue * 6.0f;
         float x = 1.0f - Math.Abs(h % 2.0f - 1.0f);
         float r = 0.0f, g = 0.0f, b = 0.0f;

         if (h < 1.0f) { r = 1.0f; g = x; b = 0.0f; }
         else if (h < 2.0f) { r = x; g = 1.0f; b = 0.0f; }
         else if (h < 3.0f) { r = 0.0f; g = 1.0f; b = x; }
         else if (h < 4.0f) { r = 0.0f; g = x; b = 1.0f; }
         else if (h < 5.0f) { r = x; g = 0.0f; b = 1.0f; }
         else { r = 1.0f; g = 0.0f; b = x; }

         float chroma = value * saturation;
         float m = value - chroma;
         return PackRgba((int)((r * chroma + m) * 255.0f), (int)((g * chroma + m) * 255.0f), (int)((b * chroma + m) * 255.0f), alpha);
      }

      private void DeleteBlip(Blip blip) {
         int handle = ((GTA.@base.iHandleObject)blip).Handle;
         if (!GTA.Native.Function.Call<bool>("DOES_BLIP_EXIST", handle)) return;
         blip.RouteActive = false;
         blip.Delete();
         if (GTA.Native.Function.Call<bool>("DOES_BLIP_EXIST", handle))
            throw new InvalidOperationException("Blip deletion failed for handle " + handle);
      }

      private void DeleteNativeWaypoint(Blip blip) {
         // the engine keeps its own waypoint state, so clear it properly before falling back to blip removal
         GTA.Native.Function.Call("SWITCH_OFF_WAYPOINT");
         int handle = ((GTA.@base.iHandleObject)blip).Handle;
         if (!GTA.Native.Function.Call<bool>("DOES_BLIP_EXIST", handle)) {
            Debug("native waypoint " + handle + " cleared by SWITCH_OFF_WAYPOINT");
            return;
         }
         Debug("native waypoint " + handle + " survived SWITCH_OFF_WAYPOINT; removing blip directly");
         GTA.Native.Function.Call("SET_THIS_SCRIPT_CAN_REMOVE_BLIPS_CREATED_BY_ANY_SCRIPT", true);
         try { DeleteBlip(blip); }
         finally { GTA.Native.Function.Call("SET_THIS_SCRIPT_CAN_REMOVE_BLIPS_CREATED_BY_ANY_SCRIPT", false); }
      }

      private void RemoveMarker(string reason) {
         SavedWaypoint old = marker;
         marker = null;
         dirty = true;
         try { if (old.RouteBlip != null) DeleteBlip(old.RouteBlip); }
         finally { DeleteBlip(old.Blip); }
         Debug("removed waypoint: " + reason);
      }

      private bool IsOwned(Blip blip) {
         return marker != null && (marker.Blip.Equals(blip) || (marker.RouteBlip != null && marker.RouteBlip.Equals(blip)));
      }

      private Blip FindNativeWaypoint() {
         // Game.GetWaypoint() also matches our marker if it uses the waypoint icon; exclude it explicitly.
         for (int type = 0; type < 10; type++) {
            Blip[] blips = Blip.GetAllBlipsOfType((BlipType)type);
            for (int i = 0; i < blips.Length; i++) {
               Blip blip = blips[i];
               if (blip.Exists() && blip.Icon == BlipIcon.Misc_Waypoint &&
                   !IsOwned(blip)) return blip;
            }
         }
         return null;
      }

      private static bool Finite(float v) {
         return !float.IsNaN(v) && !float.IsInfinity(v);
      }

      private void ReplaceWaypoint(Blip waypoint, int color) {
         Vector3 position = waypoint.Position;
         if (!Finite(position.X) || !Finite(position.Y) || !Finite(position.Z))
            throw new InvalidOperationException("Waypoint has invalid coordinates");

         if (marker != null) {
            float distance = (float)Math.Sqrt(HorizontalDistanceSquared(marker.Position, position));
            Debug("native waypoint at " + position + " is " + distance.ToString("0.0") + " m from the current one (SelectionRadius " + selectionRadius + ")");
            // placing a waypoint on (or near) the current one clears it, like the game's own toggle
            if (distance <= selectionRadius) {
               DeleteNativeWaypoint(waypoint);
               RemoveMarker("map selection");
               return;
            }
         }

         CreateMarker(position, color, waypoint);
         Debug("placed waypoint at " + position + "; episode=" + Game.CurrentEpisode + "; preset=" + colorPreset);
      }

      // The routed blip's icon ignores custom colours, so the route goes on a second, zero-size blip
      // with the same colour (the route line takes its colour) and the visible icon has no route.
      private Blip CreateRouteBlip(Vector3 position, int color) {
         Blip route = Blip.AddBlip(position);
         if (route == null || !route.Exists()) throw new InvalidOperationException("Could not create route blip");
         try {
            route.Icon = BlipIcon.Misc_Destination;
            route.Name = waypointName;
            route.Display = BlipDisplay.ArrowAndMap;
            route.Scale = 0.0f;
            route.Color = (BlipColor)color;
            route.RouteActive = true;
         } catch (Exception) {
            try { DeleteBlip(route); } catch (Exception) { }
            throw;
         }
         return route;
      }

      // Applies the route to either the visible blip or a separate route blip, per the settings.
      private void ApplyRoute(SavedWaypoint w, int color) {
         bool wantRouteBlip = routeEnabled && splitRoute;
         w.Blip.RouteActive = routeEnabled && !splitRoute;
         if (wantRouteBlip && w.RouteBlip == null) w.RouteBlip = CreateRouteBlip(w.Position, color);
         if (!wantRouteBlip && w.RouteBlip != null) {
            Blip old = w.RouteBlip;
            w.RouteBlip = null;
            DeleteBlip(old);
         }
      }

      // Creates our marker at position, replacing any previous one. When nativeWaypoint is given it
      // is deleted after the new marker is configured, so a failure never loses the destination.
      private void CreateMarker(Vector3 position, int color, Blip nativeWaypoint) {
         Blip copy = Blip.AddBlip(position);
         if (copy == null || !copy.Exists())
            throw new InvalidOperationException("Could not create replacement waypoint; original retained");
         BlipIcon icon = GetEpisodeIcon();
         Blip routeBlip = null;
         try {
            // final look and route are applied straight away, so the GPS starts calculating at once
            copy.Icon = icon;
            copy.Name = waypointName;
            copy.Display = BlipDisplay.ArrowAndMap;
            copy.Color = (BlipColor)color;
            if (routeEnabled && splitRoute) routeBlip = CreateRouteBlip(position, color);
            else copy.RouteActive = routeEnabled;
            if (nativeWaypoint != null) DeleteNativeWaypoint(nativeWaypoint);
         } catch (Exception) {
            try { if (routeBlip != null) DeleteBlip(routeBlip); } catch (Exception) { }
            try { DeleteBlip(copy); } catch (Exception ex) {
               Game.Console.Print("EpisodeWaypointColor copy cleanup failed: " + ex.Message);
            }
            throw;
         }
         if (marker != null) {
            try { RemoveMarker("replaced"); } catch (Exception ex) {
               Game.Console.Print("EpisodeWaypointColor could not remove the old waypoint: " + ex.Message);
            }
         }
         SavedWaypoint saved = new SavedWaypoint();
         saved.Blip = copy;
         saved.RouteBlip = routeBlip;
         saved.Position = position;
         saved.Color = color;
         saved.Icon = icon;
         saved.Name = waypointName;
         saved.Route = routeEnabled;
         saved.RouteApplied = true;
         saved.Configured = true;
         marker = saved;
         dirty = true;
         Debug("configured waypoint; icon=" + icon + "; color=" + color.ToString("X8") + "; route=" + routeEnabled + "; split=" + (routeBlip != null));
      }

      private string GetPersistPath() {
         string name = "EpisodeWaypointColor_Waypoints";
         if (persistPerEpisode) name += "_" + Game.CurrentEpisode.ToString();
         return Path.Combine(Path.GetDirectoryName(Settings.Filename), name + ".txt");
      }

      // A single "x y z" line, or no file when there is no waypoint.
      private void SaveWaypoints() {
         dirty = false;
         if (!persist) return;
         try {
            persistPath = GetPersistPath();
            if (marker == null) {
               if (File.Exists(persistPath)) File.Delete(persistPath);
               Debug("saved: no waypoint");
            } else {
               Vector3 v = marker.Position;
               File.WriteAllText(persistPath, v.X.ToString("R", CultureInfo.InvariantCulture) + " " +
                  v.Y.ToString("R", CultureInfo.InvariantCulture) + " " +
                  v.Z.ToString("R", CultureInfo.InvariantCulture) + Environment.NewLine);
               Debug("saved waypoint");
            }
         } catch (Exception ex) {
            Game.Console.Print("EpisodeWaypointColor could not save waypoints: " + ex.Message);
         }
      }

      private void RestoreWaypoints(int color) {
         restored = true;
         persistPath = GetPersistPath();
         if (!persist || marker != null || !File.Exists(persistPath)) return;
         string[] lines = File.ReadAllLines(persistPath);
         bool done = false;
         // older files may hold several waypoints; only the last one is restored
         for (int i = lines.Length - 1; i >= 0 && !done; i--) {
            string[] parts = lines[i].Trim().Split(' ');
            if (parts.Length != 3) continue;
            float x, y, z;
            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) ||
                !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z)) continue;
            if (!Finite(x) || !Finite(y) || !Finite(z)) continue;
            done = true;
            try {
               CreateMarker(new Vector3(x, y, z), color, null);
               Debug("restored waypoint");
            } catch (Exception ex) {
               Game.Console.Print("EpisodeWaypointColor could not restore the waypoint: " + ex.Message);
            }
         }
         dirty = false;
      }

      private float HorizontalDistanceSquared(Vector3 a, Vector3 b) {
         float dx = a.X - b.X;
         float dy = a.Y - b.Y;
         return dx * dx + dy * dy;
      }

      private void EpisodeWaypointColor_Tick(object sender, EventArgs e) {
         try {
            ReloadSettings();
            if (marker != null && marker.RouteBlip != null && !marker.RouteBlip.Exists()) {
               // the engine dropped only the route carrier; recreate it below
               marker.RouteBlip = null;
               marker.RouteApplied = false;
            }
            if (marker != null && !marker.Blip.Exists()) {
               if (marker.RouteBlip != null) {
                  try { DeleteBlip(marker.RouteBlip); } catch (Exception) { }
               }
               marker = null;
               Debug("engine removed the waypoint");
            }
            Blip waypoint = FindNativeWaypoint();
            int color = GetEpisodeColor();
            if (!restored) {
               Ped p = Player.Character;
               if (p == null || !p.Exists() || Game.Paused) playerSeenTick = 0;
               else if (playerSeenTick == 0) playerSeenTick = Environment.TickCount;
               // wait for the game to settle after loading, or the engine may clear restored blips
               else if (Environment.TickCount - playerSeenTick > 3000) RestoreWaypoints(color);
            }
            // after a failure, back off so a persistent error cannot create and delete blips every tick
            if (waypoint != null && Environment.TickCount - retryAfterTick >= 0) {
               try {
                  ReplaceWaypoint(waypoint, color);
               } catch (Exception) {
                  retryAfterTick = Environment.TickCount + 2000;
                  throw;
               }
            }
            Ped player = Player.Character;
            if (marker != null && removeOnArrival && !Game.Paused && player != null && player.Exists() &&
                HorizontalDistanceSquared(marker.Position, player.Position) <= arrivalRadius * arrivalRadius)
               RemoveMarker("arrival");
            if (marker != null) {
               BlipIcon icon = GetEpisodeIcon();
               if (!marker.Configured || marker.Icon != icon) marker.Blip.Icon = icon;
               if (!marker.Configured || marker.Name != waypointName) marker.Blip.Name = waypointName;
               if (!marker.Configured || marker.Color != color) {
                  marker.Blip.Color = (BlipColor)color;
                  if (marker.RouteBlip != null) marker.RouteBlip.Color = (BlipColor)color;
               }
               // re-sending SET_ROUTE can restart the GPS calculation, so only send real changes
               if (!marker.RouteApplied || marker.Route != routeEnabled) {
                  ApplyRoute(marker, color);
                  marker.RouteApplied = true;
               }
               if (!marker.Configured) Debug("configured waypoint; icon=" + icon + "; color=" + color.ToString("X8") + "; route=" + routeEnabled);
               marker.Color = color;
               marker.Icon = icon;
               marker.Name = waypointName;
               marker.Route = routeEnabled;
               marker.Configured = true;
            }
            if (dirty) SaveWaypoints();
         } catch (Exception ex) {
            if (Environment.TickCount - lastErrorTick >= 5000) {
               lastErrorTick = Environment.TickCount;
               Game.Console.Print("EpisodeWaypointColor error: " + ex.Message);
               Debug("ERROR " + ex.ToString());
            }
         }
      }
   }
}
