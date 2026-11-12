using System;
using System.Collections.Generic;
using System.Linq;
using DV;
using DV.Logic.Job;
using DV.ThingTypes;
using DV.ThingTypes.TransitionHelpers;
using UnityEngine;
using Random = System.Random;

namespace CircleHauls.Jobs
{
    /// <summary>
    /// Hilfsfunktionen zum Finden passender Ziele/Fracht. Diese Mod spawnt KEINE Wagen –
    /// Zugumläufe entstehen ausschließlich aus Vanilla-Ketten (VanillaIntegration).
    /// </summary>
    public static class CircleHaulGenerator
    {
        private const int MaxCargoTypesPerLeg = 3;

        internal static readonly Random Rng = new Random();

        /// <summary>Alles, was einen Zugumlauf ausmacht (Ziele, Maschinen, Fracht). Wagen kommen IMMER aus einer Vanilla-Kette.</summary>
        internal class Spec
        {
            public CircleHaulVariant Variant;
            public StationController B;
            /// <summary>Wo die Rückfracht entladen wird: A (normal) oder C (drittes Ziel).</summary>
            public StationController C;
            public WarehouseMachine FarUnload, FarLoad, Home;
            public List<CargoType> OutSet = new List<CargoType>(), BackSet = new List<CargoType>();
            public TrainCarType_v2 CarType;
        }

        // ------------------------------------------------------------------ Auftragsarten finden

        /// <summary>Zustellung (A → B, entladen) bzw. Abholung (B → A, beladen + entladen) mit einer Fracht.</summary>
        internal static Spec FindSimple(StationController scA, CircleHaulVariant variant)
        {
            var rules = scA.proceduralJobsRuleset;
            if (rules == null) return null;
            bool pickup = variant == CircleHaulVariant.Pickup;

            // Zustellung: was A verschickt (output) → B muss es entladen können
            // Abholung:   was A empfängt  (input)  → B muss es beladen, A entladen können
            var groups = pickup ? rules.inputCargoGroups : rules.outputCargoGroups;
            if (groups == null || groups.Count == 0) return null;

            var group = Pick(groups);
            if (group.cargoTypes == null || group.cargoTypes.Count == 0 || group.stations == null) return null;
            var cargo = Pick(group.cargoTypes);

            var spec = new Spec { Variant = variant };
            if (pickup)
            {
                var homes = MachinesFor(scA, cargo);
                if (homes.Count == 0) return null;
                spec.Home = Pick(homes);
            }

            var dests = group.stations.Where(o => o != null && o != scA && MachinesFor(o, cargo).Count > 0).ToList();
            if (dests.Count == 0) return null;
            spec.B = Pick(dests);
            var far = Pick(MachinesFor(spec.B, cargo));

            if (pickup) { spec.FarLoad = far; spec.BackSet.Add(cargo); }
            else { spec.FarUnload = far; spec.OutSet.Add(cargo); }

            spec.CarType = PickCarType(spec.OutSet.Concat(spec.BackSet).ToList());
            return spec.CarType != null ? spec : null;
        }

