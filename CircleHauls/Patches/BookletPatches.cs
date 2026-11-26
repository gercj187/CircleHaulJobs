using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using CircleHauls.Booklets;
using CircleHauls.Jobs;
using DV.Booklets;
using DV.RenderTextureSystem.BookletRender;
using HarmonyLib;
using UnityEngine;

namespace CircleHauls.Patches
{
    /// <summary>Komplettes Booklet für CH-Jobs.</summary>
    [HarmonyPatch(typeof(BookletCreator_Job), nameof(BookletCreator_Job.GetBookletTemplateData))]
    internal static class BookletCreator_Job_Patch
    {
        private static bool Prefix(Job_data job, ref List<TemplatePaperData> __result)
        {
            if (!CHJobType.Is(job.type)) return true;
            try
            {
                __result = CircleHaulBooklet.CreateBooklet(job);
                return false;
            }
            catch (Exception ex)
            {
                // NIE an Vanilla zurückgeben (kennt den Typ nicht → null → Render-System hängt) – Notfall-Booklet
                CircleHaulsMain.Error($"Booklet für {job.ID} fehlgeschlagen", ex);
                __result = new List<TemplatePaperData>
                {
                    new CoverPageTemplatePaperData(job.ID, CircleHaulBooklet.Title, "1", "2"),
                    BookletCreator_Job.CreateValidateJobTaskPaperData(1, 2, 2),
                };
                return false;
            }
        }
    }

    /// <summary>Übersichtsblatt am Auftragsbrett.</summary>
    [HarmonyPatch(typeof(BookletCreator_JobOverview), nameof(BookletCreator_JobOverview.GetJobOverviewTemplateData))]
    internal static class BookletCreator_JobOverview_Patch
    {
        private static bool Prefix(Job_data job, ref List<TemplatePaperData> __result)
        {
            if (!CHJobType.Is(job.type)) return true;
            try
            {
                __result = CircleHaulBooklet.CreateOverviewOnly(job);
                return false;
            }
            catch (Exception ex)
            {
                CircleHaulsMain.Error($"Übersicht für {job.ID} fehlgeschlagen", ex);
                // Das Übersichts-Prefab kennt nur die Vorderseite (FrontPage) – ein Deckblatt bliebe leer (blaues Blatt)
                try { __result = new List<TemplatePaperData> { CircleHaulBooklet.CreatePlaceholderOverview(job) }; }
                catch { __result = new List<TemplatePaperData> { new CoverPageTemplatePaperData(job.ID, CircleHaulBooklet.Title, "", "") }; }
                return false;
            }
        }
    }

    /// <summary>
    /// "Lizenz fehlt"-Blatt: Vanilla kennt unseren Typ im switch nicht. Gleiche Technik wie PassengerJobs:
    /// vor der "Unsupported"-Fehlermeldung unseren Fall einschieben.
    /// </summary>
    [HarmonyPatch(typeof(BookletCreator_JobMissingLicense), nameof(BookletCreator_JobMissingLicense.GetMissingLicenseTemplateData))]
    internal static class BookletCreator_JobMissingLicense_Patch
    {
        // Direkt per Reflection statt AccessTools.TypeByName/Field: TypeByName durchsucht ALLE Assemblies und schreibt
        // bei fremden, nicht ladbaren Typen Warnungen ins Log (Log-Spam); AccessTools.Field loggt, wenn nichts gefunden wird.
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Type DisplayClass =
            typeof(BookletCreator_JobMissingLicense).GetNestedType("<>c__DisplayClass2_0", BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo JobField = DisplayClass?.GetField("job", All);
        private static readonly FieldInfo JobTypeField = DisplayClass?.GetField("jobType", All);
        private static readonly FieldInfo JobColorField = DisplayClass?.GetField("jobColor", All);

        private static bool Prepare()
        {
            bool ok = DisplayClass != null && JobField != null && JobTypeField != null && JobColorField != null;
            if (!ok) CircleHaulsMain.Warning("MissingLicense-Patch übersprungen (Spielversion geändert?)");
            return ok;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var endSwitch = generator.DefineLabel();

            foreach (var ins in instructions)
            {
                if (ins.opcode == OpCodes.Ldstr && ins.operand is string s && s.StartsWith("Unsupported"))
                {
                    yield return new CodeInstruction(OpCodes.Ldloca_S, 0).MoveLabelsFrom(ins);
                    yield return new CodeInstruction(OpCodes.Ldfld, JobField);
                    yield return new CodeInstruction(OpCodes.Ldloca_S, 0);
                    yield return new CodeInstruction(OpCodes.Ldflda, JobTypeField);
                    yield return new CodeInstruction(OpCodes.Ldloca_S, 0);
                    yield return new CodeInstruction(OpCodes.Ldflda, JobColorField);
                    yield return CodeInstruction.Call(typeof(BookletCreator_JobMissingLicense_Patch), nameof(HandleCircleHaul));
                    yield return new CodeInstruction(OpCodes.Brtrue_S, endSwitch);
                }
                else if (ins.opcode == OpCodes.Ldarg_1)
                {
                    ins.labels.Add(endSwitch);
                }

                yield return ins;
            }
        }

        private static bool HandleCircleHaul(Job_data job, ref string jobType, ref Color jobColor)
        {
            if (!CHJobType.Is(job.type)) return false;
            jobType = CircleHaulBooklet.Title;
            jobColor = CHJobType.Color;
            return true;
        }
    }
}
