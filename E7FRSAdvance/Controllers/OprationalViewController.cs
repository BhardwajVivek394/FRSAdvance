using Domain;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web;
using System.Web.Mvc;

namespace E7FRSAdvance.Controllers
{
   // [Authenticate]
    public class OprationalViewController : Controller
    {
        // GET: OprationalView
        public ActionResult Index(int siteId)
        {
            var mDeadbandLister = new DeadbandLister();
            mDeadbandLister.SearchCriteria.SiteId = siteId;
            mDeadbandLister.SearchCriteria.Date = DateTime.Now;
            return View(mDeadbandLister);
        }

        public PartialViewResult _OprationalViewList(DeadbandLister mDeadbandLister)
        {
            mDeadbandLister.Pager.Take = mDeadbandLister.Pager.PageSize;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mDeadbandLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Site/GetOprationalView"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mDeadbandLister = JsonConvert.DeserializeObject<DeadbandLister>(jsonString);
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
            return PartialView("_OprationalViewList", mDeadbandLister);
        }
    }
}