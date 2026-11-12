using DV.ThingTypes;
using UnityEngine;

namespace CircleHauls.Jobs
{
    public enum CircleHaulVariant
    {
        Delivery = 0, // FH → SU → LH : "Bring das hin, entlade es, bring die Leeren zurück"
        Pickup = 1,   // LH → SL → FH → SU → Abstellen : "Hol mir XYZ"
        Swap = 2,     // Container: FH → SU → SL → FH → SU → Abstellen (3 Behandlungen, selten)
        Forward = 3,  // Drittes Ziel: A → B (beladen oder leer) → B beladen → C entladen → leer zurück nach A
    }

    public static class CHJobType
    {
        // Vanilla: 0..5, PassengerJobs: 101/102 → eigener Wert ohne Kollision
        public const JobType CircleHaul = (JobType)121;

        public const string IdPrefix = "CH";

        // "legendärer" Lila-Streifen
        public static readonly Color Color = new Color32(150, 60, 190, 255);

        public static bool Is(JobType type) => type == CircleHaul;

        /// <summary>Erkennung auf gerenderten Seiten über die Job-ID ("SM-CH-42") – der Titel ist übersetzt.</summary>
        public static bool IsId(string jobId) =>
            !string.IsNullOrEmpty(jobId) && (jobId.Contains("-" + IdPrefix + "-") || jobId.StartsWith(IdPrefix + "-"));
    }
}
