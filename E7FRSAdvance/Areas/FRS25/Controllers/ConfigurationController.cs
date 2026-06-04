using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;


namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    [E7FRSAdvance.Areas.FRS25.Filter.Authenticate]
    public class ConfigurationController : Controller
    {
        // GET: FRS25/Configuration
        private readonly IAssetTypeService assetTypeService;
        private readonly IZoneService zoneService;
        public ConfigurationController(IAssetTypeService assetTypeService, IZoneService zoneService)
        {
            this.assetTypeService = assetTypeService;
            this.zoneService = zoneService;
        }
        public ActionResult Index()
        {
            ViewBag.Zones = new SelectList(zoneService.GetAllZones(), "Id", "Name");
            ViewBag.AssetTypes = GetFRSAssetType();
            return View();
        }

        public ActionResult Save(List<Domain.AppConfig> mAppConfigs)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAppConfigs);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("AppConfig"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Remark has been Updated" };
                    }
                    else
                    {
                        data = new { type = "error", result = "Internal server error." };
                    }
                }
            }
            catch
            {
                data = new { type = "error", result = "Internal server error." };
            }
            return Json(data);
        }

        public JsonResult GetAppConfig(int zoneId, string appKey)
        {
            var mAppConfig = new List<Domain.AppConfig>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"AppConfig/AppKey/{appKey}/ZoneId/{zoneId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAppConfig = JsonConvert.DeserializeObject<List<Domain.AppConfig>>(jsonString);
                    }
                }
            }
            catch (Exception)
            {
                mAppConfig = new List<Domain.AppConfig>();
            }
            return Json(mAppConfig, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetAppConfigByGroupName(int zoneId, string groupName)
        {
            var mAppConfigs = new List<Domain.AppConfig>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"AppConfig/GroupName/{groupName}/ZoneId/{zoneId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAppConfigs = JsonConvert.DeserializeObject<List<Domain.AppConfig>>(jsonString);
                    }
                }
            }
            catch (Exception)
            {
                mAppConfigs = new List<Domain.AppConfig>();
            }
            return Json(mAppConfigs, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetAppConfigByGroup(string groupName)
        {
            var mAppConfigs = new List<Domain.AppConfig>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"AppConfig/GroupName/{groupName}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAppConfigs = JsonConvert.DeserializeObject<List<Domain.AppConfig>>(jsonString);
                    }
                }
            }
            catch (Exception)
            {
                mAppConfigs = new List<Domain.AppConfig>();
            }
            return Json(mAppConfigs, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetAppConfigKeyByGroup(string groupName, string appKey)
        {
            var mAppConfig = new Domain.AppConfig();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"AppConfig/GroupName/{groupName}/AppKey/{appKey}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAppConfig = JsonConvert.DeserializeObject<Domain.AppConfig>(jsonString);
                        if (mAppConfig == null)
                            mAppConfig = new Domain.AppConfig();
                    }
                }
            }
            catch (Exception)
            {
                mAppConfig = new Domain.AppConfig();
            }
            return Json(mAppConfig, JsonRequestBehavior.AllowGet);
        }

        private List<Domain.AssetType> GetFRSAssetType()
        {

            return (from E7FRSAdvance.Utility.Utility.FRSAssetType e in Enum.GetValues(typeof(E7FRSAdvance.Utility.Utility.FRSAssetType))
                    select new Domain.AssetType
                    {
                        Id = (int)e,
                        Name = e.ToString().Replace("_", " ")
                    }).ToList();
        }

    }
}