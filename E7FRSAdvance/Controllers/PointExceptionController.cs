using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;
using static E7FRSAdvance.Utility.Utility;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class PointExceptionController : Controller
    {
        private readonly ISiteService siteService;
        private readonly IAssetService assetService;

        // GET: PointException

        public PointExceptionController(ISiteService siteService, IAssetService assetService)
        {
            this.siteService = siteService;
            this.assetService = assetService;
        }

        public ActionResult Index()
        {
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            return View();
        }

        public ActionResult GetAssestBy(int siteId)
        {
            return Json(assetService.GetAssestBy(siteId, assetTypeId: (int)E7FRSAdvance.Utility.Utility.AssetType.POINT_MACHINE), JsonRequestBehavior.AllowGet);
        }

        public ActionResult _PointMachineEvent(AssetLister mAssetLister)
        {
            mAssetLister.Pager.Take = -1;

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
                        var mPointMachineException = GetPointMachineException(mAssetLister.SearchCriteria.Id);
                        if (mPointMachineException != null && mPointMachineException.Count > 0)
                        {
                            ViewBag.PointMachineException = mPointMachineException;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView(mAssetLister);
        }

        public ActionResult CreatePointMachineEvent(List<Domain.PointMachineException> mPointMachineExceptions)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mPointMachineExceptions);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("PointMachineException"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Point Machine Exception has been saved." };
                    }
                    if (response.StatusCode == HttpStatusCode.BadRequest)
                    {
                        var error = JsonConvert.DeserializeObject<string>(jsonString);
                        data = new { type = "error", result = error };
                    }
                    else
                    {
                        data = new { type = "error", result = "Internal server error." };
                    }
                }
            }
            catch (Exception ex)
            {
                data = new { type = "error", result = "Internal server error." };
            }

            return Json(data);
        }

        public List<PointMachineException> GetPointMachineException(int assetId)
        {
            List<PointMachineException> mPointMachineExceptions = new List<PointMachineException>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"PointMachineException/AssetId/{assetId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mPointMachineExceptions = JsonConvert.DeserializeObject<List<PointMachineException>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mPointMachineExceptions;
        }

        public ActionResult UpdatePointMachineEventPoint()
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"PointMachineException/Update")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        data = new { type = "success", result = "Point Machine Exception has been updated." };
                    }
                    else
                    {
                        data = new { type = "error", result = "Internal server error." };
                    }
                }
            }
            catch (Exception ex)
            {
                data = new { type = "error", result = "Internal server error." };
            }

            return Json(data);

        }


        public ActionResult Edit(int id)
        {
            PointMachineException mPointMachineException = new PointMachineException();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"PointMachineException/{id}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mPointMachineException = JsonConvert.DeserializeObject<PointMachineException>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
            }
            return PartialView(mPointMachineException);
        }


        public ActionResult _List(PointMachineExceptionLister mPointMachineExceptionLister)
        {
            mPointMachineExceptionLister.Pager.Take = -1;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {

                    var jsonStr = JsonConvert.SerializeObject(mPointMachineExceptionLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("PointMachineException/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mPointMachineExceptionLister = JsonConvert.DeserializeObject<PointMachineExceptionLister>(jsonString);
                        if (mPointMachineExceptionLister != null && mPointMachineExceptionLister.PointMachineExceptions != null && mPointMachineExceptionLister.PointMachineExceptions.Count > 0)
                        {
                            mPointMachineExceptionLister.PointMachineExceptions = mPointMachineExceptionLister.PointMachineExceptions.OrderBy(x => x.Cluster).ToList();
                        }

                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView(mPointMachineExceptionLister);
        }

        public ActionResult Update(PointMachineException mPointMachineException)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mPointMachineException);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format($"PointMachineException/{mPointMachineException.Id}"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "PointMachine Exception has been Update." };
                    }
                    else
                    {
                        data = new { type = "error", result = "Internal server error." };
                    }
                }
            }
            catch (Exception ex)
            {
                data = new { type = "error", result = "Internal server error." };
            }

            return Json(data);
        }

        public ActionResult Save(PointMachineException mPointMachineException)
        {

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {

                    var jsonStr = JsonConvert.SerializeObject(mPointMachineException);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format($"PointMachineException/{mPointMachineException.Id}"), str).Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        ViewBag.Message = "Point Machine Exception has been updated.";
                        ViewBag.Type = "Success";
                    }
                    else
                    {
                        ViewBag.Message = "Internal server error.";
                        ViewBag.Type = "Error";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Message = "Internal server error.";
                ViewBag.Type = "Error";
            }
            return PartialView("Edit", mPointMachineException);
        }

        public ActionResult Delete(int id)
        {
            dynamic data = new ExpandoObject();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.DeleteAsync(String.Format("PointMachineException/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var result = JsonConvert.DeserializeObject<bool>(jsonString);
                        if (result)
                        {
                            data = new { type = "success", result = "Remark has been deleted." };
                        }
                        else
                        {
                            data = new { type = "error", result = "Internal server error." };
                        }
                    }
                    else
                    {
                        data = new { type = "error", result = "Internal server error." };
                    }
                }
            }
            catch (Exception ex)
            {
                data = new { type = "error", result = "Internal server error." };
            }
            return Json(data, JsonRequestBehavior.AllowGet);
        }
    }
}