using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace CircleHauls
{
    /// <summary>
    /// Zeichnet den Settings-Tooltip GANZ ZULETZT im UMM-Fenster: als Postfix auf UnityModManager.UI.WindowFunction.
    /// Dann sind alle übrigen Mod-Zeilen schon gezeichnet und können den Kasten nicht mehr überdecken.
    /// Settings.Draw merkt sich nur den Text (Repaint), gezeichnet wird hier – im selben Fenster und Frame.
    /// </summary>
    internal static class TooltipOverlay
    {
        private static string _text;
        private static int _frame = -10;
        private static GUIStyle _style;
        private static bool _patched;

        /// <summary>Einmal beim Laden: Postfix manuell setzen (ohne AccessTools – kein Log-Spam, wenn es die Methode nicht gibt).</summary>
        public static void Patch(Harmony harmony)
        {
            try
            {
                var m = typeof(UnityModManager.UI).GetMethod("WindowFunction",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (m == null)
                {
                    CircleHaulsMain.Warning("UMM-Fenster nicht gefunden – Tooltips werden im Mod-Bereich gezeichnet");
                    return;
                }
                harmony.Patch(m, postfix: new HarmonyMethod(typeof(TooltipOverlay), nameof(WindowPostfix)));
                _patched = true;
            }
            catch (Exception ex)
            {
                CircleHaulsMain.Warning("Tooltip-Patch fehlgeschlagen: " + ex.Message);
            }
        }

        /// <summary>
        /// Aus Settings.Draw. true = wird später ganz oben gezeichnet; false = kein Patch, selbst sofort zeichnen.
        /// </summary>
        public static bool Queue(string text, GUIStyle style)
        {
            if (!_patched) return false;
            _text = text;
            _style = style;
            _frame = Time.frameCount;
            return true;
        }

        private static void WindowPostfix()
        {
            if (string.IsNullOrEmpty(_text) || _style == null) return;
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            if (_frame != Time.frameCount) { _text = null; return; }

            Draw(_text, Event.current.mousePosition, _style, WindowSize());
            _text = null;
        }

        private static FieldInfo _rectField;
        private static UnityModManager.UI _ui;

        /// <summary>Größe des UMM-Fensters – alles außerhalb schneidet Unity im Fenster ab.</summary>
        private static Vector2? WindowSize()
        {
            try
            {
                if (_ui == null) _ui = UnityEngine.Object.FindObjectOfType<UnityModManager.UI>();
                var ui = _ui;
                if (ui == null) return null;
                if (_rectField == null)
                    _rectField = typeof(UnityModManager.UI).GetField("mWindowRect", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (_rectField == null) return null;
                var r = (Rect)_rectField.GetValue(ui);
                return r.width > 50f && r.height > 50f ? new Vector2(r.width, r.height) : (Vector2?)null;
            }
            catch { return null; }
        }

        /// <summary>Kasten an der Maus (Koordinaten des aktuellen GUI-Bereichs): 460 px breit, schwarz 92 %, 2 px gelbe Linie oben.</summary>
        public static void Draw(string text, Vector2 mouse, GUIStyle style, Vector2? area = null)
        {
            const float width = 460f, margin = 4f;
            var content = new GUIContent(text);
            float height = style.CalcHeight(content, width);

            // sichtbarer Bereich: UMM-Fenster (lokale Koordinaten) – sonst der Bildschirm
            float maxX, maxY;
            if (area.HasValue) { maxX = area.Value.x; maxY = area.Value.y; }
            else
            {
                var br = GUIUtility.ScreenToGUIPoint(new Vector2(Screen.width, Screen.height));
                maxX = br.x; maxY = br.y;
            }

            float x = mouse.x + 18f;
            float y = mouse.y + 12f;
            if (x + width > maxX - margin) x = mouse.x - width - 8f;   // rechts kein Platz → links von der Maus
            if (x < margin) x = Mathf.Max(margin, maxX - margin - width);
            if (y + height > maxY - margin) y = mouse.y - height - 8f; // unten kein Platz → über der Maus
            if (y < margin) y = margin;
            var rect = new Rect(x, y, width, height);

            var oldColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.92f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.8f, 0.3f, 1f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 2f), Texture2D.whiteTexture);
            GUI.color = oldColor;

            GUI.Label(rect, content, style);
        }
    }
}
