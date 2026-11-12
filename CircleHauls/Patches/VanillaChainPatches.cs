using CircleHauls.Jobs;
using DV.Logic.Job;
using HarmonyLib;

namespace CircleHauls.Patches
{
    /// <summary>Neue Vanilla-Kette (Leerzug / bereitgestellter Güterzug): evtl. durch Zugumlauf ersetzen.</summary>
    [HarmonyPatch(typeof(JobChainController), nameof(JobChainController.FinalizeSetupAndGenerateFirstJob))]
    internal static class JobChainController_Finalize_Patch
    {
        private static void Prefix(JobChainController __instance, bool jobLoadedFromSavegame)
        {
            // Geladene Ketten nie verändern; eigene Ketten sind schon Zugumläufe
            if (jobLoadedFromSavegame || __instance is CircleHaulChainController) return;
            VanillaIntegration.TryConvertNewChain(__instance);
        }

        /// <summary>Jede Kette merken – Persistenz-Mods tragen ihre Ketten nicht beim Bahnhof ein.</summary>
        private static void Postfix(JobChainController __instance) => ChainRegistry.Register(__instance);
    }

    /// <summary>Beladeauftrag fertig: Vor dem Erzeugen des Güterzugs evtl. einen Zugumlauf daraus machen.</summary>
    [HarmonyPatch(typeof(StaticJobDefinition), "OnJobCompleted")]
    internal static class StaticJobDefinition_OnJobCompleted_Patch
    {
        private static void Prefix(StaticJobDefinition __instance)
        {
            if (__instance is StaticShuntingLoadJobDefinition)
                VanillaIntegration.TryReplaceAfterShuntingLoad(__instance);
        }
    }
}
