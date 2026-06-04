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

namespace E7FRSAdvance.Controllers
{
    public class TestimonialController : Controller
    {
        // GET: Testimonial
        private readonly IDivisionService divisionService;
        private readonly IZoneService zoneService;
        private readonly ISiteService siteService;
        private readonly IAssetTypeService assetTypeService;
        public TestimonialController(IDivisionService divisionService, IZoneService zoneService, ISiteService siteService, IAssetTypeService assetTypeService)
        {
            this.divisionService = divisionService;
            this.siteService = siteService;
            this.assetTypeService = assetTypeService;
            this.zoneService = zoneService;

        }
        public ActionResult Index()
        {
            ViewBag.Divisions = new SelectList(GetDivision(), "Id", "Name");
            ViewBag.Zones = new SelectList(zoneService.GetAll(), "Id", "Name");
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            ViewBag.AssetTypes = new SelectList(assetTypeService.GetAll(), "Id", "Name");
            return View();
        }

        public ActionResult _List(TestimonialLister mTestimonialLister)
        {
            mTestimonialLister.Pager.Take = mTestimonialLister.Pager.PageSize;
            mTestimonialLister.SearchCriteria.CreatedBy = ClsHttpContent.LoginUser.Id;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mTestimonialLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Testimonial/GetAll"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mTestimonialLister = JsonConvert.DeserializeObject<TestimonialLister>(jsonString);
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
                ViewBag.Divisions = new SelectList(GetDivision(), "Id", "Name");
                ViewBag.Zones = new SelectList(zoneService.GetAll(), "Id", "Name");
                ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
                ViewBag.AssetTypes = new SelectList(assetTypeService.GetAll(), "Id", "Name");
            }
            return PartialView(mTestimonialLister);
        }

        public ActionResult _Add(int id)
        {
            Testimonial mTestimonial = new Testimonial();
            if (id > 0)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = hcf.client.GetAsync(String.Format("Testimonial/Id/{0}", id)).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            mTestimonial = JsonConvert.DeserializeObject<Testimonial>(jsonString);
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
                    ViewBag.Divisions = new SelectList(GetDivision(), "Id", "Name");
                    ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
                    ViewBag.AssetTypes = new SelectList(assetTypeService.GetAll(), "Id", "Name");
                }
            }
            else
            {
                ViewBag.Divisions = new SelectList(GetDivision(), "Id", "Name");
                ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
                ViewBag.AssetTypes = new SelectList(assetTypeService.GetAll(), "Id", "Name");
            }


            return PartialView(mTestimonial);
        }

        public ActionResult Create(Testimonial mTestimonial)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mTestimonial.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mTestimonial);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Testimonial"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mTestimonial = JsonConvert.DeserializeObject<Testimonial>(jsonString);
                        if (mTestimonial != null && mTestimonial.Id > 0)
                        {
                            data = new { type = "success", result = "Testimonial has been saved." };
                        }
                        else
                        {
                            data = new { type = "error", result = "Error occured while saving Testimonial!" };
                        }
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

        public ActionResult UploadFile()
        {
            var data = new List<TestimonialImage>();

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
                data.Add(new TestimonialImage() { FileBase64 = fileBase64, FileExtension = extension });
            }

            return Json(data, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetShowRemark(int id)
        {
            Testimonial mTestimonial = new Testimonial();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Testimonial/Id/{id}/IsRailway/{false}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mTestimonial = JsonConvert.DeserializeObject<Testimonial>(jsonString);
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


            return PartialView("_Remark", mTestimonial);
        }

        public ActionResult GetShowRailwayRemark(int id)
        {
            Testimonial mTestimonial = new Testimonial();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Testimonial/Id/{id}/IsRailway/{true}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mTestimonial = JsonConvert.DeserializeObject<Testimonial>(jsonString);
                        if (mTestimonial != null)
                        {

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


            return PartialView("_RailwayRemark", mTestimonial);
        }

        public ActionResult Update(Testimonial mTestimonial)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mTestimonial.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mTestimonial);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Testimonial/Update"), str).Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Testimonial has been saved." };
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

        public ActionResult GetViewRemark(int id, bool isRailway)
        {
            Testimonial mTestimonial = new Testimonial();
            ViewBag.IsRailway = isRailway;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Testimonial/Id/{id}/IsRailway/{isRailway}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mTestimonial = JsonConvert.DeserializeObject<Testimonial>(jsonString);
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


            return PartialView("_ViewRemark", mTestimonial);
        }

        public ActionResult DownloadTestimonial(int divisionId)
        {

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Testimonial/DownloadTestimonial/DivisionId/{divisionId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                        if (csvbytes != null && csvbytes.Length > 0)
                        {
                            return File(csvbytes, "application/pdf", $"TestimonialReport.pdf");
                        }
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
            return View("Index");
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
                mDivisions = GetDivision();
            }

            return Json(mDivisions, JsonRequestBehavior.AllowGet);
        }

        private List<Division> GetDivision()
        {
            var mDivisions = divisionService.GetAll();
            if (mDivisions != null && mDivisions.Count > 0)
            {
                var mTestimonials = GetAll();
                if (mTestimonials != null && mTestimonials.Count > 0)
                {
                    foreach (var mDivision in mDivisions)
                    {
                        var testimonials = mTestimonials.Where(x => x.DivisionId == mDivision.Id).ToList();
                        if (testimonials != null && testimonials.Count > 0)
                        {
                            mDivision.Name = $"{mDivision.Name} ({testimonials.Count})";

                        }
                    }
                }
            }

            return mDivisions;
        }

        private List<Domain.Testimonial> GetAll()
        {
            List<Domain.Testimonial> mTestimonials = new List<Domain.Testimonial>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Testimonial/GetAll")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mTestimonials = JsonConvert.DeserializeObject<List<Domain.Testimonial>>(jsonString);

                    }

                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mTestimonials;
        }
    }
}