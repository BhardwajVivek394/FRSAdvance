using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;

namespace E7.AiCore.Judge
{
    // ============================================================================================
    // JudgeSettings -- every tunable value of the judge, loaded from App_Data\AlertJudge\judge-settings.json.
    //
    // CHANGE HERE when a new setting is needed: add the field with its default, then read it in Load().
    // The sections and names are the same as AlertJudge v1.1.2.0 appsettings.json (Mcp.Tools,
    // Mcp.ArgNames, History, Ai, Flow, Rules, Audit), plus the new "Live" and "Judge" sections.
    // Connection values (MCP URL, E7 login, AI provider keys, InternalApiKey) are NOT here: they are
    // the web.config keys AiChat already uses.
    //
    // The file is re-read when it changes (checked at most every 30 s), so a value can be changed
    // without a rebuild or an app-pool recycle.
    // ============================================================================================
    internal sealed class JudgeSettings
    {
        // ---- Mcp.Tools ----
        public string ToolSearchTags = "search_tags";
        public string ToolGetAssetTags = "get_asset_tags";
        public string ToolHistoryGet = "history_get";
        public string ToolAttributeRangeHist = "get_attribute_range_history";
        public string ToolGetFrsAlerts = "get_frs_alerts";
        public string ToolFamilyTracks = "";

        // ---- Mcp.ArgNames ----
        public string ArgSiteId = "SiteId";
        public string ArgAssetId = "AssetId";
        public string ArgAttributeId = "AttributeId";
        public string ArgRoleType = "RoleType";
        /// <summary>
        /// 2.6 -- "TagId", NOT "TagID". This one character is why every history_get came back
        /// empty while search_tags worked: the MCP server's history_get parameter is spelled
        /// "TagId", and JSON property names are case-sensitive, so a request carrying "TagID" has
        /// no tag identifier the server recognises at all.
        ///
        /// The response field IS "TagID" (see the liveValue rows and search_tags output) -- request
        /// parameter and response field differ in case, which is exactly the trap this fell into.
        ///
        /// AnalyzeAlertJson never had this problem because AnalysisEngine.HistWindowArgs builds
        /// every fetch as HistWindowArgs("TagId", ...). Everything else about the two calls already
        /// matched: StartDate, EndDate, limit, outputMode, sort, the ddMMyyyy_HHmmss format, the
        /// 330-minute IST offset, and send-as-UTC (Judge SendUtc=true == AnalysisEngine
        /// HistGetSendsUtc=true). The parameter name was the only difference.
        /// </summary>
        public string ArgTagId = "TagId";
        public string ArgStartDate = "StartDate";
        public string ArgEndDate = "EndDate";
        public string ArgLimit = "limit";
        public string ArgOutputMode = "outputMode";
        public string ArgSort = "sort";
        // 2.3 -- the REAL search_tags filters. The MCP server (v1.5.55.1) does NOT filter on
        // AttributeId or RoleType; its search-text filters are AssetName (ILIKE) and, to narrow to
        // one attribute, AttributeName (ILIKE). Sending ids instead returned an arbitrary page of
        // tags, and tag resolution then depended on whether the wanted attribute happened to be in
        // it -- which is exactly why attid 2 (Ir mA) reported "not on this asset" on an asset that
        // has it. Documented in AnalysisEngine.Grounding.Tags.cs v1.0.79.0.
        public string ArgAssetName = "AssetName";
        public string ArgAttributeName = "AttributeName";

        public string ArgSiteIds = "SiteIds";
        public string ArgAssetIds = "AssetIds";
        public string ArgPageSize = "PageSize";

        // ---- History ----
        public string StampFormat = "ddMMyyyy_HHmmss";

        /// <summary>
        /// 2.8 -- FALSE, and this is MEASURED, not assumed. Do not flip it back on a hunch; re-read
        /// the echo in JudgeAIlogs first.
        ///
        /// The MCP server parses the history_get date strings as IST LOCAL time. It echoes the
        /// window it actually applied as a Unix epoch, and on 09.10.2026 all EIGHT windows across
        /// alerts 1137362 and 1034216 echoed exactly 19800 s -- 5 h 30 m, the IST offset -- below
        /// the stamp read as UTC:
        ///
        ///   sent 28092026_022857   (the judge's UTC rendering of 28/09 07:58:57 IST, the alert time)
        ///   echo 1790542737        (= 28/09 02:28:57 IST = 27/09 20:58:57 UTC)
        ///
        /// So sending UTC made the server take 5 h 30 m off a second time, and every window landed
        /// 5 h 30 m before the event. The window SPANS were always right (1719 s and 346 s, both
        /// correct) -- only the position was wrong, which is why the data came back looking
        /// plausible and was read as disproof of two true alerts:
        ///
        ///   1137362 -> SUPPRESSED / relay-gate,  "False alarm", confidence 85
        ///   1034216 -> SUPPRESSED / analog-gate, "False alarm", confidence 85
        ///
        /// With false, Stamp() sends the IST wall clock (28092026_075857) and the server's own
        /// conversion lands on the right instant.
        ///
        /// ON AnalysisEngine: it reads the same choice from the appSetting HistGetSendsUtc, whose
        /// code default is also true. Either that key is set false on the server -- which would be
        /// why AnalyzeAlertJson was never affected -- or AnalyzeAlertJson has the same 5 h 30 m
        /// shift and nobody noticed, because wrong-period data still reads like real data. That is
        /// a question for the AnalysisEngine owner; nothing in this release changes it.
        ///
        /// HOW TO CONFIRM: the 24 h baseline average must land near the average the card implies.
        /// For 1137362 the card says 80% Avg = 261.26, so the true average is about 326.6; with the
        /// window 5 h 30 m early the judge computed 131.199.
        /// </summary>
        public bool SendUtc = false;

