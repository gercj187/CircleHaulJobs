using System;
using System.Collections.Generic;
using System.Linq;
using DV.Logic.Job;
using DV.ThingTypes;
using UnityEngine;

namespace CircleHauls.Jobs
{
    public enum PlanKind { Haul, Unload, Load }

    /// <summary>Zu welcher Fahrt gehört der Schritt (für das spätere Aufdecken fehlender Container).</summary>
    public enum PlanLeg { None, Out, Back }

    /// <summary>Ein Schritt des Auftrags – daraus entstehen die Tasks UND die Booklet-Seiten.</summary>
    public class PlanStep
    {
        public PlanKind Kind;
        public Track From;
        public Track To;
        public WarehouseMachine Machine;
        /// <summary>Fracht pro Wagen (CargoType.None = Wagen ohne Ladung/Container), null = alle leer.</summary>
        public List<CargoType> CargoPerCar;
        /// <summary>Rangierfahrt innerhalb eines Bahnhofs.</summary>
        public bool InStation;
        public PlanLeg Leg;
    }

    /// <summary>
    /// Ein Circle-Haul-Auftrag = EIN Job mit einer SequentialTasks-Kette aus Vanilla-Tasks.
    ///   Zustellung:      Hinfahrt beladen                    → 1 Behandlung (entladen in B)
    ///   Abholung:        Rückfahrt beladen                   → 2 Behandlungen (beladen in B, entladen in A)
    ///   Containertausch: Hin- UND Rückfahrt beladen          → 3 Behandlungen (entladen + beladen in B, entladen in A)
    /// Am Ende werden die Wagen immer auf ein Abstellgleis (Storage) in A gestellt.
    /// </summary>
    public class CircleHaulJobDefinition : StaticJobDefinition
    {
        /// <summary>Für das Booklet: Job-ID → Definition (Ablauf + Fracht pro Wagen).</summary>
        public static readonly Dictionary<string, CircleHaulJobDefinition> ByJobId = new Dictionary<string, CircleHaulJobDefinition>();

        public CircleHaulVariant Variant;
        public List<Car> Cars;

        public Track StartTrack;                  // A: hier stehen die Wagen am Anfang
        public WarehouseMachine FarUnloadMachine; // B: Entladestelle (wenn Hinfahrt beladen)
        public WarehouseMachine FarLoadMachine;   // B: Ladestelle (wenn Rückfahrt beladen)
        public WarehouseMachine HomeMachine;      // A: Entladestelle (wenn Rückfahrt beladen)
        public Track ReturnTrack;                 // A: Abstellgleis am Ende

        public List<CargoType> OutCargo;          // Fracht pro Wagen auf der Hinfahrt (None = leer)
        public List<CargoType> BackCargo;         // Fracht pro Wagen auf der Rückfahrt (None = leer)

        public static bool AnyCargo(List<CargoType> list) => list != null && list.Any(c => c != CargoType.None);

        public bool HasOut => AnyCargo(OutCargo);
        public bool HasBack => AnyCargo(BackCargo);

        public List<PlanStep> BuildPlan() =>
            CircleHaulPlan.Build(StartTrack, ReturnTrack, FarUnloadMachine, FarLoadMachine, HomeMachine, OutCargo, BackCargo);

        /// <summary>Daten für Booklet und Multiplayer-Paket.</summary>
        public CircleHaulData ToData() => new CircleHaulData
        {
            JobId = job?.ID,
            Variant = Variant,
            StartTrack = StartTrack,
            ReturnTrack = ReturnTrack,
            FarUnloadMachine = FarUnloadMachine,
            FarLoadMachine = FarLoadMachine,
            HomeMachine = HomeMachine,
            OutCargo = OutCargo,
            BackCargo = BackCargo,
            CarCount = Cars?.Count ?? 0,
            BackRevealedSource = () => BackRevealed,
        };

