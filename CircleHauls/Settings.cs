using System.Globalization;
using CircleHauls.Jobs;
using UnityEngine;
using UnityModManagerNet;

namespace CircleHauls
{
    public class Settings : UnityModManager.ModSettings
    {
        public bool Enabled = true;

        // Auftragsarten
        public bool EnableDelivery = true;   // Zustellung: beladen hin, entladen, leer zurück
        public bool EnablePickup = true;     // Abholung: leer hin, beladen, zurück, entladen
        public bool EnableSwap = true;                // Containertausch (3 Behandlungen)
        public float SwapChance = 0.5f;              // Anteil seltener Containertausch-Aufträge
        public float MissingContainerChance = 0.2f;   // Chance pro Wagen, dass kein Container drauf ist
        public float VanillaChance = 0.15f;           // Chance je passender Vanilla-Kette
        public bool EnableForward = true;             // drittes Ziel: A → B → C → A
        public float ForwardChance = 0.5f;           // Anteil Aufträge mit drittem Ziel

        // Generierung
        public int MaxJobsPerStation = 2;
        /// <summary>Mehr als 20 Wagen kann das Spiel aktuell nicht spawnen.</summary>
        public const int MaxCarsLimit = 20;
        public bool OnlyLicensedJobs = false;
        public bool CombineBaseLicenses = false;

        // Belohnung
        public float PaymentMultiplier = 1.1f;   // auf die Summe der Einzelaufträge
        public float HandlingBonus = 0.1f;      // je Wagenbehandlung: Zustellung 1×, Abholung 2×, Tausch/3. Ziel 3×
        public float TimeLimitMultiplier = 1.0f;

        public bool DebugLog = false;

        public override void Save(UnityModManager.ModEntry modEntry) => Save(this, modEntry);

        // ------------------------------------------------------------------ GUI

        public void Draw()
        {
            // Multiplayer: Der Host gibt alles vor – Clients sehen nur diesen einen Hinweis
            if (Multiplayer.MpCompat.IsClientOnly)
            {
                GUILayout.Label("<i>" + Loc.T("gui.mphost") + "</i>");
                return;
            }

            Enabled = Toggle(Enabled, "gui.enabled", Tip("gui.enabled", "tip.enabled"));

            Header("gui.types");
            EnableDelivery = Toggle(EnableDelivery, "gui.delivery", Tip("gui.delivery", "tip.delivery"));
            EnablePickup = Toggle(EnablePickup, "gui.pickup", Tip("gui.pickup", "tip.pickup"));

            EnableSwap = Toggle(EnableSwap, "gui.swap", Tip("gui.swap", "tip.swap"));
            if (EnableSwap)
            {
                SwapChance = FloatSlider("gui.swapchance", SwapChance, 0f, 1f, Pct(SwapChance), TipSwap());
                MissingContainerChance = FloatSlider("gui.missing", MissingContainerChance, 0f, 0.5f, Pct(MissingContainerChance),
                    Tip("gui.missing", "tip.missing", Pct(MissingContainerChance),
                        Mathf.RoundToInt(MaxCarsLimit * MissingContainerChance).ToString(CultureInfo.InvariantCulture)));
            }

            EnableForward = Toggle(EnableForward, "gui.forward", Tip("gui.forward", "tip.forward"));
            if (EnableForward)
                ForwardChance = FloatSlider("gui.forwardchance", ForwardChance, 0f, 1f, Pct(ForwardChance),
                    Tip("gui.forwardchance", "tip.forwardchance", Pct(ForwardChance)));

            Header("gui.generation");
            // Zugumläufe entstehen NUR aus Vanilla-Ketten (vorhandene Wagen) – diese Mod spawnt keine Wagen
            VanillaChance = FloatSlider("gui.vanillachance", VanillaChance, 0f, 1f, Pct(VanillaChance),
                Tip("gui.vanillachance", "tip.vanilla") + "\n\n" + Loc.T("tip.vanillachance", Pct(VanillaChance),
                    Mathf.RoundToInt(20 * VanillaChance).ToString(CultureInfo.InvariantCulture)));
            MaxJobsPerStation = IntSlider("gui.maxjobs", MaxJobsPerStation, 1, 5,
                Tip("gui.maxjobs", "tip.maxjobs", MaxJobsPerStation.ToString(CultureInfo.InvariantCulture)));
            OnlyLicensedJobs = Toggle(OnlyLicensedJobs, "gui.licensed", Tip("gui.licensed", "tip.licensed"));
            CombineBaseLicenses = Toggle(CombineBaseLicenses, "gui.combineicon", Tip("gui.combineicon", "tip.combineicon"));

            Header("gui.reward");
            PaymentMultiplier = FloatSlider("gui.pay", PaymentMultiplier, 1f, 4f, "x" + PaymentMultiplier.ToString("0.00"), TipPay());
            HandlingBonus = FloatSlider("gui.handling", HandlingBonus, 0f, 1f, Pct(HandlingBonus), TipHandling());
            TimeLimitMultiplier = FloatSlider("gui.time", TimeLimitMultiplier, 0.5f, 3f, "x" + TimeLimitMultiplier.ToString("0.00"),
                Tip("gui.time", "tip.time", "x" + TimeLimitMultiplier.ToString("0.00"),
                    Mathf.RoundToInt(60f * TimeLimitMultiplier).ToString(CultureInfo.InvariantCulture)));

            GUILayout.Space(6);
            DebugLog = ToggleRight(DebugLog, "gui.debug", Tip("gui.debug", "tip.debug"));

            // MUSS der letzte Aufruf sein
            DrawTooltip();
        }

