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

        internal static OpenAiClient CreateOpenAiClient(int maxTokens)
        {
            return new OpenAiClient(
                ConfigurationManager.AppSettings["OpenAiApiKey"],
                ReadStr("OpenAiModel", "gpt-5.4"),
                maxTokens,
                ReadStr("OpenAiBaseUrl", "https://api.openai.com/v1"));
        }


        internal static DeepSeekClient CreateDeepSeekClient(int maxTokens)
        {
            // v1.0.135.0: thinking defaults OFF (DeepSeek V4 enables it by default server-side,
            // adding 30-80s hidden reasoning billed as output). DeepSeekThinking=true re-enables.
            return new DeepSeekClient(
                ConfigurationManager.AppSettings["DeepSeekApiKey"],
                ReadStr("DeepSeekModel", "deepseek-v4-pro"),
                maxTokens,
                ReadStr("DeepSeekBaseUrl", "https://api.deepseek.com/v1"),
                ReadBool("DeepSeekThinking", false));
        }

        // Moved to E7.AiCore.EngineIdentity.ParseThinkCtx in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static bool? ParseThinkCtx(string v) => EngineIdentity.ParseThinkCtx(v);


        internal static AnthropicClient CreateSynthesisClient()
        {
            string key = ConfigurationManager.AppSettings["AnthropicApiKey"];
            string model = ConfigurationManager.AppSettings["AnthropicModel"];
            if (string.IsNullOrWhiteSpace(model)) { model = "claude-sonnet-4-6"; }
            return new AnthropicClient(key, model, ClampCfg(ReadInt("AnalyzeSynthesisMaxTokens", 1200), 400, 4000));
        }


        internal static AnthropicClient CreateAiClient()
        {
            string key = ConfigurationManager.AppSettings["AnthropicApiKey"];
            string model = ConfigurationManager.AppSettings["AnthropicModel"] ?? "claude-sonnet-4-6";
            int tokens = ReadInt("AnthropicMaxTokens", 8096);
            if (string.IsNullOrEmpty(key))
                throw new Exception("AnthropicApiKey missing in web.config");
            return new AnthropicClient(key, model, tokens);
        }

        // Moved to E7.AiCore.TrainingText.LoadTrainingText in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static string LoadTrainingText() => TrainingText.LoadTrainingText();

        // v1.0.160.1: MERGED FROM THE OTHER TEAM'S 1.0.159.2 DROP -- role gate on the provider.
        // A non-site-keeping user is forced onto QUANTUM (deepseek) regardless of what the client
        // asked for; site-keeping users keep their choice. Server-side enforcement, so hiding the
        // picker in the view is presentation only and cannot be bypassed by a crafted request.
        internal string GateProviderForRole(string requested, bool siteKeep)
        {
            if (siteKeep) { return requested; }
            string p = (requested ?? "").Trim().ToLowerInvariant();
            // v1.0.160.42: PRESERVE DeepSeek/QUANTUM variants (quantum11 = Flash). Collapsing every
            // request to the bare "deepseek" string discarded the variant, so an ordinary user who
            // picked QUANTUM 1.1 silently got the base PRO model. NEXUS/ORION are still forced.
            if (p.Contains("quantum") || p.Contains("deep")) { return p; }
            if (p.Length > 0)
            {
                AiLog("WARN", "GATE", "non-sitekeeping user requested provider='" + p + "'; forced to deepseek (QUANTUM)");
            }
            return "deepseek";
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


        internal Task<string> CallModelOnceAsync(object[] messages, object[] tools, string systemPrompt)
        {
            string modelOverride = AnalysisModelOverride;   // v1.0.158.0: NEXUS/ORION variant model string, else null
            if (UseDeepSeek)
            {
                // DeepSeek is OpenAI-compatible and caches repeated prefixes automatically.
                // v1.0.160.42: this branch previously ignored modelOverride (only the Anthropic and
                // OpenAI branches honoured it), so QUANTUM 1.1 would have run the base PRO model.
                // Variant clients are cached per model+thinking+maxTokens: PRO/FLASH x THINK/NO-THINK.
                bool think = ThinkOn;
                bool dsDefaultThink = ReadBool("DeepSeekThinking", false);
                if (string.IsNullOrEmpty(modelOverride) && think == dsDefaultThink)
                {
                    DeepSeekClient dc0 = Sess._synthesisMode ? _deepseekSynth.Value : _deepseek.Value;
                    return dc0.SendMessageAsync(messages, tools, systemPrompt);
                }
                string dsModel = string.IsNullOrEmpty(modelOverride) ? ReadStr("DeepSeekModel", "deepseek-v4-pro") : modelOverride;
                int dsMax = Sess._synthesisMode ? ClampCfg(ReadInt("AnalyzeSynthesisMaxTokens", 1200), 400, 4000) : ReadInt("DeepSeekMaxTokens", 8096);
                string dsKey = "ds:" + dsModel + ":" + (think ? "1" : "0") + ":" + dsMax;
                DeepSeekClient dc = (DeepSeekClient)_variantClients.GetOrAdd(dsKey, k =>
                    new DeepSeekClient(
                        ConfigurationManager.AppSettings["DeepSeekApiKey"],
                        dsModel,
                        dsMax,
                        ReadStr("DeepSeekBaseUrl", "https://api.deepseek.com/v1"),
                        think));
                return dc.SendMessageAsync(messages, tools, systemPrompt);
            }
            if (UseOpenAi)
            {
                // no cache_control plumbing here: OpenAI caches repeated prefixes
                // automatically, so there is nothing to pass.
                OpenAiClient oc;
                if (!string.IsNullOrEmpty(modelOverride))   // ORION 1.6/1.7 -> GPT-5.6 / Sol (or configured)
                {
                    oc = (OpenAiClient)_variantClients.GetOrAdd("oai:" + modelOverride, k =>
                        new OpenAiClient(
                            ConfigurationManager.AppSettings["OpenAiApiKey"],
                            modelOverride,
                            Sess._synthesisMode ? ClampCfg(ReadInt("AnalyzeSynthesisMaxTokens", 1200), 400, 4000) : ReadInt("AnthropicMaxTokens", 8096),
                            ReadStr("OpenAiBaseUrl", "https://api.openai.com/v1")));
                }
                else { oc = Sess._synthesisMode ? _oaiSynth.Value : _oai.Value; }
                return oc.SendMessageAsync(messages, tools, systemPrompt);
            }

            AnthropicClient client;
            if (!string.IsNullOrEmpty(modelOverride))   // NEXUS 1.6/1.9/4.9 -> Opus 4.6 / 4.8 / Fable (or configured)
            {
                client = (AnthropicClient)_variantClients.GetOrAdd("ant:" + modelOverride, k =>
                    new AnthropicClient(
                        ConfigurationManager.AppSettings["AnthropicApiKey"],
                        modelOverride,
                        Sess._synthesisMode ? ClampCfg(ReadInt("AnalyzeSynthesisMaxTokens", 1200), 400, 4000) : ReadInt("AnthropicMaxTokens", 8096)));
            }
            else { client = Sess._synthesisMode ? _aiSynth.Value : _ai.Value; }
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
