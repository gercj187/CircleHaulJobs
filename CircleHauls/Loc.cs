using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DV.Localization;
using Newtonsoft.Json;

namespace CircleHauls
{
    /// <summary>
    /// Übersetzungen aus lang\&lt;code&gt;.json (UTF-8 ohne BOM) im Mod-Ordner.
    /// Sprache = immer die Spielsprache (I2). Fallback Englisch (eingebaut).
    /// </summary>
    public static class Loc
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Langs =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        // Eingebauter Fallback, falls lang\en.json fehlt
        private static readonly Dictionary<string, string> BuiltInEnglish = new Dictionary<string, string>
        {
            ["title"] = "CIRCLE HAUL",
            ["sub.delivery"] = "DELIVERY",
            ["sub.pickup"] = "PICKUP",
            ["desc.delivery"] = "Urgent! Take {0} to {1} ({2}), unload it there and bring the empty cars straight back to {3}.",
            ["desc.pickup"] = "Urgent! Take the empty cars to {1} ({2}), load {0} there and bring it back to {3}. Unload it there and park the cars.",
            ["desc.swap"] = "Urgent! Take {0} to {1} ({2}) and unload it there. Load {4} and bring it back to {3}. Unload it there and park the cars.",
            ["sub.swap"] = "CONTAINER SWAP",
            ["note.missing"] = "Not every car carries a container.",
            ["task.shunt"] = "SHUNTING",
            ["gui.swap"] = "Rare container swap (unload, load, unload)",
            ["gui.swapchance"] = "Chance of a container swap",
            ["gui.missing"] = "Chance of a missing container per car",
            ["sub.forward"] = "FORWARDING",
            ["desc.forward"] = "Urgent! Take {0} to {1} ({2}) and unload it. Load {4} there and deliver it to {5} ({6}). Unload it and bring the empty cars back to {3}.",
            ["desc.forward_empty"] = "Urgent! Take the empty cars to {1} ({2}), load {4} there and deliver it to {5} ({6}). Unload it and bring the empty cars back to {3}.",
            ["gui.forward"] = "Third destination (A → B → C → A)",
            ["gui.forwardchance"] = "Chance of a third destination",
            ["tip.staysvanilla"] = "stays a normal job",
            ["gui.mphost"] = "Multiplayer: these settings are set by the host.",
            ["tip.enabled"] = "Master switch for the whole mod.\n• Off: no new Circle Hauls are created\n• Jobs that already exist keep running and can be finished",
            ["tip.delivery"] = "Loaded train from your station to B, unload there, bring the empty cars straight back and park them.\n• 1 car handling (unload at B)\n• A converted loaded train becomes a delivery unless it got a third destination or a container swap\n• Off: loaded trains only become a third destination or container swap, otherwise they stay normal jobs",
            ["tip.pickup"] = "Empty cars from your station to B, load there, bring the cargo back, unload it and park the cars.\n• 2 car handlings (load at B, unload at home)\n• A converted empty train becomes a pickup unless it got a third destination\n• Off: empty trains only become a third destination, otherwise they stay normal jobs",
            ["tip.swap"] = "Rare job with 3 car handlings, containers only:\n• Unload containers at B, load other containers, bring them home and unload them\n• Several container types per train possible\n• Single cars may have no container",
            ["tip.swapchance"] = "Chance that a converted loaded train becomes a container swap. Rolled only if ALL cars carry containers, and only after the third-destination roll.\n• Currently: {0}\n• Container train with the current settings:\n   third destination {1} · container swap {2} · delivery {3}\n• Other loaded trains and empty trains never become a swap",
            ["tip.missing"] = "Chance per car and per trip that no container is on the car.\n• Currently: {0} → with 20 cars about {1} empty cars per trip\n• No car is ever empty on both trips\n• The booklet only shows it after loading is done",
            ["tip.forward"] = "Job with a third station:\n• A → B (loaded or empty), load at B\n• Deliver to C and unload there\n• Bring the empty cars back to A and park them",
            ["tip.forwardchance"] = "Chance that a converted job gets a third destination (A → B → C → A). Rolled first, before the container swap.\n• Currently: {0}\n• Loaded train: {0} third destination (if B ships matching cargo to a C)\n• Empty train: {0} third destination, otherwise pickup – if no pickup is possible, always third destination\n• Not possible → delivery / pickup instead",
            ["tip.vanilla"] = "Normal jobs can become a Circle Haul, using the cars that are already there:\n• Empty cars ready (e.g. after unloading) → pickup / third destination\n• Loaded train ready → delivery / container swap / third destination\n• Loading job finished → Circle Haul instead of the freight haul",
            ["tip.vanillachance"] = "Chance per suitable normal job to become a Circle Haul.\n• Currently: {0} → of 20 suitable jobs about {1}\n• The station limit always applies",
            ["tip.maxjobs"] = "Maximum number of open Circle Hauls per station.\n• Currently: {0}",
            ["tip.licensed"] = "Only create Circle Hauls you hold all required licenses for (cargo, car type, train length, Shunting, Logistical Haul, Freight Haul).\n• Off: jobs may need licenses you don't have yet",
            ["tip.pay"] = "Multiplier on the sum of the single jobs (hauls, loading, unloading).\n• Currently: {0}\n• Example: single jobs worth $10,000 → ${1} (before handling bonus)",
            ["tip.handling"] = "Extra pay per car handling (loading or unloading) in the job.\n• Currently: {0} per handling\n• Delivery (1): {1}\n• Pickup (2): {2}\n• Container swap / third destination (3): {3}",
            ["tip.time"] = "Multiplier on the time bonus (all trips + shunting).\n• Currently: {0} → 60 min become {1} min\n• Loading time from LongerLoadingDelay is added on top",
            ["tip.debug"] = "Writes detailed messages to the UMM log (created jobs, conversions, multiplayer).\n• Only turn on for testing",
            ["gui.vanillachance"] = "Chance per suitable job",
            ["overview.end"] = "{0} » {1}",
            ["task.empty"] = "EMPTY RUN",
            ["task.transport"] = "TRANSPORT",
            ["task.load"] = "LOAD",
            ["task.unload"] = "UNLOAD",
            ["haul.empty"] = "Take the empty cars to {0}, track {1}.",
            ["haul.loaded"] = "Take the cars loaded with {2} to {0}, track {1}.",
            ["load.desc"] = "Load {0} into the cars at the loading machine of this track.",
            ["unload.desc"] = "Unload {0} from the cars at the unloading machine of this track.",
            ["gui.enabled"] = "Circle Hauls enabled",
            ["gui.types"] = "Job types",
            ["gui.delivery"] = "Delivery (loaded there → unload → empty back)",
            ["gui.pickup"] = "Pickup (empty there → load → back → unload)",
            ["gui.generation"] = "Generation",
            ["gui.maxjobs"] = "Max. Circle Hauls per station",
            ["gui.licensed"] = "Only jobs I hold all licenses for",
            ["gui.reward"] = "Reward",
            ["gui.pay"] = "Pay factor (on sum of single jobs)",
            ["gui.handling"] = "Bonus per car handling (load/unload)",
            ["gui.time"] = "Time bonus factor",
            ["gui.debug"] = "Debug log",
            ["gui.lld"] = "LongerLoadingDelay detected: loading time and pay are added automatically.",
        };

