using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    [E7FRSAdvance.Areas.FRS25.Filter.Authenticate]
    public class EquipmentRoomController : Controller
    {
        // GET: FRS25/EquipmentRoom
        private readonly ISiteService _siteService;
        private readonly IZoneService _zoneService;
        private readonly IDivisionService _divisionService;
        private readonly IAssetTypeService _assetTypeService;
        private readonly IAssetService _assetService;
        private readonly ISectionService _sectionService;
        private readonly IFRSAlertService _frsAlertService;
        private readonly IAssetAttributeService assetAttributeService;
        private readonly IGraphService graphService;

        public EquipmentRoomController(ISiteService siteService, IZoneService zoneService, IDivisionService divisionService, IAssetTypeService assetTypeService, IAssetService assetService, ISectionService sectionService, IFRSAlertService frsAlertService, IAssetAttributeService assetAttributeService, IGraphService graphService)
        {
            _siteService = siteService;
            _zoneService = zoneService;
            _divisionService = divisionService;
            _assetTypeService = assetTypeService;
            _assetService = assetService;
            _sectionService = sectionService;
            _frsAlertService = frsAlertService;
            this.assetAttributeService = assetAttributeService;
            this.graphService = graphService;
        }

        public ActionResult Index()
        {
            ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
            ViewBag.Zones = new SelectList(_zoneService.GetAllZones(), "Id", "Name");
            ViewBag.Divisions = new SelectList(_divisionService.GetAllDivisions(), "Id", "Name");

            return View();
        }

        public MQTTDetailList GetMQTTDetailList()
        {

            MQTTDetailList mMQTTDetail = new MQTTDetailList();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("User/GetMQTTDetail")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mMQTTDetail = JsonConvert.DeserializeObject<MQTTDetailList>(jsonString);
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
            return mMQTTDetail;
        }

        public ActionResult _List(Domain.AssetLister mAssetLister)
        {
            try
            {
                mAssetLister.Pager.Take = -1;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssetLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GetEquipmentRoom"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetLister = JsonConvert.DeserializeObject<AssetLister>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            finally
            {
                var mMQTTDetailList = GetMQTTDetailList();
                if (mMQTTDetailList != null)
                    ViewBag.MQTTDetail = mMQTTDetailList.mQTTDetailWeb;
            }
            return PartialView(mAssetLister);
        }


        public JsonResult GetDivisionByZoneId(int zoneId)
        {
            return Json(_divisionService.GetByZoneId(zoneId), JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetSiteByDivisionId(int divisionId)
        {
            var mSites = new List<Domain.Site>();
            try
            {
                mSites = _siteService.GetBy(divisionId);
            }
            catch (Exception)
            {
                mSites = new List<Domain.Site>();
            }
            return Json(mSites, JsonRequestBehavior.AllowGet);
        }

        public ActionResult History()
        {
            ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
            ViewBag.Zones = new SelectList(_zoneService.GetAllZones(), "Id", "Name");
            ViewBag.Divisions = new SelectList(_divisionService.GetAllDivisions(), "Id", "Name");

            return View();
        }

        public ActionResult _HistoryList(Domain.AssetLister mAssetLister)
        {
            try
            {
                DateTime startDate = Convert.ToDateTime(mAssetLister.SearchCriteria.StartDate);
                DateTime endDate = Convert.ToDateTime(mAssetLister.SearchCriteria.EndDate);
                var dayLeft = endDate.Subtract(startDate).TotalDays;
                mAssetLister.Pager.Take = -1;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssetLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GetEquipmentRoomHistoryNew"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetLister = JsonConvert.DeserializeObject<AssetLister>(jsonString);
                        if (mAssetLister != null && mAssetLister.mAssets != null && mAssetLister.mAssets.Count > 0)
                        {
                            foreach (var mAsset in mAssetLister.mAssets)
                            {
                                List<AssetAttribute> assetAttributes = new List<AssetAttribute>();
                                var allAssetAttributes = assetAttributeService.GetAssetAttributesBy(mAsset.AssetTypeId);
                                if (mAsset.assetAttributes != null && mAsset.assetAttributes.Count > 0)
                                {
                             

                                    foreach (var vals in mAsset.MultipleLog.OrderBy(x => x.TimeStamp).ToList())
                                    {
                                        var array = vals.CsvData.Split('~');

                                        if (!string.IsNullOrEmpty(Convert.ToString(array[0])))
                                        {
                                            var data = Convert.ToDateTime(Convert.ToString(array[0]));

                                            mAsset.DateList.Add(data.ToString("dd-mm-yyyy HH:mm:ss"));
                                        }
                                        graphService.PrepareGraphAttribute(mAsset.assetAttributes, allAssetAttributes, vals.CsvData);
                                    }

                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return PartialView(mAssetLister);
        }

        public ActionResult GetEquipmentRoom(int siteId)
        {
            List<Domain.Asset> equipmentRooms = new List<Domain.Asset>();
            Domain.AssetLister mAssetLister = new AssetLister();
            mAssetLister.SearchCriteria.SiteId = siteId;
            try
            {
                mAssetLister.Pager.Take = -1;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssetLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GetEquipmentRoom"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetLister = JsonConvert.DeserializeObject<AssetLister>(jsonString);
                        if (mAssetLister != null && mAssetLister.mAssets != null && mAssetLister.mAssets.Count > 0)
                        {
                            foreach (var item in mAssetLister.mAssets)
                            {

                                if (item.siteAttributeDatasList != null && item.siteAttributeDatasList.Count > 0)
                                {
                                    foreach (var siteAttribute in item.siteAttributeDatasList)
                                    {
                                        equipmentRooms.Add(new Domain.Asset() { Name = siteAttribute });
                                    }
                                }

                                //if (item.mAssetInfoDataloggers != null && item.mAssetInfoDataloggers.Count > 0)
                                //{
                                //    foreach (var dl in item.mAssetInfoDataloggers)
                                //    {
                                //        equipmentRooms.Add(new Domain.Asset() { Name = dl.DataloggerAttribute });

                                //    }
                                //}
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {

            }
            return Json(equipmentRooms, JsonRequestBehavior.AllowGet);
        }
    }
}