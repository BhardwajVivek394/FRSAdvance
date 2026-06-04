using Domain;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System.Net.Http;
using System.Net;
using System.Text;
using System;
using System.Web.Mvc;
using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using E7FRSAdvance.Service;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Utility;
using System.Text.RegularExpressions;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class ConsolidatedReportController : Controller
    {
        private readonly IDivisionService divisionService;
        private readonly IZoneService zoneService;
        private readonly ISiteService siteService;
        private readonly IAssetTypeService assetTypeService;

        public ConsolidatedReportController(IDivisionService divisionService, IZoneService zoneService, ISiteService siteService, IAssetTypeService assetTypeService)
        {
            this.divisionService = divisionService;
            this.zoneService = zoneService;
            this.siteService = siteService;
            this.assetTypeService = assetTypeService;
        }
        // GET: ConsolidatedReport
        public ActionResult Index()
        {
            ConsolidatedLister mConsolidatedLister = new ConsolidatedLister();

            ViewBag.Years = new SelectList(GetYears());
            ViewBag.Months = new SelectList(DateTimeFormatInfo.CurrentInfo.MonthNames.ToList());
            ViewBag.AssetTypes = new SelectList(assetTypeService.GetAll(), "Id", "Name");

            if (ClsHttpContent.LoginUser.RoleId == (int)E7FRSAdvance.Utility.Utility.Role.User)
            {
                var sites = siteService.GetAll();
                if (sites != null && sites.Count > 0)
                {
                    ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
                    var divisionIdGroup = sites.GroupBy(x => x.DivisionId).Select(x => x.Key).ToList();
                    var zoneIdGroup = sites.GroupBy(x => x.ZoneId).Select(x => x.Key).ToList();

                    var divisions = divisionService.GetAll();
                    if (divisions != null && divisions.Count > 0)
                        ViewBag.Divisions = new SelectList(divisions.Where(x => divisionIdGroup.Contains(x.Id)), "Id", "Name");
                    else
                        ViewBag.Divisions = new SelectList(divisions, "Id", "Name");

                    var zones = zoneService.GetAll();
                    if (zones != null && zones.Count > 0)
                        ViewBag.Zones = new SelectList(zones.Where(x => zoneIdGroup.Contains(x.Id)), "Id", "Name");
                    else
                        ViewBag.Zones = new SelectList(zones, "Id", "Name");

                }
            }
            else
            {
                ViewBag.Zones = new SelectList(zoneService.GetAll(), "Id", "Name");
                ViewBag.Divisions = new SelectList(divisionService.GetAll(), "Id", "Name");
                ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            }
            return View(mConsolidatedLister);
        }

        public ActionResult _List(ConsolidatedLister mConsolidatedLister)
        {
            //mConsolidatedList.Pager.Take = -1;
            //if (mConsolidatedList != null && mProbabilityLister.SearchCriteria != null && mProbabilityLister.SearchCriteria.TimeStamp != null && mProbabilityLister.SearchCriteria.TimeStamp == DateTime.MinValue)
            //    mProbabilityLister.SearchCriteria.TimeStamp = DateTime.Now;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mConsolidatedLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("ConsolidatedReport/GetAll"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mConsolidatedLister = JsonConvert.DeserializeObject<ConsolidatedLister>(jsonString);

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
                ViewBag.Months = new SelectList(DateTimeFormatInfo.CurrentInfo.MonthNames.ToList());
            }
            return PartialView(mConsolidatedLister);
        }

        public ActionResult _AlertList(ConsolidatedLister mConsolidatedLister)
        {
            //mConsolidatedList.Pager.Take = -1;
            //if (mConsolidatedList != null && mProbabilityLister.SearchCriteria != null && mProbabilityLister.SearchCriteria.TimeStamp != null && mProbabilityLister.SearchCriteria.TimeStamp == DateTime.MinValue)
            //    mProbabilityLister.SearchCriteria.TimeStamp = DateTime.Now;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mConsolidatedLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("ConsolidatedReport/GetAll"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mConsolidatedLister = JsonConvert.DeserializeObject<ConsolidatedLister>(jsonString);
                        if (mConsolidatedLister != null && mConsolidatedLister.mSMSLogs != null && mConsolidatedLister.mSMSLogs.Count > 0)
                        {
                            TempData["tempConsolidatedAlert"] = mConsolidatedLister.mSMSLogs;
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
            return PartialView(mConsolidatedLister);
        }


        public ActionResult _ProbabilityList(ConsolidatedLister mConsolidatedLister)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mConsolidatedLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("ConsolidatedReport/GetAll"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mConsolidatedLister = JsonConvert.DeserializeObject<ConsolidatedLister>(jsonString);
                        if (mConsolidatedLister != null && mConsolidatedLister.Probability != null && mConsolidatedLister.Probability.Count > 0)
                        {
                            ChangeProbabilityName(mConsolidatedLister.Probability);
                            TempData["tempConsolidatedProbability"] = mConsolidatedLister.Probability;
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
            return PartialView(mConsolidatedLister);
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

        public ActionResult DownloadSMSLog()
        {
            var tempSMSLog = TempData.Peek("tempConsolidatedAlert");
            if (tempSMSLog != null)
            {
                var csv = new StringBuilder();
                var socsvstring = string.Empty;
                var mSMSLogs = tempSMSLog as List<SMSLog>;
                if (mSMSLogs != null && mSMSLogs.Count > 0)
                {
                    socsvstring = string.Format("{0},{1},{2},{3},{4}", "Site Name", "Message", "Time Stamp", "Active", " Remark");

                    csv.AppendLine(socsvstring);
                    string type = string.Empty;
                    foreach (var item in mSMSLogs.GroupBy(x => x.AssetTypeName).ToList())
                    {
                        if (type != item.Key)
                        {
                            socsvstring = string.Format("{0},{1},{2},{3},{4}", item.Key, "", "", "", "");
                            csv.AppendLine(socsvstring);
                        }
                        foreach (var val in item.ToList())
                        {
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

                            socsvstring = string.Format("{0},{1},{2},{3},{4}", val.SiteName.RemoveComma(), strMessage, val.TimeStamp.ToString("dd/MM/yyyy HH:mm").RemoveComma(), isSmsLogActive.RemoveComma(),
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

        public ActionResult DownloadProbability()
        {
            var tempSMSLog = TempData.Peek("tempConsolidatedProbability");
            if (tempSMSLog != null)
            {
                var csv = new StringBuilder();
                var socsvstring = string.Empty;
                var mSMSLogs = tempSMSLog as List<Probability>;
                if (mSMSLogs != null && mSMSLogs.Count > 0)
                {
                    socsvstring = string.Format("{0},{1},{2},{3}", "Time Stamp", "Site Name", "Gear Name", "Category");

                    csv.AppendLine(socsvstring);
                    string type = string.Empty;
                    foreach (var item in mSMSLogs.GroupBy(x => x.AssetTypeName).ToList())
                    {
                        foreach (var val in item.ToList())
                        {
                            socsvstring = string.Format("{0},{1},{2},{3}", val.TimeStamp.ToString("dd/MM/yyyy HH:mm").RemoveComma(), val.SiteName.RemoveComma(), val.AssetName, val.Category.RemoveComma());
                            csv.AppendLine(socsvstring);
                        }
                    }

                    Response.Clear();
                    Response.Buffer = true;
                    Response.AddHeader("content-disposition", "attachment;filename=Probability" + DateTime.Now.Ticks + ".csv");
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

        private List<int> GetYears()
        {
            List<int> Years = new List<int>();
            DateTime startYear = DateTime.Now.AddYears(-3);
            while (startYear.Year <= DateTime.Now.Year)
            {
                Years.Add(startYear.Year);
                startYear = startYear.AddYears(1);
            }
            return Years;
        }

    }
}