        /// <summary>
        /// Containertausch: hin mit Containern (voll oder leer), in B entladen, andere Container laden,
        /// zurück nach A und dort entladen. Nur Container-Fracht, bis zu 3 Arten je Fahrt.
        /// </summary>
        internal static Spec FindSwap(StationController scA)
        {
            var rules = scA.proceduralJobsRuleset;
            if (rules?.outputCargoGroups == null || rules.inputCargoGroups == null) return null;

            var outGroups = rules.outputCargoGroups.Where(g => g?.cargoTypes != null && g.stations != null && g.cargoTypes.Any(IsContainer)).ToList();
            if (outGroups.Count == 0) return null;
            var g1 = Pick(outGroups);

            var bCandidates = g1.stations.Where(o => o != null && o != scA).ToList();
            if (bCandidates.Count == 0) return null;
            var scB = Pick(bCandidates);

            // Hinfahrt: Container, die A nach B schickt und B entladen kann
            var outCargos = g1.cargoTypes.Where(IsContainer).ToList();
            var farUnload = BestMachine(scB, outCargos);
            if (farUnload == null) return null;
            outCargos = outCargos.Where(farUnload.IsCargoSupported).ToList();

            // Rückfahrt: Container, die B nach A schickt (A-Input mit B als Quelle), B laden und A entladen kann
            var backCargos = rules.inputCargoGroups
                .Where(g => g?.cargoTypes != null && g.stations != null && g.stations.Contains(scB))
                .SelectMany(g => g.cargoTypes).Where(IsContainer).Distinct().ToList();
            if (backCargos.Count == 0) return null;

            // gleiche Maschine für Ent- und Beladen bevorzugen (kein Umsetzen in B)
            var farLoad = backCargos.Any(farUnload.IsCargoSupported) ? farUnload : BestMachine(scB, backCargos);
            if (farLoad == null) return null;
            backCargos = backCargos.Where(farLoad.IsCargoSupported).ToList();

            var home = BestMachine(scA, backCargos);
            if (home == null) return null;
            backCargos = backCargos.Where(home.IsCargoSupported).ToList();
            if (backCargos.Count == 0 || outCargos.Count == 0) return null;

            // Wagentyp, der möglichst viele dieser Container tragen kann
            var carType = PickCarType(outCargos.Concat(backCargos).ToList(), allowPartial: true);
            if (carType == null) return null;
            outCargos = outCargos.Where(c => CanCarry(carType, c)).ToList();
            backCargos = backCargos.Where(c => CanCarry(carType, c)).ToList();
            if (outCargos.Count == 0 || backCargos.Count == 0) return null;

            var spec = new Spec
            {
                Variant = CircleHaulVariant.Swap, B = scB,
                FarUnload = farUnload, FarLoad = farLoad, Home = home, CarType = carType,
            };
            spec.OutSet.AddRange(Shuffle(outCargos).Take(Rng.Next(1, MaxCargoTypesPerLeg + 1)));
            spec.BackSet.AddRange(Shuffle(backCargos).Take(Rng.Next(1, MaxCargoTypesPerLeg + 1)));
            return spec;
        }

        /// <summary>
        /// Drittes Ziel: A → B (mit Fracht, die A nach B schickt – oder leer), in B entladen/beladen,
        /// mit Fracht, die B nach C schickt, nach C, dort entladen und leer zurück nach A aufs Abstellgleis.
        /// </summary>
        internal static Spec FindForward(StationController scA)
        {
            var rulesA = scA.proceduralJobsRuleset;
            if (rulesA?.outputCargoGroups == null) return null;

            // B: ein Ziel, das A beliefert (sonst irgendeine Station, die A in ihren Gruppen kennt)
            var bCandidates = rulesA.outputCargoGroups.Where(g => g?.stations != null)
                .SelectMany(g => g.stations).Where(o => o != null && o != scA).Distinct().ToList();
            if (bCandidates.Count == 0) return null;
            var scB = Pick(bCandidates);

            // Leg B → C: was B verschickt, C (≠ A, ≠ B) annimmt
            var rulesB = scB.proceduralJobsRuleset;
            if (rulesB?.outputCargoGroups == null) return null;
            var legGroups = rulesB.outputCargoGroups
                .Where(g => g?.cargoTypes != null && g.stations != null && g.stations.Any(o => o != null && o != scA && o != scB))
                .ToList();
            if (legGroups.Count == 0) return null;
            var g2 = Pick(legGroups);
            var scC = Pick(g2.stations.Where(o => o != null && o != scA && o != scB).ToList());

            var legCargos = g2.cargoTypes.Where(c => MachinesFor(scB, c).Count > 0 && MachinesFor(scC, c).Count > 0).ToList();
            if (legCargos.Count == 0) return null;
            var legCargo = Pick(legCargos);
            var farLoad = Pick(MachinesFor(scB, legCargo));
            var home = Pick(MachinesFor(scC, legCargo));

            var spec = new Spec { Variant = CircleHaulVariant.Forward, B = scB, C = scC, FarLoad = farLoad, Home = home };
            spec.BackSet.Add(legCargo);

            // Hinfahrt A → B beladen, wenn es passende Fracht gibt, die derselbe Wagentyp trägt – sonst leer hin
            var outCargos = rulesA.outputCargoGroups
                .Where(g => g?.cargoTypes != null && g.stations != null && g.stations.Contains(scB))
                .SelectMany(g => g.cargoTypes).Distinct()
                .Where(c => MachinesFor(scB, c).Count > 0)
                .ToList();

            TrainCarType_v2 carType = null;
            foreach (var outCargo in Shuffle(outCargos))
            {
                carType = PickCarType(new List<CargoType> { outCargo, legCargo });
                if (carType == null) continue;
                spec.OutSet.Add(outCargo);
                // gleiche Maschine für Ent- und Beladen bevorzugen
                var sameMachine = MachinesFor(scB, outCargo).FirstOrDefault(m => m.IsCargoSupported(legCargo));
                spec.FarUnload = sameMachine ?? Pick(MachinesFor(scB, outCargo));
                if (sameMachine != null) spec.FarLoad = sameMachine;
                break;
            }
            spec.CarType = carType ?? PickCarType(new List<CargoType> { legCargo });
            return spec.CarType != null ? spec : null;
        }

