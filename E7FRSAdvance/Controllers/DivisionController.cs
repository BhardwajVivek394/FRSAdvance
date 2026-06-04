using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class DivisionController : Controller
    {
        private readonly ISiteService siteService;
        private readonly IZoneService zoneService;
        public DivisionController(ISiteService siteService, IZoneService zoneService)
        {
            this.siteService = siteService;
            this.zoneService = zoneService;
        }
        // GET: Division
        public ActionResult Index()
        {
            ViewBag.Zones = zoneService.GetAll();
            return View();
        }

        public PartialViewResult DivisionList(DivisionLister mDivisionLister)
        {
            mDivisionLister.Pager.Take = mDivisionLister.Pager.PageSize;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mDivisionLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Division/GetAllDivisionLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mDivisionLister = JsonConvert.DeserializeObject<DivisionLister>(jsonString);
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
      
            return PartialView("_DivisionListPartial", mDivisionLister);
        }

        [HttpPost]
        public ActionResult SaveDivision(Division mDivision)
        {
            APIResponse mAPIResponse = new APIResponse();
            try
            {
                mDivision.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mDivision);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Division/SaveDivision"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAPIResponse = JsonConvert.DeserializeObject<APIResponse>(jsonString);
                        if (mAPIResponse.Value != null && mAPIResponse.IsSuccess)
                        {
                            ViewBag.Message = "Division has been saved.";
                            ViewBag.Type = "Success";
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "Error occured while saving Division!";
                        }
                    }
                    else
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error.";
                    }
                }
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error.";
            }
            finally
            {
                ViewBag.Zones = zoneService.GetAll();
            }

            return PartialView("_AddDivisionPartial", new Division());

        }

        public ActionResult GetDivisionById(int id)
        {
            Division mDivision = new Division();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Division/GetDivisionById/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mDivision = JsonConvert.DeserializeObject<Division>(jsonString);
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
            finally
            {
                ViewBag.Zones = zoneService.GetAll();
            }

            return PartialView("~/Views/Division/_AddDivisionPartial.cshtml", mDivision);
        }

        public ActionResult DeleteDivisionById(int id)
        {
            APIResponse mAPIResponse = new APIResponse();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Division/DeleteDivisionById/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAPIResponse = JsonConvert.DeserializeObject<APIResponse>(jsonString);
                    }
                    else
                    {
                        mAPIResponse.IsSuccess = false;
                        mAPIResponse.Message = "Internal server error.";
                    }
                }
            }
            catch (Exception ex)
            {
                mAPIResponse.Message = ex.Message.ToString();
            }
            return Json(mAPIResponse, JsonRequestBehavior.AllowGet);
        }

        public ActionResult DivisionLayout(int divisionId)
        {

            Division mDivision = new Division();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Division/GetDivisionById/{0}", divisionId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mDivision = JsonConvert.DeserializeObject<Division>(jsonString);
                        if (mDivision != null && mDivision.Id > 0)
                        {
                            ViewBag.DivisionId = divisionId;
                            ViewBag.DivisionName = mDivision.Name;
                        }

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
            //mSipView.SiteId = siteId;
            //AssetLister mAssetLister = new AssetLister();
            //mAssetLister.SearchCriteria.SiteId = siteId;
            //mAssetLister.SearchCriteria.AssetTypeId = 2;
            //mAssetLister.Pager.Take = mAssetLister.Pager.PageSize;
            //try
            //{

            //    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
            //    {
            //        var jsonStr = JsonConvert.SerializeObject(mAssetLister);
            //        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
            //        var response = hcf.client.PostAsync(String.Format("Asset/GetAllSiteDetailsBySiteId"), str).Result;
            //        if (response.StatusCode == HttpStatusCode.OK)
            //        {
            //            string jsonString = response.Content.ReadAsStringAsync().Result;
            //            mAssetLister = JsonConvert.DeserializeObject<AssetLister>(jsonString);
            //        }
            //    }
            //}
            //catch (Exception ex)
            //{
            //    ViewBag.Error = ex.Message.ToString();
            //}
            return View(new AssetLister());
        }

        public ActionResult GetDivisionView(int divisionId)
        {
            Domain.DivisionView mDivisionView = new Domain.DivisionView();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {

                    var response = hcf.client.GetAsync(String.Format("DivisionView/DivisionId/{0}", divisionId)).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mDivisionView = JsonConvert.DeserializeObject<Domain.DivisionView>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }
            var jsonResult = Json(mDivisionView, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;

        }

        public ActionResult GetSite(int divisionId)
        {
            return Json(siteService.GetBy(divisionId), JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public ActionResult SaveDivisionView(DivisionView mDivisionView)
        {
            bool isSave = false;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mDivisionView);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("DivisionView"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        isSave = true;

                    }
                    else
                    {
                        isSave = false;
                    }
                }
            }
            catch (Exception)
            {
                isSave = false;
            }
            return Json(isSave);

        }

        public ActionResult UploadFile()
        {
            var mDivision = new Division();

            for (int i = 0; i < Request.Files.Count; i++)
            {
                var file = Request.Files[i];

                byte[] thePictureAsBytes = new byte[file.ContentLength];
                using (BinaryReader theReader = new BinaryReader(file.InputStream))
                {
                    thePictureAsBytes = theReader.ReadBytes(file.ContentLength);
                }
                var fileBase64 = Convert.ToBase64String(thePictureAsBytes);
                string extension = System.IO.Path.GetExtension(file.FileName);
                mDivision.ImageBase64 = fileBase64;
                mDivision.ImageExtension = extension;
            }

            var jsonResult = Json(mDivision, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;
        }

        public ActionResult GetDivision(int id)
        {
            Division mDivision = new Division();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Division/GetDivisionById/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mDivision = JsonConvert.DeserializeObject<Division>(jsonString);
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
            return Json(mDivision, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetFRSDivision(string code)
        {
            return Json(ExtensionMethod.BuildDivision(code), JsonRequestBehavior.AllowGet);
        }

        public ActionResult _AddDivisionLinkPartial(int id)
        {
            List<Domain.DivisionImportantLink> mDivisionImportantLinks = new List<Domain.DivisionImportantLink>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("DivisionImportantLink/DevisionId/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mDivisionImportantLinks = JsonConvert.DeserializeObject<List<Domain.DivisionImportantLink>>(jsonString);
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
            finally
            {
                ViewBag.DivisionId = id;
            }
            return PartialView("_AddDivisionLinkPartial", mDivisionImportantLinks);
        }

        public ActionResult SaveDivisionImportantLink(List<Domain.DivisionImportantLink> mDivisionImportantLinks)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mDivisionImportantLinks);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("DivisionImportantLink"), str).Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Important Link has been saved." };
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
            return Json(data, JsonRequestBehavior.AllowGet);
        }

        public ActionResult DeleteImportantLink(int id)
        {
            dynamic data = new ExpandoObject();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.DeleteAsync(String.Format($"DivisionImportantLink/{id}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Important Link has been deleted." };
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