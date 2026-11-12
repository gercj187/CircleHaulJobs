using System;
using System.Collections.Generic;
using System.Linq;
using DV.Logic.Job;
using DV.ThingTypes;
using UnityEngine;
using static CircleHauls.Jobs.CircleHaulGenerator;

namespace CircleHauls.Jobs
{
    /// <summary>
    /// Zugumläufe aus dem normalen Vanilla-Ablauf – mit den Wagen, die schon in der Welt stehen
    /// (wichtig für Persistenz-Mods wie PersistentJobs, die keine neuen Wagen spawnen):
    ///
    ///   1) Leerzug-Kette (Leerwagen bereitgestellt, z. B. nach dem Entladen)  → Abholung / drittes Ziel (leer hin)
    ///   2) Güterzug-Kette mit bereitgestellten, beladenen Wagen (+ Entladen)   → Zustellung / Containertausch / drittes Ziel
    ///   3) Beladeauftrag fertig, als Nächstes käme der Güterzug                → stattdessen Zugumlauf mit diesen Wagen
    ///
    /// Nur der Host / Singleplayer entscheidet (MpCompat.IsAuthority).
    /// </summary>
    internal static class VanillaIntegration
    {
        /// <summary>
        /// Zufall + Limit "Max. Circle Hauls pro Bahnhof" – gilt für ALLE Zugumläufe des Bahnhofs
        /// (alle sind umgewandelte Vanilla-Ketten). Die gerade betrachtete Kette zählt nicht mit.
        /// </summary>
        private static bool Roll(Station station, JobChainController self)
        {
            var s = CircleHaulsMain.Settings;
            if (s == null || !s.Enabled) { Why = "Mod deaktiviert"; return false; }
            if (!Multiplayer.MpCompat.IsAuthority) { Why = "kein Host"; return false; }
            if (!StationHasRoom(station, self)) { Why = $"Limit {s.MaxJobsPerStation} pro Bahnhof erreicht"; return false; }
            if (Rng.NextDouble() >= s.VanillaChance) { Why = "Zufall (Chance)"; return false; }
            return true;
        }

        /// <summary>Grund der letzten Ablehnung – nur fürs Debug-Log.</summary>
        internal static string Why = "";

        private static T Fail<T>(string why) where T : class { Why = why; return null; }

        private static string Describe(List<StaticJobDefinition> defs) =>
            defs == null ? "null" : string.Join(" + ", defs.Select(d => d == null ? "null" : d.GetType().Name));

        internal static bool StationHasRoom(Station station, JobChainController self)
        {
            if (station == null) return true;
            return ChainRegistry.CountCircleHauls(station.ID, self) < CircleHaulsMain.Settings.MaxJobsPerStation;
        }

        // ------------------------------------------------------------------ 1) + 2): neue Kette

        /// <summary>Vor FinalizeSetupAndGenerateFirstJob einer neuen Vanilla-Kette: ganze Kette durch einen Zugumlauf ersetzen.</summary>
        public static void TryConvertNewChain(JobChainController chain)
        {
            var defs = chain.jobChain;
            if (defs == null || defs.Count == 0 || defs.Any(d => d is CircleHaulJobDefinition)) return;
            string name = chain.jobChainGO != null ? chain.jobChainGO.name : "?";

            bool emptyChain = defs.Count == 1 && defs[0] is StaticEmptyHaulJobDefinition;
            bool loadedChain = defs[0] is StaticTransportJobDefinition &&
                               (defs.Count == 1 || (defs.Count == 2 && defs[1] is StaticShuntingUnloadJobDefinition));
            if (!emptyChain && !loadedChain)
            {
                CircleHaulsMain.Log($"[Neue Kette] {name} [{Describe(defs)}] – passt nicht (nur Leerfahrt oder Güterzug[+Entladen]); " +
                                    (defs[0] is StaticShuntingLoadJobDefinition ? "wird ggf. nach dem Beladen umgewandelt" : "bleibt Vanilla"));
                return;
            }
            if (!Roll(defs[0].logicStation, chain))
            {
                CircleHaulsMain.Log($"[Neue Kette] {name} [{Describe(defs)}] – nicht umgewandelt: {Why}");
                return;
            }

            try
            {
                CircleHaulJobDefinition def = emptyChain
                    ? BuildFromEmpty(chain.jobChainGO, (StaticEmptyHaulJobDefinition)defs[0])
                    : BuildFromLoaded(chain.jobChainGO, (StaticTransportJobDefinition)defs[0]);
                if (def == null)
                {
                    CircleHaulsMain.Log($"[Neue Kette] {name} [{Describe(defs)}] – Umwandlung nicht möglich: {Why}");
                    return;
                }

                // Kette ersetzen – Verknüpfung, Reservierungen und Events macht FinalizeSetup danach selbst
                foreach (var old in defs.ToList()) UnityEngine.Object.Destroy(old);
                defs.Clear();
                chain.jobDefToCurrentlyReservedTracks.Clear();
                chain.AddJobDefinitionToChain(def);

                CircleHaulsMain.Log($"Vanilla-Kette {chain.jobChainGO.name} → Zugumlauf ({def.Variant})");
            }
            catch (Exception ex)
            {
                CircleHaulsMain.Error("Umwandeln einer Vanilla-Kette fehlgeschlagen", ex);
            }
        }

