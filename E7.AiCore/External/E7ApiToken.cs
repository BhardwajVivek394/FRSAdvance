using System;
using System.Configuration;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace E7.AiCore
{
    // ----------------------------------------------------------------------
    //  E7ApiToken  (AiChat side)  -- v1.0.160.200
    //
    //  Single source of the E7 API session token for AiChatController. Logs in via
    //  /api/Login/UserAuthenticate, caches the token, refreshes it PROACTIVELY before
    //  its TTL and REACTIVELY on demand (force:true / Invalidate()). CallToolRoutedAsync
    //  forwards the returned token to the MCP server on every tools/call, so the MCP
    //  always uses the LATEST generation and never its own static startup token.
    //
    //  WHERE IT IS CALLED: AiChatController.CallToolRoutedAsync() only --
    //    GetTokenAsync(ct)   -> before every MCP call (cached; login only on first use / TTL)
    //    Invalidate()        -> when the MCP result carries the "e7_session_expired" marker,
    //                           so the NEXT tool call re-logs in.
    //
    //  - Service login: no HttpContext / LoginUser dependency -> works after awaits,
    //    in background/SSE, prefetch, loop and chat alike.
    //  - Thread-safe: concurrent tool calls single-flight through ONE login (SemaphoreSlim),
    //    so ten expired calls trigger one refresh, not ten.
    //  - Never throws to the caller: on failure returns null and sets LastError; the
    //    controller logs it and the MCP falls back to its own token for that call.
    //
    //  CONFIRM 3 THINGS against the real E7 API (marked >>>CONFIRM below):
    //    1) the login base URL (is UserAuthenticate on the FRS API :90, or the :8083 proxy?)
    //    2) the request body field names (UserName / Password vs username / password / ...)
    //    3) the response token field (ExtractToken already tolerates the common shapes)
    // ----------------------------------------------------------------------
    internal static class E7ApiToken
    {
        // ---- config (Web.config <appSettings>; all optional, defaults below) -------------
        private static string LoginBase
        {
            get
            {
                // >>>CONFIRM (1): prefer an explicit key; else reuse the FRS API base if present.
                string baseUrl = ConfigurationManager.AppSettings["E7LoginBaseUrl"];
                if (string.IsNullOrWhiteSpace(baseUrl))
                {
                    baseUrl = ConfigurationManager.AppSettings["APIBaseUrl"];
                }
                return (baseUrl ?? "").Trim().TrimEnd('/');
            }
        }

        private static string LoginPath
        {
            get
            {
                return ConfigurationManager.AppSettings["E7LoginPath"] ?? "/api/Login/UserAuthenticate";
            }
        }

        private static string UserName
        {
            get
            {
                return ConfigurationManager.AppSettings["E7LoginUser"] ?? "";
            }
        }

        private static string Password
        {
            get
            {
                return ConfigurationManager.AppSettings["E7LoginPass"] ?? "";
            }
        }

        private static TimeSpan Ttl
        {
            get
            {
                return TimeSpan.FromMinutes(ReadInt("E7TokenTtlMin", 360));      // 6 h
            }
        }

        private static TimeSpan Margin
        {
            get
            {
                return TimeSpan.FromMinutes(ReadInt("E7TokenMarginMin", 30));    // refresh 30 min early
            }
        }

        private static readonly HttpClient httpClient =
            new HttpClient { Timeout = TimeSpan.FromSeconds(ReadInt("E7LoginTimeoutSec", 20)) };
        private static readonly SemaphoreSlim loginGate = new SemaphoreSlim(1, 1);

        private static string cachedToken;
        private static DateTime issuedUtc = DateTime.MinValue;

        /// <summary>Last login error (for diagnostics/logging); null when the last login succeeded.</summary>
        public static string LastError { get; private set; }

        private static bool Fresh
        {
            get
            {
                return !string.IsNullOrEmpty(cachedToken) && (DateTime.UtcNow - issuedUtc) < (Ttl - Margin);
            }
        }

        /// <summary>
        /// Returns a currently-valid E7 token. Uses the cached one until it nears its TTL;
        /// force:true always re-logs in. Returns null on login failure (see LastError).
        /// </summary>
        public static async Task<string> GetTokenAsync(
            CancellationToken ct = default(CancellationToken), bool force = false)
        {
            if (!force && Fresh)
            {
                return cachedToken;
            }

            await loginGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!force && Fresh)
                {
                    return cachedToken;            // another caller just refreshed
                }
                return await LoginAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                loginGate.Release();
            }
        }

        /// <summary>Mark the cached token dead so the next GetTokenAsync forces a login.</summary>
        public static void Invalidate()
        {
            issuedUtc = DateTime.MinValue;
        }

        // ---- internals -------------------------------------------------------------------
        private static async Task<string> LoginAsync(CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(LoginBase))
                {
                    LastError = "E7 login base URL not configured (set E7LoginBaseUrl or APIBaseUrl)";
                    return null;
                }
                string url = BuildLoginUrl();

                // >>>CONFIRM (2): body field names.
                JObject body = new JObject();
                body["UserName"] = UserName;
                body["Password"] = Password;

                using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    req.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                    using (HttpResponseMessage res = await httpClient.SendAsync(req, ct).ConfigureAwait(false))
                    {
                        string raw = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!res.IsSuccessStatusCode)
                        {
                            LastError = "E7 login HTTP " + (int)res.StatusCode;
                            return null;
                        }

                        string tok = ExtractToken(raw);
                        if (string.IsNullOrEmpty(tok))
                        {
                            LastError = "E7 login: no token found in response";
                            return null;
                        }

                        cachedToken = tok;
                        issuedUtc = DateTime.UtcNow;
                        LastError = null;
                        return cachedToken;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LastError = "E7 login exception: " + ex.Message;
                return null;
            }
        }

        private static string BuildLoginUrl()
        {
            string baseUrl = LoginBase;                   // trimmed, no trailing '/'
            string path = LoginPath ?? "";
            if (!path.StartsWith("/"))
            {
                path = "/" + path;
            }
            // If the base already ends with "/api" and the path also starts with "/api/", drop one.
            if (baseUrl.EndsWith("/api", StringComparison.OrdinalIgnoreCase) &&
                path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(4);
            }
            return baseUrl + path;
        }

        // >>>CONFIRM (3): tolerant to common response shapes. Adjust once you know the real one.
        private static string ExtractToken(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }
            raw = raw.Trim();

            // bare string token (some APIs return just "eyJ...")
            if (raw[0] != '{' && raw[0] != '[')
            {
                return raw.Trim('"');
            }

            try
            {
                JToken json = JToken.Parse(raw);
                string[] names = { "token", "Token", "access_token", "accessToken", "jwt", "bearer", "Bearer" };
                string[] wraps = { "", "data.", "Data.", "result.", "Result.", "d." };
                foreach (string wrap in wraps)
                {
                    foreach (string name in names)
                    {
                        JToken value = json.SelectToken(wrap + name);
                        if (value != null && value.Type == JTokenType.String && !string.IsNullOrEmpty((string)value))
                        {
                            return (string)value;
                        }
                    }
                }
            }
            catch
            {
                // fall through -> null
            }
            return null;
        }

        private static int ReadInt(string key, int dflt)
        {
            int n;
            if (int.TryParse(ConfigurationManager.AppSettings[key], out n) && n > 0)
            {
                return n;
            }
            return dflt;
        }
    }
}
