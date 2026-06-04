using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web.Mvc;

namespace E7FRSAdvance.Service
{
    public class ZoneService : IZoneService
    {
        private readonly ISiteService siteService;
        public ZoneService(ISiteService _siteService)
        {
            this.siteService = _siteService;
        }
        public List<Domain.Zone> GetAll()
        {
            List<Domain.Zone> mZones = new List<Domain.Zone>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Zone/GetAllZones")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {

                        if (ClsHttpContent.LoginUser.RoleId == (int)E7FRSAdvance.Utility.Utility.Role.User || ClsHttpContent.LoginUser.RoleId == (int)E7FRSAdvance.Utility.Utility.Role.DivisionalAdmin || ClsHttpContent.LoginUser.RoleId == (int)E7FRSAdvance.Utility.Utility.Role.ZonalAdmin)
                        {
                            var zones = JsonConvert.DeserializeObject<List<Domain.Zone>>(jsonString);
                            var sites = siteService.GetAll();
                            if (zones != null && zones.Count > 0 && sites != null && sites.Count > 0)
                            {
                                foreach (var zone in zones)
                                {
                                    if (sites.Where(x => x.ZoneId == zone.Id).Count() > 0)
                                        mZones.Add(zone);

                                }
                            }
                            mZones.Insert(0, new Zone { Id = 0, Name = "Select Zone" });
                        }
                        else
                        {
                            mZones = JsonConvert.DeserializeObject<List<Domain.Zone>>(jsonString);
                            mZones.Insert(0, new Zone { Id = 0, Name = "Select Zone" });
                        }
                    }

                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mZones;
        }


        public List<Domain.Zone> GetAllZones()
        {
            List<Domain.Zone> mZones = new List<Domain.Zone>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Zone/GetAllZones")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {

                        if (ClsHttpContent.LoginUser.RoleId == (int)E7FRSAdvance.Utility.Utility.Role.User || ClsHttpContent.LoginUser.RoleId == (int)E7FRSAdvance.Utility.Utility.Role.DivisionalAdmin || ClsHttpContent.LoginUser.RoleId == (int)E7FRSAdvance.Utility.Utility.Role.ZonalAdmin)
                        {
                            var zones = JsonConvert.DeserializeObject<List<Domain.Zone>>(jsonString);
                            var sites = siteService.GetAll();
                            if (zones != null && zones.Count > 0 && sites != null && sites.Count > 0)
                            {
                                foreach (var zone in zones)
                                {
                                    if (sites.Where(x => x.ZoneId == zone.Id).Count() > 0)
                                        mZones.Add(zone);

                                }
                            }
                        }
                        else
                        {
                            mZones = JsonConvert.DeserializeObject<List<Domain.Zone>>(jsonString);
                        }
                    }

                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mZones;
        }

        public Zone GetZoneById(int id)
        {
            Zone mZone = new Zone();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Zone/GetZoneById/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mZone = JsonConvert.DeserializeObject<Zone>(jsonString);
                    }
                    
                }
            }
            catch (Exception ex)
            {
            }
            return mZone;
        }
    }
}