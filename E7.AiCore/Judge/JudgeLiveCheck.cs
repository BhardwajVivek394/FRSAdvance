using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace E7.AiCore.Judge
{
    // ============================================================================================
    // JudgeLiveCheck -- re-checks every condition printed on the card against live historian data
    // (new in 2.0, your rule of 7 Oct 2026):
    //
    //   "in transition a value can get captured in the alert and trigger it. Verify that the
    //    condition is still set according to LIVE data: yes -> pass, no -> suppressed. Do not depend
    //    on the card's data; check all card conditions at live."
    //
    // CHANGE HERE to change how "still set" is judged. The window, what to do with no data, whether
    // relays must hold, and card-vs-recomputed thresholds are in judge-settings.json (Live.*).
    //
    // For each card condition (enabling relay, main / derived triggered):
    //   window  = [set time, set time + checkWindow]   (card.meta.checkWindow -> catalog SustainSec -> Live.WindowSec)
    //   data    = the attribute's live step series: value in effect at set (carried forward) + every change in the window
    //   relay   : every value in the window must be in the card's state (UP / DN)
    //   analog  : every value in the window must satisfy at least one of the card's FAIL thresholds, with the
    //             card's comparison (a lower rule "< 80% avg OR MinSafe" is breached below EITHER number)
    //   derived : computed from the card's formula (or Live.Formulas) over the operands' live series, then as analog
    //   HELD    = the condition stayed true through the whole window; LAPSED = it broke (the first break is reported)
    // Combination: enabling conditions always AND; triggered conditions per card.meta.conditionLogic (AND / OR).
    // ============================================================================================
    internal sealed class JudgeLiveCheck
    {
        private readonly JudgeEvidence evidence;
        private readonly JudgeSettings cfg;
        private readonly Func<DateTimeOffset> clock;
        private readonly Func<TimeSpan, CancellationToken, Task> delay;

        internal JudgeLiveCheck(JudgeEvidence evidence, JudgeSettings cfg)
            : this(evidence, cfg, null, null)
        {
        }

        internal JudgeLiveCheck(JudgeEvidence evidence, JudgeSettings cfg, Func<DateTimeOffset> clock, Func<TimeSpan, CancellationToken, Task> delay)
        {
            this.evidence = evidence;
            this.cfg = cfg;
            this.clock = clock ?? (() => DateTimeOffset.UtcNow);
            this.delay = delay ?? ((ts, ct) => Task.Delay(ts, ct));
        }

        internal async Task<JudgeLiveResult> RunAsync(JudgeCard card, JudgeCauseDef def, JudgeEvidenceBundle ev, List<string> log, CancellationToken ct)
        {
            JudgeLiveResult r = new JudgeLiveResult();
            r.ConditionLogic = card.ConditionLogic;
            if (card.Conditions.Count == 0)
            {
                r.Result = "SKIPPED";
                r.Detail = "card carries no structured conditions (enablingConditions / triggeredConditions objects)";
                log.Add("live: " + r.Detail);
                return r;
            }

            // ---- window ----
            int windowSec;
            string source;
            ResolveWindow(card, def, out windowSec, out source);
            r.WindowSec = windowSec;
            r.WindowSource = source;
            r.WindowFrom = card.TriggeredAt;
            r.WindowTo = card.TriggeredAt.AddSeconds(windowSec);
            log.Add("live: window [" + JudgeUtil.T(r.WindowFrom, "HH:mm:ss") + ", " + JudgeUtil.T(r.WindowTo, "HH:mm:ss") + "] = " + windowSec + "s (" + source + "), logic " + card.ConditionLogic + ", " + card.Conditions.Count + " conditions");

            // ---- the window must have elapsed before live data can prove anything ----
            DateTimeOffset now = clock();
            if (r.WindowTo > now)
            {
                double need = (r.WindowTo - now).TotalSeconds + 2;
                if (need <= cfg.LiveWaitMaxSec)
                {
                    log.Add("live: window ends in " + JudgeUtil.F(need, "0") + "s -- waiting");
                    await delay(TimeSpan.FromSeconds(need), ct).ConfigureAwait(false);
                    r.WaitedSec = need;
                }
                else
                {
                    r.Result = "NO_DATA";
                    r.Detail = "window ends at " + JudgeUtil.T(r.WindowTo, "HH:mm:ss") + " (" + JudgeUtil.F(need, "0") + "s from now, more than Live.WaitMaxSec=" + cfg.LiveWaitMaxSec + "); call again after that time";
                    log.Add("live: " + r.Detail);
                    return r;
                }
            }

            // ---- per condition ----
            Dictionary<string, JudgeStepSeries> fetched = new Dictionary<string, JudgeStepSeries>(StringComparer.OrdinalIgnoreCase);
            foreach (JudgeCardCondition cond in card.Conditions)
            {
                ct.ThrowIfCancellationRequested();
                JudgeConditionResult cr = new JudgeConditionResult();
                cr.Condition = cond;
                r.Conditions.Add(cr);
                try
                {
                    if (cfg.LiveIgnoreConditions.Contains(cond.Name) || cfg.LiveIgnoreConditions.Contains(cond.Label))
                    {
                        cr.Status = "IGNORED";
                        cr.Detail = cond.Label + ": listed in Live.IgnoreConditions -- reported, not counted";
                    }
                    else if (JudgeAttrFacts.IsInProcessDelta(cond.Label) || JudgeAttrFacts.IsInProcessDelta(cond.Name))
                    {
                        // 2.3 -- a Delta compares now against a baseline AlertV3 keeps IN MEMORY
                        // (AlertStateManager.CalculateVariations: _previousValues, snapshotted only
                        // at a tick with TPR UP and no active alert). That baseline is in no
                        // historian and no range table, so no amount of history can rebuild it.
                        // Permanently non-re-derivable BY DESIGN -- say so, rather than calling it
                        // missing data and holding an alert back for it.
                        cr.Status = "IGNORED";
                        cr.Detail = cond.Label + ": compares against AlertV3's in-process baseline"
                            + " (captured at the last healthy TPR-UP tick) -- not re-derivable from"
                            + " history by design; reported, not counted";
                    }
                    else if (IsSatisfiedGate(cond))
                    {
                        // 1.0.192.0 -- A THRESHOLD THE CARD MARKED "PASS" IS A GATE, NOT A BREACH.
                        //
                        // AlertV3 puts two different kinds of row in triggeredConditions. One is
                        // the breach the alert fired on, and its threshold carries status "FAIL".
                        // The other is a GATE or DISCRIMINATOR -- it says which variant of the
                        // cause this is, and its threshold carries status "PASS". The card even
                        // names it:
                        //
                        //   {"paramLabel":"ITC BATT CHARG(mA)","measured":"-268.04",
                        //    "thresholds":[{"label":"Discharging gate (< 0)","value":"0.00",
                        //                   "status":"PASS","comparison":"<"}]}
                        //
                        // The catalogue agrees: for #2/#3 TC TFC O/P VOLT LOW (Charging/
                        // Discharging) the ITC Batt Charg attribute is the charge/discharge
                        // discriminator (Cmp "<" / ">" against 0), while VTC TFC O/P carries the
                        // real rule. "0+" appears as an EffectiveThreshold twelve times in the
                        // catalogue for exactly this kind of row.
                        //
                        // Until this build every such row was counted as a breach that had to
                        // hold CONTINUOUSLY for the cause's whole sustain window, and the alert
                        // died when it did not:
                        //
                        //   12.10.2026, alert 1376537 TC TFC O/P VOLT LOW (Discharging):
                        //     VTC TFC O/P   0.00 V vs MAX(80%Avg 5.57, MinSafe 4.50) -> HELD 112 s
                        //     TPR                                                    -> HELD
                        //     set/reset cards                                        -> CONFIRMED
                        //     ITC BATT CHARG  at set(10:02:12) = +331.777            -> LAPSED
                        //   -> SUPPRESSED / live-recheck, confidence 90, on a real fault.
                        //
                        // The discriminator was never in doubt: the card sampled it at 10:02:16
                        // as -268.04, the window minimum was -282.58 and the last reading
                        // -268.044. It is a DERIVED value (ITC TFC O/P - ITC FEED END) whose two
                        // source tags are sampled microseconds apart, so its value at exactly
                        // t=0 is noise -- which is why AlertV3 itself read it four seconds in.
                        //
                        // A gate was already evaluated and satisfied by AlertV3 at trigger time;
                        // that is what status "PASS" records. Re-deriving it from history adds
                        // no evidence about the alert and, as above, only ever subtracts. So it
                        // is reported and NOT counted -- the same treatment as
                        // Live.IgnoreConditions, with its own status so it is visible in the
                        // envelope rather than silently dropped.
                        //
                        // Narrow by design: see IsSatisfiedGate. A condition carrying any FAIL
                        // mark, or a card that states no statuses at all, is checked exactly as
                        // it was in 1.0.191.0.
                        cr.Status = "GATE";
                        cr.Detail = cond.Label + ": every threshold on this condition is marked PASS on the card ("
                            + GateLabels(cond) + ") -- a gate or variant discriminator, already satisfied at trigger"
                            + " time, not the breach the alert fired on; reported, not counted";
                    }
                    else if (cond.Kind == "relay")
                    {
                        await CheckRelayAsync(card, def, ev, cond, cr, r, fetched, log, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        await CheckAnalogAsync(card, def, ev, cond, cr, r, fetched, log, ct).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    cr.Status = "NO_DATA";
                    cr.Detail = cond.Label + ": " + ex.GetType().Name + " " + ex.Message;
                }

                log.Add("live: " + cond.Role + " " + cond.Label + " → " + cr.Status + " — " + cr.Detail);
            }

            // ---- combine ----
            // 2.3 -- IGNORED (listed in Live.IgnoreConditions) and UNMAPPED (no tag for the attribute
            // on this asset) are both "reported, not counted": neither is evidence about the alert, so
            // neither may hold back a verdict. Everything else counts.
            // 1.0.192.0 -- GATE joins them: a condition whose every threshold the card marked PASS
            // is a precondition AlertV3 already satisfied, not a claim about a breach, so it is
            // reported but may not hold a verdict back. See IsSatisfiedGate.
            Func<JudgeConditionResult, bool> counts = x => x.Status != "IGNORED" && x.Status != "UNMAPPED" && x.Status != "GATE";
            List<JudgeConditionResult> enabling = r.Conditions.Where(x => x.Condition.Role == "enabling" && counts(x)).ToList();
            List<JudgeConditionResult> triggered = r.Conditions.Where(x => x.Condition.Role != "enabling" && counts(x)).ToList();
            string enablingResult = Combine(enabling, "AND");
            // 2.2: honour the card's own OR groups. Conditions sharing a "group" combine by their
            // own "logic" (OR for an SPN alternative set); the group results then AND together.
            // Ungrouped conditions keep the card-level meta.conditionLogic, as before.
            string triggeredResult = CombineGrouped(triggered, card.ConditionLogic, log);
            string result;
            if (enablingResult == "FAIL" || triggeredResult == "FAIL")
            {
                result = "FAIL";
            }
            else if (enablingResult == "UNSUPPORTED" || triggeredResult == "UNSUPPORTED")
            {
                result = "UNSUPPORTED";
            }
            else if (enablingResult == "NO_DATA" || triggeredResult == "NO_DATA")
            {
                result = "NO_DATA";
            }
            else
            {
                result = "PASS";
            }

            r.Result = result;
            // 2.3 -- record what the re-check actually obtained, so the policy can tell "the data
            // contradicts the card" from "there was no data to contradict it with".
            r.CountedCount = enabling.Count + triggered.Count;
            r.HeldCount = r.Conditions.Count(x => x.Status == "HELD");
            r.NoEvidence = r.HeldCount == 0 && !r.Conditions.Any(x => x.Status == "LAPSED");
            List<string> failed = r.Conditions.Where(x => x.Status == "LAPSED").Select(x => x.Condition.Label).ToList();
            List<string> missing = r.Conditions.Where(x => x.Status == "NO_DATA" || x.Status == "UNSUPPORTED").Select(x => x.Condition.Label + " (" + x.Status + ")").ToList();
            // 2.3 -- the not-counted attributes, named so the data team can see exactly what to map.
            List<string> unmapped = r.Conditions.Where(x => x.Status == "UNMAPPED")
                .Select(x => x.Condition.Label + (x.Attid > 0 ? " (attid " + x.Attid + ")" : "")).ToList();
            int held = r.HeldCount;
            string notCounted = unmapped.Count > 0
                ? "; not re-checkable, so not counted: " + string.Join(", ", unmapped)
                : "";
            if (result == "PASS")
            {
                // 2.3: say how many of how many actually held. Claiming "all conditions held" when
                // every one of them was unmapped overstated what the judge had seen.
                //
                // 1.0.191.0 -- the DENOMINATOR is now the COUNTED conditions, not every condition on
                // the card. On 1263751 this read "3 of 4 conditions held in live data through 10s"
                // when all THREE counted conditions held and the fourth (∆IBALST) was IGNORED by
                // Live.IgnoreConditions. "3 of 4" reads as one condition having failed, which is the
                // opposite of what happened -- and it goes to the AI as well as to the operator.
                int notCountedN = r.Conditions.Count - r.CountedCount;
                r.Detail = held > 0
                    ? held + " of " + r.CountedCount + " counted condition" + (r.CountedCount == 1 ? "" : "s")
                      + " held in live data through " + windowSec + "s"
                      + (notCountedN > 0
                          ? " (" + notCountedN + " of the card's " + r.Conditions.Count + " not counted)"
                          : "")
                      + notCounted
                    : "nothing on this card could be re-checked against live data" + notCounted
                      + " -- the history gates above are what this verdict rests on";
            }
            else if (result == "FAIL")
            {
                r.Detail = "not set in live data: " + string.Join("; ", failed) + notCounted;
            }
            else
            {
                r.Detail = "could not verify: " + string.Join("; ", missing) + notCounted;
            }

            log.Add("live: enabling=" + enablingResult + " triggered(" + card.ConditionLogic + ")=" + triggeredResult + " → " + result);
            return r;
        }

        // ------------------------------------------------------------------------------------------
        private void ResolveWindow(JudgeCard card, JudgeCauseDef def, out int windowSec, out string source)
        {
            string pref = (cfg.LiveWindowSource ?? "card").Trim().ToLowerInvariant();
            windowSec = 0;
            source = "";
            if (pref == "fixed")
            {
                windowSec = cfg.LiveWindowSec;
                source = "fixed";
            }
            else if (pref == "catalog" && def != null && def.SustainSec > 0)
            {
                windowSec = def.SustainSec;
                source = "catalog";
            }
            else if (card.CheckWindowSec != null && card.CheckWindowSec.Value > 0)
            {
                windowSec = card.CheckWindowSec.Value;
                source = "card";
            }
            else if (def != null && def.SustainSec > 0)
            {
                windowSec = def.SustainSec;
                source = "catalog";
            }
            else
            {
                windowSec = cfg.LiveWindowSec;
                source = "default";
            }

            if (windowSec > cfg.LiveMaxWindowSec)
            {
                windowSec = cfg.LiveMaxWindowSec;
                source += ", capped";
            }

            if (windowSec < 0)
            {
                windowSec = 0;
            }
        }

        /// <summary>
        /// 2.2 -- split by the card's own group key, combine each group by its own logic, then AND
        /// the groups. A 4-way SPN OR (VSIG HG | VSIG HHG | ISIG HG | ISIG HHG, all group "g1")
        /// now passes when ONE member holds, instead of demanding all four.
        /// </summary>
        private static string CombineGrouped(List<JudgeConditionResult> list, string cardLogic, List<string> log)
        {
            if (list.Count == 0)
            {
                return "PASS";
            }

            List<JudgeConditionResult> ungrouped = new List<JudgeConditionResult>();
            Dictionary<string, List<JudgeConditionResult>> groups = new Dictionary<string, List<JudgeConditionResult>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> groupLogic = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < list.Count; i++)
            {
                JudgeConditionResult cr = list[i];
                string g = cr.Condition != null ? cr.Condition.Group : null;
                if (string.IsNullOrWhiteSpace(g))
                {
                    ungrouped.Add(cr);
                    continue;
                }

                if (!groups.ContainsKey(g))
                {
                    groups[g] = new List<JudgeConditionResult>();
                    groupLogic[g] = cardLogic;
                }

                groups[g].Add(cr);
                string lg = cr.Condition.Logic;
                if (!string.IsNullOrWhiteSpace(lg))
                {
                    groupLogic[g] = lg.Trim().ToUpperInvariant();   // any member stating OR makes the group OR
                }
            }

            if (groups.Count == 0)
            {
                return Combine(list, cardLogic);
            }

            List<string> parts = new List<string>();
            if (ungrouped.Count > 0)
            {
                parts.Add(Combine(ungrouped, cardLogic));
            }

            foreach (KeyValuePair<string, List<JudgeConditionResult>> kv in groups)
            {
                string r = Combine(kv.Value, groupLogic[kv.Key]);
                parts.Add(r);
                log.Add("live: group '" + kv.Key + "' (" + groupLogic[kv.Key] + ", " + kv.Value.Count + " conditions) -> " + r);
            }

            // the groups themselves AND together: every group must be satisfied
            if (parts.Contains("FAIL")) { return "FAIL"; }
            if (parts.Contains("UNSUPPORTED")) { return "UNSUPPORTED"; }
            if (parts.Contains("NO_DATA")) { return "NO_DATA"; }
            return "PASS";
        }


        private static string Combine(List<JudgeConditionResult> list, string logic)
        {
            if (list.Count == 0)
            {
                return "PASS";
            }

            bool anyHeld = list.Any(x => x.Status == "HELD");
            bool anyLapsed = list.Any(x => x.Status == "LAPSED");
            bool anyUnsupported = list.Any(x => x.Status == "UNSUPPORTED");
            bool anyNoData = list.Any(x => x.Status == "NO_DATA");
            if (logic == "OR")
            {
                if (anyHeld)
                {
                    return "PASS";
                }

                if (anyUnsupported)
                {
                    return "UNSUPPORTED";
                }

                if (anyNoData)
                {
                    return "NO_DATA";
                }

                return anyLapsed ? "FAIL" : "PASS";
            }

            if (anyLapsed)
            {
                return "FAIL";
            }

            if (anyUnsupported)
            {
                return "UNSUPPORTED";
            }

            if (anyNoData)
            {
                return "NO_DATA";
            }

            return "PASS";
        }

        // ------------------------------------------------------------------------------------------
        // name -> attid / role through the catalog, then the card's attributeIds
        // ------------------------------------------------------------------------------------------
        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "IR MA", "ITC RELAY END" }, { "IF MA", "ITC FEED END" }, { "VR", "VTC RELAY END" }, { "VF", "VTC FEED END" },
            { "IR", "ITC RELAY END" }, { "IF", "ITC FEED END" },
        };

        private JudgeCauseAttr FindCatalogAttr(string name, JudgeCauseDef def)
        {
            string norm = JudgeUtil.Norm(name);
            string alias;
            if (Aliases.TryGetValue(norm, out alias))
            {
                norm = alias;
            }

            IEnumerable<JudgeCauseAttr> pool = def != null ? def.Attrs.AsEnumerable() : Enumerable.Empty<JudgeCauseAttr>();
            JudgeCauseAttr hit = pool.FirstOrDefault(a => a.Attid > 0 && JudgeUtil.Norm(a.Name) == norm);
            if (hit != null)
            {
                return hit;
            }

            // any cause of the catalog: the attid of "ITC Relay End" is the same everywhere
            foreach (JudgeCauseDef d in JudgeCauseCatalog.All)
            {
                hit = d.Attrs.FirstOrDefault(a => a.Attid > 0 && JudgeUtil.Norm(a.Name) == norm);
                if (hit != null)
                {
                    return hit;
                }
            }

            return null;
        }

        private bool Resolve(JudgeCard card, JudgeCauseDef def, string name, bool relay, out int attid, out char role)
        {
            attid = 0;
            role = relay ? cfg.RoleDataLogger[0] : cfg.RoleRdpms[0];
            JudgeCauseAttr a = FindCatalogAttr(name, def);
            if (a != null)
            {
                attid = a.Attid;
                role = evidence.RoleFor(a.Datatype);
                return true;
            }

            int id;
            foreach (KeyValuePair<string, int> kv in card.AttributeIds)
            {
                if (JudgeUtil.Norm(JudgeUtil.StripUnit(kv.Key)) == JudgeUtil.Norm(name))
                {
                    id = kv.Value;
                    if (id > 0)
                    {
                        attid = id;
                        return true;
                    }
                }
            }

            // 2.3 -- LAST RESORT: a MEASURED base sensor, by any of its spellings.
            // The catalogue only names the attributes its cause rules test, which for TRACK is
            // ITC Feed End (1), ITC Relay End (2) and VTC TFC O/P (569). Charger mA (5),
            // Choke V (4), Vf (249) and Vr (3) are in no cause definition at all, so a derived
            // formula naming one of them resolved to nothing and could never be computed --
            // ITC BATT CHARG (585 = Charger mA - ITC Feed End) was dead for exactly this reason.
            // Tried last, so a catalogue entry always wins and existing behaviour is unchanged.
            int baseId = JudgeAttrFacts.BaseSensor(JudgeUtil.StripUnit(name));
            if (baseId == 0)
            {
                baseId = JudgeAttrFacts.BaseSensor(name);
            }

            if (baseId > 0)
            {
                attid = baseId;
                role = baseId == 6 && relay ? cfg.RoleDataLogger[0] : cfg.RoleRdpms[0];
                return true;
            }

            return false;
        }

        /// <summary>
        /// 2.3 -- true when SeriesAsync already tried this attribute and found NO TAG for it on this
        /// asset at all, as opposed to finding a tag whose window simply holds no samples. The two
        /// look identical downstream (both give a null/empty series) but mean opposite things: the
        /// first is a gap in our own tag mapping, the second is a real gap in the data.
        /// </summary>
        private static bool Unmapped(Dictionary<string, JudgeStepSeries> fetched, string name)
        {
            JudgeStepSeries s;
            return fetched.TryGetValue(name, out s) && s == null;
        }

        private async Task<JudgeStepSeries> SeriesAsync(JudgeCard card, JudgeCauseDef def, JudgeEvidenceBundle ev, string name, bool relay, JudgeLiveResult r, Dictionary<string, JudgeStepSeries> fetched, JudgeConditionResult cr, List<string> log, CancellationToken ct)
        {
            JudgeStepSeries cached;
            if (fetched.TryGetValue(name, out cached))
            {
                return cached;
            }

            int attid;
            char role;
            if (!Resolve(card, def, name, relay, out attid, out role))
            {
                fetched[name] = null;
                return null;
            }

            // 2.3 -- a derived attribute has no series of its own. Returning null here sends the
            // caller down the formula path, which is the only way to get a value for it.
            if (JudgeAttrFacts.IsDerived(attid))
            {
                if (cr != null)
                {
                    cr.Attid = attid;
                }

                fetched[name] = null;
                return null;
            }

            if (cr != null)
            {
                cr.Attid = attid;
            }

            // The catalog gates may already hold this tag's series over a frame that covers the window.
            // 2.3 -- DO NOT relax the StateSource == "history" condition below. It is load-bearing.
            // JudgeEvidence, when the historian has no sample at the frame start, falls back to the
            // CARD's own value and marks the series StateSource = "card"; the history gates accept
            // that as a carry-forward. The live re-check must not: its whole purpose is to test the
            // card against data the card did not supply. Feeding it a card-anchored series makes it
            // check the card against itself, which always "holds" -- the re-check silently becomes a
            // no-op and a card captured mid-transition sails through. An earlier 2.3 draft removed
            // this condition to make a relay stop reporting NO_DATA; the replay harness caught it
            // turning the analog re-check into a rubber stamp (scenarios 4 and 5 in Replay.cs).
            JudgeStepSeries fromGates;
            if (ev != null && ev.Steps.TryGetValue(attid, out fromGates) && fromGates != null && fromGates.StateSource == "history" && ev.From <= r.WindowFrom && ev.To >= r.WindowTo)
            {
                fetched[name] = fromGates;
                return fromGates;
            }

            JudgeStepSeries s = await evidence.FetchAttrAsync(card, name, attid, role, r.WindowFrom, r.WindowTo, log, ct).ConfigureAwait(false);
            fetched[name] = s;
            return s;
        }

        // ------------------------------------------------------------------------------------------
        private async Task CheckRelayAsync(JudgeCard card, JudgeCauseDef def, JudgeEvidenceBundle ev, JudgeCardCondition cond, JudgeConditionResult cr, JudgeLiveResult r, Dictionary<string, JudgeStepSeries> fetched, List<string> log, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(cond.WantState))
            {
                cr.Status = "UNSUPPORTED";
                cr.Detail = cond.Label + ": card states no relay state";
                return;
            }

            JudgeStepSeries s = await SeriesAsync(card, def, ev, cond.Name, true, r, fetched, cr, log, ct).ConfigureAwait(false);
            if (s == null)
            {
                // 2.3: not mapped on this asset is a gap in OUR tag mapping, not evidence about the
                // alert. Reported, not counted -- the same treatment the analog GATE already gives it
                // ("SKIPPED -- none of these attrs exist on this asset").
                cr.Status = "UNMAPPED";
                cr.Detail = cond.Label + ": no tag for this attribute on the asset (reported, not counted)";
                return;
            }

            cr.TagId = s.TagId;
            cr.Source = "tag";
            Fill(cr, s, r);
            if (s.StateAtStart == null && s.Transitions.Count == 0)
            {
                cr.Status = "NO_DATA";
                cr.Detail = cond.Label + ": no live value before or inside the window (lookback " + cfg.LookbackHours + "h)";
                return;
            }

            bool wantUp = cond.WantState == "UP";
            Func<double, bool> ok = v => wantUp ? v >= 0.5 : v < 0.5;
            // 3.0 -- relays keep the old semantics on purpose: an enabling relay state is a
            // precondition for the cause, not a threshold the fault crosses during the window.
            Judge(cr, s, r, ok, cond.Label + " " + cond.WantState, cfg.LiveRelaysMustHold, false, cond.CardTime);
        }

        private async Task CheckAnalogAsync(JudgeCard card, JudgeCauseDef def, JudgeEvidenceBundle ev, JudgeCardCondition cond, JudgeConditionResult cr, JudgeLiveResult r, Dictionary<string, JudgeStepSeries> fetched, List<string> log, CancellationToken ct)
        {
            List<JudgeCardThreshold> thresholds = cond.Thresholds.Where(t => string.Equals(t.Status, "FAIL", StringComparison.OrdinalIgnoreCase)).ToList();
            if (thresholds.Count == 0)
            {
                thresholds = cond.Thresholds.ToList();
            }

            if (thresholds.Count == 0 || thresholds.All(t => string.IsNullOrWhiteSpace(t.Comparison)))
            {
                cr.Status = "UNSUPPORTED";
                cr.Detail = cond.Label + ": card states no threshold / comparison";
                return;
            }

            // ---- the live series: a tag when the attribute exists on the asset, else the formula ----
            JudgeStepSeries s = null;
            int attid;
            char role;
            bool resolvable = Resolve(card, def, cond.Name, false, out attid, out role);
            if (resolvable)
            {
                s = await SeriesAsync(card, def, ev, cond.Name, false, r, fetched, cr, log, ct).ConfigureAwait(false);
                if (s != null)
                {
                    cr.Source = "tag";
                    cr.TagId = s.TagId;
                }
            }

            if (s == null)
            {
                string formula = cond.Formula;
                string cfgFormula;
                if (string.IsNullOrWhiteSpace(formula) && cfg.LiveFormulas.TryGetValue(cond.Name, out cfgFormula))
                {
                    formula = cfgFormula;
                }

                if (string.IsNullOrWhiteSpace(formula) && cfg.LiveFormulas.TryGetValue(cond.Label, out cfgFormula))
                {
                    formula = cfgFormula;
                }

                // 2.3 -- a DERIVED attribute: use the catalogue's own computable expansion when the
                // card did not print a formula. IBALST = ITC Feed End - ITC Relay End (attid 1 - 2),
                // exactly as AlertV3 computes it (AlertStateManager.CalculateDerivedValues).
                if (string.IsNullOrWhiteSpace(formula) && resolvable && JudgeAttrFacts.IsDerived(attid))
                {
                    string computable;
                    if (JudgeAttrFacts.ComputableFormula.TryGetValue(attid, out computable))
                    {
                        formula = computable;
                        log.Add("live: " + cond.Label + " is derived attid " + attid
                            + " -- computing it from base sensors: " + computable);
                    }
                    else
                    {
                        // Derived, but a ratio that also needs the FRS range (R737, per-attribute
                        // averages) which the live re-check does not hold. Honest, and not counted.
                        cr.Status = "UNMAPPED";
                        cr.Detail = cond.Label + ": derived attid " + attid + " ("
                            + JudgeAttrFacts.FormulaText(attid) + ") -- needs the FRS range, not"
                            + " re-derivable from live tags (reported, not counted)";
                        return;
                    }
                }

                if (string.IsNullOrWhiteSpace(formula))
                {
                    // 2.3: see CheckRelayAsync -- an unmapped attribute is reported, not counted.
                    cr.Status = "UNMAPPED";
                    cr.Detail = cond.Label + ": no tag for this attribute on the asset and no formula"
                        + " (reported, not counted; map the attid, or add a formula under Live.Formulas)";
                    return;
                }

                string err;
                JudgeDerived.Node node = JudgeDerived.Parse(formula, out err);
                if (node == null)
                {
                    cr.Status = "UNSUPPORTED";
                    cr.Detail = cond.Label + ": formula '" + formula + "' not understood (" + err + ")";
                    return;
                }

                cr.Formula = formula;
                cr.Source = "formula";
                Dictionary<string, JudgeStepSeries> operands = new Dictionary<string, JudgeStepSeries>(StringComparer.OrdinalIgnoreCase);
                List<string> missing = new List<string>();
                foreach (string op in JudgeDerived.OperandNames(node))
                {
                    JudgeStepSeries os = await SeriesAsync(card, def, ev, op, false, r, fetched, null, log, ct).ConfigureAwait(false);
                    if (os == null || (os.StateAtStart == null && os.Transitions.Count == 0))
                    {
                        missing.Add(op);
                    }

                    operands[op] = os;
                }

                if (missing.Count > 0)
                {
                    // 2.3: separate "the operand is not mapped on this asset" from "the operand has a
                    // tag but no samples in the window". The first is our mapping gap and is not
                    // counted; the second is a real data gap and still counts.
                    bool allUnmapped = missing.TrueForAll(m => Unmapped(fetched, m));
                    cr.Status = allUnmapped ? "UNMAPPED" : "NO_DATA";
                    // 2.1: the card's formula text already starts with the label ("IBALST(mA) = ..."),
                    // so prefixing it again printed the left side twice.
                    cr.Detail = FormulaText(cond.Label, formula)
                        + (allUnmapped
                            ? ": not mapped on this asset: " + string.Join(", ", missing) + " (reported, not counted)"
                            : ": no live data for " + string.Join(", ", missing));
                    return;
                }

                s = JudgeDerived.EvalSeries(node, operands, r.WindowFrom, r.WindowTo, cond.Name);
            }

            Fill(cr, s, r);
            if (s.StateAtStart == null && s.Transitions.Count == 0)
            {
                cr.Status = "NO_DATA";
                cr.Detail = cond.Label + ": no live value before or inside the window (lookback " + cfg.LookbackHours + "h)";
                return;
            }

            // ---- thresholds: the card's numbers (the rule that fired), or recomputed from the 24 h baseline ----
            List<KeyValuePair<string, double>> rules = new List<KeyValuePair<string, double>>();
            List<string> ruleText = new List<string>();
            foreach (JudgeCardThreshold t in thresholds)
            {
                double value = t.Value;
                string label = t.Label;
                if (!cfg.LiveUseCardThresholds && ev != null && cr.Attid > 0)
                {
                    double pct;
                    double bavg;
                    if (TryPct(t.Label, out pct) && ev.BaselineAvg.TryGetValue(cr.Attid, out bavg))
                    {
                        value = bavg * pct / 100.0;
                        label = t.Label + " recomputed";
                    }
                }

                rules.Add(new KeyValuePair<string, double>(t.Comparison, value));
                ruleText.Add(JudgeUtil.Direction(t.Comparison) + " " + JudgeUtil.F(value, "0.###") + " (" + label + ")");
            }

            // 1.0.190.0 -- COLLAPSE TO ONE NUMBER AND ONE DIRECTION, the same rule the reset check
            // and JudgeVerifier.ResolveThreshold use: MAX for a low rule, MIN for a high one. The
            // ANSWER here is unchanged -- Any(...) over same-direction clauses already equals
            // MAX/MIN -- but the single number is now RECORDED on the result, and that is what the
            // AI pack carries. Without it the model got the raw clause list and picked the wrong
            // half: on 1137362 it read "< 210" and called 217.8 mA a recovery against an effective
            // threshold of 261.26. Collapsed AFTER the baseline recompute above, so
            // Live.UseCardThresholds=false still works.
            Func<double, bool> ok;
            string ruleShown;

            List<string> dirs = rules.Select(rule => JudgeUtil.Direction(rule.Key)).Distinct().ToList();
            if (dirs.Count == 1 && (dirs[0] == "<" || dirs[0] == ">"))
            {
                string dir = dirs[0];
                double eff = dir == "<" ? rules.Max(rule => rule.Value) : rules.Min(rule => rule.Value);
                cr.EffThreshold = eff;
                cr.EffDirection = dir;
                ok = v => JudgeUtil.Cmp(dir, v, eff);
                ruleShown = rules.Count > 1
                    ? dir + " " + JudgeUtil.F(eff, "0.###") + " = " + (dir == "<" ? "MAX" : "MIN")
                      + " of " + string.Join(" OR ", ruleText)
                    : string.Join(" OR ", ruleText);
            }
            else
            {
                // a band, or no usable operator -- keep the clause list, and say so
                ok = v => rules.Any(rule => JudgeUtil.Cmp(rule.Key, v, rule.Value));
                ruleShown = string.Join(" OR ", ruleText);
                log.Add("live: " + cond.Label + " thresholds do not collapse to one number (mixed directions); using the clause list");
            }

            // 3.0 -- a FAILURE's condition may legitimately arrive during checkWindow; a
            // PREDICTIVE's must hold from the set instant, because there the rule IS sustain.
            bool onset = cfg.LiveFailureOnset
                && string.Equals(card.AlertType, "FAILURE", StringComparison.OrdinalIgnoreCase);
            Judge(cr, s, r, ok, cond.Label + " " + ruleShown, true, onset, cond.CardTime);
        }

        private static readonly Regex RxPct = new Regex(@"([0-9]+(?:\.[0-9]+)?)\s*%", RegexOptions.IgnoreCase);

        private static bool TryPct(string label, out double pct)
        {
            pct = 0;
            Match m = RxPct.Match(label ?? "");
            if (!m.Success || (label ?? "").IndexOf("avg", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            return double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out pct);
        }

        /// <summary>"LABEL = rhs" without repeating LABEL when the formula already carries it.</summary>
        private static string FormulaText(string label, string formula)
        {
            string f = (formula ?? "").Trim();
            string l = (label ?? "").Trim();
            if (l.Length > 0 && f.StartsWith(l, StringComparison.OrdinalIgnoreCase))
            {
                return f;
            }

            return l + " = " + f;
        }


        private static void Fill(JudgeConditionResult cr, JudgeStepSeries s, JudgeLiveResult r)
        {
            List<JudgeSample> segs = s.Segments(r.WindowFrom, r.WindowTo);
            cr.LiveAtSet = s.At(r.WindowFrom);
            cr.Samples = s.Transitions.Count + (s.StateAtStart != null ? 1 : 0);
            if (segs.Count > 0)
            {
                cr.LiveMin = segs.Min(x => x.Value);
                cr.LiveMax = segs.Max(x => x.Value);
                cr.LiveLast = segs[segs.Count - 1].Value;
            }
        }

        // =========================================================================================
        // 3.0 -- ONSET MODE: for a FAILURE, checkWindow is a window to FIND the condition in, not a
        // window the condition must already be true at the start of.
        //
        // WHAT WENT WRONG. Two TC SHORT alerts on the same cause and the same station, judged in the
        // same minute, came out opposite ways -- and the only thing that differed was WHEN the alert
        // service happened to take its sample:
        //
        //                        04AT 1263751 SUPPRESSED      04BT 1160057 PASS
        //   set time             08:38:30                     09:53:10
        //   window               08:38:30 -> 08:38:40         09:53:10 -> 09:53:20
        //   card's ITC sample    08:38:35  (+5s, INSIDE)      09:53:09  (-1s, BEFORE)
        //   value at window t=0  108.801  -> not < 100        6.177    -> is < 100
        //   judge said           "lapsed at 08:38:30"         "held through 10s"
        //   window min           90.225   -> IS < 100         6.177
        //
        // On 04AT the condition WAS satisfied inside the window -- the window minimum was 90.2 mA
        // against a MinFail of 100 -- and the card stamps that measurement 08:38:35, five seconds
        // into a ten-second window. The card never claimed 90.23 was the value at 08:38:30. The
        // judge read it as though it had, found 108.8 at the first instant, and discarded a real
        // short as a "False alarm" at 90% confidence.
        //
        // It was a real short. TPR dropped at 08:38:26; relay-end current fell 108.8 -> 90.2 -> 58.4
        // over 45 s while feed-end stayed at 724-807 mA, i.e. 615-716 mA going into the ballast;
        // IBALST ran 622-634 against a threshold of 36 and HELD; and the alert's own reset card
        // CONFIRMED both conditions. Everything agreed except the one anchor instant.
        //
        // And 04BT only passed by luck: its own IBALST sample is stamped 09:53:26, sixteen seconds
        // past a ten-second window. A verdict that turns on sampling jitter is not a verdict.
        //
        // THE RULE. For a FAILURE: find the first instant inside the window where the condition is
        // true, then require it to stay true from there to the end of the window. Never true
        // anywhere in the window -> LAPSED, exactly as before.
        //
        // WHY THIS IS NOT A RUBBER STAMP, and DO NOT RELAX IT FURTHER:
        //   * "true at some point" is not enough -- it must hold from its onset to the window end,
        //     so a one-sample flicker still LAPSES.
        //   * a condition that is never true in the window still LAPSES, which is the only reading
        //     of "the card is wrong" that the live data can actually support.
        //   * PREDICTIVE is untouched. There the rule genuinely IS sustain -- the value must stay
        //     past the threshold for SustainSec -- so onset mode would be wrong, and 1137362 and
        //     1034216 must keep deciding exactly as they do now.
        //   * relays are untouched: an enabling relay state is a precondition, and Live.RelaysMustHold
        //     already governs it.
        // Scenarios 11-14 of the replay harness assert all four of these. If a change here makes any
        // of them pass that should fail, the change is wrong.
        // =========================================================================================
        private static void Judge(JudgeConditionResult cr, JudgeStepSeries s, JudgeLiveResult r, Func<double, bool> ok, string rule, bool mustHold, bool onsetMode, DateTimeOffset? cardTime)
        {
            double? v0 = s.At(r.WindowFrom);
            if (v0 == null)
            {
                cr.Status = "NO_DATA";
                cr.Detail = rule + ": no live value in effect at the set time";
                return;
            }

            cr.HeldForSec = JudgeVerifier.HeldFor(s, r.WindowFrom, r.WindowTo, ok);

            List<JudgeSample> inWindow = s.Transitions
                .Where(t => t.Ts > r.WindowFrom && t.Ts <= r.WindowTo)
                .OrderBy(t => t.Ts)
                .ToList();

            bool held;
            DateTimeOffset onsetAt = r.WindowFrom;
            double onsetValue = v0.Value;
            bool onsetWasLater = false;

            if (!mustHold)
            {
                held = ok(v0.Value);
            }
            else if (onsetMode && !ok(v0.Value))
            {
                // Not true at the first instant. Look for where it becomes true inside the window.
                // JudgeSample is a struct, so index-based search rather than FirstOrDefault/null.
                int onsetIdx = -1;
                for (int i = 0; i < inWindow.Count; i++)
                {
                    if (ok(inWindow[i].Value))
                    {
                        onsetIdx = i;
                        break;
                    }
                }

                if (onsetIdx < 0)
                {
                    // Never true anywhere in the window. This is the genuine contradiction.
                    held = false;
                    cr.FirstBreakAt = r.WindowFrom;
                    cr.FirstBreakValue = v0;
                }
                else
                {
                    onsetAt = inWindow[onsetIdx].Ts;
                    onsetValue = inWindow[onsetIdx].Value;
                    onsetWasLater = true;
                    held = true;

                    // From the onset to the end of the window it must stay true.
                    for (int i = onsetIdx + 1; i < inWindow.Count; i++)
                    {
                        if (!ok(inWindow[i].Value))
                        {
                            held = false;
                            cr.FirstBreakAt = inWindow[i].Ts;
                            cr.FirstBreakValue = inWindow[i].Value;
                            break;
                        }
                    }
                }
            }
            else
            {
                held = ok(v0.Value);
                foreach (JudgeSample t in inWindow)
                {
                    if (!ok(t.Value))
                    {
                        held = false;
                        cr.FirstBreakAt = t.Ts;
                        cr.FirstBreakValue = t.Value;
                        break;
                    }
                }
            }

            if (!held && cr.FirstBreakAt == null)
            {
                cr.FirstBreakAt = r.WindowFrom;
                cr.FirstBreakValue = v0;
            }

            // 3.0 -- measured from the ONSET when the condition arrived during the window.
            // HeldFor() anchors at the window start, so in onset mode it reported 0s next to
            // "held through 10s" in the same sentence, which reads as a contradiction.
            if (onsetWasLater)
            {
                cr.HeldForSec = JudgeVerifier.HeldFor(s, onsetAt, r.WindowTo, ok);

                // 1.0.191.0 -- flag it, so the AI pack can mark the sustain-from-set result for this
                // same attribute as superseded. See JudgeConditionResult.OnsetUsed.
                cr.OnsetUsed = true;
            }

            cr.Status = held ? "HELD" : "LAPSED";
            string span = mustHold ? "through " + r.WindowSec + "s" : "at set";

            // When the condition arrived during the window rather than at its start, say so and say
            // whether the card agrees about when -- that agreement is the whole argument for
            // accepting it, so it belongs in the evidence rather than in a code comment.
            string onsetText = "";
            if (onsetWasLater)
            {
                onsetText = "; NOT yet true at the window start (" + JudgeUtil.F(v0, "0.###")
                    + ") -- became true at " + JudgeUtil.T(onsetAt.ToOffset(JudgeUtil.Ist), "HH:mm:ss")
                    + " (" + JudgeUtil.F(onsetValue, "0.###") + "), "
                    + JudgeUtil.F((onsetAt - r.WindowFrom).TotalSeconds, "0") + "s into the "
                    + r.WindowSec + "s window";
                if (cardTime != null)
                {
                    double gap = Math.Abs((cardTime.Value - onsetAt).TotalSeconds);
                    onsetText += gap <= 5
                        ? ", which matches the card's own measurement time " + JudgeUtil.T(cardTime.Value.ToOffset(JudgeUtil.Ist), "HH:mm:ss")
                        : "; the card stamps its measurement " + JudgeUtil.T(cardTime.Value.ToOffset(JudgeUtil.Ist), "HH:mm:ss") + " (" + JudgeUtil.F(gap, "0") + "s apart)";
                }
            }

            // times printed in IST, the card's own zone, so a reader can match them with the card
            cr.Detail = rule + ": at set=" + JudgeUtil.F(v0, "0.###") + " (" + s.StateSource + (s.StateAtStartTs != null ? ", last change " + JudgeUtil.T(s.StateAtStartTs.Value.ToOffset(JudgeUtil.Ist), "HH:mm:ss") : "") + ")"
                + (cr.LiveMin != null ? ", window min=" + JudgeUtil.F(cr.LiveMin, "0.###") + " max=" + JudgeUtil.F(cr.LiveMax, "0.###") : "")
                + " → " + (held ? "held " + span : "lapsed at " + JudgeUtil.T(cr.FirstBreakAt.Value.ToOffset(JudgeUtil.Ist), "HH:mm:ss") + " (value " + JudgeUtil.F(cr.FirstBreakValue, "0.###") + ")")
                + onsetText
                + "; stayed true for " + JudgeUtil.F(cr.HeldForSec, "0") + "s"
                + (cr.Condition != null && cr.Condition.CardValue != null ? "; card said " + cr.Condition.CardValueText : "");
        }

        /// <summary>
        /// 1.0.192.0 -- true when this card condition is a GATE that AlertV3 already satisfied,
        /// rather than the breach the alert fired on.
        ///
        /// Three conditions, all required, deliberately narrow:
        ///   1. the condition carries at least one threshold -- a relay or a bare value is not a
        ///      gate in this sense and is checked as before;
        ///   2. at least one of those thresholds states a status, so we know this card DOES use
        ///      PASS/FAIL marks. A card that marks nothing (the AlertV3 ProcessJson shape, or an
        ///      older builder) falls through to the normal check, so this can never silently
        ///      switch the live re-check off;
        ///   3. NONE of them is marked FAIL. If AlertV3 raised the alert, something on the card
        ///      fired; a condition with no FAIL anywhere is by definition not that something.
        ///
        /// Enabling conditions are excluded: they are the relay preconditions, they carry no
        /// thresholds, and CheckRelayAsync already treats them on their own terms.
        /// </summary>
        private static bool IsSatisfiedGate(JudgeCardCondition cond)
        {
            if (cond == null || cond.Thresholds == null || cond.Thresholds.Count == 0)
            {
                return false;
            }

            if (string.Equals(cond.Role, "enabling", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            bool anyStatus = false;
            for (int i = 0; i < cond.Thresholds.Count; i++)
            {
                string st = (cond.Thresholds[i].Status ?? "").Trim();
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

            return anyStatus;
        }

        /// <summary>The gate's own threshold labels, for the remark -- e.g. "Discharging gate (&lt; 0)".</summary>
        private static string GateLabels(JudgeCardCondition cond)
        {
            List<string> parts = new List<string>();
            for (int i = 0; i < cond.Thresholds.Count; i++)
            {
                JudgeCardThreshold th = cond.Thresholds[i];
                string lbl = (th.Label ?? "").Trim();
                string st = (th.Status ?? "").Trim();
                if (lbl.Length == 0)
                {
                    lbl = (th.Comparison ?? "").Trim() + " " + JudgeUtil.F(th.Value, "0.###");
                }

                parts.Add(st.Length > 0 ? lbl + " " + st : lbl);
            }

            return string.Join("; ", parts);
        }
    }
}