        public int IstOffsetMinutes = 330;
        public int RowLimit = 5000;
        public string OutputMode = "json";
        public int BaselineHours = 24;
        public int InstantLookbackSec = 60;
        public int LookbackHours = 48;
        public int MaxFrameHours = 6;
        public int AdjacencyWindowSec = 120;
        public string RoleDataLogger = "d";
        public string RoleRdpms = "c";
        public string RoleIps = "c";

        // ---- Ai ----
        public bool AiEnabled = true;

        /// <summary>
        /// 1.0.187.0 -- pins the AI VENDOR for the judge, overriding both the request's provider and
        /// the AiProvider appSetting. Blank (the default) = leave the choice to the request, then to
        /// Web.config, exactly as AiChat/AnalyzeAlertJson does.
        ///
        /// It exists because the Web.config AiProvider key needs an app-pool recycle and a deploy
        /// window, and the vendor is precisely what you want to change when one account stops
        /// working. This file is re-read within 30 s, so "move the judge to DeepSeek" becomes a
        /// one-line edit with no rebuild and no recycle.
        ///
        /// Accepts a vendor ("deepseek", "openai", "anthropic") or a brand label the rest of the
        /// system uses ("QUANTUM", "ENERGY7 QUANTUM", "quantum11", "NEXUS", "ORION", "ULTRA") --
        /// EngineIdentity.ResolveProviderKind maps all of them. "quantum11" also selects the DeepSeek
        /// flash model.
        ///
        /// On 10.10.2026 AiProvider was "anthropic" in Web.config while every request carried
        /// provider "QUANTUM". AnalyzeAlertJson read the request and ran DeepSeek; the judge ignored
        /// it and ran Anthropic, which had no credit. Setting this to "QUANTUM" would have made the
        /// two agree without touching Web.config at all.
        ///
        /// 1.0.188.0 -- with this blank AND no provider on the request AND no AiProvider key in
        /// Web.config, the judge now lands on DeepSeek Pro, not Anthropic: JudgeAi's fallback moved
        /// to "quantum10", the same literal AnalysisEngine._aiProviderDefault and ChatEngine.Main
        /// already used (owner decision, AnalysisEngine changelog 1.0.160.202). So on a server whose
        /// Web.config carries no AI keys at all, every endpoint in this assembly now agrees without
        /// anyone adding a setting. A key that IS present still wins over the default, as before.
        /// </summary>
        public string AiProvider = "";
        public int AiMaxTurns = 4;
        public int AiMaxTokens = 1500;
        public int AiTimeoutSec = 60;
        public string WisdomDir = "Rules\\wisdom";
        public string DecisionReferencePath = "Rules\\decision-reference.md";
        public int MaxSamplesPerAttr = 60;
        public int TrendBuckets = 6;

        // ---- Flow ----
        public string CardMismatch = "SUPPRESSED";
        public string NoData = "SUPPRESSED";
        public bool AiIsFinal = true;
        public string AiUnavailable = "SUPPRESSED";

        /// <summary>
        /// 2.9 -- what to do when the AI could not run AND the computation actually verified the
        /// alert: the live re-check held at least one condition on independent data, or the alert is
        /// closed and its own set and reset cards corroborate it.
        ///
        /// Default "PASS". The ai-unavailable branch is only reachable after the card/catalogue
        /// match, the catalogue gates and the live re-check have all passed, so reaching it means
        /// the computation has already decided. The AI is asked two questions about logic and data
        /// and can only ever REMOVE an alert the computation accepted -- so an AI that did not
        /// answer has not removed anything, and deleting a verified alert because the explainer is
        /// down inverts the whole design.
        ///
        /// This is not hypothetical. On 09.10.2026 alerts 1137362 and 1034216 passed every check --
        /// relay PASS, analog PASS, live PASS 2/2 held, reset card CONFIRMED, decision ACCEPT -- and
        /// were both SUPPRESSED because the Anthropic API answered 400 "Your credit balance is too
        /// low". A billing problem deleted two true alerts.
        ///
        /// Confidence stays 0 in this case, deliberately: the alert is delivered on the computation,
        /// not on an AI opinion, and the verdict must not read as though a model had agreed.
        ///
        /// Set to "SUPPRESSED" to require the AI for every delivery, as builds up to 1.0.184.0 did.
        /// AiUnavailable above still governs the weaker case where the live step found no evidence
        /// either way -- there the AI was the only remaining check, so its absence does suppress.
        /// </summary>
        public string AiUnavailableVerified = "PASS";
        public int AiPassMinConfidence = 0;