        // ------------------------------------------------------------------ 3): nach dem Beladen

        /// <summary>
        /// Ein Beladeauftrag ist fertig und gleich würde der Güterzug erzeugt (StaticJobDefinition.OnJobCompleted).
        /// Stattdessen den Güterzug in der laufenden Kette durch einen Zugumlauf ersetzen.
        /// </summary>
        public static void TryReplaceAfterShuntingLoad(StaticJobDefinition shuntingLoad)
        {
            var next = shuntingLoad.JobsToGenerateWhenThisJobCompleted;
            if (next == null || next.Count != 1 || !(next[0] is StaticTransportJobDefinition fh))
            {
                CircleHaulsMain.Log($"[Nach Beladen] {shuntingLoad.name} – Folgeauftrag ist kein einzelner Güterzug [{Describe(next)}]");
                return;
            }

            var chain = FindChain(shuntingLoad);
            if (chain == null)
            {
                CircleHaulsMain.Log($"[Nach Beladen] {shuntingLoad.name} – Kette in keinem Bahnhof gefunden");
                return;
            }
            if (!Roll(shuntingLoad.logicStation, chain))
            {
                CircleHaulsMain.Log($"[Nach Beladen] {chain.jobChainGO.name} – nicht umgewandelt: {Why}");
                return;
            }
            int idx = chain.jobChain.IndexOf(fh);
            if (idx < 0) return;

            try
            {
                // Persistenz-Mods (Ausweichgleis) können das Startgleis des Güterzugs ändern – maßgeblich ist,
                // wo die beladenen Wagen wirklich stehen
                var def = BuildFromLoaded(chain.jobChainGO, fh, TrackOfCars(fh.carsToTransport));
                if (def == null)
                {
                    CircleHaulsMain.Log($"[Nach Beladen] {chain.jobChainGO.name} – Umwandlung nicht möglich: {Why}");
                    return;
                }

                var yto = YardTracksOrganizer.Instance;

                // Güterzug UND alle Folgeaufträge (z. B. Rangier-Entladen in B) fallen weg –
                // der Zugumlauf entlädt selbst und bringt die Wagen zurück
                var dropped = chain.jobChain.Skip(idx).ToList();
                foreach (var old in dropped)
                {
                    if (chain.jobDefToCurrentlyReservedTracks.TryGetValue(old, out var oldRes))
                    {
                        foreach (var r in oldRes) yto.ReleaseReservedSpace(r.track, r.reservedLength);
                        chain.jobDefToCurrentlyReservedTracks.Remove(old);
                    }
                }
                var newRes = def.GetRequiredTrackReservations();
                foreach (var r in newRes) yto.ReserveSpace(r.track, r.reservedLength, false);
                chain.jobDefToCurrentlyReservedTracks[def] = newRes;

                // In die Kette einhängen (was ChainJobDefinitions/RegisterEventHandlers beim Start gemacht hätten)
                chain.jobChain.RemoveRange(idx, chain.jobChain.Count - idx);
                chain.jobChain.Add(def);
                shuntingLoad.JobsToGenerateWhenThisJobCompleted = new List<StaticJobDefinition> { def };
                def.JobsToGenerateWhenThisJobCompleted = new List<StaticJobDefinition>();
                def.JobGenerated += chain.OnJobGenerated;
                def.JobGenerated += chain.OnLastJobInChainGenerated; // ist jetzt der letzte Auftrag

                foreach (var old in dropped) UnityEngine.Object.Destroy(old);
                CircleHaulsMain.Log($"Nach dem Beladen: Güterzug in {chain.jobChainGO.name} → Zugumlauf ({def.Variant})");
            }
            catch (Exception ex)
            {
                CircleHaulsMain.Error("Ersetzen des Güterzugs nach dem Beladen fehlgeschlagen", ex);
            }
        }

