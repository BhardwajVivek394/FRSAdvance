using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class WatchlistController : Controller
    {
        // GET: Watchlist
        private readonly IDivisionService divisionService;
        private readonly ISiteService siteService;
        public WatchlistController(IDivisionService divisionService, ISiteService siteService)
        {
            this.divisionService = divisionService;
            this.siteService = siteService;
        }

        public ActionResult Index()
        {
            return View(new WatchListLister());
        }

        // [HttpPost]
        public ActionResult _List(WatchListLister mWatchListLister)
        {
            mWatchListLister.Pager.Take = -1;


            if (mWatchListLister.SearchCriteria.StartDate == null || mWatchListLister.SearchCriteria.StartDate == DateTime.MinValue)
                mWatchListLister.SearchCriteria.StartDate = DateTime.Now;

            if (mWatchListLister.SearchCriteria.EndDate == null || mWatchListLister.SearchCriteria.EndDate == DateTime.MinValue)
                mWatchListLister.SearchCriteria.EndDate = DateTime.Now;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mWatchListLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("WatchList/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mWatchListLister = JsonConvert.DeserializeObject<WatchListLister>(jsonString);
                        if (mWatchListLister != null && mWatchListLister.WatchLists != null && mWatchListLister.WatchLists.Count > 0)
                        {
                            var mAssetTypes = new List<Domain.AssetType>();
                            foreach (var watchList in mWatchListLister.WatchLists.GroupBy(x => new { x.AssetTypeId, x.AssetTypeName }).ToList())
                            {
                                mAssetTypes.Add(new AssetType() { Id = watchList.Key.AssetTypeId, Name = watchList.Key.AssetTypeName });
                            }
                            mWatchListLister.AssetTypes = mAssetTypes;
                            //mWatchListLister.WatchLists = mWatchListLister.WatchLists.Where(x => x.Status.Trim().ToLower() != "ok").ToList();


                            var mAssets = new List<Domain.Asset>();
                            foreach (var watchListAsset in mWatchListLister.WatchLists.GroupBy(x => new { x.AssetId, x.AssetName }).ToList())
                            {
                                mAssets.Add(new Asset() { Id = watchListAsset.Key.AssetId, Name = watchListAsset.Key.AssetName });
                            }
                            ViewBag.Assets = new SelectList(mAssets, "Id", "Name");


                            TempData.Remove("TempWatchlistLister");
                            TempData["TempWatchlistLister"] = mWatchListLister;
                            TempData.Keep();
                        }

                    }

                }
            }
            catch (Exception)
            {
            }
            finally
            {
                ViewBag.Divisions = new SelectList(divisionService.GetAll(), "Id", "Name");
            }
            return PartialView(mWatchListLister);
        }

        public JsonResult GetSitesBy(int divisionId)
        {
            var sites = siteService.GetBy(divisionId);
            return Json(sites, JsonRequestBehavior.AllowGet);
        }

        public ActionResult DownloadWatchList()
        {
            var tempProbability = TempData.Peek("TempWatchlistLister");
            if (tempProbability != null)
            {
                var mWatchListLister = tempProbability as WatchListLister;

                if (mWatchListLister != null && mWatchListLister.WatchLists != null && mWatchListLister.WatchLists.Count > 0)
                {
                    var csv = new StringBuilder();
                    var socsvstring = string.Empty;
                    var assetNameGroup = mWatchListLister.WatchLists.GroupBy(x => x.AssetName).ToList();
                    socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14}", "AssetType", "Asset", "Attribute", "Value", "Cluster", "Card", "ADC", "Pin", "Created Date", "Updated Date", "Status", "Issue", "Remark", "Point Machine Instance", "Creation Type");
                    csv.AppendLine(socsvstring);
                    foreach (var assetName in assetNameGroup)
                    {
                        //socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14}", "\"" + assetName.Key + "\"", "", "", "", "", "", "", "", "", "", "", "", "", "", "");
                        //csv.AppendLine(socsvstring);
                        foreach (var item in mWatchListLister.WatchLists.Where(x => x.AssetName.Trim().ToUpper() == assetName.Key.Trim().ToUpper()).GroupBy(x => x.AttributeName).Select(y => y.Key).ToList())
                        {
                            foreach (var cardLine in mWatchListLister.WatchLists.Where(x => x.AssetName.Trim().ToUpper() == assetName.Key.Trim().ToUpper() && x.AttributeName.Trim().ToUpper() == item.Trim().ToUpper()).OrderBy(x => x.Pin).ToList())
                            {
                                string adc = string.Empty;
                                string lastModifiedDate = string.Empty;
                                string createdByName = string.Empty;
                                if (cardLine.LastModifiedDate != null && cardLine.LastModifiedDate != DateTime.MinValue)
                                {
                                    lastModifiedDate = cardLine.LastModifiedDate.ToString();
                                }

                                if (cardLine.CreatedBy == (int)E7FRSAdvance.Utility.Utility.WatchListCreationType.User)
                                    createdByName = "User";
                                else if (cardLine.CreatedBy == (int)E7FRSAdvance.Utility.Utility.WatchListCreationType.System)
                                    createdByName = "System";

                                socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14}", cardLine.AssetTypeName, cardLine.AssetName.RemoveComma(), cardLine.AttributeName.RemoveComma(), cardLine.AttributeValue, cardLine.ClusterName.RemoveComma(), cardLine.CardName.RemoveComma(), cardLine.ADCName.RemoveComma(), cardLine.Pin, cardLine.CreatedDate.ToString(), lastModifiedDate, cardLine.Status, cardLine.Issue, cardLine.Remark, cardLine.PointMachineInstance, createdByName);
                                csv.AppendLine(socsvstring);
                            }
                        }
                    }

                    Response.Clear();
                    Response.Buffer = true;
                    Response.AddHeader("content-disposition", $"attachment;filename=WatchList_{DateTime.Now.ToString("dd-MM-yyyy h-mm tt")}" + ".csv");
                    Response.Charset = "utf-8";
                    Response.ContentType = "text/csv";
                    Response.Output.Write(csv);
                    Response.Flush();
                    Response.End();
                }
            }



            return RedirectToAction("Index");
        }
    }
}