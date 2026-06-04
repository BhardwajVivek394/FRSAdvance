using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;
using System.Web.UI.WebControls;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class ReportingController : Controller
    {
        // GET: Reporting
        private readonly ISiteService siteService;
        private readonly IAssetAttributeService assetAttributeService;
        private readonly IAssetTypeService assetTypeService;
        public ReportingController(ISiteService siteService, IAssetAttributeService assetAttributeService, IAssetTypeService assetTypeService)
        {
            this.siteService = siteService;
            this.assetAttributeService = assetAttributeService;
            this.assetTypeService = assetTypeService;
        }


        public ActionResult Index()
        {
            return View();
        }

        public PartialViewResult SiteList(SiteLister mSiteLister)
        {
            mSiteLister.Pager.Take = -1;
            mSiteLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
            mSiteLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSiteLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Site/GetAllSiteLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSiteLister = JsonConvert.DeserializeObject<SiteLister>(jsonString);
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Something went wrong!";
            }
            return PartialView("_SiteListPartial", mSiteLister);
        }

        public ActionResult GetAllAssetTypeBySiteId(List<int> siteIds)
        {
            List<AssetType> mAssetType = new List<AssetType>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(siteIds);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("AssetType/GetAllAssetTypeBySiteId"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetType = JsonConvert.DeserializeObject<List<AssetType>>(jsonString);
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
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView("~/Views/Reporting/_GetAssetType.cshtml", mAssetType);
        }
        public ActionResult GetAllAssetBySiteId(List<int> siteIds, int assetTypeId)
        {
            List<Asset> mAssets = new List<Asset>();
            Asset mAsset = new Asset();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    mAsset.SiteIds = siteIds;
                    mAsset.AssetTypeId = assetTypeId;
                    mAsset.CreatedBy = ClsHttpContent.LoginUser.Id;
                    var jsonStr = JsonConvert.SerializeObject(mAsset);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GetAllAssetBySiteId"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
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
            return PartialView("~/Views/Reporting/_GetAllAssetDetails.cshtml", mAssets);
        }

        public ActionResult GetAssetAttributesByAssetId(List<int> siteIds, int assetTypeId, List<int> assetIds)
        {
            List<Asset> mAssets = new List<Asset>();
            Asset mAsset = new Asset();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    mAsset.SiteIds = siteIds;
                    mAsset.AssetTypeId = assetTypeId;
                    mAsset.CreatedBy = ClsHttpContent.LoginUser.Id;
                    foreach (var item in assetIds)
                    {
                        mAsset.mAssets.Add(new Asset { Id = item });
                    }
                    var jsonStr = JsonConvert.SerializeObject(mAsset);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GetAssetAttributesByAssetId"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAsset = JsonConvert.DeserializeObject<Asset>(jsonString);
                        mAsset.assetAttributes = mAsset.assetAttributes.GroupBy(x => x.Title).Select(x => x.FirstOrDefault()).ToList();
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
            return PartialView("~/Views/Reporting/__GetAssetAttribute.cshtml", mAsset);
        }

        public JsonResult Report(List<Asset> mAssets)
        {
            if (mAssets[0].assetAttributes != null && mAssets[0].assetAttributes.Count == 0)
                mAssets[0].IsGraphLoad = true;

            TempData["Assets"] = mAssets;
            TempData.Keep();
            return Json(true, JsonRequestBehavior.AllowGet);
        }

        public ActionResult SiteReportDetails()
        {
            return View();
        }

        public JsonResult GetTime()
        {
            Asset mAsset = new Asset();
            if (TempData["Assets"] != null)
            {
                var mAssets = TempData.Peek("Assets") as List<Asset>;
                mAsset.StartDate = mAssets.FirstOrDefault().StartDate;
                mAsset.EndDate = mAssets.FirstOrDefault().EndDate;

                TempData.Keep();
            }
            return Json(mAsset, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetHeader()
        {
            Asset mAsset = new Asset();
            try
            {
                if (TempData["Assets"] != null)
                {
                    var mAssets = TempData.Peek("Assets") as List<Asset>;
                    TempData.Keep();
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        mAssets[0].CreatedBy = ClsHttpContent.LoginUser.Id;
                        var jsonStr = JsonConvert.SerializeObject(mAssets);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("Asset/GetHeader"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mAsset = JsonConvert.DeserializeObject<Asset>(jsonString);
                        }
                        else
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            ViewBag.Type = "Error";
                            ViewBag.Message = "Internal server error!";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = ex.Message;
            }
            return Json(mAsset, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetReportData()
        {
            List<Asset> assets = new List<Asset>();
            try
            {
                if (TempData["Assets"] != null)
                {
                    var mAssets = TempData.Peek("Assets") as List<Asset>;
                    TempData.Keep();
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        mAssets[0].CreatedBy = ClsHttpContent.LoginUser.Id;
                        mAssets[0].IsGraphLoad = true;

                        TempData["Assets"] = mAssets;
                        TempData.Keep();

                        var jsonStr = JsonConvert.SerializeObject(mAssets);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("Asset/GenerateGraph"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            assets = JsonConvert.DeserializeObject<List<Asset>>(jsonString);
                        }
                        else
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            ViewBag.Error = "Internal server error!";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }
            var jsonResult = Json(assetAttributeService.PrepareHtml(assets), JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;
        }



        public ActionResult DownloadReportCsv()
        {
            try
            {
                Asset mAsset = new Asset();
                if (TempData["Assets"] != null)
                {
                    var mAssets = TempData.Peek("Assets") as List<Asset>;
                    TempData.Keep();
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mAssets);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("Asset/GenerateGraph"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            var assets = JsonConvert.DeserializeObject<List<Asset>>(jsonString);
                            if (assets != null && assets.Count > 0)
                            {
                                List<string> DatasList = new List<string>();
                                string StartDate = mAssets.FirstOrDefault().StartDate;
                                string EndDate = mAssets.FirstOrDefault().EndDate;

                                Response.Clear();
                                Response.Buffer = true;
                                Response.AddHeader("content-disposition", $"attachment;filename=SiteReport-{Guid.NewGuid().ToString()}(" + StartDate + " to " + EndDate + ").csv");
                                Response.Charset = "";
                                Response.ContentType = "application/text";
                                Response.Output.Write(assetAttributeService.PrepareCSVForFastDataProvider(assets));
                                Response.Flush();
                                Response.End();

                            }

                            //mAsset = JsonConvert.DeserializeObject<Asset>(jsonString);
                            //DownloadCsv(mAsset);
                        }
                        else
                        {
                            ViewBag.Error = "Internal server error!";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }
            return View();
        }

        public void DownloadCsv(Asset mAsset)
        {
            if (mAsset != null)
            {
                var mAssets = TempData.Peek("Assets") as List<Asset>;
                TempData.Keep();
                string StartDate = mAssets.FirstOrDefault().StartDate;
                string EndDate = mAssets.FirstOrDefault().EndDate;
                string typeName = mAsset.assetAttributes.Select(x => x.AssetTypeName).FirstOrDefault();



                var multipleLogs = mAsset.MultipleLog.GroupBy(x => x.TimeStamp).ToList();

                mAsset.DateList = mAsset.siteAttributeDatasList;


                List<string> DatasList = new List<string>();
                if (multipleLogs != null && multipleLogs.Count > 0)
                {
                    foreach (var item in multipleLogs)
                    {
                        int i = 0;
                        string data = string.Empty;
                        foreach (var val in item.ToList())
                        {
                            string d = val.CsvData.Replace("~", ",");
                            var sliptVale = d.Split(',');
                            if (i == 0)
                                data += $"{sliptVale[0]},{sliptVale[1]},";

                            for (int j = 0; j < sliptVale.Length; j++)
                            {
                                if (j == 0 || j == 1 || j == 2)
                                {

                                }
                                else { data += $"{sliptVale[j]},"; }
                            }


                            i++;
                        }
                        DatasList.Add(data);
                    }
                }
                mAsset.siteAttributeDatasList = DatasList;

                List<string> assetNames = new List<string>();

                if (mAsset.DateList != null && mAsset.DateList.Count > 0)
                {
                    foreach (var item in mAsset.DateList)
                    {
                        var values = item.Replace('~', ',');

                        if (!assetNames.Contains(values.Split(',')[2]))
                        {
                            assetNames.Add(values.Split(',')[2]);
                        }
                    }

                }


                string csv = string.Empty;
                csv += "SiteName,";
                csv += "Date,";
                csv += "AssetType,";

                foreach (var asset in assetNames)
                {
                    foreach (var assetAttribute in mAsset.assetAttributes)
                    {
                        csv += $"{asset} {assetAttribute.Title},";
                    }
                }


                csv += "\r\n";
                foreach (var row in DatasList)
                {
                    string newRow = mAsset.SiteName + ",";// + row;

                    var val = row.Contains(',') ? row.Split(',') : row.Split('~');
                    for (int i = 0; i < val.Length; i++)
                    {
                        //if (i == 2 && typeName.Trim().ToLower().Contains("track".ToLower()))
                        //{
                        //    newRow += Convert.ToString(val[i] + "T") + ",";
                        //}
                        //else
                        //{
                        newRow += Convert.ToString(val[i]) + ",";
                        //}
                    }
                    //Add the Data rows.
                    csv += newRow.TrimEnd(',');
                    //Add new line.
                    csv += "\r\n";
                }

                Response.Clear();
                Response.Buffer = true;
                Response.AddHeader("content-disposition", $"attachment;filename=SiteReport-{Guid.NewGuid().ToString()}(" + StartDate + " to " + EndDate + ").csv");
                Response.Charset = "";
                Response.ContentType = "application/text";
                Response.Output.Write(csv);
                Response.Flush();
                Response.End();
            }
        }


        public JsonResult GetAllSite()
        {
            List<Site> mSites = new List<Site>();
            SiteLister mSiteLister = new SiteLister();
            mSiteLister.Pager.Take = -1;
            mSiteLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
            mSiteLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSiteLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
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
            catch (Exception ex)
            {

            }
            var jsonResult = Json(mSites, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;

            //return Json(mSites, JsonRequestBehavior.AllowGet);
        }
        public ActionResult GetAssetTypeSmsLogCount(AlertAnalyticsSearch mAlertAnalyticsSearch)
        {
            List<AssetType> mAssetTypes = new List<AssetType>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAlertAnalyticsSearch);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SMSLog/GetAssetTypesWithAlertCount"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetTypes = JsonConvert.DeserializeObject<List<AssetType>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = ex.Message;
            }
            return PartialView("_GetAssetTypeCount", mAssetTypes);
        }

        public ActionResult GetAlertCountByAsset(AlertAnalyticsSearch mAlertAnalyticsSearch)
        {
            AnalyticsLister mAnalyticsLister = new AnalyticsLister();
            try

            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAlertAnalyticsSearch);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SMSLog/GetAlertCountByAsset"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAnalyticsLister = JsonConvert.DeserializeObject<AnalyticsLister>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = ex.Message;
            }
            return PartialView("_GetAlertAnalytics", mAnalyticsLister);
        }
        public ActionResult GetSmsLogById(AlertAnalyticsSearch mAlertAnalyticsSearch)
        {
            List<SMSLog> mSmsLog = new List<SMSLog>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAlertAnalyticsSearch);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SMSLog/GetSMSLogById"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSmsLog = JsonConvert.DeserializeObject<List<SMSLog>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = ex.Message;
            }
            return PartialView("_GetAlertSMSLogs", mSmsLog);
        }

        public ActionResult GetAllMaintenanceAssetTypeBySiteId(List<int> siteIds)
        {
            List<AssetType> mAssetType = new List<AssetType>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(siteIds);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("AssetType/GetAllAssetTypeBySiteId"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetType = JsonConvert.DeserializeObject<List<AssetType>>(jsonString);
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
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView("~/Views/Reporting/_GetAssetTypeMaintenence.cshtml", mAssetType);
        }

        public ActionResult GetAllMaintenanceAssetBySiteId(List<int> siteIds, int assetTypeId)
        {
            List<MaintenanceInput> mAssets = new List<MaintenanceInput>();
            Asset mAsset = new Asset();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    mAsset.SiteIds = siteIds;
                    mAsset.AssetTypeId = assetTypeId;
                    var jsonStr = JsonConvert.SerializeObject(mAsset);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Maintenance/GetAllMaintenanceAssetBySiteId"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssets = JsonConvert.DeserializeObject<List<MaintenanceInput>>(jsonString);
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
            return PartialView("~/Views/Reporting/_GetAllMaintenenceAssetDetails.cshtml", mAssets);
        }

        public JsonResult SaveMaintencesInput(List<MaintenanceInput> maintenances)
        {
            APIResponse aPIResponse = new APIResponse();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(maintenances);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Maintenance/SaveMaintencesInput"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        aPIResponse = JsonConvert.DeserializeObject<APIResponse>(jsonString);
                    }
                }
            }
            catch (Exception)
            {

            }
            return Json(aPIResponse, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetDayMaintenace(int day)
        {
            List<DateTime> days = new List<DateTime>();
            for (int i = 1; i <= day; i++)
            {
                days.Add(DateTime.Now.AddDays(i));
            }
            return PartialView("_GetMaintenanceDay", days);
        }
        public ActionResult ScheduleMaintenenace(List<string> dateList, int siteId)
        {
            List<MaintenanceSchedule> MaintenanceSchedules = new List<MaintenanceSchedule>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(dateList);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Maintenance/ScheduleMaintenenace/{0}", siteId), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        MaintenanceSchedules = JsonConvert.DeserializeObject<List<MaintenanceSchedule>>(jsonString);
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
            return PartialView("~/Views/Reporting/_ScheduleMaintenenacePartial.cshtml", MaintenanceSchedules);
        }

        public JsonResult SaveRoster(List<RosterHistory> mRoasterList)
        {
            APIResponse aPIResponse = new APIResponse();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mRoasterList);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Maintenance/SaveRoster"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        aPIResponse = JsonConvert.DeserializeObject<APIResponse>(jsonString);
                    }
                }
            }
            catch (Exception)
            {

            }
            return Json(aPIResponse, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetAllRoster(int siteId)
        {
            List<Roster> rosters = new List<Roster>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Maintenance/GetAllRoster/{0}", siteId)).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        rosters = JsonConvert.DeserializeObject<List<Roster>>(jsonString);
                        if (rosters != null && rosters.Count > 0)
                        {
                            foreach (var item in rosters)
                            {
                                item.SiteId = siteId;
                            }
                        }
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
            return PartialView("~/Views/Reporting/_RosterHistoryPartial.cshtml", rosters);
        }

        public ActionResult GetAllRosterHistory(int siteId, int rosterId)
        {
            RosterHistoryLister rosterHistoryLister = new RosterHistoryLister();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    rosterHistoryLister.RosterHistory.SiteId = siteId;
                    rosterHistoryLister.RosterHistory.RosterId = rosterId;
                    var jsonStr = JsonConvert.SerializeObject(rosterHistoryLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Maintenance/GetRosterHistory"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        rosterHistoryLister = JsonConvert.DeserializeObject<RosterHistoryLister>(jsonString);
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
            return PartialView("~/Views/Reporting/_RosterHistoryListPartial.cshtml", rosterHistoryLister);
        }

        public JsonResult UpdateRosterHistory(List<RosterHistory> mRoasterList)
        {
            bool aPIResponse = false;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mRoasterList);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Maintenance/UpdateRosterHistory"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        aPIResponse = JsonConvert.DeserializeObject<bool>(jsonString);
                    }
                }
            }
            catch (Exception)
            {

            }
            return Json(aPIResponse, JsonRequestBehavior.AllowGet);
        }
        public JsonResult DeleteRosterHistory(int id, int siteId)
        {
            bool aPIResponse = false;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Maintenance/DeleteRosterHistory/{0}/{1}", id, siteId)).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        aPIResponse = JsonConvert.DeserializeObject<bool>(jsonString);
                    }
                }
            }
            catch (Exception)
            {

            }
            return Json(aPIResponse, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GeneratePdf(int siteId, int rosterId)
        {
            RosterHistoryLister rosterHistoryLister = new RosterHistoryLister();
            using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
            {
                rosterHistoryLister.RosterHistory.SiteId = siteId;
                rosterHistoryLister.RosterHistory.RosterId = rosterId;
                var jsonStr = JsonConvert.SerializeObject(rosterHistoryLister);
                StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                var response = hcf.client.PostAsync(String.Format("Maintenance/GetRosterHistory"), str).Result;
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    rosterHistoryLister = JsonConvert.DeserializeObject<RosterHistoryLister>(jsonString);
                }
            }

            string path = HttpContext.Server.MapPath("~/Template/RosterHistory.html");
            FileReader fileReader = new FileReader(path);
            string pdfTemplate = fileReader.Read();

            string temp = "";
            string lineItem = "";
            temp += $"<tr>";
            temp += $"<td style='width:15%;padding:3px;'>Day</td>";
            temp += $"<td style='width:15%;padding:3px;'>Track</td>";
            temp += $"<td style='width:25%;padding:3px;'>Condition</td>";
            temp += $"<td style='width:10%;padding:3px;'>Status</td>";
            temp += $"<td style='width:35%;padding:3px;'>Remark</td>";
            temp += "</tr>";
            foreach (var item in rosterHistoryLister.RosterHistories.OrderBy(x => x.AssignDate).ToList())
            {
                temp += $"<tr>";
                temp += $"<td style='width:15%;padding:3px;'>{item.AssignDate.ToShortDateString()}</td>";
                temp += $"<td style='width:15%;padding:3px;'>{item.AssetTypeName} > {item.Name}</td>";
                temp += $"<td style='width:25%;padding:3px;'>{item.MaintenanceInput}</td>";
                temp += $"<td style='width:10%;padding:3px;'>{item.Status}</td>";
                temp += $"<td style='width:35%;padding:3px;'>{item.Remark}</td>";
                temp += "</tr>";

                lineItem += $"<tr>";
                //lineItem += $"<td style='width:15%;padding:3px;'>{item.AssetTypeName} > {item.Name}</td>";
                lineItem += $"<td style='width:15%;padding:3px;'>";
                if (item.TrainMomentImagePath.IsNotNullOrEmpty())
                {
                    lineItem += $"<img src='{item.TrainMomentImagePath}' height='150' width='200' />";
                }

                if (item.GluedImagePath.IsNotNullOrEmpty())
                {
                    lineItem += $"<img src='{item.GluedImagePath}' height='150' width='200' />";
                }

                if (item.SignatureImagePath.IsNotNullOrEmpty())
                {
                    lineItem += $"<img src='{item.SignatureImagePath}' height='150' width='200' />";
                }
                lineItem += $"</td>";
                lineItem += $"</tr>";
            }

            //if (mBlog.BlogImages != null && mBlog.BlogImages.Count > 0)
            //{
            //    lineItem += $"<img src='{ConfigurationManager.AppSettings.Get("FilePathUrl")}{mBlog.BlogImages.FirstOrDefault().ImagePath}' height='150' width='200' />";
            //}
            //lineItem += "</td></tr>";

            pdfTemplate = pdfTemplate.Replace("{%Sitename%}", rosterHistoryLister.RosterHistory.SiteName);
            pdfTemplate = pdfTemplate.Replace("{%RosterDate%}", rosterHistoryLister.RosterHistory.AssignDate.ToShortDateString());
            pdfTemplate = pdfTemplate.Replace("{%DivisonName%}", rosterHistoryLister.RosterHistory.DivisionName);
            pdfTemplate = pdfTemplate.Replace("{%ZoneName%}", rosterHistoryLister.RosterHistory.ZoneName);
            pdfTemplate = pdfTemplate.Replace("{%tbodyData%}", temp);
            pdfTemplate = pdfTemplate.Replace("{%tbodyImageData%}", lineItem);
            try
            {
                Byte[] res = null;
                using (MemoryStream ms = new MemoryStream())
                {
                    var pdf = TheArtOfDev.HtmlRenderer.PdfSharp.PdfGenerator.GeneratePdf(pdfTemplate, PdfSharp.PageSize.A4, 5);
                    pdf.Save(ms);
                    res = ms.ToArray();
                }

                var responses = System.Web.HttpContext.Current.Response;
                responses.BufferOutput = true;
                responses.Clear();
                responses.ClearHeaders();
                responses.AddHeader("content-disposition", $"attachment;filename=Roster_{DateTime.Now.Ticks}.pdf");
                responses.ContentType = System.Net.Mime.MediaTypeNames.Application.Pdf;
                responses.ContentEncoding = System.Text.Encoding.UTF8;
                responses.BinaryWrite(res);
                responses.End();
            }
            catch (Exception ex)
            {
                return RedirectToAction("Index");
            }
            return View("Index");
        }


        public JsonResult GetAllAssetType(List<int> siteIds)
        {
            List<AssetType> mAssetType = new List<AssetType>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(siteIds);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("AssetType/GetAllAssetTypeBySiteId"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetType = JsonConvert.DeserializeObject<List<AssetType>>(jsonString);
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
                ViewBag.Error = ex.Message.ToString();
            }
            return Json(mAssetType, JsonRequestBehavior.AllowGet);
        }
        public JsonResult GetAllAttributes(int siteId, int assetTypeId, int attributeId = 0, string database = null)
        {
            Asset mAssetAttributes = new Asset();
            bool isReadHttpPost = false;
            bool isReadLocal = false;
            if (!string.IsNullOrEmpty(database) && database == "HTTP")
                isReadHttpPost = true;

            if (!string.IsNullOrEmpty(database) && database == "Local")
                isReadLocal = true;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("AssetType/GetAttributes/{0}/{1}/{2}/{3}/{4}", siteId, assetTypeId, attributeId, isReadHttpPost, isReadLocal)).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetAttributes = JsonConvert.DeserializeObject<Asset>(jsonString);
                    }
                    else
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
                if (assetTypeId == 1)
                {
                    var leakage = new Domain.AssetAttribute();
                    leakage.AssetName = mAssetAttributes.assetAttributes.Select(x => x.AssetName).FirstOrDefault();
                    leakage.Title = "Leakage";
                    leakage.Id = -1;
                    mAssetAttributes.assetAttributes.Add(leakage);
                }
                if (attributeId == -1)
                {
                    foreach (var data in mAssetAttributes.mAssetInfos)
                    {
                        data.Channels = mAssetAttributes.mAssets.Where(x => x.Id == data.AssetId).Select(x => x.Name).FirstOrDefault();
                    }
                }
                mAssetAttributes.ChannelList = mAssetAttributes.mAssetInfos.Select(x => x.Channels).Distinct().ToList();
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = ex.Message;
            }
            return Json(mAssetAttributes, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetAllPointMachine(int siteId, int assetTypeId)
        {
            AssetLister mAssetLister = new AssetLister();
            mAssetLister.Pager.Take = -1;
            mAssetLister.SearchCriteria.SiteId = siteId;
            mAssetLister.SearchCriteria.AssetTypeId = assetTypeId;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssetLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GetAllSiteDetailsBySiteId"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetLister = JsonConvert.DeserializeObject<AssetLister>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return Json(mAssetLister.mAssets, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetPointMachineDate(int assetId, string range, string operation, string sd, string ed)
        {
            PointMachineData pointMachineData = new PointMachineData();
            pointMachineData.AssetId = assetId;
            if (range == "Today")
            {
                pointMachineData.StartDate = DateTime.Now;
                pointMachineData.EndDate = DateTime.Now;
            }
            if (range == "Yesterday")
            {
                pointMachineData.StartDate = DateTime.Now.AddDays(-1);
                pointMachineData.EndDate = DateTime.Now.AddDays(-1);
            }
            if (range == "Last7Day")
            {
                pointMachineData.EndDate = DateTime.Now;
                pointMachineData.StartDate = DateTime.Now.AddDays(-7);
            }
            if (range == "LastMonth")
            {
                pointMachineData.EndDate = DateTime.Now;
                pointMachineData.StartDate = DateTime.Now.AddDays(-30);
            }
            if (range == "CustomeDate")
            {
                pointMachineData.EndDate = Convert.ToDateTime(ed);
                pointMachineData.StartDate = Convert.ToDateTime(sd);
            }
            //pointMachineData.StartDate = DateTime.Now;
            //pointMachineData.EndDate = DateTime.Now;
            List<PointMachineData> mPointMachineDatas = new List<PointMachineData>();
            List<PointMachineData> mPointMachines = new List<PointMachineData>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(pointMachineData);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GetPointMachineList"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mPointMachineDatas = JsonConvert.DeserializeObject<List<PointMachineData>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            if (mPointMachineDatas != null && mPointMachineDatas.Count > 0)
            {
                if (mPointMachineDatas != null && mPointMachineDatas.Count > 0 && (range == "Last7Day" || range == "LastMonth" || range == "CustomeDate"))
                {
                    List<int> assetIds = mPointMachineDatas.Select(x => x.AssetId).Distinct().ToList();
                    string op = string.Empty;
                    string op1 = string.Empty;
                    string op3 = string.Empty;
                    if (operation == "Operating Current")
                    {
                        op = "A_C";
                        op1 = "B_C";
                    }
                    if (operation == "Operating Time")
                    {
                        op = "A_C_Time";
                        op1 = "B_C_Time";
                    }
                    if (operation == "Operating Voltage")
                    {
                        op = "A_V_AVERAGE";
                        op1 = "B_V_AVERAGE";
                    }
                    for (DateTime dateTime = pointMachineData.StartDate; dateTime <= pointMachineData.EndDate; dateTime += TimeSpan.FromDays(1))
                    {
                        var mPointMachineData = mPointMachineDatas.Where(p => p.TimeStamp.Year == dateTime.Year && p.TimeStamp.Month == dateTime.Month && p.TimeStamp.Day == dateTime.Day).ToList();

                        var mPm1 = mPointMachineDatas.Where(p => p.TimeStamp.Year == dateTime.Year && p.TimeStamp.Month == dateTime.Month && p.TimeStamp.Day == dateTime.Day).FirstOrDefault();
                        if (mPointMachineData != null && mPointMachineData.Count > 0)
                        {
                            int cACount = 0;
                            int tACount = 0;
                            int vACount = 0;

                            int cBCount = 0;
                            int tBCount = 0;
                            int vBCount = 0;

                            int totalRun = mPointMachineData.Count;
                            foreach (var pointMachine in mPointMachineData)
                            {
                                if (pointMachine.PointMachineJson.A_C_AVERAGE > 0.5 && pointMachine.PointMachineJson.A_C_AVERAGE < 5)
                                {
                                    mPm1.PointMachineJson.A_C_AVERAGE += pointMachine.PointMachineJson.A_C_AVERAGE;
                                    cACount++;
                                }
                                if (pointMachine.PointMachineJson.A_V_AVERAGE > 50 && pointMachine.PointMachineJson.A_V_AVERAGE < 150)
                                {
                                    mPm1.PointMachineJson.A_V_AVERAGE += pointMachine.PointMachineJson.A_V_AVERAGE;
                                    vACount++;
                                }
                                if (pointMachine.PointMachineJson.A_C_TIME > 1000 && pointMachine.PointMachineJson.A_C_TIME < 3000)
                                {
                                    mPm1.PointMachineJson.A_C_TIME += pointMachine.PointMachineJson.A_C_TIME;
                                    tACount++;
                                }

                                if (pointMachine.PointMachineJson.B_C_AVERAGE > 0.5 && pointMachine.PointMachineJson.B_C_AVERAGE < 5)
                                {
                                    mPm1.PointMachineJson.B_C_AVERAGE += pointMachine.PointMachineJson.B_C_AVERAGE;
                                    cBCount++;
                                }
                                if (pointMachine.PointMachineJson.B_V_AVERAGE > 50 && pointMachine.PointMachineJson.B_V_AVERAGE < 150)
                                {
                                    mPm1.PointMachineJson.B_V_AVERAGE += pointMachine.PointMachineJson.B_V_AVERAGE;
                                    vBCount++;
                                }
                                if (pointMachine.PointMachineJson.B_C_TIME > 1000 && pointMachine.PointMachineJson.B_C_TIME < 3000)
                                {
                                    mPm1.PointMachineJson.B_C_TIME += pointMachine.PointMachineJson.B_C_TIME;
                                    tBCount++;
                                }
                            }
                            if (range == "Last7Day")
                            {
                                mPm1.PointMachineJson.A_C_AVERAGE = cACount == 0 ? mPm1.PointMachineJson.A_C_AVERAGE : mPm1.PointMachineJson.A_C_AVERAGE / cACount;

                                mPm1.PointMachineJson.A_V_AVERAGE = vACount == 0 ? mPm1.PointMachineJson.A_V_AVERAGE : mPm1.PointMachineJson.A_V_AVERAGE / vACount;

                                mPm1.PointMachineJson.A_C_TIME = tACount == 0 ? mPm1.PointMachineJson.A_C_TIME : mPm1.PointMachineJson.A_C_TIME / tACount;

                                mPm1.PointMachineJson.B_C_AVERAGE = cBCount == 0 ? mPm1.PointMachineJson.B_C_AVERAGE : mPm1.PointMachineJson.B_C_AVERAGE / cBCount;

                                mPm1.PointMachineJson.B_V_AVERAGE = vBCount == 0 ? mPm1.PointMachineJson.B_V_AVERAGE : mPm1.PointMachineJson.B_V_AVERAGE / vBCount;

                                mPm1.PointMachineJson.B_C_TIME = tBCount == 0 ? mPm1.PointMachineJson.B_C_TIME : mPm1.PointMachineJson.B_C_TIME / tBCount;
                            }
                            if (range == "LastMonth")
                            {
                                mPm1.PointMachineJson.A_C_AVERAGE = cACount == 0 ? mPm1.PointMachineJson.A_C_AVERAGE : mPm1.PointMachineJson.A_C_AVERAGE / cACount;

                                mPm1.PointMachineJson.A_V_AVERAGE = vACount == 0 ? mPm1.PointMachineJson.A_V_AVERAGE : mPm1.PointMachineJson.A_V_AVERAGE / vACount;

                                mPm1.PointMachineJson.A_C_TIME = tACount == 0 ? mPm1.PointMachineJson.A_C_TIME : mPm1.PointMachineJson.A_C_TIME / tACount;

                                mPm1.PointMachineJson.B_C_AVERAGE = cBCount == 0 ? mPm1.PointMachineJson.B_C_AVERAGE : mPm1.PointMachineJson.B_C_AVERAGE / cBCount;

                                mPm1.PointMachineJson.B_V_AVERAGE = vBCount == 0 ? mPm1.PointMachineJson.B_V_AVERAGE : mPm1.PointMachineJson.B_V_AVERAGE / vBCount;

                                mPm1.PointMachineJson.B_C_TIME = tBCount == 0 ? mPm1.PointMachineJson.B_C_TIME : mPm1.PointMachineJson.B_C_TIME / tBCount;
                            }
                            if (range == "CustomeDate")
                            {
                                var diff = pointMachineData.EndDate.Subtract(pointMachineData.StartDate).TotalDays;
                                mPm1.PointMachineJson.A_C_AVERAGE = cACount == 0 ? mPm1.PointMachineJson.A_C_AVERAGE : mPm1.PointMachineJson.A_C_AVERAGE / cACount;

                                mPm1.PointMachineJson.A_V_AVERAGE = vACount == 0 ? mPm1.PointMachineJson.A_V_AVERAGE : mPm1.PointMachineJson.A_V_AVERAGE / vACount;
                                mPm1.PointMachineJson.A_C_TIME = tACount == 0 ? mPm1.PointMachineJson.A_C_TIME : mPm1.PointMachineJson.A_C_TIME / tACount;

                                mPm1.PointMachineJson.B_C_AVERAGE = cBCount == 0 ? mPm1.PointMachineJson.B_C_AVERAGE : mPm1.PointMachineJson.B_C_AVERAGE / cBCount;

                                mPm1.PointMachineJson.B_V_AVERAGE = vBCount == 0 ? mPm1.PointMachineJson.B_V_AVERAGE : mPm1.PointMachineJson.B_V_AVERAGE / vBCount;

                                mPm1.PointMachineJson.B_C_TIME = tBCount == 0 ? mPm1.PointMachineJson.B_C_TIME : mPm1.PointMachineJson.B_C_TIME / tBCount;
                            }
                            mPm1.Date = Convert.ToDateTime(mPm1.Date).ToString("dd/MM/yyyy");
                            mPointMachines.Add(mPm1);
                        }
                    }
                }
                else
                {
                    mPointMachines = mPointMachineDatas;
                }
            }
            return Json(mPointMachines, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetPointMachineDateRange(int assetId, string range, string type, int assetTypeId, int siteId, string operation, string sd, string ed)
        {
            PointMachineData pointMachineData = new PointMachineData();
            pointMachineData.AssetId = assetId;
            pointMachineData.Range = range;
            pointMachineData.Type = type;
            pointMachineData.AssetTypeId = assetTypeId;
            pointMachineData.SiteId = siteId;
            if (range == "CustomeDate")
            {
                pointMachineData.StartDate = Convert.ToDateTime(sd);
                pointMachineData.EndDate = Convert.ToDateTime(ed);
            }


            List<PointMachineData> mPointMachineDatas = new List<PointMachineData>();
            List<PointMachineData> mPointMachines = new List<PointMachineData>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(pointMachineData);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GetPointMachineListRange"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mPointMachineDatas = JsonConvert.DeserializeObject<List<PointMachineData>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            if (mPointMachineDatas != null && mPointMachineDatas.Count > 0 && (range == "Last7Day" || range == "LastMonth" || range == "CustomeDate"))
            {
                List<int> assetIds = mPointMachineDatas.Select(x => x.AssetId).Distinct().ToList();
                string op = string.Empty;
                string op1 = string.Empty;
                if (operation == "Operating Current")
                {
                    op = "A_C";
                    op1 = "B_C";
                }
                if (operation == "Operating Time")
                {
                    op = "A_C_Time";
                    op1 = "B_C_Time";
                }
                if (operation == "Operating Voltage")
                {
                    op = "A_V_AVERAGE";
                    op1 = "B_V_AVERAGE";
                }
                foreach (var id in assetIds)
                {
                    var pointMA = mPointMachineDatas.Where(x => x.AssetId == id && x.Type == op).FirstOrDefault();
                    int countA = mPointMachineDatas.Where(x => x.AssetId == id && x.Type == op).Count();
                    if (pointMA != null)
                    {
                        var avg = mPointMachineDatas.Where(x => x.AssetId == id && x.Type == op).Select(x => x.Average).Sum();

                        if (range == "Last7Day")
                        {
                            pointMA.Average = avg / countA;
                        }
                        if (range == "LastMonth")
                        {
                            pointMA.Average = avg / countA;
                        }
                        if (range == "CustomeDate")
                        {
                            var diff = pointMachineData.EndDate.Subtract(pointMachineData.StartDate).TotalDays;
                            pointMA.Average = avg / countA;
                        }
                    }
                    else
                    {
                        pointMA = new PointMachineData();
                        pointMA.Type = op;
                        pointMA.Average = 0;
                        pointMA.AssetType = mPointMachineDatas.Where(x => x.AssetId == id).Select(x => x.AssetType).FirstOrDefault();
                    }
                    mPointMachines.Add(pointMA);

                    var pointMb = mPointMachineDatas.Where(x => x.AssetId == id && x.Type == op1).FirstOrDefault();
                    int countB = mPointMachineDatas.Where(x => x.AssetId == id && x.Type == op1).Count();
                    if (pointMb != null)
                    {
                        var avg = mPointMachineDatas.Where(x => x.AssetId == id && x.Type == op1).Select(x => x.Average).Sum();
                        if (range == "Last7Day")
                        {
                            pointMb.Average = avg / countB;
                        }
                        if (range == "LastMonth")
                        {
                            pointMb.Average = avg / countB;
                        }
                        if (range == "CustomeDate")
                        {
                            var diff = pointMachineData.EndDate.Subtract(pointMachineData.StartDate).TotalDays;
                            pointMb.Average = avg / countB;
                        }
                    }
                    else
                    {
                        pointMb = new PointMachineData();
                        pointMb.Type = op1;
                        pointMb.Average = 0;
                        pointMb.AssetType = mPointMachineDatas.Where(x => x.AssetId == id).Select(x => x.AssetType).FirstOrDefault();
                    }
                    mPointMachines.Add(pointMb);
                }
                //foreach (var item in mPointMachineDatas)
                //{
                //    item.PointMachineJson.A_C_AVERAGE = (double)System.Math.Round(item.PointMachineJson.A_C_AVERAGE, 2);
                //    item.PointMachineJson.B_C_AVERAGE = (double)System.Math.Round(item.PointMachineJson.B_C_AVERAGE, 2);
                //    item.PointMachineJson.A_V_AVERAGE = (double)System.Math.Round(item.PointMachineJson.A_V_AVERAGE, 2);
                //    item.PointMachineJson.B_V_AVERAGE = (double)System.Math.Round(item.PointMachineJson.B_V_AVERAGE, 2);
                //}
            }
            else
            {
                mPointMachines = mPointMachineDatas;
            }
            return Json(mPointMachines, JsonRequestBehavior.AllowGet);
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

    }
}