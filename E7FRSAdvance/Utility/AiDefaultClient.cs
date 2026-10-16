using System;
using System.Collections.Concurrent;
using System.Configuration;
using System.Threading.Tasks;
using Domain;   // AnthropicClient
using E7MRIWeb.Areas.FRS25.Controllers;   // DeepSeekClient / OpenAiClient (Domain keeps the 616 namespace)

namespace E7FRSAdvance.AiCore
{
    // ==================================================================================
    //  AiDefaultClient -- one-shot "summary" calls on the AiModelConfig IsDefault engine.
    //
    //  For pages that just send one prompt and read one text reply (PM Operation AI summary,
    //  Track Leakage AI summary). The engine row decides provider + model + key; when the API
    //  has not answered, the page's own Web.config fallback provider is used (its *Model /
    //  *ApiKey / *BaseUrl keys), so behaviour without the API is unchanged.
    //  Haiku 5.5 only: max_tokens = the model's own output cap, effort "low" (as everywhere else).
    //  All three clients return the same Anthropic-shaped JSON ({"content":[{"type":"text",...}]}).
    // ==================================================================================
    public static class AiDefaultClient
    {
        // provider:model:keyTag:maxTokens:effort -> client. KeyTag, never the key, in the cache key.
        private static readonly ConcurrentDictionary<string, object> _clients =
            new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The provider/model this call will use (for log lines), and whether a key exists.</summary>
        public static string Describe(string fallbackProvider, out bool hasKey)
        {
            string provider, model, key;
            Pick(fallbackProvider, out provider, out model, out key);
            hasKey = !string.IsNullOrEmpty(key);
            return "provider=" + provider + " model=" + model;
        }

        /// <summary>
        /// Send one prompt to the IsDefault engine. fallbackProvider (deepseek | openai | anthropic)
        /// is used when the API has no row. maxTokens applies to every model except Haiku 5.5.
        /// </summary>
        public static Task<string> SendAsync(object[] messages, string systemPrompt, int maxTokens, string fallbackProvider)
        {
            string provider, model, key;
            Pick(fallbackProvider, out provider, out model, out key);
            if (string.IsNullOrEmpty(key))
            {
                throw new InvalidOperationException("AI key is not configured (provider " + provider + ").");
            }

            if (provider == "deepseek")
            {
                DeepSeekClient dc = (DeepSeekClient)_clients.GetOrAdd(
                    "ds:" + model + ":" + AiModelConfig.KeyTag(key) + ":" + maxTokens,
                    k => new DeepSeekClient(key, model, maxTokens, Cfg("DeepSeekBaseUrl", "https://api.deepseek.com/v1"), false));
                return dc.SendMessageAsync(messages, null, systemPrompt);
            }
            if (provider == "openai")
            {
                OpenAiClient oc = (OpenAiClient)_clients.GetOrAdd(
                    "oai:" + model + ":" + AiModelConfig.KeyTag(key) + ":" + maxTokens,
                    k => new OpenAiClient(key, model, maxTokens, Cfg("OpenAiBaseUrl", "https://api.openai.com/v1")));
                return oc.SendMessageAsync(messages, null, systemPrompt);
            }

            bool haiku = AiModelConfig.IsHaiku55(model);
            int antMax = haiku ? AiModelConfig.AnthropicOutputTokens(model, key) : maxTokens;
            string effort = haiku ? "low" : null;
            AnthropicClient ac = (AnthropicClient)_clients.GetOrAdd(
                "ant:" + model + ":" + AiModelConfig.KeyTag(key) + ":" + antMax + ":" + (effort ?? "default"),
                k => new AnthropicClient(key, model, antMax, effort));
            return ac.SendMessageAsync(messages, null, systemPrompt);
        }

        // API IsDefault row first (provider, model, key); Web.config for whatever the API does not give.
        private static void Pick(string fallbackProvider, out string provider, out string model, out string key)
        {
            provider = (AiModelConfig.DefaultProvider(fallbackProvider ?? "deepseek") ?? "deepseek").Trim().ToLowerInvariant();
            if (provider == "openai")
            {
                model = AiModelConfig.ModelFor("openai", Cfg("OpenAiModel", "gpt-5.4"));
                key = AiModelConfig.KeyFor("openai", Cfg("OpenAiApiKey", ""));
            }
            else if (provider == "deepseek")
            {
                model = AiModelConfig.ModelFor("deepseek", Cfg("DeepSeekModel", "deepseek-v4-pro"));
                key = AiModelConfig.KeyFor("deepseek", Cfg("DeepSeekApiKey", ""));
            }
            else
            {
                provider = "anthropic";
                model = AiModelConfig.ModelFor("anthropic", Cfg("AnthropicModel", "claude-sonnet-4-6"));
                key = AiModelConfig.KeyFor("anthropic", Cfg("AnthropicApiKey", ""));
            }
            key = (key ?? "").Trim();
        }

        private static string Cfg(string name, string def)
        {
            string v = ConfigurationManager.AppSettings[name];
            return string.IsNullOrWhiteSpace(v) ? def : v.Trim();
        }
    }
}
