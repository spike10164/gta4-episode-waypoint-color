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
         public Vector3 Position;
         public int Color;
         public bool Configured;
         public BlipIcon Icon;
         public string Name;
         public bool Route;
      }
      private List<SavedWaypoint> waypoints = new List<SavedWaypoint>();
      private float arrivalRadius = 15.0f;
      private bool removeOnArrival = true;
      private bool routeEnabled = true;
      private bool rainbowMode = false;
      private int rainbowCycleMs = 2500;
      private BlipIcon waypointIcon = BlipIcon.Misc_Waypoint;
      private BlipIcon iconGTAIV;
      private BlipIcon iconTLAD;
      private BlipIcon iconTBOGT;
      private string colorPreset = "Episode";
      private float hueStepDegrees = 18.0f;
      private float selectionRadius = 10.0f;
      private string logPath;
      private string persistPath;
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
         persistPath = Path.Combine(Path.GetDirectoryName(Settings.Filename), "EpisodeWaypointColor_Waypoints.txt");
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
         changed |= EnsureIniKey(lines, "General", "RemoveOnArrival", "true", "; If false, waypoints remain until selected again or removed by the engine.");
         changed |= EnsureIniKey(lines, "General", "RouteEnabled", "true", "; If true, enables GPS route on the fake waypoint.");
         changed |= EnsureIniKey(lines, "General", "RainbowMode", "false", "; If true, colors cycle continuously instead of using per-episode colors.");
         changed |= EnsureIniKey(lines, "General", "RainbowCycleMs", "2500", "; Full rainbow cycle duration in milliseconds (250-60000).");
         changed |= EnsureIniKey(lines, "General", "WaypointIcon", "Misc_Waypoint", "; Any BlipIcon enum name.");
         changed |= EnsureIniKey(lines, "General", "WaypointName", "Waypoint", "; Label shown on map/legend.");
         changed |= EnsureIniKey(lines, "General", "Persist", "false", "; If true, waypoints are saved to EpisodeWaypointColor_Waypoints.txt and restored on the next launch or script reload.");
         changed |= EnsureIniKey(lines, "General", "SelectionRadius", "10", "; Clicking within this many horizontal meters of a saved waypoint removes it (1-100).");
         changed |= EnsureIniKey(lines, "Icons", "GTAIV", "Default", "; BlipIcon enum name, or Default to use General/WaypointIcon.");
         changed |= EnsureIniKey(lines, "Icons", "TLAD", "Default", null);
         changed |= EnsureIniKey(lines, "Icons", "TBOGT", "Default", null);
         changed |= EnsureIniKey(lines, "Colors", "Preset", "Episode", "; Episode (your colors below), Original, HighContrast, or Rainbow.");
         changed |= EnsureIniKey(lines, "Colors", "HueStepDegrees", "18", "; Hue difference for each later waypoint (1-60). Achromatic base colors also gain a tint.");
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
         rainbowMode = Settings.GetValueBool("RainbowMode", "General", false);
         rainbowCycleMs = Settings.GetValueInteger("RainbowCycleMs", "General", 2500);
         waypointName = Settings.GetValueString("WaypointName", "General", "Waypoint");
         if (waypointName == null || waypointName.Trim().Length == 0) waypointName = "Waypoint";
         waypointIcon = ReadIcon("General", "WaypointIcon", BlipIcon.Misc_Waypoint);
         iconGTAIV = ReadIcon("Icons", "GTAIV", waypointIcon);
         iconTLAD = ReadIcon("Icons", "TLAD", waypointIcon);
         iconTBOGT = ReadIcon("Icons", "TBOGT", waypointIcon);
         colorPreset = Settings.GetValueString("Preset", "Colors", "Episode").Trim();
         if (!PresetIs("Episode") && !PresetIs("Original") && !PresetIs("HighContrast") && !PresetIs("Rainbow")) {
            Game.Console.Print("EpisodeWaypointColor: invalid Preset; using Episode");
            colorPreset = "Episode";
         }
         hueStepDegrees = ReadRange("Colors", "HueStepDegrees", 18.0f, 1.0f, 60.0f);
         selectionRadius = ReadRange("General", "SelectionRadius", 10.0f, 1.0f, 100.0f);
         persist = Settings.GetValueBool("Persist", "General", false);
         // turning persistence on mid-session saves the current markers right away
         if (persist && !wasPersisting && restored) dirty = true;
         for (int i = 0; i < waypoints.Count; i++) waypoints[i].Configured = false;
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

      private int VariantColor(int baseColor, int index) {
         if (index == 0) return baseColor;
         // Preset indices are not packed RGB; use approximate palette colors for variants only.
         int[] palette = new int[] {
            PackRgba(255,255,255,255), PackRgba(128,0,0,255), PackRgba(0,96,0,255),
            PackRgba(0,255,255,255), PackRgba(128,128,128,255), PackRgba(255,255,0,255),
            PackRgba(255,128,0,255), PackRgba(128,0,255,255), PackRgba(0,255,0,255),
            PackRgba(255,0,0,255), PackRgba(255,128,128,255), PackRgba(255,192,128,255),
            PackRgba(0,96,96,255), PackRgba(0,192,192,255), PackRgba(255,255,128,255)
         };
         int rgba = baseColor >= 0 && baseColor <= 14 ? palette[baseColor] : baseColor;
         float r = ((rgba >> 24) & 255) / 255.0f;
         float g = ((rgba >> 16) & 255) / 255.0f;
         float b = ((rgba >> 8) & 255) / 255.0f;
         float max = Math.Max(r, Math.Max(g, b));
         float min = Math.Min(r, Math.Min(g, b));
         float delta = max - min;
         float hue = 0.0f;
         if (delta > 0.0001f) {
            if (max == r) hue = (g - b) / delta;
            else if (max == g) hue = (b - r) / delta + 2.0f;
            else hue = (r - g) / delta + 4.0f;
            hue /= 6.0f;
         }
         float saturation = max > 0.0f ? delta / max : 0.0f;
         if (delta < 0.0001f) {
            saturation = 0.35f;
            max = Math.Max(max, 0.35f);
         }
         // Small alternating offsets, then further shifts as the list grows.
         int offset = (index + 1) / 2;
         if (index % 2 == 0) offset = -offset;
         return HsvColor(hue + offset * hueStepDegrees / 360.0f, saturation, max, rgba & 255);
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
         if (!GTA.Native.Function.Call<bool>("DOES_BLIP_EXIST", handle)) return;
         GTA.Native.Function.Call("SET_THIS_SCRIPT_CAN_REMOVE_BLIPS_CREATED_BY_ANY_SCRIPT", true);
         try { DeleteBlip(blip); }
         finally { GTA.Native.Function.Call("SET_THIS_SCRIPT_CAN_REMOVE_BLIPS_CREATED_BY_ANY_SCRIPT", false); }
      }

      private void RemoveWaypoint(int index, string reason) {
         DeleteBlip(waypoints[index].Blip);
         waypoints.RemoveAt(index);
         RefreshWaypoints();
         dirty = true;
         Debug("removed waypoint " + (index + 1) + ": " + reason + "; remaining=" + waypoints.Count);
      }

      private void RefreshWaypoints() {
         for (int i = 0; i < waypoints.Count; i++) waypoints[i].Configured = false;
      }

      private bool IsOwned(Blip blip) {
         for (int i = 0; i < waypoints.Count; i++)
            if (waypoints[i].Blip.Equals(blip)) return true;
         return false;
      }

      private Blip FindNativeWaypoint() {
         // Game.GetWaypoint() also matches our copy; exclude it explicitly.
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

         int selected = -1;
         float nearest = selectionRadius * selectionRadius;
         for (int i = 0; i < waypoints.Count; i++) {
            float distance = HorizontalDistanceSquared(waypoints[i].Position, position);
            if (distance <= nearest) {
               nearest = distance;
               selected = i;
            }
         }
         if (selected >= 0) {
            DeleteNativeWaypoint(waypoint);
            RemoveWaypoint(selected, "map selection");
            return;
         }

         CreateMarker(position, color, waypoint);
         Debug("added waypoint " + waypoints.Count + " at " + position + "; episode=" + Game.CurrentEpisode + "; preset=" + colorPreset);
      }

      // Creates our marker at position. When nativeWaypoint is given it is deleted after the copy
      // is configured, so a failure before that point never loses the destination.
      private void CreateMarker(Vector3 position, int color, Blip nativeWaypoint) {
         Blip copy = Blip.AddBlip(position);
         if (copy == null || !copy.Exists())
            throw new InvalidOperationException("Could not create replacement waypoint; original retained");
         try {
            // Avoid matching a native waypoint until ownership has been registered.
            copy.Icon = BlipIcon.Misc_Destination;
            copy.Name = waypointName;
            copy.Display = BlipDisplay.ArrowAndMap;
            copy.Color = (BlipColor)VariantColor(color, waypoints.Count);
            if (nativeWaypoint != null) DeleteNativeWaypoint(nativeWaypoint);
         } catch (Exception) {
            try { DeleteBlip(copy); } catch (Exception ex) {
               Game.Console.Print("EpisodeWaypointColor copy cleanup failed: " + ex.Message);
            }
            throw;
         }
         SavedWaypoint saved = new SavedWaypoint();
         saved.Blip = copy;
         saved.Position = position;
         waypoints.Add(saved);
         RefreshWaypoints();
         dirty = true;
      }

      // One "x y z" line per waypoint, in placement order, so colors restore in the same order.
      private void SaveWaypoints() {
         dirty = false;
         if (!persist) return;
         try {
            List<string> lines = new List<string>();
            for (int i = 0; i < waypoints.Count; i++) {
               Vector3 v = waypoints[i].Position;
               lines.Add(v.X.ToString("R", CultureInfo.InvariantCulture) + " " +
                  v.Y.ToString("R", CultureInfo.InvariantCulture) + " " +
                  v.Z.ToString("R", CultureInfo.InvariantCulture));
            }
            if (lines.Count == 0) {
               if (File.Exists(persistPath)) File.Delete(persistPath);
            } else {
               File.WriteAllLines(persistPath, lines.ToArray());
            }
            Debug("saved " + lines.Count + " waypoint(s)");
         } catch (Exception ex) {
            Game.Console.Print("EpisodeWaypointColor could not save waypoints: " + ex.Message);
         }
      }

      private void RestoreWaypoints(int color) {
         restored = true;
         if (!persist || !File.Exists(persistPath)) return;
         string[] lines = File.ReadAllLines(persistPath);
         int count = 0;
         for (int i = 0; i < lines.Length; i++) {
            string[] parts = lines[i].Trim().Split(' ');
            if (parts.Length != 3) continue;
            float x, y, z;
            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) ||
                !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z)) continue;
            if (!Finite(x) || !Finite(y) || !Finite(z)) continue;
            try {
               CreateMarker(new Vector3(x, y, z), color, null);
               count++;
            } catch (Exception ex) {
               Game.Console.Print("EpisodeWaypointColor could not restore a waypoint: " + ex.Message);
            }
         }
         dirty = false;
         Debug("restored " + count + " waypoint(s)");
      }

      private float HorizontalDistanceSquared(Vector3 a, Vector3 b) {
         float dx = a.X - b.X;
         float dy = a.Y - b.Y;
         return dx * dx + dy * dy;
      }

      private void EpisodeWaypointColor_Tick(object sender, EventArgs e) {
         try {
            ReloadSettings();
            for (int i = waypoints.Count - 1; i >= 0; i--) {
               if (!waypoints[i].Blip.Exists()) {
                  waypoints.RemoveAt(i);
                  RefreshWaypoints();
                  Debug("engine removed waypoint " + (i + 1));
               }
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
            if (removeOnArrival && !Game.Paused && player != null && player.Exists()) {
               Vector3 location = player.Position;
               for (int i = waypoints.Count - 1; i >= 0; i--)
                  if (HorizontalDistanceSquared(waypoints[i].Position, location) <= arrivalRadius * arrivalRadius)
                     RemoveWaypoint(i, "arrival");
            }
            BlipIcon icon = GetEpisodeIcon();
            for (int i = 0; i < waypoints.Count; i++) {
               SavedWaypoint saved = waypoints[i];
               int variant = VariantColor(color, i);
               if (!saved.Configured || saved.Icon != icon) saved.Blip.Icon = icon;
               if (!saved.Configured || saved.Name != waypointName) saved.Blip.Name = waypointName;
               if (!saved.Configured || saved.Color != variant) saved.Blip.Color = (BlipColor)variant;
               if (!saved.Configured || saved.Route != routeEnabled) saved.Blip.RouteActive = routeEnabled;
               if (!saved.Configured) Debug("configured waypoint " + (i + 1) + "; icon=" + icon + "; color=" + variant.ToString("X8") + "; route=" + routeEnabled);
               saved.Color = variant;
               saved.Icon = icon;
               saved.Name = waypointName;
               saved.Route = routeEnabled;
               saved.Configured = true;
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
