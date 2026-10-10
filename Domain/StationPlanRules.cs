using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Domain
{
    // =====================================================================================
    //  PlanRules -- evidence to card. Pure functions, no I/O.
    //
    //  Priority is the HIGHEST of:
    //    - the alert ladder      : active now = URGENT; degrading with >= UrgentCount = URGENT;
    //                              >= SoonCount = INSPECT_SOON; any alert = MONITOR
    //    - the saved roster band : Priority / DriftBand / PmBand / TrackShortBand of open items,
    //                              IsFailedRepair = URGENT (same reading as the roster page)
    //    - the track model ladder: the roster's v2.6.0.0 rungs, read from the live reply
    //    - the PM model          : worst A/B state, or the flagged share of throws
    //  The AI never sets or changes any of this.
    // =====================================================================================
    internal static class PlanRules
    {
        internal sealed class Context
        {
            public Context(StationPlanOptions options, DateTime from, DateTime now, PlanSiteInfo site)
            {
                Options = options;
                From = from;
                Now = now;
                Site = site;
            }

            public StationPlanOptions Options { get; private set; }
            public DateTime From { get; private set; }
            public DateTime Now { get; private set; }
            public PlanSiteInfo Site { get; private set; }
        }

        private sealed class RosterView
        {
            public int Rank;
            public string Priority;
            public string Reason;
            public string CloseStatus;
            public JObject Drift;
            public JObject Track;
            public JObject Pm;
            public bool FailedRepair;
        }

        private static readonly string[] timeFormats =
        {
            "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.fff", "yyyy-MM-dd HH:mm",
            "dd-MM-yyyy HH:mm:ss", "dd/MM/yyyy HH:mm:ss", "dd-MM-yyyy HH:mm", "dd/MM/yyyy HH:mm",
            "ddMMyyyy_HHmmss", "yyyyMMdd_HHmmss"
        };

        // ------------------------------------------------------------------ JSON helpers

        public static JToken Prop(JObject o, params string[] names)
        {
            if (o == null)
            {
                return null;
            }

            foreach (string n in names)
            {
                JToken t = o[n];
                if (t != null && t.Type != JTokenType.Null)
                {
                    return t;
                }
            }

            foreach (string n in names)
            {
                JProperty p = o.Properties().FirstOrDefault(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
                if (p != null && p.Value.Type != JTokenType.Null)
                {
                    return p.Value;
                }
            }

            return null;
        }

        public static string Str(JObject o, params string[] names)
        {
            JToken t = Prop(o, names);
            if (t == null)
            {
                return null;
            }

            string s = t.Type == JTokenType.String ? (string)t : t.ToString(Formatting.None);
            s = s == null ? null : s.Trim();
            return string.IsNullOrEmpty(s) ? null : s;
        }

        public static int Int(JObject o, params string[] names)
        {
            double? d = Dbl(o, names);
            return d.HasValue ? (int)Math.Round(d.Value) : 0;
        }

        public static int? IntOrNull(JObject o, params string[] names)
        {
            double? d = Dbl(o, names);
            if (!d.HasValue)
            {
                return null;
            }

            return (int)Math.Round(d.Value);
        }

        public static double? Dbl(JObject o, params string[] names)
        {
            JToken t = Prop(o, names);
            if (t == null)
            {
                return null;
            }

            if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float)
            {
                return t.Value<double>();
            }

            double v;
            if (double.TryParse(t.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            {
                return v;
            }

            return null;
        }

        public static bool Bool(JObject o, params string[] names)
        {
            JToken t = Prop(o, names);
            if (t == null)
            {
                return false;
            }

            string s = t.ToString().Trim().ToLowerInvariant();
            return s == "true" || s == "1" || s == "yes";
        }

        public static bool TryTime(string raw, out DateTime when)
        {
            when = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            string s = raw.Trim();
            if (DateTime.TryParseExact(s, timeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out when))
            {
                return true;
            }

            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out when);
        }

        private static string Show(DateTime t)
        {
            return t.ToString("dd MMM HH:mm", CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------ worksheet

        public static List<JObject> SheetItems(JToken sheet)
        {
            List<JObject> list = new List<JObject>();
            if (sheet == null)
            {
                return list;
            }

            JArray arr = sheet as JArray;
            JObject obj = sheet as JObject;
            if (arr == null && obj != null)
            {
                arr = Prop(obj, "Items", "items", "Data", "data") as JArray;
            }

            if (arr == null)
            {
                return list;
            }

            foreach (JToken t in arr)
            {
                JObject o = t as JObject;
                if (o != null)
                {
                    list.Add(o);
                }
            }

            return list;
        }

        public static string SheetRunAt(JToken sheet, List<JObject> items)
        {
            string raw = Str(sheet as JObject, "RunAt", "RunTime", "RunStartedAt", "GeneratedAt", "GeneratedOn", "RosterRunAt", "CreatedOn", "CreatedAt");
            if (raw == null)
            {
                foreach (JObject it in items)
                {
                    raw = Str(it, "RunAt", "GeneratedAt", "GeneratedOn", "CreatedOn", "CreatedAt");
                    if (raw != null)
                    {
                        break;
                    }
                }
            }

            DateTime t;
            if (raw != null && TryTime(raw, out t))
            {
                return t.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            }

            return raw;
        }

        public static bool IsOpenItem(JObject item)
        {
            string cs = (Str(item, "CloseStatus", "closeStatus") ?? "").ToUpperInvariant();
            return cs.Length == 0 || cs == "OPEN";
        }

        private static JObject Evidence(JObject item)
        {
            JToken e = Prop(item, "Evidence15d", "evidence15d");
            if (e == null)
            {
                return null;
            }

            if (e.Type == JTokenType.String)
            {
                try
                {
                    return JToken.Parse((string)e) as JObject;
                }
                catch (JsonException)
                {
                    return null;
                }
            }

            return e as JObject;
        }

        // Generator snapshots arrive as {valid,data,error} (sourceSchema 2) or one flat record.
        private static JObject SourceData(JObject evidence, string key)
        {
            JObject sources = Prop(evidence, "sources", "Sources") as JObject;
            JObject src = Prop(sources, key) as JObject;
            if (src == null)
            {
                return null;
            }

            JObject data = Prop(src, "data", "Data") as JObject;
            if (data != null)
            {
                JToken valid = Prop(src, "valid", "Valid");
                if (valid != null && valid.Type == JTokenType.Boolean && !(bool)valid)
                {
                    return null;
                }

                return Str(data, "error", "Error") == null ? data : null;
            }

            return Str(src, "error", "Error") == null ? src : null;
        }

        private static RosterView ReadRoster(List<JObject> items)
        {
            RosterView v = new RosterView();
            foreach (JObject it in items)
            {
                if (!IsOpenItem(it))
                {
                    continue;
                }

                int r = Math.Max(PlanPriority.RankFromBand(Str(it, "Priority", "priority")),
                    Math.Max(PlanPriority.RankFromBand(Str(it, "DriftBand", "driftBand")),
                    Math.Max(PlanPriority.RankFromBand(Str(it, "PmBand", "pmBand")),
                    PlanPriority.RankFromBand(Str(it, "TrackShortBand", "trackShortBand")))));

                if (Bool(it, "IsFailedRepair", "isFailedRepair"))
                {
                    v.FailedRepair = true;
                    r = 3;
                }

                if (r > v.Rank)
                {
                    v.Rank = r;
                    v.Priority = Str(it, "Priority", "priority");
                }

                if (v.Reason == null)
                {
                    v.Reason = Str(it, "Reason", "reason", "CompositeReason", "compositeReason");
                }

                if (v.CloseStatus == null)
                {
                    v.CloseStatus = Str(it, "CloseStatus", "closeStatus") ?? "OPEN";
                }

                JObject ev = Evidence(it);
                if (ev != null)
                {
                    v.Drift = SourceData(ev, "drift") ?? v.Drift;
                    v.Track = SourceData(ev, "track") ?? v.Track;
                    v.Pm = SourceData(ev, "pm") ?? v.Pm;
                }
            }

            return v;
        }

        // ------------------------------------------------------------------ alerts

        private static PlanAlerts15d AlertStats(List<PlanAlertRow> alerts, Context ctx)
        {
            int days = ctx.Options.WindowDays;
            PlanAlerts15d s = new PlanAlerts15d();
            s.PerDay = new int[days];
            s.FromDay = ctx.From.ToString("dd MMM", CultureInfo.InvariantCulture);
            s.ToDay = ctx.Now.ToString("dd MMM", CultureInfo.InvariantCulture);
            s.Count = alerts.Count;
            if (alerts.Count == 0)
            {
                return s;
            }

            foreach (PlanAlertRow a in alerts)
            {
                int idx = (int)(a.SetAt.Date - ctx.From.Date).TotalDays;
                if (idx >= 0 && idx < days)
                {
                    s.PerDay[idx]++;
                }

                if (!a.ResetAt.HasValue)
                {
                    s.Active++;
                }

                if (a.SetAt.Hour >= 22 || a.SetAt.Hour < 6)
                {
                    s.NightCount++;
                }
                else
                {
                    s.DayCount++;
                }
            }

            // The first half gets the extra day of an odd window, so "degrading" needs a real rise.
            int firstLen = (days + 1) / 2;
            for (int i = 0; i < days; i++)
            {
                if (i < firstLen)
                {
                    s.FirstHalf += s.PerDay[i];
                }
                else
                {
                    s.SecondHalf += s.PerDay[i];
                }
            }

            PlanAlertRow last = alerts.OrderByDescending(a => a.SetAt).First();
            string cause = string.IsNullOrEmpty(last.Cause) ? "Alert" : last.Cause;
            if (last.ResetAt.HasValue)
            {
                int mins = (int)Math.Max(0, Math.Round((last.ResetAt.Value - last.SetAt).TotalMinutes));
                s.Latest = cause + " \u00b7 " + Show(last.SetAt) + " \u00b7 reset after " + mins + " min";
            }
            else
            {
                s.Latest = cause + " \u00b7 " + Show(last.SetAt) + " \u00b7 still active";
            }

            var top = alerts
                .Where(a => !string.IsNullOrEmpty(a.Cause))
                .GroupBy(a => a.Cause, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();
            if (top != null)
            {
                s.TopCause = top.Key + " (" + top.Count() + ")";
            }

            return s;
        }

        private static int AlertRank(PlanAlerts15d s, StationPlanOptions o)
        {
            if (s == null || s.Count == 0)
            {
                return 0;
            }

            if (s.Active > 0)
            {
                return 3;
            }

            if (s.SecondHalf > s.FirstHalf && s.Count >= o.UrgentCount)
            {
                return 3;
            }

            if (s.Count >= o.SoonCount)
            {
                return 2;
            }

            return 1;
        }

        // ------------------------------------------------------------------ drift

        private static PlanDrift DriftFrom(JObject data)
        {
            if (data == null)
            {
                return null;
            }

            PlanDrift d = new PlanDrift();
            d.Value = Dbl(data, "drift", "Drift");
            d.WorstAttr = Str(data, "worstAttr", "WorstAttr");
            d.Band = Str(data, "band", "Band");

            JArray attrs = Prop(data, "attrs", "Attrs") as JArray;
            if (attrs != null)
            {
                foreach (JToken t in attrs)
                {
                    JObject a = t as JObject;
                    if (a == null || Str(a, "skipped", "Skipped") != null)
                    {
                        continue;
                    }

                    int? n = IntOrNull(a, "n", "N");
                    if (n.HasValue && n.Value < 2)
                    {
                        continue;
                    }

                    d.Attrs.Add(new PlanDriftAttr
                    {
                        Attr = Str(a, "attr", "Attr", "name") ?? "Attribute",
                        Avg = Dbl(a, "avg", "Avg"),
                        Drift = Dbl(a, "drift", "Drift"),
                        Dir = Str(a, "dir", "Dir"),
                        Samples = n
                    });
                }
            }

            d.Attrs = d.Attrs
                .OrderByDescending(x => Math.Abs(x.Drift ?? 0))
                .Take(6)
                .ToList();

            if (!d.Value.HasValue && d.Attrs.Count == 0)
            {
                return null;
            }

            return d;
        }

        // ------------------------------------------------------------------ track

        // Same rungs as MaintenceRosterController v2.6.0.0 (first match wins), with the
        // v2.8.8.0 rule that the event count only raises when the ladder had no opinion.
        private static void TrackBand(PlanTrack t)
        {
            string sev = (t.Severity ?? "").Trim().ToLowerInvariant();
            string stat = (t.Status ?? "").Trim().ToLowerInvariant();
            string ovr = (t.Overall ?? "").Trim().ToLowerInvariant();

            if (sev == "critical" || stat == "ongoing" || stat == "recovery" || ovr.IndexOf("critical risk", StringComparison.Ordinal) >= 0)
            {
                t.Band = "URGENT";
                t.BandReason = sev == "critical" ? "severity critical"
                    : (stat == "ongoing" ? "leak ongoing now"
                    : (stat == "recovery" ? "leak in recovery, still receding" : "overall Critical Risk"));
                return;
            }

            if (sev == "moderate" || sev == "high" || stat == "active")
            {
                t.Band = "SOON";
                t.BandReason = (sev == "moderate" || sev == "high") ? "severity " + sev : "leak active in the last hours";
                return;
            }

            if (sev == "low" || stat == "resolved" || ovr.IndexOf("moderate risk", StringComparison.Ordinal) >= 0)
            {
                t.Band = "MONITOR";
                t.BandReason = sev == "low" ? "severity low" : (stat == "resolved" ? "leak resolved, history only" : "overall Moderate Risk");
                return;
            }

            if (t.EventCount > 0)
            {
                t.Band = "MONITOR";
                t.BandReason = t.EventCount + " shorting window(s) in the model history";
                return;
            }

            t.Band = "";
            t.BandReason = null;
        }

        private static PlanTrack TrackFromLive(JObject doc)
        {
            if (doc == null)
            {
                return null;
            }

            PlanTrack t = new PlanTrack();
            t.Source = "live";
            t.Overall = Str(doc, "overall_condition");
            JObject li = Prop(doc, "leakage_info") as JObject;
            if (li != null)
            {
                t.Severity = Str(li, "severity");
                t.Status = Str(li, "leakage_status");
                t.LeakTypeRaw = Str(li, "leakage_type");
                t.Summary = Str(li, "simple_summary");
                t.Reason = Str(li, "reason");
            }

            JArray events = Prop(doc, "major_events") as JArray;
            List<Tuple<DateTime, PlanTrackEvent>> list = new List<Tuple<DateTime, PlanTrackEvent>>();
            if (events != null)
            {
                t.EventCount = events.Count;
                foreach (JToken tok in events)
                {
                    JObject e = tok as JObject;
                    if (e == null)
                    {
                        continue;
                    }

                    DateTime st;
                    DateTime en;
                    bool hasStart = TryTime(Str(e, "Start_Time", "start"), out st);
                    bool hasEnd = TryTime(Str(e, "End_Time", "end"), out en);
                    string cause = Str(e, "Dominant_Cause", "cause");
                    PlanTrackEvent pe = new PlanTrackEvent
                    {
                        Start = hasStart ? Show(st) : Str(e, "Start_Time", "start"),
                        End = hasEnd ? Show(en) : Str(e, "End_Time", "end"),
                        Hours = Dbl(e, "Duration_Hours", "durationHours"),
                        Cause = cause,
                        LeakType = PlanLeakType.FromModelText(cause)
                    };
                    if (!pe.Hours.HasValue && hasStart && hasEnd)
                    {
                        pe.Hours = Math.Round((en - st).TotalHours, 1);
                    }

                    list.Add(Tuple.Create(hasStart ? st : DateTime.MinValue, pe));
                }
            }

            t.Events = list.OrderByDescending(x => x.Item1).Select(x => x.Item2).Take(8).ToList();
            t.LeakType = PlanLeakType.FromModelText(t.LeakTypeRaw);
            if ((t.LeakType == PlanLeakType.None) && t.Events.Count > 0)
            {
                PlanTrackEvent latest = t.Events.FirstOrDefault(e => e.LeakType != PlanLeakType.None);
                if (latest != null)
                {
                    t.LeakType = latest.LeakType;
                    if (t.LeakTypeRaw == null)
                    {
                        t.LeakTypeRaw = latest.Cause;
                    }
                }
            }

            PlanTrackEvent earlier = t.Events.FirstOrDefault(e => e.LeakType != PlanLeakType.None
                && e.LeakType != PlanLeakType.Other && e.LeakType != t.LeakType);
            t.SeenEarlier = earlier != null ? earlier.LeakType : null;

            TrackBand(t);
            return t;
        }

        private static PlanTrack TrackFromRoster(JObject data)
        {
            if (data == null)
            {
                return null;
            }

            PlanTrack t = new PlanTrack();
            t.Source = "roster";
            t.Overall = Str(data, "overall", "Overall");
            t.Severity = Str(data, "severity", "Severity");
            t.Status = Str(data, "leakStatus", "LeakStatus");
            t.LeakTypeRaw = Str(data, "worstCause", "WorstCause");
            t.LeakType = PlanLeakType.FromModelText(t.LeakTypeRaw);
            t.Summary = Str(data, "simpleSummary", "SimpleSummary", "summary");
            t.EventCount = Int(data, "events", "Events");
            t.Band = Str(data, "band", "Band");
            t.BandReason = Str(data, "bandReason", "BandReason");
            if (string.IsNullOrEmpty(t.Band))
            {
                TrackBand(t);
            }

            return t;
        }

        // ------------------------------------------------------------------ point machine

        private static int PmStateRank(string state)
        {
            string s = (state ?? "").ToUpperInvariant();
            if (Regex.IsMatch(s, "URGENT|FAULT|INSPECT"))
            {
                return 3;
            }

            if (Regex.IsMatch(s, "SOON|MAINTAIN|ALERT|WATCH"))
            {
                return 2;
            }

            return 0;
        }

        private static string OpGrade(JObject op)
        {
            string[] keys = { "confirmed", "grade", "classification", "state" };
            foreach (string k in keys)
            {
                JToken node = op[k];
                if (node == null || node.Type == JTokenType.Null)
                {
                    continue;
                }

                if (node.Type == JTokenType.Object)
                {
                    JToken g = node["grade"] ?? node["confirmed"];
                    if (g != null && g.Type != JTokenType.Null)
                    {
                        return g.ToString().Trim().ToUpperInvariant();
                    }
                }
                else
                {
                    return node.ToString().Trim().ToUpperInvariant();
                }
            }

            return "";
        }

        private static bool OpTime(JObject op, out DateTime when)
        {
            string[] keys = { "time", "timestamp", "at", "start", "op_time" };
            foreach (string k in keys)
            {
                string raw = Str(op, k);
                if (raw != null && TryTime(raw, out when))
                {
                    return true;
                }
            }

            when = DateTime.MinValue;
            return false;
        }

        private static string EndTag(string assetName, int index)
        {
            string name = assetName ?? "";
            int slash = name.IndexOf('/');
            if (slash > 0)
            {
                string first = name.Substring(0, slash).Trim();
                string second = name.Substring(slash + 1).Trim();
                if (index == 0)
                {
                    return first;
                }

                if (Regex.IsMatch(second, @"^\d"))
                {
                    Match prefix = Regex.Match(first, @"^[^\d]*");
                    return prefix.Value + second;
                }

                return second;
            }

            return name + (index == 0 ? " end A" : " end B");
        }

        private static PlanPm PmFromLive(JObject pm, string assetName, Context ctx)
        {
            if (pm == null)
            {
                return null;
            }

            PlanPm p = new PlanPm();
            p.Source = "live";
            string[] ends = { "a", "b" };
            string[] dirs = { "Normal", "Reverse" };
            int worst = 0;

            for (int e = 0; e < ends.Length; e++)
            {
                JObject m = pm[ends[e]] as JObject;
                if (m == null)
                {
                    continue;
                }

                PlanPmMachine mc = new PlanPmMachine();
                mc.End = ends[e].ToUpperInvariant();
                mc.Tag = EndTag(assetName, e);
                mc.WaveType = Str(m, "wave_type");
                mc.OpsCount = IntOrNull(m, "ops_count");

                JObject states = m["states"] as JObject;
                if (states != null)
                {
                    foreach (string dir in dirs)
                    {
                        JObject st = states[dir] as JObject;
                        if (st == null)
                        {
                            continue;
                        }

                        PlanPmDirection d = new PlanPmDirection();
                        d.Dir = dir;
                        d.State = Str(st, "state");
                        d.Throws = IntOrNull(st, "n");
                        d.Worst = Str(st, "worst_confirmed");
                        JArray rising = st["rising"] as JArray;
                        if (rising != null)
                        {
                            foreach (JToken r in rising)
                            {
                                d.Rising.Add(r.ToString());
                            }
                        }

                        int rank = PmStateRank(d.State);
                        if (rank > worst)
                        {
                            worst = rank;
                            p.WorstState = (d.State ?? "").ToUpperInvariant();
                        }

                        if (p.Condition == null)
                        {
                            p.Condition = Str(st, "condition", "worst_condition", "classification");
                        }

                        mc.Directions.Add(d);
                    }
                }

                JArray ops = m["ops"] as JArray;
                DateTime lastTime = DateTime.MinValue;
                JObject lastOp = null;
                if (ops != null)
                {
                    foreach (JToken t in ops)
                    {
                        JObject op = t as JObject;
                        if (op == null)
                        {
                            continue;
                        }

                        DateTime when;
                        bool dated = OpTime(op, out when);
                        if (dated && when < ctx.From)
                        {
                            continue;
                        }

                        if (dated && when >= lastTime)
                        {
                            lastTime = when;
                            lastOp = op;
                        }
                        else if (!dated && lastOp == null)
                        {
                            lastOp = op;
                        }

                        string g = OpGrade(op);
                        if (g.Length == 0)
                        {
                            continue;
                        }

                        int c;
                        mc.Grades[g] = mc.Grades.TryGetValue(g, out c) ? c + 1 : 1;
                        if (g == "INVALID")
                        {
                            continue;
                        }

                        mc.OpsInWindow++;
                        if (g != "NORMAL")
                        {
                            mc.Flagged++;
                        }
                    }
                }

                if (lastOp != null)
                {
                    mc.LastOp = new PlanPmOp
                    {
                        At = lastTime > DateTime.MinValue ? Show(lastTime) : null,
                        Grade = OpGrade(lastOp),
                        AvgA = Dbl(lastOp, "i_avg", "avg_current", "avg_i", "i_mean", "avg", "i_run"),
                        MaxA = Dbl(lastOp, "i_max", "max_current", "peak_current", "i_peak", "max", "peak"),
                        TimeS = Dbl(lastOp, "duration_s", "duration", "op_time_s", "operation_time", "time_s", "t_op", "dur")
                    };
                    JArray samples = Prop(lastOp, "samples", "wave", "current", "i") as JArray;
                    if (samples != null && samples.Count >= 5)
                    {
                        mc.LastOp.Samples = new List<double>();
                        foreach (JToken sv in samples)
                        {
                            double v;
                            if (double.TryParse(sv.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                            {
                                mc.LastOp.Samples.Add(Math.Round(v, 3));
                            }
                        }
                    }
                }

                p.OpsInWindow += mc.OpsInWindow;
                p.FlaggedInWindow += mc.Flagged;
                p.Machines.Add(mc);
            }

            JObject summary = pm["ai_summary"] as JObject;
            if (summary != null)
            {
                p.Verdict = Str(summary, "verdict");
                p.Recommendation = Str(summary, "recommendation");
            }

            if (p.Condition == null)
            {
                JObject cls = pm["classification"] as JObject;
                p.Condition = Str(cls, "condition") ?? Str(pm, "worst_confirmed");
            }

            int rateRank = 0;
            if (p.OpsInWindow >= ctx.Options.PmMinOps)
            {
                double share = (double)p.FlaggedInWindow / p.OpsInWindow;
                if (share >= ctx.Options.PmUrgentShare)
                {
                    rateRank = 3;
                }
                else if (share >= ctx.Options.PmSoonShare)
                {
                    rateRank = 2;
                }
            }

            p.Band = BandName(Math.Max(worst, rateRank));
            if (p.Machines.Count == 0)
            {
                return null;
            }

            return p;
        }

        private static PlanPm PmFromRoster(JObject data)
        {
            if (data == null)
            {
                return null;
            }

            PlanPm p = new PlanPm();
            p.Source = "roster";
            p.OpsInWindow = Int(data, "ops", "Ops");
            p.FlaggedInWindow = Int(data, "problem", "Problem");
            p.Band = Str(data, "band", "Band") ?? "";
            p.Condition = Str(data, "condition", "Condition");
            p.WorstState = Str(data, "worstState", "WorstState");
            return p;
        }

        private static string BandName(int rank)
        {
            if (rank >= 3)
            {
                return "URGENT";
            }

            if (rank == 2)
            {
                return "SOON";
            }

            if (rank == 1)
            {
                return "MONITOR";
            }

            return "";
        }

        private static string TopProblemGrade(PlanPm p)
        {
            Dictionary<string, int> all = new Dictionary<string, int>();
            foreach (PlanPmMachine m in p.Machines)
            {
                foreach (KeyValuePair<string, int> g in m.Grades)
                {
                    if (g.Key == "NORMAL" || g.Key == "INVALID")
                    {
                        continue;
                    }

                    int c;
                    all[g.Key] = all.TryGetValue(g.Key, out c) ? c + g.Value : g.Value;
                }
            }

            if (all.Count > 0)
            {
                return all.OrderByDescending(x => x.Value).First().Key;
            }

            string fallback = ((p.Condition ?? "") + " " + (p.WorstState ?? "")).Trim();
            return fallback.Length > 0 ? fallback.ToUpperInvariant() : null;
        }

        // ------------------------------------------------------------------ action library
        //
        // Default actions and minutes. Owner: S&T in-charge (see handover, open point 1).
        // Change text or minutes here; ids stay stable so outcomes can be tracked per action.

        private sealed class ActionPick
        {
            public string Id;
            public string Text;
            public int Minutes;
        }

        private static ActionPick PickAction(PlanAssetCard card, PlanTrack track, PlanPm pm, PlanAlerts15d alerts, string assetName)
        {
            ActionPick a = new ActionPick();
            string fam = card.Family;

            if (fam == PlanFamily.Track)
            {
                string lt = track != null ? track.LeakType : PlanLeakType.None;
                if (lt == PlanLeakType.GluedJoint)
                {
                    a.Id = "ACT-TC-GJ";
                    a.Text = "Check the glued joints at both ends (end-post, fins, metal burrs); megger across the joint; clean the ballast around it";
                    a.Minutes = 45;
                }
                else if (lt == PlanLeakType.RelaySide)
                {
                    a.Id = "ACT-TC-RS";
                    a.Text = "Check relay-end lead cable, connections and relay-side bond; measure relay current at the relay terminals";
                    a.Minutes = 30;
                }
                else if (lt == PlanLeakType.FeedSide)
                {
                    a.Id = "ACT-TC-FS";
                    a.Text = "Check feed-end connections, feed resistor and feed-side bond; measure feed current at the feed terminals";
                    a.Minutes = 30;
                }
                else if (lt == PlanLeakType.SleeperBallast)
                {
                    a.Id = "ACT-TC-SB";
                    a.Text = "Walk the section for wet or fouled ballast, damaged sleeper insulation (liners, pads) and poor drainage at low spots";
                    a.Minutes = 40;
                }
                else if (lt == PlanLeakType.ExternalShorting)
                {
                    a.Id = "ACT-TC-EX";
                    a.Text = "Walk the section for metal objects or wires across the rails; check insulation at level crossings and turnouts";
                    a.Minutes = 25;
                }
                else
                {
                    a.Id = "ACT-TC-GEN";
                    a.Text = "Check feed and relay ends, bonds and connections; read feed and relay current against normal";
                    a.Minutes = 30;
                }
            }
            else if (fam == PlanFamily.Point)
            {
                string g = pm != null ? (TopProblemGrade(pm) ?? "") : "";
                if (g.Contains("CREEP"))
                {
                    a.Id = "ACT-PM-CRP";
                    a.Text = "Inspect slide chairs, drive rod and stroke-end lubrication for creeping friction; retest the throw current";
                    a.Minutes = 35;
                }
                else if (g.Contains("OBSTR"))
                {
                    a.Id = "ACT-PM-OBS";
                    a.Text = "Clear the switch of debris or blockage; do the obstruction test; check detection contacts for wear";
                    a.Minutes = 35;
                }
                else if (g.Contains("SLUG"))
                {
                    a.Id = "ACT-PM-SLG";
                    a.Text = "Check motor brushes, gearing and the supply voltage during the throw; lubricate";
                    a.Minutes = 35;
                }
                else if (g.Contains("SIGNATURE"))
                {
                    a.Id = "ACT-PM-SIG";
                    a.Text = "Compare the throw on site with a normal throw; check detection adjustment and the motor";
                    a.Minutes = 35;
                }
                else
                {
                    a.Id = "ACT-PM-GEN";
                    a.Text = "Check detection contacts, the WKR relays and the point indication circuit";
                    a.Minutes = 30;
                }

                if ((assetName ?? "").IndexOf('/') > 0 || (pm != null && pm.Machines.Count > 1))
                {
                    a.Minutes = (int)(Math.Ceiling(a.Minutes * 1.7 / 5.0) * 5);
                }
            }
            else if (fam == PlanFamily.Signal)
            {
                if (alerts != null && alerts.Count > 0 && alerts.DayCount == 0 && alerts.NightCount > 0)
                {
                    a.Id = "ACT-SG-TRM";
                    a.Text = "Check the lamp unit termination and ECR cable at the location box; tighten and re-crimp; measure ECR current";
                    a.Minutes = 20;
                }
                else
                {
                    a.Id = "ACT-SG-GEN";
                    a.Text = "Check the signal unit, ECR and the aspect proving circuit; measure ECR current";
                    a.Minutes = 20;
                }
            }
            else
            {
                a.Id = "ACT-IPS-GEN";
                a.Text = "Check charger output, battery float voltage and the DC-DC modules";
                a.Minutes = 25;
            }

            if (card.Priority == PlanPriority.Monitor)
            {
                a.Minutes = Math.Min(a.Minutes, 15);
            }

            return a;
        }

        // ------------------------------------------------------------------ card

        public static PlanAssetCard BuildCard(StationPlanBuilder.AssetWork w, Context ctx)
        {
            PlanAssetCard card = new PlanAssetCard();
            card.AssetId = w.Info.AssetId;
            card.Name = w.Info.Name;
            card.Family = w.Info.Family;
            bool pair = (w.Info.Name ?? "").IndexOf('/') > 0;
            card.Subtitle = PlanFamily.SingularLabel(w.Info.Family) + (pair && w.Info.Family == PlanFamily.Point ? " pair" : "");

            RosterView roster = ReadRoster(w.Items);
            PlanAlerts15d alerts = AlertStats(w.Alerts, ctx);
            card.Alerts15d = alerts;
            card.Drift = DriftFrom(roster.Drift);

            int trackRank = 0;
            int pmRank = 0;
            if (w.Info.Family == PlanFamily.Track)
            {
                card.Track = TrackFromLive(w.TrackLive) ?? TrackFromRoster(roster.Track);
                if (card.Track != null)
                {
                    trackRank = PlanPriority.RankFromBand(card.Track.Band);
                }
            }
            else if (w.Info.Family == PlanFamily.Point)
            {
                card.Pm = PmFromLive(w.PmLive, w.Info.Name, ctx) ?? PmFromRoster(roster.Pm);
                if (card.Pm != null)
                {
                    pmRank = PlanPriority.RankFromBand(card.Pm.Band);
                }
            }
            else if (w.Info.Family == PlanFamily.Signal)
            {
                card.Signal = new PlanSignal { NightFailures = alerts.NightCount, DayFailures = alerts.DayCount };
            }

            if (roster.Rank > 0 || roster.Reason != null)
            {
                card.Roster = new PlanRosterInfo
                {
                    Priority = roster.Priority,
                    Reason = roster.Reason,
                    CloseStatus = roster.CloseStatus
                };
            }

            int alertRank = AlertRank(alerts, ctx.Options);
            int finalRank = Math.Max(Math.Max(alertRank, roster.Rank), Math.Max(trackRank, pmRank));
            card.Priority = PlanPriority.FromRank(finalRank);

            BuildReasons(card, alerts, roster, trackRank, pmRank);
            card.Verdict = BuildVerdict(card, alerts, roster);

            ActionPick act = PickAction(card, card.Track, card.Pm, alerts, w.Info.Name);
            card.Action = new PlanAction { Id = act.Id, Text = act.Text };
            card.Minutes = act.Minutes;

            double score = alerts.Active * 1000.0 + alerts.Count * 3.0;
            if (card.Track != null)
            {
                score += SeverityRank(card.Track.Severity) * 25.0;
            }

            if (card.Pm != null)
            {
                double worstShare = 0.0;
                foreach (PlanPmMachine m in card.Pm.Machines)
                {
                    if (m.OpsInWindow > 0)
                    {
                        worstShare = Math.Max(worstShare, (double)m.Flagged / m.OpsInWindow);
                    }
                }

                if (worstShare == 0.0 && card.Pm.OpsInWindow > 0)
                {
                    worstShare = (double)card.Pm.FlaggedInWindow / card.Pm.OpsInWindow;
                }

                score += 100.0 * worstShare;
            }

            if (card.Drift != null && card.Drift.Value.HasValue)
            {
                score += Math.Min(30.0, Math.Abs(card.Drift.Value.Value));
            }

            card.Score = score;
            return card;
        }

        private static int SeverityRank(string severity)
        {
            string s = (severity ?? "").ToLowerInvariant();
            if (s == "critical")
            {
                return 4;
            }

            if (s == "high")
            {
                return 3;
            }

            if (s == "moderate")
            {
                return 2;
            }

            if (s == "low")
            {
                return 1;
            }

            return 0;
        }

        private static void BuildReasons(PlanAssetCard card, PlanAlerts15d alerts, RosterView roster, int trackRank, int pmRank)
        {
            if (alerts.Active > 0)
            {
                card.Reasons.Add(alerts.Active + " active now");
            }

            if (alerts.Count > 0)
            {
                card.Reasons.Add(alerts.Count + (alerts.Count == 1 ? " alert" : " alerts") + " in 15 days");
                if (alerts.Count < 3)
                {
                    // One or two alerts carry no trend.
                }
                else if (alerts.SecondHalf > alerts.FirstHalf)
                {
                    card.Reasons.Add("Degrading " + alerts.FirstHalf + " \u2192 " + alerts.SecondHalf);
                }
                else if (alerts.SecondHalf < alerts.FirstHalf)
                {
                    card.Reasons.Add("Improving " + alerts.FirstHalf + " \u2192 " + alerts.SecondHalf);
                }
            }

            if (card.Track != null && (trackRank > 0 || card.Track.LeakType != PlanLeakType.None))
            {
                string lt = card.Track.LeakType != PlanLeakType.None ? PlanLeakType.Label(card.Track.LeakType) : "track model";
                card.Reasons.Add("Track model: " + lt + (string.IsNullOrEmpty(card.Track.Severity) ? "" : ", " + card.Track.Severity.ToLowerInvariant()));
            }

            if (card.Pm != null && (pmRank > 0 || card.Pm.FlaggedInWindow > 0))
            {
                string g = TopProblemGrade(card.Pm);
                string what = string.IsNullOrEmpty(g) ? "flagged" : g.ToLowerInvariant();
                if (card.Pm.OpsInWindow > 0)
                {
                    card.Reasons.Add("PM model: " + what + " on " + card.Pm.FlaggedInWindow + " of " + card.Pm.OpsInWindow + " throws");
                }
                else
                {
                    card.Reasons.Add("PM model: " + what);
                }
            }

            if (card.Drift != null && card.Drift.Value.HasValue && Math.Abs(card.Drift.Value.Value) >= 1.0)
            {
                card.Reasons.Add("Average drift " + FormatPct(card.Drift.Value.Value)
                    + (string.IsNullOrEmpty(card.Drift.WorstAttr) ? "" : " (" + card.Drift.WorstAttr + ")"));
            }

            if (roster.FailedRepair)
            {
                card.Reasons.Add("Came back after a recorded repair");
            }

            if (roster.Rank > 0 && card.Reasons.Count == 0)
            {
                card.Reasons.Add("Roster run: " + (roster.Reason ?? roster.Priority));
            }
        }

        private static string FormatPct(double v)
        {
            return (v > 0 ? "+" : "") + v.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        }

        private static string BuildVerdict(PlanAssetCard card, PlanAlerts15d alerts, RosterView roster)
        {
            List<string> parts = new List<string>();

            if (card.Track != null && card.Track.LeakType != PlanLeakType.None)
            {
                string st = (card.Track.Status ?? "").ToLowerInvariant();
                string phrase = st == "ongoing" ? "happening now"
                    : st == "active" ? "active in the last hours"
                    : st == "recovery" ? "receding now"
                    : st == "resolved" ? "resolved, seen in the model history"
                    : "seen in the model history";
                parts.Add(PlanLeakType.Label(card.Track.LeakType) + " leakage " + phrase);
            }

            if (card.Pm != null)
            {
                List<string> machineParts = new List<string>();
                foreach (PlanPmMachine m in card.Pm.Machines)
                {
                    if (m.Flagged <= 0 || m.OpsInWindow <= 0)
                    {
                        continue;
                    }

                    string top = m.Grades.Where(g => g.Key != "NORMAL" && g.Key != "INVALID")
                        .OrderByDescending(g => g.Value).Select(g => g.Key.ToLowerInvariant()).FirstOrDefault() ?? "flagged";
                    machineParts.Add("Machine " + m.End + " " + top + " on " + m.Flagged + " of " + m.OpsInWindow + " throws");
                }

                if (machineParts.Count > 0)
                {
                    parts.Add(string.Join("; ", machineParts));
                }
                else if (!string.IsNullOrEmpty(card.Pm.WorstState) && card.Pm.WorstState != "NORMAL")
                {
                    parts.Add("Model state " + card.Pm.WorstState.ToLowerInvariant());
                }
            }

            if (alerts.Count > 0)
            {
                string a = alerts.Count + (alerts.Count == 1 ? " alert" : " alerts") + " in 15 days";
                if (alerts.Active > 0)
                {
                    a += ", " + alerts.Active + " active now";
                }
                else if (card.Family == PlanFamily.Signal && alerts.DayCount == 0 && alerts.NightCount > 1)
                {
                    a += ", all at night";
                }

                parts.Add(a);
            }

            if (card.Drift != null && card.Drift.Value.HasValue && Math.Abs(card.Drift.Value.Value) >= 5.0)
            {
                parts.Add((string.IsNullOrEmpty(card.Drift.WorstAttr) ? "average" : card.Drift.WorstAttr) + " drifting " + FormatPct(card.Drift.Value.Value));
            }

            if (parts.Count == 0)
            {
                if (roster.Reason != null)
                {
                    return "Flagged by the roster run: " + roster.Reason;
                }

                return "No finding in the last 15 days";
            }

            string text = string.Join("; ", parts);
            return char.ToUpperInvariant(text[0]) + text.Substring(1);
        }
    }

    // =====================================================================================
    //  PlanSummaryPrompt -- the AI only explains the computed plan. It gets a short digest,
    //  no tools, and may not add numbers or assets. Its output is cleaned before it is sent.
    // =====================================================================================
    public static class PlanSummaryPrompt
    {
        private static readonly Regex assetToken = new Regex(@"\[\[asset:([A-Za-z0-9_-]+)\]\]", RegexOptions.Compiled);
        private static readonly Regex otherToken = new Regex(@"\[\[(?!asset:)[^\]]{0,80}\]\]", RegexOptions.Compiled);

        public static string BuildSystemPrompt(string language)
        {
            string lang;
            if (language == "hi")
            {
                lang = "Write in simple Hindi (Devanagari script). Keep asset codes and units as written.";
            }
            else if (language == "hinglish")
            {
                lang = "Write in simple Hinglish (Hindi in Latin script), the way the user wrote. Keep asset codes and units as written.";
            }
            else
            {
                lang = "Write in simple English.";
            }

            return "You explain a station maintenance plan to a railway S&T maintainer (SSE or JE).\n"
                + "The plan was computed by rules. Do not change its order, priorities or minutes, and do not add any number, asset or fact that is not in the plan digest.\n"
                + "Write 3 to 5 short paragraphs in plain words. No headings, no tables, no bullet lists, no JSON.\n"
                + "Start with item #1. For each planned item say in one or two sentences why it is in that place and what to look at first.\n"
                + "After the first mention of an asset, write its tag exactly as given in the digest, for example [[asset:a3105]].\n"
                + "Call track and point-machine model results a model classification to confirm on site.\n"
                + "If some items are for the next visit, say so in one sentence at the end.\n"
                + "Do not talk about servers, data sources, the monitoring system or yourself.\n"
                + lang;
        }

        public static string BuildDigest(StationPlan plan)
        {
            StringBuilder sb = new StringBuilder();
            string siteName = plan.Site == null ? "Station" : (plan.Site.Name + (string.IsNullOrEmpty(plan.Site.Code) ? "" : " (" + plan.Site.Code + ")"));
            sb.Append("STATION: ").Append(siteName)
              .Append(" | TIME ON SITE: ").Append(plan.Hours).Append(" h")
              .Append(" | SETUP: ").Append(plan.TimeBudget.SetupMinutes).Append(" min\n");
            sb.Append("HEADLINE: ").Append(plan.Headline).Append("\n");

            Dictionary<string, PlanAssetCard> byKey = plan.Assets.ToDictionary(a => a.Key, a => a);
            sb.Append("IN PLAN, IN THIS ORDER:\n");
            int n = 1;
            foreach (string key in plan.TimeBudget.InPlan)
            {
                PlanAssetCard c;
                if (!byKey.TryGetValue(key, out c))
                {
                    continue;
                }

                sb.Append('#').Append(n++).Append(" [[asset:").Append(c.Key).Append("]] ")
                  .Append(c.Name).Append(" | ").Append(c.Subtitle).Append(" | ").Append(c.Priority)
                  .Append(" | ").Append(c.Minutes).Append(" min | ").Append(c.Verdict);
                if (c.Reasons.Count > 0)
                {
                    sb.Append(" | facts: ").Append(string.Join("; ", c.Reasons));
                }

                if (c.Action != null)
                {
                    sb.Append(" | action: ").Append(c.Action.Text);
                }

                sb.Append("\n");
                if (sb.Length > 2600)
                {
                    break;
                }
            }

            if (plan.TimeBudget.NextVisit.Count > 0)
            {
                List<string> later = new List<string>();
                foreach (string key in plan.TimeBudget.NextVisit)
                {
                    PlanAssetCard c;
                    if (byKey.TryGetValue(key, out c))
                    {
                        later.Add("[[asset:" + c.Key + "]] " + c.Name + " (" + c.Priority + ", " + c.Minutes + " min)");
                    }
                }

                sb.Append("NEXT VISIT: ").Append(string.Join("; ", later.Take(8))).Append("\n");
            }

            List<string> healthy = plan.Families.Where(f => f.Healthy.Count > 0).Select(f => f.Label + " " + f.Healthy.Count).ToList();
            if (healthy.Count > 0)
            {
                sb.Append("HEALTHY, NOTHING TO DO: ").Append(string.Join(", ", healthy)).Append("\n");
            }

            if (plan.Assets.Count == 0)
            {
                sb.Append("NOTHING NEEDS ATTENTION TODAY.\n");
            }

            return sb.ToString();
        }

        public static string Sanitize(string text, StationPlan plan)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            HashSet<string> keys = new HashSet<string>(plan.Assets.Select(a => a.Key), StringComparer.Ordinal);
            string s = assetToken.Replace(text, m => keys.Contains(m.Groups[1].Value) ? m.Value : "");
            s = otherToken.Replace(s, "");
            s = s.Replace("[[CARD]]", "").Replace("[[/CARD]]", "").Trim();
            if (s.Length > 5000)
            {
                s = s.Substring(0, 5000);
            }

            return s;
        }
    }
}
