using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Service;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;

namespace E7FRSAdvance.Controllers
{
    public class ModbusController : Controller
    {
        private readonly IZoneService zoneService;
        private readonly IDivisionService divisionService;
        private readonly ISiteService siteService;
        private readonly IAssetTypeService assetTypeService;
        private readonly IAssetAttributeService assetAttributeService;
        public ModbusController(IZoneService zoneService, IDivisionService divisionService, ISiteService siteService, IAssetTypeService assetTypeService, IAssetAttributeService assetAttributeService)
        {
            this.zoneService = zoneService;
            this.divisionService = divisionService;
            this.siteService = siteService;
            this.assetTypeService = assetTypeService;
            this.assetAttributeService = assetAttributeService;
        }

        public ActionResult Index()
        {
            ViewBag.Zones = new SelectList(zoneService.GetAll(), "Id", "Name");
            ViewBag.Divisions = new SelectList(divisionService.GetAll(), "Id", "Name");
            ViewBag.AssetTypes = new SelectList(assetTypeService.GetAll(), "Id", "Name");
            return View();
        }

        public ActionResult _TagPartial(int adcId, int pinNo)
        {
            ViewBag.AdcId = adcId;
            ViewBag.PinNo = pinNo;
            //ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");

            return PartialView();
        }

        public JsonResult GetAllAssetType()
        {
            List<AssetType> mAssetTypes = new List<AssetType>();
            try
            {
                mAssetTypes = assetTypeService.GetAll();
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return Json(mAssetTypes, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetAllAssest(int siteId, int assetTypeId)
        {
            List<Asset> mAssets = new List<Asset>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Asset/GetAllAssest/{0}/{1}", siteId, assetTypeId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssets = JsonConvert.DeserializeObject<List<Asset>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return Json(mAssets, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetAssetAttribute(int assetTypeId)
        {
            var mAssetAttributes = assetAttributeService.GetAssetAttributesBy(assetTypeId);
            return Json(mAssetAttributes, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetYardConfigBy(int siteId)
        {
            List<CardLine> mCardLines = new List<CardLine>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("CardLine/SiteId/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mCardLines = JsonConvert.DeserializeObject<List<CardLine>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }

            return Json(mCardLines);

        }


        public JsonResult GetDivisionByZoneId(int zoneId)
        {
            List<Division> mDivisions = new List<Division>();
            if (zoneId > 0)
            {
                string result;
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
                        else
                        {
                            result = "Internal server error.";
                        }
                    }

                }
                catch (Exception ex)
                {
                    result = ex.Message.ToString();
                }
            }
            else
            {
                mDivisions = divisionService.GetAll();
            }

            return Json(mDivisions, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetSitesBy(int divisionId)
        {
            var sites = new List<Site>();
            try
            {
                sites = siteService.GetBy(divisionId);
            }
            catch (Exception)
            {
            }
            return Json(sites, JsonRequestBehavior.AllowGet);
        }

        //UpdateMuli(Domain.AssetInfo mAssetn)

        public JsonResult UpdateMultiplication(Domain.AssetInfo mAssetInfo)
        {

            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssetInfo);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format($"AssetInfo/AssetId/{mAssetInfo.AssetId}/AssetAttributeId/{mAssetInfo.AssetAttributeId}"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Multiplication is updated." };
                    }
                    else
                    {
                        data = new { type = "error", result = "Error occured while updating multiplication!" };
                    }

                }
            }
            catch (Exception ex)
            {
                data = new { type = "error", result = "Error occured while updating multiplication!" };
            }
            return Json(data, JsonRequestBehavior.AllowGet);

        }
    }
}