using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class TCPLinkController : Controller
    {
        // GET: TCPLink
        private readonly ISiteService _siteService;
        private readonly IZoneService _zoneService;
        private readonly IDivisionService _divisionService;
        public TCPLinkController(ISiteService siteService, IZoneService zoneService, IDivisionService divisionService)
        {
            this._siteService = siteService;
            this._zoneService = zoneService;
            this._divisionService = divisionService;
        }
        public ActionResult Index()
        {
            return View();
        }

        public ActionResult _List(TCPLinkLister mTCPLinkLister)
        {
            mTCPLinkLister.Pager.Take = mTCPLinkLister.Pager.PageSize;
            if (mTCPLinkLister != null && mTCPLinkLister.SearchCriteria != null && mTCPLinkLister.SearchCriteria.TimeStamp != null && mTCPLinkLister.SearchCriteria.TimeStamp == DateTime.MinValue)
            {
                mTCPLinkLister.SearchCriteria.TimeStamp = DateTime.Now;
            }

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mTCPLinkLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("TCPLink/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mTCPLinkLister = JsonConvert.DeserializeObject<TCPLinkLister>(jsonString);

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
            return PartialView(mTCPLinkLister);
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

    }
}