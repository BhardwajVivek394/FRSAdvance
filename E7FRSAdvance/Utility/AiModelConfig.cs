using System;
using System.Collections.Generic;
using System.Configuration;
using Newtonsoft.Json.Linq;

namespace E7FRSAdvance.AiCore
{
    // ----------------------------------------------------------------------
    //  AiModelConfig  -- 1.0.190.0, list form 1.0.193.0
    //
    //  Single source of the AI engines: engine code, display name, provider, model and API key.
    //  Replaces the direct Web.config reads of AiProvider, DeepSeekModel, OpenAiModel,
    //  AnthropicModel and the *ApiKey keys.
    //
    //    GET {APIBaseUrl}AiModelConfig/GetAllWithKeys
    //    -> [ { "EngineCode":"nexus10", "DisplayName":"ENERGY7 NEXUS", "Provider":"anthropic",
    //           "ModelName":"claude-sonnet-4-6", "Key":"...", "IsDefault":false }, ... ]
    //
    //  - The web app makes the call (E7FRSAdvance.Utility.AiModelConfigFilter), the same way as every
    //    other E7 API call: HttpClientFactory + ClsHttpContent.LoginUser.Token. This class only holds
    //    the answer, so it works after awaits / in background code where there is no HttpContext.
    //  - Cached for AiModelConfigCacheSec (default 300). A failed call is retried after 60 s.
    //  - The engine a request asks for (EngineCode) decides provider, model and key. Unknown or
    //    empty request -> the IsDefault row.
    //  - Fallback: nothing fetched yet / API failed -> every caller gets the value it read from
    //    Web.config before 1.0.190.0. The last good list is kept while a refresh fails.
    //  - Keys are SECRETS: never logged here, never handed to a page (pages use Engines() for
    //    EngineCode + DisplayName only).
    //  - Never throws to the caller.
    //
    //  Optional Web.config keys:
    //    AiModelConfigEnabled   (default true)
    //    AiModelConfigUrl       (path under APIBaseUrl, default "AiModelConfig/GetAllWithKeys"; a full URL also works)
    //    AiModelConfigCacheSec  (default 300)
    //    AiModelConfigTimeoutMs (default 3000)
    // ----------------------------------------------------------------------
    public static class AiModelConfig
    {
        private const int RetrySec = 60;

        private static readonly object refreshLock = new object();
        private static volatile List<AiEngineInfo> current;
        private static bool refreshInProgress;
        private static DateTime nextFetchUtc = DateTime.MinValue;

        /// <summary>Last fetch error (diagnostics); null when the last fetch succeeded.</summary>
        public static string LastError { get; private set; }

        /// <summary>True once an engine list has been stored.</summary>
        public static bool HasValue
        {
            get
            {
                return GetEngines() != null;
            }
        }

        public static bool Enabled
        {
            get
            {
                return AiSettings.ParseBool(ConfigurationManager.AppSettings["AiModelConfigEnabled"], true);
            }
        }

        /// <summary>Request path (relative to APIBaseUrl) or full URL of the API.</summary>
        public static string Path
        {
            get
            {
                return AiSettings.ParseStr(ConfigurationManager.AppSettings["AiModelConfigUrl"], "AiModelConfig/GetAllWithKeys");
            }
        }

        public static int TimeoutMs
        {
            get
            {
                int v = AiSettings.ParseInt(ConfigurationManager.AppSettings["AiModelConfigTimeoutMs"], 3000);
                if (v < 500)
                {
                    return 500;
                }
                return v;
            }
        }

        private static int CacheSec
        {
            get
            {
                int v = AiSettings.ParseInt(ConfigurationManager.AppSettings["AiModelConfigCacheSec"], 300);
                if (v < 10)
                {
                    return 10;
                }
                return v;
            }
        }

        // ---- engine list -------------------------------------------------------------------

        /// <summary>1.0.193.0: copy of all engines (dropdown). Empty when the API has not answered.</summary>
        public static List<AiEngineInfo> Engines()
        {
            List<AiEngineInfo> list = GetEngines();
            if (list == null)
            {
                return new List<AiEngineInfo>();
            }
            return new List<AiEngineInfo>(list);
        }

