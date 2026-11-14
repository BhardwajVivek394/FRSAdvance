using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace E7.AiCore.Judge
{
    // ============================================================================================
    // JudgeFileLog (2.6) -- the judge's OWN log file. Deliberately self-contained.
    //
    // WHY A NEW ONE. The judge already fed AnalysisEngine.AiLogStatic (App_Data\AiToolLogs\
    // ai-log-YYYYMMDD.txt) and JudgeAudit (App_Data\AlertJudge\Audit\judge-*.jsonl). Neither was
    // producing a readable file on the deployed server, and chasing why -- a sink that was never
    // wired, a directory the app pool cannot write, a setting left off -- is not worth the time when
    // the whole point is to see what the judge is doing. So this writer depends on nothing but
    // System.IO: it creates its own folder, names its own file, and reports its own failures.
    //
    //   <application root>\JudgeAIlogs\judgeai-YYYY-MM-DD.log
    //
    // For D:\E7WebLatest_20_08\e7mriv2web\E7MRIV2Web that is
    //   D:\E7WebLatest_20_08\e7mriv2web\E7MRIV2Web\JudgeAIlogs\judgeai-2026-10-09.log
    //
    // Override the folder with the appSetting JudgeAiLogDir (absolute, or relative to the root).
    // Turn it off with JudgeAiLogEnabled = false.
    //
    // IF IT CANNOT WRITE, it does not throw and it does not go quiet: the reason is returned in the
    // response as meta.logError, next to meta.logFile which is the path it tried. That is the one
    // thing the old logs could not tell you.
    //
    // 2.7 -- IT NO LONGER JUST REPORTS THE FAILURE, IT WORKS AROUND IT.
    //
    // 1.0.181.0 could not create its folder on the test server:
    //   logError: UnauthorizedAccessException: Access to the path
    //             'C:\inetpub\wwwroot\E7FRS25WebTest\JudgeAIlogs' is denied.
    // The app pool identity has no write permission on the application root -- which is normal and
    // correct for an IIS site; wwwroot is deployed content, not a scratch area. Reporting that and
    // then logging nothing is useless: the whole point of the log is to be there when something
    // needs tracing, and "grant Modify on a folder" is not a thing that gets done at 2 a.m.
    //
    // So the folder is now PROBED, not assumed. Candidates, in order:
    //
    //   1. JudgeAiLogDir, if set (absolute, or relative to the application root)
    //   2. <application root>\JudgeAIlogs           -- where you asked for it
    //   3. <application root>\App_Data\JudgeAIlogs  -- App_Data is already writable: the judge audit
    //                                                  (App_Data\AlertJudge\Audit) proves it
    //   4. <ASP.NET temp>\JudgeAIlogs               -- always writable by the pool identity
    //
    // Each candidate is proved by actually creating the folder and writing a probe byte, not by
    // asking the OS about permissions (which lies often enough under IIS to be worthless). The first
    // that works wins and is cached. If a later write fails -- disk fills, folder deleted underneath
    // us -- the next candidate is tried, so logging repairs itself instead of stopping.
    //
    // meta.logFile says where it ACTUALLY landed; meta.logNote says so when that was not the first
    // choice: "could not write <root>\JudgeAIlogs (UnauthorizedAccessException: ...), using
    // <root>\App_Data\JudgeAIlogs instead".
    // ============================================================================================
    internal static class JudgeFileLog
    {
        private static readonly object gate = new object();
        private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

        private static bool enabled = true;
        private static bool probed;

        /// <summary>Candidate folders in preference order. Index 0 is what was asked for.</summary>
        private static List<string> candidates;

        /// <summary>Index into <see cref="candidates"/> that is proven writable, or -1.</summary>
        private static int chosen = -1;

        private static string filePath;

        /// <summary>
        /// The full path of today's file, or the path it tried and could not write. Resolved on
        /// demand so the very first caller -- the "judge starting" line -- can name it, instead of
        /// printing "(not resolved)" because nothing had been appended yet.
        /// </summary>
        internal static string FilePath
        {
            get
            {
                if (!GeneralEnabled)
                {
                    // 1.0.206.0 -- no general file is written: name the log FOLDER, which is where
                    // the site folders and the alert .txt files are (meta.logFile).
                    try
                    {
                        string folder = chosen >= 0 && candidates != null && chosen < candidates.Count ? candidates[chosen] : Probe(0);
                        if (folder != null)
                        {
                            return folder;
                        }
                    }
                    catch
                    {
                    }
                }

                if (filePath == null)
                {
                    try
                    {
                        filePath = Today();
                    }
                    catch
                    {
                    }
                }

                return filePath;
            }
        }

        /// <summary>Why the last write failed, or null. Surfaced as meta.logError.</summary>
        internal static string LastError { get; private set; }

        /// <summary>
        /// 2.8 -- a folder named <paramref name="name"/> beside the log, in the same proven-writable
        /// place the log ended up. JudgeAudit uses it when its configured folder is denied, so the
        /// audit trail does not go silently missing on a server where App_Data cannot be written.
        /// Returns null only when no candidate folder worked at all.
        /// </summary>
        internal static string SiblingFolder(string name)
        {
            try
            {
                string folder = chosen >= 0 && candidates != null && chosen < candidates.Count
                    ? candidates[chosen]
                    : Probe(0);
                if (folder == null || string.IsNullOrWhiteSpace(name))
                {
                    return null;
                }

                string sub = Path.Combine(folder, name);
                string why;
                return Works(sub, out why) ? sub : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 2.7 -- set when the log is NOT in the folder that was asked for, naming the folder that
        /// was refused and why. Surfaced as meta.logNote. Null when the first choice worked.
        /// </summary>
        internal static string FallbackNote { get; private set; }

        /// <summary>
        /// 2.7 -- the candidate folders, best first. Built once from the appSettings and the
        /// application root; <see cref="Probe"/> picks the first that can actually be written to.
        /// </summary>
        private static List<string> Candidates()
        {
            if (candidates != null)
            {
                return candidates;
            }

            string root = AppDomain.CurrentDomain.BaseDirectory ?? "";
            string configured = null;
            try
            {
                configured = System.Configuration.ConfigurationManager.AppSettings["JudgeAiLogDir"];
                string off = System.Configuration.ConfigurationManager.AppSettings["JudgeAiLogEnabled"];
                if (!string.IsNullOrWhiteSpace(off) && off.Trim().Equals("false", StringComparison.OrdinalIgnoreCase))
                {
                    enabled = false;
                }
            }
            catch
            {
            }

            List<string> list = new List<string>();
            if (!string.IsNullOrWhiteSpace(configured))
            {
                string cfg = configured.Trim();
                Add(list, Path.IsPathRooted(cfg) ? cfg : Path.Combine(root, cfg));
            }

            // What you asked for: a JudgeAIlogs folder in the application root.
            Add(list, Path.Combine(root, "JudgeAIlogs"));

            // App_Data is writable wherever the judge audit works, which is everywhere it runs.
            Add(list, Path.Combine(root, "App_Data", "JudgeAIlogs"));

            // Last resort. Not pretty, but a log nobody can find beats no log at all, and
            // meta.logFile prints the full path on every verdict so it is never actually lost.
            try
            {
                Add(list, Path.Combine(Path.GetTempPath(), "JudgeAIlogs"));
            }
            catch
            {
            }

            candidates = list;
            return candidates;
        }

        private static void Add(List<string> list, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], path, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            list.Add(path);
        }

        /// <summary>
        /// 2.7 -- prove a folder by using it: create it, write a probe, delete the probe. Asking
        /// Windows about permissions under IIS gives answers that do not survive contact with the
        /// actual write, so this does the write.
        /// </summary>
        private static bool Works(string folder, out string why)
        {
            why = null;
            try
            {
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                string probeFile = Path.Combine(folder, ".judgeai-probe");
                File.WriteAllText(probeFile, "ok", Encoding.UTF8);
                try
                {
                    File.Delete(probeFile);
                }
                catch
                {
                    // Written but not deletable is still good enough to log into.
                }

                return true;
            }
            catch (Exception ex)
            {
                why = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 2.7 -- pick the first candidate that works, starting at <paramref name="from"/>. Returns
        /// the folder, or null when every candidate refused.
        /// </summary>
        private static string Probe(int from)
        {
            List<string> list = Candidates();
            string firstChoice = list.Count > 0 ? list[0] : null;
            string firstWhy = null;

            for (int i = Math.Max(0, from); i < list.Count; i++)
            {
                string why;
                if (Works(list[i], out why))
                {
                    chosen = i;
                    if (i == 0)
                    {
                        FallbackNote = null;
                    }
                    else
                    {
                        FallbackNote = "could not write " + firstChoice
                            + (firstWhy != null ? " (" + firstWhy + ")" : "")
                            + ", using " + list[i] + " instead";
                    }

                    return list[i];
                }

                if (i == 0 || firstWhy == null)
                {
                    firstWhy = why;
                }

                LastError = why;
            }

            chosen = -1;
            return null;
        }

        private static string Today()
        {
            string folder = chosen >= 0 && candidates != null && chosen < candidates.Count
                ? candidates[chosen]
                : Probe(0);

            if (folder == null)
            {
                // Nothing worked. Still name the folder that was asked for, so meta.logFile shows
                // what to grant permission on rather than going blank.
                List<string> list = Candidates();
                folder = list.Count > 0 ? list[0] : "JudgeAIlogs";
            }

            // 1.0.204.0 -- the single lines that belong to no site (judge starting, case file loaded,
            // warnings, the one-line ERROR) go to _general, so the log folder itself lists only
            // folders: one per site, plus _general. Judgement blocks go to the site's own folder
            // (JudgeBlockLog).
            return Path.Combine(folder, "_general", "judgeai-" + DateTimeOffset.UtcNow.ToOffset(Ist).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log");
        }

        /// <summary>
        /// 1.0.206.0 -- whether the lines that belong to no alert are written to
        /// _general\judgeai-yyyy-MM-dd.log (judge-settings.json Log.General). Off by default, so the
        /// log folder holds only site folders with one .txt per alert. The same lines still reach
        /// ai-log-yyyyMMdd.txt through JudgeLog. Set by JudgeBlockLog on every judgement.
        /// </summary>
        internal static volatile bool GeneralEnabled;

        /// <summary>One line. Never throws.</summary>
        internal static void Write(string level, string tag, string message)
        {
            if (!GeneralEnabled)
            {
                return;
            }

            Append(Line(level, tag, message));
        }

        /// <summary>
        /// A whole judgement in one block, so a verdict can be read top to bottom without grepping
        /// an interleaved file. This is what carries the per-request trace -- every step, gate,
        /// history_get and policy line the judge produced for that alert.
        /// </summary>
        internal static void WriteBlock(string header, IList<string> lines, string footer)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Line("INFO", "BEGIN", header));
            if (lines != null)
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    sb.Append(Line("INFO", "STEP", lines[i]));
                }
            }

            sb.Append(Line("INFO", "END", footer ?? ""));
            sb.Append(Environment.NewLine);
            Append(sb.ToString());
        }

        /// <summary>
        /// 1.0.203.0 -- one judgement as text: BEGIN, then each (tag, text) line, then END.
        /// The caller writes the same text to the daily file and to the alert's own file.
        /// </summary>
        internal static string FormatBlock(string header, IList<KeyValuePair<string, string>> lines, string footer)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Line("INFO", "BEGIN", header));
            if (lines != null)
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    sb.Append(Line(lines[i].Key == "ERROR" ? "ERROR" : "INFO", lines[i].Key, lines[i].Value));
                }
            }

            sb.Append(Line("INFO", "END", footer ?? ""));
            sb.Append(Environment.NewLine);
            return sb.ToString();
        }

        /// <summary>1.0.203.0 -- a ready block into today's file. Never throws.</summary>
        internal static void AppendBlock(string block)
        {
            Append(block);
        }

        /// <summary>
        /// 1.0.203.0 -- text into a file under the log folder that is actually in use (the same
        /// proven-writable folder as the daily file), e.g. by-site\167-GLTA\TRACK\TC TR VOLT LOW\24391.txt.
        /// headerIfNew is written first when the file does not exist yet (the CSV header).
        /// Never throws; a failure is left in LastError.
        /// </summary>
        internal static void AppendRelative(string relativePath, string text, string headerIfNew)
        {
            if (!enabled && probed)
            {
                return;
            }

            try
            {
                string folder = chosen >= 0 && candidates != null && chosen < candidates.Count
                    ? candidates[chosen]
                    : Probe(0);
                if (folder == null || !enabled || string.IsNullOrWhiteSpace(relativePath))
                {
                    return;
                }

                string path = Path.Combine(folder, relativePath);
                lock (gate)
                {
                    string d = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(d) && !Directory.Exists(d))
                    {
                        Directory.CreateDirectory(d);
                    }

                    if (headerIfNew != null && !File.Exists(path))
                    {
                        File.AppendAllText(path, headerIfNew, Encoding.UTF8);
                    }

                    File.AppendAllText(path, text, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                LastError = ex.GetType().Name + ": " + ex.Message;
            }
        }

        private static string Line(string level, string tag, string message)
        {
            return DateTimeOffset.UtcNow.ToOffset(Ist).ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                + " [" + (level ?? "INFO").PadRight(5) + "] [" + (tag ?? "").PadRight(5) + "] "
                + (message ?? "").Replace("\r", " ").Replace("\n", " ")
                + Environment.NewLine;
        }

        private static void Append(string text)
        {
            if (!enabled && probed)
            {
                return;
            }

            // Two attempts at most: the cached folder, then the next candidate that works. A folder
            // that stopped being writable should not cost more than one retry per call.
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    string path = Today();
                    filePath = path;
                    if (!enabled)
                    {
                        return;
                    }

                    lock (gate)
                    {
                        string d = Path.GetDirectoryName(path);
                        if (!string.IsNullOrEmpty(d) && !Directory.Exists(d))
                        {
                            Directory.CreateDirectory(d);
                        }

                        File.AppendAllText(path, text, Encoding.UTF8);
                    }

                    LastError = null;
                    probed = true;
                    return;
                }
                catch (Exception ex)
                {
                    // The reason travels back in meta.logError, and meta.logNote names the folder
                    // actually in use. 2.7: rather than give up here, step past the folder that just
                    // refused and try the next candidate -- App_Data, then the ASP.NET temp folder.
                    LastError = ex.GetType().Name + ": " + ex.Message;
                    probed = true;

                    if (attempt == 0 && Probe(chosen + 1) != null)
                    {
                        continue;
                    }

                    return;
                }
            }
        }
    }
}
