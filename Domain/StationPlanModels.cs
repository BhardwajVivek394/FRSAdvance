using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Domain
{
    // =====================================================================================
    //  Station maintenance plan -- data contract (v1)
    //
    //  Built by StationPlanBuilder, sent to the ChatBot page once as the SSE event
    //  {type:"plan", plan:{...}}. Serialise with StationPlanJson.ToJObject so property
    //  names come out camelCase while dictionary keys (PM grade names) stay as written.
    //
    //  Rule: a value the sources did not return stays null and the page hides that line.
    //  Nothing in this contract is a placeholder or a guess.
    // =====================================================================================

    public static class PlanFamily
    {
        public const string Track = "TC";
        public const string Signal = "SG";
        public const string Point = "PM";
        public const string Ips = "IPS";
        public const string Other = "OTHER";

        public static string FromTypeId(int typeId)
        {
            if (typeId == 1)
            {
                return Track;
            }

            if (typeId == 2)
            {
                return Signal;
            }

            if (typeId == 3)
            {
                return Point;
            }

            if (typeId == 34)
            {
                return Ips;
            }

            return Other;
        }

        public static string FromTypeName(string typeName)
        {
            string n = (typeName ?? "").Trim().ToUpperInvariant();
            if (n.Contains("TRACK"))
            {
                return Track;
            }

            if (n.Contains("POINT") || n == "PM")
            {
                return Point;
            }

            if (n.Contains("SIGNAL"))
            {
                return Signal;
            }

            if (n.Contains("IPS"))
            {
                return Ips;
            }

            return Other;
        }

        public static string Label(string family)
        {
            if (family == Track)
            {
                return "Track circuits";
            }

            if (family == Point)
            {
                return "Point machines";
            }

            if (family == Signal)
            {
                return "Signals";
            }

            if (family == Ips)
            {
                return "IPS";
            }

            return "Other assets";
        }

        public static string SingularLabel(string family)
        {
            if (family == Track)
            {
                return "Track circuit";
            }

            if (family == Point)
            {
                return "Point machine";
            }

            if (family == Signal)
            {
                return "Signal";
            }

            if (family == Ips)
            {
                return "IPS";
            }

            return "Asset";
        }
    }

    public static class PlanPriority
    {
        public const string Urgent = "URGENT";
        public const string Soon = "INSPECT_SOON";
        public const string Monitor = "MONITOR";
        public const string None = "";

        public static int Rank(string priority)
        {
            if (priority == Urgent)
            {
                return 3;
            }

            if (priority == Soon)
            {
                return 2;
            }

            if (priority == Monitor)
            {
                return 1;
            }

            return 0;
        }

        public static string FromRank(int rank)
        {
            if (rank >= 3)
            {
                return Urgent;
            }

            if (rank == 2)
            {
                return Soon;
            }

            if (rank == 1)
            {
                return Monitor;
            }

            return None;
        }

        // Roster bands arrive as URGENT / SOON / MAINTAIN-SOON / MONITOR.
        public static int RankFromBand(string band)
        {
            string b = (band ?? "").Trim().ToUpperInvariant();
            if (b == "URGENT")
            {
                return 3;
            }

            if (b == "SOON" || b == "MAINTAIN-SOON" || b == "INSPECT_SOON" || b == "INSPECT SOON")
            {
                return 2;
            }

            if (b == "MONITOR")
            {
                return 1;
            }

            return 0;
        }
    }

    public static class PlanLeakType
    {
        public const string FeedSide = "FeedSide";
        public const string GluedJoint = "GluedJoint";
        public const string SleeperBallast = "SleeperBallast";
        public const string RelaySide = "RelaySide";
        public const string ExternalShorting = "ExternalShorting";
        public const string Other = "Other";
        public const string None = "None";

        // The track model writes "Both (Glued Joint)", "IR Only (Relay Side)", "IF Only (Internal)"
        // and "External Sorting" (sic). Dominant_Cause in major_events uses free text, so match by
        // keyword rather than by exact string.
        public static string FromModelText(string raw)
        {
            string t = (raw ?? "").Trim().ToLowerInvariant();
            if (t.Length == 0 || t == "none" || t == "null" || t == "normal")
            {
                return None;
            }

            if (t.Contains("glued") || t.Contains("both"))
            {
                return GluedJoint;
            }

            if (t.Contains("ir only") || t.Contains("relay"))
            {
                return RelaySide;
            }

            if (t.Contains("if only") || t.Contains("internal") || t.Contains("feed"))
            {
                return FeedSide;
            }

            if (t.Contains("external") || t.Contains("sort") || t.Contains("short"))
            {
                return ExternalShorting;
            }

            if (t.Contains("ballast") || t.Contains("sleeper"))
            {
                return SleeperBallast;
            }

            return Other;
        }

        public static string Label(string leakType)
        {
            if (leakType == FeedSide)
            {
                return "Feed side";
            }

            if (leakType == GluedJoint)
            {
                return "Glued joint";
            }

            if (leakType == SleeperBallast)
            {
                return "Sleeper / ballast";
            }

            if (leakType == RelaySide)
            {
                return "Relay side";
            }

            if (leakType == ExternalShorting)
            {
                return "External shorting";
            }

            if (leakType == Other)
            {
                return "Other leakage";
            }

            return "No leakage";
        }
    }

    // ---------------------------------------------------------------------------------
    //  Inputs supplied by the host (web project) through IStationPlanSource
    // ---------------------------------------------------------------------------------

    public sealed class PlanSiteInfo
    {
        public int SiteId { get; set; }
        public int DivisionId { get; set; }
        public string Name { get; set; }
        public string Code { get; set; }
    }

    public sealed class PlanAssetInfo
    {
        public int AssetId { get; set; }
        public string Name { get; set; }
        public int TypeId { get; set; }
        public string Family { get; set; }
    }

    public sealed class PlanAlertRow
    {
        public string AlertId { get; set; }
        public int AssetId { get; set; }
        public int SiteId { get; set; }
        public string AssetName { get; set; }
        public int AssetTypeId { get; set; }
        public string AssetTypeName { get; set; }
        public string Cause { get; set; }
        public DateTime SetAt { get; set; }
        public DateTime? ResetAt { get; set; }
    }

    public sealed class PlanIntent
    {
        public PlanSiteInfo Site { get; set; }
        public string SiteText { get; set; }
        public string AssetText { get; set; }
        public int Hours { get; set; }
        public bool HoursGiven { get; set; }
        public string Language { get; set; }
        public bool AskForSite { get; set; }
    }

    public sealed class StationPlanOptions
    {
        public StationPlanOptions()
        {
            Now = DateTime.Now;
            WindowDays = 15;
            CacheSeconds = 300;
            AlertCacheSeconds = 60;
            InventoryCacheSeconds = 3600;
            LiveTimeoutMs = 4000;
            BuildTimeoutMs = 10000;
            MaxParallel = 6;
            UrgentCount = 5;
            SoonCount = 3;
            PmUrgentShare = 0.5;
            PmSoonShare = 0.1;
            PmMinOps = 10;
            SetupMinutes = 15;
            MaxCards = 12;
        }

        public DateTime Now { get; set; }
        public int WindowDays { get; set; }
        public int CacheSeconds { get; set; }
        public int AlertCacheSeconds { get; set; }
        public int InventoryCacheSeconds { get; set; }
        public int LiveTimeoutMs { get; set; }
        public int BuildTimeoutMs { get; set; }
        public int MaxParallel { get; set; }
        public int UrgentCount { get; set; }
        public int SoonCount { get; set; }
        public double PmUrgentShare { get; set; }
        public double PmSoonShare { get; set; }
        public int PmMinOps { get; set; }
        public int SetupMinutes { get; set; }
        public int MaxCards { get; set; }
    }

    // ---------------------------------------------------------------------------------
    //  Output contract
    // ---------------------------------------------------------------------------------

    public sealed class StationPlan
    {
        public StationPlan()
        {
            Version = 1;
            Assets = new List<PlanAssetCard>();
            Families = new List<PlanFamilySummary>();
            Source = new PlanSourceInfo();
            TimeBudget = new PlanTimeBudget();
        }

        public int Version { get; set; }
        public PlanSiteOut Site { get; set; }
        public int Hours { get; set; }
        public string Language { get; set; }
        public string Headline { get; set; }
        public bool SingleAsset { get; set; }
        public string NotFoundAsset { get; set; }
        public PlanSourceInfo Source { get; set; }
        public PlanTimeBudget TimeBudget { get; set; }
        public List<PlanFamilySummary> Families { get; set; }
        public List<PlanAssetCard> Assets { get; set; }
    }

    public sealed class PlanSiteOut
    {
        public int Id { get; set; }
        public int DivisionId { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
    }

    public sealed class PlanSourceInfo
    {
        public PlanSourceInfo()
        {
            Partial = new List<string>();
        }

        public string RosterRunAt { get; set; }
        public string LiveCheckAt { get; set; }
        public long BuildMs { get; set; }
        public int AlertsRead { get; set; }
        public List<string> Partial { get; set; }
    }

    public sealed class PlanTimeBudget
    {
        public PlanTimeBudget()
        {
            InPlan = new List<string>();
            NextVisit = new List<string>();
        }

        public string SetupLabel { get; set; }
        public int SetupMinutes { get; set; }
        public List<string> InPlan { get; set; }
        public List<string> NextVisit { get; set; }
        public int BufferMinutes { get; set; }
    }

    public sealed class PlanFamilySummary
    {
        public PlanFamilySummary()
        {
            Healthy = new List<string>();
        }

        public string Family { get; set; }
        public string Label { get; set; }
        public int Total { get; set; }
        public int Urgent { get; set; }
        public int Soon { get; set; }
        public int Monitor { get; set; }
        public List<string> Healthy { get; set; }
    }

    public sealed class PlanAssetCard
    {
        public PlanAssetCard()
        {
            Reasons = new List<string>();
        }

        public string Key { get; set; }
        public int AssetId { get; set; }
        public string Name { get; set; }
        public string Family { get; set; }
        public string Subtitle { get; set; }
        public string Priority { get; set; }
        public int Rank { get; set; }
        public int Minutes { get; set; }
        public string Verdict { get; set; }
        public List<string> Reasons { get; set; }
        public PlanAction Action { get; set; }
        public PlanRosterInfo Roster { get; set; }
        public PlanAlerts15d Alerts15d { get; set; }
        public PlanDrift Drift { get; set; }
        public PlanTrack Track { get; set; }
        public PlanPm Pm { get; set; }
        public PlanSignal Signal { get; set; }

        [JsonIgnore]
        public double Score { get; set; }
    }

    public sealed class PlanAction
    {
        public string Id { get; set; }
        public string Text { get; set; }
    }

    public sealed class PlanRosterInfo
    {
        public string Priority { get; set; }
        public string Reason { get; set; }
        public string CloseStatus { get; set; }
    }

    public sealed class PlanAlerts15d
    {
        public int Count { get; set; }
        public int Active { get; set; }
        public int FirstHalf { get; set; }
        public int SecondHalf { get; set; }
        public int[] PerDay { get; set; }
        public string FromDay { get; set; }
        public string ToDay { get; set; }
        public string Latest { get; set; }
        public int NightCount { get; set; }
        public int DayCount { get; set; }
        public string TopCause { get; set; }
    }

    public sealed class PlanDrift
    {
        public PlanDrift()
        {
            Attrs = new List<PlanDriftAttr>();
        }

        public double? Value { get; set; }
        public string WorstAttr { get; set; }
        public string Band { get; set; }
        public List<PlanDriftAttr> Attrs { get; set; }
    }

    public sealed class PlanDriftAttr
    {
        public string Attr { get; set; }
        public double? Avg { get; set; }
        public double? Drift { get; set; }
        public string Dir { get; set; }
        public int? Samples { get; set; }
    }

    public sealed class PlanTrack
    {
        public PlanTrack()
        {
            Events = new List<PlanTrackEvent>();
        }

        public string Source { get; set; }
        public string Overall { get; set; }
        public string LeakType { get; set; }
        public string LeakTypeRaw { get; set; }
        public string SeenEarlier { get; set; }
        public string Status { get; set; }
        public string Severity { get; set; }
        public string Band { get; set; }
        public string BandReason { get; set; }
        public string Summary { get; set; }
        public string Reason { get; set; }
        public int EventCount { get; set; }
        public List<PlanTrackEvent> Events { get; set; }
    }

    public sealed class PlanTrackEvent
    {
        public string Start { get; set; }
        public string End { get; set; }
        public double? Hours { get; set; }
        public string Cause { get; set; }
        public string LeakType { get; set; }
    }

    public sealed class PlanPm
    {
        public PlanPm()
        {
            Machines = new List<PlanPmMachine>();
        }

        public string Source { get; set; }
        public string Band { get; set; }
        public string WorstState { get; set; }
        public string Condition { get; set; }
        public int OpsInWindow { get; set; }
        public int FlaggedInWindow { get; set; }
        public string Verdict { get; set; }
        public string Recommendation { get; set; }
        public List<PlanPmMachine> Machines { get; set; }
    }

    public sealed class PlanPmMachine
    {
        public PlanPmMachine()
        {
            Grades = new Dictionary<string, int>();
            Directions = new List<PlanPmDirection>();
        }

        public string End { get; set; }
        public string Tag { get; set; }
        public string WaveType { get; set; }
        public int? OpsCount { get; set; }
        public int OpsInWindow { get; set; }
        public int Flagged { get; set; }
        public Dictionary<string, int> Grades { get; set; }
        public List<PlanPmDirection> Directions { get; set; }
        public PlanPmOp LastOp { get; set; }
    }

    public sealed class PlanPmDirection
    {
        public PlanPmDirection()
        {
            Rising = new List<string>();
        }

        public string Dir { get; set; }
        public string State { get; set; }
        public int? Throws { get; set; }
        public string Worst { get; set; }
        public List<string> Rising { get; set; }
    }

    public sealed class PlanPmOp
    {
        public string At { get; set; }
        public string Grade { get; set; }
        public double? AvgA { get; set; }
        public double? MaxA { get; set; }
        public double? TimeS { get; set; }
        public List<double> Samples { get; set; }
    }

    public sealed class PlanSignal
    {
        public int NightFailures { get; set; }
        public int DayFailures { get; set; }
    }

    // ---------------------------------------------------------------------------------
    //  JSON helper -- camelCase properties, dictionary keys untouched, nulls omitted
    // ---------------------------------------------------------------------------------

    public static class StationPlanJson
    {
        private static readonly JsonSerializer serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy
                {
                    ProcessDictionaryKeys = false,
                    OverrideSpecifiedNames = true
                }
            },
            NullValueHandling = NullValueHandling.Ignore
        });

        public static JObject ToJObject(StationPlan plan)
        {
            if (plan == null)
            {
                return new JObject();
            }

            return JObject.FromObject(plan, serializer);
        }
    }
}
