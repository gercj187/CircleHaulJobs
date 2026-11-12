using System;
using System.Collections.Generic;
using System.Linq;
using CircleHauls.Jobs;
using DV.JObjectExtstensions;
using DV.Logic.Job;
using DV.ThingTypes;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CircleHauls.Saving
{
    /// <summary>
    /// CH-Ketten liegen in einem eigenen Schlüssel im Savegame (wie bei PassengerJobs).
    /// Wird die Mod entfernt, bleibt der Vanilla-Teil des Saves lesbar.
    /// </summary>
    internal static class CircleHaulSaveData
    {
        private const string DataKey = "circlehauls_mod";
        private const string VersionKey = "version";
        private const int CurrentVersion = 1;

        public static JObject LoadedData;

        public static void Inject(SaveGameData mainData)
        {
            try
            {
                var chains = StationController.allStations
                    .Select(sc => sc.ProceduralJobsController)
                    .Where(c => c != null)
                    .SelectMany(c => c.GetCurrentJobChains())
                    .Where(CircleHaulChainController.IsCircleHaul)
                    .Distinct()
                    .Select(c => c.GetJobChainSaveData())
                    .Where(d => d is CircleHaulChainSaveData)
                    .ToArray();

                var data = new JObject();
                data.SetObjectViaJSON(SaveGameKeys.Jobs, new JobsSaveGameData(chains, 0f), JobSaveManager.serializeSettings);
                data.SetInt(VersionKey, CurrentVersion);
                mainData.SetJObject(DataKey, data);

                CircleHaulsMain.Log($"{chains.Length} Circle Haul(s) gespeichert");
            }
            catch (Exception ex)
            {
                CircleHaulsMain.Error("Speichern der Circle Hauls fehlgeschlagen", ex);
            }
        }

        public static void Extract(SaveGameData mainData)
        {
            LoadedData = mainData?.GetJObject(DataKey);
        }

        /// <summary>Unsere Ketten in die Vanilla-Liste mischen, damit LoadJobChain sie sieht.</summary>
        public static void Merge(JobsSaveGameData mainJobs)
        {
            try
            {
                if (LoadedData?.GetObjectViaJSON<JobsSaveGameData>(SaveGameKeys.Jobs, JobSaveManager.serializeSettings) is JobsSaveGameData own
                    && own.jobChains != null)
                {
                    var main = mainJobs.jobChains ?? Array.Empty<JobChainSaveData>();
                    var knownIds = new HashSet<string>(main.Select(c => c?.firstJobId).Where(id => id != null));
                    var knownCars = new HashSet<string>(main.Where(c => c?.trainCarGuids != null).SelectMany(c => c.trainCarGuids));

                    // Ältere Versionen haben Ketten doppelt gespeichert → nach Job-ID und Wagen entdoppeln
                    var unique = new List<JobChainSaveData>();
                    int dropped = 0;
                    foreach (var chain in own.jobChains)
                    {
                        if (chain == null || chain.firstJobId == null || !knownIds.Add(chain.firstJobId) ||
                            (chain.trainCarGuids != null && chain.trainCarGuids.Any(knownCars.Contains)))
                        {
                            dropped++;
                            continue;
                        }
                        if (chain.trainCarGuids != null) knownCars.UnionWith(chain.trainCarGuids);
                        unique.Add(chain);
                    }

                    if (dropped > 0) CircleHaulsMain.Warning($"{dropped} doppelte Circle-Haul-Kette(n) beim Laden verworfen");
                    mainJobs.jobChains = main.Concat(unique).ToArray();
                }
            }
            catch (Exception ex)
            {
                CircleHaulsMain.Error("Laden der Circle Hauls fehlgeschlagen", ex);
            }
        }

        public static GameObject LoadChain(CircleHaulChainSaveData save, List<JobBooklet> booklets)
        {
            var jsm = JobSaveManager.Instance;
            var data = save.jobChainData?.OfType<CircleHaulJobDefinitionData>().FirstOrDefault();
            if (data == null)
            {
                CircleHaulsMain.Warning("Circle-Haul-Kette ohne Definition – übersprungen");
                return null;
            }

            var cars = jsm.GetCarsFromCarGuids(data.transportCarGuids);
            var station = jsm.GetStationWithId(data.stationId);
            var start = jsm.GetYardTrackWithId(data.startTrackId);
            var ret = jsm.GetYardTrackWithId(data.returnTrackId);
            var variant = (CircleHaulVariant)data.variant;

            WarehouseMachine Machine(string id) => string.IsNullOrEmpty(id) ? null : jsm.GetWarehouseMachineWithId(id);
            var farUnload = Machine(data.farUnloadMachineId);
            var farLoad = Machine(data.farLoadMachineId);
            var home = Machine(data.homeMachineId);

            List<CargoType> outCargo = data.outCargo?.Select(c => (CargoType)c).ToList();
            List<CargoType> backCargo = data.backCargo?.Select(c => (CargoType)c).ToList();

            // Alte Testversion: eine Fracht, eine Maschine in B
            if (outCargo == null && backCargo == null && cars != null)
            {
                var legacyFar = Machine(data.farMachineId);
                var cargo = (CargoType)data.cargoType;
                var full = Enumerable.Repeat(cargo, cars.Count).ToList();
                var empty = Enumerable.Repeat(CargoType.None, cars.Count).ToList();
                if (variant == CircleHaulVariant.Pickup) { outCargo = empty; backCargo = full; farLoad = legacyFar; }
                else { outCargo = full; backCargo = empty; farUnload = legacyFar; }
            }

            bool hasOut = CircleHaulJobDefinition.AnyCargo(outCargo);
            bool hasBack = CircleHaulJobDefinition.AnyCargo(backCargo);

            if (cars == null || station == null || start == null || ret == null ||
                (outCargo != null && outCargo.Count != cars.Count) || (backCargo != null && backCargo.Count != cars.Count) ||
                (!hasOut && !hasBack) || (hasOut && farUnload == null) || (hasBack && (farLoad == null || home == null)))
            {
                CircleHaulsMain.Warning($"Circle Haul {save.firstJobId}: Daten unvollständig – übersprungen");
                return null;
            }

            var go = new GameObject($"[LOADED] ChainJob[CircleHaul]: {data.originStationId} - {data.destinationStationId}");
            var chain = new CircleHaulChainController(go) { carsForJobChain = cars };

            var def = go.AddComponent<CircleHaulJobDefinition>();
            def.PopulateBaseJobDefinition(station, data.timeLimitForJob, data.initialWage,
                new StationsChainData(data.originStationId, data.destinationStationId), (JobLicenses)data.requiredLicenses);
            def.ForceJobId(save.firstJobId);
            def.Variant = variant;
            def.Cars = cars;
            def.StartTrack = start;
            def.ReturnTrack = ret;
            def.FarUnloadMachine = farUnload;
            def.FarLoadMachine = farLoad;
            def.HomeMachine = home;
            def.OutCargo = outCargo;
            def.BackCargo = backCargo;

            chain.AddJobDefinitionToChain(def);
            chain.FinalizeSetupAndGenerateFirstJob(true);

            if (chain.currentJobInChain == null)
            {
                CircleHaulsMain.Warning($"Circle Haul {save.firstJobId}: Job nicht wiederherstellbar");
                chain.DestroyChain();
                return null;
            }

            if (save.jobTaken)
            {
                JobsManager.Instance.TakeJob(chain.currentJobInChain, true);
                if (save.currentJobTaskData != null)
                {
                    try { chain.currentJobInChain.OverrideTasksStates(save.currentJobTaskData); }
                    catch (Exception ex)
                    {
                        // z. B. Auftrag aus älterer Version mit anderem Ablauf – Fortschritt geht verloren, Job bleibt
                        CircleHaulsMain.Warning($"Circle Haul {save.firstJobId}: Fortschritt nicht übernehmbar ({ex.Message})");
                    }
                }
                jsm.InitializeCorrespondingJobBooklet(chain.currentJobInChain, booklets);
            }

            CircleHaulsMain.Log($"Circle Haul {save.firstJobId} geladen");
            return go;
        }
    }
}
