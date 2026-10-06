using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Domain;   // AnthropicRateLimitException / AnthropicAuthException live here

namespace E7MRIWeb.Areas.FRS25.Controllers
{
    // ======================================================================
    // OpenAiClient -- component version (Major.Minor.Build.Revision)
    // 1.0.1.0 - Use Domain's (code, body) exception constructors; max_completion_tokens
    //           instead of the deprecated max_tokens; map OpenAI usage field names onto
    //           the Anthropic ones the controller reads, so GPT runs report real tokens
    // 1.0.0.0 - OpenAI-compatible client presenting an Anthropic-shaped reply
    //
    // WHY IT LOOKS LIKE THIS
    // RunAgenticLoop, the read-only tool policy, argument clamps, the grounding
    // gate, evidence classification and verdict schema validation all read the
    // ANTHROPIC response shape. Re-teaching them a second wire format would put
    // every one of those behaviours back in play. So this class speaks OpenAI on
    // the wire and returns Anthropic-shaped JSON to the caller: the loop needs no
    // change at all, and neither does anything downstream of it.
    //
    // Works with any OpenAI-compatible endpoint -- OpenAI, Azure OpenAI, LM
    // Studio, vLLM -- by pointing OpenAiBaseUrl at it.
    //
    // web.config:
    //   <add key="AiProvider"     value="openai" />        (anthropic | openai)
    //   <add key="OpenAiApiKey"   value="sk-..." />
    //   <add key="OpenAiModel"    value="gpt-4.1" />
    //   <add key="OpenAiBaseUrl"  value="https://api.openai.com/v1" />
    // ======================================================================
    public class OpenAiClient
    {
        public const string ComponentVersion = "1.0.2.0";

        private readonly string _apiKey;
        private readonly string _model;
        private readonly int _maxTokens;
        private readonly string _baseUrl;
        // v1.0.160.35: reduced sampling variance (read once). temperature default 0 + seed as a
        // best-effort variance reducer -- OpenAI documents seed as best-effort, NOT a determinism
        // guarantee (monitor system_fingerprint for backend changes; not captured yet). Set
        // AnalyzeTemperature or AnalyzeSeed negative to OMIT that field (some OpenAI reasoning models
        // reject temperature != 1 or reject seed). True verdict consistency is the computed-verdict-wins
        // path, not sampling.
        private static readonly double _anTemp = ReadCfgDouble("AnalyzeTemperature", 0.0);
        private static readonly int _anSeed = ReadCfgInt("AnalyzeSeed", 42);
        private static double ReadCfgDouble(string key, double def)
        {
            string s = System.Configuration.ConfigurationManager.AppSettings[key];
            double d;
            if (!string.IsNullOrWhiteSpace(s) && double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out d)) { return d; }
            return def;
        }
        private static int ReadCfgInt(string key, int def)
        {
            string s = System.Configuration.ConfigurationManager.AppSettings[key];
            int i;
            if (!string.IsNullOrWhiteSpace(s) && int.TryParse(s, out i)) { return i; }
            return def;
        }

        // One HttpClient for the process. HttpWebRequest is not used here because
        // this class is optional: if System.Net.Http is unavailable in the project,
        // simply do not register the provider.
        private static readonly HttpClient _http = new HttpClient();

        public OpenAiClient(string apiKey, string model, int maxTokens, string baseUrl = null)
        {
            _apiKey = apiKey;
            _model = string.IsNullOrWhiteSpace(model) ? "gpt-4.1" : model;
            _maxTokens = maxTokens > 0 ? maxTokens : 8096;
            _baseUrl = (string.IsNullOrWhiteSpace(baseUrl) ? "https://api.openai.com/v1" : baseUrl).TrimEnd('/');
        }