        private static JobChainController FindChain(StaticJobDefinition def) => ChainRegistry.FindByDefinition(def);

        // ------------------------------------------------------------------ Aufbau aus vorhandenen Wagen

        /// <summary>Beladene Wagen in A (Ziel B laut Vanilla): Zustellung, selten Containertausch oder drittes Ziel.</summary>
        /// <summary>Gleis, auf dem alle Wagen stehen (sonst null).</summary>
        private static Track TrackOfCars(List<Car> cars)
        {
            if (cars == null || cars.Count == 0) return null;
            var t = cars[0].FrontBogieTrack;
            return t != null && cars.All(c => c.FrontBogieTrack == t && c.RearBogieTrack == t) ? t : null;
        }

        private static CircleHaulJobDefinition BuildFromLoaded(GameObject go, StaticTransportJobDefinition fh, Track actualStart = null)
        {
            var s = CircleHaulsMain.Settings;
            var cars = fh.carsToTransport;
            var cargo = fh.transportedCargoPerCar;
            if (cars == null || cargo == null || cars.Count == 0 || cargo.Count != cars.Count || fh.startingTrack == null)
                return Fail<CircleHaulJobDefinition>("Güterzug ohne Wagen/Fracht/Startgleis");

            var scA = StationController.GetStationByYardID(fh.chainData.chainOriginYardId);
            var scB = StationController.GetStationByYardID(fh.chainData.chainDestinationYardId);
            if (scA == null || scB == null || scA == scB)
                return Fail<CircleHaulJobDefinition>($"Bahnhöfe ungültig ({fh.chainData.chainOriginYardId} → {fh.chainData.chainDestinationYardId})");

            var liveries = Liveries(cars);
            if (liveries == null) return Fail<CircleHaulJobDefinition>("Wagen nicht gefunden (TrainCar fehlt)");
            var types = liveries.Select(l => l.parentType).Distinct().ToList();
            bool Carried(CargoType c) => types.All(t => CanCarry(t, c));

            var distinct = cargo.Where(c => c != CargoType.None).Distinct().ToList();
            var farUnload = BestMachine(scB, distinct);
            if (farUnload == null || !distinct.All(farUnload.IsCargoSupported))
                return Fail<CircleHaulJobDefinition>($"{scB.stationInfo.YardID} hat kein Lagergleis für {string.Join(", ", distinct)}");

            var spec = new Spec { Variant = CircleHaulVariant.Delivery, B = scB, FarUnload = farUnload };
            spec.OutSet.AddRange(distinct);

            // Upgrade: drittes Ziel (B → C) oder Containertausch (B → A)
            if (s.EnableForward && Rng.NextDouble() < s.ForwardChance)
                TryAddForwardLeg(spec, scA, Carried);
            if (spec.Variant == CircleHaulVariant.Delivery && s.EnableSwap && distinct.All(IsContainer) && Rng.NextDouble() < s.SwapChance)
                TryAddSwapLeg(spec, scA, Carried);

            // Zustellung abgeschaltet: nur drittes Ziel / Containertausch – sonst bleibt es ein normaler Auftrag
            if (spec.Variant == CircleHaulVariant.Delivery && !s.EnableDelivery)
                return Fail<CircleHaulJobDefinition>("Zustellung deaktiviert, weder drittes Ziel noch Containertausch gewürfelt");

            var backCargo = spec.BackSet.Count > 0
                ? MakeCargoList(cars.Count, spec.BackSet, spec.Variant == CircleHaulVariant.Swap ? s.MissingContainerChance : 0f)
                : Empty(cars.Count);

            float length = CarSpawner.Instance.GetTotalCarsLength(cars, true);
            if (!MachinesFit(spec, length)) return Fail<CircleHaulJobDefinition>($"Zug ({length:0} m) zu lang für das Lagergleis");

            var startTrack = actualStart ?? fh.startingTrack;
            if (actualStart != null && actualStart != fh.startingTrack)
                CircleHaulsMain.Log($"Startgleis laut Wagen {actualStart.ID} statt {fh.startingTrack.ID}");
            var def = Populate(go, scA, spec, cars, liveries, cargo.ToList(), backCargo, startTrack);
            if (def == null) return Fail<CircleHaulJobDefinition>(PopulateWhy);

            // Vanilla belädt bereitgestellte Wagen teils erst beim Erzeugen des Güterzugs
            // (forceCorrectCargoStateOnCars) – der fällt jetzt weg, also hier nachholen
            for (int i = 0; i < cars.Count; i++)
            {
                var car = cars[i];
                if (cargo[i] == CargoType.None || car.CurrentCargoTypeInCar == cargo[i]) continue;
                if (car.CurrentCargoTypeInCar != CargoType.None) continue; // fremde Ladung nicht anfassen
                float amount = fh.cargoAmountPerCar != null && i < fh.cargoAmountPerCar.Count ? fh.cargoAmountPerCar[i] : car.capacity;
                car.LoadCargo(amount, cargo[i], MachinesFor(scA, cargo[i]).FirstOrDefault());
            }
            return def;
        }

