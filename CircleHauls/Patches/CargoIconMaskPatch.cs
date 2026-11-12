using System.Collections.Generic;
using System.Linq;
using CircleHauls.Booklets;
using DV.RenderTextureSystem.BookletRender;
using HarmonyLib;
using UnityEngine;

namespace CircleHauls.Patches
{
    /// <summary>
    /// Blendet auf CH-Seiten das Fracht-Icon von Wagen ohne Container aus.
    /// Die Wagen-Objekte liegen in Reihenfolge in dynamicallyCreatedObjects und haben ein Kind "[cargo icon]".
    /// </summary>
    internal static class CargoIconMask
    {
        public static void Apply(TemplatePaper paper, object data)
        {
            if (!(data is ICargoMask mask) || mask.HiddenIcons == null) return;

            var icons = paper.dynamicallyCreatedObjects
                .Where(go => go != null)
                .Select(go => go.transform.Find("[cargo icon]"))
                .Where(t => t != null)
                .ToList();

            for (int i = 0; i < icons.Count && i < mask.HiddenIcons.Length; i++)
                if (mask.HiddenIcons[i]) icons[i].gameObject.SetActive(false);
        }
    }

    [HarmonyPatch(typeof(TaskTemplatePaper), nameof(TaskTemplatePaper.FillInData))]
    internal static class TaskTemplatePaper_Mask_Patch
    {
        private static void Postfix(TaskTemplatePaper __instance) => CargoIconMask.Apply(__instance, __instance.data);
    }

    [HarmonyPatch(typeof(FrontPageTemplatePaper), nameof(FrontPageTemplatePaper.FillInData))]
    internal static class FrontPageTemplatePaper_Mask_Patch
    {
        private static void Postfix(FrontPageTemplatePaper __instance) => CargoIconMask.Apply(__instance, __instance.data);
    }
}
