using DV.Logic.Job;
using Newtonsoft.Json;
using UnityEngine;

namespace CircleHauls.Jobs
{
    public class CircleHaulChainController : JobChainController
    {
        public CircleHaulChainController(GameObject jobChainGO) : base(jobChainGO) { }

        /// <summary>Eigene Kette oder Vanilla-Kette, in der ein Zugumlauf steckt (Vanilla-Integration).</summary>
        public static bool IsCircleHaul(JobChainController chain) =>
            chain is CircleHaulChainController ||
            (chain?.jobChain != null && chain.jobChain.Exists(d => d is CircleHaulJobDefinition));

        public override void OnLastJobInChainCompleted(Job lastJobInChain)
        {
            CircleHaulsMain.Log($"Circle Haul {lastJobInChain.ID} abgeschlossen");
            // Wagen bleiben stehen und werden vom Spiel wie normale "unbenutzte" Wagen behandelt
            base.OnLastJobInChainCompleted(lastJobInChain);
        }
    }

    /// <summary>Eigener Typ, damit wir unsere Ketten beim Speichern/Laden erkennen.</summary>
    public class CircleHaulChainSaveData : JobChainSaveData
    {
        [JsonConstructor]
        public CircleHaulChainSaveData(JobDefinitionDataBase[] jobChainData, string[] trainCarGuids, bool jobTaken,
            TaskSaveData[] currentJobTaskData, string firstJobId)
            : base(jobChainData, trainCarGuids, jobTaken, currentJobTaskData, firstJobId) { }

        public CircleHaulChainSaveData(JobChainSaveData b)
            : base(b.jobChainData, b.trainCarGuids, b.jobTaken, b.currentJobTaskData, b.firstJobId) { }
    }
}
