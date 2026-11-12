using System.Collections.Generic;
using System.Linq;
using CircleHauls.Booklets;
using CircleHauls.Jobs;
using DV.RenderTextureSystem.BookletRender;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace CircleHauls.Patches
{
    /// <summary>
    /// Das Deckblatt hat keinen Farb-Parameter – das Orange kommt aus dem Prefab.
    /// Nach FillInData färben wir die orangen Flächen für CH-Booklets lila
    /// und stellen sie für alle anderen Booklets wieder her (Prefab-Instanzen können wiederverwendet werden).
    /// </summary>
    [HarmonyPatch(typeof(CoverPageTemplatePaper), nameof(CoverPageTemplatePaper.FillInData))]
    internal static class CoverPageTemplatePaper_Patch
    {
        private static readonly Dictionary<int, Color> Originals = new Dictionary<int, Color>();
        private static bool _dumped;

        private static void Postfix(CoverPageTemplatePaper __instance)
        {
            bool isCircleHaul = __instance.data != null && CHJobType.IsId(__instance.data.jobID);

            var graphics = __instance.GetComponentsInChildren<Graphic>(true)
                .Where(g => !g.GetType().Name.Contains("Text"))
                .ToList();

            int recolored = 0;
            foreach (var g in graphics)
            {
                int id = g.GetInstanceID();

                if (isCircleHaul)
                {
                    if (!Originals.ContainsKey(id))
                    {
                        if (!IsOrange(g.color)) continue;
                        Originals[id] = g.color;
                    }
                    // Alpha des Originals behalten
                    var c = CHJobType.Color;
                    c.a = Originals[id].a;
                    g.color = c;
                    recolored++;
                }
                else if (Originals.TryGetValue(id, out var original))
                {
                    g.color = original;
                    Originals.Remove(id);
                }
            }

            if (isCircleHaul && recolored == 0 && !_dumped)
            {
                // Orange steckt dann in der Textur statt in der Graphic-Farbe → Hierarchie fürs nächste Update loggen
                _dumped = true;
                CircleHaulsMain.Warning("Deckblatt: keine orange Fläche gefunden. Hierarchie:");
                foreach (var g in graphics)
                {
                    string tex = (g as Image)?.sprite?.name ?? (g as RawImage)?.texture?.name ?? "-";
                    CircleHaulsMain.Warning($"  {Path(g.transform)} [{g.GetType().Name}] color={g.color} tex={tex}");
                }
            }
        }

        private static bool IsOrange(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            // Orange/Rotorange, kräftig gesättigt
            return h >= 0.0f && h <= 0.12f && s > 0.5f && v > 0.4f;
        }

        private static string Path(Transform t)
        {
            var parts = new List<string>();
            for (; t != null; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
