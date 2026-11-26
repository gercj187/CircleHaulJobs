// File: CircleHaulsJobKindsApi.cs
//
// Public API for other mods (e.g. ZG2): the job kind CircleHauls brings along.
//
// A Circle Haul is its own job type (JobType 121, job ID e.g. "SM-CH-42") – one job that drives out,
// (un)loads at the far station(s) and comes back. Other mods should NOT parse the ID themselves but ask this API.
//
// Only primitive / vanilla types are used, so the API can be called via reflection without referencing CircleHauls.
// Look the type up directly (AccessTools.TypeByName scans every assembly and spams the log):
//
//   Type? api = Type.GetType("CircleHauls.JobKinds, CircleHauls");                  // null = CircleHauls not installed
//   string[] codes = (string[])api.GetMethod("GetKindCodes").Invoke(null, null);     // "CH"
//   string? code   = (string?)api.GetMethod("GetKindCode", new[] { typeof(Job) }).Invoke(null, new object[] { job });
//   string  name   = (string)api.GetMethod("GetLocalizedName").Invoke(null, new object[] { code });
//   bool    haul   = (bool)api.GetMethod("IsHaul").Invoke(null, new object[] { code });
//   string? variant = (string?)api.GetMethod("GetVariant", new[] { typeof(Job) }).Invoke(null, new object[] { job });
//
// Codes are stable and are never renamed (safe to store in settings files).

#nullable enable

using System;
using CircleHauls.Jobs;
using DV.Logic.Job;
using DV.ThingTypes;

namespace CircleHauls
{
    public static class JobKinds
    {
        /// <summary>Raised only when methods are added; existing methods never change.</summary>
        public const int ApiVersion = 1;

        /// <summary>Prefix other mods can use for their own setting keys / display (e.g. "CircleHauls:CH").</summary>
        public const string ModId = "CircleHauls";

        public const string CircleHaul = CHJobType.IdPrefix; // "CH"

        /// <summary>Variant codes (stable): DL = delivery, PU = pickup, CS = container swap, TD = third destination.</summary>
        public const string VariantDelivery = "DL";
        public const string VariantPickup = "PU";
        public const string VariantContainerSwap = "CS";
        public const string VariantThirdDestination = "TD";

        private static readonly string[] codes = { CircleHaul };
        private static readonly string[] variantCodes = { VariantDelivery, VariantPickup, VariantContainerSwap, VariantThirdDestination };

        /// <summary>All job kinds CircleHauls brings along (copy, safe to modify).</summary>
        public static string[] GetKindCodes() => (string[])codes.Clone();

        /// <summary>All variant codes (copy, safe to modify).</summary>
        public static string[] GetVariantCodes() => (string[])variantCodes.Clone();

        /// <summary>Kind code of a job, or null if it is no Circle Haul.</summary>
        public static string? GetKindCode(Job? job)
        {
            if (job == null) return null;
            return job.jobType == CHJobType.CircleHaul ? CircleHaul : GetKindCodeFromJobId(job.ID);
        }

        /// <summary>Kind code from a job ID (e.g. "SM-CH-42" → "CH"), or null if it is no Circle Haul.</summary>
        public static string? GetKindCodeFromJobId(string? jobId) => CHJobType.IsId(jobId) ? CircleHaul : null;

        /// <summary>True if the job is a Circle Haul.</summary>
        public static bool IsCircleHaulJob(Job? job) => GetKindCode(job) != null;

        /// <summary>
        /// Variant of a Circle Haul (DL / PU / CS / TD), or null if unknown or no Circle Haul.
        /// Works on the host and, once the host's data has arrived, on multiplayer clients.
        /// </summary>
        public static string? GetVariant(Job? job) => job == null ? null : GetVariantFromJobId(job.ID);

        public static string? GetVariantFromJobId(string? jobId)
        {
            if (!CHJobType.IsId(jobId)) return null;
            try
            {
                var data = CircleHaulData.Get(jobId);
                if (data == null) return null;
                switch (data.Variant)
                {
                    case CircleHaulVariant.Delivery: return VariantDelivery;
                    case CircleHaulVariant.Pickup: return VariantPickup;
                    case CircleHaulVariant.Swap: return VariantContainerSwap;
                    case CircleHaulVariant.Forward: return VariantThirdDestination;
                    default: return null;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>English name as printed on the booklet ("CIRCLE HAUL"). Empty for unknown codes.</summary>
        public static string GetEnglishName(string? code) => code == CircleHaul ? "CIRCLE HAUL" : string.Empty;

        /// <summary>Name in the current game language (falls back to English). Empty for unknown codes.</summary>
        public static string GetLocalizedName(string? code)
        {
            if (code != CircleHaul) return string.Empty;
            try
            {
                string text = Loc.T("title");
                if (!string.IsNullOrEmpty(text) && text != "title") return text;
            }
            catch (Exception)
            {
                // localization not ready yet
            }
            return GetEnglishName(code);
        }

        /// <summary>
        /// I2 translation key of the name. CircleHauls localizes itself (own language files), so this is always empty –
        /// use GetLocalizedName instead.
        /// </summary>
        public static string TranslationKey(string? code) => string.Empty;

        /// <summary>Job type of the kind: the own type CircleHaul (JobType 121), null for unknown codes.</summary>
        public static JobType? GetBaseJobType(string? code) => code == CircleHaul ? CHJobType.CircleHaul : (JobType?)null;

        /// <summary>True for kinds that drive from one station to another. A Circle Haul always does.</summary>
        public static bool IsHaul(string? code) => code == CircleHaul;
    }
}