        /// <summary>
        /// 1.0.190.0 -- when the computation VERIFIED the alert (the live re-check held every
        /// counted condition through the sustain window AND the set/reset cards corroborate), the AI
        /// may not suppress on the ground that the reading recovered AFTER the window. Default true.
        ///
        /// On 11.10.2026 alerts 1137362, 1034216 and 1023426 each passed every computed check and
        /// were then deleted on data_correct=false alone, with "it recovered before reset" as the
        /// reason. No cause rule requires a condition to persist to the reset, and every closed
        /// alert recovered -- so that ground would suppress every closed alert. Two of the three
        /// reasons were also arithmetically false (the wrong half of an OR threshold).
        ///
        /// Narrow by design. The AI keeps its veto for logic_correct=false, a card contradicting
        /// its own numbers, a reading that does not show the breach INSIDE the window, and
        /// instrument artefacts. Only the recovered-later argument is refused, and only on an alert
        /// the computation already verified. The overrule is always logged and stated in the
        /// verdict reason, with decidedBy "ai-overruled" and confidence 0.
        ///
        /// Set false to restore 1.0.189.1, where one AI sentence outranked every computed check.
        /// </summary>
        public bool AiVetoNeedsNewEvidence = true;

        /// <summary>
        /// 1.0.192.0 -- what the AI may do when its own stated ground for data_correct=false is
        /// MISSING / unmapped / unverifiable telemetry rather than data that contradicts the card.
        /// PASS (default) refuses that veto and records decidedBy=ai-overruled, ground
        /// "missing-data". SUPPRESSED restores 1.0.191.0, where "I could not see the data" deleted
        /// the alert. See JudgePolicy.AiGroundIsMissingData and the 12.10.2026 log: four of the
        /// sixteen real Mode-3 suppressions were this, one of them with 3 of 3 counted conditions
        /// HELD and another on TC GJ SHORT, which has nothing to re-derive by design.
        /// </summary>
        public string AiVetoOnMissingData = "PASS";

        /// <summary>
        /// 1.0.192.3 -- what a SUPPRESS recommendation may do when the AI answered NEITHER of the
        /// two questions (logic_correct and data_correct both null -- an abstention, not a finding).
        /// PASS (default) refuses that veto and records decidedBy=ai-overruled, ground "no-answer".
        /// SUPPRESSED restores 1.0.192.2, where an all-null verdict still deleted the alert through
        /// the ai.Recommendation != "PASS" term, with a reason text that contradicted itself.
        ///
        /// This closes the gap the two caps above leave open: both are guarded on
        /// data_correct == false, and in C# (null == false) is false, so neither can ever see an
        /// abstention. 13.10.2026: both TC GJ SHORT replays (1343345, 1204111) died this way, each
        /// with confidence 92 and a reason ending "...accepted as reported rather than
        /// independently verified" beside a SUPPRESSED decision.
        ///
        /// Narrow on purpose: data_correct=false and logic_correct=false both keep their veto in
        /// full, and a PASS recommendation is untouched. Only the all-null abstention is refused.
        /// </summary>
        public string AiVetoNeedsAnswer = "PASS";

        /// <summary>2.0: a card that cannot be read -> SUPPRESSED (J1 fix). "PASS" restores the 1.1.2.0 behaviour.</summary>
        public string CardUnreadable = "SUPPRESSED";

        // ---- Rules ----
        public int ConfidenceConfirmed = 90;
        public int ConfidenceNotConfirmed = 85;
        public int CoveragePenalty = 25;

        /// <summary>
        /// 1.0.190.0 -- an alert that breached ONLY the rolling-average threshold, by less than this
        /// percentage of it, is delivered but marked low-confidence. 0 disables the mark. Default 5.
        ///
        /// 1023426 (118/119T) fired at 262.08 mA against an 80%-Avg threshold of 269.30 -- 2.7%
        /// below -- while Min Safe (210) passed, so the current was healthy in absolute terms. The
        /// card says as much itself: "(value is okay for Min/Max; alert triggered because of Avg
        /// threshold)". Such an alert is still delivered: a drift below the rolling average is what
        /// PREDICTIVE means. The mark is so a maintainer can see it is a 2.7% drift and not a fault.
        /// </summary>
        public int AvgOnlyMinMarginPct = 5;

