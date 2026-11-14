using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace E7.AlertCaseExtractor
{
    // extractor-settings.json, beside the exe. Not App.config: one file holds everything the job needs,
    // and it can carry a restricted ACL because it holds the read-only connection string.
    internal sealed class ExtractorSettings
    {
        /// <summary>Read-only SQL login. Never the sa strings from the web Web.config.</summary>
        public string ConnectionString = "";

        /// <summary>Folder for state/, out/ and logs/. Blank = the folder of the exe.</summary>
        public string WorkDir = "";

        /// <summary>First alert date to read on a full run (first run, or after a logic-version change).</summary>
        public DateTime HistoryStart = new DateTime(2026, 1, 1);

        /// <summary>Incremental runs re-read this many days before the watermark, to catch late resets and edited remarks.</summary>
        public int OverlapDays = 3;

        public int CommandTimeoutSec = 120;

        /// <summary>Alerts read per query (keyset by pending Id). About 11 KB of JSON per alert, so 500 is a few MB.</summary>
        public int BatchSize = 500;

        /// <summary>Only alerts of these SiteIds are read. Empty = every site. After changing it, run --full once.</summary>
        public List<int> SiteIds = new List<int>();

        /// <summary>
        /// 1.1.0: where the Judge reads its past cases from -- the web site's App_Data\AlertJudge folder, or a
        /// full path ending in .json. Blank = only out\judge-cases.json is written and the Judge sees nothing new.
        /// </summary>
        public string JudgeCasesPath = "";

        /// <summary>Set-time column on dbo.PendingAlertApproval. The live table has DeviceTimestamp (checked 2026-11-05); the EDMX says SetTimeStamp. Run --check.</summary>
        public string SetTimeColumn = "DeviceTimestamp";

        public string ResetTimeColumn = "ResetTimeStamp";

        /// <summary>Same cause at the same station within +/- this many minutes counts as a site-wide event.</summary>
        public int SiteWindowMinutes = 10;

        /// <summary>Weather for alerts older than this is read from the Open-Meteo archive and treated as final.</summary>
        public int WeatherFinalAfterDays = 6;

        /// <summary>Station code (as on the alert card, e.g. GLTA) to lat/long. Overrides dbo.SiteTemperature; a station in neither gets no weather columns.</summary>
        public Dictionary<string, StationGeo> Stations = new Dictionary<string, StationGeo>(StringComparer.OrdinalIgnoreCase);

        internal string Root
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(WorkDir))
                {
                    return WorkDir;
                }

                return AppDomain.CurrentDomain.BaseDirectory;
            }
        }

        internal static ExtractorSettings Load(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("settings file not found -- copy extractor-settings.example.json to extractor-settings.json and fill it in", path);
            }

            ExtractorSettings s = JsonConvert.DeserializeObject<ExtractorSettings>(File.ReadAllText(path));
            if (s == null)
            {
                throw new InvalidDataException("settings file is empty: " + path);
            }

            if (string.IsNullOrWhiteSpace(s.ConnectionString))
            {
                throw new InvalidDataException("ConnectionString is blank in " + path);
            }

            // Column names go into the SQL text, so only plain identifiers are accepted.
            RequireIdentifier("SetTimeColumn", s.SetTimeColumn);
            RequireIdentifier("ResetTimeColumn", s.ResetTimeColumn);

            if (s.OverlapDays < 0)
            {
                s.OverlapDays = 0;
            }

            if (s.SiteWindowMinutes < 1)
            {
                s.SiteWindowMinutes = 10;
            }

            if (s.SiteIds == null)
            {
                s.SiteIds = new List<int>();
            }

            return s;
        }

        private static void RequireIdentifier(string name, string value)
        {
            if (value == null || !Regex.IsMatch(value, "^[A-Za-z_][A-Za-z0-9_]*$"))
            {
                throw new InvalidDataException(name + " must be a plain column name, got '" + value + "'");
            }
        }
    }

    internal sealed class StationGeo
    {
        // set from extractor-settings.json; 0,0 is treated as "not configured"
        public double Lat = 0;
        public double Lon = 0;
    }
}
