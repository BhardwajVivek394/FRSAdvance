using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;

namespace E7FRSAdvance.Controllers
{
    // [Authenticate]
    public class AssetAttributeController : Controller
    {
        // GET: AssetAttribute
        private readonly IAssetAttributeService assetAttributeService;
        private readonly ISiteService siteService;
        public AssetAttributeController(IAssetAttributeService assetAttributeService, ISiteService siteService)
        {
            this.assetAttributeService = assetAttributeService;
            this.siteService = siteService;
        }
        public ActionResult Index()
        {
            return View();
        }

        public ActionResult _AssetAttributePartial(string siteName, int assetId, int assetTypeId, string startDate = null, string endDate = null)
        {
            if (siteName.IsNotNullOrEmpty())
                siteName = siteName.Split('-').FirstOrDefault().Trim();

            ViewBag.siteName = siteName;
            ViewBag.assetId = assetId;
            ViewBag.assetTypeId = assetTypeId;
            ViewBag.startDate = startDate;
            ViewBag.endDate = endDate;

            if (assetTypeId > 0)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = hcf.client.GetAsync(String.Format("AssetType/GetAssetTypeById/{0}", assetTypeId)).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            var mAssetType = JsonConvert.DeserializeObject<AssetType>(jsonString);
                            if (mAssetType != null && mAssetType.Id > 0)
                            {
                                ViewBag.AssetTypeName = mAssetType.Name;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                }
            }


            return PartialView(new Site());
        }

        public ActionResult _GetAssetDetails(string siteName, int assetId, int assetTypeId, string startDate = null, string endDate = null)
        {
            var mAssetLister = new AssetLister();
            if (!string.IsNullOrEmpty(startDate))
            {
                startDate = startDate.Replace("/", "-");
                mAssetLister.SearchCriteria.StartDate = startDate;

            }

            if (!string.IsNullOrEmpty(endDate))
            {
                endDate = endDate.Replace("/", "-");
                mAssetLister.SearchCriteria.EndDate = endDate;
            }
            var site = siteService.GetBy(siteName);
            if (site != null && site.Id > 0)
            {
                mAssetLister.SearchCriteria.SiteId = site.Id;
                mAssetLister.SearchCriteria.AssetTypeId = assetTypeId;
                mAssetLister.SearchCriteria.Id = assetId;
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {

                        var jsonStr = JsonConvert.SerializeObject(mAssetLister);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("Asset/GetAllSiteDetailsBySiteId"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            if (mAssetLister != null && mAssetLister.SearchCriteria != null)
                            {
                                var mAssetAttributes = assetAttributeService.GetByIsDerived(assetTypeId);
                                if (mAssetAttributes != null && mAssetAttributes.Count > 0)
                                {
                                    ViewBag.DerivedAttributes = mAssetAttributes;
                                }
                            }
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mAssetLister = JsonConvert.DeserializeObject<AssetLister>(jsonString);
                            mAssetLister.SearchCriteria.SiteName = siteName;
                            if (!string.IsNullOrEmpty(startDate))
                                mAssetLister.SearchCriteria.StartDate = startDate.Replace("-", "/");

                            if (!string.IsNullOrEmpty(endDate))
                                mAssetLister.SearchCriteria.EndDate = endDate.Replace("-", "/");

                            if (mAssetLister.SearchCriteria != null && !string.IsNullOrEmpty(mAssetLister.SearchCriteria.AssetTypeName) && mAssetLister.SearchCriteria.AssetTypeName.ToLower() == "Signal".ToLower())
                            {
                                return PartialView("_GetListSignalDashboard", mAssetLister);
                            }
                            if (mAssetLister.SearchCriteria != null && !string.IsNullOrEmpty(mAssetLister.SearchCriteria.AssetTypeName) && mAssetLister.SearchCriteria.AssetTypeName.ToLower() == "Gate".ToLower())
                            {
                                return PartialView("_GetListGateDashboard", mAssetLister);
                            }
                            if (mAssetLister.SearchCriteria != null && !string.IsNullOrEmpty(mAssetLister.SearchCriteria.AssetTypeName) && mAssetLister.SearchCriteria.AssetTypeName.ToLower() == "Point Machine".ToLower())
                            {
                                return PartialView("_GetListPointMachineDashboard", mAssetLister);
                            }
                            if (mAssetLister.SearchCriteria != null && !string.IsNullOrEmpty(mAssetLister.SearchCriteria.AssetTypeName) && mAssetLister.SearchCriteria.AssetTypeName.ToLower() == "AXLE COUNTER".ToLower())
                            {
                                return PartialView("_GetListAssetAxleCounter", mAssetLister);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    ViewBag.Error = ex.Message.ToString();
                }
            }

            return PartialView("_GetListAssetDetails", mAssetLister);
        }


    }
}