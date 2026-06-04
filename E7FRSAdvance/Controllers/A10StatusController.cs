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
using System.Text;
using System.Web.Mvc;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class A10StatusController : Controller
    {
        // GET: A10Status
        private readonly ISiteService _siteService;
        private readonly IZoneService _zoneService;
        private readonly IDivisionService _divisionService;
        public A10StatusController(ISiteService siteService, IZoneService zoneService, IDivisionService divisionService)
        {
            this._siteService = siteService;
            this._zoneService = zoneService;
            this._divisionService = divisionService;
        }
        public ActionResult Index(int siteId = 0)
        {
            var site = _siteService.Get(siteId);
            ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
            ViewBag.Zones = new SelectList(_zoneService.GetAll(), "Id", "Name");
            ViewBag.Divisions = new SelectList(_divisionService.GetAll(), "Id", "Name");

            ViewBag.MQTTDetail = GetMQTTDetailList().mQTTDetailWeb;
            return View(site);
        }

        public MQTTDetailList GetMQTTDetailList()
        {

            MQTTDetailList mMQTTDetail = new MQTTDetailList();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("User/GetMQTTDetail")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mMQTTDetail = JsonConvert.DeserializeObject<MQTTDetailList>(jsonString);
                    }
                    else
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = ex.Message;
            }
            return mMQTTDetail;
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

        public ActionResult GetGraph(int siteId, string date, string a10Id = null)
        {
            var mA10Status = new List<Domain.A10Status>();
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"A10Status/SiteId/{siteId}/SearchDate/{date.Replace("/", "-")}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mA10Status = JsonConvert.DeserializeObject<List<Domain.A10Status>>(jsonString);
                        if (mA10Status != null && mA10Status.Count > 0)
                        {
                            if (a10Id.IsNotNullOrEmpty())
                                mA10Status = mA10Status.Where(x => x.id == a10Id).ToList();

                            mA10Status = mA10Status.Where(x => x.id != "0").ToList();

                            data = mA10Status.GroupBy(x => x.id)
      .ToDictionary(x => x.Key, x => x.Select(e => new { e.last_response, e.timestamp }).ToList());
                        }

                    }
                }
            }
            catch (Exception ex)
            {
            }

            var jsonResult = Json(data, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;
        }

        public ActionResult Download(int siteId, string date, string a10Id = null)
        {
            var mA10Status = new List<Domain.A10Status>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"A10Status/SiteId/{siteId}/SearchDate/{date.Replace("/", "-")}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mA10Status = JsonConvert.DeserializeObject<List<Domain.A10Status>>(jsonString);
                        if (mA10Status != null && mA10Status.Count > 0)
                        {
                            if (a10Id.IsNotNullOrEmpty())
                                mA10Status = mA10Status.Where(x => x.id == a10Id).ToList();

                            mA10Status = mA10Status.Where(x => x.id != "0").ToList();

                        }

                    }
                }
            }
            catch (Exception ex)
            {
            }


            if (mA10Status != null && mA10Status.Count > 0)
            {
                var csv = new StringBuilder();
                var socsvstring = string.Empty;

                socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7}", "id", "name", "last_response", "status", "exceptions", "poll_time", "params", "timestamp");

                csv.AppendLine(socsvstring);
                string type = string.Empty;
                foreach (var item in mA10Status)
                {
                    socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7}", item.id.RemoveComma(), item.name.RemoveComma(), item.last_response.RemoveComma(), item.status.RemoveComma(), item.exceptions, item.poll_time, item.paramsvalue, item.timestamp.RemoveComma());
                    csv.AppendLine(socsvstring);
                }
                Response.Clear();
                Response.Buffer = true;
                Response.AddHeader("content-disposition", "attachment;filename=A10Status" + DateTime.Now.Ticks + ".csv");
                Response.Charset = "utf-8";
                Response.ContentType = "text/csv";
                Response.Output.Write(csv);
                Response.Flush();
                Response.End();

            }
            else
                return RedirectToAction("Index");


            return View();
        }
    }
}