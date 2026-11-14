using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace E7.AiCore.Judge
{
    // ============================================================================================
    // JudgeEnvelope -- builds the response JSON, in the AnalyzeAlertJson envelope:
    //   { "status": "...", "verdict": {...} | null, "meta": {...}, "context_patch": {...} }
    //
    // CHANGE HERE to change the response shape, a status value, or which meta keys are public.
    //
    //   status       HTTP   verdict   when
    //   ok           200    present   judged (card-invalid / no-rule come back as a SUPPRESSED verdict)
    //   bad_request  400    null      body not JSON / too big / no card and no alertId
    //   unauthorized 401    null      wrong or missing X-Internal-Api-Key
    //   disabled     403    null      InternalApiKey not set in web.config
    //   busy         503    null      more than Judge.MaxConcurrent judgements running
    //   model_error  200    null      internal failure (never an AI failure: the policy handles that)
    // ============================================================================================
    internal static class JudgeEnvelope
    {
        /// <summary>
        /// 2.5 -- the judge's own design version. This used to be the ONLY version in the response,
        /// hardcoded, so every verdict said "2.0.0.0" no matter which build produced it and there was
        /// no way to tell 1.0.176.0's answer from 1.0.179.0's. meta.build now carries the real
        /// assembly version alongside it.
        /// </summary>
        internal const string ComponentVersion = "2.0.0.0";

        private static string buildVersion;

        /// <summary>2.5 -- the E7.AiCore assembly version actually serving the request.</summary>
        internal static string Build
        {
            get
            {
                if (buildVersion == null)
                {
                    try
                    {
                        buildVersion = typeof(JudgeEnvelope).Assembly.GetName().Version.ToString();
                    }
                    catch
                    {
                        buildVersion = "unknown";
                    }
                }

                return buildVersion;
            }
        }

        internal static int HttpStatusFor(string status)
        {
            switch (status)
            {
                case "bad_request":
                    return 400;
                case "unauthorized":
                    return 401;
                case "disabled":
                    return 403;
                case "busy":
                    return 503;
                default:
                    return 200;
            }
        }

        internal static string Error(string status, string requestId, long elapsedMs, string error)
        {
            JObject env = new JObject();
            env["status"] = status;
            env["verdict"] = JValue.CreateNull();
            JObject meta = new JObject();
            meta["componentVersion"] = ComponentVersion;
            meta["build"] = Build;
            meta["requestId"] = requestId ?? "";
            meta["elapsed"] = elapsedMs;
            if (!string.IsNullOrEmpty(error))
            {
                meta["error"] = error;
            }

            env["meta"] = meta;
            return env.ToString(Formatting.None);
        }

        internal static string Ok(JudgeOutcome o, JudgeRequest req, long elapsedMs)
        {
            JObject env = new JObject();
            env["status"] = "ok";
            env["verdict"] = VerdictJson(o, req.Options.Trace);

            JObject meta = new JObject();
            meta["componentVersion"] = ComponentVersion;
            meta["build"] = Build;                                   // 2.5: which build answered
            meta["requestId"] = req.RequestId ?? "";
            meta["mode"] = o.Mode;
            meta["provider"] = o.Engine ?? "default";

            // 2.5 -- WHICH MODEL, and what it said if it failed. Until now the only sign the AI had
            // not run was a sentence buried in the reason text; there was no model name anywhere.
            meta["aiRequested"] = req.Options.UseAi;

            // 1.0.200.0 -- meta.provider above is what the CALLER asked for, not what ran. When the
            // AiModelConfig default overrules a service caller (the 16.10.2026 decision), the two
            // differ by design, and an envelope showing only the asked-for brand next to the real
            // model name reads as a contradiction. resolvedProvider names the vendor actually used.
            if (o.Ai != null)
            {
                meta["aiRan"] = o.Ai.Ran;
                string aiModel = o.Ai.Input != null ? o.Ai.Input.Model : null;
                meta["aiModel"] = string.IsNullOrWhiteSpace(aiModel) ? JValue.CreateNull() : (JToken)aiModel;
                string aiVendor = o.Ai.Input != null ? o.Ai.Input.Provider : null;
                meta["resolvedProvider"] = string.IsNullOrWhiteSpace(aiVendor) ? JValue.CreateNull() : (JToken)aiVendor;
                if (!string.IsNullOrEmpty(o.Ai.Error))
                {
                    meta["aiError"] = o.Ai.Error;
                }
            }
            else
            {
                meta["aiRan"] = false;
                meta["aiModel"] = JValue.CreateNull();
                meta["resolvedProvider"] = JValue.CreateNull();   // 1.0.200.0
            }

            meta["elapsed"] = elapsedMs;

            // 2.6 -- WHERE THE LOG IS, and why it is not there if it is not. This is the answer to
            // "do we even have logs?" -- it travels back with every verdict.
            meta["logFile"] = string.IsNullOrEmpty(JudgeFileLog.FilePath) ? JValue.CreateNull() : (JToken)JudgeFileLog.FilePath;
            if (!string.IsNullOrEmpty(JudgeFileLog.LastError))
            {
                meta["logError"] = JudgeFileLog.LastError;
            }

            // 2.7 -- the log now writes even when the folder asked for refuses. logFile is always
            // the real path; logNote names the folder that was refused and why.
            if (!string.IsNullOrEmpty(JudgeFileLog.FallbackNote))
            {
                meta["logNote"] = JudgeFileLog.FallbackNote;
            }

            if (!string.IsNullOrEmpty(o.AuditId))
            {
                meta["auditId"] = o.AuditId;
            }

            meta["tokenWarning"] = false;
            env["meta"] = meta;

            JObject patch = new JObject();
            if (o.Card != null)
            {
                if (!string.IsNullOrEmpty(o.Card.StationName))
                {
                    patch["station"] = o.Card.StationName;
                }

                if (!string.IsNullOrEmpty(o.Card.AssetType))
                {
                    patch["assetType"] = o.Card.AssetType;
                }

                if (o.Card.ResetAt != null)
                {
                    patch["resetTime"] = o.Card.ResetAt.Value.ToOffset(JudgeUtil.Ist).ToString("HH:mm:ss/dd.MM.yyyy");
                }
            }

            if (patch.Count > 0)
            {
                env["context_patch"] = patch;
            }

            return env.ToString(Formatting.None);
        }

        internal static JObject VerdictJson(JudgeOutcome o, bool withTrace)
        {
            JObject v = new JObject();
            v["alertId"] = o.AlertId;
            v["finalDecision"] = o.Policy.FinalDecision;
            v["decision"] = o.Policy.Decision;
            v["decidedBy"] = o.Policy.DecidedBy;
            v["reason"] = string.Join("; ", o.Policy.Reasons);
            v["likelyCause"] = o.LikelyCause;
            v["category"] = o.Category;
            v["confidence"] = o.Policy.Confidence;

            // 1.0.201.0 -- the decision as data. "reason" is prose for the operator; a replay report
            // groups by decisiveStep and shows decisionPath, so it never has to parse sentences.
            v["decisiveStep"] = DecisiveStep(o.Policy.DecidedBy);
            v["decisionPath"] = DecisionPathJson(o);
            if (o.Ai != null && o.Ai.Ran)
            {
                JObject ai = new JObject();
                ai["recommendation"] = o.Ai.Recommendation;
                ai["logicCorrect"] = o.Ai.LogicCorrect != null ? new JValue(o.Ai.LogicCorrect.Value) : JValue.CreateNull();
                ai["dataCorrect"] = o.Ai.DataCorrect != null ? new JValue(o.Ai.DataCorrect.Value) : JValue.CreateNull();
                ai["confidence"] = o.Ai.Confidence;
                ai["headline"] = o.Ai.Headline;
                ai["recommendedAction"] = o.Ai.RecommendedAction;
                v["ai"] = ai;
            }
            else if (o.Ai != null && o.Ai.Error != null)
            {
                v["aiError"] = o.Ai.Error;
            }

            v["rulesFired"] = new JArray(o.RulesFired);
            JArray evidence = new JArray();
            foreach (JudgeEvidenceItem e in o.Evidence)
            {
                JObject ej = new JObject();
                ej["tag"] = e.Tag;
                ej["text"] = e.Text;
                ej["source"] = e.Source;
                evidence.Add(ej);
            }

            v["evidence"] = evidence;
            v["conditions"] = o.Live != null ? ConditionsJson(o.Live) : new JArray();
            if (o.Live != null)
            {
                JObject lv = new JObject();
                lv["result"] = o.Live.Result;
                lv["detail"] = o.Live.Detail;
                lv["windowSec"] = o.Live.WindowSec;
                lv["windowSource"] = o.Live.WindowSource;
                lv["windowFrom"] = JudgeUtil.Iso(o.Live.WindowFrom);
                lv["windowTo"] = JudgeUtil.Iso(o.Live.WindowTo);
                lv["conditionLogic"] = o.Live.ConditionLogic;
                // 2.3 -- so a caller can tell "re-checked and held" from "nothing was re-checkable".
                lv["heldCount"] = o.Live.HeldCount;
                lv["countedCount"] = o.Live.CountedCount;
                lv["noEvidence"] = o.Live.NoEvidence;
                v["liveCheck"] = lv;
            }

            // 2.7 -- the set card against the reset card. Always present once the gates passed, so
            // a caller can tell "the alert proved itself from its own two cards" from "there was no
            // reset card to compare" -- and, when a verdict is argued about, read the two numbers
            // and the rule that separated them without re-running anything.
            if (o.Reset != null)
            {
                JObject rs = new JObject();
                rs["result"] = o.Reset.Result;
                rs["detail"] = o.Reset.Detail;
                rs["resetAt"] = JudgeUtil.IsoOrNull(o.Reset.ResetAt);
                rs["confirmedCount"] = o.Reset.ConfirmedCount;
                rs["contradictedCount"] = o.Reset.ContradictedCount;
                rs["comparedCount"] = o.Reset.ComparedCount;

                JArray rc = new JArray();
                foreach (JudgeResetCondResult x in o.Reset.Conditions)
                {
                    JObject cj = new JObject();
                    cj["name"] = x.Condition != null ? x.Condition.Label : null;
                    cj["role"] = x.Condition != null ? x.Condition.Role : null;
                    cj["status"] = x.Status;
                    cj["setValue"] = x.SetValue == null ? JValue.CreateNull() : (JToken)x.SetValue.Value;
                    cj["resetValue"] = x.ResetValue == null ? JValue.CreateNull() : (JToken)x.ResetValue.Value;
                    cj["rule"] = x.Rule;
                    cj["detail"] = x.Detail;
                    rc.Add(cj);
                }

                rs["conditions"] = rc;
                v["resetCheck"] = rs;
            }

            // 2.1: what a human already decided about this asset + cause. Always present, so a caller
            // can tell "nobody has ruled on this" (checked true, 0 rows) from "we could not ask"
            // (checked false, with the reason) -- the second must never be read as "no history".
            if (o.PriorDiscards != null)
            {
                JObject pd = new JObject();
                pd["checked"] = o.PriorDiscards.Checked;
                pd["lookbackDays"] = o.PriorDiscards.LookbackDays;
                pd["count"] = o.PriorDiscards.Rows.Count;
                pd["suppressedByReviewer"] = o.PriorDiscards.RejectedBy != null;
                pd["error"] = string.IsNullOrEmpty(o.PriorDiscards.Error) ? JValue.CreateNull() : (JToken)o.PriorDiscards.Error;
                pd["rows"] = JudgePriorDiscards.Json(o.PriorDiscards);
                v["priorDiscards"] = pd;
            }

            // 1.0.202.0: maintenance window + similar past cases. Present whenever the card was read,
            // in report mode too -- "wouldDecide" is what it says, "enforced" is whether it decided.
            if (o.Learned != null)
            {
                v["learned"] = JudgeLearned.Json(o.Learned);
            }

            v["caveats"] = o.Caveats;
            v["degraded"] = o.Degraded;
            if (withTrace)
            {
                v["trace"] = TraceJson(o.Trace);
            }

            return v;
        }

        // 1.0.201.0 -- the JudgePolicy ladder, one entry per step, in the order the policy walks it.
        // Built only from what the outcome already holds; a step that did not run says SKIPPED.
        // Exactly one entry has decisive = true: the step named by DecisiveStep(DecidedBy).
        internal static JArray DecisionPathJson(JudgeOutcome o)
        {
            string decisive = DecisiveStep(o.Policy.DecidedBy);
            JudgeVerifierOutcome v = o.Verifier;
            JArray path = new JArray();

            // card
            string cardResult = o.Card != null && decisive != "card" ? "PASS" : "FAIL";
            string cardDetail = o.Card != null
                ? o.Card.CauseCode + " on asset " + o.Card.AssetId + " triggered " + JudgeUtil.Iso(o.Card.TriggeredAt)
                : "card could not be read";
            path.Add(PathStep("card", cardResult, cardDetail, decisive));

            // card <-> catalogue
            string matchResult = "SKIPPED";
            string matchDetail = "verifier did not run";
            if (v != null && v.CardMatch != null)
            {
                matchResult = v.CardMatch.Match ? "PASS" : v.CardMatch.NothingRecognised ? "NO_DATA" : "FAIL";
                matchDetail = "agree " + v.CardMatch.Agree.Count + ", disagree " + v.CardMatch.Disagree.Count;
                if (v.CardMatch.Disagree.Count > 0)
                {
                    matchDetail = matchDetail + ": " + string.Join("; ", v.CardMatch.Disagree);
                }
            }

            path.Add(PathStep("catalog-match", matchResult, matchDetail, decisive));

            // reviewer history
            string priorResult = "SKIPPED";
            string priorDetail = "lookup not made";
            JudgePriorDiscardResult pd = o.PriorDiscards;
            if (pd != null)
            {
                if (!pd.Checked)
                {
                    priorResult = "NOT_CHECKED";
                    priorDetail = pd.Error;
                }
                else if (pd.RejectedBy != null)
                {
                    priorResult = "FAIL";
                    priorDetail = "reviewer marked this cause not valid on pending id " + pd.RejectedBy.Id
                        + " (" + JudgeUtil.T(pd.RejectedBy.DiscardedAt, "yyyy-MM-dd HH:mm") + ")";
                }
                else
                {
                    priorResult = "PASS";
                    priorDetail = pd.Rows.Count + " earlier discard(s), none marked not valid";
                }
            }

            path.Add(PathStep("prior-discard", priorResult, priorDetail, decisive));

            // 1.0.202.0 -- maintenance window, then the similar past cases
            JudgeLearnedResult lr = o.Learned;
            string maintResult = "SKIPPED";
            string maintDetail = "not looked at";
            string caseResult = "SKIPPED";
            string caseDetail = "not looked at";
            if (lr != null)
            {
                if (lr.MaintMode != "off")
                {
                    maintResult = !lr.MaintChecked ? "NOT_CHECKED" : lr.InMaintenance ? "FAIL" : "PASS";
                    maintDetail = !lr.MaintChecked ? lr.MaintError
                        : (lr.InMaintenance ? "asset in maintenance window " + lr.MaintId : "asset not in a maintenance window") + " (mode " + lr.MaintMode + ")";
                }

                if (lr.CaseMode != "off")
                {
                    caseResult = !lr.CaseChecked ? "NOT_CHECKED" : lr.Match == null || lr.Match.Decision == null ? "NO_DATA" : lr.Match.Decision == "PASS" ? "PASS" : "FAIL";
                    caseDetail = !lr.CaseChecked ? lr.CaseError
                        : lr.Match.Nearest.Count + " most similar past case(s): " + lr.Match.SuppressedCount + " discarded, " + lr.Match.PassCount
                            + " approved, pool " + lr.Match.PoolSize + " (mode " + lr.CaseMode + ")";
                }
            }

            path.Add(PathStep("maintenance", maintResult, maintDetail, decisive));
            path.Add(PathStep("similar-cases", caseResult, caseDetail, decisive));

            // catalogue gates over history
            string gateResult = "SKIPPED";
            string gateDetail = "verifier did not run";
            if (v != null)
            {
                gateResult = v.FlowResult == "PROCEED" ? "PASS" : v.FlowResult == "SUPPRESSED" ? "FAIL" : v.FlowResult;
                gateDetail = "relay " + v.Relay.Result + ", analog " + v.Analog.Result + ", adjacency " + v.Adjacency.Result;
                if (v.ResetBeforeSustain)
                {
                    gateDetail = gateDetail + ", reset before sustain " + v.SustainSec + " s";
                }
            }

            path.Add(PathStep("history-gates", gateResult, gateDetail, decisive));

            // set card against reset card (checked before live in JudgePolicy)
            string resetResult = o.Reset != null ? o.Reset.Result : "SKIPPED";
            string resetDetail = o.Reset != null ? o.Reset.Detail : "no reset card";
            path.Add(PathStep("reset-check", resetResult, resetDetail, decisive));

            // live re-check
            string liveResult = "SKIPPED";
            string liveDetail = "live re-check did not run";
            if (o.Live != null)
            {
                int triggeredHeld = o.Live.Conditions.Count(x => x.Condition != null && x.Condition.Role != "enabling" && x.Status == "HELD");
                liveResult = o.Live.Result;
                liveDetail = o.Live.HeldCount + " of " + o.Live.CountedCount + " counted condition(s) held, "
                    + triggeredHeld + " of them triggered (non-enabling)";
                if (o.Live.NoEvidence)
                {
                    liveDetail = liveDetail + ", no independent evidence";
                }
            }

            path.Add(PathStep("live-recheck", liveResult, liveDetail, decisive));

            // AI
            string aiResult = "NOT_REQUESTED";
            string aiDetail = "AI not called";
            if (o.Ai != null && o.Ai.Ran)
            {
                aiResult = o.Policy.DecidedBy == "ai-overruled" ? "OVERRULED" : o.Ai.Recommendation;
                aiDetail = "recommended " + o.Ai.Recommendation
                    + ", logic_correct " + JudgeUtil.TriText(o.Ai.LogicCorrect)
                    + ", data_correct " + JudgeUtil.TriText(o.Ai.DataCorrect)
                    + ", confidence " + o.Ai.Confidence;
            }
            else if (o.Ai != null)
            {
                aiResult = "UNAVAILABLE";
                aiDetail = o.Ai.Error;
            }

            path.Add(PathStep("ai", aiResult, aiDetail, decisive));

            path.Add(PathStep("final", o.Policy.FinalDecision, "decidedBy " + o.Policy.DecidedBy, decisive));
            return path;
        }

        // 1.0.201.0 -- every DecidedBy value JudgePolicy.Set is called with, mapped to its step.
        // "gates" (all checks passed, AI not requested or not final) and anything new map to "final".
        internal static string DecisiveStep(string decidedBy)
        {
            switch (decidedBy ?? "")
            {
                case "card-invalid":
                    return "card";
                case "no-catalog":
                case "card-match":
                    return "catalog-match";
                case "prior-discard":
                    return "prior-discard";
                case "maintenance":
                    return "maintenance";
                case "similar-cases":
                    return "similar-cases";
                case "sustain":
                case "adjacency":
                case "relay-gate":
                case "analog-gate":
                case "no-data":
                    return "history-gates";
                case "card-contradiction":
                    return "reset-check";
                case "live-recheck":
                case "live-no-data":
                case "live-unsupported":
                    return "live-recheck";
                case "ai":
                case "ai-overruled":
                case "ai-unavailable":
                    return "ai";
                default:
                    return "final";
            }
        }

        private static JObject PathStep(string step, string result, string detail, string decisiveStep)
        {
            JObject s = new JObject();
            s["step"] = step;
            s["result"] = result;
            s["detail"] = detail;
            s["decisive"] = step == decisiveStep;
            return s;
        }

        internal static JArray ConditionsJson(JudgeLiveResult live)
        {
            JArray arr = new JArray();
            foreach (JudgeConditionResult cr in live.Conditions)
            {
                JudgeCardCondition c = cr.Condition;
                JObject o = new JObject();
                o["role"] = c.Role;
                o["kind"] = c.Kind;
                o["name"] = c.Label;
                o["cardValue"] = c.CardValueText;
                if (c.Kind == "relay")
                {
                    o["wantState"] = c.WantState;
                }
                else
                {
                    JArray th = new JArray();
                    foreach (JudgeCardThreshold t in c.Thresholds)
                    {
                        JObject tj = new JObject();
                        tj["label"] = t.Label;
                        tj["value"] = t.Value;
                        tj["comparison"] = t.Comparison;
                        tj["status"] = t.Status;
                        th.Add(tj);
                    }

                    o["thresholds"] = th;
                }

                o["status"] = cr.Status;
                o["liveHeld"] = cr.Status == "HELD";
                o["source"] = cr.Source;
                o["attid"] = cr.Attid;

                // 1.0.190.0 -- the ONE number the breach was decided on, so the AI cannot pick a
                // different clause out of the card's OR list. On 1137362 it read the "< 210" clause
                // and reported 217.8 mA as a recovery when the effective threshold was 261.26.
                if (cr.EffThreshold != null)
                {
                    o["effectiveThreshold"] = JudgeUtil.NumOrNull(cr.EffThreshold);
                    o["effectiveDirection"] = cr.EffDirection;
                }

                o["liveAtSet"] = JudgeUtil.NumOrNull(cr.LiveAtSet);
                o["liveMin"] = JudgeUtil.NumOrNull(cr.LiveMin);
                o["liveMax"] = JudgeUtil.NumOrNull(cr.LiveMax);
                o["liveLast"] = JudgeUtil.NumOrNull(cr.LiveLast);
                o["heldForSec"] = JudgeUtil.NumOrNull(cr.HeldForSec);
                o["samples"] = cr.Samples;
                if (cr.FirstBreakAt != null)
                {
                    o["firstBreakAt"] = JudgeUtil.Iso(cr.FirstBreakAt.Value);
                    o["firstBreakValue"] = JudgeUtil.NumOrNull(cr.FirstBreakValue);
                }

                if (cr.Formula != null)
                {
                    o["formula"] = cr.Formula;
                }

                o["detail"] = cr.Detail;
                arr.Add(o);
            }

            return arr;
        }

        internal static JToken LiveJson(JudgeLiveResult live, bool forModel)
        {
            if (live == null)
            {
                return JValue.CreateNull();
            }

            JObject o = new JObject();
            o["result"] = live.Result;
            o["detail"] = live.Detail;
            o["windowSec"] = live.WindowSec;
            o["windowSource"] = live.WindowSource;
            o["windowFrom"] = JudgeUtil.Iso(live.WindowFrom);
            o["windowTo"] = JudgeUtil.Iso(live.WindowTo);
            o["conditionLogic"] = live.ConditionLogic;
            if (live.WaitedSec > 0)
            {
                o["waitedSec"] = live.WaitedSec;
            }

            o["conditions"] = ConditionsJson(live);
            return o;
        }

        internal static JObject TraceJson(JudgeTrace t)
        {
            if (t == null)
            {
                return null;
            }

            JObject o = new JObject();
            if (t.Card != null)
            {
                JObject c = new JObject();
                c["valid"] = t.Card.Valid;
                c["problems"] = new JArray(t.Card.Problems);
                c["causeCode"] = t.Card.CauseCode;
                c["assetType"] = t.Card.AssetType;
                c["alertType"] = t.Card.AlertType;
                c["triggeredAt"] = JudgeUtil.IsoOrNull(t.Card.TriggeredAt);
                c["valueCount"] = t.Card.ValueCount;
                c["relayCount"] = t.Card.RelayCount;
                o["card"] = c;
            }

            if (t.Verifier != null)
            {
                o["verifier"] = VerifierJson(t.Verifier);
            }

            if (t.EvidenceBundle != null)
            {
                o["evidenceBundle"] = EvidenceJson(t.EvidenceBundle);
            }

            if (t.Live != null)
            {
                o["live"] = LiveJson(t.Live, false);
            }

            if (t.Ai != null)
            {
                JudgeAiVerdict a = t.Ai;
                JObject aj = new JObject();
                aj["ran"] = a.Ran;
                aj["recommendation"] = a.Recommendation;
                aj["logicCorrect"] = a.LogicCorrect != null ? new JValue(a.LogicCorrect.Value) : JValue.CreateNull();
                aj["dataCorrect"] = a.DataCorrect != null ? new JValue(a.DataCorrect.Value) : JValue.CreateNull();
                aj["verdict"] = a.Verdict;
                aj["confidence"] = a.Confidence;
                aj["headline"] = a.Headline;
                aj["likelyCause"] = a.LikelyCause;
                aj["category"] = a.Category;
                JArray ev = new JArray();
                foreach (JudgeEvidenceItem e in a.Evidence)
                {
                    JObject ej = new JObject();
                    ej["tag"] = e.Tag;
                    ej["text"] = e.Text;
                    ej["source"] = e.Source;
                    ev.Add(ej);
                }

                aj["evidence"] = ev;
                aj["recommendedAction"] = a.RecommendedAction;
                aj["caveats"] = a.Caveats;
                aj["degradedInputs"] = new JArray(a.DegradedInputs);
                aj["error"] = a.Error;
                aj["rawJson"] = a.RawJson;
                if (a.Input != null)
                {
                    JObject inp = new JObject();
                    inp["model"] = a.Input.Model;
                    inp["systemChars"] = a.Input.SystemChars;
                    inp["userChars"] = a.Input.UserChars;
                    inp["sections"] = new JArray(a.Input.Sections);
                    inp["seriesSent"] = a.Input.SeriesSent;
                    inp["samplesSent"] = a.Input.SamplesSent;
                    aj["input"] = inp;
                }

                o["ai"] = aj;
            }

            if (t.Policy != null)
            {
                JObject p = new JObject();
                p["finalDecision"] = t.Policy.FinalDecision;
                p["decision"] = t.Policy.Decision;
                p["decidedBy"] = t.Policy.DecidedBy;
                p["confidence"] = t.Policy.Confidence;
                p["reasons"] = new JArray(t.Policy.Reasons);
                o["policy"] = p;
            }

            o["log"] = new JArray(t.Log);
            return o;
        }

        internal static JObject VerifierJson(JudgeVerifierOutcome v)
        {
            JObject o = new JObject();
            o["alertState"] = v.AlertState;
            o["setTime"] = JudgeUtil.Iso(v.SetTime);
            o["resetTime"] = JudgeUtil.IsoOrNull(v.ResetTime);
            o["evalMode"] = v.EvalMode;
            o["resetBeforeSustain"] = v.ResetBeforeSustain;
            o["heldForSec"] = JudgeUtil.NumOrNull(v.HeldForSec);
            if (v.CardMatch != null)
            {
                JObject m = new JObject();
                m["match"] = v.CardMatch.Match;
                m["agree"] = new JArray(v.CardMatch.Agree);
                m["disagree"] = new JArray(v.CardMatch.Disagree);
                m["notOnCard"] = new JArray(v.CardMatch.NotOnCard);
                o["cardMatch"] = m;
            }
            else
            {
                o["cardMatch"] = JValue.CreateNull();
            }

            o["relay"] = GateJson(v.Relay);
            o["analog"] = GateJson(v.Analog);
            JObject adj = new JObject();
            adj["result"] = v.Adjacency.Result;
            adj["detail"] = v.Adjacency.Detail;
            adj["familyTrackIds"] = new JArray(v.Adjacency.FamilyTrackIds);
            adj["findings"] = new JArray(v.Adjacency.Findings);
            o["adjacency"] = adj;
            o["flowResult"] = v.FlowResult;
            o["causeCode"] = v.CauseCode;
            o["seq"] = v.Seq;
            o["alertType"] = v.AlertType;
            o["verdict"] = v.Verdict;
            o["coverage"] = v.Coverage;
            o["coverageNotes"] = new JArray(v.CoverageNotes);
            o["confidence"] = v.Confidence;
            o["reclassifiedCause"] = v.ReclassifiedCause;
            o["stage1"] = v.Stage1;
            o["sustainSec"] = v.SustainSec;
            o["windowFrom"] = JudgeUtil.Iso(v.WindowFrom);
            o["windowTo"] = JudgeUtil.Iso(v.WindowTo);
            JArray attrs = new JArray();
            foreach (JudgeAttrResult a in v.Attrs)
            {
                JObject aj = new JObject();
                aj["name"] = a.Name;
                aj["attid"] = a.Attid;
                aj["class"] = a.Class;
                aj["gate"] = a.Gate;
                aj["status"] = a.Status;
                aj["detail"] = a.Detail;
                aj["threshold"] = JudgeUtil.NumOrNull(a.Threshold);
                aj["thresholdFormula"] = a.ThresholdFormula;
                aj["observed"] = JudgeUtil.NumOrNull(a.Observed);
                aj["samples"] = a.Samples;
                aj["tagId"] = a.TagId != null ? new JValue(a.TagId.Value) : JValue.CreateNull();
                aj["stateAtSet"] = JudgeUtil.NumOrNull(a.StateAtSet);
                aj["stateAtSetTs"] = JudgeUtil.IsoOrNull(a.StateAtSetTs);
                aj["stateSource"] = a.StateSource;
                aj["heldForSec"] = JudgeUtil.NumOrNull(a.HeldForSec);
                aj["transitionsInFrame"] = a.TransitionsInFrame;
                attrs.Add(aj);
            }

            o["attrs"] = attrs;
            o["fired"] = new JArray(v.Fired);
            o["ruleRaw"] = v.RuleRaw;
            o["verdictLogic"] = v.VerdictLogic;
            return o;
        }

        private static JObject GateJson(JudgeGate g)
        {
            JObject o = new JObject();
            o["name"] = g.Name;
            o["result"] = g.Result;
            o["detail"] = g.Detail;
            return o;
        }

        private static JObject StepJson(JudgeStepSeries s)
        {
            JObject o = new JObject();
            o["attribute"] = s.Attribute;
            o["attid"] = s.Attid;
            o["tagId"] = s.TagId;
            o["roleType"] = s.RoleType.ToString();
            o["stateAtStart"] = JudgeUtil.NumOrNull(s.StateAtStart);
            o["stateAtStartTs"] = JudgeUtil.IsoOrNull(s.StateAtStartTs);
            o["stateSource"] = s.StateSource;
            JArray tr = new JArray();
            foreach (JudgeSample t in s.Transitions)
            {
                JObject tj = new JObject();
                tj["ts"] = JudgeUtil.Iso(t.Ts);
                tj["value"] = t.Value;
                tr.Add(tj);
            }

            o["transitions"] = tr;
            o["last"] = JudgeUtil.NumOrNull(s.Last);
            return o;
        }

        internal static JObject EvidenceJson(JudgeEvidenceBundle b)
        {
            JObject o = new JObject();
            o["from"] = JudgeUtil.Iso(b.From);
            o["to"] = JudgeUtil.Iso(b.To);
            JObject steps = new JObject();
            foreach (KeyValuePair<int, JudgeStepSeries> kv in b.Steps)
            {
                steps[kv.Key.ToString()] = StepJson(kv.Value);
            }

            o["steps"] = steps;
            JObject adj = new JObject();
            foreach (KeyValuePair<int, JudgeStepSeries> kv in b.AdjacentTpr)
            {
                adj[kv.Key.ToString()] = StepJson(kv.Value);
            }

            o["adjacentTpr"] = adj;
            o["degraded"] = b.Degraded;
            o["degradedInputs"] = new JArray(b.DegradedInputs);
            JObject series = new JObject();
            foreach (KeyValuePair<string, JudgeTagSeries> kv in b.Series)
            {
                JudgeTagSeries s = kv.Value;
                JObject sj = new JObject();
                sj["attribute"] = s.Attribute;
                sj["attid"] = s.Attid;
                sj["tagId"] = s.TagId;
                sj["roleType"] = s.RoleType.ToString();
                JArray samples = new JArray();
                foreach (JudgeSample t in s.Samples)
                {
                    JObject tj = new JObject();
                    tj["ts"] = JudgeUtil.Iso(t.Ts);
                    tj["value"] = t.Value;
                    samples.Add(tj);
                }

                sj["samples"] = samples;
                sj["min"] = JudgeUtil.NumOrNull(s.Min);
                sj["max"] = JudgeUtil.NumOrNull(s.Max);
                sj["avg"] = JudgeUtil.NumOrNull(s.Avg);
                sj["last"] = JudgeUtil.NumOrNull(s.Last);
                sj["first"] = JudgeUtil.NumOrNull(s.First);
                series[kv.Key] = sj;
            }

            o["series"] = series;
            JObject present = new JObject();
            foreach (KeyValuePair<int, bool> kv in b.TagPresent)
            {
                present[kv.Key.ToString()] = kv.Value;
            }

            o["tagPresent"] = present;
            JObject avg = new JObject();
            foreach (KeyValuePair<int, double> kv in b.BaselineAvg)
            {
                avg[kv.Key.ToString()] = kv.Value;
            }

            o["baselineAvg"] = avg;
            o["frsAlert"] = b.FrsAlert;
            return o;
        }

        /// <summary>GET JudgeRuleJson: one cause or the list.</summary>
        internal static string Rules(string cause, string requestId, long elapsedMs)
        {
            JObject env = new JObject();
            JObject meta = new JObject();
            meta["componentVersion"] = ComponentVersion;
            meta["build"] = Build;
            meta["requestId"] = requestId ?? "";
            meta["elapsed"] = elapsedMs;
            if (string.IsNullOrWhiteSpace(cause))
            {
                JArray list = new JArray();
                foreach (JudgeCauseDef d in JudgeCauseCatalog.All)
                {
                    JObject o = new JObject();
                    o["seq"] = d.Seq;
                    o["cause"] = d.Cause;
                    o["alertType"] = d.AlertType;
                    o["sustainSec"] = d.SustainSec;
                    o["source"] = d.Source;
                    o["attrs"] = d.Attrs.Length;
                    list.Add(o);
                }

                env["status"] = "ok";
                env["rules"] = list;
                env["meta"] = meta;
                return env.ToString(Formatting.None);
            }

            JudgeCauseDef def = JudgeVerifier.FindByText(cause.Trim());
            if (def == null)
            {
                env["status"] = "not_found";
                env["rule"] = JValue.CreateNull();
                meta["cause"] = cause;
                env["meta"] = meta;
                return env.ToString(Formatting.None);
            }

            env["status"] = "ok";
            env["rule"] = RuleJson(def);
            env["meta"] = meta;
            return env.ToString(Formatting.None);
        }

        internal static JObject RuleJson(JudgeCauseDef d)
        {
            JObject o = new JObject();
            o["seq"] = d.Seq;
            o["cause"] = d.Cause;
            o["alertType"] = d.AlertType;
            o["ldPercent"] = JudgeUtil.NumOrNull(d.LdPercent);
            o["lowerRule"] = d.LowerRule;
            o["sustainSec"] = d.SustainSec;
            o["derivedFormula"] = d.DerivedFormula;
            o["source"] = d.Source;
            o["ruleRaw"] = d.RuleRaw;
            JArray attrs = new JArray();
            foreach (JudgeCauseAttr a in d.Attrs)
            {
                JObject aj = new JObject();
                aj["name"] = a.Name;
                aj["attid"] = a.Attid;
                aj["datatype"] = a.Datatype;
                aj["class"] = a.Class;
                aj["state"] = a.State;
                aj["cmp"] = a.Cmp;
                aj["thresholdRef"] = a.ThresholdRef;
                aj["effectiveThreshold"] = a.EffectiveThreshold;
                aj["gate"] = a.Gate;
                attrs.Add(aj);
            }

            o["attrs"] = attrs;
            o["verdictLogic"] = d.VerdictLogic;
            o["isFailure"] = d.IsFailure;
            return o;
        }
    }
}