        // ---- Audit ----
        public string AuditDir = "Audit";
        public bool AuditEnabled = true;

        // ---- Live (new in 2.0: the card-condition re-check against live data) ----
        public bool LiveEnabled = true;

        /// <summary>card = card.meta.checkWindow; catalog = the cause's SustainSec; fixed = LiveWindowSec. Falls back in that order.</summary>
        public string LiveWindowSource = "card";

        public int LiveWindowSec = 15;

        /// <summary>Hard cap on the window (a card that says "15 min" still gets at most this).</summary>
        public int LiveMaxWindowSec = 900;

        /// <summary>If the window end is still in the future, wait up to this many seconds for data (0 = never wait).</summary>
        public int LiveWaitMaxSec = 20;

        /// <summary>SUPPRESSED (your rule: unverified never passes) or PASS (mark "not verified" and let the gates decide).</summary>
        public string LiveNoData = "SUPPRESSED";

        /// <summary>SUPPRESSED or PASS when a derived condition has no formula.</summary>
        public string LiveUnsupported = "SUPPRESSED";

        /// <summary>
        /// 2.3 -- what to do when the live re-check obtained NO independent evidence either way
        /// (NoEvidence) and the alert is a FAILURE. Default "PASS": a failure means equipment is
        /// down now, and discarding a possible one because our historian lacks the tags is the
        /// unsafe direction -- it is delivered with the gap stated in the remark instead. Set to
        /// "SUPPRESSED" to go back to dropping them. PREDICTIVE alerts are unaffected and keep
        /// Live.NoData / Live.Unsupported.
        /// </summary>
        public string LiveNoEvidenceFailure = "PASS";

        /// <summary>true = use the threshold number printed on the card (the rule that fired); false = recompute avg*k from the 24 h baseline.</summary>
        public bool LiveUseCardThresholds = true;

        /// <summary>true = enabling relays must hold through the window; false = checked at the set instant only.</summary>
        public bool LiveRelaysMustHold = true;

        /// <summary>
        /// 3.0 -- for a FAILURE, treat the card's checkWindow as a window to FIND the condition in,
        /// rather than one the condition must already be true at the first instant of. Default true.
        ///
        /// Two TC SHORT alerts at the same station, judged in the same minute, came out opposite ways
        /// on nothing but sampling jitter:
        ///
        ///   04AT 1263751  card sample 08:38:35, 5s INSIDE the 08:38:30-08:38:40 window
        ///                 value at the window's first instant 108.801 -> not < 100 (MinFail)
        ///                 window minimum 90.225               -> IS  < 100
        ///                 -> LAPSED -> SUPPRESSED, "False alarm", confidence 90
        ///
        ///   04BT 1160057  card sample 09:53:09, 1s BEFORE its window
        ///                 value at the first instant 6.177     -> IS  < 100
        ///                 -> HELD -> PASS
        ///
        /// 04AT was a real short: TPR dropped 08:38:26, relay-end current fell 108.8 -> 90.2 -> 58.4
        /// over 45 s while feed-end held 724-807 mA (615-716 mA into the ballast), IBALST ran 622-634
        /// against a threshold of 36, and the reset card CONFIRMED both conditions. The card stamps
        /// its measurement 08:38:35 and never claimed it was the value at 08:38:30.
        ///
        /// The condition must still HOLD from its onset to the end of the window, and a condition
        /// never true anywhere in the window still LAPSES -- so this is not "true at some point".
        /// PREDICTIVE is unaffected: there the rule genuinely is sustain from the set instant.
        ///
        /// Set to false to restore the 1.0.185.0 behaviour.
        /// </summary>
        public bool LiveFailureOnset = true;

        /// <summary>
        /// Formulas for derived card conditions that carry no formula of their own, keyed by the
        /// card label without unit ("∆IBALST" -> "..."). Operands are card/catalog attribute names
        /// or numbers; + - * / and brackets. Empty = the condition is UNSUPPORTED.
        /// </summary>
        public Dictionary<string, string> LiveFormulas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Card conditions (label without unit, e.g. "∆IBALST") the live re-check reports but does not
        /// count. Use it to run before a formula is known; the condition shows as IGNORED in the response.
        /// </summary>
        public HashSet<string> LiveIgnoreConditions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ---- 2.7: the reset card as evidence ----------------------------------------------------

        /// <summary>
        /// true = compare the set card against the reset card (JudgeResetCheck). false = ignore the
        /// reset card's measurements entirely, which is what every build up to 1.0.182.0 did.
        /// </summary>
        public bool LiveUseResetCard = true;

