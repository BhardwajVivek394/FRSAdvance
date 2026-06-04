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

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    [E7FRSAdvance.Areas.FRS25.Filter.Authenticate]
    public class RDPMSHealthLiveController : Controller
    {
        // GET: FRS25/RDPMSHealthLive
        private readonly ISiteService _siteService;
        private readonly IZoneService _zoneService;
        private readonly IDivisionService _divisionService;
        private readonly IAssetTypeService _assetTypeService;
        private readonly IAssetService _assetService;
        private readonly ISectionService _sectionService;
        private readonly IFRSAlertService _frsAlertService;
        public RDPMSHealthLiveController(ISiteService siteService, IZoneService zoneService, IDivisionService divisionService, IAssetTypeService assetTypeService, IAssetService assetService, ISectionService sectionService, IFRSAlertService frsAlertService)
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

            return View();
        }

        public ActionResult _List(RDPMSHealthLiveLister mAssetLister)
        {

            try
            {
                mAssetLister.Pager.Take = -1;
                mAssetLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
                mAssetLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssetLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GetRDPMSHealthLiveDetails"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetLister = JsonConvert.DeserializeObject<RDPMSHealthLiveLister>(jsonString);
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
            }

            return PartialView(mAssetLister);
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

        public ActionResult Detail()
        {
            ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
            ViewBag.Zones = new SelectList(_zoneService.GetAllZones(), "Id", "Name");
            ViewBag.Divisions = new SelectList(_divisionService.GetAllDivisions(), "Id", "Name");
            ViewBag.AssetTypes = new SelectList(GetFRSAssetType(), "Id", "Name");
            return View();
        }

        public ActionResult _Detail(RDPMSHealthLiveLister mAssetLister)
        {

            try
            {
                mAssetLister.Pager.Take = -1;
                mAssetLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
                mAssetLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;

                if (mAssetLister.SearchCriteria.FromDate != null && mAssetLister.SearchCriteria.FromDate != DateTime.MinValue)
                {
                    DateTime baseDate = mAssetLister.SearchCriteria.FromDate.Date;

                    if (mAssetLister.SearchCriteria.FromTime.IsNotNullOrEmpty())
                    {
                        if (TimeSpan.TryParse(mAssetLister.SearchCriteria.FromTime, out TimeSpan time))
                            mAssetLister.SearchCriteria.FromDate = baseDate.Add(time);
                        else
                            mAssetLister.SearchCriteria.FromDate = baseDate;
                    }
                    else
                        mAssetLister.SearchCriteria.FromDate = baseDate;

                }
                else
                    mAssetLister.SearchCriteria.FromDate = DateTime.Now;

                if (mAssetLister.SearchCriteria.ToDate != null && mAssetLister.SearchCriteria.ToDate != DateTime.MinValue)
                {
                    DateTime baseDate = mAssetLister.SearchCriteria.ToDate.Date;

                    if (mAssetLister.SearchCriteria.ToTime.IsNotNullOrEmpty())
                    {
                        if (TimeSpan.TryParse(mAssetLister.SearchCriteria.ToTime, out TimeSpan time))
                            mAssetLister.SearchCriteria.ToDate = baseDate.Add(time);
                        else
                            mAssetLister.SearchCriteria.ToDate = baseDate;
                    }
                    else
                        mAssetLister.SearchCriteria.ToDate = baseDate;
                }

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssetLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GetRDPMSHealthSummary"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetLister = JsonConvert.DeserializeObject<RDPMSHealthLiveLister>(jsonString);
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
            }

            return PartialView(mAssetLister);
        }
    }
}