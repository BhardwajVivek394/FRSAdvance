using System;
using System.Collections.Generic;
using System.Linq;

namespace E7.AiCore.Judge
{
    // ============================================================================================
    // JudgeResetCheck (2.7) -- the evidence the judge already had and was not reading.
    //
    // THE PROBLEM THIS SOLVES. Alert 1137362 (TC TR VOLT LOW, GAHLOTA 04AT, 28.09) is a true alert.
    // It was discarded like this:
    //
    //   "finalDecision": "SUPPRESSED", "decidedBy": "live-no-data",
    //   "reason": "The card's conditions could not be re-checked against live data:
    //              could not verify: TPR (NO_DATA); ITC RELAY END(mA) (NO_DATA)."
    //
    // The live re-check asks "are the card's conditions still true in the card's own 15 s window?"
    // and answers it from the historian. For an alert that is DAYS OLD AND ALREADY RESET that
    // question cannot be answered -- the window is long gone, the raw samples may be pruned, and
    // whatever the tag reads now describes a healthy asset, not the moment of the fault. Discarding
    // the alert on that is the judge mistaking "I cannot see" for "it did not happen".
    //
    // Meanwhile the answer was sitting in the request body. The alert service writes TWO cards:
    //
    //   set   07:58:57   ITC RELAY END(mA) = 148.61   vs 80% Avg 261.26 FAIL, Min Safe 210 FAIL
    //   reset 08:27:36   ITC RELAY END(mA) = 332.12   vs 80% Avg 261.26 PASS, Min Safe 210 PASS
    //
    // That is a measured excursion below the threshold and a measured recovery above it, 29 minutes
    // apart, recorded by the service itself at both ends. It needs no historian, no tag mapping and
    // no retention window, so it works on an alert of any age -- precisely the case where the live
    // re-check is useless. The judge was reading the reset TIMESTAMP and discarding the numbers.
    //
    // WHAT THIS IS NOT. It is not a way to pass alerts that fail their checks:
    //
    //   * a live FAIL still suppresses. A real contradiction found in live data outranks the card,
    //     and this step is never consulted in that branch. The card is the claim; live data is the
    //     independent test of it. Corroborating the claim with more of the claim cannot overturn a
    //     test that actually ran.
    //   * the catalogue gates still decide first. This runs after them and cannot revive an alert
    //     whose sustain, adjacency, relay or analog gate failed.
    //   * it reads the SET card's rule, never the reset card's. The reset card flips the comparison
    //     (the same threshold is written "< 261.26 FAIL" at set and "≥ 261.26 PASS" at reset), so
    //     taking the operator from the reset card would ask the opposite question and confirm
    //     everything. The question asked here is always "does this value breach the rule that
    //     fired?", with the rule as the set card stated it.
    //   * it can SUPPRESS. If the set card's own measurement does not breach the set card's own
    //     threshold, the card contradicts itself, and no outside data is needed to see that. That
    //     finding is new in 2.7 and it is the negative control on this whole step: a card assembled
    //     mid-transition is now caught from the card alone.
    // ============================================================================================
    internal static class JudgeResetCheck
    {
        internal static JudgeResetResult Run(JudgeCard card, JudgeSettings cfg, List<string> log)
        {
            JudgeResetResult r = new JudgeResetResult();
            if (card == null)
            {
                return r;
            }

            r.ResetAt = card.ResetAt;

            if (!cfg.LiveUseResetCard)
            {
                r.Detail = "Live.UseResetCard = false";
                log.Add("reset: SKIPPED — Live.UseResetCard = false");
                return r;
            }

            if (card.ResetAt == null)
            {
                r.Detail = "the alert is still open (no reset card)";
                log.Add("reset: SKIPPED — alert still open, no reset card");
                return r;
            }

            if (!card.HasResetCard)
            {
                r.Detail = "reset at " + JudgeUtil.T(card.ResetAt.Value.ToOffset(JudgeUtil.Ist), "HH:mm:ss")
                    + " but the reset card carries no conditions to compare";
                log.Add("reset: SKIPPED — " + r.Detail);
                return r;
            }

            foreach (JudgeCardCondition cond in card.Conditions)
            {
                JudgeResetCondResult cr = One(cond, card.ResetConditions, cfg);
                r.Conditions.Add(cr);
            }

            r.ConfirmedCount = r.Conditions.Count(x => x.Status == "CONFIRMED_RECOVERED" || x.Status == "CONFIRMED_PERSISTS");
            r.ContradictedCount = r.Conditions.Count(x => x.Status == "CARD_CONTRADICTS");
            r.ComparedCount = r.ConfirmedCount + r.ContradictedCount;

            // A named self-contradiction outranks any amount of corroboration: one condition whose
            // own measurement does not breach its own threshold means the card is not internally
            // consistent, and nothing else on it can repair that.
            if (r.ContradictedCount > 0)
            {
                r.Result = "CONTRADICTED";
            }
            else if (r.ConfirmedCount > 0)
            {
                r.Result = "CONFIRMED";
            }
            else
            {
                r.Result = "INCONCLUSIVE";
            }

            List<string> said = r.Conditions
                .Where(x => x.Status != "IGNORED" && x.Status != "NOT_COMPARABLE")
                .Select(x => x.Detail)
                .Where(x => !string.IsNullOrEmpty(x))
                .ToList();

            r.Detail = said.Count > 0
                ? string.Join("; ", said)
                : "nothing on the two cards could be compared numerically";

            log.Add("reset: " + r.Result + " — set " + JudgeUtil.T(card.TriggeredAt.ToOffset(JudgeUtil.Ist), "HH:mm:ss")
                + " vs reset " + JudgeUtil.T(card.ResetAt.Value.ToOffset(JudgeUtil.Ist), "HH:mm:ss")
                + " — confirmed=" + r.ConfirmedCount + " contradicted=" + r.ContradictedCount
                + " of " + r.Conditions.Count + " condition(s); " + r.Detail);

            return r;
        }