        public override void GenerateJob(Station jobOriginStation, float timeLimit = 0f, float initialWage = 0f,
            string forcedJobId = null, JobLicenses requiredLicenses = JobLicenses.Basic)
        {
            if (Cars == null || Cars.Count == 0 || StartTrack == null || ReturnTrack == null ||
                (!HasOut && !HasBack) ||
                (HasOut && FarUnloadMachine == null) ||
                (HasBack && (FarLoadMachine == null || HomeMachine == null)))
            {
                CircleHaulsMain.Warning("CircleHaul: unvollständige Job-Definition, Job wird nicht erzeugt");
                return;
            }

            var plan = BuildPlan();
            var tasks = new List<Task>();
            _revealOutTasks.Clear();
            _revealBackTasks.Clear();

            for (int i = 0; i < plan.Count; i++)
            {
                var step = plan[i];
                bool last = i == plan.Count - 1;

                if (step.Kind == PlanKind.Haul)
                {
                    tasks.Add(JobsGenerator.CreateTransportTask(Cars, step.To, step.From, TransportCargo(step.CargoPerCar), last));
                }
                else
                {
                    var type = step.Kind == PlanKind.Load ? WarehouseTaskType.Loading : WarehouseTaskType.Unloading;
                    // eine WarehouseTask pro Frachtart, nur mit den Wagen, die sie betrifft (fehlende Container: kein Wagen)
                    foreach (var cargo in step.CargoPerCar.Where(c => c != CargoType.None).Distinct())
                    {
                        var subset = Cars.Where((c, idx) => step.CargoPerCar[idx] == cargo).ToList();
                        float amount = subset.Sum(c => c.capacity);
                        var wt = new WarehouseTask(subset, type, step.Machine, cargo, amount, 0L, false);
                        tasks.Add(wt);
                        // Hinfahrt: aufgedeckt nach dem Entladen in B, Rückfahrt: nach dem Beladen in B
                        if (step.Leg == PlanLeg.Out && step.Kind == PlanKind.Unload) _revealOutTasks.Add(wt);
                        if (step.Leg == PlanLeg.Back && step.Kind == PlanKind.Load) _revealBackTasks.Add(wt);
                    }
                }
            }

            var sequence = new SequentialTasks(tasks, 0L);
            job = new Job(sequence, CHJobType.CircleHaul, timeLimit, initialWage, chainData, forcedJobId, requiredLicenses);
            ByJobId[job.ID] = this;

            // LongerLoadingDelay: Lade-/Entladezeit ALLER WarehouseTasks aufs Zeitlimit.
            // Gespeichert wird nur das Basis-Limit (timeLimitForJob), daher beim Laden erneut korrekt angewendet.
            LldCompat.ApplyTimeLimit(job);

            jobOriginStation.AddJobToStation(job);

            // Multiplayer: Clients brauchen Ablauf + Fracht pro Wagen fürs Booklet (sie haben keine Definition)
            Multiplayer.MpCompat.BroadcastJob(this);
        }

        /// <summary>
        /// TransportTask prüft die Fracht pro Wagen. Leer oder teilbeladen (fehlende Container) → null,
        /// die Ladung selbst prüfen dann die WarehouseTasks. Niemals CargoType.None an Vanilla geben.
        /// </summary>
        private static List<CargoType> TransportCargo(List<CargoType> perCar)
        {
            if (perCar == null || perCar.Any(c => c == CargoType.None)) return null;
            return perCar;
        }

        // ------------------------------------------------------------ fehlende Container erst später zeigen

        private readonly List<WarehouseTask> _revealOutTasks = new List<WarehouseTask>();
        private readonly List<WarehouseTask> _revealBackTasks = new List<WarehouseTask>();
        private bool _backUpdateDone;

        /// <summary>Hinfahrt: so wie gespawnt – Wagen ohne Container sind von Anfang an leer zu sehen.</summary>
        public bool OutRevealed => true;

        /// <summary>Rückfahrt: wird voll angezeigt, bis das Beladen in B abgeschlossen ist.</summary>
        public bool BackRevealed => _revealBackTasks.Count > 0 && _revealBackTasks.All(t => t.state == TaskState.Done);

        public bool IsRevealed(PlanLeg leg) => leg != PlanLeg.Back || BackRevealed;

        /// <summary>
        /// Von TaskSetState_Patch aufgerufen, wenn eine WarehouseTask dieses Jobs fertig wird (kein Polling).
        /// Ist danach das Beladen in B komplett und sind Wagen leer geblieben → Booklet EINMAL neu rendern.
        /// </summary>
        public void OnWarehouseTaskDone(WarehouseTask task)
        {
            if (_backUpdateDone || !_revealBackTasks.Contains(task) || !BackRevealed) return;
            _backUpdateDone = true;

            if (BackCargo != null && BackCargo.Contains(CargoType.None))
            {
                RerenderBooklets();
                Multiplayer.MpCompat.BroadcastReveal(job.ID);
            }
        }