        // ------------------------------------------------------------------ Tooltip-Texte

        /// <summary>"&lt;b&gt;Titel&lt;/b&gt;" + lokalisierter Erklärtext mit live eingesetzten Werten.</summary>
        private static string Tip(string titleKey, string tipKey, params object[] args) =>
            "<b>" + Loc.T(titleKey) + "</b>\n" + (args.Length > 0 ? Loc.T(tipKey, args) : Loc.T(tipKey));

        private string TipPay()
        {
            // Beispiel: Einzeljobs zusammen $10.000
            string example = (10000f * PaymentMultiplier).ToString("N0", CultureInfo.CurrentCulture);
            string tip = Tip("gui.pay", "tip.pay", "x" + PaymentMultiplier.ToString("0.00"), example);
            if (LldCompat.Available) tip += "\n\n" + Loc.T("gui.lld");
            return tip;
        }

        /// <summary>Aufteilung eines beladenen Containerzugs mit den aktuellen Werten (drittes Ziel wird zuerst gewürfelt).</summary>
        private string TipSwap()
        {
            float f = EnableForward ? ForwardChance : 0f;
            float sw = EnableSwap ? (1f - f) * SwapChance : 0f;
            float rest = 1f - f - sw;
            string restText = Pct(rest) + (EnableDelivery ? "" : " (" + Loc.T("tip.staysvanilla") + ")");
            return Tip("gui.swapchance", "tip.swapchance", Pct(SwapChance), Pct(f), Pct(sw), restText);
        }

        private string TipHandling()
        {
            string F(int n) => "x" + (1f + HandlingBonus * n).ToString("0.00");
            return Tip("gui.handling", "tip.handling", Pct(HandlingBonus), F(1), F(2), F(3));
        }

        private static string Pct(float v) => Mathf.RoundToInt(v * 100f).ToString(CultureInfo.InvariantCulture) + " %";

        // ------------------------------------------------------------------ Bausteine (wie LongerLoadingDelay)

        private static GUIStyle _tipStyle;
        private static GUIStyle _boxStyle;

