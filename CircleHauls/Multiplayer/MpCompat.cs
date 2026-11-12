using System;
using System.IO;
using System.Reflection;
using CircleHauls.Jobs;
using UnityModManagerNet;

namespace CircleHauls.Multiplayer
{
    /// <summary>
    /// Weiche zur Multiplayer-Mod (AMacro dv-multiplayer).
    ///
    /// Der gesamte MP-Code liegt in einer EIGENEN DLL (CircleHauls.Multiplayer.dll), die nur geladen wird,
    /// wenn die Multiplayer-Mod aktiv ist. Grund: UMM ruft beim Laden Assembly.GetTypes() auf – enthielte
    /// diese DLL Typen, die von MultiplayerAPI.dll abhängen, würde das ohne installierte Multiplayer-Mod
    /// scheitern (ReflectionTypeLoadException, Mod lädt nicht, Spielkonsole bricht ab).
    ///
    /// Grundsatz (wie dv-multiplayer selbst): Der Host ist autoritativ. Nur er erzeugt Aufträge, ersetzt
    /// Vanilla-Ketten, speichert und gibt die Einstellungen vor.
    /// </summary>
    internal static class MpCompat
    {
        private const string BridgeDll = "CircleHauls.Multiplayer.dll";
        private const string BridgeType = "CircleHauls.Multiplayer.MpBridge";

        private static IMpBridge _bridge;
        private static bool _initialized;

        /// <summary>Multiplayer-Mod aktiv und Brücke geladen.</summary>
        public static bool Present => _bridge != null;

        /// <summary>Verbunden, aber nicht Host → keine eigene Spiellogik, Einstellungen ausblenden.</summary>
        public static bool IsClientOnly
        {
            get
            {
                if (_bridge == null) return false;
                try { return _bridge.IsClientOnly(); }
                catch { return false; }
            }
        }

        /// <summary>Darf diese Instanz Aufträge erzeugen/ändern? (Singleplayer oder Host)</summary>
        public static bool IsAuthority => !IsClientOnly;

        /// <summary>Einmalig nach dem Laden aller Mods (OnLateUpdate) – Ladereihenfolge ist nicht garantiert.</summary>
        public static void Init(UnityModManager.ModEntry modEntry)
        {
            if (_initialized) return;
            _initialized = true;

            var mp = UnityModManager.FindMod("Multiplayer");
            if (mp == null || !mp.Enabled || !mp.Active) return;

            string path = Path.Combine(modEntry.Path, BridgeDll);
            if (!File.Exists(path))
            {
                CircleHaulsMain.Warning($"Multiplayer aktiv, aber {BridgeDll} fehlt – keine Synchronisierung");
                return;
            }

            try
            {
                var asm = Assembly.LoadFrom(path);
                _bridge = (IMpBridge)Activator.CreateInstance(asm.GetType(BridgeType, true));
                _bridge.Init(modEntry.Info.Id);
                CircleHaulsMain.Log("Multiplayer erkannt – Synchronisierung aktiv");
            }
            catch (Exception ex)
            {
                _bridge = null;
                CircleHaulsMain.Error("Multiplayer-Anbindung fehlgeschlagen", ex);
            }
        }

        public static void BroadcastJob(CircleHaulJobDefinition def)
        {
            if (_bridge == null || def?.job == null) return;
            try { _bridge.BroadcastJob(MpHooks.ToWire(def.ToData())); }
            catch (Exception ex) { CircleHaulsMain.Warning($"MP: Auftrag {def.job.ID} nicht gesendet: {ex.Message}"); }
        }

        public static void BroadcastReveal(string jobId)
        {
            if (_bridge == null || jobId == null) return;
            try { _bridge.BroadcastReveal(jobId); }
            catch (Exception ex) { CircleHaulsMain.Warning($"MP: Container-Status {jobId} nicht gesendet: {ex.Message}"); }
        }

        public static void BroadcastRemove(string jobId)
        {
            if (_bridge == null || jobId == null) return;
            try { _bridge.BroadcastRemove(jobId); }
            catch { /* beim Beenden egal */ }
        }
    }
}
