using Domain;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using E7MRIWeb.Areas.FRS25.Controllers;   // Domain AI types (CauseCatalog, RdpmsMaps, AI clients) keep the 616 namespace
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Mvc;
using E7.AiCore;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    public partial class AiChatController : Controller, IAnalysisHost
    {
        // ---- Shared AI Core 2e (1.0.165.0) --------------------------------------------------------------
        // The analysis logic now lives in E7.AiCore.AnalysisEngine. One engine per controller (= per request),
        // created in the constructor; the engine streams back through IAnalysisHost.WriteSse.
        private readonly AnalysisEngine Engine;

        public AiChatController()
        {
            // 3c: let core code read the logged-in user without referencing the web project.
            AiUserContext.Current = () => E7FRSAdvance.Utility.ClsHttpContent.LoginUser;
            AiUserContext.AdminRoleId = E7FRSAdvance.Utility.Utility.Role.Admin.GetHashCode();
            AiUserContext.UserRoleId = E7FRSAdvance.Utility.Utility.Role.User.GetHashCode();
            Engine = new AnalysisEngine(this);
        }

        void IAnalysisHost.WriteSse(string type, object data) { WriteSse(type, data); }

        // Forwarders kept for AiDiagController, which calls these on AiChatController.
        internal static System.Threading.Tasks.Task<string> DiagProxyGetAsync(string relativePath) => AnalysisEngine.DiagProxyGetAsync(relativePath);
        internal bool DiagAllowedCore() => Engine.DiagAllowedCore();
        internal void CaptureLoginUserForDiag() => Engine.CaptureLoginUserForDiag();   // AiDiagController calls this before DiagRunCoreAsync
        internal System.Threading.Tasks.Task<JObject> DiagRunCoreAsync(string siteId, string assetId, string assetName, string assetType) => Engine.DiagRunCoreAsync(siteId, assetId, assetName, assetType);

        // v1.0.160.195 (memory-loss B): self-heal follow-up-chat grounding from the stored analyze
        // input when the chat POST arrives without the alert card. Requires AiAuditDbEnabled (nothing
        // to read without it); degrades to prior behaviour on any miss. Default on; off = exactly .194.
        private static readonly bool _chatGroundBackfill = AnalysisEngine.ReadBool("ChatGroundBackfill", true);

        private sealed class DryRunAgg
        {
            public int Calls, Ok, Fail;
            public List<int> Ms = new List<int>();
            public List<int> Bytes = new List<int>();
            public HashSet<string> Shapes = new HashSet<string>(StringComparer.Ordinal);
        }

        // ---- rev4 infrastructure ----
        // v1.0.160.6: concurrency is config-driven (was hard 2). These MUST stay above _jsonGate:
        // static field initializers run in textual order, so declaring them later would build the
        // semaphore from 0 and refuse every request.
        private static readonly int _jsonMaxConcurrent = AnalysisEngine.ClampCfg(AnalysisEngine.ReadInt("AnalyzeMaxConcurrentVerdicts", 5), 1, 16);
        // How many over-limit requests may QUEUE for a slot. A queued request holds its
        // reservation while waiting, so this also sizes the cap headroom below.
        private static readonly int _jsonQueueDepth = AnalysisEngine.ClampCfg(AnalysisEngine.ReadInt("AnalyzeJsonQueueDepth", 5), 0, 32);
        // How long a queued request waits before giving up with busy. 0 = old instant-reject.
        private static readonly int _jsonAdmitWaitMs = AnalysisEngine.ClampCfg(AnalysisEngine.ReadInt("AnalyzeJsonAdmitWaitMs", 25000), 0, 120000);
        private static readonly System.Threading.SemaphoreSlim _jsonGate =
            new System.Threading.SemaphoreSlim(_jsonMaxConcurrent, _jsonMaxConcurrent);   // max concurrent verdict requests
        // v1.0.160.6: DERIVED from the concurrency setting so the two admission gates can never
        // disagree. Sized for running + queued: a waiter reserves its worst-case weight while it
        // is queued, so at exactly _jsonMaxConcurrent*_jsonMaxChunk the running requests consume
        // the whole cap and every waiter would be bounced by the reservation check BEFORE it
        // reached the wait -- making the queue useless. Still explicitly overridable.
        private static readonly int _jsonBgOpCap = AnalysisEngine.ClampCfg(
            AnalysisEngine.ReadInt("AnalyzeJsonBgOpCap", (_jsonMaxConcurrent + _jsonQueueDepth) * AnalysisEngine._jsonMaxChunk),
            AnalysisEngine._jsonMaxChunk, 512);

        // v1.0.160.107 =========================================================================
        //  F7 -- INSTRUCTION PROTECTION. The system prompt, its rules and the tool wiring are not
        //  content. An extraction request is refused BEFORE the model runs (fail closed), and a
        //  verbatim-echo scrub inside ScrubPlatformInternals backstops any rephrasing that slips
        //  the detector. The protected corpus is the SYSTEM PROMPT ONLY -- domain knowledge is what
        //  answers are supposed to use, and scrubbing it would redact legitimate railway doctrine.
        // =====================================================================================

        private const string FixedExtractionRefusal =
            "I can help with railway asset analysis and monitoring. I am not able to share system "
            + "configuration details. What would you like to know about your assets or alerts?";

        // v1.0.172.0: the one-line answer the Asset Review panel gives a social or off-topic turn,
        // instead of running the agent loop and reciting the alert summary again. Deliberately short
        // and deliberately free of site/asset ids -- the panel already shows those in its header.
        private const string FollowupScopeRedirect =
            "I am here for this alert. I can pull the alert history, the current tag values, the "
            + "thresholds, or the track-health analysis -- which would you like?";

        public static int AiLogDroppedLines { get { return AnalysisEngine._aiLogDropped; } }
        public static int AiLogWrittenLines { get { return AnalysisEngine._aiLogWritten; } }
        // v1.0.160.57: 7W audit record. Default ON -- it is one log line and coverage is the point.
        private static readonly bool _audit7W = AnalysisEngine.ReadBool("AnalyzeAudit7W", true);
        private static readonly int _tprGateAttributeId = AnalysisEngine.ClampCfg(AnalysisEngine.ReadInt("AnalyzeTprAttributeId", 6), 1, 100000);

        // v1.0.160.182: ENFORCE evidence provenance (not merely tally it). Item 6/7 only COUNTED the
        // model's [fetched] labels and PROMPTED it to cross-check -- neither verified that a [fetched]
        // line was actually fetched. Result (alert 611604): a 9 h-stale DPR feed produced
        // "[fetched] DPR history shows 0 V at incidence" and a DG line for a tag never pulled, both
        // labelled [fetched]. This is the deterministic backstop: when the alerting tag's history does
        // NOT cover the incidence (_lastCoverageKind != Covered), the model cannot have fetched an
        // at-incidence sample, so every [fetched] evidence line is demoted to [unverified] and an honest
        // caveat is added. Confidence is capped ONLY when the verdict is NOT a genuine FAILURE
        // relay-trigger -- for a FAILURE the [record] relay evidence stands (doctrine FLD-001: a real
        // relay trigger is not cleared by thin analog coverage), so we correct provenance but keep the
        // confidence. Applies to EVERY cause code; gated by AnalyzeProvenanceEnforce (default true).
        // Runs BEFORE ComputeEvidenceProvenance so the meta tally reflects the corrected tags.
        private static readonly bool _provEnforceEnabled = AnalysisEngine.ReadBool("AnalyzeProvenanceEnforce", true);

        // v1.0.160.186 (#1/#4/#7): viz.threshold must be the one that FIRED (card triggering
        // condition), not a value re-derived from get_attribute_range (which was showing MinSafe).
        // For a LOWER OR-rule (value<avg*LD% OR value<MinSafe) the effective trigger is the HIGHER
        // threshold -> max of the card FAIL thresholds; for a HIGHER rule -> min. Direction from the
        // card comparison, else the cause's CauseToLogic rule (< / >). LD% is baked into the card's
        // avg threshold already. Gated AnalyzeVizCardThreshold (default true).
        private static readonly bool _vizCardThresholdEnabled = AnalysisEngine.ReadBool("AnalyzeVizCardThreshold", true);

        // v1.0.160.188: SQL-Server side link (dbo.FRSAlert_AIAnalysis in E7MRIV2DB). One row per
        // analysis SUBJECT, which is EITHER an FRS alert (FRSAlertId) OR a PendingAlertApproval
        // (PendingAlertApprovalId) -- exactly one id set, the other NULL. FirstAnalysisId = write-once
        // anchor, AnalysisId = latest (overwritten on re-run). The MERGE matches on whichever id is
        // > 0, so an FRS row and a pending row never collide, and re-runs update the right one.
        // Gated AnalyzeSqlAnalysisLink (default true); best-effort. E7MRIV2DB_SQL connection.
        private static readonly bool _analysisLinkEnabled = AnalysisEngine.ReadBool("AnalyzeSqlAnalysisLink", true);

        // v1.0.160.190: recover a thin analysis context from the FRS alert record. If causeCode /
        // siteId / assetId are blank but station+assetName are present, resolve them deterministically
        // (search_sites by station name -> search_assets by asset name -> get_frs_alerts around the
        // incidence -> the row matching this alertId/time) and fill ONLY the blank ctx fields. Then the
        // normal pipeline (CauseToLogic / CauseToAttributes / _gateById) has a real cause to work from.
        // Best-effort: any miss leaves ctx as-is (today's behaviour). Gated AnalyzeBackfillContext.
        private static readonly bool _backfillCtxEnabled = AnalysisEngine.ReadBool("AnalyzeBackfillContext", true);

    }
}