        // ------------------------------------------------------------------ Gemeinsamer Aufbau

        internal static string ChainName(StationController scA, Spec spec)
        {
            string a = scA.stationInfo.YardID, b = spec.B.stationInfo.YardID;
            return spec.C != null ? $"{a} - {b} - {spec.C.stationInfo.YardID} - {a}" : $"{a} - {b} - {a}";
        }

        internal static bool MachinesFit(Spec spec, float trainLength)
        {
            foreach (var m in new[] { spec.FarUnload, spec.FarLoad, spec.Home })
                if (m != null && m.WarehouseTrack.length < trainLength) return false;
            return true;
        }

        /// <summary>Kein Wagen darf auf beiden Fahrten leer sein (fehlende Container werden je Fahrt ausgewürfelt).</summary>
        internal static void FixDoubleEmpty(Spec spec, List<CargoType> outCargo, List<CargoType> backCargo)
        {
            int count = outCargo.Count;
            var outFull = MakeCargoList(count, spec.OutSet, 0f);
            var backFull = MakeCargoList(count, spec.BackSet, 0f);
            for (int i = 0; i < count; i++)
            {
                if (outCargo[i] != CargoType.None || backCargo[i] != CargoType.None) continue;
                if (Rng.Next(2) == 0) outCargo[i] = NeighbourOr(outCargo, i, outFull[i]);
                else backCargo[i] = NeighbourOr(backCargo, i, backFull[i]);
            }
        }

        /// <summary>Lizenzen: Rangieren + Logistik + Fracht immer, plus Fracht-/Wagen-/Längenlizenzen.</summary>
        internal static bool LicensesOk(Spec spec, List<CargoType> outCargo, List<CargoType> backCargo,
            IEnumerable<TrainCarType_v2> carTypes, int count, out JobLicenses flags)
        {
            var allCargo = outCargo.Concat(backCargo).Where(c => c != CargoType.None).Distinct().ToList();
            var lm = LicenseManager.Instance;
            var licenses = new HashSet<JobLicenseType_v2>
            {
                JobLicenses.Shunting.ToV2(),
                JobLicenses.LogisticalHaul.ToV2(),
                JobLicenses.FreightHaul.ToV2(),
            };
            licenses.UnionWith(lm.GetRequiredLicensesForCargoTypes(allCargo));
            licenses.UnionWith(lm.GetRequiredLicensesForCarTypes(carTypes.Distinct().ToList()));
            var lengthLicense = lm.GetRequiredLicenseForNumberOfTransportedCars(count);
            if (lengthLicense != null) licenses.Add(lengthLicense);
            licenses.RemoveWhere(l => l == null);

            flags = JobLicenseType_v2.ListToFlags(licenses);
            return !CircleHaulsMain.Settings.OnlyLicensedJobs || lm.IsLicensedForJob(licenses);
        }

        /// <summary>
        /// Setzt die Job-Definition auf (Rückgleis, Lizenzen, Lohn, Zeit) – für gespawnte UND vorhandene Wagen
        /// (Vanilla-Integration). Liefert null, wenn kein Abstellgleis frei ist oder Lizenzen fehlen.
        /// </summary>
        /// <summary>Grund, warum Populate null geliefert hat – nur fürs Debug-Log.</summary>
        internal static string PopulateWhy = "";