        /// <summary>
        /// 2.7 -- what to do when the live re-check found NO evidence either way (NO_DATA or
        /// UNSUPPORTED with nothing held and nothing lapsed) AND the alert is closed with its two
        /// cards corroborating each other.
        ///
        /// Default "PASS". A reset alert cannot be re-checked against live data by definition: its
        /// window is in the past and the condition is supposed to be false now. Discarding it on
        /// that is the judge mistaking "I cannot see" for "it did not happen" -- which is exactly
        /// how 1137362 and 1034216, both true alerts, came back SUPPRESSED / live-no-data.
        ///
        /// Set to "SUPPRESSED" to go back to requiring live data for every alert, open or closed.
        /// This never overrides a live FAIL: a contradiction found in real data still suppresses.
        /// </summary>
        public string LiveClosedConfirmedNoData = "PASS";

        /// <summary>
        /// 2.7 -- what to do when the SET card's own measurement does not breach the SET card's own
        /// threshold. Default "SUPPRESSED": the card contradicts itself and no outside data is
        /// needed to see it, which is the classic mid-transition capture. "PASS" records it as
        /// evidence and lets the gates and the AI decide.
        /// </summary>
        public string LiveCardContradiction = "SUPPRESSED";

        // ---- PriorDiscard (new in 2.1: what a reviewer already decided about this asset + cause) ----

        /// <summary>false = never look; the judge behaves exactly as 2.0 did.</summary>
        public bool PriorDiscardEnabled = true;

        /// <summary>Name of the connectionStrings entry that reaches dbo.PendingAlertApproval.</summary>
        public string PriorDiscardConnectionName = "E7MRIV2DB_SQL";

        /// <summary>How far back a human discard still counts.</summary>
        public int PriorDiscardLookbackDays = 30;

        /// <summary>Most recent N rows kept for the verdict and the AI pack.</summary>
        public int PriorDiscardMaxRows = 5;

        /// <summary>SQL command timeout. The lookup never blocks a judgement for longer than this.</summary>
        public int PriorDiscardTimeoutSec = 5;

        /// <summary>
        /// What a reviewer's "the alert was NOT valid" (DiscardWasValid = 0) does to a repeat:
        /// SUPPRESSED (the rule you chose) or PASS to record it as evidence only.
        /// </summary>
        public string PriorDiscardRejected = "SUPPRESSED";

        // ---- Learned (new in 1.0.202.0: maintenance window + similar past cases; see JudgeLearned) ----

        /// <summary>off | report | enforce. report = looked at and written to the verdict, changes nothing.</summary>
        public string LearnedMaintenanceMode = "report";

        /// <summary>off | report | enforce, for the similar-case check against judge-cases.json.</summary>
        public string LearnedCaseMode = "report";

        /// <summary>Blank = judge-cases.json beside judge-settings.json. A relative path is taken from that folder.</summary>
        public string LearnedCasesFile = "";

        /// <summary>How many most-similar past cases vote.</summary>
        public int LearnedK = 5;

        /// <summary>How much "same asset" counts, in card features (0 = the asset is ignored).</summary>
        public int LearnedAssetWeight = 3;

        /// <summary>Fewer comparable past cases than this = no answer from the similar-case check.</summary>
        public int LearnedMinCases = 1;

        /// <summary>A case file older than this is not used (the nightly job has stopped). 0 = no limit.</summary>
        public int LearnedMaxFileAgeHours = 72;

        /// <summary>SQL command timeout of the maintenance lookup.</summary>
        public int LearnedTimeoutSec = 5;

        // ---- Log (new in 1.0.203.0: what the Judge log carries and where; see JudgeBlockLog) ----

        /// <summary>Also write each judgement to by-site\&lt;site&gt;\&lt;asset type&gt;\&lt;cause&gt;\&lt;alert id&gt;.txt.</summary>
        public bool LogBySite = true;

        /// <summary>Also append one row per judgement to the site's judge-summary-yyyy-MM.csv. Off since 1.0.206.0.</summary>
        public bool LogSummary = false;

        /// <summary>1.0.206.0 -- also append every block to the site's judgeai-yyyy-MM-dd.log. Off: the alert's own .txt holds everything.</summary>
        public bool LogDaily = false;

        /// <summary>1.0.206.0 -- also write the lines that belong to no alert to _general\judgeai-yyyy-MM-dd.log. Off: they are still in ai-log-yyyyMMdd.txt.</summary>
        public bool LogGeneral = false;

        /// <summary>Put the alert card JSON, as sent, into the log block.</summary>
        public bool LogCard = true;

        // ---- Judge (host) ----
        public int MaxConcurrent = 4;
        public int MaxBodyBytes = 262144;

        // ---- paths ----
        public string BaseDir = "";
        public string SettingsPath = "";
        public string LoadError;

        // -----------------------------------------------------------------------------------------
        private static readonly object gate = new object();
        private static JudgeSettings current;
        private static DateTime lastCheckUtc = DateTime.MinValue;
        private static DateTime lastWriteUtc = DateTime.MinValue;