        // ------------------------------------------------------------------
        // Same signature the loop already calls on AnthropicClient.
        // Returns ANTHROPIC-SHAPED JSON:
        //   { "stop_reason": "tool_use|end_turn",
        //     "content": [ {"type":"text","text":...},
        //                  {"type":"tool_use","id":...,"name":...,"input":{...}} ] }
        // ------------------------------------------------------------------
        public async Task<string> SendMessageAsync(object[] messages, object[] tools, string systemPrompt)
        {
            JObject payload = new JObject();
            payload["model"] = _model;
            // max_tokens is deprecated on Chat Completions; the current field is
            // max_completion_tokens.
            payload["max_completion_tokens"] = _maxTokens;
            // v1.0.160.35: reduced sampling variance -- temperature 0 + best-effort seed (see fields above).
            if (_anTemp >= 0.0) { payload["temperature"] = _anTemp; }
            if (_anSeed >= 0) { payload["seed"] = _anSeed; }
            payload["messages"] = ToOpenAiMessages(messages, systemPrompt);

            JArray fns = ToOpenAiTools(tools);
            if (fns.Count > 0)
            {
                payload["tools"] = fns;
                payload["tool_choice"] = "auto";
            }

            string body;
            HttpStatusCode status;
            using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/chat/completions"))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                req.Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
                using (HttpResponseMessage res = await _http.SendAsync(req).ConfigureAwait(false))
                {
                    status = res.StatusCode;
                    body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }

            // Surface the SAME exception types the loop already catches, so its
            // rate-limit and auth handling keeps working unchanged.
            // Same exception TYPES the loop already catches, so its rate-limit and
            // auth handling works unchanged. They are declared in Domain and require
            // (code, body) -- there are no parameterless constructors.
            if (status == (HttpStatusCode)429 || status == (HttpStatusCode)529)
            {
                throw new AnthropicRateLimitException((int)status, Trunc(body, 300));
            }
            if (status == HttpStatusCode.Unauthorized || status == HttpStatusCode.Forbidden)
            {
                throw new AnthropicAuthException((int)status, Trunc(body, 300));
            }
            if ((int)status >= 400)
            {
                throw new Exception("OpenAI returned " + (int)status + ": " + Trunc(body, 300));
            }

            return ToAnthropicShape(body);
        }

        // ---- request conversion -------------------------------------------

        private static JArray ToOpenAiTools(object[] tools)
        {
            JArray outArr = new JArray();
            if (tools == null) { return outArr; }
            foreach (object t in tools)
            {
                JObject src = JObject.FromObject(t);
                JObject fn = new JObject();
                fn["name"] = src["name"];
                if (src["description"] != null) { fn["description"] = src["description"]; }
                // Anthropic calls it input_schema; OpenAI calls it parameters.
                JToken schema = src["input_schema"] ?? src["inputSchema"] ?? src["parameters"];
                fn["parameters"] = schema ?? new JObject { ["type"] = "object", ["properties"] = new JObject() };
                JObject wrap = new JObject();
                wrap["type"] = "function";
                wrap["function"] = fn;
                outArr.Add(wrap);
            }
            return outArr;
        }

        private static JArray ToOpenAiMessages(object[] messages, string systemPrompt)
        {
            JArray outArr = new JArray();
            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                // Anthropic takes a top-level "system"; OpenAI takes a first message.
                JObject sys = new JObject();
                sys["role"] = "system";
                sys["content"] = systemPrompt;
                outArr.Add(sys);
            }
            if (messages == null) { return outArr; }