        internal static CircleHaulJobDefinition Populate(GameObject chainGO, StationController scA, Spec spec,
            List<Car> cars, List<TrainCarLivery> liveries, List<CargoType> outCargo, List<CargoType> backCargo, Track startTrack)
        {
            var s = CircleHaulsMain.Settings;
            int count = cars.Count;
            float trainLength = CarSpawner.Instance.GetTotalCarsLength(cars, true);

            // Am Ende immer auf ein Abstellgleis (Storage) in A – notfalls das Startgleis
            var yto = YardTracksOrganizer.Instance;
            var returnCandidates = yto.FilterOutTracksWithoutRequiredFreeSpace(
                scA.logicStation.yard.StorageTracks.Where(t => t != startTrack).ToList(), trainLength);
            // Volle Bahnhöfe (z. B. mit Persistenz-Mods): notfalls zurück aufs Startgleis – das wird mit der Abfahrt frei
            var returnTrack = returnCandidates.Count > 0 ? Pick(returnCandidates) : startTrack;
            if (returnTrack == null) { PopulateWhy = "kein Rückgleis in " + scA.stationInfo.YardID; return null; }
            if (returnCandidates.Count == 0)
                CircleHaulsMain.Log($"Kein freies Abstellgleis in {scA.stationInfo.YardID} – Rückkehr aufs Startgleis {startTrack.ID}");

            var carTypes = liveries.Select(l => l.parentType).Where(t => t != null).Distinct().ToList();
            if (!LicensesOk(spec, outCargo, backCargo, carTypes, count, out var licenseFlags))
            { PopulateWhy = "Lizenzen fehlen (Einstellung 'Nur Aufträge mit Lizenz')"; return null; }

            // Bezahlung = Summe der Einzelaufträge × Faktor × Behandlungsbonus × LLD-Ladezeit
            bool hasOut = CircleHaulJobDefinition.AnyCargo(outCargo);
            bool hasBack = CircleHaulJobDefinition.AnyCargo(backCargo);

            // Teilstrecken: A → B, B → Ziel der Rückfracht (A oder C), C → A leer (nur beim dritten Ziel)
            var scB = spec.B;
            var scHome = spec.C ?? scA;
            float distOut = JobPaymentCalculator.GetDistanceBetweenStations(scA, scB);
            float distBack = JobPaymentCalculator.GetDistanceBetweenStations(scB, scHome);
            float distReturn = scHome != scA ? JobPaymentCalculator.GetDistanceBetweenStations(scHome, scA) : 0f;

            var carsData = liveries.GroupBy(l => l).ToDictionary(g => g.Key, g => g.Count());
            var outData = new PaymentCalculationData(carsData, CargoCounts(outCargo));
            var backData = new PaymentCalculationData(carsData, CargoCounts(backCargo));
            var emptyData = new PaymentCalculationData(carsData, new Dictionary<CargoType, int>());

            float payment =
                (hasOut ? JobPaymentCalculator.CalculateJobPayment(JobType.Transport, distOut, outData)
                        : JobPaymentCalculator.CalculateJobPayment(JobType.EmptyHaul, distOut, emptyData)) +
                (hasBack ? JobPaymentCalculator.CalculateJobPayment(JobType.Transport, distBack, backData)
                         : JobPaymentCalculator.CalculateJobPayment(JobType.EmptyHaul, distBack, emptyData));
            if (distReturn > 0f)
                payment += JobPaymentCalculator.CalculateJobPayment(JobType.EmptyHaul, distReturn, emptyData);
            if (hasOut) payment += JobPaymentCalculator.CalculateJobPayment(JobType.ShuntingUnload, 0f, outData);
            if (hasBack)
                payment += JobPaymentCalculator.CalculateJobPayment(JobType.ShuntingLoad, 0f, backData) +
                           JobPaymentCalculator.CalculateJobPayment(JobType.ShuntingUnload, 0f, backData);

            // Behandlungen: Zustellung 1, Abholung 2, Containertausch / drittes Ziel bis 3
            int handlings = (hasOut ? 1 : 0) + (hasBack ? 2 : 0);
            int warehouseCars = Loaded(outCargo) + 2 * Loaded(backCargo);
            payment = Mathf.Round(payment * s.PaymentMultiplier * (1f + s.HandlingBonus * handlings)
                                  * LldCompat.PayFactor(warehouseCars));

            // Basis-Zeit: alle Teilstrecken + Rangieren je Behandlung + Abstellen.
            // Die eigentliche Lade-/Entladezeit addiert LongerLoadingDelay direkt auf den Job.
            float timeLimit =
                (JobPaymentCalculator.CalculateHaulBonusTimeLimit(distOut, false) +
                 JobPaymentCalculator.CalculateHaulBonusTimeLimit(distBack, false) +
                 (distReturn > 0f ? JobPaymentCalculator.CalculateHaulBonusTimeLimit(distReturn, false) : 0f) +
                 JobPaymentCalculator.CalculateShuntingBonusTimeLimit(1, false) * (handlings + 1)) * s.TimeLimitMultiplier;

            string aId = scA.stationInfo.YardID;
            string bId = scB.stationInfo.YardID;

            var def = chainGO.AddComponent<CircleHaulJobDefinition>();
            def.PopulateBaseJobDefinition(scA.logicStation, timeLimit, payment, new StationsChainData(aId, bId), licenseFlags);
            def.Variant = spec.Variant;
            def.Cars = cars;
            def.StartTrack = startTrack;
            def.ReturnTrack = returnTrack;
            def.FarUnloadMachine = hasOut ? spec.FarUnload : null;
            def.FarLoadMachine = hasBack ? spec.FarLoad : null;
            def.HomeMachine = hasBack ? spec.Home : null;
            def.OutCargo = outCargo;
            def.BackCargo = backCargo;

            CircleHaulsMain.Log($"Circle Haul vorbereitet: {spec.Variant}, {count} Wagen, " +
                                $"hin [{string.Join(",", outCargo.Where(c => c != CargoType.None).Distinct())}] " +
                                $"zurück [{string.Join(",", backCargo.Where(c => c != CargoType.None).Distinct())}], " +
                                $"{ChainName(scA, spec)}, Start {startTrack.ID.TrackPartOnly}, Ende {returnTrack.ID.TrackPartOnly}, ${payment}");
            return def;
        }

