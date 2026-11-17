using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace E7.AiCore.Judge
{
    // ============================================================================================
    // JudgeVerifier -- the catalog gates. Port of AlertJudge v1.1.2.0 Pipeline/CatalogVerifier.cs,
    // behaviour for behaviour (Find, Stage2Candidates, MatchCard, Verify, GateOf, EvalAttr,
    // Adjacency, HeldFor, ResolveThreshold). Nothing here changed in 2.0.
    //
    // CHANGE HERE when a catalog gate must behave differently. To change a cause's rule, edit
    // JudgeCatalog.cs instead.
    // ============================================================================================
    internal static class JudgeVerifier
    {
        private static readonly HashSet<int> TrackFailureSeq = new HashSet<int>(Enumerable.Range(17, 10));

        internal static JudgeCauseDef Find(JudgeCard card)
        {
            if (card.CauseId != null)
            {
                string name;
                JudgeCauseDef byId;
                if (JudgeCauseCatalog.DbIdToCause.TryGetValue(card.CauseId.Value, out name) && JudgeCauseCatalog.ByCause.TryGetValue(name, out byId))
                {
                    return byId;
                }
            }

            JudgeCauseDef byName;
            if (!string.IsNullOrWhiteSpace(card.CauseCode) && JudgeCauseCatalog.ByCause.TryGetValue(card.CauseCode, out byName))
            {
                return byName;
            }

            string norm = JudgeUtil.Norm(card.CauseCode);
            return JudgeCauseCatalog.All.FirstOrDefault(d => JudgeUtil.Norm(d.Cause) == norm);
        }

        internal static JudgeCauseDef FindByText(string cause)
        {
            int id;
            if (int.TryParse(cause, out id))
            {
                string name;
                JudgeCauseDef byId;
                if (JudgeCauseCatalog.DbIdToCause.TryGetValue(id, out name) && JudgeCauseCatalog.ByCause.TryGetValue(name, out byId))
                {
                    return byId;
                }
            }

            JudgeCauseDef d;
            if (JudgeCauseCatalog.ByCause.TryGetValue(cause ?? "", out d))
            {
                return d;
            }

            return null;
        }

        internal static List<JudgeCauseDef> Stage2Candidates(JudgeCauseDef def)
        {
            if (def == null || !def.IsFailure || !TrackFailureSeq.Contains(def.Seq))
            {
                return new List<JudgeCauseDef>();
            }

            return JudgeCauseCatalog.All.Where(d => TrackFailureSeq.Contains(d.Seq) && d.Seq != def.Seq && d.Seq != 26).ToList();
        }

        internal static bool IsTrackFailure(JudgeCauseDef def)
        {
            return def != null && def.IsFailure && TrackFailureSeq.Contains(def.Seq);
        }

        // =========================================================================================
        // Step 1 -- card <-> catalog
        // =========================================================================================
        internal static JudgeCardCatalogMatch MatchCard(JudgeCard card, JudgeCauseDef def)
        {
            JudgeCardCatalogMatch m = new JudgeCardCatalogMatch();

            // 2.2: nothing to match when the trigger is decided outside this system. TC GJ SHORT is
            // raised from a boolean "tjShort" flag the RDPMS/EdgeX bridge sends after doing the
            // T1/T2 adjacent-track correlation itself; AlertV3 trusts it directly and never
            // evaluates the SPN rule, so the card carries no condition set to compare. Asserting
            // the SPN rule here produced a guaranteed mismatch and suppressed every one of them.
            if (def.ExternallyDetermined)
            {
                m.Agree.Add(def.Cause + ": trigger determined outside RDPMS (field-device flag) - nothing to re-derive from the card");
                m.Match = true;
                return m;
            }

            foreach (JudgeCauseAttr a in def.Attrs)
            {
                string cardName = CardNameFor(card, a);
                if (cardName == null)
                {
                    m.NotOnCard.Add(a.Name);
                    continue;
                }

                // 2.4 -- "temporal" is a RELAY with a timing constraint (NWCR UP for the whole
                // operation or 12 s max; NWKR DN within 2 s). MatchCard had no branch for it at
                // all, so NWCR and NWKR contributed nothing -- not Agree, not Disagree, not even
                // NotOnCard. They were silently dropped, which is how a card the matcher HAD found
                // NWCR on still ended with zero agreements. The state is matched here like any
                // relay; the timing itself is a gate matter, not a card/catalogue question.
                if (a.Class == "relay" || a.Class == "temporal")
                {
                    int bit;
                    if (!card.Relays.TryGetValue(cardName, out bit))
                    {
                        m.NotOnCard.Add(a.Name);
                        continue;
                    }

                    int want = WantBit(a.State);
                    if (want < 0)
                    {
                        m.Agree.Add(a.Name + ": state '" + a.State + "' (not checked on card)");
                        continue;
                    }

                    if (bit == want)
                    {
                        m.Agree.Add(a.Name + ": card " + (bit == 1 ? "UP" : "DN") + " = catalog " + a.State);
                    }
                    else if (a.Class == "temporal")
                    {
                        // 1.0.208.0 -- A POINT OPERATION RELAY IS A SEQUENCE, NOT A STATE. The SPN
                        // has two kinds of point failure: WITH operation (NWCR/RWCR UP during the
                        // operation, then the KR down) and WITHOUT. The card prints one instant, and
                        // for the OP causes AlertV3 prints the non-OP relay rows, so the card can show
                        // CR DN where the rule needs CR UP. That is not a contradiction: whether the
                        // CR was UP is read from history around the alert time (EvalAttr, temporal).
                        m.Agree.Add(a.Name + ": card " + (bit == 1 ? "UP" : "DN") + ", catalog " + a.State
                            + " -- operation relay, checked from history around the alert time instead of the card");
                    }
                    else
                    {
                        m.Disagree.Add(a.Name + ": card " + (bit == 1 ? "UP" : "DN") + " ≠ catalog " + a.State);
                    }
                }
                else if (a.Class == "analog" || a.Class == "ips")
                {
                    // 1.0.192.0 -- A CONDITION THE CARD MARKED PASSING DID NOT FIRE, SO IT CANNOT
                    // CONTRADICT THE RULE.
                    //
                    // AlertCardJsonBuilder.Thr() prints the comparison that is LITERALLY TRUE of
                    // the reading it just took, not the rule's operator:
                    //     status     = fail ? "FAIL" : "PASS";
                    //     comparison = fail ? (isLower ? "<" : ">") : (isLower ? "≥" : "≤");
                    // so a PASSING threshold states the INVERSE of its own rule. Correct for the
                    // operator card; not a statement about the rule.
                    //
                    // It bites hardest on OR rules, where one member passing is the NORMAL case.
                    // SIG RG VOLT/CURR LOW is (VSIG RG < eff) OR (ISIG RG < eff), both carrying
                    // Gate "RECR / OR-G1":
                    //   12.10.2026, alert 14377:
                    //     agree    : VSIG RG: card '<' = catalog '<'        <- the member that FAILED
                    //     disagree : ISIG RG: card [≥] != catalog '<'       <- the member that PASSED
                    //   -> SUPPRESSED / card-match, before any history was fetched. A real signal-
                    //      current fault deleted because the VOLTAGE was healthy. Identical shape on
                    //      14378, and on 14373/74/75/76/80 and 14382 (SHSIG OFF ASPECT, ISHSIG OFF /
                    //      ISHSIG PILOT, OR-G1). Eight of the sixteen real Mode-3 suppressions.
                    //
                    // NARROW, and it does NOT touch the 2.1 not-zero work below: CardConditionPassing
                    // fires only when EVERY threshold on the attribute is marked PASS. TC SHORT's
                    // ITC Relay End carries "MinFail 100 '<' FAIL" AND "NearZero 4 '>=' PASS", so it
                    // has a FAIL, returns false here, and both catalogue terms are matched from the
                    // full comparison list exactly as 2.1 intended. CardComparisons is unchanged.
                    if (CardConditionPassing(card, cardName))
                    {
                        m.Agree.Add(a.Name + ": the card marks every threshold on it PASS, so this is not the"
                                  + " condition the alert fired on -- a healthy member of the rule, not a contradiction");
                        continue;
                    }

                    List<string> cmps = CardComparisons(card, cardName);
                    if (cmps.Count == 0)
                    {
                        m.Agree.Add(a.Name + ": value on card, no comparison stated");
                        continue;
                    }

                    string used = PickDirection(cmps, a.Cmp);
                    if (used != null)
                    {
                        m.Agree.Add(a.Name + ": card '" + used + "' = catalog '" + a.Cmp + "'"
                                  + (IsNotZeroRef(a) ? " (not-zero guard)" : ""));
                    }
                    else if (IsNotZeroRef(a))
                    {
                        // 1.0.192.0 -- "0+" means NOT ZERO (an SPN remark), and 2.1 made it a real
                        // check for the cards that DO carry it as a second threshold ("NearZero 4.00
                        // '>=' PASS"). When a card does not carry that row at all, the guard has
                        // nothing to line up against and the old code called that a contradiction:
                        //   12.10.2026, alerts 14367 and 14370 TC SHORT -- the card had only the
                        //   MinFail row, so the SAME attribute landed in both lists at once:
                        //     agree    : ITC Relay End: card '<' = catalog '<'     <- the real rule
                        //     disagree : ITC Relay End: card [<] != catalog '>'    <- the 0+ guard
                        //   -> SUPPRESSED / card-match. The guard is verified from history by the
                        //      gates; its absence from the card is a gap in the card, not a dispute.
                        // The 2.1 agreement above is untouched: this branch is only reached when
                        // PickDirection found nothing.
                        m.Agree.Add(a.Name + ": catalogue's '" + a.Cmp + " 0+' not-zero guard is not stated on this"
                                  + " card (card says [" + string.Join(", ", cmps) + "]) -- verified from history by the"
                                  + " gates, not a contradiction");
                    }
                    else
                    {
                        m.Disagree.Add(a.Name + ": card [" + string.Join(", ", cmps) + "] ≠ catalog '" + a.Cmp + "'");
                    }
                }
                else if (a.Class == "derived")
                {
                    // 1.0.192.0 -- same rule as the analog branch above.
                    if (CardConditionPassing(card, cardName))
                    {
                        m.Agree.Add(a.Name + ": derived, the card marks every threshold on it PASS -- a gate or"
                                  + " variant discriminator, not the condition the alert fired on");
                        continue;
                    }

                    List<string> cmps = CardComparisons(card, cardName);
                    if (cmps.Count > 0)
                    {
                        string used = PickDirection(cmps, a.Cmp);
                        if (used != null)
                        {
                            m.Agree.Add(a.Name + ": derived, card '" + used + "' = catalog '" + a.Cmp + "'");
                        }
                        else
                        {
                            m.Disagree.Add(a.Name + ": derived, card [" + string.Join(", ", cmps) + "] ≠ catalog '" + a.Cmp + "'");
                        }
                    }
                    else
                    {
                        m.NotOnCard.Add(a.Name);
                    }
                }
            }

            // 2.4 -- THREE outcomes, not two. "Nothing on the card could be lined up against the
            // catalogue" is a gap in OUR naming, and it is not the same finding as "the card and
            // the catalogue contradict each other". Collapsing them into Match=false produced the
            // verdict on alert 1280129: SUPPRESSED / card-match with the remark
            // "...the two cannot both be right: ." -- an accusation with nothing after the colon.
            m.Match = m.Disagree.Count == 0 && m.Agree.Count > 0;
            m.NothingRecognised = m.Disagree.Count == 0 && m.Agree.Count == 0;
            if (m.NothingRecognised)
            {
                m.Agree.Add("none of the " + def.Attrs.Length + " catalogue attributes could be matched to a card condition"
                    + (m.NotOnCard.Count > 0 ? " (" + string.Join(", ", m.NotOnCard) + ")" : "")
                    + " -- a naming gap on our side, not a contradiction");
            }

            return m;
        }

        /// <summary>
        /// 1.0.192.3 -- stamps the timing facts onto an outcome built by one of JudgeEngine's three
        /// short-circuit branches (rules mode, card/catalogue mismatch, prior human discard). None
        /// of those reaches Verify(), so before 1.0.192.3 each shipped the field initialisers --
        /// alertState "SET_ONLY", evalMode "", sustainSec 0 -- regardless of what the card said.
        ///
        /// On the 13.10.2026 log that made all nine card-match records claim the alert was still
        /// open while carrying a non-null resetTime; made five TC TPR I/P VOLT LOW records report
        /// sustainSec 0 when the catalogue says 15; and made alertState useless for separating real
        /// Mode-3 gate calls from replays, which has to be done on resetTime instead.
        ///
        /// This mirrors Verify()'s own lines verbatim so the two can never drift. It DESCRIBES the
        /// case; it decides nothing and cannot change any verdict.
        /// </summary>
        internal static void StampTiming(JudgeVerifierOutcome o, JudgeCard card, JudgeCauseDef def)
        {
            if (o == null || card == null)
            {
                return;
            }

            o.AlertState = card.ResetAt == null ? "SET_ONLY" : "SET_RESET";
            o.SustainSec = def != null ? def.SustainSec : 0;
            int sustain = o.AlertState == "SET_RESET" ? o.SustainSec : 0;
            o.EvalMode = sustain > 0 ? "sustain[set, set+" + sustain + "s]" : "instant@set";
        }

        // =========================================================================================
        // Steps 2-3 -- history gates
        // =========================================================================================
        internal static JudgeVerifierOutcome Verify(JudgeCard card, JudgeCauseDef def, JudgeEvidenceBundle ev, List<JudgeCauseDef> stage2, List<string> log, JudgeSettings cfg)
        {
            JudgeVerifierOutcome o = new JudgeVerifierOutcome();
            o.ExternallyDetermined = def != null && def.ExternallyDetermined;   // 2.2
            o.CauseCode = card.CauseCode;
            o.WindowFrom = ev.From;
            o.WindowTo = ev.To;
            o.SetTime = card.TriggeredAt;
            o.ResetTime = card.ResetAt;
            if (def == null)
            {
                o.Verdict = "NO_RULE";
                o.FlowResult = "NO_DATA";
                return o;
            }

            o.Seq = def.Seq;
            o.CauseCode = def.Cause;
            o.AlertType = def.AlertType;
            o.SustainSec = def.SustainSec;
            o.RuleRaw = def.RuleRaw;
            o.VerdictLogic = def.VerdictLogic;

            o.AlertState = card.ResetAt == null ? "SET_ONLY" : "SET_RESET";
            int sustain = o.AlertState == "SET_RESET" ? def.SustainSec : 0;
            o.EvalMode = sustain > 0 ? "sustain[set, set+" + sustain + "s]" : "instant@set";
            if (o.AlertState == "SET_RESET" && def.SustainSec > 0 && card.ResetAt.Value < card.TriggeredAt.AddSeconds(def.SustainSec))
            {
                o.ResetBeforeSustain = true;
                log.Add("verify: SET_RESET — reset " + JudgeUtil.F((card.ResetAt.Value - card.TriggeredAt).TotalSeconds, "0") + "s after set, before SustainSec=" + def.SustainSec + " → transient");
            }

            log.Add("verify: case " + o.AlertState + ", eval " + o.EvalMode + ", set " + JudgeUtil.T(card.TriggeredAt, "HH:mm:ss") + (card.ResetAt != null ? " reset " + JudgeUtil.T(card.ResetAt, "HH:mm:ss") : ""));

            List<JudgeAttrResult> results = def.Attrs.Select(a => EvalAttr(a, card, ev, sustain, cfg.PointOpWindowSec)).ToList();
            List<JudgeAttrResult> gating = results.Where(r => (r.Status == "HELD" || r.Status == "LAPSED") && r.HeldForSec != null).ToList();
            o.HeldForSec = gating.Count > 0 ? gating.Min(r => r.HeldForSec) : null;
            o.Attrs = results;
            o.Fired = results.Where(r => r.Status == "HELD").Select(r => r.Name).ToList();

            // 1.0.208.0 -- point operation relays (class "temporal") count as relays. Before, a point
            // cause had NO relay in this gate, so the gate was SKIPPED and every point FAILURE
            // ended NO_DATA whatever the data said.
            List<JudgeAttrResult> relays = results.Where(r => r.Class == "relay" || (r.Class == "temporal" && r.Status != "UNSUPPORTED")).ToList();
            o.Relay = GateOf("DataLogger relays", relays);

            List<JudgeAttrResult> analogs = results.Where(r => r.Class == "analog" || r.Class == "ips").ToList();
            o.Analog = GateOf("RDPMS analog", analogs);

            foreach (JudgeAttrResult r in results.Where(r => r.Status == "UNSUPPORTED"))
            {
                o.CoverageNotes.Add(r.Name + ": " + r.Detail);
            }

            if (IsTrackFailure(def))
            {
                o.Adjacency = Adjacency(card, ev, log, cfg);
            }
            else
            {
                o.Adjacency = new JudgeAdjacency();
                o.Adjacency.Result = "SKIPPED";
                o.Adjacency.Detail = "not a track failure cause";
            }

            string flow;
            if (def.IsFailure)
            {
                flow = o.Relay.Result == "PASS" ? "PROCEED" : o.Relay.Result == "FAIL" ? "SUPPRESSED" : "NO_DATA";
                if (flow == "SUPPRESSED" && stage2.Count > 0)
                {
                    foreach (JudgeCauseDef other in stage2)
                    {
                        List<JudgeAttrResult> res2 = other.Attrs.Select(a => EvalAttr(a, card, ev, o.AlertState == "SET_RESET" ? other.SustainSec : 0, cfg.PointOpWindowSec)).ToList();
                        JudgeGate g2 = GateOf("relay", res2.Where(r => r.Class == "relay" || (r.Class == "temporal" && r.Status != "UNSUPPORTED")).ToList());
                        if (g2.Result == "PASS")
                        {
                            flow = "PROCEED";
                            o.ReclassifiedCause = other.Cause;
                            log.Add("verify: reclassified → #" + other.Seq + " " + other.Cause);
                            break;
                        }
                    }
                }
            }
            else
            {
                bool relayYes = o.Relay.Result == "PASS" || o.Relay.Result == "SKIPPED";
                bool analogYes = o.Analog.Result == "PASS" || o.Analog.Result == "SKIPPED";
                bool anyNoData = o.Relay.Result == "NO_DATA" || o.Analog.Result == "NO_DATA";
                flow = relayYes && analogYes ? "PROCEED" : anyNoData && o.Relay.Result != "FAIL" && o.Analog.Result != "FAIL" ? "NO_DATA" : "SUPPRESSED";
            }

            if (flow == "PROCEED" && o.Adjacency.Result == "FAIL")
            {
                flow = "SUPPRESSED";
            }

            if (flow == "PROCEED" && o.ResetBeforeSustain)
            {
                flow = "SUPPRESSED";
            }

            o.FlowResult = flow;
            o.Verdict = flow == "PROCEED" ? "CONFIRMED" : "NOT_CONFIRMED";
            o.Coverage = flow == "NO_DATA";
            o.Confidence = flow == "PROCEED" ? cfg.ConfidenceConfirmed : flow == "SUPPRESSED" ? cfg.ConfidenceNotConfirmed : 0;
            log.Add("verify: #" + def.Seq + " " + def.Cause + " " + def.AlertType + ": relay=" + o.Relay.Result + " analog=" + o.Analog.Result + " adjacency=" + o.Adjacency.Result + " → " + flow);
            return o;
        }

        private static JudgeGate GateOf(string name, List<JudgeAttrResult> rs)
        {
            JudgeGate g = new JudgeGate();
            g.Name = name;
            List<JudgeAttrResult> considered = rs.Where(r => r.Status != "SKIPPED").ToList();
            if (rs.Count == 0)
            {
                g.Result = "SKIPPED";
                g.Detail = "no attrs of this class in the cause";
                return g;
            }

            if (considered.Count == 0)
            {
                g.Result = "SKIPPED";
                g.Detail = "none of these attrs exist on this asset";
                return g;
            }

            var groups = considered.GroupBy(r => OrGroup(r.Gate) ?? "__and__" + r.Name).ToList();
            bool anyFail = false;
            bool anyNoData = false;
            List<string> parts = new List<string>();
            foreach (var grp in groups)
            {
                bool held = grp.Any(r => r.Status == "HELD");
                bool lapsed = grp.All(r => r.Status == "LAPSED" || r.Status == "NO_DATA") && grp.Any(r => r.Status == "LAPSED");
                bool nodata = grp.All(r => r.Status == "NO_DATA" || r.Status == "UNSUPPORTED");
                string names = string.Join("|", grp.Select(r => r.Name));
                if (held)
                {
                    parts.Add(names + ":HELD");
                }
                else if (nodata)
                {
                    anyNoData = true;
                    parts.Add(names + ":NO_DATA");
                }
                else if (lapsed)
                {
                    anyFail = true;
                    parts.Add(names + ":LAPSED");
                }
                else
                {
                    anyNoData = true;
                    parts.Add(names + ":?");
                }
            }

            g.Result = anyFail ? "FAIL" : anyNoData ? "NO_DATA" : "PASS";
            g.Detail = string.Join("; ", parts);
            return g;
        }

        // =========================================================================================
        // One attr over the frame, step-function semantics
        // =========================================================================================
        private static JudgeAttrResult EvalAttr(JudgeCauseAttr a, JudgeCard card, JudgeEvidenceBundle ev, int sustainSec, int opWindowSec = 15)
        {
            JudgeAttrResult r = new JudgeAttrResult();
            r.Name = a.Name;
            r.Attid = a.Attid;
            r.Class = a.Class;
            r.Gate = string.IsNullOrEmpty(a.Gate) ? null : a.Gate;
            r.ThresholdFormula = a.EffectiveThreshold;
            if (a.Class == "derived" || a.Attid == 0)
            {
                r.Status = "UNSUPPORTED";
                r.Detail = "derived attr — not evaluated in v1";
                return r;
            }

            // 1.0.208.0 -- a temporal RELAY (point NWCR/RWCR/NWKR/RWKR) is now checked from history
            // around the alert time, further down. Anything else with a TIMING rule stays unsupported.
            bool temporalRelay = a.Class == "temporal" && WantBit(a.State) >= 0;
            if (!temporalRelay && (a.Class == "temporal" || (a.EffectiveThreshold ?? "").StartsWith("TIMING", StringComparison.OrdinalIgnoreCase)))
            {
                r.Status = "UNSUPPORTED";
                r.Detail = "point-op timing — needs role-p PM ops";
                return r;
            }

            bool present;
            if (ev.TagPresent.TryGetValue(a.Attid, out present) && !present)
            {
                r.Status = "SKIPPED";
                r.Detail = "attr not on this asset";
                return r;
            }

            JudgeStepSeries s;
            if (!ev.Steps.TryGetValue(a.Attid, out s))
            {
                r.Status = "NO_DATA";
                r.Detail = "not fetched";
                return r;
            }

            r.TagId = s.TagId;
            r.Samples = s.Transitions.Count + (s.StateAtStart != null ? 1 : 0);
            r.StateAtSet = s.StateAtStart;
            r.StateAtSetTs = s.StateAtStartTs;
            r.StateSource = s.StateSource;
            r.TransitionsInFrame = s.Transitions.Count;
            if (s.StateAtStart == null && s.Transitions.Count == 0)
            {
                r.Status = "NO_DATA";
                r.Detail = "no value before or inside the frame (history or card)";
                return r;
            }

            if (temporalRelay)
            {
                // The relay must have been in the required state at SOME moment inside
                // [alert time - window, alert time + window]. The history gives the state at the
                // alert time with the time of its last change, plus every change after it. A relay
                // has two states, so a last change INSIDE the window means it was in the other
                // state just before that change.
                int w = Math.Max(0, opWindowSec);
                DateTimeOffset lo = ev.From.AddSeconds(-w);
                DateTimeOffset hi = ev.From.AddSeconds(w);
                bool sawUp = false;
                bool sawDn = false;
                if (s.StateAtStart != null)
                {
                    bool up = s.StateAtStart.Value >= 0.5;
                    sawUp = up;
                    sawDn = !up;
                    if (s.StateSource == "history" && s.StateAtStartTs != null && s.StateAtStartTs.Value >= lo)
                    {
                        sawUp = true;
                        sawDn = true;
                    }
                }

                foreach (JudgeSample t in s.Transitions)
                {
                    if (t.Ts > hi)
                    {
                        continue;
                    }

                    if (t.Value >= 0.5)
                    {
                        sawUp = true;
                    }
                    else
                    {
                        sawDn = true;
                    }
                }

                string want = (a.State ?? "").ToUpperInvariant();
                bool okT = want == "UP" ? sawUp : want == "UP->DN" ? (sawUp && sawDn) : sawDn;
                r.Observed = s.Last ?? s.StateAtStart;
                r.Status = okT ? "HELD" : "LAPSED";
                r.Detail = a.Name + " " + a.State + " within +/-" + w + "s of the alert time: at set=" + JudgeUtil.R(s.StateAtStart)
                    + " (" + s.StateSource + ", last change " + JudgeUtil.T(s.StateAtStartTs, "HH:mm:ss") + "), changes after set="
                    + s.Transitions.Count + ", seen UP=" + (sawUp ? "yes" : "no") + " DN=" + (sawDn ? "yes" : "no")
                    + " → " + (okT ? "found" : "NOT found");
                return r;
            }

            DateTimeOffset from = ev.From;
            DateTimeOffset to = sustainSec > 0 ? ev.From.AddSeconds(sustainSec) : ev.From;
            if (to > ev.To)
            {
                to = ev.To;
            }

            List<JudgeSample> segs = s.Segments(from, to);
            if (segs.Count == 0)
            {
                r.Status = "NO_DATA";
                r.Detail = "no state at set time";
                return r;
            }

            string through = sustainSec > 0 ? "through " + sustainSec + "s" : "at set";
            if (a.Class == "relay")
            {
                string st = (a.State ?? "").ToUpperInvariant();
                bool ok;
                switch (st)
                {
                    case "UP":
                        ok = segs.All(x => x.Value >= 0.5);
                        break;
                    case "DN":
                    case "NOT UP":
                        ok = segs.All(x => x.Value < 0.5);
                        break;
                    case "UP->DN":
                        ok = (s.StateAtStart >= 0.5 || s.Transitions.Any(x => x.Value >= 0.5 && x.Ts <= to)) && segs.Any(x => x.Value < 0.5) && segs[segs.Count - 1].Value < 0.5;
                        break;
                    default:
                        ok = false;
                        break;
                }

                if ((a.EffectiveThreshold ?? "").Equals("EXCLUSION", StringComparison.OrdinalIgnoreCase))
                {
                    ok = !ok;
                }

                Func<double, bool> relayOk = v =>
                {
                    if (st == "UP")
                    {
                        return v >= 0.5;
                    }

                    if (st == "DN" || st == "NOT UP" || st == "UP->DN")
                    {
                        return v < 0.5;
                    }

                    return false;
                };
                r.HeldForSec = HeldFor(s, from, ev.To, relayOk);
                r.Observed = segs[segs.Count - 1].Value;
                r.Status = ok ? "HELD" : "LAPSED";
                r.Detail = a.Name + " " + a.State + ": at set=" + JudgeUtil.R(s.StateAtStart) + " (" + s.StateSource + ", last change " + JudgeUtil.T(s.StateAtStartTs, "HH:mm:ss") + ") → " + (ok ? "held" : "lapsed") + " " + through + "; stayed true for " + JudgeUtil.F(r.HeldForSec, "0") + "s; transitions in frame=" + s.Transitions.Count + "; now=" + JudgeUtil.R(s.Last);
                return r;
            }

            string note;
            double? thr = ResolveThreshold(a, card, ev, out note);
            r.Threshold = thr;
            if (thr == null)
            {
                r.Status = "NO_DATA";
                r.Detail = note;
                return r;
            }

            bool held = segs.All(x => JudgeUtil.Cmp(a.Cmp, x.Value, thr.Value));
            r.HeldForSec = HeldFor(s, from, ev.To, v => JudgeUtil.Cmp(a.Cmp, v, thr.Value));
            r.Observed = (a.Cmp == "<" || a.Cmp == "<=") ? segs.Max(x => x.Value) : segs.Min(x => x.Value);
            r.Status = held ? "HELD" : "LAPSED";
            r.Detail = a.Name + " " + a.Cmp + " " + JudgeUtil.F(thr, "0.###") + " (" + note + "): at set=" + JudgeUtil.F(s.StateAtStart, "0.###") + " (" + s.StateSource + ", " + JudgeUtil.T(s.StateAtStartTs, "HH:mm:ss") + ") → " + (held ? "held" : "lapsed") + " " + through + "; stayed true for " + JudgeUtil.F(r.HeldForSec, "0") + "s; worst=" + JudgeUtil.F(r.Observed, "0.###") + "; now=" + JudgeUtil.F(s.Last, "0.###");
            return r;
        }

        // =========================================================================================
        // Adjacency: main track DN at alert, family tracks UP at alert; family drop within AdjacencyWindowSec -> train
        // =========================================================================================
        private static JudgeAdjacency Adjacency(JudgeCard card, JudgeEvidenceBundle ev, List<string> log, JudgeSettings cfg)
        {
            JudgeAdjacency ar = new JudgeAdjacency();
            if (ev.AdjacentTpr.Count == 0)
            {
                ar.Result = "SKIPPED";
                ar.Detail = "no family tracks supplied / resolved";
                return ar;
            }

            DateTimeOffset t1 = ev.From;
            DateTimeOffset winEnd = t1.AddSeconds(cfg.AdjacencyWindowSec);
            bool anyFail = false;
            foreach (KeyValuePair<int, JudgeStepSeries> kv in ev.AdjacentTpr)
            {
                int adjId = kv.Key;
                JudgeStepSeries step = kv.Value;
                ar.FamilyTrackIds.Add(adjId);
                double? atT1 = step.At(t1);
                if (atT1 == null)
                {
                    ar.Findings.Add("track " + adjId + ": TPR unknown at alert time");
                    continue;
                }

                if (atT1 < 0.5)
                {
                    anyFail = true;
                    ar.Findings.Add("track " + adjId + ": TPR already DN at alert time → not an isolated drop");
                    continue;
                }

                JudgeSample drop = step.Transitions.FirstOrDefault(x => x.Ts > t1 && x.Ts <= winEnd && x.Value < 0.5);
                if (drop.Ts != default(DateTimeOffset))
                {
                    anyFail = true;
                    ar.Findings.Add("track " + adjId + ": TPR dropped " + JudgeUtil.F((drop.Ts - t1).TotalSeconds, "0") + "s after the alert → train movement (shunt)");
                }
                else
                {
                    ar.Findings.Add("track " + adjId + ": TPR UP at alert and stayed UP for " + cfg.AdjacencyWindowSec + "s");
                }
            }

            ar.Result = anyFail ? "FAIL" : "PASS";
            ar.Detail = anyFail ? "adjacent track occupied — sequential drops = train" : "only the main track dropped";
            log.Add("verify: adjacency " + ar.Result + " — " + string.Join("; ", ar.Findings));
            return ar;
        }

        /// <summary>Seconds from `from` the predicate stayed true: until the first transition that breaks it, else until `end`.</summary>
        internal static double? HeldFor(JudgeStepSeries s, DateTimeOffset from, DateTimeOffset end, Func<double, bool> ok)
        {
            double? v0 = s.At(from);
            if (v0 == null || !ok(v0.Value))
            {
                return 0;
            }

            foreach (JudgeSample t in s.Transitions.Where(t => t.Ts > from).OrderBy(t => t.Ts))
            {
                if (!ok(t.Value))
                {
                    return (t.Ts - from).TotalSeconds;
                }
            }

            return (end - from).TotalSeconds;
        }

        // =========================================================================================
        /// <summary>
        /// 2.4 -- a name reduced for comparison: upper-cased, every space removed, and a leading
        /// single-letter END DESIGNATOR ("A ", "B ") dropped. "B VPT 110 DC LOC N " and
        /// "VPT 110DC Loc N" both become "VPT110DCLOCN". No real attribute name is one letter long,
        /// so dropping a lone leading letter cannot collide with a genuine name.
        /// </summary>
        internal static string Tight(string name)
        {
            string n = JudgeUtil.Norm(JudgeUtil.StripUnit(name ?? ""));
            if (n.Length > 2 && (n[0] == 'A' || n[0] == 'B') && n[1] == ' ')
            {
                n = n.Substring(2);
            }

            return n.Replace(" ", string.Empty);
        }

        internal static string CardNameFor(JudgeCard card, JudgeCauseAttr a)
        {
            if (a.Attid > 0)
            {
                string byId = card.AttributeIds.Where(kv => kv.Value == a.Attid).Select(kv => kv.Key)
                    .FirstOrDefault(n => a.Class == "relay" ? card.Relays.ContainsKey(n) : card.Values.ContainsKey(n));
                if (byId != null)
                {
                    return byId;
                }
            }

            string norm = JudgeUtil.Norm(a.Name);
            foreach (string k in card.Values.Keys.Concat(card.Relays.Keys).Concat(card.Comparisons.Keys))
            {
                if (JudgeUtil.Norm(k) == norm || JudgeUtil.Norm(JudgeUtil.StripUnit(k)) == norm)
                {
                    return k;
                }
            }

            // 2.4 -- POINT MACHINE per-end labels. A PM card names its attributes with the end
            // designator and its own spacing: "B VPT 110 DC LOC N " where the catalogue says
            // "VPT 110DC Loc N", and "A TPT N" / "B IPT N" for "TPT N" / "IPT N". Exact-name
            // matching missed every one of them, so on alert 1280129 the matcher recognised nothing
            // and reported a mismatch it could not name. Compare with spaces removed and a leading
            // single-letter end designator dropped. Tried after exact matching, so nothing that
            // already matched can change.
            string tight = Tight(a.Name);
            if (tight.Length > 1)
            {
                foreach (string k in card.Values.Keys.Concat(card.Relays.Keys).Concat(card.Comparisons.Keys))
                {
                    if (Tight(k) == tight || Tight(JudgeUtil.StripUnit(k)) == tight)
                    {
                        return k;
                    }
                }
            }

            string alias = null;
            switch (norm)
            {
                case "ITC RELAY END":
                    alias = "IR MA";
                    break;
                case "ITC FEED END":
                    alias = "IF MA";
                    break;
                case "VTC RELAY END":
                    alias = "VR";
                    break;
                case "VTC FEED END":
                    alias = "VF";
                    break;
                case "IBALST":
                    alias = "IBALST";
                    break;
            }

            if (alias != null)
            {
                foreach (string k in card.Values.Keys)
                {
                    if (JudgeUtil.Norm(k) == alias)
                    {
                        return k;
                    }
                }
            }

            return null;
        }

        private static int WantBit(string state)
        {
            switch ((state ?? "").ToUpperInvariant())
            {
                case "UP":
                    return 1;
                case "DN":
                case "NOT UP":
                case "UP->DN":
                    return 0;
                default:
                    return -1;
            }
        }

        private static bool SameDirection(string cardCmp, string catCmp)
        {
            return JudgeUtil.Direction(cardCmp) == JudgeUtil.Direction(catCmp);
        }


        // =========================================================================================
        // 2.1 -- RDSO/SPN/257/2025 v2.0 lists the SAME parameter twice in several logics. TC SHORT
        // (p.118), TC GJ SHORT (p.117) and TC RAIL RES OPEN (p.120) all read
        //     I(TC RELAY END) < Min Fail value  &&  I(TC RELAY END) > 0+
        // and the catalogue reproduces that faithfully -- it is NOT a duplicate entry to delete.
        // The two terms do different jobs: "< MinFail" says the current dropped, "> 0+" says it did
        // NOT drop to zero, which is what separates a SHORT from an open circuit (TC CKT OPEN, p.120,
        // is the mirror image: I < 0+). Delete the second term and TC SHORT swallows every cable cut.
        //
        // The alert card carries both, but as ONE condition with TWO thresholds:
        //     ITC RELAY END(mA): MinFail 100.00 '<' FAIL ; NearZero 4.00 '>=' PASS
        // and the SPN explicitly allows that 4.00 -- "A small positive value can be used in place of
        // 0+ to confirm that current is not zero."
        //
        // The defect was here: card.Comparisons holds ONE comparison per attribute NAME, so the
        // card's second threshold was invisible and the catalogue's second entry could never agree.
        // Every TC SHORT / TC GJ SHORT / TC RAIL RES OPEN was SUPPRESSED on card-match in ~4 ms,
        // before any data was fetched. Now every comparison the card states for an attribute counts:
        // the flattened one AND each threshold on the matching condition.
        //
        // This also makes the not-zero guard a REAL check for the first time. Before, it either
        // disagreed (same attribute) or landed silently in NotOnCard (different attribute, e.g.
        // "ITC Feed End > 0+" in TC CH RES OPEN), so it was never verified on any cause.
        // =========================================================================================

        /// <summary>Every comparison the card states for one attribute: the flattened one plus each threshold.</summary>
        private static List<string> CardComparisons(JudgeCard card, string cardName)
        {
            List<string> outp = new List<string>();
            string cmp;
            if (card.Comparisons.TryGetValue(cardName, out cmp) && !string.IsNullOrWhiteSpace(cmp))
            {
                outp.Add(cmp.Trim());
            }

            string norm = JudgeUtil.Norm(JudgeUtil.StripUnit(cardName));
            for (int i = 0; i < card.Conditions.Count; i++)
            {
                JudgeCardCondition c = card.Conditions[i];
                string cn = JudgeUtil.Norm(JudgeUtil.StripUnit(string.IsNullOrEmpty(c.Name) ? c.Label : c.Name));
                if (cn != norm)
                {
                    continue;
                }

                for (int t = 0; t < c.Thresholds.Count; t++)
                {
                    string tc = c.Thresholds[t].Comparison;
                    if (string.IsNullOrWhiteSpace(tc))
                    {
                        continue;
                    }

                    tc = tc.Trim();
                    if (!outp.Contains(tc))
                    {
                        outp.Add(tc);
                    }
                }
            }

            return outp;
        }

        /// <summary>
        /// 1.0.192.0 -- true when the card states PASS/FAIL marks for this attribute and NONE of
        /// them is FAIL: the attribute is healthy, and is not the condition the alert fired on.
        ///
        /// BOTH halves are required. Without the "states marks at all" half, a card carrying no
        /// statuses would read as entirely passing and the whole card/catalogue match would switch
        /// itself off silently -- so that case returns false and is matched exactly as in
        /// 1.0.191.0. An attribute with even one FAIL also returns false, which is what keeps the
        /// 2.1 two-threshold work (TC SHORT / TC GJ SHORT / TC RAIL RES OPEN) intact.
        /// </summary>
        private static bool CardConditionPassing(JudgeCard card, string cardName)
        {
            string norm = JudgeUtil.Norm(JudgeUtil.StripUnit(cardName));
            bool anyStatus = false;

            for (int i = 0; i < card.Conditions.Count; i++)
            {
                JudgeCardCondition c = card.Conditions[i];
                string cn = JudgeUtil.Norm(JudgeUtil.StripUnit(string.IsNullOrEmpty(c.Name) ? c.Label : c.Name));
                if (cn != norm)
                {
                    continue;
                }

                for (int t = 0; t < c.Thresholds.Count; t++)
                {
                    string st = (c.Thresholds[t].Status ?? "").Trim();
                    if (st.Length == 0)
                    {
                        continue;
                    }

                    anyStatus = true;
                    if (string.Equals(st, "FAIL", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
            }

            return anyStatus;
        }

        /// <summary>The first card comparison pointing the same way as the catalogue's, or null.</summary>
        private static string PickDirection(List<string> cardCmps, string catCmp)
        {
            for (int i = 0; i < cardCmps.Count; i++)
            {
                if (SameDirection(cardCmps[i], catCmp))
                {
                    return cardCmps[i];
                }
            }

            return null;
        }

        /// <summary>A "0+" threshold reference means NOT ZERO, not a numeric comparison (SPN remark).</summary>
        private static bool IsNotZeroRef(JudgeCauseAttr a)
        {
            string r = (a.ThresholdRef ?? "") + " " + (a.EffectiveThreshold ?? "");
            return r.IndexOf("0+", StringComparison.Ordinal) >= 0;
        }

        private static readonly Regex RxMaxMin = new Regex(@"^(MAX|MIN)\(\s*avg\s*\*\s*([0-9.]+)\s*,\s*(MinSafe|MaxSafe|MinFail)\s*\)$", RegexOptions.IgnoreCase);
        private static readonly Regex RxAvgK = new Regex(@"^(?:avg\s*\*\s*([0-9.]+)|([0-9.]+)\s*\*\s*avg|([0-9.]+)x\s*avg)$", RegexOptions.IgnoreCase);

        private static double? CardThr(JudgeCard card, string key)
        {
            double v;
            if (card.Thresholds.TryGetValue(key, out v))
            {
                return v;
            }

            return null;
        }

        private static string Pct(double k)
        {
            return (k * 100).ToString("0", CultureInfo.InvariantCulture) + "%Avg";
        }

        internal static double? ResolveThreshold(JudgeCauseAttr a, JudgeCard card, JudgeEvidenceBundle ev, out string note)
        {
            string f = (a.EffectiveThreshold ?? "").Trim();
            int dash = f.IndexOf("--", StringComparison.Ordinal);
            if (dash > 0)
            {
                f = f.Substring(0, dash).Trim();
            }

            if (f == "-" || f == "- (alias)" || f == "")
            {
                note = "no threshold in catalog";
                return null;
            }

            if (f.StartsWith("compare with", StringComparison.OrdinalIgnoreCase) || f.StartsWith("added to", StringComparison.OrdinalIgnoreCase))
            {
                note = "cross-attribute — unsupported";
                return null;
            }

            if (f == "0+")
            {
                note = "0+";
                return 0;
            }

            if (f == "0")
            {
                note = "0";
                return 0;
            }

            double bavg;
            double? avg = ev.BaselineAvg.TryGetValue(a.Attid, out bavg) ? bavg : (double?)null;
            Match m = RxMaxMin.Match(f);
            if (m.Success)
            {
                double k = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                string safeName = m.Groups[3].Value;
                double? safe = CardThr(card, safeName);
                bool isMax = m.Groups[1].Value.Equals("MAX", StringComparison.OrdinalIgnoreCase);
                double? cardPct = CardThr(card, Pct(k));
                if (cardPct != null && safe != null)
                {
                    double v0 = isMax ? Math.Max(cardPct.Value, safe.Value) : Math.Min(cardPct.Value, safe.Value);
                    note = f + ": card " + Pct(k) + "=" + JudgeUtil.F(cardPct, "0.###") + " " + safeName + "=" + JudgeUtil.F(safe, "0.###") + " → " + JudgeUtil.F(v0, "0.###");
                    return v0;
                }

                if (avg == null && safe == null)
                {
                    note = cardPct != null ? f + ": card " + Pct(k) + "=" + JudgeUtil.F(cardPct, "0.###") : f + ": avg and " + safeName + " unavailable";
                    return cardPct;
                }

                // 1.0.192.2 -- THE CARD DROPPED ONE SIDE ON PURPOSE. PREFER THE CARD.
                //
                // AlertV3's ThresholdUtil.Low/High is the single source of truth for this rule, and
                // it has a case this resolver did not model: a POLLUTED AVERAGE. On the low side,
                // when avg < minSafe the average has drifted below the safe limit and is DROPPED --
                // Effective becomes minSafe alone, AvgActive goes false, and
                // AlertCardJsonBuilder.LowThresholds then emits ONLY the "Min Safe" row. High side
                // mirrors it: avg > maxSafe drops the average, leaving maxSafe.
                //
                // So a card carrying "Min Safe" and NO "NN% Avg" row is not an incomplete card. It
                // is the service telling us it threw that average away. Falling through to the
                // historian's own 24 h baseline below would RE-INTRODUCE exactly the average
                // AlertV3 discarded, and compute a threshold the service never used.
                //
                // The mirror case is the same principle: a card carrying "NN% Avg" and no safe row
                // is ThresholdUtil's "avg only" outcome, where Effective is ld*avg -- which is the
                // number already printed on the card. Recomputing it from the historian baseline
                // can only disagree with it.
                //
                // Both branches below therefore prefer what the card actually printed. The
                // historian fallbacks are kept for the case the card states NEITHER row.
                if (cardPct == null && safe != null)
                {
                    note = f + ": card states " + safeName + "=" + JudgeUtil.F(safe, "0.###") + " and NO "
                        + Pct(k) + " row — AlertV3 suppressed the average (ThresholdUtil polluted-avg"
                        + " rule), so the safe limit alone is the effective threshold";
                    return safe;
                }

                if (cardPct != null && safe == null)
                {
                    note = f + ": card states " + Pct(k) + "=" + JudgeUtil.F(cardPct, "0.###") + " and no "
                        + safeName + " row — avg-only rule, the card's own threshold is used";
                    return cardPct;
                }

                if (avg == null)
                {
                    note = f + ": avg unavailable → " + safeName + "=" + JudgeUtil.F(safe, "0.###");
                    return safe;
                }

                if (safe == null)
                {
                    note = f + ": " + safeName + " unavailable → avg*" + k.ToString(CultureInfo.InvariantCulture) + "=" + JudgeUtil.F(avg * k, "0.###");
                    return avg * k;
                }

                double v = isMax ? Math.Max(avg.Value * k, safe.Value) : Math.Min(avg.Value * k, safe.Value);
                note = f + ": avg=" + JudgeUtil.F(avg, "0.###") + " " + safeName + "=" + JudgeUtil.F(safe, "0.###") + " → " + JudgeUtil.F(v, "0.###");
                return v;
            }

            m = RxAvgK.Match(f);
            if (m.Success)
            {
                string ks = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
                double k = double.Parse(ks, CultureInfo.InvariantCulture);
                double? cardPct = CardThr(card, Pct(k));
                if (cardPct != null)
                {
                    note = f + ": card " + Pct(k) + "=" + JudgeUtil.F(cardPct, "0.###");
                    return cardPct;
                }

                if (avg == null)
                {
                    note = f + ": baseline avg unavailable";
                    return null;
                }

                note = f + ": avg=" + JudgeUtil.F(avg, "0.###") + " → " + JudgeUtil.F(avg * k, "0.###");
                return avg * k;
            }

            foreach (string key in new[] { "MinFail", "MinSafe", "MaxSafe", "MaxFail", "MinWarn", "MaxWarn" })
            {
                if (f.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    double? t = CardThr(card, key);
                    if (t != null)
                    {
                        note = key + "=" + JudgeUtil.F(t, "0.###") + " (card)";
                        return t;
                    }

                    note = key + " not on card";
                    return null;
                }
            }

            double lit;
            if (double.TryParse(f, NumberStyles.Float, CultureInfo.InvariantCulture, out lit))
            {
                note = "literal " + lit.ToString(CultureInfo.InvariantCulture);
                return lit;
            }

            note = "threshold form '" + f + "' not understood";
            return null;
        }

        private static string OrGroup(string gate)
        {
            if (string.IsNullOrEmpty(gate))
            {
                return null;
            }

            Match m = Regex.Match(gate, @"OR-G\w*", RegexOptions.IgnoreCase);
            return m.Success ? m.Value.ToUpperInvariant() : null;
        }
    }
}
