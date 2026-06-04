using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Service;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;
using YamlDotNet.Core.Tokens;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class PerformanceController : Controller
    {
        // GET: Performance
        private readonly ISiteService siteService;
        public PerformanceController(ISiteService siteService)
        {
            this.siteService = siteService;
        }
        public ActionResult Index()
        {
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            return View();
        }

        public ActionResult _PerformanceLister(PerformanceLister mPerformanceLister)
        {
            var mAssetAttributes = new List<AssetAttribute>();

            mPerformanceLister.Pager.Take = -1;
            if (mPerformanceLister != null && mPerformanceLister.SearchCriteria != null && mPerformanceLister.SearchCriteria.SiteId > 0)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mPerformanceLister);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("Performance/GetLister"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mPerformanceLister = JsonConvert.DeserializeObject<PerformanceLister>(jsonString);
                            if (mPerformanceLister != null && mPerformanceLister.Performances != null && mPerformanceLister.Performances.Count > 0)
                            {
                                var assetAttributes = GetAssetAttributes(mPerformanceLister.SearchCriteria.SiteId, mPerformanceLister.SearchCriteria.AssetId);
                                if (assetAttributes != null && assetAttributes.Count > 0)
                                {
                                    foreach (var item in assetAttributes)
                                    {
                                        if (item.Title.Contains("Last Update Relay End( sec ago)") ||
                                            item.Title.Contains("Last Update Feed End( sec ago)") || item.Title.Contains("Zero offset") || item.Title.Contains("Last Updated")
                                            || item.Title.Contains("Last Access (Sec)") || item.Title.Contains("Last Access")
                                            )
                                        {
                                        }
                                        else
                                        {
                                            mAssetAttributes.Add(item);
                                        }
                                    }


                                    if (mAssetAttributes.Select(x => x.AssetTypeName).FirstOrDefault().ToLower() == "TRACK".ToLower())
                                    {
                                        var leakage = new Domain.AssetAttribute();
                                        leakage.AssetName = mAssetAttributes.Select(x => x.AssetName).FirstOrDefault();
                                        leakage.Title = "Leakage";
                                        mAssetAttributes.Add(leakage);
                                    }

                                    var dates = mPerformanceLister.Performances.GroupBy(x => x.TimeStamp).ToList();
                                    foreach (var dateTime in dates)
                                    {
                                        mPerformanceLister.DateList.Add(dateTime.Key.ToString("MM/dd/yyyy"));
                                        var date = dateTime.Key.ToString("dd MMM yyyy");
                                        foreach (var vals in mPerformanceLister.Performances.Where(y => y.TimeStamp.Date == dateTime.Key.Date).Select(x => x.CsvData).ToList())
                                        {
                                            var array = vals.Split('~');

                                            if (!string.IsNullOrEmpty(Convert.ToString(array[0])))
                                            {
                                                var data = Convert.ToDateTime(Convert.ToString(array[0]));

                                            }

                                            //mAsset.DateList.Add(Convert.ToString(array[0]));
                                            int i = 3;
                                            decimal ifma = 0;
                                            decimal irma = 0;
                                            foreach (var item in mAssetAttributes)
                                            {
                                                if (i != 9 || i != 10)
                                                {
                                                    if (item.Title == "If mA")
                                                    {
                                                        if (!string.IsNullOrEmpty(array[i]))
                                                        {
                                                            ifma = Convert.ToDecimal(array[i]);
                                                        }
                                                        else
                                                        {
                                                            ifma = 0;
                                                        }

                                                    }
                                                    if (item.Title == "Ir mA")
                                                    {
                                                        if (!string.IsNullOrEmpty(array[i]))
                                                        {
                                                            irma = Convert.ToDecimal(array[i]);
                                                        }
                                                        else
                                                        {
                                                            irma = 0;
                                                        }

                                                    }
                                                    if (item.Title == "Leakage")
                                                    {
                                                        item.DataArray.Add(Convert.ToString(ifma - irma));
                                                    }
                                                    else
                                                    {
                                                        item.DataArray.Add(Convert.ToString(array[i]));
                                                    }
                                                }
                                                i++;
                                            }


                                        }
                                        foreach (var atrv in mAssetAttributes)
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

                            }
                        }
                    }
                }
                catch (Exception ex)
                {

                }
            }
            mPerformanceLister.AssetAttributes = mAssetAttributes;
            return Json(mPerformanceLister, JsonRequestBehavior.AllowGet);
        }

        public ActionResult _GetPointMachineEvent(AssetLister mAssetLister)
        {
            mAssetLister.Pager.Take = -1;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    mAssetLister.SearchCriteria.CreatedBy = ClsHttpContent.LoginUser.Id;
                    if (string.IsNullOrEmpty(mAssetLister.SearchCriteria.StartDate))
                        mAssetLister.SearchCriteria.StartDate = DateTime.Now.ToShortDateString();

                    if (string.IsNullOrEmpty(mAssetLister.SearchCriteria.EndDate))
                        mAssetLister.SearchCriteria.EndDate = DateTime.Now.ToShortDateString();


                    var jsonStr = JsonConvert.SerializeObject(mAssetLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Performance/GetPointMachineEvent"), str).Result;
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
            return PartialView("~/Views/Performance/_GetPointMachineDashboard.cshtml", mAssetLister);
        }

        public ActionResult GetAssetTypeBySiteId(int siteId)
        {
            List<AssetType> mAssetTypes = new List<AssetType>();
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
                            mAssetTypes = JsonConvert.DeserializeObject<List<AssetType>>(jsonString);
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

        public ActionResult GetAssetById(int assetTypeId, int siteId)
        {
            List<Asset> mAsset = new List<Asset>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync($"Asset/GetAllAssest/{siteId}/{assetTypeId}").Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAsset = JsonConvert.DeserializeObject<List<Asset>>(jsonString);
                    }
                }
            }
            catch (Exception)
            {
            }
            return Json(mAsset, JsonRequestBehavior.AllowGet);
        }

        public List<AssetAttribute> GetAssetAttributes(int siteId, int assetId)
        {
            List<AssetAttribute> mAssetAttributes = new List<AssetAttribute>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync($"AssetAttribute/SiteId/{siteId}/AssetId/{assetId}").Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetAttributes = JsonConvert.DeserializeObject<List<AssetAttribute>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {

            }
            return mAssetAttributes;
        }

        public List<AssetAttribute> GetAssetAttributesBy(int assetTypeId)
        {
            List<AssetAttribute> mAssetAttributes = new List<AssetAttribute>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync($"AssetAttribute/AssetTypeId/{assetTypeId}").Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetAttributes = JsonConvert.DeserializeObject<List<AssetAttribute>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {

            }
            return mAssetAttributes;
        }

        public ActionResult DownloadPerformance(int assetTypeId, int siteId)
        {
            var csv = new StringBuilder();
            var socsvstring = string.Empty;

            var mAssetAttributes = new List<AssetAttribute>();
            PerformanceLister mPerformanceLister = new PerformanceLister();
            mPerformanceLister.Pager.Take = -1;
            mPerformanceLister.SearchCriteria.SiteId = siteId;
            mPerformanceLister.SearchCriteria.AssetTypeId = assetTypeId;
            if (mPerformanceLister != null && mPerformanceLister.SearchCriteria != null && mPerformanceLister.SearchCriteria.SiteId > 0)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mPerformanceLister);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("Performance/GetLister"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mPerformanceLister = JsonConvert.DeserializeObject<PerformanceLister>(jsonString);
                            if (mPerformanceLister != null && mPerformanceLister.Performances != null && mPerformanceLister.Performances.Count > 0)
                            {
                                var assetAttributes = GetAssetAttributesBy(mPerformanceLister.SearchCriteria.AssetTypeId);
                                if (assetAttributes != null && assetAttributes.Count > 0)
                                {
                                    foreach (var item in assetAttributes)
                                    {
                                        if (item.Title.Contains("Last Update Relay End( sec ago)") ||
                                            item.Title.Contains("Last Update Feed End( sec ago)") || item.Title.Contains("Zero offset") || item.Title.Contains("Last Updated")
                                            || item.Title.Contains("Last Access (Sec)") || item.Title.Contains("Last Access")
                                            )
                                        {
                                        }
                                        else
                                        {
                                            mAssetAttributes.Add(item);
                                        }
                                    }


                                    if (mAssetAttributes.Select(x => x.AssetTypeName).FirstOrDefault().ToLower() == "TRACK".ToLower())
                                    {
                                        var leakage = new Domain.AssetAttribute();
                                        leakage.AssetName = mAssetAttributes.Select(x => x.AssetName).FirstOrDefault();
                                        leakage.Title = "Leakage";
                                        mAssetAttributes.Add(leakage);
                                    }

                                    foreach (var assetAttribute in mAssetAttributes)
                                    {
                                        csv.AppendLine(socsvstring);
                                        var dates = mPerformanceLister.Performances.GroupBy(x => x.TimeStamp.Date).ToList();
                                        socsvstring += ",";
                                        foreach (var dateTime in dates)
                                        {
                                            socsvstring += $"{assetAttribute.Title},";
                                        }
                                        csv.AppendLine(socsvstring);
                                        socsvstring = string.Empty;
                                        socsvstring += ",";
                                        foreach (var dateTime in dates)
                                        {
                                            socsvstring += $"{dateTime.Key.ToString("MM/dd/yyyy")},";
                                        }
                                        csv.AppendLine(socsvstring);
                                        socsvstring = string.Empty;


                                        var assetIds = mPerformanceLister.Performances.GroupBy(x => x.AssetId).Select(x => x.Key).ToList();
                                        if (assetIds != null && assetIds.Count > 0)
                                        {
                                            foreach (var assetId in assetIds)
                                            {
                                                var mPerformance = mPerformanceLister.Performances.Where(x => x.AssetId == assetId).FirstOrDefault();
                                                if (mPerformance != null)
                                                {
                                                    var array = mPerformance.CsvData.Split('~');
                                                    socsvstring += $"{array[2]},";
                                                }

                                                foreach (var dateTime in dates)
                                                {
                                                    mPerformanceLister.DateList.Add(dateTime.Key.ToString("MM/dd/yyyy"));
                                                    var date = dateTime.Key.ToString("dd MMM yyyy");
                                                    var vals = mPerformanceLister.Performances.Where(y => y.TimeStamp.Date == dateTime.Key.Date && y.AssetId == assetId).LastOrDefault();
                                                    if (vals != null && vals.CsvData.IsNotNullOrEmpty())
                                                    {
                                                        //foreach (var vals in performances)
                                                        {
                                                            var array = vals.CsvData.Split('~');
                                                            if (!string.IsNullOrEmpty(Convert.ToString(array[0])))
                                                            {
                                                                var data = Convert.ToDateTime(Convert.ToString(array[0]));

                                                            }

                                                            //mAsset.DateList.Add(Convert.ToString(array[0]));
                                                            int i = 3;
                                                            decimal ifma = 0;
                                                            decimal irma = 0;
                                                            foreach (var item in mAssetAttributes)
                                                            {
                                                                if (i != 9 || i != 10)
                                                                {
                                                                    if (item.Title == "If mA")
                                                                    {
                                                                        if (!string.IsNullOrEmpty(array[i]))
                                                                        {
                                                                            ifma = Convert.ToDecimal(array[i]);
                                                                        }
                                                                        else
                                                                        {
                                                                            ifma = 0;
                                                                        }

                                                                    }
                                                                    if (item.Title == "Ir mA")
                                                                    {
                                                                        if (!string.IsNullOrEmpty(array[i]))
                                                                        {
                                                                            irma = Convert.ToDecimal(array[i]);
                                                                        }
                                                                        else
                                                                        {
                                                                            irma = 0;
                                                                        }

                                                                    }
                                                                    if (item.Title == "Leakage")
                                                                    {
                                                                        item.DataArray.Add(Convert.ToString(ifma - irma));
                                                                        item.MultiplicationValue = Convert.ToString(ifma - irma);
                                                                    }
                                                                    else
                                                                    {
                                                                        if (array.Length > i)
                                                                        {
                                                                            item.DataArray.Add(Convert.ToString(array[i]));
                                                                            item.MultiplicationValue = Convert.ToString(array[i]);
                                                                        }

                                                                    }
                                                                }

                                                                i++;
                                                            }


                                                            foreach (var csvAssetAttr in mAssetAttributes.Where(x => x.Id == assetAttribute.Id).ToList())
                                                            {
                                                                if (csvAssetAttr.MultiplicationValue.IsNotNullOrEmpty())
                                                                    socsvstring += $"{csvAssetAttr.MultiplicationValue},";
                                                            }



                                                        }
                                                    }
                                                    else
                                                    {
                                                        socsvstring += ",";
                                                    }


                                                }
                                                csv.AppendLine(socsvstring);
                                                socsvstring = string.Empty;
                                            }
                                        }




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
            mPerformanceLister.AssetAttributes = mAssetAttributes;
            Response.Clear();
            Response.Buffer = true;
            Response.AddHeader("content-disposition", "attachment;filename=Performance" + DateTime.Now.Ticks + ".csv");
            Response.Charset = "utf-8";
            Response.ContentType = "text/csv";
            Response.Output.Write(csv);
            Response.Flush();
            Response.End();
            return View("");
        }

        public PerformanceLister GetPerformanceAttributes(int assetTypeId, int siteId)
        {
            var mAssetAttributes = new List<AssetAttribute>();
            PerformanceLister mPerformanceLister = new PerformanceLister();
            mPerformanceLister.Pager.Take = -1;
            mPerformanceLister.SearchCriteria.SiteId = siteId;
            mPerformanceLister.SearchCriteria.AssetTypeId = assetTypeId;
            if (mPerformanceLister != null && mPerformanceLister.SearchCriteria != null && mPerformanceLister.SearchCriteria.SiteId > 0)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mPerformanceLister);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("SiteKeeping/GetPerformanceLister"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mPerformanceLister = JsonConvert.DeserializeObject<PerformanceLister>(jsonString);
                            if (mPerformanceLister != null && mPerformanceLister.Performances != null && mPerformanceLister.Performances.Count > 0)
                            {
                                var assetAttributes = GetAssetAttributesBy(mPerformanceLister.SearchCriteria.AssetTypeId);
                                if (assetAttributes != null && assetAttributes.Count > 0)
                                {
                                    foreach (var item in assetAttributes)
                                    {
                                        if (item.Title.Contains("Last Update Relay End( sec ago)") ||
                                            item.Title.Contains("Last Update Feed End( sec ago)") || item.Title.Contains("Zero offset") || item.Title.Contains("Last Updated")
                                            || item.Title.Contains("Last Access (Sec)") || item.Title.Contains("Last Access")
                                            )
                                        {
                                        }
                                        else
                                        {
                                            mAssetAttributes.Add(item);
                                        }
                                    }


                                    if (mAssetAttributes.Select(x => x.AssetTypeName).FirstOrDefault().ToLower() == "TRACK".ToLower())
                                    {
                                        var leakage = new Domain.AssetAttribute();
                                        leakage.AssetName = mAssetAttributes.Select(x => x.AssetName).FirstOrDefault();
                                        leakage.Title = "Leakage";
                                        mAssetAttributes.Add(leakage);
                                    }

                                    var dates = mPerformanceLister.Performances.GroupBy(x => x.TimeStamp).ToList();
                                    foreach (var dateTime in dates)
                                    {
                                        mPerformanceLister.DateList.Add(dateTime.Key.ToString("MM/dd/yyyy"));
                                        var date = dateTime.Key.ToString("dd MMM yyyy");
                                        foreach (var vals in mPerformanceLister.Performances.Where(y => y.TimeStamp.Date == dateTime.Key.Date).Select(x => x.CsvData).ToList())
                                        {
                                            var array = vals.Split('~');

                                            if (!string.IsNullOrEmpty(Convert.ToString(array[0])))
                                            {
                                                var data = Convert.ToDateTime(Convert.ToString(array[0]));

                                            }

                                            //mAsset.DateList.Add(Convert.ToString(array[0]));
                                            int i = 3;
                                            decimal ifma = 0;
                                            decimal irma = 0;
                                            foreach (var item in mAssetAttributes)
                                            {
                                                if (i != 9 || i != 10)
                                                {
                                                    if (item.Title == "If mA")
                                                    {
                                                        if (!string.IsNullOrEmpty(array[i]))
                                                        {
                                                            ifma = Convert.ToDecimal(array[i]);
                                                        }
                                                        else
                                                        {
                                                            ifma = 0;
                                                        }

                                                    }
                                                    if (item.Title == "Ir mA")
                                                    {
                                                        if (!string.IsNullOrEmpty(array[i]))
                                                        {
                                                            irma = Convert.ToDecimal(array[i]);
                                                        }
                                                        else
                                                        {
                                                            irma = 0;
                                                        }

                                                    }
                                                    if (item.Title == "Leakage")
                                                    {
                                                        item.DataArray.Add(Convert.ToString(ifma - irma));
                                                    }
                                                    else
                                                    {
                                                        item.DataArray.Add(Convert.ToString(array[i]));
                                                    }
                                                }
                                                i++;
                                            }


                                        }
                                        foreach (var atrv in mAssetAttributes)
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

                            }
                        }
                    }
                }
                catch (Exception)
                {

                }
            }
            mPerformanceLister.AssetAttributes = mAssetAttributes;

            return mPerformanceLister;
        }

    }
}