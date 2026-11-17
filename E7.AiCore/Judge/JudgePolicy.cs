using System;
using System.Collections.Generic;
using System.Linq;

namespace E7.AiCore.Judge
{
    // ============================================================================================
    // JudgePolicy -- turns the gates, the live re-check and the AI answer into PASS / SUPPRESSED.
    //
    // CHANGE HERE to change the decision rule itself. Settings (judge-settings.json):
    //   Flow.CardUnreadable, Flow.CardMismatch, Flow.NoData, Flow.AiIsFinal, Flow.AiUnavailable,
    //   Flow.AiPassMinConfidence, Live.NoData, Live.Unsupported.
    //
    // The order (2.0):
    //   card cannot be read                      -> Flow.CardUnreadable  (SUPPRESSED; J1 fix)      decidedBy card-invalid
    //   card has problems / no catalog entry     -> Flow.NoData                                     card-invalid / no-catalog
    //   card <-> catalog mismatch                -> Flow.CardMismatch                               card-match
    //   a reviewer already discarded this asset
    //   + cause and answered "not valid" (2.1)   -> PriorDiscard.Rejected                            prior-discard
    //   a catalog gate FAILED                    -> SUPPRESSED                                      sustain / adjacency / relay-gate / analog-gate
    //   a catalog gate had NO_DATA               -> Flow.NoData                                     no-data
    //   live re-check: a card condition LAPSED   -> SUPPRESSED                                      live-recheck
    //   live re-check: no data / no formula      -> Live.NoData / Live.Unsupported                  live-no-data / live-unsupported
    //   AI not requested or Flow.AiIsFinal=false -> PASS                                            gates
    //   AI did not run                           -> Flow.AiUnavailable                              ai-unavailable
    //   AI ran                                   -> PASS only if recommendation PASS, logic_correct
    //                                               and data_correct are not false, confidence >= min  ai
    // Port of AlertJudge v1.1.2.0 Pipeline/DecisionPolicy.cs plus the live and logic/data steps.
    // ============================================================================================
    internal static class JudgePolicy
    {
        internal static JudgePolicyOutcome Decide(JudgeCard card, JudgeCardSummary summary, JudgeVerifierOutcome v, JudgeLiveResult live, JudgeResetResult reset, JudgeAiVerdict ai, JudgePriorDiscardResult prior, bool aiRequested, JudgeSettings cfg, List<string> log, JudgeLearnedResult learned = null)
        {
            JudgePolicyOutcome o = new JudgePolicyOutcome();
            o.Decision = "REVIEW";
            o.Confidence = v != null ? v.Confidence : 0;

            // 0. card unusable
            if (card == null)
            {
                return Set(o, JudgeUtil.MapDecision(cfg.CardUnreadable), "card-invalid", "card problems: " + string.Join("; ", summary.Problems), log);
            }

            // 0a. (1.0.208.0) MAINTENANCE IS THE FIRST CHECK. An asset under maintenance at the alert
            //     time is suppressed before the card is even validated; only the asset id is needed.
            if (learned != null && learned.WouldDecide != null
                && (learned.WouldDecideBy == "maintenance" || (!cfg.DataCheckFirst && learned.WouldDecideBy == "rain")))
            {
                return Set(o, JudgeUtil.MapDecision(learned.WouldDecide), learned.WouldDecideBy, JudgeLearned.Reason(learned), log);
            }

            if (!summary.Valid)
            {
                return Set(o, JudgeUtil.MapDecision(cfg.NoData), "card-invalid", "card problems: " + string.Join("; ", summary.Problems), log);
            }

            // 0b. (1.0.202.0) the maintenance window, or the most similar past cases a reviewer decided.
            //     Passed in only when its mode is "enforce" AND the card does not contradict the
            //     catalogue AND no reviewer marked this cause not valid (JudgeEngine.learnedDecides);
            //     otherwise it is null here and the ladder below runs exactly as before. It sits above
            //     the no-catalog step because reviewers also decided causes the catalogue lacks.
            if (!cfg.DataCheckFirst && learned != null && learned.WouldDecide != null)
            {
                return Set(o, JudgeUtil.MapDecision(learned.WouldDecide), learned.WouldDecideBy, JudgeLearned.Reason(learned), log);
            }

            // 1.0.208.0 -- with no catalogue rule the card's own conditions are still verified by
            // the live re-check (JudgeEngine). Only when that check did not run does it stop here.
            if (v.Verdict == "NO_RULE" && live == null)
            {
                return Set(o, JudgeUtil.MapDecision(cfg.NoData), "no-catalog", "no CauseDef for this cause", log);
            }

            // 1. card <-> catalog
            // 2.4 -- only a NAMED contradiction suppresses here. When nothing could be lined up at
            // all (NothingRecognised) there is no contradiction to report, and the old code emitted
            // "...the two cannot both be right: ." and discarded the alert on the strength of an
            // empty list. A naming gap on our side is not evidence about the alert, so the pipeline
            // carries on and the gap is stated in the remark instead.
            string unrecognised = "";
            // 2.4 -- a PARTIAL match must not read as a full cross-check. NotOnCard has never
            // blocked a verdict and still does not, but the remark now says how much of the
            // catalogue rule was actually compared against the card.
            if (v.CardMatch != null && !v.CardMatch.NothingRecognised && v.CardMatch.NotOnCard.Count > 0)
            {
                unrecognised = " " + v.CardMatch.NotOnCard.Count + " of the catalogue's attributes for this cause ("
                    + string.Join(", ", v.CardMatch.NotOnCard)
                    + ") are not on the card, so that part of the rule was not cross-checked.";
            }

            if (v.CardMatch != null && v.CardMatch.NothingRecognised)
            {
                unrecognised = " " + string.Join(" ", v.CardMatch.Agree)
                    + ", so the card could not be cross-checked against the catalogue at all.";
                log.Add("policy: card<->catalogue recognised nothing — carrying on, not treating it as a mismatch");
            }
            else if (v.CardMatch != null && !v.CardMatch.Match && cfg.CardMismatchVerifies)
            {
                // 1.0.208.0 -- THE DATA DECIDES FIRST, THE CARD IS THE SECOND PART. Each cause has its
                // rule in the catalogue; that rule is checked against the data of the alert time by
                // the gates below. A card that prints the rule differently is noted, not obeyed.
                unrecognised = " The card differs from the catalogue rule (" + string.Join("; ", v.CardMatch.Disagree)
                    + "); the catalogue rule was checked against the data of the alert time instead.";
                log.Add("policy: card<->catalogue differ (" + string.Join("; ", v.CardMatch.Disagree)
                    + ") — not decisive, the catalogue rule is verified against the data (Flow.CardMismatch=VERIFY)");
            }
            else if (v.CardMatch != null && !v.CardMatch.Match)
            {
                return Set(o, JudgeUtil.MapDecision(cfg.CardMismatch), "card-match",
                    "The alert card does not describe this cause the way the catalogue does, so the two cannot both be right: "
                    + string.Join("; ", v.CardMatch.Disagree) + ".", log);
            }

            // 1c. (1.0.208.0) the card's printed values against history at the card's OWN times.
            //     The card says "v1 at t1" for each condition; history was read at t1. A card whose
            //     printed values the recorded data does not show is not describing real data.
            if (v.CardData != null && v.CardData.Mismatch)
            {
                return Set(o, "SUPPRESSED", "card-data",
                    "The values printed on the card are not what the history shows at the card's own times: "
                    + v.CardData.DifferText()
                    + ". The card does not match the recorded data, so the alert is suppressed.", log);
            }

            if (v.CardData != null && v.CardData.Agree > 0)
            {
                log.Add("policy: card data confirmed -- " + v.CardData.Agree
                    + " printed value(s) match history at the card's own times");
            }

            // 1b. (2.1) a person has already ruled on this asset + cause. Their answer outranks the
            //     machine gates below, which is why this sits above them: a reviewer who inspected the
            //     equipment and recorded "the alert was not valid" has information the telemetry does not.
            if (!cfg.DataCheckFirst && prior != null && prior.RejectedBy != null)
            {
                JudgePriorDiscard d = prior.RejectedBy;
                string when = JudgeUtil.T(d.DiscardedAt, "dd MMM yyyy HH:mm");
                string said = string.IsNullOrWhiteSpace(d.Remark) ? "" : " They wrote: “" + JudgeUtil.Trunc(d.Remark.Trim(), 300) + "”.";
                return Set(o, JudgeUtil.MapDecision(cfg.PriorDiscardRejected), "prior-discard",
                    "A reviewer already discarded this same alert on this asset on " + when
                    + " and recorded that the alert was not valid, so raising it again adds nothing new." + said
                    + " (Approval record " + d.Id + "; " + prior.Rows.Count + " manual discard"
                    + (prior.Rows.Count == 1 ? "" : "s") + " of this cause in the last " + prior.LookbackDays + " days.)", log);
            }

            // 2/3. history gates
            if (v.FlowResult == "SUPPRESSED")
            {
                string who = v.ResetBeforeSustain && v.Relay.Result != "FAIL" && v.Analog.Result != "FAIL" ? "sustain"
                    : v.Adjacency.Result == "FAIL" ? "adjacency" : v.Relay.Result == "FAIL" ? "relay-gate" : "analog-gate";
                string window = v.AlertState == "SET_RESET" ? "for the whole " + v.SustainSec + " s it had to hold" : "at the moment the alert was raised";
                string why;
                switch (who)
                {
                    case "sustain":
                        why = "The alert cleared on its own after "
                            + JudgeUtil.F((v.ResetTime.Value - v.SetTime).TotalSeconds, "0")
                            + " s, before the " + v.SustainSec + " s this cause has to hold to count, so it was a transient, not a failure.";
                        break;
                    case "adjacency":
                        why = "The neighbouring track circuits explain this one: " + v.Adjacency.Detail
                            + ". " + string.Join("; ", v.Adjacency.Findings) + ".";
                        break;
                    case "relay-gate":
                        why = "The DataLogger history does not support the card: the relay state did not hold " + window
                            + ". " + v.Relay.Detail + ".";
                        break;
                    default:
                        why = "The RDPMS history does not support the card: the reading was not outside its limit " + window
                            + ". " + v.Analog.Detail + ".";
                        break;
                }

                return Set(o, "SUPPRESSED", who, why, log);
            }

            if (v.FlowResult == "NO_DATA")
            {
                return Set(o, JudgeUtil.MapDecision(cfg.NoData), "no-data",
                    "There is not enough history to confirm or rule out this alert (relay check " + v.Relay.Result
                    + ", reading check " + v.Analog.Result + "), and an alert that cannot be verified is not delivered."
                    // 1.0.208.0 -- name what was missing, so the log line alone says what to fix.
                    + (v.NotFound != null && v.NotFound.Count > 0
                        ? " NOT FOUND on asset " + card.AssetId + ": " + string.Join(", ", v.NotFound)
                          + ". Fix: map these attributes to the asset, or add their tag title to JudgeAttrFacts.TagTitle."
                        : " The tags were found but returned no rows for the alert time."), log);
            }

            // 3b. THE CARD AGAINST ITSELF (2.7). The set card printed a number and a threshold; if
            // that number does not breach that threshold, the alert argues against itself and no
            // outside data is needed to see it. This is the mid-transition capture caught from the
            // card alone, and it is checked BEFORE the live step so it is not masked by an empty
            // historian -- the case where the live check can say nothing at all.
            if (reset != null && reset.Result == "CONTRADICTED")
            {
                return Set(o, JudgeUtil.MapDecision(cfg.LiveCardContradiction), "card-contradiction",
                    "The card's own measurements do not satisfy the card's own rule, so the alert contradicts itself: "
                    + reset.Detail + ".", log);
            }

            // 4. live re-check of the card's own conditions (2.0)
            string noEvidence = "";
            if (live != null)
            {
                // A real contradiction. This is the ONE live finding that is evidence against the
                // alert, and the only one that suppresses on its own.
                if (live.Result == "FAIL")
                {
                    // 3.0 -- when the alert's own reset card CONFIRMS the excursion, say so. On
                    // 1263751 this verdict read "False alarm", confidence 90, while that same
                    // response carried resetCheck CONFIRMED on two conditions. A suppression that
                    // hides the disagreement inside it cannot be argued with, and that is exactly
                    // the verdict a person needs to be able to argue with.
                    string contra = reset != null && reset.Result == "CONFIRMED"
                        ? " Note that the alert's own set and reset cards do NOT agree with this:"
                          + " " + reset.Detail + ". The live data is still preferred, because it is"
                          + " an independent reading of the window rather than more of the card --"
                          + " but if the two keep disagreeing on this cause, the window or the"
                          + " sampling is what to look at, not the alert."
                        : "";
                    return Set(o, "SUPPRESSED", "live-recheck",
                        "Re-checked against live historian data, the conditions printed on the card are no longer true: "
                        + live.Detail + ". The card's own numbers were most likely captured mid-transition."
                        + contra, log);
                }

                // 2.3 -- NO INDEPENDENT EVIDENCE EITHER WAY is a different finding from a
                // contradiction, and for a FAILURE it must not be treated as one. A FAILURE means
                // equipment is down now; discarding a possible one because our own historian has no
                // tag for the attribute is the unsafe direction, and it also hides the mapping gap
                // behind a verdict instead of reporting it. So a FAILURE with nothing held and
                // nothing lapsed carries on to the gates/AI step with the gap stated in the remark.
                // PREDICTIVE alerts are unchanged: they keep Live.NoData / Live.Unsupported.
                bool blank = live.NoEvidence && (live.Result == "NO_DATA" || live.Result == "UNSUPPORTED");
                bool isFailure = string.Equals(card.AlertType, "FAILURE", StringComparison.OrdinalIgnoreCase);
                bool carryOn = blank && isFailure && JudgeUtil.MapDecision(cfg.LiveNoEvidenceFailure) == "PASS";

                // 2.7 -- A CLOSED ALERT CANNOT BE RE-CHECKED AGAINST LIVE DATA, BY DEFINITION.
                //
                // The live step asks "are the card's conditions still true in the card's 15 s
                // window?". For an alert that has already reset, that window is in the past and the
                // condition is SUPPOSED to be false now -- that is what reset means. On a 3-day-old
                // alert the historian has nothing to say about it either way, and 1.0.172.0 read
                // that silence as disproof:
                //
                //   1137362 TC TR VOLT LOW -> SUPPRESSED / live-no-data, a true alert
                //   1034216 TC TR VOLT LOW -> SUPPRESSED / live-no-data, a true alert
                //
                // Both carry their own proof. The set card measured ITC RELAY END below its
                // threshold and the reset card measured it back above, ~29 min apart, written by
                // the alert service at the two ends of the event. JudgeResetCheck compares them.
                // When it CONFIRMS and the live step found nothing either way, the alert carries on
                // to the AI with the basis stated in the remark rather than being discarded.
                //
                // This applies to PREDICTIVE as well as FAILURE -- unlike the 2.3 carry-on above --
                // because the reason is different. 2.3 carries a FAILURE on the safety argument
                // that equipment may be down now. This carries a CLOSED alert on positive evidence
                // that the excursion was measured twice. A live FAIL is still never overridden:
                // that branch returned above, before this line.
                bool closed = card.ResetAt != null;
                bool corroborated = reset != null && reset.Result == "CONFIRMED";
                bool closedCarry = blank && closed && corroborated
                    && JudgeUtil.MapDecision(cfg.LiveClosedConfirmedNoData) == "PASS";

                if (closedCarry && !carryOn)
                {
                    noEvidence = " The alert had already reset, so its conditions cannot be re-checked"
                        + " against live data -- the window has passed and the condition is expected to"
                        + " be clear now (" + live.Detail + "). It is instead corroborated by the alert's"
                        + " own set and reset cards: " + reset.Detail + ".";
                    log.Add("policy: live " + live.Result + " on a CLOSED alert corroborated by its reset card — carrying on (Live.ClosedConfirmedNoData=PASS)");
                }
                else if (carryOn)
                {
                    noEvidence = " The re-check obtained no independent reading either way ("
                        + live.Detail + "), so this verdict rests on the history checks and the card alone;"
                        + " a FAILURE is not discarded for want of data on our side.";
                    if (corroborated)
                    {
                        noEvidence += " The alert's own set and reset cards do corroborate it: " + reset.Detail + ".";
                    }

                    log.Add("policy: live " + live.Result + " with no evidence either way on a FAILURE — carrying on (Live.NoEvidenceFailure=PASS)");
                }

                carryOn = carryOn || closedCarry;

                if (!carryOn)
                {
                    if (live.Result == "NO_DATA")
                    {
                        // 2.7 -- when the alert is closed, SAY SO in the reason. "could not be
                        // re-checked against live data" on an alert that reset three days ago reads
                        // as a data fault when it is usually just an expired window, and that
                        // ambiguity cost several builds of guesswork.
                        string why = closed
                            ? " The alert had already reset at " + JudgeUtil.T(card.ResetAt.Value.ToOffset(JudgeUtil.Ist), "HH:mm:ss/dd.MM.yyyy")
                              + ", so its window is in the past"
                              + (reset != null && reset.Result == "INCONCLUSIVE"
                                  ? ", and its own set and reset cards carry nothing comparable either (" + reset.Detail + ")"
                                  : "")
                              + "."
                            : "";
                        return Set(o, JudgeUtil.MapDecision(cfg.LiveNoData), "live-no-data",
                            "The card's conditions could not be re-checked against live data: " + live.Detail + "." + why, log);
                    }

                    if (live.Result == "UNSUPPORTED")
                    {
                        return Set(o, JudgeUtil.MapDecision(cfg.LiveUnsupported), "live-unsupported",
                            "At least one condition on the card cannot be re-checked automatically: " + live.Detail
                            + ". Add a formula under Live.Formulas, or list the label under Live.IgnoreConditions, to settle it.", log);
                    }
                }
            }

            // 4b. (1.0.208.0) THE DATA CHECK PASSED. Only now do rain, the past cases and a prior
            //     discard decide (Flow.DataCheckFirst): the alert-time data is checked in one place
            //     and comes before them.
            if (cfg.DataCheckFirst)
            {
                if (learned != null && learned.WouldDecide != null)
                {
                    return Set(o, JudgeUtil.MapDecision(learned.WouldDecide), learned.WouldDecideBy,
                        "The alert-time data check passed. " + JudgeLearned.Reason(learned), log);
                }

                if (prior != null && prior.RejectedBy != null)
                {
                    JudgePriorDiscard pd = prior.RejectedBy;
                    return Set(o, JudgeUtil.MapDecision(cfg.PriorDiscardRejected), "prior-discard",
                        "The alert-time data check passed, but a reviewer already discarded this same alert on this asset on "
                        + JudgeUtil.T(pd.DiscardedAt, "dd MMM yyyy HH:mm")
                        + " and recorded that the alert was not valid. (Approval record " + pd.Id + ".)", log);
                }
            }

            // 5. gates (and live) passed -> the AI confirms logic + data
            o.Decision = "ACCEPT";
            // 2.2: never let an externally determined cause read as if the judge verified it.
            string external = v.ExternallyDetermined
                ? " The trigger for this cause is set by the field device and is not re-derivable from"
                  + " historian data, so it was accepted as reported rather than independently verified."
                : "";
            string seen = prior != null && prior.Rows.Count > 0
                ? " A reviewer discarded this cause on this asset " + prior.Rows.Count + " time"
                  + (prior.Rows.Count == 1 ? "" : "s") + " in the last " + prior.LookbackDays
                  + " days without marking it invalid, so it did not block this alert."
                : "";
            // 2.3 -- a live PASS no longer always means "the conditions were re-checked and held": it
            // also covers "nothing on the card was re-checkable, so nothing contradicted it". Say
            // which one it was, rather than claiming a verification that did not happen.
            bool anyHeld = live != null && live.Conditions.Any(x => x.Status == "HELD");
            string liveText = live == null || live.Result != "PASS" ? ""
                : anyHeld ? ", and the card's conditions still hold on live data: " + live.Detail
                : ", and nothing in the live data contradicts the card: " + live.Detail;
            string gatesText = (v.Verdict == "NO_RULE"
                    ? "The catalogue has no rule for this cause, so the card's own conditions were checked against the alert time frame"
                    : "Every history check passed")
                + (v.ReclassifiedCause != null ? ", reclassified as " + v.ReclassifiedCause : "")
                + liveText
                + ".";
            if (!cfg.AiIsFinal || !aiRequested)
            {
                return Set(o, "PASS", "gates", gatesText + (aiRequested ? "" : " No AI confirmation was requested.") + unrecognised + noEvidence + external + seen, log);
            }

            if (ai == null || !ai.Ran || string.IsNullOrEmpty(ai.Recommendation))
            {
                // 2.9 -- AN ALERT THE COMPUTATION VERIFIED IS NOT THROWN AWAY BECAUSE THE EXPLAINER
                // IS DOWN.
                //
                // On 09.10.2026 both of these came back SUPPRESSED:
                //
                //   1137362  card↔catalog agree, relay PASS, analog PASS, live PASS (2 of 2 held
                //            through 15 s), reset card CONFIRMED, no prior discard -> decision
                //            ACCEPT -> finalDecision SUPPRESSED
                //   1034216  identical
                //
                // because the Anthropic API answered 400 "Your credit balance is too low". A billing
                // problem deleted two true alerts.
                //
                // This branch is only reachable AFTER the card/catalogue match, the catalogue gates
                // and the live re-check have all passed, so getting here means the computation has
                // already decided. The design of this judge is "the computation decides, the AI
                // explains": the AI is asked two questions about logic and data and can only ever
                // REMOVE an alert the computation accepted. An explainer that did not answer has not
                // removed anything.
                //
                // So VERIFIED is split from UNVERIFIED, because they are not the same claim:
                //
                //   verified    the live re-check held at least one condition on independent data,
                //               or the alert is closed and its own two cards corroborate it.
                //               Something was checked and it passed -> Flow.AiUnavailableVerified,
                //               default PASS. Delivered, with the AI's absence stated plainly and
                //               confidence left at 0 so nobody reads it as an AI-backed verdict.
                //
                //   unverified  the live step found no evidence either way and carried on (2.3,
                //               2.7). Nothing independent was confirmed, so the AI was the only
                //               remaining check -> Flow.AiUnavailable, default SUPPRESSED, which is
                //               what every build up to 1.0.184.0 did in both cases.
                //
                // Set Flow.AiUnavailableVerified = "SUPPRESSED" to go back to requiring the AI for
                // every delivery.
                bool liveHeldSomething = live != null && live.Result == "PASS" && live.HeldCount > 0;
                bool resetConfirmed = reset != null && reset.Result == "CONFIRMED";
                bool verified = liveHeldSomething || resetConfirmed;
                string aiWhy = ai != null && ai.Error != null ? ai.Error : "no recommendation returned";

                string tail = verified
                    ? " The AI confirmation could not run (" + aiWhy + "), so there is no AI opinion"
                      + " on this alert either way. It is delivered on the checks above, which did run"
                      + " and did pass: the AI can only rule an alert out, and it did not answer, so"
                      + " it has ruled nothing out."
                    : " The AI confirmation could not run (" + aiWhy + "). Nothing independent was"
                      + " confirmed either, so no check remained that could support this alert, and an"
                      + " alert nothing confirms is not delivered.";

                string avgNote0 = verified ? AvgOnlyMargin(card, cfg) : null;
                if (avgNote0 != null) { log.Add("policy: marginal average-only drift — " + avgNote0); }

                return Set(o, JudgeUtil.MapDecision(verified ? cfg.AiUnavailableVerified : cfg.AiUnavailable),
                    "ai-unavailable", gatesText + tail + unrecognised + noEvidence + seen
                    + (avgNote0 != null ? " " + avgNote0 : ""), log);
            }

            o.Confidence = ai.Confidence;
            bool logicOk = ai.LogicCorrect != false;
            bool dataOk = ai.DataCorrect != false;

            // =====================================================================================
            // 1.0.190.0 -- THE VETO CAP. Flow.AiVetoNeedsNewEvidence, default true.
            //
            // WHAT HAPPENED. On 11.10.2026 all three of 1137362, 1034216 and 1023426 passed every
            // computed check -- relay PASS, analog PASS, live PASS 2/2 held, reset CONFIRMED,
            // logic_correct true -- and were then deleted on data_correct=false alone, with
            // "the value recovered before the reset" as the reason. Seven checks said yes, one said
            // no, and AiIsFinal=true made the one win. Two of the three reasons were also
            // arithmetically false: the model read the wrong half of an OR threshold and called
            // 217.8 mA a recovery against an effective threshold of 261.26.
            //
            // THE RULE. When the computation VERIFIED the alert -- the live re-check held every
            // counted condition through the sustain window AND the set/reset cards corroborate each
            // other -- the AI may still suppress, but not on the ground that the condition stopped
            // later. Nothing in any cause rule requires a condition to persist to the reset, and
            // every closed alert recovered, so that ground would suppress every closed alert.
            //
            // This is narrow on purpose. The AI keeps its veto for everything it is actually good
            // at: logic_correct=false, a card contradicting its own numbers, a reading that does
            // not show the breach INSIDE the window, an instrument artefact. Only the
            // recovered-later argument is refused, and only when the computation already verified
            // the alert. Set Flow.AiVetoNeedsNewEvidence=false to restore 1.0.189.1 behaviour.
            // =====================================================================================
            bool liveHeldAll = live != null && live.Result == "PASS"
                && live.HeldCount > 0 && live.HeldCount == live.CountedCount;

            // 1.0.192.2 -- THIS CAP COULD NEVER FIRE IN PRODUCTION, AND THIS IS WHY.
            //
            // It used to read:
            //     resetCorroborates  = reset != null && reset.Result == "CONFIRMED"
            //                                        && reset.ContradictedCount == 0;
            //     computationVerified = liveHeldAll && resetCorroborates;
            //
            // AlertV3 calls the Judge from ONE place only -- the pre-Set gate -- and
            // BuildJudgeRequestJson hardcodes resetTime and resetCard to null there, because at
            // that moment the alert has not reset and no reset card exists. It cannot. So
            // JudgeCardParser leaves card.ResetAt null, JudgeResetCheck.Run returns at its
            // "the alert is still open (no reset card)" guard, and JudgeResetResult.Result keeps
            // its initialiser value "SKIPPED".
            //
            // "SKIPPED" is not "CONFIRMED". resetCorroborates was therefore false on EVERY real
            // gate call, computationVerified was false on every real gate call, and the whole
            // 1.0.190.0 veto cap -- the thing written to stop one AI sentence deleting an alert
            // that every computed check had passed -- was unreachable code the moment it shipped.
            // It only ever fired on a REPLAY of a closed alert, which is exactly the one case
            // that does not matter.
            //
            // The ai-unavailable branch above got this right by accident: it uses
            // (liveHeldSomething || resetConfirmed), an OR, so the missing reset card costs it
            // nothing. This cap used an AND.
            //
            // THE RULE NOW. A reset card is CORROBORATION when it exists, not a PRECONDITION.
            // The live re-check holding EVERY counted condition through the sustain window is the
            // verification; a reset card can only take that away by actively contradicting it.
            //
            //   reset.Result   liveHeldAll   before   now
            //   SKIPPED (every real gate call)  true   false <- bug   TRUE
            //   CONFIRMED                       true   true          true   (unchanged)
            //   CONTRADICTED                    true   false         false  (unchanged)
            //   INCONCLUSIVE                    true   false         true   (nothing comparable
            //                                                                either way is not a
            //                                                                contradiction)
            //   anything                        false  false         false  (unchanged)
            bool resetContradicts = reset != null
                && (reset.Result == "CONTRADICTED" || reset.ContradictedCount > 0);
            bool computationVerified = liveHeldAll && !resetContradicts;
            bool vetoCapped = false;

            string vetoGround = null;

            if (cfg.AiVetoNeedsNewEvidence && computationVerified
                && ai.DataCorrect == false && ai.LogicCorrect != false)
            {
                vetoCapped = true;
                dataOk = true;
                vetoGround = AiVetoGround(ai);   // 1.0.191.0 -- what it actually argued
                log.Add("policy: AI data_correct=false OVERRULED — the live re-check held "
                    + live.HeldCount + "/" + live.CountedCount + " counted conditions through the sustain"
                    + " window"
                    + (reset == null || reset.Result == "SKIPPED"
                        ? " and the alert is still open, so there is no reset card to consult"
                        : " and the set/reset cards do not contradict it (" + reset.Result + ")")
                    + " (Flow.AiVetoNeedsNewEvidence=true). Ground classified as: "
                    + (vetoGround ?? "unclassified — recorded, not characterised")
                    + ". AI said: " + (ai.Headline ?? "(no headline)"));
            }
            else if (JudgeUtil.MapDecision(cfg.AiVetoOnMissingData) == "PASS"
                && ai.DataCorrect == false && ai.LogicCorrect != false
                && AiGroundIsMissingData(ai))
            {
                // 1.0.192.0 -- "I CANNOT VERIFY THIS" IS NOT "THIS DID NOT HAPPEN".
                //
                // Every other step in this policy refuses to suppress for want of evidence --
                // Flow.NoData, Live.NoData, Live.Unsupported and Live.NoEvidenceFailure all exist
                // to make missing data a PASS. The AI step was the one hole left: the model is
                // handed a condition set it cannot resolve, answers data_correct=false because it
                // honestly cannot confirm anything, and one sentence undoes every check that did
                // pass.
                //
                // The 190 cap above does not close this hole, because it requires
                // computationVerified -- every counted condition held AND the reset cards
                // corroborate. When the tags are unmapped there is nothing to hold, so
                // liveHeldAll is false and that cap never engages. This one does not ask for a
                // verified computation; it asks only what the AI's GROUND was.
                //
                // 12.10.2026, the four AI suppressions out of sixteen real Mode-3 gate calls:
                //   14372 SIG RG CURR HIGH   "No telemetry available for either gating
                //                             attribute; alert cannot be confirmed."   conf 92
                //   14379 SIG RG CURR HIGH   same                                       conf 92
                //   14371 TC TR UP TPR DN    "...VTC 24 DC TPR I/P(V) condition is unmapped and
                //                             not verifiable in live data."              conf 82
                //                             -- and 3 of 3 COUNTED conditions HELD.
                //   14366 TC GJ SHORT        "No telemetry available to confirm..."      conf 95
                //                             -- and TC GJ SHORT is ExternallyDetermined: the
                //                                trigger is a field-device flag and there is
                //                                nothing to re-derive BY DESIGN.
                //
                // Reaching this line already means the card matched the catalogue, the catalogue
                // gates passed and the live re-check did not contradict the card. The AI is the
                // last witness, and a witness who says "I could not see" has not testified
                // against the alert.
                //
                // NARROW: only when the AI's own words name missing, unmapped or unverifiable
                // data (AiGroundIsMissingData). A model that says the numbers CONTRADICT the card
                // keeps its veto in full, exactly as before. Set
                // Flow.AiVetoOnMissingData=SUPPRESSED to restore 1.0.191.0 behaviour.
                vetoCapped = true;
                dataOk = true;
                vetoGround = "missing-data";
                log.Add("policy: AI data_correct=false OVERRULED — its stated ground is missing or"
                    + " unmapped telemetry, not data that contradicts the card"
                    + " (Flow.AiVetoOnMissingData=PASS). The gates and the live re-check had already"
                    + " passed. AI said: " + (ai.Headline ?? "(no headline)"));
            }
            else if (JudgeUtil.MapDecision(cfg.AiVetoNeedsAnswer) == "PASS"
                && ai.Recommendation != "PASS"
                && ai.DataCorrect != false && ai.LogicCorrect != false)
            {
                // 1.0.192.3 -- A "SUPPRESS" WITH NO ANSWER BEHIND IT IS NOT TESTIMONY.
                //
                // THE HOLE. Both caps above are guarded on ai.DataCorrect == false. A model that
                // returns data_correct=null -- it DECLINED to answer -- satisfies neither, because
                // in C# (null == false) is false. The recommendation then kills the alert on its
                // own, through the ai.Recommendation != "PASS" term of aiPass below, and the
                // reason text reads "the evidence does not support the alert" even though the
                // model never said that.
                //
                // 13.10.2026, both TC GJ SHORT replays, identical shape:
                //   1343345  recommendation=SUPPRESS  logic_correct=null  data_correct=null  conf 92
                //            "No telemetry series available to confirm the TC GJ SHORT predictive
                //             alert for 2T1."
                //            caveats: "Telemetry series is empty and thresholds are empty ...
                //             Per P0, missing data never confirms."
                //   1204111  same, "No telemetry series available to confirm the TC GJ SHORT
                //             predictive cause."
                //
                // Both are TC GJ SHORT -- the catalogue's ONLY ExternallyDetermined cause. attrs=0,
                // SustainSec=0, relay/analog/adjacency all SKIPPED, live check skipped by design.
                // The model is handed an empty evidence bundle (0 series / 0 samples) and asked to
                // judge it. Answering null is the honest reply. Suppressing on it is not.
                //
                // The old reason text proved the point by contradicting itself: it ended
                // "...so it was accepted as reported rather than independently verified" while the
                // decision field said SUPPRESSED.
                //
                // THE RULE. An AI veto is testimony only when the AI actually answered one of the
                // two questions with an explicit false. A SUPPRESS recommendation carried by two
                // nulls is an abstention, and an abstention does not outvote the computation.
                //
                // NARROW, and note what this does NOT touch: data_correct=false keeps its veto in
                // full, subject only to the two caps above; logic_correct=false keeps its veto
                // untouched; a PASS recommendation is unaffected. Only the all-null abstention is
                // refused. Set Flow.AiVetoNeedsAnswer=SUPPRESSED to restore 1.0.192.2 behaviour.
                vetoCapped = true;
                vetoGround = "no-answer";
                log.Add("policy: AI recommendation=" + (ai.Recommendation ?? "(null)")
                    + " OVERRULED — the AI answered neither of the two questions (logic_correct="
                    + JudgeUtil.TriText(ai.LogicCorrect) + ", data_correct="
                    + JudgeUtil.TriText(ai.DataCorrect) + "), so there is no finding to weigh"
                    + " against the computation (Flow.AiVetoNeedsAnswer=PASS). AI said: "
                    + (ai.Headline ?? "(no headline)"));
            }

            bool aiPass = ai.Recommendation == "PASS" && logicOk && dataOk && ai.Confidence >= cfg.AiPassMinConfidence;
            if (vetoCapped)
            {
                aiPass = logicOk && dataOk;   // the computation carries it; the AI's PASS/SUPPRESS word does not
            }

            // Name the ONE thing that decided it, so the remark explains the verdict instead of listing fields.
            string verdictWhy = aiPass
                ? "the cause logic on the card is right for this cause and the data really shows it"
                : ai.LogicCorrect == false ? "the cause logic on the card is not the right logic for this cause"
                : ai.DataCorrect == false ? "the data does not actually show the condition the card claims"
                : ai.Recommendation != "PASS" ? "the evidence does not support the alert"
                : "confidence " + ai.Confidence + "% is below the " + cfg.AiPassMinConfidence + "% required to deliver an alert";

            string answer = gatesText + " The AI then checked the two questions that decide it and found that "
                + verdictWhy + " (confidence " + ai.Confidence + "%)."
                + (ai.Headline != null ? " " + ai.Headline.TrimEnd('.') + "." : "");

            // 1.0.190.0 -- when the veto was capped, say so in the reason the operator reads, and
            // name what the AI argued. Hiding an overruled AI opinion would be worse than the bug.
            if (vetoCapped)
            {
                o.Confidence = 0;

                // 1.0.191.0 -- say which ground it actually was. 1.0.190.0 asserted "recovered" for
                // every overrule, which was false on 1263751 (the ground there was late onset).
                string why;
                if (vetoGround == "recovered")
                {
                    why = ", on the ground that the reading recovered after the sustain window — which"
                        + " is what a reset means and is not required by this cause's rule.";
                }
                else if (vetoGround == "onset")
                {
                    why = ", on the ground that the condition was not already true at the first instant"
                        + " of the check window. For a FAILURE that is not the rule: Live.FailureOnset"
                        + " is true, so checkWindow is a window to FIND the condition in, and a"
                        + " condition that arrives inside it and holds to the window end counts as"
                        + " held. That is a configured decision, not a data finding.";
                }
                else if (vetoGround == "no-answer")
                {
                    // 1.0.192.3 -- do not write "the AI argued the data does not show the
                    // condition" here: it did not argue that, or anything else. Say what happened.
                    why = null;
                }
                else
                {
                    why = ". Its objection is recorded above as written; it did not identify a"
                        + " condition failing inside the sustain window, which is what data_correct"
                        + " asks. Review it if this alert is questioned.";
                }

                // 1.0.192.3 -- live can be NULL here and this line used to dereference it
                // unconditionally. It is null whenever the live re-check never ran: an
                // ExternallyDetermined cause (TC GJ SHORT skips it by design), or an early
                // live-stage skip. The missing-data cap above has never required live != null
                // either, so this was a latent NullReferenceException in 1.0.192.0-192.2 that only
                // stayed hidden because that cap is guarded on data_correct == false and the
                // all-null verdicts never reached it. The no-answer cap below reaches it directly.
                string computationClause = live != null
                    ? " The live re-check held " + live.HeldCount + " of " + live.CountedCount
                      + " counted condition" + (live.CountedCount == 1 ? "" : "s")
                      + " through the window and the set and reset cards corroborate each other, so"
                      + " the alert is delivered on the computation."
                    : " The live re-check did not run for this cause, so there is nothing it could"
                      + " have contradicted; the alert is delivered on the card and the catalogue"
                      + " gates, which passed.";

                answer = vetoGround == "no-answer"
                    ? gatesText + " The AI was then asked the two questions that decide it and"
                      + " answered neither — logic_correct=" + JudgeUtil.TriText(ai.LogicCorrect)
                      + ", data_correct=" + JudgeUtil.TriText(ai.DataCorrect)
                      + (ai.Headline != null ? ", saying only \"" + ai.Headline.TrimEnd('.') + "\"" : "")
                      + ". An abstention is not a finding against the alert, so it does not"
                      + " outvote the computation."
                      + computationClause
                      + " Confidence is left at 0 because it is not an AI-backed verdict."
                    : gatesText + " The AI argued the data does not show the condition"
                      + (ai.Headline != null ? " (\"" + ai.Headline.TrimEnd('.') + "\")" : "")
                      + why
                      + computationClause
                      + " Confidence is left at 0 because it is not an AI-backed verdict.";
            }

            // 1.0.190.0 -- mark a marginal average-only drift on delivery (Rules.AvgOnlyMinMarginPct).
            string avgNote = aiPass ? AvgOnlyMargin(card, cfg) : null;
            if (avgNote != null)
            {
                if (o.Confidence > cfg.ConfidenceNotConfirmed)
                {
                    o.Confidence = cfg.ConfidenceNotConfirmed;
                }

                log.Add("policy: marginal average-only drift — " + avgNote);
            }

            return Set(o, aiPass ? "PASS" : "SUPPRESSED", vetoCapped ? "ai-overruled" : "ai",
                answer + unrecognised + noEvidence + external + seen
                + (avgNote != null ? " " + avgNote : ""), log);
        }