        /// <summary>The current settings (re-read when the file changed; never throws).</summary>
        public static JudgeSettings Current
        {
            get
            {
                lock (gate)
                {
                    DateTime now = DateTime.UtcNow;
                    if (current == null || (now - lastCheckUtc).TotalSeconds > 30)
                    {
                        lastCheckUtc = now;
                        string path = ResolveSettingsPath();
                        DateTime write = DateTime.MinValue;
                        try
                        {
                            if (File.Exists(path))
                            {
                                write = File.GetLastWriteTimeUtc(path);
                            }
                        }
                        catch
                        {
                        }

                        if (current == null || write != lastWriteUtc)
                        {
                            current = Load(path);
                            lastWriteUtc = write;
                        }
                    }

                    return current;
                }
            }
        }

        /// <summary>Test hook: use these settings until the next Reset().</summary>
        internal static void Override(JudgeSettings s)
        {
            lock (gate)
            {
                current = s;
                lastCheckUtc = DateTime.MaxValue;
            }
        }

        internal static void Reset()
        {
            lock (gate)
            {
                current = null;
                lastCheckUtc = DateTime.MinValue;
            }
        }

        internal static string ResolveBaseDir()
        {
            string dir = ConfigurationManager.AppSettings["JudgeSettingsDir"];
            if (!string.IsNullOrWhiteSpace(dir))
            {
                return dir.Trim();
            }

            string root = AppDomain.CurrentDomain.BaseDirectory ?? "";
            return Path.Combine(root, "App_Data", "AlertJudge");
        }

        private static string ResolveSettingsPath()
        {
            return Path.Combine(ResolveBaseDir(), "judge-settings.json");
        }

        internal static JudgeSettings Load(string path)
        {
            JudgeSettings s = new JudgeSettings();
            s.BaseDir = Path.GetDirectoryName(path) ?? "";
            s.SettingsPath = path;
            try
            {
                if (!File.Exists(path))
                {
                    s.LoadError = "judge-settings.json not found at " + path + " (defaults in use)";
                    return s;
                }

                JObject root = JObject.Parse(File.ReadAllText(path));
                Apply(s, root);
            }
            catch (Exception ex)
            {
                s.LoadError = "judge-settings.json: " + ex.Message + " (defaults in use)";
            }

            return s;
        }

        internal static JudgeSettings FromJson(JObject root)
        {
            JudgeSettings s = new JudgeSettings();
            Apply(s, root);
            return s;
        }