        /// <summary>
        /// One condition, set card against reset card, judged by the SET card's rule in both cases.
        /// </summary>
        private static JudgeResetCondResult One(JudgeCardCondition cond, List<JudgeCardCondition> resetConds, JudgeSettings cfg)
        {
            JudgeResetCondResult cr = new JudgeResetCondResult();
            cr.Condition = cond;

            // Same exclusion list as the live re-check, so the two steps never disagree about what
            // counts. A condition the live check is told to ignore is not evidence here either.
            if (cfg.LiveIgnoreConditions.Contains(cond.Name) || cfg.LiveIgnoreConditions.Contains(cond.Label))
            {
                cr.Status = "IGNORED";
                cr.Detail = cond.Label + ": listed in Live.IgnoreConditions — reported, not counted";
                return cr;
            }

            JudgeCardCondition reset = Match(cond, resetConds);

            // ---- relays -------------------------------------------------------------------------
            // A relay's enabling state is a precondition, not a measurement that breaches anything.
            // The set and reset cards agreeing on it is worth recording but proves nothing either
            // way, so it is reported and not counted.
            if (cond.Kind == "relay")
            {
                cr.SetValue = cond.CardValue;
                cr.ResetValue = reset != null ? reset.CardValue : null;
                cr.Status = "NOT_COMPARABLE";
                cr.Detail = cond.Label + ": relay state "
                    + (cond.WantState ?? "?")
                    + (reset != null && reset.WantState != null ? " at set, " + reset.WantState + " at reset" : " at set, not on the reset card")
                    + " — a precondition, not a threshold breach";
                return cr;
            }

            // ---- analogs and derived ------------------------------------------------------------
            List<JudgeCardThreshold> rules = cond.Thresholds.Where(t => !string.IsNullOrWhiteSpace(t.Comparison)).ToList();
            if (cond.CardValue == null || rules.Count == 0)
            {
                cr.Status = "NOT_COMPARABLE";
                cr.Detail = cond.Label + ": "
                    + (cond.CardValue == null ? "no numeric value on the set card" : "no threshold with a comparison on the set card");
                return cr;
            }

            cr.SetValue = cond.CardValue;

            // =================================================================================
            // 1.0.190.0 -- THE BUG, AND THE FIX.
            //
            // This used to build its rule by OR-ing EVERY threshold on the card that carried a
            // comparison, including ones the card marked PASS. Those carry the OPPOSITE operator,
            // so the rule mixed directions.
            //
            // On 1023426 the set card read "80% Avg 269.30 FAIL (<)" and "Min Safe 210 PASS (>=)",
            // which made the rule "< 269.3 OR >= 210". The reset value 273.68 is a HEALTHY current
            // -- and 273.68 >= 210 is true, so it "breached", and a recovered alert was reported as
            //
            //     CONFIRMED_PERSISTS: "262.08 at set and 273.68 at reset, both breaching
            //                          ... the condition was real and had not recovered"
            //
            // which is false, and was handed to the AI as a statement contradicting the numbers
            // printed beside it. The correct status is CONFIRMED_RECOVERED.
            //
            // Now it collapses to ONE threshold and ONE direction, per the owner's rule and the
            // catalogue's own EffectiveThreshold form -- MAX(avg*LD, MinSafe) for a low rule,
            // MIN(avg*LC, MaxSafe) for a high one. See JudgeUtil.EffectiveThreshold.
            //
            // The old Any(...) remains the fallback for a card whose FAIL thresholds genuinely
            // point both ways (a band), so no card changes shape silently.
            // =================================================================================
            double effThr;
            string effDir;
            string effRule;
            Func<double, bool> breaches;

            if (JudgeUtil.EffectiveThreshold(cond.Thresholds, out effThr, out effDir, out effRule))
            {
                cr.Rule = effRule;
                breaches = v => JudgeUtil.Cmp(effDir, v, effThr);
            }
            else
            {
                cr.Rule = string.Join(" OR ", rules.Select(t =>
                    JudgeUtil.Direction(t.Comparison) + " " + JudgeUtil.F(t.Value, "0.###") + " (" + t.Label + ")").ToArray());
                breaches = v => rules.Any(t => JudgeUtil.Cmp(t.Comparison, v, t.Value));
            }

            if (!breaches(cond.CardValue.Value))
            {
                // THE NEGATIVE CONTROL. The card says this condition fired, and the number the card
                // itself printed does not breach the threshold the card itself printed. No historian
                // needed to know something is wrong with this alert.
                cr.Status = "CARD_CONTRADICTS";
                cr.Detail = cond.Label + ": the card's own measurement " + JudgeUtil.F(cond.CardValue, "0.###")
                    + " does not breach the card's own rule " + cr.Rule + " — the card contradicts itself";
                return cr;
            }

            if (reset == null || reset.CardValue == null)
            {
                // The set card is self-consistent but there is no reset measurement to pair with it.
                // That is corroboration of a kind -- the alert rule did fire on the stated number --
                // but not the excursion-and-recovery pair, so it is not counted as confirmation.
                cr.Status = "NO_RESET_ROW";
                cr.Detail = cond.Label + ": set " + JudgeUtil.F(cond.CardValue, "0.###")
                    + " breaches " + cr.Rule + ", but the reset card carries no measurement for it";
                return cr;
            }

            cr.ResetValue = reset.CardValue;

            if (breaches(reset.CardValue.Value))
            {
                cr.Status = "CONFIRMED_PERSISTS";
                cr.Detail = cond.Label + ": " + JudgeUtil.F(cond.CardValue, "0.###") + " at set and "
                    + JudgeUtil.F(reset.CardValue, "0.###") + " at reset, both breaching " + cr.Rule
                    + " — the condition was real and had not recovered when the alert was cleared";
                return cr;
            }

            cr.Status = "CONFIRMED_RECOVERED";
            cr.Detail = cond.Label + ": " + JudgeUtil.F(cond.CardValue, "0.###") + " at set breaches "
                + cr.Rule + ", " + JudgeUtil.F(reset.CardValue, "0.###") + " at reset does not"
                + " — a measured excursion and recovery, both recorded by the alert service";
            return cr;
        }

        /// <summary>
        /// Find the same condition on the reset card. Exact label first, then name without the unit,
        /// then JudgeVerifier.Tight (spaces removed, a leading A/B end designator dropped) -- the
        /// Point Machine cards label the same attribute "B VPT 110 DC LOC N" on one card and
        /// "VPT 110DC Loc N" elsewhere, and 1.0.179.0 already had to solve that for the catalogue.
        /// </summary>
        private static JudgeCardCondition Match(JudgeCardCondition cond, List<JudgeCardCondition> resetConds)
        {
            if (resetConds == null || resetConds.Count == 0)
            {
                return null;
            }

            JudgeCardCondition hit = resetConds.FirstOrDefault(x =>
                string.Equals(x.Label, cond.Label, StringComparison.OrdinalIgnoreCase));
            if (hit != null)
            {
                return hit;
            }

            hit = resetConds.FirstOrDefault(x =>
                string.Equals(x.Name, cond.Name, StringComparison.OrdinalIgnoreCase));
            if (hit != null)
            {
                return hit;
            }

            string tight = JudgeVerifier.Tight(cond.Name);
            return resetConds.FirstOrDefault(x => JudgeVerifier.Tight(x.Name) == tight);
        }
    }
}
