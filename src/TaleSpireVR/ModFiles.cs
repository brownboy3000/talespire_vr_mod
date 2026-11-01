using System.IO;

namespace TaleSpireVR
{
    /// <summary>
    /// Where the mod keeps its files. Settings are one .ini file per kind of feature, directly in the game's VRMod folder
    /// (written with every option and its explanation on the first start, read once at startup):
    ///   TaleSpireVR.Core.ini          VR or normal mode at startup (the main menu button writes it), resolution, logging
    ///   TaleSpireVR.Controls.ini      the table view: scale, moving, zooming, turning, the laser pointer
    ///   TaleSpireVR.Menus.ini         the floating menus, the radial menu and the forearm dice
    ///   TaleSpireVR.Graphics.ini      draw distance, shadows, bloom, anti-aliasing
    ///   TaleSpireVR.StreamCamera.ini  the stream window and the held camera
    ///   TaleSpireVR.FirstPerson.ini   first-person mode, hands and weapons
    /// What you pick in game for each creature is not a setting: TaleSpireVR.Weapons.txt and TaleSpireVR.HandColors.txt
    /// stay in BepInEx\config\TaleSpireVR. TaleSpire's own files are never touched.
    /// </summary>
    internal static class ModFiles
    {
        public static string Dir => Path.Combine(BepInEx.Paths.GameRootPath, "VRMod");
        public static string CoreIni => Path.Combine(Dir, "TaleSpireVR.Core.ini");
        public static string ControlsIni => Path.Combine(Dir, "TaleSpireVR.Controls.ini");
        public static string MenusIni => Path.Combine(Dir, "TaleSpireVR.Menus.ini");
        public static string GraphicsIni => Path.Combine(Dir, "TaleSpireVR.Graphics.ini");
        public static string StreamCameraIni => Path.Combine(Dir, "TaleSpireVR.StreamCamera.ini");
        public static string FirstPersonIni => Path.Combine(Dir, "TaleSpireVR.FirstPerson.ini");

        private static string SavedDir => Path.Combine(BepInEx.Paths.ConfigPath, "TaleSpireVR");
        public static string Weapons => Path.Combine(SavedDir, "TaleSpireVR.Weapons.txt");
        public static string HandColors => Path.Combine(SavedDir, "TaleSpireVR.HandColors.txt");

        /// <summary>Once, before anything is read or saved.</summary>
        public static void Prepare()
        {
            try { Directory.CreateDirectory(SavedDir); }
            catch (System.Exception e) { Plugin.Log.LogWarning("Preparing the mod's folders failed: " + e.Message); }
        }
    }
}
