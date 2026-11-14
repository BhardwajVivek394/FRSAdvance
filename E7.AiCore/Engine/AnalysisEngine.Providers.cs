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
    // AnalysisEngine -- Providers.
    // Moved verbatim from AiChatController.Providers.cs in 1.0.165.0 (Shared AI Core 2e);
    // only access modifiers changed (private -> internal). The web app sees internals via InternalsVisibleTo.
    public sealed partial class AnalysisEngine
    {

        // 1.0.194.0 -- CreateOpenAiClient / CreateDeepSeekClient / CreateSynthesisClient /
        // CreateAiClient REMOVED. Each one read a vendor's *ApiKey and *Model appSetting and
        // hardcoded a model fallback ("gpt-5.4", "deepseek-v4-pro", "claude-sonnet-4-6"). Those
        // fallbacks were the quiet half of the problem: a missing key did not fail, it silently ran
        // a model nobody had chosen. Clients are now built in CallModelOnceAsync from the
        // AiModelConfig row, which carries the key and the model together, so there is no fallback
        // to disagree with the table. Verified zero other callers in either project before removing.

        // Moved to E7.AiCore.EngineIdentity.ParseThinkCtx in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static bool? ParseThinkCtx(string v) => EngineIdentity.ParseThinkCtx(v);

        // Moved to E7.AiCore.TrainingText.LoadTrainingText in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static string LoadTrainingText() => TrainingText.LoadTrainingText();

        // v1.0.160.1: MERGED FROM THE OTHER TEAM'S 1.0.159.2 DROP -- role gate on the provider.
        // A non-site-keeping user is forced onto QUANTUM (deepseek) regardless of what the client
        // asked for; site-keeping users keep their choice. Server-side enforcement, so hiding the
        // picker in the view is presentation only and cannot be bypassed by a crafted request.
        // =========================================================================================
        // 1.0.194.0 -- D3 (16.10.2026): an ordinary user gets THE DEFAULT ROW, not a hardcoded
        // vendor. The old body ended `return "deepseek";` -- a vendor name compiled into the
        // method. With the table in charge that line would have fought it: flip IsDefault to
        // nexus20 and privileged users would move while ordinary users stayed on DeepSeek, which is
        // the opposite of what a column called IsDefault promises.
        //
        // NOTE this method is now BELT AND BRACES, not the enforcement point. AnalysisEngine.AiRow
        // resolves with Sess._roleSiteKeep itself, so a caller that forgets to gate (Endpoints
        // L1440 does exactly that) still cannot obtain a restricted engine. This method stays
        // because two call sites use its return value as Sess._provider, and because a gate that
        // reads correctly at the call site is worth keeping even once it is redundant.
        // =========================================================================================
        internal string GateProviderForRole(string requested, bool siteKeep)
        {
            if (siteKeep) { return requested; }
            string p = (requested ?? "").Trim();
            string def;
            try { def = AiModelCatalog.Default().EngineCode; }
            catch (Exception ex)
            {
                // the catalogue is unreachable AND uncached -- say so and hand back what was asked
                // for; AiRow will throw the same error at the point the call is actually made,
                // where it can be reported to the caller properly.
                AiLog("ERROR", "GATE", "cannot read AiModelConfig to gate this request: " + ex.Message);
                return p;
            }
            if (p.Length > 0 && !string.Equals(p, def, StringComparison.OrdinalIgnoreCase))
            {
                AiLog("WARN", "GATE", "non-sitekeeping user requested engine='" + p
                    + "'; forced to the AiModelConfig default '" + def + "'");
            }
            return def;
        }

        // Moved to E7.AiCore in 1.0.163.0 (forwarder keeps call sites unchanged).
        internal static bool IsSiteKeepOverrideEmail(string email) => EngineIdentity.IsSiteKeepOverrideEmail(email, _siteKeepOverride);

        // Moved to E7.AiCore.EngineIdentity.ResolveEngineAlias in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static string ResolveEngineAlias(string requested) => EngineIdentity.ResolveEngineAlias(requested);

        // Moved to E7.AiCore.EngineIdentity.OpaqueProviderId in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static string OpaqueProviderId(string input) => EngineIdentity.OpaqueProviderId(input);

        // Moved to E7.AiCore in 1.0.163.0 (forwarder keeps call sites unchanged).
        internal static string EngineName(int owner) => EngineIdentity.EngineName(owner, _engineName1, _engineName2);

        // Moved to E7.AiCore in 1.0.163.0 (forwarder keeps call sites unchanged).
        internal static string MaskModelIdentity(string text) => EngineIdentity.MaskModelIdentity(text, ReadStr("AiEngineBrand", "ENERGY7 ULTRA"));


        internal async Task<string> CallModelAsync(object[] messages, object[] tools, string systemPrompt)
        {
            int attempts = 1 + _modelRetries;
            System.Diagnostics.Stopwatch swRetry = System.Diagnostics.Stopwatch.StartNew();
            for (int a = 0; a < attempts; a++)
            {
                try
                {
                    return await CallModelOnceAsync(messages, tools, systemPrompt).ConfigureAwait(false);
                }
                catch (ClientBusyException) { throw; }          // 503 backpressure, not transient
                catch (OperationCanceledException) { throw; }    // cancellation, not transient
                catch (MissingMethodException) { throw; }        // DLL mismatch, handled in CallModelOnceAsync
                catch (Exception ex)
                {
                    // v1.0.160.181: log the TRUE cause (inner chain) to the server log before the
                    // message is sanitized to "internal error". This is the line that names DNS vs
                    // firewall vs TLS vs dead-host for a bytesRecv=0 connection failure.
                    AiLog("ERROR", "AI", "model call exception [engine=" + (Sess._provider ?? "?")
                        + "]: " + DescribeException(ex));
                    bool more = (a + 1) < attempts;
                    if (!more || swRetry.ElapsedMilliseconds > _modelRetryMaxElapsedMs)
                    {
                        AiLog("WARN", "AI", "model call failed, no retry" +
                            (more ? " (past retry budget)" : "") + ": " + SanitizeJsonError(ex.Message));
                        throw;
                    }
                    AiLog("WARN", "AI", "model call transient failure, retry " + (a + 1) + "/" +
                        (attempts - 1) + " after " + _modelRetryBackoffMs + "ms: " + SanitizeJsonError(ex.Message));
                    await Task.Delay(_modelRetryBackoffMs).ConfigureAwait(false);
                }
            }
            return null; // unreachable: the loop either returns or throws
        }


        // =========================================================================================
        // 1.0.194.0 -- ONE CLIENT BUILD PATH, KEYED ON THE ROW.
        //
        // Was: three branches, each choosing between a process-lifetime Lazy<> singleton (the base
        // model) and a _variantClients entry (the NEXUS/ORION/QUANTUM variants). The singleton half
        // is what made a table edit invisible until a recycle, so both halves now go through
        // _variantClients. The key carries provider + model + maxTokens + thinking: an unchanged row
        // reuses its client, a changed row builds a new one on the next request.
        //
        // The branching that REMAINS is only about the wire format -- Anthropic's /v1/messages with
        // its prompt-cache parameters, versus the OpenAI-compatible shape that DeepSeek and OpenAI
        // both speak. That is a real difference between transports and is unchanged, including the
        // three-tier MissingMethodException fallback for older AnthropicClient builds.
        // =========================================================================================
        internal Task<string> CallModelOnceAsync(object[] messages, object[] tools, string systemPrompt)
        {
            AiModelRow row = AiRow;
            int synthMax = ClampCfg(ReadInt("AnalyzeSynthesisMaxTokens", 1200), 400, 4000);
            bool synth = Sess._synthesisMode;

            if (UseDeepSeek)
            {
                bool think = ThinkOn;
                int dsMax = synth ? synthMax : row.TokenCeiling(ReadInt("DeepSeekMaxTokens", 8096));
                string dsKey = "ds:" + row.EngineCode + ":" + row.ModelName + ":" + (think ? "1" : "0") + ":" + dsMax;
                DeepSeekClient dc = (DeepSeekClient)_variantClients.GetOrAdd(dsKey, k =>
                    new DeepSeekClient(row.Key, row.ModelName, dsMax, row.BaseUrlOrConfig(), think));
                return dc.SendMessageAsync(messages, tools, systemPrompt);
            }

            if (UseOpenAi)
            {
                // no cache_control plumbing here: OpenAI caches repeated prefixes automatically.
                int oaMax = synth ? synthMax : row.TokenCeiling(ReadInt("AnthropicMaxTokens", 8096));
                string oaKey = "oai:" + row.EngineCode + ":" + row.ModelName + ":" + oaMax;
                OpenAiClient oc = (OpenAiClient)_variantClients.GetOrAdd(oaKey, k =>
                    new OpenAiClient(row.Key, row.ModelName, oaMax, row.BaseUrlOrConfig()));
                return oc.SendMessageAsync(messages, tools, systemPrompt);
            }

            int antMax = synth ? synthMax : row.TokenCeiling(ReadInt("AnthropicMaxTokens", 8096));
            string antKey = "ant:" + row.EngineCode + ":" + row.ModelName + ":" + antMax;
            AnthropicClient client = (AnthropicClient)_variantClients.GetOrAdd(antKey, k =>
                new AnthropicClient(row.Key, row.ModelName, antMax));

            if (!_aiCacheParamMissing)
            {
                try
                {
                    // v1.0.113.0: pass the 1-hour-TTL flag. Requires AnthropicClient 1.0.2.0+
                    // (5-arg overload). On an older DLL this throws MissingMethodException and we
                    // fall back to the 4-arg call below (5-min cache) or the 3-arg reflection path.
                    return client.SendMessageAsync(messages, tools, systemPrompt, _promptCache, _promptCache && _promptCache1h);
                }
                catch (MissingMethodException)
                {
                    // 5-arg overload absent - try the 4-arg (cacheSystem only, 5-min TTL).
                    try
                    {
                        return client.SendMessageAsync(messages, tools, systemPrompt, _promptCache);
                    }
                    catch (MissingMethodException)
                    {
                        _aiCacheParamMissing = true;
                        AiLog("ERROR", "AI",
                            "deployed AnthropicClient has no cacheSystem parameter -- Domain.dll is older "
                            + "than this controller. Continuing WITHOUT prompt caching; deploy "
                            + "AnthropicClient 1.0.2.0 to restore it (1.0.1.0 for 5-min cache only).");
                    }
                }
            }

            if (_aiSend3 == null)
            {
                _aiSend3 = client.GetType().GetMethod("SendMessageAsync",
                    new Type[] { typeof(object[]), typeof(object[]), typeof(string) });
            }
            if (_aiSend3 == null)
            {
                throw new InvalidOperationException(
                    "AnthropicClient exposes no compatible SendMessageAsync overload. Deploy Domain.dll "
                    + "built from AnthropicClient 1.0.1.0.");
            }
            return (Task<string>)_aiSend3.Invoke(client, new object[] { messages, tools, systemPrompt });
        }

        // Moved to E7.AiCore.EngineIdentity.ScrubEngineIdentity in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static string ScrubEngineIdentity(string v) => EngineIdentity.ScrubEngineIdentity(v);
    }
}
