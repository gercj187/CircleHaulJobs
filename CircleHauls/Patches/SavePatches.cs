using System.Collections.Generic;
using System.Linq;
using CircleHauls.Jobs;
using CircleHauls.Saving;
using HarmonyLib;
using UnityEngine;

namespace CircleHauls.Patches
{
    [HarmonyPatch(typeof(SaveGameManager))]
    internal static class SaveGameManager_Patch
    {
        [HarmonyPatch(nameof(SaveGameManager.DoSaveIO))]
        [HarmonyPrefix]
        private static void DoSaveIO_Prefix(SaveGameData data) => CircleHaulSaveData.Inject(data);

        [HarmonyPatch(nameof(SaveGameManager.FindStartGameData))]
        [HarmonyPostfix]
        private static void FindStartGameData_Postfix(SaveGameManager __instance)
        {
            CircleHaulSaveData.LoadedData = null;
            if (__instance.data != null) CircleHaulSaveData.Extract(__instance.data);
        }
    }

    [HarmonyPatch(typeof(JobSaveManager))]
    internal static class JobSaveManager_Patch
    {
        [HarmonyPatch(nameof(JobSaveManager.LoadJobSaveGameData))]
        [HarmonyPrefix]
        private static void LoadJobSaveGameData_Prefix(JobsSaveGameData saveData) => CircleHaulSaveData.Merge(saveData);

        [HarmonyPatch(nameof(JobSaveManager.LoadJobChain))]
        [HarmonyPrefix]
        private static bool LoadJobChain_Prefix(JobChainSaveData chainSaveData, List<JobBooklet> jobBooklets, ref GameObject __result)
        {
            if (!(chainSaveData is CircleHaulChainSaveData own)) return true;
            __result = CircleHaulSaveData.LoadChain(own, jobBooklets);
            return false;
        }

        /// <summary>Unsere Ketten NICHT im Vanilla-Teil speichern (die liegen im eigenen Schlüssel).</summary>
        [HarmonyPatch(nameof(JobSaveManager.GetJobsSaveGameData))]
        [HarmonyPostfix]
        private static void GetJobsSaveGameData_Postfix(ref JobsSaveGameData __result)
        {
            if (__result?.jobChains == null) return;
            __result.jobChains = __result.jobChains.Where(c => !(c is CircleHaulChainSaveData)).ToArray();
        }
    }
}
