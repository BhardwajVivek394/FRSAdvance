using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;

namespace E7FRSAdvance.Controllers
{
    [Utility.Authorization]
    public class AssetController : Controller
    {
        // GET: Asset
        private readonly IZoneService _zoneService;
        private readonly IDivisionService _divisionService;
        private readonly ISiteService _siteService;

        public AssetController(IZoneService zoneService, IDivisionService divisionService, ISiteService siteService)
        {
            _zoneService = zoneService;
            _divisionService = divisionService;
            _siteService = siteService;
        }
        public ActionResult Index()
        {
            ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
            ViewBag.Zones = new SelectList(_zoneService.GetAll(), "Id", "Name");
            ViewBag.Divisions = new SelectList(_divisionService.GetAll(), "Id", "Name");
            return View(new Domain.SearchCriteria());
        }

        public PartialViewResult SiteList(SiteLister mSiteLister)
        {
            mSiteLister.Pager.Take = -1;
            mSiteLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
            mSiteLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
            mSiteLister.SearchCriteria.IsCheckStatus = true;
            mSiteLister.SearchCriteria.IsActive = true;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSiteLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Site/GetAllSite"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSiteLister = JsonConvert.DeserializeObject<SiteLister>(jsonString);
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
                Domain.AssetLister mAssetLister = new AssetLister();
                mAssetLister.SearchCriteria.DivisionId = mSiteLister.SearchCriteria.DivisionId;
                mAssetLister.SearchCriteria.ZoneId = mSiteLister.SearchCriteria.ZoneId;
                mAssetLister.SearchCriteria.SiteId = mSiteLister.SearchCriteria.Id;
                ViewBag.Asset = GetAssetList(mAssetLister);

                Domain.SearchCriteria mSearchCriteria = new Domain.SearchCriteria();
                mSearchCriteria.DivisionId = mSiteLister.SearchCriteria.DivisionId;
                mSearchCriteria.ZoneId = mSiteLister.SearchCriteria.ZoneId;
                mSearchCriteria.SiteId = mSiteLister.SearchCriteria.Id;
                ViewBag.AlertInfoActivation = GetAlertInfoActivationList(mSearchCriteria);
            }
            return PartialView(mSiteLister);
        }

        public ActionResult _AssetList(Domain.AssetLister mAssetLister)
        {
            return PartialView(GetAssetList(mAssetLister));
        }

        public ActionResult _AlertInfoActivationList(Domain.SearchCriteria searchCriteria)
        {
            return PartialView(GetAlertInfoActivationList(searchCriteria));
        }

        public ActionResult UpdateAsset(Domain.Asset mAsset)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mAsset.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAsset);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync($"Asset/Update/{mAsset.Id}", str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Asset has been activated." };
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

            return Json(data);
        }

        public ActionResult UpdateAlertInfoActivation(Domain.AlertInfoActivation mAlertInfoActivation)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAlertInfoActivation);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync($"AlertInfoActivation/{mAlertInfoActivation.Id}", str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Cause code has been activated." };
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

            return Json(data);
        }

        public JsonResult GetDivisionByZoneId(int zoneId)
        {
            List<Division> mDivisions = new List<Division>();
            if (zoneId > 0)
            {
                string result;
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = hcf.client.GetAsync(String.Format("Division/GetAllDivisionsByZoneId/{0}", zoneId)).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            mDivisions = JsonConvert.DeserializeObject<List<Division>>(jsonString);
                        }
                        else
                        {
                            result = "Internal server error.";
                        }
                    }

                }
                catch (Exception ex)
                {
                    result = ex.Message.ToString();
                }
            }
            else
            {
                // mDivisions = GetDivisions();
            }

            return Json(mDivisions, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetSiteByDivisionId(int divisionId)
        {
            var mSites = new List<Domain.Site>();
            try
            {
                mSites = _siteService.GetBy(divisionId);
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }
            return Json(mSites, JsonRequestBehavior.AllowGet);
        }

        #region Private Method
        public List<Domain.AlertInfoActivation> GetAlertInfoActivationList(Domain.SearchCriteria searchCriteria)
        {
            var mAlertInfoActivations = new List<Domain.AlertInfoActivation>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(searchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("AlertInfoActivation/GetInActive"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAlertInfoActivations = JsonConvert.DeserializeObject<List<Domain.AlertInfoActivation>>(jsonString);
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

            return mAlertInfoActivations;
        }

        public Domain.AssetLister GetAssetList(Domain.AssetLister mAssetLister)
        {
            try
            {
                mAssetLister.Pager.Take = -1;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssetLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GetInActiveLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetLister = JsonConvert.DeserializeObject<Domain.AssetLister>(jsonString);
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
                ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
                ViewBag.Zones = new SelectList(_zoneService.GetAll(), "Id", "Name");
                ViewBag.Divisions = new SelectList(_divisionService.GetAll(), "Id", "Name");
            }
            return mAssetLister;
        }

        #endregion
    }
}