        /// <summary>
        /// Leere Wagen in A, Leerfahrt nach B (dort würden sie beladen):
        ///   • B belädt eine Fracht, die A annimmt → Abholung (leer hin, beladen zurück, in A entladen)
        ///   • sonst / per Chance: B belädt für C → drittes Ziel (leer hin, B → C beladen, leer zurück nach A)
        /// Es wird nur Fracht gewählt, die ALLE vorhandenen Wagen tragen können.
        /// </summary>
        private static CircleHaulJobDefinition BuildFromEmpty(GameObject go, StaticEmptyHaulJobDefinition eh)
        {
            var s = CircleHaulsMain.Settings;
            var cars = eh.carsToTransport;
            if (cars == null || cars.Count == 0 || eh.startingTrack == null) return Fail<CircleHaulJobDefinition>("Leerfahrt ohne Wagen/Startgleis");

            var scA = StationController.GetStationByYardID(eh.startingTrack.ID.yardId);
            if (scA == null) return Fail<CircleHaulJobDefinition>($"Bahnhof {eh.startingTrack.ID.yardId} unbekannt");

            var liveries = Liveries(cars);
            if (liveries == null) return Fail<CircleHaulJobDefinition>("Wagen nicht gefunden (TrainCar fehlt)");
            var types = liveries.Select(l => l.parentType).Distinct().ToList();
            bool Carried(CargoType c) => types.All(t => CanCarry(t, c));
            float length = CarSpawner.Instance.GetTotalCarsLength(cars, true);
            string typeNames = string.Join("/", types.Select(t => t.id));

            // B bevorzugt das Ziel der Leerfahrt (dort sollten die Wagen ohnehin beladen werden), danach andere Bahnhöfe
            var scDest = eh.destinationTrack != null ? StationController.GetStationByYardID(eh.destinationTrack.ID.yardId) : null;
            var bList = new List<StationController>();
            if (scDest != null && scDest != scA) bList.Add(scDest);
            bList.AddRange(Shuffle(StationController.allStations.Where(o => o != null && o != scA && o != scDest)));

            var pickups = new List<Spec>();
            var forwards = new List<Spec>();
            foreach (var scB in bList)
            {
                var groups = scB.proceduralJobsRuleset?.outputCargoGroups;
                if (groups == null) continue;
                foreach (var g in groups)
                {
                    if (g?.cargoTypes == null || g.stations == null) continue;
                    foreach (var cargo in g.cargoTypes)
                    {
                        if (!Carried(cargo)) continue;
                        var loadAtB = MachinesFor(scB, cargo);
                        if (loadAtB.Count == 0) continue;
                        var farLoad = Pick(loadAtB);
                        if (farLoad.WarehouseTrack.length < length) continue;

                        // Abholung: B → A
                        if (s.EnablePickup && g.stations.Contains(scA))
                        {
                            var home = MachinesFor(scA, cargo).Where(m => m.WarehouseTrack.length >= length).ToList();
                            if (home.Count > 0)
                            {
                                var p = new Spec { Variant = CircleHaulVariant.Pickup, B = scB, FarLoad = farLoad, Home = Pick(home) };
                                p.BackSet.Add(cargo);
                                pickups.Add(p);
                            }
                        }
                        // drittes Ziel: B → C (C ≠ A, B)
                        if (s.EnableForward)
                        {
                            foreach (var scC in g.stations.Where(o => o != null && o != scA && o != scB))
                            {
                                var atC = MachinesFor(scC, cargo).Where(m => m.WarehouseTrack.length >= length).ToList();
                                if (atC.Count == 0) continue;
                                var f = new Spec { Variant = CircleHaulVariant.Forward, B = scB, C = scC, FarLoad = farLoad, Home = Pick(atC) };
                                f.BackSet.Add(cargo);
                                forwards.Add(f);
                            }
                        }
                    }
                }
                // Ziel der Leerfahrt hat etwas Passendes → dabei bleiben
                if (scB == scDest && (pickups.Count > 0 || forwards.Count > 0)) break;
                if (pickups.Count + forwards.Count >= 40) break;
            }

            if (pickups.Count == 0 && forwards.Count == 0)
                return Fail<CircleHaulJobDefinition>($"keine Fracht für Wagentyp {typeNames} gefunden, die hin und zurück passt");

            // Reihenfolge: per Chance drittes Ziel zuerst, sonst Abholung zuerst; jeweils das andere als Ersatz
            bool forwardFirst = forwards.Count > 0 && (pickups.Count == 0 || Rng.NextDouble() < s.ForwardChance);
            var order = forwardFirst ? Shuffle(forwards).Concat(Shuffle(pickups)) : Shuffle(pickups).Concat(Shuffle(forwards));

            string last = "";
            foreach (var spec in order.Take(10))
            {
                var backCargo = MakeCargoList(cars.Count, spec.BackSet, 0f);
                var def = Populate(go, scA, spec, cars, liveries, Empty(cars.Count), backCargo, eh.startingTrack);
                if (def != null) return def;
                last = PopulateWhy;
            }
            return Fail<CircleHaulJobDefinition>(last);
        }

