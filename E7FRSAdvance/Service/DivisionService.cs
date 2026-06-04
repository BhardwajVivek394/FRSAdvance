using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Controllers;
using E7FRSAdvance.Interface;
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
    public class DivisionService : IDivisionService
    {
        private readonly ISiteService siteService;
        public DivisionService(ISiteService siteService)
        {
            this.siteService = siteService;
        }

        public Division Get(int id)
        {
            Division mDivision = new Division();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Division/GetDivisionById/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mDivision = JsonConvert.DeserializeObject<Division>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mDivision;
        }

        public List<Division> GetAll()
        {
            List<Division> mDivisions = new List<Division>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Division/GetAllDivisions")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        if (ClsHttpContent.LoginUser.RoleId == (int)E7FRSAdvance.Utility.Utility.Role.User || ClsHttpContent.LoginUser.RoleId == (int)E7FRSAdvance.Utility.Utility.Role.DivisionalAdmin || ClsHttpContent.LoginUser.RoleId == (int)E7FRSAdvance.Utility.Utility.Role.ZonalAdmin)
                        {
                            var divisions = JsonConvert.DeserializeObject<List<Division>>(jsonString);
                            var sites = siteService.GetAll();
                            if (divisions != null && divisions.Count > 0 && sites != null && sites.Count > 0)
                            {
                                foreach (var division in divisions)
                                {
                                    if (sites.Where(x => x.DivisionId == division.Id).Count() > 0)
                                        mDivisions.Add(division);

                                }
                            }
                            mDivisions.Insert(0, new Division { Id = 0, Name = "Select Division" });
                        }
                        else
                        {
                            mDivisions = JsonConvert.DeserializeObject<List<Division>>(jsonString);
                            mDivisions.Insert(0, new Division { Id = 0, Name = "Select Division" });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mDivisions;
        }

        public List<Division> GetAllDivisions()
        {
            List<Division> mDivisions = new List<Division>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Division/GetAllDivisions")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        if (ClsHttpContent.LoginUser.RoleId == (int)E7FRSAdvance.Utility.Utility.Role.User || ClsHttpContent.LoginUser.RoleId == (int)E7FRSAdvance.Utility.Utility.Role.DivisionalAdmin || ClsHttpContent.LoginUser.RoleId == (int)E7FRSAdvance.Utility.Utility.Role.ZonalAdmin)
                        {
                            var divisions = JsonConvert.DeserializeObject<List<Division>>(jsonString);
                            var sites = siteService.GetAll();
                            if (divisions != null && divisions.Count > 0 && sites != null && sites.Count > 0)
                            {
                                foreach (var division in divisions)
                                {
                                    if (sites.Where(x => x.DivisionId == division.Id).Count() > 0)
                                        mDivisions.Add(division);

                                }
                            }
                        }
                        else
                        {
                            mDivisions = JsonConvert.DeserializeObject<List<Division>>(jsonString);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mDivisions;
        }

        public List<Division> GetByZoneId(int zoneId)
        {
            List<Division> mDivisions = new List<Division>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Division/GetAllDivisionsByZoneId/{0}", zoneId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mDivisions = JsonConvert.DeserializeObject<List<Division>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mDivisions;
        }


        public List<Domain.Division> GetDivisionList(DivisionLister mDivisionLister)
        {
            var divisions = new List<Domain.Division>();
            mDivisionLister.Pager.Take = -1;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mDivisionLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Division/GetAllDivisionLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mDivisionLister = JsonConvert.DeserializeObject<DivisionLister>(jsonString);
                        if (mDivisionLister != null && mDivisionLister.mDivisions != null && mDivisionLister.mDivisions.Count > 0)
                        {
                            if (ClsHttpContent.LoginUser.RoleId == (int)E7FRSAdvance.Utility.Utility.Role.User || ClsHttpContent.LoginUser.RoleId == (int)E7FRSAdvance.Utility.Utility.Role.DivisionalAdmin || ClsHttpContent.LoginUser.RoleId == (int)E7FRSAdvance.Utility.Utility.Role.ZonalAdmin)
                            {
                                var sites = siteService.GetAll();
                                if (mDivisionLister.mDivisions != null && mDivisionLister.mDivisions.Count > 0 && sites != null && sites.Count > 0)
                                {
                                    foreach (var division in mDivisionLister.mDivisions)
                                    {
                                        if (sites.Where(x => x.DivisionId == division.Id).Count() > 0)
                                            divisions.Add(division);

                                    }
                                }
                            }
                            else
                            {
                                divisions = mDivisionLister.mDivisions;
                            }
                        }

                        
                    }
                }
            }
            catch (Exception)
            {
            }

            return divisions;
        }
    }
}