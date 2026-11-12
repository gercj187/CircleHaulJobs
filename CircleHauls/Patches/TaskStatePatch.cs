using CircleHauls.Jobs;
using DV.Logic.Job;
using HarmonyLib;

namespace CircleHauls.Patches
{
    /// <summary>
    /// Meldet abgeschlossene WarehouseTasks von CH-Jobs an ihre Definition – ereignisgesteuert, kein Polling.
    /// Wird nur bei einem echten Zustandswechsel nach "Done" ausgelöst.
    /// </summary>
    [HarmonyPatch(typeof(Task), "SetState")]
    internal static class TaskSetState_Patch
    {
        private static void Prefix(Task __instance, out TaskState __state)
        {
            __state = __instance.state;
        }

        private static void Postfix(Task __instance, TaskState __state)
        {
            if (__state == TaskState.Done || __instance.state != TaskState.Done) return;
            if (!(__instance is WarehouseTask wt)) return;

            var job = wt.Job;
            if (job == null || !CHJobType.Is(job.jobType)) return;

            if (CircleHaulJobDefinition.ByJobId.TryGetValue(job.ID, out var def) && def != null)
                def.OnWarehouseTaskDone(wt);
        }
    }
}
