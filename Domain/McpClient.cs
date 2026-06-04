using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Domain
{
    /// <summary>
    /// MCP Client — direct HTTP POST mode (your existing RDPMS server).
    /// 
    /// BUG FIX: _cachedTools was static — both _mcp and _mcp2 shared the
    /// same cache, so whichever server was called first, its tools were
    /// returned for BOTH. Now instance-level.
    /// </summary>
    public class McpClient
    {
        private static readonly HttpClient _http;

        private readonly string _baseUrl;
        private readonly string _token;

        // ── FIX: was static — now instance-level ──
        private JArray _cachedTools = null;
        private readonly object _cacheLock = new object();

        private static int _idCounter = 0;

        static McpClient()
        {
            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback =
                (msg, cert, chain, err) => true;

            _http = new HttpClient(handler);
            _http.Timeout = TimeSpan.FromSeconds(120);
        }

        public McpClient(string mcpSseUrl)
        {
            _token = string.Empty;
            try
            {
                var uri = new Uri(mcpSseUrl);
                var qs = HttpUtility.ParseQueryString(uri.Query);
                var tok = qs["token"];
                if (tok != null) _token = tok;
            }
            catch { }

            int idx = mcpSseUrl.IndexOf("/sse", StringComparison.OrdinalIgnoreCase);
            _baseUrl = idx >= 0
                ? mcpSseUrl.Substring(0, idx) + "/"
                : mcpSseUrl.TrimEnd('/');
        }

        public async Task<JArray> ListToolsAsync()
        {
            lock (_cacheLock)
            {
                if (_cachedTools != null)
                    return _cachedTools;
            }

            var rpc = new
            {
                jsonrpc = "2.0",
                id = Interlocked.Increment(ref _idCounter),
                method = "tools/list",
                @params = new { }
            };

            var result = await PostRpcAsync(rpc).ConfigureAwait(false);
            var rawTools = result != null && result["tools"] is JArray arr
                ? arr
                : new JArray();

            var anthropicTools = new JArray();
            foreach (var t in rawTools)
            {
                var inputSchema = t["inputSchema"] as JObject;
                if (inputSchema == null)
                {
                    inputSchema = new JObject();
                    inputSchema["type"] = "object";
                    inputSchema["properties"] = new JObject();
                }

                var tool = new JObject();
                tool["name"] = t["name"] != null ? t["name"].ToString() : string.Empty;
                tool["description"] = t["description"] != null ? t["description"].ToString() : string.Empty;
                tool["input_schema"] = inputSchema;
                anthropicTools.Add(tool);
            }

            lock (_cacheLock)
            {
                _cachedTools = anthropicTools;
            }

            return anthropicTools;
        }

        public async Task<string> CallToolAsync(string name, JToken args, int maxChars)
        {
            object arguments = new { };
            if (args != null)
            {
                try { arguments = args.ToObject<object>(); }
                catch { }
            }

            var rpc = new
            {
                jsonrpc = "2.0",
                id = Interlocked.Increment(ref _idCounter),
                method = "tools/call",
                @params = new { name = name, arguments = arguments }
            };

            JObject result;
            try
            {
                result = await PostRpcAsync(rpc).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return "Tool call failed: " + ex.Message;
            }

            if (result == null)
                return "No data returned from tool";

            if (result["error"] != null)
                return "Tool error: " + result["error"].ToString();

            var content = result["content"] as JArray;
            if (content == null || content.Count == 0)
                return JsonConvert.SerializeObject(result);

            var sb = new StringBuilder();
            foreach (var block in content)
            {
                if (block["type"] != null && block["type"].ToString() == "text"
                    && block["text"] != null)
                {
                    sb.Append(block["text"].ToString());
                }
            }

            return CapToolResult(sb.ToString(), maxChars);
        }

        public void ResetToolCache()
        {
            lock (_cacheLock)
            {
                _cachedTools = null;
            }
        }

        private async Task<JObject> PostRpcAsync(object rpc)
        {
            var json = JsonConvert.SerializeObject(rpc);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl);
            request.Content = content;

            if (!string.IsNullOrEmpty(_token))
            {
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);
            }

            HttpResponseMessage resp;
            try
            {
                resp = await _http.SendAsync(request).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new Exception("MCP server unreachable: " + ex.Message, ex);
            }

            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                int maxLen = Math.Min(300, body.Length);
                throw new Exception("MCP HTTP " + (int)resp.StatusCode +
                                    ": " + body.Substring(0, maxLen));
            }

            string jsonBody = body;
            if (body.TrimStart().StartsWith("data:"))
            {
                var lines = body.Split('\n');
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("data:"))
                    {
                        var candidate = trimmed.Substring(5).Trim();
                        if (!string.IsNullOrEmpty(candidate))
                        {
                            jsonBody = candidate;
                            break;
                        }
                    }
                }
            }

            JObject parsed;
            try
            {
                parsed = JObject.Parse(jsonBody);
            }
            catch (Exception ex)
            {
                int maxLen = Math.Min(300, body.Length);
                throw new Exception("MCP JSON parse error: " + ex.Message +
                                    " body=" + body.Substring(0, maxLen));
            }

            if (parsed["error"] != null)
                throw new Exception("MCP error: " + parsed["error"].ToString());

            return parsed["result"] as JObject;
        }

        private static string CapToolResult(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
                return text;

            int half = (int)Math.Floor(maxChars * 0.4);
            var head = text.Substring(0, half);
            var tail = text.Substring(text.Length - half);
            int dropped = text.Length - head.Length - tail.Length;

            return head
                + "\n\n[..." + dropped
                + " chars truncated — narrow the query or use Limit/Page to drill in...]\n\n"
                + tail;
        }
    }
}
