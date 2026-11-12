using System;
using System.Reflection;
using DV.Logic.Job;
using UnityModManagerNet;

namespace CircleHauls.Jobs
{
    /// <summary>
    /// Optionale Anbindung an LongerLoadingDelay (keine harte Abhängigkeit, nur Reflection).
    /// LLD erweitert das Zeitlimit nur per Postfix auf CreateShuntingLoad/UnloadJob – unsere Jobs
    /// entstehen anders, deshalb rufen wir LLDs eigene Logik direkt auf:
    ///   JobTimeHelper.Apply(Job)      → zählt ALLE Wagen in WarehouseTasks des Jobs und verlängert das Zeitlimit
    ///   LoadPay.FactorForCars(int)    → Lohnfaktor für die Ladedauer
    /// </summary>
    internal static class LldCompat
    {
        private static bool _initialized;
        private static MethodInfo _applyTime;
        private static MethodInfo _payFactor;

        public static bool Available
        {
            get { Init(); return _applyTime != null; }
        }

        private static void Init()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                var mod = UnityModManager.FindMod("LongerLoadingDelay");
                if (mod == null || !mod.Enabled || mod.Assembly == null) return;

                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                _applyTime = mod.Assembly.GetType("LongerLoadingDelay.JobTimeHelper")?.GetMethod("Apply", flags);
                _payFactor = mod.Assembly.GetType("LongerLoadingDelay.LoadPay")?.GetMethod("FactorForCars", flags);

                CircleHaulsMain.Log($"LongerLoadingDelay erkannt (Zeit: {_applyTime != null}, Lohn: {_payFactor != null})");
            }
            catch (Exception ex)
            {
                CircleHaulsMain.Warning($"LongerLoadingDelay-Anbindung fehlgeschlagen: {ex.Message}");
                _applyTime = null;
                _payFactor = null;
            }
        }

        /// <summary>Zeitlimit um LLDs Be-/Entladezeit verlängern (alle WarehouseTasks des Jobs).</summary>
        public static void ApplyTimeLimit(Job job)
        {
            Init();
            if (_applyTime == null || job == null) return;
            try { _applyTime.Invoke(null, new object[] { job }); }
            catch (Exception ex) { CircleHaulsMain.Warning($"LLD-Zeitlimit für {job.ID} fehlgeschlagen: {ex.Message}"); }
        }

        /// <summary>LLD-Lohnfaktor für die Anzahl be-/entladener Wagen (1 ohne LLD).</summary>
        public static float PayFactor(int warehouseCars)
        {
            Init();
            if (_payFactor == null || warehouseCars <= 0) return 1f;
            try { return Convert.ToSingle(_payFactor.Invoke(null, new object[] { warehouseCars })); }
            catch { return 1f; }
        }
    }
}
