using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace E7.AiCore.Judge
{
    /// <summary>
    /// The one door to the MCP tool server. Production: JudgeMcpDomain (Domain McpClient + E7 token,
    /// the client AiChat runs). Tests: a fake that answers from fixture files.
    /// </summary>
    internal interface IJudgeMcp
    {
        /// <summary>Returns the tool result text (JSON) or throws. Never null.</summary>
        Task<string> CallToolAsync(string toolName, JObject args, CancellationToken ct);
    }

    internal sealed class JudgeToolResult
    {
        public string Tool;
        public bool Ok;
        public string Text = "";
        public JToken Data;
        public long Ms;
        public string Error;
    }

    // ============================================================================================
    // JudgeEvidence -- the MCP history fetch. Port of AlertJudge v1.1.2.0 Pipeline/EvidenceFetcher.cs
    // plus McpClient.CallToolAsync's result handling (isError / e7_session_expired / JSON parse).
    //
    // CHANGE HERE when an MCP tool name, argument or result shape changes (the names themselves are
    // in judge-settings.json: Mcp.Tools and Mcp.ArgNames).
    //
    // Frame = [alert time, reset ?? now] capped at MaxFrameHours. For every catalog attr:
    //   search_tags  -> does the asset have this attr (P3)?
    //   history_get  [frame - LookbackHours, frameStart] sort desc limit 1 -> the value IN EFFECT at frame start
    //   history_get  [frameStart, frameEnd]                                -> transitions inside the frame
    // If history has nothing before the frame, the card's own value + device time seeds the state.
    // 2.0 adds: FetchCardConditionsAsync -- the same two reads for every attribute the CARD's
    // conditions name (by name -> attid through the catalog), used by the live re-check.
    // ============================================================================================
    internal sealed class JudgeEvidence
    {
        private const int TprAttid = 6;
        private readonly IJudgeMcp mcp;
        private readonly JudgeSettings cfg;

        internal JudgeEvidence(IJudgeMcp mcp, JudgeSettings cfg)
        {
            this.mcp = mcp;
            this.cfg = cfg;
        }

        internal async Task<JudgeEvidenceBundle> FetchAsync(JudgeCard card, JudgeCauseDef def, List<JudgeCauseDef> stage2, List<int> familyTrackIds, List<string> log, CancellationToken ct)
        {
            DateTimeOffset start = card.TriggeredAt;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset end = card.ResetAt ?? now;
            if (end > start.AddHours(cfg.MaxFrameHours))
            {
                end = start.AddHours(cfg.MaxFrameHours);
            }

            if (end < start)
            {
                end = start;
            }

            JudgeEvidenceBundle b = new JudgeEvidenceBundle();
            b.From = start;
            b.To = end;
            log.Add("evidence: frame " + JudgeUtil.T(start, "u") + " → " + JudgeUtil.T(end, "u") + " (" + JudgeUtil.F((end - start).TotalMinutes, "0.#") + " min; " + (card.ResetAt != null ? "to reset" : "to now") + "); carry-forward lookback " + cfg.LookbackHours + "h");

            JObject frsArgs = new JObject();
            frsArgs[cfg.ArgSiteIds] = new JArray((long)card.SiteId);
            frsArgs[cfg.ArgPageSize] = 20;
            JudgeToolResult frs = await CallAsync(cfg.ToolGetFrsAlerts, frsArgs, ct).ConfigureAwait(false);
            if (frs.Ok && frs.Data != null)
            {
                b.FrsAlert = frs.Data;
            }

            if (def == null)
            {
                b.Degraded = true;
                b.DegradedInputs.Add("no CauseDef");
                return b;
            }

            List<JudgeCauseAttr> attrs = new List<JudgeCauseAttr>(def.Attrs);
            foreach (JudgeCauseDef o in stage2)
            {
                attrs.AddRange(o.Attrs);
            }

            List<JudgeCauseAttr> wanted = attrs.Where(a => a.Attid > 0).GroupBy(a => a.Attid).Select(g => g.First()).ToList();

            foreach (JudgeCauseAttr a in wanted)
            {
                ct.ThrowIfCancellationRequested();
                char role = RoleFor(a.Datatype);

                // 2.3 -- a derived attribute (IBALST 684, ITC BATT CHARG 585, the RTC/RRAIL ratios)
                // has no sensor of its own, so there is nothing to resolve and nothing to fetch.
                // Saying "not on asset" for one of these was wrong and sent people looking for a
                // mapping that was never meant to exist.
                if (JudgeAttrFacts.IsDerived(a.Attid))
                {
                    b.TagPresent[a.Attid] = false;
                    b.Derived.Add(a.Attid);
                    log.Add("evidence: attid " + a.Attid + " '" + a.Name + "' is DERIVED ("
                        + JudgeAttrFacts.FormulaText(a.Attid) + ") → computed, not fetched");
                    continue;
                }

                long? tagId = await ResolveTagAsync(card.SiteId, card.AssetId, card.AssetName, a.Attid, role, log, ct).ConfigureAwait(false);
                b.TagPresent[a.Attid] = tagId != null;
                if (tagId == null)
                {
                    log.Add("evidence: attid " + a.Attid + " '" + a.Name + "' not on asset → SKIPPED");
                    continue;
                }

                JudgeStepSeries step = await StepSeriesAsync(a.Name, a.Attid, role, tagId.Value, start, end, ct, log).ConfigureAwait(false);

                if (step.StateAtStart == null)
                {
                    string cardName = card.AttributeIds.FirstOrDefault(kv => kv.Value == a.Attid).Key ?? a.Name;
                    double cv;
                    int rv;
                    bool have = false;
                    cv = 0;
                    if (cardName != null)
                    {
                        if (card.Values.TryGetValue(cardName, out cv))
                        {
                            have = true;
                        }
                        else if (card.Relays.TryGetValue(cardName, out rv))
                        {
                            cv = rv;
                            have = cv >= 0;
                        }
                    }

                    if (have)
                    {
                        step.StateAtStart = cv;
                        step.StateSource = "card";
                        DateTimeOffset vt;
                        step.StateAtStartTs = card.ValueTimes.TryGetValue(cardName, out vt) ? vt : start;
                    }
                }

                b.Steps[a.Attid] = step;
                JudgeTagSeries flat = new JudgeTagSeries();
                flat.Attribute = a.Name;
                flat.Attid = a.Attid;
                flat.TagId = tagId.Value;
                flat.RoleType = role;
                if (step.StateAtStart != null)
                {
                    flat.Samples.Add(new JudgeSample(step.StateAtStartTs ?? start, step.StateAtStart.Value));
                }

                flat.Samples.AddRange(step.Transitions);
                b.Series["attid:" + a.Attid] = flat;
                if (!b.Series.ContainsKey(a.Name))
                {
                    b.Series[a.Name] = flat;
                }

                log.Add("evidence: " + a.Name + " attid=" + a.Attid + " tag=" + tagId + " stateAtStart=" + (step.StateAtStart != null ? JudgeUtil.F(step.StateAtStart, "0.###") : "none") + " (" + step.StateSource + " @ " + JudgeUtil.T(step.StateAtStartTs, "HH:mm:ss") + ") transitions=" + step.Transitions.Count + " last=" + (step.Last != null ? JudgeUtil.F(step.Last, "0.###") : "-"));

                bool needsAvg = attrs.Any(x => x.Attid == a.Attid && (x.EffectiveThreshold ?? "").IndexOf("avg", StringComparison.OrdinalIgnoreCase) >= 0);
                if (needsAvg && role != 'd')
                {
                    List<JudgeSample> basel = await HistoryAsync(a.Name + " baseline(" + cfg.BaselineHours + "h)", tagId.Value, start.AddHours(-cfg.BaselineHours), start, ct, true, null, log).ConfigureAwait(false);
                    if (basel != null && basel.Count > 0)
                    {
                        b.BaselineAvg[a.Attid] = basel.Average(x => x.Value);
                        log.Add("evidence: baseline avg " + a.Name + " = " + JudgeUtil.F(b.BaselineAvg[a.Attid], "0.###") + " (" + basel.Count + " samples / " + cfg.BaselineHours + "h)");
                    }
                    else
                    {
                        b.DegradedInputs.Add(a.Name + ": baseline avg unavailable");
                    }
                }
            }

            foreach (int adjId in familyTrackIds.Distinct().Where(id => id != card.AssetId))
            {
                long? tagId = await ResolveTagAsync(card.SiteId, adjId, null, TprAttid, cfg.RoleDataLogger[0], log, ct).ConfigureAwait(false);
                if (tagId == null)
                {
                    log.Add("evidence: adjacent track " + adjId + " has no TPR tag");
                    continue;
                }

                JudgeStepSeries step = await StepSeriesAsync("TPR@" + adjId, TprAttid, cfg.RoleDataLogger[0], tagId.Value, start, end, ct, log).ConfigureAwait(false);
                b.AdjacentTpr[adjId] = step;
                log.Add("evidence: adjacent " + adjId + " TPR stateAtStart=" + JudgeUtil.R(step.StateAtStart) + " transitions=" + step.Transitions.Count);
            }

            b.Degraded = b.DegradedInputs.Count > 0;
            return b;
        }

        /// <summary>
        /// 2.0: history for one named card attribute (resolved to attid by JudgeLiveCheck) over
        /// [windowFrom - lookback, windowTo]. Returns null when the asset has no such tag.
        /// </summary>
        internal async Task<JudgeStepSeries> FetchAttrAsync(JudgeCard card, string name, int attid, char role, DateTimeOffset from, DateTimeOffset to, List<string> log, CancellationToken ct)
        {
            long? tagId = await ResolveTagAsync(card.SiteId, card.AssetId, card.AssetName, attid, role, log, ct).ConfigureAwait(false);
            if (tagId == null)
            {
                return null;
            }

            return await StepSeriesAsync(name, attid, role, tagId.Value, from, to, ct, log).ConfigureAwait(false);
        }

        // ------------------------------------------------------------------------------------------
        private async Task<JudgeStepSeries> StepSeriesAsync(string name, int attid, char role, long tagId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct, List<string> log)
        {
            JudgeStepSeries step = new JudgeStepSeries();
            step.Attribute = name;
            step.Attid = attid;
            step.TagId = tagId;
            step.RoleType = role;

            List<JudgeSample> before = await HistoryAsync(name + " carry-forward(" + cfg.LookbackHours + "h)", tagId, start.AddHours(-cfg.LookbackHours), start, ct, true, 1, log).ConfigureAwait(false);
            if (before != null && before.Count > 0)
            {
                JudgeSample last = before.OrderByDescending(x => x.Ts).First();
                step.StateAtStart = last.Value;
                step.StateAtStartTs = last.Ts;
                step.StateSource = "history";
            }

            List<JudgeSample> inside = await HistoryAsync(name + " in-frame", tagId, start, end, ct, false, null, log).ConfigureAwait(false);
            if (inside != null)
            {
                step.Transitions = inside.OrderBy(x => x.Ts).ToList();
                if (step.StateAtStart == null && step.Transitions.Count > 0 && step.Transitions[0].Ts <= start.AddSeconds(2))
                {
                    step.StateAtStart = step.Transitions[0].Value;
                    step.StateAtStartTs = step.Transitions[0].Ts;
                    step.StateSource = "history";
                }
            }

            return step;
        }

        private async Task<long?> ResolveTagAsync(int siteId, int assetId, int attid, char role, List<string> log, CancellationToken ct)
        {
            return await ResolveTagAsync(siteId, assetId, null, attid, role, log, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 2.3 -- resolve one attid to its historian TagID.
        ///
        /// THE FIX. search_tags does NOT filter on AttributeId or RoleType: the MCP server
        /// (v1.5.55.1) reads AssetName (ILIKE) and, to narrow to one attribute, AttributeName
        /// (ILIKE) -- documented in AnalysisEngine.Grounding.Tags.cs v1.0.79.0, which hit the same
        /// wall. Sending ids meant the server applied NO filter and returned an arbitrary page of
        /// tags; resolution then came down to whether the wanted attribute happened to be in that
        /// page. On GLTA 04AT (asset 41869) attid 6 and attid 569 landed in it and resolved, while
        /// attid 2 -- Ir mA, which the asset plainly has, TagID 752119432408662018 -- did not, and
        /// was reported as "attr not on this asset". That single mis-shaped call is what suppressed
        /// a real TC SHORT.
        ///
        /// AttributeName must be the REAL tag Title ("Ir mA"), never the card's label
        /// ("ITC RELAY END(mA)") -- an ILIKE on the label filters the correct tag out.
        /// </summary>
        private async Task<long?> ResolveTagAsync(int siteId, int assetId, string assetName, int attid, char role, List<string> log, CancellationToken ct)
        {
            // 2.3 -- a derived attribute has no sensor of its own; never ask the historian for one.
            if (JudgeAttrFacts.IsDerived(attid))
            {
                log.Add("evidence: attid " + attid + " is DERIVED (" + JudgeAttrFacts.FormulaText(attid)
                    + ") -- computed, not a tag; search_tags not called");
                return null;
            }

            JObject args = new JObject();
            args[cfg.ArgSiteId] = siteId;
            args[cfg.ArgAssetId] = assetId;

            // the filters the server actually honours
            if (!string.IsNullOrWhiteSpace(assetName))
            {
                args[cfg.ArgAssetName] = assetName.Trim();
            }

            string title = null;
            if (role != cfg.RoleDataLogger[0])
            {
                JudgeAttrFacts.AnalogTagTitle.TryGetValue(attid, out title);
            }

            if (title == null)
            {
                JudgeAttrFacts.TagTitle.TryGetValue(attid, out title);
            }

            if (!string.IsNullOrWhiteSpace(title))
            {
                args[cfg.ArgAttributeName] = title;
            }

            JudgeToolResult r = await CallAsync(cfg.ToolSearchTags, args, ct).ConfigureAwait(false);
            if (!r.Ok || r.Data == null)
            {
                log.Add("evidence: search_tags asset=" + assetId + " attid=" + attid + " role=" + role + " failed (" + r.Error + ")");
                return null;
            }

            JToken d = r.Data;
            JToken arr = d;
            if (JudgeUtil.IsObject(d))
            {
                foreach (string k in new[] { "tags", "Tags", "items", "rows" })
                {
                    JToken t = JudgeUtil.Prop(d, k);
                    if (JudgeUtil.IsArray(t))
                    {
                        arr = t;
                        break;
                    }
                }
            }

            if (JudgeUtil.IsArray(arr))
            {
                // 2.3 -- MATCHING, corrected. "AssetAttributeId" IS the attribute id in this
                // schema -- a live /api/liveValue/41869 row reads
                // { AssetAttributeId: 2, AssetAttributeName: "Ir mA", TagID: 752119432408662018 }
                // and there is no "AttributeId" field at all. (A 1.0.176.0 change read
                // "AttributeId" first on the wrong assumption that AssetAttributeId was a mapping
                // row PK; it was reverted here.) search_tags matches by NAME and can return rows
                // for other assets, so both AssetId and AssetAttributeId are checked, and the row
                // whose DataType matches the role we asked for wins -- asset 41869 carries TWO rows
                // for AssetAttributeId 6, "TPR" (DataLogger) and "TPR V" (RDPMS).
                int rows = 0;
                int otherAsset = 0;
                int otherAttr = 0;
                long fallback = 0;
                bool haveFallback = false;
                string wantType = role == cfg.RoleDataLogger[0] ? "DATALOGGER" : "RDPMS";

                // 1.0.189.0 -- record what this asset DOES have, for the failure message below.
                // WHY: on 11.10.2026 point machine 41898 (PT-115/116) reported "3 row(s), 0 for
                // another asset, 3 for another attribute -> no tag" for NWCR, RWCR, RWKR, NWKR AND
                // VIPS DC R EXT -- five different attids, five identical counts, because none of the
                // five has an entry in JudgeAttrFacts.TagTitle, so all five calls went out
                // byte-identical with no AttributeName filter at all. The log could not say whether
                // those 3 rows were a truncated unfiltered page or the asset's entire mapping, and
                // that is precisely the question that decides whether this is a code fault or a
                // site mapping gap. Now the log answers it.
                List<string> present = new List<string>();

                foreach (JToken e in arr)
                {
                    rows++;

                    int ra;
                    if (JudgeUtil.TryInt(e, out ra, "AssetId", "assetId") && ra != assetId)
                    {
                        otherAsset++;
                        continue;
                    }

                    int aa;
                    if (JudgeUtil.TryInt(e, out aa, "AssetAttributeId", "assetAttributeId", "AttributeId") && aa != attid)
                    {
                        otherAttr++;
                        if (present.Count < 40)
                        {
                            JToken nmt = JudgeUtil.Prop(e, "AssetAttributeName") ?? JudgeUtil.Prop(e, "assetAttributeName")
                                      ?? JudgeUtil.Prop(e, "AttributeName");
                            present.Add(aa + (nmt != null ? ":'" + nmt.ToString().Trim() + "'" : ""));
                        }
                        continue;
                    }

                    long id;
                    if (!JudgeUtil.TryLong(e, out id, "TagID", "TagId", "tagId"))
                    {
                        continue;
                    }

                    JToken dt = JudgeUtil.Prop(e, "DataType") ?? JudgeUtil.Prop(e, "dataType");
                    string type = dt != null ? dt.ToString().Trim().ToUpperInvariant() : null;
                    if (type == null || type == wantType)
                    {
                        log.Add("evidence: search_tags asset=" + assetId + " attid=" + attid + " role=" + role
                            + " -> " + rows + " row(s), tag=" + id + (type != null ? " (" + type + ")" : ""));
                        return id;
                    }

                    // right asset, right attribute, wrong datatype -- keep it only as a last resort
                    if (!haveFallback)
                    {
                        fallback = id;
                        haveFallback = true;
                    }
                }

                if (haveFallback)
                {
                    log.Add("evidence: search_tags asset=" + assetId + " attid=" + attid + " role=" + role
                        + " -> no " + wantType + " row; using the other datatype's tag=" + fallback);
                    return fallback;
                }

                // 2.3 -- say WHY nothing resolved, so the trace answers it without a manual MCP call.
                // 1.0.189.0 -- and name the attributes that ARE on the asset, plus whether this call
                // could narrow at all. Without the title the server gets no AttributeName filter and
                // returns an arbitrary page, so "not on asset" may mean "not in the page I was sent".
                bool narrowed = !string.IsNullOrWhiteSpace(title);
                log.Add("evidence: search_tags asset=" + assetId + " attid=" + attid + " role=" + role
                    + " -> " + rows + " row(s), " + otherAsset + " for another asset, " + otherAttr
                    + " for another attribute -> no tag"
                    + (narrowed ? "" : "  [no AttributeName filter sent: attid " + attid
                        + " has no JudgeAttrFacts.TagTitle entry, so this page is whatever the server chose]")
                    + (present.Count > 0 ? "; this asset reported attids " + string.Join(", ", present.ToArray()) : ""));
                return null;
            }

            long single;
            if (JudgeUtil.TryLong(d, out single, "TagID", "TagId", "tagId"))
            {
                return single;
            }

            return null;
        }

        /// <summary>
        /// 2.5 -- every history_get, logged.
        ///
        /// WHY: this method used to end `if (!r.Ok || r.Data == null) return null;` with the error
        /// text THROWN AWAY and no log line at all. A failed call and a genuinely empty window were
        /// indistinguishable from that point on -- both surfaced as "no live value before or inside
        /// the window". Three alerts (1263751, 1137362, 1034216) all showed zero rows on an asset
        /// whose tags demonstrably hold data, and there was no way to tell whether the historian
        /// said "nothing here", refused the request, or errored.
        ///
        /// Now every call writes one line naming the tag, the EXACT StartDate/EndDate strings sent
        /// (the timestamp convention is a live suspect: SendUtc=true means IST is converted to UTC
        /// before formatting), and the outcome: how many rows, or EMPTY, or FAILED with the reason.
        /// </summary>
        private async Task<List<JudgeSample>> HistoryAsync(string who, long tagId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct, bool sortDesc, int? limit, List<string> log)
        {
            string startStamp = Stamp(from);
            string endStamp = Stamp(to);
            JObject args = new JObject();
            args[cfg.ArgTagId] = tagId;
            args[cfg.ArgStartDate] = startStamp;
            args[cfg.ArgEndDate] = endStamp;
            args[cfg.ArgLimit] = limit ?? cfg.RowLimit;
            args[cfg.ArgOutputMode] = cfg.OutputMode;
            args[cfg.ArgSort] = sortDesc ? "desc" : "asc";

            string call = "history_get " + (who ?? "?") + " tag=" + tagId
                + " sent[" + startStamp + " → " + endStamp + "]"
                + " (IST " + JudgeUtil.T(from.ToOffset(TimeSpan.FromMinutes(cfg.IstOffsetMinutes)), "dd/MM HH:mm:ss")
                + " → " + JudgeUtil.T(to.ToOffset(TimeSpan.FromMinutes(cfg.IstOffsetMinutes)), "dd/MM HH:mm:ss")
                + ", SendUtc=" + cfg.SendUtc + ", fmt=" + cfg.StampFormat + ")"
                + " sort=" + (sortDesc ? "desc" : "asc") + " limit=" + (limit ?? cfg.RowLimit);

            JudgeToolResult r = await CallAsync(cfg.ToolHistoryGet, args, ct).ConfigureAwait(false);

            // THE FAILURE, named. An error here is a completely different finding from an empty
            // window and must never again be silently folded into "no data".
            if (!r.Ok)
            {
                Note(log, "ERROR", call + " → FAILED after " + r.Ms + "ms: " + (r.Error ?? "(no error text)")
                    + (string.IsNullOrEmpty(r.Text) ? "" : " | reply: " + JudgeUtil.Trunc(r.Text, 300)));
                return null;
            }

            if (r.Data == null)
            {
                Note(log, "WARN", call + " → no parsable JSON after " + r.Ms + "ms | reply: " + JudgeUtil.Trunc(r.Text, 300));
                return null;
            }

            // 2.6 -- THE GUARD. The server echoes back the from/to it actually applied. If that
            // disagrees with what we asked for, the request was misunderstood and the evidence is
            // for the wrong period -- which is far better to see as a WARN than to read as "no
            // data". AnalysisEngine has had this check (CheckHistWindowEcho) since the same class of
            // bug bit it; the judge did not, which is why a mis-spelled parameter name stayed
            // invisible through five builds.
            EchoCheck(r.Data, startStamp, endStamp, who, log);

            List<JudgeSample> rows = ParseSamples(r.Data);
            if (rows == null || rows.Count == 0)
            {
                // Empty is legitimate (a change-logged tag with no change in the window) but it is
                // also what a wrong window looks like, so print the raw reply to tell them apart.
                Note(log, "WARN", call + " → EMPTY (0 rows) after " + r.Ms + "ms | reply: " + JudgeUtil.Trunc(r.Text, 300));
                return rows;
            }

            Note(log, "INFO", call + " → " + rows.Count + " row(s) after " + r.Ms + "ms, first "
                + JudgeUtil.T(rows[0].Ts, "dd/MM HH:mm:ss") + "=" + JudgeUtil.R(rows[0].Value)
                + ", last " + JudgeUtil.T(rows[rows.Count - 1].Ts, "dd/MM HH:mm:ss") + "=" + JudgeUtil.R(rows[rows.Count - 1].Value));

            return WindowGuard(rows, from, to, who, call, log, cfg.LookbackHours);
        }

        // =========================================================================================
        // 2.8 -- THE WINDOW GUARD, narrowed in 2.9.
        //
        // WHAT I GOT WRONG IN 2.8. The guard was built to stop this verdict:
        //
        //   1137362 -> SUPPRESSED by relay-gate, "False alarm", confidence 85, on rows stamped
        //   27/09 18:13:11 for a window of 28/09 02:28:57 - 02:57:36
        //
        // and I read those rows as junk the server had invented. They were not. The server returns
        // THE LAST ROW BEFORE THE WINDOW as the state at the window start, which is exactly what a
        // step series needs and exactly what the judge's own carry-forward fetch asks for. Those
        // rows were a correct carry-forward of a window that was the wrong 5 h 30 m. The WINDOW was
        // the bug -- fixed by History.SendUtc = false and detected by EchoCheck above.
        //
        // So 2.8 dropped data it should keep. On the 09.10.2026 run it threw away TPR picking up at
        // 07:57:48 -- 69 s before a window starting 07:58:57, i.e. precisely the anchor that says
        // TPR was UP at the set instant -- and logged an alarming OUT OF WINDOW line for it. No harm
        // resulted, because the judge refetches the anchor separately, but a guard that discards
        // good evidence and shouts about it is worse than no guard.
        //
        // WHAT IS ACTUALLY WORTH GUARDING. Only one thing: a row stamped AFTER the window ended
        // cannot describe what happened inside it, under any convention. Those are dropped.
        //
        // A row BEFORE the window start is kept, because it is the anchor. It is reported when the
        // nearest one is older than the carry-forward lookback, since an anchor that stale is weak
        // evidence and the reader should know -- but it is never silently discarded.
        // =========================================================================================
        private static List<JudgeSample> WindowGuard(List<JudgeSample> rows, DateTimeOffset from, DateTimeOffset to, string who, string call, List<string> log, int lookbackHours)
        {
            // A second of slack at each edge: a server that rounds to whole seconds must not lose a
            // boundary sample to a rounding difference.
            DateTimeOffset lo = from.AddSeconds(-1);
            DateTimeOffset hi = to.AddSeconds(1);

            List<JudgeSample> kept = new List<JudgeSample>();
            List<double> after = new List<double>();
            double? nearestBefore = null;

            for (int i = 0; i < rows.Count; i++)
            {
                JudgeSample s = rows[i];
                if (s.Ts > hi)
                {
                    // Cannot inform the window. This is the only case that is dropped.
                    after.Add((s.Ts - to).TotalSeconds);
                    continue;
                }

                if (s.Ts < lo)
                {
                    double age = (from - s.Ts).TotalSeconds;
                    if (nearestBefore == null || age < nearestBefore.Value)
                    {
                        nearestBefore = age;
                    }
                }

                kept.Add(s);
            }

            if (after.Count > 0)
            {
                Note(log, "WARN", call + " → " + after.Count + " of " + rows.Count + " row(s) are stamped"
                    + " AFTER the window ended (by up to " + JudgeUtil.F(after.Max(), "0") + "s) and were"
                    + " dropped: a reading from after the window cannot describe what happened inside it."
                    + " If this is every row, compare the WINDOW SKEW line above -- the window the"
                    + " server applied is probably not the one that was asked for.");
            }

            // A stale anchor is reported, never dropped. The gate logic already treats a long-unchanged
            // value as still in effect, which is correct for a change-logged historian; this line just
            // makes it visible that the "value at set" came from that far back.
            if (nearestBefore != null && nearestBefore.Value > lookbackHours * 3600.0)
            {
                Note(log, "WARN", call + " → the nearest reading before the window is "
                    + JudgeUtil.F(nearestBefore.Value / 3600.0, "0.#") + "h old, beyond the "
                    + lookbackHours + "h carry-forward lookback. It is still used as the value in"
                    + " effect at the set instant, but it is weak evidence -- the tag may have stopped"
                    + " reporting.");
            }

            return after.Count > 0 ? kept : rows;
        }

        /// <summary>
        /// 2.6 -- does the window the server says it used match the one we sent? Any "from"/"to"
        /// style field in the reply is compared as text against the stamps we sent; a difference is
        /// reported once per call. Purely diagnostic -- it never changes a verdict.
        /// </summary>
        // =========================================================================================
        // 2.8 -- DECODE THE ECHO. It comes back as a Unix epoch, which is unreadable next to a
        // ddMMyyyy_HHmmss string, so 1.0.183.0's warning could only say "a different period".
        //
        // It is worth more than that. On 09.10.2026 all eight windows across two alerts echoed
        // EXACTLY 19800 s -- 5 h 30 m, the IST offset -- below the stamp read as UTC:
        //
        //   sent 28092026_022857 (= 28/09 02:28:57 UTC = 07:58:57 IST, the alert time)
        //   echo 1790542737      (= 28/09 02:28:57 IST = 27/09 20:58:57 UTC)
        //
        // The server parses the stamp as IST LOCAL. We had already converted to UTC, so another
        // 5 h 30 m came off and every window landed 5 h 30 m before the event. The spans were always
        // right (1719 s and 346 s, both correct) -- only the position was wrong, which is exactly
        // why the returned data looked plausible and was read as disproof of a true alert.
        //
        // The fix is History.SendUtc = false, so this now says that in the log rather than leaving
        // the next person to work out what the epoch means. Still purely diagnostic: it never
        // changes a verdict. The guard that keeps bad rows out of a verdict is WindowGuard above.
        // =========================================================================================
        private static string Decode(string sentFrom, string gotFrom, string sentTo, string gotTo, bool sentAsUtc, int istOffsetMinutes)
        {
            try
            {
                double? a = Shift(sentFrom, gotFrom, sentAsUtc, istOffsetMinutes);
                double? b = Shift(sentTo, gotTo, sentAsUtc, istOffsetMinutes);
                double? shift = a ?? b;
                if (shift == null)
                {
                    return "";
                }

                // Both edges off by the same amount = the whole window is displaced, and its span is
                // intact. That distinguishes "the server misread our timestamps" from "the server
                // applied a different window".
                bool uniform = a != null && b != null && Math.Abs(a.Value - b.Value) < 2;
                string head = " [echo decodes to " + JudgeUtil.T(DateTimeOffset.FromUnixTimeSeconds((long)double.Parse(gotFrom ?? "0", CultureInfo.InvariantCulture)).ToOffset(JudgeUtil.Ist), "dd/MM HH:mm:ss")
                    + " IST, " + JudgeUtil.F(shift, "0") + "s from what was sent"
                    + (uniform ? ", same at both edges so the span is intact" : "") + "]";

                // 2.9 -- only when we sent UTC. If SendUtc is already false and the shift is still
                // 5h30m, the cause is the opposite one and the cure is the opposite setting; saying
                // "set it to false" then would be actively misleading.
                if (uniform && Math.Abs(Math.Abs(shift.Value) - 19800) < 120)
                {
                    head += sentAsUtc
                        ? " THE SHIFT IS THE IST OFFSET (5h30m): the server is reading the stamp as"
                          + " IST local, not UTC. Set History.SendUtc = false in judge-settings.json."
                        : " THE SHIFT IS THE IST OFFSET (5h30m) but History.SendUtc is ALREADY false,"
                          + " so the server is reading the stamp as UTC after all. Set History.SendUtc"
                          + " = true in judge-settings.json.";
                }

                return head;
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Seconds between the instant the stamp MEANT and the echoed epoch.
        ///
        /// 2.9 -- "the instant the stamp meant" is the fix. This read every stamp as UTC, which was
        /// right only while SendUtc was true. With SendUtc=false the stamp is the IST wall clock, so
        /// reading it as UTC invented a 19800 s shift on every single call and then printed
        /// "Set History.SendUtc = false" at someone who had already done exactly that -- 40 such
        /// lines in the 09.10.2026 log, every one of them wrong, while the window was in fact
        /// correct to the second:
        ///
        ///   sent 28092026_075857 as IST = epoch 1790562537, and the server echoed 1790562537.
        ///
        /// A diagnostic that cries wolf is worse than none: it trains you to ignore the line that
        /// will matter next time. The stamp must be read exactly the way Stamp() wrote it.
        /// </summary>
        private static double? Shift(string sent, string got, bool sentAsUtc, int istOffsetMinutes)
        {
            if (string.IsNullOrWhiteSpace(sent) || string.IsNullOrWhiteSpace(got))
            {
                return null;
            }

            long epoch;
            if (!long.TryParse(got.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out epoch))
            {
                return null;   // not an epoch echo; nothing to decode
            }

            DateTime dt;
            if (!DateTime.TryParseExact(sent.Trim(), "ddMMyyyy_HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            {
                return null;
            }

            // Stamp() writes UTC when SendUtc is true and the IST wall clock when it is false, so
            // the offset to attach here is the mirror of that choice.
            DateTimeOffset meant = new DateTimeOffset(dt, sentAsUtc ? TimeSpan.Zero : TimeSpan.FromMinutes(istOffsetMinutes));
            return (meant - DateTimeOffset.FromUnixTimeSeconds(epoch)).TotalSeconds;
        }

        private void EchoCheck(JToken data, string sentFrom, string sentTo, string who, List<string> log)
        {
            try
            {
                if (!JudgeUtil.IsObject(data))
                {
                    return;
                }

                string gotFrom = null;
                string gotTo = null;
                foreach (string k in new[] { "StartDate", "startDate", "from", "From", "start", "Start" })
                {
                    JToken v = JudgeUtil.Prop(data, k);
                    if (v != null && v.Type != JTokenType.Null) { gotFrom = v.ToString(); break; }
                }

                foreach (string k in new[] { "EndDate", "endDate", "to", "To", "end", "End" })
                {
                    JToken v = JudgeUtil.Prop(data, k);
                    if (v != null && v.Type != JTokenType.Null) { gotTo = v.ToString(); break; }
                }

                if (gotFrom == null && gotTo == null)
                {
                    return;   // this server does not echo the window; nothing to check
                }

                bool fromOk = gotFrom == null || gotFrom.IndexOf(sentFrom, StringComparison.OrdinalIgnoreCase) >= 0 || sentFrom.IndexOf(gotFrom, StringComparison.OrdinalIgnoreCase) >= 0;
                bool toOk = gotTo == null || gotTo.IndexOf(sentTo, StringComparison.OrdinalIgnoreCase) >= 0 || sentTo.IndexOf(gotTo, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!fromOk || !toOk)
                {
                    // 3.0 -- the string compare can only ever fail here, because the echo is a Unix
                    // epoch and what we sent is ddMMyyyy_HHmmss. Decode the echo first: when it
                    // resolves to the very instant we asked for, there is no skew and nothing to
                    // report. 1.0.185.0 skipped this and logged 47 WINDOW SKEW warnings per run
                    // saying "a different period" while its own decoded text said "0s from what was
                    // sent" two clauses later. A warning that is always wrong is worse than none:
                    // it trains the reader to skip the line that will matter.
                    double? dFrom = Shift(sentFrom, gotFrom, cfg.SendUtc, cfg.IstOffsetMinutes);
                    double? dTo = Shift(sentTo, gotTo, cfg.SendUtc, cfg.IstOffsetMinutes);
                    bool noSkew = (dFrom != null || dTo != null)
                        && (dFrom == null || Math.Abs(dFrom.Value) < 1.0)
                        && (dTo == null || Math.Abs(dTo.Value) < 1.0);
                    if (noSkew)
                    {
                        return;
                    }

                    Note(log, "WARN", "history_get " + (who ?? "?") + " WINDOW SKEW -- sent ["
                        + sentFrom + " → " + sentTo + "] but the server echoed ["
                        + (gotFrom ?? "?") + " → " + (gotTo ?? "?") + "]"
                        + Decode(sentFrom, gotFrom, sentTo, gotTo, cfg.SendUtc, cfg.IstOffsetMinutes)
                        + "; the history returned is for a different period than asked for");
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// 2.5 -- one line into BOTH sinks: the per-request trace (so it reaches the response and the
        /// audit .jsonl) and the leveled ai-log, where it can be grepped by level across requests.
        ///
        /// 2.8 -- tagged TRACE, not JUDGE. Every line added to the trace is flushed to JudgeAIlogs
        /// as one block at the end of the judgement (JudgeEngine), so a line sent here under tag
        /// JUDGE landed in that file TWICE -- which is why the 09.10.2026 log repeated every
        /// history_get and WINDOW SKEW line. JudgeHost sends TRACE to the ai-log only, so nothing
        /// that greps ai-log-*.txt loses a line and JudgeAIlogs stops doubling.
        /// </summary>
        private static void Note(List<string> log, string level, string message)
        {
            if (log != null)
            {
                log.Add((level == "INFO" ? "evidence: " : "evidence " + level + ": ") + message);
            }

            JudgeLog.Write(level, "TRACE", message);
        }

        private string Stamp(DateTimeOffset t)
        {
            DateTime ist = t.ToOffset(TimeSpan.FromMinutes(cfg.IstOffsetMinutes)).DateTime;
            DateTime send = cfg.SendUtc ? ist - TimeSpan.FromMinutes(cfg.IstOffsetMinutes) : ist;
            return send.ToString(cfg.StampFormat, CultureInfo.InvariantCulture);
        }

        internal char RoleFor(string datatype)
        {
            switch ((datatype ?? "").ToUpperInvariant())
            {
                case "DATALOGGER":
                    return cfg.RoleDataLogger[0];
                case "IPS":
                    return cfg.RoleIps[0];
                default:
                    return cfg.RoleRdpms[0];
            }
        }

        // ------------------------------------------------------------------------------------------
        // MCP call + result handling (McpClient.CallToolAsync of 1.1.2.0)
        // ------------------------------------------------------------------------------------------
        private async Task<JudgeToolResult> CallAsync(string toolName, JObject args, CancellationToken ct)
        {
            JudgeToolResult res = new JudgeToolResult();
            res.Tool = toolName;
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                string text = await mcp.CallToolAsync(toolName, args, ct).ConfigureAwait(false) ?? "";
                sw.Stop();
                res.Ms = sw.ElapsedMilliseconds;
                res.Text = text;
                if (text.IndexOf("e7_session_expired", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    res.Ok = false;
                    res.Error = "auth: e7_session_expired";
                    return res;
                }

                JToken data = null;
                try
                {
                    data = JudgeUtil.Parse(text);
                }
                catch
                {
                }

                // Domain McpClient folds an MCP isError result into {"error": "..."}; an object with an
                // "error" key is therefore a failed call (AnalysisEngine.LooksUsableToolResult does the same).
                bool isError = false;
                if (JudgeUtil.IsObject(data))
                {
                    JObject obj = (JObject)data;
                    if (obj["error"] != null && obj.Count <= 2)
                    {
                        isError = true;
                        res.Error = obj["error"].ToString();
                    }

                    if (obj["isError"] != null && obj["isError"].Type == JTokenType.Boolean && obj["isError"].Value<bool>())
                    {
                        isError = true;
                        res.Error = res.Error ?? JudgeUtil.Trunc(text, 300);
                    }
                }

                res.Ok = !isError;
                res.Data = data;
                return res;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                res.Ms = sw.ElapsedMilliseconds;
                res.Ok = false;
                res.Error = ex.GetType().Name + ": " + ex.Message;
                return res;
            }
        }

        internal static List<JudgeSample> ParseSamples(JToken d)
        {
            List<JudgeSample> list = new List<JudgeSample>();
            JToken arr = d;
            if (JudgeUtil.IsObject(d))
            {
                JToken tsCol = JudgeUtil.Prop(d, "TimestampDevice");
                JToken vCol = JudgeUtil.Prop(d, "Value");
                if (JudgeUtil.IsArray(tsCol) && JudgeUtil.IsArray(vCol))
                {
                    int n = Math.Min(((JArray)tsCol).Count, ((JArray)vCol).Count);
                    for (int i = 0; i < n; i++)
                    {
                        DateTimeOffset? ts = JudgeUtil.ParseRowTs(tsCol[i]);
                        double? v = JudgeUtil.ParseRowNum(vCol[i]);
                        if (ts != null && v != null)
                        {
                            list.Add(new JudgeSample(ts.Value, v.Value));
                        }
                    }

                    return list;
                }

                foreach (string k in new[] { "rows", "data", "samples", "history", "items", "result" })
                {
                    JToken a = JudgeUtil.Prop(d, k);
                    if (JudgeUtil.IsArray(a))
                    {
                        arr = a;
                        break;
                    }
                }
            }

            if (!JudgeUtil.IsArray(arr))
            {
                return list;
            }

            foreach (JToken row in arr)
            {
                DateTimeOffset? ts = null;
                double? val = null;
                if (JudgeUtil.IsArray(row) && ((JArray)row).Count >= 2)
                {
                    ts = JudgeUtil.ParseRowTs(row[0]);
                    val = JudgeUtil.ParseRowNum(row[1]);
                }
                else if (JudgeUtil.IsObject(row))
                {
                    foreach (string k in new[] { "TimestampDevice", "timestampDevice", "TimestampLocal", "ts", "t", "time", "Timestamp" })
                    {
                        JToken tv = JudgeUtil.Prop(row, k);
                        if (tv != null)
                        {
                            ts = JudgeUtil.ParseRowTs(tv);
                            if (ts != null)
                            {
                                break;
                            }
                        }
                    }

                    foreach (string k in new[] { "Value", "value", "v", "CurrentValue" })
                    {
                        JToken vv = JudgeUtil.Prop(row, k);
                        if (vv != null)
                        {
                            val = JudgeUtil.ParseRowNum(vv);
                            if (val != null)
                            {
                                break;
                            }
                        }
                    }
                }

                if (ts != null && val != null)
                {
                    list.Add(new JudgeSample(ts.Value, val.Value));
                }
            }

            return list;
        }
    }
}
