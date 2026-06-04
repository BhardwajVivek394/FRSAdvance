using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
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
    public class ProbabilityController : Controller
    {
        // GET: Probability
        private readonly ISiteService siteService;
        public ProbabilityController(ISiteService siteService)
        {
            this.siteService = siteService;
        }

        public ActionResult Index()
        {
            ProbabilityLister mProbabilityLister = new ProbabilityLister();
            DateTime date = DateTime.Now;
            mProbabilityLister.SearchCriteria.StartDate = DateTime.Now.Date;
            mProbabilityLister.SearchCriteria.EndDate = DateTime.Now;


            return View(mProbabilityLister);
        }

        public ActionResult _List(ProbabilityLister mProbabilityLister)
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

                        TempData.Remove("TempProbabilityLister");
                        TempData["TempProbabilityLister"] = mProbabilityLister;
                        TempData.Keep();
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
            return PartialView(mProbabilityLister);
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

        public ActionResult UpdateProbability(Probability mProbability)
        {
            try
            {
                mProbability.UserId = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mProbability);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format("SiteKeeping/UpdateProbability"), str).Result;
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

        public ActionResult DeleteProbability(Probability mProbability)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mProbability.UserId = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mProbability);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SiteKeeping/DeleteProbability"), str).Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Probability has been deleted." };
                    }
                    else
                    {
                        data = new { type = "error", result = "Internal server error." };
                    }
                }
            }
            catch (Exception)
            {
                data = new { type = "error", result = "Internal server error." };
            }
            return Json(data, JsonRequestBehavior.AllowGet);
        }

        public ActionResult DeleteAllProbability(List<Probability> mProbabilities)
        {
            dynamic data = new ExpandoObject();
            if (mProbabilities != null && mProbabilities.Count > 0)
            {
                foreach (var mProbability in mProbabilities)
                {
                    try
                    {
                        mProbability.UserId = ClsHttpContent.LoginUser.Id;
                        using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                        {
                            var jsonStr = JsonConvert.SerializeObject(mProbability);
                            StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                            var response = hcf.client.PostAsync(String.Format("SiteKeeping/DeleteProbability"), str).Result;
                            if (response.StatusCode == HttpStatusCode.NoContent)
                            {
                            }
                        }
                    }
                    catch (Exception)
                    {

                    }
                }
            }

            return Json("", JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetRemark(Probability mProbability)
        {
            try
            {
                var tempSMSLog = TempData.Peek("TempProbabilityLister");
                if (tempSMSLog != null)
                {
                    var csv = new StringBuilder();
                    var socsvstring = string.Empty;
                    var mProbabilityLister = tempSMSLog as ProbabilityLister;
                    if (mProbabilityLister != null && mProbabilityLister.Probability != null && mProbabilityLister.Probability.Count > 0)
                    {
                        var probability = mProbabilityLister.Probability.Where(x => x.AssetId == mProbability.AssetId && x.SiteId == mProbability.SiteId && x.TimeStamp.Date == mProbability.TimeStamp.Date && x.CategoryAlias.Contains(mProbability.Category)).FirstOrDefault();
                        if (probability != null && probability.ProbabilityRemarks != null && probability.ProbabilityRemarks.Count > 0)
                            mProbability.ProbabilityRemarks.AddRange(probability.ProbabilityRemarks);
                    }
                }
            }
            catch (Exception)
            {
            }
            return PartialView("_Remark", mProbability);
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
                        if (ClsHttpContent.LoginUser.RoleId == (int)E7FRSAdvance.Utility.Utility.Role.User)
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


        public ActionResult DownloadProbability()
        {
            var tempProbability = TempData.Peek("TempProbabilityLister");
            if (tempProbability != null)
            {
                var csv = new StringBuilder();
                var socsvstring = string.Empty;
                var mProbabilityLister = tempProbability as ProbabilityLister;
                if (mProbabilityLister != null && mProbabilityLister.Probability != null && mProbabilityLister.Probability.Count > 0)
                {

                    socsvstring = string.Format("{0},{1},{2},{3},{4}", "Time Stamp", "Site Name", "Asset Type", "Gear Name", "Category");

                    csv.AppendLine(socsvstring);
                    string type = string.Empty;
                    foreach (var item in mProbabilityLister.Probability.GroupBy(x => x.AssetTypeName).ToList())
                    {
                        foreach (var val in item.ToList())
                        {
                            socsvstring = string.Format("{0},{1},{2},{3},{4}", val.TimeStamp.ToString("dd/MM/yyyy HH:mm").RemoveComma(), val.SiteName.RemoveComma(), val.AssetTypeName, val.AssetName, val.Category.RemoveComma());
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
    }
}