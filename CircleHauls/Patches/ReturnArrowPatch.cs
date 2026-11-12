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
    /// Übersichtsseite: Das kleine schwarze Dreieck zeigt links von Start (Zeile 1) nach Ziel (Zeile 2).
    /// Für Circle Hauls klonen wir es, drehen es um 180° und setzen es gespiegelt nach rechts:
    /// es zeigt dann vom Ziel (Zeile 2) zurück zum Start (Zeile 1).
    /// Der Klon kommt in dynamicallyCreatedObjects → Vanilla-CleanUp entfernt ihn wieder.
    /// </summary>
    [HarmonyPatch(typeof(FrontPageTemplatePaper), nameof(FrontPageTemplatePaper.FillInData))]
    internal static class FrontPageTemplatePaper_ReturnArrow_Patch
    {
        private static bool _dumped;


        private static void Postfix(FrontPageTemplatePaper __instance)
        {
            var data = __instance.data;
            if (data == null || !CHJobType.IsId(data.jobId)) return;
            if (string.IsNullOrEmpty(data.startStationName)) return;

            var startBand = __instance.startStationName.transform.parent;
            var endBand = __instance.endStationName.transform.parent;
            var container = startBand != null ? startBand.parent as RectTransform : null;
            if (container == null) return;

            var arrow = FindArrow(__instance, container, startBand, endBand);
            if (arrow == null)
            {
                DumpOnce(container);
                return;
            }

            var clone = Object.Instantiate(arrow.gameObject, arrow.transform.parent, true);
            clone.name = "[CircleHaul return arrow]";

            // Position im Container spiegeln (links ↔ rechts), Höhe bleibt gleich
            Vector3 p = container.InverseTransformPoint(arrow.transform.position);
            Rect r = container.rect;
            p.x = r.xMin + r.xMax - p.x;

            clone.transform.position = container.TransformPoint(p);

            // 180° drehen: zeigt jetzt nach oben zurück in die Startzeile
            clone.transform.localRotation = arrow.transform.localRotation * Quaternion.Euler(0f, 0f, 180f);
            clone.transform.SetAsLastSibling();
            clone.SetActive(true);

            __instance.dynamicallyCreatedObjects.Add(clone);
        }

        /// <summary>
        /// Das Dreieck ist ein kleines, dunkles Image unter den beiden Bahnhofsbalken
        /// (nicht die Balken-Hintergründe, keine Lizenzen/Wagen).
        /// </summary>
        private static Image FindArrow(FrontPageTemplatePaper page, RectTransform container, Transform startBand, Transform endBand)
        {
            var exclude = new HashSet<Image> { page.startStationBgColor, page.endStationBgColor, page.singleStationBgColor };

            var candidates = container.GetComponentsInChildren<Image>(true)
                .Where(img => !exclude.Contains(img))
                .Where(img => img.transform.IsChildOf(startBand) || img.transform.IsChildOf(endBand) || img.transform.parent == container)
                .Where(img => !img.name.StartsWith("[CircleHaul"))
                .ToList();

            // 1) per Name
            var byName = candidates.FirstOrDefault(img =>
            {
                string n = img.name.ToLowerInvariant();
                return n.Contains("arrow") || n.Contains("triangle") || n.Contains("pointer") || n.Contains("dreieck");
            });
            if (byName != null) return byName;

            // 2) kleinstes dunkles Image
            return candidates
                .Where(img =>
                {
                    Color.RGBToHSV(img.color, out _, out _, out float v);
                    var size = img.rectTransform.rect.size;
                    return v < 0.35f && size.x > 0f && size.x < 60f && size.y < 60f;
                })
                .OrderBy(img => img.rectTransform.rect.width * img.rectTransform.rect.height)
                .FirstOrDefault();
        }

        private static void DumpOnce(Transform container)
        {
            if (_dumped) return;
            _dumped = true;
            CircleHaulsMain.Warning("Übersicht: Dreieck nicht gefunden. Aufbau der Bahnhofszeilen:");
            foreach (var img in container.GetComponentsInChildren<Image>(true))
            {
                var rt = img.rectTransform;
                CircleHaulsMain.Warning($"  {Path(img.transform, container)} color={img.color} size={rt.rect.size} sprite={img.sprite?.name ?? "-"}");
            }
        }

        private static string Path(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (; t != null && t != root; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
