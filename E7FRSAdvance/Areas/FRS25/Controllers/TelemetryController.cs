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

        private List<AlertAudit> GetAlertAudit(DateTime FromDate, DateTime ToDate)
        {
            List<AlertAudit> mAlertAudits = new List<AlertAudit>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var fromDate = FromDate.ToShortDateString().Replace("/", "-");
                    var toDate = ToDate.ToShortDateString().Replace("/", "-");

                    var response = hcf.client.GetAsync($"AlertAudit/GetByAlertDate/StartDate/{fromDate}/EndDate/{toDate}").Result;
                    string jsonStr = response.Content.ReadAsStringAsync().Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAlertAudits = JsonConvert.DeserializeObject<List<AlertAudit>>(jsonStr);
                    }
                }
            }
            catch (Exception ex)
            {

            }
            return mAlertAudits;
        }


        [HttpPost]
        public JsonResult GetSipAssetAlertAnalytics(
    int siteId,
    int assetId,
    string assetName,
    DateTime? fromDate,
    DateTime? toDate,
    string alertIds)
        {
            try
            {
                var from = fromDate.HasValue
                    ? fromDate.Value.Date
                    : DateTime.Today;

                var to = toDate.HasValue
                    ? toDate.Value.Date.AddDays(1).AddSeconds(-1)
                    : DateTime.Today.AddDays(1).AddSeconds(-1);

                var fRSAlertLister = new Domain.FRSAlertLister();

                fRSAlertLister.SearchCriteria.SiteId = siteId;
                fRSAlertLister.SearchCriteria.FromDate = from;
                fRSAlertLister.SearchCriteria.ToDate = to;

                if (assetId > 0)
                    fRSAlertLister.SearchCriteria.AssetId = assetId;

                if (!string.IsNullOrWhiteSpace(assetName))
                    fRSAlertLister.SearchCriteria.AssetName = assetName.Trim();

                fRSAlertLister = _frsAlertService.GetAcknowledgementAllAlert(fRSAlertLister);

                var alerts = fRSAlertLister != null && fRSAlertLister.mFRSAlerts != null
                    ? fRSAlertLister.mFRSAlerts
                    : new List<Domain.FRSAlert>();

                var selectedAlertIds = new List<int>();

                if (!string.IsNullOrWhiteSpace(alertIds))
                {
                    selectedAlertIds = alertIds
                        .Split(',')
                        .Select(x =>
                        {
                            int id;
                            return int.TryParse(x, out id) ? id : 0;
                        })
                        .Where(x => x > 0)
                        .Distinct()
                        .ToList();
                }

                if (siteId > 0)
                    alerts = alerts.Where(x => x.SiteId == siteId).ToList();

                if (selectedAlertIds.Count > 0)
                {
                    alerts = alerts.Where(x => selectedAlertIds.Contains(x.Id)).ToList();
                }
                else
                {
                    if (assetId > 0)
                    {
                        alerts = alerts.Where(x => x.AssetId == assetId).ToList();
                    }
                    else if (!string.IsNullOrWhiteSpace(assetName))
                    {
                        alerts = alerts
                            .Where(x => (x.AssetName ?? "")
                            .Trim()
                            .Equals(assetName.Trim(), StringComparison.OrdinalIgnoreCase))
                            .ToList();
                    }
                }

                alerts = alerts
                    .Where(x => x.SetTimeStamp >= from && x.SetTimeStamp <= to)
                    .ToList();

                var ids = alerts.Select(x => x.Id).Distinct().ToList();

                var mAlertAudits = GetAlertAudit(from, to);

                if (mAlertAudits != null && mAlertAudits.Count > 0 && ids.Count > 0)
                {
                    mAlertAudits = mAlertAudits
                        .Where(x => ids.Contains(x.AlertId))
                        .ToList();

                    foreach (var frsAlert in alerts)
                    {
                        var alertAudit = mAlertAudits.FirstOrDefault(x => x.AlertId == frsAlert.Id);

                        if (alertAudit != null && alertAudit.Id > 0)
                        {
                            frsAlert.mAlertAudit = alertAudit;
                            frsAlert.IsAlert = alertAudit.IsAlert;
                        }
                    }
                }

                int predictiveId = (int)E7FRSAdvance.Utility.Utility.AlertType.Predictive;
                int failureId = (int)E7FRSAdvance.Utility.Utility.AlertType.Failure;

                var predictiveAlerts = alerts
                    .Where(x => x.AlertTypeId == predictiveId)
                    .ToList();

                var failureAlerts = alerts
                    .Where(x => x.AlertTypeId == failureId)
                    .ToList();

                var predictiveCauses = predictiveAlerts
                    .GroupBy(x => string.IsNullOrWhiteSpace(x.CauseCode) ? "-" : x.CauseCode)
                    .Select(g => new
                    {
                        code = g.Key,
                        count = g.Count(),
                        alertIds = g.Select(x => x.Id).Distinct().ToList()
                    })
                    .OrderByDescending(x => x.count)
                    .ToList();

                var failureCauses = failureAlerts
                    .GroupBy(x => string.IsNullOrWhiteSpace(x.CauseCode) ? "-" : x.CauseCode)
                    .Select(g => new
                    {
                        code = g.Key,
                        count = g.Count(),
                        alertIds = g.Select(x => x.Id).Distinct().ToList()
                    })
                    .OrderByDescending(x => x.count)
                    .ToList();

                return Json(new
                {
                    success = true,

                    total = alerts.Count,
                    pred = predictiveAlerts.Count,
                    fail = failureAlerts.Count,

                    alertIds = ids,

                    predictiveCauses = predictiveCauses,
                    failureCauses = failureCauses
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;

                return Json(new
                {
                    success = false,
                    message = ex.Message
                }, JsonRequestBehavior.AllowGet);
            }
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


        //[HttpPost]
        //public JsonResult GetBulkAssetMetadata(AssetLister mAssetLister)
        //{
        //    mAssetLister.Pager.Take = -1;

        //    try
        //    {
        //        using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
        //        {
        //            mAssetLister.SearchCriteria.CreatedBy = ClsHttpContent.LoginUser.Id;
        //            mAssetLister.SearchCriteria.IsMobileView = true;

        //            if (string.IsNullOrEmpty(mAssetLister.SearchCriteria.StartDate))
        //                mAssetLister.SearchCriteria.StartDate = DateTime.Now.ToShortDateString();

        //            if (string.IsNullOrEmpty(mAssetLister.SearchCriteria.EndDate))
        //                mAssetLister.SearchCriteria.EndDate = DateTime.Now.ToShortDateString();

        //            var jsonStr = JsonConvert.SerializeObject(mAssetLister);
        //            StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
        //            var response = hcf.client.PostAsync(String.Format("Asset/GetAllSiteDetailsBySiteId"), str).Result;

        //            if (response.StatusCode == HttpStatusCode.OK)
        //            {
        //                string jsonString = response.Content.ReadAsStringAsync().Result;
        //                mAssetLister = JsonConvert.DeserializeObject<AssetLister>(jsonString);

        //                if (mAssetLister != null && mAssetLister.mAssets != null && mAssetLister.mAssets.Count > 0)
        //                {
        //                    mAssetLister.mAssets = mAssetLister.mAssets.OrderBy(x => x.Sequence).ToList();
        //                }

        //                mAssetLister.Pager.PageSize = -1;

        //                return Json(new
        //                {
        //                    success = true,
        //                    mAssets = mAssetLister.mAssets ?? new List<Asset>()
        //                }, JsonRequestBehavior.AllowGet);
        //            }
        //            else
        //            {
        //                return Json(new
        //                {
        //                    success = false,
        //                    mAssets = new List<Asset>(),
        //                    errorMessage = "API returned status: " + response.StatusCode
        //                }, JsonRequestBehavior.AllowGet);
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        return Json(new
        //        {
        //            success = false,
        //            mAssets = new List<Asset>(),
        //            errorMessage = ex.Message
        //        }, JsonRequestBehavior.AllowGet);
        //    }
        //}
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
                    var response = hcf.client.PostAsync("Asset/GetAllSiteDetailsBySiteId", str).Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetLister = JsonConvert.DeserializeObject<AssetLister>(jsonString);

                        if (mAssetLister?.mAssets != null && mAssetLister.mAssets.Count > 0)
                        {
                            mAssetLister.mAssets = mAssetLister.mAssets.OrderBy(x => x.Sequence).ToList();
                        }

                        var assets = mAssetLister.mAssets ?? new List<Asset>();

                        // ─────────────────────────────────────────────────────────
                        // FIX: Project ONLY the fields the front-end JS needs.
                        //
                        // WHY THIS MATTERS:
                        //   AssetAttribute carries Data (List<string>),
                        //   DataArray (List<string>), Datas (List<decimal>),
                        //   Dates (List<string>), AttributeData, etc.
                        //   AssetInfoDatalogger carries mDataloggers (List<Datalogger>).
                        //
                        //   For Point Machine with ~180K rows, serializing ALL of
                        //   these nested lists produces 5-10+ MB of JSON, which
                        //   exceeds JavaScriptSerializer's 2 MB maxJsonLength.
                        //
                        //   The JS client (telemetrylive.js) only reads:
                        //     AssetAttribute  → Id, Title, AliasName,
                        //                       Multiplication, Absolute,
                        //                       MinValue, MaxValue
                        //     Datalogger      → Id, DataloggerAttributeId,
                        //                       DataloggerAttribute,
                        //                       DataloggerAssetName, Role
                        //
                        //   Projecting these fields shrinks the payload from
                        //   ~5-10 MB down to ~100-300 KB.
                        // ─────────────────────────────────────────────────────────

                        var slimAssets = assets.Select(a => new
                        {
                            a.Id,
                            a.Name,
                            a.AssetName,
                            a.SiteId,
                            a.AssetTypeId,
                            a.Sequence,
                            a.ZeroOffsetValue,

                            // AssetAttribute → keep only what JS reads
                            // JS key lookups:
                            //   attrId    = attr.Id
                            //   display   = attr.Title  (Point Machine)
                            //             = attr.AliasName (Track/Signal)
                            //   transform = attr.Multiplication, attr.Absolute
                            //   range     = attr.MinValue, attr.MaxValue
                            assetAttributes = (a.assetAttributes ?? new List<AssetAttribute>())
                                .Select(attr => new
                                {
                                    attr.Id,
                                    attr.AssetTypeId,
                                    attr.Title,
                                    attr.AliasName,
                                    attr.Multiplication,
                                    attr.Absolute,
                                    attr.MinValue,
                                    attr.MaxValue
                                }).ToList(),

                            // AssetInfoDatalogger → keep only what JS reads
                            // JS key lookups:
                            //   dlRole = dl.DataloggerAttributeId || dl.Role || dl.Id
                            //   dlName = dl.DataloggerAttribute || dl.DataloggerAssetName
                            mAssetInfoDataloggers = (a.mAssetInfoDataloggers ?? new List<AssetInfoDatalogger>())
                                .Select(dl => new
                                {
                                    dl.Id,
                                    dl.DataloggerAttributeId,
                                    dl.DataloggerAttribute,
                                    dl.DataloggerAssetName,
                                    dl.Value
                                }).ToList()

                        }).ToList();

                        mAssetLister.Pager.PageSize = -1;

                        // ─────────────────────────────────────────────────────────
                        // FIX: Return JsonResult with MaxJsonLength = int.MaxValue
                        //
                        // The default Json() helper uses JavaScriptSerializer
                        // with a hard 2 MB cap. Even after projection, some
                        // large sites may still exceed it. Setting MaxJsonLength
                        // removes the cap as a safety net.
                        // ─────────────────────────────────────────────────────────
                        return new JsonResult
                        {
                            Data = new
                            {
                                success = true,
                                mAssets = slimAssets
                            },
                            JsonRequestBehavior = JsonRequestBehavior.AllowGet,
                            MaxJsonLength = int.MaxValue
                        };
                    }
                    else
                    {
                        return Json(new
                        {
                            success = false,
                            mAssets = new List<object>(),
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
                    mAssets = new List<object>(),
                    errorMessage = ex.Message
                }, JsonRequestBehavior.AllowGet);
            }
        }


        public ActionResult GetListActiveAlerts(Domain.SMSLog mSmsLog)
        {
            string token = string.Empty;
            SMSLogLister mSMSLogLister = new SMSLogLister();
            mSMSLogLister.Pager.Take = -1;
            if (ClsHttpContent.LoginUser != null)
            {
                mSMSLogLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
                mSMSLogLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
                token = ClsHttpContent.LoginUser.Token;
            }
            mSMSLogLister.SearchCriteria = mSmsLog;
            mSMSLogLister.SearchCriteria.IsSmsLogActive = true;
            var fromDate = new DateTime(mSMSLogLister.SearchCriteria.TimeStamp.Year, mSMSLogLister.SearchCriteria.TimeStamp.Month, 1);
            mSMSLogLister.SearchCriteria.FromDate = fromDate;
            mSMSLogLister.SearchCriteria.ToDate = DateTime.Now;
            try
            {
                using (var hcf = new HttpClientFactory(token: token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSMSLogLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SMSLog/GetAll"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSMSLogLister = JsonConvert.DeserializeObject<SMSLogLister>(jsonString);
                    }
                }
            }
            catch (Exception)
            {

            }
            // return PartialView("~/Views/Reporting/_GetAlertSMSLogs.cshtml", mSMSLogLister.mSMSLogs);
            return Json(mSMSLogLister, JsonRequestBehavior.AllowGet);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // ADD TO YOUR EXISTING TelemetryController.cs
        // ═══════════════════════════════════════════════════════════════════════

        #region SIP Event Log & Active Alarms

        /// <summary>
        /// Event Log tab: alert history + maintenance mode records for a date range.
        /// POST /FRS25/Telemetry/GetSipEventLog
        /// </summary>
        [HttpPost]
        public ActionResult GetSipEventLog(SipEventLogRequest request)
        {
            var result = new SipEventLogResponse();

            try
            {
                if (request == null || request.AssetId <= 0)
                    return Json(new { Success = false, Message = "AssetId is required." });

                DateTime fromDate = DateTime.Today;
                DateTime toDate = DateTime.Today.AddDays(1).AddSeconds(-1);

                if (!string.IsNullOrEmpty(request.FromDate))
                    DateTime.TryParse(request.FromDate, out fromDate);
                if (!string.IsNullOrEmpty(request.ToDate))
                    DateTime.TryParse(request.ToDate, out toDate);

                // ── 1. ALERT HISTORY ──────────────────────────────────────────
                var alertLister = new Domain.FRSAlertLister
                {
                    SearchCriteria = new Domain.FRSAlert
                    {
                        //IsAcknowledgement = true,
                        AssetIds = new List<int> { request.AssetId },
                        SiteIds = request.SiteId > 0
                            ? new List<int> { request.SiteId }
                            : null,
                        AssetTypeIds = request.AssetTypeId > 0
                            ? new List<int> { request.AssetTypeId }
                            : null,
                        FromDate = fromDate,
                        ToDate = toDate
                    },
                    Pager = new Domain.Pager
                    {
                        PageSize = request.Take > 0 ? request.Take : 200,
                        Take = request.Take > 0 ? request.Take : 200
                    }
                };

                alertLister = _frsAlertService.GetListerWithPagination(alertLister);

                if (alertLister?.mFRSAlerts != null)
                {
                    result.Alerts = alertLister.mFRSAlerts.Select(a => new SipAlertItem
                    {
                        IncidentDateTime = a.SetTimeStamp,
                        CauseCode = a.CauseCode ?? "",
                        AlertStatus = a.AlertStatus,
                        AssetName = a.AssetName ?? "",
                        AlertType = a.AlertType ?? "",
                        Description = a.Description ?? ""
                    }).ToList();
                }

                // ── 2. MAINTENANCE MODE HISTORY ───────────────────────────────
                //    Fetch ALL records for the asset, then filter by date in C#
                //    because the API may not filter by FromDate/ToDate reliably.
                var maintLister = new MaintenanceModeLister
                {
                    SearchCriteria = new MaintenanceMode
                    {
                        AssetIds = new List<int> { request.AssetId },
                        SiteIds = request.SiteId > 0
                            ? new List<int> { request.SiteId }
                            : null,
                        IsCheckMaintenceModeStatus = true,
                        RoleId = ClsHttpContent.LoginUser.RoleId,
                        UserId = ClsHttpContent.LoginUser.Id
                    },
                    Pager = new Domain.Pager { Take = -1 }
                };

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(maintLister);
                    var content = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync("MaintenanceMode/GetLister", content).Result;

                    if (response.StatusCode == System.Net.HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        var parsed = JsonConvert.DeserializeObject<MaintenanceModeLister>(jsonString);

                        if (parsed?.mMaintenanceModes != null)
                        {
                            // Filter: keep records where ActiveTime OR InActiveTime
                            // falls within the requested date range
                            result.MaintenanceModes = parsed.mMaintenanceModes
                                .Where(m => m.AssetId == request.AssetId)
                                .Where(m =>
                                {
                                    var active = m.ActiveTime;
                                    var inactive = m.InActiveTime ?? DateTime.MinValue;
                                    // Record overlaps the range if:
                                    // - activated during range, OR
                                    // - deactivated during range, OR
                                    // - was active spanning the range (activated before, deactivated after or still active)
                                    bool activatedInRange = active >= fromDate && active <= toDate;
                                    bool deactivatedInRange = inactive >= fromDate && inactive <= toDate;
                                    bool spansRange = active <= fromDate && (m.IsMaintenceMode || inactive >= toDate);
                                    return activatedInRange || deactivatedInRange || spansRange;
                                })
                                .Select(m => new SipMaintenanceItem
                                {
                                    Id = m.Id,
                                    IsMaintenceMode = m.IsMaintenceMode,
                                    ActiveTime = m.ActiveTime,
                                    InActiveTime = m.InActiveTime,
                                    Asset = m.Asset ?? "",
                                    Site = m.Site ?? "",
                                }).ToList();
                        }
                    }
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = "Error loading event log: " + ex.Message;
            }

            var jsonResult = Json(result, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;
        }


        /// <summary>
        /// Alarm tab: all ACTIVE alerts for a specific asset.
        /// POST /FRS25/Telemetry/GetSipActiveAlarms
        /// Uses GetListerWithPagination with AlertStatus = 1 (Active).
        /// </summary>
        [HttpPost]
        public ActionResult GetSipActiveAlarms(SipActiveAlarmsRequest request)
        {
            var result = new SipActiveAlarmsResponse();

            try
            {
                if (request == null || request.AssetId <= 0)
                    return Json(new { Success = false, Message = "AssetId is required." });

                var alertLister = new Domain.FRSAlertLister
                {
                    SearchCriteria = new Domain.FRSAlert
                    {
                        IsAcknowledgement = true,
                        AlertStatus = 1,   // Active only
                        AssetIds = new List<int> { request.AssetId },
                        SiteIds = request.SiteId > 0
                            ? new List<int> { request.SiteId }
                            : null,
                        AssetTypeIds = request.AssetTypeId > 0
                            ? new List<int> { request.AssetTypeId }
                            : null
                    },
                    Pager = new Domain.Pager
                    {
                        PageSize = 100,
                        Take = 100
                    }
                };

                alertLister = _frsAlertService.GetListerWithPagination(alertLister);

                if (alertLister?.mFRSAlerts != null)
                {
                    var now = DateTime.Now;

                    result.Alarms = alertLister.mFRSAlerts
                        .Where(a => a.AlertStatus == 1)   // double-check active
                        .Select(a =>
                        {
                            // Calculate duration from IncidentDateTime to now
                            var raised = a.SetTimeStamp;
                            var duration = raised > DateTime.MinValue
                                ? (now - raised)
                                : TimeSpan.Zero;

                            return new SipActiveAlarmItem
                            {
                                CauseCode = a.CauseCode ?? "",
                                RaisedAt = a.SetTimeStamp,
                                DurationMinutes = (int)duration.TotalMinutes,
                                DurationDisplay = raised > DateTime.MinValue
                                    ? string.Format("{0:D2}:{1:D2}:{2:D2}",
                                        (int)duration.TotalHours, duration.Minutes, duration.Seconds)
                                    : "--",
                                AlertType = a.AlertType ?? "",
                                Description = a.Description ?? "",
                                AssetName = a.AssetName ?? ""
                            };
                        }).ToList();

                    result.ActiveCount = result.Alarms.Count;
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = "Error loading alarms: " + ex.Message;
            }

            return Json(result, JsonRequestBehavior.AllowGet);
        }

        #endregion


        // ═══════════════════════════════════════════════════════════════════════
        // MODELS
        // ═══════════════════════════════════════════════════════════════════════

        #region SIP Models

        // ── Event Log ────────────────────────────────────────────────────────

        public class SipEventLogRequest
        {
            public int AssetId { get; set; }
            public int SiteId { get; set; }
            public int AssetTypeId { get; set; }
            public string FromDate { get; set; }
            public string ToDate { get; set; }
            public int Take { get; set; }
        }

        public class SipEventLogResponse
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public List<SipAlertItem> Alerts { get; set; } = new List<SipAlertItem>();
            public List<SipMaintenanceItem> MaintenanceModes { get; set; } = new List<SipMaintenanceItem>();
        }

        public class SipAlertItem
        {
            public DateTime? IncidentDateTime { get; set; }
            public string CauseCode { get; set; }
            public int AlertStatus { get; set; }
            public string Severity { get; set; }
            public string AssetName { get; set; }
            public string AlertType { get; set; }
            public string Description { get; set; }
        }

        public class SipMaintenanceItem
        {
            public int Id { get; set; }
            public bool IsMaintenceMode { get; set; }
            public DateTime? ActiveTime { get; set; }
            public DateTime? InActiveTime { get; set; }
            public string Asset { get; set; }
            public string Site { get; set; }
            public DateTime? CreatedDate { get; set; }
        }

        // ── Active Alarms ────────────────────────────────────────────────────

        public class SipActiveAlarmsRequest
        {
            public int AssetId { get; set; }
            public int SiteId { get; set; }
            public int AssetTypeId { get; set; }
        }

        public class SipActiveAlarmsResponse
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public int ActiveCount { get; set; }
            public List<SipActiveAlarmItem> Alarms { get; set; } = new List<SipActiveAlarmItem>();
        }

        public class SipActiveAlarmItem
        {
            public string CauseCode { get; set; }
            public DateTime? RaisedAt { get; set; }
            public int DurationMinutes { get; set; }
            public string DurationDisplay { get; set; }
            public string Severity { get; set; }
            public string AlertType { get; set; }
            public string Description { get; set; }
            public string AssetName { get; set; }
        }
        #endregion


    }
}
#endregion