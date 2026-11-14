using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CircleHauls.Jobs;
using DV;
using DV.RenderTextureSystem.BookletRender;
using DV.ThingTypes;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CircleHauls.Patches
{
    /// <summary>
    /// Deckblatt / Auftragsübersicht: Das Prefab hat nur 5 Lizenz-Felder. Ein Zugumlauf braucht oft mehr
    /// (Rangieren + Logistik + Güterverkehr + Fracht + Länge …). Für CH-Aufträge werden zusätzliche Felder
    /// links daneben gesetzt – gleicher Abstand, gleiche Höhe. Überlappt die Beschriftung "Lizenz(en):",
    /// wird sie schmaler (Text verkleinert sich automatisch).
    /// Die Template-Instanzen werden für alle Booklets wiederverwendet → vor jedem Aufruf alles zurücksetzen.
    /// </summary>
    [HarmonyPatch(typeof(FrontPageTemplatePaper), "DisplayRequiredLicenses")]
    internal static class FrontPageLicenseOverflow_Patch
    {
        private class LabelBackup
        {
            public TextMeshProUGUI Tmp;
            public Vector3 Position;
            public Vector2 SizeDelta, AnchoredPos;
            public string Text;
            public TMP_FontAsset Font;
            public Material Material;
            public float FontSize, Min, Max, CharSpacing;
            public bool AutoSize;
            public TextWrappingModes Wrap;
            public FontStyles Style;
            public TextAlignmentOptions Align;
            public Color Color;
        }

        private class State
        {
            public Image[] Original;
            public readonly List<Image> Extras = new List<Image>();
            public readonly List<LabelBackup> Labels = new List<LabelBackup>();
        }

        private static readonly Dictionary<FrontPageTemplatePaper, State> States = new Dictionary<FrontPageTemplatePaper, State>();

        /// <summary>Was der Postfix wissen muss: ob es ein Zugumlauf ist und ob die 3 Grundlizenzen zusammengefasst sind.</summary>
        internal class CallState
        {
            public bool Combined;
            public bool IsCircleHaul;
        }

        private const JobLicenses BaseTrio = JobLicenses.Shunting | JobLicenses.LogisticalHaul | JobLicenses.FreightHaul;

        private static bool Prefix(FrontPageTemplatePaper __instance, ref JobLicenses requiredLicenses, out CallState __state)
        {
            __state = new CallState();
            try
            {
                if (!States.TryGetValue(__instance, out var st))
                {
                    if (__instance.requiredLicenseSlots == null) return true;
                    st = new State { Original = __instance.requiredLicenseSlots };
                    States[__instance] = st;
                }

                // Zurücksetzen (vorheriger Auftrag könnte ein Zugumlauf gewesen sein)
                foreach (var e in st.Extras) if (e != null) e.gameObject.SetActive(false);
                RestoreLabels(st);
                __instance.requiredLicenseSlots = st.Original;

                // für ALLE Booklets: niedrigere Hazmat-/Military-Stufen ausblenden, wenn eine höhere verlangt wird
                requiredLicenses = LicenseTiers.Reduce(requiredLicenses);

                var data = __instance.data;
                if (data == null || !CHJobType.IsId(data.jobId)) return true;
                __state.IsCircleHaul = true;

                // Rangieren + Logistik + Güterverkehr → EIN Kombi-Icon (DVHR S|L|F). Das Spiel zeichnet dafür nur das
                // FH-Icon, der Postfix tauscht dessen Bild gegen das Kombi-Icon.
                __state.Combined = CircleHaulsMain.Settings != null && CircleHaulsMain.Settings.CombineBaseLicenses &&
                                   (requiredLicenses & BaseTrio) == BaseTrio && CombinedLicenseIcon.Sprite != null;

                // "License requirement(s):" aus dem Feld nehmen und – wie bei den anderen Feldern – fett und einzeilig darüber setzen
                MoveLabelAbove(__instance, st);

                // Reihenfolge selbst bestimmen: Feld 0 ist (wie im Spiel) das rechte, weitere nach links.
                // Zuerst Rangieren, Logistik, Güterverkehr (bzw. Kombi-Icon), dann die übrigen in Spiel-Reihenfolge.
                var icons = OrderedIcons(requiredLicenses, __state.Combined);
                int count = icons.Count;
                int baseCount = st.Original.Length;
                if (count <= baseCount || baseCount < 2)
                {
                    FillSlots(st.Original, icons);
                    return false;
                }
                int need = count - baseCount;

                var first = st.Original[0];
                var parent = first.transform.parent;
                while (st.Extras.Count < need)
                {
                    var go = UnityEngine.Object.Instantiate(first.gameObject, parent, false);
                    go.name = first.gameObject.name + "_CH" + st.Extras.Count;
                    // falls die Felder in einer LayoutGroup liegen: selbst positionieren
                    var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
                    le.ignoreLayout = true;
                    go.SetActive(false);
                    st.Extras.Add(go.GetComponent<Image>());
                }

                // Abstand und Richtung aus den vorhandenen Feldern
                Vector3 step = st.Original[1].transform.localPosition - st.Original[0].transform.localPosition;
                bool fillsLeftward = step.x < 0f;
                var slots = new List<Image>();
                if (fillsLeftward)
                {
                    // Feld 0 rechts, weitere nach links → neue Felder hinten anhängen, weiter nach links
                    Vector3 last = st.Original[baseCount - 1].transform.localPosition;
                    slots.AddRange(st.Original);
                    for (int k = 0; k < need; k++)
                    {
                        st.Extras[k].transform.localPosition = last + step * (k + 1);
                        slots.Add(st.Extras[k]);
                    }
                }
                else
                {
                    // Feld 0 links, weitere nach rechts → neue Felder links VOR Feld 0, Reihenfolge bleibt links → rechts
                    Vector3 p0 = st.Original[0].transform.localPosition;
                    for (int k = need; k >= 1; k--)
                    {
                        var img = st.Extras[k - 1];
                        img.transform.localPosition = p0 - step * k;
                        slots.Add(img);
                    }
                    slots.AddRange(st.Original);
                }

                // Füllreihenfolge = Feld 0 zuerst; bei Feldern links von Feld 0 (fillsLeftward == false) entsprechend umdrehen
                var fillOrder = fillsLeftward ? slots : Enumerable.Reverse(slots).ToList();
                FillSlots(fillOrder.ToArray(), icons);
                return false;
            }
            catch (Exception ex)
            {
                CircleHaulsMain.Warning("Lizenzfelder (Deckblatt) konnten nicht erweitert werden: " + ex.Message);
                return true;
            }
        }

        private static List<Sprite> OrderedIcons(JobLicenses flags, bool combined)
        {
            var all = Globals.G.Types.jobLicenses.Skip(1).Where(l => l != null && (flags & l.v1) != 0).ToList();
            var res = new List<Sprite>();
            if (combined) res.Add(CombinedLicenseIcon.Sprite);
            else
            {
                foreach (var want in new[] { JobLicenses.Shunting, JobLicenses.LogisticalHaul, JobLicenses.FreightHaul })
                {
                    var l = all.FirstOrDefault(x => x.v1 == want);
                    if (l != null) res.Add(l.icon);
                }
            }
            res.AddRange(all.Where(l => (l.v1 & BaseTrio) == 0).Select(l => l.icon));
            return res;
        }

        /// <summary>Alle Felder aus, dann die ersten n mit den Icons belegen und einschalten.</summary>
        private static void FillSlots(Image[] slots, List<Sprite> icons)
        {
            foreach (var x in slots) if (x != null) x.gameObject.SetActive(false);
            for (int i = 0; i < icons.Count && i < slots.Length; i++)
            {
                if (slots[i] == null) continue;
                slots[i].sprite = icons[i];
                slots[i].gameObject.SetActive(true);
            }
        }

        /// <summary>Rangfolge der Grundlizenzen auf CH-Booklets: Rangieren, Logistik, Güterverkehr (bzw. Kombi-Icon), dann der Rest.</summary>
        internal static int BaseRank(Sprite icon)
        {
            if (icon == null) return 99;
            if (CombinedLicenseIcon.Sprite != null && icon == CombinedLicenseIcon.Sprite) return 0;
            foreach (var l in Globals.G.Types.jobLicenses)
            {
                if (l == null || l.icon != icon) continue;
                if (l.v1 == JobLicenses.Shunting) return 1;
                if (l.v1 == JobLicenses.LogisticalHaul) return 2;
                if (l.v1 == JobLicenses.FreightHaul) return 3;
            }
            return 99;
        }

        /// <summary>Nach dem Befüllen wieder das Original-Array einsetzen – die Zusatzfelder bleiben für dieses Rendern sichtbar.</summary>
        private static void Postfix(FrontPageTemplatePaper __instance, CallState __state)
        {
            // Das Spiel hat für CH nichts gezeichnet (Prefix = false) – nur sicherstellen, dass das Original-Array wieder drin ist
            if (__state?.IsCircleHaul == true && States.TryGetValue(__instance, out var st)) __instance.requiredLicenseSlots = st.Original;
        }

        private static readonly Vector3[] Corners = new Vector3[4];

        /// <summary>
        /// Rechteck in den LOKALEN Koordinaten der Seite (x = rechts, y = oben auf dem Blatt).
        /// Weltkoordinaten taugen nicht: das Blatt liegt im Render-System beliebig gedreht im Raum.
        /// </summary>
        private static Rect PageRect(RectTransform rt, Transform page)
        {
            rt.GetWorldCorners(Corners);
            float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                var p = page.InverseTransformPoint(Corners[i]);
                xMin = Mathf.Min(xMin, p.x); xMax = Mathf.Max(xMax, p.x);
                yMin = Mathf.Min(yMin, p.y); yMax = Mathf.Max(yMax, p.y);
            }
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        /// <summary>Unter-/Oberkante der wirklich gerenderten Schrift in Seitenkoordinaten (NaN = keine Schrift).</summary>
        private static bool TextSpan(TMP_Text t, Transform page, out float bottom, out float top)
        {
            bottom = top = float.NaN;
            var b = t.textBounds;
            if (b.size.y <= 0f) return false;
            float y1 = page.InverseTransformPoint(t.transform.TransformPoint(new Vector3(b.center.x, b.min.y, 0f))).y;
            float y2 = page.InverseTransformPoint(t.transform.TransformPoint(new Vector3(b.center.x, b.max.y, 0f))).y;
            bottom = Mathf.Min(y1, y2);
            top = Mathf.Max(y1, y2);
            return true;
        }

        /// <summary>Verschiebung in Seitenkoordinaten auf ein Element anwenden.</summary>
        private static void MoveBy(RectTransform rt, Transform page, float dx, float dy)
        {
            rt.position += page.TransformVector(new Vector3(dx, dy, 0f));
        }

        /// <summary>
        /// Beschriftung "License requirement(s):" finden (Text links neben den Lizenzfeldern, gleiche Höhe) und
        /// über das Lizenzfeld setzen – Schrift, Größe, Stil und Abstand wie die Überschrift "TIME BONUS" über dem Zeitfeld.
        /// </summary>
        private static void MoveLabelAbove(FrontPageTemplatePaper page, State st)
        {
            if (st.Original.Length == 0 || page.timeBonus == null) return;
            var P = page.transform;

            Rect slotsRect = PageRect(st.Original[0].rectTransform, P);
            foreach (var s in st.Original)
            {
                var r = PageRect(s.rectTransform, P);
                slotsRect = Rect.MinMaxRect(Mathf.Min(slotsRect.xMin, r.xMin), Mathf.Min(slotsRect.yMin, r.yMin),
                                            Mathf.Max(slotsRect.xMax, r.xMax), Mathf.Max(slotsRect.yMax, r.yMax));
            }

            var texts = page.GetComponentsInChildren<TextMeshProUGUI>(false);
            var slotSet = st.Original.Select(s => s.transform).ToList();

            // 1) Beschriftung: links von den Feldern, im selben Band
            TextMeshProUGUI label = null;
            foreach (var t in texts)
            {
                if (slotSet.Any(s => t.transform.IsChildOf(s)) || string.IsNullOrWhiteSpace(t.text)) continue;
                var r = PageRect(t.rectTransform, P);
                if (r.yMax > slotsRect.yMin && r.yMin < slotsRect.yMax && r.center.x < slotsRect.xMin) { label = t; break; }
            }
            if (label == null) return;

            // 2) Feld = der graue Balken hinter Beschriftung und Lizenzfeldern: kleinste Grafik (nicht in einem Lizenzfeld),
            //    die die Mitte der Lizenzfelder UND die Mitte der Beschriftung enthält.
            //    (Der gemeinsame Elternknoten ist oft ein größerer Container – dessen Oberkante liegt viel zu hoch.)
            var labelRect0 = PageRect(label.rectTransform, P);
            Rect? fieldOpt = null;
            foreach (var g in page.GetComponentsInChildren<Image>(false))
            {
                if (slotSet.Any(x => g.transform.IsChildOf(x))) continue;
                var r = PageRect(g.rectTransform, P);
                if (!r.Contains(slotsRect.center) || !r.Contains(labelRect0.center)) continue;
                if (fieldOpt == null || r.width * r.height < fieldOpt.Value.width * fieldOpt.Value.height) fieldOpt = r;
            }
            // ohne Balken: Zeile aus Beschriftung + Feldern
            var field = fieldOpt ?? Rect.MinMaxRect(Mathf.Min(labelRect0.xMin, slotsRect.xMin), slotsRect.yMin,
                                                    slotsRect.xMax, slotsRect.yMax);
            // höher als die Lizenzfelder selbst + eine Feldhöhe kann der Balken nicht sein
            if (field.yMax > slotsRect.yMax + slotsRect.height) field.yMax = slotsRect.yMax;

            // 3) Vorlage "TIME BONUS": Text über dem Zeitbonus-Wert, horizontal überlappend, am nächsten darüber
            var valueRect = PageRect(page.timeBonus.rectTransform, P);
            TextMeshProUGUI header = null;
            float best = float.MaxValue;
            foreach (var t in texts)
            {
                if (t == page.timeBonus || t == label || string.IsNullOrWhiteSpace(t.text)) continue;
                var r = PageRect(t.rectTransform, P);
                if (r.center.x < valueRect.xMin || r.center.x > valueRect.xMax) continue;
                float d = r.center.y - valueRect.center.y;
                if (d <= 0f || d >= best) continue;
                best = d; header = t;
            }

            var rt = label.rectTransform;
            st.Labels.Add(new LabelBackup
            {
                Tmp = label, Position = rt.position, SizeDelta = rt.sizeDelta, AnchoredPos = rt.anchoredPosition,
                Text = label.text, Font = label.font, Material = label.fontSharedMaterial,
                FontSize = label.fontSize, Min = label.fontSizeMin, Max = label.fontSizeMax, CharSpacing = label.characterSpacing,
                AutoSize = label.enableAutoSizing, Wrap = label.textWrappingMode,
                Style = label.fontStyle, Align = label.alignment, Color = label.color,
            });

            string text = label.text.Replace("<br>", " ").Replace("\r", " ").Replace("\n", " ").Trim().TrimEnd(':').Trim();
            while (text.Contains("  ")) text = text.Replace("  ", " ");

            // Abstand: Schrift-Unterkante "TIME BONUS" → Oberkante des grauen Wertekastens.
            // Der Kasten ist der Elternknoten des Werts (falls er nicht auch die Überschrift umschließt), sonst der Wert selbst.
            float gap = float.NaN;
            Rect? valueBox = null;
            if (header != null)
            {
                label.font = header.font;
                label.fontSharedMaterial = header.fontSharedMaterial;
                label.fontSize = header.fontSize;
                label.fontStyle = header.fontStyle | FontStyles.Bold;
                label.color = header.color;
                label.characterSpacing = header.characterSpacing;
                if (header.text == header.text.ToUpperInvariant()) text = text.ToUpperInvariant();

                // grauer Kasten des Werts = kleinste Grafik (kein Text), die die Wert-Mitte enthält, aber nicht die Überschrift
                var hRect = PageRect(header.rectTransform, P);
                Rect? boxRect = null;
                foreach (var g in page.GetComponentsInChildren<Image>(false))
                {
                    var r = PageRect(g.rectTransform, P);
                    if (!r.Contains(valueRect.center) || r.Contains(hRect.center)) continue;
                    if (boxRect == null || r.width * r.height < boxRect.Value.width * boxRect.Value.height) boxRect = r;
                }
                valueBox = boxRect;
                if (boxRect != null && TextSpan(header, P, out float hb, out _)) gap = hb - boxRect.Value.yMax;
            }
            else label.fontStyle |= FontStyles.Bold | FontStyles.UpperCase;

            label.text = text;
            label.enableAutoSizing = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.alignment = TextAlignmentOptions.Bottom;

            // volle Feldbreite, Mitte über dem Feld
            float scaleX = Mathf.Abs(rt.lossyScale.x) > 1e-6f ? Mathf.Abs(rt.lossyScale.x) / Mathf.Max(1e-6f, Mathf.Abs(P.lossyScale.x)) : 1f;
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, field.width / scaleX);
            var cur = PageRect(rt, P);
            MoveBy(rt, P, field.center.x - cur.center.x, 0f);

            // Schrift-Unterkante = Feldoberkante + Abstand
            label.ForceMeshUpdate();
            if (!TextSpan(label, P, out float lb, out float lt))
            {
                // keine Schriftmaße verfügbar → Rechteck-Unterkante (Text ist unten ausgerichtet) knapp über das Feld
                var r = PageRect(rt, P);
                lb = r.yMin; lt = r.yMin + Mathf.Max(1e-4f, Mathf.Min(r.height, field.height * 0.3f));
            }
            float h = lt - lb;
            // plausibel: zwischen 10 % und 80 % der Schrifthöhe – sonst fester Abstand wie optisch bei "TIME BONUS"
            if (float.IsNaN(gap) || gap < h * 0.1f || gap > h * 0.8f) gap = h * 0.4f;
            gap = Mathf.Min(gap, h * 0.15f); // nah ans Feld (Wunsch nach Test: 15 %)
            // mittig zwischen Unterkante der Wertekästen (TIME BONUS …) und Oberkante des Lizenzbalkens
            if (valueBox != null && valueBox.Value.yMin > field.yMax + h)
                MoveBy(rt, P, 0f, (valueBox.Value.yMin + field.yMax) / 2f - (lb + lt) / 2f);
            else
                MoveBy(rt, P, 0f, field.yMax + gap - lb);

            if (CircleHaulsMain.Settings?.DebugLog == true)
                CircleHaulsMain.Log($"Lizenz-Überschrift: Feld oben {field.yMax:0.###}, Abstand {gap:0.###}, Schrift unten vorher {lb:0.###}, Vorlage {(header != null ? header.text : "-")}");
        }

        private static void RestoreLabels(State st)
        {
            foreach (var b in st.Labels)
            {
                var t = b.Tmp;
                if (t == null) continue;
                t.text = b.Text;
                t.font = b.Font;
                t.fontSharedMaterial = b.Material;
                t.fontSize = b.FontSize;
                t.fontSizeMin = b.Min;
                t.fontSizeMax = b.Max;
                t.characterSpacing = b.CharSpacing;
                t.enableAutoSizing = b.AutoSize;
                t.textWrappingMode = b.Wrap;
                t.fontStyle = b.Style;
                t.alignment = b.Align;
                t.color = b.Color;
                t.rectTransform.sizeDelta = b.SizeDelta;
                t.rectTransform.anchoredPosition = b.AnchoredPos;
            }
            st.Labels.Clear();
        }
    }

    /// <summary>
    /// "Abgelehnt"-Blatt: Das Prefab hat nur 5 Zeilen. Für CH-Aufträge:
    ///   • nur fehlende Lizenzen, Reihenfolge Rangieren, Logistik, Güterverkehr, dann der Rest,
    ///   • bei mehr als 5 Lizenzen kommen zusätzliche Zeilen dazu; alle Zeilen werden im selben Bereich
    ///     gleichmäßig verteilt und dafür etwas kleiner (das Blatt ist ein Einzelblatt, Umblättern geht nicht).
    /// </summary>
    [HarmonyPatch(typeof(MissingLicensesPageTemplatePaper), nameof(MissingLicensesPageTemplatePaper.FillInData))]
    internal static class MissingLicenseOverflow_Patch
    {
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly FieldInfo EntriesField = typeof(MissingLicensesPageTemplatePaper).GetField("licenseEntries", All);
        private static readonly Type EntryType = EntriesField?.FieldType.GetGenericArguments().FirstOrDefault();
        private static readonly FieldInfo ParentField = EntryType?.GetField("parentObject", All);

        private class State
        {
            public List<object> Original;
            public List<Vector3> Positions, Scales;
            public readonly List<object> Extras = new List<object>();
        }

        private static readonly Dictionary<MissingLicensesPageTemplatePaper, State> States = new Dictionary<MissingLicensesPageTemplatePaper, State>();

        private static bool Prepare()
        {
            bool ok = EntriesField != null && EntryType != null && ParentField != null;
            if (!ok) CircleHaulsMain.Warning("Abgelehnt-Blatt: Erweiterung übersprungen (Spielversion geändert?)");
            return ok;
        }

        private static GameObject Root(object entry) => ParentField.GetValue(entry) as GameObject;

        private static void Prefix(MissingLicensesPageTemplatePaper __instance)
        {
            try
            {
                if (!(EntriesField.GetValue(__instance) is IList current)) return;

                if (!States.TryGetValue(__instance, out var st))
                {
                    var orig = current.Cast<object>().ToList();
                    if (orig.Any(e => Root(e) == null)) return;
                    st = new State
                    {
                        Original = orig,
                        Positions = orig.Select(e => Root(e).transform.localPosition).ToList(),
                        Scales = orig.Select(e => Root(e).transform.localScale).ToList(),
                    };
                    States[__instance] = st;
                }

                // Zurücksetzen auf das Prefab
                for (int i = 0; i < st.Original.Count; i++)
                {
                    var t = Root(st.Original[i]).transform;
                    t.localPosition = st.Positions[i];
                    t.localScale = st.Scales[i];
                }
                foreach (var e in st.Extras) Root(e)?.SetActive(false);
                SetEntries(__instance, st.Original);

                var data = __instance.data;
                if (data == null || !CHJobType.IsId(data.jobId) || data.licensesData == null) return;

                // gleiche Reihenfolge wie auf dem Deckblatt: Rangieren, Logistik, Güterverkehr, dann der Rest
                // nur FEHLENDE Lizenzen zeigen – vorhandene sind kein Ablehnungsgrund
                // (Blatt "zu viele gleichzeitige Aufträge" o. ä. ohne fehlende Lizenz: nichts ausblenden)
                bool anyMissing = data.licensesData.Any(l => !l.isAcquired);
                var sorted = data.licensesData.Where(l => !anyMissing || !l.isAcquired).Select((l, i) => new { l, i })
                    .OrderBy(e => FrontPageLicenseOverflow_Patch.BaseRank(e.l.licenseIcon)).ThenBy(e => e.i)
                    .Select(e => e.l).ToList();
                data.licensesData.Clear();
                data.licensesData.AddRange(sorted);

                int n = data.licensesData.Count, baseCount = st.Original.Count;
                if (n <= baseCount || baseCount < 2) return;

                while (st.Extras.Count < n - baseCount)
                    st.Extras.Add(CloneEntry(st.Original[baseCount - 1], st.Extras.Count));

                // alle Zeilen gleichmäßig im ursprünglichen Bereich (erste bis letzte Zeile) verteilen
                Vector3 p0 = st.Positions[0];
                Vector3 span = st.Positions[baseCount - 1] - p0;
                Vector3 step = span / (n - 1);
                float oldStep = (st.Positions[1] - p0).magnitude;
                float factor = oldStep > 1e-4f ? Mathf.Min(1f, step.magnitude / oldStep) : 1f;

                var rows = st.Original.Concat(st.Extras.Take(n - baseCount)).ToList();
                for (int i = 0; i < rows.Count; i++)
                {
                    var t = Root(rows[i]).transform;
                    t.localPosition = p0 + step * i;
                    t.localScale = st.Scales[0] * factor;
                }
                SetEntries(__instance, rows);
            }
            catch (Exception ex)
            {
                CircleHaulsMain.Warning("Abgelehnt-Blatt konnte nicht erweitert werden: " + ex.Message);
            }
        }

        private static void SetEntries(MissingLicensesPageTemplatePaper page, List<object> entries)
        {
            var list = (IList)Activator.CreateInstance(EntriesField.FieldType);
            foreach (var e in entries) list.Add(e);
            EntriesField.SetValue(page, list);
        }

        /// <summary>Zeile klonen: GameObject kopieren und alle Verweise (Text, Icons) auf die Kopie umbiegen.</summary>
        private static object CloneEntry(object template, int index)
        {
            var srcRoot = Root(template);
            var clone = UnityEngine.Object.Instantiate(srcRoot, srcRoot.transform.parent, false);
            clone.name = srcRoot.name + "_CH" + index;
            var le = clone.GetComponent<LayoutElement>() ?? clone.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            clone.SetActive(false);

            var entry = Activator.CreateInstance(EntryType, true);
            foreach (var f in EntryType.GetFields(All))
            {
                var val = f.GetValue(template);
                object mapped = val;
                if (val is GameObject go) mapped = Map(go.transform, srcRoot.transform, clone.transform)?.gameObject ?? (object)go;
                else if (val is Component c)
                {
                    var t = Map(c.transform, srcRoot.transform, clone.transform);
                    mapped = t != null ? t.GetComponent(c.GetType()) : c;
                }
                f.SetValue(entry, mapped);
            }
            return entry;
        }

        /// <summary>Gleiche Stelle im Klon über den relativen Pfad finden.</summary>
        private static Transform Map(Transform t, Transform srcRoot, Transform cloneRoot)
        {
            if (t == srcRoot) return cloneRoot;
            if (!t.IsChildOf(srcRoot)) return null;
            var path = new List<int>();
            for (var cur = t; cur != srcRoot; cur = cur.parent) path.Insert(0, cur.GetSiblingIndex());
            var res = cloneRoot;
            foreach (int i in path)
            {
                if (i >= res.childCount) return null;
                res = res.GetChild(i);
            }
            return res;
        }
    }
}
