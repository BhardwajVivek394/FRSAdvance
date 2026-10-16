using System;

namespace E7FRSAdvance.AiCore
{
    // ----------------------------------------------------------------------
    //  main branch only -- port of the 619 Haiku / AiModelConfig work (619 = E7.AiCore 1.0.198.0).
    //
    //  main has no E7.AiCore project, so AiModelConfig / AiEngineInfo live in the web project.
    //  Namespace E7FRSAdvance.AiCore (NOT E7.AiCore): a server that ran the 619 build still has
    //  E7.AiCore.dll in bin, and Razor views compile against every bin assembly -- the same type name
    //  in both would fail the views with "ambiguous type". When main later gets the E7.AiCore
    //  project, delete these files and point the callers back to E7.AiCore.
    //
    //  This file supplies the two E7.AiCore helpers AiModelConfig needs:
    //    AiSettings -- exact copy of the parse helpers from E7.AiCore/Settings/AiSettings.cs (619)
    //    AiCoreLog  -- writes to the same ai-log file as AiChatController (App_Data\AiToolLogs)
    // ----------------------------------------------------------------------
    internal static class AiSettings
    {
        public static int ParseInt(string raw, int def)
        {
            int v;
            return int.TryParse(raw, out v) ? v : def;
        }

        public static bool ParseBool(string raw, bool def)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return def;
            }
            raw = raw.Trim();
            return raw.Equals("true", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("1", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("on", StringComparison.OrdinalIgnoreCase);
        }

        public static string ParseStr(string raw, string def)
        {
            string v = raw;
            return string.IsNullOrWhiteSpace(v) ? def : v.Trim();
        }
    }

    internal static class AiCoreLog
    {
        /// <summary>ai-log line (App_Data\AiToolLogs\ai-log-YYYYMMDD.txt), same writer as AiChatController.</summary>
        internal static void Write(string level, string tag, string message)
        {
            E7FRSAdvance.Areas.FRS25.Controllers.AiChatController.AiLogStatic(level, tag, message);
        }
    }

    // FRS Advance: one place that decides where the log files go, so every writer and the AiDiag
    // "L - LOGS" check agree. AiLogDir / RosLogDir in Web.config move a folder (e.g. when the app
    // pool cannot write App_Data); empty or missing = the original App_Data folder.
    internal static class AiLogPaths
    {
        /// <summary>ai-log-*.txt, ai-tool-*.jsonl, chat logs: AiLogDir, else App_Data\AiToolLogs.</summary>
        internal static string AiToolLogs
        {
            get
            {
                string d = (System.Configuration.ConfigurationManager.AppSettings["AiLogDir"] ?? "").Trim();
                if (d.Length > 0)
                {
                    return d;
                }
                return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "AiToolLogs");
            }
        }

        /// <summary>roster-*.log: RosLogDir, else ~/App_Data/RosterLog (same rule as MaintenceRosterController.RosLog).</summary>
        internal static string RosterLogs
        {
            get
            {
                string d = (System.Configuration.ConfigurationManager.AppSettings["RosLogDir"] ?? "").Trim();
                if (d.Length > 0)
                {
                    return d;
                }
                try
                {
                    d = System.Web.Hosting.HostingEnvironment.MapPath("~/App_Data/RosterLog");
                }
                catch
                {
                    d = null;
                }
                if (string.IsNullOrEmpty(d))
                {
                    d = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data\\RosterLog");
                }
                return d;
            }
        }

        /// <summary>Creates the folder and writes/deletes a probe file. null = writable, else the reason.</summary>
        internal static string ProbeWrite(string dir)
        {
            try
            {
                System.IO.Directory.CreateDirectory(dir);
                string probe = System.IO.Path.Combine(dir, "_write_probe.tmp");
                System.IO.File.WriteAllText(probe, "probe");
                System.IO.File.Delete(probe);
                return null;
            }
            catch (Exception ex)
            {
                return ex.GetType().Name + ": " + ex.Message;
            }
        }
    }
}
