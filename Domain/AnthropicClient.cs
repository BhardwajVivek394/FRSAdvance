using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class AnthropicClient
    {
        private static readonly HttpClient _http = new HttpClient();

        private readonly string _apiKey;
        private readonly string _model;
        private readonly int _maxTokens;

        static AnthropicClient()
        {
            _http.Timeout = TimeSpan.FromSeconds(300);
        }

        public AnthropicClient(string apiKey, string model, int maxTokens)
        {
            _apiKey = apiKey;
            _model = model;
            _maxTokens = maxTokens;
        }

        // Returns raw JSON string from Anthropic.
        // Caller does JObject.Parse() on the result.
        public async Task<string> SendMessageAsync(
            object[] messages,
            object[] tools,
            string systemPrompt)
        {
            // Build body and serialise to string ONCE.
            // We keep the raw string so we can rebuild StringContent on retry
            // without touching the already-disposed request.Content.
            object body;
            if (tools != null && tools.Length > 0)
            {
                body = new
                {
                    model = _model,
                    max_tokens = _maxTokens,
                    stream = false,
                    system = systemPrompt ?? string.Empty,
                    messages = messages,
                    tools = tools
                };
            }
            else
            {
                body = new
                {
                    model = _model,
                    max_tokens = _maxTokens,
                    stream = false,
                    system = systemPrompt ?? string.Empty,
                    messages = messages
                };
            }

            // Keep json as a plain string — reused on retry without re-serialising
            var json = JsonConvert.SerializeObject(body);

            var resp = await DoPost(json).ConfigureAwait(false);
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
                    resp = await DoPost(json).ConfigureAwait(false);
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
        private async Task<HttpResponseMessage> DoPost(string json)
        {
            var request = new HttpRequestMessage(HttpMethod.Post,
                "https://api.anthropic.com/v1/messages");

            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            request.Headers.Add("x-api-key", _apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");

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