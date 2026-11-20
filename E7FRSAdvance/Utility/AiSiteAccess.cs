using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace E7FRSAdvance.Utility
{
    // Site filter for the AI/ML pages (Track Shorting, PM Operation, PM Clustering, ELD AI).
    // These pages used to filter the Historian site list by LoginUser.UserSites, the rows captured at login.
    // That list is not what the rest of the app shows: SiteService.GetAll gives Admin every site and other
    // roles a live Site/GetSiteByUserId list. So a site such as Bhestan could show everywhere except AI/ML
    // (an Admin with a few explicit UserSite rows, or a site assigned after login).
    // This helper matches SiteService.GetAll: Admin is not filtered; other roles get the live list
    // combined with the login rows. A null result means "do not filter".
    public static class AiSiteAccess
    {
        public static async Task<HashSet<int>> GetAllowedSiteIdsAsync()
        {
            var u = ClsHttpContent.LoginUser;
            if (u == null) return null;
            if (u.RoleId == (int)Utility.Role.Admin) return null;

            var ids = new HashSet<int>();
            if (u.UserSites != null)
                foreach (var us in u.UserSites) ids.Add(us.SiteId);

            try
            {
                using (var hcf = new HttpClientFactory(token: u.Token))
                {
                    var response = await hcf.client.GetAsync($"Site/GetSiteByUserId/UserId/{u.Id}").ConfigureAwait(false);
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var sites = JsonConvert.DeserializeObject<List<Domain.Site>>(json);
                        if (sites != null)
                            foreach (var s in sites) ids.Add(s.Id);
                    }
                }
            }
            catch (Exception)
            {
                // Live lookup failed: keep the login rows only (the old behaviour).
            }

            // Old behaviour: no rows at all means no filter.
            return ids.Count > 0 ? ids : null;
        }

        // Keeps only the allowed sites in a JSON array of site objects; idField names the id property.
        public static string FilterSiteArray(string json, HashSet<int> allowed, string idField)
        {
            if (allowed == null) return json;
            var sites = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(json);
            if (sites == null) return json;
            sites = sites.Where(s => s.ContainsKey(idField) && s[idField] != null && allowed.Contains(Convert.ToInt32(s[idField]))).ToList();
            return JsonConvert.SerializeObject(sites);
        }
    }
}