        private static void TryAddForwardLeg(Spec spec, StationController scA, Func<CargoType, bool> carried)
        {
            var scB = spec.B;
            var groups = scB.proceduralJobsRuleset?.outputCargoGroups?
                .Where(g => g?.cargoTypes != null && g.stations != null && g.stations.Any(o => o != null && o != scA && o != scB))
                .ToList();
            if (groups == null || groups.Count == 0) return;

            var g2 = Pick(groups);
            var scC = Pick(g2.stations.Where(o => o != null && o != scA && o != scB).ToList());
            var legCargos = g2.cargoTypes.Where(c => carried(c) && MachinesFor(scB, c).Count > 0 && MachinesFor(scC, c).Count > 0).ToList();
            if (legCargos.Count == 0) return;

            var legCargo = Pick(legCargos);
            spec.Variant = CircleHaulVariant.Forward;
            spec.C = scC;
            spec.FarLoad = spec.FarUnload != null && spec.FarUnload.IsCargoSupported(legCargo) ? spec.FarUnload : Pick(MachinesFor(scB, legCargo));
            spec.Home = Pick(MachinesFor(scC, legCargo));
            spec.BackSet.Add(legCargo);
        }

        private static void TryAddSwapLeg(Spec spec, StationController scA, Func<CargoType, bool> carried)
        {
            var scB = spec.B;
            var backCargos = scA.proceduralJobsRuleset?.inputCargoGroups?
                .Where(g => g?.cargoTypes != null && g.stations != null && g.stations.Contains(scB))
                .SelectMany(g => g.cargoTypes).Where(c => IsContainer(c) && carried(c)).Distinct().ToList();
            if (backCargos == null || backCargos.Count == 0) return;

            var farLoad = backCargos.Any(spec.FarUnload.IsCargoSupported) ? spec.FarUnload : BestMachine(scB, backCargos);
            if (farLoad == null) return;
            backCargos = backCargos.Where(farLoad.IsCargoSupported).ToList();
            var home = BestMachine(scA, backCargos);
            if (home == null) return;
            backCargos = backCargos.Where(home.IsCargoSupported).ToList();
            if (backCargos.Count == 0) return;

            spec.Variant = CircleHaulVariant.Swap;
            spec.FarLoad = farLoad;
            spec.Home = home;
            spec.BackSet.AddRange(Shuffle(backCargos).Take(Rng.Next(1, 4)));
        }

        private static List<TrainCarLivery> Liveries(List<Car> cars)
        {
            var trainCars = TrainCar.ExtractTrainCars(cars);
            if (trainCars == null || trainCars.Count != cars.Count || trainCars.Any(tc => tc == null || tc.carLivery == null)) return null;
            return trainCars.Select(tc => tc.carLivery).ToList();
        }
    }
}
