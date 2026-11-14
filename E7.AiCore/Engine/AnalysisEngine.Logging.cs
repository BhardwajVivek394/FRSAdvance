using Domain;
using E7MRIWeb.Areas.FRS25.Controllers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace E7.AiCore
{
    // AnalysisEngine -- Logging.
    // Moved verbatim from AiChatController.Logging.cs in 1.0.165.0 (Shared AI Core 2e);
    // only access modifiers changed (private -> internal). The web app sees internals via InternalsVisibleTo.
    public sealed partial class AnalysisEngine
    {

        internal void AddDiag(string code)
        {
            lock (_jsonDiagLock)
            {
                if (Sess._jsonDiag != null && Sess._jsonDiag.Count < 8) { Sess._jsonDiag.Add(code); }
            }
        }


        internal string SnapshotDiag()
        {
            lock (_jsonDiagLock)
            {
                if (Sess._jsonDiag == null || Sess._jsonDiag.Count == 0) { return ""; }
                int n = Math.Min(3, Sess._jsonDiag.Count);
                string[] copy = new string[n];
                for (int i = 0; i < n; i++) { copy[i] = Sess._jsonDiag[i]; }
                return string.Join("; ", copy);
            }
        }


        // Answers, in one call: where are the logs, can the app pool write
        // there, has it written anything, and if not -- why.
        internal static object LogDiagnostics()
        {
            string probeError = null;
            bool canWrite = false;
            try
            {
                System.IO.Directory.CreateDirectory(_toolLogDir);
                string probe = System.IO.Path.Combine(_toolLogDir, "_write_probe.tmp");
                System.IO.File.WriteAllText(probe, "probe");
                System.IO.File.Delete(probe);
                canWrite = true;
            }
            catch (Exception ex)
            {
                probeError = ex.GetType().Name + ": " + ex.Message;
            }

            string[] files = new string[0];
            try
            {
                if (System.IO.Directory.Exists(_toolLogDir))
                {
                    files = System.IO.Directory.GetFiles(_toolLogDir, "ai-*");
                }
            }
            catch { }

            List<string> names = new List<string>();
            for (int i = 0; i < files.Length && i < 12; i++)
            {
                try
                {
                    System.IO.FileInfo fi = new System.IO.FileInfo(files[i]);
                    names.Add(fi.Name + " (" + fi.Length + "B, " + fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm") + ")");
                }
                catch { }
            }

            return new
            {
                logDir = _toolLogDir,
                dirExists = System.IO.Directory.Exists(_toolLogDir),
                canWrite = canWrite,
                writeError = probeError,
                minLevel = _aiLogMinLevel,
                traceEnabled = _analyzeTraceEnabled,
                toolLoggingEnabled = _toolLoggingEnabled,
                writtenLines = _aiLogWritten,
                droppedLines = _aiLogDropped,
                lastError = _aiLogLastError,
                fileCount = files.Length,
                files = names
            };
        }


        internal static int LevelRank(string lvl)
        {
            if (lvl == "DEBUG") { return 0; }
            if (lvl == "INFO") { return 1; }
            if (lvl == "WARN") { return 2; }
            if (lvl == "ERROR") { return 3; }
            return 1;
        }


        // v1.0.160.73: static twin of AiLog. The audit-DB helpers are static (breaker state is
        // process-wide) and AiLog is an instance method using _traceId -- calling it from static
        // context is CS0120, which has already cost this arc three compile breaks. Same file, same
        // lock, no traceId.
        internal static void AiLogStatic(string level, string tag, string message)
        {
            try
            {
                if (LevelRank(level) < LevelRank(_aiLogMinLevel)) { return; }
                DateTime now = DateTime.Now;
                string line = now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                    + " [" + level.PadRight(5) + "]"
                    + " [" + (tag ?? "").PadRight(7) + "]"
                    + " [-] "
                    + (message ?? "").Replace("\r", " ").Replace("\n", " ");
                System.IO.Directory.CreateDirectory(_toolLogDir);
                string path = System.IO.Path.Combine(
                    _toolLogDir, "ai-log-" + now.ToString("yyyyMMdd") + ".txt");
                lock (_toolLogLock)
                {
                    System.IO.File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }


        internal void AiLog(string level, string tag, string message)
        {
            try
            {
                if (LevelRank(level) < LevelRank(_aiLogMinLevel)) { return; }
                DateTime now = DateTime.Now;
                string line = now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                    + " [" + level.PadRight(5) + "]"
                    + " [" + (tag ?? "").PadRight(7) + "]"
                    + " [" + (Sess._traceId ?? "-") + "] "
                    + (message ?? "").Replace("\r", " ").Replace("\n", " ");

                RollIfNewDay(now);
                System.IO.Directory.CreateDirectory(_toolLogDir);
                string path = System.IO.Path.Combine(
                    _toolLogDir, "ai-log-" + now.ToString("yyyyMMdd") + ".txt");
                lock (_toolLogLock)
                {
                    System.IO.File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
                }
                System.Threading.Interlocked.Increment(ref _aiLogWritten);
            }
            catch (Exception ex)
            {
                // Never let logging break an analysis -- but do not go silent
                // either. A swallowed failure here is why "no logs anywhere"
                // used to be undiagnosable; Status now reports this.
                System.Threading.Interlocked.Increment(ref _aiLogDropped);
                _aiLogLastError = ex.GetType().Name + ": " + ex.Message;
            }
        }


        internal void AiLogEx(string tag, string message, Exception ex)
        {
            string detail = ex == null ? "" : " | " + ex.GetType().Name + ": " + ex.Message;
            AiLog("ERROR", tag, message + detail);
            if (ex != null)
            {
                System.Diagnostics.Trace.TraceError("[AiChat/" + tag + "] " + ex.ToString());
            }
        }


        // gzip yesterday's logs on the first write of a new day, off-thread
        internal static void RollIfNewDay(DateTime now)
        {
            DateTime today = now.Date;
            lock (_aiLogDayLock)
            {
                if (_aiLogLastDay == today) { return; }
                _aiLogLastDay = today;
            }
            Task.Run(() => CompressOldLogs(today));
        }


        internal static void CompressOldLogs(DateTime today)
        {
            try
            {
                if (!System.IO.Directory.Exists(_toolLogDir)) { return; }
                string[] pats = { "ai-log-*.txt", "ai-analyze-*.jsonl", "ai-tool-*.jsonl" };
                string stamp = today.ToString("yyyyMMdd");
                for (int p2 = 0; p2 < pats.Length; p2++)
                {
                    string[] files = System.IO.Directory.GetFiles(_toolLogDir, pats[p2]);
                    for (int i = 0; i < files.Length; i++)
                    {
                        string f = files[i];
                        if (f.IndexOf(stamp, StringComparison.Ordinal) >= 0) { continue; } // today's stays open
                        string gz = f + ".gz";
                        if (System.IO.File.Exists(gz)) { continue; }
                        try
                        {
                            using (System.IO.FileStream src = System.IO.File.OpenRead(f))
                            using (System.IO.FileStream dst = System.IO.File.Create(gz))
                            using (System.IO.Compression.GZipStream zip =
                                new System.IO.Compression.GZipStream(dst, System.IO.Compression.CompressionMode.Compress))
                            {
                                src.CopyTo(zip);
                            }
                            System.IO.File.Delete(f);
                        }
                        catch { /* file in use or unreadable: leave it, retry tomorrow */ }
                    }
                }
                // retention now covers the archives too
                DateTime cut = DateTime.UtcNow.AddDays(-_traceRetainDays);
                string[] old = System.IO.Directory.GetFiles(_toolLogDir, "ai-*.gz");
                for (int i = 0; i < old.Length; i++)
                {
                    if (System.IO.File.GetLastWriteTimeUtc(old[i]) < cut)
                    {
                        try { System.IO.File.Delete(old[i]); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("[AiChat] log compression failed: " + ex.Message);
            }
        }


        internal void TraceAnalyze(string step, JObject data)
        {
            // NOTE: _traceActive gates ACCUMULATION at call sites and stays on for
            // analyze endpoints regardless of the file switch, so envelope timing
            // meta is populated even when file tracing is disabled.
            if (!_analyzeTraceEnabled || !Sess._traceActive) { return; }
            try
            {
                JObject e = new JObject();
                e["tsUtc"] = DateTime.UtcNow.ToString("o");
                e["traceId"] = Sess._traceId ?? "";
                e["step"] = step;
                if (data != null) { e["data"] = data; }
                System.IO.Directory.CreateDirectory(_toolLogDir);
                string path = System.IO.Path.Combine(
                    _toolLogDir,
                    "ai-analyze-" + DateTime.UtcNow.ToString("yyyyMMdd") + ".jsonl");
                lock (_toolLogLock)
                {
                    System.IO.FileInfo fi = new System.IO.FileInfo(path);
                    if (!fi.Exists) { CleanupOldTraces(); } // retention on day roll, best-effort
                    if (fi.Exists && fi.Length > (long)_traceMaxMB * 1024 * 1024) { return; } // daily file size bound
                    System.IO.File.AppendAllText(path, e.ToString(Formatting.None) + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (Exception tex)
            {
                System.Diagnostics.Trace.TraceWarning("[AiChat] analyze trace failed: " + tex.Message);
            }
        }

        internal static void CleanupOldTraces()
        {
            try
            {
                string[] files = System.IO.Directory.GetFiles(_toolLogDir, "ai-analyze-*.jsonl");
                DateTime cut = DateTime.UtcNow.AddDays(-_traceRetainDays);
                for (int k = 0; k < files.Length; k++)
                {
                    if (System.IO.File.GetLastWriteTimeUtc(files[k]) < cut)
                    {
                        try { System.IO.File.Delete(files[k]); } catch { }
                    }
                }
            }
            catch { }
        }

        internal static string RedactForTrace(string s0)
        {
            if (string.IsNullOrEmpty(s0)) { return s0; }
            try
            {
                string o = _redactDq.Replace(s0, "$1\"[REDACTED]\"");
                o = _redactQuoted.Replace(o, "$1\"[REDACTED]\"");
                o = _redactFlag.Replace(o, "$1[REDACTED]");
                o = _redactNumeric.Replace(o, "$1[REDACTED]");
                o = _redactScheme.Replace(o, "$1 [REDACTED]");
                o = _redactBare.Replace(o, "$1[REDACTED]");
                return o;
            }
            catch { return s0; }
        }


        // Anthropic returns usage on every reply; the OpenAI adapter passes it
        // through. Real counts beat the chars/4 estimate, and cache_read shows
        // whether prompt caching is actually working.
        internal static JObject ReadUsage(string raw)
        {
            if (string.IsNullOrEmpty(raw)) { return null; }
            try
            {
                JObject o = JObject.Parse(raw);
                return o["usage"] as JObject;
            }
            catch { return null; }
        }


        internal static string TraceTrunc(string s0, int max)
        {
            if (s0 == null) { return ""; }
            return s0.Length <= max ? s0 : s0.Substring(0, max) + "...[+" + (s0.Length - max) + " chars]";
        }


        internal static string SanitizeToolFailure(Exception ex)
        {
            if (ex == null) { return "internal error"; }
            if (ex is OperationCanceledException) { return "timeout"; }
            if (ex is System.Net.Http.HttpRequestException) { return "connection error"; }
            if (ex is System.Net.WebException) { return "connection error"; }
            System.Diagnostics.Trace.TraceError("[AiChat] tool failure: " + ex.ToString());
            return "internal error";
        }


        internal static string SanitizeJsonError(string msg)
        {
            if (string.IsNullOrEmpty(msg)) { return "internal error"; }
            for (int i = 0; i < _jsonErrExact.Length; i++)
            {
                if (string.Equals(msg, _jsonErrExact[i], StringComparison.Ordinal)) { return msg; }
            }
            if (msg.StartsWith("verdict schema invalid: ", StringComparison.Ordinal)) { return msg; }
            if (msg.StartsWith("ctx.", StringComparison.Ordinal)) { return msg; } // our shape reasons
            System.Diagnostics.Trace.TraceError("[AnalyzeAlertJson] " + msg);
            return "internal error";
        }


        internal static string TracePreview(string result)
        {
            if (string.IsNullOrEmpty(result)) { return "(empty)"; }
            string t = RedactSecretText(result).Replace("\r", " ").Replace("\n", " ").Trim();
            if (t.Length <= 200) { return t; }
            return t.Substring(0, 140) + " ...TAIL: " + t.Substring(t.Length - 60);
        }


        // v1.0.128.0: model-call retry. CallModelAsync now wraps CallModelOnceAsync (the raw
        // single call, below) and retries a TRANSIENT failure before it becomes a model_error.
        // Never retries ClientBusyException (that is 503 backpressure) or cancellation, and skips
        // the retry once elapsed exceeds the budget, so a slow-failing call cannot double the
        // latency or hold _aiLock too long. Call sites are unchanged. Config (all optional):
        // AnalyzeModelRetries (default 1), AnalyzeModelRetryBackoffMs (1500), AnalyzeModelRetryMaxElapsedMs (15000).
        // v1.0.160.181: name the REAL cause of a model-call failure. ex.Message on an
        // HttpRequestException is the generic "An error occurred while sending the request.";
        // the actual reason -- DNS failure, outbound 443 blocked, TLS handshake, dead host --
        // lives in the INNER exception chain, which the old log line threw away (it logged only
        // SanitizeJsonError(ex.Message), flattened to "internal error"). This walks the whole
        // chain to the SERVER log (never the client -- H4 unchanged), surfacing the decisive
        // fields: SocketException.SocketErrorCode (HostNotFound=DNS, ConnectionRefused/TimedOut=
        // firewall/dead host) and WebException.Status (TrustFailure/SecureChannelFailure=TLS/CA).
        internal static string DescribeException(Exception ex)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int depth = 0;
            for (Exception e = ex; e != null && depth < 8; e = e.InnerException, depth++)
            {
                if (depth > 0) { sb.Append(" <- "); }
                sb.Append(e.GetType().Name).Append(": ").Append(e.Message);
                System.Net.Sockets.SocketException se = e as System.Net.Sockets.SocketException;
                if (se != null) { sb.Append(" [SocketError=").Append(se.SocketErrorCode).Append("]"); }
                System.Net.WebException we = e as System.Net.WebException;
                if (we != null) { sb.Append(" [WebStatus=").Append(we.Status).Append("]"); }
            }
            return sb.ToString();
        }


        internal static string DescribeFault(Exception ex)
        {
            if (ex == null) { return null; }
            Exception inner = ex;
            int guard = 0;
            while (inner.InnerException != null && guard++ < 5) { inner = inner.InnerException; }
            string msg = RedactSecretText(inner.Message ?? "");
            if (msg.Length > 200) { msg = msg.Substring(0, 200) + "..."; }
            string outerType = ex.GetType().Name;
            string innerType = inner.GetType().Name;
            return outerType == innerType
                ? outerType + ": " + msg
                : outerType + " -> " + innerType + ": " + msg;
        }


        internal static string FriendlyError(Exception ex)
        {
            if (ex is AnthropicRateLimitException) return "Rate limit. Wait ~60s.";
            if (ex is AnthropicAuthException) return "Auth failed. Check web.config.";
            string msg = ex.Message ?? "";
            if (msg.Contains("529")) return "API overloaded. Try again.";
            // v1.0.160.118: a provider exception quotes the model string ("model deepseek-v4-flash
            // ..."), and this message is written straight to the client via WriteSse("error", ...).
            // Run it through the same scrub the verdict text uses instead of returning it raw.
            return ScrubEngineIdentity(msg);
        }
    }
}