        /// <summary>1.0.193.0: the IsDefault row (first row when none is flagged), or null.</summary>
        public static AiEngineInfo DefaultRow()
        {
            List<AiEngineInfo> list = GetEngines();
            if (list == null || list.Count == 0)
            {
                return null;
            }
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].IsDefault)
                {
                    return list[i];
                }
            }
            return list[0];
        }

        /// <summary>
        /// 1.0.193.0: the row whose EngineCode is the requested engine (old vendor ids anthropic /
        /// openai / deepseek map to nexus10 / orion10 / quantum10), else the IsDefault row, else null.
        /// </summary>
        public static AiEngineInfo Resolve(string engine)
        {
            List<AiEngineInfo> list = GetEngines();
            if (list == null || list.Count == 0)
            {
                return null;
            }
            string code = (engine ?? "").Trim().ToLowerInvariant();
            if (code == "anthropic")
            {
                code = "nexus10";
            }
            else if (code == "openai")
            {
                code = "orion10";
            }
            else if (code == "deepseek")
            {
                code = "quantum10";
            }
            if (code.Length > 0)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (string.Equals(list[i].EngineCode, code, StringComparison.OrdinalIgnoreCase))
                    {
                        return list[i];
                    }
                }
            }
            return DefaultRow();
        }

        /// <summary>1.0.193.0: true when the engine code is a row of the API list.</summary>
        public static bool IsKnownEngine(string engine)
        {
            List<AiEngineInfo> list = GetEngines();
            if (list == null || string.IsNullOrWhiteSpace(engine))
            {
                return false;
            }
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].EngineCode, engine.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Provider (deepseek | openai | anthropic) of the requested engine, or null without an API list.</summary>
        public static string ProviderForEngine(string engine)
        {
            AiEngineInfo row = Resolve(engine);
            if (row == null)
            {
                return null;
            }
            return row.ProviderKind;
        }

        /// <summary>1.0.193.0: ModelName of the requested engine, or null without an API list.</summary>
        public static string ModelForEngine(string engine)
        {
            AiEngineInfo row = Resolve(engine);
            if (row == null)
            {
                return null;
            }
            return row.ModelName;
        }

        /// <summary>1.0.193.0: API key of the requested engine; fallback when the row has none.</summary>
        public static string KeyForEngine(string engine, string fallback)
        {
            AiEngineInfo row = Resolve(engine);
            if (row == null || string.IsNullOrWhiteSpace(row.Key))
            {
                return fallback;
            }
            return row.Key.Trim();
        }

        /// <summary>1.0.193.0: {"EngineCode":"DisplayName",...} for page scripts. No keys.</summary>
        public static string EngineLabelsJson()
        {
            JObject labels = new JObject();
            List<AiEngineInfo> list = Engines();
            for (int i = 0; i < list.Count; i++)
            {
                labels[list[i].EngineCode] = list[i].DisplayName;
            }
            return labels.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>1.0.193.0: [{"id":EngineCode,"label":DisplayName},...] for page scripts. No keys.</summary>
        public static string EngineListJson()
        {
            JArray items = new JArray();
            List<AiEngineInfo> list = Engines();
            for (int i = 0; i < list.Count; i++)
            {
                JObject item = new JObject();
                item["id"] = list[i].EngineCode;
                item["label"] = list[i].DisplayName;
                items.Add(item);
            }
            return items.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>1.0.193.0: a value as a JavaScript string literal (quotes included).</summary>
        /// <summary>
        /// main: true for Claude Haiku 5.5 (claude-haiku-5-5). The Haiku-only settings -- the model's own
        /// output cap, effort "low" unless Think, the one full-thinking re-run -- apply ONLY to this model.
        /// Every other Anthropic model keeps its original max_tokens and sends no effort.
        /// </summary>
        public static bool IsHaiku55(string model)
        {
            return !string.IsNullOrEmpty(model)
                && model.Trim().StartsWith("claude-haiku-5-5", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>main: ["engineCode",...] of API engines that run Claude Haiku 5.5 (page shows Think for them). No keys.</summary>
        public static string HaikuEngineListJson()
        {
            JArray items = new JArray();
            List<AiEngineInfo> list = Engines();
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].ProviderKind == "anthropic" && IsHaiku55(list[i].ModelName))
                {
                    items.Add(list[i].EngineCode);
                }
            }
            return items.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static string JsString(string value)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(value ?? "");
        }

        // ---- Anthropic output cap (1.0.196.0) -----------------------------------------------
        // Claude 5-family models (Haiku 5.5, Opus 5.5, Sonnet 5.5, Fable) think by default and the
        // thinking counts against max_tokens; a local cap cut the verdict off (15.10.2026: 3862 of
        // 4000 tokens were thinking). max_tokens is now the MODEL'S OWN output cap, read from
        // GET /v1/models/{model} (field max_tokens) and cached. Ceiling only -- billing is for tokens
        // actually produced, and time depends on what the model writes, not on the ceiling.
        private sealed class ModelCap
        {
            public int Cap;
            public DateTime ValidUntilUtc;
        }

        private const int AnthropicCapFallback = 64000;

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ModelCap> anthropicCaps =
            new System.Collections.Concurrent.ConcurrentDictionary<string, ModelCap>(StringComparer.OrdinalIgnoreCase);

        private static readonly System.Net.Http.HttpClient capHttp = BuildCapHttp();

        private static System.Net.Http.HttpClient BuildCapHttp()
        {
            System.Net.Http.HttpClient h = new System.Net.Http.HttpClient();
            h.Timeout = TimeSpan.FromSeconds(5);
            return h;
        }

        /// <summary>
        /// 1.0.196.0: max_tokens for an Anthropic call = the model's own output cap from Anthropic,
        /// cached per model+key for 24 h. Lookup failure -> 64000 for 10 minutes, then retried.
        /// Never throws. The key is never logged.
        /// </summary>
        public static int AnthropicOutputTokens(string model, string apiKey)
        {
            string m = (model ?? "").Trim();
            if (m.Length == 0)
            {
                return AnthropicCapFallback;
            }
            string cacheKey = m + "|" + KeyTag(apiKey);
            ModelCap known;
            if (anthropicCaps.TryGetValue(cacheKey, out known) && DateTime.UtcNow < known.ValidUntilUtc)
            {
                return known.Cap;
            }

            ModelCap fresh = new ModelCap();
            try
            {
                using (System.Net.Http.HttpRequestMessage req = new System.Net.Http.HttpRequestMessage(
                    System.Net.Http.HttpMethod.Get, "https://api.anthropic.com/v1/models/" + Uri.EscapeDataString(m)))
                {
                    req.Headers.Add("x-api-key", apiKey ?? "");
                    req.Headers.Add("anthropic-version", "2023-06-01");
                    System.Net.Http.HttpResponseMessage resp = capHttp.SendAsync(req).GetAwaiter().GetResult();
                    string body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    if (!resp.IsSuccessStatusCode)
                    {
                        throw new Exception("HTTP " + (int)resp.StatusCode);
                    }
                    JObject o = JObject.Parse(body);
                    int cap;
                    if (o["max_tokens"] == null || !int.TryParse(o["max_tokens"].ToString(), out cap) || cap <= 0)
                    {
                        throw new Exception("no max_tokens in model object");
                    }
                    fresh.Cap = cap;
                    fresh.ValidUntilUtc = DateTime.UtcNow.AddHours(24);
                    AiCoreLog.Write("INFO", "AIMODEL", "output cap for " + m + " = " + cap + " (from Anthropic)");
                }
            }
            catch (Exception ex)
            {
                fresh.Cap = (known != null && known.Cap > 0) ? known.Cap : AnthropicCapFallback;
                fresh.ValidUntilUtc = DateTime.UtcNow.AddMinutes(10);
                AiCoreLog.Write("WARN", "AIMODEL", "output cap lookup for " + m + " failed (" + ex.Message
                    + ") -- using " + fresh.Cap + " for 10 min");
            }
            anthropicCaps[cacheKey] = fresh;
            return fresh.Cap;
        }

        /// <summary>Short tag of a key for client-cache separation only. Never log it.</summary>
        public static string KeyTag(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return "";
            }
            if (key.Length <= 6)
            {
                return key;
            }
            return key.Substring(key.Length - 6);
        }

        // ---- default engine (callers without an engine choice: roster, fallback paths) ---------

        /// <summary>Default engine code (e.g. "quantum10"); fallback when the API has none.</summary>
        public static string DefaultEngine(string fallback)
        {
            AiEngineInfo row = DefaultRow();
            if (row == null || string.IsNullOrWhiteSpace(row.EngineCode))
            {
                return fallback;
            }
            return row.EngineCode;
        }

        /// <summary>Default provider (deepseek | openai | anthropic); fallback when the API has none.</summary>
        public static string DefaultProvider(string fallback)
        {
            AiEngineInfo row = DefaultRow();
            if (row == null)
            {
                return fallback;
            }
            return row.ProviderKind;
        }

        /// <summary>1.0.193.0: default engine's DisplayName; fallback when the API has none.</summary>
        public static string DefaultDisplayName(string fallback)
        {
            AiEngineInfo row = DefaultRow();
            if (row == null || string.IsNullOrWhiteSpace(row.DisplayName))
            {
                return fallback;
            }
            return row.DisplayName;
        }

        /// <summary>Default engine's ModelName when it is this provider, else fallback.</summary>
        public static string ModelFor(string providerKind, string fallback)
        {
            string live = LiveModelFor(providerKind);
            if (live == null)
            {
                return fallback;
            }
            return live;
        }

        /// <summary>Default engine's ModelName when it is this provider, else null.</summary>
        public static string LiveModelFor(string providerKind)
        {
            AiEngineInfo row = DefaultRow();
            if (row == null || string.IsNullOrWhiteSpace(providerKind))
            {
                return null;
            }
            if (!string.Equals(row.ProviderKind, providerKind.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            return row.ModelName;
        }

        /// <summary>1.0.193.0: default engine's key when it is this provider and has one, else fallback.</summary>
        public static string KeyFor(string providerKind, string fallback)
        {
            AiEngineInfo row = DefaultRow();
            if (row == null || string.IsNullOrWhiteSpace(row.Key) || string.IsNullOrWhiteSpace(providerKind))
            {
                return fallback;
            }
            if (!string.Equals(row.ProviderKind, providerKind.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return fallback;
            }
            return row.Key.Trim();
        }

        // ---- refresh protocol (called by the web app) -------------------------------------

        /// <summary>
        /// True when the cached answer is due for a refresh AND no other request is already
        /// fetching it. The caller that gets true must finish with Update() or ReportFailure().
        /// </summary>
        public static bool TryBeginRefresh()
        {
            if (!Enabled)
            {
                return false;
            }
            lock (refreshLock)
            {
                if (refreshInProgress || DateTime.UtcNow < nextFetchUtc)
                {
                    return false;
                }
                refreshInProgress = true;
                return true;
            }
        }

        /// <summary>
        /// Store the raw GetAllWithKeys JSON. A response with no usable row counts as a failure.
        /// 1.0.193.0: the body is NEVER logged -- it carries API keys.
        /// </summary>
        public static void Update(string rawJson, string source)
        {
            List<AiEngineInfo> list = null;
            try
            {
                list = Parse(rawJson);
            }
            catch (Exception ex)
            {
                ReportFailure("AiModelConfig: bad JSON (" + ex.Message + "), " + (rawJson ?? "").Length + " chars");
                return;
            }
            if (list == null)
            {
                ReportFailure("AiModelConfig: no usable engine (EngineCode + Provider + ModelName) in response, "
                    + (rawJson ?? "").Length + " chars");
                return;
            }

            string oldSummary = Summary(current);
            current = list;
            LastError = null;
            lock (refreshLock)
            {
                nextFetchUtc = DateTime.UtcNow.AddSeconds(CacheSec);
                refreshInProgress = false;
            }
            string newSummary = Summary(list);
            if (oldSummary != newSummary)
            {
                AiCoreLog.Write("INFO", "AIMODEL", newSummary + " (from " + source + ")");
            }
        }

        /// <summary>Record a failed fetch; the last good list (if any) stays in use.</summary>
        public static void ReportFailure(string error)
        {
            LastError = error;
            lock (refreshLock)
            {
                nextFetchUtc = DateTime.UtcNow.AddSeconds(Math.Min(RetrySec, CacheSec));
                refreshInProgress = false;
            }
            AiCoreLog.Write("WARN", "AIMODEL", error
                + (current == null ? " -- using Web.config models" : " -- keeping last API answer"));
        }

        // ---- internals -------------------------------------------------------------------
        private static List<AiEngineInfo> GetEngines()
        {
            if (!Enabled)
            {
                return null;
            }
            return current;
        }

        /// <summary>Log line for a list: no keys.</summary>
        private static string Summary(List<AiEngineInfo> list)
        {
            if (list == null || list.Count == 0)
            {
                return "";
            }
            AiEngineInfo def = list[0];
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].IsDefault)
                {
                    def = list[i];
                    break;
                }
            }
            return "engines=" + list.Count + " default engine=" + def.EngineCode
                + " provider=" + def.ProviderKind + " model=" + def.ModelName;
        }

        private static List<AiEngineInfo> Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            JToken root = JToken.Parse(raw);
            JObject obj = root as JObject;
            if (obj != null)
            {
                // tolerate a { "Data": ... } wrapper
                JToken inner = obj.GetValue("Data", StringComparison.OrdinalIgnoreCase);
                if (inner != null && (inner.Type == JTokenType.Array || inner.Type == JTokenType.Object))
                {
                    root = inner;
                }
            }

            List<JObject> items = new List<JObject>();
            bool singleObject = false;
            JArray arr = root as JArray;
            if (arr != null)
            {
                foreach (JToken t in arr)
                {
                    JObject o = t as JObject;
                    if (o != null)
                    {
                        items.Add(o);
                    }
                }
            }
            else if (root is JObject)
            {
                // old GetDefault shape: one object = the default engine
                items.Add((JObject)root);
                singleObject = true;
            }

            List<AiEngineInfo> list = new List<AiEngineInfo>();
            for (int i = 0; i < items.Count; i++)
            {
                AiEngineInfo row = new AiEngineInfo();
                row.EngineCode = Str(items[i], "EngineCode");
                row.DisplayName = Str(items[i], "DisplayName");
                row.Provider = Str(items[i], "Provider");
                row.ModelName = Str(items[i], "ModelName");
                row.Key = Str(items[i], "Key");
                row.IsDefault = singleObject || Bool(items[i], "IsDefault");
                if (string.IsNullOrEmpty(row.EngineCode) || row.ProviderKind == null || string.IsNullOrEmpty(row.ModelName))
                {
                    continue;
                }
                if (string.IsNullOrEmpty(row.DisplayName))
                {
                    row.DisplayName = row.EngineCode;
                }
                list.Add(row);
            }
            if (list.Count == 0)
            {
                return null;
            }
            return list;
        }

        private static string Str(JObject obj, string name)
        {
            JToken value = obj.GetValue(name, StringComparison.OrdinalIgnoreCase);
            if (value == null || value.Type == JTokenType.Null)
            {
                return null;
            }
            string text = value.ToString().Trim();
            if (text.Length == 0)
            {
                return null;
            }
            return text;
        }

        private static bool Bool(JObject obj, string name)
        {
            JToken value = obj.GetValue(name, StringComparison.OrdinalIgnoreCase);
            if (value == null || value.Type == JTokenType.Null)
            {
                return false;
            }
            if (value.Type == JTokenType.Boolean)
            {
                return (bool)value;
            }
            return AiSettings.ParseBool(value.ToString(), false);
        }
    }
}