            foreach (object m in messages)
            {
                JObject src = JObject.FromObject(m);
                string role = src["role"] != null ? src["role"].ToString() : "user";
                JToken content = src["content"];

                if (content != null && content.Type == JTokenType.String)
                {
                    JObject msg = new JObject();
                    msg["role"] = role;
                    msg["content"] = content.ToString();
                    outArr.Add(msg);
                    continue;
                }

                JArray blocks = content as JArray;
                if (blocks == null) { continue; }

                if (role == "assistant")
                {
                    // Anthropic: content blocks mixing text and tool_use.
                    // OpenAI: one assistant message with content + tool_calls[].
                    StringBuilder text = new StringBuilder();
                    JArray calls = new JArray();
                    foreach (JToken b in blocks)
                    {
                        string bt = b["type"] != null ? b["type"].ToString() : "";
                        if (bt == "text" && b["text"] != null) { text.Append(b["text"].ToString()); }
                        else if (bt == "tool_use")
                        {
                            JObject fn = new JObject();
                            fn["name"] = b["name"];
                            // OpenAI wants arguments as a STRING, Anthropic as an object.
                            fn["arguments"] = b["input"] != null
                                ? b["input"].ToString(Formatting.None) : "{}";
                            JObject call = new JObject();
                            call["id"] = b["id"];
                            call["type"] = "function";
                            call["function"] = fn;
                            calls.Add(call);
                        }
                    }
                    JObject msg = new JObject();
                    msg["role"] = "assistant";
                    // must be "" and not null when tool_calls are present
                    msg["content"] = text.ToString();
                    if (calls.Count > 0) { msg["tool_calls"] = calls; }
                    outArr.Add(msg);
                    continue;
                }

                // Anthropic packs EVERY tool_result into ONE user message.
                // OpenAI needs ONE message per result, role "tool".
                foreach (JToken b in blocks)
                {
                    string bt = b["type"] != null ? b["type"].ToString() : "";
                    if (bt != "tool_result")
                    {
                        if (bt == "text" && b["text"] != null)
                        {
                            JObject um = new JObject();
                            um["role"] = "user";
                            um["content"] = b["text"].ToString();
                            outArr.Add(um);
                        }
                        continue;
                    }
                    JObject tm = new JObject();
                    tm["role"] = "tool";
                    tm["tool_call_id"] = b["tool_use_id"];
                    JToken c = b["content"];
                    tm["content"] = c == null ? ""
                        : (c.Type == JTokenType.String ? c.ToString() : c.ToString(Formatting.None));
                    outArr.Add(tm);
                }
            }
            return outArr;
        }

        // ---- response conversion ------------------------------------------

        private static string ToAnthropicShape(string openAiBody)
        {
            JObject root = JObject.Parse(openAiBody);
            JArray choices = root["choices"] as JArray;
            JObject msg = (choices != null && choices.Count > 0) ? choices[0]["message"] as JObject : null;
            string finish = (choices != null && choices.Count > 0 && choices[0]["finish_reason"] != null)
                ? choices[0]["finish_reason"].ToString() : "stop";

            JArray content = new JArray();
            if (msg != null)
            {
                JToken txt = msg["content"];
                if (txt != null && txt.Type == JTokenType.String && txt.ToString().Length > 0)
                {
                    JObject tb = new JObject();
                    tb["type"] = "text";
                    tb["text"] = txt.ToString();
                    content.Add(tb);
                }

                JArray calls = msg["tool_calls"] as JArray;
                if (calls != null)
                {
                    foreach (JToken c in calls)
                    {
                        JToken fn = c["function"];
                        if (fn == null) { continue; }
                        JObject ub = new JObject();
                        ub["type"] = "tool_use";
                        ub["id"] = c["id"] != null ? c["id"] : Guid.NewGuid().ToString("N");
                        ub["name"] = fn["name"];
                        // CRITICAL: OpenAI returns arguments as a JSON STRING.
                        // ClampToolArgs and the tool policy expect an OBJECT -- left
                        // as a string every argument would be silently discarded.
                        ub["input"] = ParseArgs(fn["arguments"]);
                        content.Add(ub);
                    }
                }
            }

            JObject outObj = new JObject();
            outObj["stop_reason"] = (finish == "tool_calls") ? "tool_use"
                                  : (finish == "length" ? "max_tokens" : "end_turn");
            outObj["content"] = content;
            // OpenAI names these differently from Anthropic, and the controller reads
            // the Anthropic names -- copied verbatim, every GPT run reported zero
            // tokens and zero cache, making cost comparison meaningless.
            JObject ou = root["usage"] as JObject;
            if (ou != null)
            {
                JObject u = new JObject();
                u["input_tokens"] = ou["prompt_tokens"] ?? 0;
                u["output_tokens"] = ou["completion_tokens"] ?? 0;
                JToken det = ou["prompt_tokens_details"];
                u["cache_read_input_tokens"] = (det != null && det["cached_tokens"] != null)
                    ? det["cached_tokens"] : 0;
                u["cache_creation_input_tokens"] = 0;
                outObj["usage"] = u;
            }
            return outObj.ToString(Formatting.None);
        }

        private static JToken ParseArgs(JToken args)
        {
            if (args == null) { return new JObject(); }
            if (args.Type == JTokenType.Object) { return args; }
            string s = args.ToString();
            if (string.IsNullOrWhiteSpace(s)) { return new JObject(); }
            try { return JObject.Parse(s); }
            catch { return new JObject(); }
        }

        private static string Trunc(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) { return ""; }
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }
    }
}
