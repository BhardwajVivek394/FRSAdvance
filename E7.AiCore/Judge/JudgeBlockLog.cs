using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace E7.AiCore.Judge
{
    // ============================================================================================
    // JudgeBlockLog (1.0.203.0) -- the complete record of one judgement, as text.
    //
    // One block per judgement, the same text in up to two places plus one CSV row:
    //
    //   <log folder>\<siteId>-<station>\judgeai-yyyy-MM-dd.log                 the site's day
    //   <log folder>\<siteId>-<station>\<ASSET TYPE>\<CAUSE>\<alertId>.txt     Log.BySite
    //   <log folder>\<siteId>-<station>\judge-summary-yyyy-MM.csv              Log.Summary
    //   <log folder>\_general\judgeai-yyyy-MM-dd.log     lines that belong to no site (JudgeFileLog.Write)
    // (1.0.204.0: the log folder itself holds only folders -- one per site, plus _general.)
    //
    //   [BEGIN] alert, cause, asset, site, type, build, request id
    //   [REQ  ] what was asked: mode, useAi, trace, family tracks
    //   [CARD ] the alert card JSON as sent, on one line                     Log.Card
    //   [STEP ] every line the pipeline wrote (card, catalogue, match, prior discard, learned,
    //           each historian call, gates, live re-check, reset check, AI, policy)
    //   [CASE ] each past case the similar-case check matched
    //   [AI   ] model, recommendation, logic / data, confidence, headline
    //   [AIRAW] the AI's whole reply as it came back
    //   [PATH ] the result of every step, in order
    //   [ERROR] the exception, when the judgement failed
    //   [RESP ] the response JSON exactly as the caller received it (the full verdict)
    //   [END  ] the verdict, the step that decided, confidence, time, reason
    //
    // The alert's own file is appended to, so an alert judged three times (AlertV3 retries) has
    // three blocks in one file, oldest first.
    //
    // Nothing here can fail a judgement: every call is wrapped, and JudgeFileLog never throws.
    // ============================================================================================
    internal static class JudgeBlockLog
    {
        private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

        private const string CsvHeader =
            "time,siteId,station,assetType,assetId,assetName,cause,alertType,alertId,requestId,status,finalDecision,decidedBy,"
            + "decisiveStep,confidence,elapsedMs,learnedWouldDecide,learnedBy,casesDiscarded,casesApproved,aiRecommendation,error\r\n";

        /// <summary>outcome may be partial (error != null) or null (the request never reached the pipeline).</summary>
        internal static void Write(JudgeRequest req, JudgeOutcome outcome, string rid, long elapsedMs, JudgeSettings cfg, Exception error, string responseJson)
        {
            try
            {
                // no settings object = the defaults: the alert's own .txt with the card, nothing else
                JudgeFileLog.GeneralEnabled = cfg != null && cfg.LogGeneral;
                WriteCore(req, outcome, rid, elapsedMs, cfg == null || cfg.LogCard, cfg == null || cfg.LogBySite, cfg != null && cfg.LogSummary, cfg != null && cfg.LogDaily, error, responseJson);
            }
            catch (Exception ex)
            {
                JudgeLog.Write("WARN", "JUDGE", "log block not written for alert " + (req != null ? req.AlertId.ToString(CultureInfo.InvariantCulture) : "?") + ": " + ex.Message);
            }
        }

        private static void WriteCore(JudgeRequest req, JudgeOutcome outcome, string rid, long elapsedMs, bool logCard, bool logBySite, bool logSummary, bool logDaily, Exception error, string responseJson)
        {
            JudgeCard card = outcome != null ? outcome.Card : null;
            JudgePolicyOutcome pol = outcome != null ? outcome.Policy : null;
            bool failed = error != null || pol == null;
            string station = Station(card);

            string header = "alert=" + req.AlertId
                + " cause=" + (card != null ? card.CauseCode : "?")
                + " asset=" + (card != null ? card.AssetName + "/" + card.AssetId : "?")
                + " site=" + (card != null ? card.SiteId.ToString(CultureInfo.InvariantCulture) + (station.Length > 0 ? " " + station : "") : "?")
                + " type=" + (card != null ? card.AssetType + "/" + card.AlertType : "?")
                + " build=" + JudgeEnvelope.Build
                + " rid=" + rid;

            List<KeyValuePair<string, string>> lines = new List<KeyValuePair<string, string>>();
            JudgeOptions opt = req.Options ?? new JudgeOptions();
            Add(lines, "REQ", "rid=" + rid + " mode=" + opt.Mode + " useAi=" + opt.UseAi + " trace=" + opt.Trace
                + (opt.Engine != null ? " engine=" + opt.Engine : "")
                + (opt.FamilyTrackIds != null && opt.FamilyTrackIds.Count > 0 ? " familyTrackIds=" + string.Join(",", opt.FamilyTrackIds) : ""));

            JToken cardJson = req.CardRaw ?? req.Card;
            if (logCard && cardJson != null)
            {
                Add(lines, "CARD", cardJson.ToString(Formatting.None));
            }

            if (outcome != null && outcome.Trace != null)
            {
                foreach (string s in outcome.Trace.Log.ToArray())
                {
                    Add(lines, "STEP", s);
                }
            }

            JudgeLearnedResult learned = outcome != null ? outcome.Learned : null;
            int casesDiscarded = 0;
            int casesApproved = 0;
            if (learned != null && learned.Match != null)
            {
                casesDiscarded = learned.Match.SuppressedCount;
                casesApproved = learned.Match.PassCount;
                foreach (JudgeCaseNeighbor n in learned.Match.Nearest)
                {
                    Add(lines, "CASE", "pendingId=" + n.Case.Id + " asset=" + n.Case.Asset
                        + " " + (n.Case.Pass ? "APPROVED" : "DISCARDED" + (string.IsNullOrEmpty(n.Case.Reason) ? "" : " " + n.Case.Reason))
                        + " set " + n.Case.Set.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                        + " decided " + n.Case.Decided.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                        + " distance " + n.Distance.ToString("0.####", CultureInfo.InvariantCulture));
                }
            }

            JudgeAiVerdict ai = outcome != null ? outcome.Ai : null;
            if (ai != null)
            {
                Add(lines, "AI", ai.Ran
                    ? "model=" + (ai.Input != null ? ai.Input.Model + " (" + ai.Input.Provider + ")" : "?")
                        + " recommendation=" + ai.Recommendation
                        + " logic_correct=" + JudgeUtil.TriText(ai.LogicCorrect)
                        + " data_correct=" + JudgeUtil.TriText(ai.DataCorrect)
                        + " confidence=" + ai.Confidence
                        + " headline=" + ai.Headline
                    : (ai.Error != null && ai.Error.StartsWith("not run", StringComparison.OrdinalIgnoreCase) ? ai.Error : "not run: " + (ai.Error ?? "no reason recorded")));

                // the AI's whole reply, exactly as it came back: evidence, likely cause, action, caveats
                if (ai.RawJson != null)
                {
                    Add(lines, "AIRAW", ai.RawJson.ToString(Formatting.None));
                }
            }

            string decisive = "";
            if (!failed)
            {
                decisive = JudgeEnvelope.DecisiveStep(pol.DecidedBy);
                JArray path = JudgeEnvelope.DecisionPathJson(outcome);
                Add(lines, "PATH", string.Join(" ", path.OfType<JObject>().Select(p => (string)p["step"] + "=" + (string)p["result"])));
            }

            string errorText = null;
            if (error != null)
            {
                Exception inner = error;
                while (inner.InnerException != null && inner is AggregateException)
                {
                    inner = inner.InnerException;
                }

                errorText = inner.GetType().Name + ": " + inner.Message;
                Add(lines, "ERROR", errorText);
            }

            // the response exactly as the caller received it (the full verdict JSON), on one line
            if (!string.IsNullOrEmpty(responseJson))
            {
                string oneLine = responseJson;
                try
                {
                    oneLine = JToken.Parse(responseJson).ToString(Formatting.None);
                }
                catch (JsonException)
                {
                    // not JSON: keep it as it is; the line writer flattens line breaks
                }

                Add(lines, "RESP", oneLine);
            }

            string footer = failed
                ? "ERROR model_error in " + elapsedMs + "ms"
                : pol.FinalDecision + " by " + pol.DecidedBy + " (decisive step " + decisive + ")"
                    + " conf=" + pol.Confidence
                    + " in " + elapsedMs + "ms"
                    + " | " + string.Join(" ", pol.Reasons);

            string block = JudgeFileLog.FormatBlock(header, lines, footer);

            // 1.0.204.0 -- everything of a site lives in that site's folder, directly in the log
            // folder (no "by-site" level, no judgement files in the log folder itself):
            //   <siteId>-<station>\judgeai-yyyy-MM-dd.log          the site's judgements of the day
            //   <siteId>-<station>\judge-summary-yyyy-MM.csv       one row per judgement
            //   <siteId>-<station>\<ASSET TYPE>\<CAUSE>\<alertId>.txt
            DateTimeOffset now = DateTimeOffset.UtcNow.ToOffset(Ist);
            string site = card != null && card.SiteId > 0
                ? Safe(card.SiteId.ToString(CultureInfo.InvariantCulture) + (station.Length > 0 ? "-" + station : ""))
                : "UNKNOWN";
            // 1.0.206.0 -- off by default: the alert's own .txt below already holds the whole block
            if (logDaily)
            {
                JudgeFileLog.AppendRelative(Path.Combine(site, "judgeai-" + now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log"), block, null);
            }

            if (logBySite)
            {
                string file = req.AlertId > 0 ? req.AlertId.ToString(CultureInfo.InvariantCulture) : "noid-" + Safe(rid);
                string rel = Path.Combine(site,
                    card != null && !string.IsNullOrWhiteSpace(card.AssetType) ? Safe(card.AssetType.ToUpperInvariant()) : "UNKNOWN",
                    card != null && !string.IsNullOrWhiteSpace(card.CauseCode) ? Safe(card.CauseCode.Trim().ToUpperInvariant()) : "UNKNOWN",
                    file + ".txt");
                JudgeFileLog.AppendRelative(rel, block, null);
            }

            if (logSummary)
            {
                string row = string.Join(",", new[]
                {
                    Csv(now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                    Csv(card != null ? card.SiteId.ToString(CultureInfo.InvariantCulture) : ""),
                    Csv(station),
                    Csv(card != null ? card.AssetType : ""),
                    Csv(card != null ? card.AssetId.ToString(CultureInfo.InvariantCulture) : ""),
                    Csv(card != null ? card.AssetName : ""),
                    Csv(card != null ? card.CauseCode : ""),
                    Csv(card != null ? card.AlertType : ""),
                    Csv(req.AlertId.ToString(CultureInfo.InvariantCulture)),
                    Csv(rid),
                    Csv(failed ? "model_error" : "ok"),
                    Csv(failed ? "" : pol.FinalDecision),
                    Csv(failed ? "" : pol.DecidedBy),
                    Csv(decisive),
                    Csv(failed ? "" : pol.Confidence.ToString(CultureInfo.InvariantCulture)),
                    Csv(elapsedMs.ToString(CultureInfo.InvariantCulture)),
                    Csv(learned != null ? learned.WouldDecide : ""),
                    Csv(learned != null ? learned.WouldDecideBy : ""),
                    Csv(casesDiscarded.ToString(CultureInfo.InvariantCulture)),
                    Csv(casesApproved.ToString(CultureInfo.InvariantCulture)),
                    Csv(ai != null && ai.Ran ? ai.Recommendation : ""),
                    Csv(errorText)
                }) + "\r\n";
                JudgeFileLog.AppendRelative(Path.Combine(site, "judge-summary-" + now.ToString("yyyy-MM", CultureInfo.InvariantCulture) + ".csv"), row, CsvHeader);
            }
        }

        private static void Add(List<KeyValuePair<string, string>> lines, string tag, string text)
        {
            lines.Add(new KeyValuePair<string, string>(tag, text ?? ""));
        }

        /// <summary>The station code on the card (GLTA), else the station name, else empty.</summary>
        private static string Station(JudgeCard card)
        {
            if (card == null)
            {
                return "";
            }

            JObject raw = card.Raw as JObject;
            if (raw != null)
            {
                JToken code = raw.GetValue("stationCode", StringComparison.OrdinalIgnoreCase);
                if (code != null && code.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string)code))
                {
                    return ((string)code).Trim();
                }
            }

            return string.IsNullOrWhiteSpace(card.StationName) ? "" : card.StationName.Trim();
        }

        /// <summary>A folder or file name: characters Windows refuses become "-", length capped, never empty or dots only.</summary>
        private static string Safe(string name)
        {
            StringBuilder sb = new StringBuilder();
            char[] bad = Path.GetInvalidFileNameChars();
            foreach (char c in (name ?? "").Trim())
            {
                sb.Append(Array.IndexOf(bad, c) >= 0 || c < 32 ? '-' : c);
            }

            string s = sb.ToString().Trim().TrimEnd('.', ' ');
            if (s.Length > 80)
            {
                s = s.Substring(0, 80).TrimEnd('.', ' ');
            }

            return s.Length == 0 ? "UNKNOWN" : s;
        }

        private static string Csv(string v)
        {
            if (string.IsNullOrEmpty(v))
            {
                return "";
            }

            string s = v.Replace("\r", " ").Replace("\n", " ");
            return s.IndexOfAny(new[] { ',', '"' }) < 0 ? s : "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