        private void RerenderBooklets() => RerenderBooklets(job?.ID);

        /// <summary>Alle Booklets eines Jobs neu rendern (Host und Client).</summary>
        public static void RerenderBooklets(string jobId)
        {
            if (jobId == null) return;
            try
            {
                foreach (var booklet in JobBooklet.allExistingJobBooklets.ToList())
                {
                    if (booklet == null || booklet.job == null || booklet.job.ID != jobId) continue;
                    DV.Booklets.BookletCreator_Job.Render(booklet.gameObject, new DV.Booklets.Job_data(booklet.job));
                    CircleHaulsMain.Log($"Booklet {jobId} neu gerendert");
                }
            }
            catch (Exception ex)
            {
                CircleHaulsMain.Warning($"Booklet {jobId} konnte nicht neu gerendert werden: {ex.Message}");
            }
        }

        private void OnDestroy()
        {
            foreach (var key in ByJobId.Where(kv => kv.Value == this).Select(kv => kv.Key).ToList())
            {
                ByJobId.Remove(key);
                Multiplayer.MpCompat.BroadcastRemove(key);
            }
        }

        public override List<TrackReservation> GetRequiredTrackReservations()
        {
            float length = CarSpawner.Instance.GetTotalCarsLength(Cars, true);
            var tracks = new HashSet<Track>();
            if (FarUnloadMachine != null && HasOut) tracks.Add(FarUnloadMachine.WarehouseTrack);
            if (FarLoadMachine != null && HasBack) tracks.Add(FarLoadMachine.WarehouseTrack);
            if (HomeMachine != null && HasBack) tracks.Add(HomeMachine.WarehouseTrack);
            if (ReturnTrack != null) tracks.Add(ReturnTrack);
            return tracks.Select(t => new TrackReservation(t, length)).ToList();
        }

        public override JobDefinitionDataBase GetJobDefinitionSaveData()
        {
            var guids = GetGuidsFromCars(Cars) ?? throw new Exception("CircleHaul: Wagen-GUIDs nicht lesbar");

            return new CircleHaulJobDefinitionData(
                timeLimitForJob, initialWage, logicStation.ID, chainData.chainOriginYardId, chainData.chainDestinationYardId,
                (int)requiredLicenses, guids, (int)Variant,
                StartTrack.ID.FullID, ReturnTrack.ID.FullID,
                FarUnloadMachine?.ID, FarLoadMachine?.ID, HomeMachine?.ID,
                OutCargo?.Select(c => (int)c).ToArray(), BackCargo?.Select(c => (int)c).ToArray(),
                0, null);
        }
    }

    public class CircleHaulJobDefinitionData : JobDefinitionDataBase
    {
        public string[] transportCarGuids;
        public int variant;
        public string startTrackId;
        public string returnTrackId;
        public string farUnloadMachineId;
        public string farLoadMachineId;
        public string homeMachineId;
        public int[] outCargo;
        public int[] backCargo;

        // Alt (erste Testversion): eine Fracht + eine Maschine in B – wird beim Laden umgerechnet
        public int cargoType;
        public string farMachineId;

        [Newtonsoft.Json.JsonConstructor]
        public CircleHaulJobDefinitionData(float timeLimitForJob, float initialWage, string stationId, string originStationId,
            string destinationStationId, int requiredLicenses, string[] transportCarGuids, int variant,
            string startTrackId, string returnTrackId, string farUnloadMachineId, string farLoadMachineId, string homeMachineId,
            int[] outCargo, int[] backCargo, int cargoType, string farMachineId)
            : base(timeLimitForJob, initialWage, stationId, originStationId, destinationStationId, requiredLicenses)
        {
            this.transportCarGuids = transportCarGuids;
            this.variant = variant;
            this.startTrackId = startTrackId;
            this.returnTrackId = returnTrackId;
            this.farUnloadMachineId = farUnloadMachineId;
            this.farLoadMachineId = farLoadMachineId;
            this.homeMachineId = homeMachineId;
            this.outCargo = outCargo;
            this.backCargo = backCargo;
            this.cargoType = cargoType;
            this.farMachineId = farMachineId;
        }
    }
}
