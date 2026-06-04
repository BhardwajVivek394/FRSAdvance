using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Service;
using Newtonsoft.Json;
using System.Net.Http;
using System.Net;
using System.Text;
using System;
using System.Web.Mvc;
using E7FRSAdvance.Interface;
using System.Linq;
using System.Collections.Generic;

namespace E7FRSAdvance.Controllers
{
    [Utility.Authorization]
    public class ActualTimeMonitorController : Controller
    {
        // GET: ActualTimeMonitor
        private readonly ISiteService _siteService;
        public ActualTimeMonitorController(ISiteService siteService)
        {
            this._siteService = siteService;
        }
        public ActionResult Index(int siteId = 0)
        {
            AssetLister mAssetLister = new AssetLister();
            if (siteId > 0)
            {
                mAssetLister.SearchCriteria.SiteId = siteId;
                mAssetLister.Site = _siteService.Get(mAssetLister.SearchCriteria.SiteId);

            }

            ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
            return View(mAssetLister);
        }

        public ActionResult _GetAssetType(int siteId)
        {
            List<Domain.AssetType> mAssetType = new List<Domain.AssetType>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("AssetType/GetAllAssestType/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssetType = JsonConvert.DeserializeObject<List<Domain.AssetType>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView(mAssetType);
        }

        public ActionResult _GetAssetDetails(AssetLister mAssetLister)
        {
            mAssetLister.Pager.Take = -1;// mAssetLister.Pager.PageSize;
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
                        if (mAssetLister != null && mAssetLister.mAssets != null && mAssetLister.mAssets.Count > 0)
                        {
                            mAssetLister.mAssets = mAssetLister.mAssets.OrderBy(x => x.Sequence).ToList();
                        }
                        mAssetLister.Pager.PageSize = -1;

                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            finally
            {
                ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
            }
            return PartialView(mAssetLister);
        }
    }
}