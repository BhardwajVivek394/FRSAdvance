using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;

namespace E7FRSAdvance.Service
{
    public class SiteService : ISiteService
    {
        public List<Domain.Site> GetAll()
        {
            var sites = new List<Domain.Site>();
           
            if (ClsHttpContent.LoginUser.RoleId == (int)E7FRSAdvance.Utility.Utility.Role.User || ClsHttpContent.LoginUser.RoleId == (int)E7FRSAdvance.Utility.Utility.Role.DivisionalAdmin || ClsHttpContent.LoginUser.RoleId == (int)E7FRSAdvance.Utility.Utility.Role.ZonalAdmin)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = hcf.client.GetAsync(String.Format($"Site/GetSiteByUserId/UserId/{ClsHttpContent.LoginUser.Id}")).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            sites = JsonConvert.DeserializeObject<List<Domain.Site>>(jsonString);
                            //sites.Insert(0, new Domain.Site { Id = 0, Name = "Select site" });
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
            else if (ClsHttpContent.LoginUser.RoleId == (int)E7FRSAdvance.Utility.Utility.Role.Admin)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = hcf.client.GetAsync(String.Format($"Site/GetAll")).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            sites = JsonConvert.DeserializeObject<List<Domain.Site>>(jsonString);
                            //sites.Insert(0, new Domain.Site { Id = 0, Name = "Select site" });
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
            if (sites != null && sites.Count > 0)
            {
                sites = sites.OrderBy(s => s.Name).ToList();
                sites.ForEach(x =>
                {
                    if (x.StationCode.IsNotNullOrEmpty())
                        x.Name = $"{x.Name} - {x.StationCode}";
                });
            }

            return sites;
        }

        public Domain.Site Get(int id)
        {
            Domain.Site mSite = new Domain.Site();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Site/GetSiteById/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mSite = JsonConvert.DeserializeObject<Domain.Site>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mSite;
        }

        public Domain.Site GetBy(string siteName)
        {
            Domain.Site mSite = new Domain.Site();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Site/SiteName/{0}", siteName)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mSite = JsonConvert.DeserializeObject<Domain.Site>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mSite;
        }

        public List<Domain.Site> GetBy(int zoneId, int divisionId)
        {
            var sites = new List<Domain.Site>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Site/GetSites/{zoneId}/{divisionId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        sites = JsonConvert.DeserializeObject<List<Domain.Site>>(jsonString);
                        if (ClsHttpContent.LoginUser.RoleId == (int)E7FRSAdvance.Utility.Utility.Role.User || ClsHttpContent.LoginUser.RoleId == (int)E7FRSAdvance.Utility.Utility.Role.DivisionalAdmin || ClsHttpContent.LoginUser.RoleId == (int)E7FRSAdvance.Utility.Utility.Role.ZonalAdmin)
                        {
                            var mSites = GetAll();
                            if (mSites != null && mSites.Count > 0)
                            {
                                var siteIds = mSites.Select(x => x.Id).ToList();
                                sites = sites.Where(x => siteIds.Contains(x.Id)).ToList();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return sites;
        }

        public List<Domain.Site> GetBy(int divisionId)
        {
            var sites = new List<Domain.Site>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Site/GetSites/DivisionId/{divisionId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        sites = JsonConvert.DeserializeObject<List<Domain.Site>>(jsonString);
                        if (ClsHttpContent.LoginUser.RoleId == (int)E7FRSAdvance.Utility.Utility.Role.User || ClsHttpContent.LoginUser.RoleId == (int)E7FRSAdvance.Utility.Utility.Role.DivisionalAdmin || ClsHttpContent.LoginUser.RoleId == (int)E7FRSAdvance.Utility.Utility.Role.ZonalAdmin)
                        {
                            var mSites = GetAll();
                            if (mSites != null && mSites.Count > 0)
                            {
                                var siteIds = mSites.Select(x => x.Id).ToList();
                                sites = sites.Where(x => siteIds.Contains(x.Id)).ToList();
                            }
                        }

                        if (sites != null && sites.Count > 0)
                        {
                            sites.ForEach(x =>
                            {
                                if (x.StationCode.IsNotNullOrEmpty())
                                    x.Name = $"{x.Name} - {x.StationCode}";
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
            }
            return sites;
        }

        public List<Domain.Site> GetSite()
        {
            var sites = new List<Domain.Site>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Site/GetAll")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        sites = JsonConvert.DeserializeObject<List<Domain.Site>>(jsonString);
                        //sites.Insert(0, new Domain.Site { Id = 0, Name = "Select site" });
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }

            return sites;
        }

        public List<Domain.Site> GetDataLoggerSite()
        {
            var sites = new List<Domain.Site>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Site/GetDataLoggerSite")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        sites = JsonConvert.DeserializeObject<List<Domain.Site>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }

            if (ClsHttpContent.LoginUser.RoleId == (int)E7FRSAdvance.Utility.Utility.Role.User || ClsHttpContent.LoginUser.RoleId == (int)E7FRSAdvance.Utility.Utility.Role.DivisionalAdmin || ClsHttpContent.LoginUser.RoleId == (int)E7FRSAdvance.Utility.Utility.Role.ZonalAdmin)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = hcf.client.GetAsync(String.Format($"Site/GetSiteByUserId/UserId/{ClsHttpContent.LoginUser.Id}")).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            var userSites = JsonConvert.DeserializeObject<List<Domain.Site>>(jsonString);
                            if (userSites != null && userSites.Count > 0)
                            {
                                var siteIds = userSites.Select(x => x.Id).ToList();
                                sites = sites.Where(x => siteIds.Contains(x.Id)).ToList();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
            if (sites != null && sites.Count > 0)
            {
                sites = sites.OrderBy(s => s.Name).ToList();
                sites.ForEach(x =>
                {
                    if (x.StationCode.IsNotNullOrEmpty())
                        x.Name = $"{x.Name} - {x.StationCode}";
                });
            }

            return sites;
        }

        public List<Domain.Site> GetSiteLister(SiteLister mSiteLister)
        {
            var mSites = new List<Domain.Site>();
            mSiteLister.Pager.Take = -1;
            mSiteLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
            mSiteLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSiteLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                   // var response = hcf.client.PostAsync(String.Format("Site/GetAllSite"), str).Result;
                    var response = hcf.client.PostAsync(String.Format("Site/GetAllSiteLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSiteLister = JsonConvert.DeserializeObject<SiteLister>(jsonString);
                        if (mSiteLister != null && mSiteLister.mSites != null && mSiteLister.mSites.Count > 0)
                        {
                            mSites = mSiteLister.mSites;
                        }
                    }

                }
            }
            catch (Exception)
            {
            }
            return mSites;
        }
    }
}