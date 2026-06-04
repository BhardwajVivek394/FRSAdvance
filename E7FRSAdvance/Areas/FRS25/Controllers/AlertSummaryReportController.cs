using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Newtonsoft.Json;
using System.Net;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    [E7FRSAdvance.Areas.FRS25.Filter.Authenticate]
    public class AlertSummaryReportController : Controller
    {
        // GET: FRS25/AlertSummaryReport
        private readonly ISiteService _siteService;
        private readonly IZoneService _zoneService;
        private readonly IDivisionService _divisionService;
        private readonly IAssetTypeService _assetTypeService;
        private readonly IAssetService _assetService;
        private readonly ISectionService _sectionService;
        private readonly IFRSAlertService _frsAlertService;
        public AlertSummaryReportController(ISiteService siteService, IZoneService zoneService, IDivisionService divisionService, IAssetTypeService assetTypeService, IAssetService assetService, ISectionService sectionService, IFRSAlertService frsAlertService)
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
            ViewBag.Sections = new SelectList(_sectionService.GetAll(), "Id", "Name");
            ViewBag.AssetTypes = new SelectList(GetFRSAssetType(), "Id", "Name");
            ViewBag.CauseCode = new SelectList(GetCauseCode(), "Id", "Name");
            return View();
        }

        public ActionResult _List(FRSAlertLister mFRSAlertLister)
        {
            if (mFRSAlertLister != null && mFRSAlertLister.SearchCriteria != null && mFRSAlertLister.SearchCriteria.FromDate != null && mFRSAlertLister.SearchCriteria.FromDate == DateTime.MinValue)
            {
                var today = DateTime.Today;
                int diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
                var fromDate = today.AddDays(-diff);

                mFRSAlertLister.SearchCriteria.FromDate = fromDate;

            }

            if (mFRSAlertLister != null && mFRSAlertLister.SearchCriteria != null && mFRSAlertLister.SearchCriteria.ToDate != null && mFRSAlertLister.SearchCriteria.ToDate == DateTime.MinValue)
            {
                mFRSAlertLister.SearchCriteria.ToDate = DateTime.Now;
            }

            mFRSAlertLister.SearchCriteria.IsAcknowledgement = true;
            mFRSAlertLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
            mFRSAlertLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
            if (ClsHttpContent.LoginUser.IsSiteKeeping)
            {
                mFRSAlertLister.SearchCriteria.RoleId = (int)E7FRSAdvance.Utility.Utility.Role.Admin;
            }
            try
            {
                mFRSAlertLister = _frsAlertService.GetWithAcknowledgementAlert(mFRSAlertLister);
                if (mFRSAlertLister != null && mFRSAlertLister.mFRSAlerts != null && mFRSAlertLister.mFRSAlerts.Count > 0)
                {
                    foreach (var mFRSAlert in mFRSAlertLister.mFRSAlerts.GroupBy(x => new { x.AssetTypeId, x.AssetType }).Select(x => x.Key).ToList())
                    {
                        mFRSAlertLister.AssetTypes.Add(new Domain.AssetType() { Id = mFRSAlert.AssetTypeId, Name = mFRSAlert.AssetType });
                    }
                }
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Something went wrong!";
            }

            return PartialView(mFRSAlertLister);
        }

        private List<Domain.AssetType> GetFRSAssetType()
        {

            return (from E7FRSAdvance.Utility.Utility.FRSAssetType e in Enum.GetValues(typeof(E7FRSAdvance.Utility.Utility.FRSAssetType))
                    select new Domain.AssetType
                    {
                        Id = (int)e,
                        Name = e.ToString().Replace("_", " ")
                    }).ToList();
            //ViewBag.StatusEnumList = new SelectList(enumData, "Id", "Name");

            // return new SelectList(enumData, "Id", "Name");
        }

        private List<Domain.AlertInfo> GetCauseCode()
        {
            List<Domain.AlertInfo> causeCodes = new List<Domain.AlertInfo>();
            try
            {
                using (var mHttpClientFactory = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = mHttpClientFactory.client.GetAsync(String.Format("AlertInfo/GetAll")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        causeCodes = JsonConvert.DeserializeObject<List<Domain.AlertInfo>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }

            return causeCodes;
        }

    }
}