        private static void Apply(JudgeSettings s, JObject root)
        {
            JObject mcp = root["Mcp"] as JObject;
            JObject tools = mcp != null ? mcp["Tools"] as JObject : null;
            JObject args = mcp != null ? mcp["ArgNames"] as JObject : null;
            s.ToolSearchTags = Str(tools, "SearchTags", s.ToolSearchTags);
            s.ToolGetAssetTags = Str(tools, "GetAssetTags", s.ToolGetAssetTags);
            s.ToolHistoryGet = Str(tools, "HistoryGet", s.ToolHistoryGet);
            s.ToolAttributeRangeHist = Str(tools, "AttributeRangeHist", s.ToolAttributeRangeHist);
            s.ToolGetFrsAlerts = Str(tools, "GetFrsAlerts", s.ToolGetFrsAlerts);
            s.ToolFamilyTracks = Str(tools, "FamilyTracks", s.ToolFamilyTracks);
            s.ArgSiteId = Str(args, "SiteId", s.ArgSiteId);
            s.ArgAssetId = Str(args, "AssetId", s.ArgAssetId);
            s.ArgAttributeId = Str(args, "AttributeId", s.ArgAttributeId);
            s.ArgRoleType = Str(args, "RoleType", s.ArgRoleType);
            s.ArgTagId = Str(args, "TagId", s.ArgTagId);
            s.ArgStartDate = Str(args, "StartDate", s.ArgStartDate);
            s.ArgEndDate = Str(args, "EndDate", s.ArgEndDate);
            s.ArgLimit = Str(args, "Limit", s.ArgLimit);
            s.ArgOutputMode = Str(args, "OutputMode", s.ArgOutputMode);
            s.ArgSort = Str(args, "Sort", s.ArgSort);
            s.ArgSiteIds = Str(args, "SiteIds", s.ArgSiteIds);
            s.ArgAssetName = Str(args, "AssetName", s.ArgAssetName);
            s.ArgAttributeName = Str(args, "AttributeName", s.ArgAttributeName);
            s.ArgAssetIds = Str(args, "AssetIds", s.ArgAssetIds);
            s.ArgPageSize = Str(args, "PageSize", s.ArgPageSize);

            JObject h = root["History"] as JObject;
            s.StampFormat = Str(h, "StampFormat", s.StampFormat);
            s.SendUtc = Bool(h, "SendUtc", s.SendUtc);
            s.IstOffsetMinutes = Int(h, "IstOffsetMinutes", s.IstOffsetMinutes);
            s.RowLimit = Int(h, "RowLimit", s.RowLimit);
            s.OutputMode = Str(h, "OutputMode", s.OutputMode);
            s.BaselineHours = Int(h, "BaselineHours", s.BaselineHours);
            s.InstantLookbackSec = Int(h, "InstantLookbackSec", s.InstantLookbackSec);
            s.LookbackHours = Int(h, "LookbackHours", s.LookbackHours);
            s.MaxFrameHours = Int(h, "MaxFrameHours", s.MaxFrameHours);
            s.AdjacencyWindowSec = Int(h, "AdjacencyWindowSec", s.AdjacencyWindowSec);
            s.RoleDataLogger = Str(h, "RoleDataLogger", s.RoleDataLogger);
            s.RoleRdpms = Str(h, "RoleRdpms", s.RoleRdpms);
            s.RoleIps = Str(h, "RoleIps", s.RoleIps);

            JObject ai = root["Ai"] as JObject;
            s.AiEnabled = Bool(ai, "Enabled", s.AiEnabled);
            s.AiProvider = Str(ai, "Provider", s.AiProvider);                                              // 1.0.187.0
            s.AiMaxTurns = Int(ai, "MaxTurns", s.AiMaxTurns);
            s.AiMaxTokens = Int(ai, "MaxTokens", s.AiMaxTokens);
            s.AiTimeoutSec = Int(ai, "TimeoutSec", s.AiTimeoutSec);
            s.WisdomDir = Str(ai, "WisdomDir", s.WisdomDir);
            s.DecisionReferencePath = Str(ai, "DecisionReferencePath", s.DecisionReferencePath);
            s.MaxSamplesPerAttr = Int(ai, "MaxSamplesPerAttr", s.MaxSamplesPerAttr);
            s.TrendBuckets = Int(ai, "TrendBuckets", s.TrendBuckets);

            JObject flow = root["Flow"] as JObject;
            s.CardMismatch = Str(flow, "CardMismatch", s.CardMismatch);
            s.NoData = Str(flow, "NoData", s.NoData);
            s.AiIsFinal = Bool(flow, "AiIsFinal", s.AiIsFinal);
            s.AiUnavailable = Str(flow, "AiUnavailable", s.AiUnavailable);
            s.AiUnavailableVerified = Str(flow, "AiUnavailableVerified", s.AiUnavailableVerified);   // 2.9
            s.AiPassMinConfidence = Int(flow, "AiPassMinConfidence", s.AiPassMinConfidence);
            s.AiVetoNeedsNewEvidence = Bool(flow, "AiVetoNeedsNewEvidence", s.AiVetoNeedsNewEvidence);
            s.AiVetoOnMissingData = Str(flow, "AiVetoOnMissingData", s.AiVetoOnMissingData);   // 1.0.190.0
            s.AiVetoNeedsAnswer = Str(flow, "AiVetoNeedsAnswer", s.AiVetoNeedsAnswer);   // 1.0.192.3
            s.CardUnreadable = Str(flow, "CardUnreadable", s.CardUnreadable);

            JObject rules = root["Rules"] as JObject;
            s.ConfidenceConfirmed = Int(rules, "ConfidenceConfirmed", s.ConfidenceConfirmed);
            s.ConfidenceNotConfirmed = Int(rules, "ConfidenceNotConfirmed", s.ConfidenceNotConfirmed);
            s.AvgOnlyMinMarginPct = Int(rules, "AvgOnlyMinMarginPct", s.AvgOnlyMinMarginPct);   // 1.0.190.0
            s.CoveragePenalty = Int(rules, "CoveragePenalty", s.CoveragePenalty);

            JObject audit = root["Audit"] as JObject;
            s.AuditDir = Str(audit, "Dir", s.AuditDir);
            s.AuditEnabled = Bool(audit, "Enabled", s.AuditEnabled);

            JObject live = root["Live"] as JObject;
            s.LiveEnabled = Bool(live, "Enabled", s.LiveEnabled);
            s.LiveWindowSource = Str(live, "WindowSource", s.LiveWindowSource);
            s.LiveWindowSec = Int(live, "WindowSec", s.LiveWindowSec);
            s.LiveMaxWindowSec = Int(live, "MaxWindowSec", s.LiveMaxWindowSec);
            s.LiveWaitMaxSec = Int(live, "WaitMaxSec", s.LiveWaitMaxSec);
            s.LiveNoData = Str(live, "NoData", s.LiveNoData);
            s.LiveUnsupported = Str(live, "Unsupported", s.LiveUnsupported);
            s.LiveNoEvidenceFailure = Str(live, "NoEvidenceFailure", s.LiveNoEvidenceFailure);
            s.LiveUseCardThresholds = Bool(live, "UseCardThresholds", s.LiveUseCardThresholds);
            s.LiveRelaysMustHold = Bool(live, "RelaysMustHold", s.LiveRelaysMustHold);
            s.LiveFailureOnset = Bool(live, "FailureOnset", s.LiveFailureOnset);                             // 3.0
            s.LiveUseResetCard = Bool(live, "UseResetCard", s.LiveUseResetCard);                            // 2.7
            s.LiveClosedConfirmedNoData = Str(live, "ClosedConfirmedNoData", s.LiveClosedConfirmedNoData);   // 2.7
            s.LiveCardContradiction = Str(live, "CardContradiction", s.LiveCardContradiction);               // 2.7
            JArray ignore = live != null ? live["IgnoreConditions"] as JArray : null;
            if (ignore != null)
            {
                foreach (JToken t in ignore)
                {
                    if (t.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string)t))
                    {
                        s.LiveIgnoreConditions.Add(((string)t).Trim());
                    }
                }
            }

