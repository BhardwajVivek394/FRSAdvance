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

        // POST /AiChat/AnalyzeAlert
        // Scoped single-alert analysis. Reuses the same MCP tools + agentic loop as Chat,
        // but with an analyze-specific system prompt and raw JSON output (no operational
        // filter, no out-of-scope gate). Streams the SAME SSE frames as Chat.
        [HttpPost]
        public async Task<ActionResult> AnalyzeAlert()
        {
            string body;
            using (System.IO.StreamReader sr = new System.IO.StreamReader(Request.InputStream, Encoding.UTF8))
            {
                body = await sr.ReadToEndAsync().ConfigureAwait(false);
            }

            JObject ctx;
            try
            {
                ctx = JObject.Parse(body);
            }
            catch
            {
                Response.StatusCode = 400;
                return Content("Bad JSON", "text/plain");
            }

            string userMsg = BuildAnalyzeUserMessage(ctx);
            JArray messages = new JArray();
            JObject um = new JObject();
            um["role"] = "user";
            um["content"] = userMsg;
            messages.Add(um);

            Response.ContentType = "text/event-stream";
            Response.Headers["Cache-Control"] = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no";
            Response.Buffer = false;
            Response.BufferOutput = false;

            try
            {
                await RunAgenticLoop(messages, AnalyzeSystemPrompt(), true).ConfigureAwait(false);
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

        private static string GetCtx(JObject o, string key)
        {
            JToken t = o[key];
            if (t == null || t.Type == JTokenType.Null)
            {
                return "";
            }
            return t.ToString();
        }

        private static string BuildAnalyzeUserMessage(JObject ctx)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Assess this SINGLE railway alert against telemetry and decide whether the ");
            sb.Append("alert is CONFIRMED (the telemetry supports a genuine field condition) or ");
            sb.Append("NOT_CONFIRMED (the telemetry does not support a sustained fault). Use the ");
            sb.Append("MCP tools to pull supporting evidence before deciding. Do NOT describe the ");
            sb.Append("alert as false or wrong; report what the data shows.").Append("\n\n");
            sb.Append("ALERT CONTEXT:").Append("\n");
            sb.Append("- AlertId: ").Append(GetCtx(ctx, "alertId")).Append("\n");
            sb.Append("- Station/Site: ").Append(GetCtx(ctx, "station")).Append("\n");
            sb.Append("- Asset: ").Append(GetCtx(ctx, "assetName")).Append("\n");
            sb.Append("- Cause code: ").Append(GetCtx(ctx, "causeCode")).Append("\n");
            sb.Append("- Alert type: ").Append(GetCtx(ctx, "alertType")).Append("\n");
            sb.Append("- Incidence time (IST): ").Append(GetCtx(ctx, "time")).Append("\n");
            string reset = GetCtx(ctx, "resetTime");
            if (reset.Length > 0)
            {
                sb.Append("- Rectification/reset time (IST): ").Append(reset).Append("\n");
            }
            string desc = GetCtx(ctx, "description");
            if (desc.Length > 0)
            {
                sb.Append("- Description: ").Append(desc).Append("\n");
            }
            string raw = GetCtx(ctx, "rawMessage");
            if (raw.Length > 0)
            {
                sb.Append("- Alert message: ").Append(raw).Append("\n");
            }
            JToken card = ctx["alertCard"];
            if (card != null && card.Type == JTokenType.Object)
            {
                sb.Append("- Alert condition JSON: ").Append(card.ToString(Formatting.None)).Append("\n");
            }
            JToken rcard = ctx["resetCard"];
            if (rcard != null && rcard.Type == JTokenType.Object)
            {
                sb.Append("- Reset condition JSON: ").Append(rcard.ToString(Formatting.None)).Append("\n");
            }
            sb.Append("\nSteps: (1) resolve the site and asset with search_sites / search_assets; ");
            sb.Append("(2) get the baseline safe range (min / avg / max) with get_attribute_range for the asset attribute(s); ");
            sb.Append("(3) pull ONLY the last ~6 samples around the incidence time with a LIMITED COMPACT history call (small Limit; prefer trend_get if it is available, otherwise a compact/limited history_get) - do NOT request a wide date range or full-resolution series, and make just ONE history call; a short compact window is enough to read direction against the threshold; ");
            sb.Append("(4) check the alert record and any operator remark with get_frs_alerts; ");
            sb.Append("(5) decide CONFIRMED vs NOT_CONFIRMED using the history shape versus the threshold and baseline. ");
            sb.Append("If the history window is empty or too short to judge, return INCONCLUSIVE - never guess.").Append("\n");
            return sb.ToString();
        }

        private static string AnalyzeSystemPrompt()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("You are an EdgeX RDPMS railway alert analyst. You analyze ONE alert at a time ");
            sb.Append("and decide whether the telemetry CONFIRMS a genuine field condition behind the ");
            sb.Append("alert, or does NOT confirm a sustained fault. You never call the alert 'false' ");
            sb.Append("or 'wrong'; you report whether the data supports it.").Append("\n");
            sb.Append("You MUST call the available MCP tools to gather evidence (baseline range, value ");
            sb.Append("history, alert record). Never invent values. All timestamps are IST (+05:30); ");
            sb.Append("do not convert them.").Append("\n\n");
            sb.Append("Categories to choose from: Field HW, Config, Calibration/Threshold, Platform, ");
            sb.Append("A10 transition, Datalogger, Genuine failure.").Append("\n\n");
            sb.Append("Return your answer as a SINGLE JSON object and NOTHING else - no prose, no ");
            sb.Append("markdown, no code fences. Exact shape:").Append("\n");
            sb.Append("{").Append("\n");
            sb.Append("  \"verdict\": \"CONFIRMED | NOT_CONFIRMED | INCONCLUSIVE\",").Append("\n");
            sb.Append("  \"confidence\": 0,").Append("\n");
            sb.Append("  \"headline\": \"one short line, max 15 words\",").Append("\n");
            sb.Append("  \"likely_cause\": \"one short sentence, max 18 words - no narrative\",").Append("\n");
            sb.Append("  \"category\": \"one of the categories above\",").Append("\n");
            sb.Append("  \"evidence\": [\"3 to 5 short bullets, each ONE line max ~14 words, most important first, no repetition, prefer numbers and units\"],").Append("\n");
            sb.Append("  \"recommended_action\": \"2 to 4 terse steps, each a short imperative phrase\",").Append("\n");
            sb.Append("  \"caveats\": \"one short line; omit detail if nothing material\",").Append("\n");
            sb.Append("  \"viz\": {").Append("\n");
            sb.Append("    \"metric\": \"short label of the key metric e.g. Ir\", \"unit\": \"e.g. mA\",").Append("\n");
            sb.Append("    \"value\": 0, \"threshold\": 0, \"baseline\": 0,").Append("\n");
            sb.Append("    \"direction\": \"declining | rising | flat\", \"pct_change\": 0,").Append("\n");
            sb.Append("    \"series\": [],").Append("\n");
            sb.Append("    \"secondary\": { \"label\": \"optional 2nd metric\", \"value\": 0, \"x_threshold\": 0 }").Append("\n");
            sb.Append("  }").Append("\n");
            sb.Append("}").Append("\n");
            sb.Append("BE TERSE AND SCANNABLE: no paragraphs, no narrative, do not restate the alert. ");
            sb.Append("Keep every field short enough to read at a glance; prefer numbers and units over sentences.").Append("\n");
            sb.Append("viz drives an on-screen trend chart. Fill value/threshold/baseline/pct_change as NUMBERS ");
            sb.Append("(never strings) from the tool data; direction is declining/rising/flat. series is the value-history ");
            sb.Append("samples you pulled, OLDEST FIRST as plain numbers; leave series as [] if you could not pull history. ");
            sb.Append("secondary is an optional derived metric (e.g. leakage current) with x_threshold = how many times over its limit; ");
            sb.Append("use null when a number is unknown - never invent viz numbers.").Append("\n");
            sb.Append("WINDOW NAMING: do NOT attach any day-count to averages or the baseline. Never write 7-day, 15-day, ");
            sb.Append("or any N-day - just say \"average\", \"baseline\", or \"safe range\" with no number of days.").Append("\n");
            sb.Append("confidence is an integer 0-100. Use CONFIRMED when the history sustains a real ");
            sb.Append("deviation past the threshold/baseline; NOT_CONFIRMED when it was momentary, ");
            sb.Append("within band, or explained by calibration/config rather than a field fault; ");
            sb.Append("INCONCLUSIVE with low confidence when history is empty or too short. Do NOT ");
            sb.Append("recommend operational actions like allowing/holding trains or operating points; ");
            sb.Append("recommend maintenance/inspection steps only.").Append("\n");
            return sb.ToString();
        }

        // POST /AiChat/AnalyzeChat
        // Follow-up chat about ONE alert, continuing after AnalyzeAlert. The browser sends
        // the alert context, the verdict JSON it received, and the follow-up thread; the
        // server rebuilds the conversation (context message first, then the thread) and
        // streams prose answers through the same agentic loop + MCP tools. Same SSE frames.
        [HttpPost]
        public async Task<ActionResult> AnalyzeChat()
        {
            string body;
            using (System.IO.StreamReader sr = new System.IO.StreamReader(Request.InputStream, Encoding.UTF8))
            {
                body = await sr.ReadToEndAsync().ConfigureAwait(false);
            }

            JObject root;
            try
            {
                root = JObject.Parse(body);
            }
            catch
            {
                Response.StatusCode = 400;
                return Content("Bad JSON", "text/plain");
            }

            JObject ctx = root["context"] as JObject;
            if (ctx == null)
            {
                ctx = new JObject();
            }
            JToken v = root["verdict"];
            string verdictJson = (v != null && v.Type == JTokenType.Object) ? v.ToString(Formatting.None) : "";
            JArray thread = root["messages"] as JArray;
            if (thread == null)
            {
                thread = new JArray();
            }

            JArray messages = new JArray();
            JObject first = new JObject();
            first["role"] = "user";
            first["content"] = BuildFollowupContextMessage(ctx, verdictJson);
            messages.Add(first);

            foreach (JToken t in thread)
            {
                JObject m = t as JObject;
                if (m == null)
                {
                    continue;
                }
                string role = m["role"] != null ? m["role"].ToString() : "";
                string content = m["content"] != null ? m["content"].ToString() : "";
                if (content.Length == 0)
                {
                    continue;
                }
                if (content.Length > 4000)
                {
                    content = content.Substring(0, 4000);
                }
                JObject nm = new JObject();
                nm["role"] = role == "assistant" ? "assistant" : "user";
                nm["content"] = content;
                messages.Add(nm);
            }

            Response.ContentType = "text/event-stream";
            Response.Headers["Cache-Control"] = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no";
            Response.Buffer = false;
            Response.BufferOutput = false;

            try
            {
                await RunAgenticLoop(messages, AnalyzeFollowupSystemPrompt(), true).ConfigureAwait(false);
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

        private static string BuildFollowupContextMessage(JObject ctx, string verdictJson)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("You are continuing a diagnostic conversation about ONE railway alert. ");
            sb.Append("The alert context and the earlier diagnostic verdict are below; the ");
            sb.Append("messages after this one are the follow-up conversation.").Append("\n\n");
            sb.Append("ALERT CONTEXT:").Append("\n");
            sb.Append("- AlertId: ").Append(GetCtx(ctx, "alertId")).Append("\n");
            sb.Append("- Station/Site: ").Append(GetCtx(ctx, "station")).Append("\n");
            sb.Append("- Asset: ").Append(GetCtx(ctx, "assetName")).Append("\n");
            sb.Append("- Cause code: ").Append(GetCtx(ctx, "causeCode")).Append("\n");
            sb.Append("- Alert type: ").Append(GetCtx(ctx, "alertType")).Append("\n");
            sb.Append("- Incidence time (IST): ").Append(GetCtx(ctx, "time")).Append("\n");
            string reset = GetCtx(ctx, "resetTime");
            if (reset.Length > 0)
            {
                sb.Append("- Rectification/reset time (IST): ").Append(reset).Append("\n");
            }
            string desc = GetCtx(ctx, "description");
            if (desc.Length > 0)
            {
                sb.Append("- Description: ").Append(desc).Append("\n");
            }
            string raw = GetCtx(ctx, "rawMessage");
            if (raw.Length > 0)
            {
                sb.Append("- Alert message: ").Append(raw).Append("\n");
            }
            JToken card = ctx["alertCard"];
            if (card != null && card.Type == JTokenType.Object)
            {
                sb.Append("- Alert condition JSON: ").Append(card.ToString(Formatting.None)).Append("\n");
            }
            JToken rcard = ctx["resetCard"];
            if (rcard != null && rcard.Type == JTokenType.Object)
            {
                sb.Append("- Reset condition JSON: ").Append(rcard.ToString(Formatting.None)).Append("\n");
            }
            if (verdictJson.Length > 0)
            {
                sb.Append("\nEARLIER DIAGNOSTIC VERDICT (JSON): ").Append(verdictJson).Append("\n");
            }
            sb.Append("\nAnswer the user's follow-up questions about this alert. Call the MCP tools ");
            sb.Append("whenever fresh data is needed to answer; never invent values.").Append("\n");
            return sb.ToString();
        }

        private static string AnalyzeFollowupSystemPrompt()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("You are an EdgeX RDPMS railway alert analyst answering follow-up questions ");
            sb.Append("about ONE specific alert whose context and verdict are in the first message.").Append("\n");
            sb.Append("You MUST call the available MCP tools whenever a question needs data ");
            sb.Append("(history, baseline, alert records). Never invent values. All timestamps ");
            sb.Append("are IST (+05:30); do not convert them.").Append("\n\n");
            sb.Append("Answer in plain concise prose - short paragraphs, or short dash lists when ");
            sb.Append("listing readings. No JSON, no markdown headings, no code fences.").Append("\n");
            sb.Append("Ground every claim in tool data or the given context; if the data cannot ");
            sb.Append("answer the question, say so plainly.").Append("\n");
            sb.Append("Never describe the alert as false or wrong; report whether telemetry ");
            sb.Append("confirms it. Do NOT recommend operational actions like allowing/holding ");
            sb.Append("trains or operating points; recommend maintenance/inspection steps only.").Append("\n");
            return sb.ToString();
        }

        // ═════════════════════════════════════════════════════════
        //  AGENTIC LOOP
        // ═════════════════════════════════════════════════════════
        private async Task RunAgenticLoop(JArray incomingMessages, string systemPromptOverride = null, bool rawJsonMode = false)
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

            if (!rawJsonMode && IsOutOfScope(lastUserText))
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
                          "You MUST use the available tools to answer -- never make up data. " +
                          "Always call a tool first. Never answer from memory.\n\n" +
                          "=== PERFORMANCE / HEALTH EVALUATION ===\n" +
                          "When the user asks about 'performance', 'health', 'status', " +
                          "'how is X doing/performing', 'behaviour of X', or similar, follow this workflow:\n\n" +
                          "STEP 1 - IDENTIFY SCOPE: Resolve entity (asset/site/section/division/zone). " +
                          "Use search_sites to get SiteId. Use search_assets for AssetId.\n\n" +
                          "STEP 2 - BASELINE: Call get_attribute_range(SiteId) for safe range " +
                          "(AverageValue, MaxSafeValue, MinSafeValue, MinFailValue per attribute). " +
                          "Cross-reference AssetId with search_assets to get AssetName.\n\n" +
                          "STEP 3 - LIVE VALUES: Call get_tag_current(SiteId) or by AssetId.\n\n" +
                          "STEP 4 - DETERIORATION CHECK: Compare live vs baseline. " +
                          "Current > MaxSafeValue or < MinSafeValue = YELLOW. " +
                          "Current beyond MinFailValue or deviation > 50% from AverageValue = RED. " +
                          "Otherwise = GREEN. Skip attributes where all thresholds are null. " +
                          "Only report attributes that are NOT green.\n\n" +
                          "STEP 5 - ALERTS: Call get_frs_alerts with appropriate window: " +
                          "Asset level: SiteIds + AssetIds, last 5 days. " +
                          "Site/Section/Division/Zone: SiteIds, last 1 day. " +
                          "Use pagination (PageSize=50, Skip for next pages).\n\n" +
                          "STEP 6 - FILTER TESTING ALERTS: Exclude alerts where Remark or " +
                          "MaintainerRemarks contains (case-insensitive): " +
                          "'testing', 'test', 'check', 'routine', 'maintenance', 'calibration', " +
                          "'energy7 staff', 'e7 staff', 'working'. " +
                          "Count excluded alerts separately.\n\n" +
                          "STEP 7 - PRODUCE HEALTH VERDICT:\n" +
                          "ENTITY_NAME - Health: GREEN/YELLOW/RED (N active concerns)\n\n" +
                          "DETERIORATION FLAGS (only non-GREEN attributes):\n" +
                          "  Asset Attribute: current X (avg Y, safe Z-W) [% deviation]\n\n" +
                          "ACTIVE ALERTS (genuine only):\n" +
                          "  Asset - CauseCode (Failure/Predictive) - since HH:MM (duration)\n" +
                          "  (N testing/check alerts excluded)\n\n" +
                          "LAST N-DAY SUMMARY:\n" +
                          "  N genuine alerts, M testing excluded\n" +
                          "  Most active: Asset1 (count), Asset2 (count)\n\n" +
                          "RULES:\n" +
                          "- Do NOT dump raw alert tables or bare sensor values.\n" +
                          "- Always show deviation from baseline, not just raw numbers.\n" +
                          "- Keep concise -- highlight only anomalies and concerns.\n" +
                          "- For site/division/zone: roll up per asset type (Track/Signal/Point Machine/Power Supply).\n" +
                          "=== END PERFORMANCE / HEALTH EVALUATION ===";
            }
            string systemPrompt = !string.IsNullOrWhiteSpace(systemPromptOverride)
                ? systemPromptOverride + BuildDateAnchor()
                : sysBase + BuildDateAnchor() + LoadTrainingText();

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

                    string fullText = rawJsonMode ? sb.ToString() : FilterOperationalActions(sb.ToString());
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

        // Resolve India Standard Time (Asia/Kolkata, UTC+05:30) once, with fallbacks
        // so it works on Windows ("India Standard Time"), Linux/ICU ("Asia/Kolkata"),
        // or a hard-coded +05:30 offset if the OS has no tz database entry.
        private static readonly Lazy<TimeZoneInfo> _istZone =
            new Lazy<TimeZoneInfo>(ResolveIstZone);

        private static TimeZoneInfo ResolveIstZone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"); }
            catch { }
            try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); }
            catch { }
            return TimeZoneInfo.CreateCustomTimeZone(
                "IST", TimeSpan.FromMinutes(330), "India Standard Time", "IST");
        }

        private static string BuildDateAnchor()
        {
            DateTime utcNow = DateTime.UtcNow;
            DateTime istNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, _istZone.Value);

            string edgexIst = istNow.ToString("ddMMyyyy") + "_" + istNow.ToString("HHmmss");

            return "\n\n=== CURRENT DATE/TIME ===\n"
                 + "Today in India (Asia/Kolkata, IST, UTC+05:30) is "
                 + istNow.ToString("yyyy-MM-dd") + " (" + istNow.ToString("HH:mm:ss") + " IST).\n"
                 + "The EdgeX data source stores and returns ALL timestamps in IST (+05:30) "
                 + "e.g. fields like TimestampEdgeX / TimestampLocal / TimestampDevice / TimestampEvent "
                 + "look like \"2026-07-10T19:11:53+05:30\". Do NOT convert these; they are already IST.\n"
                 + "EdgeX timestamp for \"now\" (IST): " + edgexIst + "\n"
                 + "Current IST 'now' as ISO: " + istNow.ToString("yyyy-MM-ddTHH:mm:ss") + "+05:30\n"
                 + "Interpret the user's 'today', 'yesterday', 'last hour' in IST, and "
                 + "ALWAYS present dates and times to the user in IST (Asia/Kolkata, UTC+05:30).\n"
                 + "CRITICAL: When a tool accepts a time range / from / to / start / end / window / period "
                 + "parameter, ALWAYS pass EXPLICIT values computed from the IST 'now' above. "
                 + "Do NOT rely on a tool's default 'now' or omit the end time - the server computes its "
                 + "default in UTC, which is 5h30m behind IST and produces a wrong report window and "
                 + "'Report Generated' stamp. e.g. for 'last 1 hour' pass from="
                 + istNow.AddHours(-1).ToString("yyyy-MM-ddTHH:mm:ss") + "+05:30 to="
                 + istNow.ToString("yyyy-MM-ddTHH:mm:ss") + "+05:30 .\n"
                 + "If a tool result shows a 'Report Period' end or 'Report Generated' time that is about "
                 + "5.5 hours behind the IST 'now' above, it is a UTC server value: add 5h30m and label it IST "
                 + "when you present it (do NOT alter the individual alert incidence times, which are already IST).\n"
                 //+ "Ignore any zero/epoch timestamps like \"0001-01-01T00:00:00+00:00\" - they mean the value was never set.\n"
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