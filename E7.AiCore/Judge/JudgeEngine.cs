using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace E7.AiCore.Judge
{
    // ============================================================================================
    // JudgeEngine -- the order of steps for one judgement. The web action (AiChatController.Judge.cs)
    // calls HandleAsync and returns what comes back; it adds nothing.
    //
    // CHANGE HERE to add, remove or reorder a step.
    //
    //   1. read + check the body                      JudgeRequestReader   -> bad_request
    //   2. card = the CLAIM                           JudgeCardParser
    //   3. catalog lookup + card <-> catalog match    JudgeVerifier
    //   4. history over [set, reset|now]              JudgeEvidence (MCP)
    //   5. catalog gates (relay / analog / adjacency) JudgeVerifier
    //   6. live re-check of the card's conditions     JudgeLiveCheck      (2.0)
    //   7. AI: logic correct? data correct?           JudgeAi             (only when 5 and 6 passed)
    //   8. PASS / SUPPRESSED                          JudgePolicy
    //   9. envelope + audit                           JudgeEnvelope, JudgeAudit
    // Port of AlertJudge v1.1.2.0 Controllers/AlertController.cs (Judge, RulesOnly) plus step 6.
    // ============================================================================================
    internal sealed class JudgeEngine
    {
        private readonly IJudgeMcp mcp;
        private readonly JudgeModelCall modelCall;
        private readonly Func<DateTimeOffset> clock;
        private readonly Func<TimeSpan, CancellationToken, Task> delay;
        // 1.0.192.3 -- REMOVED: private readonly SemaphoreSlim admission = new SemaphoreSlim(64, 64);
        //
        // Declared since the first build and never awaited, acquired or released anywhere. It read
        // like a 64-deep safety backstop under Judge.MaxConcurrent and it was not one: the only
        // admission control is the `running` counter below against Math.Max(1, cfg.MaxConcurrent),
        // which has a floor of 1 and no ceiling. With MaxConcurrent now at 1000 someone reading
        // this class would have concluded the real limit was 64. Deleted rather than wired up --
        // the AI provider's own rate limit is the intended backstop, and it fails open.
        private int running;

        internal JudgeEngine(IJudgeMcp mcp, JudgeModelCall modelCall, Func<DateTimeOffset> clock, Func<TimeSpan, CancellationToken, Task> delay)
        {
            this.mcp = mcp;
            this.modelCall = modelCall;
            this.clock = clock;
            this.delay = delay;
        }

        // ------------------------------------------------------------------------------------------
        // HTTP entry: POST JudgeAlertJson
        // ------------------------------------------------------------------------------------------
        internal async Task<JudgeHttpResult> HandleAsync(JudgeHttpRequest http, CancellationToken ct)
        {
            Stopwatch sw = Stopwatch.StartNew();
            JudgeSettings cfg = JudgeSettings.Current;
            JudgeHttpResult res = new JudgeHttpResult();
            string rid = http.RequestId;

            if (string.IsNullOrEmpty(http.ConfiguredKey))
            {
                return Fail(res, "disabled", rid, sw, "InternalApiKey not set");
            }

            if (!AiText.FixedTimeEquals(http.ConfiguredKey, http.SuppliedKey))
            {
                return Fail(res, "unauthorized", rid, sw, null);
            }

            if (http.ContentLength > cfg.MaxBodyBytes || (http.Body != null && http.Body.Length > cfg.MaxBodyBytes))
            {
                return Fail(res, "bad_request", rid, sw, "body over " + cfg.MaxBodyBytes + " bytes");
            }

            string badReason;
            JudgeRequest req = JudgeRequestReader.Read(http.Body, http.RequestId, out badReason);
            if (req == null)
            {
                return Fail(res, "bad_request", rid, sw, badReason);
            }

            rid = req.RequestId;
            if (Interlocked.Increment(ref running) > Math.Max(1, cfg.MaxConcurrent))
            {
                Interlocked.Decrement(ref running);
                return Fail(res, "busy", rid, sw, "more than " + cfg.MaxConcurrent + " judgements running");
            }

            try
            {
                JudgeLog.Write("INFO", "JUDGE", "start alert=" + req.AlertId + " mode=" + req.Options.Mode + " useAi=" + req.Options.UseAi + " rid=" + rid);
                JudgeOutcome outcome = await RunAsync(req, cfg, ct).ConfigureAwait(false);
                sw.Stop();
                outcome.AuditId = JudgeAudit.Write(outcome, sw.ElapsedMilliseconds, cfg);
                res.AuditId = outcome.AuditId;
                res.HttpStatus = 200;
                res.Json = JudgeEnvelope.Ok(outcome, req, sw.ElapsedMilliseconds);
                JudgeLog.Write("INFO", "JUDGE", "end alert=" + req.AlertId + " → " + outcome.Policy.FinalDecision + " (by " + outcome.Policy.DecidedBy + ") conf=" + outcome.Policy.Confidence + " " + sw.ElapsedMilliseconds + "ms rid=" + rid);

                // 2.6 -- the whole judgement, as one readable block, into JudgeAIlogs. Almost every
                // line the judge produces goes into the per-request trace rather than through
                // JudgeLog, so flushing the trace here is what makes the file complete without
                // touching fifty call sites. It is written whatever the request asked for "trace":
                // that flag only controls the HTTP response.
                // 1.0.203.0 -- the block now also carries the request, the card, the matched past
                // cases, the AI answer and the decision path, and is written to the alert's own
                // file under by-site\<site>\<asset type>\<cause>\ and to the monthly summary CSV.
                JudgeBlockLog.Write(req, outcome, rid, sw.ElapsedMilliseconds, cfg, null, res.Json);
                return res;
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                res.HttpStatus = 200;
                res.Json = JudgeEnvelope.Error("timeout", rid, sw.ElapsedMilliseconds, "cancelled");
                return res;
            }
            catch (Exception ex)
            {
                sw.Stop();
                JudgeLog.Write("ERROR", "JUDGE", "alert=" + req.AlertId + " failed: " + ex);

                res.HttpStatus = 200;
                res.Json = JudgeEnvelope.Error("model_error", rid, sw.ElapsedMilliseconds, "internal");

                // 1.0.203.0 -- a failed judgement gets its block too: the steps done so far, then the error
                JudgeBlockLog.Write(req, req.Partial, rid, sw.ElapsedMilliseconds, cfg, ex, res.Json);
                return res;
            }
            finally
            {
                Interlocked.Decrement(ref running);
            }
        }

        /// <summary>GET JudgeRuleJson?cause=</summary>
        internal JudgeHttpResult HandleRules(JudgeHttpRequest http, string cause)
        {
            Stopwatch sw = Stopwatch.StartNew();
            JudgeHttpResult res = new JudgeHttpResult();
            if (string.IsNullOrEmpty(http.ConfiguredKey))
            {
                return Fail(res, "disabled", http.RequestId, sw, "InternalApiKey not set");
            }

            if (!AiText.FixedTimeEquals(http.ConfiguredKey, http.SuppliedKey))
            {
                return Fail(res, "unauthorized", http.RequestId, sw, null);
            }

            res.HttpStatus = 200;
            res.Json = JudgeEnvelope.Rules(cause, http.RequestId, sw.ElapsedMilliseconds);
            return res;
        }

        /// <summary>A gate line that tells "not run" apart from "ran and found nothing".</summary>
        private static string GateText(JudgeGate g, bool gatesRan)
        {
            string detail = (g.Detail ?? "").Trim();
            if (!gatesRan && detail.Length == 0)
            {
                return g.Name + ": not run — the card and the catalogue disagreed, so no history was fetched";
            }

            return g.Name + ": " + g.Result + (detail.Length > 0 ? " — " + detail : "");
        }


        private static JudgeHttpResult Fail(JudgeHttpResult res, string status, string rid, Stopwatch sw, string error)
        {
            sw.Stop();
            res.HttpStatus = JudgeEnvelope.HttpStatusFor(status);
            res.Json = JudgeEnvelope.Error(status, rid, sw.ElapsedMilliseconds, error);
            return res;
        }

        // ------------------------------------------------------------------------------------------
        // The pipeline (test entry point too)
        // ------------------------------------------------------------------------------------------
        internal async Task<JudgeOutcome> RunAsync(JudgeRequest req, JudgeSettings cfg, CancellationToken ct)
        {
            JudgeOptions opt = req.Options;
            JudgeTrace trace = new JudgeTrace();
            List<string> log = trace.Log;
            JudgeOutcome outcome = new JudgeOutcome();
            outcome.AlertId = req.AlertId;
            outcome.Trace = trace;
            req.Partial = outcome;   // 1.0.203.0 -- lets HandleAsync log a judgement that throws part-way
            outcome.Mode = opt.Mode == "rules" ? "rules" : (opt.UseAi ? "rules+live+ai" : "rules+live");
            outcome.Engine = opt.UseAi ? (opt.Engine ?? "default") : "rules-only";

            // 1. card = the CLAIM
            JudgeCardSummary summary;
            JudgeCard card = JudgeCardParser.Parse(req.AlertId, req.Card, out summary);
            trace.Card = summary;
            if (card != null && req.CardRaw != null)
            {
                card.Raw = req.CardRaw.DeepClone();
            }

            log.Add("card: valid=" + summary.Valid + " cause=" + summary.CauseCode + " type=" + summary.AssetType + "/" + summary.AlertType + " problems=[" + string.Join("; ", summary.Problems) + "]");
            outcome.Card = card;
            if (card == null)
            {
                outcome.Policy = JudgePolicy.Decide(null, summary, null, null, null, null, null, false, cfg, log);
                trace.Policy = outcome.Policy;
                outcome.Degraded = true;
                outcome.Caveats = "card is not a JSON object";
                outcome.Category = outcome.Policy.FinalDecision == "PASS" ? "Genuine failure" : "False alarm";
                return outcome;
            }

            // 2. catalog lookup
            JudgeCauseDef def = JudgeVerifier.Find(card);
            List<JudgeCauseDef> stage2 = JudgeVerifier.Stage2Candidates(def);
            log.Add(def == null ? "catalog: no CauseDef for '" + card.CauseCode + "' / id " + (card.CauseId != null ? card.CauseId.ToString() : "")
                : "catalog: #" + def.Seq + " " + def.Cause + " " + def.AlertType + " SustainSec=" + def.SustainSec + " attrs=" + def.Attrs.Length + " stage2=" + stage2.Count);

            // 3. card <-> catalog must agree before anything is fetched
            JudgeCardCatalogMatch match = def != null ? JudgeVerifier.MatchCard(card, def) : null;
            if (match != null)
            {
                log.Add("match: " + (match.Match ? "OK" : "MISMATCH") + " agree=[" + string.Join("; ", match.Agree) + "] disagree=[" + string.Join("; ", match.Disagree) + "] notOnCard=[" + string.Join(", ", match.NotOnCard) + "]");
            }

            // 3b. (2.1) what a PERSON already decided about this asset + this cause. Read-only; nothing
            //     in AlertV3 changes. A reviewer who discarded an earlier alert and answered "the alert
            //     was NOT valid" has settled this cause on this asset, so the repeat is suppressed
            //     without spending MCP history or an AI call on it -- the same short-circuit the
            //     card/catalog mismatch branch below already uses. The lookup never fails a judgement.
            JudgePriorDiscardResult prior = JudgePriorDiscards.Lookup(card, cfg, log);
            outcome.PriorDiscards = prior;
            trace.PriorDiscards = prior;
            bool priorRejects = prior.RejectedBy != null
                             && JudgeUtil.MapDecision(cfg.PriorDiscardRejected) == "SUPPRESSED"
                             && opt.Mode != "rules";

            // 3c. (1.0.202.0) what reviewers decided about alerts LIKE this one: a maintenance window
            //     on the asset, and the most similar past cards of the same cause (judge-cases.json).
            //     In "report" mode (the default) this is recorded and changes nothing. In "enforce"
            //     mode it decides, the same way the prior-discard short-circuit does. It never
            //     overrides a card that contradicts the catalogue or a reviewer's "not valid". It DOES
            //     decide a cause the catalogue does not know (IPS SMR-2..5 VOLT FAIL and others are
            //     raised by AlertV3 but have no CauseDef): reviewers decided those too.
            JudgeLearnedResult learned = JudgeLearned.Check(card, req.CardRaw ?? req.Card, cfg, log);
            outcome.Learned = learned;
            // 1.0.208.0 -- an asset under maintenance is SUPPRESSED before every other check: the
            //     card/catalogue mismatch and the prior-discard branches no longer come first.
            //     It also comes before the card validity check: only the asset id is needed.
            bool maintDecides = learned.Enforced && learned.InMaintenance
                             && opt.Mode != "rules";
            // 1.0.208.0 -- THE RAIN RULE. Rain at the site explains the Learned.RainCauses causes
            //     (TC TR VOLT LOW, TC BALST/SLPR RES LOW), so the alert is real and is PASSED. It
            //     comes after maintenance and before the past cases. A weather fault gives no answer.
            bool rainDecides = false;
            string rainMode = (cfg.LearnedRainMode ?? "").Trim().ToLowerInvariant();
            if (!maintDecides && summary.Valid && opt.Mode != "rules"
                && (rainMode == "enforce" || rainMode == "report")
                && JudgeWeather.Applies(card.CauseCode, cfg))
            {
                learned.Rain = await JudgeWeather.CheckAsync(mcp, card, cfg, log, ct).ConfigureAwait(false);
                if (learned.Rain.Checked && learned.Rain.Rain)
                {
                    if (rainMode == "enforce")
                    {
                        learned.WouldDecide = "PASS";
                        learned.WouldDecideBy = "rain";
                        learned.Enforced = true;
                        rainDecides = true;
                        log.Add("learned: rain -> PASS (ENFORCED)");
                    }
                    else
                    {
                        log.Add("learned: rain -> PASS (report only, verdict not changed)");
                    }
                }
            }

            bool learnedDecides = maintDecides || rainDecides
                               || (learned.Enforced && learned.WouldDecide != null
                                   && summary.Valid && opt.Mode != "rules"
                                   && !(match != null && !match.Match) && !priorRejects);

            JudgeVerifierOutcome v;
            JudgeEvidenceBundle ev;
            JudgeLiveResult live = null;
            JudgeResetResult reset = null;   // 2.7 -- set card vs reset card
            JudgeAiVerdict ai;
            if (opt.Mode == "rules")
            {
                // rules only: card parsing + catalog match, no history, no AI (the old POST /api/alert/rules)
                ev = new JudgeEvidenceBundle();
                ev.From = card.TriggeredAt;
                ev.To = card.TriggeredAt;
                v = new JudgeVerifierOutcome();
                v.CardMatch = match;
                v.CauseCode = def != null ? def.Cause : card.CauseCode;
                v.Seq = def != null ? def.Seq : 0;
                v.AlertType = def != null ? def.AlertType : card.AlertType;
                v.Verdict = def == null ? "NO_RULE" : "NOT_CONFIRMED";
                v.FlowResult = "NO_DATA";
                v.SetTime = card.TriggeredAt;
                v.ResetTime = card.ResetAt;
                JudgeVerifier.StampTiming(v, card, def);   // 1.0.192.3 -- else alertState/evalMode/sustainSec ship as initialisers
                ai = new JudgeAiVerdict();
            }
            else if (!maintDecides && !rainDecides && !cfg.CardMismatchVerifies && match != null && !match.Match)
            {
                ev = new JudgeEvidenceBundle();
                ev.From = card.TriggeredAt;
                ev.To = card.TriggeredAt;
                v = new JudgeVerifierOutcome();
                v.CardMatch = match;
                v.CauseCode = def.Cause;
                v.Seq = def.Seq;
                v.AlertType = def.AlertType;
                v.FlowResult = "SUPPRESSED";
                v.Verdict = "NOT_CONFIRMED";
                v.SetTime = card.TriggeredAt;
                v.ResetTime = card.ResetAt;
                JudgeVerifier.StampTiming(v, card, def);   // 1.0.192.3 -- all 9 card-match records on 13.10 reported SET_ONLY on a closed alert
                ai = new JudgeAiVerdict();
                ai.Error = "not run: card/catalog mismatch";
            }
            else if (!cfg.DataCheckFirst && !maintDecides && !rainDecides && priorRejects)
            {
                // A reviewer already judged this exact asset + cause not a real alert. Nothing the
                // history or the model could add changes that, so neither is spent.
                ev = new JudgeEvidenceBundle();
                ev.From = card.TriggeredAt;
                ev.To = card.TriggeredAt;
                v = new JudgeVerifierOutcome();
                v.CardMatch = match;
                v.CauseCode = def != null ? def.Cause : card.CauseCode;
                v.Seq = def != null ? def.Seq : 0;
                v.AlertType = def != null ? def.AlertType : card.AlertType;
                v.FlowResult = "SUPPRESSED";
                v.Verdict = "NOT_CONFIRMED";
                v.SetTime = card.TriggeredAt;
                v.ResetTime = card.ResetAt;
                JudgeVerifier.StampTiming(v, card, def);   // 1.0.192.3 -- same initialiser leak on the prior-discard short-circuit
                ai = new JudgeAiVerdict();
                ai.Error = "not run: a reviewer already discarded this cause on this asset as not valid";
                log.Add("flow: short-circuited by a prior human discard -- history and AI skipped");
            }
            else if (maintDecides || (!cfg.DataCheckFirst && learnedDecides))
            {
                // 1.0.208.0 -- with Flow.DataCheckFirst only MAINTENANCE stops here. Rain, the past
                // cases and a prior discard decide AFTER the alert-time data check (JudgePolicy).
                // 1.0.202.0 -- decided by the maintenance window or by the similar past cases.
                // Nothing is fetched: the gates are reported as not run, never as passed.
                ev = new JudgeEvidenceBundle();
                ev.From = card.TriggeredAt;
                ev.To = card.TriggeredAt;
                v = new JudgeVerifierOutcome();
                v.CardMatch = match;
                v.CauseCode = def != null ? def.Cause : card.CauseCode;
                v.Seq = def != null ? def.Seq : 0;
                v.AlertType = def != null ? def.AlertType : card.AlertType;
                v.FlowResult = learned.WouldDecide == "SUPPRESSED" ? "SUPPRESSED" : "NO_DATA";
                v.Verdict = def == null ? "NO_RULE" : "NOT_CONFIRMED";
                v.SetTime = card.TriggeredAt;
                v.ResetTime = card.ResetAt;
                JudgeVerifier.StampTiming(v, card, def);
                ai = new JudgeAiVerdict();
                ai.Error = "not run: decided by " + learned.WouldDecideBy;
                log.Add("flow: short-circuited by " + learned.WouldDecideBy + " -- history and AI skipped");
            }
            else
            {
                // 4. history: carry-forward state + transitions over [alert, reset|now]; adjacent tracks for TC failures
                JudgeEvidence evidence = new JudgeEvidence(mcp, cfg);
                ev = await evidence.FetchAsync(card, def, stage2, opt.FamilyTrackIds, log, ct).ConfigureAwait(false);

                // 5. gates: relay (DataLogger) decides FAILURE; relay AND analog for PREDICTIVE; adjacency for TC
                v = JudgeVerifier.Verify(card, def, ev, stage2, log, cfg);
                v.CardMatch = match;
                v.NotFound = ev.NotFound;   // 1.0.208.0 -- named in the no-data reason

                // 1.0.208.0 -- CARD-DATA VALIDATION. The card prints each measured value WITH its
                // own timestamp ("v1 at t1"); every pair is checked against the value history shows
                // at that very instant. A card whose values the history does not show is suppressed
                // in the policy (card-data); tags already resolved by the rule fetch are reused.
                v.CardData = await JudgeCardDataCheck.RunAsync(card, evidence, ev, cfg, log, ct).ConfigureAwait(false);

                // 1.0.208.0 -- NO CATALOGUE RULE IS NOT THE END. The alert card carries its own
                // conditions (enabling + triggered, with thresholds). When the catalogue has no rule
                // for the cause, those conditions are verified against the data of the alert time
                // frame by the live re-check below, instead of stopping at "no-catalog".
                if (def == null && cfg.LiveEnabled)
                {
                    v.FlowResult = "PROCEED";
                    log.Add("catalog: no rule for this cause -- verifying the card's own conditions against the alert time frame instead");
                }

                // 6. live re-check of every card condition (2.0) -- only worth doing when the gates passed
                // 2.2: an externally determined cause has no condition set to re-check -- the
                // trigger is a field-device flag, not a historian tag. Running the live check would
                // report UNSUPPORTED and suppress it, which is exactly the wrong answer.
                if (def != null && def.ExternallyDetermined)
                {
                    log.Add("live: skipped (" + def.Cause + " is determined outside RDPMS - no re-derivable condition)");
                }
                else if (cfg.LiveEnabled && cfg.LiveOnlyWhenNoRule && def != null)
                {
                    // 1.0.208.0 -- ONE DATA CHECK. The cause has a catalogue rule and that rule was
                    // just checked against the alert-time data. The card's own conditions are a
                    // second copy of the same question and are used only when there is no rule.
                    log.Add("live: not run (the catalogue rule is the data check; card conditions are checked only for a cause with no rule)");
                }
                else if (cfg.LiveEnabled && v.FlowResult == "PROCEED")
                {
                    JudgeLiveCheck check = new JudgeLiveCheck(evidence, cfg, clock, delay);
                    live = await check.RunAsync(card, def, ev, log, ct).ConfigureAwait(false);
                }
                else if (cfg.LiveEnabled)
                {
                    log.Add("live: skipped (gates → " + v.FlowResult + ")");
                }

                // 6b. the set card against the reset card (2.7). Costs nothing -- no historian, no
                // network -- and it is the ONLY evidence available for an alert whose window is too
                // old to re-check. Run unconditionally once the gates passed, so the policy has it
                // whichever way the live step went.
                if (v.FlowResult == "PROCEED")
                {
                    reset = JudgeResetCheck.Run(card, cfg, log);
                }

                // 7. AI -- only when the gates and the live re-check passed
                bool livePassed = live == null || live.Result == "PASS" || live.Result == "SKIPPED";

                // 2.7 -- a CLOSED alert whose two cards corroborate each other must reach the AI as
                // well. Without this it clears the policy's live branch and then falls at
                // "ai-unavailable" instead, which is how 1.0.179.0 turned one suppression into
                // another (aiError: "not run: live re-check → NO_DATA"). Same condition as the
                // policy's carry-on, kept deliberately in step with it.
                bool closedCarry = !livePassed
                    && live != null && live.NoEvidence
                    && (live.Result == "NO_DATA" || live.Result == "UNSUPPORTED")
                    && reset != null && reset.Result == "CONFIRMED"
                    && JudgeUtil.MapDecision(cfg.LiveClosedConfirmedNoData) == "PASS";
                if (closedCarry)
                {
                    livePassed = true;
                    log.Add("live: " + live.Result + " carried on — closed alert corroborated by its own reset card (Live.ClosedConfirmedNoData=PASS)");
                }

                // 1.0.208.0 -- the data check passed and rain / past cases / a prior discard will
                // decide next (JudgePolicy), so the AI call is not spent.
                bool postDecides = cfg.DataCheckFirst && (learnedDecides || priorRejects);
                if (postDecides && v.FlowResult == "PROCEED" && livePassed)
                {
                    ai = new JudgeAiVerdict();
                    ai.Error = "not run: decided by " + (learnedDecides ? learned.WouldDecideBy : "prior-discard") + " after the data check";
                }
                else if (opt.UseAi && v.FlowResult == "PROCEED" && livePassed)
                {
                    JudgeAi reasoner = new JudgeAi(cfg, modelCall);
                    ai = await reasoner.ReasonAsync(card, def, v, ev, live, prior, opt.Engine, log, ct).ConfigureAwait(false);
                }
                else
                {
                    ai = new JudgeAiVerdict();
                    ai.Error = opt.UseAi ? (v.FlowResult != "PROCEED" ? "not run: gates → " + v.FlowResult : "not run: live re-check → " + live.Result) : "useAi=false";
                }
            }

            trace.Verifier = v;
            if (opt.Mode != "rules")
            {
                trace.EvidenceBundle = ev;
                trace.Live = live;
                trace.Ai = ai;
            }

            // 8. binary decision
            JudgePolicyOutcome pol = JudgePolicy.Decide(card, summary, v, live, reset, ai, prior, opt.UseAi && opt.Mode != "rules", cfg, log, learnedDecides ? learned : null);
            trace.Policy = pol;
            outcome.Policy = pol;
            outcome.Verifier = v;
            outcome.Live = live;
            outcome.Reset = reset;
            outcome.Ai = ai;

            // ---- provenance-tagged evidence ----
            List<JudgeEvidenceItem> evidenceList = new List<JudgeEvidenceItem>();
            if (v.CardMatch != null && opt.Mode != "rules")
            {
                evidenceList.Add(JudgeEvidenceItem.Of("record", "card↔catalog: " + (v.CardMatch.Match ? "agree" : "DISAGREE") + " — " + string.Join("; ", v.CardMatch.Agree.Concat(v.CardMatch.Disagree)), "match"));
            }

            if (opt.Mode != "rules")
            {
                // 2.1: a gate that never ran (card/catalogue mismatch short-circuits before any
                // fetch) used to be reported as "NO_DATA — " with an empty detail, which reads as
                // "we looked and found nothing". Say it was not run instead.
                bool gatesRan = ev != null && ev.Steps != null && ev.Steps.Count > 0;
                evidenceList.Add(JudgeEvidenceItem.Of("fetched", GateText(v.Relay, gatesRan), "gate:relay"));
                evidenceList.Add(JudgeEvidenceItem.Of("fetched", GateText(v.Analog, gatesRan), "gate:analog"));
                if (v.Adjacency.Result != "SKIPPED")
                {
                    evidenceList.Add(JudgeEvidenceItem.Of("fetched", "adjacency: " + v.Adjacency.Result + " — " + string.Join("; ", v.Adjacency.Findings), "gate:adjacency"));
                }

                foreach (JudgeAttrResult a in v.Attrs.Where(a => a.Status == "HELD" || a.Status == "LAPSED"))
                {
                    evidenceList.Add(JudgeEvidenceItem.Of("fetched", a.Name + ": " + a.Status + " — " + a.Detail, "verify:attid" + a.Attid));
                }

                foreach (JudgeAttrResult a in v.Attrs.Where(a => a.Status == "NO_DATA" || a.Status == "UNSUPPORTED" || a.Status == "SKIPPED"))
                {
                    evidenceList.Add(JudgeEvidenceItem.Of("computed", a.Name + ": " + a.Status + " — " + a.Detail, "verify:attid" + a.Attid));
                }

                HashSet<long> seen = new HashSet<long>();
                foreach (JudgeTagSeries s in ev.Series.Values)
                {
                    if (!seen.Add(s.TagId))
                    {
                        continue;
                    }

                    evidenceList.Add(JudgeEvidenceItem.Of("fetched", s.Attribute + ": n=" + s.Samples.Count + " min=" + JudgeUtil.F(s.Min, "0.###") + " max=" + JudgeUtil.F(s.Max, "0.###") + " last=" + JudgeUtil.F(s.Last, "0.###") + " [" + JudgeUtil.T(ev.From, "HH:mm:ss") + "–" + JudgeUtil.T(ev.To, "HH:mm:ss") + "]", "mcp.history_get"));
                }

                if (live != null)
                {
                    foreach (JudgeConditionResult cr in live.Conditions)
                    {
                        string tag = cr.Status == "HELD" || cr.Status == "LAPSED" ? "fetched" : "computed";
                        evidenceList.Add(JudgeEvidenceItem.Of(tag, "live " + cr.Condition.Role + " " + cr.Condition.Label + ": " + cr.Status + " — " + cr.Detail, "live:" + cr.Source));
                    }
                }

                // 2.7 -- tagged "record", not "fetched": these are the alert service's own two cards,
                // not something the judge went and measured. The distinction matters when a verdict
                // is read back -- corroboration from the record is weaker than an independent
                // reading, and the evidence list should not blur the two.
                if (reset != null && reset.Result != "SKIPPED")
                {
                    evidenceList.Add(JudgeEvidenceItem.Of("record",
                        "set↔reset card: " + reset.Result + " — " + reset.Detail, "card:reset"));
                    foreach (JudgeResetCondResult rr in reset.Conditions)
                    {
                        if (rr.Status == "NOT_COMPARABLE" || rr.Status == "IGNORED")
                        {
                            continue;
                        }

                        evidenceList.Add(JudgeEvidenceItem.Of("record",
                            (rr.Condition != null ? rr.Condition.Label : "?") + ": " + rr.Status + " — " + rr.Detail,
                            "card:reset"));
                    }
                }
            }

            evidenceList.AddRange(ai.Evidence);
            outcome.Evidence = evidenceList;

            List<string> caveatParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(ai.Caveats))
            {
                caveatParts.Add(ai.Caveats);
            }

            if (v.CoverageNotes.Count > 0)
            {
                caveatParts.Add("not evaluated: " + string.Join("; ", v.CoverageNotes.Distinct()));
            }

            if (live != null && (live.Result == "NO_DATA" || live.Result == "UNSUPPORTED"))
            {
                caveatParts.Add("live re-check " + live.Result + ": " + live.Detail);
            }

            if (!summary.Valid)
            {
                caveatParts.Add("Card problems: " + string.Join("; ", summary.Problems));
            }

            if (opt.Mode == "rules")
            {
                caveatParts.Add("rules-only: card parsing + catalog match only, no history");
            }

            if (cfg.LoadError != null)
            {
                caveatParts.Add(cfg.LoadError);
            }

            outcome.Caveats = caveatParts.Count > 0 ? string.Join(" ", caveatParts) : null;
            outcome.LikelyCause = v.ReclassifiedCause ?? ai.LikelyCause ?? (v.FlowResult == "PROCEED" ? v.CauseCode : null);
            // 2.1: a verdict nothing actually verified is not a "False alarm" at 90% -- those numbers
            // come from the catalogue defaults and mislead the reviewer. An outcome decided before
            // any evidence (card/catalogue problems) or on an unverifiable re-check is reported as
            // Unverified at 0.
            bool unverified = pol.DecidedBy == "card-invalid" || pol.DecidedBy == "no-catalog"
                           || pol.DecidedBy == "card-match"   || pol.DecidedBy == "no-data"
                           || pol.DecidedBy == "live-no-data" || pol.DecidedBy == "live-unsupported"
                           || pol.DecidedBy == "ai-unavailable";
            if (unverified)
            {
                pol.Confidence = 0;
                outcome.Policy.Confidence = 0;
            }

            outcome.Category = opt.Mode == "rules" ? null
                             : unverified ? "Unverified"
                             : (ai.Category ?? (pol.FinalDecision == "PASS" ? "Genuine failure" : "False alarm"));
            // 2.1: v.Fired is the GATE's view. When the live re-check could not confirm a condition
            // (NO_DATA / UNSUPPORTED / FAIL), naming it as a fired rule overstates what was verified.
            outcome.RulesFired = v.Fired;
            if (live != null && live.Result != "PASS" && live.Result != "SKIPPED" && v.Fired != null)
            {
                List<string> unconfirmed = new List<string>();
                for (int i = 0; i < live.Conditions.Count; i++)
                {
                    JudgeConditionResult lc = live.Conditions[i];
                    string ln = lc.Condition != null
                        ? (string.IsNullOrEmpty(lc.Condition.Name) ? lc.Condition.Label : lc.Condition.Name)
                        : null;
                    // HELD = confirmed live; IGNORED = deliberately not counted. Anything else
                    // (LAPSED / NO_DATA / UNSUPPORTED) was not confirmed.
                    if (lc.Status != "HELD" && lc.Status != "IGNORED" && !string.IsNullOrWhiteSpace(ln))
                    {
                        unconfirmed.Add(JudgeUtil.Norm(JudgeUtil.StripUnit(ln)));
                    }
                }

                outcome.RulesFired = v.Fired
                    .Where(n => !unconfirmed.Contains(JudgeUtil.Norm(JudgeUtil.StripUnit(n))))
                    .ToList();
            }
            outcome.Degraded = ev.Degraded || v.FlowResult == "NO_DATA" || opt.Mode == "rules";
            return outcome;
        }
    }
}