        // ------------------------------------------------------------------ Fracht pro Wagen

        /// <summary>
        /// Teilt den Zug in bis zu 3 zusammenhängende Blöcke verschiedener Fracht
        /// und lässt mit der angegebenen Chance einzelne Wagen leer (fehlender Container).
        /// </summary>
        internal static List<CargoType> MakeCargoList(int count, List<CargoType> set, float missingChance)
        {
            var types = Shuffle(set).Take(Math.Min(set.Count, count)).ToList();
            var list = new List<CargoType>(count);

            // Blockgrößen zufällig, jeder Block mindestens 1 Wagen
            var cuts = Enumerable.Range(1, count - 1).OrderBy(_ => Rng.Next()).Take(types.Count - 1).OrderBy(x => x).ToList();
            int blockStart = 0;
            for (int b = 0; b < types.Count; b++)
            {
                int blockEnd = b < cuts.Count ? cuts[b] : count;
                for (int i = blockStart; i < blockEnd; i++) list.Add(types[b]);
                blockStart = blockEnd;
            }

            if (missingChance > 0f)
            {
                for (int i = 0; i < count; i++)
                    if (Rng.NextDouble() < missingChance) list[i] = CargoType.None;
                if (list.All(c => c == CargoType.None)) list[Rng.Next(count)] = types[0];
            }
            return list;
        }

        internal static List<CargoType> Empty(int n) => Enumerable.Repeat(CargoType.None, n).ToList();

        /// <summary>Fracht des nächsten beladenen Nachbarwagens (damit Blöcke zusammenbleiben), sonst Ersatz.</summary>
        internal static CargoType NeighbourOr(List<CargoType> list, int i, CargoType fallback)
        {
            for (int d = 1; d < list.Count; d++)
            {
                if (i - d >= 0 && list[i - d] != CargoType.None) return list[i - d];
                if (i + d < list.Count && list[i + d] != CargoType.None) return list[i + d];
            }
            return fallback;
        }

        internal static int Loaded(List<CargoType> list) => list.Count(c => c != CargoType.None);

        internal static Dictionary<CargoType, int> CargoCounts(List<CargoType> list) =>
            list.Where(c => c != CargoType.None).GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());

        // ------------------------------------------------------------------ Container & Wagentypen

