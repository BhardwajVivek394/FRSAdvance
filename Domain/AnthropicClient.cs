using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class AnthropicClient
    {
        // ------------------------------------------------------------------
        // Component version (Major.Minor.Build.Revision)
        // 1.0.2.0 - Optional 1-HOUR cache TTL (extendedCacheTtl). The default cache is
        //           5 minutes; for callers whose requests are spread out (gaps > 5 min)
        //           the 5-min entry expires between calls, so an opt-in 1-hour TTL is
        //           added. It requires BOTH ttl:"1h" on the cache_control block AND the
        //           beta header "anthropic-beta: extended-cache-ttl-2025-04-11" on the
        //           request. Defaults OFF, so existing callers are byte-identical.
        // 1.0.1.0 - Optional prompt caching on the tools+system prefix
        // 1.0.0.0 - Initial client
        //
        // PROMPT CACHING, and why the marker goes where it does.
        // Anthropic builds its cache prefix in the order tools -> system ->
        // messages, and a cache_control marker caches everything from the start
        // of the prompt up to and INCLUDING the marked block. So ONE marker on
        // the system block covers tools + system together -- no second marker on
        // the tools array is needed, and one breakpoint means one cache entry
        // rather than two.
        //
        // The API only accepts cache_control on a system CONTENT BLOCK, not on a
        // plain string, so system is emitted as an array when caching is on.
        // ------------------------------------------------------------------
        public const string ComponentVersion = "1.0.5.0";   // 1.0.5.0: 2-block system (cached stable + uncached dynamic) + optional automatic messages cache. 1.0.4.0: optional output_config.effort

        private static readonly HttpClient _http = new HttpClient();

        private readonly string _apiKey;
        private readonly string _model;
        private readonly int _maxTokens;
        // v1.0.160.34: NO sampling parameters are sent to Anthropic. Claude Opus 4.7+ and Claude Fable 5
        // reject temperature / top_p / top_k -- any non-default value returns HTTP 400 -- and the
        // documented migration is to OMIT them and steer with the prompt. This client therefore sends no
        // temperature and no seed. Verdict consistency comes from the computed sustain/breach result
        // (computed-verdict-wins), not from sampling controls.

        static AnthropicClient()
        {
            _http.Timeout = TimeSpan.FromSeconds(300);
        }

        // 1.0.4.0: optional thinking effort -> output_config.effort ("low", "medium", "high", ...).
        // null = not sent, the model uses its own default. Claude 5-family models think by default,
        // and effort is the control that sets how much.
        private readonly string _effort;

        public AnthropicClient(string apiKey, string model, int maxTokens)
            : this(apiKey, model, maxTokens, null)
        {
        }

        public AnthropicClient(string apiKey, string model, int maxTokens, string effort)
        {
            _apiKey = apiKey;
            _model = model;
            _maxTokens = maxTokens;
            _effort = string.IsNullOrWhiteSpace(effort) ? null : effort.Trim();
        }

        // Returns raw JSON string from Anthropic.
        // Caller does JObject.Parse() on the result.
        //
        // cacheSystem is OPTIONAL and defaults to false, so every existing call
        // site keeps its current behaviour untouched.
        public async Task<string> SendMessageAsync(
            object[] messages,
            object[] tools,
            string systemPrompt,
            bool cacheSystem = false,
            bool extendedCacheTtl = false)
        {
            // Build body and serialise to string ONCE.
            // We keep the raw string so we can rebuild StringContent on retry
            // without touching the already-disposed request.Content.
            object systemField;
            if (cacheSystem && !string.IsNullOrEmpty(systemPrompt))
            {
                // Anthropic requires >= 1024 tokens for a cache entry. The analyze
                // system prompt is well past that; a short prompt simply will not
                // be cached, which is a silent no-op rather than an error.
                // 1-hour TTL needs ttl:"1h" here AND the beta header on the request
                // (added in DoPost when extendedCacheTtl is true). Without the header the
                // API ignores the ttl and silently falls back to the 5-minute cache.
                object cacheControl = extendedCacheTtl
                    ? (object)new { type = "ephemeral", ttl = "1h" }
                    : (object)new { type = "ephemeral" };
                systemField = new object[]
                {
                    new
                    {
                        type = "text",
                        text = systemPrompt,
                        cache_control = cacheControl
                    }
                };
            }
            else
            {
                systemField = systemPrompt ?? string.Empty;
            }

            // 1.0.4.0: a dictionary so output_config is only present when an effort is set
            var body = new System.Collections.Generic.Dictionary<string, object>();
            body["model"] = _model;
            body["max_tokens"] = _maxTokens;
            body["stream"] = false;
            body["system"] = systemField;
            body["messages"] = messages;
            if (tools != null && tools.Length > 0)
            {
                body["tools"] = tools;
            }
            if (_effort != null)
            {
                body["output_config"] = new { effort = _effort };
            }

            return await SendBodyAsync(body, extendedCacheTtl).ConfigureAwait(false);
        }

        // 1.0.5.0: PROMPT CACHING WITH A DYNAMIC TAIL.
        // A prompt cache is a PREFIX match: render order is tools -> system -> messages, and one changed
        // byte invalidates everything after it. A system prompt that mixes fixed rules with per-request
        // text (current time, page context, the user's stations) therefore never caches as one string.
        // This overload sends system as TWO blocks:
        //   [0] systemStable  -- carries cache_control = breakpoint 1: caches tools + this block
        //   [1] systemDynamic -- no marker: re-read at full price every request, but it no longer
        //                        breaks the cached part in front of it
        // cacheMessages adds the TOP-LEVEL cache_control (automatic caching = breakpoint 2): the API
        // puts it on the last message block and moves it forward, so in a tool loop step N reads steps
        // 1..N-1 from cache. Both breakpoints use the same TTL (a longer TTL may not follow a shorter one).
        // With cacheSystem=false and cacheMessages=false the request is the plain string
        // systemStable + systemDynamic, i.e. no caching at all.
        public async Task<string> SendMessageAsync(
            object[] messages,
            object[] tools,
            string systemStable,
            string systemDynamic,
            bool cacheSystem,
            bool extendedCacheTtl,
            bool cacheMessages)
        {
            object cacheControl = extendedCacheTtl
                ? (object)new { type = "ephemeral", ttl = "1h" }
                : (object)new { type = "ephemeral" };

            object systemField;
            if (cacheSystem && !string.IsNullOrEmpty(systemStable))
            {
                var blocks = new List<object>
                {
                    new { type = "text", text = systemStable, cache_control = cacheControl }
                };
                if (!string.IsNullOrEmpty(systemDynamic))
                {
                    blocks.Add(new { type = "text", text = systemDynamic });
                }
                systemField = blocks.ToArray();
            }
            else
            {
                systemField = (systemStable ?? string.Empty) + (systemDynamic ?? string.Empty);
            }

            var body = new System.Collections.Generic.Dictionary<string, object>();
            body["model"] = _model;
            body["max_tokens"] = _maxTokens;
            body["stream"] = false;
            body["system"] = systemField;
            body["messages"] = messages;
            if (tools != null && tools.Length > 0)
            {
                body["tools"] = tools;
            }
            if (_effort != null)
            {
                body["output_config"] = new { effort = _effort };
            }
            if (cacheMessages)
            {
                body["cache_control"] = cacheControl;
            }

            return await SendBodyAsync(body, extendedCacheTtl).ConfigureAwait(false);
        }

        // Serialise once, POST, one retry on 429/529 -- shared by both SendMessageAsync overloads.
        private async Task<string> SendBodyAsync(System.Collections.Generic.Dictionary<string, object> body, bool extendedCacheTtl)
        {
            // Keep json as a plain string — reused on retry without re-serialising
            var json = JsonConvert.SerializeObject(body);

            var resp = await DoPost(json, extendedCacheTtl).ConfigureAwait(false);
            var respBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                int code = (int)resp.StatusCode;

                if (code == 429 || code == 529)
                {
                    // Read Retry-After header (Anthropic returns seconds to wait)
                    int waitSec = 15;
                    if (resp.Headers.TryGetValues("retry-after", out var ra))
                    {
                        int parsed;
                        if (int.TryParse(
                                System.Linq.Enumerable.FirstOrDefault(ra) ?? "",
                                out parsed))
                            waitSec = Math.Min(parsed, 30);
                    }

                    await Task.Delay(waitSec * 1000).ConfigureAwait(false);

                    // Retry once — build fresh request from the saved json string
                    resp = await DoPost(json, extendedCacheTtl).ConfigureAwait(false);
                    respBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (!resp.IsSuccessStatusCode)
                    {
                        int code2 = (int)resp.StatusCode;
                        if (code2 == 429 || code2 == 529)
                            throw new AnthropicRateLimitException(code2, respBody);
                        throw new AnthropicException(code2, respBody);
                    }

                    return respBody;
                }

                if (code == 401 || code == 403)
                    throw new AnthropicAuthException(code, respBody);

                throw new AnthropicException(code, respBody);
            }

            return respBody;
        }

        // Builds a fresh HttpRequestMessage every time — required because
        // HttpRequestMessage / StringContent cannot be reused after SendAsync.
        private async Task<HttpResponseMessage> DoPost(string json, bool extendedCacheTtl = false)
        {
            var request = new HttpRequestMessage(HttpMethod.Post,
                "https://api.anthropic.com/v1/messages");

            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            request.Headers.Add("x-api-key", _apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            // 1-hour cache TTL is a beta feature; the ttl:"1h" marker is ignored without this.
            if (extendedCacheTtl)
                request.Headers.Add("anthropic-beta", "extended-cache-ttl-2025-04-11");

            try
            {
                return await _http.SendAsync(request).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                throw new Exception("Anthropic request timed out. Try again in a moment.");
            }
        }
    }

    // ── Typed exceptions ────────────────────────────────────────────────────

    public class AnthropicException : Exception
    {
        public int StatusCode { get; private set; }
        public AnthropicException(int code, string body)
            : base("Anthropic " + code + ": " + body)
        { StatusCode = code; }
    }

    public class AnthropicRateLimitException : AnthropicException
    {
        public AnthropicRateLimitException(int code, string body)
            : base(code, body) { }
    }

    public class AnthropicAuthException : AnthropicException
    {
        public AnthropicAuthException(int code, string body)
            : base(code, body) { }
    }
}