        public static void LoadFrom(string modPath)
        {
            Langs.Clear();
            Langs["en"] = new Dictionary<string, string>(BuiltInEnglish);

            string dir = Path.Combine(modPath ?? string.Empty, "lang");
            if (!Directory.Exists(dir)) return;

            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    string text = File.ReadAllText(file, Encoding.UTF8).TrimStart('﻿');
                    var dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(text);
                    if (dict == null) continue;

                    string code = Path.GetFileNameWithoutExtension(file);
                    if (!Langs.TryGetValue(code, out var target))
                        Langs[code] = target = new Dictionary<string, string>();
                    foreach (var kv in dict) target[kv.Key] = kv.Value;
                }
                catch (Exception ex)
                {
                    CircleHaulsMain.Warning($"Sprachdatei {Path.GetFileName(file)} fehlerhaft: {ex.Message}");
                }
            }
        }

        public static string CurrentCode
        {
            get
            {
                // Spielsprache aus I2 Localization – NICHT LocalizationAPI.CC (das ist die Windows-Zahlenformat-Kultur).
                // Nur so passen eigene Texte zu den vom Spiel übersetzten Bahnhofs- und Frachtnamen.
                string code = GameLanguageCode();
                return string.IsNullOrEmpty(code) ? "en" : Resolve(code);
            }
        }

        private static System.Reflection.PropertyInfo _i2CodeProperty;
        private static bool _i2Searched;

        /// <summary>I2.Loc.LocalizationManager.CurrentLanguageCode per Reflection (keine Extra-Referenz nötig).</summary>
        private static string GameLanguageCode()
        {
            try
            {
                if (!_i2Searched)
                {
                    _i2Searched = true;
                    var type = Type.GetType("I2.Loc.LocalizationManager, I2.Localization");
                    _i2CodeProperty = type?.GetProperty("CurrentLanguageCode",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (_i2CodeProperty == null)
                        CircleHaulsMain.Warning("Spielsprache (I2) nicht gefunden – verwende Englisch");
                }
                return _i2CodeProperty?.GetValue(null) as string;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Original-Spieltext, falls der Schlüssel existiert – sonst eigener Text.</summary>
        public static string GameOr(string gameKey, string ownKey)
        {
            try
            {
                if (LocalizationAPI.HasTranslation(gameKey))
                {
                    string s = LocalizationAPI.L(gameKey);
                    if (!string.IsNullOrEmpty(s) && s != gameKey) return s;
                }
            }
            catch { /* Spiel noch nicht bereit */ }
            return T(ownKey);
        }

        /// <summary>
        /// Spielcode → Sprachdatei. Die 26 Spielsprachen: en bg zh-CN zh-TW cs da nl fi fr de hi hu it ja ko
        /// no pl pt-PT pt-BR ro ru sk es sv tr uk. Varianten (zh-Hans, zh-HK, pt, nb …) werden zugeordnet.
        /// </summary>
        private static string Resolve(string code)
        {
            code = code.Replace('_', '-');
            if (Langs.ContainsKey(code)) return code;

            string lower = code.ToLowerInvariant();
            string lang = lower.Split('-')[0];
            switch (lang)
            {
                case "zh":
                    // Traditionell: Taiwan, Hongkong, Macau, "Hant"
                    return lower.Contains("tw") || lower.Contains("hk") || lower.Contains("mo") || lower.Contains("hant")
                        ? "zh-TW" : "zh-CN";
                case "pt":
                    return lower.Contains("br") ? "pt-BR" : "pt-PT";
                case "nb":
                case "nn":
                    return "no";
            }
            return lang;
        }

        public static string T(string key)
        {
            if (Langs.TryGetValue(CurrentCode, out var d) && d.TryGetValue(key, out var s) && !string.IsNullOrEmpty(s)) return s;
            if (Langs.TryGetValue("en", out var en) && en.TryGetValue(key, out var e)) return e;
            return BuiltInEnglish.TryGetValue(key, out var b) ? b : key;
        }

        public static string T(string key, params object[] args)
        {
            try { return string.Format(T(key), args); }
            catch { return T(key); }
        }
    }
}
