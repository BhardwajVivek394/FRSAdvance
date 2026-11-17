using Newtonsoft.Json;
using System;
using System.Net;
using System.Net.Http;
using System.Web;
using System.Web.Caching;

namespace E7FRSAdvance.Utility
{
    /// <summary>Shape returned by the API (api/UserFavouriteSite) and used by e7-favourite-site.js.</summary>
    public class FavouriteSite
    {
        public int SiteId { get; set; }
        public string SiteName { get; set; }
        public string StationCode { get; set; }
        public int ZoneId { get; set; }
        public int DivisionId { get; set; }
        public string ZoneName { get; set; }
        public string DivisionName { get; set; }
        public DateTime? UpdatedOn { get; set; }
    }

    /// <summary>
    /// One favourite site per user, stored in DB table dbo.UserFavouriteSite and
    /// reached through the Web API (api/UserFavouriteSite/Get|Save|Delete).
    /// The layout asks for it on every page, so the value is cached per user in
    /// memory and refreshed whenever the user saves or removes it.
    /// </summary>
    public static class FavouriteSiteStore
    {
        private const string CacheKeyPrefix = "E7FavouriteSite:";
        private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan CacheFailureFor = TimeSpan.FromMinutes(1);
        // Page loads read the favourite: keep this short so a slow API never holds up a page.
        private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);
        // Save / remove are user clicks: the API can take 30 s+ on its first call after a
        // restart (Entity Framework loads the whole model), so allow for that.
        private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(90);
        private const string BusyMessage = "The server is busy starting up. Please try again in a moment.";

        // Wrapper so "no favourite" (null) can be cached too.
        private class Entry { public FavouriteSite Value; }

        private static string Key(int userId) { return CacheKeyPrefix + userId; }

        private static void Remember(int userId, FavouriteSite fav, TimeSpan ttl)
        {
            HttpRuntime.Cache.Insert(Key(userId), new Entry { Value = fav }, null,
                DateTime.UtcNow.Add(ttl), Cache.NoSlidingExpiration);
        }

        private static HttpClientFactory NewClient(TimeSpan timeout)
        {
            var user = ClsHttpContent.LoginUser;
            var hcf = new HttpClientFactory(token: user == null ? null : user.Token);
            hcf.client.Timeout = timeout;
            return hcf;
        }

        /// <summary>Runs an API call; turns timeouts / connection failures into a readable message.</summary>
        private static HttpStatusCode Send(Func<HttpClientFactory, System.Threading.Tasks.Task<HttpResponseMessage>> call, TimeSpan timeout, out string body)
        {
            using (var hcf = NewClient(timeout))
            {
                try
                {
                    var response = call(hcf).Result;
                    body = response.Content.ReadAsStringAsync().Result;
                    return response.StatusCode;
                }
                catch (AggregateException ex)
                {
                    var inner = ex.GetBaseException();
                    if (inner is System.Threading.Tasks.TaskCanceledException)
                        throw new InvalidOperationException(BusyMessage);
                    throw new InvalidOperationException("Could not reach the server: " + inner.Message);
                }
            }
        }

        private static string ErrorText(string body, HttpStatusCode code)
        {
            try { var s = JsonConvert.DeserializeObject<string>(body); if (!string.IsNullOrWhiteSpace(s)) return s; } catch (Exception) { }
            return "Request failed (" + (int)code + ")";
        }

        /// <summary>Current user's favourite, or null. Never throws.</summary>
        public static FavouriteSite GetForCurrentUser()
        {
            var user = ClsHttpContent.LoginUser;
            if (user == null || user.Id <= 0) return null;

            var cached = HttpRuntime.Cache.Get(Key(user.Id)) as Entry;
            if (cached != null) return cached.Value;

            try
            {
                string body;
                var code = Send(h => h.client.GetAsync("UserFavouriteSite/Get"), ReadTimeout, out body);
                if (code != HttpStatusCode.OK)
                {
                    Remember(user.Id, null, CacheFailureFor);
                    return null;
                }
                var fav = JsonConvert.DeserializeObject<FavouriteSite>(body);
                if (fav != null && fav.SiteId <= 0) fav = null;
                Remember(user.Id, fav, CacheFor);
                return fav;
            }
            catch (Exception)
            {
                // API down / table not created yet: pages simply show no favourite.
                Remember(user.Id, null, CacheFailureFor);
                return null;
            }
        }

        /// <summary>Sets (replaces) the current user's favourite. Throws with a user-readable message on failure.</summary>
        public static FavouriteSite SaveForCurrentUser(int siteId)
        {
            var user = ClsHttpContent.LoginUser;
            if (user == null || user.Id <= 0) throw new InvalidOperationException("Session expired. Please log in again.");

            var payload = JsonConvert.SerializeObject(new { SiteId = siteId });
            string body;
            var code = Send(h => h.PostAsync("UserFavouriteSite/Save", payload), WriteTimeout, out body);
            if (code != HttpStatusCode.OK)
                throw new InvalidOperationException(ErrorText(body, code));

            var fav = JsonConvert.DeserializeObject<FavouriteSite>(body);
            Remember(user.Id, fav, CacheFor);
            return fav;
        }

        /// <summary>Removes the current user's favourite. Throws with a user-readable message on failure.</summary>
        public static void ClearForCurrentUser()
        {
            var user = ClsHttpContent.LoginUser;
            if (user == null || user.Id <= 0) throw new InvalidOperationException("Session expired. Please log in again.");

            string body;
            var code = Send(h => h.PostAsync("UserFavouriteSite/Delete", "{}"), WriteTimeout, out body);
            if (code != HttpStatusCode.OK)
                throw new InvalidOperationException(ErrorText(body, code));
            Remember(user.Id, null, CacheFor);
        }

        /// <summary>
        /// JSON for the logged-in user's favourite ("null" when none), rendered
        /// into the layout as window.E7_FAV_SITE so pages need no extra request.
        /// </summary>
        public static IHtmlString CurrentUserJson()
        {
            try
            {
                var fav = GetForCurrentUser();
                // Escape "<" so a site name can never close the <script> tag.
                return new HtmlString(JsonConvert.SerializeObject(fav).Replace("<", "\\u003c"));
            }
            catch (Exception)
            {
                return new HtmlString("null");
            }
        }

        /// <summary>
        /// A number that stays the same for one login and changes on the next one:
        /// the identity of the LoginUser object kept in the (InProc) session, which
        /// the login creates afresh. Lets the page open the favourite's SIP once
        /// per login. Not a secret and not derived from the token.
        /// </summary>
        public static int LoginStamp()
        {
            var user = ClsHttpContent.LoginUser;
            return user == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(user);
        }

        /// <summary>
        /// True when the user may open Telemetry (where the live SIP lives): same
        /// menu rule as the [Authenticate] filter on TelemetryController.
        /// </summary>
        public static bool CanViewSip()
        {
            try
            {
                var menus = ClsHttpContent.FRS25Menus;
                if (menus == null) return false;
                foreach (var menu in menus)
                {
                    if (menu == null) continue;
                    if (menu.Main != null && string.Equals((menu.Main.ControllerName ?? "").Trim(), "Telemetry", StringComparison.OrdinalIgnoreCase))
                        return true;
                    if (menu.Childs != null)
                        foreach (var sub in menu.Childs)
                            if (sub != null && string.Equals((sub.ControllerName ?? "").Trim(), "Telemetry", StringComparison.OrdinalIgnoreCase))
                                return true;
                }
            }
            catch (Exception) { }
            return false;
        }
    }
}
