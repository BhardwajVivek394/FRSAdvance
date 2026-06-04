using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Domain
{
    /// <summary>
    /// MCP Client — standard SSE transport mode (for ngrok / standard MCP servers).
    /// .NET Framework 4.7.2 / 4.8 compatible.
    ///
    /// Protocol:
    ///   1. GET /sse                → opens long-lived SSE stream
    ///   2. Server sends event: endpoint, data: /messages?sessionId=xxx
    ///   3. Client POSTs JSON-RPC to that endpoint URL
    ///   4. Server sends responses back via SSE as event: message
    ///
    /// Same interface as McpClient (ListToolsAsync, CallToolAsync)
    /// so the controller can use either.
    /// </summary>
    public class McpSseClient : IDisposable
    {
        private readonly string _sseUrl;
        private readonly HttpClient _http;
        private readonly TimeSpan _timeout;

        // Connection state
        private string _postEndpoint = null;
        private bool _initialized = false;
        private readonly SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);

        // Pending JSON-RPC responses keyed by request id
        private readonly ConcurrentDictionary<string, TaskCompletionSource<JObject>> _pending =
            new ConcurrentDictionary<string, TaskCompletionSource<JObject>>();

        private int _nextId = 0;
        private CancellationTokenSource _sseCts = null;

        // Tool cache (instance-level, not static)
        private JArray _cachedTools = null;
        private readonly object _cacheLock = new object();

        public McpSseClient(string sseUrl, int timeoutSeconds = 30)
        {
            _sseUrl  = sseUrl;
            _timeout = TimeSpan.FromSeconds(timeoutSeconds);

            HttpClientHandler handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback =
                (msg, cert, chain, err) => true;

            _http = new HttpClient(handler);
            _http.Timeout = TimeSpan.FromMinutes(10); // SSE is long-lived

            // Skip ngrok browser warning
            _http.DefaultRequestHeaders.Add("ngrok-skip-browser-warning", "true");
        }

        // ═════════════════════════════════════════════════════════
        //  CONNECT — open SSE stream, wait for endpoint, initialize
        // ═════════════════════════════════════════════════════════
        private async Task EnsureConnectedAsync()
        {
            if (_initialized && _postEndpoint != null)
                return;

            await _initLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_initialized && _postEndpoint != null)
                    return;

                // Cancel old listener
                if (_sseCts != null)
                {
                    _sseCts.Cancel();
                    _sseCts.Dispose();
                }
                _sseCts = new CancellationTokenSource();

                // ── Step 1: GET /sse to open the stream ──
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, _sseUrl);
                request.Headers.Add("Accept", "text/event-stream");

                HttpResponseMessage response = await _http
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    int maxLen = Math.Min(300, body.Length);
                    throw new Exception("SSE connect failed (" + (int)response.StatusCode + "): "
                        + body.Substring(0, maxLen));
                }

                Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

                // ── Step 2: Wait for "endpoint" event ──
                TaskCompletionSource<string> endpointTcs = new TaskCompletionSource<string>();

                // Start background SSE reader
                CancellationToken ct = _sseCts.Token;
                Task readerTask = Task.Run(async () =>
                {
                    await ReadSseStream(stream, endpointTcs, ct).ConfigureAwait(false);
                });

                // Wait with timeout
                Task completed = await Task.WhenAny(
                    endpointTcs.Task,
                    Task.Delay(_timeout)
                ).ConfigureAwait(false);

                if (completed != endpointTcs.Task)
                    throw new TimeoutException(
                        "MCP server did not send endpoint event within "
                        + _timeout.TotalSeconds + "s. Check URL: " + _sseUrl);

                string endpointPath = await endpointTcs.Task.ConfigureAwait(false);

                // Build full POST URL
                if (endpointPath.StartsWith("http://") || endpointPath.StartsWith("https://"))
                {
                    _postEndpoint = endpointPath;
                }
                else
                {
                    Uri baseUri = new Uri(_sseUrl);
                    string origin = baseUri.Scheme + "://" + baseUri.Authority;
                    if (!endpointPath.StartsWith("/"))
                        endpointPath = "/" + endpointPath;
                    _postEndpoint = origin + endpointPath;
                }

                // ── Step 3: Send "initialize" ──
                JObject initParams = new JObject();
                initParams["protocolVersion"] = "2024-11-05";
                initParams["capabilities"] = new JObject();
                JObject clientInfo = new JObject();
                clientInfo["name"] = "e7-csharp-sse-client";
                clientInfo["version"] = "1.0.0";
                initParams["clientInfo"] = clientInfo;

                JObject initResult = await SendRpcAsync("initialize", initParams)
                    .ConfigureAwait(false);

                // ── Step 4: Send "notifications/initialized" ──
                await SendNotificationAsync("notifications/initialized", new JObject())
                    .ConfigureAwait(false);

                _initialized = true;

                System.Diagnostics.Trace.TraceInformation(
                    "[McpSseClient] Connected to " + _sseUrl
                    + " → POST endpoint: " + _postEndpoint);
            }
            finally
            {
                _initLock.Release();
            }
        }

        // ═════════════════════════════════════════════════════════
        //  LIST TOOLS
        // ═════════════════════════════════════════════════════════
        public async Task<JArray> ListToolsAsync()
        {
            lock (_cacheLock)
            {
                if (_cachedTools != null)
                    return _cachedTools;
            }

            await EnsureConnectedAsync().ConfigureAwait(false);

            JObject result = await SendRpcAsync("tools/list", new JObject())
                .ConfigureAwait(false);

            JArray rawTools = result["tools"] as JArray;
            if (rawTools == null) rawTools = new JArray();

            // Convert to Anthropic format (same as McpClient)
            JArray anthropicTools = new JArray();
            foreach (JToken t in rawTools)
            {
                JObject inputSchema = t["inputSchema"] as JObject;
                if (inputSchema == null)
                {
                    inputSchema = new JObject();
                    inputSchema["type"] = "object";
                    inputSchema["properties"] = new JObject();
                }

                JObject tool = new JObject();
                tool["name"] = t["name"] != null ? t["name"].ToString() : "";
                tool["description"] = t["description"] != null ? t["description"].ToString() : "";
                tool["input_schema"] = inputSchema;
                anthropicTools.Add(tool);
            }

            lock (_cacheLock)
            {
                _cachedTools = anthropicTools;
            }

            return anthropicTools;
        }

        // ═════════════════════════════════════════════════════════
        //  CALL TOOL
        // ═════════════════════════════════════════════════════════
        public async Task<string> CallToolAsync(string name, JToken args, int maxChars)
        {
            await EnsureConnectedAsync().ConfigureAwait(false);

            JObject callParams = new JObject();
            callParams["name"] = name;

            if (args != null)
                callParams["arguments"] = args;
            else
                callParams["arguments"] = new JObject();

            JObject result;
            try
            {
                result = await SendRpcAsync("tools/call", callParams)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return "Tool call failed: " + ex.Message;
            }

            if (result == null)
                return "No data returned from tool";

            // Extract text from content blocks
            JArray content = result["content"] as JArray;
            if (content == null || content.Count == 0)
                return JsonConvert.SerializeObject(result);

            StringBuilder sb = new StringBuilder();
            foreach (JToken block in content)
            {
                if (block["type"] != null && block["type"].ToString() == "text"
                    && block["text"] != null)
                {
                    sb.Append(block["text"].ToString());
                }
            }

            string text = sb.ToString();

            // Truncate if needed
            if (maxChars > 0 && text.Length > maxChars)
            {
                int half = (int)Math.Floor(maxChars * 0.4);
                string head = text.Substring(0, half);
                string tail = text.Substring(text.Length - half);
                int dropped = text.Length - head.Length - tail.Length;
                text = head + "\n\n[..." + dropped + " chars truncated]\n\n" + tail;
            }

            return text;
        }

        // ═════════════════════════════════════════════════════════
        //  SSE STREAM READER (background task)
        // ═════════════════════════════════════════════════════════
        private async Task ReadSseStream(
            Stream stream,
            TaskCompletionSource<string> endpointTcs,
            CancellationToken ct)
        {
            try
            {
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string currentEvent = null;
                    string currentData  = null;

                    while (!ct.IsCancellationRequested)
                    {
                        string line = await reader.ReadLineAsync().ConfigureAwait(false);
                        if (line == null) break; // stream closed

                        if (line == string.Empty)
                        {
                            // Empty line = end of one SSE event
                            if (currentEvent != null && currentData != null)
                            {
                                ProcessSseEvent(currentEvent, currentData, endpointTcs);
                            }
                            currentEvent = null;
                            currentData  = null;
                            continue;
                        }

                        if (line.StartsWith("event:"))
                        {
                            currentEvent = line.Substring(6).Trim();
                        }
                        else if (line.StartsWith("data:"))
                        {
                            string data = line.Substring(5).Trim();
                            if (currentData == null)
                                currentData = data;
                            else
                                currentData = currentData + "\n" + data;
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning(
                    "[McpSseClient] SSE reader error: " + ex.Message);
            }
        }

        // ═════════════════════════════════════════════════════════
        //  PROCESS ONE SSE EVENT
        // ═════════════════════════════════════════════════════════
        private void ProcessSseEvent(
            string eventType,
            string data,
            TaskCompletionSource<string> endpointTcs)
        {
            // "endpoint" — server tells us the POST URL
            if (eventType == "endpoint")
            {
                endpointTcs.TrySetResult(data);
                return;
            }

            // "message" — JSON-RPC response
            if (eventType == "message")
            {
                try
                {
                    JObject msg = JObject.Parse(data);
                    JToken idToken = msg["id"];

                    if (idToken != null)
                    {
                        string id = idToken.ToString();
                        TaskCompletionSource<JObject> tcs;
                        if (_pending.TryRemove(id, out tcs))
                        {
                            if (msg["error"] != null)
                            {
                                string errMsg = msg["error"]["message"] != null
                                    ? msg["error"]["message"].ToString()
                                    : "Unknown MCP error";
                                tcs.TrySetException(new Exception("MCP error: " + errMsg));
                            }
                            else
                            {
                                JObject result = msg["result"] as JObject;
                                if (result == null) result = new JObject();
                                tcs.TrySetResult(result);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceWarning(
                        "[McpSseClient] Parse error: " + ex.Message);
                }
            }
        }

        // ═════════════════════════════════════════════════════════
        //  SEND JSON-RPC REQUEST (expects response via SSE)
        // ═════════════════════════════════════════════════════════
        private async Task<JObject> SendRpcAsync(string method, JObject parameters)
        {
            int id = Interlocked.Increment(ref _nextId);
            string idStr = id.ToString();

            JObject rpc = new JObject();
            rpc["jsonrpc"] = "2.0";
            rpc["id"]      = id;
            rpc["method"]  = method;
            rpc["params"]  = parameters;

            // Register pending response
            TaskCompletionSource<JObject> tcs = new TaskCompletionSource<JObject>();
            _pending[idStr] = tcs;

            try
            {
                string json = rpc.ToString(Formatting.None);
                StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

                HttpResponseMessage resp = await _http.PostAsync(_postEndpoint, content)
                    .ConfigureAwait(false);

                if (!resp.IsSuccessStatusCode)
                {
                    string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    int maxLen = Math.Min(300, body.Length);
                    throw new Exception("MCP POST failed (" + (int)resp.StatusCode + "): "
                        + body.Substring(0, maxLen));
                }

                // Wait for response on SSE stream
                Task completed = await Task.WhenAny(
                    tcs.Task,
                    Task.Delay(_timeout)
                ).ConfigureAwait(false);

                if (completed != tcs.Task)
                {
                    TaskCompletionSource<JObject> removed;
                    _pending.TryRemove(idStr, out removed);
                    throw new TimeoutException(
                        "MCP server did not respond to '" + method + "' within "
                        + _timeout.TotalSeconds + "s");
                }

                return await tcs.Task.ConfigureAwait(false);
            }
            catch
            {
                TaskCompletionSource<JObject> removed;
                _pending.TryRemove(idStr, out removed);
                throw;
            }
        }

        // ═════════════════════════════════════════════════════════
        //  SEND NOTIFICATION (no response expected)
        // ═════════════════════════════════════════════════════════
        private async Task SendNotificationAsync(string method, JObject parameters)
        {
            JObject rpc = new JObject();
            rpc["jsonrpc"] = "2.0";
            rpc["method"]  = method;
            rpc["params"]  = parameters;

            string json = rpc.ToString(Formatting.None);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            await _http.PostAsync(_postEndpoint, content).ConfigureAwait(false);
        }

        // ═════════════════════════════════════════════════════════
        //  DISPOSE
        // ═════════════════════════════════════════════════════════
        public void Dispose()
        {
            if (_sseCts != null)
            {
                _sseCts.Cancel();
                _sseCts.Dispose();
                _sseCts = null;
            }
            if (_http != null)
            {
                _http.Dispose();
            }
        }
    }
}
