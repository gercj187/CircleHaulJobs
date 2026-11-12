using System;
using System.Collections.Generic;
using System.Linq;
using CircleHauls.Jobs;
using DV.Booklets;
using DV.Localization;
using DV.Logic.Job;
using DV.RenderTextureSystem.BookletRender;
using DV.ThingTypes;
using DV.ThingTypes.TransitionHelpers;
using UnityEngine;

namespace CircleHauls.Booklets
{
    /// <summary>
    /// Baut das Booklet aus Vanilla-Seitenvorlagen – Seiten kommen direkt aus dem Auftragsablauf (PlanStep):
    /// Deckblatt, Übersicht (lila), Ankuppeln, je Schritt eine Seite, Abkuppeln auf dem Abstellgleis, Entwerten.
    ///
    /// WICHTIG: Der Renderer verträgt kein CargoType.None. Leere Wagen → Liste null (wie Vanilla),
    /// teilbeladen (fehlende Container) → Ersatz-Fracht + Icon-Maske (CHTaskPaperData / CargoIconMaskPatch).
    /// </summary>
    internal static class CircleHaulBooklet
    {
        /// <summary>Übersetzter Auftragsname (z. B. "CIRCLE HAUL", "ZUGUMLAUF").</summary>
        public static string Title => Loc.T("title");

        private class Ctx
        {
            public Job_data Job;
            public CircleHaulData Def;
            public List<PlanStep> Plan;
            public List<Car_data> Cars;
        }

        private static Ctx Extract(Job_data job)
        {
            // Host: aus der Job-Definition. Multiplayer-Client: per Paket vom Host empfangen.
            var def = CircleHaulData.Get(job.ID)
                      ?? throw new Exception($"Circle Haul {job.ID}: Auftragsdaten (noch) nicht vorhanden");

            var seq = job.tasksData?.FirstOrDefault();
            var cars = seq?.nestedTasks?.FirstOrDefault(t => t.instanceTaskType == TaskType.Transport)?.cars;
            if (cars == null || cars.Count != def.CarCount)
                throw new Exception($"Circle Haul {job.ID}: Wagenliste passt nicht");

            return new Ctx { Job = job, Def = def, Plan = def.BuildPlan(), Cars = cars };
        }

        // ---------------------------------------------------------------- Booklet

        public static List<TemplatePaperData> CreateBooklet(Job_data job)
        {
            var c = Extract(job);

            // Die letzte Fahrt ist immer das Abstellen. Im selben Bahnhof → keine eigene Fahrt-Seite,
            // das erledigt die Abkuppeln-Seite.
            var shown = c.Plan.Where((p, i) => !(i == c.Plan.Count - 1 && p.InStation)).ToList();

            // Deckblatt + Übersicht + Ankuppeln + Schritte + Abkuppeln + Entwerten
            int total = 2 + 1 + shown.Count + 1 + 1;
            int page = 1;
            int step = 1;

            var pages = new List<TemplatePaperData>
            {
                new CoverPageTemplatePaperData(job.ID, Title, page.ToString(), total.ToString()),
            };
            page++;
            pages.Add(CreateOverview(c, page++, total));

            // Wagen holen
            var start = StationOf(c.Def.StartTrack);
            // Ankuppeln: alles sieht beladen aus – ob ein Container fehlt, zeigt sich erst an der Entladestelle
            // Ankuppeln wie Vanilla (gleiche Texte), aber mit Maske: Hinfahrt so zeigen, wie der Zug gespawnt ist
            var startCargo = Display(c.Def.OutCargo, c.Def.IsRevealed(PlanLeg.Out), out var startHidden);
            pages.Add(new CHTaskPaperData(startHidden,
                (step++).ToString(), LocalizationAPI.L("job/task_type_couple"), LocalizationAPI.L("job/task_desc_couple"),
                start.YardID, start.StationColor, c.Def.StartTrack.ID.TrackPartOnly, C.TRACK_COLOR,
                string.Empty, string.Empty, TemplatePaperData.NOT_USED_COLOR,
                c.Cars, startCargo, (page++).ToString(), total.ToString()));

            foreach (var p in shown)
            {
                pages.Add(p.Kind == PlanKind.Haul
                    ? CreateHaulPage(c, p, step++, page++, total)
                    : CreateWarehousePage(c, p, step++, page++, total));
            }

            // abstellen – danach sind die Wagen immer leer
            var end = StationOf(c.Def.ReturnTrack);
            pages.Add(BookletCreator_Job.CreateUncoupleTaskPaperData(step++, end.YardID, end.StationColor,
                c.Def.ReturnTrack.ID.TrackPartOnly, c.Cars, null, page++, total));

            pages.Add(BookletCreator_Job.CreateValidateJobTaskPaperData(step, page, total));
            return pages;
        }

