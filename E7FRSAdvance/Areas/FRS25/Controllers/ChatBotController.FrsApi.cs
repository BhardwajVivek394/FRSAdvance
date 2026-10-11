using System;
using System.Collections.Generic;
using System.Configuration;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    // ============================================================================
    // v1.3.0.0 -- FRS REST API tool source (one file).
    //
    // Adds the FRS Web API (FRSAlert / FRSAttributeRange, base https://<ip>:590) as a
    // THIRD tool source (owner 3) offered ALONGSIDE the MCP tools. The model picks by
    // description; if one source fails to fetch, the executor returns a plain
    // "could not fetch -- use the other source" string so the model falls back instead
    // of giving up (and the v1.2.9.6 loop guard still forces a final answer).
    //
    // Auth is CENTRAL here (one place), which is also what removes the 403-on-expiry that
    // was emptying alerts. Base URL, credentials and auth mode are web.config keys:
    //   <add key="FrsApiBaseUrl"  value="http://<server-ip>:590" />   (use the IP, not the hostname)
    //   <add key="FrsApiUser"     value="Energy7@91Api" />
    //   <add key="FrsApiPassword" value="energy7@91" />
    //   <add key="FrsAuthMode"    value="basic" />   <!-- "basic" or "token" -->
    //   <!-- token mode only: -->
    //   <add key="FrsLoginPath"      value="/api/Login/UserAuthenticate" />
    //   <add key="FrsLoginUserField" value="UserName" />
    //   <add key="FrsLoginPassField" value="Password" />
    //   <add key="FrsTokenField"     value="" />          <!-- blank = auto-detect token/access_token/Token -->
    //   <add key="FrsAuthHeaderName" value="Authorization" />
    //   <add key="FrsAuthScheme"     value="Bearer" />
    //   <add key="FrsTokenTtlMin"    value="300" />        <!-- refresh margin under the 6h session -->
    //   <!-- optional: -->
    //   <add key="FrsIgnoreCertErrors" value="false" />    <!-- true only for an IP with a hostname-mismatched cert -->
    //   <add key="FrsTimeoutSec"       value="20" />
    //
    // NOTE: default auth is BASIC (needs no login endpoint, uses the creds above directly).
    // If the :590 API is token-login instead, set FrsAuthMode=token and the login keys.
    // Only READ (GET) alert/baseline tools are exposed here; the POST list tools
    // (GetList / GetUserFRSLister) are added once their request-body shape is confirmed.
    // ============================================================================
    public partial class ChatBotController
    {
        // v1.3.2.1: optional path prefix for an API hosted under a virtual directory
        // (e.g. FrsApiBaseUrl=http://10.x.x.x:590 + FrsApiPathPrefix=/FRSApi  ->  .../FRSApi/api/FRSAlert/...).
        // Leave FrsApiPathPrefix blank when the API is at the root.
        private static string FrsApiBaseUrl
        {
            get
            {
                string b = ReadStr("FrsApiBaseUrl", "").TrimEnd('/');
                string p = ReadStr("FrsApiPathPrefix", "").Trim();
                if (p.Length > 0) { b += "/" + p.Trim('/'); }
                return b;
            }
        }
        private static string FrsApiUser    { get { return ReadStr("FrsApiUser", ""); } }
        private static string FrsApiPassword{ get { return ReadStr("FrsApiPassword", ""); } }
        private static string FrsAuthMode   { get { return ReadStr("FrsAuthMode", "basic").ToLowerInvariant(); } }
        private static bool   FrsEnabled    { get { return !string.IsNullOrWhiteSpace(FrsApiBaseUrl); } }

        private static readonly Lazy<HttpClient> _frsHttp = new Lazy<HttpClient>(() =>
        {
            try { System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12; }
            catch { }
            HttpClientHandler h = new HttpClientHandler();
            if (ReadBool("FrsIgnoreCertErrors", false))
            {
                try { h.ServerCertificateCustomValidationCallback = (m, c, ch, e) => true; } catch { }
            }
            HttpClient client = new HttpClient(h);
            client.Timeout = TimeSpan.FromSeconds(ReadInt("FrsTimeoutSec", 20));
            return client;
        });

        // ── token cache (token mode) ───────────────────────────────────────────
        private static string _frsToken;
        private static DateTime _frsTokenExpUtc = DateTime.MinValue;
        private static readonly SemaphoreSlim _frsTokenLock = new SemaphoreSlim(1, 1);

        // ── tool catalog: name -> (method is GET for all here), path template, required args ──
        // Path params in {braces} are filled from the model's tool input.
        private sealed class FrsTool
        {
            public string Name;
            public string Description;
            public string PathTemplate;          // e.g. "/api/FRSAlert/GetBySiteId/{siteId}"
            public string[] Required;            // required arg names
            public string[] Optional;            // optional arg names (appended into the path if the template has them)
        }

        private static readonly FrsTool[] _frsTools = new FrsTool[]
        {
            new FrsTool {
                Name = "frs_active_alert_count",
                Description = "FRS: count of currently ACTIVE alerts system-wide. Use for 'any active alerts?' / "
                            + "'how many active alerts'. Primary alert source; if an MCP alert tool returned empty "
                            + "or errored, use this instead.",
                PathTemplate = "/api/FRSAlert/GetActiveCount",
                Required = new string[0], Optional = new string[0]
            },
            new FrsTool {
                Name = "frs_alerts_by_site",
                Description = "FRS: all alerts for a site by SiteId (both active and reset). Each row carries the "
                            + "active/reset state, cause code, asset, incidence and reset times -- filter for active "
                            + "yourself. Use for 'alerts at site X' / 'active alerts for a station'. If MCP's alert "
                            + "tool returns 0 or fails, use this as the alternative.",
                PathTemplate = "/api/FRSAlert/GetBySiteId/{siteId}",
                Required = new[] { "siteId" }, Optional = new string[0]
            },
            new FrsTool {
                Name = "frs_last_alert_by_asset",
                Description = "FRS: the most recent alert for an asset by AssetId (cause, state, times).",
                PathTemplate = "/api/FRSAlert/GetLastByAssetId/{assetId}",
                Required = new[] { "assetId" }, Optional = new string[0]
            },
            new FrsTool {
                Name = "frs_attribute_range",
                Description = "FRS: baseline thresholds per attribute for an asset (AverageValue / MinSafe / MaxSafe / "
                            + "MinFail). Use to judge whether a live value is in or out of range. Give assetId; "
                            + "optionally attributeId for a single attribute.",
                PathTemplate = "/api/FRSAttributeRange/AssetId/{assetId}",
                Required = new[] { "assetId" }, Optional = new[] { "attributeId" }
            },
        };

        // Build tool definitions in the SAME schema-key shape the loaded MCP tools use
        // (input_schema vs inputSchema), so the model clients accept them unchanged.
        private static JArray FrsBuildTools(JArray existing)
        {
            string schemaKey = "input_schema";
            try
            {
                if (existing != null)
                {
                    foreach (JToken t in existing)
                    {
                        if (t["input_schema"] != null) { schemaKey = "input_schema"; break; }
                        if (t["inputSchema"] != null) { schemaKey = "inputSchema"; break; }
                    }
                }
            }
            catch { }

            JArray outp = new JArray();
            foreach (FrsTool ft in _frsTools)
            {
                JObject props = new JObject();
                JArray required = new JArray();
                foreach (string p in ft.Required)
                {
                    props[p] = new JObject { ["type"] = "string", ["description"] = p + " (required)" };
                    required.Add(p);
                }
                foreach (string p in ft.Optional)
                {
                    props[p] = new JObject { ["type"] = "string", ["description"] = p + " (optional)" };
                }
                JObject schema = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = props,
                    ["required"] = required
                };
                JObject tool = new JObject
                {
                    ["name"] = ft.Name,
                    ["description"] = ft.Description,
                    [schemaKey] = schema
                };
                outp.Add(tool);
            }
            return outp;
        }

        // Routing/fallback guidance for the prompt (only when FRS is wired).
        private static string FrsToolRoutingRule()
        {
            if (!FrsEnabled) { return ""; }
            return "\n\n=== FRS DATA TOOLS (alongside MCP) ===\n"
                 + "You also have direct FRS tools (frs_*) for alerts and baselines: frs_active_alert_count, "
                 + "frs_alerts_by_site, frs_last_alert_by_asset, frs_attribute_range. These and the MCP tools are "
                 + "ALTERNATIVES for the same data. If a tool reports it could not fetch (an '[FRS API error]' or an "
                 + "MCP failure), immediately try the equivalent tool from the OTHER source before answering -- do "
                 + "not conclude 'no data' or '0 alerts' after a single source fails. frs_alerts_by_site returns both "
                 + "active and reset rows; decide 'active' from the row's own active/reset field, not by the tool "
                 + "choice.\n"
                 // v1.3.2.1: the fallback must be LIKE-FOR-LIKE. The sensor-health stream is not an alert source.
                 + "WHAT COUNTS AS AN 'ALERT': when the user asks for alerts, they mean FRS ALERTS -- the railway "
                 + "alert records with a cause code (TC SHORT, PT VOLT FAIL AT NWKR, SIG RG VOLT/CURR LOW, TC TFC I/P "
                 + "VOLT LOW ...), an incidence time and a reset time. The equivalent fallback is get_frs_alerts ONLY. "
                 + "Sensor-health / telemetry-chain rows (CorrelationBreak, 'sensor failure', modem DOWN, A10 "
                 + "OFC/TCP/MQTT, gateway or DFI health timeouts, Debouncer warnings) are NOT alerts and are NEVER a "
                 + "substitute: do not list them in an alert answer, do not count them as active alerts, and do not "
                 + "split an alert answer into 'railway-side' vs 'monitoring' sections. If BOTH frs_alerts_by_site and "
                 + "get_frs_alerts fail, say the FRS alert source is unreachable right now and stop -- do not fall "
                 + "back to a health or telemetry tool. Monitoring/health is answered only when the user asks for "
                 + "health, sensor, modem or connectivity status by name.\n"
                 + "=== END FRS DATA TOOLS ===";
        }

        // ── executor ────────────────────────────────────────────────────────────
        private static async Task<string> CallFrsToolAsync(string toolName, JToken args)
        {
            FrsTool ft = null;
            for (int i = 0; i < _frsTools.Length; i++)
            {
                if (string.Equals(_frsTools[i].Name, toolName, StringComparison.OrdinalIgnoreCase)) { ft = _frsTools[i]; break; }
            }
            if (ft == null) { return FrsFail(toolName, "unknown FRS tool"); }
            if (!FrsEnabled) { return FrsFail(toolName, "FRS base URL not configured"); }

            try
            {
                // required args present?
                for (int i = 0; i < ft.Required.Length; i++)
                {
                    if (string.IsNullOrEmpty(FrsArg(args, ft.Required[i])))
                        return FrsFail(toolName, "missing required argument '" + ft.Required[i] + "'");
                }

                string path = ft.PathTemplate;
                foreach (string p in ft.Required)
                    path = path.Replace("{" + p + "}", Uri.EscapeDataString(FrsArg(args, p)));

                // optional attributeId -> extend the FRSAttributeRange path when supplied
                string attrId = FrsArg(args, "attributeId");
                if (!string.IsNullOrEmpty(attrId) && path.IndexOf("/FRSAttributeRange/AssetId/", StringComparison.OrdinalIgnoreCase) >= 0)
                    path = path + "/AttributeId/" + Uri.EscapeDataString(attrId);

                string url = FrsApiBaseUrl + path;

                using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    string authErr = await FrsApplyAuthAsync(req).ConfigureAwait(false);
                    if (authErr != null) { return FrsFail(toolName, authErr); }

                    HttpResponseMessage resp = await _frsHttp.Value.SendAsync(req).ConfigureAwait(false);
                    string body = resp.Content != null ? await resp.Content.ReadAsStringAsync().ConfigureAwait(false) : "";

                    if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized
                        || resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        // token may have expired mid-flight: drop it so the next call re-logs in
                        _frsToken = null; _frsTokenExpUtc = DateTime.MinValue;
                        return FrsFail(toolName, "auth rejected (" + (int)resp.StatusCode + ")");
                    }
                    if (!resp.IsSuccessStatusCode)
                    {
                        // v1.3.2.1: a 404 on a path copied from Swagger means the BASE is wrong (virtual
                        // directory / app path). Log the full URL so ops can see what was actually hit.
                        System.Diagnostics.Trace.TraceWarning("[ChatBot][FRS] HTTP " + (int)resp.StatusCode
                            + " for " + toolName + " -> " + url);
                        return FrsFail(toolName, "HTTP " + (int)resp.StatusCode
                            + ((int)resp.StatusCode == 404 ? " -- check FrsApiBaseUrl / FrsApiPathPrefix in web.config" : ""));
                    }

                    if (string.IsNullOrWhiteSpace(body)) { return "[]"; }   // empty, not an error
                    if (body.Length > _maxToolLen) { body = body.Substring(0, _maxToolLen); }
                    return body;
                }
            }
            catch (TaskCanceledException) { return FrsFail(toolName, "timed out"); }
            catch (Exception ex) { return FrsFail(toolName, ex.GetType().Name); }
        }

        // Clean fallback string fed back to the model (never the raw exception / URL).
        private static string FrsFail(string toolName, string reason)
        {
            // v1.3.2.1: name the ONE like-for-like fallback; forbid health/telemetry substitution.
            return "[FRS API error] '" + toolName + "' could not fetch (" + reason + "). "
                 + "The only acceptable alternative is the MCP get_frs_alerts tool (FRS alert records). Do NOT "
                 + "substitute get_alerts / sensor-health / modem / A10 / gateway tools -- those are telemetry, not "
                 + "alerts. If get_frs_alerts also fails, tell the user the FRS alert source is unreachable right "
                 + "now; never report 'no alerts' or list health rows as alerts because of this failure.";
        }

        // ── v1.3.2.3: ALERT INTENT -- code-enforced FRS-only alerts ───────────────────
        // "alert(s)" in the latest user turn, unless the user is asking for health/sensor/
        // modem/connectivity by name (then telemetry is the subject and is allowed).
        private static readonly System.Text.RegularExpressions.Regex _alertAskRx =
            new System.Text.RegularExpressions.Regex(@"\balerts?\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex _healthAskRx =
            new System.Text.RegularExpressions.Regex(
                @"\b(?:health|sensor|modem|a10|ofc|mqtt|tcp|gateway|dfi|connectivity|telemetry|correlation|datalogger|debouncer|rssi|network|nw)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        private static bool IsAlertIntent(List<JObject> messages)
        {
            try
            {
                string u = LastUserTextForCause(messages) ?? "";
                if (u.Length == 0) { return false; }
                return _alertAskRx.IsMatch(u) && !_healthAskRx.IsMatch(u);
            }
            catch { return false; }
        }

        // Tools that return telemetry/health, never FRS alert records. Removed from the tool list
        // for an alert question. (get_alerts on MCP is the sensor-health stream, NOT get_frs_alerts.)
        private static readonly string[] _healthToolNames = new string[]
        {
            "get_alerts", "alert_smart", "get_alert_history", "get_sensor_health", "sensor_health",
            "get_health", "health_get", "get_modem_status", "modem_status", "get_a10_status",
            "get_gateway_health", "get_device_health", "device_health", "get_network_status",
            "get_connectivity", "state_get"
        };

        private static object[] PruneHealthToolsForAlerts(object[] tools)
        {
            if (tools == null) { return null; }
            var keep = new List<object>();
            foreach (object t in tools)
            {
                string n = "";
                try { JObject jo = t as JObject ?? JObject.FromObject(t); n = jo["name"] != null ? jo["name"].ToString() : ""; } catch { }
                bool drop = false;
                foreach (string h in _healthToolNames)
                {
                    if (string.Equals(n, h, StringComparison.OrdinalIgnoreCase)) { drop = true; break; }
                }
                if (!drop) { keep.Add(t); }
            }
            return keep.ToArray();
        }

        // Markers that identify a telemetry/health row rather than an FRS alert record.
        private static readonly string[] _healthRowMarks = new string[]
        {
            "CorrelationBreak", "sensor failure", "Sensor failure", "SENSOR FAILURE", "sensor fault",
            "modem DOWN", "Modem DOWN", "MODEM DOWN", "modem down", "OFC down", "OFC DOWN", "TCP down",
            "MQTT down", "MQTT DOWN", "Gateway health", "gateway health", "DFI ", "health timeout",
            "Health timeout", "Debouncer health", "ws_pmop", "ConnectionLost", "connection lost",
            "RSSI", "A10 OFC", "A10 TCP", "A10 MQTT", "NW down", "NW DOWN", "reconnect storm",
            "device offline", "Device offline", "SensorHealth", "sensorHealth"
        };

        // Filter a tool result to FRS alert rows. Works on JSON arrays / {data:[...]} / {rows:[...]}
        // / {Alerts:[...]}; a row is kept when it carries an FRS cause code or alert fields AND none
        // of the health markers. Non-JSON or unrecognised shapes are returned unchanged.
        private static string FrsFilterAlertRows(string toolName, string result)
        {
            if (string.IsNullOrWhiteSpace(result)) { return result; }
            string trimmed = result.TrimStart();
            if (!(trimmed.StartsWith("[") || trimmed.StartsWith("{"))) { return result; }
            try
            {
                JToken root = JToken.Parse(result);
                JArray rows = root as JArray;
                string wrapKey = null; JObject wrapper = null;
                if (rows == null)
                {
                    wrapper = root as JObject;
                    if (wrapper == null) { return result; }
                    foreach (string k in new[] { "data", "Data", "rows", "Rows", "alerts", "Alerts", "items", "Items", "result", "Result", "records", "Records" })
                    {
                        if (wrapper[k] is JArray) { rows = (JArray)wrapper[k]; wrapKey = k; break; }
                    }
                    if (rows == null) { return result; }
                }

                int before = rows.Count, dropped = 0;
                var kept = new JArray();
                foreach (JToken r in rows)
                {
                    string flat = r.ToString(Newtonsoft.Json.Formatting.None);
                    bool health = false;
                    foreach (string m in _healthRowMarks)
                    {
                        if (flat.IndexOf(m, StringComparison.Ordinal) >= 0) { health = true; break; }
                    }
                    if (health) { dropped++; continue; }
                    kept.Add(r);
                }
                if (dropped == 0) { return result; }

                string note = "_note";
                if (wrapper != null)
                {
                    wrapper[wrapKey] = kept;
                    wrapper[note] = "filtered: " + dropped + " telemetry/health row(s) removed; " + kept.Count + " FRS alert row(s) remain";
                    return wrapper.ToString(Newtonsoft.Json.Formatting.None);
                }
                JObject outp = new JObject
                {
                    ["rows"] = kept,
                    [note] = "filtered: " + dropped + " telemetry/health row(s) removed; " + kept.Count + " FRS alert row(s) remain (" + before + " received)"
                };
                return outp.ToString(Newtonsoft.Json.Formatting.None);
            }
            catch { return result; }
        }

        private static string FrsArg(JToken args, string key)
        {
            try
            {
                if (args == null) { return ""; }
                JToken v = args[key];
                return v == null || v.Type == JTokenType.Null ? "" : v.ToString().Trim();
            }
            catch { return ""; }
        }

        // Attach auth to the request. Returns null on success, or an error reason.
        private static async Task<string> FrsApplyAuthAsync(HttpRequestMessage req)
        {
            if (FrsAuthMode == "token")
            {
                string tok = await FrsGetTokenAsync().ConfigureAwait(false);
                if (string.IsNullOrEmpty(tok)) { return "login failed"; }
                string headerName = ReadStr("FrsAuthHeaderName", "Authorization");
                string scheme = ReadStr("FrsAuthScheme", "Bearer");
                if (string.Equals(headerName, "Authorization", StringComparison.OrdinalIgnoreCase))
                    req.Headers.TryAddWithoutValidation("Authorization",
                        string.IsNullOrEmpty(scheme) ? tok : (scheme + " " + tok));
                else
                    req.Headers.TryAddWithoutValidation(headerName, tok);
                return null;
            }

            // default: HTTP Basic
            string basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(FrsApiUser + ":" + FrsApiPassword));
            req.Headers.TryAddWithoutValidation("Authorization", "Basic " + basic);
            return null;
        }

        private static async Task<string> FrsGetTokenAsync()
        {
            if (!string.IsNullOrEmpty(_frsToken) && DateTime.UtcNow < _frsTokenExpUtc) { return _frsToken; }
            await _frsTokenLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!string.IsNullOrEmpty(_frsToken) && DateTime.UtcNow < _frsTokenExpUtc) { return _frsToken; }

                string loginUrl = FrsApiBaseUrl + ReadStr("FrsLoginPath", "/api/Login/UserAuthenticate");
                JObject payload = new JObject
                {
                    [ReadStr("FrsLoginUserField", "UserName")] = FrsApiUser,
                    [ReadStr("FrsLoginPassField", "Password")] = FrsApiPassword
                };
                using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, loginUrl))
                {
                    req.Content = new StringContent(payload.ToString(Newtonsoft.Json.Formatting.None),
                                                    Encoding.UTF8, "application/json");
                    HttpResponseMessage resp = await _frsHttp.Value.SendAsync(req).ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode) { return null; }
                    string body = resp.Content != null ? await resp.Content.ReadAsStringAsync().ConfigureAwait(false) : "";
                    string tok = FrsExtractToken(body);
                    if (string.IsNullOrEmpty(tok)) { return null; }
                    _frsToken = tok;
                    _frsTokenExpUtc = DateTime.UtcNow.AddMinutes(ReadInt("FrsTokenTtlMin", 300));
                    return _frsToken;
                }
            }
            catch { return null; }
            finally { _frsTokenLock.Release(); }
        }

        private static string FrsExtractToken(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) { return null; }
            string s = body.Trim();
            try
            {
                JToken j = JToken.Parse(s);
                if (j.Type == JTokenType.String) { return j.ToString(); }
                JObject o = j as JObject;
                if (o != null)
                {
                    string configured = ReadStr("FrsTokenField", "");
                    if (!string.IsNullOrEmpty(configured) && o[configured] != null) { return o[configured].ToString(); }
                    string[] names = { "token", "access_token", "Token", "AccessToken", "accessToken", "jwt", "Jwt", "bearer" };
                    for (int i = 0; i < names.Length; i++)
                    {
                        if (o[names[i]] != null && o[names[i]].Type != JTokenType.Null) { return o[names[i]].ToString(); }
                    }
                    // sometimes nested under data/result
                    foreach (string wrap in new[] { "data", "Data", "result", "Result" })
                    {
                        JObject inner = o[wrap] as JObject;
                        if (inner != null)
                        {
                            for (int i = 0; i < names.Length; i++)
                                if (inner[names[i]] != null) { return inner[names[i]].ToString(); }
                        }
                    }
                }
            }
            catch
            {
                // not JSON: a bare token string with no quotes
                if (s.Length > 0 && s.IndexOf(' ') < 0 && s.IndexOf('{') < 0) { return s; }
            }
            return null;
        }
    }
}
