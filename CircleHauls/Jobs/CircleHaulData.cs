using System;
using System.Collections.Generic;
using System.Linq;
using DV.Logic.Job;
using DV.ThingTypes;

namespace CircleHauls.Jobs
{
    /// <summary>
    /// Alles, was das Booklet über einen Zugumlauf wissen muss – unabhängig davon, ob es vom Host
    /// (aus der Job-Definition) oder im Multiplayer per Paket vom Host kommt (Client hat keine Definition).
    /// </summary>
    public class CircleHaulData
    {
        /// <summary>Im Multiplayer vom Host empfangene Daten (nur auf Clients gefüllt).</summary>
        public static readonly Dictionary<string, CircleHaulData> Remote = new Dictionary<string, CircleHaulData>();

        public string JobId;
        public CircleHaulVariant Variant;
        public Track StartTrack;
        public Track ReturnTrack;
        public WarehouseMachine FarUnloadMachine;
        public WarehouseMachine FarLoadMachine;
        public WarehouseMachine HomeMachine;
        public List<CargoType> OutCargo;
        public List<CargoType> BackCargo;
        public int CarCount;

        /// <summary>Host: live aus den Tasks. Client: Flag aus dem Paket.</summary>
        public Func<bool> BackRevealedSource;
        public bool BackRevealedRemote;

        public bool HasOut => CircleHaulJobDefinition.AnyCargo(OutCargo);
        public bool HasBack => CircleHaulJobDefinition.AnyCargo(BackCargo);

        public bool IsRevealed(PlanLeg leg) =>
            leg != PlanLeg.Back || (BackRevealedSource != null ? BackRevealedSource() : BackRevealedRemote);

        public List<PlanStep> BuildPlan() =>
            CircleHaulPlan.Build(StartTrack, ReturnTrack, FarUnloadMachine, FarLoadMachine, HomeMachine, OutCargo, BackCargo);

        /// <summary>Host-Definition bevorzugt, sonst die vom Host empfangenen Daten.</summary>
        public static CircleHaulData Get(string jobId)
        {
            if (jobId == null) return null;
            if (CircleHaulJobDefinition.ByJobId.TryGetValue(jobId, out var def) && def != null) return def.ToData();
            return Remote.TryGetValue(jobId, out var data) ? data : null;
        }
    }

    /// <summary>Baut die Schrittfolge – identisch für Tasks (Host) und Booklet (Host und Client).</summary>
    public static class CircleHaulPlan
    {
        public static List<PlanStep> Build(Track start, Track ret, WarehouseMachine farUnload, WarehouseMachine farLoad,
            WarehouseMachine home, List<CargoType> outCargo, List<CargoType> backCargo)
        {
            bool hasOut = CircleHaulJobDefinition.AnyCargo(outCargo);
            bool hasBack = CircleHaulJobDefinition.AnyCargo(backCargo);
            var plan = new List<PlanStep>();
            Track cur = start;

            // Hinfahrt
            var firstDest = hasOut ? farUnload.WarehouseTrack : farLoad.WarehouseTrack;
            plan.Add(new PlanStep { Kind = PlanKind.Haul, From = cur, To = firstDest, CargoPerCar = hasOut ? outCargo : null,
                                    Leg = hasOut ? PlanLeg.Out : PlanLeg.None });
            cur = firstDest;

            if (hasOut)
                plan.Add(new PlanStep { Kind = PlanKind.Unload, Machine = farUnload, To = cur, CargoPerCar = outCargo, Leg = PlanLeg.Out });

            if (hasBack)
            {
                var loadTrack = farLoad.WarehouseTrack;
                if (cur != loadTrack)
                {
                    // Ent- und Ladestelle in B verschieden → umsetzen
                    plan.Add(new PlanStep { Kind = PlanKind.Haul, From = cur, To = loadTrack, InStation = true });
                    cur = loadTrack;
                }
                plan.Add(new PlanStep { Kind = PlanKind.Load, Machine = farLoad, To = cur, CargoPerCar = backCargo, Leg = PlanLeg.Back });

                var homeTrack = home.WarehouseTrack;
                plan.Add(new PlanStep { Kind = PlanKind.Haul, From = cur, To = homeTrack, CargoPerCar = backCargo, Leg = PlanLeg.Back });
                cur = homeTrack;
                plan.Add(new PlanStep { Kind = PlanKind.Unload, Machine = home, To = cur, CargoPerCar = backCargo, Leg = PlanLeg.Back });
            }

            // Abstellen
            plan.Add(new PlanStep
            {
                Kind = PlanKind.Haul, From = cur, To = ret,
                InStation = cur.ID.yardId == ret.ID.yardId,
            });
            return plan;
        }
    }
}