        public static List<TemplatePaperData> CreateOverviewOnly(Job_data job)
        {
            return new List<TemplatePaperData> { CreateOverview(Extract(job), 0, 0) };
        }

        // ---------------------------------------------------------------- Seiten

        private static TemplatePaperData CreateOverview(Ctx c, int page, int total)
        {
            var job = c.Job;
            var def = c.Def;
            var startInfo = job.chainOriginStationInfo;
            var farInfo = job.chainDestinationStationInfo;

            // Angezeigt wird die Fracht der Hinfahrt, bei der Abholung die der Rückfahrt
            var shownCargo = def.HasOut ? def.OutCargo : def.BackCargo;
            var display = Display(shownCargo, def.IsRevealed(def.HasOut ? PlanLeg.Out : PlanLeg.Back), out var hidden);

            string timeBonus = job.timeLimit > 0f ? $"{Mathf.FloorToInt(job.timeLimit / 60f)} min" : C.NO_BONUS_TIME_LIMIT_STR;
            string length = $"{C.GetCarsTotalLength(c.Cars).ToString("N1", LocalizationAPI.CC)} m";
            string mass = $"{(C.GetCarsTotalMass(c.Cars, display) * 0.001f).ToString("N1", LocalizationAPI.CC)} t";
            string value = $"${(C.GetTrainValue(c.Cars, display) / 1_000_000f).ToString("N2", LocalizationAPI.CC)}m";
            string payment = job.basePayment.ToString("N0", LocalizationAPI.CC);

            var distinct = def.OutCargo.Concat(def.BackCargo).Where(x => x != CargoType.None).Distinct().ToList();
            string startName = LocalizationAPI.L(startInfo.LocalizationKey);
            string farName = LocalizationAPI.L(farInfo.LocalizationKey);

            // Drittes Ziel: Station, an der die Rückfracht entladen wird (≠ Start)
            StationInfo thirdInfo = null;
            if (def.HasBack && def.HomeMachine != null)
            {
                var third = StationOf(def.HomeMachine.WarehouseTrack);
                if (third != null && third.YardID != startInfo.YardID) thirdInfo = third;
            }
            string thirdName = thirdInfo != null ? LocalizationAPI.L(thirdInfo.LocalizationKey) : string.Empty;

            // {0}=Fracht hin, {1}=ferner Bahnhof, {2}=dessen Kürzel, {3}=Heimatkürzel, {4}=Fracht zurück,
            // {5}=drittes Ziel, {6}=dessen Kürzel
            string key = thirdInfo != null ? (def.HasOut ? "desc.forward" : "desc.forward_empty")
                       : def.Variant == CircleHaulVariant.Swap ? "desc.swap"
                       : def.HasOut ? "desc.delivery" : "desc.pickup";
            string description = Loc.T(key,
                def.HasOut ? CargoNames(def.OutCargo) : CargoNames(def.BackCargo),
                farName, farInfo.YardID, startInfo.YardID, CargoNames(def.BackCargo),
                thirdName, thirdInfo?.YardID ?? string.Empty);

            string subKey = thirdInfo != null ? "sub.forward"
                          : def.Variant == CircleHaulVariant.Swap ? "sub.swap" : def.HasOut ? "sub.delivery" : "sub.pickup";

            // Zielzeile: beim dritten Ziel "B » C", sonst nur B (Rückweg zeigt das gespiegelte Dreieck)
            string endLabel = thirdInfo != null ? Loc.T("overview.end", farName, thirdName) : farName;

            return new CHFrontPaperData(hidden,
                Title, Loc.T(subKey), job.ID, CHJobType.Color,
                description, job.requiredLicenses,
                distinct, display,
                string.Empty, string.Empty, TemplatePaperData.NOT_USED_COLOR,
                startName, startInfo.Type, startInfo.StationColor,
                endLabel, farInfo.Type, farInfo.StationColor,
                c.Cars,
                length, mass, value, timeBonus, payment,
                page > 0 ? page.ToString() : string.Empty,
                total > 0 ? total.ToString() : string.Empty);
        }

