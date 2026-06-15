using Domain;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Mvc;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    public class AiChatController : Controller
    {
        private static readonly int _maxIter = ReadInt("MaxIter", 10);
        private static readonly int _maxHistory = ReadInt("MaxHistoryTurns", 6);
        private static readonly int _maxToolLen = ReadInt("MaxToolResultChars", 20000);
        private static readonly int _warnTokens = ReadInt("WarnTokenThreshold", 40000);

        // ── Tool logging ───────────────────────────────────────
        // Enable with: <add key="AiToolLoggingEnabled" value="true" />
        private static readonly bool _toolLoggingEnabled =
            ReadBool("AiToolLoggingEnabled", false);
        private static readonly int _maxToolLogChars =
            ReadInt("AiToolLogMaxChars", 20000);
        private static readonly object _toolLogLock = new object();
        private static readonly string _toolLogDir =
            System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "App_Data", "AiToolLogs");

        private static readonly Lazy<AnthropicClient> _ai =
            new Lazy<AnthropicClient>(CreateAiClient);

        // ── MCP Server 1: direct HTTP POST (your existing RDPMS server) ──
        private static readonly Lazy<McpClient> _mcp =
            new Lazy<McpClient>(() =>
            {
                string url = ConfigurationManager.AppSettings["McpServerUrl"];
                if (string.IsNullOrEmpty(url))
                    throw new Exception("McpServerUrl missing in web.config");
                return new McpClient(url);
            });

        // ── MCP Server 2: standard SSE protocol (ngrok server) ──
        private static readonly Lazy<McpSseClient> _mcp2 =
            new Lazy<McpSseClient>(() =>
            {
                string url = ConfigurationManager.AppSettings["McpServerUrl2"];
                if (string.IsNullOrEmpty(url))
                    throw new Exception("McpServerUrl2 missing in web.config");
                return new McpSseClient(url);
            });

        // Tool routing map
        private static readonly Dictionary<string, int> _toolOwner =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _toolOwnerLock = new object();

        private class ToolLoadResult
        {
            public JArray Tools { get; set; }
            public int Count1 { get; set; }
            public int Count2 { get; set; }
            public string Error1 { get; set; }
            public string Error2 { get; set; }
        }

        private static AnthropicClient CreateAiClient()
        {
            string key = ConfigurationManager.AppSettings["AnthropicApiKey"];
            string model = ConfigurationManager.AppSettings["AnthropicModel"] ?? "claude-sonnet-4-6";
            int tokens = ReadInt("AnthropicMaxTokens", 8096);
            if (string.IsNullOrEmpty(key))
                throw new Exception("AnthropicApiKey missing in web.config");
            return new AnthropicClient(key, model, tokens);
        }

        private static int ReadInt(string key, int def)
        {
            int v;
            return int.TryParse(ConfigurationManager.AppSettings[key], out v) ? v : def;
        }

        private static bool ReadBool(string key, bool def)
        {
            string raw = ConfigurationManager.AppSettings[key];
            if (string.IsNullOrWhiteSpace(raw)) return def;
            raw = raw.Trim();
            return raw.Equals("true", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("1", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("on", StringComparison.OrdinalIgnoreCase);
        }

        private static int ResolveToolOwner(string toolName)
        {
            int owner = 1;
            lock (_toolOwnerLock)
            {
                if (!string.IsNullOrEmpty(toolName) && _toolOwner.ContainsKey(toolName))
                    owner = _toolOwner[toolName];
            }
            return owner;
        }

        private static void LogToolEvent(
            string conversationId,
            string phase,
            int iteration,
            string toolUseId,
            string toolName,
            int owner,
            JToken args,
            string result,
            Exception error,
            long elapsedMs)
        {
            if (!_toolLoggingEnabled) return;

            try
            {
                JObject entry = new JObject();
                entry["tsUtc"] = DateTime.UtcNow.ToString("o");
                entry["conversationId"] = conversationId ?? string.Empty;
                entry["phase"] = phase ?? string.Empty; // requested / response / error
                entry["iteration"] = iteration;
                entry["toolUseId"] = toolUseId ?? string.Empty;
                entry["toolName"] = toolName ?? string.Empty;
                entry["server"] = owner > 0 ? "srv" + owner : string.Empty;
                if (elapsedMs >= 0) entry["elapsedMs"] = elapsedMs;

                string argsJson = args == null
                    ? string.Empty
                    : CloneAndRedact(args).ToString(Formatting.None);

                entry["argsPreview"] = LimitForLog(argsJson, _maxToolLogChars);
                entry["resultLength"] = string.IsNullOrEmpty(result) ? 0 : result.Length;
                entry["resultPreview"] = LimitForLog(result, _maxToolLogChars);
                entry["error"] = error == null ? string.Empty : LimitForLog(error.ToString(), _maxToolLogChars);

                System.IO.Directory.CreateDirectory(_toolLogDir);
                string path = System.IO.Path.Combine(
                    _toolLogDir,
                    "ai-tool-" + DateTime.UtcNow.ToString("yyyyMMdd") + ".jsonl");

                lock (_toolLogLock)
                {
                    System.IO.File.AppendAllText(
                        path,
                        entry.ToString(Formatting.None) + Environment.NewLine,
                        Encoding.UTF8);
                }
            }
            catch (Exception logEx)
            {
                System.Diagnostics.Trace.TraceWarning(
                    "[AiChat] Tool log failed: " + logEx.Message);
            }
        }

        private static string LimitForLog(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            text = RedactSecretText(text);
            if (maxChars <= 0 || text.Length <= maxChars) return text;
            return text.Substring(0, maxChars)
                + "\n...[truncated " + (text.Length - maxChars) + " chars]";
        }

        private static string RedactSecretText(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return Regex.Replace(
                text,
                @"(?i)(password|token|secret|api[_-]?key|authorization)\s*[:=]\s*[""']?[^,""'\s}]+",
                "$1=***REDACTED***");
        }

        private static JToken CloneAndRedact(JToken token)
        {
            if (token == null) return JValue.CreateNull();

            JObject obj = token as JObject;
            if (obj != null)
            {
                JObject copy = new JObject();
                foreach (JProperty prop in obj.Properties())
                {
                    if (IsSensitiveKey(prop.Name))
                        copy[prop.Name] = "***REDACTED***";
                    else
                        copy[prop.Name] = CloneAndRedact(prop.Value);
                }
                return copy;
            }

            JArray arr = token as JArray;
            if (arr != null)
            {
                JArray copy = new JArray();
                foreach (JToken item in arr)
                    copy.Add(CloneAndRedact(item));
                return copy;
            }

            return token.DeepClone();
        }

        private static bool IsSensitiveKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            string k = key.ToLowerInvariant();
            return k.Contains("password")
                || k.Contains("token")
                || k.Contains("secret")
                || k.Contains("apikey")
                || k.Contains("api_key")
                || k.Contains("authorization");
        }

        // ── Training file ───────────────────────────────────────
        private static string _trainingCache = null;
        private static long _trainingMtime = 0;
        private static readonly object _trainLock = new object();
        private static readonly string _trainingPath =
            System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "App_Data", "training_.txt");

        private static string LoadTrainingText()
        {
            if (!System.IO.File.Exists(_trainingPath))
                return string.Empty;
            long mtime = new System.IO.FileInfo(_trainingPath).LastWriteTimeUtc.Ticks;
            lock (_trainLock)
            {
                if (_trainingCache != null && mtime == _trainingMtime)
                    return _trainingCache;
                string raw = System.IO.File.ReadAllText(_trainingPath, Encoding.UTF8).Trim();
                _trainingCache = string.IsNullOrEmpty(raw)
                    ? string.Empty
                    : "\n\n=== DOMAIN TRAINING ===\n" + raw + "\n=== END TRAINING ===";
                _trainingMtime = mtime;
                return _trainingCache;
            }
        }

        // ═════════════════════════════════════════════════════════
        //  LOAD + MERGE TOOLS
        // ═════════════════════════════════════════════════════════
        private async Task<ToolLoadResult> LoadAllToolsAsync()
        {
            ToolLoadResult r = new ToolLoadResult();
            r.Tools = new JArray();
            r.Error1 = null;
            r.Error2 = null;

            JArray tools1 = new JArray();
            try
            {
                tools1 = await _mcp.Value.ListToolsAsync().ConfigureAwait(false);
                r.Count1 = tools1.Count;
            }
            catch (Exception ex)
            {
                r.Error1 = ex.Message;
            }

            JArray tools2 = new JArray();
            try
            {
                tools2 = await _mcp2.Value.ListToolsAsync().ConfigureAwait(false);
                r.Count2 = tools2.Count;
            }
            catch (Exception ex)
            {
                r.Error2 = ex.Message;
            }

            lock (_toolOwnerLock)
            {
                _toolOwner.Clear();
                foreach (JToken tool in tools1)
                {
                    string name = tool["name"] != null ? tool["name"].ToString() : null;
                    if (!string.IsNullOrEmpty(name) && !_toolOwner.ContainsKey(name))
                    {
                        _toolOwner[name] = 1;
                        r.Tools.Add(tool);
                    }
                }
                foreach (JToken tool in tools2)
                {
                    string name = tool["name"] != null ? tool["name"].ToString() : null;
                    if (!string.IsNullOrEmpty(name) && !_toolOwner.ContainsKey(name))
                    {
                        _toolOwner[name] = 2;
                        r.Tools.Add(tool);
                    }
                }
            }

            return r;
        }

        // ═════════════════════════════════════════════════════════
        //  ROUTE TOOL CALL
        // ═════════════════════════════════════════════════════════
        private async Task<string> CallToolRoutedAsync(string toolName, JToken toolArgs)
        {
            int owner = ResolveToolOwner(toolName);

            // McpClient and McpSseClient have the same method signature
            if (owner == 2)
                return await _mcp2.Value.CallToolAsync(toolName, toolArgs, _maxToolLen).ConfigureAwait(false);
            else
                return await _mcp.Value.CallToolAsync(toolName, toolArgs, _maxToolLen).ConfigureAwait(false);
        }

        // ═════════════════════════════════════════════════════════
        //  GET /AiChat/Status
        // ═════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<ActionResult> Status()
        {
            try
            {
                ToolLoadResult loaded = await LoadAllToolsAsync().ConfigureAwait(false);
                return Json(new
                {
                    ok = loaded.Tools.Count > 0,
                    tools = loaded.Tools.Count,
                    mcp1 = new { tools = loaded.Count1, error = loaded.Error1 },
                    mcp2 = new { tools = loaded.Count2, error = loaded.Error2 }
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        // ═════════════════════════════════════════════════════════
        //  GET /AiChat/Status2
        // ═════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<ActionResult> Status2()
        {
            try
            {
                JArray tools = await _mcp2.Value.ListToolsAsync().ConfigureAwait(false);
                return Content(tools.ToString(Formatting.Indented), "application/json");
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        // ═════════════════════════════════════════════════════════
        //  POST /AiChat/Chat
        // ═════════════════════════════════════════════════════════
        [HttpPost]
        public async Task<ActionResult> Chat()
        {
            string body;
            using (System.IO.StreamReader sr = new System.IO.StreamReader(Request.InputStream, Encoding.UTF8))
                body = await sr.ReadToEndAsync().ConfigureAwait(false);

            JArray messagesRaw;
            try
            {
                JObject payload = JObject.Parse(body);
                messagesRaw = payload["messages"] as JArray;
                if (messagesRaw == null) messagesRaw = new JArray();
            }
            catch
            {
                Response.StatusCode = 400;
                return Content("Bad JSON", "text/plain");
            }

            if (messagesRaw.Count == 0)
            {
                Response.StatusCode = 400;
                return Content("No messages", "text/plain");
            }

            Response.ContentType = "text/event-stream";
            Response.Headers["Cache-Control"] = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no";
            Response.Buffer = false;
            Response.BufferOutput = false;

            try
            {
                await RunAgenticLoop(messagesRaw).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                WriteSse("error", new { message = FriendlyError(ex) });
            }
            finally
            {
                try { Response.Write("data: [DONE]\n\n"); Response.Flush(); } catch { }
            }

            return new EmptyResult();
        }

        // ═════════════════════════════════════════════════════════
        //  AGENTIC LOOP
        // ═════════════════════════════════════════════════════════
        private async Task RunAgenticLoop(JArray incomingMessages)
        {
            List<JObject> messages = new List<JObject>();
            foreach (JToken m in incomingMessages)
            {
                JObject jo = m as JObject;
                if (jo != null) messages.Add(jo);
                else messages.Add(JObject.FromObject(m));
            }

            string conversationId = Guid.NewGuid().ToString("N");

            string lastUserText = string.Empty;
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                if (messages[i]["role"] != null && messages[i]["role"].ToString() == "user")
                {
                    lastUserText = messages[i]["content"] != null ? messages[i]["content"].ToString() : "";
                    break;
                }
            }

            if (IsOutOfScope(lastUserText))
            {
                WriteSse("text", new
                {
                    text = "I am not trained to answer that. I can only assist with " +
                           "EdgeX RDPMS railway monitoring data — asset values, " +
                           "history, alerts and timestamps."
                });
                return;
            }

            // ── Load tools from both servers ──
            ToolLoadResult loaded;
            try
            {
                loaded = await LoadAllToolsAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                WriteSse("text", new { text = "[MCP failed: " + ex.Message + "]\n\n" });
                loaded = new ToolLoadResult { Tools = new JArray() };
            }

            if (loaded.Error1 != null)
                WriteSse("text", new { text = "[MCP1 error: " + loaded.Error1 + "]\n" });
            if (loaded.Error2 != null)
                WriteSse("text", new { text = "[MCP2 error: " + loaded.Error2 + "]\n" });

            if (loaded.Tools.Count == 0)
                WriteSse("text", new { text = "[No tools from either server]\n\n" });
            else
                WriteSse("tool_use", new { name = "MCP1:" + loaded.Count1 + " MCP2:" + loaded.Count2 });

            // ── System prompt ──
            string sysBase = ConfigurationManager.AppSettings["SystemPrompt"];
            if (string.IsNullOrWhiteSpace(sysBase))
            {
                sysBase = "You are an EdgeX industrial data assistant. " +
                          "You MUST use the available tools to answer — never make up data. " +
                          "Always call a tool first. Never answer from memory.";
            }
            string systemPrompt = sysBase + BuildDateAnchor() + LoadTrainingText();

            List<object> workMessages = TrimHistory(messages, _maxHistory).Cast<object>().ToList();
            object[] toolsArray = loaded.Tools.ToObject<object[]>();

            for (int iter = 0; iter < _maxIter; iter++)
            {
                int est = EstimateTokens(workMessages, systemPrompt);
                if (est > _warnTokens)
                    System.Diagnostics.Trace.TraceWarning("[AiChat] est_tokens=" + est);

                string raw;
                try
                {
                    raw = await _ai.Value
                        .SendMessageAsync(workMessages.ToArray(), toolsArray, systemPrompt)
                        .ConfigureAwait(false);
                }
                catch (AnthropicRateLimitException)
                {
                    WriteSse("error", new { message = "Rate limit. Wait ~60s." });
                    return;
                }
                catch (AnthropicAuthException)
                {
                    WriteSse("error", new { message = "AI auth failed." });
                    return;
                }

                JObject resp = JObject.Parse(raw);
                string stopReason = resp["stop_reason"] != null ? resp["stop_reason"].ToString() : "";
                JArray content = resp["content"] as JArray;
                if (content == null) content = new JArray();

                List<JToken> toolUseBlocks = new List<JToken>();
                List<JToken> textBlocks = new List<JToken>();
                foreach (JToken block in content)
                {
                    string t = block["type"] != null ? block["type"].ToString() : "";
                    if (t == "tool_use") toolUseBlocks.Add(block);
                    if (t == "text") textBlocks.Add(block);
                }

                // ── TOOL USE ──
                if (stopReason == "tool_use" && toolUseBlocks.Count > 0)
                {
                    foreach (JToken block in toolUseBlocks)
                    {
                        string toolName = block["name"] != null ? block["name"].ToString() : "";
                        int owner = ResolveToolOwner(toolName);
                        WriteSse("tool_use", new { name = toolName + " (srv" + owner + ")" });
                    }

                    workMessages.Add(new { role = "assistant", content = content });

                    List<object> toolResults = new List<object>();
                    foreach (JToken block in toolUseBlocks)
                    {
                        string toolName = block["name"] != null ? block["name"].ToString() : "";
                        string toolId = block["id"] != null ? block["id"].ToString() : "";
                        JToken toolArgs = block["input"];
                        int owner = ResolveToolOwner(toolName);

                        LogToolEvent(
                            conversationId,
                            "requested",
                            iter,
                            toolId,
                            toolName,
                            owner,
                            toolArgs,
                            null,
                            null,
                            -1);

                        string result;
                        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                        try
                        {
                            result = await CallToolRoutedAsync(toolName, toolArgs)
                                .ConfigureAwait(false);
                            sw.Stop();

                            LogToolEvent(
                                conversationId,
                                "response",
                                iter,
                                toolId,
                                toolName,
                                owner,
                                toolArgs,
                                result,
                                null,
                                sw.ElapsedMilliseconds);
                        }
                        catch (Exception ex)
                        {
                            sw.Stop();
                            result = "Tool '" + toolName + "' failed: " + ex.Message;

                            LogToolEvent(
                                conversationId,
                                "error",
                                iter,
                                toolId,
                                toolName,
                                owner,
                                toolArgs,
                                result,
                                ex,
                                sw.ElapsedMilliseconds);
                        }

                        toolResults.Add(new
                        {
                            type = "tool_result",
                            tool_use_id = toolId,
                            content = result
                        });
                    }

                    workMessages.Add(new { role = "user", content = toolResults });
                    continue;
                }

                // ── TEXT ──
                if (textBlocks.Count > 0)
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (JToken b in textBlocks)
                        if (b["text"] != null) sb.Append(b["text"].ToString());

                    string fullText = FilterOperationalActions(sb.ToString());
                    string[] words = fullText.Split(' ');
                    StringBuilder chunk = new StringBuilder();
                    for (int w = 0; w < words.Length; w++)
                    {
                        if (chunk.Length > 0) chunk.Append(' ');
                        chunk.Append(words[w]);
                        if (chunk.Length > 60 || w == words.Length - 1)
                        {
                            WriteSse("text", new { text = chunk.ToString() });
                            chunk.Clear();
                        }
                    }
                }
                else if (stopReason == "end_turn")
                {
                    WriteSse("text", new { text = "[No response generated]" });
                }
                return;
            }

            WriteSse("text", new { text = "\n\n[Reached " + _maxIter + "-step limit.]" });
        }

        private void WriteSse(string type, object data)
        {
            try
            {
                JObject payload = JObject.FromObject(data);
                payload["type"] = type;
                Response.Write("data: " + payload.ToString(Formatting.None) + "\n\n");
                Response.Flush();
            }
            catch { }
        }

        private static List<JObject> TrimHistory(List<JObject> messages, int maxTurns)
        {
            if (messages.Count <= 2 || maxTurns <= 0) return messages;
            List<int> realUserIdx = new List<int>();
            for (int i = 0; i < messages.Count; i++)
            {
                if (messages[i]["role"] == null || messages[i]["role"].ToString() != "user") continue;
                JToken c = messages[i]["content"];
                bool isToolResult = false;
                JArray arr = c as JArray;
                if (arr != null && arr.Count > 0 && arr[0]["type"] != null)
                    isToolResult = arr[0]["type"].ToString() == "tool_result";
                if (!isToolResult) realUserIdx.Add(i);
            }
            if (realUserIdx.Count <= maxTurns) return messages;
            return messages.Skip(realUserIdx[realUserIdx.Count - maxTurns]).ToList();
        }

        private static int EstimateTokens(IEnumerable<object> messages, string system)
        {
            int total = Chars(system);
            foreach (object msg in messages)
            {
                JObject j;
                JObject jo = msg as JObject;
                j = jo != null ? jo : JObject.FromObject(msg);
                JToken c = j["content"];
                if (c == null) continue;
                if (c.Type == JTokenType.String)
                    total += Chars(c.ToString());
                else if (c.Type == JTokenType.Array)
                    foreach (JToken b in (JArray)c)
                    {
                        string t = b["type"] != null ? b["type"].ToString() : "";
                        if (t == "text" && b["text"] != null) total += Chars(b["text"].ToString());
                        if (t == "tool_use" && b["input"] != null) total += Chars(JsonConvert.SerializeObject(b["input"]));
                        if (t == "tool_result" && b["content"] != null) total += Chars(b["content"].ToString());
                    }
            }
            return total;
        }

        private static int Chars(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            return (int)Math.Ceiling(s.Length / 4.0);
        }

        private static string BuildDateAnchor()
        {
            DateTime now = DateTime.UtcNow;
            string edgex = now.ToString("ddMMyyyy") + "_" + now.ToString("HHmmss");
            return "\n\n=== CURRENT DATE/TIME ===\nToday is " + now.ToString("yyyy-MM-dd")
                 + " (" + now.ToString("HH:mm:ss") + " UTC).\n"
                 + "EdgeX timestamp format for \"now\": " + edgex + "\n"
                 + "Use this as anchor for today/yesterday/last hour. "
                 + "Do NOT infer a date from training data.\n"
                 + "ALWAYS call a tool before speculating about data availability.\n"
                 + "=== END CURRENT DATE/TIME ===";
        }

        private static readonly string[] OutOfScope = new string[]
        {
            "weather","recipe","cook","poem","joke","capital of",
            "python error","javascript","2+2","math","history of india",
            "time in","news","stock","bitcoin","email","translate"
        };

        private static bool IsOutOfScope(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string lower = text.ToLowerInvariant();
            for (int i = 0; i < OutOfScope.Length; i++)
                if (lower.Contains(OutOfScope[i])) return true;
            return false;
        }

        private static readonly Regex OpPattern = new Regex(
            @"\b(should|must|allow train|hold train|acknowledge alert|ack alert|" +
            @"reset|override|dispatch|send train|clear signal|change aspect|" +
            @"throw point|operate point)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static string FilterOperationalActions(string text)
        {
            if (OpPattern.IsMatch(text))
                return "I cannot make that determination. Operational decisions belong to the section controller.";
            return text;
        }

        private static string FriendlyError(Exception ex)
        {
            if (ex is AnthropicRateLimitException) return "Rate limit. Wait ~60s.";
            if (ex is AnthropicAuthException) return "Auth failed. Check web.config.";
            string msg = ex.Message ?? "";
            if (msg.Contains("529")) return "API overloaded. Try again.";
            return msg;
        }
    }
}
