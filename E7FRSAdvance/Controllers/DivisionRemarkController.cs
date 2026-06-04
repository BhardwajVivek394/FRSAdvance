using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
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
using static E7FRSAdvance.Utility.Utility;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class DivisionRemarkController : Controller
    {
        private readonly IDivisionService divisionService;
        private readonly IZoneService zoneService;
        private readonly ISiteService siteService;
        private readonly IAssetTypeService assetTypeService;
        public DivisionRemarkController(IDivisionService divisionService, IZoneService zoneService, ISiteService siteService, IAssetTypeService assetTypeService)
        {
            this.divisionService = divisionService;
            this.zoneService = zoneService;
            this.siteService = siteService;
            this.assetTypeService = assetTypeService;
        }

        // GET: DivisionRemark
        public ActionResult Index()
        {
            ViewBag.Zones = new SelectList(zoneService.GetAll(), "Id", "Name");
            ViewBag.Divisions = new SelectList(divisionService.GetAll(), "Id", "Name");
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            ViewBag.AssetTypes = new SelectList(assetTypeService.GetAll(), "Id", "Name");
            BindOperationType();
            return View(new DivisionRemark());
        }

        public PartialViewResult List(DivisionRemarkLister mLister)
        {
            mLister.Pager.Take = mLister.Pager.PageSize;
            mLister.SearchCriteria.CreatedBy = ClsHttpContent.LoginUser.Id;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("DivisionRemark/GetAll"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mLister = JsonConvert.DeserializeObject<DivisionRemarkLister>(jsonString);
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
                ViewBag.Zones = new SelectList(zoneService.GetAll(), "Id", "Name");
                ViewBag.Divisions = new SelectList(divisionService.GetAll(), "Id", "Name");
            }
            return PartialView("_List", mLister);
        }

        public ActionResult _Add(int id)
        {
            var mDivisionRemark = new DivisionRemark();
            try
            {
                if (id > 0)
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = hcf.client.GetAsync(String.Format("DivisionRemark/{0}", id)).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            mDivisionRemark = JsonConvert.DeserializeObject<DivisionRemark>(jsonString);
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "Internal server error!";
                        }
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
                ViewBag.Zones = new SelectList(zoneService.GetAll(), "Id", "Name");
                ViewBag.Divisions = new SelectList(divisionService.GetAll(), "Id", "Name");
                ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
                ViewBag.AssetTypes = new SelectList(assetTypeService.GetAll(), "Id", "Name");
                BindOperationType();
            }

            return PartialView(mDivisionRemark);
        }

        public JsonResult GetSitesBy(int divisionId)
        {
            var sites = new List<Site>();
            try
            {
                sites = siteService.GetBy(divisionId);
            }
            catch (Exception)
            {
            }
            return Json(sites, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public ActionResult Save(DivisionRemark mDivisionRemark)
        {
            try
            {
                mDivisionRemark.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mDivisionRemark);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("DivisionRemark"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mDivisionRemark = JsonConvert.DeserializeObject<DivisionRemark>(jsonString);
                        if (mDivisionRemark != null && mDivisionRemark.Id > 0)
                        {
                            ViewBag.Message = "Division Remark has been saved.";
                            ViewBag.Type = "Success";
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "Error occured while saving Division Remark!";
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
                ViewBag.Zones = new SelectList(zoneService.GetAll(), "Id", "Name");
                ViewBag.Divisions = new SelectList(divisionService.GetAll(), "Id", "Name");
                ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
                ViewBag.AssetTypes = new SelectList(assetTypeService.GetAll(), "Id", "Name");
                BindOperationType();
            }
            return PartialView("_Add", mDivisionRemark);

        }

        public void BindOperationType()
        {
            var enumData = from OperationType e in Enum.GetValues(typeof(OperationType))
                           select new
                           {
                               ID = (int)e,
                               Name = e.ToString().Replace("_", " ")
                           };
            ViewBag.OperationType = new SelectList(enumData, "Id", "Name");

        }

        public ActionResult Delete(int id)
        {
            dynamic data = new ExpandoObject();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.DeleteAsync(String.Format($"DivisionRemark/{id}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        data = new { type = "success", result = "Division Remark has been deleted." };
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

        public ActionResult UploadFile()
        {
            var mDivisionRemark = new DivisionRemark();

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
                mDivisionRemark.ImageBase64 = fileBase64;
                //mDivisionRemark.ImageExtension = extension;
            }

            var jsonResult = Json(mDivisionRemark, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;
        }
    }
}