        private static TemplatePaperData CreateHaulPage(Ctx c, PlanStep p, int step, int page, int total)
        {
            var dest = StationOf(p.To);
            var display = Display(p.CargoPerCar, c.Def.IsRevealed(p.Leg), out var hidden);
            bool empty = display == null;

            string destName = LocalizationAPI.L(dest.LocalizationKey);
            string track = $"{p.To.ID.yardId}-{p.To.ID.TrackPartOnly}";

            string type = p.InStation ? Loc.GameOr("job/shunting_job_type_name", "task.shunt")
                        : empty ? Loc.T("task.empty")
                        : Loc.GameOr("job/task_type_haul", "task.transport");
            string desc = Loc.T(empty ? "haul.empty" : "haul.loaded", destName, track, CargoNames(p.CargoPerCar));

            // Wie Vanilla-Haul-Seite: Zielbahnhof im Stationsfeld, Gleis steht in der Beschreibung
            return new CHTaskPaperData(hidden,
                step.ToString(), type, desc,
                string.Empty, TemplatePaperData.NOT_USED_COLOR, string.Empty, TemplatePaperData.NOT_USED_COLOR,
                destName, dest.Type, dest.StationColor,
                c.Cars, display,
                page.ToString(), total.ToString());
        }

        private static TemplatePaperData CreateWarehousePage(Ctx c, PlanStep p, int step, int page, int total)
        {
            var station = StationOf(p.To);
            bool loading = p.Kind == PlanKind.Load;
            var display = Display(p.CargoPerCar, c.Def.IsRevealed(p.Leg), out var hidden);

            string type = loading
                ? Loc.GameOr("job/task_type_load", "task.load")
                : Loc.GameOr("job/task_type_unload", "task.unload");
            string desc = Loc.T(loading ? "load.desc" : "unload.desc", CargoNames(p.CargoPerCar));

            return new CHTaskPaperData(hidden,
                step.ToString(), type, desc,
                station.YardID, station.StationColor, p.To.ID.TrackPartOnly, C.TRACK_COLOR,
                string.Empty, string.Empty, TemplatePaperData.NOT_USED_COLOR,
                c.Cars, display,
                page.ToString(), total.ToString());
        }

        // ---------------------------------------------------------------- Helfer

        private static StationInfo StationOf(Track track)
        {
            var sc = StationController.GetStationByYardID(track.ID.yardId);
            return sc != null ? sc.stationInfo : null;
        }

        /// <summary>Nur vollständig beladene Listen (für Vanilla-Helfer ohne Maske), sonst null.</summary>
        private static List<CargoType> FullOrNull(List<CargoType> list) =>
            list == null || list.Any(x => x == CargoType.None) ? null : list;

        /// <summary>
        /// Anzeige-Liste ohne CargoType.None: leere Wagen bekommen die Fracht ihres Nachbarn.
        /// reveal=true → ihr Icon wird per Maske ausgeblendet (Hinfahrt: immer, so wie gespawnt;
        /// Rückfahrt: erst nach dem Beladen in B). Vorher sieht die Rückfahrt voll aus. Komplett leer → null.
        /// </summary>
        private static List<CargoType> Display(List<CargoType> list, bool reveal, out bool[] hidden)
        {
            hidden = null;
            if (!CircleHaulJobDefinition.AnyCargo(list)) return null;

            var result = new List<CargoType>(list);
            hidden = new bool[list.Count];
            CargoType fill = list.First(x => x != CargoType.None);
            for (int i = 0; i < result.Count; i++)
            {
                if (result[i] == CargoType.None) { result[i] = fill; hidden[i] = reveal; }
                else fill = result[i];
            }
            return result;
        }

        /// <summary>Spielübersetzte Frachtnamen, z. B. "Rohöl" oder "Elektronik Novae, Werkzeug Iskar".</summary>
        private static string CargoNames(List<CargoType> cargo)
        {
            if (cargo == null) return string.Empty;
            var names = cargo.Where(x => x != CargoType.None).Distinct().Select(x =>
            {
                try { return LocalizationAPI.L(x.ToV2().localizationKeyFull); }
                catch { return x.ToString(); }
            });
            return string.Join(", ", names);
        }
    }
}
