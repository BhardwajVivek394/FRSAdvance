using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Mvc;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class SMSLogController : Controller
    {
        // GET: SMSLog
        private readonly ISiteService siteService;
        private readonly IAssetAttributeService assetAttributeService;
        private readonly IAssetTypeService assetTypeService;
        private readonly IAssetService assetService;
        public SMSLogController(ISiteService siteService, IAssetAttributeService assetAttributeService, IAssetTypeService assetTypeService, IAssetService assetService)
        {
            this.siteService = siteService;
            this.assetAttributeService = assetAttributeService;
            this.assetTypeService = assetTypeService;
            this.assetService = assetService;
        }

        public ActionResult Index()
        {
            SMSLogLister mSMSLogLister = new SMSLogLister();
            mSMSLogLister.SearchCriteria.IsSmsLogActive = true;
            return View(mSMSLogLister);
        }

        public PartialViewResult SMSLogList(SMSLogLister mSMSLogLister)
        {
            mSMSLogLister.Pager.Take = mSMSLogLister.Pager.PageSize;
            mSMSLogLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
            mSMSLogLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;

            try
            {
                if (mSMSLogLister.SearchCriteria.IsActive == "1")
                {
                    mSMSLogLister.SearchCriteria.IsSmsLogActive = true;
                }
                else
                {
                    mSMSLogLister.SearchCriteria.IsSmsLogActive = false;
                }
                int assetypeId = mSMSLogLister.SearchCriteria.AssetTypeId;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSMSLogLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SMSLog/GetAllSMSLogLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSMSLogLister = JsonConvert.DeserializeObject<SMSLogLister>(jsonString);
                        mSMSLogLister.SearchCriteria.AssetTypeId = assetypeId;
                        if (mSMSLogLister != null && mSMSLogLister.mSMSLogs != null)
                        {
                            TempData.Remove("TempSMSLogLister");
                            TempData["TempSMSLogLister"] = mSMSLogLister.mSMSLogs;
                            TempData.Keep();
                        }
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
            GetAllDivisions();
            return PartialView("_SMSLogListPartial", mSMSLogLister);
        }

        public List<FamilyTrack> GetFamilyTrack(int assetId)
        {
            var mFamilyTracks = new List<FamilyTrack>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"FamilyTrack/AssetId/{assetId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mFamilyTracks = JsonConvert.DeserializeObject<List<FamilyTrack>>(jsonString);
                    }
                    else
                    {
                        //  result = false;
                    }
                }
            }
            catch (Exception ex)
            {

            }
            return mFamilyTracks;

        }

        public ActionResult DownloadSMSLog()
        {
            var tempSMSLog = TempData.Peek("TempSMSLogLister");
            if (tempSMSLog != null)
            {
                var csv = new StringBuilder();
                var socsvstring = string.Empty;
                var mSMSLogs = tempSMSLog as List<SMSLog>;
                if (mSMSLogs != null && mSMSLogs.Count > 0)
                {
                    socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8}", "Site Name", "Asset Type", "Asset Name", "Alert Type", "Current Value", "Message", "Time Stamp", "Active", " Remark");

                    csv.AppendLine(socsvstring);
                    string type = string.Empty;
                    foreach (var item in mSMSLogs.GroupBy(x => x.AssetTypeName).ToList())
                    {

                        if (type != item.Key)
                        {
                            socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8}", item.Key, "", "", "", "", "", "", "", "");
                            csv.AppendLine(socsvstring);
                        }
                        foreach (var val in item.ToList())
                        {
                            string value = string.Empty;
                            var isSmsLogActive = "";
                            string strMessage = "";
                            if (val.IsSmsLogActive.Value)
                                isSmsLogActive = "Active";
                            else
                                isSmsLogActive = "InActive";

                            if (val.Message.IsNotNullOrEmpty())
                            {
                                strMessage = Regex.Replace(val.Message.RemoveComma(), @"\t|\n|\r", "");
                            }
                            if (item.Key == "ELD")
                                val.AlertName = "ELD";

                            if (strMessage.IsNotNullOrEmpty())
                            {

                                try
                                {
                                    var splitstr = strMessage.Split(new string[] { "current val", "current cal", "current charging", "current vf", "Avg current : " }, StringSplitOptions.None);
                                    if (splitstr != null && splitstr.Length > 1)
                                    {
                                        string[] splitWithSpace = new string[splitstr.Length];
                                        if (strMessage.Contains("Avg current :"))
                                        {
                                            splitWithSpace = splitstr[2].Trim().Split(' ');
                                        }
                                        else
                                        {

                                            splitWithSpace = splitstr[1].Trim().Split(' ');
                                        }
                                        if (splitWithSpace != null && splitWithSpace.Length > 0)
                                        {

                                            value = Regex.Match(splitWithSpace[0], @"\d+.+\d").Value;
                                            if (value.IsNullOrEmpty())
                                                value = Regex.Match(splitWithSpace[0], @"\d+").Value;
                                        }
                                    }
                                }
                                catch (Exception)
                                {
                                }

                            }

                            socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8}", val.SiteName.RemoveComma(), val.AssetTypeName.RemoveComma(), val.AssetName.RemoveComma(), val.AlertName.RemoveComma(), value, strMessage, val.TimeStamp.ToString("dd/MM/yyyy HH:mm").RemoveComma(), isSmsLogActive.RemoveComma(),
               val.Remark.RemoveComma());
                            csv.AppendLine(socsvstring);

                        }
                    }

                    Response.Clear();
                    Response.Buffer = true;
                    Response.AddHeader("content-disposition", "attachment;filename=SMSLog" + DateTime.Now.Ticks + ".csv");
                    Response.Charset = "utf-8";
                    Response.ContentType = "text/csv";
                    Response.Output.Write(csv);
                    Response.Flush();
                    Response.End();
                }
                else
                    return RedirectToAction("Index");
            }
            else
                return RedirectToAction("Index");


            return View();
        }

        public ActionResult Alert(int assetId, bool? isHide)
        {
            SMSLogLister mSMSLogLister = new SMSLogLister();
            mSMSLogLister.SearchCriteria.Id = assetId;
            mSMSLogLister.Pager.Take = -1;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSMSLogLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SMSLog/GetAlertLogById"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSMSLogLister = JsonConvert.DeserializeObject<SMSLogLister>(jsonString);
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
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Something went wrong!";
            }
            finally
            {
                ProbabilityLister mProbabilityLister = new ProbabilityLister();
                mProbabilityLister.SearchCriteria.AssetId = assetId;
                var fromDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
                mProbabilityLister.SearchCriteria.StartDate = fromDate;
                mProbabilityLister.SearchCriteria.EndDate = DateTime.Now;
                mProbabilityLister = GetProbabilityLister(mProbabilityLister);
                if (mProbabilityLister != null && mProbabilityLister.Probability != null && mProbabilityLister.Probability.Count > 0)
                {
                    ViewBag.Probability = mProbabilityLister.Probability;
                }


                var mAsset = assetService.Get(assetId);

                if (mAsset != null && mAsset.Id > 0 && mAsset.AssetTypeId == (int)Utility.Utility.AssetType.POINT_MACHINE)
                {
                    var mPointMachineEvents = GetPointMachineEvent(assetId);
                    if (mPointMachineEvents != null && mPointMachineEvents.Count > 0)
                    {
                        ViewBag.PointMachineEvent = mPointMachineEvents;
                    }
                }


                if (mAsset != null && mAsset.Id > 0 && mAsset.IsDatalogger && (mAsset.AssetTypeId == (int)Utility.Utility.AssetType.TRACK || mAsset.AssetTypeId == (int)Utility.Utility.AssetType.SIGNAL || mAsset.AssetTypeId == (int)Utility.Utility.AssetType.POINT_MACHINE))
                {
                    var mSearchCriteria = new SearchCriteria();
                    //mSearchCriteria.TimeStamp = DateTime.Now;

                    mSearchCriteria.SearchDate = DateTime.Now.ToString("d-M-yyyy");

                    mSearchCriteria.SiteId = mAsset.SiteId;
                    mSearchCriteria.AssetId = mAsset.Id;
                    var mDatalogger = DataLoggerEvent(mSearchCriteria);
                    if (mDatalogger != null && mDatalogger.Count > 0)
                    {
                        ViewBag.Datalogger = mDatalogger;
                    }
                }

            }
            if (isHide != null && isHide == true)
            {
                ViewBag.Hide = "none";
            }
            return PartialView("~/Views/SMSLog/_Alert.cshtml", mSMSLogLister);
        }

     

        public ActionResult GetActiveAlerts(Domain.SMSLog mSmsLog)
        {
            SMSLogLister mSMSLogLister = new SMSLogLister();
            mSMSLogLister.Pager.Take = -1;
            mSMSLogLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
            mSMSLogLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
            mSMSLogLister.SearchCriteria = mSmsLog;
            var fromDate = new DateTime(mSMSLogLister.SearchCriteria.TimeStamp.Year, mSMSLogLister.SearchCriteria.TimeStamp.Month, 1);
            mSMSLogLister.SearchCriteria.FromDate = fromDate;
            mSMSLogLister.SearchCriteria.ToDate = DateTime.Now;
            mSMSLogLister.SearchCriteria.IsSmsLogActive = true;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
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
            return PartialView("~/Views/Reporting/_GetAlertSMSLogs.cshtml", mSMSLogLister.mSMSLogs);
            // return Json(mSMSLogLister,JsonRequestBehavior.AllowGet);
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

        public ActionResult DeleteSMSLogById(int id)
        {
            bool result = false;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("SMSLog/DeleteSmsLog/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        result = JsonConvert.DeserializeObject<bool>(jsonString);
                    }
                    else
                    {
                        result = false;
                    }
                }
            }
            catch (Exception ex)
            {

            }
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        public ActionResult DeleteSMSLogByIds(List<int> ids)
        {
            bool result = false;
            try
            {
                foreach (var id in ids)
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = hcf.client.GetAsync(String.Format("SMSLog/DeleteSmsLog/{0}", id)).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            result = JsonConvert.DeserializeObject<bool>(jsonString);
                        }
                        else
                        {
                            result = false;
                        }
                    }
                }

            }
            catch (Exception ex)
            {

            }
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        public ActionResult DownloadReportCsv(int siteId, int assetId, DateTime timeStamp)
        {
            try
            {
                Asset mAsset = new Asset();
                mAsset.Id = assetId;
                mAsset.SortDirection = "Graph";
                mAsset.IsGraphLoad = true;
                mAsset.StartDate = timeStamp.AddHours(-1).ToShortDateString();
                mAsset.StartTime = timeStamp.AddHours(-1).ToShortTimeString();

                mAsset.EndDate = timeStamp.AddHours(1).ToShortDateString();
                mAsset.EndTime = timeStamp.AddHours(1).ToShortTimeString();
                mAsset.SiteId = siteId;

                var mAssets = new List<Asset>();
                mAssets.Add(mAsset);

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssets);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GenerateGraph"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        var assets = JsonConvert.DeserializeObject<List<Asset>>(jsonString);
                        //DownloadCsv(mAsset, assets);

                        string StartDate = mAssets.FirstOrDefault().StartDate;
                        string EndDate = mAssets.FirstOrDefault().EndDate;
                        Response.Clear();
                        Response.Buffer = true;
                        Response.AddHeader("content-disposition", "attachment;filename=SiteReport(" + StartDate + " to " + EndDate + ").csv");
                        Response.Charset = "";
                        Response.ContentType = "application/text";
                        Response.Output.Write(assetAttributeService.PrepareCSV(assets));
                        Response.Flush();
                        Response.End();
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
            return View();
        }

        public ActionResult DownloadTprReportCsv(int siteId, int assetId, DateTime timeStamp)
        {
            try
            {
                Asset mAsset = new Asset();
                mAsset.Id = assetId;
                mAsset.SortDirection = "Graph";
                mAsset.IsGraphLoad = true;
                mAsset.StartDate = timeStamp.AddHours(-1).ToShortDateString();
                mAsset.StartTime = timeStamp.AddHours(-1).ToShortTimeString();

                mAsset.EndDate = timeStamp.AddHours(1).ToShortDateString();
                mAsset.EndTime = timeStamp.AddHours(1).ToShortTimeString();
                mAsset.SiteId = siteId;

                mAsset.assetAttributes.Add(new AssetAttribute() { Id = 6 });
                var mFamilyTracks = GetFamilyTrack(assetId);

                var mAssets = new List<Asset>();
                mAssets.Add(mAsset);
                if (mFamilyTracks != null && mFamilyTracks.Count > 0)
                {
                    foreach (var mFamilyTrack in mFamilyTracks)
                    {
                        var assetAttributes = new List<AssetAttribute>();
                        assetAttributes.Add(new AssetAttribute() { Id = 6 });
                        mAssets.Add(new Asset()
                        {
                            Id = mFamilyTrack.TrackFamilyId,
                            SortDirection = "Graph",
                            IsGraphLoad = true,
                            SiteId = siteId,
                            StartDate = timeStamp.AddHours(-1).ToShortDateString(),
                            StartTime = timeStamp.AddHours(-1).ToShortTimeString(),
                            EndDate = timeStamp.AddHours(1).ToShortDateString(),
                            EndTime = timeStamp.AddHours(1).ToShortTimeString(),
                            assetAttributes = assetAttributes

                        });
                    }

                }
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

                            string StartDate = mAssets.FirstOrDefault().StartDate;
                            string EndDate = mAssets.FirstOrDefault().EndDate;

                            Response.Clear();
                            Response.Buffer = true;
                            Response.AddHeader("content-disposition", $"attachment;filename=SiteReport-{Guid.NewGuid().ToString()}(" + StartDate + " to " + EndDate + ").csv");
                            Response.Charset = "";
                            Response.ContentType = "application/text";
                            Response.Output.Write(assetAttributeService.PrepareCSV(assets));
                            Response.Flush();
                            Response.End();
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
            return View();
        }

        public ActionResult DownloadTrainOnTrack(int assetId, string date)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync($"SMSLog/DownloadTrainOnTrack/{assetId}/{date}").Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                        if (csvbytes != null && csvbytes.Length > 0)
                        {
                            return File(csvbytes, "text/csv", $"{assetId}-TrainOnTrack.csv");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
            }
            return RedirectToAction("Index");
        }

        public void DownloadCsv(Asset asset, List<Asset> mAssets)
        {
            if (mAssets != null && mAssets.Count > 0)
            {
                //var mAssets = TempData.Peek("AssetsSearch") as List<Asset>;
                //TempData.Keep();
                string StartDate = asset.StartDate;
                string EndDate = asset.EndDate;
                string csv = string.Empty;
                csv += "SiteName,";
                csv += "Date,";
                csv += "AssetType,";
                csv += "AssetName,";

                foreach (var mAsset in mAssets)
                {
                    foreach (var assetAttribute in mAsset.assetAttributes)
                    {
                        csv += assetAttribute.AssetTypeName + ">" + @assetAttribute.AssetName + ">" + @assetAttribute.Title + ',';
                    }
                }

                csv += "\r\n";
                foreach (var mAsset in mAssets)
                {
                    var allAssetAttributes = assetAttributeService.GetAssetAttributesBy(mAsset.AssetTypeId);

                    foreach (var multipleLog in mAsset.MultipleLog.OrderByDescending(x => x.TimeStamp))
                    {
                        var str = string.Empty;
                        var assetAttributes = assetAttributeService.GetGraphAttribute(mAsset.assetAttributes, allAssetAttributes, multipleLog.CsvData);
                        if (assetAttributes != null && assetAttributes.Count > 0)
                        {
                            foreach (var assetAttribute in assetAttributes)
                            {
                                str += $"{assetAttribute.Data.FirstOrDefault()},";

                            }
                        }
                        string newRow = $"{mAsset.SiteName},{multipleLog.TimeStamp.ToString()},{mAsset.AssetTypeName},{mAsset.Name},{str}";
                        //Add the Data rows.
                        csv += newRow;
                        //Add new line.
                        csv += "\r\n";
                    }
                }


                Response.Clear();
                Response.Buffer = true;
                Response.AddHeader("content-disposition", "attachment;filename=SiteReport(" + StartDate + " to " + EndDate + ").csv");
                Response.Charset = "";
                Response.ContentType = "application/text";
                Response.Output.Write(csv);
                Response.Flush();
                Response.End();
            }
        }

        public ActionResult UpdateRemarks(SMSLog mSMSLog)
        {
            bool result = false;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSMSLog);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format($"SMSLog/{mSMSLog.Id}"), str).Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        //string jsonString = response.Content.ReadAsStringAsync().Result;
                        result = true;
                    }
                    else
                    {
                        result = false;
                    }
                }
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
                result = false;
            }
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetDeadband(int id, int siteId, int assetId, string timeStemp, bool isGear)
        {
            var mDeadbands = new List<Deadband>();
            var dtTimeStemp = Convert.ToDateTime(timeStemp);
            ViewBag.Id = id;
            ViewBag.AssetId = assetId;
            ViewBag.TimeStemp = timeStemp;
            ViewBag.IsGear = isGear;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"SMSLog/GetDeadband/SiteId/{siteId}/AssetId/{assetId}/TimeStamp/{dtTimeStemp.Ticks}/IsGear/{isGear}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mDeadbands = JsonConvert.DeserializeObject<List<Deadband>>(jsonString);
                    }
                    else
                    {
                        //  result = false;
                    }
                }
            }
            catch (Exception ex)
            {

            }

            return PartialView("_DeadbandListPartial", mDeadbands);
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
                        if (ClsHttpContent.LoginUser.RoleId == (int)Utility.Utility.Role.User)
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
                            mDivisions.Insert(0, new Division { Id = 0, Name = "Select" });
                        }
                        else
                        {
                            mDivisions = JsonConvert.DeserializeObject<List<Division>>(jsonString);
                            mDivisions.Insert(0, new Division { Id = 0, Name = "Select" });
                        }

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
            ViewBag.Divisions = new SelectList(mDivisions.ToList(), "Id", "Name");

            return mDivisions;
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

        public JsonResult GetAllSites()
        {
            var sites = new List<Site>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Site/GetSiteByUserId/UserId/{ClsHttpContent.LoginUser.Id}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        sites = JsonConvert.DeserializeObject<List<Site>>(jsonString);
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
            return Json(sites, JsonRequestBehavior.AllowGet);
        }

        private ProbabilityLister GetProbabilityLister(ProbabilityLister mProbabilityLister)
        {
            mProbabilityLister.Pager.Take = -1;
            mProbabilityLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
            mProbabilityLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;

            if (mProbabilityLister != null && mProbabilityLister.SearchCriteria != null && mProbabilityLister.SearchCriteria.TimeStamp != null && mProbabilityLister.SearchCriteria.TimeStamp == DateTime.MinValue)
                mProbabilityLister.SearchCriteria.TimeStamp = DateTime.Now;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mProbabilityLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SiteKeeping/GetProbability"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mProbabilityLister = JsonConvert.DeserializeObject<ProbabilityLister>(jsonString);
                        if (mProbabilityLister != null && mProbabilityLister.Probability != null && mProbabilityLister.Probability.Count > 0)
                            ChangeProbabilityName(mProbabilityLister.Probability);

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
            return mProbabilityLister;
        }

        #region SMSLog CSV
        public ActionResult FailureAlert()
        {
            SMSLogLister mSMSLogLister = new SMSLogLister();

            mSMSLogLister.SearchCriteria.TimeStamp = DateTime.Now;
            mSMSLogLister.SearchCriteria.IsSmsLogActive = true;
            mSMSLogLister.SearchCriteria.Validity = true;
            var fromDate = new DateTime(mSMSLogLister.SearchCriteria.TimeStamp.Year, mSMSLogLister.SearchCriteria.TimeStamp.Month, 1);
            mSMSLogLister.SearchCriteria.FromDate = fromDate;
            mSMSLogLister.SearchCriteria.ToDate = DateTime.Now;
            return View(mSMSLogLister);
        }

        public ActionResult _FailureAlert(SMSLogLister mSMSLogLister)
        {
            mSMSLogLister.Pager.Take = -1;
            mSMSLogLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
            mSMSLogLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;

            if (mSMSLogLister != null && mSMSLogLister.SearchCriteria != null && mSMSLogLister.SearchCriteria.TimeStamp != null && mSMSLogLister.SearchCriteria.TimeStamp == DateTime.MinValue)
            {
                mSMSLogLister.SearchCriteria.TimeStamp = DateTime.Now;
            }

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSMSLogLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SMSLog/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSMSLogLister = JsonConvert.DeserializeObject<SMSLogLister>(jsonString);
                        if (mSMSLogLister != null && mSMSLogLister.mSMSLogs != null)
                        {
                            var smslogs = new List<SMSLog>();

                            foreach (var item in mSMSLogLister.AssetTypes)
                            {
                                foreach (var site in mSMSLogLister.mSMSLogs.Where(x => x.AssetTypeId == item.Id).GroupBy(x => new { x.SiteId, x.SiteName }).Select(x => x.Key).ToList())
                                {
                                    foreach (var alert in mSMSLogLister.mSMSLogs.Where(x => x.AssetTypeId == item.Id && x.SiteId == site.SiteId && x.AlertName != null && x.AlertName != string.Empty).GroupBy(x => x.AlertName).Select(x => x.Key).ToList())
                                    {
                                        foreach (var asset in mSMSLogLister.mSMSLogs.Where(x => x.AssetTypeId == item.Id && x.SiteId == site.SiteId && x.AlertName == alert).GroupBy(x => new { x.AssetId, x.AssetName }).Select(g => new { g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Select(x => x.Key).ToList())
                                        {
                                            var count = mSMSLogLister.mSMSLogs.Where(x => x.AssetTypeId == item.Id && x.SiteId == site.SiteId && x.AssetId == asset.AssetId && x.AlertName == alert).Count();
                                            if (count > 1)
                                            {
                                                var startDate = new DateTime(mSMSLogLister.SearchCriteria.TimeStamp.Year, mSMSLogLister.SearchCriteria.TimeStamp.Month, 1);

                                                var toDate = new DateTime(mSMSLogLister.SearchCriteria.TimeStamp.Year, mSMSLogLister.SearchCriteria.TimeStamp.Month, 15);

                                                var lastDayOfMonth = startDate.AddMonths(1).AddDays(-1);

                                                //var smsCount = mSMSLogLister.mSMSLogs.Where(x => x.AssetTypeId == item.Id && x.SiteId == site.SiteId && x.AssetId == asset.AssetId && x.AlertName == alert && x.TimeStamp.Date >= startDate.Date && x.ToDate <= toDate.Date).OrderByDescending(x => x.TimeStamp).Count();

                                                var smsfirst15Days = mSMSLogLister.mSMSLogs.Where(x => x.AssetTypeId == item.Id && x.SiteId == site.SiteId && x.AssetId == asset.AssetId && x.AlertName == alert && x.TimeStamp.Date >= startDate.Date && x.TimeStamp.Date <= toDate.Date).OrderByDescending(x => x.TimeStamp).FirstOrDefault();
                                                if (smsfirst15Days != null && smsfirst15Days.Id > 0)
                                                {
                                                    smsfirst15Days.isTenDays = true;
                                                    smsfirst15Days.TenDaysCount = 0;
                                                    smslogs.Add(smsfirst15Days);
                                                }


                                                var smsSecond15Days = mSMSLogLister.mSMSLogs.Where(x => x.AssetTypeId == item.Id && x.SiteId == site.SiteId && x.AssetId == asset.AssetId && x.AlertName == alert && x.TimeStamp.Date > toDate.Date && x.TimeStamp.Date <= lastDayOfMonth.Date).OrderByDescending(x => x.TimeStamp).FirstOrDefault();
                                                if (smsSecond15Days != null && smsSecond15Days.Id > 0)
                                                {
                                                    smsSecond15Days.isTenDays = true;
                                                    smsSecond15Days.TenDaysCount = 0;
                                                    smslogs.Add(smsSecond15Days);
                                                }

                                                //var startDate = mSMSLogLister.SearchCriteria.ToDate.AddDays(-10);
                                                //var toDate = mSMSLogLister.SearchCriteria.ToDate;

                                                //bool isTenDays = true;
                                                //for (DateTime dateTime = Convert.ToDateTime(startDate); dateTime <= Convert.ToDateTime(toDate); dateTime += TimeSpan.FromDays(1))
                                                //{
                                                //    var dsdds = mSMSLogLister.mSMSLogs.Where(x => x.AssetTypeId == item.Id && x.SiteId == site.SiteId && x.AssetId == asset.AssetId && x.AlertName == alert && x.TimeStamp.Date == dateTime.Date).Count();
                                                //    if (dsdds > 0)
                                                //    {

                                                //    }
                                                //    else
                                                //    {
                                                //        isTenDays = false;
                                                //    }
                                                //}

                                                //if (isTenDays)
                                                //{
                                                //    var smsCount = mSMSLogLister.mSMSLogs.Where(x => x.AssetTypeId == item.Id && x.SiteId == site.SiteId && x.AssetId == asset.AssetId && x.AlertName == alert).OrderByDescending(x => x.TimeStamp).Count();

                                                //    var sms = mSMSLogLister.mSMSLogs.Where(x => x.AssetTypeId == item.Id && x.SiteId == site.SiteId && x.AssetId == asset.AssetId && x.AlertName == alert).OrderByDescending(x => x.TimeStamp).FirstOrDefault();
                                                //    if (sms != null && sms.Id > 0)
                                                //    {
                                                //        sms.isTenDays = true;
                                                //        sms.TenDaysCount = smsCount;
                                                //        smslogs.Add(sms);
                                                //    }
                                                //}
                                                //else
                                                //{
                                                //    foreach (var smsLog in mSMSLogLister.mSMSLogs.Where(x => x.AssetTypeId == item.Id && x.SiteId == site.SiteId && x.AssetId == asset.AssetId && x.AlertName == alert).ToList())
                                                //    {
                                                //        smslogs.Add(smsLog);
                                                //    }
                                                //}
                                            }
                                            else
                                            {
                                                foreach (var smsLog in mSMSLogLister.mSMSLogs.Where(x => x.AssetTypeId == item.Id && x.SiteId == site.SiteId && x.AssetId == asset.AssetId && x.AlertName == alert).ToList())
                                                {
                                                    smslogs.Add(smsLog);
                                                }
                                            }


                                        }
                                    }
                                }

                            }

                            if (smslogs != null && smslogs.Count > 0)
                            {
                                mSMSLogLister.mSMSLogs = smslogs;
                            }

                            TempData.Remove("TempSMSLogLister");
                            TempData["TempSMSLogLister"] = mSMSLogLister.mSMSLogs;
                            TempData.Keep();
                        }

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
            GetAllDivisions();
            ViewBag.AssetTypes = new SelectList(assetTypeService.GetAll(), "Id", "Name");
            ViewBag.Alerts = new SelectList(GetAlert(), "Id", "AlertName");


            return PartialView(mSMSLogLister);
        }

        public ActionResult UpdateSMSLog(SMSLog mSMSLog)
        {
            try
            {
                mSMSLog.UserId = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSMSLog);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format($"SMSLog/{mSMSLog.Id}"), str).Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                    }
                }
            }
            catch (Exception)
            {
            }
            return Json("");
        }

        //public ActionResult DeleteSMSLog(SMSLog mSMSLog)
        //{
        //    dynamic data = new ExpandoObject();
        //    try
        //    {
        //        mSMSLog.UserId = ClsHttpContent.LoginUser.Id;
        //        using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
        //        {
        //            var jsonStr = JsonConvert.SerializeObject(mSMSLog);
        //            StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
        //            var response = hcf.client.PostAsync(String.Format("SMSLogCSV/DeleteSMSLog"), str).Result;
        //            if (response.StatusCode == HttpStatusCode.NoContent)
        //            {
        //                data = new { type = "success", result = "Alert has been deleted." };
        //            }
        //            else
        //            {
        //                data = new { type = "error", result = "Internal server error." };
        //            }
        //        }
        //    }
        //    catch (Exception)
        //    {
        //        data = new { type = "error", result = "Internal server error." };
        //    }


        //    return Json(data, JsonRequestBehavior.AllowGet);
        //}

        //public ActionResult DeleteAllSMSLog(List<SMSLog> mSMSLogs)
        //{
        //    if (mSMSLogs != null && mSMSLogs.Count > 0)
        //    {
        //        foreach (var mSMSLog in mSMSLogs)
        //        {
        //            try
        //            {
        //                mSMSLog.UserId = ClsHttpContent.LoginUser.Id;
        //                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
        //                {
        //                    var jsonStr = JsonConvert.SerializeObject(mSMSLog);
        //                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
        //                    var response = hcf.client.PostAsync(String.Format("SMSLogCSV/DeleteSMSLog"), str).Result;
        //                    if (response.StatusCode == HttpStatusCode.NoContent)
        //                    {
        //                    }
        //                }
        //            }
        //            catch (Exception)
        //            {
        //            }
        //        }
        //    }

        //    return Json("");
        //}

        public ActionResult GetFailureAlertRemark(SMSLog mSMSLog)
        {
            try
            {
                var tempSMSLog = TempData.Peek("TempSMSLogLister");
                if (tempSMSLog != null)
                {
                    var csv = new StringBuilder();
                    var socsvstring = string.Empty;
                    var mSMSLogs = tempSMSLog as List<SMSLog>;
                    if (mSMSLogs != null && mSMSLogs.Count > 0)
                    {
                        var sms = mSMSLogs.Where(x => x.AssetId == mSMSLog.AssetId && x.AlertId == mSMSLog.AlertId && x.SiteId == mSMSLog.SiteId).FirstOrDefault();
                        if (sms != null && sms.mSMSLogRemarks != null && sms.mSMSLogRemarks.Count > 0)
                            mSMSLog.mSMSLogRemarks.AddRange(sms.mSMSLogRemarks);
                    }
                }
            }
            catch (Exception)
            {
            }
            return PartialView("_FailureAlertRemrk", mSMSLog);
        }

        public ActionResult _ProbabilityAlertList(SearchCriteria mSearchCriteria)
        {
            return PartialView(GetProbabilityAlert(mSearchCriteria));

        }

        public ActionResult _TenDaysAlertAlertList(SearchCriteria mSearchCriteria)
        {
            var mSMSLogLister = new SMSLogLister();
            mSMSLogLister.SearchCriteria.SiteId = mSearchCriteria.SiteId;
            mSMSLogLister.SearchCriteria.AssetId = mSearchCriteria.AssetId;
            mSMSLogLister.SearchCriteria.AlertTypeId = mSearchCriteria.AlertTypeId;

            var startDate = new DateTime(mSearchCriteria.TimeStamp.Year, mSearchCriteria.TimeStamp.Month, 1);
            var fifteenDate = new DateTime(mSearchCriteria.TimeStamp.Year, mSearchCriteria.TimeStamp.Month, 15);
            var lastDayOfMonth = startDate.AddMonths(1).AddDays(-1);

            if (mSearchCriteria.TimeStamp >= startDate.Date && mSearchCriteria.TimeStamp <= fifteenDate.Date)
            {
                mSMSLogLister.SearchCriteria.FromDate = startDate;
                mSMSLogLister.SearchCriteria.ToDate = fifteenDate;
            }

            if (mSearchCriteria.TimeStamp > fifteenDate.Date && mSearchCriteria.TimeStamp <= lastDayOfMonth.Date)
            {
                mSMSLogLister.SearchCriteria.FromDate = fifteenDate.AddDays(1);
                mSMSLogLister.SearchCriteria.ToDate = lastDayOfMonth;
            }


            mSMSLogLister.Pager.Take = -1;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSMSLogLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SMSLog/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSMSLogLister = JsonConvert.DeserializeObject<SMSLogLister>(jsonString);

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

            return PartialView("_ProbabilityAlertList", mSMSLogLister);

        }

        public ActionResult _ProbabilityCSVList(SearchCriteria mSearchCriteria)
        {
            return PartialView(GetProbability(mSearchCriteria));
        }

        public void ChangeProbabilityName(List<Probability> mProbabilites)
        {
            try
            {
                if (mProbabilites != null && mProbabilites.Count > 0)
                {
                    foreach (var mProbability in mProbabilites)
                    {
                        var strings = mProbability.Category.Split('(');
                        if (strings != null && strings.Count() > 0)
                        {
                            var firstString = strings[0];
                            if (firstString == "SignalMoment")
                            {
                                firstString = "Unstable Current";
                                mProbability.Category = $"{firstString}";
                                if (strings.Count() > 1)
                                {
                                    var secondString = strings[1];
                                    if (secondString.IsNotNullOrEmpty())
                                        mProbability.Category += $"({secondString}";
                                }
                            }

                            if (firstString == "TPRMoment")
                            {
                                firstString = "Unstable Voltage";
                                mProbability.Category = $"{firstString}";
                                if (strings.Count() > 1)
                                {
                                    var secondString = strings[1];
                                    if (secondString.IsNotNullOrEmpty())
                                        mProbability.Category += $"({secondString}";
                                }
                            }

                            if (firstString == "PointMachineMoment")
                            {
                                firstString = "IRegular Signature";
                                mProbability.Category = $"{firstString}";
                                if (strings.Count() > 1)
                                {
                                    var secondString = strings[1];
                                    if (secondString.IsNotNullOrEmpty())
                                        mProbability.Category += $"({secondString}";
                                }
                            }

                            if (firstString == "TrainMomentLog")
                            {
                                firstString = "Prone To Shorting";
                                mProbability.Category = $"{firstString}";
                                if (strings.Count() > 1)
                                {
                                    var secondString = strings[1];
                                    if (secondString.IsNotNullOrEmpty())
                                        mProbability.Category += $"({secondString}";
                                }
                            }



                        }

                    }
                }

            }
            catch (Exception ex)
            {
            }
        }

        public JsonResult Showlap(SearchCriteria mSearchCriteria)
        {
            dynamic data = new ExpandoObject();
            if (mSearchCriteria.Month.IsNotNullOrEmpty())
                mSearchCriteria.TimeStamp = DateTime.ParseExact(mSearchCriteria.Month, "MMMM", CultureInfo.CurrentCulture);
            else
                mSearchCriteria.TimeStamp = DateTime.Now;

            int alertCount = 0;
            int probCount = 0;
            var alert = GetProbabilityAlert(mSearchCriteria);
            if (alert != null && alert.mSMSLogs != null)
            {
                alertCount = alert.mSMSLogs.Count();
            }
            var prob = GetProbability(mSearchCriteria);
            if (prob != null && prob.Probability != null)
            {
                probCount = prob.Probability.Count();
            }

            data = new { AlertCount = alertCount, ProbabilityCount = probCount };
            return Json(data);
        }

        public SMSLogLister GetProbabilityAlert(SearchCriteria mSearchCriteria)
        {
            if (mSearchCriteria.Month.IsNotNullOrEmpty())
                mSearchCriteria.TimeStamp = DateTime.ParseExact(mSearchCriteria.Month, "MMMM", CultureInfo.CurrentCulture);
            else
                mSearchCriteria.TimeStamp = DateTime.Now;

            var mSMSLogLister = new SMSLogLister();
            mSMSLogLister.SearchCriteria.SiteId = mSearchCriteria.SiteId;
            mSMSLogLister.SearchCriteria.AssetId = mSearchCriteria.AssetId;
            mSMSLogLister.SearchCriteria.FromDate = new DateTime(mSearchCriteria.TimeStamp.Year, mSearchCriteria.TimeStamp.Month, 1);
            mSMSLogLister.SearchCriteria.ToDate = mSMSLogLister.SearchCriteria.FromDate.AddMonths(1).AddDays(-1);
            mSMSLogLister.Pager.Take = -1;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSMSLogLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SMSLog/GetAll"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSMSLogLister = JsonConvert.DeserializeObject<SMSLogLister>(jsonString);

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
            return mSMSLogLister;
        }

        public ProbabilityLister GetProbability(SearchCriteria mSearchCriteria)
        {
            var mProbabilityLister = new ProbabilityLister();

            if (mSearchCriteria.Month.IsNotNullOrEmpty())
                mSearchCriteria.TimeStamp = DateTime.ParseExact(mSearchCriteria.Month, "MMMM", CultureInfo.CurrentCulture);
            else
                mSearchCriteria.TimeStamp = DateTime.Now;

            mProbabilityLister.SearchCriteria.SiteId = mSearchCriteria.SiteId;
            mProbabilityLister.SearchCriteria.AssetId = mSearchCriteria.AssetId;
            mProbabilityLister.SearchCriteria.StartDate = new DateTime(mSearchCriteria.TimeStamp.Year, mSearchCriteria.TimeStamp.Month, 1);
            mProbabilityLister.SearchCriteria.EndDate = mProbabilityLister.SearchCriteria.StartDate.AddMonths(1).AddDays(-1);
            mProbabilityLister.Pager.Take = -1;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mProbabilityLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SiteKeeping/GetProbability"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mProbabilityLister = JsonConvert.DeserializeObject<ProbabilityLister>(jsonString);
                        if (mProbabilityLister != null && mProbabilityLister.Probability != null && mProbabilityLister.Probability.Count > 0)
                            ChangeProbabilityName(mProbabilityLister.Probability);

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

            return mProbabilityLister;
        }

        public ActionResult GetTodayActiveAlerts(Domain.SMSLog mSmsLog)
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
            mSMSLogLister.SearchCriteria.FromDate = DateTime.Now;
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

        public List<Domain.Alert> GetAlert()
        {
            List<Domain.Alert> mAlerts = new List<Domain.Alert>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("AlertLog/GetAlerts")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAlerts = JsonConvert.DeserializeObject<List<Alert>>(jsonString);
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

            return mAlerts;
        }

        public ActionResult FailureAlertList()
        {
            SMSLogLister mSMSLogLister = new SMSLogLister();
            mSMSLogLister.SearchCriteria.TimeStamp = DateTime.Now;
            mSMSLogLister.SearchCriteria.Validity = true;
            var fromDate = new DateTime(mSMSLogLister.SearchCriteria.TimeStamp.Year, mSMSLogLister.SearchCriteria.TimeStamp.Month, 1);
            mSMSLogLister.SearchCriteria.FromDate = DateTime.Now;
            mSMSLogLister.SearchCriteria.ToDate = DateTime.Now;
            return View(mSMSLogLister);
        }

        public ActionResult _FailureAlertList(SMSLogLister mSMSLogLister)
        {
            mSMSLogLister.Pager.Take = -1;
            mSMSLogLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
            mSMSLogLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
            mSMSLogLister.SearchCriteria.ToDate = DateTime.Now;

            if (mSMSLogLister != null && mSMSLogLister.SearchCriteria != null && mSMSLogLister.SearchCriteria.TimeStamp != null && mSMSLogLister.SearchCriteria.TimeStamp == DateTime.MinValue)
            {
                mSMSLogLister.SearchCriteria.TimeStamp = DateTime.Now;
            }

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSMSLogLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SMSLog/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSMSLogLister = JsonConvert.DeserializeObject<SMSLogLister>(jsonString);
                        if (mSMSLogLister != null && mSMSLogLister.mSMSLogs != null)
                        {

                            TempData.Remove("TempFailureAlertList");
                            TempData["TempFailureAlertList"] = mSMSLogLister.mSMSLogs;
                            TempData.Keep();
                            var smslogs = new List<SMSLog>();

                            //foreach (var item in mSMSLogLister.AssetTypes)
                            //{
                            //    foreach (var site in mSMSLogLister.mSMSLogs.Where(x => x.AssetTypeId == item.Id).GroupBy(x => new { x.SiteId, x.SiteName }).Select(x => x.Key).ToList())
                            //    {
                            //        foreach (var alert in mSMSLogLister.mSMSLogs.Where(x => x.AssetTypeId == item.Id && x.SiteId == site.SiteId && x.AlertName != null && x.AlertName != string.Empty).GroupBy(x => x.AlertName).Select(x => x.Key).ToList())
                            //        {
                            //            foreach (var asset in mSMSLogLister.mSMSLogs.Where(x => x.AssetTypeId == item.Id && x.SiteId == site.SiteId && x.AlertName == alert).GroupBy(x => new { x.AssetId, x.AssetName }).Select(g => new { g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Select(x => x.Key).ToList())
                            //            {
                            //                var count = mSMSLogLister.mSMSLogs.Where(x => x.AssetTypeId == item.Id && x.SiteId == site.SiteId && x.AssetId == asset.AssetId && x.AlertName == alert).Count();
                            //                if (count > 1)
                            //                {
                            //                    var startDate = new DateTime(mSMSLogLister.SearchCriteria.TimeStamp.Year, mSMSLogLister.SearchCriteria.TimeStamp.Month, 1);

                            //                    var toDate = new DateTime(mSMSLogLister.SearchCriteria.TimeStamp.Year, mSMSLogLister.SearchCriteria.TimeStamp.Month, 15);

                            //                    var lastDayOfMonth = startDate.AddMonths(1).AddDays(-1);

                            //                    var smsfirst15Days = mSMSLogLister.mSMSLogs.Where(x => x.AssetTypeId == item.Id && x.SiteId == site.SiteId && x.AssetId == asset.AssetId && x.AlertName == alert && x.TimeStamp.Date >= startDate.Date && x.TimeStamp.Date <= toDate.Date).OrderByDescending(x => x.TimeStamp).FirstOrDefault();
                            //                    if (smsfirst15Days != null && smsfirst15Days.Id > 0)
                            //                    {
                            //                        smsfirst15Days.isTenDays = true;
                            //                        smsfirst15Days.TenDaysCount = 0;
                            //                        smslogs.Add(smsfirst15Days);
                            //                    }


                            //                    var smsSecond15Days = mSMSLogLister.mSMSLogs.Where(x => x.AssetTypeId == item.Id && x.SiteId == site.SiteId && x.AssetId == asset.AssetId && x.AlertName == alert && x.TimeStamp.Date > toDate.Date && x.TimeStamp.Date <= lastDayOfMonth.Date).OrderByDescending(x => x.TimeStamp).FirstOrDefault();
                            //                    if (smsSecond15Days != null && smsSecond15Days.Id > 0)
                            //                    {
                            //                        smsSecond15Days.isTenDays = true;
                            //                        smsSecond15Days.TenDaysCount = 0;
                            //                        smslogs.Add(smsSecond15Days);
                            //                    }
                            //                }
                            //                else
                            //                {
                            //                    foreach (var smsLog in mSMSLogLister.mSMSLogs.Where(x => x.AssetTypeId == item.Id && x.SiteId == site.SiteId && x.AssetId == asset.AssetId && x.AlertName == alert).ToList())
                            //                    {
                            //                        smslogs.Add(smsLog);
                            //                    }
                            //                }
                            //            }
                            //        }
                            //    }
                            //}

                            //if (smslogs != null && smslogs.Count > 0)
                            //{
                            //    mSMSLogLister.mSMSLogs = smslogs;
                            //}

                            //TempData.Remove("TempSMSLogLister");
                            //TempData["TempSMSLogLister"] = mSMSLogLister.mSMSLogs;
                            //TempData.Keep();
                        }

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
            finally
            {
                ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            }

            return PartialView(mSMSLogLister);
        }

        public ActionResult DownloadFailureAlertList(int assetTypeId = 0)
        {
            var tempSMSLog = TempData.Peek("TempFailureAlertList");
            if (tempSMSLog != null)
            {
                var csv = new StringBuilder();
                var socsvstring = string.Empty;
                var mSMSLogs = tempSMSLog as List<SMSLog>;
                if (mSMSLogs != null && mSMSLogs.Count > 0)
                {
                    socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8}", "Site Name", "Asset Type", "Asset Name", "Alert Type", "Current Value", "Message", "Time Stamp", "Active", " Remark");

                    csv.AppendLine(socsvstring);
                    string type = string.Empty;

                    //if (type != item.Key)
                    //{
                    //    socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8}", item.Key, "", "", "", "", "", "", "", "");
                    //    csv.AppendLine(socsvstring);
                    //}
                    if (assetTypeId > 0)
                    {
                        mSMSLogs = mSMSLogs.Where(x => x.AssetTypeId == assetTypeId).ToList();
                    }

                    foreach (var val in mSMSLogs)
                    {
                        string value = string.Empty;
                        var isSmsLogActive = "";
                        string strMessage = "";
                        if (val.IsSmsLogActive.Value)
                            isSmsLogActive = "Active";
                        else
                            isSmsLogActive = "InActive";

                        if (val.Message.IsNotNullOrEmpty())
                        {
                            strMessage = Regex.Replace(val.Message.RemoveComma(), @"\t|\n|\r", "");
                        }

                        if (strMessage.IsNotNullOrEmpty())
                        {

                            try
                            {
                                var splitstr = strMessage.Split(new string[] { "current val", "current cal", "current charging", "current vf", "Avg current : " }, StringSplitOptions.None);
                                if (splitstr != null && splitstr.Length > 1)
                                {
                                    string[] splitWithSpace = new string[splitstr.Length];
                                    if (strMessage.Contains("Avg current :"))
                                    {
                                        splitWithSpace = splitstr[2].Trim().Split(' ');
                                    }
                                    else
                                    {

                                        splitWithSpace = splitstr[1].Trim().Split(' ');
                                    }
                                    if (splitWithSpace != null && splitWithSpace.Length > 0)
                                    {

                                        value = Regex.Match(splitWithSpace[0], @"\d+.+\d").Value;
                                        if (value.IsNullOrEmpty())
                                            value = Regex.Match(splitWithSpace[0], @"\d+").Value;
                                    }
                                }
                            }
                            catch (Exception)
                            {
                            }

                        }

                        socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8}", val.SiteName.RemoveComma(), val.AssetTypeName.RemoveComma(), val.AssetName.RemoveComma(), val.AlertName.RemoveComma(), value, strMessage, val.TimeStamp.ToString("dd/MM/yyyy HH:mm").RemoveComma(), isSmsLogActive.RemoveComma(),
           val.Remark.RemoveComma());
                        csv.AppendLine(socsvstring);

                    }


                    Response.Clear();
                    Response.Buffer = true;
                    Response.AddHeader("content-disposition", "attachment;filename=SMSLog" + DateTime.Now.Ticks + ".csv");
                    Response.Charset = "utf-8";
                    Response.ContentType = "text/csv";
                    Response.Output.Write(csv);
                    Response.Flush();
                    Response.End();
                }
                else
                    return RedirectToAction("Index");
            }
            else
                return RedirectToAction("Index");


            return View();
        }
        private List<Domain.PointMachineEvent> GetPointMachineEvent(int assetId)
        {
            List<Domain.PointMachineEvent> mPointMachineEvents = new List<Domain.PointMachineEvent>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("PointMachineEvent/AssetId/{0}", assetId)).Result;
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
            return mPointMachineEvents;
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
                    var response = hcf.client.PostAsync(String.Format("DataloggerAsset/GetTempGraphData"), str).Result;
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
        #endregion


    }
}