using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Service;
using E7FRSAdvance.Utility;
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
    [Authenticate]
    public class RailwayBordDashbordController : Controller
    {
        private readonly IZoneService zoneService;
        private readonly IDivisionService divisionService;
        private readonly ISiteService siteService;
        private readonly IFRSAlertService alertService;
        private readonly IAssetService assetService;
        private readonly IAssetAttributeService assetAttributeService;
        private readonly IGraphService graphService;
        private static List<Domain.ChatBot> mChatBots = new List<ChatBot>();
        private static bool isCheck = true;

        public RailwayBordDashbordController(IZoneService zoneService, IDivisionService divisionService, ISiteService siteService, IFRSAlertService alertService, IAssetService assetService, IAssetAttributeService assetAttributeService, IGraphService graphService)
        {
            this.zoneService = zoneService;
            this.divisionService = divisionService;
            this.siteService = siteService;
            this.alertService = alertService;
            this.assetService = assetService;
            this.assetAttributeService = assetAttributeService;
            this.graphService = graphService;

        }
        // GET: RailwayBordDashbord
        public ActionResult Index()
        {
            mChatBots = new List<ChatBot>();
            isCheck = true;
            mChatBots.Add(new Domain.ChatBot() { Message = $"Hi {ClsHttpContent.LoginUser.FirstName} 👋 <br /> I’m E7 AI, your assistant for rail insights. <br /> Ask me anything...", Name = "ChatBot" });
            var enumlist = (from E7FRSAdvance.Utility.Utility.AcknowledgemenStatus e in Enum.GetValues(typeof(E7FRSAdvance.Utility.Utility.AcknowledgemenStatus))
                            select new Domain.AssetType
                            {
                                Id = (int)e,
                                Name = e.ToString().Replace("_", " ")
                            }).ToList();

            ViewBag.AcknowledgemenStatus = enumlist;
            ViewBag.Zones = new SelectList(zoneService.GetAllZones(), "Id", "Name");
            ViewBag.Divisions = new SelectList(divisionService.GetAllDivisions(), "Id", "Name");
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            return View();
        }

        public JsonResult GetListAlerts()
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"RailwayBordDashbord/GetAssetHealthCount")).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;

                        data = new { Status = "success", Record = jsonString };
                    }
                }
            }
            catch (Exception)
            {

            }
            return Json(data, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetAccuracy()
        {
            var data = new object();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"RailwayBordDashbord/GetAccuracy")).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;

                        data = new { Status = "success", Record = jsonString };
                    }
                }
            }
            catch (Exception)
            {

            }
            return Json(data, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetWithOutAcknowledgementAlert(Domain.FRSAlertLister mFRSAlertLister)
        {
            var mFRSAlerts = new List<Domain.FRSAlert>();
            mFRSAlertLister = alertService.GetWithOutAcknowledgementAlert(mFRSAlertLister);

            if (mFRSAlertLister != null && mFRSAlertLister.mFRSAlerts.Count > 0)
                mFRSAlerts = mFRSAlertLister.mFRSAlerts;

            return Json(mFRSAlerts, JsonRequestBehavior.AllowGet);
        }


        public JsonResult GetWithAcknowledgementAlert(Domain.FRSAlertLister mFRSAlertLister)
        {
            var mFRSAlerts = new List<Domain.FRSAlert>();
            mFRSAlertLister = alertService.GetWithAcknowledgementAlert(mFRSAlertLister);

            if (mFRSAlertLister != null && mFRSAlertLister.mFRSAlerts.Count > 0)
                mFRSAlerts = mFRSAlertLister.mFRSAlerts;

            return Json(mFRSAlerts, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetAssetList(Domain.SearchCriteria mSearchCriteria)
        {
            var data = new object();
            try
            {
                mSearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
                mSearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSearchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("RailwayBordDashbord/GetAssetCount"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;

                        data = new { Status = "success", Record = jsonString };

                    }
                }
            }
            catch (Exception)
            {
            }
            return Json(data, JsonRequestBehavior.AllowGet);
        }

        public List<Domain.Asset> GetAsset()
        {
            List<Domain.Asset> mAssets = new List<Domain.Asset>();
            Domain.AssetLister mAssetLister = new AssetLister();
            try
            {

                mAssetLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
                mAssetLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
                mAssetLister.Pager.Take = -1;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssetLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GetUserWiseLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetLister = JsonConvert.DeserializeObject<Domain.AssetLister>(jsonString);
                        if (mAssetLister != null && mAssetLister.mAssets.Count > 0)
                        {
                            mAssets = mAssetLister.mAssets;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            return mAssets;
        }

        public JsonResult GetAnomalyCluster()
        {
            var data = new object();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"RailwayBordDashbord/GetAnomalyCluster")).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;

                        data = new { Status = "success", Record = jsonString };
                    }
                }
            }
            catch (Exception)
            {

            }
            return Json(data, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetActiveAlertList()
        {
            Domain.FRSAlertLister mFRSAlertLister = new FRSAlertLister();

            try
            {
                mFRSAlertLister.SearchCriteria.AlertStatus = (int)E7FRSAdvance.Utility.Utility.AlertStatus.Active;
                mFRSAlertLister.SearchCriteria.IsAcknowledgement = false;
                mFRSAlertLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
                mFRSAlertLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
                if (ClsHttpContent.LoginUser.IsSiteKeeping)
                {
                    mFRSAlertLister.SearchCriteria.RoleId = (int)E7FRSAdvance.Utility.Utility.Role.Admin;
                }
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mFRSAlertLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("FRSAlert/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mFRSAlertLister = JsonConvert.DeserializeObject<FRSAlertLister>(jsonString);
                    }

                }
            }
            catch (Exception)
            {
            }
            return Json(mFRSAlertLister, JsonRequestBehavior.AllowGet);
        }

        public ActionResult _FRSAlert(Domain.FRSAlertLister mFRSAlertLister)
        {
            return PartialView(alertService.GetWithOutAcknowledgementAlert(mFRSAlertLister));
        }

        public ActionResult _FRSAcknowledgementAlert(Domain.FRSAlertLister mFRSAlertLister)
        {
            try
            {
                mFRSAlertLister.SearchCriteria.IsAcknowledgement = true;
                mFRSAlertLister.SearchCriteria.RoleId = (int)E7FRSAdvance.Utility.Utility.Role.User;
                mFRSAlertLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
                if (ClsHttpContent.LoginUser.IsSiteKeeping)
                    mFRSAlertLister.SearchCriteria.RoleId = (int)E7FRSAdvance.Utility.Utility.Role.Admin;

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mFRSAlertLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("FRSAlert/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mFRSAlertLister = JsonConvert.DeserializeObject<FRSAlertLister>(jsonString);
                    }
                }
            }
            catch (Exception)
            {
            }
            return PartialView("_FRSAlert", mFRSAlertLister);
        }

        public ActionResult GetUserHistory(UserLister mUserLister)
        {
            var users = new List<User>();
            mUserLister.Pager.Take = -1;
            mUserLister.SearchCriteria.RoleId = (int)E7FRSAdvance.Utility.Utility.Role.User;
            mUserLister.SearchCriteria.SiteKeepingSearch = false;

            if (mUserLister.SearchCriteria.FromDate == DateTime.MinValue)
                mUserLister.SearchCriteria.FromDate = DateTime.Now.AddDays(-7);

            if (mUserLister.SearchCriteria.ToDate == DateTime.MinValue)
                mUserLister.SearchCriteria.ToDate = DateTime.Now;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mUserLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("User/GetUserHistory"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mUserLister = JsonConvert.DeserializeObject<UserLister>(jsonString);
                        if (mUserLister != null && mUserLister.Users != null && mUserLister.Users.Count > 0)
                        {
                            users = mUserLister.Users;
                            foreach (var user in users)
                            {
                                user.Duration = user.WebDuration + user.Duration;
                            }
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
            return Json(users, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetPDFDownload(string fileName = null, string filePath = null, string fileType = null)
        {
            string strings = string.Empty;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(new Domain.ChatBot() { FileName = fileName, FilePath = filePath });
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("ChatBot/GetPDFDownload"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {

                            byte[] filebytes = System.Convert.FromBase64String(stringInBase64);
                            if (filebytes != null && filebytes.Length > 0 && fileType.IsNotNullOrEmpty())
                            {
                                if (fileType.Trim().ToLower() == "csv")
                                    return File(filebytes, "text/csv", $"{fileName}");
                                else if (fileType.Trim().ToLower() == "pdf")
                                    return File(filebytes, "application/pdf", $"{fileName}");
                                else
                                    return File(filebytes, $"application/{fileType}", $"{fileName}");
                            }
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


            return RedirectToAction("Index");
        }

        [HttpPost]
        public ActionResult BindQuestionMessage(string message = null)
        {
            string strings = string.Empty;
            if (!string.IsNullOrEmpty(message))
            {
                mChatBots.Add(new Domain.ChatBot() { Message = message, UserId = ClsHttpContent.LoginUser.Id, Name = $"{ClsHttpContent.LoginUser.FirstName} {ClsHttpContent.LoginUser.LastName}", MessageType = "Question" });

            }


            return Json(strings, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public ActionResult SearchMessageList(string message = null)
        {
            string strings = string.Empty;
            try
            {
                //if (!string.IsNullOrEmpty(message))
                //{
                //    mChatBots.Add(new Domain.ChatBot() { Message = message, UserId = ClsHttpContent.LoginUser.Id, Name = $"{ClsHttpContent.LoginUser.FirstName} {ClsHttpContent.LoginUser.LastName}", MessageType = "Question" });

                //}


                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(new Domain.E7ChatBot() { message = message, checkpoint_id = ClsHttpContent.LoginUser.EmailAddress, reset_memory = isCheck });

                    if (isCheck)
                        isCheck = false;

                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("ChatBot/GetE7Chat"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var outputstr = JsonConvert.DeserializeObject<string>(jsonString);
                        if (outputstr.IsNotNullOrEmpty())
                        {
                            var mDivisionPdfResponse = JsonConvert.DeserializeObject<DivisionPdfResponse>(outputstr);
                            if (mDivisionPdfResponse != null)
                            {
                                strings = mDivisionPdfResponse.Text;
                                mChatBots.Add(new Domain.ChatBot() { Message = mDivisionPdfResponse.Text, Name = "ChatBot", MessageType = "Answer", Question = message, Files = mDivisionPdfResponse.Files });
                            }

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


            return Json(strings, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetAssestBy(int siteId, int assetTypeId)
        {
            return Json(assetService.GetAssestBy(siteId, assetTypeId), JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetMessageList()
        {
            return Json(mChatBots, JsonRequestBehavior.AllowGet);
        }

        #region Analytics

        public ActionResult _Analytics()
        {
            ViewBag.Divisions = new SelectList(divisionService.GetAllDivisions(), "Id", "Name");
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            ViewBag.AssetTypes = new SelectList(GetFRSAssetType(), "Id", "Name");
            return PartialView();
        }

        [HttpPost]
        public ActionResult _AnalyticsReportData(Asset asset)
        {
            return PartialView(GetAnalytics(asset));
        }

       // [HttpPost]
        public ActionResult DownloadAnalyticsReportData(Asset asset)
        {
            var dsds = GetAnalytics(asset);
            return View("Index");
        }

        private Domain.Asset GetAnalytics(Asset asset)
        {
            List<Asset> mAssets = new List<Asset>();
            var mAsset = new Asset();
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
                        var allAssetAttributes = assetAttributeService.GetAssetAttributesBy(mAsset.AssetTypeId);
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
                                        graphService.PrepareWithDataArrayGraphAttribute(mAsset.assetAttributes, allAssetAttributes, vals);

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
                                    graphService.PrepareGraphAttribute(mAsset.assetAttributes, allAssetAttributes, vals);
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

            return mAsset;
        }

        #endregion

        private List<Domain.AssetType> GetFRSAssetType()
        {

            return (from E7FRSAdvance.Utility.Utility.AssetType e in Enum.GetValues(typeof(E7FRSAdvance.Utility.Utility.AssetType))
                    select new Domain.AssetType
                    {
                        Id = (int)e,
                        Name = e.ToString().Replace("_", " ")
                    }).ToList();
        }

    }
}