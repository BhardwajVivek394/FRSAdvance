using Domain;
using E7MRIWeb.Areas.FRS25.Controllers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace E7.AiCore
{
    // RosterEngine -- Maintenance Roster rules and helpers (banding, PM grading, drift, config/JSON helpers,
    // the Diagnose model call), moved verbatim from MaintenceRosterController.cs in 2.9.0.0 (Shared AI Core 3c).
    // All members are static: no per-request state. Only access modifiers changed (private -> internal).
    public static partial class RosterEngine
    {
        public const string ComponentVersion = "2.11.0.0";   // 2.11.0.0: Asset Health API (AssetHealthEngine); 2.10.0.0: Division UI 3.0


        // =====================================================================
        // PATCH for MaintenceRosterController.cs  --  make RosLog VISIBLE
        //
        // WHY: the existing RosLog writes only to System.Diagnostics.Trace:
        //        Trace.WriteLine("[ROSTER-WEB] " + level + " " + message);
        //      In production there is usually NO TraceListener, so every roster
        //      log line (INFO/WARN/ERROR and all the new track DEBUG lines) goes
        //      nowhere. This patch ALSO appends each line to a daily log file so
        //      you can actually read it while debugging. Trace is kept for dev.
        //
        // WHERE THE FILE GOES:
        //   default:  ~/App_Data/RosterLog/roster-YYYYMMDD.log
        //   override: <add key="RosLogDir" value="D:\logs\roster" /> in Web.config
        //   (App_Data is not web-served, so the log is not publicly reachable.)
        //
        // LEVELS: nothing is filtered -- INFO/WARN/ERROR/DEBUG are all written.
        //   To mute DEBUG in production, set  <add key="RosLogMinLevel" value="INFO" />
        //   (accepted: DEBUG < INFO < WARN < ERROR). Omit it to log everything.
        //
        // APPLY: replace the existing RosLog method (around line 115) with the
        //        version below, and add the two static fields just above it.
        // =====================================================================

        // ---- add these two static fields next to the other private statics ----
        internal static readonly object _rosLogLock = new object();

        internal static int _rosLogMinRank = -1;   // resolved once from config; -1 = not yet

        internal static bool _rosLogBanner = false;

        internal static bool rosLogSinkFailed = false;   // one-shot alarm: the file sink died


        // ---- OLD (delete) --------------------------------------------------
        //   private static void RosLog(string level, string message)
        //   {
        //       try
        //       {
        //           System.Diagnostics.Trace.WriteLine("[ROSTER-WEB] " + level + " " + message);
        //       }
        //       catch { }
        //   }
        // ---- NEW (paste) ---------------------------------------------------
        internal static int RosLevelRank(string level)
        {
            if (string.IsNullOrEmpty(level)) { return 1; }
            switch (level.Trim().ToUpperInvariant())
            {
                case "DEBUG": return 0;
                case "INFO": return 1;
                case "WARN": return 2;
                case "ERROR": return 3;
                default: return 1;
            }
        }


        internal static void RosLogRaw(string level, string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [ROSTER-WEB v" + ComponentVersion + "] " + level + " " + message;
            try { System.Diagnostics.Trace.WriteLine(line); } catch { }
            try
            {
                string d = (System.Configuration.ConfigurationManager.AppSettings["RosLogDir"] ?? "").Trim();
                if (d.Length == 0) { try { d = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "RosterLog") /* 3c: was HostingEnvironment.MapPath("~/App_Data/RosterLog") -- same folder */; } catch { d = null; } if (string.IsNullOrEmpty(d)) { d = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data\\RosterLog"); } }
                if (!System.IO.Directory.Exists(d)) { System.IO.Directory.CreateDirectory(d); }
                lock (_rosLogLock) { System.IO.File.AppendAllText(System.IO.Path.Combine(d, "roster-" + DateTime.Now.ToString("yyyyMMdd") + ".log"), line + Environment.NewLine); }
            }
            catch { }
        }


        internal static void RosLog(string level, string message)
        {
            // resolve the minimum level once (default: log everything)
            if (_rosLogMinRank < 0)
            {
                string min = (System.Configuration.ConfigurationManager.AppSettings["RosLogMinLevel"] ?? "").Trim();
                _rosLogMinRank = min.Length > 0 ? RosLevelRank(min) : 0;
            }
            if (RosLevelRank(level) < _rosLogMinRank) { return; }

            if (!_rosLogBanner)
            {
                _rosLogBanner = true;
                try { RosLogRaw("INFO", "=== Roster component v" + ComponentVersion + " logging started (pid=" + System.Diagnostics.Process.GetCurrentProcess().Id + ") ==="); } catch { }
            }

            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [ROSTER-WEB v" + ComponentVersion + "] " + level + " " + message;

            // keep Trace (visible in dev / DebugView / a configured listener)
            try { System.Diagnostics.Trace.WriteLine(line); } catch { }

            // ALSO append to a daily file so it is readable in production
            try
            {
                string dir = (System.Configuration.ConfigurationManager.AppSettings["RosLogDir"] ?? "").Trim();
                if (dir.Length == 0)
                {
                    try { dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "RosterLog") /* 3c: was HostingEnvironment.MapPath("~/App_Data/RosterLog") -- same folder */; }
                    catch { dir = null; }
                    if (string.IsNullOrEmpty(dir))
                    { dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data\\RosterLog"); }
                }
                if (!System.IO.Directory.Exists(dir)) { System.IO.Directory.CreateDirectory(dir); }
                string path = System.IO.Path.Combine(dir, "roster-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                lock (_rosLogLock) { System.IO.File.AppendAllText(path, line + Environment.NewLine); }
            }
            catch (Exception ex)
            {
                // logging must never throw -- but say ONCE, loudly, that the file sink is dead,
                // so "the log is empty" is never mistaken for "nothing failed".
                if (!rosLogSinkFailed)
                {
                    rosLogSinkFailed = true;

                    try
                    {
                        System.Diagnostics.Trace.WriteLine("[ROSTER-WEB] FATAL roster file log disabled: "
                            + ex.GetType().Name + " " + ex.Message);
                    }
                    catch
                    {
                    }

                    try
                    {
                        System.Diagnostics.EventLog.WriteEntry("Application",
                            "Roster file log disabled: " + ex.GetType().Name + " " + ex.Message,
                            System.Diagnostics.EventLogEntryType.Warning);
                    }
                    catch
                    {
                    }
                }
            }
        }


        // =====================================================================
        // Item 8 helper -- HttpWebRequest.GetResponse() throws on any non-2xx, and
        // ex.Message is only "The remote server returned an error: (500) ...". The
        // upstream's ACTUAL reason (auth failure, unknown asset) lives in ex.Response
        // and was being thrown away. Used by TrackShort / PmOps / RangeHistory.
        // =====================================================================
        internal static string RosUpstreamBody(Exception ex)
        {
            try
            {
                System.Net.WebException wex = ex as System.Net.WebException;
                if (wex == null || wex.Response == null)
                {
                    return "";
                }

                System.Net.HttpWebResponse resp = wex.Response as System.Net.HttpWebResponse;
                string status = resp != null ? (" http=" + (int)resp.StatusCode) : "";

                using (System.IO.StreamReader sr = new System.IO.StreamReader(wex.Response.GetResponseStream()))
                {
                    string text = sr.ReadToEnd();
                    if (text != null && text.Length > 300)
                    {
                        text = text.Substring(0, 300) + "...";
                    }

                    return status + " upstream=" + text;
                }
            }
            catch
            {
                return "";
            }
        }


        // A date the API can parse, and never the server's idea of "today" silently:
        // an empty value is resolved here and the caller always sees which date it got.
        internal static string NormalizeDate(string rosterDate)
        {
            DateTime d;
            if (!String.IsNullOrEmpty(rosterDate) && DateTime.TryParse(rosterDate, out d))
            {
                return d.ToString("yyyy-MM-dd");
            }
            return DateTime.Now.ToString("yyyy-MM-dd");
        }


        // 401/403 is the one an operator can act on, so it is named rather than lumped in
        // with "something went wrong".
        internal static string DescribeHttp(string what, HttpStatusCode code)
        {
            if (code == HttpStatusCode.Unauthorized || code == HttpStatusCode.Forbidden)
            {
                return what + " refused (session or token expired) -- sign in again.";
            }
            if (code == HttpStatusCode.NotFound)
            {
                return what + " endpoint not found -- check the API deployment.";
            }
            return what + " service returned HTTP " + (int)code + ".";
        }


        // pull the headline counts out of a roll-up / worksheet reply so the log says what the
        // page actually received, not just that it received something. Best-effort and silent:
        // an unexpected shape returns "" rather than logging noise about its own parsing.
        internal static string RosCounts(string body)
        {
            try
            {
                if (string.IsNullOrEmpty(body))
                {
                    return "";
                }

                string bt = body.TrimStart();
                if (!bt.StartsWith("{"))
                {
                    return "";
                }

                Newtonsoft.Json.Linq.JObject jo = Newtonsoft.Json.Linq.JObject.Parse(body);
                string outText = "";

                Newtonsoft.Json.Linq.JArray stations = jo["Stations"] as Newtonsoft.Json.Linq.JArray;
                if (stations != null)
                {
                    outText += " stations=" + stations.Count;
                }

                Newtonsoft.Json.Linq.JArray items = jo["Items"] as Newtonsoft.Json.Linq.JArray;
                if (items != null)
                {
                    outText += " items=" + items.Count;

                    // Did CompositeReason survive GenerateDaily and come back? If this is 0 while
                    // the rescore raised items, the API is dropping fields it does not know and the
                    // WHY can never reach the UI -- worth knowing without guessing.
                    int withWhy = 0;
                    foreach (Newtonsoft.Json.Linq.JToken t in items)
                    {
                        Newtonsoft.Json.Linq.JObject io2 = t as Newtonsoft.Json.Linq.JObject;
                        if (io2 != null && io2["CompositeReason"] != null
                            && !string.IsNullOrEmpty(io2["CompositeReason"].ToString()))
                        {
                            withWhy++;
                        }
                    }

                    outText += " withCompositeReason=" + withWhy;
                }

                if (jo["UrgentCount"] != null)
                {
                    outText += " urgent=" + jo["UrgentCount"].ToString();
                }

                if (jo["SoonCount"] != null)
                {
                    outText += " soon=" + jo["SoonCount"].ToString();
                }

                if (jo["IsSuccess"] != null)
                {
                    outText += " ok=" + jo["IsSuccess"].ToString();
                }

                return outText;
            }
            catch
            {
                return "";
            }
        }


        internal static string Head(string s)
        {
            if (String.IsNullOrEmpty(s)) { return ""; }
            return s.Length > 200 ? s.Substring(0, 200) : s;
        }


        // GET the model endpoint and hand the body back verbatim.
        // Throws on transport failure; the callers turn that into { error }.
        internal static string PmFetch(string url, int timeoutMs)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;   // 2.9.0.0: TLS 1.0/1.1 no longer enabled (process-wide setting)
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = timeoutMs;
            req.Accept = "application/json";
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new System.IO.StreamReader(resp.GetResponseStream()))
            { return sr.ReadToEnd(); }
        }


        internal static string PmUrl(int assetId, int siteId, string suffix)
        {
            string apiBase = (System.Configuration.ConfigurationManager.AppSettings["PmPredictBaseUrl"] ?? "").Trim();
            if (apiBase.Length == 0) { return null; }
            while (apiBase.EndsWith("/")) { apiBase = apiBase.Substring(0, apiBase.Length - 1); }
            return apiBase + "/" + (siteId > 0 ? (siteId.ToString() + "/") : "") + assetId.ToString() + (suffix ?? "");
        }


        // Raw provider transport, cloned from the stable clients: Anthropic
        // /v1/messages (x-api-key + anthropic-version 2023-06-01, content
        // blocks) or an OpenAI-compatible /chat/completions (Bearer,
        // choices[0].message.content).
        internal static string RosterModelCall(string url, string apiKey, bool anthropicShape, string reqBody)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;   // 2.9.0.0: TLS 1.0/1.1 no longer enabled (process-wide setting)
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Accept = "application/json";
            req.Timeout = 60000;
            req.ReadWriteTimeout = 60000;
            if (anthropicShape)
            {
                req.Headers["x-api-key"] = apiKey;
                req.Headers["anthropic-version"] = "2023-06-01";
            }
            else
            {
                req.Headers["Authorization"] = "Bearer " + apiKey;
            }
            byte[] buf = System.Text.Encoding.UTF8.GetBytes(reqBody);
            req.ContentLength = buf.Length;
            using (var rs = req.GetRequestStream()) { rs.Write(buf, 0, buf.Length); }
            string respBody;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new System.IO.StreamReader(resp.GetResponseStream()))
            { respBody = sr.ReadToEnd(); }
            var root = Newtonsoft.Json.Linq.JObject.Parse(respBody);
            if (anthropicShape)
            {
                var content = root["content"] as Newtonsoft.Json.Linq.JArray;
                var sb = new System.Text.StringBuilder();
                if (content != null)
                {
                    foreach (var blk in content)
                    {
                        var o = blk as Newtonsoft.Json.Linq.JObject;
                        if (o != null && (o["type"] == null || o["type"].ToString() == "text") && o["text"] != null)
                        { sb.Append(o["text"].ToString()); }
                    }
                }
                return sb.ToString();
            }
            var choices = root["choices"] as Newtonsoft.Json.Linq.JArray;
            if (choices != null && choices.Count > 0)
            {
                var msg = choices[0]["message"] as Newtonsoft.Json.Linq.JObject;
                if (msg != null && msg["content"] != null) { return msg["content"].ToString(); }
            }
            return "";
        }

        internal static bool RosterParseDay(Newtonsoft.Json.Linq.JObject req, out DateTime day)
        {
            day = DateTime.Today;
            string dateStr = req["date"] != null ? req["date"].ToString().Trim() : "";
            if (dateStr.Length == 0) { return true; }   // no date -> today's roster
            string[] fmts = { "yyyy-MM-dd", "dd-MM-yyyy", "dd/MM/yyyy", "yyyy/MM/dd" };
            if (DateTime.TryParseExact(dateStr, fmts, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out day)) { day = day.Date; return true; }
            if (DateTime.TryParse(dateStr, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out day)) { day = day.Date; return true; }
            day = DateTime.Today; return true;          // unparseable -> today, never a hard failure
        }


        internal static bool RosterDtProp(object o, string name, out DateTime dt)
        {
            dt = DateTime.MinValue;
            try
            {
                var p = o.GetType().GetProperty(name);
                if (p == null) { return false; }
                object v = p.GetValue(o, null);
                if (v == null) { return false; }
                if (v is DateTime) { dt = (DateTime)v; return dt != DateTime.MinValue; }
                return DateTime.TryParse(v.ToString(), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out dt) && dt != DateTime.MinValue;
            }
            catch { return false; }
        }


        internal static Newtonsoft.Json.Linq.JToken RosterPropCI(Newtonsoft.Json.Linq.JObject o, string name)
        {
            foreach (var p in o.Properties())
            { if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) { return p.Value; } }
            return null;
        }

        internal static string RosterFirstCI(Newtonsoft.Json.Linq.JObject o, string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                var v = RosterPropCI(o, names[i]);
                if (v != null && v.Type != Newtonsoft.Json.Linq.JTokenType.Null)
                { string s = v.ToString().Trim(); if (s.Length > 0) { return s; } }
            }
            return null;
        }

        internal static bool RosterDateCI(Newtonsoft.Json.Linq.JObject o, string[] names, out DateTime dt)
        {
            dt = DateTime.MinValue;
            string s = RosterFirstCI(o, names);
            if (string.IsNullOrEmpty(s)) { return false; }
            return DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out dt);
        }


        // rank a point-machine state string exactly as the view's rosPmWorst and AiChat's
        // aiPmWorst do, so the roster count and both UIs can never disagree
        internal static int RosPmStateRank(string state)
        {
            string sv = (state ?? "").Trim().ToUpperInvariant();
            if (sv.IndexOf("URGENT") >= 0 || sv.IndexOf("INSPECT") >= 0)
            {
                return 3;
            }

            if (sv.IndexOf("MAINTAIN") >= 0 || sv == "SOON")
            {
                return 2;
            }

            if (sv.IndexOf("WATCH") >= 0)
            {
                return 1;
            }

            return 0;
        }


        // worst state across a/b and Normal/Reverse in the SMALL (no-query) pm-operation-v4 reply
        internal static int RosPmWorstState(Newtonsoft.Json.Linq.JObject pm, out string worstState)
        {
            worstState = "NORMAL";
            int worst = 0;
            if (pm == null)
            {
                return 0;
            }

            string[] ends = { "a", "b" };
            string[] dirs = { "Normal", "Reverse" };

            for (int e = 0; e < ends.Length; e++)
            {
                Newtonsoft.Json.Linq.JObject end = pm[ends[e]] as Newtonsoft.Json.Linq.JObject;
                if (end == null)
                {
                    continue;
                }

                Newtonsoft.Json.Linq.JObject states = end["states"] as Newtonsoft.Json.Linq.JObject;
                if (states == null)
                {
                    continue;
                }

                for (int d = 0; d < dirs.Length; d++)
                {
                    Newtonsoft.Json.Linq.JObject st = states[dirs[d]] as Newtonsoft.Json.Linq.JObject;
                    if (st == null || st["state"] == null)
                    {
                        continue;
                    }

                    string sv = st["state"].ToString();
                    int r = RosPmStateRank(sv);
                    if (r > worst)
                    {
                        worst = r;
                        worstState = sv.Trim().ToUpperInvariant();
                    }
                }
            }

            return worst;
        }


        // The condition text is the point-machine equivalent of the track's simple_summary.
        // In the DETAILS reply it sits at rows[].a_classification.condition; the small reply is
        // not documented, so look in the obvious places and settle for worst_confirmed rather
        // than showing the operator nothing.
        internal static string RosPmCondition(Newtonsoft.Json.Linq.JObject pm)
        {
            try
            {
                if (pm == null)
                {
                    return "";
                }

                string[] ends = { "a", "b" };
                string[] dirs = { "Normal", "Reverse" };
                string best = "";

                for (int e = 0; e < ends.Length; e++)
                {
                    Newtonsoft.Json.Linq.JObject end = pm[ends[e]] as Newtonsoft.Json.Linq.JObject;
                    if (end == null)
                    {
                        continue;
                    }

                    Newtonsoft.Json.Linq.JObject states = end["states"] as Newtonsoft.Json.Linq.JObject;
                    if (states == null)
                    {
                        continue;
                    }

                    for (int d = 0; d < dirs.Length; d++)
                    {
                        Newtonsoft.Json.Linq.JObject st = states[dirs[d]] as Newtonsoft.Json.Linq.JObject;
                        if (st == null)
                        {
                            continue;
                        }

                        string[] keys = { "condition", "worst_condition", "classification" };
                        for (int k = 0; k < keys.Length; k++)
                        {
                            Newtonsoft.Json.Linq.JToken node = st[keys[k]];
                            if (node == null)
                            {
                                continue;
                            }

                            string v = node.Type == Newtonsoft.Json.Linq.JTokenType.Object
                                ? (node["condition"] != null ? node["condition"].ToString() : "")
                                : node.ToString();

                            v = (v ?? "").Trim();
                            if (v.Length > 0 && !v.Equals("null", StringComparison.OrdinalIgnoreCase) && best.Length == 0)
                            {
                                best = v;
                            }
                        }

                        if (best.Length == 0 && st["worst_confirmed"] != null)
                        {
                            string wc = st["worst_confirmed"].ToString().Trim().ToUpperInvariant();
                            if (wc.Length > 0 && wc != "NORMAL" && wc != "INVALID")
                            {
                                best = wc;
                            }
                        }
                    }
                }

                return best;
            }
            catch (Exception ex)
            {
                RosLog("WARN", "RosPmCondition " + ex.GetType().Name + " " + ex.Message);
                return "";
            }
        }


        // grade string off one op record, whatever the upstream calls the field
        internal static string RosOpGrade(Newtonsoft.Json.Linq.JObject op)
        {
            if (op == null)
            {
                return "";
            }

            string[] keys = { "confirmed", "grade", "classification", "state" };
            for (int k = 0; k < keys.Length; k++)
            {
                Newtonsoft.Json.Linq.JToken node = op[keys[k]];
                if (node == null)
                {
                    continue;
                }

                if (node.Type == Newtonsoft.Json.Linq.JTokenType.Object)
                {
                    Newtonsoft.Json.Linq.JToken g = node["grade"] ?? node["confirmed"];
                    if (g != null)
                    {
                        return g.ToString();
                    }
                }
                else
                {
                    return node.ToString();
                }
            }

            return "";
        }


        // timestamp off one op record, for the recent-window rate
        internal static bool RosOpTime(Newtonsoft.Json.Linq.JObject op, out DateTime when)
        {
            when = DateTime.MinValue;
            if (op == null)
            {
                return false;
            }

            string[] keys = { "time", "timestamp", "at", "start", "op_time" };
            for (int k = 0; k < keys.Length; k++)
            {
                Newtonsoft.Json.Linq.JToken node = op[keys[k]];
                if (node == null)
                {
                    continue;
                }

                if (DateTime.TryParse(node.ToString(), System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out when))
                {
                    return true;
                }
            }

            return false;
        }


        internal static bool RosGradeIsProblem(string g)
        {
            if (string.IsNullOrEmpty(g)) { return false; }
            g = g.Trim().ToUpperInvariant();
            return g != "NORMAL" && g != "INVALID";
        }


        internal static void RosCollectGrade(Newtonsoft.Json.Linq.JObject op, string key,
            System.Collections.Generic.List<string> into)
        {
            if (op == null) { return; }
            var node = op[key];
            if (node == null) { return; }
            if (node.Type == Newtonsoft.Json.Linq.JTokenType.Object)
            {
                var g = node["grade"];
                if (g != null) { into.Add(g.ToString()); }
            }
            else { into.Add(node.ToString()); }   // flat "grade":"..."
        }


        // Even sampling down to at most `max` points, first and last always kept so the
        // drawn line starts and ends where the real series does.
        internal static System.Collections.Generic.List<double> RosThin(
            System.Collections.Generic.List<double> xs, int max)
        {
            var outv = new System.Collections.Generic.List<double>();
            if (xs == null || xs.Count == 0)
            {
                return outv;
            }

            if (xs.Count <= max)
            {
                for (int i = 0; i < xs.Count; i++)
                {
                    outv.Add(Math.Round(xs[i], 3));
                }

                return outv;
            }

            double step = (double)(xs.Count - 1) / (max - 1);
            for (int i = 0; i < max; i++)
            {
                int idx = (int)Math.Round(i * step);
                if (idx > xs.Count - 1)
                {
                    idx = xs.Count - 1;
                }

                outv.Add(Math.Round(xs[idx], 3));
            }

            return outv;
        }


        internal static double RosMean(System.Collections.Generic.List<double> xs, int from, int count)
        {
            if (xs == null || count <= 0 || from < 0 || from >= xs.Count) { return 0.0; }
            int to = Math.Min(xs.Count, from + count);
            double s = 0.0; int n = 0;
            for (int i = from; i < to; i++) { s += xs[i]; n++; }
            return n > 0 ? s / n : 0.0;
        }


        // Compute drift for one AvgValues series. Returns 0 when too short to judge.
        // A series with two or fewer distinct values is a state flag, not a measurement.
        // Its "drift" is meaningless -- a mean moving 0.1 -> 0.6 reads as 500%.
        internal static bool RosSeriesIsDigital(System.Collections.Generic.List<double> series)
        {
            if (series == null || series.Count == 0)
            {
                return false;
            }

            var seen = new System.Collections.Generic.List<double>();
            for (int i = 0; i < series.Count; i++)
            {
                bool known = false;
                for (int k = 0; k < seen.Count; k++)
                {
                    if (Math.Abs(seen[k] - series[i]) < 1e-9)
                    {
                        known = true;
                        break;
                    }
                }

                if (!known)
                {
                    seen.Add(series[i]);
                    if (seen.Count > 2)
                    {
                        return false;
                    }
                }
            }

            return true;
        }


        // v2.6.0.0 -- skipReason tells the caller WHY a drift was not computed, instead of
        // returning a fabricated number. The old version fell back to a base of 1.0 when the
        // early mean was near zero, which turned an absolute delta into a fake percentage:
        // a 0.6 unit move on a normally-zero channel read as 60% and tripped URGENT.
        internal static double RosSeriesDriftPct(System.Collections.Generic.List<double> series, double averageValue,
            out string dir, out string skipReason)
        {
            dir = "";
            skipReason = "";

            if (series == null || series.Count < 6)
            {
                skipReason = "series too short (" + (series == null ? 0 : series.Count) + " < 6)";
                return 0.0;
            }

            if (RosSeriesIsDigital(series))
            {
                skipReason = "digital/state series";
                return 0.0;
            }

            int q = Math.Max(2, series.Count / 4);
            double early = RosMean(series, 0, q);
            double late = RosMean(series, series.Count - q, q);

            double base_ = 0.0;
            if (Math.Abs(early) > 1e-6)
            {
                base_ = early;
            }
            else if (Math.Abs(averageValue) > 1e-6)
            {
                base_ = averageValue;
            }
            else
            {
                // no honest denominator -- say so rather than inventing 1.0
                skipReason = "baseline is zero, percent drift undefined";
                return 0.0;
            }

            double drift = (late - early) / Math.Abs(base_) * 100.0;
            dir = drift > 0 ? "rising" : (drift < 0 ? "falling" : "");
            return drift;
        }


        // How close the recent level sits to its safe limits, as a percentage of the safe band.
        // 0 = at the middle, 100 = exactly on a limit, >100 = outside. Returns false when the
        // attribute has no usable safe band.
        internal static bool RosSafeBandUse(System.Collections.Generic.List<double> series,
            double minSafe, double maxSafe, out double usedPct, out string edge)
        {
            usedPct = 0.0;
            edge = "";

            if (series == null || series.Count == 0)
            {
                return false;
            }

            double span = maxSafe - minSafe;
            if (span <= 1e-6)
            {
                return false;
            }

            int q = Math.Max(1, series.Count / 4);
            double late = RosMean(series, series.Count - q, q);
            double mid = (maxSafe + minSafe) / 2.0;
            double half = span / 2.0;

            usedPct = Math.Abs(late - mid) / half * 100.0;
            edge = late >= mid ? "max" : "min";
            return true;
        }


        // =====================================================================
        // HOW GetRollup ADOPTS THIS (server, to change the LANDING counts):
        //   During the run/Regenerate, for each candidate asset of a site
        //   (all assets, not only alerted ones, to "add" pure drifters):
        //     var d = RangeHistory(assetId, start, end);   // start/end = the run window, ddMMyyyy_HHmmss
        //     // fold d.band into the asset's effective band (worst of alert / short / pm / drift):
        //     if (d.band == "URGENT") effBand = Urgent;
        //     else if (d.band == "SOON" && effBand < Soon) effBand = Soon;
        //   Then count assets per effBand as today. Cache d per run so it is not
        //   re-fetched on every Load. (Bounded: one RangeHistory call per asset
        //   per run; reuse the 10-min band cache pattern from RosterSiteRanges if
        //   you want to throttle.)
        // =====================================================================




        // =====================================================================
        // DROP-IN for MaintenceRosterController  (Areas/FRS25/Controllers/)
        // Paste this method inside the class (e.g. right after PmOps()).
        // No new using directives needed (same set as Roster_Live_Action.cs).
        //
        // PURPOSE  (TRACK CIRCUITS ONLY)
        //   Same-origin proxy for the track shorting/leakage predictor, and a
        //   server-computed SHORTING summary per track. Browser must not hit the
        //   Historian (:8005) directly, so this proxies it server-side.
        //
        //   Upstream (Historian):
        //     GET {HistorianApiBaseUrl}/api/asset/ai-prediction/track/{siteId}/{assetId}
        //   Web.config:
        //     <add key="HistorianApiBaseUrl" value="http://172.31.25.102:8005" />
        //     <add key="HistorianApiAuth"    value="user:pass" />   (optional Basic)
        //
        // UPSTREAM SHAPE (from the predict_track_health tool response):
        //   { asset_id, track_name, overall_condition,
        //     leakage_info:{ leakage_detected(bool), leakage_status, leakage_type,
        //                    severity, simple_summary, reason },
        //     score_breakdown:{...},
        //     major_events:[ { Start_Time, End_Time, Duration_Hours, Dominant_Cause }, ... ] }
        //   leakage_type locates the fault: "Both (Glued Joint)" = external shorting
        //   at the glued joint, "IF Only (Internal)" = feed side, "IR Only (Relay Side)"
        //   = relay side.  major_events are the shorting/leakage WINDOWS.
        //
        // RULE (yours):
        //   hasShorting = there are major_events (shorting windows) OR leakage_detected.
        //   live        = shorting is happening NOW = leakage_detected, OR the current
        //                 time falls inside a major_event window (Start..End), OR an
        //                 event has no End (still open).
        //   Band for the roster count:
        //     live         -> "URGENT"     (track is shorting right now)
        //     hasShorting  -> "SOON"       (had shorting windows, not currently live)
        //     else         -> ""
        //
        // RETURNS:
        //   { siteId, assetId, events, hasShorting, live, severity, overall,
        //     worstCause, band, lastEnd }
        //
        // Version: bump ComponentVersion, e.g.
        //   2.6.0.0 - TrackShort proxy (ai-prediction/track major_events + live shorting)
        // =====================================================================

        internal static bool RosTryTime(string s, out DateTime dt)
        {
            dt = DateTime.MinValue;
            if (string.IsNullOrEmpty(s)) { return false; }
            return DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out dt);
        }


        // MVC's Json() serialises with JavaScriptSerializer, which does not understand Newtonsoft tokens:
        // a JArray of JObject leaves the server as nested empty arrays ([[[]],[[]]]), so every field of every
        // event is lost in transport. Convert to plain dictionaries / lists / primitives first. Dates are
        // written as text so they do not become \/Date(...)\/.
        internal static object RosPlain(Newtonsoft.Json.Linq.JToken token)
        {
            if (token == null)
            {
                return null;
            }

            Newtonsoft.Json.Linq.JObject obj = token as Newtonsoft.Json.Linq.JObject;

            if (obj != null)
            {
                var map = new Dictionary<string, object>();

                foreach (Newtonsoft.Json.Linq.JProperty prop in obj.Properties())
                {
                    map[prop.Name] = RosPlain(prop.Value);
                }

                return map;
            }

            Newtonsoft.Json.Linq.JArray arr = token as Newtonsoft.Json.Linq.JArray;

            if (arr != null)
            {
                var list = new List<object>();

                foreach (Newtonsoft.Json.Linq.JToken item in arr)
                {
                    list.Add(RosPlain(item));
                }

                return list;
            }

            Newtonsoft.Json.Linq.JValue val = token as Newtonsoft.Json.Linq.JValue;

            if (val == null)
            {
                return token.ToString();
            }

            if (val.Value is DateTime)
            {
                return ((DateTime)val.Value).ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            }

            if (val.Value is DateTimeOffset)
            {
                return ((DateTimeOffset)val.Value).ToString("yyyy-MM-dd HH:mm:ss zzz", System.Globalization.CultureInfo.InvariantCulture);
            }

            return val.Value;
        }

        // =====================================================================
        // After this, tail the log and you will see, interleaved with the server
        // lines, entries like:
        //   .. [ROSTER-WEB v2.6.0.0] ERROR UI[TRACK] fetch failed 46T asset=41814 ... @ https://.../MaintenceRoster (view v2.6.0.0) user=12
        //   .. [ROSTER-WEB v2.6.0.0] ERROR UI[WINDOW] Cannot read properties of undefined ... @ .../Index:4821:17 (view v2.6.0.0)
        //   .. [ROSTER-WEB v2.6.0.0] ERROR UI[PROMISE] <rejected reason> (view v2.6.0.0)
        // =====================================================================
        // =====================================================================
        // DROP-IN for MaintenceRosterController.cs
        // Makes the LANDING KPI counts COMPOSITE (alert + track-short + pm-ops +
        // avg-drift), not alert-only. It re-scores built.Items INSIDE Regenerate,
        // BEFORE they are posted to Roster/GenerateDaily -- so the persisted
        // roster (and therefore GetRollup / the KPI strip / the station table)
        // reflect the composite. The browser cannot change these numbers; this is
        // the only place they can change.
        //
        // It REUSES the three proxy actions you already pasted (TrackShort, PmOps,
        // RangeHistory) -- one source of truth, including the resolved-leak fix --
        // by reading the `band` off each JsonResult. No logic is duplicated.
        //
        // REQUIRES (already added): TrackShort(sid,aid), PmOps(sid,aid[,start,end]),
        //   RangeHistory(aid,start,end), RosLog. Uses Newtonsoft.Json.Linq (JArray/JObject).
        //
        // ASSUMPTIONS (confirm against your roster item + GenerateDaily):
        //   1. Each built.Items entry serialises with fields: AssetId, SiteId,
        //      Priority, AssetTypeName. (The view already reads these keys.)
        //   2. Roster/GenerateDaily derives the roll-up bands FROM each item's
        //      Priority (the v2.1.0.0 "REAL ITEMS" change). If the backend re-derives
        //      priority server-side and ignores the posted value, raising it here has
        //      no effect -- then the same rule must live in RosterItemBuilder instead.
        //   3. Priority values are URGENT / SOON / MONITOR (case-insensitive). We
        //      write back UPPERCASE; if GenerateDaily is case/enum-sensitive, match it.
        //
        // COST: during Regenerate this makes up to 3 Historian/FRS calls per asset
        //   (sequential). For a large division that adds seconds-to-a-minute to the
        //   build. It runs once per Regenerate / nightly run and is cached in the
        //   persisted roster, NOT on every Load. Parallelise later if needed.
        //
        // Version: bump ComponentVersion to 2.6.0.0.
        // =====================================================================

        internal static int RosPriRank(string p)
        {
            p = (p ?? "").Trim().ToUpperInvariant();
            return p == "URGENT" ? 1 : (p == "SOON" ? 2 : 3);
        }


        // worst (most urgent) of two bands; "" == none
        internal static string RosWorstBand(string a, string b)
        {
            int r = Math.Min(RosPriRank(string.IsNullOrEmpty(a) ? "MONITOR" : a),
                             RosPriRank(string.IsNullOrEmpty(b) ? "MONITOR" : b));
            return r == 1 ? "URGENT" : (r == 2 ? "SOON" : "");
        }


        // raise cur by band; never lowers, preserves cur when band doesn't apply
        internal static string RosRaisePriority(string cur, string band)
        {
            if (string.IsNullOrEmpty(band)) { return cur; }
            int r = Math.Min(RosPriRank(cur), RosPriRank(band));
            if (r == 1) { return "URGENT"; }
            if (r == 2) { return "SOON"; }
            return cur;
        }


        // v2.6.0.0 -- MUST mirror the view's rosFamily, cause fallback included. The old version
        // looked at AssetTypeName only. The view bothered to build a cause fallback, which means
        // AssetTypeName is not always populated on a roster item -- and when it is not, the server
        // classified EVERY asset as "Other", so the rescue never called TrackShort or PmOps at all.
        // Silent, because a call that is never made cannot log a dropped signal.
        internal static string RosFamilyOf(string typeName, string cause)
        {
            string t = (typeName ?? "").ToUpperInvariant();
            string c = (cause ?? "").ToUpperInvariant();

            if (t.IndexOf("POINT") >= 0 || c.StartsWith("PT "))
            {
                return "Point";
            }

            if (t.IndexOf("SIGNAL") >= 0 || c.StartsWith("SIG") || c.IndexOf("ROSIG") >= 0)
            {
                return "Signal";
            }

            if (t.IndexOf("TRACK") >= 0 || c.StartsWith("TC ") || c.IndexOf("RAIL") >= 0 || c.IndexOf("BALST") >= 0)
            {
                return "Track";
            }

            return "Other";
        }




        // The view's rosPriEff has a fifth rule the server never had: a SHORT in the alert's
        // dominant cause raises the band with no fetch at all (rosIsShort). Without it an asset
        // could read SOON in the peek list and MONITOR in the station column right beside it.
        // Source order matches the view: Evidence15d.dominantCause first, then the item's Reason.
        internal static readonly System.Text.RegularExpressions.Regex RosShortRx =
            new System.Text.RegularExpressions.Regex(@"(^|[^A-Z])SHORT([^A-Z]|$)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
                | System.Text.RegularExpressions.RegexOptions.Compiled);


        internal static string RosCauseShortBand(Newtonsoft.Json.Linq.JObject it, out string cause, out bool sourceFound)
        {
            cause = "";
            sourceFound = false;

            try
            {
                bool activeNow = false;
                Newtonsoft.Json.Linq.JToken evTok = it["Evidence15d"] ?? it["evidence15d"];

                if (evTok != null)
                {
                    string raw = evTok.Type == Newtonsoft.Json.Linq.JTokenType.String ? evTok.ToString() : evTok.ToString();
                    if (!string.IsNullOrEmpty(raw))
                    {
                        Newtonsoft.Json.Linq.JObject ev = Newtonsoft.Json.Linq.JObject.Parse(raw);
                        sourceFound = true;
                        if (ev["dominantCause"] != null)
                        {
                            cause = ev["dominantCause"].ToString();
                        }

                        if (ev["activeNow"] != null)
                        {
                            bool.TryParse(ev["activeNow"].ToString(), out activeNow);
                        }
                    }
                }

                if (string.IsNullOrEmpty(cause))
                {
                    Newtonsoft.Json.Linq.JToken rTok = it["Reason"] ?? it["reason"];
                    if (rTok != null)
                    {
                        sourceFound = true;
                        cause = rTok.ToString().Split('.')[0];
                    }
                }

                if (string.IsNullOrEmpty(cause) || !RosShortRx.IsMatch(cause))
                {
                    return "";
                }

                // same as the view: an active short is Urgent, a past one is Soon
                return activeNow ? "URGENT" : "SOON";
            }
            catch (Exception ex)
            {
                RosLog("WARN", "RosCauseShortBand " + ex.GetType().Name + " " + ex.Message);
                return "";
            }
        }


        // =====================================================================
        // The SITE SWEEP that lived here in 2.6.0.0 has been REMOVED in 2.7.0.0.
        //
        // It harvested asset ids out of FRSAttributeRange because no asset list was available,
        // then walked them in a second pass beside the alert-first rescore. RosCompositeRescore
        // is now asset-first and covers the same ground in ONE pass from a real asset list
        // (IAssetService.GetAssestBy), so keeping both would mean two places deciding which
        // assets get scored -- and they would drift.
        //
        // Still used by the asset-first loop: RosSweepDegree, RosSweepBudgetSeconds.
        // No longer used: RosSweepOff, RosSweepPm, RosSweepPmDegree, RosSweepMaxAssets,
        // RosAddDriftPct, RosAddSafePct, RosAddTrkEvents.
        // =====================================================================

        internal static int RosCfgInt(string key, int fallback)
        {
            try
            {
                int v;
                if (int.TryParse((System.Configuration.ConfigurationManager.AppSettings[key] ?? "").Trim(), out v) && v > 0)
                {
                    return v;
                }
            }
            catch
            {
            }

            return fallback;
        }


        // Accepts the spellings people actually type. A kill switch that silently ignores
        // value="1" and leaves the feature running is worse than no kill switch, so an
        // unrecognised value is logged rather than quietly falling back.
        internal static bool RosCfgBool(string key, bool fallback)
        {
            try
            {
                string v = (System.Configuration.ConfigurationManager.AppSettings[key] ?? "").Trim().ToUpperInvariant();

                if (v.Length == 0)
                {
                    return fallback;
                }

                if (v == "TRUE" || v == "1" || v == "YES" || v == "Y" || v == "ON")
                {
                    return true;
                }

                if (v == "FALSE" || v == "0" || v == "NO" || v == "N" || v == "OFF")
                {
                    return false;
                }

                RosLog("WARN", "appSetting " + key + "=\"" + v + "\" is not a recognised true/false value -- using "
                    + (fallback ? "true" : "false") + ". Use true or false.");
            }
            catch
            {
            }

            return fallback;
        }


        // Both sentences, one column. Skips the join when they are empty or say the same thing,
        // and caps at the SignalNote column width so a long reason cannot truncate the verdict.
        internal static string RosJoinNote(string summary, string reason)
        {
            summary = (summary ?? "").Trim();
            reason = (reason ?? "").Trim();

            if (reason.Length == 0 || string.Equals(summary, reason, StringComparison.OrdinalIgnoreCase))
            {
                return summary.Length > 480 ? summary.Substring(0, 480) : summary;
            }

            if (summary.Length == 0)
            {
                return reason.Length > 480 ? reason.Substring(0, 480) : reason;
            }

            string joined = summary + " -- " + reason;
            return joined.Length > 480 ? joined.Substring(0, 480) : joined;
        }








        // bounded parallel map -- these are synchronous HttpWebRequest calls, so every one blocks
        // a thread-pool thread. Unbounded would starve the app on a large site.
        internal static void RosParallel<T>(System.Collections.Generic.IList<T> items, int degree, Action<T> body)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }

            // every call targets ONE host, and .NET caps concurrent connections per host. Without
            // this the tasks run in parallel while the requests quietly queue two at a time.
            if (ServicePointManager.DefaultConnectionLimit < degree + 4)
            {
                ServicePointManager.DefaultConnectionLimit = degree + 4;
            }

            var opts = new System.Threading.Tasks.ParallelOptions();
            opts.MaxDegreeOfParallelism = Math.Max(1, degree);

            System.Threading.Tasks.Parallel.ForEach(items, opts, item =>
            {
                try
                {
                    body(item);
                }
                catch (Exception ex)
                {
                    RosLog("WARN", "Parallel task " + ex.GetType().Name + " " + ex.Message);
                }
            });
        }


        internal static int RosJInt(Newtonsoft.Json.Linq.JObject o, params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                Newtonsoft.Json.Linq.JToken t = o[names[i]];

                if (t == null || t.Type == Newtonsoft.Json.Linq.JTokenType.Null)
                {
                    continue;
                }

                int v;
                if (int.TryParse(t.ToString(), out v) && v != 0)
                {
                    return v;
                }
            }

            return 0;
        }


        internal static string RosJStr(Newtonsoft.Json.Linq.JObject o, params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                Newtonsoft.Json.Linq.JToken t = o[names[i]];

                if (t == null || t.Type == Newtonsoft.Json.Linq.JTokenType.Null)
                {
                    continue;
                }

                string v = t.ToString().Trim();

                if (v.Length > 0)
                {
                    return v;
                }
            }

            return "";
        }


        internal static string RosEscJson(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return "";
            }

            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
        }


        /// <summary>
        /// ASSET-FIRST composition (2.7.0.0). Was alert-first, which meant an asset that was
        /// shorting but had raised no alert was never called and never stored.
        ///
        ///   1. every site in the division  ->  every asset  (GetAssestBy)
        ///   2. TRACK -> TrackShort, POINT_MACHINE -> PmOps
        ///   3. alert present  -> the built item, plus the signal bands
        ///      no alert, band -> a signal-only item, IsSwept = 1
        ///      neither        -> nothing
        ///
        /// The site list comes from the division, not from the alerts: a site with ZERO alerts
        /// is exactly where a silently shorting track hides, and an alert-first loop can never
        /// see it.
        ///
        /// Counting follows from the stored columns and needs no separate bookkeeping:
        /// Alert excludes IsSwept, Track shorting counts TrackShortBand, PM counts PmBand.
        /// </summary>
        internal static string RosRosterFamily(int id) { return id == 1 ? "Track" : id == 2 ? "Signal" : id == 3 ? "Point" : id == 34 ? "IPS" : "Other"; }




        internal static void RosAttachSources(Newtonsoft.Json.Linq.JObject item, Newtonsoft.Json.Linq.JObject sources, string start, string end)
        {
            Newtonsoft.Json.Linq.JObject evidence;
            try { evidence = Newtonsoft.Json.Linq.JObject.Parse((string)item["Evidence15d"] ?? "{}"); } catch { evidence = new Newtonsoft.Json.Linq.JObject(); }
            evidence["sources"] = sources.DeepClone(); evidence["sourceWindowStart"] = start; evidence["sourceWindowEnd"] = end; evidence["sourceSchema"] = 2;
            item["Evidence15d"] = evidence.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
