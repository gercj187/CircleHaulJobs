using System.Collections.Generic;
using System.Linq;
using DV.Logic.Job;

namespace CircleHauls.Jobs
{
    /// <summary>
    /// Alle Jobketten, die durch JobChainController.FinalizeSetupAndGenerateFirstJob gelaufen sind –
    /// egal ob vom Vanilla-Generator, aus dem Spielstand oder von Persistenz-Mods (z. B. PersistentJobsRework),
    /// die ihre Ketten NICHT beim StationProceduralJobsController des Bahnhofs eintragen.
    /// </summary>
    internal static class ChainRegistry
    {
        private static readonly HashSet<JobChainController> Chains = new HashSet<JobChainController>();

        public static void Register(JobChainController chain)
        {
            if (chain != null) Chains.Add(chain);
        }

        private static IEnumerable<JobChainController> Alive()
        {
            // zerstörte Ketten (GameObject weg) aufräumen
            Chains.RemoveWhere(c => c == null || c.jobChainGO == null);
            return Chains;
        }

        public static JobChainController FindByDefinition(StaticJobDefinition def)
        {
            if (def == null) return null;
            var hit = Alive().FirstOrDefault(c => c.jobChain != null && c.jobChain.Contains(def));
            if (hit != null) return hit;

            // Fallback: Ketten, die der Bahnhof selbst kennt
            foreach (var sc in StationController.allStations)
            {
                hit = sc?.ProceduralJobsController?.GetCurrentJobChains()?
                    .FirstOrDefault(c => c?.jobChain != null && c.jobChain.Contains(def));
                if (hit != null) { Register(hit); return hit; }
            }
            return null;
        }

        /// <summary>Alle aktiven Zugumläufe mit Startbahnhof <paramref name="yardId"/>.</summary>
        public static int CountCircleHauls(string yardId, JobChainController except)
        {
            var all = new HashSet<JobChainController>(Alive());
            var sc = StationController.GetStationByYardID(yardId);
            var own = sc?.ProceduralJobsController?.GetCurrentJobChains();
            if (own != null) all.UnionWith(own.Where(c => c != null));

            return all.Count(c => c != except && CircleHaulChainController.IsCircleHaul(c) && OriginYard(c) == yardId);
        }

        private static string OriginYard(JobChainController c)
        {
            var first = c.jobChain?.FirstOrDefault(d => d != null);
            return first?.logicStation?.ID ?? c.responsibleStationForJobChain?.stationInfo?.YardID;
        }
    }
}
