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

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    [E7FRSAdvance.Areas.FRS25.Filter.Authenticate]
    public class MaintenanceModeController : Controller
    {
        // GET: FRS25/MaintenanceMode
        private readonly ISiteService _siteService;
        private readonly IZoneService _zoneService;
        private readonly IDivisionService _divisionService;
        private readonly IAssetTypeService _assetTypeService;
        private readonly IAssetService _assetService;
        private readonly ISectionService _sectionService;
        private readonly IFRSAlertService _frsAlertService;
        public MaintenanceModeController(ISiteService siteService, IZoneService zoneService, IDivisionService divisionService, IAssetTypeService assetTypeService, IAssetService assetService, ISectionService sectionService, IFRSAlertService frsAlertService)
        {
            _siteService = siteService;
            _zoneService = zoneService;
            _divisionService = divisionService;
            _assetTypeService = assetTypeService;
            _assetService = assetService;
            _sectionService = sectionService;
            _frsAlertService = frsAlertService;
        }

        public ActionResult Index()
        {
            ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
            ViewBag.Zones = new SelectList(_zoneService.GetAllZones(), "Id", "Name");
            ViewBag.Divisions = new SelectList(_divisionService.GetAllDivisions(), "Id", "Name");
            ViewBag.AssetTypes = new SelectList(GetFRSAssetType(), "Id", "Name");
            //ViewBag.CauseCode = new SelectList(GetCauseCode(), "Id", "Name");
            ViewBag.Sections = new SelectList(_sectionService.GetAll(), "Id", "Name");
            return View();
        }

        public ActionResult _List(Domain.MaintenanceModeLister mMaintenanceModeLister)
        {
            try
            {
                mMaintenanceModeLister.Pager.Take = -1;
                mMaintenanceModeLister.SearchCriteria.IsMaintenceMode = true;
                mMaintenanceModeLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
                mMaintenanceModeLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mMaintenanceModeLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("MaintenanceMode/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mMaintenanceModeLister = JsonConvert.DeserializeObject<MaintenanceModeLister>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return PartialView(mMaintenanceModeLister);
        }


        public ActionResult UpdateMaintenanceActiveMode(Domain.MaintenanceMode mMaintenanceMode)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mMaintenanceMode.IsMaintenceMode = true;
                if (mMaintenanceMode.FromDate != null && mMaintenanceMode.FromDate != DateTime.MinValue)
                {
                    DateTime baseDate = mMaintenanceMode.FromDate.Date;

                    if (mMaintenanceMode.FromTime.IsNotNullOrEmpty())
                    {
                        if (TimeSpan.TryParse(mMaintenanceMode.FromTime, out TimeSpan time))
                            mMaintenanceMode.ActiveTime = baseDate.Add(time);
                        else
                            mMaintenanceMode.ActiveTime = baseDate;
                    }
                    else
                        mMaintenanceMode.ActiveTime = baseDate;

                }
                else
                    mMaintenanceMode.ActiveTime = DateTime.Now;

                if (mMaintenanceMode.ToDate != null && mMaintenanceMode.ToDate != DateTime.MinValue)
                {
                    DateTime baseDate = mMaintenanceMode.ToDate.Date;

                    if (mMaintenanceMode.ToTime.IsNotNullOrEmpty())
                    {
                        if (TimeSpan.TryParse(mMaintenanceMode.ToTime, out TimeSpan time))
                            mMaintenanceMode.InActiveTime = baseDate.Add(time);
                        else
                            mMaintenanceMode.InActiveTime = baseDate;
                    }
                    else
                        mMaintenanceMode.InActiveTime = baseDate;
                }
                mMaintenanceMode.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mMaintenanceMode);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("MaintenanceMode/Create"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Remark has been Updated" };
                    }
                    else
                    {
                        data = new { type = "error", result = "Internal server error." };
                    }
                }
            }
            catch
            {
                data = new { type = "error", result = "Internal server error." };
            }
            return Json(data);
        }

        public ActionResult UpdateMaintenanceInActiveMode(Domain.MaintenanceMode mMaintenanceMode)
        {
            dynamic data = new ExpandoObject();
            try
            {


                mMaintenanceMode.InActiveTime = DateTime.Now;
                mMaintenanceMode.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mMaintenanceMode);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("MaintenanceMode/SetInActive"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        data = new { type = "success", result = "Remark has been Updated" };
                    }
                    else
                    {
                        data = new { type = "error", result = "Internal server error." };
                    }
                }
            }
            catch
            {
                data = new { type = "error", result = "Internal server error." };
            }
            return Json(data);
        }


        public ActionResult DownloadMaintenanceMode(Domain.MaintenanceModeLister mMaintenanceModeLister)
        {
            var mMaintenanceModes = new List<MaintenanceMode>();
            try
            {

                mMaintenanceModeLister.Pager.Take = -1;
                mMaintenanceModeLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
                mMaintenanceModeLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
                mMaintenanceModeLister.SearchCriteria.IsCheckMaintenceModeStatus = true;
                MergeDateAndTime(mMaintenanceModeLister.SearchCriteria);
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mMaintenanceModeLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("MaintenanceMode/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mMaintenanceModeLister = JsonConvert.DeserializeObject<MaintenanceModeLister>(jsonString);
                        if (mMaintenanceModeLister != null && mMaintenanceModeLister.mMaintenanceModes != null)
                        {
                            mMaintenanceModes = mMaintenanceModeLister.mMaintenanceModes;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            var jsonResult = Json(mMaintenanceModes, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;
        }

        private void MergeDateAndTime(Domain.MaintenanceMode mMaintenanceMode)
        {
            if (mMaintenanceMode.FromDate != null && mMaintenanceMode.FromDate != DateTime.MinValue)
            {
                DateTime baseDate = mMaintenanceMode.FromDate.Date;

                if (mMaintenanceMode.FromTime.IsNotNullOrEmpty())
                {
                    if (TimeSpan.TryParse(mMaintenanceMode.FromTime, out TimeSpan time))
                        mMaintenanceMode.ActiveTime = baseDate.Add(time);
                    else
                        mMaintenanceMode.ActiveTime = baseDate;
                }
                else
                    mMaintenanceMode.ActiveTime = baseDate;

            }
            else
                mMaintenanceMode.ActiveTime = DateTime.Now;

            if (mMaintenanceMode.ToDate != null && mMaintenanceMode.ToDate != DateTime.MinValue)
            {
                DateTime baseDate = mMaintenanceMode.ToDate.Date;

                if (mMaintenanceMode.ToTime.IsNotNullOrEmpty())
                {
                    if (TimeSpan.TryParse(mMaintenanceMode.ToTime, out TimeSpan time))
                        mMaintenanceMode.InActiveTime = baseDate.Add(time);
                    else
                        mMaintenanceMode.InActiveTime = baseDate;
                }
                else
                    mMaintenanceMode.InActiveTime = baseDate;
            }
        }

        private List<Domain.AssetType> GetFRSAssetType()
        {

            return (from E7FRSAdvance.Utility.Utility.FRSAssetType e in Enum.GetValues(typeof(E7FRSAdvance.Utility.Utility.FRSAssetType))
                    select new Domain.AssetType
                    {
                        Id = (int)e,
                        Name = e.ToString().Replace("_", " ")
                    }).ToList();
        }

    }
}