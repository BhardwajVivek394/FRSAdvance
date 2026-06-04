using Domain;
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
    public class ExternalAssetController : Controller
    {
        // GET: ExternalAsset
        public ActionResult Index(string siteName = null, string assettypeName = null, string assetName = null)
        {
            AssetLister mAssetLister = new AssetLister();
            Asset mAsset = new Asset();
            mAssetLister.Pager.Take = mAssetLister.Pager.PageSize;
            try
            {
                if ((!string.IsNullOrEmpty(siteName) && !string.IsNullOrWhiteSpace(siteName)) && (!string.IsNullOrEmpty(assettypeName) && !string.IsNullOrWhiteSpace(assettypeName)) && (!string.IsNullOrEmpty(assetName) && !string.IsNullOrWhiteSpace(assetName)))
                {
                    mAssetLister.SearchCriteria.SiteName = siteName;
                    mAssetLister.SearchCriteria.AssetTypeName = assettypeName;
                    mAssetLister.SearchCriteria.Name = assetName;
                    if (!string.IsNullOrEmpty(mAssetLister.SearchCriteria.StartDate) && !string.IsNullOrEmpty(mAssetLister.SearchCriteria.EndDate))
                    {
                        mAssetLister.SearchCriteria.StartDate = mAssetLister.SearchCriteria.StartDate.Replace('/', '-');
                        mAssetLister.SearchCriteria.EndDate = mAssetLister.SearchCriteria.EndDate.Replace('/', '-');
                    }
                    else
                    {
                        //mAssetLister.SearchCriteria.StartDate = DateTime.Now.ToString("MM-dd-yyyy");
                        mAssetLister.SearchCriteria.StartDate = DateTime.Now.AddMonths(-1).ToString("MM-dd-yyyy");
                        mAssetLister.SearchCriteria.EndDate = DateTime.Now.ToString("MM-dd-yyyy");
                    }

                    using (var hcf = new HttpClientFactory())
                    {
                        var jsonStr = JsonConvert.SerializeObject(mAssetLister);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("ExternalAsset/GetAssetDetails"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mAssetLister = JsonConvert.DeserializeObject<AssetLister>(jsonString);
                        }

                        if (mAssetLister.mAssets != null && mAssetLister.mAssets.Count > 0)
                            mAsset = mAssetLister.mAssets.FirstOrDefault();
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return View(mAsset);
        }

        public ActionResult GetAssetInfo(int id, int assetTypeId)
        {
            List<AssetAttribute> mAssetAttributes = new List<AssetAttribute>();
            try
            {
                using (var hcf = new HttpClientFactory())
                {
                    var response = hcf.client.GetAsync(String.Format("ExternalAsset/GetAssetInfo/{0}/{1}", id, assetTypeId)).Result;
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
            return PartialView("~/Views/Site/_AssetInfoPartial.cshtml", mAssetAttributes);
        }

        public JsonResult GetAssetAttribute(int assetId, int siteId, int assetTypeId, string date)
        {
            List<AssetAttribute> mAssetAttributes = new List<AssetAttribute>();
            try
            {
                using (var hcf = new HttpClientFactory())
                {
                    var response = hcf.client.GetAsync(String.Format("ExternalAsset/GetAttribueData/{0}/{1}/{2}/{3}", siteId, assetTypeId, assetId, date)).Result;
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

        public JsonResult GetAssetAttributesData(int assetId, int assetTypeId, int siteId, string startDate, string endDate)
        {
            AssetLister mAssetLister = new AssetLister();
            Asset mAsset = new Asset();
            mAssetLister.Pager.Take = mAssetLister.Pager.PageSize;
            try
            {
                mAssetLister.SearchCriteria.AssetTypeId = assetTypeId;
                mAssetLister.SearchCriteria.Id = assetId;
                mAssetLister.SearchCriteria.SiteId = siteId;
                if (!string.IsNullOrEmpty(startDate) && !string.IsNullOrEmpty(endDate))
                {
                    mAssetLister.SearchCriteria.StartDate = startDate.Replace('/', '-');
                    mAssetLister.SearchCriteria.EndDate = endDate.Replace('/', '-');
                }
                else
                {
                    //mAssetLister.SearchCriteria.StartDate = DateTime.Now.ToString("MM-dd-yyyy");
                    mAssetLister.SearchCriteria.StartDate = DateTime.Now.AddMonths(-1).ToString("MM-dd-yyyy");
                    mAssetLister.SearchCriteria.EndDate = DateTime.Now.ToString("MM-dd-yyyy");
                }
                using (var hcf = new HttpClientFactory())
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssetLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("ExternalAsset/GetAllSiteDetailsBySiteId"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetLister = JsonConvert.DeserializeObject<AssetLister>(jsonString);
                    }

                    if (mAssetLister.mAssets != null && mAssetLister.mAssets.Count > 0)
                        mAsset = mAssetLister.mAssets.FirstOrDefault();
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return Json(mAsset, JsonRequestBehavior.AllowGet);
        }
    }
}