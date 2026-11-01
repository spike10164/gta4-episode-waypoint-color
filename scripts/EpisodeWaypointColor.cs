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
      private Blip lastBlip = null;
      private int lastColor = 0;

      public EpisodeWaypointColor() {
         LoadSettings();
         // the waypoint blip is recreated whenever the player sets a new one, so poll
         this.Interval = Settings.GetValueInteger("UpdateIntervalMs", "General", 500);
         this.Tick += new EventHandler(this.EpisodeWaypointColor_Tick);
      }

      // Creates EpisodeWaypointColor.ini next to the script on first launch.
      private void LoadSettings() {
         try {
            if (!File.Exists(Settings.Filename)) {
               Settings.SetValue("UpdateIntervalMs", "General", 500);
               Settings.SetValue("GTAIV", "Colors", DefaultGTAIV);
               Settings.SetValue("TLAD", "Colors", DefaultTLAD);
               Settings.SetValue("TBOGT", "Colors", DefaultTBOGT);
               Settings.Save();
            }
         } catch (Exception ex) {
            Game.Log("EpisodeWaypointColor could not create ini: " + ex.Message);
         }
         colorGTAIV = ReadColor("GTAIV", DefaultGTAIV);
         colorTLAD = ReadColor("TLAD", DefaultTLAD);
         colorTBOGT = ReadColor("TBOGT", DefaultTBOGT);
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
         Game.Log("EpisodeWaypointColor: invalid color '" + hex + "' for " + name + ", using default");
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

      private void EpisodeWaypointColor_Tick(object sender, EventArgs e) {
         try {
            Blip waypoint = Game.GetWaypoint();
            if (waypoint == null || !waypoint.Exists()) {
               lastBlip = null;
               return;
            }

            int color = GetEpisodeColor();
            if (lastBlip != null && lastBlip.Equals(waypoint) && color == lastColor) {
               return;
            }

            waypoint.Color = (BlipColor)color;
            lastBlip = waypoint;
            lastColor = color;
         } catch (Exception ex) {
            Game.Log("EpisodeWaypointColor error: " + ex.Message);
         }
      }
   }
}
