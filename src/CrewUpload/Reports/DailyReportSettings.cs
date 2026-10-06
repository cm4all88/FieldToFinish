using System.Collections.Generic;
using Newtonsoft.Json;

namespace CrewUpload.Reports
{
    /// <summary>A piece of survey equipment on the daily report, with its asset numbers.</summary>
    public sealed class ReportEquipment
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("assets")] public string Assets { get; set; }
        public override string ToString() => Name;
    }

    /// <summary>A company vehicle on the daily report.</summary>
    public sealed class ReportVehicle
    {
        /// <summary>AUT 103</summary>
        [JsonProperty("id")] public string Id { get; set; }

        /// <summary>Puy 2019 Toyota Tundra</summary>
        [JsonProperty("description")] public string Description { get; set; }

        [JsonProperty("assets")] public string Assets { get; set; }
        public override string ToString() => Id + "  " + Description;
    }

    /// <summary>
    /// The daily report's form: its title and the lists it offers, as on Form 03-SV-125-GW
    /// (Rev. 01/03/2025). Kept in job-folders.json so a new truck or form revision is an edit, not a
    /// rebuild. Where the copies go is set here too.
    /// </summary>
    public sealed class DailyReportSettings
    {
        [JsonProperty("formNumber")] public string FormNumber { get; set; } = "Form 03-SV-125-GW/Rev. 01/03/2025";
        [JsonProperty("title")] public string Title { get; set; } = "DAILY FIELD SURVEYOR\u2019S REPORT";
        [JsonProperty("team")] public string Team { get; set; } = "Greater Washington Survey Team";

        /// <summary>Added to the download folder name for the report's PDF: 20260507-JBB-1800-119-STK-DR.pdf.</summary>
        [JsonProperty("fileSuffix")] public string FileSuffix { get; set; } = "-DR";

        /// <summary>The admin copy and the report records, by year and month underneath.</summary>
        [JsonProperty("adminFolder")] public string AdminFolder { get; set; } = @"\\parametrix.com\pmx\PSO\Shared\Divisions\00Survey\FLD\CrewUpload\DailyReports";

        /// <summary>The crew's own copy on this PC. Environment variables are expanded.</summary>
        [JsonProperty("localFolder")] public string LocalFolder { get; set; } = @"%USERPROFILE%\Documents\Daily Reports";

        [JsonProperty("personalAuto")] public string PersonalAuto { get; set; } = "Personal Auto";

        [JsonProperty("noEquipment")] public string NoEquipment { get; set; } = "No Equipment";

        [JsonProperty("equipment", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<ReportEquipment> Equipment { get; set; } = new List<ReportEquipment>
        {
            new ReportEquipment { Name = "PSO Survey Equipment", Assets = "249999 / 4129" },
            new ReportEquipment { Name = "Scanner", Assets = "247462 / 4127" },
            new ReportEquipment { Name = "Wingtra UAV", Assets = "248251 / 4131" },
            new ReportEquipment { Name = "LiDAR UAV", Assets = "248193 / 4134" },
            new ReportEquipment { Name = "Single Beam", Assets = "244070 / 4055" },
            new ReportEquipment { Name = "Multi Beam", Assets = "244065 / 4050" },
        };

        [JsonProperty("vehicles", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<ReportVehicle> Vehicles { get; set; } = new List<ReportVehicle>
        {
            V("AUT 61", "Puy 2007 Toyota Tundra", "243994"),
            V("AUT 63", "Sea 2008 Toyota Tundra", "244099"),
            V("AUT 67", "Puy 2008 Toyota Highlander", "244163"),
            V("AUT 71", "Brem 2008 Toyota Tundra", "244323"),
            V("AUT 76", "Brem 2015 Toyota Tundra", "245503"),
            V("AUT 79", "Puy 2016 Toyota Tundra", "245726"),
            V("AUT 97", "Sea 2017 Toyota Tundra", "245983"),
            V("AUT 103", "Puy 2019 Toyota Tundra", "246676"),
            V("AUT 7076r", "Sea 2020 Toyota Tundra", "247076"),
            V("AUT 7269", "Muk 2015 Toyota Tundra", "247269"),
            V("AUT 7270", "Muk 2017 Toyota Tundra", "247270"),
            V("AUT 7272", "Muk 2019 Toyota Tacoma", "247272"),
            V("AUT 7986", "Puy 2022 Ford F-150", "247986"),
            V("AUT 8541", "Brem 2024 Toyota Tundra", "248541"),
            V("AUT 8687", "Muk 2024 Toyota Tacoma", "248687"),
        };

        [JsonProperty("safetyObservations", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> SafetyObservations { get; set; } = new List<string>
        {
            "Dangerous Snakes/Wildlife", "Extreme Temperatures", "Insects", "Traffic",
            "Dense Vegetation", "Hazardous Material", "Open Trenches", "Uneven Ground",
            "Drop-Offs and Edges > 4 feet", "Homeless Encampments", "Over/Adjacent to Water Bodies",
            "Equipment/Machinery", "Ice/Slippery", "Road Conditions",
        };

        [JsonProperty("safetyPrecautions", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> SafetyPrecautions { get; set; } = new List<string>
        {
            "Appropriate Clothing for Cold/Heat", "Fall Avoidance/Handrail/PPE", "Hearing Protection", "Rain Gear", "Use Cones/Flags/Signs",
            "Appropriate Clothing for Insects/Wildlife", "First Aid Kit", "Hydration", "Safety Vest", "Waders",
            "Flashers/Beacons", "Keep Safe Distance", "Sunscreen", "Work in Pairs",
            "Gloves", "Life Vest", "Tall Leather Boots",
            "Aware of Surroundings/Be Alert", "Hand and Leg Cut Protection", "Mask/Respirator",
        };

        private static ReportVehicle V(string id, string description, string asset) =>
            new ReportVehicle { Id = id, Description = description, Assets = asset + " / 2702" };
    }
}
