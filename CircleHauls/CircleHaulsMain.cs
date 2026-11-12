using System;
using System.Reflection;
using HarmonyLib;
using UnityModManagerNet;

namespace CircleHauls
{
    public static class CircleHaulsMain
    {
        public static UnityModManager.ModEntry Mod;
        public static Settings Settings;

        private static Harmony _harmony;

        // UMM-Einstieg (info.json: CircleHauls.CircleHaulsMain.Load)
        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Mod = modEntry;
            Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);
            Loc.LoadFrom(modEntry.Path);

            modEntry.OnGUI = entry => Settings.Draw();
            // Multiplayer erst einbinden, wenn alle Mods geladen sind (Ladereihenfolge nicht garantiert)
            modEntry.OnLateUpdate += LateInit;
            modEntry.OnSaveGUI = entry => Settings.Save(entry);

            // PatchAll genau EINMAL (Performance-Hinweis aus der Community)
            if (_harmony == null)
            {
                try
                {
                    _harmony = new Harmony(modEntry.Info.Id);
                    _harmony.PatchAll(Assembly.GetExecutingAssembly());
                    TooltipOverlay.Patch(_harmony);
                    // Debug: welche Spielmethoden wirklich gepatcht sind (Vanilla-Haken für die Umwandlung)
                    foreach (var m in _harmony.GetPatchedMethods())
                        Log($"Patch aktiv: {m.DeclaringType?.Name}.{m.Name}");
                }
                catch (Exception ex)
                {
                    Error("Harmony-Patches fehlgeschlagen", ex);
                    _harmony = null;
                    return false;
                }
            }

            Log("geladen");
            return true;
        }

        private static void LateInit(UnityModManager.ModEntry modEntry, float dt)
        {
            modEntry.OnLateUpdate -= LateInit;
            Multiplayer.MpCompat.Init(modEntry);
        }

        /// <summary>Nur bei aktiviertem Debug-Log – kein Log-Spam im Normalbetrieb.</summary>
        public static void Log(string msg)
        {
            if (Settings != null && Settings.DebugLog)
                Mod?.Logger.Log(msg);
        }

        public static void Warning(string msg) => Mod?.Logger.Warning(msg);

        public static void Error(string msg, Exception ex = null)
        {
            Mod?.Logger.Error(ex == null ? msg : $"{msg}: {ex}");
        }
    }
}
