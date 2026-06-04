using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    [E7FRSAdvance.Areas.FRS25.Filter.Authenticate]
    public class TelemetryController : Controller
    {
        // GET: FRS25/Telemetry
        private readonly ISiteService _siteService;
        private readonly IZoneService _zoneService;
        private readonly IDivisionService _divisionService;
        private readonly IAssetTypeService _assetTypeService;
        private readonly IAssetService _assetService;
        private readonly ISectionService _sectionService;
        private readonly IFRSAlertService _frsAlertService;
        private readonly IAssetAttributeService _assetAttributeService;
        private readonly IGraphService _graphService;

        public TelemetryController(ISiteService siteService, IZoneService zoneService, IDivisionService divisionService, IAssetTypeService assetTypeService, IAssetService assetService, ISectionService sectionService, IFRSAlertService frsAlertService, IAssetAttributeService assetAttributeService, IGraphService graphService)
        {
            _siteService = siteService;
            _zoneService = zoneService;
            _divisionService = divisionService;
            _assetTypeService = assetTypeService;
            _assetService = assetService;
            _sectionService = sectionService;
            _frsAlertService = frsAlertService;
            _assetAttributeService = assetAttributeService;
            _graphService = graphService;
        }

        public ActionResult Index()
        {
            ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
            ViewBag.Zones = new SelectList(_zoneService.GetAllZones(), "Id", "Name");
            ViewBag.Divisions = new SelectList(_divisionService.GetAllDivisions(), "Id", "Name");
            ViewBag.AssetTypes = new SelectList(GetFRSAssetType(), "Id", "Name");
            return View();
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

        public ActionResult GetAssestBy(int siteId, int assetTypeId)
        {
            return Json(_assetService.GetAssestBy(siteId, assetTypeId), JsonRequestBehavior.AllowGet);
        }

        public ActionResult _Table(AssetLister mAssetLister)
        {
            mAssetLister.Pager.Take = -1;// mAssetLister.Pager.PageSize;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    mAssetLister.SearchCriteria.CreatedBy = ClsHttpContent.LoginUser.Id;
                    mAssetLister.SearchCriteria.IsMobileView = true;
                    if (string.IsNullOrEmpty(mAssetLister.SearchCriteria.StartDate))
                        mAssetLister.SearchCriteria.StartDate = DateTime.Now.ToShortDateString();

                    if (string.IsNullOrEmpty(mAssetLister.SearchCriteria.EndDate))
                        mAssetLister.SearchCriteria.EndDate = DateTime.Now.ToShortDateString();


                    var jsonStr = JsonConvert.SerializeObject(mAssetLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GetAllSiteDetailsBySiteId"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetLister = JsonConvert.DeserializeObject<AssetLister>(jsonString);
                        if (mAssetLister != null && mAssetLister.mAssets != null && mAssetLister.mAssets.Count > 0)
                        {
                            mAssetLister.mAssets = mAssetLister.mAssets.OrderBy(x => x.Sequence).ToList();
                        }
                        mAssetLister.Pager.PageSize = -1;



                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView(mAssetLister);
        }

        [HttpPost]
        public JsonResult GetReportData(Asset asset)
        {
            List<Asset> mAssets = new List<Asset>();
            Asset mAsset = new Asset();
            if (asset.IValue == "Today")
            {
                asset.StartDate = DateTime.Now.ToShortDateString();
                asset.EndDate = DateTime.Now.ToShortDateString();
            }
            if (asset.IValue == "Yesterday")
            {
                asset.StartDate = DateTime.Now.AddDays(-1).ToShortDateString();
                asset.EndDate = DateTime.Now.AddDays(-1).ToShortDateString();
            }
            if (asset.IValue == "Last 7 Day")
            {
                asset.StartDate = DateTime.Now.AddDays(-7).ToShortDateString();
                asset.EndDate = DateTime.Now.ToShortDateString();
            }
            if (asset.IValue == "Last 30 Day")
            {
                asset.StartDate = DateTime.Now.AddDays(-30).ToShortDateString();
                asset.EndDate = DateTime.Now.ToShortDateString();
            }
            if (asset.IValue == "This Month")
            {
                int day = DateTime.Now.Day * -1;
                asset.StartDate = DateTime.Now.AddDays(day).AddDays(1).ToShortDateString();
                asset.EndDate = DateTime.Now.ToShortDateString();
            }
            if (asset.IValue == "Last Month")
            {
                int day = DateTime.Now.AddMonths(-1).Day * -1;
                asset.StartDate = DateTime.Now.AddMonths(-1).AddDays(day).AddDays(1).ToShortDateString();
                asset.EndDate = DateTime.Now.AddMonths(-1).AddDays(day).AddDays(30).ToShortDateString();
            }
            try
            {
                DateTime startDate = Convert.ToDateTime(asset.StartDate);
                DateTime endDate = Convert.ToDateTime(asset.EndDate);
                var dayLeft = endDate.Subtract(startDate).TotalDays;
                asset.SortDirection = "Graph";
                asset.IsGraphLoad = true;
                asset.IsPointMachineGraph = true;
                mAssets.Add(asset);
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssets);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GenerateReport"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAsset = JsonConvert.DeserializeObject<Asset>(jsonString);
                        var sessionData = mAsset;
                        List<AssetAttribute> assetAttributes = new List<AssetAttribute>();
                        var allAssetAttributes = _assetAttributeService.GetAssetAttributesBy(mAsset.AssetTypeId);
                        if (mAsset.assetAttributes != null && mAsset.assetAttributes.Count > 0)
                        {
                            foreach (var item in mAsset.assetAttributes)
                            {
                                if (item.Title == "Last Update Relay End( sec ago)" ||
                                    item.Title == "Last Update Feed End( sec ago)" || item.Title == "Zero offset" || item.Title == "Last Updated"
                                    || item.Title == "Last Access (Sec)" || item.Title == "Last Access" || item.Title == "A10 id"
                                    )
                                {
                                    //mAsset.assetAttributes.Remove(item);
                                }
                                else
                                {
                                    assetAttributes.Add(item);
                                }
                            }
                            mAsset.assetAttributes = assetAttributes;

                            if (mAsset.assetAttributes.Select(x => x.AssetTypeName).FirstOrDefault().ToLower() == "TRACK".ToLower())
                            {
                                var leakage = new Domain.AssetAttribute();
                                leakage.AssetName = mAsset.assetAttributes.Select(x => x.AssetName).FirstOrDefault();
                                leakage.Title = "Leakage";
                                mAsset.assetAttributes.Add(leakage);
                                allAssetAttributes.Add(leakage);
                            }
                            if (dayLeft >= 1)
                            {

                                for (DateTime dateTime = startDate; dateTime <= endDate; dateTime += TimeSpan.FromDays(1))
                                {
                                    mAsset.DateList.Add(dateTime.ToString("MM/dd/yyyy"));
                                    var date = dateTime.ToString("dd MMM yyyy");
                                    foreach (var vals in mAsset.siteAttributeDatasList.Where(x => x.Contains(date)).OrderBy(x => x).ToList())
                                    {
                                        var array = vals.Split('~');

                                        if (!string.IsNullOrEmpty(Convert.ToString(array[0])))
                                        {
                                            var data = Convert.ToDateTime(Convert.ToString(array[0]));

                                        }
                                        _graphService.PrepareWithDataArrayGraphAttribute(mAsset.assetAttributes, allAssetAttributes, vals);

                                    }
                                    foreach (var atrv in mAsset.assetAttributes)
                                    {
                                        decimal value = 0;
                                        foreach (var item in atrv.DataArray)
                                        {

                                            if (!string.IsNullOrEmpty(item))
                                            {
                                                value += Convert.ToDecimal(item);
                                            }
                                            else
                                            {
                                                value += 0;
                                            }
                                        }
                                        if (atrv.DataArray.Count > 0)
                                            value = value / atrv.DataArray.Count;

                                        atrv.Data.Add(Convert.ToString(value.ToString("0.##")));
                                        atrv.DataArray = new List<string>();
                                    }
                                }
                            }
                            else
                            {
                                foreach (var vals in mAsset.siteAttributeDatasList.OrderBy(x => x).ToList())
                                {
                                    var array = vals.Split('~');

                                    if (!string.IsNullOrEmpty(Convert.ToString(array[0])))
                                    {
                                        var data = Convert.ToDateTime(Convert.ToString(array[0]));

                                        mAsset.DateList.Add(data.ToString("HH:mm"));
                                    }
                                    _graphService.PrepareGraphAttribute(mAsset.assetAttributes, allAssetAttributes, vals);
                                }
                            }

                        }
                    }
                    else
                    {
                        ViewBag.Error = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }
            mAsset.DateList = mAsset.DateList.OrderBy(x => x).ToList();
            mAsset.MultipleLog = new List<MultipleLog>();
            mAsset.siteAttributeDatasList = null;
            return Json(mAsset, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public JsonResult _Graph(Asset asset)
        {
            List<Asset> mAssets = new List<Asset>();
            Asset mAsset = new Asset();
            asset.StartDate = DateTime.Now.ToShortDateString();
            asset.EndDate = DateTime.Now.ToShortDateString();
            try
            {
                DateTime startDate = Convert.ToDateTime(asset.StartDate);
                DateTime endDate = Convert.ToDateTime(asset.EndDate);
                var dayLeft = endDate.Subtract(startDate).TotalDays;
                asset.SortDirection = "Graph";
                asset.IsGraphLoad = true;
                asset.IsPointMachineGraph = true;
                mAssets.Add(asset);
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssets);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GenerateReport"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAsset = JsonConvert.DeserializeObject<Asset>(jsonString);

                        List<AssetAttribute> assetAttributes = new List<AssetAttribute>();
                        var allAssetAttributes = _assetAttributeService.GetAssetAttributesBy(mAsset.AssetTypeId);
                        if (mAsset.assetAttributes != null && mAsset.assetAttributes.Count > 0)
                        {
                            foreach (var item in mAsset.assetAttributes)
                            {
                                if (item.Title == "Last Update Relay End( sec ago)" ||
                                    item.Title == "Last Update Feed End( sec ago)" || item.Title == "Zero offset" || item.Title == "Last Updated"
                                    || item.Title == "Last Access (Sec)" || item.Title == "Last Access" || item.Title == "A10 id"
                                    )
                                {
                                    //mAsset.assetAttributes.Remove(item);
                                }
                                else
                                {
                                    assetAttributes.Add(item);
                                }
                            }
                            mAsset.assetAttributes = assetAttributes;

                            if (mAsset.assetAttributes.Select(x => x.AssetTypeName).FirstOrDefault().ToLower() == "TRACK".ToLower())
                            {
                                var leakage = new Domain.AssetAttribute();
                                leakage.AssetName = mAsset.assetAttributes.Select(x => x.AssetName).FirstOrDefault();
                                leakage.Title = "Leakage";
                                mAsset.assetAttributes.Add(leakage);
                                allAssetAttributes.Add(leakage);
                            }
                            if (dayLeft >= 1)
                            {

                                for (DateTime dateTime = startDate; dateTime <= endDate; dateTime += TimeSpan.FromDays(1))
                                {
                                    mAsset.DateList.Add(dateTime.ToString("MM/dd/yyyy"));
                                    var date = dateTime.ToString("dd MMM yyyy");
                                    foreach (var vals in mAsset.siteAttributeDatasList.Where(x => x.Contains(date)).OrderBy(x => x).ToList())
                                    {
                                        var array = vals.Split('~');

                                        if (!string.IsNullOrEmpty(Convert.ToString(array[0])))
                                        {
                                            var data = Convert.ToDateTime(Convert.ToString(array[0]));

                                        }
                                        _graphService.PrepareWithDataArrayGraphAttribute(mAsset.assetAttributes, allAssetAttributes, vals);

                                    }
                                    foreach (var atrv in mAsset.assetAttributes)
                                    {
                                        decimal value = 0;
                                        foreach (var item in atrv.DataArray)
                                        {

                                            if (!string.IsNullOrEmpty(item))
                                            {
                                                value += Convert.ToDecimal(item);
                                            }
                                            else
                                            {
                                                value += 0;
                                            }
                                        }
                                        if (atrv.DataArray.Count > 0)
                                            value = value / atrv.DataArray.Count;

                                        atrv.Data.Add(Convert.ToString(value.ToString("0.##")));
                                        atrv.DataArray = new List<string>();
                                    }
                                }
                            }
                            else
                            {
                                foreach (var vals in mAsset.siteAttributeDatasList.OrderBy(x => x).ToList())
                                {
                                    var array = vals.Split('~');

                                    if (!string.IsNullOrEmpty(Convert.ToString(array[0])))
                                    {
                                        var data = Convert.ToDateTime(Convert.ToString(array[0]));

                                        mAsset.DateList.Add(data.ToString("HH:mm"));
                                    }
                                    _graphService.PrepareGraphAttribute(mAsset.assetAttributes, allAssetAttributes, vals);
                                }
                            }

                        }
                    }
                    else
                    {
                        ViewBag.Error = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }
            mAsset.DateList = mAsset.DateList.OrderBy(x => x).ToList();
            mAsset.MultipleLog = new List<MultipleLog>();
            mAsset.siteAttributeDatasList = null;
            return Json(mAsset, JsonRequestBehavior.AllowGet);
        }

        public ActionResult _Circuit(int assetTypeId, int assetId)
        {
            Domain.AdvanceAssetTypeCircuitDiagram mAssetTypeCircuitDiagram = new Domain.AdvanceAssetTypeCircuitDiagram();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("AssetType/GetAdvanceAssetTypeCircuitDiagram/{0}", assetTypeId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssetTypeCircuitDiagram = JsonConvert.DeserializeObject<AdvanceAssetTypeCircuitDiagram>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            finally
            {
                var mAsset = _assetService.GetSignalAspectData(assetId);
                if (mAsset != null)
                {
                    var mAssetAttributes = _assetAttributeService.GetAssetAttributes(mAsset.SiteId, assetId);
                    if (mAssetAttributes != null && mAssetAttributes.Count > 0)
                    {
                        mAsset.assetAttributes = mAssetAttributes;
                    }
                    var mAssetInfoDataloggers = GetAssetInfoDatalogger(assetId);
                    if (mAssetInfoDataloggers != null && mAssetInfoDataloggers.Count > 0)
                    {
                        mAsset.mAssetInfoDataloggers = mAssetInfoDataloggers;
                    }
                    if (mAsset.mGlobalConfigs != null && mAsset.mGlobalConfigs.Count > 0)
                    {
                        var zeroOffsetAttr = mAsset.mGlobalConfigs.Where(x => x.AttributeName == "Zero offset").FirstOrDefault();
                        if (zeroOffsetAttr != null && zeroOffsetAttr.Value != null && zeroOffsetAttr.Value.Value > 0)
                            mAsset.ZeroOffsetValue = zeroOffsetAttr.Value.Value;
                    }


                    var site = _siteService.Get(mAsset.SiteId);
                    ViewBag.Site = site;
                    ViewBag.Asset = mAsset;
                }
                ViewBag.CardLine = GetCardLine(assetTypeId, assetId);
                var assetAttributes = _assetAttributeService.GetAllAssetAttributeBy(assetTypeId);
                if (assetAttributes != null && assetAttributes.Count > 0)
                {
                    ViewBag.AssetAttributes = assetAttributes.Where(x => x.IsDerived != null && x.IsDerived.Value);
                }
                ViewBag.MQTTDetail = GetMQTTDetailList().mQTTDetailWeb;
            }
            return PartialView(mAssetTypeCircuitDiagram);
        }

        public ActionResult _Yard(Domain.SearchCriteria searchCriteria)
        {
            List<CardLine> mCardLines = new List<CardLine>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("CardLine/assetTypeId/{0}/AssetId/{1}", searchCriteria.AssetTypeId, searchCriteria.AssetId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mCardLines = JsonConvert.DeserializeObject<List<CardLine>>(jsonString);

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

            return PartialView(mCardLines);
        }
        // In TelemetryController.cs


        //[HttpPost]
        //public ActionResult GetEventLog(int assetId)
        //{
        //    var mAsset = _assetService.Get(assetId);

        //    if (mAsset != null)
        //    {
        //        // Get FRS Alerts
        //        try
        //        {
        //            var alertLister = new Domain.FRSAlertLister
        //            {
        //                SearchCriteria = new Domain.FRSAlert
        //                {
        //                    AssetId = assetId,
        //                    SiteId = mAsset.SiteId,
        //                    FromDate = DateTime.Now.AddDays(-90),
        //                    ToDate = DateTime.Now
        //                },
        //                Pager = new Domain.Pager
        //                {
        //                    PageSize = 100,
        //                    CurrentPage = 1
        //                }
        //            };
        //            var alertResult = _frsAlertService.GetWithOutAcknowledgementAlert(alertLister);
        //            ViewBag.FRSAlerts = (alertResult != null && alertResult.mFRSAlerts != null)
        //                ? alertResult.mFRSAlerts
        //                : new List<Domain.FRSAlert>();
        //        }
        //        catch
        //        {
        //            ViewBag.FRSAlerts = new List<Domain.FRSAlert>();
        //        }

        //        // Get Point Machine Events (for Point Machine assets only)
        //        if (mAsset.AssetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.POINT_MACHINE)
        //        {
        //            string searchDate = DateTime.Now.ToString("d-M-yyyy");

        //            // Get Point Machine Events
        //            List<Domain.PointMachineEvent> pointEvents = new List<Domain.PointMachineEvent>();
        //            try
        //            {
        //                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
        //                {
        //                    var response = hcf.client.GetAsync(String.Format("PointMachineEvent/AssetId/{0}/SearchDate/{1}", assetId, searchDate)).Result;
        //                    if (response.StatusCode == System.Net.HttpStatusCode.OK)
        //                    {
        //                        string jsonString = response.Content.ReadAsStringAsync().Result;
        //                        pointEvents = JsonConvert.DeserializeObject<List<Domain.PointMachineEvent>>(jsonString);
        //                    }
        //                }
        //            }
        //            catch { }
        //            ViewBag.PointMachineEvent = pointEvents ?? new List<Domain.PointMachineEvent>();

        //            // Get Point Event Logs
        //            List<Domain.PointEventLog> pointLogs = new List<Domain.PointEventLog>();
        //            try
        //            {
        //                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
        //                {
        //                    var response = hcf.client.GetAsync(String.Format("PointMachineEvent/GetEventLog/AssetId/{0}/SearchDate/{1}", assetId, searchDate)).Result;
        //                    if (response.StatusCode == System.Net.HttpStatusCode.OK)
        //                    {
        //                        string jsonString = response.Content.ReadAsStringAsync().Result;
        //                        pointLogs = JsonConvert.DeserializeObject<List<Domain.PointEventLog>>(jsonString);
        //                    }
        //                }
        //            }
        //            catch { }
        //            ViewBag.PointEventLog = pointLogs ?? new List<Domain.PointEventLog>();
        //        }
        //        else
        //        {
        //            ViewBag.PointMachineEvent = new List<Domain.PointMachineEvent>();
        //            ViewBag.PointEventLog = new List<Domain.PointEventLog>();
        //        }

        //        // Get Asset Infos for Change Time tab
        //        List<Domain.AssetInfo> assetInfos = new List<Domain.AssetInfo>();
        //        try
        //        {
        //            using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
        //            {
        //                var response = hcf.client.GetAsync(String.Format("AssetInfo/AssetId/{0}", assetId)).Result;
        //                if (response.StatusCode == System.Net.HttpStatusCode.OK)
        //                {
        //                    string jsonString = response.Content.ReadAsStringAsync().Result;
        //                    assetInfos = JsonConvert.DeserializeObject<List<Domain.AssetInfo>>(jsonString);
        //                }
        //            }
        //        }
        //        catch { }
        //        ViewBag.AssetInfos = assetInfos ?? new List<Domain.AssetInfo>();

        //        // Get Change Time data
        //        List<Domain.DynamoTable> changeTime = new List<Domain.DynamoTable>();
        //        try
        //        {
        //            using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
        //            {
        //                var response = hcf.client.GetAsync(String.Format("SiteKeeping/GetChangeValue/SiteId/{0}", assetId)).Result;
        //                if (response.StatusCode == System.Net.HttpStatusCode.OK)
        //                {
        //                    string jsonString = response.Content.ReadAsStringAsync().Result;
        //                    changeTime = JsonConvert.DeserializeObject<List<Domain.DynamoTable>>(jsonString);
        //                }
        //            }
        //        }
        //        catch { }
        //        ViewBag.ChangeTimeAssets = changeTime ?? new List<Domain.DynamoTable>();

        //        // Get Datalogger Info
        //        List<Domain.AssetInfoDatalogger> dataloggerInfo = new List<Domain.AssetInfoDatalogger>();
        //        try
        //        {
        //            using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
        //            {
        //                var response = hcf.client.GetAsync(String.Format("AssetInfoDatalogger/AssetId/{0}", assetId)).Result;
        //                if (response.StatusCode == System.Net.HttpStatusCode.OK)
        //                {
        //                    string jsonString = response.Content.ReadAsStringAsync().Result;
        //                    dataloggerInfo = JsonConvert.DeserializeObject<List<Domain.AssetInfoDatalogger>>(jsonString);
        //                }
        //            }
        //        }
        //        catch { }
        //        ViewBag.AssetInfoDatalogger = dataloggerInfo ?? new List<Domain.AssetInfoDatalogger>();
        //        ViewBag.Datalogger = new List<Domain.Datalogger>();
        //        ViewBag.PeriodicDatalogger = null;
        //    }
        //    else
        //    {
        //        // Set empty defaults if asset not found
        //        ViewBag.FRSAlerts = new List<Domain.FRSAlert>();
        //        ViewBag.PointMachineEvent = new List<Domain.PointMachineEvent>();
        //        ViewBag.PointEventLog = new List<Domain.PointEventLog>();
        //        ViewBag.AssetInfos = new List<Domain.AssetInfo>();
        //        ViewBag.ChangeTimeAssets = new List<Domain.DynamoTable>();
        //        ViewBag.AssetInfoDatalogger = new List<Domain.AssetInfoDatalogger>();
        //        ViewBag.Datalogger = new List<Domain.Datalogger>();
        //        ViewBag.PeriodicDatalogger = null;
        //    }

        //    return PartialView("_EventLog", mAsset);
        //}
        [HttpPost]
        public ActionResult _PointMachineEvent(Domain.SearchCriteria searchCriteria)
        {
            List<Domain.PointMachineEvent> mPointMachineEvents = new List<Domain.PointMachineEvent>();

            if (!string.IsNullOrEmpty(searchCriteria.SearchDate))
            {
                var sdateTime = E7FRSAdvance.Utility.ExtensionMethod.ConvertToDateTimeFormat(searchCriteria.SearchDate, "d/M/yyyy");
                searchCriteria.SearchDate = sdateTime.ToString("d-M-yyyy");
            }
            else
            {
                searchCriteria.SearchDate = DateTime.Now.ToString("d-M-yyyy");
            }

            mPointMachineEvents = GetPointMachineEvent(searchCriteria.AssetId, searchCriteria.SearchDate);

            return PartialView(mPointMachineEvents);
        }

        [HttpPost]
        public ActionResult _EventDataLoggerGraph(Domain.SearchCriteria searchCriteria)
        {
            List<Domain.Datalogger> mDataloggers = new List<Domain.Datalogger>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(searchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync("DataloggerAsset/GetTempGraphDataByAsset", str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mDataloggers = JsonConvert.DeserializeObject<List<Domain.Datalogger>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }

            return PartialView(mDataloggers);
        }

        public ActionResult GetAssetTypeBySiteId(int siteId)
        {
            List<Domain.AssetType> mAssetTypes = new List<Domain.AssetType>();
            if (siteId > 0)
            {
                try
                {
                    using (var mHttpClientFactory = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = mHttpClientFactory.client.GetAsync(String.Format("AssetType/GetAllAssestType/{0}", siteId)).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            mAssetTypes = JsonConvert.DeserializeObject<List<Domain.AssetType>>(jsonString);
                        }
                        else
                        {
                            ViewBag.Error = "Internal server error.";
                        }
                    }
                }
                catch (Exception ex)
                {
                    ViewBag.Error = ex.Message.ToString();
                }
            }

            return Json(mAssetTypes, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetHistoryData(int assetId, string startDate, string endDate)
        {
            try
            {
                string baseUrl = ConfigurationManager.AppSettings["ProxyBaseUrl"];
                string apiUrl = string.Format("{0}/api/HistoryValue?AssetId={1}&StartDate={2}&EndDate={3}",
                    baseUrl, assetId, startDate, endDate);

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(60);
                    var response = client.GetAsync(apiUrl).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return Content(jsonString, "application/json");
                    }
                    else
                    {
                        return Json(new { error = "API returned status: " + response.StatusCode }, JsonRequestBehavior.AllowGet);
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        public ActionResult GetZeroOffsetValue(int assetId)
        {
            double zeroOffsetValue = 0;
            bool success = false;
            string errorMessage = null;

            try
            {
                // Reuse the same service call that _Circuit uses
                var mAsset = _assetService.GetSignalAspectData(assetId);

                if (mAsset != null && mAsset.mGlobalConfigs != null && mAsset.mGlobalConfigs.Count > 0)
                {
                    // Primary: match by AttributeName (same as _Circuit action)
                    var zeroOffsetAttr = mAsset.mGlobalConfigs
                        .Where(x => x.AttributeName == "Zero offset")
                        .FirstOrDefault();

                    // Fallback: match by AssetAttributeId = 30 (same as old JS parsing logic)
                    if (zeroOffsetAttr == null)
                    {
                        zeroOffsetAttr = mAsset.mGlobalConfigs
                            .Where(x => x.AssetAttributeId == 30)
                            .FirstOrDefault();
                    }

                    if (zeroOffsetAttr != null && zeroOffsetAttr.Value != null && zeroOffsetAttr.Value.Value > 0)
                    {
                        zeroOffsetValue = (double)zeroOffsetAttr.Value.Value;
                        success = true;
                    }
                    else
                    {
                        // Config row found but value was null/zero — still a valid response
                        errorMessage = "Zero offset config found but value is null or zero for assetId: " + assetId;
                        success = true; // not a server error, just no value configured
                    }
                }
                else
                {
                    errorMessage = "No mGlobalConfigs returned for assetId: " + assetId;
                }
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                success = false;
            }

            return Json(new
            {
                success = success,
                assetId = assetId,
                zeroOffset = zeroOffsetValue,   // the ONLY thing JS actually needs
                errorMessage = errorMessage
            }, JsonRequestBehavior.AllowGet);
        }
        public JsonResult GetAttributesByAssestId(int id)
        {
            List<AssetAttribute> mAssetAttributes = new List<AssetAttribute>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("AssetType/GetAttributesByAssestId/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssetAttributes = JsonConvert.DeserializeObject<List<AssetAttribute>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return Json(mAssetAttributes, JsonRequestBehavior.AllowGet);
        }
        public ActionResult GetUserAssetInfoDatalogger(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync("AssetInfo/GetUserAssetInfo/SiteId/" + siteId).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return Content(jsonString, "application/json");
                    }
                    else
                    {
                        return Json(new { error = "API returned status: " + response.StatusCode }, JsonRequestBehavior.AllowGet);
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        public ActionResult GetSipView(int siteId)
        {
            Domain.AdvancedSIPView mSipView = new Domain.AdvancedSIPView();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {

                    var response = hcf.client.GetAsync(String.Format("Site/GetAdvSipView/{0}", siteId)).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSipView = JsonConvert.DeserializeObject<Domain.AdvancedSIPView>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }
            if (mSipView == null)
            {
                mSipView = new Domain.AdvancedSIPView();
                mSipView.SiteId = siteId;
            }
            return Json(mSipView, JsonRequestBehavior.AllowGet);
        }


        [HttpGet]
        public JsonResult GetSafetyThresholds()
        {
            var thresholds = new Dictionary<int, SafetyThreshold>();

            // ==================== TRACK CIRCUIT ====================
            thresholds[1] = new SafetyThreshold { MaxSafe = 500, MinSafe = 250, MinFail = 240 };        // If mA
            thresholds[2] = new SafetyThreshold { MaxSafe = 400, MinSafe = 210, MinFail = 200 };        // Ir mA
            thresholds[3] = new SafetyThreshold { MaxSafe = 4.2, MinSafe = 2.1, MinFail = null };       // Vr
            thresholds[4] = new SafetyThreshold { MaxSafe = null, MinSafe = 0.6, MinFail = null };       // Choke V
            thresholds[5] = new SafetyThreshold { MaxSafe = null, MinSafe = 50, MinFail = null };       // Charger mA
            thresholds[6] = new SafetyThreshold { MaxSafe = null, MinSafe = 20, MinFail = 18 };         // TPR V
            thresholds[249] = new SafetyThreshold { MaxSafe = null, MinSafe = 2.5, MinFail = 1.5 };        // Vf
            thresholds[344] = new SafetyThreshold { MaxSafe = null, MinSafe = 90, MinFail = null };       // Charger V
            thresholds[569] = new SafetyThreshold { MaxSafe = null, MinSafe = 4.5, MinFail = 4 };          // Charger OP V
            thresholds[570] = new SafetyThreshold { MaxSafe = null, MinSafe = 23, MinFail = 21 };         // TPR V (Loc)
            thresholds[571] = new SafetyThreshold { MaxSafe = 4.2, MinSafe = 2.1, MinFail = null };       // TR V (Relay)
            thresholds[585] = new SafetyThreshold { MaxSafe = 1800, MinSafe = 100, MinFail = null };       // ITC BATT CHARG
            thresholds[586] = new SafetyThreshold { MaxSafe = 4.7, MinSafe = 0.5, MinFail = null };       // VTC VAR RES
            thresholds[587] = new SafetyThreshold { MaxSafe = 7.2, MinSafe = 1.2, MinFail = null };       // RTC CH FEED END
            thresholds[588] = new SafetyThreshold { MaxSafe = 18.8, MinSafe = 1, MinFail = null };       // RTC VAR RES
            thresholds[589] = new SafetyThreshold { MaxSafe = 17, MinSafe = 0.75, MinFail = null };       // RTC CH RELAY END
            thresholds[590] = new SafetyThreshold { MaxSafe = 6.67, MinSafe = 0.75, MinFail = null };       // RRAIL
            thresholds[591] = new SafetyThreshold { MaxSafe = 3.4, MinSafe = 0.3, MinFail = null };       // VTC CH RELAY END

            // ==================== SIGNAL ====================
            thresholds[9] = new SafetyThreshold { MaxSafe = null, MinSafe = 90, MinFail = 82 };         // RG V
            thresholds[10] = new SafetyThreshold { MaxSafe = 150, MinSafe = 110, MinFail = 108 };        // RG mA
            thresholds[11] = new SafetyThreshold { MaxSafe = null, MinSafe = 90, MinFail = 82 };         // DG V
            thresholds[12] = new SafetyThreshold { MaxSafe = 150, MinSafe = 110, MinFail = 108 };        // DG mA
            thresholds[13] = new SafetyThreshold { MaxSafe = null, MinSafe = 90, MinFail = 82 };         // HG V
            thresholds[14] = new SafetyThreshold { MaxSafe = 150, MinSafe = 110, MinFail = 108 };        // HG mA
            thresholds[15] = new SafetyThreshold { MaxSafe = null, MinSafe = 90, MinFail = 82 };         // HHG V
            thresholds[16] = new SafetyThreshold { MaxSafe = 150, MinSafe = 110, MinFail = 108 };        // HHG mA
            thresholds[51] = new SafetyThreshold { MaxSafe = null, MinSafe = 93, MinFail = 87 };         // On Aspect V
            thresholds[52] = new SafetyThreshold { MaxSafe = 62, MinSafe = 42, MinFail = 40 };         // On Aspect mA
            thresholds[53] = new SafetyThreshold { MaxSafe = null, MinSafe = 93, MinFail = 87 };         // Off Aspect V
            thresholds[54] = new SafetyThreshold { MaxSafe = 62, MinSafe = 42, MinFail = 40 };         // Off Aspect mA
            thresholds[326] = new SafetyThreshold { MaxSafe = 131, MinSafe = 110, MinFail = 90 };         // BUG mA
            thresholds[327] = new SafetyThreshold { MaxSafe = null, MinSafe = 21, MinFail = 18 };         // HHPR
            thresholds[328] = new SafetyThreshold { MaxSafe = null, MinSafe = 21, MinFail = 18 };         // DPR
            thresholds[329] = new SafetyThreshold { MaxSafe = null, MinSafe = 21, MinFail = 18 };         // HPR
            thresholds[330] = new SafetyThreshold { MaxSafe = null, MinSafe = 21, MinFail = 18 };         // BUPR
            thresholds[332] = new SafetyThreshold { MaxSafe = 131, MinSafe = 110, MinFail = 90 };         // CUG mA
            thresholds[333] = new SafetyThreshold { MaxSafe = null, MinSafe = 21, MinFail = 18 };         // CUPR
            thresholds[337] = new SafetyThreshold { MaxSafe = 62, MinSafe = 42, MinFail = 40 };         // PILOT mA
            thresholds[356] = new SafetyThreshold { MaxSafe = 131, MinSafe = 110, MinFail = 90 };         // AUG mA
            thresholds[357] = new SafetyThreshold { MaxSafe = 131, MinSafe = 110, MinFail = 90 };         // DUG mA
            thresholds[358] = new SafetyThreshold { MaxSafe = 131, MinSafe = 110, MinFail = 90 };         // EUG mA
            thresholds[497] = new SafetyThreshold { MaxSafe = null, MinSafe = 93, MinFail = 87 };         // AUG V
            thresholds[499] = new SafetyThreshold { MaxSafe = null, MinSafe = 93, MinFail = 87 };         // PILOT V
            thresholds[573] = new SafetyThreshold { MaxSafe = null, MinSafe = 93, MinFail = 87 };         // BUG V
            thresholds[574] = new SafetyThreshold { MaxSafe = null, MinSafe = 93, MinFail = 87 };         // CUG V
            thresholds[601] = new SafetyThreshold { MaxSafe = null, MinSafe = 21, MinFail = 18 };         // AUPR
            thresholds[602] = new SafetyThreshold { MaxSafe = null, MinSafe = 93, MinFail = 87 };         // DUG V
            thresholds[603] = new SafetyThreshold { MaxSafe = null, MinSafe = 21, MinFail = 18 };         // DUPR
            thresholds[608] = new SafetyThreshold { MaxSafe = null, MinSafe = 21, MinFail = 18 };         // EUPR
            thresholds[610] = new SafetyThreshold { MaxSafe = 165, MinSafe = 110, MinFail = 90 };         // Co_Hg mA
            thresholds[611] = new SafetyThreshold { MaxSafe = null, MinSafe = 93, MinFail = 87 };         // Co_Hg V
            thresholds[612] = new SafetyThreshold { MaxSafe = null, MinSafe = 21, MinFail = 18 };         // Co_HPR
            thresholds[613] = new SafetyThreshold { MaxSafe = null, MinSafe = 93, MinFail = 87 };         // EUG V

            // ==================== POINT MACHINE ====================
            thresholds[25] = new SafetyThreshold { MaxSafe = null, MinSafe = 20, MinFail = 18 };         // A End - NWKR
            thresholds[26] = new SafetyThreshold { MaxSafe = null, MinSafe = 20, MinFail = 18 };         // A End - RWKR
            thresholds[27] = new SafetyThreshold { MaxSafe = null, MinSafe = 20, MinFail = 18 };         // B End - NWKR
            thresholds[28] = new SafetyThreshold { MaxSafe = null, MinSafe = 20, MinFail = 18 };         // B End - RWKR
            thresholds[212] = new SafetyThreshold { MaxSafe = null, MinSafe = 90, MinFail = 80 };         // B End - NW-V
            thresholds[213] = new SafetyThreshold { MaxSafe = 4.5, MinSafe = 1.2, MinFail = 0.8 };        // B End - NW-C
            thresholds[214] = new SafetyThreshold { MaxSafe = null, MinSafe = 90, MinFail = 80 };         // B End - RW-V
            thresholds[215] = new SafetyThreshold { MaxSafe = 4.5, MinSafe = 1.2, MinFail = 0.8 };        // B End - RW-C
            thresholds[216] = new SafetyThreshold { MaxSafe = null, MinSafe = 90, MinFail = 80 };         // A End - NW-V
            thresholds[217] = new SafetyThreshold { MaxSafe = 4.5, MinSafe = 1.2, MinFail = 0.8 };        // A End - NW-C
            thresholds[218] = new SafetyThreshold { MaxSafe = null, MinSafe = 90, MinFail = 80 };         // A End - RW-V
            thresholds[219] = new SafetyThreshold { MaxSafe = 4.5, MinSafe = 1.2, MinFail = 0.8 };        // A End - RW-C
            thresholds[576] = new SafetyThreshold { MaxSafe = null, MinSafe = 23, MinFail = 21.6 };       // A End - NWKR (Loc)
            thresholds[577] = new SafetyThreshold { MaxSafe = null, MinSafe = 23, MinFail = 21.6 };       // A End - RWKR (Loc)
            thresholds[578] = new SafetyThreshold { MaxSafe = null, MinSafe = 23, MinFail = 21.6 };       // B End - NWKR (Loc)
            thresholds[579] = new SafetyThreshold { MaxSafe = null, MinSafe = 23, MinFail = 21.6 };       // B End - RWKR (Loc)
            thresholds[581] = new SafetyThreshold { MaxSafe = 9, MinSafe = 3, MinFail = null };       // A End - NW-T
            thresholds[582] = new SafetyThreshold { MaxSafe = 9, MinSafe = 3, MinFail = null };       // A End - RW-T
            thresholds[583] = new SafetyThreshold { MaxSafe = 9, MinSafe = 3, MinFail = null };       // B End - NW-T
            thresholds[584] = new SafetyThreshold { MaxSafe = 9, MinSafe = 3, MinFail = null };       // B End - RW-T

            return Json(thresholds, JsonRequestBehavior.AllowGet);
        }
        private List<CardLine> GetCardLineData(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync($"CardLine/SiteId/{siteId}").Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return JsonConvert.DeserializeObject<List<CardLine>>(jsonString);
                    }
                    else
                    {
                        // Log the error (you can use your logging framework)
                        System.Diagnostics.Debug.WriteLine($"CardLine API error: {response.StatusCode} - {jsonString}");
                        return new List<CardLine>();
                    }
                }
            }
            catch (Exception ex)
            {
                // Log the exception
                System.Diagnostics.Debug.WriteLine($"GetCardLineData exception: {ex.Message}");
                return new List<CardLine>();
            }
        }
        [HttpGet]
        public ActionResult GetCardLineConfig(int siteId)
        {
            try
            {
                List<CardLine> cardLines = GetCardLineData(siteId);
                return Json(cardLines, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }
        public ActionResult GetLiveValue(int assetId)
        {
            try
            {
                string baseUrl = ConfigurationManager.AppSettings["ProxyBaseUrl"];
                string apiUrl = string.Format("{0}/api/LiveValue/{1}", baseUrl, assetId);
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(15);
                    var response = client.GetAsync(apiUrl).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return Content(jsonString, "application/json");
                    }
                    else
                    {
                        return Json(new { error = "API returned status: " + response.StatusCode }, JsonRequestBehavior.AllowGet);
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        public JsonResult GetAssetInfoListData(int assetId)
        {
            var data = GetAssetInfoList(assetId);
            return Json(data, JsonRequestBehavior.AllowGet);
        }

        
        #region Private Helper Methods for Event Log

        private List<Domain.PointMachineEvent> GetPointMachineEvent(int assetId, string searchDate)
        {
            List<Domain.PointMachineEvent> mPointMachineEvents = new List<Domain.PointMachineEvent>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    string url = String.Format("PointMachineEvent/AssetId/{0}/SearchDate/{1}", assetId, searchDate);
                    var response = hcf.client.GetAsync(url).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mPointMachineEvents = JsonConvert.DeserializeObject<List<Domain.PointMachineEvent>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return mPointMachineEvents ?? new List<Domain.PointMachineEvent>();
        }

        private List<Domain.PointEventLog> GetPointLog(int assetId, string searchDate)
        {
            List<Domain.PointEventLog> strLogs = new List<Domain.PointEventLog>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    string url = String.Format("PointMachineEvent/GetEventLog/AssetId/{0}/SearchDate/{1}", assetId, searchDate);
                    var response = hcf.client.GetAsync(url).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        strLogs = JsonConvert.DeserializeObject<List<Domain.PointEventLog>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return strLogs ?? new List<Domain.PointEventLog>();
        }

        private List<Domain.AssetInfo> GetAssetInfoList(int assetId)
        {
            var mAssetInfos = new List<Domain.AssetInfo>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    string url = String.Format("AssetInfo/AssetId/{0}", assetId);
                    var response = hcf.client.GetAsync(url).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssetInfos = JsonConvert.DeserializeObject<List<Domain.AssetInfo>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return mAssetInfos ?? new List<Domain.AssetInfo>();
        }

        private List<Domain.DynamoTable> GetChangeTime(int assetId)
        {
            List<Domain.DynamoTable> mDynamoTables = new List<Domain.DynamoTable>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    string url = String.Format("SiteKeeping/GetChangeValue/SiteId/{0}", assetId);
                    var response = hcf.client.GetAsync(url).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mDynamoTables = JsonConvert.DeserializeObject<List<Domain.DynamoTable>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return mDynamoTables ?? new List<Domain.DynamoTable>();
        }


        #endregion

        #region Detail View

        public ActionResult Detail()
        {
            ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
            ViewBag.Zones = new SelectList(_zoneService.GetAllZones(), "Id", "Name");
            ViewBag.Divisions = new SelectList(_divisionService.GetAllDivisions(), "Id", "Name");
            ViewBag.AssetTypes = new SelectList(GetFRSAssetType(), "Id", "Name");

            return View();
        }

        public ActionResult _DetailList(Asset asset)
        {
            List<Asset> mAssets = new List<Asset>();
            Asset mAsset = new Asset();
            try
            {
                DateTime startDate = Convert.ToDateTime(asset.StartDate);
                DateTime endDate = Convert.ToDateTime(asset.EndDate);
                var dayLeft = endDate.Subtract(startDate).TotalDays;
                asset.SortDirection = "Graph";
                asset.IsGraphLoad = true;
                mAssets.Add(asset);
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssets);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GenerateReport"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAsset = JsonConvert.DeserializeObject<Asset>(jsonString);
                        var sessionData = mAsset;
                        List<AssetAttribute> assetAttributes = new List<AssetAttribute>();
                        var allAssetAttributes = _assetAttributeService.GetAssetAttributesBy(mAsset.AssetTypeId);
                        if (mAsset.assetAttributes != null && mAsset.assetAttributes.Count > 0)
                        {
                            foreach (var item in mAsset.assetAttributes)
                            {
                                if (item.Title == "Last Update Relay End( sec ago)" ||
                                    item.Title == "Last Update Feed End( sec ago)" || item.Title == "Zero offset" || item.Title == "Last Updated"
                                    || item.Title == "Last Access (Sec)" || item.Title == "Last Access" || item.Title == "A10 id"
                                    )
                                {
                                    //mAsset.assetAttributes.Remove(item);
                                }
                                else
                                {
                                    assetAttributes.Add(item);
                                }
                            }
                            mAsset.assetAttributes = assetAttributes;

                            if (mAsset.assetAttributes.Select(x => x.AssetTypeName).FirstOrDefault().ToLower() == "TRACK".ToLower())
                            {
                                var leakage = new Domain.AssetAttribute();
                                leakage.AssetName = mAsset.assetAttributes.Select(x => x.AssetName).FirstOrDefault();
                                leakage.Title = "Leakage";
                                mAsset.assetAttributes.Add(leakage);
                                allAssetAttributes.Add(leakage);
                            }
                            if (dayLeft >= 1)
                            {

                                for (DateTime dateTime = startDate; dateTime <= endDate; dateTime += TimeSpan.FromDays(1))
                                {
                                    //mAsset.DateList.Add(dateTime.ToString("MM/dd/yyyy"));
                                    //var date = dateTime.ToString("dd MMM yyyy");
                                    foreach (var vals in mAsset.siteAttributeDatasList.OrderBy(x => x).ToList())
                                    {
                                        var array = vals.Split('~');

                                        if (!string.IsNullOrEmpty(Convert.ToString(array[0])))
                                        {
                                            var data = Convert.ToDateTime(Convert.ToString(array[0]));

                                            mAsset.DateList.Add(data.ToString("dd-MM-yyyy HH:mm"));
                                        }
                                        _graphService.PrepareGraphAttribute(mAsset.assetAttributes, allAssetAttributes, vals);
                                    }
                                }
                            }
                            else
                            {
                                foreach (var vals in mAsset.siteAttributeDatasList.OrderBy(x => x).ToList())
                                {
                                    var array = vals.Split('~');

                                    if (!string.IsNullOrEmpty(Convert.ToString(array[0])))
                                    {
                                        var data = Convert.ToDateTime(Convert.ToString(array[0]));

                                        mAsset.DateList.Add(data.ToString("HH:mm"));
                                    }
                                    _graphService.PrepareGraphAttribute(mAsset.assetAttributes, allAssetAttributes, vals);
                                }
                            }

                        }
                    }
                    else
                    {
                        ViewBag.Error = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }
            mAsset.DateList = mAsset.DateList.OrderBy(x => x).ToList();
            mAsset.MultipleLog = new List<MultipleLog>();
            mAsset.siteAttributeDatasList = null;

            return View(mAsset);
        }

        public ActionResult GetProxyBaseUrl()
        {
            string baseUrl = ConfigurationManager.AppSettings["ProxyBaseUrl"];
            string webSocketUrl = ConfigurationManager.AppSettings["WebSocketBaseUrl"];
            return Json(new { ProxyBaseUrl = baseUrl, WebSocketUrl = webSocketUrl }, JsonRequestBehavior.AllowGet);
        }

        #endregion

        #region Private Method

        private List<Domain.AssetInfoDatalogger> GetAssetInfoDatalogger(int assetId)
        {
            List<Domain.AssetInfoDatalogger> mAssetInfoDataloggers = new List<Domain.AssetInfoDatalogger>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("AssetInfoDatalogger/AssetId/{0}", assetId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssetInfoDataloggers = JsonConvert.DeserializeObject<List<Domain.AssetInfoDatalogger>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return mAssetInfoDataloggers;
        }

        private List<CardLine> GetCardLine(int assetTypeId, int assetId)
        {
            List<CardLine> mCardLines = new List<CardLine>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CardLine/AssetTypeId/{assetTypeId}/AssetId/{assetId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mCardLines = JsonConvert.DeserializeObject<List<CardLine>>(jsonString);
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

            return mCardLines;

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

        private MQTTDetailList GetMQTTDetailList()
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
        public ActionResult GetAssetAttributes()
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync("AssetAttribute/GetAll").Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return Content(jsonString, "application/json");
                    }
                    else
                    {
                        return Json(new { error = "API returned status: " + response.StatusCode }, JsonRequestBehavior.AllowGet);
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        public ActionResult GetEventLog(int assetId)
        {
            var mAsset = _assetService.Get(assetId);
            Domain.FRSAlertLister fRSAlertLister = new FRSAlertLister();
            fRSAlertLister.SearchCriteria.AssetId = assetId;
            //fRSAlertLister.SearchCriteria.AlertStatus = (int)E7FRSAdvance.Utility.Utility.FRSAlertStatus.Active;
            fRSAlertLister = _frsAlertService.GetWithOutAcknowledgementAlert(fRSAlertLister);
            if (fRSAlertLister != null && fRSAlertLister.mFRSAlerts != null && fRSAlertLister.mFRSAlerts.Count > 0)
            {
                ViewBag.FRSAlerts = fRSAlertLister.mFRSAlerts;
            }

            if (mAsset != null && mAsset.Id > 0 && mAsset.AssetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.POINT_MACHINE)
            {
                var mPointMachineEvents = GetPointMachineEvent(assetId, DateTime.Now.ToString("d-M-yyyy"));
                if (mPointMachineEvents != null && mPointMachineEvents.Count > 0)
                {
                    ViewBag.PointMachineEvent = mPointMachineEvents;
                }
            }

            if (mAsset != null && mAsset.Id > 0 && mAsset.IsDatalogger)
            {
                var mSearchCriteria = new SearchCriteria();
                mSearchCriteria.SearchDate = DateTime.Now.ToString("d-M-yyyy");
                mSearchCriteria.SiteId = mAsset.SiteId;
                mSearchCriteria.AssetId = mAsset.Id;
                var mDatalogger = DataLoggerEvent(mSearchCriteria);
                if (mDatalogger != null && mDatalogger.Count > 0)
                {
                    ViewBag.Datalogger = mDatalogger;
                }

                if (mAsset != null && mAsset.Id > 0)
                {

                    var mPointEventLogs = GetPointLog(assetId, DateTime.Now.ToString("d-M-yyyy"));
                    if (mPointEventLogs != null && mPointEventLogs.Count > 0)
                    {
                        ViewBag.PointEventLog = mPointEventLogs;
                    }
                }

                ViewBag.AssetInfoDatalogger = GetAssetInfoDatalogger(mAsset.Id);
            }
            if (mAsset != null && mAsset.Id > 0)
            {
                ViewBag.ChangeTimeAssets = GetChangeTime(mAsset.Id);
                ViewBag.AssetInfos = GetAssetInfo(mAsset.Id);
            }

            return PartialView("_EventLog", mAsset);
        }
        private List<Domain.Datalogger> DataLoggerEvent(Domain.SearchCriteria searchCriteria)
        {
            List<Domain.Datalogger> mDataloggers = new List<Domain.Datalogger>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(searchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("DataloggerAsset/GetTempGraphDataByAsset"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        //mDataloggers = JsonConvert.DeserializeObject<List<Domain.DataloggerEventData>>(jsonString);


                        mDataloggers = JsonConvert.DeserializeObject<List<Domain.Datalogger>>(jsonString);


                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return mDataloggers;
        }
        private List<AssetInfo> GetAssetInfo(int assetId)
        {
            var mAssetInfos = new List<AssetInfo>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"AssetInfo/AssetId/{assetId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssetInfos = JsonConvert.DeserializeObject<List<AssetInfo>>(jsonString);
                    }

                }
            }
            catch (Exception)
            {
            }
            return mAssetInfos;
        }


        [HttpPost]
        public JsonResult GetBulkAssetMetadata(AssetLister mAssetLister)
        {
            mAssetLister.Pager.Take = -1;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    mAssetLister.SearchCriteria.CreatedBy = ClsHttpContent.LoginUser.Id;
                    mAssetLister.SearchCriteria.IsMobileView = true;

                    if (string.IsNullOrEmpty(mAssetLister.SearchCriteria.StartDate))
                        mAssetLister.SearchCriteria.StartDate = DateTime.Now.ToShortDateString();

                    if (string.IsNullOrEmpty(mAssetLister.SearchCriteria.EndDate))
                        mAssetLister.SearchCriteria.EndDate = DateTime.Now.ToShortDateString();

                    var jsonStr = JsonConvert.SerializeObject(mAssetLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GetAllSiteDetailsBySiteId"), str).Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetLister = JsonConvert.DeserializeObject<AssetLister>(jsonString);

                        if (mAssetLister != null && mAssetLister.mAssets != null && mAssetLister.mAssets.Count > 0)
                        {
                            mAssetLister.mAssets = mAssetLister.mAssets.OrderBy(x => x.Sequence).ToList();
                        }

                        mAssetLister.Pager.PageSize = -1;

                        return Json(new
                        {
                            success = true,
                            mAssets = mAssetLister.mAssets ?? new List<Asset>()
                        }, JsonRequestBehavior.AllowGet);
                    }
                    else
                    {
                        return Json(new
                        {
                            success = false,
                            mAssets = new List<Asset>(),
                            errorMessage = "API returned status: " + response.StatusCode
                        }, JsonRequestBehavior.AllowGet);
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    mAssets = new List<Asset>(),
                    errorMessage = ex.Message
                }, JsonRequestBehavior.AllowGet);
            }
        }





        #endregion

    }
}