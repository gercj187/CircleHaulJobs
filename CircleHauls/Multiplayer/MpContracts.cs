using System.Collections.Generic;
using System.Linq;
using CircleHauls.Jobs;
using DV.Logic.Job;
using DV.ThingTypes;

namespace CircleHauls.Multiplayer
{
    /// <summary>
    /// Schnittstelle zur optionalen CircleHauls.Multiplayer.dll. Nur primitive Typen bzw. CircleHaulWire,
    /// damit diese (Haupt-)DLL keinerlei Abhängigkeit zur MultiplayerAPI hat.
    /// </summary>
    public interface IMpBridge
    {
        void Init(string modId);
        bool IsClientOnly();
        void BroadcastJob(CircleHaulWire job);
        void BroadcastReveal(string jobId);
        void BroadcastRemove(string jobId);
    }

    /// <summary>Netzwerk-taugliche Form der Zugumlauf-Daten (nur IDs und Zahlen).</summary>
    public class CircleHaulWire
    {
        public string JobId = "";
        public byte Variant;
        public string StartTrackId = "";
        public string ReturnTrackId = "";
        public string FarUnloadMachineId = "";
        public string FarLoadMachineId = "";
        public string HomeMachineId = "";
        public int[] OutCargo = new int[0];
        public int[] BackCargo = new int[0];
        public int CarCount;
        public bool BackRevealed;
    }

    /// <summary>Aufrufe aus CircleHauls.Multiplayer.dll zurück in die Mod.</summary>
    public static class MpHooks
    {
        /// <summary>Host: alle laufenden Zugumläufe (für Spieler, die später dazukommen).</summary>
        public static List<CircleHaulWire> GetAllJobs() =>
            CircleHaulJobDefinition.ByJobId.Values
                .Where(d => d != null && d.job != null)
                .Select(d => ToWire(d.ToData()))
                .ToList();

        public static CircleHaulWire ToWire(CircleHaulData d) => new CircleHaulWire
        {
            JobId = d.JobId ?? "",
            Variant = (byte)d.Variant,
            StartTrackId = d.StartTrack?.ID.FullID ?? "",
            ReturnTrackId = d.ReturnTrack?.ID.FullID ?? "",
            FarUnloadMachineId = d.FarUnloadMachine?.ID ?? "",
            FarLoadMachineId = d.FarLoadMachine?.ID ?? "",
            HomeMachineId = d.HomeMachine?.ID ?? "",
            OutCargo = d.OutCargo?.Select(c => (int)c).ToArray() ?? new int[0],
            BackCargo = d.BackCargo?.Select(c => (int)c).ToArray() ?? new int[0],
            CarCount = d.CarCount,
            BackRevealed = d.IsRevealed(PlanLeg.Back),
        };

        /// <summary>Client: Daten vom Host übernehmen und ggf. schon gerenderte Booklets neu zeichnen.</summary>
        public static void OnRemoteJob(CircleHaulWire w)
        {
            if (w == null || string.IsNullOrEmpty(w.JobId)) return;

            var jsm = JobSaveManager.Instance;
            WarehouseMachine Machine(string id) => string.IsNullOrEmpty(id) ? null : jsm.GetWarehouseMachineWithId(id);

            var data = new CircleHaulData
            {
                JobId = w.JobId,
                Variant = (CircleHaulVariant)w.Variant,
                StartTrack = jsm.GetYardTrackWithId(w.StartTrackId),
                ReturnTrack = jsm.GetYardTrackWithId(w.ReturnTrackId),
                FarUnloadMachine = Machine(w.FarUnloadMachineId),
                FarLoadMachine = Machine(w.FarLoadMachineId),
                HomeMachine = Machine(w.HomeMachineId),
                OutCargo = w.OutCargo?.Select(c => (CargoType)c).ToList(),
                BackCargo = w.BackCargo?.Select(c => (CargoType)c).ToList(),
                CarCount = w.CarCount,
                BackRevealedRemote = w.BackRevealed,
            };

            if (data.StartTrack == null || data.ReturnTrack == null)
            {
                CircleHaulsMain.Warning($"MP: Gleise für {w.JobId} nicht gefunden");
                return;
            }

            CircleHaulData.Remote[w.JobId] = data;
            CircleHaulsMain.Log($"MP: Zugumlauf {w.JobId} vom Host empfangen");
            CircleHaulJobDefinition.RerenderBooklets(w.JobId);
        }

        public static void OnRemoteReveal(string jobId)
        {
            if (jobId == null || !CircleHaulData.Remote.TryGetValue(jobId, out var data)) return;
            data.BackRevealedRemote = true;
            CircleHaulJobDefinition.RerenderBooklets(jobId);
        }

        public static void OnRemoteRemove(string jobId)
        {
            if (jobId != null) CircleHaulData.Remote.Remove(jobId);
        }
    }
}
