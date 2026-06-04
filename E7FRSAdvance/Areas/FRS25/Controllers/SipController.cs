using Domain;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Service;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Policy;
using System.Text;
using System.Web;
using System.Web.Mvc;
using static E7FRSAdvance.Utility.Utility;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    public class SipController : Controller
    {
        private readonly ISiteService _siteService;
        private readonly IZoneService _zoneService;
        private readonly IDivisionService _divisionService;

        public SipController(ISiteService siteService, IZoneService zoneService, IDivisionService divisionService)
        {
            _siteService = siteService;
            _zoneService = zoneService;
            _divisionService = divisionService;
        }

        // ---------------------------------------------------------------------
        //  Index — main editor page.
        //
        //  When a siteId is supplied (either by deep-link query-string or by
        //  the cascade dropdown's POST-back), this action:
        //      1. Populates the Zone / Division / Site dropdowns
        //      2. Fetches the existing AdvancedSIPView row for that site, or
        //         builds an empty one if none exists yet  (← the "create" case)
        //      3. Pushes the layout JSON into ViewBag so the view can hydrate
        //         the editor without an extra GetSipView AJAX call.
        //
        //  The save path (SaveAdvSipView) handles both create and update from
        //  the same endpoint — Id=0 means create, Id>0 means update.
        // ---------------------------------------------------------------------
        public ActionResult Index(int siteId = 0)
        {
            // Cascade dropdowns
            ViewBag.Zones = new SelectList(_zoneService.GetAllZones(), "Id", "Name");
            ViewBag.Divisions = new SelectList(_divisionService.GetAllDivisions(), "Id", "Name");
            ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");

            // Asset lister payload for the existing controls
            AssetLister mAssetLister = new AssetLister();
            mAssetLister.SearchCriteria.SiteId = siteId;
            mAssetLister.SearchCriteria.AssetTypeId = 2;
            mAssetLister.Pager.Take = mAssetLister.Pager.PageSize;

            // Default to an empty SIP view — this is what gets shown for sites
            // that have never been saved yet (the "create" case).
            Domain.AdvancedSIPView mSipView = new Domain.AdvancedSIPView();
            mSipView.SiteId = siteId;

            if (siteId == 0)
            {
                // No site picked yet — render the editor with a blank canvas
                // and let the user pick a Zone / Division / Site from the
                // cascade dropdowns.
                ViewBag.AdvSipView = mSipView;
                return View(mAssetLister);
            }

            // 1. Fetch site/asset details so the right-hand inspectors have
            //    real data to bind against.
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssetLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync("Asset/GetAllSiteDetailsBySiteId", str).Result;
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

            // 2. Bind the existing SIP view for this site (if any).  Falls back
            //    to a fresh AdvancedSIPView { SiteId = siteId, Id = 0 } when
            //    the backend has nothing — that's what the editor uses to
            //    create a new row on first Save.
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Site/GetAdvSipView/{0}", siteId)).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        var fetched = JsonConvert.DeserializeObject<Domain.AdvancedSIPView>(jsonString);
                        if (fetched != null) mSipView = fetched;
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }

            // Guarantee SiteId is set even if the backend payload omitted it.
            if (mSipView.SiteId == 0) mSipView.SiteId = siteId;

            ViewBag.AdvSipView = mSipView;
            return View(mAssetLister);
        }

        // ---------------------------------------------------------------------
        //  Zone / Division / Site cascade endpoints
        // ---------------------------------------------------------------------

        public JsonResult GetDivisionByZoneId(int zoneId)
        {
            var divisions = new List<Domain.Division>();
            try
            {
                divisions = _divisionService.GetByZoneId(zoneId);
            }
            catch (Exception)
            {
                divisions = new List<Domain.Division>();
            }
            return Json(divisions, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetSiteByDivisionId(int divisionId)
        {
            var sites = new List<Domain.Site>();
            try
            {
                sites = _siteService.GetBy(divisionId);
            }
            catch (Exception)
            {
                sites = new List<Domain.Site>();
            }
            return Json(sites, JsonRequestBehavior.AllowGet);
        }

        // ---------------------------------------------------------------------
        //  SIP view save  —  handles BOTH create (Id=0) and update (Id>0).
        //  The backend's Site/SaveAdvSipView decides which by inspecting Id.
        // ---------------------------------------------------------------------
        public JsonResult SaveAdvSipView(Domain.AdvancedSIPView sipView)
        {
            bool isSuccess = false;

            // Defensive: never write a row without a real site.
            if (sipView == null || sipView.SiteId <= 0)
            {
                return Json(false, JsonRequestBehavior.AllowGet);
            }

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(sipView);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Site/SaveAdvSipView"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        string value = JsonConvert.DeserializeObject<string>(jsonString);
                        if (value == "Success")
                            isSuccess = true;
                        else
                            isSuccess = false;
                    }
                    else
                    {
                        isSuccess = false;
                    }
                }
            }
            catch (Exception)
            {
                isSuccess = false;
            }
            return Json(isSuccess, JsonRequestBehavior.AllowGet);
        }

        // ---------------------------------------------------------------------
        //  GetSipView — kept for the cascade dropdown so changing the site
        //  in the dropdowns reloads the canvas without a full page refresh.
        // ---------------------------------------------------------------------
        public ActionResult GetSipView(int siteId)
        {
            Domain.AdvancedSIPView mSipView = new Domain.AdvancedSIPView();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {

                    var response = hcf.client.GetAsync(String.Format("Site/GetAdvSipView/{0}", siteId)).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSipView = JsonConvert.DeserializeObject<Domain.AdvancedSIPView>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }
            if (mSipView == null)
            {
                mSipView = new Domain.AdvancedSIPView();
                mSipView.SiteId = siteId;
            }
            return Json(mSipView, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetAllAssetWithAttribute(int siteId)
        {
            List<Asset> mAsset = new List<Asset>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Asset/GetAllAssetWithAttribute/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAsset = JsonConvert.DeserializeObject<List<Asset>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return Json(mAsset, JsonRequestBehavior.AllowGet);
        }
    }
}
