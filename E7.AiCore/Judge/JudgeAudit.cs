using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Text;

namespace E7.AiCore.Judge
{
    // ============================================================================================
    // JudgeAudit -- one JSON line per judgement in App_Data\AlertJudge\Audit\judge-YYYY-MM-DD.jsonl
    // (the 7W shape of AlertJudge v1.1.2.0 Audit/AuditSink.cs: when, what, where, who, which model,
    // outcome, why, plus the trace). File sink on purpose: no DB dependency.
    //
    // CHANGE HERE to change what is logged per judgement. Audit.Dir / Audit.Enabled in judge-settings.json.
    // ============================================================================================
    internal static class JudgeAudit
    {
        private static readonly object gate = new object();

        internal static string Write(JudgeOutcome outcome, long durationMs, JudgeSettings cfg)
        {
            string auditId = Guid.NewGuid().ToString("N");
            if (!cfg.AuditEnabled)
            {
                return auditId;
            }

            try
            {
                DateTimeOffset when = DateTimeOffset.UtcNow;
                JObject row = new JObject();
                row["auditId"] = auditId;
                row["when"] = JudgeUtil.Iso(when);
                row["alertId"] = outcome.AlertId;
                row["causeCode"] = outcome.Verifier != null ? outcome.Verifier.CauseCode : null;
                row["siteId"] = outcome.Card != null ? outcome.Card.SiteId : 0;
                row["assetId"] = outcome.Card != null ? outcome.Card.AssetId : 0;
                row["assetName"] = outcome.Card != null ? outcome.Card.AssetName : null;
                row["who"] = "AlertJudge";
                row["engine"] = outcome.Engine;
                row["decision"] = outcome.Policy.FinalDecision + "/" + outcome.Policy.Decision;
                row["decidedBy"] = outcome.Policy.DecidedBy;
                row["confidence"] = outcome.Policy.Confidence;
                row["why"] = string.Join("; ", outcome.Policy.Reasons);
                row["ruleStatus"] = outcome.Verifier != null ? outcome.Verifier.Verdict + (outcome.Verifier.ReclassifiedCause != null ? "→" + outcome.Verifier.ReclassifiedCause : "") : null;
                row["rulesFired"] = new JArray(outcome.RulesFired);
                row["live"] = outcome.Live != null ? outcome.Live.Result + ": " + outcome.Live.Detail : null;
                row["degraded"] = outcome.Degraded;
                row["durationMs"] = durationMs;
                row["trace"] = JudgeEnvelope.TraceJson(outcome.Trace);

                // 2.8 -- the audit goes wherever it CAN go, the same way the log does.
                //
                // On the test server this failed on every single judgement:
                //   audit write failed: Access to the path
                //     'C:\inetpub\wwwroot\E7FRS25WebTest\App_Data\AlertJudge\Audit' is denied.
                // The app pool identity has no write permission under wwwroot, which is normal and
                // correct for an IIS site. The judgement still completed, but the audit trail -- the
                // record of what the judge decided and why -- was silently empty, and that is
                // exactly the file wanted when a verdict is questioned weeks later. JudgeFileLog
                // already solves this by probing for a folder it can write to, so reuse that rather
                // than inventing a second answer to the same problem.
                string dir = cfg.ResolvePath(cfg.AuditDir);
                string written = TryWrite(dir, when, row);
                if (written == null)
                {
                    string fallback = JudgeFileLog.SiblingFolder("Audit");
                    if (fallback != null && !string.Equals(fallback, dir, StringComparison.OrdinalIgnoreCase))
                    {
                        written = TryWrite(fallback, when, row);
                        if (written != null)
                        {
                            JudgeLog.Write("WARN", "JUDGE", "audit: could not write " + dir
                                + ", using " + fallback + " instead");
                        }
                    }
                }

                if (written == null)
                {
                    JudgeLog.Write("WARN", "JUDGE", "audit write failed in " + dir
                        + " and in the fallback folder. The judgement itself is unaffected.");
                }
            }
            catch (Exception ex)
            {
                JudgeLog.Write("WARN", "JUDGE", "audit write failed: " + ex.Message);
            }

            return auditId;
        }

        /// <summary>
        /// 2.8 -- append one audit line to a folder. Returns the file path, or null when the folder
        /// cannot be created or written. Never throws: an audit is a record of a decision, not part
        /// of making it, so it must never be able to fail a judgement.
        /// </summary>
        private static string TryWrite(string dir, DateTimeOffset when, JObject row)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dir))
                {
                    return null;
                }

                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "judge-" + when.ToString("yyyy-MM-dd") + ".jsonl");
                string line = row.ToString(Formatting.None) + Environment.NewLine;
                lock (gate)
                {
                    File.AppendAllText(file, line, Encoding.UTF8);
                }

                return file;
            }
            catch
            {
                return null;
            }
        }
    }
}
