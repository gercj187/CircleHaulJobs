using System.Collections.Generic;
using DV.Booklets;
using DV.RenderTextureSystem.BookletRender;
using DV.ThingTypes;
using UnityEngine;

namespace CircleHauls.Booklets
{
    /// <summary>
    /// Vanilla-Seitendaten + Maske für Wagen OHNE Container.
    /// Der Renderer verträgt kein CargoType.None (ToV2-Lookup → Absturz), daher bekommt er für leere Wagen
    /// eine Ersatz-Fracht, und CargoIconMaskPatch blendet deren Icon danach wieder aus.
    /// </summary>
    internal interface ICargoMask
    {
        bool[] HiddenIcons { get; }
    }

    internal class CHTaskPaperData : TaskTemplatePaperData, ICargoMask
    {
        public bool[] HiddenIcons { get; }

        public CHTaskPaperData(bool[] hidden, string stepNum, string taskType, string taskDescription,
            string yardId, Color yardColor, string trackId, Color trackColor,
            string stationName, string stationType, Color stationColor,
            List<Car_data> cars, List<CargoType> cargoTypePerCar, string pageNumber, string totalPages)
            : base(stepNum, taskType, taskDescription, yardId, yardColor, trackId, trackColor,
                stationName, stationType, stationColor, cars, cargoTypePerCar, pageNumber, totalPages)
        {
            HiddenIcons = hidden;
        }
    }

    internal class CHFrontPaperData : FrontPageTemplatePaperData, ICargoMask
    {
        public bool[] HiddenIcons { get; }

        public CHFrontPaperData(bool[] hidden, string jobType, string jobSubtype, string jobId, Color jobTypeColor,
            string jobDescription, JobLicenses requiredLicenses, List<CargoType> distinctCargoTypes, List<CargoType> cargoTypePerCar,
            string singleStationName, string singleStationType, Color singleStationBgColor,
            string startStationName, string startStationType, Color startStationBgColor,
            string endStationName, string endStationType, Color endStationBgColor,
            List<Car_data> cars, string trainLength, string trainMass, string trainValue, string timeBonus, string payment,
            string pageNumber, string totalPages)
            : base(jobType, jobSubtype, jobId, jobTypeColor, jobDescription, requiredLicenses, distinctCargoTypes, cargoTypePerCar,
                singleStationName, singleStationType, singleStationBgColor,
                startStationName, startStationType, startStationBgColor,
                endStationName, endStationType, endStationBgColor,
                cars, trainLength, trainMass, trainValue, timeBonus, payment, pageNumber, totalPages)
        {
            HiddenIcons = hidden;
        }
    }
}
