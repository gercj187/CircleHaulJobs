using System.Linq;
using CircleHauls.Jobs;
using DV.Logic.Job;
using DV.ThingTypes;
using HarmonyLib;

namespace CircleHauls.Patches
{
    /// <summary>Job-IDs im Format "SM-CH-42".</summary>
    [HarmonyPatch(typeof(IdGenerator), nameof(IdGenerator.GenerateJobID))]
    internal static class IdGenerator_Patch
    {
        private static bool Prefix(IdGenerator __instance, JobType jobType, StationsChainData jobStationsInfo, ref string __result)
        {
            if (!CHJobType.Is(jobType)) return true;

            string yardId = jobStationsInfo?.chainOriginYardId;
            int start = IdGenerator.idRng.Next(0, 100);

            for (int i = 0; i < 100; i++)
            {
                int n = (start + i) % 100;
                string id = yardId != null ? $"{yardId}-{CHJobType.IdPrefix}-{n:D2}" : $"{CHJobType.IdPrefix}-{n:D2}";
                if (!__instance.existingJobIds.Contains(id))
                {
                    __instance.RegisterJobId(id);
                    __result = id;
                    return false;
                }
            }

            CircleHaulsMain.Warning("Keine freie CH-Job-ID gefunden, verwende 00");
            __result = yardId != null ? $"{yardId}-{CHJobType.IdPrefix}-00" : $"{CHJobType.IdPrefix}-00";
            return false;
        }
    }

    [HarmonyPatch(typeof(JobChainController))]
    internal static class JobChainController_Patch
    {
        /// <summary>Speicherdaten als eigenen Typ markieren.</summary>
        [HarmonyPatch(nameof(JobChainController.GetJobChainSaveData))]
        [HarmonyPostfix]
        private static void GetJobChainSaveData_Postfix(JobChainController __instance, ref JobChainSaveData __result)
        {
            // Auch Vanilla-Ketten mit Zugumlauf → in unseren eigenen Save-Bereich
            if (__result != null && CircleHaulChainController.IsCircleHaul(__instance) &&
                __result.jobChainData != null && __result.jobChainData.Any(d => d is CircleHaulJobDefinitionData))
                __result = new CircleHaulChainSaveData(__result);
        }

        // Abgebrochene Aufträge: KEIN eigenes Löschen der Wagen mehr – Vanilla markiert sie selbst als
        // unbenutzt. Doppeltes Löschen führte zu NullReferenceException beim Teleport (UnusedTrainCarDeleter).
    }
}
