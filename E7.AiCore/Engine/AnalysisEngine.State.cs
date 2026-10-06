using Domain;
using E7MRIWeb.Areas.FRS25.Controllers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace E7.AiCore
{
    // AnalysisEngine -- per-request session, provider properties, shared static state and nested types.
    // Moved verbatim from AiChatController.cs in 1.0.165.0 (Shared AI Core 2e);
    // only access modifiers changed (private -> internal). The web app sees internals via InternalsVisibleTo.
    public sealed partial class AnalysisEngine
    {
        // ------------------------------------------------------------------
        // AlertLive AI-Analyze -- component version (Major.Minor.Build.Revision)
        // Keep in lockstep with CHANGELOG.md at the project root.
        // ------------------------------------------------------------------
        public const string ComponentVersion = "1.0.167.0";
        // 1.0.167.0  - bash removed for all users (both permit branches, AnalyzeBashInVerdict ignored, never in the
        //            model's tool list -- including the all-tools fallback -- and refused by CallToolRoutedAsync).
        //            FIX: dangling else in CallToolRoutedAsync -- the server-1 call ran after every branch and
        //            overwrote server-2 results. Shared: ScopeGuard.IsOutOfScope / VerdictRules.FilterOperationalActions
        //            gained per-product overloads (AiChat behaviour unchanged).
        // 1.0.166.0  - SHARED AI CORE 3b + 3c. 3b: the two blocks shared by the analysis entry points (role capture +
        //            body read; X-Analysis-Id header) are now controller helpers. 3c: login-user reads go through
        //            E7.AiCore.AiUserContext (registered by the controllers), so the core has no web-project reference.
        // 1.0.165.0  - SHARED AI CORE 2e. AiChat analysis logic moved verbatim into E7.AiCore.AnalysisEngine
        //            (398 methods, session, provider properties, static state, nested types); this constant moved
        //            with it. AiChatController keeps the 32 web members and calls Engine / AnalysisEngine.
        //            E7.AiCore retargeted to .NET Framework 4.8 (agreed fallback: Domain is net48) and references
        //            Domain. CircuitWisdom, RailwaySignallingWisdom, CauseLogicMaps, E7ApiToken moved into the core.

        // 1.0.164.0  - SHARED AI CORE 2d. All 118 per-request instance fields (117 here + 1 from VerdictCache.cs)
        //            moved, in order and with their comments, into the nested AnalysisSession class; the controller
        //            holds one instance (Sess). 915 references renamed _x -> Sess._x. No logic change.
        // 1.0.163.0  - SHARED AI CORE 2b-ii. Settings parsing moved to E7.AiCore.AiSettings (ReadInt/ReadDbl/ReadBool/
        //            ReadStr/ClampCfg/ReadLong are now forwarders that pass the raw appSettings value). EngineName,
        //            IsSiteKeepOverrideEmail, MaskModelIdentity moved to EngineIdentity with their config values
        //            passed in; _modelNameRx moved with MaskModelIdentity. No logic change.
        // 1.0.162.0  - SHARED AI CORE 2a-2c (structure only). New class library E7.AiCore (netstandard2.0).
        //            41 self-contained methods moved verbatim into it: AiText, AiJson, AiTime, Redaction,
        //            ScopeGuard, EngineIdentity, TrainingText, VerdictRules; 15 fields moved with them.
        //            _ddFmt, _istOffset, _verdictCategories now read from E7.AiCore. Each moved method keeps a
        //            same-signature forwarder here, so no call site changed. No logic change.
        // 1.0.161.0  - STRUCTURE ONLY (Shared AI Core step 1). AiChatController.cs split into 16 topic
        //            partial files: Endpoints, Providers, Mcp, Prefetch, Grounding.Tags/Card/Derived/Trend/
        //            Coverage, Prompts, AgentLoop, Verdict, Guardrails, Logging, Config, Util. Every method
        //            moved verbatim with its leading comments; fields, constants and nested types stay
        //            here (ClientBusyException moved to .Mcp.cs). No logic, signature or field change.
        // 1.0.160.202- DEFAULT ENGINE = QUANTUM (DeepSeek Pro) FOR ALL. Owner decision supersedes .201:
        //            _aiProviderDefault "quantum11" -> "quantum10". ResolveEngineAlias maps quantum10 to the
        //            bare "deepseek" family id, AnalysisModelOverride has no variant for it, so the DeepSeek
        //            branch runs DeepSeekModel (default deepseek-v4-pro). Provider-less callers therefore get
        //            Pro; a Web.config AiProvider key still overrides. GateProviderForRole is untouched --
        //            its ordinary-user fallthrough already returns "deepseek" (Pro), so the server and the
        //            page now agree for every role. Pairs with Index 1.0.55.173 (_aiProvider, the
        //            site-keep gate and the dropdown default all moved to quantum10). Note: Pro is the
        //            slower model; Flash (quantum11) remains selectable for privileged users.
        // 1.0.160.201- DEFAULT ENGINE = QUANTUM 1.1 (DeepSeek Flash). _aiProviderDefault fell back to
        //            "anthropic" when Web.config had no AiProvider key, so any caller that sends no
        //            provider (API posts, DryRunTools, Status) ran claude-sonnet-4-6 while the page has
        //            run quantum11 since Index 1.0.55.66. One-line default change: "anthropic" ->
        //            "quantum11". ProviderKind resolves it to deepseek and AnalysisModelOverride to
        //            ModelQuantum11 (default deepseek-v4-flash) on the existing .160.42 variant path, so
        //            provider AND model flip together. A Web.config AiProvider key still overrides it;
        //            the "?? \"anthropic\"" fallbacks at ProviderKind are unreachable (the static is never
        //            null) and were left alone. Pairs with Index 1.0.55.172 (dropdown 'selected' moved to
        //            quantum11 so the display matches the engine that actually runs).
        // 1.0.160.200- E7 TOKEN FORWARDED TO THE MCP (owner: the controller owns the token; the MCP's
        //            static startup token goes unused). The MCP server logged into E7 once at startup and
        //            kept that token, so every E7 tool call failed after the 6 h session TTL. Now the
        //            controller is the single source: new E7ApiToken.cs does a SERVICE login via
        //            /api/Login/UserAuthenticate, caches the token, refreshes it proactively (TTL minus
        //            margin) and single-flights concurrent callers through one login. CallToolRoutedAsync
        //            fetches the latest token before EVERY tools/call and forwards it as the 4th
        //            CallToolAsync arg (McpClient/McpSseClient put it on the per-request X-E7-Token header;
        //            NOT on the shared client -- _mcp is a static singleton). Reactive path: when a tool
        //            result carries the MCP's "e7_session_expired" marker the token is Invalidate()d so the
        //            NEXT call re-logs in (no method restructure, no in-place retry). No HttpContext /
        //            LoginUser dependency, so prefetch, loop, chat and SSE all get a live token. Token
        //            fetch never throws: null = "MCP falls back to its own token for this call", logged.
        //            REQUIRES: McpClient.CallToolAsync(name, args, maxLen, string e7Token = null) +
        //            MCP server honouring X-E7-Token and emitting the marker on E7 401/403. Web.config:
        //            E7LoginBaseUrl (else APIBaseUrl), E7LoginPath, E7LoginUser, E7LoginPass,
        //            E7TokenTtlMin (360), E7TokenMarginMin (30), E7LoginTimeoutSec (20).
        // 1.0.160.199- CHAT OPS V/C FIX ON THE RIGHT ENDPOINT. The .196/.197 PM operation-value fixes
        //            were on Chat(), but the web popup posts to AnalyzeChat() -- so they never took effect
        //            there. Ported: AnalyzeChat now appends BuildPmOperationValues(ctx) to the follow-up
        //            context message, so the web chat returns the last-operation voltage/current
        //            deterministically (same values as the Live Data panel). PT* + assetId only.
        // 1.0.160.198- LIVE DATA snapshot (before Refresh) showed 0 for PM operation V/C and a different
        //            tag set. The snapshot is built from get_asset_tags, which does not carry the PM
        //            "-Avg" rows, so ParseSnapshotValues' v.174 substitution was starved. Now the analysis
        //            path also fetches the live tag table (api/LiveValue, shared FetchLiveValueMap) and
        //            MergePmAvgIntoSnapshot overwrites the op V/C cards with the last-operation average +
        //            recomputes in/out -- so the FIRST render matches Refresh (LiveValues, AlertAnalysis
        //            1.0.6.7). PM assets only, one 4s fetch, no-op/graceful on miss. Also refactored the
        //            chat PM bind (.197) to use the same FetchLiveValueMap (one fetch impl).
        // 1.0.160.197- CHAT PM OPERATION VALUES BOUND (deterministic). The .196 guidance was not enough:
        //            live_get returns a positional payload the model cannot map, and predict_pm_operation
        //            can be down, so chat still reported the 0-at-rest RDPMS tags. Chat() now fetches the
        //            EdgeX PointMachine "-Avg" values server-side (api/LiveValue -> NW-V 2002/4002, NW-C
        //            1002/3002, RW-V 7002/9002, RW-C 6002/8002) and binds them into the grounding as
        //            labeled text, so the model answers from real numbers with no tool call. PT* + assetId
        //            only, 4s timeout, "" on any miss (falls back to .196 guidance). Chat and the Live
        //            Data panel now return the same value.
        // 1.0.160.196- CHAT POINT-MACHINE OPERATION V/C. Follow-up chat answered "operation voltage/
        //            current" for a point machine from the RDPMS "A End - NW-V" tag (0 between throws) or
        //            a raw history spike, so it disagreed with the Live Data panel. BuildChatGrounding now
        //            appends, for PT* causes only, the EdgeX PointMachine "-Avg" attids that carry the
        //            last-operation average (NW-V 2002/4002, NW-C 1002/3002, RW-V 7002/9002, RW-C
        //            6002/8002) and tells the model to use those, not the 0-at-rest RDPMS tags or a raw
        //            history sample. Pairs with AlertAnalysis 1.0.6.7 (same fix on the Live Data panel) so
        //            chat and panel return the same value. Guidance only (no fetch); scoped to PT causes.
        // 1.0.160.195- MEMORY-LOSS FIX (grounding backfill, option B). A follow-up chat that arrived
        //            WITHOUT the alert card built no grounding, so the model re-discovered the alert every
        //            turn (search_tags/get_frs_alerts...) and thrashed -- and even claimed it could not
        //            retrieve the dwell time that was in the card all along. Fix: when the chat POST has no
        //            card, Chat() self-heals by reloading the stored analyze input (AlertInputJson, persisted
        //            at analysis start) via the breaker-safe CacheGet (/AIAnalysis/Get?AlertId=..&IncludeJson=1),
        //            and grounds from that. Read-only; reuses proven plumbing; on any miss it falls back to
        //            prior behaviour. Requires AiAuditDbEnabled. Master switch ChatGroundBackfill (default on;
        //            off == .194). Pairs with the .194 loop guard: B stops the thrash at the source, the guard
        //            is the net. New helpers: ChatPayloadHasCard, LoadStoredAlertContext.
        // 1.0.160.194- UNLIMITED-CHAT LOOP GUARD (chat path only, rawJsonMode==false). The follow-up
        //            chat could re-issue the same lookup (search_tags) each turn and thrash to the step
        //            cap, returning "[Reached N-step limit.]" with no answer. Fix: (1) chat cap raised to
        //            a backstop ChatMaxIter (default 40, clamped >=2); (2) ChatMaxWallSec budget (default
        //            90, clamped >=5); (3) repeat-call guard on IsRepeatGuardTool (search_tags) blocks the
        //            2nd identical call and nudges the model to answer; (4) on the last turn / wall / repeat,
        //            chatForceAnswer sends an EMPTY tool array so the model must synthesize a reply (same
        //            mechanism as synthesis mode). Master kill-switch ChatLoopGuard (default on); when off,
        //            loopCap==_maxIter and every guard branch is skipped -- behaviour identical to .193.
        //            Analyze/verdict path untouched. Does NOT fix grounding loss (separate change).
        // 1.0.160.193- PER-TAG FETCH BREAKDOWN in the Log. request_end.tagFetches[] lists every
        //            history_get: attr name (TagId resolved via _tagMeta), ms, bytes, iter(-1=prefetch),
        //            and used(in driverSet) vs wasted. Plus tagFetchCount/tagWastedCount/tagWastedMs and
        //            a [TAGOPT] line naming the wasted fetches. Shows which tags cost time / were unwanted.
        // 1.0.160.192- PER-CAUSE TIMING TRACE in the Log. request_end now carries totalMs,
        //            prefetchWallMs, inLoopWallMs, toolCallMsSum, prefetch/inLoop call counts,
        //            slowestTool+ms, mode(synthesis/agentic), coverage -- and a [TIMING] log line
        //            (cause=... total=... prefetch=... slowest=pm-operation-v4:14847ms mode=...).
        //            Lets you see per cause where response time went, straight from the Log section.
        // 1.0.160.191- FOLLOW-UP CHAT GROUNDING. Chat() now reads the alert card + alertId/analysisId
        //            the web sends (same card given to analyze) and builds a grounding system prompt
        //            (alert detail + thresholds + relays + resolved values + the cause's FRS logic from
        //            CauseToLogic) passed to RunAgenticLoop via systemPromptOverride -- so the follow-up
        //            model answers from context in 1-2 steps instead of re-discovering the alert and
        //            hitting the step limit. bash is withheld from grounded follow-up chat (kept for
        //            pure free-chat). Fixes "chat not working" on data-heavy questions.
        // 1.0.160.190- THIN-CONTEXT SELF-HEAL. BackfillContextAsync recovers a missing
        //            causeCode/siteId/assetId BEFORE analysis: fetch_alert_details_frs by alertId (one
        //            call, even from near-empty context), else search_sites->search_assets->get_frs_alerts
        //            around the time and match the alert. Fills only blank ctx fields, then the normal
        //            pipeline (CauseToLogic/_gateById) has a real cause instead of thrashing blind.
        //            Hooked on BOTH AnalyzeAlert (SSE) and AnalyzeAlertJson. Gated AnalyzeBackfillContext,
        //            best-effort (failure = today's behaviour). Fixes INCONCLUSIVE-from-thin-context
        //            (e.g. 775072: no causeCode/siteId/assetId -> 35 blind tool calls).
        // 1.0.160.189- MERGE: X-Analysis-Id response header on BOTH AnalyzeAlert (SSE) and
        //            AnalyzeAlertJson (JSON, which this tree now audits) so the web can read the
        //            AnalysisId off the response for reopen / rating / chat / SQL link. PM-analysis
        //            work and JSON-path audit from this tree are preserved unchanged; the only
        //            controller add over 1.0.160.188 is the header.
        // 1.0.160.188- SQL-SERVER ANALYSIS LINK now source-aware: subject is EITHER an FRS alert
        //            (FRSAlertId from ctx.alertId) OR a PendingAlertApproval (PendingAlertApprovalId). The web reuses ctx.alertId for the id in BOTH paths, so a
        //            discriminator (ctx.source=="pending" / ctx.isPending / explicit
        //            ctx.pendingAlertApprovalId) decides the column. Exactly one id column
        //            set; MERGE matches on whichever is >0 so the two sources never collide. A [SQLLINK]
        //            log prints both ids per run so you can verify what the web sent on each path.
        // 1.0.160.187- SQL-SERVER ANALYSIS LINK. Mirror the Postgres AnalysisId into SQL Server so the
        //            web has it on REOPEN (per-user chat/remarks/rating bind to it). New table
        //            dbo.FRSAlert_AIAnalysis (E7MRIV2DB): one row per FRS alert, FirstAnalysisId =
        //            write-once anchor, AnalysisId = latest. WriteAnalysisLink runs a MERGE right after
        //            AuditStartAnalysis returns the id (E7MRIV2DB_SQL connection). First analysis inserts
        //            (both = new id); re-run updates AnalysisId only. Gated AnalyzeSqlAnalysisLink
        //            (default true); best-effort, wrapped -- never fails the verdict.
        // 1.0.160.186- RE-APPLY the alert-analysis fixes that collided with this tree's PM-Ops
        //            .183/.184/.185 (those version numbers here are PM-Ops/PmOpsV4, not the alert
        //            fixes). This build carries all three onto the current tree:
        //            (#6) viz.secondary COERCE not REJECT -- non-numeric secondary.value drops the
        //                 optional block instead of failing the verdict.
        //            (#1/#4/#7) OverrideVizThresholdFromCard -- viz.threshold from the card trigger:
        //                 lower rule -> max(avg*LD%, MinSafe), higher rule -> min(avg*LD%, MaxSafe);
        //                 direction from card comparison else CauseToLogic. Fixes threshold mismatch,
        //                 card-avg-vs-analysis-minsafe, and PT R IND VOLT LOW AT LOC (min->max).
        //            (#2) IPS cross-asset fetch -- VIPS/IIPS drivers resolved on the site's IPS asset
        //                 (AssetTypeId 39) instead of being dropped; card-gated, cached per site.
        //            All gated (AnalyzeVizCardThreshold / AnalyzeIpsCrossAsset default true) and
        //            best-effort. (.182 provenance was already present in this tree.)
        // 1.0.160.185- CURRENT CURVE, DOWNSAMPLED. The Class Timeline tiles draw each stroke's
        //            current waveform, which .183 dropped wholesale -- so the tiles could never
        //            show it. Sending the raw arrays is not an option: measured on the ARNETHA
        //            asset, current[] alone is ~4 MB across 1706 ops (voltage doubles it).
        //            SlimPmOps now keeps current[] but resamples it to 40 evenly spaced points at
        //            2 decimals -- ~200 bytes an op, ~333 KB for the same asset, and visually
        //            identical in a 96x46 px tile. voltage[] is still dropped entirely: nothing
        //            in the popup draws it.
        // 1.0.160.184- WRONG ENDPOINT in .183. PmOpsV4 called the pm-operation-v4 route the .176
        //            prefetch uses; the running analysis API is HistorianApiBaseUrl + api/pm4/analyze
        //            with SiteId/AssetId/WindowDays/AssetName/SiteName (the same call the Point
        //            Machine Operation page's Model 4 makes -- see PointMachineOperationController
        //            GetPm4Analyze). Now calls that, adds ForceLive for an explicit reload, and
        //            keeps cond_notes on each op (it carries the detector's sigma/shape reasoning,
        //            e.g. "END-FRICTION: end-rise 6.0 sigma above this machine's normal").
        //            Confirmed against a real ARNETHA 119/120 response: ops[] carry features{} for
        //            all 13 measurements plus current[]/voltage[] sample arrays -- those arrays are
        //            exactly what SlimPmOps drops.
        // 1.0.160.183- PM ANALYSIS VISUALS need the per-operation rows this controller strips.
        //            The .176 prefetch fetches pm-operation-v4 server-side and then removes ops[]
        //            from both sides before streaming the 'pm' event, and the cache-serve path
        //            emits no 'pm' event at all -- so the popup has never had the data the
        //            Machine A/B cards, Timeline, Measurement trend and Class Timeline are built
        //            from. New [HttpGet] PmOpsV4 serves that data same-origin (the browser must
        //            not call the v4 host directly: it is http, the page is https). SlimPmOps
        //            keeps only the eight fields those four visuals read and drops everything
        //            else -- above all the waveform sample arrays, which would turn a few
        //            hundred KB into tens of MB. The SSE 'pm' event is UNCHANGED: the compact
        //            PM card keeps working exactly as before, including when this new endpoint
        //            fails.
        // 1.0.160.182- ENFORCE EVIDENCE PROVENANCE, ALL CAUSES (owner: "why [fetched] on tags never
        //            fetched / not covering incidence -- fix for all causecode"). Item 6/7 only TALLIED
        //            the model's [fetched] labels and PROMPTED a cross-check; neither verified a
        //            [fetched] line was actually fetched. Alert 611604 (re-run 25 Aug): DPR feed 9 h
        //            stale (history ends 01:13, incidence 10:21) yet verdict cited "[fetched] DPR
        //            history shows 0 V at incidence, 29 V before/after" AND "[fetched] DG mA 133.6" for
        //            a tag never pulled -- both fabricated, only 1 history_get ran. New
        //            EnforceEvidenceProvenance runs before the tally: when the alerting tag's coverage
        //            is not Covered (_lastCoverageKind), the at-incidence sample cannot have been
        //            fetched, so every [fetched] evidence line (both languages) is demoted to
        //            [unverified] and an honest caveat is added. Confidence is capped to
        //            AnalyzeDowngradeConfidenceCap ONLY when the verdict is NOT a genuine FAILURE
        //            relay-trigger -- for a FAILURE the [record] relay evidence stands (FLD-001: a real
        //            trigger is not cleared by thin analog coverage), so provenance is corrected but
        //            confidence kept. Deterministic, all 147 causes, gated by AnalyzeProvenanceEnforce
        //            (default true). NOTE: this stops the FABRICATION; it does not yet FETCH the missing
        //            terms -- that is VerifyCauseConditions (analog+relay+IPS full-term fetch), still
        //            pending the relay-read decision.
        // 1.0.160.181- LOG THE REAL CAUSE OF A MODEL-CALL FAILURE (owner: field diagnosis of the
        //            "An error occurred while sending the request." banner). The model-call catch
        //            logged only SanitizeJsonError(ex.Message), which flattens to "internal error"
        //            and discards the inner exception -- so a bytesRecv=0 connection failure (DNS,
        //            outbound 443 blocked, TLS handshake, dead host) was indistinguishable from a
        //            provider error in the server log. New DescribeException(ex) walks the whole
        //            inner chain and surfaces SocketException.SocketErrorCode and WebException.Status
        //            (the fields that separate DNS vs firewall vs TLS), logged at ERROR to the SERVER
        //            log only -- the client-facing message is unchanged (H4 redaction intact). Root
        //            case in the field was a data-centre network blip wedging the outbound connection
        //            pool; an app restart cleared it. This line makes the next occurrence self-name.
        // 1.0.160.180- CACHE-SERVE SHOWED A FROZEN RESET STATE (owner: "when cache value show, check
        //            alert set condition from api -- if reset/set show exact state"). A verdict cached
        //            while the alert was ACTIVE stored resetTime empty; TryServeFromCache replayed that
        //            stored context_patch verbatim, so re-opening a since-cleared alert still printed
        //            "Still active. No reset recorded" on the panel. Cache staleness rule 5 ("a reset
        //            arriving after the stored analysis") only marks the row stale for a FRESH run; the
        //            SERVE path itself never re-checked the live state. Now, on serve, when the cached
        //            resetTime is empty, TryLiveResetTimeAsync re-fetches the current FRS row for this
        //            alert (get_frs_alerts -> mFRSAlerts -> match Id -> ResetTimeStamp) and, only if it
        //            is now reset, injects the live stamp into context_patch before it is emitted.
        //            GATED to the empty-reset case: a cache already holding a reset is finished history
        //            that cannot change, so it keeps its zero-latency serve and fires no extra call.
        //            Fetch failure returns null and leaves the cached value untouched (no regression).
        // 1.0.160.179- PM-OPERATION-V4 SERVER-SIDE FETCH TIMEOUT 8s -> 60s (owner: "make it 60 sec").
        //            The point-machine PM Analysis card is filled by a server-side GET to
        //            172.31.25.102:8005 pm-operation-v4, injected to the page via the SSE 'pm' event.
        //            v4 compute is intermittently slow (GAHLOTA 167/41898 took >8s -> TaskCanceled),
        //            so the fetch died on the 8s HttpClient timeout, window._aiPm stayed null, and the
        //            card fell back to the browser LOAD ANALYSIS path -- which targets the LAN IP the
        //            browser cannot reach, surfacing the misleading "endpoint unreachable". Raising the
        //            server-side timeout lets slow computes still inject, so the fallback never fires.
        //            One-line change (_pmHc.Timeout); payload is ~1.8KB, cost is compute latency only.
        // 1.0.160.167- MAINTENANCE PLAN RULES REACH THE FOLLOW-UP TOO (owner: "this should apply to
        //            aichat also"). The follow-up chat is a free-text surface -- an operator can and
        //            does ask "iss station ka plan bana do" inside an alert popup -- and it had
        //            NEITHER the railway-only scope rule NOR the plan format. The ChatBot got both
        //            in 1.2.9.3/1.2.9.5; this brings the same two rules to the follow-up path, in
        //            the same words, so an answer does not change character with the surface it was
        //            asked on. Attached to the DEFAULT prompt only (sysBase), so the verdict path --
        //            which passes systemPromptOverride and has its own schema -- is untouched.
        //            15-day evidence, the 50-row paging instruction, the ordering and the [[CARD]]
        //            block form are identical to the ChatBot text; views 1.0.55.112 render them.
        // 1.0.160.166- "No adjacent track telemetry" WAS A GATE, NOT A DATA GAP. PrefetchAlreadyHas
        //            was keyed on the TOOL NAME ALONE, so once prefetch had called get_asset_tags for
        //            the alerting asset, the SAME call for a DIFFERENT asset was refused with
        //            "Calling it again returns identical bytes" -- which is false for different args,
        //            and the log shows it happening (18.08 05:43, get_asset_tags AssetId 2223 denied
        //            after the model had legitimately walked search_tags/search_assets/get_frs_alerts
        //            for that same asset). Family tracks arrive in the range-history payload
        //            ("familyTracks":[{"TrackFamilyId":41869},...]) but their TagIds could then never
        //            be resolved, so history_get had nothing to ask for. Meanwhile the prompt tells
        //            the model to "check at least one adjacent family-track log" -- an instruction the
        //            gate made impossible. The existing search_tags DEAD END GUARD documents exactly
        //            this failure mode; it was just written for one tool and one condition.
        //            The denial is now scoped to the PREFETCHED ASSET: a call naming a different
        //            asset/track/site passes through, a genuine repeat is still refused, and every
        //            refusal logs the asset it was scoped to so the next one is visible.
        // 1.0.160.165- THE ACTION IS A FIELD INSTRUCTION, so it carries the same railway-only rule
        //            the ChatBot maintenance answers now do (owner). recommended_action /
        //            recommended_action_en must name railway signalling work ONLY: never check the
        //            modem, restart the gateway, chase OFC/4G/MQTT/TCP, inspect the A10/ADC/
        //            datalogger card, raise it with the monitoring team, or clear/acknowledge
        //            stale alerts. New FilterMonitoringAction() strips such sentences and, when
        //            nothing railway-side remains, replaces the field with a plain statement that
        //            no railway field action follows from this alert.
        //            DELIBERATELY NOT APPLIED TO THE DIAGNOSIS. likely_cause, category, evidence and
        //            caveats must still be able to say a reading came from the measurement chain --
        //            "Platform (zero-insert)", "Datalogger" and "A10 transition" are the owner's own
        //            false-alert categories, and this system's highest-value output is catching an
        //            alert that is NOT a field fault. Naming that cause is diagnosis; sending a
        //            technician after it is an instruction. Only the instruction is constrained.
        // 1.0.160.164- DIAG PICKERS MOVED SERVER-SIDE (owner). The page fetched the historian
        //            directly from the browser, which published the proxy host to every client,
        //            needed CORS, and put an internal endpoint in the network tab. This file now
        //            owns DiagProxyGetAsync() -- one short-timeout HttpClient of its own, base from
        //            ConfigurationManager.AppSettings["ProxyBaseUrl"] (DiagHistorianBase kept as a
        //            fallback so an existing deployment does not lose its setting). AiDiagController
        //            1.0.0.3 exposes Sites()/Assets(siteId) on top of it, same [Authenticate] +
        //            DiagAllowedCore() gate. New key: DiagProxyTimeoutMs (default 8000).
        // 1.0.160.163- GREETING: honorific + tone (owner). The name now carries "sir" -- "Anil sir",
        //            "Siddharth sir" -- and the whole line uses a courteous register. Humour is
        //            the RARE seasoning, respect the constant: a light, still-deferential touch
        //            appears ONLY on already-reset or low-stakes predictive alerts; a Failure or a
        //            firm high-confidence CONFIRMED always gets the plain respectful line, never a
        //            quip (a joke above "megger the section" reads as flippant about a safety
        //            event). GreetingHonorific (default "sir") and GreetingTone
        //            (plain|light|off, default light) are config, so the register dials without a
        //            redeploy. Still header-only, flag-gated, logged-in name only.
        // 1.0.160.162- GREETING line (owner: option D). A short, warm header shown at the top of the
        //            analysing panel while the verdict is computed -- time-of-day + first name, a
        //            task synopsis (this asset's recent same-cause count when Layer A ran), and a
        //            rotating task-reassurance phrase. It is a HEADER: emitted as its own SSE event
        //            right after meta, never touches the verdict path, and its absence changes
        //            nothing. Name is shown only to a logged-in session (privileged capture); the
        //            whole feature is behind AnalyzeGreetingEnabled (default OFF). Deliberately
        //            TASK reassurance, not personal check-ins: on a safety surface the calming
        //            thing is competence ("I've pulled the telemetry and history"), not small talk.
        // 1.0.160.161- AiDiag 403 fix (AiDiagController 1.0.0.2). Adds the internal
        //            CaptureLoginUserForDiag() shim so the split controller performs the same
        //            login capture the old in-controller Diag actions did as their first
        //            statement -- DiagAllowedCore() reads _luSiteKeep/_luEmail, which do not
        //            exist until that runs. CaptureLoginUser() itself stays private: every
        //            analysis path depends on where it is called. The other two causes of the
        //            403 were in the new controller (missing [Authenticate] filter; IIS custom
        //            errors replacing the body) -- see that file's history.
        // 1.0.160.160- DIAG SPLIT OUT (owner: "one separate controller with view for Diag").
        //            New AiDiagController (v1.0.0.1) owns the routes, the gate decision, the view
        //            and the response; Views/AiDiag/Index.cshtml (v1.0.0.2) replaces
        //            Views/AiChat/Diag.cshtml. AiChatController.Diag.cs now holds NO ACTIONS --
        //            only the probe catalog, exposed as internal DiagRunCoreAsync() plus
        //            DiagAllowedCore()/DiagHistorianBase(). The catalog stays here because it
        //            calls ~19 private members of this controller; widening or duplicating them
        //            would be worse than one internal seam. The GATE is unchanged in behaviour
        //            and still has exactly ONE implementation -- a security gate copied into two
        //            files is a gate that drifts.
        //            ROUTE CHANGE: /FRS25/AiChat/Diag -> /FRS25/AiDiag ; /FRS25/AiChat/DiagRun ->
        //            /FRS25/AiDiag/Run. The LogWatch config must be updated with the new route.
        // 1.0.160.159- THE LOG TRANSPORTS WERE THE REMAINING LEAK. A second browser review traced
        //            vendor identity after .156 -- not through the audit SSE (sealed) but through
        //            the RUN LOG surfaces: RunLogFeed projects the on-disk audit_7w JSONL
        //            (which.provider/model, where.providerUrl) raw to the viewer tab, and
        //            DownloadAiLogs zips the raw day files, whose log LINES name the vendor.
        //            Rule .156 applies to every browser-bound transport: (1) RunLogFeed rows now
        //            pass through RedactAuditForClient(r, false) -- calling name kept, vendor
        //            fields removed, serialised scrub backstop; (2) DownloadAiLogs scrubs each
        //            text file with ScrubEngineIdentity while zipping -- calling names survive so
        //            downloaded logs stay debuggable; on-disk files untouched (ops truth).
        //            _vendorIdRx widened with gemini/mistral/llama/grok/qwen families so the
        //            scrub holds whichever provider a calling name maps to next.
        //            OPEN (from the same review, not fixed here): the provider dropdown can show
        //            nexus10 while the session default runs quantum11 -- UI state vs session
        //            engine mismatch, needs repro; and the data-residency observation (telemetry
        //            tokens leave for the provider) is a policy item, not a code defect.
        // 1.0.160.158- Diag gate tightened (owner): site-keeping REQUIRED, and when
        //            DiagAllowedEmails (web.config, semicolon/comma list) is non-empty the
        //            logged-in email must ALSO be on it. Empty/absent key = site-keeping alone,
        //            so a missing key can never lock the page. Both Diag and DiagRun swapped
        //            from AiDebugAllowed() to the new DiagAllowed(); 403 text made generic so
        //            the response does not teach the gate. LogWatch's service account email
        //            must be on the list once the key is set.
        // 1.0.160.157- DIAGNOSTIC DASHBOARD (design GO). New partial AiChatController.Diag.cs +
        //            new standalone page Diag.cshtml (own strand, v1.0.0.1). /FRS25/AiChat/Diag
        //            renders the page (AiDebugAllowed gate); /FRS25/AiChat/DiagRun executes the
        //            probe catalog against an operator-picked site/asset and returns classified
        //            tiles as JSON (LogWatch calls the same route). Probes reuse the REAL
        //            analysis plumbing -- LoadAllToolsAsync, PrefetchCallAsync, the .155
        //            SiteIds+AssetIds asset-history shape, NewRangeHistoryArgs, and the
        //            production HistWindowArgs/HistFetchAsync path so the HistGetSendsUtc
        //            conversion under test is the one analyses use. Classification is the
        //            week's three failure classes: E7-failure text = FAIL (session), unusable
        //            text = FAIL (schema rejection, "SiteIds (int array) is required."),
        //            thin = WARN. Site/asset pickers are fetched live from the Historian API
        //            (historian-client_11 pattern; DiagHistorianBase config, default
        //            proxy.energy7.org:8083) and those fetches ARE checks D0a/D0b. E-group
        //            tiles SKIP naming their DataAPI dependency. No model calls anywhere.
        //            NOTE FOR BUILD: AiChatController.Diag.cs needs a csproj <Compile Include>;
        //            Diag.cshtml a <Content Include> -- csproj is not in this package.
        // 1.0.160.156- CALLING NAMES ONLY, for every session. An independent browser review traced
        //            provider/model/providerUrl from window._aiLastMeta -- through the .119 gate,
        //            because the reviewing session WAS site-keeping and the privileged copy was
        //            deliberately unredacted. Owner's ruling extends .155: "we know what calling
        //            name is what -- imprint calling names instead." The client audit copy now
        //            ALWAYS goes through the redaction path, privileged or not: engine stays the
        //            opaque calling id (nexus*/orion*/quantum*), provider/model/providerUrl are
        //            removed, and the serialised scrub replaces vendor strings anywhere else in the
        //            payload (including inside prompt text). Engine-mismatch debugging keeps
        //            working client-side because cache rule 3 compares ENGINE strings -- the
        //            calling names. Non-privileged sessions still receive audit_denied (the .119
        //            withhold is unchanged). The server-side audit row and JSONL keep the real
        //            vendor; that is where it belongs. One argument changed at one call site.
        // 1.0.160.155- TWO FIXES ON .154, both from the first live run (23:28 IST, run
        //            sse-2f3e1c4436d8):
        //            (1) THE ASSET-HISTORY CALL VIOLATED THE TOOL SCHEMA. get_frs_alerts declares
        //                SiteIds REQUIRED -- stated in this file's own v1.0.118.0 note -- and
        //                NewAssetHistoryArgs sent AssetIds alone, modelled on the probe's FALLBACK
        //                branch. Server answered 32 bytes: "SiteIds (int array) is required."
        //                Now sends SiteIds + AssetIds together; the ahist task also requires a
        //                non-empty siteId.
        //            (2) THE FAILURE WAS SILENT -- the reduction returned null and nothing logged,
        //                the same ON-and-empty invisibility .150 was built to kill. When the flag
        //                is ON, every run now logs either the summary line or a WARN carrying the
        //                head of whatever came back ("SiteIds (int array) is required." would have
        //                named this bug on the first run).
        //            ALSO, owner decision: cached_engine REMOVED entirely -- "model never display,
        //            just cache/fresh". .153's site-keeping gate is gone; no session receives
        //            vendor identity on the badge. Engine/ModelId remain on the audit rows and in
        //            the 7W audit block. The view segment is removed too (1.0.55.104), because
        //            CACHED rows stored before .155 still carry cached_engine inside their copied
        //            payload -- the controller change alone cannot clean those.
        //            NOTED, not chased: the wire shows PageSize=10 on both frs calls despite 25/50
        //            in the builders -- something downstream clamps it, so a busy asset's 30-day
        //            history is at most 10 rows. Raised for the EdgeX team with the sort order
        //            question (asc would return the OLDEST 10 -- the history_get trap).
        // 1.0.160.154- ASSET HISTORY, Layer A (design 6b defaults; owner GO). The +/-2h site window
        //            answers "what else fired around this asset"; nothing answered "is this asset a
        //            repeat offender". New parallel get_frs_alerts call with AssetIds over a
        //            configurable lookback (AssetHistoryLookbackDays, default 30, clamp 7-90),
        //            OFF by default (AssetAlertHistoryEnabled). Reduced controller-side to a
        //            summary -- total, by-cause spread, same-cause count with last occurrence and
        //            reset latency, span, TRACK-family rollup -- injected as a labelled
        //            ground-truth evidence block (numbers + stated reading boundaries, no pre-baked
        //            severity score) and attached to the verdict as asset_history for the story's
        //            new pie chapter (views 1.0.55.103). Failure, including an E7-API 403 via the
        //            .153 DropE7ApiFailure path, just leaves the chapter absent -- context, not a
        //            required input, so it never degrades the verdict on its own.
        //            Also: DegradedInputs sent as a COLUMN on Complete (queryable for the future
        //            AssetId= analysis-history endpoint and for LogWatch) -- harmlessly dropped by
        //            the server whitelist until DataAPI adds the column; the .153 in-JSON stamp
        //            remains the authority. Layer B (prior AI verdicts) deliberately NOT here:
        //            blocked on the AssetId= read endpoint and on Layer A proving out first.
        // 1.0.160.153- E7 API 403s ARE NOT DATA. 15.08 17:05-17:34 IST the E7 API session expired;
        //            get_attribute_range / get_attribute_range_history / get_frs_alerts returned the
        //            ERROR TEXT ("E7 API returned 403: Session expired...") as a normal tool RESULT.
        //            84/60 bytes, non-empty -- so the .150 size line said INFO and four displays
        //            degraded with no cause named. Worse: the 17:34 analysis of 541980 ran on those
        //            starved inputs, was cached, and replayed at 20:41 -- NOT_CONFIRMED 72, where the
        //            healthy re-run said CONFIRMED 82. Three changes:
        //            (1) the prefetch recognises the E7-API failure text on range/rangeHist/frs/pred,
        //                WARNs with the status line and treats the input as MISSING, so the
        //                .150/.151 diagnostics fire correctly;
        //            (2) a verdict produced with any such failed input is stamped degraded_inputs
        //                (+ degraded_note) in the emitted AND stored payload;
        //            (3) TryServeFromCache refuses degraded rows (same policy as the .148
        //                thin-verdict refusal) -- one healthy re-analysis replaces them. While an
        //                outage lasts every open re-analyses; accepted -- honest current state over
        //                a cheap wrong answer.
        //            Also: cached_engine (vendor/model identity) is now sent only to site-keeping
        //            sessions (same class as the .143 audit-SSE leak), and cached_at is emitted as
        //            ISO yyyy-MM-ddTHH:mm:ss -- the view truncated at 16 chars assuming ISO, so the
        //            locale string "8/15/2026 8:47:35 PM" rendered as "8/15/2026 8:47:3".
        //            (AnalysisCompletedUtc carries IST wall-clock despite the name -- recorded, not
        //            changed here.)
        // 1.0.160.152- BUILD FIX: CS0841, rangeHist used before declaration. My scope check searched
        //            for "rangeHist" and matched L11312 -- which is `rangeHistT`, the TASK. The
        //            STRING is awaited 50 lines later at L11392, after the log line. A substring
        //            match found a different symbol and I read it as confirmation.
        //            Second time today the same check has failed the same way: `_trainCtxBase`
        //            matched the prefix of `_trainCtxBaseUrl`. A name check must match the whole
        //            identifier AND be scoped to the enclosing method -- I had been doing neither.
        //            rangeHist dropped from the line; tags/range/alerts are the three that matter,
        //            and all three resolve above it.
        // 1.0.160.151- The envelope failure is REPRODUCIBLE, not transient -- 17:07 and 17:11 both
        //            showed it, so my "probably a blip" read in .150 was wrong.
        //            Both failing tools depend on siteId, by two different routes:
        //              get_attribute_range -- inside `if (!IsNullOrWhiteSpace(siteId))`, so a blank
        //                                     siteId means it is never even PLANNED
        //              get_frs_alerts      -- planned regardless, but built with
        //                                     NewFrsAlertsProbeArgs(siteId, ...), so a blank siteId
        //                                     sends an empty SiteIds array and returns nothing
        //            (My first draft of this note said both sat inside the guard. They do not --
        //            checked before shipping.) One blank field produces all four
        //            reported symptoms -- missing envelopes, missing averages, blank assetType and
        //            "Still active" on a cleared alert -- and nothing logged it, so it looked like
        //            four separate regressions.
        //            .151 logs the empty-siteId branch explicitly. Combined with .150's input-size
        //            line, the next run distinguishes NOT CALLED from CALLED-AND-EMPTY, which is the
        //            distinction this whole investigation kept turning on.
        // 1.0.160.150- INSTRUMENT. The 17:07 run showed "configured envelope unavailable" on every
        //            component and "Still active" on a cleared alert -- and the only clue was one
        //            line, "baseline from alert card (get_attribute_range unavailable)". The
        //            assetType/resetTime fill never logged either, meaning get_frs_alerts also came
        //            back empty. Two upstream tools returning nothing produces four unrelated-looking
        //            display faults, and there was no line saying the inputs were missing.
        //            Now the prefetch states the size of every input it received and WARNs when
        //            range or alerts is empty, so a transient tool failure can be told apart from a
        //            code regression without guessing.
        // 1.0.160.149- A REUSED ANALYSIS MUST BE A CLONE, not a subset. A fresh run emits FOUR things
        //            to the browser -- verdict, asset_condition, snapshot and context_patch -- and
        //            .144 stored only the verdict. So even a full stored verdict rendered without the
        //            Live data chapter, without the asset-health band and without the station /
        //            assetType / resetTime fill. All four are now stored together and replayed in the
        //            same order, context_patch first because the view merges it before rendering.
        // 1.0.160.148- THE THIN VERDICT WAS REPRODUCING ITSELF. .147 fixed the read and the cached
        //            verdict now renders CONFIRMED 82% with a REUSED badge -- but LIKELY CAUSE and
        //            ACTION were still "-", because every stored row is 364-366 chars: the four-field
        //            summary written before .144.
        //            And it could not heal on its own, because of a trap I built in .140: the
        //            cached-serve path copies the SERVED payload into the row it opens. So a thin row
        //            produced another thin row on every open, no fresh analysis ever ran, and .144
        //            never got the chance to store a full one. Only a manual Re-run escaped it.
        //            Now the cache REFUSES a verdict with neither likely_cause nor recommended_action
        //            -- it cannot be rendered, so it is not worth serving. One re-analysis writes a
        //            full row and the loop ends by itself.
        // 1.0.160.147- THE CACHED VERDICT RENDERED BLANK BECAUSE IT WAS DOUBLE-WRAPPED. The owner was
        //            right that it loaded from the DB -- the log says "HIT for alert 541980 ->
        //            analysis 211 reused (no model call)" and it returned in under a second.
        //            TryServeFromCache returns the stored ENVELOPE {"verdict":{...}}, and the SSE
        //            write wrapped it a second time. The browser therefore received
        //            {"verdict":{"verdict":{...}}} and read every field off the wrapper: blank
        //            assessment, 0% confidence, INCONCLUSIVE -- and a FRESH ANALYSIS badge, because
        //            served_from_cache was on the inner object where the badge never looked.
        //            I read the blank fields as "nothing was stored" and went looking at the write
        //            side twice. The payload was thin AND the read was wrong; only one of those was
        //            visible, and I stopped at the first explanation that fitted.
        // 1.0.160.146- INSTRUMENT, not a fix. The popup showed FRESH ANALYSIS with INCONCLUSIVE, 0%
        //            and every field blank -- and the log cannot say why, because the SSE path never
        //            records the verdict it produced. Row open, tool calls and completion size are
        //            all logged; the verdict is not. So an empty answer and a full one are
        //            indistinguishable after the fact.
        //            Row 211 stored 161 chars, which is far below even the four-field summary, so
        //            the model very likely returned little or nothing usable -- but that is an
        //            inference, and I would rather log it than keep inferring. Every verdict now
        //            states its verdict, confidence, likely_cause, field count and payload size, and
        //            WARNs when it is thin.
        // 1.0.160.145- FOLLOW-UP CHAT could not run per-asset tools. BuildFollowupContextMessage
        //            carried alertId, station, assetName, causeCode and the alert text -- but NOT
        //            siteId and NOT assetId, which are exactly the required arguments of
        //            predict_track_health(SiteId, TrackIds:[AssetId]), predict_pm_operation and
        //            get_attribute_range. A follow-up about glued joints or sleepers therefore had
        //            the rule (.131) telling it to consult the ML and NO IDS to call it with, so it
        //            answered from the verdict text instead.
        //            The asset NAME was present and the IDENTIFIER was not -- which reads exactly
        //            like forgetting the asset mid-conversation: it knew what the asset was called
        //            and could not address it. Both ids now carried, with the tools that need them
        //            named so the connection is explicit rather than left to be inferred.
        // 1.0.160.144- THE CACHE NOW HITS -- and served an EMPTY verdict. "HIT for alert 541980 ->
        //            analysis 209 reused (no model call)" is correct and the chain works; what was
        //            stored was wrong. .141 wrote _lastVerdictSummary, which carries four fields and
        //            drops likely_cause, action, evidence, story and viz -- so the popup rendered
        //            INCONCLUSIVE at 0% with every row blank. 364 chars was the tell.
        //            Now the FULL vObj is stored, at the point it is sent to the browser, so a reused
        //            verdict is byte-identical to the one first served.
        //            Rows written before this build carry the truncated payload and will render
        //            empty; a single Re-run per alert replaces them.
        // 1.0.160.143- AuditCompleteAnalysis logged NOTHING. The open logs ("analysis row N opened")
        //            and the close was silent, so a row completed WITH a verdict and a row completed
        //            EMPTY looked identical in the log -- and an empty one is precisely what stops
        //            the cache serving. Chasing this took four rounds partly because I could not see
        //            which had happened. Now every completion states whether a payload went with it.
        //            Same rule I put into two system prompts this week: absent is not negative.
        // 1.0.160.142- BLANK POPUP, my regression from .139. I emitted WriteSse("analysis_id", ...)
        //            immediately after AuditStartAnalysis -- 73 lines BEFORE the SSE headers are
        //            written. The comment sitting on the very next WriteSse says it outright:
        //            "MUST be after the SSE headers above -- writing before them corrupts the
        //            stream." Writing SSE data first sends the body before the headers, the browser
        //            gets a malformed stream, and the popup renders nothing.
        //            The log shows it exactly: row opened, cache checked, then silence -- no
        //            prefetch, no model call, no completion. And because the run died before
        //            completing, each attempt left ANOTHER orphaned RUNNING row, which then made the
        //            cache report "stored analysis is RUNNING" on the next try. One bad line
        //            manufactured both symptoms.
        //            Moved to sit beside the meta event, after the headers.
        // 1.0.160.141- THE ACTUAL REASON THE CACHE NEVER SERVED. `_lastResponseFull` -- the field
        //            AuditCompleteAnalysis writes into AnalysisOutputJson -- is assigned ONLY inside
        //            AnalyzeAlertJsonCore. The SSE path (AnalyzeAlert, which every popup uses) never
        //            touches it, so EVERY streamed run has been writing a COMPLETED row with NO
        //            VERDICT PAYLOAD since the audit went in. The cache then rejected each one, as
        //            it should.
        //            .138 and .140 were both necessary and neither was sufficient: .138 stopped an
        //            orphaned RUNNING row blocking the scan, .140 stopped a payload-less row being
        //            treated as serveable -- but with no row ever carrying a payload, the cache would
        //            still have served nothing. The rows were empty at the source.
        //            Now the SSE path stores the verdict envelope before completing the row.
        // 1.0.160.140- The cache got one gate further and stopped again: "stored row has no verdict
        //            payload". The .138 scan tested STATUS only, so it skipped the RUNNING row and
        //            landed on a COMPLETED row whose AnalysisOutputJson was empty -- just as
        //            unusable. Status was never the right test; SERVEABLE means terminal AND
        //            carrying a verdict. Also: the cached-serve completion now writes the served
        //            payload, so closing an audit row cannot manufacture the next payload-less row.
        // 1.0.160.139- Emits analysisId with the verdict so the UI can attach alert-validity
        //            feedback to the right analysis row. The feedback ENDPOINT itself lives in
        //            AlertAnalysisController (1.0.6.7) -- it is a UI action about an alert, not part
        //            of the AI analysis flow, and that controller already owns the other UI-support
        //            endpoints and the DataAPI POST pattern.
        // 1.0.160.138- THE STORED VERDICT WAS STILL NOT SERVED, even with force=false. The log said
        //            "miss for alert 541980: stored analysis is RUNNING". Two faults, both mine:
        //            (1) the cached-serve path opened an audit row and returned WITHOUT completing
        //            it -- exactly the Gap 1 I described and did not fix. Now completed with a
        //            servedFromCache marker.
        //            (2) worse, the cache read rows[0] only, so ONE orphaned RUNNING row blocked
        //            that alert's cache permanently. A single interrupted run poisoned every later
        //            open. The cache now scans for the newest TERMINAL row.
        //            (2) is the real defect: (1) creates orphans, but any interrupted request could
        //            too, and one orphan should never disable a feature for good.
        // 1.0.160.137- BUILD FIX: CS0103 _trainCtxBase. The field is _trainCtxBaseUrl. I verified it
        //            with `grep -c` and read 3 hits as "it exists" -- but that counted the
        //            declaration, one real use, and MY OWN typo. A reference count is not an
        //            existence check; only a DECLARATION match is. Same mistake shape as the earlier
        //            invented symbols, caught by the compiler rather than by the sweep that was
        //            supposed to catch it.
        // 1.0.160.136- TRAIN CONTEXT REGRESSION, caused by my own SL 7 fix. ResolveStationCode returns
        //            ctx["station"] FIRST -- but that is the station NAME ("SAKHUN"), and the
        //            timetable is keyed on the CODE ("SK"). Before 1.0.160.110, ctx["station"] was
        //            EMPTY (that was the SL 7 defect), so the function fell through to the alert
        //            card's stationCode and train context worked. Filling the field made the first
        //            branch return a name, the lookup found nothing, and the best-effort path
        //            discarded it in silence -- so the feature disappeared with no log line and no
        //            failing test. A fix to one field broke an unrelated feature through a lookup
        //            that treated two different things as interchangeable.
        //            Now: CODE sources are tried first, and ctx["station"] is used only when it
        //            actually looks like a code (short, no spaces).
        // 1.0.160.135- Live-data readings now carry AVERAGE and MinFail alongside MinSafe/MaxSafe.
        //            get_attribute_range already returns both for every attribute and neither reached
        //            the card, so a value could be shown "in range" with no indication of where NORMAL
        //            sits inside that range. On S-46 the safe band is 90-150 V and the average is
        //            109.3: a reading of 95 V is inside the band and well below normal, and the bar
        //            could not say so. MinFail matters for the same reason -- MinSafe 90 and MinFail
        //            82 are different thresholds and only one of them was visible.
        // 1.0.160.134- TRAIN CONTEXT went missing and the log could not say why. The block had THREE
        //            silent exits -- feature flag off, fetch returned nothing, parse returned null --
        //            and none of them logged. Only the "no station code" branch did. So an operator
        //            asking "where did the train data go" got a log with no train line at all, which
        //            is indistinguishable from the feature never having been asked to run.
        //            This is the same fault the K2 work fixed and the same rule now written into two
        //            system prompts: ABSENT IS NOT NEGATIVE. Every exit now says which one it took.
        //            (Behaviour unchanged -- logging only. Train context remains best-effort and
        //            still never touches the verdict.)
        // 1.0.160.133- The point-operation prefetch fetched the right tags over the WRONG WINDOW: two
        //            days, when a throw lasts three to twelve seconds. At limit 500 across 48 h the
        //            samples that make up the stroke are sparse or truncated, so the actual CURRENT
        //            SIGNATURE never reached the model -- and since these runs are prefetch-only
        //            (inLoop=0), the model had no way to ask for it either. Telling it to fetch the
        //            samples, as .132 did, could not work on its own: same shape as SL 1, where the
        //            check existed and the data never arrived.
        //            Now the same tags are ALSO fetched over a tight stroke window (-90s .. +150s
        //            around incidence) as a second, separate block. Small payload -- a throw is tens
        //            of samples -- and it is the only block that contains the shape of the throw.
        // 1.0.160.132- POINT MACHINE signature, same gap as PART A but inverted. predict_pm_operation
        //            returns a PRE-SUMMARISED object (avg/max current, op time, counts, last 10 ops);
        //            the stroke WAVEFORM is not in it. A PM verdict that says "obstruction" on the
        //            classifier alone is quoting a label, not evidence. The samples exist -- fetch
        //            the current/voltage tags across the stroke and cite them.
        //            Also: both predict_pm_* tools ACCEPT StartDate/EndDate and DISCARD them (window
        //            fixed server-side, v1.5.54.46). Stating a date range for those results reports
        //            a window that was never applied.
        // 1.0.160.131- PART A: predict_track_health is now the AUTHORITY for glued-joint / ballast /
        //            sleeper attribution, not merely corroboration. The tool was already prefetched
        //            for every track alert and the prompt already mapped Dominant_Cause, but it said
        //            only "if the alert falls inside a major_event window, say so" -- leaving the
        //            model free to attribute a glued joint from raw currents. It did: verdicts on
        //            541980 read "glued-joint leakage" while the ML reported Resolved / Low / Normal.
        //            If/Ir movement cannot distinguish a joint from ballast from a bond; that is an
        //            integrated multi-day judgement and the model that computes it is on the call.
        //            Also adds the RECENCY guard -- if the most severe event closed more than 24 h
        //            before the incidence, the verdict must say so rather than quote its AUC as if
        //            current.
        // 1.0.160.130- "Still active" on an alert that HAD cleared, still wrong after the client-side
        //            fallback shipped. The banner reads ctx.resetTime, which the client fills from a
        //            reset-card object or the board row -- both optional, both able to be absent when
        //            the popup opens. Meanwhile the CONTROLLER holds the answer outright: since .129
        //            fixed the date format, get_frs_alerts returns THIS alert with
        //            ResetTimeStamp = "2026-08-13T13:35:30.513". Filling resetTime server-side from
        //            the row we already fetched ends the guessing: one authoritative source instead
        //            of two optional ones. Emitted through the existing context_patch.
        // 1.0.160.129- get_frs_alerts was sending its window in the WRONG FORMAT, so the server
        //            silently discarded the filter. Per the MCP catalog this is the ONE tool whose
        //            dates are `yyyy-MM-ddTHH:mm:ss`, not the `ddMMyyyy_HHmmss` every other tool
        //            takes; we sent _ddFmt. The E7 API could not parse it, dropped FromDate/ToDate,
        //            and returned its default page -- which is exactly what the traces showed:
        //            asked for a 3 h window around the incidence, received alerts spanning two days.
        //            Every "simultaneous alerts at this site" correlation has therefore been drawn
        //            from an UNFILTERED page, not the incidence window.
        //            Also: PageSize 40 -> 25. The hidden 200 cap was removed in EdgeX v1.5.57.18, so
        //            a large page is no longer clamped server-side, and this response is the largest
        //            non-history payload in the prefetch.
        // 1.0.160.128- BUILD FIX: 22 x CS0103. A slice-based edit in .126 replaced everything between
        //            BuildAssetCondition() and CaptureLoginUser(), and THIRTEEN members added across
        //            the programme had been inserted at exactly that anchor -- so they were deleted
        //            wholesale. Brace balance stayed 0/0/0 because complete, well-formed members were
        //            removed; only the compiler saw it. All thirteen restored, now kept in one
        //            labelled block so a future anchored edit cannot take them silently.
        // 1.0.160.127- The snapshot caption said "Current values read <time>" and then showed NO
        //            VALUES. SL 17 added the stamp to label a current-values panel that does not
        //            exist in the popup, so the line was an orphaned caption -- it told the reader
        //            when something was read without ever showing it. Now the snapshot carries the
        //            readings themselves (attribute, value, safe range, per-reading device time) so
        //            the caption labels real content. Out-of-range readings are marked server-side
        //            against MinSafe/MaxSafe rather than left for the view to re-derive.
        // 1.0.160.126- ASSET CONDITION band rewritten after owner review of a live render. Three faults,
        //            all mine: (1) it used `reason`, engineer-facing text full of "External Sorting
        //            (Glued Joint)" and "Total AUC 100.09 mA-days" -- a maintainer does not know what
        //            AUC is, and the ML ALREADY supplies `simple_summary` in plain language, which I
        //            ignored. (2) A RESOLVED historical leak raised a band styled like a live problem:
        //            `leakage_detected` stays true after the leak clears, so my notable-gate fired on
        //            history. (3) "NORMAL" rendered amber. Now: state is derived from
        //            overall_condition AND leakage_status, so ok=green / watch=amber / issue=red, and
        //            a resolved condition says so instead of alarming. AUC is translated to
        //            "leak load", with the raw figure kept only in a technical detail line.
        // 1.0.160.125- MERGE of independent work delivered against a 1.0.160.107 base (their
        //            "ChangesDone_v1.0.160.107"): two TRACK verdict-accuracy prompt blocks, ported
        //            VERBATIM into both prompt builders.
        //            (A) TRACK-FAILURE TRAIN-MOVEMENT CHECK -- on FAILURE causes, rule out train
        //            movement before CONFIRMING: adjacent family-track DIGITAL TPR DROP->PICK in
        //            correspondence with the main recovering = movement -> NOT_CONFIRMED; mapped
        //            signalTrack OFFECR=1 within +/-5 s of the drop = shunt route -> NOT_CONFIRMED.
        //            (B) TC GJ SHORT (predictive) -- CONFIRM only when T1 is TPR DOWN and an adjacent
        //            T2 is TPR UP with Ir mA below threshold but > 0; anchor the SHORT to T2.
        //            ONLY these four Append lines were taken. Their archive was otherwise an OLDER
        //            snapshot: it carried the pre-Cut-H code (meta["model"], raw meta["provider"],
        //            UNGATED AnalyzeChat _provider, ex.Message in Status, the srv2 config-key string,
        //            full tool schemas) and a reinstated fnAaRenderChips. Merging the archive would
        //            have reverted five disclosure fixes and the engine-authorization gate, so the
        //            prompt text was lifted and nothing else.
        // 1.0.160.124- BUILD FIX + a real defect it exposed. CS0120: the .122 SL3 block called the
        //            INSTANCE AiLog from inside the STATIC BuildPushBlock. The compile error was the
        //            symptom; the defect was that the block was in the WRONG METHOD at all --
        //            BuildPushBlock builds the FCM push NOTIFICATION, not the verdict. The anchor
        //            string `PushStr(v["recommended_action"])` exists in both, and I matched the
        //            wrong one. Swapping AiLog for AiLogStatic would have COMPILED and left the
        //            verdict drifting while only the push text was pinned. Moved to the verdict
        //            sanitiser, beside the Cut C and Cut E work, where it is an instance context and
        //            AiLog is legal.
        // 1.0.160.123- SL 5: assetType stayed empty because alertCard carries no such field -- the
        //            enrichment fallback never reached the value, which the get_frs_alerts row DOES
        //            carry as "AssetType". The 1.0.160.110 assert is what surfaced this: two
        //            [CTX] WARNs in the 14 Aug log. Now filled from the matching alert row.
        //            Matters beyond tidiness: IsTrackAssetForCircuit (SL 1) and the comparator's
        //            envelope gate both read assetType and were falling back to causeCode.
        // 1.0.160.122- SL 3: recommended_action drifted between IDENTICAL runs. Two consecutive runs
        //            of 544759 -- same inputs, same tools, same verdict, same confidence -- produced
        //            "Monitor trend; IF RECURRENCE, megger section" and then "SCHEDULE megger test and
        //            glued-joint inspection per ML leakage flag". One tells the technician to watch
        //            it; the other tells him to book work. That is non-determinism in the only field
        //            field staff act on.
        //            Temperature is NOT the lever: it is already 0 where the provider accepts it, and
        //            this file already records that "temperature 0 is NOT determinism".
        //            Root cause of the DIRECTION of drift: the ML standing condition was being folded
        //            into the alert's action -- run 1 explicitly excluded it, run 2 folded it in. Cut F
        //            gave that finding its OWN band with its OWN action, so the alert's action must
        //            now address the ALERT ONLY.
        //            Fix: for the categories where the action is a CONSEQUENCE of the category rather
        //            than a judgement -- "Transient - no action", "Derived artifact", "Coverage gap" --
        //            the controller SETS the action deterministically. Everything else keeps the
        //            model's text. Plus: any action that cites the standing condition is stripped of
        //            that clause, because that finding is rendered separately and must not turn a
        //            transient into scheduled work by the back door.
        // 1.0.160.121- SL 2: windowMedian was computed over EVERY sample, train-shunted ones included.
        //            On 544759 IF MA reported windowMedian 1098.371 against an at-alert value of
        //            385.325. The series is BIMODAL: 62 clear-track samples (median 354.3) and 66
        //            shunted (median 1135.5). The alert fired at TPR:PICKUP -- track CLEAR -- so the
        //            honest comparison is +8.7%, while the figure shown implied -64.9%. The number
        //            was not describing the same operating state as the value beside it.
        //            Fix: reuse the EXISTING train-free machinery (BuildTrainExclusionRaw +
        //            ComputeTrainFreeRef, already used by the trend frame) instead of SeriesMean over
        //            everything. When the mask is unavailable or leaves too few samples, the field is
        //            OMITTED rather than filled with a figure spanning two states -- and it is
        //            labelled windowMedianTrainFree so a reader can tell which it is.
        // 1.0.160.120- SL 1: K2 circuit coherence was SKIPPED ON 90% OF RUNS (122 of 135 on 14 Aug;
        //            only 10 had usable inputs). K2 was never disabled -- it was STARVED. It reads
        //            If/Ir from _trendRawByAttr, which holds only what the DERIVED-COMPONENT fetch
        //            happened to pull for that alert's formula, so `IR MA` appeared in a harvest
        //            exactly ONCE across 135 runs. Fix: on a TRACK asset, ensure BOTH the feed and
        //            relay current series are fetched regardless of the formula's driver set. They
        //            go through HistFetchAsync, so a series the formula already pulled is deduped
        //            and costs nothing. Also: 76 of those runs harvested NOTHING and included
        //            SIGNAL and POINT MACHINE alerts, where K2 does not apply at all -- those now
        //            return quietly instead of logging a WARN that reads like a defect. And when K2
        //            genuinely cannot run on a track asset, that is stated in the verdict rather
        //            than only in a server log nobody reads per-run.
        // 1.0.160.119- SEAL of the .118 hotfix. Sweep result: every SERVER endpoint was already
        //            sound -- DownloadAiLogs requires AiDebugAllowed()||IsWisdomReviewer(),
        //            RunLogFeed and ToolTrace return 403. The ONLY gap was the SSE push, because the
        //            per-run diagnostic is assembled IN THE BROWSER from window._aiLastMeta. Hiding
        //            the button with style.display='none' hides the control, not the payload: the
        //            data still arrived at every session and was readable from the console.
        //            Sealed three ways: (1) the audit event is NOT EMITTED AT ALL to a
        //            non-privileged session -- redaction is the second line, not the first;
        //            (2) if it is emitted, it is redacted AND the serialised form is scrubbed, so a
        //            vendor string added to the audit later cannot ride out in a field I did not
        //            anticipate; (3) the client clears its copy when the server declines to send one,
        //            so a privileged run followed by a non-privileged one cannot leave stale data in
        //            the page.
        // 1.0.160.118- HOTFIX: the 7W AUDIT was pushed to the browser UNGATED. Cut H closed the `meta`
        //            path but this is a SECOND transport -- WriteSse("audit", ...) feeds the per-run
        //            Log download, and Build7WAudit carries which.provider ("deepseek"),
        //            which.model ("deepseek-v4-flash") and where.providerUrl (the vendor endpoint).
        //            So the page hid the model while the Log button handed it over in plain text to
        //            any user. Found by an independent browser-side review, not by the code checks.
        //            Now: a REDACTED copy goes to the client unless the session is privileged --
        //            engine stays the opaque id, provider/model/providerUrl are removed, and the
        //            server-side audit row + JSONL are UNCHANGED (ops still needs the real values).
        // 1.0.160.117- CUT C (SL 14): wisdom ATTRIBUTION, privileged-only.
        //            24,125 chars of wisdom were injected per run and the audit recorded only BULLET
        //            COUNTS -- no rule id, no text, no outcome. So a never-relevant rule cost tokens
        //            on every run invisibly, a wrong rule could only be found by re-reading the
        //            prompt, and SL 9/15 (learned=0) could not move because nothing surfaces which
        //            rule mattered. Now: each injected rule is tagged [DOC-nnn]/[FLD-nnn]/[CIR-nnn]/
        //            [CHK-nnn]/[LRN-nnn]; the ids + sizes are recorded; the model cites the ones that
        //            changed its conclusion in a STRUCTURED field; the controller EXTRACTS that,
        //            records it, then STRIPS it before the verdict reaches the browser; and
        //            ScrubWisdomIds() removes any id the model wrote into PROSE -- the >=12-word
        //            instruction scrub cannot catch a three-token id.
        //            Ids are position-based within each block: APPEND new rules, never reorder, or
        //            historical citations stop lining up.
        // 1.0.160.116- CUT G3 (SL 18 visibility): the alert LIST can now show whether an alert has a
        //            usable stored verdict. New POST VerdictStates takes the page's alert ids plus the
        //            board's OPAQUE engine id, resolves it through ResolveEngineAlias +
        //            GateProviderForRole (so eligibility compares the EFFECTIVE internal engine, never
        //            a raw client value) and returns valid | outdated | not_analyzed per id.
        //            DISPLAY ONLY -- it never triggers analysis. Bounded per request. The popup stays
        //            authoritative: the list cannot evaluate the alert-card fingerprint (rule 4), so a
        //            row shown "valid" may still re-analyse when opened -- the safe direction.
        // 1.0.160.115- CUT B (SL 13/16): sensor-health telemetry, OBSERVE-ONLY.
        //            The freshness capability already existed (CircuitWisdom tells the model to
        //            "check per-device freshness before treating an incoherence as a fault", and the
        //            trend builder computes last-sample age) but it is ADVISORY and ONE-DIRECTIONAL:
        //            it guards against calling stale data a FAULT, never against calling it HEALTHY.
        //            This cut RECORDS the evidence and CHANGES NO VERDICT. Two measured facts shape
        //            it: (1) rate must come from OBSERVED coverage, because a 500-capped block of
        //            Charger mA over a 96 h window computes to 5.2/hour -- below any sane gate --
        //            versus 38.1/hour from first-to-last coverage; (2) occupancy is PER-CHANNEL:
        //            during an 18 h shunt Charger mA emitted 792 samples while If mA emitted 3.
        //            Enforcement (confidence cap / INCONCLUSIVE) is deliberately NOT here: one asset
        //            is not a fleet threshold, and a gate that fires on a parked train gets switched
        //            off in a week.
        // 1.0.160.114- CUT E (SL 8): viz.series came from the MODEL, so on 543194 it was three
        //            invented points [1779.56, 1803.96, 1779.56] -- reset value, breach value, reset
        //            value -- a restatement of the evidence lines drawn with the visual authority of
        //            measurement. The controller now OVERRIDES it with the real series it already
        //            builds (SynthDerivedSeries for a derived alert, else the trigger attribute's
        //            captured raw series), as timestamped [ts,v] pairs, downsampled MIN/MAX per
        //            bucket so a transient breach survives the reduction (decimation would delete
        //            the very spike the alert is about). New viz.seriesSource = measured | derived |
        //            unavailable; when nothing real exists the series is EMPTY and the view says so
        //            -- a shape is never synthesised.
        // 1.0.160.113- TRANSPORT FIX for Cut D/F: context_patch, asset_condition and snapshot were
        //            added to the JSON envelope (env) only. The POPUP is fed by the SSE endpoint, so
        //            the view had nothing to render. All three are now emitted as SSE events too,
        //            from the same controller-built values. Pairs with view 1.0.55.67.
        // 1.0.160.112- CUT F (SL 12/17): presentation truth.
        //            (SL12) a CRITICAL standing asset condition was surviving only as the last line
        //            of caveats -- on 543194 predict_track_health reported "Critical Risk / active
        //            glued-joint leakage, 43.6 h" while the headline read "Transient - no action".
        //            The verdict is CORRECT (different question: was THIS breach sustained), so the
        //            verdict and its confidence are UNCHANGED; the finding is promoted to its own
        //            asset_condition object, BUILT IN THE CONTROLLER from the validated ML result --
        //            never promoted by the model -- and passed through the same string filters.
        //            (SL17) the current-value snapshot is stamped with retrievedAt +
        //            ageFromIncidenceSec, because the analysis can run many hours after the alert
        //            (543194: 22.8 h) and "current" values may describe a different track state.
        //            NO occupancy claim is made: the TPR fetch ends at incidence+2h and cannot
        //            support one.
        // 1.0.160.111- CUT G (SL 18): a verdict is mapped to the AlertId for the LIFE of the alert.
        //            Cache rule 6 (age backstop) no longer applies to RESET alerts -- they are
        //            finished history and rules 1-5 already invalidate on every real change. Rules
        //            1-5 REMAIN for reset alerts: rule 5 in particular, or an alert analysed while
        //            ACTIVE and reset later would serve its pre-reset verdict forever. New rule 3b
        //            invalidates when the stored ControllerVersion is below a configured floor
        //            (compiled default 1.0.160.106, the first build with the corrected derived
        //            panel), compared with Version.TryParse -- never string comparison.
        //            Flag AiCacheResetNeverExpires (default true), named rather than encoded as a
        //            large AiCacheMaxAgeResetMin.
        // 1.0.160.110- CUT D (SL 5/6/6b/7): audit measurement truth + follow-up grounding.
        //            (SL5) prefetch tool time was NEVER accumulated -- _trToolMs is incremented only
        //            on the in-loop path, so a prefetch-only run (the normal case) reported
        //            tools=0ms while the calls took 4,476ms. Prefetch IS concurrent (tagsT/rangeT/
        //            stagsT/predT/alertsT/rangeHistT all start before any await), so summed per-call
        //            ms can exceed wall-clock and MUST NOT be used for reconciliation: the audit now
        //            records toolCallMsSum / prefetchWallMs / inLoopWallMs and per-stage counts, and
        //            only WALL-CLOCK phases feed total. Interlocked accumulation.
        //            (SL6) _trTurns counts BILLABLE model calls (incl. the Hindi translation) -- it
        //            is emitted as modelCalls, with turns kept as an alias for one release.
        //            (SL6b) the prompt-cache warning was gated on _trTurns > 1, which every bilingual
        //            single-turn run satisfies -- it now uses the new _trAnalysisTurns.
        //            (SL7) station/assetType are enriched and returned as context_patch so the
        //            follow-up chat stops inheriting blanks.
        // 1.0.160.109- CUT A (SL 1/2/3/4): history window correctness.
        //            ONE typed builder HistWindowArgs(key,val,DateTime,DateTime,limit,outputMode)
        //            replaces the string-based HistArgs; ALL EIGHT date-bearing history_get sites now
        //            route through it, so sort=desc (and the EdgeX UTC compensation) can no longer be
        //            missed by a hand-built site. Adds: a per-run fetch cache keyed by the FULL shape
        //            (target, window, limit, sort, outputMode) -- consolidation alone did NOT remove
        //            the duplicate driver fetch; a censored flag when a block returns exactly `limit`
        //            rows; an at-alert sample age guard (AtSampleMaxAgeMin, default 30) so a stale
        //            sample can never drive the movement panel; and a window-echo check that logs a
        //            from/to mismatch. probeArgs KEEPS outputMode=json + limit 1 -- PickAlertingTagId
        //            scans the response for the literal "TagID", so columnar would break it silently.
        // 1.0.160.108- CUT H: completes the .107 identity hardening after external review found four
        //            release blockers. (H1) publicMeta ALLOWLIST -- .107 masked only provider/model
        //            and the ChangesDone wrongly claimed an allowlist; fault/toolTrace/diagnostics/
        //            promptCacheWarning/tokens/library-name were still emitted to everyone.
        //            (H2) AnalyzeChat applied ResolveEngineAlias but NEVER GateProviderForRole -- a
        //            crafted follow-up could select a restricted engine; role is now captured once
        //            pre-await and both actions gate on it. (H4) Status/Status2 never emit ex.Message
        //            or config-key names; Status2 returns tool NAMES only.
        // 1.0.160.107- IDENTITY + MECHANISM HARDENING. A browser console could recover the real
        //            provider identity from meta/provider and the Status endpoints, plus the tool
        //            topology from Status2. Now: opaque engine ids in (ResolveEngineAlias) and out
        //            (OpaqueProviderId, fail-closed); a non-privileged metadata allowlist that omits
        //            model/diagnostics/toolTrace/promptCache/fault; Status reduced to {ok} /
        //            {ok,error:"Unavailable"} and Status2 403 with NO tool discovery for
        //            non-privileged callers; raw provider/model/library names masked for EVERY
        //            browser response (they remain in server logs and audit rows); privilege
        //            captured BEFORE the first await in every affected action; and an
        //            instruction-extraction detector that refuses BEFORE the model runs, with a
        //            ScrubInstructionEcho backstop in the central sanitiser.

        // Version history (one line per shipped version, newest first):
        // 1.0.160.106- Ships the previous-in-envelope comparator + card-limit emission (di.value/
        //            limit/limitUnit/breachAbove) developed across the burned 103-105 cuts, with NO
        //            further controller change this cut; pairs with Index_15 1.0.55.60, which makes the
        //            DERIVED hero FAIL CLOSED ("Alert value/limit unavailable - re-run required") when
        //            the authoritative di.value/di.limit/di.breachAbove are absent, instead of falling
        //            back to the model z.value/z.threshold/z.unit (which can belong to a different metric).
        // 1.0.160.105- BURNED (unreleased): controller correct, but the paired view still fell back to
        //            z.* in the derived hero. Reworked into 1.0.160.106 (view-only fix).
        // 1.0.160.104- BURNED (unreleased): NO-GO -- derived hero only hid the % pill; still rendered
        //            the baseline/resting flow and the model value, never reading derived_inputs.value.
        // 1.0.160.103- BURNED (unreleased): NO-GO in review -- controller version not bumped, noReading
        //            time mix, missing equal-ts guard, weak range grounding, unguarded ToUnixSeconds.
        // 1.0.160.102- No controller change; pairs with Index_15 1.0.55.56, which merges the other
        //            team's server-side date range (body.fromDate/toDate + toServerDate) and resolves
        //            a VERSION COLLISION -- both teams had shipped a 1.0.55.55.
        // 1.0.160.101- THE DERIVED-INPUT BASELINE WAS AVERAGING TRAIN-OCCUPIED SAMPLES, and on one
        //            input it INVERTED THE SIGN shown to the maintainer.
        //            SeriesMean was a plain arithmetic mean over the whole fetched window. A track
        //            circuit is bimodal: resting, then near-zero while a train shunts it. The owner's
        //            trend export for 03AT shows it plainly -- Ir p25 = 6.0 mA against a median of
        //            284.8; Vf p25 = 0.4 V against 3.7. Those low clusters are occupancy, not drift.
        //            Averaging both modes gives a baseline that describes NEITHER, and the card then
        //            compares a resting at-alert value against it:
        //              VF   4.146 vs mean 2.283 -> '+81.6%'  (against resting ~3.7 it is about +12%)
        //              IR   214.1 vs mean 121.4 -> '+76.3% UP'  -- against resting ~285 it is DOWN ~25%
        //            The second one is the serious case: the panel told a maintainer relay-end current
        //            had RISEN 76% when it had in fact FALLEN, on a TC RAIL RES HIGH alert where the
        //            direction is the diagnosis.
        //            Fixed with the MEDIAN, which lands in the dominant mode instead of between the
        //            two. Not the arithmetic mean, and not a TPR mask -- the mask is not in scope at
        //            this point and plumbing it here would be a much larger change than the fault
        //            warrants. The method now also reports the p25/p75 spread so the card can say when
        //            a series is too bimodal for ANY single baseline to be honest.
        // 1.0.160.100- The FIRST real Analysis row read back from the DB (row 37, alert 529633) proved
        //            the writes are healthy -- AnalysisStatus COMPLETED, full 64-char fingerprint,
        //            IsLatest true -- and exposed six columns I was never populating:
        //            AssetId, AssetName, AssetType, UserName, UserRole, ServerNode were all NULL,
        //            because they were sent on the REQUEST row only. The ANALYSIS table is the one the
        //            cache and every future query read, so it could not say which asset or which
        //            operator an analysis belonged to.
        //            Flag_TrainContext / Flag_ComputedVerdict / Flag_GatedDerived were never sent
        //            either, so every row read false. Flag_TrainContext reading false on a row that
        //            HAD a train-context summary is worse than a missing value -- it is a wrong one.
        //            McpTotalLatencyMs added on the same pass.
        //            Worth recording how this was found: three consecutive AI-generated 'curl outputs'
        //            reported a duplicate row that did not exist -- camelCase where the API returns
        //            PascalCase, a Content-Length that disagreed with its own body, and MCP counts
        //            contradicting our own log. The real row, fetched outside the AI path, showed
        //            none of it. The log file and the database are ground truth; a generated answer
        //            about them is not.
        // 1.0.160.99 - No controller change; pairs with Index_15 1.0.55.55, which MERGES the other
        //            team's two 1.0.55.46 fixes: (a) fnShowAiVerdictView re-showed aiDebugToolsBtn on
        //            EVERY verdict open, undoing aaApplySiteKeepGate -- a non-site-keeping user got
        //            Debug tools back the moment they opened any verdict; (b) date range default 24h.
        //            Taken verbatim rather than reimplemented. Two defects surfaced while merging,
            //        both MINE: window.AA_SITEKEEP was undefined here (AA_SITEKEEP is a plain var), so
        //            their condition would have hidden the button from EVERYONE including site-keeping
        //            users; and my first edit misplaced a semicolon after a trailing comment.
        // 1.0.160.98 - AnalysisOutputJson WAS ALWAYS EMPTY -- the verdict cache could never have
        //            worked. Proven live: analysis 29 is COMPLETED, has a fingerprint, verdict
        //            CONFIRMED, engine quantum11 -- and outLen=0.
        //            TWO faults, both mine, and either alone was fatal:
        //            (1) WRONG PATH. `_lastResponseFull` was assigned in AnalyzeAlertJsonCore -- the
        //                JSON BATCH path. The UI streams, so that line never executes and the field
        //                stayed null for every operator-driven analysis.
        //            (2) WRONG OBJECT. Even on the batch path it captured `tv`, which is the TIMING
        //                envelope (modelMs, toolMs, tokens, bytes) -- not the verdict. So the cache
        //                would have served a block of stopwatch numbers as an analysis.
        //            Now captured from `verdictObj` on the STREAMING path, at the point it is fully
        //                built: sanitised, triage attached, weather and train context in, Hinglish
        //                done. That is exactly what the panel renders, so a cache hit shows what the
        //                first operator saw.
        //            This is why the cache flag was never worth enabling: every lookup would have
        //            found a row and had nothing to serve.
        // 1.0.160.97 - No controller change; pairs with Index_15 1.0.55.54. Export PDF: the QUESTION
        //            printed as faint grey text with no attribution -- on a maintenance record a
        //            reader could not tell a question from an answer nor who asked it. Also: the live
        //            engine status line ("ENERGY7 ULTRA ready - 27 capabilities - live_get") printed
        //            inside the answer; the story deck kept its screen padding and shadow, which is
        //            part of why page 1 read half empty; and the footer printed "component" with
        //            nothing after it, because _aiLastMeta only ever holds { audit: ... }.
        // 1.0.160.96 - No controller change; pairs with Index_15 1.0.55.53. The export printed
        //            ASSESSMENT and EVIDENCE side by side and then left most of a sheet blank: .rv
        //            becomes a 2-column grid above 900px -- a deliberate SCREEN layout that the export
        //            inherited because the new window is wide. Forced to one column, and the body
        //            pinned to 186mm so the layout follows the PAGE rather than whatever window size
        //            the operator happens to have.
        // 1.0.160.95 - No controller change; pairs with Index_15 1.0.55.52 -- three export defects from
        //            the first real PDF: a blank two-thirds of page 1 (break-inside:avoid on whole
        //            SECTIONS forces a section taller than the remaining space onto the next page),
        //            the story tab buttons printing as navigation to nothing, and the conversation
        //            printing raw markdown.
        // 1.0.160.94 - CHAT TURNS WERE NEVER BEING STORED. AuditChatTurn was written in 1.0.160.78 and
        //            has ONE definition and ZERO call sites -- AIStats confirms it: ChatMessageCount 0.
        //            Every follow-up conversation since the audit went live has been lost, not merely
        //            unavailable. That is the worse kind of gap: the feature looked wired.
        //            Now called on BOTH sides of each exchange -- the question as it arrives, the
        //            answer once the loop completes -- with AlertId, which EdgeX 1.5.57.6 added to
        //            AI_ALERT_ChatHistory specifically so a thread can be fetched per ALERT rather
        //            than per analysis.
        //            TurnIndex continues the CLIENT thread length, so turns from a second engineer on
        //            the same alert do not all land as turn 0 and sort arbitrarily.
        //            Storage only -- the shared thread is NOT yet loaded into the panel, and is NOT
        //            fed to the model as context. Both are deliberate: reading comes next, and
        //            silently prepending 50 prior turns would change verdicts for reasons the
        //            operator cannot see.
        // 1.0.160.93 - No controller change; pairs with Index_15 1.0.55.51. Export PDF produced THREE
        //            IDENTICAL PAGES: the panel sits inside .vd-modal (position:fixed; inset:0;
        //            overflow:auto), and a fixed overlay has no page flow -- the browser prints its
        //            visible viewport and repeats it per page. No @media print rule on the inner
        //            elements could fix it, because the clipper is the ANCESTOR.
        //            The export now builds a real DOCUMENT in a new window from the rendered DOM --
        //            still no second renderer, but laid out linearly so it paginates properly.
        // 1.0.160.92 - No controller change; pairs with Index_15 1.0.55.50, which adds EXPORT PDF: one
        //            continuous record -- the analysis, then each question and the answer that
        //            followed, in order.
        //            Print-based, NOT a PDF library. html2canvas would emit a screenshot: no selectable
        //            text, no search, and the dark verdict band prints as a black slab. No new package
        //            enters a railway safety app for what is fundamentally "make this readable on
        //            paper", and a real printer works too, which is what a site visit needs.
        // 1.0.160.91 - THE ROLE GATE WAS DENYING EVERYONE, INCLUDING SITE-KEEPING USERS. The owner
        //            reported bash had stopped working; it had, and so had every other gated feature.
        //            AiDebugAllowed -> SafeIsSiteKeeping -> ClsHttpContent.LoginUser, which reads
        //            HttpContext.Current. Tool filtering runs deep in the agentic loop, long after the
        //            first await, and ConfigureAwait(false) has destroyed the context by then -- so
        //            LoginUser is NULL, SafeIsSiteKeeping returns false, and the gate denies everybody
        //            regardless of role or SiteKeepingOverrideEmails.
        //            This is the SAME root cause as the audit reading who=ENERGY7 role=operator, which
        //            I fixed in 160.85 -- but I wired the captured identity into the AUDIT paths only
        //            and left the PREDICATE reading the null object. Half a fix, and the audit line
        //            'role=operator' on a privileged login was the evidence sitting in plain sight.
        //            Blast radius was five features, not one: bash (both paths), ToolTrace 403,
        //            DownloadAiLogs, the Log/AI-logs/Debug/Run-log buttons via meta.debugAllowed, and
        //            the wisdom approve/reject endpoints.
        //            Fix: AiDebugAllowed is now an INSTANCE method preferring the identity captured
        //            before the first await, falling back to the live read for any path that has not
        //            captured yet. All 6 call sites are instance methods, so no CS0120.
        // 1.0.160.90 - THE UNDERLYING MODEL NAME MUST NEVER REACH A USER. The owner saw a reply
        //            beginning "chatgpt 5.6 didn't parse this type" -- a provider error quoting its
        //            own model string straight to a railway operator.
        //            Two routes, and NEITHER app guarded them: provider errors quote the model
        //            ('gpt-5.6 did not parse', 'deepseek-v4-pro: context length'), and the model can
        //            name ITSELF in its reply ('As ChatGPT, I...').
        //            The whole point of the ENERGY7 NEXUS/ORION/QUANTUM labels is that the transport
        //            is ours to know and not the operator's. A leaked vendor name also invites the
        //            reader to weigh a verdict by brand reputation instead of by the evidence in it.
        //            MaskModelIdentity folds every known vendor/model token to AiEngineBrand, and is
        //            called INSIDE ScrubPlatformInternals so all 12 existing call sites are covered
        //            without adding a 13th thing to remember. Same class of leak -- internal detail
        //            leaving the building -- differing only in whether it is our platform or our
        //            supplier.
        // 1.0.160.89 - No controller logic change; pairs with Index_15 1.0.55.49, which shows a badge
        //            on EVERY verdict: FRESH ANALYSIS or REUSED. 1.0.55.48 labelled only the reused
        //            case, so 'fresh' was implied by the ABSENCE of a banner -- and an absent banner
        //            is indistinguishable from an older build, a failed render, or the cache being
        //            off. Both states are now stated in the same place, so the operator never infers.
        // 1.0.160.88 - THE 500 IS NAMED, one run after 160.86 started logging the error body:
        //              invalid byte sequence for encoding "UTF8": 0x97
        //            0x97 is the CP-1252 EM-DASH -- the exact PostgreSQL trap already written into
        //            this project's own coding rules for C# sources. It reaches us in FRS alert text
        //            (SQL Server CP-1252 columns) and rides into AlertInputJson unchanged.
        //            PgSafe handled NUL and lone surrogates but not the C1 range U+0080-U+009F, which
        //            is where 0x97 lives. Those code points carry no legitimate text meaning, so they
        //            are dropped; en/em dashes, curly quotes, ellipsis and nbsp are folded to ASCII on
        //            the same principle the source files already follow -- keep what reaches
        //            PostgreSQL plain rather than trusting every layer to re-encode correctly.
        //            This also retires the CauseCode VARCHAR(16) theory for good: the width was never
        //            the fault, and the log said so as soon as it was allowed to.
        // 1.0.160.87 - NOT_CONFIRMED was carrying two OPPOSITE meanings, and the wording implied the
        //            ALERT LOGIC was flawed when it had behaved correctly.
        //            A breach that genuinely occurred and then recovered is NOT a false alert: FRS was
        //            right to fire, the condition simply did not hold for the sustain window. Saying
        //            'not confirmed -- transient spike' reads as 'the code was wrong', which erodes
        //            trust in an engine that did its job.
        //            The owner's two screenshots showed there are THREE cases, not two -- one alert
        //            (04BT TC BT CHG CURR HIGH) was called a COMPUTATION ARTIFACT by this controller
        //            (IF MA is the DIVISOR and fell 19.1%, which alone lifts the derived value ~24%)
        //            and a REAL TRANSIENT by another panel. Those need different messages: one
        //            vindicates the alert engine, the other points at the derived formula inputs.
        //            No new VERDICT value -- the enum is validated, stored in FinalVerdict, keyed on
        //            by the cache and rendered by the view; a fourth value would ripple through all of
        //            it. The distinction is a REASON for the same outcome, so it rides the CATEGORY,
        //            which already exists and is safely coerced.
        //            Added: 'Transient - no action' (breach real, recovered) and 'Derived artifact'
        //            (breach not real -- a divisor moved, a component was held). CoerceCategory maps
        //            the obvious phrasings, and StashComputedVerdict now labels the COMPUTED path
        //            itself rather than leaving it to the model.
        //            CONFIRMED still means ACT -- deliberately NOT widened to mean 'the alert was
        //            valid', which would send maintainers to site for self-clearing events.
        // 1.0.160.86 - LOG THE SERVER'S ERROR BODY. AuditPost read the response into `txt` and then
        //            logged only "-> HTTP 500", discarding it. The DataAPI answers every failure with
        //            a detail message (57 APIErrorMessage.Json sites) that names the column or
        //            constraint at fault -- so three rounds were spent inferring a cause the server
        //            was willing to state outright.
        //            My CauseCode VARCHAR(16) theory is now DISPROVED by the owner's own log: alert
        //            524613 has cause 'TC TR VOLT LOW' (14 chars, fits) and Start still 500s, while
        //            the one row that DID land had 'TC VAR RES HIGH' (15). The width is still worth
        //            widening -- 21- and 23-char codes exist -- but it is not what is failing now.
        //            The next 500 will name itself. Same lesson as the six silent returns in
        //            BuildCircuitResidualLine: a failure that will not say why costs deploy cycles.
        // 1.0.160.85 - WHY THE AUDIT USER WAS ALWAYS "ENERGY7". Not a lookup problem -- a TIMING one.
        //            ClsHttpContent.LoginUser reads HttpContext.Current. This controller uses
        //            .ConfigureAwait(false) in 70 places, which drops the ASP.NET synchronization
        //            context, so after the FIRST await HttpContext.Current is NULL and LoginUser
        //            returns nothing. The audit runs at request_end -- long past that point.
        //            The evidence fits exactly: UserIp came through (49.36.70.6) because I read it
        //            from the controller's own Request property, which survives; the NAME came from
        //            HttpContext.Current, which does not. UserRole read "operator" for the same
        //            reason -- SafeIsSiteKeeping consults the same null object, so a site-keeping
        //            user was being recorded as an ordinary operator.
        //            The Wisdom PANEL shows the real name because Razor renders synchronously, before
        //            any await. Same source, different moment.
        //            Fix: capture identity ONCE at the top of the action, before any await, into
        //            instance fields, and have every audit path read those. Follows what the view
        //            already does rather than inventing a new mechanism.
        // 1.0.160.84 - THE SAME ONE-LETTER BUG, SECOND LOCATION. 160.83 fixed the WRITE side
        //            (COMPLETE -> COMPLETED); the verdict cache still TESTED for "COMPLETE" when
        //            deciding whether a stored row may be reused. It would therefore have rejected
        //            every correctly-written row and reported 'stored analysis is COMPLETED' as a
        //            miss -- a cache that never hits, on data that is perfectly good. Found only
        //            because the owner's AIStats probe showed AnalysisByStatus {RUNNING: 1} and I
        //            went back to check what the cache compares against.
        //            Now accepts COMPLETED | CACHED | REANALYSIS -- the server's three terminal
        //            success states.
        //            Lesson: fixing a magic string on one side of an interface is half a fix. Both
        //            sides had to agree, and only one was changed.
        // 1.0.160.83 - AIAnalysis/Complete was returning 400 on the ONE alert whose Start succeeded:
        //            I sent AnalysisStatus="COMPLETE" but the server accepts COMPLETED | CACHED |
        //            REANALYSIS (FAILED goes to /Fail, which was already right). One letter, and it
        //            meant rows were OPENED and never CLOSED -- so a verdict cache would have found
        //            rows that never satisfy its 'must be COMPLETE' test and every lookup would miss.
        //            The 2026-08-12 12:58 log is otherwise the first genuinely good news: AIRequest
        //            and AIMcpCall both landed -- 'request 1 + 12 mcp calls written' -- which proves
        //            the Start -> RequestId -> batched McpCall chain works end to end.
        //            STILL FAILING and NOT fixable here: Start returns 500 for any alert whose cause
        //            code exceeds VARCHAR(16). The ALTER from 160.82 has not been applied yet -- the
        //            one alert that succeeded had a short code, the rest still 500.
        // 1.0.160.82 - Audit Start was returning HTTP 500 on EVERY alert. Two width overflows, one
        //            theirs and one MINE, found by comparing every VARCHAR column against what we send.
        //            THEIRS (schema, needs an ALTER): CauseCode is VARCHAR(16), but real cause codes
        //            are longer -- TC BALST/SLPR RES LOW is 21, PT NWKR RELAY DEFECT OP is 23, IPS 110
        //            DC VOLT FAIL and SIG RG VOLT/CURR LOW are 20. PostgreSQL raises 22001 and the API
        //            returns 500, so the row never lands. NOT worked around here: truncating a cause
        //            code to 16 would store 'TC BALST/SLPR RE', corrupting the one column every future
        //            query groups by. The column has to widen.
        //            MINE: TrainContextSummary is VARCHAR(512) and I capped it at 4000 without
        //            checking the schema -- that would have failed the whole Complete insert the
        //            moment a train context existed. Now capped at 500.
        //            The fail-open design held throughout: the log shows the breaker opening after 3
        //            failures and every verdict completing normally, which is exactly the intent.
        // 1.0.160.81 - The get_attribute_range_history truncation warning is MOSTLY harmless, but it
        //            was silently costing the STATION CODE, and with it the whole train context.
        //            Measured on alert 525404 (2026-08-12): the payload is ~35KB, the server cut
        //            15825 chars, and the audit came back `where: site=167` with the station BLANK and
        //            no train_context at all -- even though `"stationCode":"GLTA"` sits in the FIRST
        //            120 characters of that payload and survived the cut intact.
        //            Cause: ResolveStationCode used JObject.Parse, which throws on a payload cut
        //            mid-array. TryExtractLatLon, three lines below, already used ParseJsonDocuments
        //            and kept working -- which is why weather survived while trains did not. Same
        //            payload, same run, two different parsers, one of them fragile.
        //            Now: ParseJsonDocuments first, then a regex scan of the head as a last resort, so
        //            the station code is recovered from a truncated payload rather than lost.
        //            NOT changed: the truncation itself. The controller only ever puts
        //            TrimForPrompt(rangeHist, 3500) into the prompt -- 3.5KB of a 35KB payload -- so
        //            the dropped tail costs the analysis nothing. Raising MaxToolResultChars would
        //            pull ~35KB per alert into memory to throw 90% of it away. The warning is worth
        //            keeping visible, but it is not a fault to chase.
        // 1.0.160.80 - The ALERTING TAG was fetched TWICE. In the 2026-08-12 trace for 525404, seq 8
        //            pulls TagId ...970 (Ir) with sort=desc and seq 10 pulls the SAME TagId over the
        //            SAME window without sort -- 186 identical samples, ~250ms and 3.9KB wasted on
        //            every track analysis.
        //            Cause is mine: the 160.69 driver loop fetches the whole driver set, which
        //            INCLUDES the alerting attribute, while the older alerting-tag fetch further down
        //            was never taught to check.
        //            Fixed on the DRIVER-LOOP side, not the alerting side: the loop now skips a driver
        //            that IS the alerting attribute, because that path fetches it anyway and does so
        //            with the coverage/paging/truncation checks the driver loop does not run. Guarding
        //            the other site would have meant wrapping a long block in an else -- a far larger
        //            edit for the same result, and it briefly broke the brace balance when attempted.
        //            Non-alerting drivers (If here) are unaffected, so K2 still gets both series.
        // 1.0.160.79 - VERDICT CACHE: reuse a stored analysis instead of calling the model again.
        //            The waste was measured, not assumed -- on 2026-08-11, 33 runs covered about 12
        //            distinct alerts (525404 alone was analysed NINE times), so most of the day's
        //            575k tokens re-derived answers already held.
        //            In its OWN partial (AiChatController.VerdictCache.cs) per the owner's splitting
        //            strategy, and because this subsystem is ACTIVE -- unlike the passive audit it can
        //            SKIP the model call, so it must be reasonable about, and revertible, alone.
        //            The lookup is trivial; the STALENESS rules are the substance. A cached verdict
        //            can be wrong in a way a slow verdict never is. Any of these forces a fresh run:
        //            Re-run pressed; a DIFFERENT engine (quantum11 and pro disagreed on 525605 and
        //            525751 -- serving one for the other would hide a real model disagreement behind
        //            a cache hit); the alert card changed (AlertFingerprint, from 160.77); a reset
        //            arriving AFTER the stored analysis; and an age backstop that is SHORTER for an
        //            active alert (15m) than a reset one (60m), because an active alert is exactly
        //            the one whose data keeps moving.
        //            Only 'card changed' and 'reset arrived' mark the row STALE. A Re-run or an
        //            engine switch says nothing about that row's validity for its own engine --
        //            marking those stale would discard a good analysis.
        //            CacheGet is a SEPARATE helper because /AIAnalysis/Get returns 404 for an
        //            ordinary MISS, and AuditPost treats 404 as 'route absent' and opens the breaker:
        //            reusing it would have let the first cache miss disable the whole audit subsystem
        //            for ten minutes.
        //            Flag AiVerdictCacheEnabled default FALSE, and requires AiAuditDbEnabled.
        // 1.0.160.78 - AIRequest + AIMcpCall + AIChat wired. Route coverage 3/20 -> 10/20.
        //            ORDER MATTERS: McpCall REQUIRES a RequestId, which only exists after
        //            AIRequest/Start returns one -- so Request and McpCall are one unit of work, not
        //            two independent ones.
        //            McpCall/Add accepts a JSON ARRAY (AiAlertAPI L1450 loops the body), so all tool
        //            calls of a run go in ONE post -- roughly 1 extra HTTP call per analysis instead
        //            of 8-10, which matters at ~300 tool calls a day.
        //            COLLECTION PROBLEM AND ITS GUARD: LogToolEvent is STATIC, so an instance list
        //            cannot be filled from it. Calls are collected into a static map keyed by
        //            traceId. That map is a LEAK RISK -- a run that fails never drains it -- so it is
        //            bounded three ways: at most McpCallMaxPerRun (40) entries per run, entries older
        //            than 30 minutes are pruned on every add, and the run's entry is REMOVED when
        //            drained. Without those a long-lived app pool would accumulate every tool call it
        //            ever made.
        // 1.0.160.77 - Fill the AI_ALERT_Analysis columns we ALREADY compute. Complete was sending 15
        //            of the 50 whitelisted columns while the controller held most of the rest -- the
        //            row was archival rather than queryable. Without ComputedVerdict/ComputedWon you
        //            cannot ask 'how often did the computed sustain override the model?', which is the
        //            question that started this whole arc.
        //            Sourced from existing state, no new computation: ComputedVerdict/ComputedWon +
        //            BreachDurationSec/FrsSustainSec (StashComputedVerdict already stores class,
        //            durSec and needSec), TrainContextCount/Summary (_trainContext), PrefetchDurationMs,
        //            McpToolsDistinct, FinalRecommendation, and AlertFingerprint.
        //            TWO new fields were needed because the values live in LOCALS the audit cannot
        //            reach: _gateDroppedLast (the [GATE] filter count, local at ~9847) and
        //            _computedWonLast (set at the override site ~9032, the only place that knows the
        //            computed class actually replaced the model's). Reaching into a local would not
        //            have compiled; inferring 'won' by comparing strings afterwards would have been
        //            wrong whenever the model already agreed.
        //            AlertFingerprint is SHA-256 over cause+asset+incidence+triggered values -- and it
        //            is a PREREQUISITE for the verdict cache, which cannot otherwise detect that the
        //            alert card changed.
        // 1.0.160.76 - PgSafe for every TEXT column written to PostgreSQL. Prompted by an owner
        //            question about JSON-inside-JSON: that part is FINE (Newtonsoft escapes the inner
        //            document, the server unescapes it -- base64 would only make the column
        //            unreadable in psql). But checking it found a real hazard: Trunc/TraceTrunc use a
        //            raw Substring, so a cut can land BETWEEN the halves of a surrogate pair. The lone
        //            surrogate is invalid UTF-8, PostgreSQL rejects it (22021), and the ENTIRE insert
        //            fails -- not just that column. Verdict text carries Hinglish and symbols and is
        //            truncated at 400/2000/4000 chars, so this is a live path, and the failure would
        //            have looked like a random missing audit row.
        //            PgSafe drops NUL (a PG TEXT column cannot hold it at all), drops unpaired
        //            surrogate halves, and never cuts between a pair.
        // 1.0.160.75 - MY 160.73 Complete WAS WRITING NOTHING. It sent "AuditJson", which is not a
        //            column in DBSchema_AI.sql and not in the API whitelist -- and CollectColumns
        //            silently DROPS unknown keys, so every completed analysis stored an empty result.
        //            Found while designing the verdict cache, not by a failure: the write returned 200.
        //            Worse for what comes next: AnalysisOutputJson was never populated, so a cache
        //            lookup would find a row with NO verdict to serve -- the feature would have been
        //            unbuildable on top of it.
        //            Complete now maps onto the real whitelist: AnalysisStatus, AnalysisCompletedUtc,
        //            AnalysisOutputJson (the rendered envelope), FinalVerdict/FinalConfidence/
        //            FinalReasoning, TotalTokensIn/Out, McpToolCallTotal, AiTurnCount,
        //            TotalEstCostUsd, TotalDurationMs, ModelDurationMs, ThinkingEnabled,
        //            EvidenceSummary. Fail sends AnalysisStatus=FAILED.
        //            Lesson: a 200 from a whitelist-based API is NOT proof the data landed. Verify
        //            against the schema, not the status code.
        // 1.0.160.74 - Audit-DB code moved to a PARTIAL (AiChatController.AiAudit.cs) at the owner's
        //            request -- AiChatController.cs had reached 15,261 lines and this subsystem is
        //            self-contained. Main file now 15,077; the partial is 226. No behaviour change.
        //            Plus, per 1.5.57.1: AlertId/SiteId are Int columns server-side, and GetCtx
        //            returns "" (never null) when absent -- so the required-field check PASSES and the
        //            failure surfaces as a 500 on type conversion. 500s carry no AlertId echo, making
        //            them the hardest to trace. New CtxLong sends 0 instead of depending on server
        //            coercion. Your own Run log shows this is live, not theoretical: alerts 706, 707,
        //            712, 716, 719 all rendered with a blank site.
        //            COVERAGE, asked and answered: 3 of 20 routes and 1 of 6 tables are wired
        //            (AIAnalysis Start/Complete/Fail -> AI_ALERT_Analysis). Unused today: Get/Served/
        //            Outcome/Tier/Stale (the CACHE and tier machinery -- the point of the design),
        //            Assessment, Rating, Chat, Request, McpCall, Stats.
        // 1.0.160.73 - AI AUDIT DB integration (EdgeX DataAPI 1.5.57.0, AI_ALERT_* tables), FAIL-OPEN
        //            BY CONSTRUCTION. Owner requirement: the routes may not exist on the running
        //            DataAPI and nothing here may ever stop or fail a verdict.
        //            The running DataAPI at 172.31.25.103:8083 reports 1.5.55.9 -- OLDER than the
        //            1.5.57.0 that introduced /api/AI*. So the routes are absent TODAY, and this ships
        //            dormant by DETECTION rather than by collecting 404s: the root endpoint returns
        //            {message, version}, so one probe per process decides capability. Below 1.5.57.0
        //            the client never attempts an AI route at all.
        //            Guards, in order of what they protect against: master flag AiAuditDbEnabled
        //            (default FALSE, ships dark); version probe (feature absent -> silent); circuit
        //            breaker after AiAuditDbMaxFail consecutive failures, dormant AiAuditDbRetryMin
        //            (an absent or sick API must not cost a round trip on every alert forever); its
        //            OWN HttpClient at AiAuditDbTimeoutMs (1500ms) so it can never inherit the 600s
        //            DeepSeek timeout; every method returns null/void on ANY failure so no exception
        //            can reach the verdict path; and if Start fails, all child writes are skipped --
        //            a correct partial state, not a cascade.
        //            Complete is fed from the SAME Build7WAudit object the JSONL uses, so the file and
        //            the DB cannot drift, and the JSONL is unaffected whether the DB is on, off or
        //            absent. AIRequest/AIMcpCall wiring follows once this pipe is proven live.
        // 1.0.160.72 - STOP THE MODEL CALLING history_get(AssetId). 59 tool results were truncated on
        //            2026-08-11 -- history_get x13 cut by ~48KB each. An AssetId call returns EVERY tag
        //            on the asset (~60KB); a per-TagId call returns 3.5-7.9KB, as the driver-fetch
        //            lines show (IR MA 3539, IF MA 7886). So the cut ones are the AssetId-wide calls.
        //            Truncation is not graceful: the marker is spliced into the MIDDLE of the payload,
        //            so the JSON is unparseable and the series is LOST, which is what trips the
        //            coverage gate -- exactly the 522365 INCONCLUSIVE.
        //            The prompt was ACTIVELY RECOMMENDING it: two places told the model to read the
        //            TagID from a history_get(AssetId) response. Both now say to use search_tags for
        //            TagIds and history_get(TagId) only, and state WHY (the AssetId form exceeds the
        //            transport limit and the result is discarded, not merely shortened).
        //            Pair with MaxToolResultChars=24000 in config: the current 15000 is BELOW the code
        //            default of 20000, and one legitimate 500-row single-tag series is ~9-12KB.
        // 1.0.160.71 - COMPILE FIX: CS0121 duplicate NormAttrKey. I wrote my own at 13344 without
        //            checking, and the codebase ALREADY had one at 11835 -- a better one, stripping
        //            known unit suffixes by regex, collapsing whitespace and upper-casing, and already
        //            used by DomainAttributeName. Mine deleted; the existing one is used.
        //            Note what this means: the codebase already knew that attribute keys carry a
        //            "(mA)" suffix and already had the helper for it. Had I searched for an existing
        //            normaliser when I first wrote the K2 lookup, the silent-K2 bug would never have
        //            existed. Same lesson as RangeAvgForAttid in 160.53 -- check for the helper before
        //            writing one.
        // 1.0.160.70 - ROOT CAUSE of the silent K2, found in the owner's log rather than inferred:
        //            the harvested series key is "ITC RELAY END(mA)" -- WITH the unit suffix, exactly
        //            as the HIST line prints it -- while my matcher tested exact equality against
        //            "ITC RELAY END" and "IR MA". It never matched, irRaw was always null, and the
        //            method returned "" silently. Every earlier fix (driver set, AddCanon, spelling,
        //            the fetch itself) was real and necessary, and none of them could ever have made
        //            K2 fire, because the lookup at the end was broken the whole time.
        //            Keys are now NORMALISED (upper-case, parenthetical unit stripped) before
        //            comparison, so "ITC RELAY END(mA)", "Ir mA" and "IR MA" all resolve.
        //            AND -- the reason this took four rounds -- BuildCircuitResidualLine had SIX
        //            early returns that all returned "" with no trace, so a K2 that never ran looked
        //            identical to a K2 with nothing to say. Every one now logs WHICH precondition
        //            failed, including the actual harvested keys. That is the real fix to the method
        //            that cost the owner four deploy-and-check rounds.
        // 1.0.160.69 - 160.66 was ALSO inert, and for an embarrassing reason: I inserted the min/max
        //            computation BEFORE the original `long.TryParse(lastTok, out lastTs)` instead of
        //            REPLACING it, so the original overwrote my value two lines later. The live log
        //            still reads 'TPR ends 50.7 h'. The dead lines are now removed, not merely
        //            preceded. Third inert 'fix' in this run of the same bug -- each time I verified
        //            the edit APPLIED without verifying it had EFFECT.
        //            Also: K2 still cannot fire, and the driver-set work was only half wired. 160.55
        //            made NewSearchArgs stop narrowing the TAG SEARCH, but the HISTORY PLAN still
        //            fetches only the alerting tag plus TPR -- the live trace shows exactly two
        //            history_get calls (TPR, ITC RELAY END) even though driverSet now correctly reads
        //            'IR MA, IF MA'. Resolving a driver set and then not fetching it is why every
        //            K2 fix so far has changed nothing. The plan now fetches history for EVERY
        //            resolved driver, capped by AnalyzeDriverSetMax.
        // 1.0.160.68 - 7W AUDIT completed against the owner's table: real WHO, INR cost, timeout, and
        //            the full prompt/response inline.
        //            WHO: every audit row read user=ENERGY7 -- the WisdomDefaultIdentity fallback --
        //            because ResolveWisdomIdentity checks User.Identity (anonymous here, the portal
        //            runs its own login) then a session key that is not configured. The portal's real
        //            user is ClsHttpContent.LoginUser, already used by IsWisdomReviewer, and the view
        //            already renders 'Signed in as Siddharth Singh' from FirstName+LastName. The audit
        //            now reads that first, so WHO identifies the operator instead of the system.
        //            COST in INR (owner: INR, not USD). Rates are CONFIG, defaulting to DeepSeek's
        //            published per-1M USD figures checked 2026-08-11: v4-flash 0.14 miss / 0.0028 hit
        //            / 0.28 out; v4-pro 0.435 / 0.003625 / 0.87. Cache-hit tokens are billed at the
        //            hit rate -- ignoring that would overstate cost ~50x on the cached prefix. USD is
        //            converted by AiUsdToInr (default 88) and the field is named costInrEst so nobody
        //            mistakes an estimate for billing truth; an unknown model yields NO cost rather
        //            than a guess.
        //            Also added: cost.timeoutSec (was missing from HOW MUCH), and what.promptFull /
        //            what.responseFull -- the owner asked for the raw JSON both ways in the audit row
        //            rather than only sizes plus a traceId join.
        // 1.0.160.67 - No controller logic change; pairs with Index_15 1.0.55.47. The Run log viewer
        //            printed '[object Object]' in the verdict column of every row, and its
        //            'inconclusive' counter therefore always read 0. what.verdict is a JObject
        //            {verdict, confidence, category, headline} (set at L14219) and I rendered it as a
        //            string. The viewer is otherwise working on live data: 18 runs, 575k tokens, and
        //            it immediately showed that runs before 1.0.160.63 report tools=0 while later ones
        //            report 8 -- the prefetch-counting fix landing, visible in the UI.
        // 1.0.160.66 - 160.65 and 160.63 both shipped INERT. The live 2026-08-11 logs carry the
        //            1.0.160.65 marker yet still print 'TPR ends 50.1 h', and the driver set is still
        //            'IR MA, IBLAST' with no IF MA. Three causes, all mine:
        //            (1) TWO span parsers exist. I fixed HistorySpan (used by the PAGER) but the log
        //                line comes from NoteHistoryTruncation, which parses ts ITSELF with
        //                arr.LastIndexOf(',') -- so the desc-order bug lived on in the place actually
        //                being read. Fixing one parser and declaring the class of bug closed was the
        //                error; NoteHistoryTruncation now uses min/max too.
        //            (2) AddCanon dropped already-canonical names. CircuitDriversFor RETURNS canonical
        //                forms ('IF MA'), and AddCanon re-canonicalised them through
        //                DomainAttributeName, which maps LABELS ('ITC FEED END(mA)') and does not
        //                round-trip its own output -- so 'IF MA' resolved to nothing and was silently
        //                discarded. It now falls back to the raw upper-cased name.
        //            (3) the attribute is spelled IBlAST in the range table, so its canonical form is
        //                IBLAST, while CircuitDriversFor keyed on IBALST -- the K2 expansion could
        //                never match. Both spellings accepted.
        //            Net effect until now: K2 has NEVER fired in production (0 'circuit residual
        //                injected' lines all day) and every run reported a phantom 50 h TPR gap.
        // 1.0.160.65 - HistorySpan assumed ASCENDING order, which MY OWN 160.38 sort=desc broke. It took
        //            the FIRST token of the ts array as earliest and the LAST as latest. That held while
        //            every series came back ascending; since 160.38 the TPR fetch carries sort=desc, so
        //            its last element is the OLDEST sample and every run reported a phantom ~50 h gap.
        //            Proven on 2026-08-11: TPR ts ends 1786289826, winTo 1786470450 -> 50.17 h, and the
        //            log printed 'ends 50.2 h before window end'. TPR was actually current to 15:20:45,
        //            roughly 8 h from the window end, not 50.
        //            Two consequences, both live: (1) coverage looked broken on every analysis when
        //            nothing was truncated at all -- the same logs read 'n=146 of limit 5000 (not
        //            truncated)', i.e. 3% of the cap; (2) the pager is driven by the same number
        //            (`winTo - pLast <= 3600`), so a fabricated 50 h gap made it probe forward for
        //            pages that do not exist, on every run.
        //            Fixed by taking MIN and MAX of the array instead of first and last, which is
        //            correct for asc, desc, and any future ordering. The detected order is logged so a
        //            regression here cannot hide again.
        //            NOTE (not this fix): winTo comes back +5:30 later than the requested EndDate --
        //            the server reads the date string as UTC -- so every coverage figure is inflated by
        //            5.5 h on top. Raised separately; it needs an EdgeX-side decision.
        // 1.0.160.64 - No controller logic change; version bump pairs with Index_15 1.0.55.46, which
        //            stops the Log download SILENTLY omitting the AUDIT (7W) block. Silence made three
        //            different situations look identical -- old controller, cached verdict, or a real
        //            bug -- and the operator could not tell which. The block now always prints and
        //            names the reason.
        // 1.0.160.63 - THREE FLAWS found in the FIRST live run of the new code (alert 524613, 03AT SK).
        //            The audit block did its job immediately -- it exposed all three.
        //            (A) cost.toolCalls=0 / toolMs=0 while the trace showed EIGHT prefetch calls.
        //                _trToolCallCount is incremented only inside the agentic loop (L13756), so the
        //                prefetch stage -- which is where nearly all tool work happens -- was invisible.
        //                An audit line that reports zero tool calls for a run with eight is worse than
        //                no line. Prefetch calls and ms are now counted and reported separately.
        //            (B) wisdom doctrinal=0 and checklist=0 were FALSE ZEROS. CountRuleLines only
        //                matched lines starting '- ' or '* ', but the checklist emits NUMBERED items
        //                ('1. Shorting: ...') and doctrinal emits cited blocks. So the headline metric
        //                I proposed -- '% runs with NO wisdom injected' -- would have read as missing
        //                wisdom when 22975 chars had in fact been injected. Now counts bullets,
        //                numbered items and bracketed citations.
        //            (C) the K2 circuit residual could NEVER fire on TC TR VOLT LOW. That cause maps to
        //                ITC RELAY END alone, the card carries one condition, and 160.55 L3 expanded
        //                only DERIVED members -- so If was never fetched and K2 (If = Ir + IBALST)
        //                silently returned nothing. Confirmed live: circuit=7 rules injected (Phase 1)
        //                yet no [circuit] evidence line (Phase 2). L3 now also pairs MEASURED invariant
        //                partners: Ir pulls If and vice versa, so the discriminator can actually run.
        // 1.0.160.62 - COMPILE FIXES to my own 160.57 Build7WAudit: CS0103 x2 + CS1955. I wrote the
        //            audit record against members whose SHAPE I had not verified:
        //            (a) `_siteKeep` is a LOCAL variable inside the request methods (L2964), not a
        //                field, so Build7WAudit could not see it -> now calls SafeIsSiteKeeping()
        //                directly, which is what that local is assigned from anyway;
        //            (b) `AnalysisModelOverride` is a PROPERTY (L1898), not a method -> the ()
        //                removed.
        //            Third compile break of this arc (CS0120 in .55, CS0579 in .59, these). All three
        //            passed the brace/paren/bracket scan, because that scan proves STRUCTURE and says
        //            nothing about whether a symbol exists, is static, is a property, or already
        //            carries an attribute. Every identifier used by new code is now checked against
        //            its declaration before the build, not after.
        // 1.0.160.61 - COMPILE FIX to my own 160.59: CS0579 duplicate [HttpGet]. I inserted RunLogFeed
        //            using `public ActionResult DownloadAiLogs(...)` as the anchor, but that method's
        //            [HttpGet] sits on the line ABOVE it -- so the insertion landed BETWEEN the
        //            attribute and its method: RunLogFeed ended up with two attributes and
        //            DownloadAiLogs with none. Anchoring on a method signature is unsafe whenever the
        //            method carries attributes; the anchor must include them.
        //            Fixed: one [HttpGet] each. No behaviour change -- 160.59/.60 never compiled.
        // 1.0.160.60b- TWO MORE HOLES from the same audit, and the second is the serious one.
        //            HOLE C: bash admission had NO ROLE CHECK anywhere -- not in free chat (since
        //            160.42) and not in analysis (160.59). The follow-up chat box is available to
        //            EVERY authenticated user, so an ordinary railway user could ask a follow-up, the
        //            model could call bash, and platform/hardware log content could come straight back
        //            in the reply. That is the exact outcome the owner ruled out, reached by a route
        //            nobody had gated. Bash is a TOOL, and the owner's rule is that tools belong to
        //            site-keeping users and the config emails only -- so both admission branches now
        //            require AiDebugAllowed().
        //            HOLE D: the free-chat / follow-up prose path (fullText) ran only
        //            FilterOperationalActions, so even for a privileged user any bash output pasted
        //            into a reply was unredacted. It now passes ScrubPlatformInternals as the verdict
        //            fields do. viz.secondary label/unit scrubbed too, for completeness.
        // 1.0.160.60 - SELF-AUDIT FIXES to my own 160.59 redaction. Two holes, the same mistake twice:
        //            I scrubbed SOME verdict text instead of ALL of it.
        //            HOLE A: recommended_action took the FilterRecommendedAction branch and never
        //            reached ScrubPlatformInternals -- it is model-written prose and can carry a path.
        //            HOLE B (worse): the ENTIRE Hinglish block bypassed it. `hi` is produced by a
        //            SEPARATE translation call that re-renders the same content and runs only
        //            FilterOperationalActions. So a leak redacted from the English headline could
        //            reappear verbatim in hi.headline -- a translator carries a file path through
        //            unchanged. Redacting the English while publishing the Hinglish is no protection
        //            at all: both are rendered to the same railway user on the same card.
        //            Both now pass ScrubPlatformInternals, including recommended_action_en which is
        //            copied into the hi block from the English side.
        // 1.0.160.59 - BASH IN ANALYSIS (owner decisions: cap 2 calls, REDACT on leak, BOTH paths) plus
        //            the RUN LOG VIEWER tab. Bash was free-chat only since 160.42; the owner wants it
        //            available during analysis for scripts/queries that verify telemetry, because the
        //            question a verdict most often cannot answer -- 'did the sensor stop reporting, or
        //            did the value genuinely not change?' -- is a database question.
        //            Owner also ruled logwatch_* OUT: those are platform/hardware logs and must not
        //            reach railway users. That ruling is what shapes the guard here, because bash can
        //            read those same files with NO restriction, and a verdict is rendered TO railway
        //            users. So the boundary is enforced in CODE, not by instruction:
        //            ScrubPlatformInternals runs over every verdict string after
        //            FilterOperationalActions and REDACTS UNC shares, service paths, log-level tokens,
        //            .log filenames and stack markers, logging each hit. Advisory prompt text cannot
        //            stop a paraphrase; this can.
        //            Cap: AnalyzeBashMaxCalls (default 2) per run -- beyond it the call is refused with
        //            a message the model can act on, so a loop cannot burn the 18-iteration budget.
        //            Flag AnalyzeBashInVerdict default FALSE. NOTE it does not change that bash runs
        //            as SYSTEM with ApiToken empty: the scrub bounds what LEAVES in a verdict, not
        //            what a script may do. Set ApiToken and move the service off LocalSystem first.
        // 1.0.160.58 - DEBUG/TOOLS PRIVILEGE GATE (owner rule: site-keeping users and the explicit
        //            emails in config get ALL debug and tools; everyone else gets Analyze and Re-run
        //            only). SafeIsSiteKeeping() ALREADY expresses exactly that -- the site-keeping
        //            role OR a match in SiteKeepingOverrideEmails -- so this reuses it rather than
        //            inventing a second scheme.
        //            **Closes a live exposure**: ToolTrace (L2426) had NO authorisation at all, so any
        //            authenticated user could fetch the full tool trace of any run by convId -- every
        //            call, argument and RAW RESULT. That is precisely where platform/hardware detail
        //            surfaces, which the owner ruled must not reach railway users. DownloadAiLogs was
        //            gated (IsWisdomReviewer); ToolTrace had been missed.
        //            New AiDebugAllowed() is the single predicate; ToolTrace now returns 403 without
        //            it, and Status reports `debugAllowed` so the view can hide what it must not
        //            offer. Hiding buttons is PRESENTATION ONLY -- the server check is the control.
        // 1.0.160.57 - 7W AUDIT LINE + wisdom-injection counters. One `audit_7w` JSONL record per run
        //            in ai-analyze-*.jsonl, keyed to the owner's 7W table (WHO/WHAT/WHEN/WHERE/WHICH/
        //            WHY/HOW MUCH) and shaped so the later RAG migration is an INSERT..SELECT over the
        //            JSONL rather than a re-instrumentation. This COMPLETES the existing TraceAnalyze
        //            stream (request_start/prompt/request_end/envelope already carried WHEN, WHY,
        //            WHERE and the token counts) rather than adding a subsystem.
        //            The block that did NOT exist in any form is WISDOM: AiChatController.Wisdom.cs
        //            logged only save/edit/approve, so 'did this analysis actually use wisdom?' was
        //            answerable only by inference from the verdict text. Now the injected rule counts
        //            and total chars are recorded per run, alongside the resolved driver set.
        //            USD is deliberately NOT estimated -- token counts are exact and a wrong price in
        //            an audit table is worse than a missing one; add it when a rate table is agreed.
        //            Emitted for verdict, chat and follow-up (requestType distinguishes them), inside
        //            the existing finally block so it survives error paths, and try/catch wrapped so
        //            an audit failure can never affect a verdict. Flag AnalyzeAudit7W (default true).
        //            The per-run Log download (Index_15 1.0.55.43) now carries the same block.
        // 1.0.160.56 - COMPILE FIX to my own 160.55: CS0120. I put an AiLog(...) call inside
        //            NewSearchArgs, which is STATIC, while AiLog is an INSTANCE method (L5409). The
        //            driver-set logging is moved to the instance call site in the prefetch, where the
        //            set is recomputed once purely to log it (DriverSetForCause is pure and cheap --
        //            dictionary lookups over the card, no I/O). 160.55 never ran; it did not compile.
        //            No behaviour change beyond the log line moving.
        // 1.0.160.55 - PREFETCH THE CAUSE'S DRIVER SET, not one attribute. Alert 522365 (46T GLTA,
        //            TC CKT OPEN) went INCONCLUSIVE after 65s and 2 turns on an alert whose answer was
        //            on the card. NewSearchArgs named the alerting attribute as
        //            triggeredConditions[0].paramLabel -- ALWAYS the first condition. That card has two
        //            OR'd conditions: [0] ITC FEED END 1072.78 PASS, [1] ITC RELAY END 2.16 FAIL. It
        //            narrowed the tag search to the condition that PASSED, never resolved the relay-end
        //            TagId, could not prefetch the alerting history, and left history to the model --
        //            which fetched by AssetId (all 11 tags, ~45KB), was truncated, and could not prove
        //            coverage. RdpmsMaps.CauseToAttributes ALREADY holds the right answer
        //            (TC CKT OPEN -> ITC RELAY END + ITC FEED END) and NewSearchArgs never consulted
        //            it. Now the driver set is built in three layers, most authoritative first:
        //            L1 CauseToAttributes[cause]; L2 EVERY triggeredConditions[].paramLabel (not [0]),
        //            failing ones first; L3 circuit-wisdom upstream drivers -- a derived member such as
        //            IBALST expands to If and Ir, without which the 160.50/.53 K2 residual silently
        //            returns nothing for want of one series. Capped at 6 with L1 first. When the set
        //            has more than one member the AttributeName narrowing is SUPPRESSED and the full
        //            tag list is fetched by AssetId, exactly as the existing derived and PM/IPS
        //            branches already do. Single-member sets keep the old narrowing. The [0] fallback
        //            survives only for causes absent from the map, and even then prefers the first
        //            FAILING condition.
        // 1.0.160.54 - K2 SIGN TEST (owner test case) + weather/train in one row (Index_15 1.0.55.39).
        //            The owner posed: T1 If=316 Ir=322, T2 If=316 Ir=500. Both are PHYSICALLY
        //            IMPOSSIBLE -- ballast can only STEAL current, so IBALST = If - Ir can never be
        //            negative and Ir can never exceed If. Replayed against the shipped code, BOTH came
        //            back as 'mixed -- judge on the attribute that moved': K2 tested the CHANGE in
        //            IBALST but never its SIGN, so an impossible reading was handed to the model as
        //            ordinary data. That is the exact path by which a calibration fault gets reported
        //            as ballast leakage and a healthy section gets meggered. Now the sign is tested
        //            FIRST: beyond tolerance negative -> IMPOSSIBLE, name the suspects (multiplication
        //            factor, swapped If/Ir channel, wrong tag binding) and state it is NOT a track
        //            fault; within ~2% negative -> calibration offset between two independently scaled
        //            sensors (If x207.92 vs Ir x204.85 on 03AT), noted and not alarmed.
        // 1.0.160.53 - PRE-DEPLOY REVIEW FIXES to my own 160.51 K4. Two flaws, both found by auditing
        //            against the existing codebase rather than re-reading my own diff.
        //            FLAW A (duplicate + fragile): I added ReadRelayCoilOhm with a bare
        //            JArray.Parse(range). The codebase ALREADY has RangeAvgForAttid(range, attid),
        //            which uses ParseJsonDocuments + SelectTokens + case-insensitive aliases
        //            (AssetAttributeId/AttributeId/Id/t, AverageValue/avg/Average) precisely because
        //            the payload is NOT reliably a bare array. My parse would have thrown on a wrapped
        //            payload, been swallowed by the catch, and silently used the 10.5 default forever.
        //            FLAW B (tautology -- the serious one): TR V (571) is DERIVED in this system as
        //            Ir x R737 / 1000 (ComputeDerivedDirect, 'derived TR_V571'; all assets are
        //            derive-571, there are no physical 571 sensors). So my K4 compared R737 x Ir
        //            against a value COMPUTED from R737 x Ir -- it can never disagree. The 0.001 V
        //            'verification' I ran proved an arithmetic identity, not physical coherence, and
        //            the check would have reported 'coherent' forever, giving false confidence.
        //            FIX: K4 now uses MEASURED sensors only -- Vr (att 3, a real sensor) and Ir (att 2)
        //            -- and tests DIRECTIONAL TRACKING over the window instead of absolute equality:
        //            relay volts and relay current must move together through the coil. Absolute
        //            equality against Vr is unusable because the Vr-vs-(R737 x Ir) offset differs per
        //            asset (3.4% on 41559, 0.7% on 41818) -- Vr is a different point in the circuit.
        //            ReadRelayCoilOhm DELETED (unused after the rewrite).
        // 1.0.160.52 - TRAIN CONTEXT CARD in the view (Index_15 -> 1.0.55.38). No controller logic
        //            change; version bump only, so meta.componentVersion still identifies the deployed
        //            pair. The verdict has carried train_context since 1.0.160.33 and correctly since
        //            .46/.47, but NO view ever rendered it -- 'train_context' appeared ZERO times in
        //            either view file, so every operator saw the weather card and no trains at all.
        //            The data was right; the UI simply had no slot for it.
        // 1.0.160.51 - K4 becomes a per-asset EQUALITY (owner: the coil resistance is in the asset
        //            range table). 160.50 shipped K4 as a direction rule because the ohm value was not
        //            confirmed constant. It is not constant -- it is PER ASSET, and already prefetched:
        //            attribute 737 'Track Relay resistance' in get_attribute_range. Verified exact on
        //            two live assets: 41559 R737 12.1 x Ir 238.95mA = 2.891V vs TR V(571) 2.89 (0.001V
        //            out); 41818 R737 10.2 x Ir 260.59mA = 2.658V vs 2.66 (0.002V out). Against Vr
        //            (att 3) the same product is 3.3% out, so the paired voltage is TR V (571), NOT Vr.
        //            New ReadRelayCoilOhm parses att 737 AverageValue from the range payload and falls
        //            back to 10.5 (the owner wisdom-file example computes 3.10V/294.75mA = 10.52) when
        //            the asset has no 737 row. BuildCircuitResidual now emits a K4 line alongside K2:
        //            expected TR V = R737 x Ir, compared with the measured value; a mismatch beyond 10%
        //            means relay volts and relay current disagree -> sensor/wiring, not degradation.
        //            IPS family still deferred (owner: add later, do the three first).
        // 1.0.160.50 - CIRCUIT TOPOLOGY + UPSTREAM CAUSALITY (owner wisdom files). The verdict judged a
        //            breach on its own series and sustain without ever asking whether the PHYSICS
        //            permits it. Owner rule: every value has an upstream driver, so a downstream value
        //            cannot move unless something upstream moved -- either an upstream mover NAMES the
        //            cause, or the change is a computation/sensor artifact. PHASE 1 (knowledge): new
        //            CircuitWisdom module (AiChatController.CircuitWisdom.cs) injects the per-family
        //            flow chain, the invariants (K1-K6 track, S1-S3 signal, P1-P3 point, X1 shared) and
        //            the causality rule as an ADVISORY block, subordinate to CauseLogicMaps/RdpmsMaps.
        //            Structure only -- the owner's example figures are one snapshot and are NOT
        //            reproduced as thresholds. PHASE 2 (computed): BuildCircuitResidual evaluates K2
        //            (ITC FEED END = ITC RELAY END + IBALST) at the incidence from values already
        //            prefetched and emits a [circuit] evidence line naming which side moved --
        //            relay-end falling with feed-end HELD = ballast/joint leakage; both falling
        //            together = feed/supply, NOT leakage. Gated by AnalyzeCircuitWisdom (default
        //            FALSE, canary) + AnalyzeCircuitWisdomMaxRules (default 8). Best-effort try/catch;
        //            a fault here never sinks the verdict, and it NEVER overturns a card-confirmed
        //            breach on its own. K4 ships as a DIRECTION rule (not an equality) because the
        //            relay coil ohm value is not confirmed constant per asset. IPS/power-supply family
        //            deferred -- no wisdom file supplied for it.
        // 1.0.160.49 - WEATHER CONDITION: real sky state instead of Rain/Dry. BuildWeatherCard emitted
        //            only "Thunderstorm" | "Rain" | "Dry". The view's rvwxKind() already classifies
        //            sun/cloud/fog/rain/snow/storm/wind -- but "Dry" matches NONE of its patterns, so
        //            every non-rainy alert fell through to the generic 'def' cloud scene. The data was
        //            already being fetched and thrown away: the Open-Meteo request asks for
        //            weather_code, and SummariseWeather read those codes ONLY to test 95/96/99 for
        //            thunder, then discarded them. Now the incidence-hour WMO code is carried through
        //            (weather_code + the worst code in the window) and mapped to a real condition:
        //            Clear / Partly cloudy / Overcast / Fog / Drizzle / Rain / Showers / Snow /
        //            Thunderstorm. NO VIEW CHANGE -- every emitted word already matches an existing
        //            rvwxKind pattern. Day/night is NOT split (is_day is not in the hourly request):
        //            code 0 emits "Clear", which the view draws as sun at any hour.
        // 1.0.160.48 - WISDOM DISTILLATION ON ALL PROVIDERS. Rule distillation was hardcoded to a
        //            dedicated Anthropic client (CreateWisdomAiClient) that ignored the engine
        //            selection and THREW if AnthropicApiKey was absent. On a DeepSeek-first
        //            deployment every verdict ran on QUANTUM while distillation still billed the
        //            Anthropic key -- and zero Anthropic credit silently killed 'Save last as
        //            wisdom' while every other feature stayed healthy. Wisdom.cs is a PARTIAL of
        //            this class, so the fix is a client swap, not new plumbing: the distill call now
        //            goes through CallModelAsync (the same provider dispatch the verdict uses).
        //            SaveWisdom sets _provider from the context it ALREADY parsed (the value was
        //            parsed but unused for the model call) so a rule is distilled by the same engine
        //            that produced the verdict being corrected; _thinkReq is forced false (a one-line
        //            rule never justifies 40-90s of hidden reasoning). _wisdomAi/CreateWisdomAiClient
        //            deleted; _wisdomAiLock kept. ExtractRuleText needs NO change -- the DeepSeek and
        //            OpenAI clients already return Anthropic-shaped content[{type:text}] (verified in
        //            DeepSeekClient), which is exactly what it parses.
        // 1.0.160.47 - TRAIN-CONTEXT last/next train, ALWAYS present (owner request). The block only
        //            reported trains INSIDE +/-TrainContextWindowMin, so a quiet station returned
        //            scheduled=false with nearest=null and no sense of traffic at all (SAKHUN 15:21:
        //            6 trains all day, closest 191 min away). Widening the window is the wrong lever --
        //            it would need +/-191 min there, which at PPTA (88 trains) would match half the
        //            timetable. Instead two NEW fields are always emitted, independent of the window:
        //            `previous` (last train to have DEPARTED before the alert) and `next` (next train
        //            to ARRIVE after it), each with mins_since / mins_until and full 24h wrap so the
        //            last train of the night rolls to the first of the morning. The window-based
        //            `scheduled`/`matches`/`nearest` are UNCHANGED -- only they mean 'a train was
        //            plausibly on this track'. previous/next are ORIENTATION ONLY and the note says so
        //            explicitly, so a train that passed hours earlier is never read as the cause of a
        //            breach.
        // 1.0.160.46 - TRAIN-CONTEXT station-code resolution. The block only read the TOP-LEVEL ctx
        //            keys "station"/"stationCode", but the AlertAnalysis payload sends station="" and
        //            carries the real code NESTED at alertCard.stationCode ("SK"), so stn was empty and
        //            the whole block was skipped -- no train_context, and the diagnostic header printed
        //            'station: -'. GetCtx reads top level only, by design, so a fallback chain is added:
        //            ctx.station -> ctx.stationCode -> ctx.alertCard.stationCode -> the stationCode the
        //            E7 API already returns in get_attribute_range_history (rangeHist, the same payload
        //            the weather block reads lat/lon from -- server-sourced, so it survives any client
        //            that omits the code). stationNAME is deliberately NOT used as a fallback: 'SAKHUN'
        //            is not a valid timetable code (it hits the generic all-services page, which the
        //            160.41 Guard A rejects anyway). Verified live: SK -> SK/Sakhun, 6 trains.
        // 1.0.160.45 - Index_21.cshtml DELETED from the tree. 160.44 retired it but still shipped it
        //            (banner only), so the drop still contained two views and the wrong one could
        //            still be built. Verified no .cs references it -- neither controller names a view
        //            explicitly, so MVC resolves Index by convention and the file was dead weight.
        //            ONE view remains: Index_15.cshtml (1.0.55.37) carrying BOTH the [GATE] UI and
        //            QUANTUM 1.1 + THINK. No logic change; version + this note only.
        // 1.0.160.44 - SINGLE VIEW. No controller logic change (version + this note only). The project
        //            carried TWO diverged AlertAnalysis views: Index_15 (1.0.55.36, the newer trunk,
        //            carrying the [GATE] evidence UI) and Index_21 (1.0.55.31 base, where QUANTUM 1.1
        //            + THINK had been built). Deploying either alone silently dropped one team's
        //            feature. The QUANTUM 1.1 / THINK UI is now ported onto Index_15 (-> 1.0.55.37),
        //            which is the ONE view to build and deploy. Index_21.cshtml is RETIRED: kept in
        //            the tree for reference only, marked DO NOT DEPLOY at its head.
        // 1.0.160.43 - MERGE of two independently-cut 1.0.160.42 builds. Both were stamped .42 from the
        //            same 160.41 (TrainContext) trunk, so .42 is a COLLIDED/BURNED integer -- do not
        //            reuse it. Contents are unchanged from each side; this is a re-stamp + merge only.
        //            (a) GATED DERIVED COMPUTE (other team): track component samples are filtered to
        //            those taken while TPR is steady UP (+/-GateGuardSec) before the derived/sustain
        //            compute, so train reads and transition edges cannot contaminate RRAIL et al.
        //            Flags AnalyzeGatedDerived (false) + GateGuardSec (10). (b) QUANTUM 1.1 + THINK
        //            (this team): DeepSeek Flash as engine 'quantum11', per-request THINK toggle,
        //            verdict deadline disabled while thinking, DeepSeek HttpClient timeout 100s->600s.
        //            3-way merge was CLEAN (zero conflicts): the only overlap was this header block --
        //            their 6 hunks (5628/8994/9007/9062/10251) and my 8 (1581..13487) are disjoint.
        //            Interaction note: the gate runs UPSTREAM of the 160.37 computed-verdict and is
        //            model-agnostic, so it applies identically on QUANTUM 1.1 and under THINK.
        // 1.0.160.42 - QUANTUM 1.1 (DeepSeek Flash) + per-request THINK toggle. (A) new engine option
        //            'quantum11' -> ModelQuantum11 (default deepseek-v4-flash), added to the existing
        //            model list. The DeepSeek branch of CallModelOnceAsync previously IGNORED
        //            AnalysisModelOverride (only the Anthropic/OpenAI branches honoured it), so a
        //            variant would silently have run the base PRO model -- it now builds a cached
        //            variant client keyed by model+thinking+maxTokens. (B) THINK is a per-request UI
        //            toggle (ctx.think) rather than a process-wide config flag, so PRO/FLASH x
        //            THINK/NO-THINK are four distinct cached clients; DeepSeekThinking remains the
        //            default when the request does not specify. (C) thinking runs 43-92s+, so with
        //            THINK on the controller verdict deadline is DISABLED (owner rule) and the
        //            DeepSeek HttpClient timeout is raised from the .NET default 100s (never set --
        //            it, not the controller deadline, was the real killer) to DeepSeekHttpTimeoutSec
        //            (default 600). Bounded rather than infinite on purpose: AnalyzeMaxConcurrentVerdicts
        //            is 5, so a permanently stalled provider call would wedge a slot with no recovery.
        //            (D) GateProviderForRole collapsed every non-sitekeeping request to plain
        //            'deepseek', which would have made QUANTUM 1.1 invisible to ordinary users; it now
        //            PRESERVES deepseek/quantum variants while still forcing NEXUS/ORION to deepseek.
        // 1.0.160.41 - TRAIN-CONTEXT page-identity guards (regression introduced by my own 160.38
        //            div-parse). totaltraininfo does NOT 404 an unknown station code -- it serves a
        //            GENERIC page with a blank station name that still carries the "List of all trains"
        //            marker and ~1454 sch-Rows (Mumbai suburban 97xxx, one per minute). The pre-160.38
        //            </table> bound truncated that page to ~1 row, so it failed safe BY ACCIDENT; the
        //            div parser reads all 1453, and with a train every minute EVERY alert would match
        //            -> a confident, wrong "train at_station". Two independent guards, both applied
        //            before caching and before BuildTrainContext: (A) the page <title> must begin with
        //            the requested station code (real: "PPTA / Patliputra Junction..."; generic:
        //            " /  Railway Station..."); (B) parsed rows must not exceed TrainContextMaxTrains
        //            (default 400) -- one station never lists ~1500. Either guard alone kills the
        //            generic page; both ship for defence in depth. Verified: ppta/sk/glta PASS
        //            (88/6/6 trains), sakhun/gahlota/jig REJECTED (were 1453 bogus trains each).
        // 1.0.160.40 - SELF-REVIEW FIXES on 160.38/.39 (3 defects, none of which fired on today's live
        //            page -- all latent). D1 (SAFETY): the bash free-chat gate keyed on _jsonCapture,
        //            which is set ONLY by the JSON-verdict helper -- the STREAMING AnalyzeAlert path
        //            (rawJsonMode:true, validateVerdictOutput:true) leaves it null, so bash was exposed
        //            to the automated verdict loop, breaching the free-chat-only rule. Now gated on the
        //            loop's own intent flags (!rawJsonMode && !validateVerdictOutput) plus the old
        //            _jsonCapture check; Chat() and AnalyzeChat() (operator follow-up) keep bash, both
        //            verdict paths do not. D2 (data): PATH A took the first two HH:MM in the flattened
        //            row, so any stray time before the Arr cell became the arrival (proven: 'Updated
        //            23:59' -> arr=23:59). Times now come from sch-Cell cells whose ENTIRE text is a
        //            bare HH:MM. D3 (cosmetic): the name took the first title= in the row (a leading UI
        //            title such as 'Sort ascending' would win); the title is now read from the cell that
        //            also carries the /train/<no>/schedule anchor.
        // 1.0.160.39 - TRAIN-CONTEXT phase label: each train_context match (and 'nearest') now carries
        //            a 'phase' field -- arriving (arrival still ahead) | at_station (alert inside the
        //            arrival..departure interval: dwelling or passing now) | passed (departure behind the
        //            alert) -- so the report reads the train's position without interpreting delta_min.
        //            New TrainPhase helper; BuildTrainContext sets m[phase] + nearest[phase]. Matching
        //            logic unchanged (SignedDeltaToInterval). Fixes the 'passed/arrival not shown' gap
        //            that was masked in <=160.37 by the parser returning 1 train (fixed at 160.38).
        // 1.0.160.38 - BUNDLE (EdgeX 1.5.56.2 consumer + TrainContext fix): (1) TrainContext parser
        //            table->div: totaltraininfo migrated the timetable to <div class=sch-Table/sch-Row/
        //            sch-Cell>; new sch-Row path yields ~88 trains vs 1, legacy <table>/<tr> kept as a
        //            fallback. (2) sort=desc consumer: ShortAtEnd recovery advice + the TRUNCATED log now
        //            say 'use sort=desc' (was 'narrow the window') -- history_get gained a sort param.
        //            (3) HistArgs prefetch sends sort=desc so limit keeps the NEWEST rows (incident near
        //            window end); columnar [ts,v] carries absolute time and is re-sorted by ts downstream,
        //            so no array reverse is needed. (4) bash tool reachable in FREE-CHAT only
        //            (_jsonCapture==null); verdict read-only allowlist untouched; server BashToolEnabled
        //            stays false by default.
        // 1.0.160.36 - TRAIN-CONTEXT cache/parser hardening (P2s from external review of 160.35). (P2) table
        //            bounding: a missing </table> after the marker now makes the page INVALID (return no
        //            rows) instead of parsing the rest of the document. (P2) cache eviction: SweepTrainCtxCache
        //            drops expired entries on every write (TTL controlled reuse but never removed them, so the
        //            dictionary could grow for the process lifetime) + a hard 512-entry ceiling. (P2) cache
        //            only VALIDATED pages: the fetched body is cached ONLY when it parses to >=1 train, so a
        //            bot-challenge / changed-format page can no longer suppress a station for the whole TTL
        //            (it is simply re-fetched next time). Running-day filtering STILL not done, now with the
        //            precise reason: totaltraininfo's "Departure Days" column is populated CLIENT-SIDE by JS
        //            and is ABSENT from the server HTML a server-side HttpClient GET receives (confirmed on
        //            SK + GLTA: empty cells + "Loading..."); running_day_checked=false stands. Feature still
        //            OFF by default; still display-only; Index render still a follow-on. Controller-only.
        //            No verdict-logic change -> no 23-label re-run.
        // 1.0.160.35 - TRAIN-CONTEXT HARDENING (P1s from external review of 160.33/34) + OpenAI doc fix.
        //            (P1) parse-validity: BuildTrainContext now returns NULL when ZERO trains parse, so a
        //            blocked/changed/invalid-station page is OMITTED instead of reported as a false
        //            "scheduled=false / no train" (a page that DID parse but has none in the window still
        //            yields a real scheduled=false). ParseStationTrains now REQUIRES the "List of all
        //            trains" marker and bounds the region to that table (marker -> next </table>), not the
        //            whole page. (P1) timeout: fetch uses ResponseContentRead so the 4s linked-CTS timeout
        //            covers the FULL body download (headers-only completion let a stalled body run long) +
        //            MaxResponseContentBufferSize caps the buffered body (TrainContextMaxKB, default 2048).
        //            (P1) running day: schedule is the BOOKED timetable; running day is NOT verified --
        //            added running_day_checked=false and the note now says so (true day-filtering deferred:
        //            needs the raw day-column, not in the rendered page). Matching now uses the
        //            arrival..departure INTERVAL +/- window (delta_min 0 = standing at the station), so a
        //            train already at the platform when the alert fired is caught (was arrival-only); adds
        //            dep_time to matches/nearest. Per-station short-TTL cache (TrainContextCacheMinutes,
        //            default 10) so repeated alerts do not re-hit the public site. train_context stays
        //            display-only (attached post-verdict; does NOT change classification) and the Index
        //            render is still a follow-on -- feature remains OFF by default (TrainContextEnabled).
        //            (Doc) OpenAiClient 1.0.1.0->1.0.2.0: comment no longer claims "identical/deterministic";
        //            seed is documented best-effort (system_fingerprint capture noted as a follow-up).
        //            No verdict-logic change -> no 23-label re-run for this build.
        // 1.0.160.34 - SAMPLING FIX (P0/P1 from external review of 160.32). (P0) Anthropic: the verdict
        //            model call sent temperature=0 to every Anthropic model, but Claude Opus 4.7+ and
        //            Claude Fable 5 REJECT temperature/top_p/top_k -- any non-default value returns HTTP
        //            400 (confirmed in the official migration + Opus 4.8 docs) -- so NEXUS 1.9/4.9 failed
        //            EVERY request. AnthropicClient now sends NO temperature and NO seed (Domain
        //            AnthropicClient 1.0.2.0->1.0.3.0). (P1) DeepSeek: seed is not in DeepSeek's Chat
        //            Completions schema (ignored or rejected); DeepSeekClient now sends temperature only,
        //            no seed (Domain DeepSeekClient 1.0.1.0->1.0.2.0). OpenAI unchanged (accepts temp+seed;
        //            negative-sentinel omit path retained). Feature renamed "reduced sampling variance" --
        //            temperature 0 is NOT determinism (it never guaranteed identical output on any model);
        //            true verdict consistency is the computed-verdict-wins path (sustain/breach result
        //            sets the classification, model narrates it), still pending owner's rule. Config
        //            AnalyzeTemperature(0)/AnalyzeSeed(42) still honoured where the provider accepts them;
        //            with this fix the DEFAULTS are safe on all three providers (no Web.config change
        //            needed). No verdict-logic change -> no 23-label re-validation required for this build.
        // 1.0.160.33 - TRAIN-CONTEXT (Part B), gated by TrainContextEnabled (default FALSE). Attaches a
        //            train_context block to the verdict: trains SCHEDULED through the alert's STATION
        //            within +/- TrainContextWindowMin (default 5) of the alert time. Direct HTTP GET of a
        //            public timetable (totaltraininfo.com/station/<code>/, no API key) parsed controller-
        //            side, best-effort, zero model tokens -- mirrors the weather card exactly. Signed
        //            delta_min (negative = before the alert); nearest + matches[]. Every output labelled
        //            "scheduled, NOT confirmed passage -- verify live with section control; not authority
        //            to occupy the track" (station-level scheduled data only; no TPR/track-circuit check).
        //            Any failure (egress/timeout/parse, non-IR station) leaves no train_context and never
        //            touches the verdict. Source is config-swappable (TrainContextBaseUrl) so a structured
        //            IR API can replace it later. Verified SK + GLTA (NWR) parse cleanly. Controller-only;
        //            no map/prompt change. Index popup line + push footer are small follow-ons (not in
        //            this build). TrainContextEnabled=false = exact pre-160.33 behaviour.
        // 1.0.160.32 - DETERMINISTIC SAMPLING. The verdict model call set NO temperature and NO seed, so
        //            every provider ran at its default (~1.0) stochastic temperature -- identical input
        //            produced DIFFERENT verdicts (proven: alert 484520 ran twice, byte-identical context,
        //            got NOT_CONFIRMED then CONFIRMED). Fix: all three provider clients (DeepSeek, OpenAI,
        //            Anthropic) now send temperature (default 0 = greedy) + a fixed seed (DeepSeek/OpenAI;
        //            Anthropic has none). Configurable: AnalyzeTemperature (default 0), AnalyzeSeed
        //            (default 42); set either negative to OMIT the field (fall back to provider default)
        //            if a model rejects it (e.g. an OpenAI reasoning model that only accepts temperature 1).
        //            Anthropic clamps temperature to 0..1. Makes verdicts REPRODUCIBLE (same input -> same
        //            output); it does NOT make a borderline verdict "correct" -- for that the decision must
        //            be COMPUTED (see the 4b sustain-window work), which is the follow-up. Behaviour change:
        //            verdicts may shift to the single most-likely answer -- validate vs the 23-label set;
        //            AnalyzeTemperature>0 restores stochastic behaviour. Client-side; no map/prompt change.
        // 1.0.160.31 - DERIVED SERIES + SUSTAIN CHECK (Item 4b), gated by AnalyzeDerivedSeries
        //            (default true). Reconstructs the derived value as a per-instance SERIES on the
        //            UNION timeline of its input sensors (each carried forward, deadband), computed with
        //            ComputeDerivedDirect at every instant; finds the longest continuous breach run and
        //            confirms its DURATION against the FRS sustain window ("... for 15s", parsed from
        //            CauseToLogic) -- so sustained-vs-transient for the DERIVED value is COMPUTED, not
        //            inferred from the input trends. Threshold (MaxSafe/MinSafe) and direction come from
        //            the FRS range for the derived attid + the cause name (HIGH/OPEN = upper, LOW = lower).
        //            Injects one evidence line ([DERIVED SERIES ... longest run = Ns, peak X vs MaxSafe Y;
        //            FRS requires >= Ms -> SUSTAINED/MOMENTARY]). ADDITIVE (evidence only, does not hard-
        //            override the verdict) + gated; builds only from real fetched input series. Completes
        //            the derived work on top of 160.30's at-incidence compute. AnalyzeDerivedSeries=false
        //            reverts. Controller-only; no EdgeX / map / self-learning change.
        // 1.0.160.30 - DERIVED IN-CONTROLLER COMPUTE (Item 4c), gated by AnalyzeDerivedComputeV2
        //            (default true). Fixes the nested derived causes (RTC VAR RES 588, RRAIL 590,
        //            CH RELAY END 589/591) that came back uncomputable. Two root causes fixed:
        //            (1) the base sensors were fetched by PARSING the prose formula, whose short forms
        //            (ChgOpV/Vf/If) DomainAttributeName drops -> inputs silently never fetched. Now
        //            fetched by a PINNED canonical input list per derived attid (DerivedBaseInputs),
        //            which resolves against search_tags. (2) TryEvalFormula cannot tokenise the nested
        //            formula strings (VTC_VAR_RES/TR_V571 underscores, [If!=0], where-clauses). Now a
        //            hardcoded ComputeDerivedDirect computes each attid from the base as-of values +
        //            R737 (attid 737 AverageValue from the fetched FRS range; TR_V571 = Ir*R737/1000,
        //            the derived track-relay voltage RRAIL/CH-RELAY-END build on -- NEVER the 571 sensor).
        //            ADDITIVE + gated: runs only as the fallback when TryEvalFormula fails, computes only
        //            from real fetched inputs (never fabricates), and the existing card cross-check
        //            catches divergence. VALIDATE on a live derived alert (TC VAR RES / RRAIL) that the
        //            computed value now populates and matches the card; AnalyzeDerivedComputeV2=false to
        //            revert. Controller-only; no EdgeX / map / self-learning change.
        // 1.0.160.29 - REMOVED the legacy IPS 24V DC causes (IPS 24V DC LOW + IPS 24V DC FAIL) too --
        //            same as the 60V in 160.28: legacy bus-level placeholders with EMPTY attribute lists,
        //            "(legacy)" in CauseToLogic, and REDUNDANT because the 24V DC LOC rail (VIPS DC R EXT)
        //            is already monitored where it matters -- it is referenced by the point (PT N/R IND
        //            VOLT LOW/FAIL AT LOC), signal (all HPR) and track (TC TR RELAY DEFECT / CONTACT RES)
        //            causes (27 references, ALL intact -- the removal touches only the empty bus causes,
        //            never VIPS DC R EXT). Deleted from RdpmsMaps.CauseToAttributes + CauseToLogic. With
        //            160.28 this clears all 4 legacy IPS *V DC bus causes; no IPS cause now has an empty list.
        // 1.0.160.28 - REMOVED the legacy IPS 60V DC causes (IPS 60V DC LOW + IPS 60V DC FAIL) -- no
        //            physical 60V DC rail exists (the IPS DC-DC bus is 110/24/6 V DC; the "60V" causes
        //            were legacy placeholders with EMPTY attribute lists). Deleted from BOTH maps:
        //            RdpmsMaps.CauseToAttributes and CauseLogicMaps.CauseToLogic (the logic entries were
        //            explicitly labelled "(legacy)"). No other references (controller/Index resolve causes
        //            generically). The IPS 24V DC bus causes are ALSO legacy but were LEFT IN pending the
        //            owner's mapping decision (VIPS DC R EXT = the 24V DC LOC rail). Map-only change.
        // 1.0.160.27 - EVIDENCE PROVENANCE (Item 6), gated by AnalyzeEvidenceProvenance (default true).
        //            6A: a BuildFieldWisdom rule tells the model to prefix EVERY evidence bullet with a
        //            source tag -- [fetched] (from a tool/telemetry call), [record] (from the alert card /
        //            get_frs_alerts, not independently fetched), [computed] (derived from fetched inputs);
        //            tag excluded from the word count; prefer fetched/computed over record. 6B: new static
        //            ComputeEvidenceProvenance(v) tallies those tags from the verdict evidence array and the
        //            envelope attaches meta.provenance = {fetched,record,computed,tagged,total,verified} (best-
        //            effort try/catch). Makes audit-independence VISIBLE + measurable: verified=false or a high
        //            record count = a verdict leaning on the card. Additive -- does NOT change verdict
        //            classification; no map/logic/self-learning change. Index badge is a separate follow-up.
        // 1.0.160.26 - CARD CROSS-CHECK (Item 7), in BuildFieldWisdom right after W7, gated by
        //            AnalyzeCardCrossCheck (default true). Operationalises the "verify with the card, do
        //            NOT decide from it" principle: for each triggered card condition, compare its claimed
        //            value/state against the FETCHED history for the SAME tag -- a relay-state condition
        //            against the relay's DataLogger transitions, an analog/derived/RDPMS condition against
        //            the RDPMS tag's history at the incidence. AGREE -> CONFIRMED (cite both); DISAGREE ->
        //            FLAG as a possible phantom/stale/false trigger, report both values, lower confidence
        //            (this is how a false alert like 505228 phantom-0V gets caught); UNFETCHABLE -> fall
        //            back to the card value. Missing history never downgrades (preserves W7's anti-false-
        //            coverage-gap rule); only CONTRADICTING history does. CHANGES verdicts (can now FLAG a
        //            contradicted card) -- validate vs 23-label + session cases; AnalyzeCardCrossCheck=false
        //            for instant rollback. Prompt-only; no map/logic/self-learning change. On the 160.25 trunk.
        // 1.0.160.25 - FIELD-EXPERT WISDOM v2 (Item 8), in BuildFieldWisdom, gated by AnalyzeFieldWisdomV2
        //            (default true). CENTERPIECE (section 2): the Failure-vs-Predictive verdict logic --
        //            a FAILURE (relay-state) trigger that genuinely occurred is CONFIRMED regardless of
        //            recovery / train presence (recovery affects severity only), while a PREDICTIVE
        //            (analog) breach is a VALID TRIGGER whose verdict still follows diagnostic reasoning
        //            (may be CONFIRMED / NOT CONFIRMED / INCONCLUSIVE); "valid trigger" != "CONFIRMED".
        //            Plus general data-scope rules (full-day >=24h, raw datalogger + family logs, predictive
        //            gating escalation, widen-scope-when-challenged, field-action pointers), track rules
        //            (diurnal ballast-leakage signature, ML/leakage caveat), and a supply sharpening
        //            (negative=always measurement-chain vs zero=ambiguous/can be genuine disconnection).
        //            CHANGES verdict CLASSIFICATION behaviour -- VALIDATE against the 23-label set + the
        //            Item 8 session cases before wide rollout; set AnalyzeFieldWisdomV2=false for instant
        //            rollback to exact pre-160.25 wisdom. Prompt-only; no map / logic / self-learning change.
        // 1.0.160.24 - DOCTRINAL SIGNALLING WISDOM (Item 9). New file AiChatController.RailwaySignalling
        //            Wisdom.cs = 149 normative/derived rules curated from RDSO SPN/257/2025, IRSEM 2021,
        //            and the Glued Joint Manual (RailwayWisdomRule[] + scored FindRules + BuildPrompt).
        //            New BuildDoctrinalWisdom(ctx) maps the alert's asset type -> rule asset_family
        //            (Track Circuit / Point Machine / Signal / Power Supply), retrieves the top-N rules
        //            scored for that cause+family, and appends them as an ADVISORY reference block on the
        //            SAME prompt injection as BuildFieldWisdom. ADDITIVE + SUBORDINATE: the block states
        //            (and the class SafetyContract states) that CauseLogicMaps/RdpmsMaps and the FRS cause
        //            rule remain authoritative; where they differ, the maps win. Each rule cites its
        //            source clause (citations spot-verified: SIG-002 cl.7.1.12 'Aspects of Signals',
        //            TRK-002 cl.17.1.1 'Track Circuits General' - exact). Gated by AnalyzeDoctrinalWisdom
        //            (default true) + AnalyzeDoctrinalWisdomMaxRules (default 6). Best-effort (try/catch,
        //            never sinks the verdict). No change to verdict logic, maps, or the wisdom self-learning
        //            system. Controller + one new file only; Index unchanged.
        // 1.0.160.23 - MERGE: 160.20 (srv2 gate) + 160.21 (per-end attid anchor) + 160.22 (unknown-
        //            cause reclassification) reconciled into ONE trunk. 160.21/160.22 were cut from the
        //            160.19 base and LACKED the 160.20 srv2 gate (blank/absent McpServerUrl2 would throw
        //            on the Lazy init). This build applies the 160.20 srv2-gate hunks (init/discovery/
        //            routing/Status2) onto the 160.22 controller; per-end (Wisdom.cs CauseToAttributesPerEnd
        //            + PickTagByAttid) and unknown-reclass carried verbatim. No new behaviour beyond the
        //            three merged changes. Deploy DLL + Index to ALL web instances.
        // 1.0.160.22 - UNKNOWN-CAUSE RECLASSIFICATION. The 4 PT FAIL UNKNOWN causes (208/209/253/254)
        //            in _pmCauseTags fetched RELAYS ONLY, so a mis-tagged alert could never be pinned
        //            to its real cause. Now they also fetch the end's operation values (110V, current,
        //            op-time via the EdgeX-op attids -> RDPMS range via PmRangeAttidFor), and the
        //            prefetch injects an "[UNKNOWN-CAUSE RECLASSIFICATION]" note telling the model to
        //            name the real cause when one operation value clearly breaches (110V low -> VOLT/
        //            CURR LOW/FAIL, op-time high -> OBS) or keep UNKNOWN if the operation is clean.
        //            NOTE: the ops-tag=EdgeX / range=RDPMS / per-end mapping this builds on was ALREADY
        //            correct (PmTag templates + _pmValueToRangeAttid + _pmCauseTags) -- this only fills
        //            the UNKNOWN operation-fetch gap. Built on the 160.21 line; apply onto 160.20 trunk.
        // 1.0.160.21 - PER-END ATTID ANCHOR for the 14 PM NWKR/RWKR causes. Their FRS attributes are
        //            stored PER-END (A/B); generic-name resolution (VPT NWKR -> rollup 890) anchored the
        //            grounding tag on a rollup with no per-incidence samples -> "coverage gap" false
        //            INCONCLUSIVE (e.g. PT-1 PT VOLT LOW AT NWKR, card "A End NWKR V"=18.63 = attid 25).
        //            FIX: new map CauseLogicMaps.CauseToAttributesPerEnd (cause -> per-end attids,
        //            ALERTING attribute first) + helper PickTagByAttid; PickAlertingTagId now resolves
        //            these causes by AssetAttributeId (exact attid) BEFORE name scoring -- immune to the
        //            VPT110/VPT 110 name-spelling inconsistency. OP variants share the SAME indication
        //            attids as their non-OP twin (operation V/I is a separate EdgeX-op namespace, not
        //            anchored here). No other logic change; Index unchanged.
        //            NOTE: built on the 160.19 line in this workspace (the 160.20 srv2-gate is NOT here
        //            -- apply this delta onto the 160.20 trunk to get 160.21).
        // 1.0.160.20 - srv2 (MCP2) ENABLED ONLY WHEN McpServerUrl2 IS CONFIGURED (non-blank). Owner rule:
        //            URL present = srv2 on (unchanged); absent/blank = srv2 off, cleanly and SILENTLY.
        //            New static _srv2Enabled (IsNullOrWhiteSpace). When off: discovery SKIPPED so tools2
        //            stays empty -> no srv2 tool enters the model tool list / _toolOwner, nothing routes
        //            to owner 2. Error2 left NULL when disabled (do NOT set it) so no [MCP2 error] is
        //            logged/surfaced AND the srv1 discovery cache is not invalidated. Routing guards
        //            owner==2 with _srv2Enabled; Status2 reports disabled; Lazy hardened w/ Trim. Default
        //            byte-identical when URL set; reversible (repopulate the URL). [merged here in 160.23]
        // 1.0.160.19 - COMPILE FIX for the map relocation. 160.16-160.18 kept the maps as a second
        //            `partial class RdpmsMaps`, which BREAKS if gen_rdpms_maps.py regenerates
        //            RdpmsMaps.cs (the generator emits a NON-partial `class RdpmsMaps`, so the two
        //            declarations mismatch and the generated members - CauseToAttributes, HealthBands,
        //            AliasToTitle, HealthBand - appear "missing"). FIX: the three maps now live in
        //            their OWN class `CauseLogicMaps` (still in AiChatController.Wisdom.cs, no csproj
        //            change), and RdpmsMaps.cs is back to the plain generated `class RdpmsMaps` (no
        //            `partial`). So RdpmsMaps.cs == exactly what the generator emits (regeneration is
        //            now safe and needs no edit), and the cause-logic maps are fully decoupled from it.
        //            Call sites updated: RdpmsMaps.CauseToLogic/DerivedFormula/CauseToDerived ->
        //            CauseLogicMaps.*. No behaviour change; W6/W7 wisdom unchanged; Index unchanged.
        // 1.0.160.18 - W7 FIELD WISDOM (general): the ALERT CARD is authoritative for the incidence
        //            value. A triggered condition with status FAIL against MinSafe/MaxSafe is a
        //            CONFIRMED breach even when history_get for that attribute is unavailable/empty --
        //            the card's measured value IS the incidence sample; judge transient vs sustained
        //            from the RESET card value; anchor verdict+viz on the FAILED condition, not a
        //            PASSED sibling. Stops the "coverage gap" false INCONCLUSIVE when the card already
        //            carries the failing value. Fixes 466798 (SIG RG VOLT/CURR LOW: card shows
        //            RG mA = 97.14 < MinSafe 110 FAIL, recovered to 124.10 at reset = transient, but
        //            the model went INCONCLUSIVE citing "RG mA history unavailable" and showed the
        //            PASSING RG V). Prompt-only. Controller logic otherwise unchanged; Index unchanged.
        // 1.0.160.17 - Re-cut of 160.16 to avoid a csproj change. Same two changes, but the
        //            cause-logic maps (CauseToLogic / DerivedFormula / CauseToDerived) now live in
        //            AiChatController.Wisdom.cs (an ALREADY-compiled file) as `partial class RdpmsMaps`
        //            instead of a new standalone RdpmsMaps.CauseLogic.cs -> NO new csproj Compile
        //            entry needed. Still protected from gen_rdpms_maps.py regeneration (they are no
        //            longer in the generated RdpmsMaps.cs, which stays `partial`). Call sites
        //            unchanged. W6 field-wisdom (below) is retained as-is.
        // 1.0.160.16 - (a) MAP SPLIT (superseded by 160.17: maps moved to Wisdom.cs, not a new file).
        //            (b) W6 FIELD WISDOM (gated to derived causes via CauseLogicMaps.CauseToDerived): a HELD
        //            (deadband) component in a derived formula is VALID at the incidence, not stale;
        //            when one component moved fresh and the other is held, the derived breach is REAL
        //            - judge on the term that moved, do not call INCONCLUSIVE for the held term's age.
        //            Fixes the 506089 false INCONCLUSIVE (TC BT CHG CURR LOW: Charger mA collapse real,
        //            If mA held, ITC BATT CHARG = -281 matched the card yet was downgraded as "stale").
        // 1.0.160.15 - DERIVED TR_V CORRECTION. RRAIL / RTC-CH-RELAY-END / VTC-CH-RELAY-END use the
        //            DERIVED TR V (attid 571) = Ir * R737 / 1000, NOT the measured 571 sensor (the
        //            571 sensor row is written but forbidden as a calc input). R737 (attid 737, Track
        //            Relay res) is read from the FRS Range API for the AssetId - the SAME asset-scoped
        //            range fetch that already supplies Min/MaxSafe, so no config/SQL wiring and no
        //            sensor fallback. DerivedFormula entries 589/590/591 updated accordingly; the
        //            other derived causes (585/586/587/588/684) unchanged. Map-text only.
        // 1.0.160.14 - DERIVED-VALUE FORMULAS mapped per cause. New RdpmsMaps maps: DerivedFormula
        //            (derived attid -> how it is computed from base TPR-gated sensors) and
        //            CauseToDerived (the 15 TRACK causes that test a derived attribute -> its attid).
        //            Prefetch now injects, for a derived cause, the compute formula (e.g.
        //            RRAIL = 2*(Vf - TR_V571)/(If+Ir)*1000) + the base-sensor legend, so the derived
        //            value is computed from components already on srv1 instead of needing a
        //            (non-existent) derived telemetry tool. 12 of 15 compute from base sensors alone;
        //            RRAIL + CH-RELAY-END use TR_V571 = Ir*R737/1000 with a documented fallback to the
        //            measured 571 sensor when R737 (fixed SQL) is unavailable. Additive; derived
        //            causes only; pairs with CauseToLogic (160.13). Controller-only; Index unchanged.
        // 1.0.160.13 - EMBED FRS CAUSE LOGIC / DROP srv2 analyse_alert. The cause pass/fail rule
        //            (formerly analyse_alert.cause_code_def from srv2) is now a local map:
        //            CauseLogicMaps.CauseToLogic, generated verbatim from the RDPMS master
        //            (RDSO/SPN/257/2025) - all 151 causes (PM 26, TC 26, SIG 53, IPS 46), keyed
        //            by the exact master cause names. The prefetch no longer calls analyse_alert;
        //            it looks up CauseToLogic[causeCode] and injects the rule as evidence, plus a
        //            one-line threshold-resolution note (avg x LD% -> MinSafe/MinFail fallback).
        //            Fixes the analyse_alert cause-name mismatch (missed 9 of 11 PM causes) at the
        //            source and removes a slow srv2 round-trip. Thresholds still come from the live
        //            range fetch (get_attribute_range + PmRangeAttidFor). get_frs_alerts and
        //            get_hist_realtime remain on srv2, unchanged. Controller-only; Index unchanged.
        // 1.0.160.12 - COVERAGE ANCHOR FIX. PickAlertingTagId anchored the coverage/grounding tag
        //            on triggeredConditions[0] unconditionally. For a multi-condition AND rule where
        //            a LATER condition FAILED, [0] is the wrong attribute -- the gate coverage-
        //            checked a PASSING parameter, found it stale, and spuriously downgraded to
        //            INCONCLUSIVE while the failing (deciding) attribute went unfetched. Live 505259
        //            TC TR RELAY DEFECT: [0] ITC RELAY END PASSED (345>=210), [1] VTC 24 DC LOC
        //            FAILED (0.00<16.51) = the fault; gate anchored on Ir mA and downgraded. Now
        //            anchors on the condition whose threshold FAILED (skips type=="ips"), falling
        //            back to [0] when none failed, so single-condition alerts are unchanged. One
        //            method + one static helper (TriggeredConditionFailed). PM/IPS attid fast-paths
        //            above untouched. (160.11 was the QUANTUM Contains fix; 160.10 the merge.)
        // 1.0.160.10 - UNIFIED TRUNK. Merges two divergent lines that both forked from 1.0.160.4:
        //            LINE A (push/concurrency): 1.0.160.6 verdict concurrency (config-driven
        //            admission wait-queue) + 1.0.160.7 push notification block + 1.0.160.9
        //            QUANTUM/NEXUS/ORION brand-label fix in ProviderKind.
        //            LINE B (PM/range): 1.0.160.6 get_attribute_range AssetId-scope +
        //            StripTagWaveforms + tool-trace false-"failed" fix (Index 55.36) +
        //            1.0.160.7 PmRangeAttidFor value->range attid map + 1.0.160.8 PM
        //            search_tags-by-name RESOLUTION NOTE. Changes sit in different methods/
        //            regions - additive merge, no logic overlap. Numbered 160.10 to supersede
        //            both 160.8 (Line B) and 160.9 (Line A) with no collision. See CHANGELOG.
        // 1.0.160.9 - QUANTUM->anthropic bug: app/API sends BRAND labels (QUANTUM/NEXUS/ORION),
        //            not provider values; a bare "quantum" fell through ProviderKind to the
        //            final "anthropic" and ran on the Anthropic key. Fixed by mapping the bare
        //            brands after the numbered-variant checks (base model, overrides untouched).
        // 1.0.160.8 - Point-machine search_tags-by-name RESOLUTION NOTE (prompt-only).
        // 1.0.160.7 - PUSH block. verdictObj now carries a "push" section for the notification
        //            dispatcher, assembled SERVER-SIDE from already-sanitized verdict fields
        //            (zero extra model tokens/cost, deterministic, hard char caps so it can
        //            never overflow a notification). Two shapes: type1 {title, body} = a
        //            STANDALONE AI summary card that names the alert (for "send alert first,
        //            AI review follows"); type2 {header, footer} = verdict SPLIT for appending
        //            to the original message (header = punchline that survives the collapsed
        //            view, footer = evidence+action+caveat on expand). Hinglish twins
        //            type1_hi/type2_hi from the hi{} block. meta{verdict,confidence,asset,
        //            station,causeCode,colour} for the app to build its own layout. Decline-
        //            safe: when recommended_action is a controller-decision decline the push
        //            carries NO fabricated action (uses the caveat instead). Word-safe
        //            truncation never cuts mid-number. Attached right after hinglish, so it
        //            inherits every sanitisation. JSON verdict path only.
        // 1.0.160.6 - Verdict CONCURRENCY: max concurrent verdicts 2 -> 5, and an over-limit
        //            request now WAITS for a slot instead of being rejected instantly. BOTH
        //            admission gates had to move: the _jsonGate semaphore AND the
        //            _jsonBgReserved/_jsonBgOpCap reservation cap were each pinned at 2
        //            (8 / _jsonMaxChunk[4] = 2), so raising only the semaphore would have left
        //            the reservation check bouncing the 3rd request. The cap is now DERIVED from
        //            the concurrency setting plus a queue depth, so the two can never disagree --
        //            and because a queued request HOLDS its reservation while waiting, that
        //            queue headroom is what lets waiters wait at all (a cap of exactly
        //            concurrency*chunk would bounce every waiter before it reached the wait).
        //            .Wait(0) -> await WaitAsync(budget, clientDisconnectedToken): the request
        //            thread is not blocked while queued and a closed tab abandons the wait.
        //            Config: AnalyzeMaxConcurrentVerdicts (5), AnalyzeJsonQueueDepth (5),
        //            AnalyzeJsonAdmitWaitMs (25000), AnalyzeJsonBgOpCap (default derived).
        //            Verdict path only -- streaming/chat/follow-up stay unserialized. NOTE:
        //            5 concurrent = up to 5 simultaneous calls on ONE provider key; if that
        //            key's rate limit is lower you get provider 429s instead of 503 busy.
        // 1.0.152.3 - Derived HERO line on the trend chart. For a derived alert (RRAIL etc.)
        //            the chart was plotting the drivers but not the derived value itself. Now
        //            SynthDerivedSeries reconstructs the derived series per-timestamp from the
        //            driver series via the card formula (TryEvalFormula, nearest-sample bind
        //            within DerivedSynthTolSec) and streams it FIRST/isDerived so the chart
        //            leads with the hero. Drivers still shown. Client draws each series in its
        //            own cell on one auto-fitting row.
        // 1.0.152.2 - MERGED another team's three PM/IPS mapping fixes (owner-confirmed) onto
        //            the train-mask line: (1) four ...OP RELAY causes remapped from operation
        //            voltage/current attids to INDICATION + datalogger relay (VPT/NWKR/RWKR/
        //            NWCR/RWCR) -- these are indication/relay events, not operation; (2) PmNWCR
        //            alt 284 removed (resolves on 282 only); (3) search_tags name-narrowing now
        //            suppressed for PM/IPS too (their tags are named by number, so the name
        //            filter starved the attid resolver -- proven on asset 41898). No overlap
        //            with the 152.1 train-mask work.
        // 1.0.152.1 - Train-mask correctness (external review P0s). Mask now runs on the RAW
        //            series (not the <=240 chart points) so TPR transitions/short occupations
        //            are not lost; DERIVED alerts (RRAIL) emit driver series with NO invented
        //            derived threshold; refThreshold comes only from the alert card (else
        //            omitted, source labelled) -- dropped the trimmed-mean*0.5 guess; the
        //            digital TPR STATE tag is resolved explicitly (0/1 values), not any "TPR"
        //            substring; TPR is FORCE-FETCHED for track assets; and the train-free
        //            reference is fed into the COMPUTED TREND block the model reads (model
        //            input DOES change now -- may improve the verdict). Frame schema:2 with a
        //            mask{status} block and per-series reference. Config AnalyzeTrmMinSamples.
        // 1.0.152.0 - Multi-attribute trend chart data + deterministic train mask. Capture
        //            EVERY attribute series (not just the trigger) into _trendSeriesByAttr;
        //            the trend SSE frame now carries series:[{attr,points,isTrigger,isTpr}]
        //            plus, when a TPR series was captured, a TRAIN-FREE refValue/refThreshold
        //            for the trigger computed in-controller (median/trimmed-mean over samples
        //            outside +/-AnalyzeTrmExcludeSec of TPR transitions and TPR-dropped spans)
        //            so the chart no longer shows a train-polluted 0 threshold. Model input
        //            and verdict unchanged; SSE-to-browser only, zero extra model tokens.
        // 1.0.151.3 - Accuracy of the computed-trend heading (external-review P1): when
        //            more than 240 samples were fetched, the heading said "from 240
        //            fetched samples" though 240 is the downsampled-for-stats count. Now
        //            reads "stats from 240 sampled points out of N fetched" using the true
        //            fetched count in _trendCandidate["n"]. Wording only.
        // 1.0.151.2 - Implementation fixes from an external code review (valid ones only):
        //            (1) BuildPointIndicationNote built FROM _pointIndication (was dead
        //            data + a separate hardcode that could drift); (2) per-day trend keyed
        //            yyyyMMdd and sorted so first/last-day avg is truly chronological;
        //            (3) downsample keeps the true last sample (round(i*(n-1)/(cap-1)))
        //            so last-reading/staleness stay correct; (4) UI copies assetType into
        //            the alert context (Index) so Signal alerts always get the checklist.
        // 1.0.151.1 - POINT INDICATION added (was missing from the 151.0 checklist):
        //            ported the Debouncer CR/KR indication model -- NWKR=Normal proved,
        //            RWKR=Reverse proved (NWCR/RWCR = commanded). PM checklist now states
        //            which indication is proved, whether it matches the commanded end, and
        //            averages only the proved-indication end. Prompt/checklist only.
        // 1.0.151.0 - Averaging discipline from the tested Debouncer: (TRACK) averages/
        //            thresholds EXCLUDE train movement -- ignore samples within +/-2 min
        //            of any TPR transition and while TPR dropped. (SIGNAL) average only
        //            aspect-ACTIVE samples; ported the Debouncer ECR proof map (RG<-RECR,
        //            HG<-HR+HECR, DG<-DR+DECR, HHG<-HR+HHR+HHECR, SH_OFF<-HR+OFFECR) into
        //            the prompt. (POINT) average only commanded-end operation samples.
        //            Prompt/checklist layer; no schema/tool/verdict change.
        // 1.0.150.1 - Compile fix: BuildComputedTrendBlock (added in 150) declared a
        //            StringBuilder "b" in the same method scope as the series.Sort
        //            comparison delegate's parameter "b" (CS0136). Renamed to sbTrend.
        //            No behavior change from 1.0.150.0.
        // 1.0.150.0 - (1) Trend harvest understands E7 PARALLEL-ARRAY history shape
        //            ({"ts":[epochSec..],"v":[..]}) with epoch-sec/ms -> local ISO --
        //            root cause of "no series captured" / old-style chart. (2) Tabular
        //            tool-output rule in the follow-up prompt: never guess column
        //            identity (the TPRV-vs-ChargerOPV chat mislabel). Index 1.0.50:
        //            3D verdict card + glossy footer buttons (cosmetic).
        // 1.0.149.0 - Prefetch v2 (controller-side bundle, per user direction: no MCP
        //            change): universal per-call CHECK with one auto-retry inside
        //            PrefetchCallAsync; SECTION STATUS truth table in the evidence;
        //            deterministic COMPUTED TREND & SENSOR HEALTH block (per-day
        //            min/avg/max + direction, stale age, flatline run, 4-sigma spikes)
        //            from the harvested trigger series. Plus DownloadAiLogs endpoint
        //            (Wisdom.cs) zipping the day's ai-log/analyze/tool/chat for sharing.
        //            Deferred (documented): SIGNAL V/I companion fetch, sequential flag.
        // 1.0.148.0 - Trend-capture fixes: (1) unwrap MCP string-wrapped payloads
        //            ({"result":{"content":[{"text":"<json>"}]}}) so the series walk
        //            reaches the real arrays; (2) [TREND] ai-log lines on harvest and
        //            on no-capture, so a fallback chart is diagnosable. Index 1.0.48:
        //            E7 "HH:mm:ss/dd.MM.yyyy" timestamps normalized for DATA POINTS
        //            (previously only ALERT/RESET markers), fixing silent fallback.
        // 1.0.147.0 - Per-asset-type ANALYSIS CHECKLIST (owner-defined): TRACK (shorting,
        //            weather, degradation-vs-jitter-vs-fluctuation MANDATORY shape call,
        //            stats, 3-day trigger trend, past alerts, derived components, ML
        //            correlation), SIGNAL (V+I both, stats, 3-day trend, active threshold,
        //            alerts, sensor-health line), POINT MACHINE (predictive: op-history
        //            trend, alert hist, own-signature compare, averages; failure: fired
        //            condition + past alerts). BuildAssetChecklist injected beside the
        //            wisdom block; "address EVERY item, say ABSENT" rule. Prompt-only:
        //            no schema/tool/verdict/coverage change; AnalyzeWindowDays already 3.
        // 1.0.146.0 - MERGED RELEASE (three lines united): (a) Team A 1.0.143/144 --
        //            PM alerting-tag fast-path + IPS attid resolution re-applied, plus
        //            IPS-only any-tag fallback (PickAnyTagIdForAsset). (b) Team B
        //            1.0.143/145 -- wisdom auth via portal roles (LoginUser.RoleId /
        //            IsSiteKeeping) with reviewer/approver split + WhoAmI displayName
        //            (Wisdom.cs taken wholesale). (c) Trend line -- TryHarvestTrend +
        //            grounding at the CallToolRoutedAsync funnel, "trend" SSE frame;
        //            Index 1.0.47.0 plots real telemetry with ALERT/RESET markers.
        // 1.0.142.0 - Grounding carry-forward: ids (SiteId/AssetId) resolved by tools
        //            during the initial analysis (alert record, searches) are harvested
        //            into a per-alert cache and merged into FOLLOW-UP CHAT context, so
        //            chat inherits identity instead of re-resolving (stops zero-id
        //            refusal loops + saves tool budget on thin-context alerts).
        // 1.0.141.0 - Manage page chip drill-down: asset-type chips -> cause-code chips
        //            -> rules; "All assets"/"All causes" at each level. Wisdom.cs
        //            template only; endpoints unchanged.
        // 1.0.140.0 - Wisdom Manage page regrouped: ASSET TYPE first, cause code inside,
        //            rules latest-first (was cause-only groups, pending-first). Wisdom.cs
        //            template only; endpoints unchanged.
        // 1.0.139.0 - Wisdom UX fixes (Wisdom.cs): (1) all 400/409/422 rejections now
        //            return JSON {ok:false,error} + TrySkipIisCustomErrors -- the IIS
        //            custom-error module was replacing 422 bodies ("does not recognize
        //            this error"), hiding validation reasons like the rule word-limit.
        //            (2) ListWisdom gains optional causeCode/siteId scope params; the
        //            popup panel (Index 1.0.40.0) is scoped to the current alert.
        // 1.0.138.0 - Confidence fraction-scale fix: DeepSeek emits confidence 0-1
        //            (0.85 = 85%); the 136 coercion rounded those to 0/1%, making its
        //            verdicts look worthless. Decimal values <= 1.0 now scale x100.
        // 1.0.137.0 - Rejected-verdict capture: when the popup verdict fails JSON parse
        //            or validation, the raw model reply (redacted, 1500-char clamp, one
        //            line) is logged to ai-log so provider A/B failures are diagnosable.
        // 1.0.136.0 - Verdict validation: coerce numeric confidence (72.0 / "72.5" ->
        //            round+clamp) instead of rejecting the whole verdict. DeepSeek flash
        //            emits floats where the schema says integer; Sonnet unaffected.
        // 1.0.135.0 - DeepSeek latency fix: V4 models run thinking mode ON by default
        //            (hidden 30-80s reasoning billed as output) -- that, not the model
        //            choice, made QUANTUM take 43-92s. DeepSeekClient now always sends
        //            {"thinking":{"type":"disabled"}} unless DeepSeekThinking=true.
        // 1.0.134.0 - DeepSeek provider added (new DeepSeekClient.cs, OpenAI-compatible).
        //            Three selectable models, white-labeled in the UI: ENERGY7 ULTRA =
        //            Anthropic, ENERGY7 TESLA 1 = OpenAI, ENERGY7 TESLA 2 = DeepSeek.
        //            ProviderKind resolves anthropic|openai|deepseek; CallModelOnceAsync
        //            dispatches to the third client. Ask + Re-analyze now offer a model
        //            picker (Index 1.0.36.0). New DeepSeek* appSettings; default provider
        //            unchanged (AiProvider).
        // 1.0.133.0 - Learning loop + footer indicator. (1) Popup emits a "stats" SSE
        //            frame (tokens in/out, cache, model/tool/discovery ms) -> Index 1.0.35
        //            shows a small token+time line under the verdict. (2) ai-analyze
        //            request_end now records the verdict summary + causeCode/siteId/station
        //            + tokens. (3) follow-up chat now logs the ASSISTANT turn too
        //            (RunAgenticLoop fills _assistantCapture; AnalyzeChat writes it paired
        //            with the user turn by chatCallId). (4) new ListCandidates derives
        //            promotable candidates from the date-wise logs; the management page
        //            (1.0.35) Candidates tab promotes one into PENDING wisdom via SaveWisdom.
        // 1.0.132.0 - Wisdom management page + bulk scope (AiChatController.Wisdom.cs).
        //            New [HttpGet] WisdomManage serves a self-contained cause-code-
        //            grouped management UI (status inside each cause; approve / reject /
        //            edit rule+scope / view original correction). New [HttpPost]
        //            BulkScopeWisdom applies a scope change to all PENDING rules under a
        //            cause code (approver-gated). Index 1.0.34.0 links to it. No change
        //            to analysis or existing wisdom endpoints.
        // 1.0.131.0 - WisdomDefaultIdentity (e.g. "ENERGY7"): optional last-resort
        //            shared identity when neither ASP.NET auth nor the session key
        //            yields a name. Resolve order: User.Identity -> Session[
        //            WisdomIdentitySessionKey] -> WisdomDefaultIdentity -> deny.
        //            Off by default. Shared identity = no per-user audit trail.
        // 1.0.130.0 - Wisdom identity via app session. ai-log showed "denied
        //            identity=<none>": the portal has NO ASP.NET authentication, so
        //            User.Identity is anonymous for everyone and the fail-closed
        //            wisdom auth can never pass. ResolveWisdomIdentity now falls back
        //            to Session[<WisdomIdentitySessionKey>] (the portal login user);
        //            inert until that key is configured. WisdomDenied returns JSON
        //            with the resolved identity + TrySkipIisCustomErrors so the panel
        //            shows the precise reason instead of the IIS 403 HTML page.
        // 1.0.129.0 - Wisdom endpoint hardening (AiChatController.Wisdom.cs): all five
        //            wisdom endpoints catch-all -> real exception logged to ai-log
        //            (WISDOM tag, full type+stack clamped) and clean JSON {ok:false,
        //            error} returned with TrySkipIisCustomErrors, instead of the
        //            ASP.NET Runtime Error HTML page (masked by customErrors).
        //            Index_15 1.0.33.0 renders errors as a short message. No
        //            functional change to save/approve/inject.
        // 1.0.128.0 - Endpoint resilience: CallModelAsync retries a TRANSIENT model failure once
        //            (config AnalyzeModelRetries) before returning model_error -- kills most
        //            transient model_error/HTTP-200-not-usable. Retry runs inside _aiLock (no
        //            re-acquire), skips ClientBusyException/cancellation, and is budget-capped so a
        //            slow failure never doubles latency. Pairs with raising AnalyzeJsonClientLockMs
        //            (20000->45000) + caller-side timeout/retry (see verdict-caller hardening doc).
        // 1.0.127.0 - Reunify: WISDOM WhoAmI (in-panel identity, from the wisdom lineage) added
        //            ONTO the 1.0.126 merge (which combined the unified point-cause tag table with
        //            the 1.0.125 Wisdom feature). Also RESTORES two build fixes the merge dropped
        //            from AiChatController.Wisdom.cs: vreason definite-assignment and System.IO.File
        //            qualification (Controller.File shadow). Point-machine code unchanged; Index 1.0.32.
        // 1.0.125.0 - WISDOM self-learning feature (new partial AiChatController.Wisdom.cs).
        //            Teach a correction in the diagnostic chat -> PENDING wisdom -> review /
        //            approve -> APPROVED rules injected as advisory domain guidance for
        //            matching cause/asset/site, ALWAYS subordinate to measured evidence and
        //            the verdict/safety rules. Authorized + anti-forgery endpoints, event-
        //            sourced store, isolated distillation client, redacted chat log. Gated by
        //            EnableWisdom (default off). Injection hooks below (analysis + follow-up)
        //            + chat-log hook; the 1.0.124 probe is retained until identity is captured.
        //            Requires Index_15 1.0.31 and the new Web.config keys.
        // 1.0.124.0 - Identity probe (merge onto the PM 1.0.120-1.0.123 mainline). TEMPORARY
        //            GET /AiChat/WisdomAuthProbe (WisdomAuthProbe key) logs User.Identity
        //            (auth status/type/name/principalType/identityType) on each authenticated
        //            hit while key on, to capture the deployment identity for upcoming Wisdom
        //            auth. 404 when key off; nothing logged for anonymous. + fail-closed
        //            ResolveWisdomIdentity() helper (unused here; reused by the Wisdom build).
        //            Additive; zero overlap with the point-machine prefetch. Remove in Wisdom.
        // 1.0.109.0 - DERIVED-VALUE TRANSPARENCY. For a derived alert (RRAIL, IBALST, RTC*,
        //            ITC BATT CHARG) the verdict now carries a server-computed "derived_inputs":
        //            { param, formula, value, inputs:[{name,value,baseline,driftPct,drove}] }.
        //            Built in the component loop from data ALREADY fetched - each component's
        //            as-of value and its history mean (baseline); driftPct = (value-baseline)/
        //            |baseline|*100; the largest |driftPct| is marked drove=true (the raw input
        //            that moved the derived value). SERVER-COMPUTED so the numbers are
        //            authoritative, not model-transcribed. Attached AFTER ValidateAndSanitize
        //            Verdict on BOTH paths (JSON endpoint + SSE popup); it is pure structured
        //            numeric data (no free text), so no operational-directive surface. Null for
        //            non-derived alerts. New SeriesMean helper (mean of columnar {ts,v}). The
        //            card (Index_15 1.0.25) renders the inputs table + a static cause-chain
        //            diagram (input drifted -> derived moved -> alert) and a weather placeholder.
        // 1.0.123.0 - Point TIME-cause prefetch now fetches the THREE tags the cause actually
        //            needs (owner spec), not just op-time. PT R TIME HIGH (472856) stayed
        //            INCONCLUSIVE because the two PROVING RELAYS were never fetched: op-time high
        //            alone cannot confirm - you need WCR (throw ran, op<=12s) + WKR (completed
        //            <=2s) to prove a real operation. Now: PmEndFromAlert resolves the END (A/B)
        //            from the paramLabel/description (card shows "A TPT R" -> A); PmTimeCauseTags
        //            returns op-time(END) + WCR + WKR - Reverse 6005/8005 + RWCR 283 + RWKR 268,
        //            Normal 1005/3005 + NWCR 282 + NWKR 267. All resolved via search_tags ->
        //            TagId -> history_get (relays are in search_tags too). End unknown -> both
        //            ends op-time + relays. VOLT/CURR/OBS causes keep the map-based op attids.
        // 1.0.122.0 - Fix the point op-attid -> TagId resolver field names (this is why PT R TIME
        //            HIGH 472856 still went INCONCLUSIVE - "op-time sample not captured"). The
        //            search_tags record exposes the op attid as EdgeXAttributeId / AssetAttributeId
        //            (= 6005) with TagID; the resolver matched only "AssetAttributeId" and (in one
        //            path) the wrong "AttributeId" name. Now matches AssetAttributeId OR
        //            EdgeXAttributeId OR AttributeId, keeping TagID. Verified against the real
        //            search_tags record: 6005 -> TagID resolves across bare-array / Data-wrapped /
        //            MCP content-text shapes. No new calls - the existing search_tags prefetch
        //            carries these records; only the field match was wrong.
        // 1.0.121.0 - Generalized the point operation prefetch to ALL 12 point causes whose
        //            evidence is OPERATION data (1.0.120 handled only PT * TIME *). Replaced the
        //            TIME-only pattern-match with _pmCauseOpAttids, a cause -> EdgeX op attid
        //            table generated from RDPMS_cause_code_attid_map (the "EdgeX op attid"
        //            column): PT N/R TIME HIGH, PT N/R VOLT/CURR LOW, PT N/R VOLT/CURR FAIL,
        //            PT N/R OBS, PT VOLT FAIL AT N/RWKR OP, PT N/RWKR RELAY DEFECT OP. Each maps
        //            to its exact op attids (current/voltage/time, A & B end). The other 14 point
        //            causes (indication-voltage / detection-relay / UNKNOWN) return no op attids
        //            and stay on the DB/RDPMS attid path. Same option-A resolution (op attid ->
        //            TagId via search_tags -> history_get). Evidence wording generalized to cover
        //            current/voltage as well as time. Authoritative (reads the map, not inferred).
        // 1.0.120.0 - POINT OPERATION-TIME prefetch (fixes PT * TIME * -> INCONCLUSIVE/Coverage-
        //            gap, seen on real alert 472856 PT R TIME HIGH). These alerts turn on the
        //            per-operation TIME (+ current) series on the EdgeX operation attids, but the
        //            controller left the op-attid -> TagId -> history_get chain to the model,
        //            which did not complete it in the prefetch window. Now resolved server-side:
        //            PmOpTimeAttidsFor maps the cause (N/R) to the op attids (6005/8005 op-time +
        //            6002/8002 current) via the existing PmOpAttid mapper; PickTagIdByAttrId finds
        //            their TagIds by numeric AssetAttributeId in the ALREADY-fetched search_tags
        //            result (option A - get_asset_tags carries no TagId); history_get each; inject
        //            as evidence with the logic check (op-time vs 150% avg; high+current=binding,
        //            high+current~0=sensor artifact; >6000ms=obstruction; A-vs-B localize).
        //            Honors the owner ID rule (operation attrs -> EdgeX op attids). Reuses the
        //            existing search_tags prefetch - no extra search call. Best-effort throughout.
        // 1.0.119.0 - Weather now shows on the CARD, not just in the evidence. 1.0.117 injected
        //            weather as evidence text (the model used it - real card 467979 cited "rain
        //            4.3 mm"), but the card WEATHER tile reads a structured v.weather object that
        //            was never populated, so it still showed "pending". Now the fetched weather is
        //            reshaped (BuildWeatherCard) into the card contract {condition, tempC, summary}
        //            and attached to the verdict server-side at both paths (weather is a server-
        //            only key - stripped from model output, re-added after validation). On 467979
        //            the tile now reads "Rain / 25.8C / 4.3 mm around alert".
        // 1.0.118.0 - DryRunTools get_frs_alerts args fixed against the MCP1 tools/list. The
        //            debug tool-health probe called get_frs_alerts with a singular NewArgs(
        //            "SiteId",...) scalar, but the tool declares SiteIds/AssetIds as ARRAYS
        //            (required: SiteIds) - so the probe sent an unknown arg and omitted the
        //            required one, and failed. Now uses NewFrsAlertsProbeArgs (correct array
        //            shape). The VERDICT path (NewSiteAlertArgs) was already correct. Audited the
        //            other DryRunTools MCP1 args (get_asset_tags/history_get/get_attribute_range)
        //            against the tools/list - all match. MCP2 (FRS) tools (analyse_alert,
        //            get_hist_realtime, fetch_alert_details_frs) are on the OTHER server and were
        //            not in this list - not verified here (no MCP2 tools/list available yet).
        // 1.0.117.0 - Weather via Option A (controller-side, NOT an MCP tool) + get_attribute_
        //            range_history integration. (1) get_weather REMOVED as an MCP tool: weather
        //            is now fetched server-side from Open-Meteo during prefetch, gated on weather-
        //            relevant cause codes, and injected as a [WEATHER ...] evidence block - so no
        //            third-party API sits in the EdgeX MCP. (2) NEW tool get_attribute_range_
        //            history wired (per the MCP handoff): asset-scoped historical AVG trend + site
        //            geo/topology (lat/long/familyTracks/signalTrack). Prefetched + injected as
        //            evidence, and the SOURCE of the site lat/long weather needs. (3) The three
        //            range/history tools are COMPLEMENTARY, none trimmed: get_attribute_range =
        //            LIVE/current normal; get_attribute_range_history = historical trend + geo;
        //            history_get = precise timestamped samples (as-of/derived/coverage need these).
        //            Weather is best-effort (egress/timeout -> no block, model never invents it)
        //            and gated by appSettings WeatherEnrichment (default on). Routing for the new
        //            tool is auto-discovered from ListTools - no hardcoded route.
        // 1.0.116.0 - No controller logic change. Paired with Index_15 1.0.30, where the debug
        //            "Debug tools" button visibility is now rendered from the SAME EnableDryRunTools
        //            appSetting that gates this DryRunTools endpoint - so ONE Web.config switch
        //            controls both. Version bumped to keep controller+UI in lockstep.
        // 1.0.115.0 - Follow-up chat fixes (from a real 03AT session where the thread lost the
        //            alert and asked the user for a SiteId it already had). (1) Narrowed the
        //            out-of-scope keyword list: removed "translate"/"weather"/"email"/"math"/
        //            "news"/"time in" etc. that substring-matched legitimate railway follow-ups
        //            (a Hindi request, a weather question, "email the report", "estimate").
        //            (2) Softened the refusal from the untrue "I am not trained to answer that"
        //            to a warm redirect that keeps the door open. (3) Reinforced in the follow-up
        //            prompt that the alert SiteId/AssetId/station are known for the WHOLE
        //            conversation - never ask the user to re-supply them, and use the SiteId to
        //            look up adjacent tracks. Context was always SENT every turn; the model was
        //            ignoring it after an off-topic detour. No change to the verdict path.
        // 1.0.114.0 - Derived-panel null/staleness fix (from card 399305: TR V (RELAY) read 0,
        //            window mean 2.364, was tagged the LARGEST DEVIATION at -100%). A component
        //            whose as-of value is exactly 0 against a clearly non-zero window mean is a
        //            MISSING sample encoded as zero, not a reading: it is now excluded from the
        //            largest-deviation pick and flagged noReading (the card shows "no reading").
        //            Each component now also carries its staleness age (ageSec), and when any
        //            formula input is null the derived block is marked inputSuspect so the card
        //            renders the system-computed value as INDICATIVE only - explaining why it can
        //            diverge from the alert-card value (a null propagates into the formula).
        //            Does NOT change how components are fetched; the deeper as-of/incidence lag
        //            remains a separate item. Index_15 -> 1.0.29.
        // 1.0.113.0 - Prompt-cache 1-HOUR TTL wiring (cost). Adds AnthropicPromptCache1h flag
        //            (default OFF): when the cache is on AND alerts are spread out (gaps > 5
        //            min), the default 5-min entry expires between verdicts and nets ~0 saving,
        //            so the 1-hour TTL is what actually produces cache reads at this cadence.
        //            Passes extendedCacheTtl to AnthropicClient 1.0.2.0+ (5-arg overload); on an
        //            older Domain.dll it falls back to the 4-arg (5-min) or 3-arg call, so this
        //            is deploy-order safe. No behaviour change unless both flags are set.
        // 1.0.112.0 - Follow-up review of 1.0.111 (core delta got GO; 4 more real bugs in the
        //            two hardened features). (P0) BILINGUAL LOCK BYPASS: 1.0.111 called
        //            WithClientLock(..., mustOwn:false), which SHORT-CIRCUITS the lock - so the
        //            translation ran UNSERIALIZED against the shared model client despite the
        //            changelog claiming otherwise. Now takes _aiLock directly (honouring
        //            _serializeClients + _clientLockMs), skips on lock timeout. (P0) RESERVATION
        //            CORRUPTION: it also routed through AwaitWithCancel, whose cancellation path
        //            does Interlocked.Add(ref _jsonBgReserved, -w) for work this call never
        //            reserved - corrupting the admission counter. AwaitWithCancel removed from
        //            the translation path. (P0) TRIAGE PARSE: used a naive JToken.Parse that
        //            returns ZERO rows on the real get_frs_alerts MCP content[].text envelope, so
        //            triage silently never fired. Now uses ParseJsonDocuments + SelectTokens the
        //            same way the evidence path does (proven: 0 -> correct count on the wire
        //            format). (P0) CURRENT ALERT COUNTED: the incidence-window history includes
        //            the current alert; counting it inflated every alert to >=1. Now excluded by
        //            alertId, de-duped, and the field is otherSameCauseInWindow with honest
        //            wording (window recurrence, not lifetime). Triage/bilingual remain default
        //            OFF. Index_15 -> 1.0.28.
        // 1.0.111.0 - SAFETY/CORRECTNESS fixes from external review of 1.0.110 (3 P0s + 2 P1s,
        //            all in the 1.0.108/109 features). (1) BILINGUAL: default OFF now; hi is
        //            SERVER-ONLY - removed from model-accepted keys and stripped at validation
        //            entry (a model-invented Hindi block could bypass the ENGLISH operational
        //            filter). AttachHinglishAsync hardened: runs under _aiLock with its own
        //            timeout+cancellation, records its tokens/turns (turns=1 was a lie; a 2nd
        //            paid call went unaccounted), and extracts assistant TEXT from the raw reply
        //            correctly (it was treating the whole API envelope as the text - bilingual
        //            was effectively broken). recommended_action stays ENGLISH (the one field
        //            that could carry a directive). (2) DERIVED: the "drove"/cause-chain causal
        //            claim was UNSOUND (largest %-deviation is not necessarily the largest
        //            contributor - for A-B a small % move in a large term can dominate). Now
        //            emits largestDeviation (observation, not attribution) + windowMean (labelled
        //            honestly, not the configured baseline); derived_inputs is SERVER-ONLY and
        //            SUPPRESSED on INCONCLUSIVE / inadequate coverage. (3) TRIAGE: was the model
        //            deciding maintenance priority with no verified basis. Now SERVER-COMPUTED
        //            from a mechanical count of prior alerts with the same AssetId+cause code;
        //            SERVER-ONLY, default OFF (EnableTriage) pending owner-approved thresholds,
        //            suppressed on INCONCLUSIVE. (P1) CoerceCategory ordering fixed (wiring->Field
        //            HW not Config; data-gap->Coverage gap not Platform) + coercion logged. (P1)
        //            weather is server-only (future strip guard). Index_15 -> 1.0.27.
        // 1.0.110.0 - Do not discard a valid verdict over a bad CATEGORY label (real defect,
        //            alert 458789 PT R OBS point-obstruction FAILURE: the model produced a full
        //            correct verdict but its category string was not one of the 8 allowed, so
        //            ValidateAndSanitizeVerdict REJECTED the whole verdict -> operator saw
        //            "category must be one of..." and NO diagnosis, wasting the entire analysis).
        //            Category is a classification LABEL, the least safety-critical field. Now
        //            CoerceCategory maps an unknown/misspelled/descriptive category to the
        //            nearest valid bucket (case-insensitive exact match, then keyword heuristics
        //            e.g. obstruction/mechanical/point -> Genuine failure, then a verdict-based
        //            default) instead of failing. Only the LABEL changes; verdict/confidence/
        //            evidence/action are untouched and the operational-action filter is
        //            unaffected. verdict/confidence enums STILL reject (cannot be guessed). Also
        //            fixed a prompt/validation mismatch: "Coverage gap" (valid since 1.0.75) was
        //            in validation but listed in NEITHER prompt - added to both, plus explicit
        //            guidance that a mechanical/obstruction failure is "Genuine failure", a field
        //            HW fault is "Field HW", missing data is "Coverage gap", so the model picks a
        //            valid bucket up front. No change to #1/#4 (1.0.109) or bilingual (1.0.108).
        // 1.0.109.0 - Card enrichment (4 features from the alert-466916 card review):
        //            #1 DERIVED INPUTS: for a derived-value alert (RRAIL, IBALST, ...), the
        //            verdict now carries derived_inputs { param, value, inputs:[{ name, value,
        //            baseline, driftPct, drove }] } so the card shows the RAW component inputs
        //            behind the derived value and which one drove it. The model already had the
        //            component as-of values; this makes it EMIT them. #4 TRIAGE: an optional
        //            maintenance-PRIORITY hint triage { severity(high|medium|low),
        //            trend(worsening|stable|improving|isolated), note } so a maintainer can tell
        //            "attend soon" from "can wait" instead of being sent to field on every
        //            CONFIRMED - judged from the electrical evidence AND the count/direction of
        //            PRIOR same-cause alerts on the asset (get_frs_alerts, already allowlisted +
        //            prefetched). SAFETY: triage is ADVISORY only - severity/trend are strict
        //            enums, the note passes FilterOperationalActions so it can NEVER carry a
        //            movement/operational directive; the maintainer decides. Both new fields are
        //            OPTIONAL, sanitized, added to _verdictTopKeys, and dropped if malformed
        //            (never fail the verdict). Prompts (both synthesis + agentic) describe when
        //            to emit each. #2 cause-chain diagram and #3 weather placeholder are rendered
        //            client-side in Index_15 1.0.26 from derived_inputs / (pending) weather.
        //            No change to verdict/confidence/category logic or the safety filter.
        // 1.0.108.0 - BILINGUAL verdicts (English + Hinglish). SAFE-BY-ORDER: the Hinglish is
        //            produced by translating the ALREADY-SANITIZED verdict (AFTER
        //            ValidateAndSanitizeVerdict has run FilterOperationalActions + TrimWords),
        //            via a separate single-shot model call (CallModelAsync, no tools), attached
        //            as verdictObj["hi"]. The Hindi therefore INHERITS the operational-action
        //            filtering - no directive can appear in Hindi that was not already stripped
        //            from English - and AttachHinglishAsync re-runs the filter on the Hinglish
        //            as a belt-and-braces guard. Wired into BOTH verdict paths: the JSON
        //            endpoint (AnalyzeAlertJsonCore) and the SSE popup frame. Best-effort: any
        //            translation failure ships the English verdict unchanged (never blocks or
        //            weakens it). appSettings "BilingualHindi"=false disables it (default on).
        //            Style: Romanized Hindi, tech terms + numbers kept verbatim. Adds one model
        //            call (~latency+cost) per verdict. Index_15 1.0.24 stacks the hi block under
        //            the English in the card. Follow-up chat stays English (prose, not verdict).
        // 1.0.107.0 - TPR-DROP / TRAIN-MOVEMENT rule (owner-confirmed, validated against real
        //            diagnostic 466916 which returned a false CONFIRMED 78% on a TC RAIL RES
        //            HIGH predictive alert, citing "TPR dropped 10+ times, recurring pattern"
        //            as primary evidence). A TPR relay dropping/de-energizing BY ITSELF is
        //            NORMAL TRAIN MOVEMENT - a track circuit's TPR drops whenever a train
        //            occupies the section - NOT a fault. Both prompts now: do NOT treat a
        //            TPR-drop count/recurrence as fault evidence or let it drive CONFIRMED; the
        //            "TPR weakens" in the causal chain means a SUSTAINED ELECTRICAL decline of
        //            the relay-end feed (Ir mA / TR V vs baseline, feed held), not the momentary
        //            de-energization as a train passes; TPR drops support a fault only with a
        //            genuine TRACK-FAILURE alert on that track or drops persisting with no train
        //            present, else LOWER confidence / NOT_CONFIRMED / INCONCLUSIVE. The
        //            electrical leading indicators still drive the verdict - this only removes
        //            TPR-drop COUNT as false corroboration. Also: RdpmsMaps.generated.cs renamed
        //            to RdpmsMaps.cs and the class is now PUBLIC (was internal) - update the
        //            .csproj Compile Include to the new filename.
        // 1.0.106.0 - Corrected the DryRunTools AUTH claim in comments/changelog: 1.0.105 said
        //            it "requires X-Internal-Api-Key" but the code actually gates on appSettings
        //            EnableDryRunTools (default off) and relies on the page app auth - the code
        //            was right, the header was wrong. Header now states the flag gate and
        //            suggests [Authorize] for a stricter posture. Also repackaged with the
        //            CORRECT Index_15.cshtml: 1.0.105 accidentally bundled a STALE 1.0.17 view,
        //            reverting four safety fixes (provider toggle 1.0.18/19, server-VALIDATED
        //            verdict frame 1.0.20, per-provider follow-up/cache 1.0.21). The debug
        //            button is now merged onto Index_15 1.0.22 (= 1.0.21 + the debug button),
        //            preserving the verdict-frame safety code. No controller logic change vs
        //            1.0.105 beyond the comment fix.
        // 1.0.105.0 - CRITICAL: restored the Chat() method DECLARATION, which the 1.0.103
        //            DryRunTools insertion had accidentally deleted - the endpoint replaced
        //            the "[HttpPost] public async Task<ActionResult> Chat()" header and left a
        //            bare block, so the controller could not compile (a brace scanner passes it
        //            because a bare block is brace-balanced - it is NOT a brace error). Also
        //            hardened DryRunTools per review: (P0) now [HttpPost], GATED by appSettings
        //            EnableDryRunTools (default OFF -> 403, a production kill-switch); it is a
        //            browser-facing diagnostic so it relies on the page's app auth, NOT the
        //            service X-Internal-Api-Key (a browser cannot hold it) - add [Authorize] for
        //            a stricter posture. Runs cap lowered to 5, ML tools run ONLY
        //            when includeMl=true (default false), observes Response.ClientDisconnected
        //            Token. (P1) removed C#7-only constructs (value tuples -> Tuple.Create,
        //            local function -> Action, "is JObject o" pattern -> explicit cast) so it
        //            builds under older language levels. (P1) scope corrected: it is an MCP
        //            TRANSPORT/AVAILABILITY test, NOT a full verdict-path test - it does not
        //            run ClampToolArgs, NarrowAttributeRange, evidence-grade validation or
        //            per-request budgets, so a green run proves the tools RESPOND, not that the
        //            verdict path accepts every result. No functional change to the 1.0.104
        //            site-context/baseline fix or the prompt policy.
        // 1.0.104.0 - CRITICAL fix to the 1.0.102 site-context change, which BROKE the range
        //            baseline. NarrowAttributeRange emitted "[SITE CONTEXT] ...\nlegend:\n[rows]"
        //            - text, not JSON. get_attribute_range is an evidence tool (requireJson=
        //            true), so LooksUsableToolResult REJECTED the whole thing: the baseline,
        //            coordinates and topology never reached the model, the degraded card
        //            baseline was used, and a wasted second model turn produced the 51-byte
        //            "no usable data" diagnostic (~30 s). FIX: return ONE valid JSON object
        //            {siteContext:{site,code,latitude,longitude,familyTracks,signalTrack,
        //            pointTrack}, legend:{...}, rows:[{a,t,n,avg,max,min,fail}]}. Passes the
        //            evidence validator on prefetch AND both agentic paths, keeps geo +
        //            topology, stays compact. Both prompts updated to read the siteContext
        //            object and to CITE the topology asset ID (name it only if other evidence
        //            maps the ID to a name). (P1) get_weather added to _optionalTools so an
        //            unavailable/failed weather result never weakens an otherwise complete
        //            verdict. (P1) prefetched multi-attribute note now respects conditionLogic
        //            (AND: all branches; OR: one branch) instead of a looser blanket allowance.
        //            (Minor) SiteContextObject emit-test now includes site/code/pointTrack.
        // 1.0.103.0 - Added GET /AiChat/DryRunTools?siteId=&assetId=&days=&runs= : runs the
        //            REAL read-only MCP tools the AI uses (via CallToolRoutedAsync), server-
        //            side, and returns per-tool ok/ms/bytes/shape + a dense line log + a
        //            cross-run stability flag. This is the TRUE end-to-end tool path for the
        //            Historian "MCP Test" tab - not the browser hitting raw REST endpoints
        //            (which fails on CORS/auth the AI never sees) and not the model (no
        //            verdict, no Anthropic call, no writes). Tools exercised: get_asset_tags,
        //            history_get(AssetId), get_hist_realtime, get_attribute_range(SiteId),
        //            get_frs_alerts(opt), predict_track_health/predict_pm_operation(opt).
        //            Read-only; safe to repeat for stability. The tab should call THIS instead
        //            of fetching the APIs directly.
        // 1.0.102.0 - Review fixes to 1.0.101 (the weather/topology feature did not reach the
        //            fast path). (P0-2) SITE CONTEXT now surfaced: NarrowAttributeRange handles
        //            the OBJECT form of the range response and emits a [SITE CONTEXT] line with
        //            lat/lon + familyTracks + signalTrack + site/code, ONCE, before compacting
        //            the attribute rows (the per-row compactor dropped these). (P0-1) Corrected
        //            the over-claim: the SYNTHESIS (fast) path sends NO tools, so weather is
        //            used there ONLY if already present in evidence - the model is told it
        //            cannot fetch it on that path; get_weather is an AGENTIC-path option (a
        //            deterministic weather PREFETCH for the fast path is a separate, endpoint-
        //            tested task). (P0-3) Multi-attribute verdict now respects conditionLogic:
        //            OR - one aligned breached branch may CONFIRM, NOT_CONFIRMED needs every
        //            branch; AND - every branch for CONFIRMED, one failed branch may
        //            NOT_CONFIRMED. Missing evidence alone does not force INCONCLUSIVE, but it
        //            REMAINS required for stale/contradictory/implausible/incoherent evidence,
        //            a commanded throw with no operation telemetry, or a logically incomplete
        //            formula. (P1) Weather made CORROBORATIVE not causal: rain/thunderstorm =
        //            PLAUSIBILITY only; thunderstorm codes are NOT a confirmed strike; site-wide
        //            needs correlated family-track/mapped-signal/multi-asset telemetry, named.
        // 1.0.101.0 - Weather + topology for environmental / site-wide reasoning, using the
        //            richer FRSAttributeRangeHistory response (which carries the site Lat/Long
        //            + FamilyTrack + SignalTrackMapping alongside the per-attribute bands).
        //            (1) Added get_weather to the verdict-mode tool caps (cap 1) so the model
        //            MAY call a weather-by-latlong tool; rain -> ballast/leakage/drainage,
        //            lightning -> surge/multi-asset - both support a site-wide/environmental
        //            read (backed by IR failure data: rain 134, lightning 113, multi-asset).
        //            (2) Both prompts now use the TOPOLOGY (FamilyTrack, SignalTrackMapping):
        //            a family-track or mapped-signal alert in the same window strengthens
        //            site-wide. (3) Both prompts told to use weather ONLY if present and NEVER
        //            to invent it (the model has no web access; the tool does the fetch).
        //            IMPORTANT: the get_weather TOOL (Open-Meteo wrapper by lat/long) must be
        //            built + tested + exposed on the MCP server side; this change only permits
        //            and guides it. Until the tool exists the model never sees it (no error).
        // 1.0.100.0 - TRACK If-Ir gap rule made DYNAMIC (owner + real-data driven). A real
        //            healthy track asset (AssetId 41824) showed a 32 mA If-Ir gap - BELOW the
        //            fixed 50-100 mA "normal" band from 1.0.94. Owner: the gap must be judged
        //            against THIS asset's OWN baseline, not a fixed number (what is normal
        //            differs per asset). Dropped the fixed 50-100 / ~100 mA figures from both
        //            prompts; the rule now says compare the gap to the asset's AverageValue
        //            (which the range data provides) and flag only a gap clearly WIDENED
        //            beyond that baseline with feed held. Verified the rules read correctly on
        //            the real healthy asset (32 mA gap, TPR V matching Loc -> not a fault).
        // 1.0.99.0 - PM operation obstruction rule, VALIDATED against a real one-month point-
        //            machine dataset (AssetId 42872). The dataset confirmed the att grid
        //            matches PmOpAttid (1001-1006 AC-Normal etc.) and that operation TIME
        //            lives on the CURRENT channels (A/B end) in MILLISECONDS (voltage OpTime
        //            is 0). Real normal throw ~5 s; owner-confirmed obstruction threshold is
        //            >6000 ms (6 s), flagged SOFTLY as "possible obstruction/high load" for
        //            the engineer (telemetry does not name the cause). This CORRECTS the
        //            earlier PMServ-derived ">810 ADC samples" rule, which was for the raw
        //            ADC path and would never fire against the HistoryCompact ms units.
        //            Added to both prompts and the PM evidence block. Option 1 (guidance +
        //            existing history_get tool) - the model fetches the operation-time channel
        //            via its normal tool; no new HTTP path built (a dedicated HistoryCompact
        //            prefetch remains a separate, endpoint-tested task).
        // 1.0.98.0 - MULTI-ATTRIBUTE verdict policy encoded (owner decision: Option B /
        //            graded). For a cause code with RELATED ATTRIBUTES, the ANCHOR (the attr
        //            with incidence-aligned history) may drive a binary verdict; do NOT force
        //            INCONCLUSIVE merely because a related condition lacks aligned evidence.
        //            Each required related condition with no aligned evidence LOWERS confidence
        //            and is named in caveats; the verdict stays actionable and the missing
        //            corroboration rides in confidence + caveats for the engineer to weigh
        //            (NOT an automated gate). INCONCLUSIVE only when the ANCHOR itself lacks
        //            aligned history. Added to BOTH prompts + the prefetch related-attr note.
        //            Builds on the 1.0.96 guard (current/untimestamped values never satisfy an
        //            incidence-time condition; missing != no movement).
        // 1.0.97.0 - Review fix to 1.0.96: "Room and Loc agreeing = clean detection" was too
        //            broad. Agreement only rules out a room-vs-location transmission mismatch;
        //            it does NOT establish the commanded lie, NWKR/RWKR exclusivity, or A/B
        //            agreement (e.g. Normal commanded but both sides stay Reverse = agreeing
        //            but wrong). Reworded to say agreement means no transmission mismatch and
        //            to still verify exclusivity, A/B agreement and command outcome. Also
        //            fixed a stale in-code comment ("four detection Titles" -> eight).
        // 1.0.96.0 - Review fixes to 1.0.95 (two P0 contradictions + polish). (P0-1) Removed
        //            the OLD "no recent sample = steady, not missing" point-freshness wording
        //            from BOTH prompts - it contradicted the new "absence = idle ONLY if no
        //            NWCR/RWCR command; commanded throw with absent telemetry = DATA GAP" rule
        //            and could hide missing operation telemetry. (P0-2) Multi-attribute
        //            coherence note now guarded: evaluate coherence ONLY for related attrs
        //            with incidence-aligned values; missing related history is a LIMITATION,
        //            not proof they did not move - do not use current/untimestamped values to
        //            contradict an incidence-time trend (the one-turn path has no tools to
        //            fetch the related histories). (P1) Added COSIG to SIGNAL classification
        //            (13 COSIG causes existed but analyse_alert was skipped for them). (P1)
        //            Cable-fault wording softened to "supports localizing to the cable path;
        //            telemetry does not establish the failed part" (both places), per the
        //            global no-part-diagnosis rule. (P1) Added IsPointDetectionRelayCause +
        //            explicit "Point cause class: DETECTION-RELAY | OPERATION/CURRENT/VOLTAGE"
        //            line so the model does not infer class from wording. Fixed stale
        //            _pmDetectionAttrs comment (was "unconfirmed/room-only"; now all 8,
        //            mapping confirmed).
        // 1.0.95.0 - SIGNAL + POINT rules corrected from AUTHORITATIVE source (Debouncer
        //            ProcessService.cs aspect patterns + PMServ HandleCluster.cs operation
        //            model), replacing inferred rules. SIGNAL: the aspect is proved by DL
        //            relays, and compound aspects light MULTIPLE lamps - double-yellow (HHG)
        //            = HHG+HG currents, route/calling-on aspects add the route/Co lamp - so
        //            "exactly one aspect current high" was WRONG. Correct rule: judge against
        //            the relay-proved aspect evidence set (>=V50/mA50/PR15); multiple highs
        //            can be correct; a set-lamp with V but low mA = phantom; a high outside
        //            the set is the concern. POINT: operation is a per-throw waveform event -
        //            a longer/extended operation = obstruction or high mechanical load
        //            (authoritative: >16s / >810 ADC samples = extended). Room-side
        //            authoritative + (Loc)-vs-room cable localization (from #3). #4/#5 of the
        //            domain-confirmation items now resolved from source, not guessed.
        // 1.0.94.0 - TRACK coherence corrected with field-confirmed domain facts (owner):
        //            (a) a 50-100 mA feed-relay (If-Ir) gap is NORMAL baseline ballast
        //            leakage - do not flag; only a gap clearly beyond ~100 mA or widening
        //            past it (feed held) is real leakage. The old "healthy If approx Ir"
        //            was too tight and would flag a normal 80 mA gap. (b) Added the causal
        //            chain that ends in track loss: rail-R/leakage -> Ir mA or TR V drops
        //            -> proving TPR relay at loc (driven by Ir/TR V) weakens -> relay-room
        //            TPR V feed degrades -> track down soon. So Ir mA and TR V are the
        //            LEADING indicators; a sustained drop with TPR feed degrading = imminent
        //            track loss (weight toward CONFIRMED, not transient). Both prompts.
        // 1.0.93.0 - Review fixes to the 1.0.92 coherence wording (the rules were too
        //            absolute and could force wrong verdicts). (1) POINT rules made
        //            CAUSE-AWARE: detection disagreement warrants INCONCLUSIVE for a
        //            current/voltage/operation alert, but MAY CONFIRM for a detection-relay
        //            alert (PT NWKR/RWKR RELAY DEFECT) when timestamp-aligned. (2) "No
        //            IPT/VPT/TPT = idle" now conditional on NO throw/control event in window;
        //            a commanded throw with absent telemetry = DATA GAP -> INCONCLUSIVE.
        //            (3) Removed component-fault diagnosis ("obstruction/motor fault") ->
        //            "operation/detection coherence failure, telemetry cannot name the
        //            cause". (4) Detection state usable for incidence coherence ONLY when
        //            timestamp within the alert window (current/untimestamped = present
        //            state only). (5) SIGNAL one-aspect rule softened to subtype-aware
        //            (route indicators may energize multiple lamps). (6) TRACK charger
        //            wording de-absolutised (check If/Ir/Vf/TR V coherence, not "never
        //            RRAIL"). (7) HealthBandNote reverse index from Resolves -> fixes
        //            TR V (Relay) and other titles that could not reach their band key.
        //            Open (owner confirm): room-side vs location-side NWKR/RWKR mapping;
        //            room/location NWKR/RWKR mapping confirmed (#3); PM live-fetch remains.
        // 1.0.92.0 - Circuit-topology coherence rules + point-machine detection branch,
        //            from the track/signal/point circuit diagrams. (1) Per-asset COHERENCE
        //            signatures added to BOTH prompts: TRACK (If approx Ir, widening gap =
        //            leakage; RRAIL up <=> Ir+TR V down; charger is upstream), SIGNAL (one
        //            aspect current high, rest near-zero; two high = wrong-side; V present +
        //            I~0 = phantom/lamp), POINT (NWKR xor RWKR per end; A-END must agree with
        //            B-END; IPT/VPT/TPT only during a throw). These sharpen the coherence
        //            gate. (2) POINT-MACHINE detection-coherence evidence block (additive,
        //            point-only) surfacing the four detection relays + PM ML tools so the
        //            model checks both-ends-agree. The block GUIDES on detection attributes;
        //            it does not yet fetch each detection tag live in prefetch -- that is the
        //            next refinement, pending a real PM alert to validate against. Honest
        //            scaffold->branch progression: Stage 4 is now on the hot path for PM
        //            alerts (guidance), with live per-relay fetch as the remaining step.
        // 1.0.91.0 - STAGE 1 of the RDPMS wisdom integration: the alias->Title map is now
        //            GENERATED from the authoritative wisdom xlsx (RdpmsMaps.generated.cs),
        //            all asset types (Track/Signal/Point/IPS/LC/env) -- 173 entries --
        //            replacing the hand-built Track-only table from 1.0.85 (23 entries).
        //            DomainAttributeName now reads RdpmsMaps.AliasToTitle. The generated
        //            map is a strict SUPERSET of the old one (every 1.0.85 entry verified
        //            present, incl. ITC TFC O/P->Charger mA and the derived intermediates).
        //            Signal/Point/IPS attribute resolution now works -- previously those
        //            fell back to the fragile heuristic. The xlsx is the source of truth;
        //            gen_rdpms_maps.py regenerates the .cs when the doc changes (no hand-
        //            transcription, no runtime xlsx parsing). ALL 5 STAGES landed this
        //            version: (1) full alias map; (2) sensor health bands injected into
        //            derived-component evidence via HealthBandNote; (3) deterministic
        //            cause_code->attribute in PickAlertingTagId when the card lacks a
        //            paramLabel (empty-payload case); (4) PmOpAttidFor resolver for point-
        //            machine operation attids -- SCAFFOLD ONLY (resolver present, NOT yet
        //            called; PM prefetch branch is the next step); (5) RDPMS domain model
        //            added to BOTH AnalyzeSystemPrompt AND AnalyzeSynthesisPrompt (the fast
        //            one-turn path) with neutral coherence wording. Alias normalization
        //            strips ONLY unit suffixes and preserves semantic qualifiers (BUG/CUG/
        //            Sig/Loc); the converter ABORTS on genuine alias collisions.
        // 1.0.90.0 - Evidence trim for DERIVED alerts (cost + grounding). Each component
        //            was dumping ~1200 chars of RAW EPOCH history (300 samples of Unix
        //            timestamps the model cannot read as times) -> 4-component alerts hit
        //            ~7,600 evidence tokens. Since 1.0.88 already computes+supplies the
        //            as-of value, its age, and the derived value in code, the raw series
        //            is largely redundant. Now each component emits a COMPACT readable
        //            window around incidence (up to 8 "HH:mm:ss=value" samples, the as-of
        //            one marked) + a REDUCED 500-char raw tail as a safety net (kept per
        //            the safe path). Saves ~880 tok on 4-comp alerts (~5%/alert) AND
        //            reads clearly -- e.g. the charger collapse shows as 16:14:25=-0.83.
        //            Falls back to the full 1200 tail if the columnar parse fails.
        // 1.0.89.0 - Cache diagnostics (log-only, no logic change). cache_read AND
        //            cache_write were both 0 on single-turn synthesis runs. Rather than
        //            ship a blind cache fix, this logs the facts that decide WHY: the
        //            cached-prefix token estimate (Anthropic caches only >=1024 tokens),
        //            the tool count (synthesis mode sends 0 tools, dropping the ~2k tool
        //            schema block that is the main thing worth caching), the synthesis
        //            flag, and whether the prefix clears the 1024 minimum. One run now
        //            explains cache=0 (most likely: compact synthesis prompt < 1024 tok
        //            so Anthropic declines to cache) instead of guessing. Cost fix, if
        //            any, follows once the log confirms the cause.
        // 1.0.88.0 - Option C part 2 + COV-correct coverage. Two linked changes to the
        //            derived branch: (1) COVERAGE is now AS-OF/carry-forward, not sample-
        //            proximity. The history API is change-of-value: a row appears only on
        //            change; a steady value returns bracket rows (confirmed: a 1h window
        //            returned n=2 identical rows stamped ~4.7h earlier). So "no in-window
        //            sample" = STEADY, not missing. AsOfValue() parses columnar {ts,v},
        //            takes the last value with ts<=incidence (bracket rows count),
        //            reports its age + whether the window was bracket-only. A component
        //            is COVERED whenever an as-of value exists; only zero rows <=
        //            incidence is true no-data. This fixes 406304-class INCONCLUSIVE,
        //            where steady components were mis-graded covered=0. (2) The derived
        //            VALUE is now COMPUTED IN CODE from the components as-of values via
        //            the card's OWN formula string (TryEvalFormula, shunting-yard over
        //            + - * / and parens; zero drift vs the alert engine -- validated:
        //            ITC BATT CHARG computes -176.0 vs the card's -175.92). The evidence
        //            now carries the system-computed value + each component's as-of value
        //            and staleness age; the model weighs which input moved + how fresh,
        //            and no longer hand-computes. Direct alerts unchanged.
        // 1.0.87.0 - viz.series validation now COERCES instead of rejecting. With
        //            1.0.86 the derived path finally grounds (log: RRAIL resolved=4
        //            covered=2 adequate=yes, 1 turn, 15s) -- but the otherwise-complete
        //            verdict was thrown away by the output gate with "viz.series[0] must
        //            be numeric": the model had emitted a series value as a STRING
        //            ("8.53") or slipped a null in. Rejecting a grounded verdict over a
        //            coercible formatting nit is a brittle gate. Now: numeric strings are
        //            parsed in place (invariant culture), nulls/empty strings dropped,
        //            and only a genuinely non-numeric element (object, non-numeric text)
        //            still rejects. Turns the 465382 win into a delivered CONFIRMED.
        // 1.0.86.0 - THE derived-resolution bug, found in the live tool log. The
        //            derived-branch prefetch search_tags call was narrowing by
        //            AttributeName = the DERIVED name ("RRAIL") -- which is NOT a stored
        //            tag -> server returned ZERO rows -> 0 components resolved -> every
        //            derived alert INCONCLUSIVE. Proven on the wire: prefetch search
        //            {AssetName:117T, AttributeName:RRAIL} -> "No tags found", while the
        //            SAME asset by AssetId returns all 11 component tags (If mA, Ir mA,
        //            Vf, Choke V, Charger mA, ...). So rev J (1.0.82) was RUNNING and the
        //            alias->Title map (1.0.85) was correct, but the search that feeds
        //            component resolution came back empty because it was filtered to the
        //            phantom derived tag. Fix: NewSearchArgs now takes isDerived and
        //            SUPPRESSES the AttributeName narrowing for derived alerts, so the
        //            full asset tag list returns and the formula components resolve
        //            against it. (isDerived computed at the call site via IsDerivedCard,
        //            since the _derivedAlert field is set later in prefetch.) Direct
        //            alerts unchanged -- they still narrow by their real attribute.
        // 1.0.85.0 - Authoritative alias->Title resolution (Option C, part 1: RESOLUTION).
        //            Root cause of the derived-path fragility: the ALERT CARD formula uses
        //            ALIAS names ("ITC FEED END(mA)") but search_tags works on the REAL
        //            Title name ("If mA"). rev J matched the alias against the tag table
        //            and the old DomainAttributeName GUESSED via Contains() -- so
        //            "ITC TFC O/P" mis-resolved (should be Charger mA) and 406304 could
        //            not ground. Now DomainAttributeName uses the site alias table
        //            (_aliasToTitle, Track) with unit-strip + whitespace-collapse
        //            normalization (handles the double-space duplicate in the source
        //            table), falling back to the legacy heuristic only for unmapped
        //            names. All derived-formula components (RRAIL, ITC BATT CHARG, RTC*,
        //            RTC VAR RES) now resolve to real Titles -- verified. Compute-in-code
        //            of the time-aligned derived series is Option C part 2 (next), needs
        //            the finalized formula set; this part fixes RESOLUTION so components
        //            are pulled correctly and the model identifies the right sensors.
        // 1.0.84.0 - Two safe band-aids for DERIVED alerts (real compute-in-code fix is
        //            Option C, next rev, pending exact formula+tag-name mapping):
        //            (1) ExtractFormulaComponents regex fixed - "ITC TFC O/P" was split
        //            at the "/" into "ITC TFC O"+"P" so that component never resolved
        //            (alert 406304 could not ground). Now name-internal "/" and "." stay
        //            in the token; the LHS is stripped first; the space-delimited division
        //            operator is not grabbed as a name. All 3 real formulas tokenize clean.
        //            (2) No-hand-compute guard - the model was computing the derived value
        //            from component samples at DIFFERENT timestamps and getting garbage
        //            (alert 450713: hand-computed 15.64 vs true 6.23). The note now tells
        //            it to use the card's authoritative derived value and use components
        //            ONLY to identify WHICH input moved, not to recompute the value.
        // 1.0.83.0 - Latency/token trim for DERIVED alerts. rev J (1.0.82) fixed the
        //            turn count (4->1) but the component pull sent 4 series at up to 5000
        //            chars each (~20k chars ~= 21k-token context -> a 24s model turn on
        //            alert 398990). The component history is now emitted via TailForPrompt
        //            at 1500 chars: (a) the TAIL not the head, because a time series has
        //            the incidence + recovery at the END (head-trimming both sent too much
        //            AND could cut off the incidence), and (b) smaller. Coverage was
        //            already graded on the FULL raw cHist upstream, so this shrinks only
        //            the prompt copy -- grounding is unaffected. Expected: derived context
        //            ~21k -> ~11k, turn ~24s -> ~14-16s, same verdict. Direct alerts
        //            unchanged.
        // 1.0.82.0 - rev J: DERIVED alerting attributes (e.g. RRAIL, card
        //            triggeredConditions[0].type=="derived" with a formula). These have
        //            NO telemetry tag, so the tag hunt + AssetId fallback could never
        //            succeed and the coverage gate GUARANTEED Coverage-gap INCONCLUSIVE
        //            (measured: 46s, 4 turns, 42k context, phantom-series pulls of
        //            1.3KB/433B/0B on alert 399113). Now: DetectDerived up front; prefetch
        //            extracts the formula components (ExtractFormulaComponents ->
        //            DomainAttributeName canonicals), resolves each from the search_tags
        //            rows (TagIdForAttributeName; "TR V" also accepts "VR"), pulls the
        //            component series in PARALLEL, stamps their coverage, and the gate
        //            passes when >=2 components are Covered including one voltage AND one
        //            current when the formula uses both (_derivedComponentsAdequate).
        //            Evidence block carries the formula + component series + an explicit
        //            "no direct series exists, do NOT history_get it" note (kills the
        //            phantom pulls). Direct (non-derived) alerts: byte-for-byte unchanged
        //            behavior -- the branch activates only when the card says derived.
        // 1.0.81.0 - Prompt-cache fix: BuildDateAnchor embedded the current time TO THE
        //            SECOND inside the cached system block, so the tools+system prefix was
        //            never byte-identical across turns -> Anthropic prompt cache always
        //            MISSED (0 cache reads, full ~7.8k-token prefix re-sent each turn).
        //            AnthropicClient (Domain.dll) placed cache_control CORRECTLY -- the
        //            controller was poisoning its own cached block with a per-second value.
        //            Fix: round the anchor time to the HOUR (istHour), keeping the date and
        //            all IST window guidance; the model only needs "now" for relative
        //            reasoning (the incidence time is in the evidence block), so hour
        //            granularity is analytically identical for a 2-turn run seconds apart
        //            and makes the system block stable -> turn 2+ READS the cache. Large
        //            input-token cut on multi-turn runs. No Domain.dll change.
        // 1.0.80.0 - DEGRADED baseline fallback. get_attribute_range fails upstream
        //            (the MCP server calls the E7 API FRSAttributeRange endpoint WITHOUT
        //            the Basic auth that get_frs_alerts uses, so it returns a ~2991-char
        //            non-JSON auth page on every call -> "UNUSABLE NOT_JSON" -> prefetch
        //            surfaced no baseline -> "baseline thresholds missing" -> the model
        //            wasted a full turn re-calling the same broken tool). The real fix is
        //            server-side (add Basic auth to the FRSAttributeRange call); this
        //            controller change is the degraded-mode safety net: when
        //            get_attribute_range yields nothing, BuildCardBaseline extracts the
        //            ALERTING attribute's thresholds from the alert card
        //            (triggeredConditions[0].thresholds -- MinSafe + percent-Avg) and
        //            emits a clearly-labelled [BASELINE (DEGRADED...)] block so the model
        //            has a baseline and does not waste the turn. Partial (card lacks
        //            MaxSafe/MinFail/true-Average and covers only the triggered
        //            attribute); inert once the server auth fix lands (get_attribute_range
        //            then returns JSON, gotRange set by the existing block, fallback never
        //            fires). Search_tags fix (1.0.79.0) confirmed working on the wire.
        //            SEPARATE open item: prompt caching shows 0 cache reads (cache_control
        //            marker placement inside Domain.dll's AnthropicClient -- not editable
        //            from the controller; needs the Domain.dll owner).
        // 1.0.79.0 - ROOT CAUSE of the "search_tags returns wrong assets" failure,
        //            per the authoritative MCP tool doc (MCP_TOOLS_EDGEX_README):
        //            search_tags NEVER read a "query" param -- the handler filters on
        //            AssetName/AttributeName/SiteId/etc. NewSearchArgs was sending
        //            o["query"], which the server ignored, so it returned unfiltered/
        //            arbitrary tags (point-machine tags for a track circuit), the
        //            matcher correctly refused them, and every alert fell through to
        //            the AssetId fallback / coverage-gap INCONCLUSIVE. The server
        //            schema was corrected in v1.5.55.1 (dead query removed, real
        //            filters declared); this release matches it: NewSearchArgs now
        //            sends AssetName (the real search text) plus AttributeName ONLY
        //            when DomainAttributeName yields a canonical name (raw paramLabel
        //            "ITC RELAY END(mA)" does not ILIKE-match the tag name "Ir mA", so
        //            passing it would filter the right tag OUT; omitted otherwise so it
        //            cannot over-narrow). Limit 60->50 (server hard cap). Added
        //            SearchTagsStats prefetch logging (rows/uniqueAssets/requestedAsset
        //            present/resolvedTag) to prove the fix on the wire. The 1.0.78.0
        //            AssetId fallback + preserveJson are RETAINED as canary redundancy
        //            (reviewer condition) -- not removed. DEPLOY DEPENDENCY: ClampToolArgs
        //            strips undeclared args, so the app must see the v1.5.55.1 schema
        //            (recycle app pool / wait out the 300s tool cache) or AssetName is
        //            stripped and the fix is a no-op. Synthesis model = claude-sonnet-4-6
        // 1.0.78.0 - AssetId fallback for the search_tags failure (superseded as the
        //            PRIMARY path by 1.0.79.0's root-cause fix; kept as redundancy).
        //            When PickAlertingTagId could not resolve, prefetch probed
        //            history_get(AssetId) and tried to resolve a tag; the shipped probe
        //            approach was itself buggy (history_get carries no attribute names
        //            in any mode) -- retained only as a fallback behind the now-working
        //            search_tags path.
        // 1.0.77.0 - Sixth external review round: follow-up chat (AnalyzeChat) passed
        //            rawJsonMode=true to RunAgenticLoop purely to keep the existing
        //            out-of-scope-check skip, but that flag ALSO forced every completed
        //            turn to be parsed as verdict JSON -- so a normal prose answer like
        //            "the current value is..." failed JSON.Parse, emitted an SSE error
        //            frame ("Verdict was not valid JSON."), and the browser discarded
        //            the accumulated prose on that error. New validateVerdictOutput
        //            parameter is now the ONLY thing that gates the parse-and-emit-
        //            verdict-frame step; AnalyzeAlert passes it explicitly (raw output
        //            IS JSON, still validated); AnalyzeAlertJson leaves it false (that
        //            path already validates via the separate schemaReason contract);
        //            AnalyzeChat now passes rawJsonMode=false too, since its raw output
        //            is prose -- it gets the same FilterOperationalActions safety pass
        //            the SSE popup applies to prose, and picks up the out-of-scope
        //            guard as a side benefit rather than staying silently exempt from
        //            it. Also: the forced-downgrade evidence line "Measured alert value
        //            and configured threshold remain available" softened to "Available
        //            alert facts remain visible; incidence-covering trend is not
        //            verified" -- the old wording assumed facts not guaranteed present
        //            on every alert card
        // 1.0.76.0 - Fifth external review round on the shipped 1.0.75.0 build:
        //            requiredAlertingTagId is now pinned the INSTANT PickAlertingTagId
        //            matches, not after its history_get succeeds, so a failed/timed-out
        //            fetch can no longer erase a known alerting tag; the presumed-tag
        //            fallback no longer trusts the model's first history_get on faith --
        //            TryResolveAlertingTagFromSearch runs the same matcher against any
        //            successful search_tags result, and an unresolved tag is left
        //            unresolved rather than substituted; gotUsableHistoryGet is now set
        //            BEFORE the incidence-parse check, so an unparseable incidence with a
        //            usable payload can no longer bypass scoped enforcement; a coverage
        //            downgrade now REPLACES the evidence array with controlled factual
        //            lines (word-filtering a few assertive phrases was fragile) and
        //            neutralizes viz.series/trigger_pct/recovery_pct/phase_note/secondary
        //            so a rejected series can no longer narrate a confirmed trend
        // 1.0.75.0 - History coverage rebuilt end to end after four external review
        //            rounds: columnar cols/rows + MCP content[].text parsing;
        //            adequacy from UNIQUE PRE-ALERT samples (dedup, exclude
        //            post-alert); categorized failure reasons (SHORT_AT_END /
        //            INSUFFICIENT_SAMPLES / INSUFFICIENT_SPAN / UNVERIFIABLE) each
        //            with its own retry advice; coverage tracked PER ALERTING TAG,
        //            never satisfied by a different tag's series; RETRY REQUIRED
        //            status permitted by both MISSING-only prompts; mechanical
        //            final-verdict gate in ValidateAndSanitizeVerdict downgrades
        //            CONFIRMED/NOT_CONFIRMED to a fully-rewritten, non-contradictory
        //            INCONCLUSIVE (headline/cause/action/category/evidence/caveats)
        //            when the alerting tag never reached VerifiedAdequate; policy is
        //            AnalyzeStrictCoverage (0=scoped, 1=strict); AnalyzeChat no
        //            longer NREs on a missing context object; evidence-count
        //            comment corrected to match the 1-10/trim-to-4 validation
        // 1.0.74.0 - SAFETY: the popup verdict now passes ValidateAndSanitizeVerdict
        //            server-side and arrives as its own SSE frame. It previously applied
        //            none of the verdict contract -- no movement-directive filter, no
        //            category or numeric checks, no word limits. Follow-up chat also
        //            keeps the selected provider
        // 1.0.73.0 - Coverage is now PROVEN, not assumed: tri-state (covered / short /
        //            cannot-verify), last timestamp read from the series rows, tolerance
        //            300s not 3600s, and the incidence epoch converted with an explicit
        //            IST offset instead of the server timezone. Synthesis also requires
        //            audit or ML for track/PM alerts, and baseline is evidence-graded
        // 1.0.72.0 - Review fixes: synthesis now requires COMPLETE evidence (tags,
        //            baseline, and history that reaches the incidence), not merely
        //            "history exists"; audit keeps head AND tail; prefetch judges
        //            evidence as strictly as the model path; unknown cause codes are no
        //            longer submitted to the audit as TRACK
        // 1.0.71.0 - Provider switch: OpenAI selectable per REQUEST (ctx.provider) so the
        //            two can be compared on the same alert. OpenAiClient returns
        //            Anthropic-shaped JSON, so the loop and all gates are unchanged
        // 1.0.70.0 - Enforce the word budgets the prompt already claimed. headline 15,
        //            likely_cause 25, each evidence line 20, action 35, caveats 20 --
        //            trimmed, not rejected. The limits were advice the code never kept
        // 1.0.69.0 - SYNTHESIS MODE: once pre-fetch has grounded the analysis, send NO
        //            tools and a compact prompt, making a single turn structural rather
        //            than hoped-for. Removes the tool schemas AND the tool-use system
        //            prompt the API adds with them. Output capped by a second client
        // 1.0.68.0 - Pre-fetch analyse_alert and get_frs_alerts too. Both derive entirely
        //            from the alert context, yet fetching them late cost 13.2s across
        //            three extra model turns (measured on a 37.6s run)
        // 1.0.67.0 - Do not deny search_tags when pre-fetch failed to resolve the tag.
        //            The 1.0.64.0 denial closed the only route to a TagId, so the model
        //            could not fetch history at all and the verdict shipped with an
        //            empty series and no chart
        // 1.0.66.0 - Survive an AnthropicClient assembly mismatch. An optional parameter
        //            is compiled into the CALL SITE, so an older Domain.dll threw
        //            MissingMethodException on the first model call and killed every
        //            analysis. Fall back to the 3-argument overload and say so
        // 1.0.65.0 - meta.fault carries the exception TYPE and a redacted message. The
        //            sanitiser flattens every unrecognised error to "internal error",
        //            which hid a real model-call failure behind a generic string
        // 1.0.64.0 - REFUSE tools whose payload pre-fetch already supplied. Three prompt
        //            revisions failed to stop the model re-calling search_tags and
        //            get_attribute_range, so context reached 44k -- worse than before
        //            pre-fetch existed. history_get stays callable: a different tag is a
        //            legitimate request
        // 1.0.63.0 - Fix CS0136: 'note' was declared in two nested scopes of the same
        //            method (history coverage vs status summary)
        // 1.0.62.0 - Pre-fetch the ML analysis too. The prediction APIs are ASSET-keyed
        //            (SiteId + AssetId) while EdgeX history is TAG-keyed, so predictions
        //            need nothing beyond the alert record and cost the model zero turns
        // 1.0.61.0 - Attribute naming and threshold rules taken from the historian client:
        //            canonical names (tr v, tpr v (loc), charger op v) and the platform's
        //            own breach/warn semantics, which the model had been inferring
        // 1.0.60.0 - search_tags was returning 3 rows (server default), so the alerting
        //            attribute was never in the list and no matcher could have found it.
        //            Request Limit=60 and raise the clamp for discovery tools, which
        //            need breadth rather than a small page
        // 1.0.59.0 - Pre-fetch step no longer shows a warning when it succeeded partially:
        //            ok now means "gathered something", with an n/4 note saying what
        // 1.0.58.0 - Output budget stated as a HARD per-field word count. Measured: the
        //            final turn wrote ~1,138 tokens against a ~340 spec (3.3x) and took
        //            28s, of which 21.6s was generation. Series cap 24 -> 12
        // 1.0.57.0 - Tag matching needed DOMAIN knowledge, not word overlap: the alert
        //            says "ITC RELAY END(mA)" and the tag is "Ir mA". Map relay/feed end
        //            and current/voltage to Ir/If/Vr/Vf; a lone weak word hit no longer
        //            counts, since "MA" matches several attributes equally
        // 1.0.56.0 - Pre-fetch no longer spends the model's tool budget. Its own
        //            search_tags call was counting against the cap of 2, so the model's
        //            second lookup was DENIED mid-analysis
        // 1.0.55.0 - Pre-fetch now gets TagIds. Confirmed from live responses:
        //            get_asset_tags returns NO TagID (only AssetAttributeId); search_tags
        //            is the only source, and history_get needs one. The matcher was also
        //            reading field names that do not exist ("AttributeName" vs
        //            "AssetAttributeName"), so it returned null every time. Asset hints
        //            are seeded up front so the baseline is narrowed during pre-fetch too
        // 1.0.54.0 - METHOD block now yields to the PRE-FETCHED EVIDENCE status list.
        //            The two were contradicting each other and the model followed METHOD
        // 1.0.53.0 - Pre-fetch now states WHICH steps are already done and why anything is
        //            missing. The model was re-calling get_asset_tags despite the tags
        //            being in front of it, because the METHOD block still told it to
        // 1.0.52.0 - Align the STREAMING path with the verdict path. It had none of the
        //            safeguards: no argument clamps, no zero-id refusal, no budget caps,
        //            no attribute-range narrowing, no usability check, no truncation
        //            repair, no coverage note. Only the OUTPUT differs now
        // 1.0.51.0 - Pre-fetch also runs on the STREAMING path (the popup a user watches),
        //            and no longer calls the verdict-only budget check there, which would
        //            have thrown on a null counter
        // 1.0.50.0 - PRE-FETCH (AnalyzePrefetch, default OFF): gather tag list, baseline
        //            and value series before the model runs, so a verdict takes ONE turn
        //            instead of five. Tools stay available as a fallback; pre-fetched
        //            telemetry satisfies the grounding gate exactly as a tool call does
        // 1.0.49.0 - The FINAL turn is output-bound, not input-bound: measured 1,569
        //            output tokens = 33 s against 10,540 input on the same turn. Cap
        //            viz.series at 24 points (trim, do not reject) and tell the model
        //            that everything it writes after the tools answer delays the result
        // 1.0.48.0 - Accept siteId/assetId in the alert context and present them as
        //            authoritative, so the model skips resolution entirely. The station
        //            is a CODE ("SK") and search_sites matches the NAME ("SAKHUN"), so
        //            resolution could not succeed -- 5 failed lookups in one observed run
        // 1.0.47.0 - Fix CS0103 'sw': the 1.0.45.0 tool_done frame was placed in the
        //            VERDICT branch (stopwatch is swAll there, and that path does not
        //            stream). Moved to the streaming branch where sw is in scope
        // 1.0.46.0 - Evidence limit 200 -> 500. `limit` controls how far FORWARD the
        //            series reaches, not payload size: 200 over a 3-day window ended ~29h
        //            before the incidence, which is the reported gap. 500 + 3-day window
        //            reaches the alert at ~7 KB
        // 1.0.45.0 - SSE "step" frames (model_start / model_done / tool_done) carrying
        //            duration, tokens and result size, so the popup can show a LIVE
        //            progress trail instead of a fixed checklist that hides where the
        //            time goes
        // 1.0.44.0 - History never reached the alert: the API returns the OLDEST rows up
        //            to `limit`, so a 7-day window at ~1 sample/13min covered 4.4 days and
        //            ended 58 h short. Window 7d -> 3d (AnalyzeWindowDays), plus a
        //            coverage note appended to any series that stops before the incidence
        // 1.0.43.0 - SAFETY: refuse tool calls with a zero identifier. get_hist_realtime
        //            with asset_id=0 returned a 3,217-char zero-filled shell that passed
        //            the usability check and, being history-class, would have grounded a
        //            verdict on no telemetry
        // 1.0.42.0 - The evidence chain needs NO SiteId: history_get / get_asset_tags /
        //            get_tag_current take TagId or AssetId only. The model was looping on
        //            search_sites (22 calls across 13 runs) with the station CODE, which
        //            search_sites cannot match - it matches SiteName. Only
        //            get_attribute_range needs a site, and the baseline is optional
        // 1.0.41.0 - REMOVE trend_get from the allowlist. 11 calls across every trace,
        //            all returning 45-50 bytes, ~4 attempts per run, each costing a turn
        //            that re-sends the whole conversation. Demotion was not enough
        // 1.0.40.0 - Follow-up chat answers "what is it now" with live_get /
        //            get_tag_current, not get_asset_tags and never history_get: a
        //            current-value question was pulling a whole time series
        // 1.0.39.0 - Allowlist get_tag_current / live_get: the 1.0.27.0 filter reached the
        //            follow-up chat and would have denied the live-value lookups the
        //            follow-up prompt asks for
        // 1.0.38.0 - Follow-up chat scoped to the asset: live-value questions now go
        //            search_assets -> get_asset_tags(AssetId) instead of a site-wide scan,
        //            and resolved ids are reused for the rest of the conversation
        // 1.0.37.0 - A failed OPTIONAL analysis (predict_* / trend_get) now tells the model
        //            explicitly not to cite it: its absence is not a limitation of the
        //            verdict, and reporting it made sound conclusions look under-evidenced
        // 1.0.36.0 - Operator-facing engine names (AiEngineBrand / AiEngineName1 / 2)
        //            replace the internal MCP1/MCP2 plumbing labels in the UI; logs and
        //            traces keep srv1/srv2 for diagnosis
        // 1.0.35.0 - Caveats must state a limitation that actually weakens THIS verdict; a
        //            source that was not needed (REDA logs, a failed optional ML call) is
        //            not a caveat and made sound verdicts look under-evidenced
        // 1.0.34.0 - Allowlist the ML analyses predict_track_health / predict_pm_operation /
        //            predict_pm_cluster (cap 1 each, evidence-class not history-class) and
        //            tell the model when to use them: track leakage type locates the fault
        //            electrically and major_events correlate with the incidence time
        // 1.0.33.0 - Coherence findings are reported as OBSERVATIONS, never as a diagnosis
        //            that a sensor is faulty (telemetry cannot establish that, and the claim
        //            carries liability). Incoherent data now yields INCONCLUSIVE, not
        //            NOT_CONFIRMED -- the latter would assert the asset is sound
        // 1.0.32.0 - SENSOR HEALTH FIRST: check timestamp coherence, physical plausibility,
        //            coupled movement, frozen data and recovery shape before concluding a
        //            field fault -- a failing sensor and a failing asset give the same value
        // 1.0.31.0 - Status reports promptCache state, scope and how to verify it
        // 1.0.30.0 - Fix CS0165: 'raw' was read by the instrumentation finally block
        //            before definite assignment, so every build needed a manual edit
        // 1.0.29.0 - Batch B complete: single cache_control marker on the SYSTEM block
        //            (covers tools + system, 87% of the fixed prompt) via
        //            AnthropicClient 1.0.1.0; the tools-block marker is dropped as
        //            redundant. Still default OFF -- confirm meta.tokensCacheRead
        // 1.0.28.0 - Batch B (partial): optional cache_control on the tools block
        //            (AnthropicPromptCache, default OFF) -- caches ~2,000 tok/turn that
        //            the trace showed being re-sent at full price on every turn.
        //            System-block caching still needs AnthropicClient.cs
        // 1.0.27.0 - Batch A: tool schemas filtered on BOTH paths (was 28/turn on SSE);
        //            METHOD block moved from the per-alert user message into the system
        //            prompt (cacheable share 42% -> 87%); trend_get demoted out of the
        //            history family and marked supplementary; get_asset_tags named the
        //            authoritative TagId source; attribute rows compacted to short keys
        //            with a legend (~55% smaller)
        // 1.0.26.0 - Status reports the log directory, write permission, counters and
        //            the last logging error. A swallowed write failure previously made
        //            "no logs anywhere" impossible to diagnose
        // 1.0.25.0 - Greppable leveled log ai-log-YYYYMMDD.txt (INFO/WARN/ERROR) across
        //            REQ/MCP/TOOL/GROUND/VERDICT/LIFE; daily rotation with background
        //            gzip of older files, retention over the archives, drop counters
        // 1.0.24.0 - Deep instrumentation: exact bytes sent/received per model turn,
        //            REAL token usage from the reply (incl. cache read/write), per
        //            tool-call timing, args, raw vs kept size, narrowed/truncated
        //            flags and reject reason; totals in meta and the envelope trace
        // 1.0.23.0 - get_attribute_range is SITE-scoped server-side (~266KB, then
        //            truncated and discarded). Narrow the result to the AssetIds this
        //            analysis is actually using, client-side
        // 1.0.22.0 - Narrowing guidance was tool-wrong: history_get declares TagId and
        //            NOT AttributeId, trend_get the reverse. An undeclared identifier is
        //            stripped before the call, leaving AssetId alone -> every tag on the
        //            asset -> 103KB reply. State the identifier per tool
        // 1.0.21.0 - Require history calls to name ONE attribute (TagId/AttributeId).
        //            Measured: whole-asset month = 206KB track / 862KB signal / 12MB
        //            point machine, all cut in half in transit; one tag over the window
        //            is 1-12KB and arrives intact
        // 1.0.20.0 - The MCP client splices "[...N chars truncated]" into the MIDDLE of
        //            large JSON results, so head and tail parsed but the whole did not.
        //            Salvage and repair the head instead of discarding the payload
        // 1.0.19.0 - Operational filter was replacing factual EVIDENCE lines: it matched
        //            ordinary words (reset / should / must). Split it -- movement
        //            directives are blocked everywhere, advisory words only in
        //            recommended_action
        // 1.0.18.0 - Tool results arrive as a TRUNCATED document followed by a COMPLETE
        //            one on a later line; the sequential reader aborted on the broken
        //            first document and discarded intact telemetry. Recover
        //            line-delimited documents. Context quote now resolves line+position
        // 1.0.17.0 - Salvage the verdict object when the model wraps it in prose or a
        //            fence, instead of discarding a good verdict over packaging;
        //            report the REAL parser message and the characters at its
        //            position so payload corruption is identified, not guessed
        // 1.0.16.0 - Tool results can arrive as SEVERAL concatenated JSON documents
        //            (one per MCP content block). A single-document parse rejected
        //            the lot, so complete, well-formed telemetry was discarded as
        //            malformed. Parse permissively and judge each document
        // 1.0.15.0 - Evidence limit 500 -> 200 (configurable): a 500-sample series
        //            overran MaxToolResultChars, was truncated, and truncated JSON
        //            cannot parse -- so the richest evidence was being discarded.
        //            toolTrace now reports a reject reason and the payload tail
        // 1.0.14.0 - Evidence gate accepted only top-level data keys or top-level numbers,
        //            so the FRS tools' NESTED payloads were rejected as ungrounded even
        //            though they carry telemetry; accept substantive nested content.
        //            toolTrace now shows a preview of anything still rejected
        // 1.0.13.0 - Allowlist the FRS (MCP2) tools analyse_alert / get_hist_realtime /
        //            fetch_alert_details_frs. They were filtered out of verdict mode, so
        //            the model never saw the tools built for alert analysis and could
        //            never satisfy the history-evidence gate
        // 1.0.12.0 - Hand the model an explicit EdgeX-format (ddMMyyyy_HHmmss) analysis
        //            window; add per-tool outcome trace to meta so "ungrounded" names the
        //            call that returned nothing; history-family cap 2 -> 3
        // 1.0.11.0 - ProduceVerdictJsonAsync: in-process entry point so another
        //            controller can get a verdict without the site calling itself
        // 1.0.10.0 - Fix SSE word-gluing: boundary space was lost between text chunks
        //            ("threshold4.67"); streaming verdict text now reads correctly
        // 1.0.9.0 - viz narrative: trigger_pct / recovery_pct / phase_note + secondary.unit
        //           so the chart shows healthy -> degrade -> trigger -> recovery
        // 1.0.8.0 - Evidence quality: history calls get full scale (limit cap 10 -> 500,
        //           the API max) now that compact columnar output makes it affordable
        // 1.0.7.0 - Latency: cache MCP tool discovery (was re-fetched every request);
        //           send only allowlisted tool schemas to the model in verdict mode
        // 1.0.6.0 - AnalyzeAlertJson service endpoint (single-JSON verdict envelope),
        //           analyze trace log, tool budget + read-only allowlist, grounding gate,
        //           schema-driven tool args, columnar evidence output
        // 1.0.5.0 - AlertLive AI-analyze URL fix (@Url.Action); prompt speed/terseness pass;
        //           window-naming rule (no day-count on averages); viz block for the chart
        // 1.0.4.0 - Follow-up chat in the diagnostic popup (AnalyzeChat)
        // 1.0.3.0 - Preview: site-wise triage scheme
        // 1.0.2.0 - Verdict reframe + main-page polish
        // 1.0.1.0 - UI redesign
        // 1.0.0.0 - Per-alert AI verdict via AiChat/AnalyzeAlert + MCP

        internal static readonly int _maxIter = ReadInt("MaxIter", 10);

        internal static readonly int _maxHistory = ReadInt("MaxHistoryTurns", 6);

        internal static readonly int _maxToolLen = ReadInt("MaxToolResultChars", 20000);

        internal static readonly int _warnTokens = ReadInt("WarnTokenThreshold", 40000);

        // v1.0.160.194: chat-loop bounds. Applied to the CHAT path only (rawJsonMode == false);
        // analyze keeps _maxIter unchanged. _chatLoopGuard is the master kill-switch (field revert);
        // when false, chat reverts to _maxIter and all guard branches below are skipped -- exactly .193.
        // Values are clamped so a bad config can never skip the loop or force-answer on turn 0.
        internal static readonly int _chatMaxIter = Math.Max(2, ReadInt("ChatMaxIter", 40));

        internal static readonly int _chatMaxWallSec = Math.Max(5, ReadInt("ChatMaxWallSec", 90));

        internal static readonly bool _chatLoopGuard = ReadBool("ChatLoopGuard", true);


        // ── Tool logging ───────────────────────────────────────
        // Enable with: <add key="AiToolLoggingEnabled" value="true" />
        internal static readonly bool _toolLoggingEnabled =
            ReadBool("AiToolLoggingEnabled", false);

        internal static readonly int _maxToolLogChars =
            ReadInt("AiToolLogMaxChars", 20000);

        internal static readonly object _toolLogLock = new object();

        internal static readonly string _toolLogDir =
            System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "App_Data", "AiToolLogs");


        internal static readonly Lazy<AnthropicClient> _ai =
            new Lazy<AnthropicClient>(CreateAiClient);


        // SYNTHESIS CLIENT. Same class, smaller max_tokens. The global 8096 is right
        // for follow-up chat and far too loose for a verdict object: an output ceiling
        // is the only hard control over the slowest phase of the analysis. Constructing
        // a second client avoids touching AnthropicClient's signature.
        internal static readonly Lazy<AnthropicClient> _aiSynth =
            new Lazy<AnthropicClient>(CreateSynthesisClient);


        // ---- OpenAI provider -------------------------------------------------
        // OpenAiClient speaks OpenAI on the wire and returns ANTHROPIC-SHAPED JSON,
        // so the agentic loop, tool policy, grounding gate and verdict validation are
        // untouched by the switch. Selected per request, so the two can be compared
        // on the SAME alert rather than on different ones.
        //   <add key="OpenAiApiKey"  value="sk-..." />
        //   <add key="OpenAiModel"   value="gpt-5.4" />
        //   <add key="OpenAiBaseUrl" value="https://api.openai.com/v1" />
        internal static readonly Lazy<OpenAiClient> _oai =
            new Lazy<OpenAiClient>(() => CreateOpenAiClient(ReadInt("AnthropicMaxTokens", 8096)));

        internal static readonly Lazy<OpenAiClient> _oaiSynth =
            new Lazy<OpenAiClient>(() => CreateOpenAiClient(ClampCfg(ReadInt("AnalyzeSynthesisMaxTokens", 1200), 400, 4000)));

        // v1.0.134.0: DeepSeek provider (OpenAI-compatible). Branded "ENERGY7 TESLA 2".
        internal static readonly Lazy<DeepSeekClient> _deepseek =
            new Lazy<DeepSeekClient>(() => CreateDeepSeekClient(ReadInt("DeepSeekMaxTokens", 8096)));

        internal static readonly Lazy<DeepSeekClient> _deepseekSynth =
            new Lazy<DeepSeekClient>(() => CreateDeepSeekClient(ClampCfg(ReadInt("AnalyzeSynthesisMaxTokens", 1200), 400, 4000)));


        // Default provider; a request may override it (UI toggle).
        internal static readonly string _aiProviderDefault = ReadStr("AiProvider", "quantum10");   // v1.0.160.202: QUANTUM (DeepSeek Pro) is the default for everyone


        // v1.0.134.0: three base providers. UI sends "anthropic" | "openai" | "deepseek"
        // (branded ENERGY7 NEXUS / ORION / QUANTUM). Normalize defensively.
        // v1.0.158.0: NEXUS/ORION model families. Each variant reuses the anthropic/openai
        // CLIENT but with its own model string (see AnalysisModelOverride). ProviderKind maps
        // every variant to its transport; _provider keeps the raw value for the model pick.
        //   nexus16 -> Anthropic Opus 4.6 | nexus19 -> Opus 4.8 | nexus49 -> Fable 5
        //   orion16 -> OpenAI GPT-5.6      | orion17 -> GPT-5.6 Sol
        internal string ProviderKind
        {
            get
            {
                string p = (Sess._provider ?? _aiProviderDefault ?? "anthropic").Trim().ToLowerInvariant();
                if (p.Contains("deep")) { return "deepseek"; }
                if (p == "orion16" || p == "orion17") { return "openai"; }   // ORION variants = OpenAI transport
                if (p == "nexus16" || p == "nexus19" || p == "nexus49") { return "anthropic"; }  // NEXUS variants = Anthropic transport
                // v1.0.160.9: bare BRAND labels. The app/API sends QUANTUM/NEXUS/ORION (brand),
                // not the provider value (deepseek/openai/anthropic). Without these a bare
                // "quantum" fell through to the final "anthropic" and ran on the Anthropic key.
                // Placed AFTER the numbered-variant checks so their model overrides are untouched;
                // v1.0.160.11: bare-brand match must be CONTAINS, not ==, or the full label the
                // picker sends ("ENERGY7 QUANTUM") fails the equality and falls through to the
                // anthropic default -- reintroducing the exact QUANTUM->anthropic bug. Contains
                // handles bare ("QUANTUM") AND prefixed ("ENERGY7 QUANTUM"/"tesla 2"). Numbered
                // variant checks above still run first, so their model overrides are untouched.
                // bare brands use each transport's base model (AnalysisModelOverride returns null).
                if (p.Contains("quantum") || p.Contains("tesla 2") || p.Contains("tesla2")) { return "deepseek"; }   // QUANTUM = DeepSeek transport
                if (p.Contains("orion") || p.Contains("tesla 1") || p.Contains("tesla1")) { return "openai"; }       // ORION = OpenAI transport
                if (p.Contains("nexus") || p.Contains("ultra")) { return "anthropic"; }                              // NEXUS = Anthropic transport
                if (p == "openai" || p == "gpt" || p.Contains("gpt")) { return "openai"; }
                return "anthropic";
            }
        }

        // v1.0.158.0: model-string override for the NEXUS/ORION variants. Returns null for the
        // base providers (NEXUS/ORION/QUANTUM use their configured default model). Each is
        // config-overridable so the exact model IDs can be corrected without a rebuild.
        internal string AnalysisModelOverride
        {
            get
            {
                string p = (Sess._provider ?? _aiProviderDefault ?? "").Trim().ToLowerInvariant();
                if (p == "nexus16") { return ReadStr("ModelNexus16", "claude-opus-4-6"); }
                if (p == "nexus19") { return ReadStr("ModelNexus19", "claude-opus-4-8"); }
                if (p == "nexus49") { return ReadStr("ModelNexus49", "claude-fable-5"); }
                if (p == "orion16") { return ReadStr("ModelOrion16", "gpt-5.6"); }
                if (p == "orion17") { return ReadStr("ModelOrion17", "gpt-5.6-sol"); }
                // v1.0.160.42: QUANTUM 1.1 = DeepSeek Flash. Config-overridable so the exact
                // model id can be corrected without a rebuild.
                if (p == "quantum11") { return ReadStr("ModelQuantum11", "deepseek-v4-flash"); }
                return null;
            }
        }

        internal bool UseOpenAi { get { return ProviderKind == "openai"; } }

        internal bool UseDeepSeek { get { return ProviderKind == "deepseek"; } }

        // v1.0.160.42: effective thinking state -- request toggle wins, else the config default.
        internal bool ThinkOn { get { return Sess._thinkReq.HasValue ? Sess._thinkReq.Value : ReadBool("DeepSeekThinking", false); } }


        // ── MCP Server 1: direct HTTP POST (your existing RDPMS server) ──
        internal static readonly Lazy<McpClient> _mcp =
            new Lazy<McpClient>(() =>
            {
                string url = ConfigurationManager.AppSettings["McpServerUrl"];
                if (string.IsNullOrEmpty(url))
                    throw new Exception("McpServerUrl missing in web.config");
                return new McpClient(url);
            });


        // ── MCP Server 2: standard SSE protocol (ngrok server) ──
        // v1.0.160.20: srv2 (MCP2) is used ONLY when McpServerUrl2 is configured (non-blank).
        // Absent or whitespace = srv2 DISABLED. Read once at startup; the URL's presence IS the
        // switch (no separate flag). Every _mcp2.Value access is guarded by this.
        internal static readonly bool _srv2Enabled =
            !string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings["McpServerUrl2"]);


        internal static readonly Lazy<McpSseClient> _mcp2 =
            new Lazy<McpSseClient>(() =>
            {
                string url = ConfigurationManager.AppSettings["McpServerUrl2"];
                // Invariant defence: all callers are gated by _srv2Enabled, so this should not run
                // when srv2 is disabled. If a future unguarded access reaches here with no URL, fail
                // loudly rather than build a client on a blank endpoint.
                if (string.IsNullOrWhiteSpace(url))
                    throw new InvalidOperationException("srv2 disabled (McpServerUrl2 not set)");
                return new McpSseClient(url.Trim());
            });


        // Tool routing map
        internal static readonly Dictionary<string, int> _toolOwner =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        internal static readonly object _toolOwnerLock = new object();


        internal class ToolLoadResult
        {
            public JArray Tools { get; set; }
            public int Count1 { get; set; }
            public int Count2 { get; set; }
            public string Error1 { get; set; }
            public string Error2 { get; set; }
            // per-request routing snapshot: immune to another request rebuilding
            // the shared _toolOwner dictionary mid-flight
            public Dictionary<string, int> OwnerMap { get; set; }
            // per-tool DECLARED argument names from the discovered inputSchema
            // (wire keys: "name" / "inputSchema" / "properties"). Used to inject
            // and keep ONLY arguments a tool actually accepts -- covers MCP2 too,
            // since it comes from discovery rather than hardcoded assumptions.
            public Dictionary<string, HashSet<string>> ArgMap { get; set; }
        }


        // ═════════════════════════════════════════════════════════
        //  LOAD + MERGE TOOLS
        // ═════════════════════════════════════════════════════════
        // Tool discovery was re-run on EVERY request (2 x ListToolsAsync over
        // HTTPS for the same 22 schemas). Cached for AnalyzeToolCacheSec
        // (default 300). Only a GOOD result is cached -- a partial or failed
        // discovery is never stored, so an MCP outage cannot be pinned in.
        internal static readonly int _toolCacheSec = ReadInt("AnalyzeToolCacheSec", 300);

        internal static readonly object _toolCacheLock = new object();

        internal static ToolLoadResult _toolCache;

        internal static DateTime _toolCacheAt = DateTime.MinValue;


        // ═════════════════════════════════════════════════════════
        //  ROUTE TOOL CALL
        // ═════════════════════════════════════════════════════════
        // The MCP/AI clients are static singletons whose thread-safety contract
        // is not in hand. Until their sources are supplied, calls to each client
        // are SERIALIZED here (conservative): correctness first, throughput later.
        // Disable only after certifying the clients: AnalyzeJsonSerializeClients=false.
        // Strict parse: only an explicit, well-formed "false" disables verdict-path
        // serialization. Any other value (including malformed) keeps it ENABLED.
        internal static readonly bool _serializeClients = !string.Equals(
            (ConfigurationManager.AppSettings["AnalyzeJsonSerializeClients"] ?? "").Trim(),
            "false", StringComparison.OrdinalIgnoreCase);

        internal static readonly System.Threading.SemaphoreSlim _mcp1Lock = new System.Threading.SemaphoreSlim(1, 1);

        internal static readonly System.Threading.SemaphoreSlim _mcp2Lock = new System.Threading.SemaphoreSlim(1, 1);

        internal static readonly System.Threading.SemaphoreSlim _aiLock = new System.Threading.SemaphoreSlim(1, 1);


        // BOUNDED acquisition. A never-completing client call must not wedge
        // the client for the whole app (that hazard was introduced by the
        // unbounded lock in rev 10). If the lock cannot be taken within
        // AnalyzeJsonClientLockMs, the call proceeds WITHOUT serialization:
        // degraded isolation is strictly better than a global stall, and the
        // pre-rev10 behaviour was unserialized anyway.
        internal static readonly int _clientLockMs = ClampCfg(ReadInt("AnalyzeJsonClientLockMs", 20000), 1000, 120000);


        // v1.0.160.172 (Fix 3): cache the static per-asset range calls (safe bands / topology barely
        // change). Reused across every alert on the same asset, so it removes those calls from the
        // serialized prefetch entirely. Keyed by tool+args, so correctness is preserved; TTL-bounded.
        internal static readonly int _prefetchCacheTtlSec = ClampCfg(ReadInt("PrefetchRangeCacheSec", 900), 0, 86400);

        internal static readonly System.Collections.Concurrent.ConcurrentDictionary<string, KeyValuePair<DateTime, string>> _prefetchCache
            = new System.Collections.Concurrent.ConcurrentDictionary<string, KeyValuePair<DateTime, string>>();


        // v1.0.160.172 (Fix 4): per-call timeout + one retry. Applied ONLY when serialization is OFF
        // (parallel prefetch) -- there it caps the slowest call so one stalled tool cannot dominate the
        // batch. In serialized mode it is disabled, because a call legitimately waits up to
        // _clientLockMs for the lock and a timeout would kill queued calls prematurely.
        internal static readonly int _prefetchTimeoutMs = ClampCfg(ReadInt("PrefetchCallTimeoutMs", 12000), 2000, 60000);

        internal static readonly int _prefetchRetries = ClampCfg(ReadInt("PrefetchCallRetries", 1), 0, 3);


        // v1.0.160.184: the only fields the four PM visuals read. Everything else on an op --
        // above all the current[]/voltage[] sample arrays -- is removed before the payload leaves
        // the server. cond_notes is kept (added .184): it carries the detector's own reasoning for
        // a flagged stroke, which is the one piece of an op worth reading in words.
        // Returns the op count so the caller can log what it served.
        internal static readonly string[] PmOpKeepFields =
        {
            "time", "direction", "grade", "confirmed", "condition", "cond_notes", "fault_type", "reason",
            "features", "current"
        };


        // v1.0.160.185: a tile is ~96 px wide, so 40 points carry the stroke's shape exactly as
        // well as 486 do. Raising this raises the payload linearly -- see the .185 note above.
        internal const int PmCurveMaxPoints = 40;


        // v1.0.160.109 CUT A: the ONE history-window builder. Typed DateTime in, so the timezone
        // decision is made in exactly one place and cannot be re-derived (or forgotten) per call site.
        //
        // EdgeX MCP `history_get` RELABELS the naive ddMMyyyy_HHmmss string as UTC
        // (MCPHelper.Compact.cs L56-57: SpecifyKind(start, DateTimeKind.Utc)) instead of converting,
        // which shifted every window +5.5 h. Its sibling tools (summary_get, gated_get,
        // get_attribute_range_history) take a naive DateTime and are CORRECT -- so the compensation
        // must be applied HERE, to history_get only. Compensating globally would break the others.
        // Flip HistGetSendsUtc to false the moment EdgeX ships Kind.Local, or the window shifts
        // -5.5 h instead of +5.5 h.
        internal static readonly bool _histGetSendsUtc = ReadBool("HistGetSendsUtc", true);

        // HARD background bound by RESERVATION: each admitted request reserves
        // MaxChunk credits up front (its worst-case in-flight abandonable work:
        // one model call OR one tool chunk). Credits are released only via the
        // wrapper-loop completion continuation -- which runs AFTER any
        // abandonment registration -- minus the weight of actually-abandoned
        // ops, each of which releases its own weight when the REAL client task
        // completes. Invariant: reserved >= live abandoned work at all times;
        // admission against the cap is therefore a true bound with no gap and
        // no double count. No TTL: never-completing ops keep refusing new work
        // (the fix is the client timeout, not forgetting live work).
        internal const int _jsonMaxChunk = 4;

        internal static int _jsonBgReserved;

        internal static readonly bool _jsonParallelTools = ReadBool("AnalyzeJsonParallelTools", false);

        internal static readonly object _jsonDiagLock = new object();

        // v1.0.160.78: per-run MCP call collection for AI_ALERT_McpCall. LogToolEvent is STATIC, so
        // this cannot be an instance list. Bounded: capped per run, pruned by age, removed on drain --
        // a failed run never drains, and without those guards the pool would grow without limit.
        internal static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Tuple<DateTime, List<JObject>>>
            _mcpCollect = new System.Collections.Concurrent.ConcurrentDictionary<string, Tuple<DateTime, List<JObject>>>();

        internal static readonly int _mcpMaxPerRun = ClampCfg(ReadInt("McpCallMaxPerRun", 40), 1, 200);


        // ---- analyze trace: per-request JSONL (prompts, turns, tools, timings) ----
        internal static readonly bool _analyzeTraceEnabled = ReadBool("AnalyzeTraceEnabled", true);

        internal static readonly bool _traceDeep = ReadBool("AnalyzeTraceDeep", true);

        // <add key="AnthropicPromptCache" value="true" />
        // Anthropic caches the prefix tools -> system -> messages, so ONE marker on
        // the system block covers BOTH the tool schemas and the system prompt --
        // which after 1.0.27.0 is 87% of the fixed prompt. Requires AnthropicClient
        // 1.0.1.0 or later (the cacheSystem parameter). Verify with
        // meta.tokensCacheRead before relying on it.
        internal static readonly bool _promptCache = ReadBool("AnthropicPromptCache", false);

        // v1.0.113.0: use the 1-HOUR cache TTL instead of the default 5-minute one. For verdict
        // traffic spread out over minutes/hours (gaps > 5 min) the 5-min entry expires between
        // alerts, so the extended TTL is what actually yields cache reads. Requires AnthropicClient
        // 1.0.2.0+ (the extendedCacheTtl parameter + beta header); with an older Domain.dll this
        // flag is a silent no-op (the 4-arg overload is used). Only meaningful when
        // AnthropicPromptCache is also true. Costs 2x on cache WRITES, but you read far more than
        // you write at this cadence.
        internal static readonly bool _promptCache1h = ReadBool("AnthropicPromptCache1h", false);

        // v1.0.108.0 / 1.0.111.0: emit a Hinglish translation alongside the English verdict.
        // DEFAULT OFF (per review) - enable via appSettings "BilingualHindi"=true only after the
        // bilingual canary passes. The translation runs on the ALREADY-SANITIZED verdict, under
        // the AI lock, with a dedicated timeout, and its tokens are accounted.
        internal static readonly bool _bilingualHindi = ReadBool("BilingualHindi", false);

        internal static readonly int _hindiTimeoutSec = ReadInt("HindiTranslateTimeoutSec", 20);

        // v1.0.111.0: triage (maintenance-priority hint) DEFAULT OFF - it requires owner-approved
        // severity thresholds. Enable via appSettings "EnableTriage"=true once those are set.
        internal static readonly bool _enableTriage = ReadBool("EnableTriage", false);

        // v1.0.160.154 Layer A: per-asset FRS alert lookback (design 6b defaults; OFF until canary).
        internal static readonly bool _assetHistEnabled = ReadBool("AssetAlertHistoryEnabled", false);

        // v1.0.160.162: greeting header, OFF by default.
        internal static readonly bool _greetingEnabled = ReadBool("AnalyzeGreetingEnabled", false);

        // v1.0.160.163: how the name is addressed, and how much warmth. Honorific default "sir"
        // (owner). Tone: plain = respectful, no humour ever; light = a dry touch on safe
        // low-stakes cases only; off = suppress. Read once, static, like the other greeting cfg.
        internal static readonly string _greetHonorific = ReadStr("GreetingHonorific", "sir");

        internal static readonly string _greetTone = ReadStr("GreetingTone", "light");

        internal static readonly int _assetHistDays = ClampCfg(ReadInt("AssetHistoryLookbackDays", 30), 7, 90);


        // PRE-FETCH. Every verdict needs the same four things: the asset's tag list,
        // the baseline range, the alert record and the value series. Letting the model
        // discover that costs 4-5 round trips, and 42% of the prompt is tool schemas
        // that exist only so it can choose. Fetching them here collapses the analysis
        // to ONE turn. Tools stay available, so the model can still ask for something
        // the pre-fetch did not cover -- it just pays a turn only when it needs one.
        // <add key="AnalyzePrefetch" value="true" />
        internal static readonly bool _prefetch = ReadBool("AnalyzePrefetch", false);

        // v1.0.142.0: per-alert GROUNDING carry-forward. The initial analysis often
        // resolves identity mid-loop (alert record / search results carry SiteId,
        // AssetId, cause) that the caller's context lacked. Harvest those ids into a
        // small static cache so FOLLOW-UP CHAT inherits them instead of re-resolving
        // (which burned tool budgets and tripped the zero-id guard).
        internal static readonly System.Collections.Concurrent.ConcurrentDictionary<string, JObject> _groundingByAlert =
            new System.Collections.Concurrent.ConcurrentDictionary<string, JObject>(StringComparer.Ordinal);


        internal static readonly string[] _groundingTools = { "alert_get", "fetch_alert_details_frs", "search_assets", "search_sites", "get_frs_alerts" };

        internal static readonly System.Text.RegularExpressions.Regex _histNRx =
            new System.Text.RegularExpressions.Regex("\"n\"\\s*:\\s*(\\d+)", System.Text.RegularExpressions.RegexOptions.Compiled);

        internal static readonly System.Text.RegularExpressions.Regex _histToRx =
            new System.Text.RegularExpressions.Regex("\"to\"\\s*:\\s*(\\d+)", System.Text.RegularExpressions.RegexOptions.Compiled);

        internal static readonly System.Text.RegularExpressions.Regex _histTsRx =
            new System.Text.RegularExpressions.Regex("\"ts\"\\s*:\\s*\\[([^\\]]*)\\]", System.Text.RegularExpressions.RegexOptions.Compiled);

        internal static readonly System.Text.RegularExpressions.Regex _histTagRx =
            new System.Text.RegularExpressions.Regex("\"tag\"\\s*:\\s*\"?(\\d+)", System.Text.RegularExpressions.RegexOptions.Compiled);

        // v1.0.159.0 (C1): jump to the page holding the END of the requested window. Page 1 gave n
        // rows spanning firstTs..lastTs, so the sample rate is known and the rows remaining to the
        // window end are rate * (winTo - lastTs) -- that gives the page directly instead of walking
        // every page. A few bounded probes correct the estimate, because change-on-value storage
        // bunches samples around activity and density is not uniform.
        // v1.0.159.0 (C2/C3): the MCP server already exposes gated_get and summary_get.
        // gated_get aggregates an attribute ONLY while a sibling DataLogger gate attribute holds a
        // value -- gate on TPR==1 and TimescaleDB returns the TRAIN-FREE statistics that this
        // controller has been computing in C# since 1.0.152.0. summary_get gives the multi-day
        // baseline without spending any of the 500-row budget, which matters now that C1 pages the
        // raw rows to the EVENT end of the window.
        // Both are ADDITIVE here: the deterministic mask still runs and still produces refValue.
        // The gated number is carried as evidence and cross-checked in the log, so we can prove the
        // two agree on real alerts before anything is removed.
        // v1.0.160.0: GATE MAP ported verbatim from the PROVEN Energy7In.GatedAverage service
        // (v1.0.16.15). This is the authority on what makes a reading MEANINGFUL, and it corrects a
        // real error in this controller: we gated EVERYTHING on TPR. TPR proves only that a TRACK is
        // clear. A signal aspect is proved by ITS OWN ECR; route lamps by UECR; shunt by ONECR /
        // OFFECR; point detection by NWKR/RWKR with the OTHER one proven DOWN; charger and
        // point-operation values are ungated. Gating a signal on TPR is meaningless -- which is why
        // train movement and an active aspect could not be told apart.
        internal static readonly Dictionary<int, string> _gateById = new Dictionary<int, string>
        {
            // TRACK CIRCUIT -> TPR (meaningful only while the track is clear)
            { 1, "TPR" }, { 2, "TPR" }, { 3, "TPR" }, { 4, "TPR" },
            { 6, "TPR" }, { 249, "TPR" }, { 570, "TPR" }, { 571, "TPR" },
            // SIGNAL ASPECTS -> that aspect's own ECR
            { 9, "RECR" }, { 10, "RECR" },
            { 11, "DECR" }, { 12, "DECR" }, { 328, "DECR" },
            { 13, "HECR" }, { 14, "HECR" }, { 329, "HECR" },
            { 15, "HHECR" }, { 16, "HHECR" }, { 327, "HHECR" },
            // ROUTE LAMPS -> UECR
            { 497, "UECR" }, { 356, "UECR" }, { 573, "UECR" }, { 326, "UECR" },
            { 574, "UECR" }, { 332, "UECR" }, { 602, "UECR" }, { 357, "UECR" },
            { 613, "UECR" }, { 358, "UECR" }, { 632, "UECR" }, { 631, "UECR" },
            // ROUTE PROVING RELAYS -> own route HR
            { 601, "AUHR" }, { 330, "BUHR" }, { 333, "CUHR" }, { 603, "DUHR" }, { 608, "EUHR" },
            // CALLING-ON
            { 611, "Co_HECR" }, { 610, "Co_HECR" }, { 612, "Co_HECR" },
            // SHUNT
            { 51, "ONECR" }, { 52, "ONECR" },
            { 53, "OFFECR" }, { 54, "OFFECR" }, { 630, "OFFECR" },
            // POINT DETECTION -> combined NWKR / RWKR (bare token matches "Combined-NWKR")
            { 25, "NWKR" }, { 576, "NWKR" }, { 27, "NWKR" }, { 578, "NWKR" },
            { 26, "RWKR" }, { 577, "RWKR" }, { 28, "RWKR" }, { 579, "RWKR" }
        };

        internal static readonly HashSet<int> _gateUngatedIds = new HashSet<int>
        {
            5, 344, 569,                                     // charger mA / V / OP V
            1002, 1005, 2002, 3002, 3005, 4002,              // point operation values
            6002, 6005, 7002, 8002, 8005, 9002
        };


        // v1.0.160.3: the override list, so the client-side exception actually GRANTS access.
        // Without this the view un-hid the picker while GateProviderForRole still forced QUANTUM --
        // the user saw the buttons but the server ignored the choice. Defaults to the address the
        // other team hard-coded, so behaviour matches with no Web.config change required.
        internal static readonly string _siteKeepOverride =
            (ConfigurationManager.AppSettings["SiteKeepingOverrideEmails"] ?? "9410500007@energy7.in");


        internal const int InstructionEchoMinWords = 12;


        internal static readonly Regex _numberedRuleRx = new Regex(@"^\s*\d+[.)]\s", RegexOptions.Compiled);


        // Prose backstop: the instruction-echo scrub matches >=12-word verbatim runs and cannot catch
        // a three-token rule id. A rule id in prose discloses internal doctrine taxonomy.
        internal static readonly Regex _wisdomIdRx = new Regex(
            @"\[?\b(?:DOC|FLD|CIR|CHK|LRN)-\d{2,4}\b\]?", RegexOptions.IgnoreCase | RegexOptions.Compiled);


        // v1.0.160.2: ALIAS BRIDGE. Alert cards and derived formulas are written in ALIAS names
        // (VTC TFC O/P, ITC FEED END) while telemetry arrives under RDPMS TITLES (Charger OP V,
        // If mA). On 2026-08-08 alert 484640 this cost a verdict: the model reported "VTC TFC O/P
        // and VTC CH FEED END unavailable" and returned INCONCLUSIVE at 40% -- while Charger OP V
        // (7.223 vs the card's 7.22) and Choke V (1.007 vs 1.01) had BOTH been fetched with 12 and
        // 229 samples. The data was there; only the naming was missing. Source: RDPMS_Attributes
        // in the RDSO master workbook.
        internal static readonly string[,] _aliasPairs = new string[,]
        {
            { "If mA",        "ITC FEED END" },
            { "Ir mA",        "ITC RELAY END" },
            { "Vf",           "VTC FEED END" },
            { "Vr",           "VTC RELAY END" },
            { "Choke V",      "VTC CH FEED END" },
            { "Charger mA",   "ITC TFC O/P" },
            { "Charger OP V", "VTC TFC O/P" },
            { "Charger V",    "VTC TFC I/P" },
            { "TPR V",        "VTC 24 DC TPR I/P" },
            { "TPR V (Loc)",  "VTC 24 DC LOC" }
        };


        internal static readonly string[] _trendTools = { "history_get", "get_attribute_range_history", "get_hist_realtime" };

        internal static readonly string[] _tsKeys = { "ts", "timestamp", "time", "t", "dt", "TimeStamp", "Time" };

        internal static readonly string[] _valKeys = { "value", "val", "v", "Value", "Val" };


        // v1.0.152.3: synthesize the DERIVED value series (e.g. RRAIL) from its driver series,
        // applying the alert card's own formula at each timestamp. The derived attribute has no
        // telemetry tag of its own, so without this the chart shows only the drivers and omits
        // the actual alerting value. Time-alignment: build the union of driver timestamps; at
        // each, bind every formula variable to that driver's value at-or-nearest that time
        // (within a tolerance), evaluate the formula. Returns a points JArray [[isoTs,val]..] or
        // null if the formula/drivers cannot be resolved.
        // v1.0.160.114 CUT E: cap and spike-preserving reduction. Decimation (every Nth point) can
        // drop a single-sample breach entirely; min/max per bucket cannot, because the extreme of
        // each bucket is always kept. Output is [epochSec, value] pairs -- timestamped, so the chart
        // cannot silently rescale time.
        internal static readonly int _vizSeriesMaxPts = ClampCfg(ReadInt("VizSeriesMaxPoints", 120), 20, 400);


        // v1.0.151.0: SIGNAL ASPECT DETECTION -- ported from the tested Debouncer
        // (ProcessService AspectPattern / drive-proving logic). A signal aspect is ACTIVE
        // only when its ECR relay is PROVED: RG<-RECR, HG<-HR+HECR, DG<-DR+DECR, HHG<-HR+
        // HHR+HHECR, shunt OFF<-HR+OFFECR. Averaging a signal's V/mA is only meaningful
        // while the RESPECTIVE aspect is active -- so the analysis prompt is told to average
        // only aspect-active samples and which relay proves each aspect. (Point machine:
        // only the commanded-end operation samples, i.e. the CR/KR side that was driven.)
        internal static readonly string[][] _aspectProof = new string[][] {
            new string[] { "RG",  "RECR" },
            new string[] { "HG",  "HR", "HECR" },
            new string[] { "DG",  "DR", "DECR" },
            new string[] { "HHG", "HR", "HHR", "HHECR" },
            new string[] { "SH_OFF", "HR", "OFFECR" }
        };


        // v1.0.151.1: POINT-MACHINE INDICATION -- the point equivalent of a signal's
        // active aspect. From the Debouncer CR/KR pair model (ProcessService PMPendingBatch):
        // NWKR = Normal indication relay (point proved sitting NORMAL), RWKR = Reverse
        // indication relay (proved REVERSE); NWCR/RWCR are the control (commanded) relays.
        // A point operation's electricals are only meaningful for the end whose INDICATION
        // relay is up -- averaging current/voltage when no indication is proved (mid-stroke,
        // or indication lost) is not the operation's signature.
        internal static readonly string[][] _pointIndication = new string[][] {
            new string[] { "NORMAL",  "NWKR", "(commanded by NWCR)" },
            new string[] { "REVERSE", "RWKR", "(commanded by RWCR)" }
        };

        internal static readonly string _engineBrand = ReadStr("AiEngineBrand", "ENERGY7 ULTRA");

        internal static readonly string _engineName1 = ReadStr("AiEngineName1", "ENERGY7 ULTRA 1");

        internal static readonly string _engineName2 = ReadStr("AiEngineName2", "ENERGY7 ULTRA 2");


        internal static readonly int _traceMaxMB = ReadInt("AnalyzeTraceMaxMB", 64);


        // ==================================================================
        // LEVELED LOG  ->  App_Data/AiToolLogs/ai-log-YYYYMMDD.txt
        //
        // Companion to the JSONL trace: the trace answers "what were the
        // numbers", this answers "what went wrong and where". One line per
        // event, fixed-width columns so grep and awk both work:
        //
        //   2026-08-01 14:23:05.123 [WARN ] [GROUND ] [dryrun-1b83a5] message
        //
        //   grep " \[ERROR\] " ai-log-20260801.txt
        //   grep " \[MCP    \] " ai-log-*.txt
        //   grep "dryrun-1b83a5" ai-log-*.txt      <- one whole analysis
        //
        // Files older than today are gzipped in the background on the first
        // write of a new day, then swept by AnalyzeTraceRetainDays.
        // ==================================================================
        internal static readonly string _aiLogMinLevel =
            (ConfigurationManager.AppSettings["AiLogMinLevel"] ?? "INFO").Trim().ToUpperInvariant();

        internal static int _aiLogDropped;          // non-zero is always actionable

        internal static int _aiLogWritten;

        internal static DateTime _aiLogLastDay = DateTime.MinValue;

        internal static string _aiLogLastError;   // surfaced by Status

        internal static readonly object _aiLogDayLock = new object();


        internal static readonly int _traceRetainDays = ReadInt("AnalyzeTraceRetainDays", 14);


        // free-text analogue of the tool log's CloneAndRedact: strip
        // key/token/password-like assignments before the prompt is persisted.
        internal const string _credWords = "api[_-]?key|apikey|access[_-]?token|token|password|passwd|pwd|secret|authorization|auth|credential";

        // quoted JSON form: "token": "abc123"  /  'secret' = 'x y z'
        internal static readonly System.Text.RegularExpressions.Regex _redactQuoted =
            new System.Text.RegularExpressions.Regex(
                "(?i)([\"']?(?:" + _credWords + ")[\"']?\\s*[:=]\\s*)([\"'])(?:\\\\.|[^\"'])*\\2",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        // double-quoted value that may contain apostrophes: "token":"O'Brien"
        internal static readonly System.Text.RegularExpressions.Regex _redactDq =
            new System.Text.RegularExpressions.Regex(
                "(?i)([\"]?(?:" + _credWords + ")[\"]?\\s*[:=]\\s*)\"(?:\\\\.|[^\"])*\"",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        // CLI flag form: --token abcdefgh
        internal static readonly System.Text.RegularExpressions.Regex _redactFlag =
            new System.Text.RegularExpressions.Regex(
                "(?i)(--?(?:" + _credWords + ")\\s+)\\S+",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        // bare form: token=abc123 / secret: abc123
        // unquoted JSON scalar: "token": 123456789
        internal static readonly System.Text.RegularExpressions.Regex _redactNumeric =
            new System.Text.RegularExpressions.Regex(
                "(?i)([\"']?(?:" + _credWords + ")[\"']?\\s*[:=]\\s*)(-?[0-9][0-9\\.]*(?:[eE][-+]?[0-9]+)?)",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        internal static readonly System.Text.RegularExpressions.Regex _redactBare =
            new System.Text.RegularExpressions.Regex(
                "(?i)((?:" + _credWords + ")\\s*[:=]\\s*)\\S+",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        // scheme form: Bearer abc123 / Basic dGVzdA==
        internal static readonly System.Text.RegularExpressions.Regex _redactScheme =
            new System.Text.RegularExpressions.Regex(
                "(?i)\\b(bearer|basic)\\s+[A-Za-z0-9\\-\\._~\\+/=]{3,}",
                System.Text.RegularExpressions.RegexOptions.Compiled);


        // Only messages we generate ourselves may leave the endpoint; everything
        // else is logged server-side and replaced. Exact/prefix whitelist -- no
        // substring matching.
        internal static readonly string[] _jsonErrExact =
        {
            "Rate limit. Wait ~60s.", "AI auth failed.", "Auth failed. Check web.config.",
            "API overloaded. Try again.", "AI request timed out",
            "verdict not valid JSON",
            "no verdict text produced (empty response or step limit)", "client busy",
            "no successful telemetry tool call; verdict ungrounded",
            "no usable history/baseline/alert evidence; verdict ungrounded"
        };


        // mechanical cost bounds, overflow-safe: limit-like keys of ANY incoming
        // type normalized to an integer in [1,10] via decimal-range checks BEFORE
        // any cast; skip/page/offset capped; ID arrays truncated to 20; Limit
        // INJECTED for the history family when absent ("Limit" -- the only
        // consuming code in hand reads args["Limit"]; the packaged registry
        // schema shows lowercase "limit", unresolvable without the missing
        // handler partial -- recorded residual); start/end date pairs clamped to
        // a 15-day span PRESERVING the incoming format (ddMMyyyy_HHmmss or
        // ISO-like); unknown formats are left untouched (recorded residual).
        internal static readonly string[] _limitKeys =
            { "limit", "max_results", "maxresults", "count", "pagesize", "page_size", "top", "n" };

        internal static readonly string[] _skipKeys = { "skip", "page", "offset" };

        internal static readonly string[] _startKeys =
            { "startdate", "start", "from", "fromdate", "start_time", "starttime" };

        internal static readonly string[] _endKeys =
            { "enddate", "end", "to", "todate", "end_time", "endtime" };

        internal const string _ddFmt = AiTime._ddFmt;   // value now owned by E7.AiCore.AiTime (1.0.162.0)


        // The history API returns the OLDEST rows up to `limit` (MCPHelper.Compact:
        // limit = Math.Min(lim, 500), page defaults to 1). So `limit` is not a size
        // guard -- it decides HOW FAR FORWARD IN TIME the series reaches, and a limit
        // smaller than the window's sample count silently ends the data before the
        // alert.
        //
        // Measured at ~1 sample / 13 min on a track tag:
        //     limit=200, window=3d  -> covers 1.8d, ends ~29h SHORT   <- the bug
        //     limit=500, window=3d  -> covers 3.0d, REACHES the alert, ~7 KB
        //     limit=500, window=7d  -> covers 4.5d, ends ~60h short
        //
        // I cut this to 200 in 1.0.15.0 believing oversized payloads were the cause
        // of the parse failures. They were not -- that was the transport splicing a
        // truncation marker mid-JSON (fixed in 1.0.20.0).
        // v1.0.158.3 (owner instruction "make it 5000"): the ceiling is raised 500 -> 5000 and the
        // default with it. IMPORTANT: a request is not a guarantee -- the history API was measured
        // in 1.0.152.5 to CAP AT 500 rows regardless of the value asked for. If that cap is still
        // in force, asking for 5000 changes nothing on the wire; NoteHistoryTruncation() below now
        // logs the actual returned n against the requested limit on every history call, so one run
        // tells us whether the cap still exists (look for "HIST" WARN lines in ai-log-*.txt).
        // Even at 5000, a busy tag over a 3-day window can still be cut: If mA runs ~308 samples/day
        // (~950 over the window), so the WINDOW, not just the limit, decides whether the series
        // reaches the incidence. See History_Truncation_Finding_MD.md.
        internal static readonly int _limitCapEvidence = ClampCfg(ReadInt("AnalyzeEvidenceLimit", 5000), 50, 5000);

        internal const int _limitCapOther = 10;


        // The history API returns the OLDEST rows in the window, capped at `limit`.
        // Measured on a track tag: ~1 sample / 13 min, so limit=500 spans only ~4.4
        // days. A 7-day window therefore NEVER reached the alert -- the series ended
        // 58 h short of it and the breach was invisible in history, exactly as the
        // verdict caveats reported. 3 days at ~1/13min is ~330 samples: inside the
        // cap, and still enough baseline to judge a sustained deviation.
        internal static readonly int _evidenceWindowDays = ClampCfg(ReadInt("AnalyzeWindowDays", 3), 1, 15);

        // v1.0.158.4: PROVEN on 2026-08-07 -- we requested limit=5000 and the API returned EXACTLY
        // 500 twice (If mA, Vf). The 500 cap is real and unchanged, so the limit is NOT the lever.
        // When a series is cut, re-fetch just the tail of the window (which contains the incidence)
        // so the analysis sees the event even though it cannot see the whole span.
        internal static readonly int _histTailHours = ClampCfg(ReadInt("AnalyzeHistoryTailHours", 8), 2, 72);

        // v1.0.159.0 (C1): reading the EdgeX source settled it -- history_get ALREADY pages, and
        // page 1 is the OLDEST rows (DbHelper: ORDER BY "TimestampDevice" ... LIMIT/OFFSET with
        // offset=(page-1)*limit). The newest rows were always reachable; we never sent `page`.
        // A cut series is therefore fixed by PAGING to the end of the window, not by narrowing it.
        internal static readonly bool _histPagingEnabled = ReadBool("AnalyzeHistoryPaging", true);

        internal static readonly int _histMaxPageProbes = ClampCfg(ReadInt("AnalyzeHistoryMaxPageProbes", 4), 1, 12);

        // C2/C3: gated_get and summary_get already exist on the MCP server and do in SQL what this
        // controller has been doing in C#. Additive for now -- the deterministic mask STAYS.
        internal static readonly bool _gatedRefEnabled = ReadBool("AnalyzeGatedRef", true);

        internal static readonly bool _summaryBaselineEnabled = ReadBool("AnalyzeSummaryBaseline", true);

        // v1.0.160.24 (Item 9): doctrinal signalling wisdom -- retrieval-injected advisory rules
        // (RDSO SPN/257/2025, IRSEM 2021, Glued Joint Manual). Subordinate to CauseLogicMaps/RdpmsMaps.
        internal static readonly bool _doctrinalWisdomEnabled = ReadBool("AnalyzeDoctrinalWisdom", true);

        // v1.0.160.50: circuit topology + upstream causality. Default OFF for canary -- flip on after
        // validating against the flip-prone leakage/resistance causes.
        internal static readonly bool _circuitWisdomEnabled = ReadBool("AnalyzeCircuitWisdom", false);

        // v1.0.160.59: bash during analysis. Default OFF -- turn on only after ApiToken is set.
        internal static readonly bool _bashInVerdict = false;   // 1.0.167.0: bash removed for all users; AnalyzeBashInVerdict is ignored

        internal static readonly int _bashMaxCalls = ClampCfg(ReadInt("AnalyzeBashMaxCalls", 2), 1, 6);

        internal static readonly int _circuitWisdomMaxRules = ClampCfg(ReadInt("AnalyzeCircuitWisdomMaxRules", 8), 1, 20);

        internal static readonly int _doctrinalWisdomMaxRules = ClampCfg(ReadInt("AnalyzeDoctrinalWisdomMaxRules", 6), 1, 20);

        // v1.0.160.25 (Item 8): field-expert wisdom v2 -- adds the Failure-vs-Predictive verdict logic
        // (section 2) + data-scope/discriminator rules. CHANGES verdict CLASSIFICATION; toggle for rollback.
        internal static readonly bool _fieldWisdomV2Enabled = ReadBool("AnalyzeFieldWisdomV2", true);

        // v1.0.160.26 (Item 7): cross-check the alert card against fetched history per tag (relay via
        // datalogger, analog/RDPMS via RDPMS). CHANGES verdicts (can FLAG a contradicted card); toggle.
        internal static readonly bool _cardCrossCheckEnabled = ReadBool("AnalyzeCardCrossCheck", true);

        // v1.0.160.27 (Item 6): tag each evidence line [fetched]/[record]/[computed] + tally into meta.
        internal static readonly bool _evidenceProvenanceEnabled = ReadBool("AnalyzeEvidenceProvenance", true);

        // v1.0.160.30 (Item 4c): compute the derived value in-controller from the base sensors +
        // R737 (from the FRS range), fetching inputs by a pinned canonical list instead of parsing
        // the prose formula. Fixes the nested derived causes (RTC VAR RES, RRAIL, CH RELAY END).
        internal static readonly bool _derivedComputeV2 = ReadBool("AnalyzeDerivedComputeV2", true);

        // v1.0.160.31 (Item 4b): reconstruct the derived value as a per-instance SERIES on the union
        // timeline of its inputs, find the longest breach run, and confirm it against the FRS sustain
        // window ("... for 15s") -- so sustained-vs-transient is COMPUTED, not inferred.
        internal static readonly bool _derivedSeriesEnabled = ReadBool("AnalyzeDerivedSeries", true);

        // v1.0.160.37: computed-verdict-WINS master flag. OFF by default (validate against the 23-label
        // set, then flip ON). When ON, a computed sustain decision for a PREDICTIVE/ANALOG alert SETS the
        // verdict class deterministically; failure/relay-state alerts are untouched (the 160.25 rule stands).
        internal static readonly bool _computedVerdictEnabled = ReadBool("AnalyzeComputedVerdict", false);

        // v1.0.160.42: GATED DERIVED COMPUTE. When ON, each track component sensor series that feeds a
        // DERIVED value is filtered to samples taken while the related relay (TPR) is STEADY UP across
        // +/-GateGuardSec of the sample's device timestamp -- discard a sample if TPR toggles in that
        // window (train edge / clock skew) or is DOWN (train present). Charger inputs are ungated. The
        // band (MinSafe/MaxSafe) NEVER discards here: a sub-band value during steady-up is real
        // degradation and is kept; the FRS sustain then judges it. OFF by default (validate vs 23-set).
        internal static readonly bool _gatedDerivedEnabled = ReadBool("AnalyzeGatedDerived", false);

        internal static readonly int _gateGuardSec = ClampCfg(ReadInt("GateGuardSec", 10), 0, 120);

        // v1.0.160.103: movement panel compares each derived input against its PREVIOUS IN-ENVELOPE
        // reading (configured MinFail..MaxSafe), not a window statistic. Max age is measured from the
        // AT-ALERT sample, not the stamped incidence (incidence can post-date the trigger by minutes).
        internal static readonly int _prevCompMaxAgeMin = ClampCfg(ReadInt("PrevComparableMaxAgeMin", 60), 1, 360);

        // v1.0.160.109 CUT A (SL 1): how old the AT-ALERT sample itself may be, measured from the
        // anchor. PrevComparableMaxAgeMin bounds reference->at-sample (15 s in the 543194 case, so it
        // passed); NOTHING bounded at-sample->anchor, which was 32.2 h. Evidence for the default:
        // Charger mA clear-track p99.9 gap is 9.2 min, so 30 min fires only on genuine outages
        // (3 gaps in 4 days across 7,628 samples).
        internal static readonly int _atSampleMaxAgeMin = ClampCfg(ReadInt("AtSampleMaxAgeMin", 30), 1, 1440);

        // The API's own hard row cap, measured at 500 on 2026-08-07 (asked 5000, got exactly 500).
        // Used to tell a CUT series from a merely sparse one; raise if the server cap is lifted.
        internal static readonly int _histCapHint = ClampCfg(ReadInt("AnalyzeHistoryCapHint", 500), 50, 5000);


        // usable = a result the model can genuinely ground on. Rejects: our own
        // failure text, HTML pages, malformed JSON, MCP isError envelopes,
        // error-marker objects, empty-data shells ({"data":[],"count":0}), and
        // common "no records" prose. RESIDUAL (stated): relevance to the alert's
        // asset/time window is NOT verified -- that needs per-tool result schemas.
        internal static readonly string[] _noDataPhrases =
            { "no record", "no data", "not found", "no rows", "no results", "empty result",
              "unauthorized", "forbidden", "internal server error", "bad request",
              "service unavailable", "exception", "stack trace", "execution failed",
              "n/a", "null", "--" };


        internal static readonly string[] _dataKeys =
            { "rows", "data", "items", "values", "results", "records", "list", "alerts", "history", "points" };


        // keys that identify or annotate a row but are not themselves a
        // measurement: a row of only these is metadata, not evidence
        internal static readonly string[] _metaKeys =
            { "message", "status", "success", "ok", "type", "info", "note",
              "ts", "timestamp", "time", "date", "datetime", "epoch", "t0",
              "dt", "delta", "deltats", "offset",
              "id", "rowid", "tagid", "assetid", "siteid", "attributeid",
              "name", "unit", "units", "index", "seq", "page", "count", "total" };


        internal static readonly string[] _statusFailWords = { "error", "failed", "fail", "failure", "denied", "unauthorized" };


        internal static readonly System.Text.RegularExpressions.Regex _truncMarker =
            new System.Text.RegularExpressions.Regex(
                @"\[\s*\.\.\.\s*[0-9][0-9,]*\s*chars\s+truncated[^\]]*\]",
                System.Text.RegularExpressions.RegexOptions.Compiled);


        internal static readonly string[] _evidenceTools =
            { "history_get", "get_attribute_range", "get_attribute_range_history", "get_frs_alerts",
              "analyse_alert", "get_hist_realtime", "fetch_alert_details_frs",
              "predict_track_health", "predict_pm_operation", "predict_pm_cluster" };


        // ═════════════════════════════════════════════════════════
        //  WEATHER (Option A) - controller-side external enrichment, NOT an MCP tool.
        //  Rain/lightning are top external causes and hit multiple assets; if it rained/
        //  thundered at the site around incidence, that supports a site-wide/environmental
        //  read. The controller fetches this itself (Open-Meteo, no API key), gated on
        //  weather-relevant cause codes, and injects it as evidence - so the EdgeX MCP
        //  server never carries a third-party API. Best-effort: any failure/timeout ->
        //  no weather block (the model then never mentions weather).
        // ═════════════════════════════════════════════════════════
        internal static readonly bool _weatherEnabled = ReadBool("WeatherEnrichment", true);

        internal static readonly int _weatherTimeoutSec = ReadInt("WeatherTimeoutSec", 6);

        internal static readonly System.Net.Http.HttpClient _weatherHttp = new System.Net.Http.HttpClient();


        // v1.0.160.33: TRAIN-CONTEXT config. Scheduled trains through the alert's STATION around the
        // alert time, fetched by direct HTTP from a public timetable (totaltraininfo) and parsed
        // controller-side -- best-effort, zero model tokens, mirrors the weather card. SCHEDULED, not
        // confirmed passage. OFF by default; set TrainContextEnabled=true to enable per site.
        internal static readonly bool _trainCtxEnabled = ReadBool("TrainContextEnabled", false);

        internal static readonly int _trainCtxWindowMin = ClampCfg(ReadInt("TrainContextWindowMin", 5), 1, 60);

        internal static readonly int _trainCtxTimeoutMs = ClampCfg(ReadInt("TrainContextTimeoutMs", 4000), 1000, 15000);

        internal static readonly string _trainCtxBaseUrl = ReadStr("TrainContextBaseUrl", "https://www.totaltraininfo.com/station/");

        // v1.0.160.41 (Guard B): a real station page lists tens of trains; the generic fallback page
        // served for an UNKNOWN code lists ~1454. Anything above this ceiling is not a station page.
        internal static readonly int _trainCtxMaxTrains = ClampCfg(ReadInt("TrainContextMaxTrains", 400), 20, 5000);

        internal static readonly int _trainCtxCacheMin = ClampCfg(ReadInt("TrainContextCacheMinutes", 10), 0, 1440);

        internal static readonly int _trainCtxMaxBytes = ClampCfg(ReadInt("TrainContextMaxKB", 2048), 64, 16384) * 1024;

        internal static readonly System.Net.Http.HttpClient _trainCtxHttp = MakeTrainCtxHttp();

        // v1.0.160.35: per-station short-TTL cache so repeated alerts at the same station do not re-hit the
        // public site every time. Key = lowercased station code -> (fetchedUtc, html).
        internal static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Tuple<DateTime, string>> _trainCtxCache
            = new System.Collections.Concurrent.ConcurrentDictionary<string, Tuple<DateTime, string>>();


        // Supplementary analyses. When one is unavailable the verdict still stands
        // on the sample series, so its absence must not surface as a caveat -- doing
        // so made sound verdicts look under-evidenced.
        internal static readonly string[] _optionalTools =
            { "predict_track_health", "predict_pm_operation", "predict_pm_cluster" };


        // The API caps rows and returns the OLDEST ones, so a wide window silently
        // stops short of the alert. Say so IN the tool result: the model cannot see
        // that the last sample predates the incidence unless it is told.
        // Alert timestamps are IST wall-clock with no DateTimeKind, so
        // ToUniversalTime() silently used the SERVER's timezone -- on a UTC host the
        // comparison was 5h30m out, which is exactly the size of error that would let
        // a short history look complete. Convert with an explicit offset instead.
        internal static readonly TimeSpan _istOffset = AiTime._istOffset;   // value now owned by E7.AiCore.AiTime (1.0.162.0)


        // -- v1.0.75.0 -- REMOVED LastHistoryEpoch: read only {"ts":[...]} property
        // arrays on the OUTER document, so (a) the columnar cols/rows format this
        // controller itself forces returned 0, and (b) the same JSON wrapped in an
        // MCP content[].text envelope was invisible; the trailing `to` branch was a
        // no-op. Replaced by ParseCoverageDocuments + ComputeHistoryCoverage below.
        // private static long LastHistoryEpoch(string result)
        // {
        //     try
        //     {
        //         List<JToken> docs = ParseJsonDocuments(result);
        //         long best = 0;
        //         for (int d = 0; d < docs.Count; d++)
        //         {
        //             foreach (JToken tsArr in docs[d].SelectTokens("$..ts"))
        //             {
        //                 JArray a = tsArr as JArray;
        //                 if (a == null || a.Count == 0) { continue; }
        //                 long v;
        //                 if (long.TryParse(a[a.Count - 1].ToString(), out v) && v > best) { best = v; }
        //             }
        //             JToken to = docs[d].SelectToken("$..to");
        //             long t2;
        //             if (to != null && long.TryParse(to.ToString(), out t2) && t2 > best && best == 0) { best = 0; }
        //         }
        //         return best;
        //     }
        //     catch { return 0; }
        // }

        // Machine-readable coverage categories: the recovery advice DIFFERS by
        // reason (narrower window cannot add samples or lengthen a span), so the
        // caller must know WHICH check failed, not merely that one did.
        internal enum CoverageStateKind
        {
            Unverifiable = 0,
            ShortAtEnd = 1,
            InsufficientSamples = 2,
            InsufficientSpan = 3,
            Covered = 4
        }


        // Everything the coverage/adequacy gate needs, computed from UNIQUE
        // pre-alert samples only: duplicated documents, repeated encodings of the
        // same series, and post-alert rows must not inflate adequacy.
        internal sealed class HistoryCoverage
        {
            public long LastBefore;                 // last unique sample at/before incidence (+15 s grace)
            public long FirstAfter;                 // first unique sample after the bound, 0 if none
            public int PreAlertSampleCount;         // unique samples at/before the bound
            public long EarliestBefore;             // earliest unique pre-alert sample
            public long MedianPreAlertIntervalSec;  // median spacing of pre-alert samples, 0 when < 3
        }


        internal static readonly int _historyCoverToleranceSec =
            ClampCfg(ReadInt("AnalyzeHistoryCoverToleranceSec", 300), 30, 3600);

        // v1.0.75.0 adequacy policy (Review rev D decisions D1-D3, canary defaults)
        internal static readonly int _historyAdaptiveCapSec =
            ClampCfg(ReadInt("AnalyzeHistoryAdaptiveCapSec", 1800), 300, 7200);

        internal static readonly int _historyMinSamples =
            ClampCfg(ReadInt("AnalyzeHistoryMinSamples", 6), 1, 100);

        internal static readonly int _historyMinSpanSec =
            ClampCfg(ReadInt("AnalyzeHistoryMinSpanSec", 14400), 600, 259200);


        // v1.0.75.0: coverage is tracked PER TAG and enforced for the ALERTING
        // tag only -- a different tag's good series provides corroboration, never
        // coverage. gotUsableHistoryGet is deliberately NOT _jsonGotHistory: that
        // flag is raised by the whole history family including analyse_alert and
        // get_hist_realtime, which carry no series to verify.
        internal enum HistoryCoverageLevel
        {
            Unknown = 0,
            Unverified = 1,
            Inadequate = 2,
            VerifiedAdequate = 3
        }


        // scalar keys must be scalars (bounded), cards must be objects
        internal static readonly string[] _ctxScalarKeys =
            { "alertId", "causeCode", "assetName", "station", "time", "resetTime",
              "alertType", "description", "rawMessage", "requestId",
              "siteId", "assetId", "provider" };

        internal static readonly Dictionary<string, int> _jsonToolCaps =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "search_sites", 2 }, { "search_assets", 2 }, { "search_tags", 2 },
                { "get_asset_tags", 2 }, { "get_timestamps", 1 },
                { "get_attribute_range", 2 }, { "get_frs_alerts", 2 },
                { "history_get", 2 },
                // trend_get REMOVED. Measured across every trace we have: 11 calls,
                // every one returning 45-50 bytes (a valid but degenerate single-bucket
                // summary). It never once produced usable evidence, yet the model
                // reached for it ~4 times per run -- and each attempt costs a turn,
                // which re-sends the entire accumulated conversation. Demoting it was
                // not enough; it had to go. If a slope figure is ever wanted, re-add
                // here and to _evidenceTools.
                // MCP2 (FRS server) -- the tools actually built for alert analysis.
                // They were absent from this allowlist, so verdict mode filtered
                // them out of the tool list and the model never saw them:
                // analyse_alert in particular runs the cause-code formula check
                // that this endpoint exists to perform.
                { "analyse_alert", 2 }, { "get_hist_realtime", 2 },
                { "fetch_alert_details_frs", 2 },
                // ML analyses. Capped at ONE call each -- they are the slowest tools
                // and a second call adds nothing. Evidence-class, but NOT
                // history-class: they return a derived assessment, and the verdict
                // must still rest on the sample series.
                { "predict_track_health", 1 }, { "predict_pm_operation", 1 },
                { "predict_pm_cluster", 1 },
                // v1.0.117.0: get_attribute_range_history - asset-scoped historical AVG trend +
                // site geo/topology. Cap 2. Prefetched (controller reads lat/long from it for the
                // weather enrichment), but also allowed if the model wants the trend directly.
                { "get_attribute_range_history", 2 },
                // v1.0.117.0: get_weather REMOVED as an MCP tool. Weather is now fetched
                // controller-side (Open-Meteo) during prefetch and injected as evidence (Option A) -
                // so no third-party API sits in the MCP server. See FetchWeatherAsync + the WEATHER
                // block in the prefetch flow. The model receives weather as evidence, not a tool.
                // Live-value lookups. Needed by the FOLLOW-UP chat ("what is it now?").
                // 1.0.27.0 applied this allowlist to the streaming path as well, which
                // silently removed the ability to answer that -- the follow-up would
                // have been denied the very tool the prompt tells it to use. Verdict
                // mode is separately instructed not to call these, and the caps bound
                // them either way.
                { "get_tag_current", 4 }, { "live_get", 2 }
            };

        // Tools returning VALUE TELEMETRY, which satisfy the grounding gate.
        // get_hist_realtime returns historian readings around the alert and
        // analyse_alert returns HIST data plus the evaluated formula, so both are
        // genuine value evidence -- not merely lookups.
        // trend_get is DEMOTED: observed returning 46 bytes (an empty summary) and
        // grounding a verdict on a slope figure is not acceptable -- the model must
        // see the samples to tell a sustained breach from a transient. It stays
        // callable as a supplementary figure, but no longer satisfies the gate.
        internal static readonly string[] _jsonHistoryFamily =
            { "history_get", "get_hist_realtime", "analyse_alert" };

        internal const int _jsonHistoryFamilyCap = 3; // one retry if the first window misses

        internal const int _jsonToolTotalCap = 12;


        // ---- strict verdict schema validation + operational-action filtering ----
        internal static readonly string[] _verdictCategories = VerdictRules._verdictCategories;   // value now owned by E7.AiCore.VerdictRules (1.0.162.0)

        // v1.0.111.0: derived_inputs, triage, hi and weather are SERVER-CONTROLLED - they are
        // NOT accepted from the model. They are attached ONLY by trusted post-validation code
        // (derived_inputs + triage computed server-side; hi from the sanitize-then-translate
        // step). They are deliberately absent here so StripUnknown removes any model-emitted
        // version (a model-invented Hindi block would otherwise bypass the English filter).
        internal static readonly string[] _verdictTopKeys =
            { "verdict", "confidence", "headline", "likely_cause", "category",
              "evidence", "recommended_action", "caveats", "viz" };

        // fields the server may attach AFTER validation (never accepted from the model)
        internal static readonly string[] _serverOnlyVerdictKeys =
            { "derived_inputs", "triage", "hi", "weather" };

        internal static readonly string[] _vizKeys =
            { "metric", "unit", "value", "threshold", "baseline", "direction",
              "pct_change", "series", "secondary", "trigger_pct", "recovery_pct", "phase_note" };

        internal static readonly string[] _vizSecondaryKeys = { "label", "value", "unit", "x_threshold" };

        internal static readonly int  _provEnforceCap = ClampCfg(ReadInt("AnalyzeDowngradeConfidenceCap", 40), 0, 100);


        // v1.0.160.186 (#2): IPS drivers (VIPS.../IIPS...) live on the site's IPS asset
        // (AssetTypeId 39). ResolveIpsAssetIdAsync finds it via search_assets{SiteId}, cached per
        // site; CardMentionsDriver gates the cross-asset fetch to terms the card carries.
        internal static readonly bool _ipsCrossAssetEnabled = ReadBool("AnalyzeIpsCrossAsset", true);

        internal const string _analysisLinkSql =
            "MERGE dbo.FRSAlert_AIAnalysis AS t " +
            "USING (SELECT @FRSAlertId AS FRSAlertId, @PendingId AS PendingAlertApprovalId) AS s " +
            "ON ((@FRSAlertId > 0 AND t.FRSAlertId = @FRSAlertId) " +
            "    OR (@PendingId > 0 AND t.PendingAlertApprovalId = @PendingId)) " +
            "WHEN MATCHED THEN UPDATE SET AnalysisId = @NewId, ModifiedDate = GETDATE() " +
            "WHEN NOT MATCHED THEN INSERT (FRSAlertId, PendingAlertApprovalId, FirstAnalysisId, AnalysisId, CreatedDate, ModifiedDate) " +
            "VALUES (NULLIF(@FRSAlertId,0), NULLIF(@PendingId,0), @NewId, @NewId, GETDATE(), GETDATE());";


        // v1.0.75.0 enforcement policy (D4/D5 -- Review rev D)
        internal static readonly bool _strictCoverage =
            ReadInt("AnalyzeStrictCoverage", 0) != 0;   // 0=scoped (default), 1=strict

        internal static readonly int _downgradeConfidenceCap =
            ClampCfg(ReadInt("AnalyzeDowngradeConfidenceCap", 40), 0, 100);


        // v1.0.160.103: result of the previous-in-envelope comparison for one derived component.
        internal sealed class DerivedCompare
        {
            public double AtVal;              // at-alert value (ALWAYS set; never envelope-gated)
            public long AtTs;                 // Unix epoch seconds of the at-alert sample
            public long AtAgeSec;             // anchor-relative age of the at-alert sample (anchor - AtTs)
            public bool StaleAtSample;        // v1.0.160.109: at-sample older than AtSampleMaxAgeMin
            public bool CurrentOutside;       // at-alert value is outside the configured envelope (possible genuine step)
            public bool HasRef;               // a previous in-envelope reference was found
            public double RefVal;             // reference value
            public long RefTs;                // Unix epoch seconds of the reference sample
            public long RefAgeSec;            // incidence-relative age of the reference (anchor - refTs)
            public bool HasDelta;             // deltaPct is valid (reference non-zero)
            public double DeltaPct;           // (AtVal - RefVal)/|RefVal|*100
            public string Kind;               // "in_envelope" (track) | "previous_sample" (non-track)
            public string Reason;             // explanatory string when no delta
        }


        // Word overlap alone cannot do this. The alert names the parameter in
        // ENGINEERING terms -- "ITC RELAY END(mA)" -- while the tag is called "Ir mA".
        // "RELAY" appears in neither tag name, and "MA" matches If/Ir/Charger equally,
        // so the overlap score picked whichever came first. The mapping is domain
        // knowledge: Ir is relay-end current, If is feed-end current, Vr/Vf the
        // corresponding voltages. Returns null when no rule applies, and the caller
        // then falls back to word overlap.
        // v1.0.85.0: authoritative alias -> Title map (Track), from the site's
        // Attribute alias table. The ALERT CARD formula uses ALIAS names
        // ("ITC FEED END(mA)"); search_tags works on the REAL Title name ("If mA").
        // So component resolution MUST go alias -> Title -> tag. Keys are the alias
        // NORMALIZED (unit stripped, whitespace collapsed, upper). This replaces the
        // old Contains()-based guesswork, which mis-resolved names like
        // "ITC TFC O/P" (-> should be Charger mA) and could not handle O/P at all.
        // v1.0.91.0 STAGE 1: the alias->Title map now comes from RdpmsMaps.AliasToTitle,
        // GENERATED from the RDPMS wisdom xlsx (all asset types: Track/Signal/Point/IPS/
        // LC/env), replacing the hand-built Track-only table from 1.0.85. The generated
        // map is the single source of truth; edit the xlsx and re-run the converter to
        // update it. See RdpmsMaps.generated.cs.
        internal static Dictionary<string, string> _aliasToTitle { get { return RdpmsMaps.AliasToTitle; } }


        // v1.0.91.0 STAGE 2: format the sensor health band + correlation partner for an
        // attribute, from RdpmsMaps.HealthBands (generated from the wisdom xlsx). Returns
        // "" if the attribute has no band (e.g. DERIVED/computed values have none -- they
        // are judged by the computed value, not a sensor band). Keys are matched on the
        // normalized Title AND on the raw component name, since the health table is keyed
        // by tag name (If_mA) which may differ from the alias/title form.
        // v1.0.91.0 STAGE 4: resolve a point-machine OPERATION attid from (assetAttr,
        // direction, dataType) via RdpmsMaps.PmOpAttid (generated from the wisdom xlsx).
        // Point machines carry per-operation telemetry (A/B end x Normal/Reverse x metric)
        // as COMPUTED EdgeX attids (BaseCode+Offset), distinct from the sensed INDICATION
        // channels. assetAttr is AC/AV/BC/BV (A-Current/A-Voltage/B-Current/B-Voltage),
        // dataType is Array/Avg/Min/Max/OperationTime/Count. Returns null if not mapped.
        // NOTE: this resolver is available for a future PM-specific prefetch branch; the
        // PM data model (per-operation, no freshness) differs enough to warrant its own
        // grounding path, mirroring the derived branch. Wired but not yet on the hot path.
        // v1.0.92.0 STAGE 4 (PM branch), room/loc mapping CONFIRMED in 1.0.94.0: the eight
        // DETECTION attribute Titles for a point machine, both ends and both sides. A point end
        // is normally Normal (NWKR up) XOR Reverse (RWKR up); A-END normally agrees with B-END.
        // Owner-confirmed model: ROOM-SIDE (A/B End - NWKR/RWKR) is AUTHORITATIVE (what the
        // interlocking acts on); LOCATION-SIDE (...(Loc)) is field truth before the cable run
        // back to the relay room. A (Loc)-vs-room mismatch supports localizing to the cable
        // path (telemetry alone does not name the failed part). All eight are listed so the
        // model can tell relay-room vs cable vs location-side incoherence apart; it is told to
        // trust a value for incidence coherence only when timestamp-aligned. Live per-relay
        // fetch is still not built (needs a real PM alert to validate against).
        internal static readonly string[] _pmDetectionAttrs = new string[]
        {
            "A End - NWKR", "A End - RWKR", "B End - NWKR", "B End - RWKR",
            "A End - NWKR (Loc)", "A End - RWKR (Loc)", "B End - NWKR (Loc)", "B End - RWKR (Loc)"
        };


        // v1.0.123.0: the EXACT tags a point operation-TIME cause needs (owner spec): the
        // operation-TIME of the alerting END (A or B, resolved from the description) + the two
        // proving relays WCR and WKR (they prove the operation actually occurred and completed).
        // Reverse (PT R TIME): op-time 6005(A)/8005(B) + RWCR 283 + RWKR 268.
        // Normal  (PT N TIME): op-time 1005(A)/3005(B) + NWCR 282 + NWKR 267.
        // Returns (attid, label) pairs; empty if not a TIME cause. If the end is unknown, BOTH
        // ends' op-time are included (so it still fetches something useful).
        // v1.0.124.0: UNIFIED per-cause point-machine tag spec, from the CORRECTED owner mapping.
        // Each point cause -> the exact tags its FRS logic needs, resolved by AssetAttributeId via
        // search_tags -> history_get. End-specific tags (op-time/current/voltage, and the per-END
        // indication voltages) carry A & B attids and are picked by the alert's END. Relays carry a
        // primary + alt attid (e.g. NWCR 282/284). The VPT-110 DC / IPT / VIPS-110 params resolve
        // via the EXISTING attribute mapping and are deliberately NOT repeated here (owner).
        internal sealed class PmTag
        {
            public readonly string A;     // A-end attid (or the single attid)
            public readonly string B;     // B-end attid; null if not end-specific
            public readonly string Alt;   // alternate attid; null if none
            public readonly string Label;
            public PmTag(string a, string b, string alt, string label) { A = a; B = b; Alt = alt; Label = label; }
        }


        // reusable tag templates -------------------------------------------------
        // operation (EdgeX op attids), end-specific A/B:
        internal static readonly PmTag PmOpTimeN = new PmTag("1005", "3005", null, "op-time (N)");

        internal static readonly PmTag PmOpTimeR = new PmTag("6005", "8005", null, "op-time (R)");

        internal static readonly PmTag PmCurrN   = new PmTag("1002", "3002", null, "current (N)");

        internal static readonly PmTag PmCurrR   = new PmTag("6002", "8002", null, "current (R)");

        internal static readonly PmTag PmVoltN   = new PmTag("2002", "4002", null, "voltage (N)");

        internal static readonly PmTag PmVoltR   = new PmTag("7002", "9002", null, "voltage (R)");

        // indication (RDPMS), corrected per-END attids:
        internal static readonly PmTag PmInd24N  = new PmTag("576", "578", null, "VPT 24 DC LOC N");

        internal static readonly PmTag PmInd24R  = new PmTag("577", "579", null, "VPT 24 DC LOC R");

        internal static readonly PmTag PmIndNWKR = new PmTag("25", "27", null, "VPT NWKR");

        internal static readonly PmTag PmIndRWKR = new PmTag("26", "28", null, "VPT RWKR");

        // datalogger relays (primary + alt; not end-specific):
        internal static readonly PmTag PmNWCR = new PmTag("282", null, null, "NWCR relay");   // v1.0.152.2: alt 284 removed (NWCR resolves on 282 only)

        internal static readonly PmTag PmRWCR = new PmTag("283", null, null, "RWCR relay");

        internal static readonly PmTag PmNWKR = new PmTag("267", null, null, "NWKR relay");

        internal static readonly PmTag PmRWKR = new PmTag("268", null, null, "RWKR relay");


        // v1.0.160.6: point-machine VALUE-attid -> FRS-RANGE-attid map. The operation VALUE lives
        // under the EdgeX/search_tags attid (2002 "..-PM-02002-Avg"), but its Min/MaxSafe RANGE
        // lives under a DIFFERENT RDPMS attid in the FRS range DB (216 "A End - NW-V"). Two
        // databases, two attid schemes for the SAME physical quantity -- so a PT operation cause
        // must translate the value-attid to its range-attid to read the threshold from
        // get_attribute_range / get_asset_tags. Confirmed on asset 43022: attid 216 carries
        // MinValue 90 / MaxValue 135 for the NW voltage whose value is attid 2002.
        //   voltage N: 2002/4002(A/B value) -> 216/212(range)   current N: 1002/3002 -> 217/213
        //   voltage R: 7002/9002            -> 218/214          current R: 6002/8002 -> 219/215
        //   op-time N: 1005/3005            -> 581/583          op-time R: 6005/8005 -> 582/584
        internal static readonly Dictionary<string, string> _pmValueToRangeAttid =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "2002", "216" }, { "4002", "212" },   // NW voltage A / B
            { "1002", "217" }, { "3002", "213" },   // NW current A / B
            { "7002", "218" }, { "9002", "214" },   // RW voltage A / B
            { "6002", "219" }, { "8002", "215" },   // RW current A / B
            { "1005", "581" }, { "3005", "583" },   // NW op-time A / B
            { "6005", "582" }, { "8005", "584" },   // RW op-time A / B
        };


        // v1.0.143.0: IPS cause -> attid (same model as point machines). IPS cause -> attid ->
        // PickTagIdByAttrId(AssetId, attid) -> TagID, matching AssetAttributeId (not name). 42 of 46
        // IPS causes mapped; the 4 bare 24V/60V causes have no attribute in CauseToAttributes.
        internal static readonly Dictionary<string, string> _ipsCauseAttid =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "IPS 110V DC LOW", "616" }, { "IPS 110V AC LOW", "617" },
            { "IPS 110 DC BATT VOLT LOW (Charging)", "616" }, { "IPS 110 DC BATT VOLT LOW (Discharging)", "616" },
            { "IPS 110 AC Sig-1 VOLT LOW", "617" }, { "IPS 110 AC TR-1 VOLT LOW", "621" },
            { "IPS SMR-1 VOLT LOW", "625" }, { "IPS DC-DC R INT VOLT LOW", "647" },
            { "IPS DC-DC R EXT VOLT LOW", "648" }, { "IPS DC-DC AXLE C VOLT LOW", "649" },
            { "IPS DC-DC PAN IND VOLT LOW", "650" }, { "IPS DC-DC BLOCK LOCAL VOLT LOW", "651" },
            { "IPS DC-DC HKT MAG VOLT LOW", "652" }, { "IPS DC-DC BLOCK LINE UP VOLT LOW", "653" },
            { "IPS DC-DC BLOCK LINE DN VOLT LOW", "654" }, { "IPS DC-DC BLOCK TEL UP VOLT LOW", "655" },
            { "IPS DC-DC BLOCK TEL DN VOLT LOW", "656" }, { "IPS DC-DC DATALOG VOLT LOW", "657" },
            { "IPS 110V DC FAIL", "616" }, { "IPS 110V AC FAIL", "617" },
            { "IPS 110 DC VOLT FAIL", "616" }, { "IPS 110 AC Sig-1 VOLT FAIL", "617" },
            { "IPS 110 AC TR-1 VOLT FAIL", "621" }, { "IPS SMR-1 VOLT FAIL", "625" },
            { "IPS DC-DC R INT VOLT FAIL", "647" }, { "IPS DC-DC R EXT VOLT FAIL", "648" },
            { "IPS DC-DC AXLE C VOLT FAIL", "649" }, { "IPS DC-DC PAN IND VOLT FAIL", "650" },
            { "IPS DC-DC BLOCK LOCAL VOLT FAIL", "651" }, { "IPS DC-DC HKT MAG VOLT FAIL", "652" },
            { "IPS DC-DC BLOCK LINE UP VOLT FAIL", "653" }, { "IPS DC-DC BLOCK LINE DN VOLT FAIL", "654" },
            { "IPS DC-DC BLOCK TEL UP VOLT FAIL", "655" }, { "IPS DC-DC BLOCK TEL DN VOLT FAIL", "656" },
            { "IPS DC-DC DATALOG VOLT FAIL", "657" },
            { "IPS I/P VOLT LOW", "660" }, { "IPS I/P VOLT FAIL", "660" },
            { "IPS DC-DC EI VOLT LOW", "658" }, { "IPS DC-DC EI VOLT FAIL", "658" },
            { "IPS BATT CHAR CURR LOW", "659" }, { "IPS BATT CHAR CURR FAIL", "659" },
            { "IPS DC-DC BATT CHAR CURR FAIL", "659" },
        };


        internal static readonly Dictionary<string, PmTag[]> _pmCauseTags =
            new Dictionary<string, PmTag[]>(StringComparer.OrdinalIgnoreCase)
        {
            // operation causes (op tags + proving relays)
            { "PT N TIME HIGH",          new PmTag[] { PmOpTimeN, PmNWCR, PmNWKR } },
            { "PT R TIME HIGH",          new PmTag[] { PmOpTimeR, PmRWCR, PmRWKR } },
            { "PT N VOLT/CURR LOW",      new PmTag[] { PmVoltN, PmCurrN, PmNWCR, PmNWKR } },
            { "PT R VOLT/CURR LOW",      new PmTag[] { PmVoltR, PmCurrR, PmRWCR, PmRWKR } },
            { "PT N VOLT/CURR FAIL",     new PmTag[] { PmVoltN, PmCurrN, PmNWCR, PmNWKR } },
            { "PT R VOLT/CURR FAIL",     new PmTag[] { PmVoltR, PmCurrR, PmRWCR, PmRWKR } },
            { "PT N OBS",                new PmTag[] { PmVoltN, PmCurrN, PmOpTimeN, PmNWCR, PmNWKR } },
            { "PT R OBS",                new PmTag[] { PmVoltR, PmCurrR, PmOpTimeR, PmRWCR, PmRWKR } },
            { "PT VOLT FAIL AT NWKR OP", new PmTag[] { PmInd24N, PmIndNWKR, PmNWCR, PmNWKR } },
            { "PT VOLT FAIL AT RWKR OP", new PmTag[] { PmInd24R, PmIndRWKR, PmRWCR, PmRWKR } },
            { "PT NWKR RELAY DEFECT OP", new PmTag[] { PmIndNWKR, PmNWCR, PmNWKR } },
            { "PT RWKR RELAY DEFECT OP", new PmTag[] { PmIndRWKR, PmRWCR, PmRWKR } },
            // indication / relay-defect causes (per-END indication + relays)
            { "PT N IND VOLT LOW AT LOC",  new PmTag[] { PmInd24N, PmNWKR } },
            { "PT R IND VOLT LOW AT LOC",  new PmTag[] { PmInd24R, PmRWKR } },
            { "PT VOLT LOW AT NWKR",       new PmTag[] { PmInd24N, PmIndNWKR, PmNWKR } },
            { "PT VOLT LOW AT RWKR",       new PmTag[] { PmInd24R, PmIndRWKR, PmRWKR } },
            { "PT NWKR RELAY DEFECT",      new PmTag[] { PmIndNWKR, PmNWCR, PmRWCR, PmRWKR, PmNWKR } },
            { "PT RWKR RELAY DEFECT",      new PmTag[] { PmIndRWKR, PmNWCR, PmRWCR, PmNWKR, PmRWKR } },
            { "PT N IND VOLT FAIL AT LOC", new PmTag[] { PmInd24N, PmNWCR, PmRWCR, PmRWKR, PmNWKR } },
            { "PT R IND VOLT FAIL AT LOC", new PmTag[] { PmInd24R, PmNWCR, PmRWCR, PmNWKR, PmRWKR } },
            { "PT VOLT FAIL AT NWKR",      new PmTag[] { PmInd24N, PmIndNWKR, PmNWCR, PmRWCR, PmRWKR, PmNWKR } },
            { "PT VOLT FAIL AT RWKR",      new PmTag[] { PmInd24R, PmIndRWKR, PmNWCR, PmRWCR, PmNWKR, PmRWKR } },
            // unknown causes (relays + OPERATION values so the model can RECLASSIFY -- v1.0.160.22:
            // fetch the end's op voltage/current/op-time; if one clearly breaches (110V collapsed,
            // op-time > 150% avg, current abnormal) the verdict can name the REAL cause instead of
            // echoing UNKNOWN. Op tags resolve by EdgeX-op attid; range via PmRangeAttidFor (RDPMS).
            { "PT N FAIL UNKNOWN",         new PmTag[] { PmVoltN, PmCurrN, PmOpTimeN, PmNWCR, PmRWCR, PmRWKR, PmNWKR } },
            { "PT R FAIL UNKNOWN",         new PmTag[] { PmVoltR, PmCurrR, PmOpTimeR, PmNWCR, PmRWCR, PmNWKR, PmRWKR } },
            { "PT N FAIL UNKNOWN OP",      new PmTag[] { PmVoltN, PmCurrN, PmOpTimeN, PmNWCR, PmNWKR } },
            { "PT R FAIL UNKNOWN OP",      new PmTag[] { PmVoltR, PmCurrR, PmOpTimeR, PmRWCR, PmRWKR } },
        };


        // v1.0.92.0: reverse index into HealthBands built from each band's Resolves field
        // (e.g. "RDPMS: TR V (Relay) / VTC TR") plus the raw band key. The direct key match
        // misses titles whose normalized form keeps a non-unit qualifier - e.g. component
        // "TR V (Relay)" normalizes to TR_V_(RELAY) but the band key is TR_V. Indexing every
        // name that appears in Resolves (and the key itself) lets a component Title, alias, or
        // tag-name all reach the right band. Built once, lazily.
        internal static Dictionary<string, RdpmsMaps.HealthBand> _healthBandIndex;

        internal static readonly object _hbLock = new object();


        // Normalize an attribute name for alias-map lookup. v1.0.91.0: strip only a
        // recognized FINAL unit suffix -- (mA),(V),(A),(ohm),(C),(%) etc. -- and PRESERVE
        // semantic qualifiers like (BUG),(CUG),(AUG),(Sig),(Loc). Stripping from the first
        // '(' (the old behavior) collapsed distinct attributes: IROSIG (CUG) and
        // IROSIG (BUG) are different route-signal phases, not units. Must match the
        // converter's norm() exactly so lookups hit the generated keys.
        internal static readonly System.Text.RegularExpressions.Regex _unitSuffix =
            new System.Text.RegularExpressions.Regex(
                @"\s*\((?:mA|A|V|ohm|C|%|sec|s|Hz|W|kW|VA)\)\s*$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);


        internal static readonly string[] _platformMarks = new string[]
        {
            "\\\\E7EDGEX", "\\\\ENERGY7-SERVICE", "C:\\DataEdgeX", "C:\\inetpub", "C:\\E7LogWatch",
            "[ERROR]", "[WARN ]", "[INFO ]", "[DEBUG]", "[FATAL]",
            "IncomingLogs", "EdgeXLogs", "DebouncerLogs", "FailedInserts", "REDA-logs",
            "at Energy7In.", "Exception:", ".log:"
        };


        // Resolve India Standard Time (Asia/Kolkata, UTC+05:30) once, with fallbacks
        // so it works on Windows ("India Standard Time"), Linux/ICU ("Asia/Kolkata"),
        // or a hard-coded +05:30 offset if the OS has no tz database entry.
        internal static readonly Lazy<TimeZoneInfo> _istZone =
            new Lazy<TimeZoneInfo>(ResolveIstZone);


        // Exception TYPE plus a redacted, truncated message. Types are safe to expose
        // and name the fault; the message is redacted through the same filter used for
        // prompts so nothing sensitive rides along. Includes the innermost exception,
        // which is where an await usually hides the real cause.
        // OPTIONAL PARAMETERS DO NOT SURVIVE AN ASSEMBLY MISMATCH.
        // C# bakes the default into the CALL SITE, so compiling against
        // AnthropicClient 1.0.1.0 emits a 4-argument call even where the source
        // passes three. If the deployed Domain.dll still has the 3-parameter method,
        // the JIT throws MissingMethodException on the FIRST model call and the whole
        // analysis dies with nothing but "internal error" -- which is exactly what
        // happened, and it cost a full deploy cycle to identify.
        //
        // Fall back through reflection to the old signature so a mismatched deploy
        // degrades to "works, without prompt caching" instead of failing outright,
        // and says so once in the log.
        internal static volatile bool _aiCacheParamMissing;

        internal static System.Reflection.MethodInfo _aiSend3;


        internal static readonly int _modelRetries = ClampCfg(ReadInt("AnalyzeModelRetries", 1), 0, 3);

        internal static readonly int _modelRetryBackoffMs = ClampCfg(ReadInt("AnalyzeModelRetryBackoffMs", 1500), 250, 10000);

        internal static readonly int _modelRetryMaxElapsedMs = ClampCfg(ReadInt("AnalyzeModelRetryMaxElapsedMs", 15000), 2000, 60000);


        // v1.0.158.0: per-model client cache for the NEXUS/ORION variants (they reuse the
        // anthropic/openai transport but with a different model string). Keyed by model so we
        // build each variant once, not per call.
        internal static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> _variantClients
            = new System.Collections.Concurrent.ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        // ---- per-request state (Shared AI Core 2d, 1.0.164.0) -----------------------------------------
        // All per-request instance fields of AiChatController live in one object. MVC creates one controller
        // per request, and this field is initialised with the controller, so lifetime and initialisation
        // timing are unchanged. Every former reference `_x` is now `Sess._x`. In 2e this object becomes the
        // session passed into E7.AiCore.
        internal readonly AnalysisSession Sess = new AnalysisSession();


        internal sealed class AnalysisSession
        {
            internal string _provider;   // resolved per request
            // v1.0.160.42: per-request THINK toggle (UI checkbox -> ctx.think). null = not specified
            // by the request, in which case the DeepSeekThinking config default applies.
            internal bool? _thinkReq;
            // v1.0.160.57: wisdom actually INJECTED into this run. Counted where the blocks are built,
            // because nothing else in the system records it -- Wisdom.cs logs only save/edit/approve.
            internal int _wisDoctrinal, _wisField, _wisCircuit, _wisLearned, _wisChecklist, _wisChars;
            internal string _driverSetStr;
            internal int _bashCalls;   // v1.0.160.59: per-run bash budget
            // v1.0.160.63: prefetch tool work. _trToolCallCount counts only IN-LOOP calls, so an audit
            // built from it reported 0 for a run that made 8 prefetch calls.
            internal int _trPrefetchCalls;
            // v1.0.160.68: raw prompt/response for the audit record.
            internal string _lastPromptFull, _lastResponseFull;
            // v1.0.160.85: identity captured BEFORE the first await. HttpContext.Current is gone
            // after ConfigureAwait(false), so it cannot be read later in the run.
            internal string _luName, _luEmail; internal bool _luSiteKeep;
            internal long _auditAnalysisId;   // v1.0.160.73: 0 = no row (Start failed or DB dormant)
            internal DateTime _reqStartUtc = DateTime.UtcNow;

            // Per-run fetch cache. The KEY MUST carry the full shape: two callers wanting the same window
            // in different shapes (json vs columnar, limit 1 vs 5000) are NOT interchangeable.
            internal readonly Dictionary<string, string> _histCache = new Dictionary<string, string>();

            internal int _histCacheHits;
            // v1.0.160.110 SL5: summed per-call tool ms. NOT a wall-clock share -- prefetch is concurrent,
            // so this CAN exceed the elapsed time of the phase. Reconciliation uses the wall-clock phase
            // figures below, never this.
            internal long _trToolCallMsSum;
            internal long _trPrefetchWallMs;
            internal long _trInLoopWallMs;
            // v1.0.160.192: per-run slowest-tool tracking for the Log optimization summary
            internal string _trSlowestTool;
            internal long _trSlowestToolMs;
            internal int _trDistinctTools;
            // v1.0.160.193: per-tag fetch tally for the Log -- which tag/attr was fetched, how long, how
            // big, and (at request_end) whether it was USED (in the driver set / trigger) or WASTED.
            internal readonly List<JObject> _trTagFetches = new List<JObject>();
            // v1.0.160.110 SL6b: ANALYSIS turns only -- _trTurns counts BILLABLE calls including the
            // Hindi translation, so gating the prompt-cache warning on it fired on every bilingual
            // single-turn run. These two answer different questions and must not be conflated.
            internal int _trAnalysisTurns;
            internal bool _histSkewLogged;
            internal int _jsonAbandonedW; // this request's abandoned weight
            internal List<string> _jsonDiag;
            internal int _jsonToolOk;
            internal bool _jsonGotEvidence;
            internal bool _jsonGotHistory; // usable history_get/trend_get result required for ok
            // v1.0.75.0: usable payload and VERIFIED, ALERTING-TAG-SPECIFIC coverage
            // are different facts. gotUsableHistoryGet is deliberately NOT
            // _jsonGotHistory: that flag is raised by the whole history family
            // (history_get, get_hist_realtime, analyse_alert), which would let an
            // audit-only grounding silently trip the coverage gate below.
            internal bool gotUsableHistoryGet;
            internal string requiredAlertingTagId;   // prefetch-matched (PickAlertingTagId)
            // v1.0.82.0 rev J -- derived (formula) alerting attributes, e.g. RRAIL.
            // These have NO telemetry tag of their own; grounding is via the formula's
            // COMPONENT tags. When _derivedAlert, the coverage gate reads
            // _derivedComponentsAdequate instead of the (unsatisfiable) alerting-tag level.
            internal bool _derivedAlert;
            internal string _derivedParam;
            internal bool _derivedComponentsAdequate;
            // v1.0.160.111 (BUG-5): coverage-gate bypass signals. _cardHasFail = card carries a FAIL
            // threshold (incidence established regardless of history density). _isFailureAlert = alertType
            // is FAILURE (discrete relay-state / blank / op-fail event, self-proving). Either bypasses gate.
            internal bool _cardHasFail;
            internal bool _isFailureAlert;
            // v1.0.160.170: one of the 10 TC failure causes. A track FAILURE cannot end INCONCLUSIVE --
            // the card FAIL is authoritative; the only outcomes are CONFIRMED (genuine) or NOT_CONFIRMED
            // (train movement via C1/C2). Used to forbid INCONCLUSIVE on the failure path.
            internal bool _isTrackFailureCause;
            // v1.0.160.173: the alert arrived with NO structured card -- only a rawMessage carrying the
            // breach value (e.g. "Operation Time high. <b>TPT N: 9.04</b>"). True when that stated breach
            // was parsed; treated like a card value for the coverage-gate bypass and surfaced as an anchor.
            internal bool _rawHasBreach;
            // v1.0.109.0: structured raw inputs behind a derived alert (RRAIL etc.), built in the
            // component loop from data already fetched (as-of value + component history mean as a
            // baseline). Attached to the verdict as "derived_inputs" so the card can show WHICH raw
            // input drove the derived move. Null for non-derived alerts. Server-computed - the
            // numbers are authoritative, not model-transcribed.
            internal JObject _derivedInputs;
            internal JObject _weatherCard;   // v1.0.119.0: card-shaped weather for the verdict WEATHER tile
            internal JObject _trainContext;  // v1.0.160.33: scheduled train-context block for the verdict
            // v1.0.160.77: audit-visible copies of values that otherwise live only in locals.
            internal int _gateDroppedLast;      // [GATE] samples removed by the relay-steady-UP filter
            internal bool _computedWonLast;     // the computed class actually REPLACED the model verdict
            internal JObject _computedVerdict; // v1.0.160.37: computed CONFIRMED/NOT_CONFIRMED decision (owner sustain rule)

            // v1.0.111.0: server-side triage inputs (captured at prefetch)
            internal string _triageAlertsRaw;
            internal string _triageAssetId;
            internal string _triageCauseCode;
            internal string _triageCurrentAlertId;
            internal string presumedAlertingTagId;   // D7: first model-issued history_get when unmatched
            internal bool coverageTagPresumed;
            internal Dictionary<string, HistoryCoverageLevel> coverageByTag;
            internal CoverageStateKind _lastCoverageKind;
            internal string coverageDowngrade;       // set when the final gate rewrites a verdict
            internal string _jsonEvidenceArgs; // redacted args of the grounding history call (audit)

            internal HashSet<string> _jsonAssetHints; // AssetIds this analysis is actually about
            // Tools whose ENTIRE payload is already in the pre-fetched block. Telling the
            // model not to repeat them did not hold across three prompt revisions -- it
            // kept calling search_tags and get_attribute_range and paying a turn for bytes
            // already in front of it. Denial is the mechanism it does respect.
            internal HashSet<string> _prefetchDone;
            // v1.0.160.166: what prefetch actually covered. The one-shot denials below apply ONLY to
            // this asset; a family/adjacent asset must stay reachable.
            internal string _prefetchAssetId;
            // The sanitiser exists to stop backend text reaching a caller, and it turns
            // every unrecognised message into "internal error". That is right for the
            // envelope and useless for us: a real fault becomes indistinguishable from any
            // other. Keep the EXCEPTION TYPE and a short, redacted message separately --
            // a type name is not backend data, and it is usually enough to identify the
            // fault without another round of guessing.
            internal string _jsonFaultDetail;
            internal long _incidenceEpoch;            // for history coverage checking
            internal List<string> _jsonToolTrace; // per-call outcome, surfaced in meta for diagnosis
            internal bool _traceActive;
            internal string _traceId;
            internal long _trModelMs, _trToolMs, _trDiscoveryMs;
            internal int _trTurns;
            // deep instrumentation: exact wire volumes and real token usage
            internal long _trBytesSent, _trBytesRecv, _trToolBytes, _trToolBytesKept;
            internal long _trTokIn, _trTokOut, _trTokCacheRead, _trTokCacheWrite;
            internal JObject _lastVerdictSummary;   // v1.0.133.0
            internal System.Text.StringBuilder _assistantCapture;   // v1.0.133.0
            internal int _trToolCallCount;

            // v1.0.146.0 (trend line): REAL trend capture. The history the analysis fetches
            // already contains timestamped samples around the incidence; keep the best series
            // and stream it so "How the fault developed" plots actual telemetry.
            internal JObject _trendCandidate;
            internal int _trendScore;
            // v1.0.152.0: ALL captured series keyed by attribute name (for the multi-line chart).
            // attr(upper) -> JObject { attr, points:[[ts,v]..], n }. The trigger series is also
            // kept as _trendCandidate (used for the model's computed stats, unchanged).
            internal Dictionary<string, JObject> _trendSeriesByAttr = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            // v1.0.152.1: FULL RAW series per attribute (pre-downsample) -- the train mask and the
            // deterministic train-free stats MUST run on raw data, not the <=240 chart points, or
            // TPR transitions and short occupations are lost. attr -> List<object[]>{ts,value}.
            internal Dictionary<string, List<object[]>> _trendRawByAttr = new Dictionary<string, List<object[]>>(StringComparer.OrdinalIgnoreCase);
            // v1.0.152.4: prefetch history_get calls pass a TagId, not an AttributeName, so a harvested
            // series would otherwise be anonymous ("series1"). Register each TagId's real identity at
            // the fetch site; TryHarvestTrend recovers attr/role from here. This is what lets the TPR
            // mask find its series and the derived-hero synth match drivers by formula-variable name.
            internal readonly object _trendLock = new object();
            internal bool _lastSynthGapped; internal double _lastSynthMaxAgeSec;   // v1.0.152.7: derived-synth freshness coverage
            internal double _lastSynthWorstRejAgeSec;   // v1.0.152.8: the age that CAUSED a gap (>= the accepted max age)
            internal bool _lastSynthEvalGapped;         // v1.0.152.9: a formula-undefined span broke the line
            internal Dictionary<string, JObject> _tagMeta = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            // v1.0.158.3: the history API returns the OLDEST rows and stops at `limit`. When it returns
            // exactly `limit` rows the series is CUT, and on a busy tag the cut lands long before the
            // incidence -- the analysis then reasons from telemetry hours or days stale while fresh
            // samples exist. Detect it, log it, and tell the client so a cut is never shown as a dead feed.
            internal HashSet<string> _truncatedAttrs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Set once per run to the system prompt actually sent, so the backstop compares against the
            // real instructions rather than a copy that can drift.
            internal string _protectedCorpus = string.Empty;

            // v1.0.160.108 H2: the ONE role decision for this request, taken before the first await.
            // MUST be AiDebugAllowed(), never the raw _luSiteKeep: AiDebugAllowed also grants via the
            // SiteKeepingOverrideEmails list, so using the raw flag would silently revoke access for
            // every override user.
            internal bool _roleSiteKeep;

            // v1.0.160.112 CUT F.
            internal DateTime _snapshotRetrievedAt = DateTime.MinValue;
            internal JArray _snapshotValues;
            internal string _predictRaw;
            // v1.0.160.153: set when any prefetch input came back as an E7-API failure text; feeds the
            // degraded_inputs stamp on the verdict.
            internal bool _e7ApiInputFailed;
            // v1.0.160.154: reduced per-asset alert history (Layer A); injected into the verdict as
            // asset_history and rendered as the story's pie chapter.
            internal JObject _assetHistory;

            // ==================================================================
            //  v1.0.160.128 RESTORED BLOCK.
            //  These thirteen members were deleted by a slice-based edit in .126: that edit replaced
            //  everything between BuildAssetCondition() and CaptureLoginUser(), and every member added
            //  earlier in the programme had been inserted at exactly that anchor. Nothing flagged it --
            //  brace balance stayed 0/0/0 because whole well-formed members were removed. Only the
            //  compiler caught it, as 22 CS0103s.
            //  Kept together, ABOVE CaptureLoginUser, so a future anchored edit cannot silently take
            //  them again.
            // ==================================================================

            // SL 1: set when K2 could not run on an asset where it SHOULD have; surfaced in caveats so
            // the ABSENCE of the check is visible to the reader, not just to a server log.
            internal string _circuitSkipReason;

            // CUT C: wisdom attribution. Ids are internal taxonomy -- trace/audit only, never a browser.
            internal readonly JArray _wisdomInjected = new JArray();
            internal JArray _wisdomUsed;
            internal bool _wisdomIdStripLogged;

            internal JObject _prefetchCtx;   // set by the verdict endpoint before the loop runs
            internal bool _synthesisMode;    // one call, no tools, compact prompt
            // Synthesis removes the model's ability to fetch anything, so it must only
            // engage when the evidence is genuinely COMPLETE. "History exists" is not
            // enough: a series that stops before the incidence still set _jsonGotHistory,
            // and the model was then unable to request a narrower window -- turning a
            // recoverable gap into an avoidable INCONCLUSIVE.
            internal bool _prefetchComplete;
            internal string _prefetchGap;
            internal string _triggerAttId;          // v1.0.159.0: AssetAttributeId of the alerting tag
            internal string _gatedRefSummary;       // v1.0.159.0: gated stats straight from TimescaleDB
            internal string _gateNameUsed;          // v1.0.160.0: which relay proved the reading

            // ---- enforced read-only tool policy for verdict mode ----
            internal Dictionary<string, int> _jsonToolUsed;
            internal int _jsonToolTotal;
            internal readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _ipsAssetBySite
                = new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.Ordinal);

            internal string _jsonRequestId;

            // -- v1.0.5 AnalyzeAlertJson -- when capture is active, SSE output is
            // redirected into a buffer instead of the HTTP response. Streaming
            // endpoints are unaffected (capture is null for them).
            internal StringBuilder _jsonCapture;
            internal string _jsonCaptureError;

            internal bool _servedFromCache;   // set when a hit was rendered, for the audit and the UI
        }
    }
}