        private static void Header(string key)
        {
            GUILayout.Space(6);
            GUILayout.Label("<b>" + Loc.T(key) + "</b>");
        }

        /// <summary>Gelbes, fettes " ?" mit Tooltip direkt hinter dem Wert.</summary>
        private static void TipIcon(string tip)
        {
            if (string.IsNullOrEmpty(tip)) return;
            if (_tipStyle == null)
            {
                _tipStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
                _tipStyle.normal.textColor = new Color(1f, 0.8f, 0.3f);
            }
            GUILayout.Label(new GUIContent(" ?", tip), _tipStyle, GUILayout.ExpandWidth(false));
        }

        /// <summary>Breite einer Einstellungszeile: Text (inkl. "?") + Regler + Wert.</summary>
        private const float LabelWidth = 340f, SliderWidth = 220f, ValueWidth = 60f;

        private static bool Toggle(bool value, string labelKey, string tip)
        {
            GUILayout.BeginHorizontal();
            value = GUILayout.Toggle(value, " " + Loc.T(labelKey), GUILayout.ExpandWidth(false));
            TipIcon(tip);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return value;
        }

        /// <summary>Schalter ganz rechts im Fenster (z. B. Debug).</summary>
        private static bool ToggleRight(bool value, string labelKey, string tip)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            value = GUILayout.Toggle(value, " " + Loc.T(labelKey), GUILayout.ExpandWidth(false));
            TipIcon(tip);
            GUILayout.Space(8);
            GUILayout.EndHorizontal();
            return value;
        }

        /// <summary>Beschriftung mit "?" direkt hinter dem Text, feste Breite – Regler stehen dadurch bündig.</summary>
        private static void SliderLabel(string labelKey, string tip)
        {
            GUILayout.BeginHorizontal(GUILayout.Width(LabelWidth));
            GUILayout.Label(Loc.T(labelKey), GUILayout.ExpandWidth(false));
            TipIcon(tip);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private static float FloatSlider(string labelKey, float value, float min, float max, string shown, string tip)
        {
            GUILayout.BeginHorizontal();
            SliderLabel(labelKey, tip);
            value = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(SliderWidth));
            GUILayout.Label(shown, GUILayout.Width(ValueWidth));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return value;
        }

        private static int IntSlider(string labelKey, int value, int min, int max, string tip)
        {
            GUILayout.BeginHorizontal();
            SliderLabel(labelKey, tip);
            value = Mathf.RoundToInt(GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(SliderWidth)));
            GUILayout.Label(value.ToString(CultureInfo.InvariantCulture), GUILayout.Width(ValueWidth));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return value;
        }

        /// <summary>
        /// Tooltip NICHT hier zeichnen: UMM zeichnet nach unserem Draw() noch die übrigen Mod-Zeilen –
        /// die lägen sonst über dem Kasten. Stattdessen den Text an TooltipOverlay übergeben,
        /// das ihn per Postfix am Ende von UMMs WindowFunction zeichnet. Danach GUI.tooltip leeren (sonst zeigt UMM ihn doppelt).
        /// </summary>
        private static void DrawTooltip()
        {
            if (Event.current.type != EventType.Repaint || string.IsNullOrEmpty(GUI.tooltip)) return;

            if (_boxStyle == null)
            {
                _boxStyle = new GUIStyle(GUI.skin.label)
                {
                    wordWrap = true,
                    richText = true,
                    alignment = TextAnchor.UpperLeft,
                    padding = new RectOffset(10, 10, 8, 8),
                };
                _boxStyle.normal.textColor = Color.white;
            }

            // bevorzugt ganz zuletzt im UMM-Fenster zeichnen; ohne Patch direkt hier
            if (!TooltipOverlay.Queue(GUI.tooltip, _boxStyle))
                TooltipOverlay.Draw(GUI.tooltip, Event.current.mousePosition, _boxStyle);
            GUI.tooltip = "";
        }
    }
}
