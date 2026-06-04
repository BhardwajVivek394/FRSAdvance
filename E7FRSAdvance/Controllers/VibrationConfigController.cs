using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Service;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;
using static E7FRSAdvance.Utility.Utility;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class VibrationConfigController : Controller
    {
        // GET: VibrationConfig
        private readonly ISiteService siteService;
        public VibrationConfigController(ISiteService siteService)
        {
            this.siteService = siteService;
        }

        public ActionResult Index()
        {
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            return View();
        }

        public ActionResult GetAllVibrationType()
        {
            var enumData = from VibrationType e in Enum.GetValues(typeof(VibrationType))
                           select new
                           {
                               ID = (int)e,
                               Name = e.ToString().Replace("_", " ")
                           };
            //ViewBag.StatusEnumList = new SelectList(enumData, "Id", "Name");

            return Json(new SelectList(enumData, "Id", "Name"), JsonRequestBehavior.AllowGet);

        }

        public ActionResult GetAllAsset(int siteId, int assetTypeId)
        {
            List<Asset> mAssets = new List<Asset>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Asset/GetAllAssest/{siteId}/{assetTypeId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssets = JsonConvert.DeserializeObject<List<Asset>>(jsonString);
                    }
                   
                }
            }
            catch (Exception)
            {
            }

            return Json(mAssets, JsonRequestBehavior.AllowGet);

        }

        [HttpPost]
        public ActionResult SaveVibrationConfig(VibrationConfig mVibrationConfig)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mVibrationConfig);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("VibrationConfig"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;

                        mVibrationConfig = JsonConvert.DeserializeObject<VibrationConfig>(jsonString);
                    }
                }
            }
            catch (Exception)
            {
            }
            return Json(mVibrationConfig, JsonRequestBehavior.AllowGet);

        }

        public ActionResult GetAllVibrationConfig(int siteId)
        {
            var mVibrationConfigs = new List<VibrationConfig>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"VibrationConfig/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mVibrationConfigs = JsonConvert.DeserializeObject<List<VibrationConfig>>(jsonString);
                    }
                    else
                    {
                        ViewBag.Error = "Internal server error.";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = "Internal server error.";
            }
            return Json(mVibrationConfigs, JsonRequestBehavior.AllowGet);
        }

        public ActionResult Delete(int id)
        {
            dynamic data = new { type = "", result = "" };
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.DeleteAsync(String.Format("VibrationConfig/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var isDelete = JsonConvert.DeserializeObject<bool>(jsonString);
                        if (isDelete)
                            data = new { type = "success", result = "Deleted" };
                        else
                            data = new { type = "error", result = "Internal server error." };
                    }
                    else
                        data = new { type = "error", result = "Internal server error." };
                }
            }
            catch (Exception)
            {
                data = new { type = "error", result = "Internal server error." };
            }
            return Json(data, JsonRequestBehavior.AllowGet);
        }
    }
}