using Domain;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Web;
using System.Web.Mvc;
using E7FRSAdvance.Interface;

namespace E7FRSAdvance.Controllers
{
    public class LuxConfigController : Controller
    {
        // GET: LuxConfig
        private readonly ISiteService siteService;
        public LuxConfigController(ISiteService siteService)
        {
            this.siteService = siteService;
        }

        public ActionResult Index()
        {
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            return View();
        }


        //public ActionResult GetAllVibrationType()
        //{
        //    var enumData = from VibrationType e in Enum.GetValues(typeof(VibrationType))
        //                   select new
        //                   {
        //                       ID = (int)e,
        //                       Name = e.ToString().Replace("_", " ")
        //                   };
        //    //ViewBag.StatusEnumList = new SelectList(enumData, "Id", "Name");

        //    return Json(new SelectList(enumData, "Id", "Name"), JsonRequestBehavior.AllowGet);

        //}

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
                    else
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = ex.Message;
            }

            return Json(mAssets, JsonRequestBehavior.AllowGet);

        }

        [HttpPost]
        public ActionResult Save(LuxConfig mLuxConfig)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mLuxConfig);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("LuxConfig"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;

                        mLuxConfig = JsonConvert.DeserializeObject<LuxConfig>(jsonString);
                    }
                }
            }
            catch (Exception)
            {
            }
            return Json(mLuxConfig, JsonRequestBehavior.AllowGet);

        }

        public ActionResult GetAll(int siteId)
        {
            var mLuxConfigs = new List<LuxConfig>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"LuxConfig/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mLuxConfigs = JsonConvert.DeserializeObject<List<LuxConfig>>(jsonString);
                    }
                    else
                    {
                        ViewBag.Error = "Internal server error.";
                    }
                }
            }
            catch (Exception ex)
            {
            }
            return Json(mLuxConfigs, JsonRequestBehavior.AllowGet);
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
            catch (Exception ex)
            {
                data = new { type = "error", result = "Internal server error." };
            }
            return Json(data, JsonRequestBehavior.AllowGet);
        }
    }
}