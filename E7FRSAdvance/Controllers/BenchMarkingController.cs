using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Service;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Net;
using System;
using System.Web.Mvc;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Dynamic;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class BenchMarkingController : Controller
    {
        // GET: BenchMarking
        private readonly ISiteService siteService;
        private readonly IAssetTypeService assetTypeService;
        private readonly IAssetAttributeService assetAttributeService;
        private readonly IGraphService graphService;
        private readonly IAssetService assetService;
        public BenchMarkingController(ISiteService siteService, IAssetTypeService assetTypeService, IAssetAttributeService assetAttributeService, IGraphService graphService, IAssetService assetService)
        {
            this.siteService = siteService;
            this.assetTypeService = assetTypeService;
            this.assetAttributeService = assetAttributeService;
            this.graphService = graphService;
            this.assetService = assetService;
        }

        public ActionResult Index()
        {
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            ViewBag.AssetTypes = new SelectList(assetTypeService.GetAll(), "Id", "Name");
            return View();
        }

        public ActionResult GetAssestBy(int siteId, int assetTypeId)
        {
            return Json(assetService.GetAssestBy(siteId, assetTypeId), JsonRequestBehavior.AllowGet);
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
                TempData["AssetsSearch"] = mAssets;
                TempData.Keep();
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
                        System.Web.HttpContext.Current.Session["Assets"] = sessionData;
                        TempData["Assets"] = mAsset;
                        TempData.Keep();
                        TempData["AssetsSearch"] = mAssets;
                        TempData.Keep();
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
            mAsset.siteAttributeDatasList = null;
            return Json(mAsset, JsonRequestBehavior.AllowGet);
        }

        public ActionResult Create(List<Domain.BenchMarking> mBenchMarkings)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mBenchMarkings);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("BenchMarking"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Benchmarking has been saved." };
                    }
                    else
                    {
                        data = new { type = "error", result = "Internal server error." };
                    }
                }
            }
            catch (Exception ex)
            {
                data = new { type = "error", result = "Internal server error." };
            }

            return Json(data);
        }

        public ActionResult Delete(Domain.BenchMarking mBenchMarking)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mBenchMarking);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("BenchMarking/Delete"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Benchmarking has been deleted." };
                    }
                    else
                    {
                        data = new { type = "error", result = "Internal server error." };
                    }
                }
            }
            catch (Exception ex)
            {
                data = new { type = "error", result = "Internal server error." };
            }

            return Json(data);
        }

        public ActionResult Update(Domain.BenchMarking mBenchMarking)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mBenchMarking);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("BenchMarking/Update"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Benchmarking has been Update." };
                    }
                    else
                    {
                        data = new { type = "error", result = "Internal server error." };
                    }
                }
            }
            catch (Exception ex)
            {
                data = new { type = "error", result = "Internal server error." };
            }

            return Json(data);
        }

        public PartialViewResult List(BenchMarkingLister mBenchMarkingLister)
        {
            mBenchMarkingLister.Pager.Take = mBenchMarkingLister.Pager.PageSize;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mBenchMarkingLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("BenchMarking/GetAll"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mBenchMarkingLister = JsonConvert.DeserializeObject<BenchMarkingLister>(jsonString);
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
            return PartialView(mBenchMarkingLister);
        }

        public JsonResult GetGraph(BenchMarkingLister mBenchMarkingLister)
        {
            Asset mAsset = new Asset();
            mBenchMarkingLister.Pager.Take = mBenchMarkingLister.Pager.PageSize;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mBenchMarkingLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("BenchMarking/GetAll"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mBenchMarkingLister = JsonConvert.DeserializeObject<BenchMarkingLister>(jsonString);
                        if (mBenchMarkingLister != null && mBenchMarkingLister.BenchMarkings != null)
                        {
                            var assetAttributes = mBenchMarkingLister.BenchMarkings.GroupBy(x => new { x.AssetAttributeId, x.AssetAttribute }).ToList();
                            foreach (var item in assetAttributes)
                            {
                                mAsset.assetAttributes.Add(new AssetAttribute() { Id = item.Key.AssetAttributeId, Title = item.Key.AssetAttribute });

                            }
                        }
                        var dateLists = mBenchMarkingLister.BenchMarkings.GroupBy(x => x.TimeStamp).ToList();
                        foreach (var dateList in dateLists)
                        {
                            mAsset.DateList.Add(dateList.Key.ToString());
                            foreach (var assetAttribute in mAsset.assetAttributes)
                            {
                                var benchMarking = mBenchMarkingLister.BenchMarkings.Where(x => x.TimeStamp == dateList.Key && x.AssetAttributeId == assetAttribute.Id).FirstOrDefault();
                                if (benchMarking != null && benchMarking.Id > 0)
                                    assetAttribute.Data.Add(Convert.ToString(benchMarking.Value));
                                else
                                    assetAttribute.Data.Add("0");
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
            mAsset.DateList = mAsset.DateList.OrderBy(x => x).ToList();
            mAsset.MultipleLog = new List<MultipleLog>();
            mAsset.siteAttributeDatasList = null;
            return Json(mAsset, JsonRequestBehavior.AllowGet);
        }

        public ActionResult _PointMachineEvent(AssetLister mAssetLister)
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

                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView(mAssetLister);
        }

        public ActionResult GetPointMachineEvent(int assetId)
        {
            Domain.Asset mAsset = new Domain.Asset();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"BenchMarking/GetPointMachineEvent/Id/{assetId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAsset = JsonConvert.DeserializeObject<Domain.Asset>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView("PointMachineEventGraph", mAsset);
        }

        public ActionResult CreatePointMachineEvent(List<Domain.BenchMarkingPointMachine> mBenchMarkingPointMachines)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mBenchMarkingPointMachines);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("BenchMarking/CreateBenchMarkingPointMachine"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Benchmarking has been saved." };
                    }
                    else
                    {
                        data = new { type = "error", result = "Internal server error." };
                    }
                }
            }
            catch (Exception ex)
            {
                data = new { type = "error", result = "Internal server error." };
            }

            return Json(data);
        }

        public ActionResult UpdatePointMachineEvent(Domain.BenchMarkingPointMachine mBenchMarkingPointMachine)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mBenchMarkingPointMachine);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("BenchMarking/UpdatePointMachineEvent"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Benchmarking has been Update." };
                    }
                    else
                    {
                        data = new { type = "error", result = "Internal server error." };
                    }
                }
            }
            catch (Exception ex)
            {
                data = new { type = "error", result = "Internal server error." };
            }

            return Json(data);
        }

        public ActionResult DeletePointMachineEvent(int id)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.DeleteAsync(String.Format($"BenchMarking/DeletePointMachineEvent/{id}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "PointMachine event has been deleted." };
                    }
                    else
                    {
                        data = new { type = "error", result = "Internal server error." };
                    }
                }
            }
            catch (Exception ex)
            {
                data = new { type = "error", result = "Internal server error." };
            }

            return Json(data);
        }
    }
}