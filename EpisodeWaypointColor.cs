using System;
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
      private Blip ownBlip = null;
      private int ownColor = 0;
      private float arrivalRadius = 15.0f;
      private int lastSettingsCheck = 0;
      private DateTime settingsWriteTime;
      private int lastErrorTick = -10000;

      public EpisodeWaypointColor() {
         LoadSettings();
         // the waypoint blip is recreated whenever the player sets a new one, so poll
         this.Tick += new EventHandler(this.EpisodeWaypointColor_Tick);
      }

      private bool AddMissingSetting(string name, string section, string value) {
         if (Settings.GetValueString(name, section, "") != "") return false;
         Settings.SetValue(name, section, value);
         return true;
      }

      // Creates EpisodeWaypointColor.ini next to the script on first launch.
      private void LoadSettings() {
         try {
            bool changed = !File.Exists(Settings.Filename);
            changed |= AddMissingSetting("UpdateIntervalMs", "General", "50");
            changed |= AddMissingSetting("Debug", "General", "false");
            changed |= AddMissingSetting("ArrivalRadius", "General", "15");
            changed |= AddMissingSetting("GTAIV", "Colors", DefaultGTAIV);
            changed |= AddMissingSetting("TLAD", "Colors", DefaultTLAD);
            changed |= AddMissingSetting("TBOGT", "Colors", DefaultTBOGT);
            if (changed) Settings.Save();
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
         if (float.IsNaN(arrivalRadius) || float.IsInfinity(arrivalRadius) || arrivalRadius < 1.0f || arrivalRadius > 100.0f) {
            Game.Console.Print("EpisodeWaypointColor: ArrivalRadius must be 1-100 metres; using 15");
            arrivalRadius = 15.0f;
         }
      }

      private void ReloadSettings() {
         int now = Environment.TickCount;
         if (now - lastSettingsCheck < 1000) return;
         lastSettingsCheck = now;
         DateTime writeTime = File.GetLastWriteTimeUtc(Settings.Filename);
         if (writeTime == settingsWriteTime) return;
         Settings.Load();
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
         if (hex.Length <= 2 && int.TryParse(hex, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) {
            return value;
         }
         Game.Console.Print("EpisodeWaypointColor: invalid color '" + hex + "' for " + name + ", using default");
         return int.Parse(defaultHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
      }

      private int GetEpisodeColor() {
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
         if (debug) Game.Console.Print("EpisodeWaypointColor: " + text);
      }

      private void RemoveWaypoint() {
         if (ownBlip != null && ownBlip.Exists()) {
            ownBlip.RouteActive = false;
            ownBlip.Delete();
         }
         ownBlip = null;
      }

      private Blip FindNativeWaypoint() {
         // Game.GetWaypoint() also matches our copy; exclude it explicitly.
         for (int type = 0; type < 10; type++) {
            Blip[] blips = Blip.GetAllBlipsOfType((BlipType)type);
            for (int i = 0; i < blips.Length; i++) {
               Blip blip = blips[i];
               if (blip.Exists() && blip.Icon == BlipIcon.Misc_Waypoint &&
                   (ownBlip == null || !ownBlip.Equals(blip))) return blip;
            }
         }
         return null;
      }

      private void ApplyColor(int color) {
         ownBlip.Color = (BlipColor)color;
         ownBlip.RouteActive = true;
         ownColor = color;
      }

      private static bool Finite(float v) {
         return !float.IsNaN(v) && !float.IsInfinity(v);
      }

      // Replaces the engine waypoint with our own blip. Blip positions cannot be changed in this
      // API, so a moved waypoint means deleting ours and creating a new one. Nothing is thrown
      // while a half-built copy exists; any failure removes the copy so the native waypoint is kept.
      private void ReplaceWaypoint(Blip waypoint, int color) {
         Vector3 position = waypoint.Position;
         if (!Finite(position.X) || !Finite(position.Y) || !Finite(position.Z))
            throw new InvalidOperationException("Waypoint has invalid coordinates");

         if (ownBlip != null && ownBlip.Exists()) {
            Vector3 mine = ownBlip.Position;
            float dx = mine.X - position.X;
            float dy = mine.Y - position.Y;
            if (dx * dx + dy * dy < 1.0f) {
               // same spot: the engine marker is still (or again) there, just switch it off
               GTA.Native.Function.Call("SWITCH_OFF_WAYPOINT");
               return;
            }
            RemoveWaypoint();
         }

         Blip copy = Blip.AddBlip(position);
         if (copy == null || !copy.Exists())
            throw new InvalidOperationException("Could not create replacement waypoint; original retained");
         try {
            copy.Icon = BlipIcon.Misc_Waypoint;
            copy.Name = "Waypoint";
            copy.Display = BlipDisplay.ArrowAndMap;
            copy.Color = (BlipColor)color;
            copy.RouteActive = true;
         } catch (Exception) {
            try { copy.Delete(); } catch (Exception) { }
            throw;
         }
         ownBlip = copy;
         ownColor = color;
         GTA.Native.Function.Call("SWITCH_OFF_WAYPOINT");
         Debug("replacement placed, color " + color.ToString("X8"));
      }

      private void EpisodeWaypointColor_Tick(object sender, EventArgs e) {
         try {
            ReloadSettings();
            if (ownBlip != null && !ownBlip.Exists()) ownBlip = null;
            Blip waypoint = FindNativeWaypoint();
            int color = GetEpisodeColor();
            if (waypoint != null) {
               ReplaceWaypoint(waypoint, color);
            }
            if (ownBlip == null) return;
            if (color != ownColor) ApplyColor(color);
            if (Game.Paused) return;
            Ped player = Player.Character;
            if (player == null || !player.Exists()) return;
            Vector3 target = ownBlip.Position;
            Vector3 location = player.Position;
            float dx = target.X - location.X;
            float dy = target.Y - location.Y;
            // Waypoint Z can be map-derived rather than road height.
            if (dx * dx + dy * dy <= arrivalRadius * arrivalRadius) {
               RemoveWaypoint();
               Debug("arrived; waypoint removed");
            }
         } catch (Exception ex) {
            if (Environment.TickCount - lastErrorTick >= 5000) {
               lastErrorTick = Environment.TickCount;
               Game.Console.Print("EpisodeWaypointColor error: " + ex.Message);
            }
         }
      }
   }
}
