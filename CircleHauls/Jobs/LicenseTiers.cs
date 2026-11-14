using DV.ThingTypes;

namespace CircleHauls.Jobs
{
    /// <summary>
    /// Gestufte Lizenzen: Wer eine höhere Stufe braucht, braucht die niedrigeren nicht zusätzlich
    /// (wie bei Zuglänge: Long II verlangt kein Long I). Gilt für Hazmat I–III und Military I–III.
    /// </summary>
    internal static class LicenseTiers
    {
        public static JobLicenses Reduce(JobLicenses f)
        {
            if ((f & JobLicenses.Hazmat3) != 0) f &= ~(JobLicenses.Hazmat1 | JobLicenses.Hazmat2);
            else if ((f & JobLicenses.Hazmat2) != 0) f &= ~JobLicenses.Hazmat1;

            if ((f & JobLicenses.Military3) != 0) f &= ~(JobLicenses.Military1 | JobLicenses.Military2);
            else if ((f & JobLicenses.Military2) != 0) f &= ~JobLicenses.Military1;

            if ((f & JobLicenses.TrainLength2) != 0) f &= ~JobLicenses.TrainLength1;
            return f;
        }
    }
}