            JObject formulas = live != null ? live["Formulas"] as JObject : null;
            if (formulas != null)
            {
                foreach (JProperty p in formulas.Properties())
                {
                    if (p.Value != null && p.Value.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string)p.Value))
                    {
                        s.LiveFormulas[p.Name.Trim()] = ((string)p.Value).Trim();
                    }
                }
            }

            JObject prior = root["PriorDiscard"] as JObject;
            s.PriorDiscardEnabled = Bool(prior, "Enabled", s.PriorDiscardEnabled);
            s.PriorDiscardConnectionName = Str(prior, "ConnectionName", s.PriorDiscardConnectionName);
            s.PriorDiscardLookbackDays = Int(prior, "LookbackDays", s.PriorDiscardLookbackDays);
            s.PriorDiscardMaxRows = Int(prior, "MaxRows", s.PriorDiscardMaxRows);
            s.PriorDiscardTimeoutSec = Int(prior, "TimeoutSec", s.PriorDiscardTimeoutSec);
            s.PriorDiscardRejected = Str(prior, "Rejected", s.PriorDiscardRejected);

            JObject learned = root["Learned"] as JObject;
            s.LearnedMaintenanceMode = Str(learned, "MaintenanceMode", s.LearnedMaintenanceMode);
            s.LearnedCaseMode = Str(learned, "CaseMode", s.LearnedCaseMode);
            s.LearnedCasesFile = Str(learned, "CasesFile", s.LearnedCasesFile);
            s.LearnedK = Int(learned, "K", s.LearnedK);
            s.LearnedAssetWeight = Int(learned, "AssetWeight", s.LearnedAssetWeight);
            s.LearnedMinCases = Int(learned, "MinCases", s.LearnedMinCases);
            s.LearnedMaxFileAgeHours = Int(learned, "MaxFileAgeHours", s.LearnedMaxFileAgeHours);
            s.LearnedTimeoutSec = Int(learned, "TimeoutSec", s.LearnedTimeoutSec);

            JObject logCfg = root["Log"] as JObject;
            s.LogBySite = Bool(logCfg, "BySite", s.LogBySite);
            s.LogSummary = Bool(logCfg, "Summary", s.LogSummary);
            s.LogCard = Bool(logCfg, "Card", s.LogCard);
            s.LogDaily = Bool(logCfg, "Daily", s.LogDaily);
            s.LogGeneral = Bool(logCfg, "General", s.LogGeneral);

            JObject judge = root["Judge"] as JObject;
            s.MaxConcurrent = Int(judge, "MaxConcurrent", s.MaxConcurrent);
            s.MaxBodyBytes = Int(judge, "MaxBodyBytes", s.MaxBodyBytes);
        }

        private static string Str(JObject o, string key, string dflt)
        {
            if (o == null)
            {
                return dflt;
            }

            JToken v = o[key];
            if (v == null || v.Type == JTokenType.Null)
            {
                return dflt;
            }

            return v.ToString();
        }

        private static int Int(JObject o, string key, int dflt)
        {
            if (o == null)
            {
                return dflt;
            }

            JToken v = o[key];
            if (v == null)
            {
                return dflt;
            }

            int i;
            if (int.TryParse(v.ToString(), out i))
            {
                return i;
            }

            return dflt;
        }

        private static bool Bool(JObject o, string key, bool dflt)
        {
            if (o == null)
            {
                return dflt;
            }

            JToken v = o[key];
            if (v == null)
            {
                return dflt;
            }

            if (v.Type == JTokenType.Boolean)
            {
                return v.Value<bool>();
            }

            bool b;
            if (bool.TryParse(v.ToString(), out b))
            {
                return b;
            }

            return dflt;
        }

        internal string ResolvePath(string relPath)
        {
            if (string.IsNullOrWhiteSpace(relPath))
            {
                return null;
            }

            if (Path.IsPathRooted(relPath))
            {
                return relPath;
            }

            return Path.Combine(BaseDir, relPath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
