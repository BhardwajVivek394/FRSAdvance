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
using AiModelConfig = E7.AiCore.AiModelConfig;   // 616 merge: Domain.AiModelConfig (the AI Model Config admin screen) has the same name
using E7.AiCore;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    // AiChatController -- prompt-extraction refusal, scope checks, scrubbing, redaction, role checks.
    // Split out of AiChatController.cs in 1.0.161.0 (move-only; see CHANGELOG.md).
    public partial class AiChatController : Controller
    {

        // v1.0.160.24 (Item 9): DOCTRINAL SIGNALLING WISDOM -- 149 normative/derived rules curated
        // from RDSO SPN/257/2025, IRSEM 2021, and the Glued Joint Manual (RailwaySignallingWisdom).
        // Retrieval-injected: the top-N rules scored for this cause + asset family are appended as an
        // ADVISORY reference block. SUBORDINATE to CauseLogicMaps/RdpmsMaps (the class's SafetyContract
        // says so). Best-effort; a fault here never sinks the verdict. Off via AnalyzeDoctrinalWisdom=false.
        // v1.0.160.50 PHASE 1: circuit topology + upstream causality, retrieval-injected per family.
        // ADVISORY and SUBORDINATE to the FRS cause rule + RDPMS maps, exactly like the doctrinal block.
        // 1.0.197.0: one ai-log line per alert analysis with the token totals of every model call in it
        // (verdict + Hindi translation). answer = out - thinking. No cost: see Build7WAudit's rule.
        private string TokensLine(JObject ctx, long totalMs)
        {
            string pk = Engine.ProviderKind;
            string engine = !string.IsNullOrEmpty(Engine.Sess._provider) ? Engine.Sess._provider : AiModelConfig.DefaultEngine(pk);
            string mo = Engine.AnalysisModelOverride;
            long answer = Engine.Sess._trTokOut - Engine.Sess._trTokThinking;
            if (answer < 0)
            {
                answer = 0;
            }
            return "alert=" + AnalysisEngine.GetCtx(ctx, "alertId")
                + " cause=" + AnalysisEngine.GetCtx(ctx, "causeCode")
                + " engine=" + engine + " provider=" + pk + " model=" + (string.IsNullOrEmpty(mo) ? "(config default)" : mo)
                + " calls=" + Engine.Sess._trTurns
                + " in=" + Engine.Sess._trTokIn + " out=" + Engine.Sess._trTokOut
                + " thinking=" + Engine.Sess._trTokThinking + " answer=" + answer
                + " cacheRead=" + Engine.Sess._trTokCacheRead + " cacheWrite=" + Engine.Sess._trTokCacheWrite
                + " totalMs=" + totalMs;
        }

        // v1.0.160.57: how many RULES an injected block contains. Rules are emitted one per line
        // starting "- " (doctrinal, circuit) or "* " (checklist/field), so counting those lines is
        // exact rather than a guess from character length.
        // v1.0.160.57: the 7W audit record. Field names are chosen to map 1:1 onto the planned RAG
        // columns, so the migration is an INSERT..SELECT over this JSONL rather than a second round
        // of instrumentation. Written once per run in the finally block, so it survives error paths.
        // NO usd estimate: token counts are exact, a price is not, and a wrong cost in an audit table
        // is worse than an absent one. Add it when a per-provider rate table is agreed.
        private JObject Build7WAudit(JObject ctx, string requestType, DateTime startUtc)
        {
            JObject a = new JObject();
            try
            {
                JObject who = new JObject();
                // v1.0.160.68: the PORTAL login first. ResolveWisdomIdentity checks User.Identity
                // (anonymous -- this portal runs its own login) then an unconfigured session key, so it
                // always fell through to WisdomDefaultIdentity and every row read "ENERGY7".
                // v1.0.160.85: use the identity captured before the first await. Reading LoginUser
                // here returns null -- ConfigureAwait(false) has already dropped HttpContext.Current.
                string usr = Engine.Sess._luName ?? "";
                if (!string.IsNullOrEmpty(Engine.Sess._luEmail)) { who["email"] = Engine.Sess._luEmail; }
                if (usr.Length == 0) { try { usr = ResolveWisdomIdentity(); } catch { } }
                who["user"] = usr;
                bool sk = Engine.Sess._luSiteKeep || AnalysisEngine.SafeIsSiteKeeping();   // v1.0.160.85: captured value first
                who["role"] = sk ? "sitekeeping" : "operator";
                try { who["ip"] = (Request != null) ? Request.UserHostAddress : ""; } catch { who["ip"] = ""; }
                try { who["session"] = (Session != null) ? Session.SessionID : ""; } catch { who["session"] = ""; }
                a["who"] = who;

                JObject what = new JObject();
                what["bytesSent"] = Engine.Sess._trBytesSent; what["bytesRecv"] = Engine.Sess._trBytesRecv;
                if (Engine.Sess._lastVerdictSummary != null) { what["verdict"] = Engine.Sess._lastVerdictSummary; }
                // v1.0.160.68: owner asked for the raw JSON BOTH WAYS in the audit row itself, not just
                // sizes plus a traceId join into the prompt/envelope steps.
                if (!string.IsNullOrEmpty(Engine.Sess._lastPromptFull)) { what["promptFull"] = Engine.Sess._lastPromptFull; }
                if (!string.IsNullOrEmpty(Engine.Sess._lastResponseFull)) { what["responseFull"] = Engine.Sess._lastResponseFull; }
                a["what"] = what;

                JObject when = new JObject();
                when["startUtc"] = startUtc.ToString("o");
                when["endUtc"] = DateTime.UtcNow.ToString("o");
                when["totalMs"] = (long)(DateTime.UtcNow - startUtc).TotalMilliseconds;
                when["modelMs"] = Engine.Sess._trModelMs; when["toolMs"] = Engine.Sess._trToolMs; when["discoveryMs"] = Engine.Sess._trDiscoveryMs;
                // v1.0.160.110 SL5: separate the summed per-call figure from WALL-CLOCK phases.
                // Prefetch is concurrent, so toolCallMsSum can exceed prefetchWallMs -- only the
                // wall-clock figures may be used to reconcile against total.
                when["toolCallMsSum"] = System.Threading.Interlocked.Read(ref Engine.Sess._trToolCallMsSum);
                when["prefetchWallMs"] = Engine.Sess._trPrefetchWallMs;
                when["inLoopWallMs"] = Engine.Sess._trInLoopWallMs;
                when["prefetchCalls"] = Engine.Sess._trPrefetchCalls;
                when["inLoopCalls"] = Engine.Sess._trToolCallCount;
                // v1.0.160.110 SL6: "turns" counts BILLABLE model calls (the Hindi translation is a
                // second paid call). Emitted as modelCalls; turns kept as an alias for one release.
                when["modelCalls"] = Engine.Sess._trTurns;
                when["analysisTurns"] = Engine.Sess._trAnalysisTurns;
                a["when"] = when;

                JObject where = new JObject();
                where["siteId"] = AnalysisEngine.GetCtx(ctx, "siteId"); where["station"] = AnalysisEngine.GetCtx(ctx, "station");
                try { where["node"] = Environment.MachineName; } catch { where["node"] = ""; }
                where["endpoint"] = requestType;
                string pk = Engine.ProviderKind;
                where["providerUrl"] = (pk == "deepseek") ? AnalysisEngine.ReadStr("DeepSeekBaseUrl", "")
                                     : (pk == "openai") ? AnalysisEngine.ReadStr("OpenAiBaseUrl", "") : "anthropic";
                a["where"] = where;

                JObject which = new JObject();
                which["engine"] = !string.IsNullOrEmpty(Engine.Sess._provider) ? Engine.Sess._provider : AiModelConfig.DefaultEngine(pk);   // 1.0.195.0: engine that ran
                which["provider"] = pk;
                string mo = Engine.AnalysisModelOverride;   // v1.0.160.62: PROPERTY, not a method
                which["model"] = !string.IsNullOrEmpty(mo) ? mo
                    : AiModelConfig.ModelFor(pk, pk == "deepseek" ? AnalysisEngine.ReadStr("DeepSeekModel", "")
                    : (pk == "openai" ? AnalysisEngine.ReadStr("OpenAiModel", "") : AnalysisEngine.ReadStr("AnthropicModel", "")));
                which["thinking"] = Engine.ThinkOn;
                which["roleGated"] = !sk;
                a["which"] = which;

                JObject why = new JObject();
                why["requestType"] = requestType;
                why["alertId"] = AnalysisEngine.GetCtx(ctx, "alertId");
                why["alertType"] = AnalysisEngine.GetCtx(ctx, "alertType");
                why["causeCode"] = AnalysisEngine.GetCtx(ctx, "causeCode");
                why["asset"] = AnalysisEngine.GetCtx(ctx, "assetName");
                a["why"] = why;

                JObject cost = new JObject();
                cost["tokIn"] = Engine.Sess._trTokIn; cost["tokOut"] = Engine.Sess._trTokOut;
                cost["cacheRead"] = Engine.Sess._trTokCacheRead; cost["cacheWrite"] = Engine.Sess._trTokCacheWrite;
                cost["tokThinking"] = Engine.Sess._trTokThinking;   // 1.0.197.0: fills audit DB TotalTokensThinking
                cost["toolCalls"] = Engine.Sess._trToolCallCount + Engine.Sess._trPrefetchCalls;   // v1.0.160.63: loop + prefetch
                cost["toolCallsInLoop"] = Engine.Sess._trToolCallCount;
                cost["toolCallsPrefetch"] = Engine.Sess._trPrefetchCalls;
                cost["toolMsInLoop"] = Engine.Sess._trToolMs;   // v1.0.160.63: prefetch ms is NOT measured here --
                                                    // reporting a total that omits it would be wrong,
                                                    // and the per-call timing is already in the trace.
                cost["iterations"] = Engine.Sess._trTurns;
                cost["timeoutSec"] = (AnalysisEngine._bashInVerdict && Engine.ThinkOn) ? 0 : AnalysisEngine.ReadInt("AnalyzeJsonTimeoutSec", 120);
                // v1.0.160.68: INR estimate. Rates are per 1M tokens in USD, from DeepSeek's published
                // table (checked 2026-08-11), overridable in config so a price change needs no rebuild.
                // Cache-hit input is billed FAR lower than cache-miss -- on v4-flash 0.0028 vs 0.14, a
                // 50x difference -- so ignoring cacheRead would overstate the bill badly.
                try
                {
                    string mdl = (which["model"] != null ? which["model"].ToString() : "").ToLowerInvariant();
                    double inMiss = -1, inHit = -1, outR = -1;
                    if (mdl.Contains("v4-flash"))
                    {
                        inMiss = AnalysisEngine.ReadDbl("RateFlashInMissUsdPerM", 0.14);
                        inHit = AnalysisEngine.ReadDbl("RateFlashInHitUsdPerM", 0.0028);
                        outR = AnalysisEngine.ReadDbl("RateFlashOutUsdPerM", 0.28);
                    }
                    else if (mdl.Contains("v4-pro"))
                    {
                        inMiss = AnalysisEngine.ReadDbl("RateProInMissUsdPerM", 0.435);
                        inHit = AnalysisEngine.ReadDbl("RateProInHitUsdPerM", 0.003625);
                        outR = AnalysisEngine.ReadDbl("RateProOutUsdPerM", 0.87);
                    }
                    // Unknown model -> NO cost field. A wrong number in an audit table is worse than none.
                    if (inMiss >= 0)
                    {
                        double hitTok = Engine.Sess._trTokCacheRead;
                        double missTok = Engine.Sess._trTokIn - hitTok; if (missTok < 0) { missTok = Engine.Sess._trTokIn; }
                        double usd = (missTok / 1000000.0) * inMiss
                                   + (hitTok / 1000000.0) * inHit
                                   + (Engine.Sess._trTokOut / 1000000.0) * outR;
                        double fx = AnalysisEngine.ReadDbl("AiUsdToInr", 88.0);
                        cost["costUsdEst"] = Math.Round(usd, 6);
                        cost["costInrEst"] = Math.Round(usd * fx, 4);
                        cost["rateBasis"] = mdl + " @ " + inMiss + "/" + inHit + "/" + outR + " USD per 1M; fx " + fx;
                    }
                }
                catch { }
                a["cost"] = cost;

                JObject wis = new JObject();
                wis["doctrinal"] = Engine.Sess._wisDoctrinal; wis["field"] = Engine.Sess._wisField;
                wis["circuit"] = Engine.Sess._wisCircuit; wis["learned"] = Engine.Sess._wisLearned;
                wis["checklist"] = Engine.Sess._wisChecklist; wis["chars"] = Engine.Sess._wisChars;
                if (!string.IsNullOrEmpty(Engine.Sess._driverSetStr)) { wis["driverSet"] = Engine.Sess._driverSetStr; }
                a["wisdom"] = wis;
                // v1.0.160.115 CUT B: record what the inputs actually looked like, so
                // "how many verdicts were issued on dead inputs?" becomes answerable. Today it is not.
                // v1.0.160.117 CUT C: what was injected, and what the model says it used. Audit/trace
                // ONLY -- these ids are never added to the non-privileged metadata allowlist.
                if (Engine.Sess._wisdomInjected.Count > 0) { wis["injected"] = Engine.Sess._wisdomInjected; }
                if (Engine.Sess._wisdomUsed != null && Engine.Sess._wisdomUsed.Count > 0) { wis["used"] = Engine.Sess._wisdomUsed; }
                JArray sh = Engine.BuildSensorHealth();
                if (sh != null && sh.Count > 0) { a["sensorHealth"] = sh; }
            }
            catch { }
            return a;
        }
    }
}