        /// <summary>
        /// Container-Frachten für den Containertausch – feste Liste, damit nie Traktoren, Busse o. Ä. dabei sind.
        /// </summary>
        private static readonly HashSet<CargoType> Containers = new HashSet<CargoType>
        {
            CargoType.DairyProducts, CargoType.MeatProducts, CargoType.CannedFood, CargoType.CatFood,
            CargoType.TropicalFruits, CargoType.ScrapContainers,
            CargoType.ElectronicsIskar, CargoType.ElectronicsKrugmann, CargoType.ElectronicsAAG,
            CargoType.ElectronicsNovae, CargoType.ElectronicsTraeg,
            CargoType.ToolsIskar, CargoType.ToolsBrohm, CargoType.ToolsAAG, CargoType.ToolsNovae, CargoType.ToolsTraeg,
            CargoType.Furniture,
            CargoType.ClothingObco, CargoType.ClothingNeoGamma, CargoType.ClothingNovae, CargoType.ClothingTraeg,
            CargoType.Medicine, CargoType.ChemicalsIskar, CargoType.ChemicalsSperex,
            CargoType.CryoOxygen, CargoType.CryoHydrogen, CargoType.Argon, CargoType.Nitrogen,
            CargoType.AmmoniumNitrate, CargoType.Ammunition,
            CargoType.EmptySunOmni, CargoType.EmptyIskar, CargoType.EmptyObco, CargoType.EmptyGoorsk,
            CargoType.EmptyKrugmann, CargoType.EmptyBrohm, CargoType.EmptyAAG, CargoType.EmptySperex,
            CargoType.EmptyNovae, CargoType.EmptyTraeg, CargoType.EmptyChemlek, CargoType.EmptyNeoGamma,
        };

        public static bool IsContainer(CargoType cargo) => Containers.Contains(cargo);

        internal static bool TryCarTypes(Dictionary<CargoType_v2, List<TrainCarType_v2>> map, CargoType cargo, out List<TrainCarType_v2> types)
        {
            types = null;
            try
            {
                var v2 = cargo.ToV2();
                return v2 != null && map.TryGetValue(v2, out types) && types != null;
            }
            catch { return false; }
        }

        internal static bool CanCarry(TrainCarType_v2 carType, CargoType cargo) =>
            TryCarTypes(Globals.G.Types.CargoToLoadableCarTypes, cargo, out var types) && types.Contains(carType);

        /// <summary>Wagentyp für alle Frachten; allowPartial = der Typ, der die meisten trägt.</summary>
        internal static TrainCarType_v2 PickCarType(List<CargoType> cargos, bool allowPartial = false)
        {
            var map = Globals.G.Types.CargoToLoadableCarTypes;
            var candidates = cargos
                .SelectMany(c => TryCarTypes(map, c, out var t) ? t : new List<TrainCarType_v2>())
                .Distinct()
                .Where(t => t.liveries != null && t.liveries.Count > 0)
                .Select(t => new { Type = t, Count = cargos.Count(c => CanCarry(t, c)) })
                .ToList();
            if (candidates.Count == 0) return null;

            int best = candidates.Max(x => x.Count);
            if (!allowPartial && best < cargos.Count) return null;
            return Pick(candidates.Where(x => x.Count == best).Select(x => x.Type).ToList());
        }

        internal static WarehouseMachine BestMachine(StationController sc, List<CargoType> cargos)
        {
            var machines = sc?.logicStation?.yard?.WarehouseMachines;
            if (machines == null) return null;
            var scored = machines.Where(m => m != null)
                .Select(m => new { M = m, N = cargos.Count(m.IsCargoSupported) })
                .Where(x => x.N > 0).ToList();
            if (scored.Count == 0) return null;
            int best = scored.Max(x => x.N);
            return Pick(scored.Where(x => x.N == best).Select(x => x.M).ToList());
        }

        internal static List<WarehouseMachine> MachinesFor(StationController sc, CargoType cargo)
        {
            var machines = sc?.logicStation?.yard?.WarehouseMachines;
            if (machines == null) return new List<WarehouseMachine>();
            return machines.Where(m => m != null && m.IsCargoSupported(cargo)).ToList();
        }

        internal static List<T> Shuffle<T>(IEnumerable<T> items) => items.OrderBy(_ => Rng.Next()).ToList();

        internal static T Pick<T>(IList<T> list) => list[Rng.Next(list.Count)];
    }
}
