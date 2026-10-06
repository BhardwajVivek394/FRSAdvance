using System;
using System.Collections.Generic;

namespace Domain
{
    public static class RosterStatus
    {
        public const string Generating = "GENERATING";
        public const string Ready = "READY";
        public const string Failed = "FAILED";
    }

    public static class RosterPriority
    {
        public const string Urgent = "URGENT";
        public const string Soon = "SOON";
        public const string Monitor = "MONITOR";
    }

    public static class RosterCloseStatus
    {
        public const string Open = "OPEN";
        public const string ConfirmedDefect = "CONFIRMED_DEFECT";
        public const string NoDefectFound = "NO_DEFECT_FOUND";
        public const string WorkDone = "WORK_DONE";
    }

    public class RosterRun
    {
        public long RunId { get; set; }
        public int DivisionId { get; set; }
        public DateTime RosterDate { get; set; }
        public DateTime GeneratedAtIst { get; set; }
        public string GeneratedBy { get; set; }
        public string Status { get; set; }
        public bool IsCurrent { get; set; }
        public int ItemCount { get; set; }
        public int? AlertsRead { get; set; }
        public int? AlertsTotal { get; set; }
        public string SourceNote { get; set; }
        public string Error { get; set; }

        // Convenience for the view: a run that read fewer alerts than existed must
        // never be presented as complete.
        public bool IsPartial
        {
            get { return AlertsTotal.HasValue && AlertsRead.HasValue && AlertsRead.Value < AlertsTotal.Value; }
        }

        public string Message { get; set; }
        public bool IsSuccess { get; set; }
    }

    public class RosterItem
    {
        public long ItemId { get; set; }
        public long RunId { get; set; }
        public int DivisionId { get; set; }
        public int SiteId { get; set; }
        public string StationName { get; set; }
        public int AssetId { get; set; }
        public string AssetName { get; set; }
        public int? AssetTypeId { get; set; }
        public string AssetTypeName { get; set; }

        public string Priority { get; set; }
        public bool IsFailedRepair { get; set; }
        public string Reason { get; set; }
        public string Evidence15d { get; set; }     // json blob, rendered by the view
        public string MlNote { get; set; }
        public string LastOpNote { get; set; }
        public string ActionText { get; set; }

        public string ItemKey { get; set; }
        public DateTime FirstSeenDate { get; set; }
        public int DayCount { get; set; }
        public string CarryReason { get; set; }
        public int CleanInspections { get; set; }
        public DateTime? WatchUntilDate { get; set; }

        public string CloseStatus { get; set; }
        public int? ClosedByUserId { get; set; }
        public string ClosedByName { get; set; }
        public DateTime? ClosedAtIst { get; set; }
        public string ClosedRemark { get; set; }
        public string SignalNote { get; set; }
    }

    // POST body for GenerateDaily. The caller (scheduler task, or the generator that
    // gathers the 15-day evidence) supplies the candidate items; the service applies
    // carry-forward, failed-repair detection and persistence.
    public class RosterGenerateRequest
    {
        public int DivisionId { get; set; }
        public DateTime? RosterDate { get; set; }   // IST date; null = today (server must pass it explicitly for the scheduler)
        public string GeneratedBy { get; set; }     // 'scheduler' | 'manual:<userId>'
        public bool Force { get; set; }             // true = regenerate even though a current run exists
        public int? AlertsRead { get; set; }
        public int? AlertsTotal { get; set; }
        public string SourceNote { get; set; }
        public List<RosterItem> Items { get; set; }

        public RosterGenerateRequest()
        {
            Items = new List<RosterItem>();
        }
    }

    public class RosterCloseRequest
    {
        public long ItemId { get; set; }
        public string CloseStatus { get; set; }     // CONFIRMED_DEFECT | NO_DEFECT_FOUND | WORK_DONE
        public string Remark { get; set; }
        // ClosedByUserId is taken from the authenticated header, never from the body.
    }

    // One station's worksheet: the run it came from plus that station's items.
    public class RosterWorksheet
    {
        public RosterRun Run { get; set; }
        public int SiteId { get; set; }
        public string StationName { get; set; }
        public List<RosterItem> Items { get; set; }

        public int UrgentCount { get; set; }
        public int SoonCount { get; set; }
        public int MonitorCount { get; set; }

        public string Message { get; set; }
        public bool IsSuccess { get; set; }

        public RosterWorksheet()
        {
            Items = new List<RosterItem>();
        }
    }

    public class RosterStationLine
    {
        public int SiteId { get; set; }
        public string StationName { get; set; }
        public int UrgentCount { get; set; }
        public int SoonCount { get; set; }
        public int MonitorCount { get; set; }
        public int FailedRepairCount { get; set; }
        public int OldestOpenDays { get; set; }
        public string WorstItem { get; set; }
        public int TrackAlert { get; set; }
        public int SignalAlert { get; set; }
        public int PointAlert { get; set; }
        public int OtherAlert { get; set; }
        public int TrackShortCount { get; set; }
        public int PointPmCount { get; set; }
        public int DriftCount { get; set; }
        public int SweptCount { get; set; }
        public int AssetCount { get; set; }
        public List<string> TopUrgent { get; set; }

        public RosterStationLine()
        {
            TopUrgent = new List<string>();
        }
    }

    // The controller's division view -- a PROJECTION of the same items, never a
    // second computation, so a station and the controller cannot disagree.
    public class RosterRollup
    {
        public RosterRun Run { get; set; }
        public List<RosterStationLine> Stations { get; set; }

        public int FailedRepairCount { get; set; }
        public int UrgentCount { get; set; }
        public int SoonCount { get; set; }
        public int OldestOpenDays { get; set; }
        public int ClosedYesterday { get; set; }
        public int ItemsYesterday { get; set; }

        public string Message { get; set; }
        public bool IsSuccess { get; set; }

        public RosterRollup()
        {
            Stations = new List<RosterStationLine>();
        }
    }
}
