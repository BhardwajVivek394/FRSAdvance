using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace E7.AiCore
{
    // =============================================================================================
    // 1.0.193.0 -- dbo.AiModelConfig IS THE SINGLE SOURCE OF PROVIDER, MODEL AND KEY.
    //
    // WHY THIS EXISTS. Until 1.0.192.3 the answer to "which vendor and which model" was computed in
    // FOUR separate places, each with its own copy of the brand->vendor map and its own default:
    //
    //     EngineIdentity.ResolveProviderKind   Contains-match, default quantum10   (correct)
    //     AnalysisEngine.ProviderKind          Contains-match, default quantum10   (correct)
    //     ChatEngine.ProviderKind              Contains-match, default quantum10   (correct)
    //     AssetHealthEngine L913               == equality,    default anthropic   (WRONG)
    //     MaintenceRosterController L1014      == equality,    default anthropic   (WRONG)
    //
    // The two == copies could not recognise a brand label at all. With Web.config carrying
    // AiProvider = "quantum10" on 619, "quantum10" equals neither "openai" nor "deepseek", so both
    // fell through to the Anthropic branch -- Roster Diagnose and the Asset Health card were quietly
    // billing the Anthropic account while the Judge, AiChat and ChatBot ran DeepSeek. Nothing logged
    // it. That is the 10.10.2026 fault (AiProvider=anthropic sending the Judge to a vendor with no
    // credit) happening a third time, which is why the map now lives in ONE place that reads ONE
    // table instead of five copies reading appSettings.
    //
    // PRECEDENCE, highest first (the 16.10.2026 decision):
    //   1. judge-settings.json Ai.Provider   -- Judge only; the operator's no-recycle pin
    //   2. a privileged human's engine pick  -- the AiChat / ChatBot picker, siteKeep users only
    //   3. the IsDefault = 1 AND IsEnabled = 1 row
    //
    // Note what is NOT on that list: the provider string a SERVICE sends. AlertV3 puts a baked-in
    // "QUANTUM" on every judge request; that is a hardcoded literal in another program, not a person
    // choosing a model, so the catalogue's default outranks it. A human at the picker still wins.
    //
    // THE KEY IS NEVER LOGGED. Describe() exists so that a log line can name the row without the
    // secret; nothing else in this file writes Row.Key anywhere.
    // =============================================================================================
    public sealed class AiModelRow
    {
        public int Id;
        public string EngineCode = "";     // "nexus20"            -- the lookup key
        public string DisplayName = "";    // "ENERGY7 NEXUS 2.0"   -- the only name a browser may see
        public string Provider = "";       // "anthropic" | "openai" | "deepseek"
        public string ModelName = "";      // "claude-haiku-5-5"    -- the wire value
        public string Key = "";            // the vendor API key    -- NEVER logged, NEVER returned to a browser
        public bool IsDefault;
        public bool IsEnabled;
        public int SortOrder;

        // 1.0.193.0 -- OPTIONAL COLUMNS. The table does not carry these yet, so null means "keep
        // doing exactly what the appSetting already does". That is what makes this release safe to
        // deploy before the ALTER TABLE in AiModelConfig_1.0.193.0.sql is run.
        public string BaseUrl;             // null -> OpenAiBaseUrl / DeepSeekBaseUrl appSetting
        public int? MaxTokens;             // null -> the per-vendor appSetting
        public bool? Thinking;             // null -> DeepSeekThinking appSetting
        public string ApiShape;            // null -> inferred from Provider

        /// <summary>True when this row speaks Anthropic's /v1/messages shape rather than OpenAI's.</summary>
        public bool AnthropicShape
        {
            get
            {
                if (!string.IsNullOrEmpty(ApiShape))
                {
                    return string.Equals(ApiShape.Trim(), "anthropic", StringComparison.OrdinalIgnoreCase);
                }
                return string.Equals(Provider, "anthropic", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// The chat-completions endpoint for this row. BaseUrl from the table wins; otherwise the
        /// existing appSetting, so no new configuration key is introduced by this release.
        /// </summary>
        public string Endpoint()
        {
            if (string.Equals(Provider, "anthropic", StringComparison.OrdinalIgnoreCase))
            {
                string ab = (BaseUrl ?? "").Trim();
                if (ab.Length == 0) { return "https://api.anthropic.com/v1/messages"; }
                return ab.TrimEnd('/') + "/messages";
            }

            string b = (BaseUrl ?? "").Trim();
            if (b.Length == 0)
            {
                b = string.Equals(Provider, "openai", StringComparison.OrdinalIgnoreCase)
                    ? (ConfigurationManager.AppSettings["OpenAiBaseUrl"] ?? "https://api.openai.com/v1")
                    : (ConfigurationManager.AppSettings["DeepSeekBaseUrl"] ?? "https://api.deepseek.com/v1");
            }
            return b.Trim().TrimEnd('/') + "/chat/completions";
        }

        /// <summary>
        /// 1.0.194.0 -- the BASE url, for the client classes that append their own path.
        /// Endpoint() above returns the full chat-completions URL for the raw-HTTP callers
        /// (Judge, Asset Health, Roster Diagnose); this returns just the base, for DeepSeekClient
        /// and OpenAiClient which build the path themselves. Two callers, two needs, one row --
        /// returning the wrong one of these produces a 404 against a real host, which is a
        /// confusing failure, so they are named apart rather than overloaded.
        /// Anthropic needs neither: AnthropicClient has no base-url parameter.
        /// </summary>
        public string BaseUrlOrConfig()
        {
            string b = (BaseUrl ?? "").Trim();
            if (b.Length > 0) { return b.TrimEnd('/'); }
            if (string.Equals(Provider, "openai", StringComparison.OrdinalIgnoreCase))
            {
                return (ConfigurationManager.AppSettings["OpenAiBaseUrl"] ?? "https://api.openai.com/v1").Trim().TrimEnd('/');
            }
            if (string.Equals(Provider, "deepseek", StringComparison.OrdinalIgnoreCase))
            {
                return (ConfigurationManager.AppSettings["DeepSeekBaseUrl"] ?? "https://api.deepseek.com/v1").Trim().TrimEnd('/');
            }
            return null;   // anthropic -- AnthropicClient takes no base url
        }

        /// <summary>max_tokens for this row -- the table's value if present, else the vendor appSetting.</summary>
        public int TokenCeiling(int fallback)
        {
            if (MaxTokens.HasValue && MaxTokens.Value > 0) { return MaxTokens.Value; }
            string key = string.Equals(Provider, "deepseek", StringComparison.OrdinalIgnoreCase)
                ? "DeepSeekMaxTokens"
                : "AnthropicMaxTokens";
            return AiSettings.ParseInt(ConfigurationManager.AppSettings[key], fallback);
        }

        /// <summary>Thinking state -- the table's value if present, else the DeepSeekThinking appSetting.</summary>
        public bool ThinkingOn()
        {
            if (Thinking.HasValue) { return Thinking.Value; }
            return AiSettings.ParseBool(ConfigurationManager.AppSettings["DeepSeekThinking"], false);
        }
    }


    /// <summary>
    /// Where the catalogue rows come from. The built-in implementation GETs the AiModelConfig API;
    /// a test substitutes its own so no HTTP and no live keys are needed to exercise the resolver.
    /// </summary>
    public interface IAiModelCatalogSource
    {
        string FetchJson();
    }


    /// <summary>
    /// Thrown only when the catalogue cannot be established AT ALL -- no live answer and nothing
    /// cached. The message names the appSetting to check, because "ai disabled or not configured"
    /// alone sent us looking in the wrong place once already (1.0.187.0).
    /// </summary>
    public sealed class AiModelCatalogException : Exception
    {
        public AiModelCatalogException(string message) : base(message) { }
        public AiModelCatalogException(string message, Exception inner) : base(message, inner) { }
    }


    public static class AiModelCatalog
    {
        // ---- configuration knobs ------------------------------------------------------------------
        // 1.0.200.0 -- THESE WERE CODE CONSTANTS, AND THAT WAS THE BUG. Web.config already carried
        // AiModelConfigEnabled / Url / CacheSec / TimeoutMs, but only Settings/AiModelConfig read
        // them. This class -- the one the Judge, ChatBot, Roster and AiChat all resolve through --
        // ignored every one of them and ran on hardcoded values no operator could change. The keys
        // are now read here too, with the old constants as the defaults, so one set of settings
        // governs every component instead of two halves disagreeing.
        //
        //   AiModelConfigEnabled         (default true)  false turns the catalogue off
        //   AiModelConfigUrl             (default "AiModelConfig/GetAllWithKeys") a path under the
        //                                base URL, OR a full http(s):// URL, which wins outright
        //   AiModelConfigCacheSec        (default 60)    seconds a good read is served from memory
        //   AiModelConfigTimeoutMs       (default 8000)
        //   AiModelConfigCertThumbprint  (optional)      see HttpFetchJson -- scoped TLS pinning
        //   AiModelConfigHttpFallback    (default false) see HttpFetchJson -- opt-in plaintext retry
        private const string DefaultPathSuffix = "AiModelConfig/GetAllWithKeys";
        private const int DefaultTimeoutMs = 8000;
        private const int DefaultCacheSeconds = 60;

        private static string CfgStr(string key, string fallback)
        {
            string v = (ConfigurationManager.AppSettings[key] ?? "").Trim();
            return v.Length == 0 ? fallback : v;
        }

        private static int CfgInt(string key, int fallback)
        {
            int v = AiSettings.ParseInt(ConfigurationManager.AppSettings[key], fallback);
            return v > 0 ? v : fallback;
        }

        private static bool CfgBool(string key, bool fallback)
        {
            return AiSettings.ParseBool(ConfigurationManager.AppSettings[key], fallback);
        }

        /// <summary>AiModelConfigUrl: a path under the base URL, or a full http(s) URL.</summary>
        private static string PathSuffix { get { return CfgStr("AiModelConfigUrl", DefaultPathSuffix); } }

        private static int TimeoutMs { get { return CfgInt("AiModelConfigTimeoutMs", DefaultTimeoutMs); } }

        /// <summary>AiModelConfigEnabled = false turns the whole catalogue off.</summary>
        private static bool CatalogEnabled { get { return CfgBool("AiModelConfigEnabled", true); } }

        private static readonly string[] AuthKeyCandidates =
        {
            // the house convention is one appSetting holding "user:pass", base64'd into the header
            // (RangeApiAuth, HistorianApiAuth). Tried in order; the one that answers is logged.
            "AiModelConfigApiAuth", "APIBaseUrlSecureAuth", "ApiAuth"
        };

        /// <summary>
        /// Test / host override for the cache window. 0 -- the default -- means "read
        /// AiModelConfigCacheSec, itself defaulting to 60". Set a positive value to pin it in code.
        /// </summary>
        public static int CacheSeconds = 0;

        private static int CacheSecondsEffective
        {
            get
            {
                return CacheSeconds > 0 ? CacheSeconds : CfgInt("AiModelConfigCacheSec", DefaultCacheSeconds);
            }
        }

        /// <summary>Seconds to wait before retrying after a failed fetch, while serving the stale copy.</summary>
        public static int RetrySeconds = 15;

        /// <summary>The URL the last fetch attempt actually used. Named in the cold-start error.</summary>
        private static volatile string lastUrlTried;

        /// <summary>Overridable for tests. Null means "use the built-in API source".</summary>
        public static IAiModelCatalogSource Source;

        /// <summary>level, tag, message. Wired by the host; null is allowed and means "do not log".</summary>
        public static Action<string, string, string> Log;

        private static readonly object gate = new object();
        private static List<AiModelRow> cache;
        private static DateTime cacheUtc = DateTime.MinValue;
        private static DateTime nextTryUtc = DateTime.MinValue;
        private static DateTime lastWarnUtc = DateTime.MinValue;
        private static string authKeyUsed;

        // 1.0.193.0 -- NO WIRING STEP. Log defaults to the ai-log sink every other engine already
        // writes to, so a host that forgets to set it still gets the lines. JudgeHost had to wire
        // JudgeLog.Sink by hand and a missed wiring there is invisible until something goes wrong.
        private static void W(string level, string msg)
        {
            Action<string, string, string> f = Log;
            try
            {
                if (f != null) { f(level, "AIMODEL", msg); }
                else { AnalysisEngine.AiLogStatic(level, "AIMODEL", msg); }
            }
            catch { }
        }

        /// <summary>A row named for a log line. Contains no key material, by construction.</summary>
        public static string Describe(AiModelRow r)
        {
            if (r == null) { return "(no row)"; }
            return "engine=" + r.EngineCode
                 + " display='" + r.DisplayName + "'"
                 + " provider=" + r.Provider
                 + " model=" + r.ModelName
                 + " key=" + (string.IsNullOrEmpty(r.Key) ? "MISSING" : "present")
                 + (r.IsDefault ? " [IsDefault]" : "");
        }

        /// <summary>How many of the catalogue's rows are actually selectable (IsEnabled = 1).</summary>
        public static int EnabledCount(List<AiModelRow> rows)
        {
            int n = 0;
            if (rows == null) { return 0; }
            for (int i = 0; i < rows.Count; i++) { if (rows[i].IsEnabled) { n++; } }
            return n;
        }

        /// <summary>Drops the cache so the next call re-reads the API. Used by the admin screen.</summary>
        public static void Invalidate()
        {
            lock (gate) { cache = null; cacheUtc = DateTime.MinValue; nextTryUtc = DateTime.MinValue; }
            W("INFO", "catalogue cache dropped; next call re-reads the API");
        }


        // =========================================================================================
        // Rows -- the cache, the retry policy and the last-known-good rule in one place.
        //
        // The 16.10.2026 decision: serve the last known good copy for as long as the API is down,
        // and never let a catalogue refresh failure be what takes the AI offline. A cold start with
        // the API already down is the one case with nothing to serve, and that throws by design --
        // silently inventing a vendor is how the Anthropic-vs-DeepSeek split happened in the first
        // place, so this fails loudly and names the appSetting instead.
        // =========================================================================================
        public static List<AiModelRow> Rows()
        {
            // 1.0.200.0 (Fix 10) -- the first of three distinct failures. Before this, a disabled
            // catalogue, an unreachable API and a table with no default row all produced the same
            // "no provider can be chosen" line, so a log could not tell config error from network
            // error from data error.
            if (!CatalogEnabled)
            {
                throw new AiModelCatalogException(
                    "the AiModelConfigEnabled appSetting is false, so the AI model catalogue is "
                    + "switched off and no provider can be chosen. Set AiModelConfigEnabled to true "
                    + "to read dbo.AiModelConfig.");
            }

            List<AiModelRow> warm;
            bool due;
            lock (gate)
            {
                warm = cache;
                due = warm == null
                    || (DateTime.UtcNow - cacheUtc).TotalSeconds >= CacheSecondsEffective;
                if (due && warm != null && DateTime.UtcNow < nextTryUtc) { due = false; }
            }

            if (!due) { return warm; }

            string json = null;
            Exception err = null;
            try
            {
                IAiModelCatalogSource src = Source;
                json = src != null ? src.FetchJson() : HttpFetchJson();
            }
            catch (Exception ex) { err = ex; }

            if (err == null && !string.IsNullOrWhiteSpace(json))
            {
                List<AiModelRow> parsed;
                try { parsed = Parse(json); }
                catch (Exception pex) { parsed = null; err = pex; }

                if (parsed != null && parsed.Count > 0)
                {
                    lock (gate)
                    {
                        cache = parsed;
                        cacheUtc = DateTime.UtcNow;
                        nextTryUtc = DateTime.MinValue;
                    }
                    if (warm == null)
                    {
                        W("INFO", "catalogue loaded: " + EnabledCount(parsed) + " of "
                            + parsed.Count + " row(s) enabled"
                            + (authKeyUsed != null ? ", auth from appSetting '" + authKeyUsed + "'" : ", no auth header")
                            + ". Default -> " + Describe(FirstDefault(parsed)));
                    }
                    return parsed;
                }
                if (err == null) { err = new AiModelCatalogException("the API answered with no usable rows"); }
            }

            // ---- the fetch failed -----------------------------------------------------------
            lock (gate) { nextTryUtc = DateTime.UtcNow.AddSeconds(RetrySeconds); }

            if (warm != null)
            {
                if ((DateTime.UtcNow - lastWarnUtc).TotalSeconds >= 60)
                {
                    lastWarnUtc = DateTime.UtcNow;
                    W("WARN", "AiModelConfig API unreachable -- serving the last known good catalogue ("
                        + warm.Count + " row(s), read "
                        + ((int)(DateTime.UtcNow - cacheUtc).TotalSeconds) + "s ago). Reason: "
                        + Short(err));
                }
                return warm;
            }

            // 1.0.200.0 (Fix 10) -- the second distinct failure: the API could not be read and there
            // is no warm copy. Name the URL that was actually tried, not a guess at an appSetting:
            // the address can now come from AiModelConfigUrl (full URL), APIBaseUrlSecure, or the
            // APIBaseUrl fallback, and pointing at the wrong one cost a diagnosis session.
            throw new AiModelCatalogException(
                "AiModelConfig is unavailable and nothing is cached, so no provider can be chosen. "
                + "Tried " + (lastUrlTried ?? "(no address could be resolved)")
                + " -- check that it answers from this server, and the AiModelConfigUrl / "
                + "APIBaseUrlSecure / APIBaseUrl appSettings. Reason: " + Short(err), err);
        }


        // =========================================================================================
        // Default -- the IsDefault = 1 AND IsEnabled = 1 row.
        //
        // The table has no constraint enforcing exactly one, so this is deterministic rather than
        // arbitrary: lowest SortOrder, then lowest Id. Two servers reading the same table therefore
        // cannot disagree. Zero defaults is an error, not a silent fall to Anthropic.
        // AiModelConfig_1.0.193.0.sql adds the unique filtered index that makes this moot.
        // =========================================================================================
        public static AiModelRow Default()
        {
            List<AiModelRow> rows = Rows();
            AiModelRow d = FirstDefault(rows);
            if (d != null) { return d; }
            throw new AiModelCatalogException(
                "no row in dbo.AiModelConfig has IsDefault = 1 AND IsEnabled = 1, so there is no "
                + "engine to fall back on (" + rows.Count + " enabled row(s) returned).");
        }

        private static AiModelRow FirstDefault(List<AiModelRow> rows)
        {
            AiModelRow best = null;
            if (rows == null) { return null; }
            for (int i = 0; i < rows.Count; i++)
            {
                AiModelRow r = rows[i];
                if (!r.IsDefault || !r.IsEnabled) { continue; }
                if (best == null
                    || r.SortOrder < best.SortOrder
                    || (r.SortOrder == best.SortOrder && r.Id < best.Id)) { best = r; }
            }
            return best;
        }


        // =========================================================================================
        // Resolve -- the ONE answer to "which vendor and which model".
        //
        // humanChoice is the whole decision. true means a person picked this engine in the UI (the
        // caller has already established that they are a site-keeping / ChatBotAllowed user, or that
        // an operator typed it into judge-settings.json). false means the string arrived from
        // another program, and the catalogue default outranks it.
        //
        // Every path that does not return the requested row logs WHY, because a silent substitution
        // is exactly what made "are both endpoints on the same vendor?" unanswerable from a log.
        // =========================================================================================
        public static AiModelRow Resolve(string requested, bool humanChoice)
        {
            List<AiModelRow> rows = Rows();
            string want = (requested ?? "").Trim();

            if (!humanChoice || want.Length == 0) { return Default(); }

            AiModelRow hit = Find(rows, want);
            if (hit != null && hit.IsEnabled) { return hit; }

            AiModelRow def = Default();
            if (hit != null)
            {
                W("WARN", "engine '" + want + "' is present in AiModelConfig but IsEnabled = 0; using the default instead -> " + Describe(def));
            }
            else
            {
                W("WARN", "engine '" + want + "' matches no row in AiModelConfig; using the default instead -> " + Describe(def));
            }
            return def;
        }

        /// <summary>Convenience for a caller that has no user context at all (services, background work).</summary>
        public static AiModelRow ResolveForService(string requestedButIgnored)
        {
            return Default();
        }


        // ---- lookup -----------------------------------------------------------------------------
        // EngineCode, then DisplayName, then the old opaque alias, then a brand label. Each step is
        // narrower than the next is broad, and the brand step returns null rather than guessing --
        // EngineIdentity.ResolveProviderKind ends with "return anthropic", which is right for ITS
        // job and would be wrong here: an unrecognised string must reach the default, not Anthropic.
        private static AiModelRow Find(List<AiModelRow> rows, string want)
        {
            if (rows == null) { return null; }
            int i;

            for (i = 0; i < rows.Count; i++)
            {
                if (string.Equals(rows[i].EngineCode, want, StringComparison.OrdinalIgnoreCase)) { return rows[i]; }
            }
            for (i = 0; i < rows.Count; i++)
            {
                if (string.Equals(rows[i].DisplayName, want, StringComparison.OrdinalIgnoreCase)) { return rows[i]; }
            }

            // nexus10 / orion10 / quantum10 used to arrive as the raw vendor name
            string alias = EngineIdentity.OpaqueProviderId(want);
            if (!string.Equals(alias, "unknown", StringComparison.Ordinal))
            {
                for (i = 0; i < rows.Count; i++)
                {
                    if (string.Equals(rows[i].EngineCode, alias, StringComparison.OrdinalIgnoreCase)) { return rows[i]; }
                }
            }

            string vendor = BrandVendor(want);
            if (vendor == null) { return null; }

            bool flash = EngineIdentity.WantsFlashVariant(want);
            AiModelRow best = null;
            for (i = 0; i < rows.Count; i++)
            {
                AiModelRow r = rows[i];
                if (!r.IsEnabled) { continue; }
                if (!string.Equals(r.Provider, vendor, StringComparison.OrdinalIgnoreCase)) { continue; }
                bool isFlash = (r.ModelName ?? "").IndexOf("flash", StringComparison.OrdinalIgnoreCase) >= 0;
                if (flash != isFlash) { continue; }
                if (best == null || r.SortOrder < best.SortOrder
                    || (r.SortOrder == best.SortOrder && r.Id < best.Id)) { best = r; }
            }
            if (best != null) { return best; }

            // asked for flash and this vendor has none (or the reverse) -- take the vendor's first row
            for (i = 0; i < rows.Count; i++)
            {
                AiModelRow r = rows[i];
                if (!r.IsEnabled) { continue; }
                if (!string.Equals(r.Provider, vendor, StringComparison.OrdinalIgnoreCase)) { continue; }
                if (best == null || r.SortOrder < best.SortOrder
                    || (r.SortOrder == best.SortOrder && r.Id < best.Id)) { best = r; }
            }
            return best;
        }

        /// <summary>Brand label -> vendor, or null when the string names no brand we know.</summary>
        private static string BrandVendor(string requested)
        {
            string p = (requested ?? "").Trim().ToLowerInvariant();
            if (p.Length == 0) { return null; }
            if (p == "deepseek" || p == "openai" || p == "anthropic") { return p; }
            if (p.Contains("quantum") || p.Contains("deep") || p.Contains("tesla 2") || p.Contains("tesla2")) { return "deepseek"; }
            if (p.Contains("orion") || p.Contains("tesla 1") || p.Contains("tesla1")) { return "openai"; }
            if (p.Contains("nexus") || p.Contains("ultra")) { return "anthropic"; }
            if (p.Contains("gpt")) { return "openai"; }
            if (p.Contains("claude")) { return "anthropic"; }
            // LAST, so "quantum flash" is already settled by the quantum rule above. Bare "flash"
            // reaches here, and EngineIdentity.WantsFlashVariant treats it as meaningful, so this
            // must too -- otherwise a caller sending "flash" silently gets the default instead of
            // the flash model. Only DeepSeek has a distinct flash model today.
            if (p.Contains("flash")) { return "deepseek"; }
            return null;
        }


        // ---- parsing ----------------------------------------------------------------------------
        // Tolerant on shape, strict on content. IsDefault/IsEnabled arrive as true/false from the
        // API and as 1/0 straight from the bit column, so both are accepted. A row missing anything
        // the transport needs is dropped with a named WARN rather than half-used.
        internal static List<AiModelRow> Parse(string json)
        {
            JToken root = JToken.Parse(json);
            JArray arr = root as JArray;
            if (arr == null)
            {
                JObject o = root as JObject;
                if (o != null)
                {
                    JToken inner = o["Data"] ?? o["data"] ?? o["Result"] ?? o["result"] ?? o["Items"] ?? o["items"];
                    arr = inner as JArray;
                }
            }
            if (arr == null) { throw new AiModelCatalogException("the AiModelConfig response is not a JSON array"); }

            List<AiModelRow> list = new List<AiModelRow>();
            int dropped = 0;
            foreach (JToken t in arr)
            {
                JObject o = t as JObject;
                if (o == null) { dropped++; continue; }

                AiModelRow r = new AiModelRow();
                r.Id = (int)Num(o, "Id", 0);
                r.EngineCode = Str(o, "EngineCode");
                r.DisplayName = Str(o, "DisplayName");
                r.Provider = Str(o, "Provider").ToLowerInvariant();
                r.ModelName = Str(o, "ModelName");
                r.Key = Str(o, "Key");
                r.IsDefault = Flag(o, "IsDefault");
                r.IsEnabled = Flag(o, "IsEnabled");
                r.SortOrder = (int)Num(o, "SortOrder", 0);

                string bu = Str(o, "BaseUrl");
                r.BaseUrl = bu.Length > 0 ? bu : null;
                JToken mt = o["MaxTokens"];
                if (mt != null && mt.Type != JTokenType.Null)
                {
                    int mv = (int)Num(o, "MaxTokens", 0);
                    if (mv > 0) { r.MaxTokens = mv; }
                }
                JToken th = o["Thinking"];
                if (th != null && th.Type != JTokenType.Null) { r.Thinking = Flag(o, "Thinking"); }
                string sh = Str(o, "ApiShape");
                r.ApiShape = sh.Length > 0 ? sh : null;

                if (r.EngineCode.Length == 0 || r.ModelName.Length == 0)
                {
                    dropped++;
                    W("WARN", "AiModelConfig row Id=" + r.Id + " dropped: EngineCode or ModelName is blank");
                    continue;
                }
                if (r.Provider != "anthropic" && r.Provider != "openai" && r.Provider != "deepseek")
                {
                    dropped++;
                    W("WARN", "AiModelConfig row " + r.EngineCode + " dropped: Provider '" + r.Provider
                        + "' is not one of anthropic | openai | deepseek, so there is no transport for it");
                    continue;
                }
                // 1.0.193.0 -- DISABLED ROWS ARE KEPT, not discarded. Dropping them here made
                // Resolve's "you asked for a disabled engine" branch unreachable: an explicit pick
                // of a disabled row fell through to the brand lookup and came back with a DIFFERENT
                // model from the same vendor. A user who picks QUANTUM (Pro) and silently receives
                // Flash is the 1.0.160.42 fault wearing a new hat. Keeping the row means Find can
                // see it, say so, and fall to the default instead. Every SELECTION path --
                // FirstDefault and both brand loops in Find -- filters on IsEnabled itself.
                list.Add(r);
            }

            if (dropped > 0)
            {
                W("WARN", "AiModelConfig: " + dropped + " row(s) dropped as unusable; "
                    + EnabledCount(list) + " of " + list.Count + " remaining row(s) enabled");
            }
            return list;
        }

        private static string Str(JObject o, string name)
        {
            JToken t = o[name];
            if (t == null || t.Type == JTokenType.Null) { return ""; }
            return (t.ToString() ?? "").Trim();
        }

        private static long Num(JObject o, string name, long def)
        {
            JToken t = o[name];
            if (t == null || t.Type == JTokenType.Null) { return def; }
            long v;
            return long.TryParse(t.ToString().Trim(), out v) ? v : def;
        }

        private static bool Flag(JObject o, string name)
        {
            JToken t = o[name];
            if (t == null || t.Type == JTokenType.Null) { return false; }
            string s = (t.ToString() ?? "").Trim();
            return s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1";
        }


        // ---- the built-in API source ------------------------------------------------------------
        // Same transport shape as RosterEngine.RosterModelCall and the same auth convention as
        // RangeApiAuth / HistorianApiAuth: one appSetting holding "user:pass", base64'd into the
        // header.
        //
        // 1.0.200.0 -- ADDRESS RESOLUTION (Fix 5). This used to read APIBaseUrlSecure and throw if
        // it was blank, with no fallback. Two things went wrong with that. First, APIBaseUrlSecure
        // is a UI-shaped setting -- HttpClientFactory picks it only when the inbound BROWSER request
        // is HTTPS -- and the Judge has no inbound request at all, so a background service inherited
        // an HTTPS endpoint by accident. Second, APIBaseUrl was sitting right there, configured and
        // working, and a blank APIBaseUrlSecure took the AI offline rather than using it.
        // Order is now: a full URL in AiModelConfigUrl wins; else APIBaseUrlSecure; else APIBaseUrl.
        private static string ResolveRequestUrl()
        {
            string path = PathSuffix;
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            string b = (ConfigurationManager.AppSettings["APIBaseUrlSecure"] ?? "").Trim();
            bool fellBack = false;
            if (b.Length == 0)
            {
                b = (ConfigurationManager.AppSettings["APIBaseUrl"] ?? "").Trim();
                fellBack = b.Length > 0;
            }

            if (b.Length == 0)
            {
                throw new AiModelCatalogException(
                    "no address is configured for the AiModelConfig API: AiModelConfigUrl is not a "
                    + "full URL, and both APIBaseUrlSecure and APIBaseUrl are missing or blank.");
            }

            if (fellBack)
            {
                W("WARN", "APIBaseUrlSecure is blank; falling back to APIBaseUrl for the "
                    + "AiModelConfig API. Set AiModelConfigUrl to a full URL to make this explicit.");
            }

            if (!b.EndsWith("/")) { b += "/"; }
            return b + path.TrimStart('/');
        }

        /// <summary>Hex only, upper case -- so a thumbprint pasted with spaces or colons matches.</summary>
        private static string NormaliseThumbprint(string s)
        {
            if (string.IsNullOrEmpty(s)) { return ""; }
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))
                {
                    sb.Append(char.ToUpperInvariant(c));
                }
            }
            return sb.ToString();
        }

        // 1.0.200.0 -- TLS (Fix 3). The catalogue used to inherit whatever certificate policy the
        // process happened to have. HttpClientFactory sets
        // ServicePointManager.ServerCertificateValidationCallback = (...) => true, but ONLY when an
        // inbound request arrives over HTTPS -- and that property is process-wide and static. The
        // Judge runs with no HttpContext, so whether its own HTTPS call validated depended on
        // whether some unrelated page had been browsed over HTTPS first in that worker process.
        // That is what produced 93 TrustFailures overnight and success after 10:00 the next morning,
        // and an app-pool recycle put it straight back.
        //
        // Set AiModelConfigCertThumbprint and this call validates for ITSELF, against that one
        // certificate, on its own request -- no reliance on ambient state and no blanket accept.
        // Left blank, behaviour is exactly as before.
        private static void ApplyCertificatePolicy(HttpWebRequest req, string url)
        {
            string want = NormaliseThumbprint(CfgStr("AiModelConfigCertThumbprint", ""));
            if (want.Length == 0) { return; }
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) { return; }

            req.ServerCertificateValidationCallback = (sender, cert, chain, errors) =>
            {
                if (cert == null) { return false; }
                bool ok = NormaliseThumbprint(cert.GetCertHashString()) == want;
                if (!ok)
                {
                    W("WARN", "AiModelConfig certificate thumbprint does not match "
                        + "AiModelConfigCertThumbprint, so the call was refused. Expected the "
                        + "configured value; the endpoint presented a different certificate.");
                }
                return ok;
            };
        }

        private static string HttpFetchJson()
        {
            string url = ResolveRequestUrl();
            try
            {
                return FetchOnce(url);
            }
            catch (WebException wex)
            {
                // 1.0.200.0 -- opt-in only, and OFF by default. A trust failure against the
                // configured HTTPS address is normally a certificate problem to fix, not something
                // to work around: retrying over plain HTTP sends this response -- which contains
                // every vendor KEY VALUE -- unencrypted. Enable it only on a trusted internal path,
                // and prefer AiModelConfigCertThumbprint or a correct certificate instead.
                if (wex.Status == WebExceptionStatus.TrustFailure
                    && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    && CfgBool("AiModelConfigHttpFallback", false))
                {
                    string alt = (ConfigurationManager.AppSettings["APIBaseUrl"] ?? "").Trim();
                    if (alt.Length > 0 && alt.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!alt.EndsWith("/")) { alt += "/"; }
                        string p = PathSuffix;
                        if (p.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                            || p.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        {
                            p = DefaultPathSuffix;
                        }
                        alt = alt + p.TrimStart('/');
                        W("WARN", "AiModelConfig TLS trust failed for " + url
                            + " and AiModelConfigHttpFallback is on, so retrying over " + alt
                            + ". This sends the catalogue, INCLUDING KEY VALUES, unencrypted -- fix "
                            + "the certificate or set AiModelConfigCertThumbprint instead.");
                        return FetchOnce(alt);
                    }
                }
                throw;
            }
        }

        private static string FetchOnce(string url)
        {
            lastUrlTried = url;
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            ApplyCertificatePolicy(req, url);
            req.Method = "GET";
            req.Accept = "application/json";
            req.Timeout = TimeoutMs;
            req.ReadWriteTimeout = TimeoutMs;

            for (int i = 0; i < AuthKeyCandidates.Length; i++)
            {
                string auth = (ConfigurationManager.AppSettings[AuthKeyCandidates[i]] ?? "").Trim();
                if (auth.Length == 0) { continue; }
                req.Headers["Authorization"] = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(auth));
                authKeyUsed = AuthKeyCandidates[i];
                break;
            }

            // No Basic key set: the API expects a Bearer token, and this runs with no LoginUser
            // (services, Judge), so use the service login. Its awaits are all ConfigureAwait(false).
            bool bearer = false;
            if (string.IsNullOrEmpty(req.Headers["Authorization"]))
            {
                using (CancellationTokenSource cts = new CancellationTokenSource(TimeoutMs))
                {
                    string tok = E7ApiToken.GetTokenAsync(cts.Token).GetAwaiter().GetResult();
                    if (tok == null)
                    {
                        throw new AiModelCatalogException("no E7 service token: " + E7ApiToken.LastError);
                    }
                    req.Headers["Authorization"] = "Bearer " + tok;
                    authKeyUsed = "E7ApiToken";
                    bearer = true;
                }
            }

            try
            {
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream()))
                {
                    return sr.ReadToEnd();
                }
            }
            catch (WebException wex)
            {
                HttpWebResponse r = wex.Response as HttpWebResponse;
                if (bearer && r != null && (r.StatusCode == HttpStatusCode.Unauthorized || r.StatusCode == HttpStatusCode.Forbidden))
                {
                    E7ApiToken.Invalidate();
                }
                throw;
            }
        }

        private static string Short(Exception ex)
        {
            if (ex == null) { return "(none)"; }
            WebException wex = ex as WebException;
            if (wex != null)
            {
                HttpWebResponse r = wex.Response as HttpWebResponse;
                if (r != null) { return "HTTP " + (int)r.StatusCode + " " + r.StatusCode; }
                return wex.Status + ": " + wex.Message;
            }
            return ex.GetType().Name + ": " + ex.Message;
        }
    }
}