        // =============================================================================================
        // 1.0.190.0 -- Rules.AvgOnlyMinMarginPct. The owner's answer on marginal drift: deliver it,
        // but mark it, and keep the level in config.
        //
        // 1023426 (118/119T) fired at 262.08 mA against an 80%-Avg threshold of 269.30 -- 2.7% below --
        // while Min Safe (210) PASSED, so the current was healthy in absolute terms. The alert card
        // says so itself: "(value is okay for Min/Max; alert triggered because of Avg threshold)".
        //
        // It is still delivered: drift below the rolling average is exactly what PREDICTIVE means, and
        // suppressing it would disable a whole class of predictive alerting. The mark exists so a
        // maintainer can see at a glance that this is a 2.7% drift and not a collapse. 0 disables it.
        //
        // Only the AVERAGE threshold may have fired. A condition that also breached a Min/Max safe
        // limit is an absolute breach and is never marked.
        // =============================================================================================

        // =============================================================================================
        // 1.0.191.0 -- WHAT DID THE AI ACTUALLY ARGUE?
        //
        // 1.0.190.0's veto cap delivered the right alerts and then described them wrongly. Its reason
        // text asserted, unconditionally, that
        //
        //     "its only ground was that the reading recovered after the sustain window"
        //
        // On 1263751 that was FALSE. The AI's ground was late ONSET inside the window -- "ITC Relay
        // End was not below MinFail at the set time; breach began 5s into the sustain window" --
        // which is a different argument entirely. The decision was right; the record lied about why.
        // For a reason string a maintainer reads and an audit row that is kept, that is not
        // acceptable, and it was my error.
        //
        // So the ground is now classified from what the AI wrote, and the reason text says which it
        // was. Three outcomes, all still delivered when the computation verified the alert -- missing
        // a real failure is worse than delivering a questioned one -- but each described truthfully:
        //
        //   "recovered"  the reading returned to normal after the window. Inadmissible: every closed
        //                alert recovered, and no cause rule requires persistence to the reset.
        //   "onset"      the condition was not true at the first instant of checkWindow but arrived
        //                inside it. Inadmissible for a FAILURE while Live.FailureOnset=true, which is
        //                a configured decision (1.0.186.0), not a data finding.
        //   null         something else. The objection is RECORDED in the reason verbatim and the
        //                alert is still delivered on the computation, but nothing is claimed about
        //                what the AI meant. Confidence 0 flags it for a human either way.
        // =============================================================================================
        /// <summary>
        /// 1.0.192.0 -- does the AI's own text say it could not SEE the data, as opposed to saying
        /// the data disagrees with the card? Read from the headline, the caveats and the
        /// recommended action -- the same three fields AiVetoGround reads for onset/recovered.
        ///
        /// Deliberately phrase-based rather than clever: the model writes these sentences in a
        /// narrow register because the prompt asks it two fixed questions, and a missing-data
        /// answer always names the absence. A sentence about values being out of range, holding,
        /// recovering or contradicting matches none of these and keeps its veto.
        /// </summary>
        private static readonly string[] MissingDataMarks = new[]
        {
            "no telemetry",
            "telemetry available",
            "telemetry is not",
            "not verifiable",
            "unverifiable",
            "unmapped",
            "not re-checkable",
            "not recheckable",
            "cannot be confirmed",
            "could not be confirmed",
            "cannot be verified",
            "could not be verified",
            "cannot verify",
            "could not verify",
            "no history",
            "no historian",
            "no data available",
            "no live data",
            "insufficient data",
            "insufficient telemetry",
            "not available in live data",
            "no measurement",
            "absent from history",
            "no tag",
            // 1.0.192.1 -- added from the 12.10.2026 14:00 log, which carried six more real
            // missing-data suppressions than the 11:40 one. Three phrasings the 1.0.192.0 list
            // missed, all on TC BT CHG CURR LOW where the derived attid 585 does not exist on the
            // asset at all:
            //   14400 "...attribute does not exist on asset 1T -- telemetry cannot confirm..."
            //   14410 "...does not exist on asset 2T1 -- no analog telemetry to confirm the breach"
            //   14407 "...formula attribute not present on asset 2T -- analog breach cannot be
            //          verified"   (this one the 192.0 list already caught, on "cannot be verified")
            // "no telemetry" did not match "no ANALOG telemetry" -- one word in the middle.
            "does not exist",
            "not existing on",
            "not present on",
            "cannot confirm",
            "telemetry cannot",
            "telemetry to confirm",
            "telemetry is available",
            "no raw analog",
            // DELIBERATELY NOT ADDED: "not on this asset" / "attr not on". Alert 14440 argues the
            // derived value is "a physically impossible charging current ... an instrument/loop
            // artefact" -- a POSITIVE claim about the data, which must keep its veto -- but its
            // caveats also quote the string "attr not on this asset" from attrsAtSet. Adding that
            // phrase would have capped a genuine objection. See the test in ChangesDone 192.1.
        };

        private static bool AiGroundIsMissingData(JudgeAiVerdict ai)
        {
            if (ai == null)
            {
                return false;
            }

            string hay = ((ai.Headline ?? "") + " | " + (ai.Caveats ?? "") + " | "
                        + (ai.RecommendedAction ?? "")).ToLowerInvariant();
            if (hay.Trim().Length <= 4)
            {
                return false;
            }

            for (int i = 0; i < MissingDataMarks.Length; i++)
            {
                if (hay.IndexOf(MissingDataMarks[i], StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string AiVetoGround(JudgeAiVerdict ai)
        {
            string t = ((ai.Headline ?? "") + " " + (ai.Caveats ?? "") + " " + (ai.RecommendedAction ?? ""))
                .ToLowerInvariant();
            if (t.Trim().Length == 0)
            {
                return null;
            }

            // onset first: "not below X at the set time / breach began Ns into the window" is a
            // statement about the window's first instant, and it often also mentions a recovery.
            string[] onsetMarks =
            {
                "at the set time", "at set time", "at the set instant", "not yet true",
                "began 5s into", "into the sustain window", "into the 10s window", "into the window",
                "late onset", "late-developing", "did not hold continuously from the set"
            };
            foreach (string m in onsetMarks)
            {
                if (t.Contains(m)) { return "onset"; }
            }

            string[] recoveredMarks =
            {
                "recovered", "recovery", "returned to normal", "came back", "rose back",
                "before reset", "before the reset", "did not persist", "not sustained",
                "transient", "spike",
                // 1.0.191.0 -- the persistence-through-the-alert's-LIFE phrasings. 1034216's real
                // headline was "TPR relay dropped 28s after set, before the alert reset at 346s; the
                // cause did not hold continuously through the alert window" -- the same inadmissible
                // class as "recovered", and the first pass of this classifier missed all of it.
                // These sit AFTER the onset checks, so "did not hold continuously FROM THE SET" still
                // classifies as onset.
                "did not hold continuously", "through the alert window", "through the alert frame",
                "for the alert's whole", "whole life of the alert", "dropped 28s after set"
            };
            foreach (string m in recoveredMarks)
            {
                if (t.Contains(m)) { return "recovered"; }
            }

            return null;
        }

        private static string AvgOnlyMargin(JudgeCard card, JudgeSettings cfg)
        {
            if (cfg.AvgOnlyMinMarginPct <= 0 || card == null || card.Conditions == null)
            {
                return null;
            }

            foreach (JudgeCardCondition c in card.Conditions)
            {
                if (c == null || c.CardValue == null || c.Thresholds == null || c.Role != "main")
                {
                    continue;
                }

                List<JudgeCardThreshold> fired = c.Thresholds
                    .Where(t => t != null && string.Equals(t.Status, "FAIL", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (fired.Count != 1)
                {
                    continue;
                }

                string label = fired[0].Label ?? "";
                if (label.IndexOf("avg", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;   // an absolute Min/Max breach, not a drift
                }

                double thr = fired[0].Value;
                if (Math.Abs(thr) < 1e-9)
                {
                    continue;
                }

                double marginPct = Math.Abs(c.CardValue.Value - thr) / Math.Abs(thr) * 100.0;
                if (marginPct < cfg.AvgOnlyMinMarginPct)
                {
                    return c.Label + " breached ONLY the rolling-average threshold, by "
                        + JudgeUtil.F(marginPct, "0.0") + "% (" + JudgeUtil.F(c.CardValue, "0.###")
                        + " vs " + JudgeUtil.F(thr, "0.###") + " " + label + "); "
                        + (c.Thresholds.Count > fired.Count
                            ? "the absolute safe limit passed, so the reading is healthy in absolute terms. "
                            : "")
                        + "A drift this small is marked low-confidence (Rules.AvgOnlyMinMarginPct="
                        + cfg.AvgOnlyMinMarginPct + "%), not suppressed.";
                }
            }

            return null;
        }

        private static JudgePolicyOutcome Set(JudgePolicyOutcome o, string final, string by, string reason, List<string> log)
        {
            o.FinalDecision = final;
            o.DecidedBy = by;
            o.Reasons.Add(reason);
            if (o.Decision == "REVIEW")
            {
                o.Decision = final == "PASS" ? "ACCEPT" : "DISCARD";
            }

            log.Add("policy: " + final + " (by " + by + ") — " + reason);
            return o;
        }
    }
}
