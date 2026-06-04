using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Utility;
using ICSharpCode.SharpZipLib.Zip;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class CommissioningDocumentController : Controller
    {
        // GET: CommissioningDocument
        private readonly ISiteService siteService;
        public CommissioningDocumentController(ISiteService siteService)
        {
            this.siteService = siteService;
        }

        public ActionResult Index(int siteId = 0)
        {
            List<Domain.CommissioningDocument> mCommissioningDocuments = new List<Domain.CommissioningDocument>();
            if (siteId > 0)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = hcf.client.GetAsync(String.Format("CommissioningDocument/SiteId/{0}", siteId)).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            mCommissioningDocuments = JsonConvert.DeserializeObject<List<Domain.CommissioningDocument>>(jsonString);
                        }
                        else if (response.StatusCode == HttpStatusCode.InternalServerError)
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "Internal server error!";
                        }
                        else if (response.StatusCode == HttpStatusCode.Forbidden)
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "Forbidden!";
                        }
                    }
                }
                catch (Exception ex)
                {
                    ViewBag.Type = "Error";
                    ViewBag.Message = "Internal server error!";
                }
            }
            ViewBag.CommissioningDocuments = E7FRSAdvance.Utility.EnumHelper.GetEnumDisplayNames(new E7FRSAdvance.Utility.Utility.CommissioningDocument());
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            return View(mCommissioningDocuments);
        }

        public ActionResult DownloadNetworkDetails(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CommissioningDocument/DownloadNetworkDetails/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                var mSite = siteService.Get(siteId);
                                if (mSite != null && mSite.Id > 0)
                                {
                                    return File(csvbytes, "application/pdf", $"{mSite.Name}-NetworkDetails.pdf");
                                }
                            }
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "File not found!";
                        }

                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }

            return View("Index", BindDropDown(siteId));
        }

        public ActionResult UploadNetworkDetails(int siteId)
        {
            dynamic data = new ExpandoObject();
            for (int i = 0; i < Request.Files.Count; i++)
            {
                var file = Request.Files[i];

                byte[] thePictureAsBytes = new byte[file.ContentLength];
                using (BinaryReader theReader = new BinaryReader(file.InputStream))
                {
                    thePictureAsBytes = theReader.ReadBytes(file.ContentLength);
                }
                if (thePictureAsBytes != null && thePictureAsBytes.Length > 0)
                {
                    try
                    {
                        using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                        {
                            Domain.CommissioningDocument mCommissioningDocument = new Domain.CommissioningDocument() { FileBase64 = Convert.ToBase64String(thePictureAsBytes), SiteId = siteId, TypeId = (int)E7FRSAdvance.Utility.Utility.CommissioningDocument.NetworkDetails, CreatedBy = ClsHttpContent.LoginUser.Id };

                            var jsonStr = JsonConvert.SerializeObject(mCommissioningDocument);
                            StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                            var response = hcf.client.PostAsync(String.Format("CommissioningDocument/UploadNetworkDetails"), str).Result;
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            if (response.StatusCode == HttpStatusCode.OK)
                            {
                                mCommissioningDocument = JsonConvert.DeserializeObject<Domain.CommissioningDocument>(jsonString);
                                if (mCommissioningDocument != null && mCommissioningDocument.Id > 0)
                                    data = new { type = "success", result = "Updated." };
                                else
                                    data = new { type = "error", result = "Internal server error!" };
                            }
                            else
                                data = new { type = "error", result = "Internal server error!" };
                        }
                    }
                    catch (Exception)
                    {
                        data = new { type = "error", result = "Internal server error!" };
                    }
                }

            }

            return Json(data, JsonRequestBehavior.AllowGet);
        }


        public ActionResult DownloadSiteSurvey(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CommissioningDocument/DownloadSiteSurvey/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                var mSite = siteService.Get(siteId);
                                if (mSite != null && mSite.Id > 0)
                                    return File(csvbytes, "application/pdf", $"{mSite.Name}-SiteSurvey.pdf");
                            }
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "File not found!";
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }

            return View("Index", BindDropDown(siteId));
        }

        public ActionResult UploadSiteSurvey(int siteId)
        {
            dynamic data = new ExpandoObject();
            for (int i = 0; i < Request.Files.Count; i++)
            {
                var file = Request.Files[i];

                byte[] fileBytes = new byte[file.ContentLength];
                using (BinaryReader theReader = new BinaryReader(file.InputStream))
                {
                    fileBytes = theReader.ReadBytes(file.ContentLength);
                }
                if (fileBytes != null && fileBytes.Length > 0)
                {
                    try
                    {
                        using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                        {
                            Domain.CommissioningDocument mCommissioningDocument = new Domain.CommissioningDocument() { FileBase64 = Convert.ToBase64String(fileBytes), SiteId = siteId, TypeId = (int)E7FRSAdvance.Utility.Utility.CommissioningDocument.SiteSurvey, CreatedBy = ClsHttpContent.LoginUser.Id };

                            var jsonStr = JsonConvert.SerializeObject(mCommissioningDocument);
                            StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                            var response = hcf.client.PostAsync(String.Format("CommissioningDocument/UploadSiteSurvey"), str).Result;
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            if (response.StatusCode == HttpStatusCode.OK)
                            {
                                mCommissioningDocument = JsonConvert.DeserializeObject<Domain.CommissioningDocument>(jsonString);
                                if (mCommissioningDocument != null && mCommissioningDocument.Id > 0)
                                    data = new { type = "success", result = "Updated." };
                                else
                                    data = new { type = "error", result = "Internal server error!" };
                            }
                            else
                                data = new { type = "error", result = "Internal server error!" };
                        }
                    }
                    catch (Exception)
                    {
                        data = new { type = "error", result = "Internal server error!" };
                    }
                }

            }

            return Json(data, JsonRequestBehavior.AllowGet);
        }

        public ActionResult DownloadSiteSurveyAutomatic(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CommissioningDocument/DownloadSiteSurveyAutomatic/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                var mSite = siteService.Get(siteId);
                                if (mSite != null && mSite.Id > 0)
                                    return File(csvbytes, "application/pdf", $"{mSite.Name}-SiteSurvey.pdf");
                            }
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "File not found!";
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }

            return View("Index", BindDropDown(siteId));
        }


        public ActionResult DownloadPannelTestReport(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CommissioningDocument/DownloadPannelTestReport/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                var mSite = siteService.Get(siteId);
                                if (mSite != null && mSite.Id > 0)
                                    return File(csvbytes, "application/pdf", $"{mSite.Name}-PannelTestReport.pdf");
                            }
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "File not found!";
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }

            return View("Index", BindDropDown(siteId));
        }

        public ActionResult UploadPannelTestReport(int siteId)
        {
            dynamic data = new ExpandoObject();
            for (int i = 0; i < Request.Files.Count; i++)
            {
                var file = Request.Files[i];

                byte[] fileBytes = new byte[file.ContentLength];
                using (BinaryReader theReader = new BinaryReader(file.InputStream))
                {
                    fileBytes = theReader.ReadBytes(file.ContentLength);
                }
                if (fileBytes != null && fileBytes.Length > 0)
                {
                    try
                    {
                        using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                        {
                            Domain.CommissioningDocument mCommissioningDocument = new Domain.CommissioningDocument() { FileBase64 = Convert.ToBase64String(fileBytes), SiteId = siteId, TypeId = (int)E7FRSAdvance.Utility.Utility.CommissioningDocument.PannelTestReport, CreatedBy = ClsHttpContent.LoginUser.Id };

                            var jsonStr = JsonConvert.SerializeObject(mCommissioningDocument);
                            StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                            var response = hcf.client.PostAsync(String.Format("CommissioningDocument/UploadPannelTestReport"), str).Result;
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            if (response.StatusCode == HttpStatusCode.OK)
                            {
                                mCommissioningDocument = JsonConvert.DeserializeObject<Domain.CommissioningDocument>(jsonString);
                                if (mCommissioningDocument != null && mCommissioningDocument.Id > 0)
                                    data = new { type = "success", result = "Updated." };
                                else
                                    data = new { type = "error", result = "Internal server error!" };
                            }
                            else
                                data = new { type = "error", result = "Internal server error!" };
                        }
                    }
                    catch (Exception)
                    {
                        data = new { type = "error", result = "Internal server error!" };
                    }
                }

            }

            return Json(data, JsonRequestBehavior.AllowGet);
        }


        public ActionResult DownloadClusterValidation(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CommissioningDocument/DownloadClusterValidation/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                var mSite = siteService.Get(siteId);
                                if (mSite != null && mSite.Id > 0)
                                    return File(csvbytes, "application/pdf", $"{mSite.Name}-ClusterValidationFile.pdf");
                            }
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "File not found!";
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }

            return View("Index", BindDropDown(siteId));
        }

        public ActionResult DownloadClusterValidationAutomatic(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CommissioningDocument/DownloadClusterValidationAutomatic/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                var mSite = siteService.Get(siteId);
                                if (mSite != null && mSite.Id > 0)
                                    return File(csvbytes, "application/pdf", $"{mSite.Name}-ClusterValidationFile.pdf");
                            }
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "File not found!";
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }

            return View("Index", BindDropDown(siteId));
        }

        public ActionResult UploadClusterValidation(int siteId)
        {
            dynamic data = new ExpandoObject();
            for (int i = 0; i < Request.Files.Count; i++)
            {
                var file = Request.Files[i];

                byte[] fileBytes = new byte[file.ContentLength];
                using (BinaryReader theReader = new BinaryReader(file.InputStream))
                {
                    fileBytes = theReader.ReadBytes(file.ContentLength);
                }
                if (fileBytes != null && fileBytes.Length > 0)
                {
                    try
                    {
                        using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                        {
                            Domain.CommissioningDocument mCommissioningDocument = new Domain.CommissioningDocument() { FileBase64 = Convert.ToBase64String(fileBytes), SiteId = siteId, TypeId = (int)E7FRSAdvance.Utility.Utility.CommissioningDocument.ClusterValidation, CreatedBy = ClsHttpContent.LoginUser.Id };

                            var jsonStr = JsonConvert.SerializeObject(mCommissioningDocument);
                            StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                            var response = hcf.client.PostAsync(String.Format("CommissioningDocument/UploadClusterValidation"), str).Result;
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            if (response.StatusCode == HttpStatusCode.OK)
                            {
                                mCommissioningDocument = JsonConvert.DeserializeObject<Domain.CommissioningDocument>(jsonString);
                                if (mCommissioningDocument != null && mCommissioningDocument.Id > 0)
                                    data = new { type = "success", result = "Updated." };
                                else
                                    data = new { type = "error", result = "Internal server error!" };
                            }
                            else
                                data = new { type = "error", result = "Internal server error!" };
                        }
                    }
                    catch (Exception)
                    {
                        data = new { type = "error", result = "Internal server error!" };
                    }
                }

            }

            return Json(data, JsonRequestBehavior.AllowGet);
        }


        public ActionResult DownloadRDPMSCommissioningList(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CommissioningDocument/DownloadRDPMSCommissioningList/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                var mSite = siteService.Get(siteId);
                                if (mSite != null && mSite.Id > 0)
                                    return File(csvbytes, "application/pdf", $"{mSite.Name}-RDPMSCommissioning.pdf");
                            }
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "File not found!";
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }

                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }

            return View("Index", BindDropDown(siteId));
        }

        public ActionResult UploadRDPMSCommissioningList(int siteId)
        {
            dynamic data = new ExpandoObject();
            for (int i = 0; i < Request.Files.Count; i++)
            {
                var file = Request.Files[i];

                byte[] fileBytes = new byte[file.ContentLength];
                using (BinaryReader theReader = new BinaryReader(file.InputStream))
                {
                    fileBytes = theReader.ReadBytes(file.ContentLength);
                }
                if (fileBytes != null && fileBytes.Length > 0)
                {
                    try
                    {
                        using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                        {
                            Domain.CommissioningDocument mCommissioningDocument = new Domain.CommissioningDocument() { FileBase64 = Convert.ToBase64String(fileBytes), SiteId = siteId, TypeId = (int)E7FRSAdvance.Utility.Utility.CommissioningDocument.RDPMSCommissioningList, CreatedBy = ClsHttpContent.LoginUser.Id };

                            var jsonStr = JsonConvert.SerializeObject(mCommissioningDocument);
                            StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                            var response = hcf.client.PostAsync(String.Format("CommissioningDocument/UploadRDPMSCommissioningList"), str).Result;
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            if (response.StatusCode == HttpStatusCode.OK)
                            {
                                mCommissioningDocument = JsonConvert.DeserializeObject<Domain.CommissioningDocument>(jsonString);
                                if (mCommissioningDocument != null && mCommissioningDocument.Id > 0)
                                    data = new { type = "success", result = "Updated." };
                                else
                                    data = new { type = "error", result = "Internal server error!" };
                            }
                            else
                                data = new { type = "error", result = "Internal server error!" };
                        }
                    }
                    catch (Exception)
                    {
                        data = new { type = "error", result = "Internal server error!" };
                    }
                }

            }

            return Json(data, JsonRequestBehavior.AllowGet);
        }


        public ActionResult DownloadJointCalibrationReport(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CommissioningDocument/DownloadJointCalibrationReport/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                        if (csvbytes != null && csvbytes.Length > 0)
                        {
                            var mSite = siteService.Get(siteId);
                            if (mSite != null && mSite.Id > 0)
                                return File(csvbytes, "application/pdf", $"{mSite.Name}-JointCalibration.pdf");
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "File not found!";
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }

            return View("Index", BindDropDown(siteId));
        }

        public ActionResult DownloadJointCalibrationReportAutomatic(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CommissioningDocument/DownloadJointCalibrationReportAutomatic/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                        if (csvbytes != null && csvbytes.Length > 0)
                        {
                            var mSite = siteService.Get(siteId);
                            if (mSite != null && mSite.Id > 0)
                                return File(csvbytes, "application/pdf", $"{mSite.Name}-JointCalibration.pdf");
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }

            return View("Index", BindDropDown(siteId));
        }

        public ActionResult UploadJointCalibrationReport(int siteId)
        {
            dynamic data = new ExpandoObject();
            for (int i = 0; i < Request.Files.Count; i++)
            {
                var file = Request.Files[i];

                byte[] fileBytes = new byte[file.ContentLength];
                using (BinaryReader theReader = new BinaryReader(file.InputStream))
                {
                    fileBytes = theReader.ReadBytes(file.ContentLength);
                }
                if (fileBytes != null && fileBytes.Length > 0)
                {
                    try
                    {
                        using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                        {
                            Domain.CommissioningDocument mCommissioningDocument = new Domain.CommissioningDocument() { FileBase64 = Convert.ToBase64String(fileBytes), SiteId = siteId, TypeId = (int)E7FRSAdvance.Utility.Utility.CommissioningDocument.JointCalibrationReport, CreatedBy = ClsHttpContent.LoginUser.Id };

                            var jsonStr = JsonConvert.SerializeObject(mCommissioningDocument);
                            StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                            var response = hcf.client.PostAsync(String.Format("CommissioningDocument/UploadJointCalibrationReport"), str).Result;
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            if (response.StatusCode == HttpStatusCode.OK)
                            {
                                mCommissioningDocument = JsonConvert.DeserializeObject<Domain.CommissioningDocument>(jsonString);
                                if (mCommissioningDocument != null && mCommissioningDocument.Id > 0)
                                    data = new { type = "success", result = "Updated." };
                                else
                                    data = new { type = "error", result = "Internal server error!" };
                            }
                            else
                                data = new { type = "error", result = "Internal server error!" };
                        }
                    }
                    catch (Exception)
                    {
                        data = new { type = "error", result = "Internal server error!" };
                    }
                }

            }

            return Json(data, JsonRequestBehavior.AllowGet);
        }


        public ActionResult DownloadBitChart(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CommissioningDocument/DownloadBitChart/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                var mSite = siteService.Get(siteId);
                                if (mSite != null && mSite.Id > 0)
                                    return File(csvbytes, "application/pdf", $"{mSite.Name}-BitChart.pdf");
                            }
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }

            return View("Index", BindDropDown(siteId));
        }

        public ActionResult DownloadMaterialPositioning(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CommissioningDocument/DownloadMaterialPositioning/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                        if (csvbytes != null && csvbytes.Length > 0)
                        {
                            var mSite = siteService.Get(siteId);
                            if (mSite != null && mSite.Id > 0)
                                return File(csvbytes, "application/pdf", $"{mSite.Name}-MaterialPositioning.pdf");
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }

            return View("Index", BindDropDown(siteId));
        }

        public ActionResult DownloadPannelTestReportAutomatic(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CommissioningDocument/DownloadPannelTestReportAutomatic/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                        if (csvbytes != null && csvbytes.Length > 0)
                        {
                            var mSite = siteService.Get(siteId);
                            if (mSite != null && mSite.Id > 0)
                                return File(csvbytes, "application/pdf", $"{mSite.Name}-PannelTestReport.pdf");
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }

            return View("Index", BindDropDown(siteId));
        }



        public ActionResult DownloadCircuitDrawing(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CommissioningDocument/DownloadCircuitDrawing/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                        if (csvbytes != null && csvbytes.Length > 0)
                        {
                            var mSite = siteService.Get(siteId);
                            if (mSite != null && mSite.Id > 0)
                                return File(csvbytes, "application/pdf", $"{mSite.Name}-CircuitDrawing.pdf");
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }

            return View("Index", BindDropDown(siteId));
        }

        public ActionResult DownloadGeneralCircuitDiagram()
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CommissioningDocument/DownloadGeneralCircuitDiagram")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                return File(csvbytes, "application/pdf", $"General Circuit Diagram.pdf");
                            }
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }

            return RedirectToAction("Index");
        }

        public ActionResult DownloadRDPMSManual()
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CommissioningDocument/DownloadRDPMSManual")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] pdfbytes = System.Convert.FromBase64String(stringInBase64);
                            if (pdfbytes != null && pdfbytes.Length > 0)
                            {
                                return File(pdfbytes, "application/pdf", $"RDPMSManual.pdf");
                            }
                        }

                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }

            return RedirectToAction("Index");
        }

        public ActionResult DownloadRDPMSArchitecture()
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CommissioningDocument/DownloadRDPMSArchitecture")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                return File(csvbytes, "application/pdf", $"RDPMSArchitecture.pdf");
                            }
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }

            return RedirectToAction("Index");
        }

        public ActionResult DownloadRDPMSBooklet()
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CommissioningDocument/DownloadRDPMSBooklet")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                return File(csvbytes, "application/pdf", $"RDPMS Booklet.pdf");
                            }
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }

            return RedirectToAction("Index");
        }

        public ActionResult DownloadLabTestReport()
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CommissioningDocument/DownloadLabTestReport")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                return File(csvbytes, "application/pdf", $"LabTestReport.zip");
                            }
                        }
                        else if (response.StatusCode == HttpStatusCode.Forbidden)
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "Forbidden!";
                        }
                        else if (response.StatusCode == HttpStatusCode.InternalServerError)
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
                ViewBag.Message = "Internal server error!";
            }

            return RedirectToAction("Index");
        }

        public ActionResult DownloadAllFileInZip(int siteId)
        {
            Dictionary<string, byte[]> byteArrayList = new Dictionary<string, byte[]>();

            #region NetworkDetails
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CommissioningDocument/DownloadNetworkDetails/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                byteArrayList.Add("NetworkDetails", csvbytes);
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            #endregion

            #region BitChart
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CommissioningDocument/DownloadBitChart/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                byteArrayList.Add("BitChart", csvbytes);
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            #endregion

            #region SiteSurvey
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CommissioningDocument/DownloadSiteSurvey/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                byteArrayList.Add("SiteSurvey", csvbytes);
                            }
                        }

                    }
                }
            }
            catch (Exception)
            {
            }

            #endregion

            #region PannelTestReport
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CommissioningDocument/DownloadPannelTestReport/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                byteArrayList.Add("PannelTestReport", csvbytes);

                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            #endregion

            #region ClusterValidationFile
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CommissioningDocument/DownloadClusterValidation/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                byteArrayList.Add("ClusterValidationFile", csvbytes);
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            #endregion

            #region RDPMSCommissioning
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CommissioningDocument/DownloadRDPMSCommissioningList/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                byteArrayList.Add("RDPMSCommissioning", csvbytes);
                            }
                        }

                    }
                }
            }
            catch (Exception)
            {
            }

            #endregion

            #region MaterialPositioning
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CommissioningDocument/DownloadMaterialPositioning/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                        if (csvbytes != null && csvbytes.Length > 0)
                        {
                            byteArrayList.Add("MaterialPositioning", csvbytes);
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            #endregion

            #region JointCalibration
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CommissioningDocument/DownloadJointCalibrationReport/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                        if (csvbytes != null && csvbytes.Length > 0)
                        {
                            byteArrayList.Add("JointCalibration", csvbytes);
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            #endregion

            #region CircuitDrawing
            //try
            //{
            //    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
            //    {
            //        var response = hcf.client.GetAsync(String.Format($"CommissioningDocument/DownloadCircuitDrawing/SiteId/{siteId}")).Result;
            //        string jsonString = response.Content.ReadAsStringAsync().Result;
            //        if (response.StatusCode == HttpStatusCode.OK)
            //        {
            //            string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
            //            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
            //            if (csvbytes != null && csvbytes.Length > 0)
            //            {
            //                byteArrayList.Add("CircuitDrawing", csvbytes);
            //            }
            //        }
            //    }
            //}
            //catch (Exception ex)
            //{
            //}

            #endregion

            #region General Circuit Diagram
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CommissioningDocument/DownloadGeneralCircuitDiagram")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                byteArrayList.Add("General Circuit Diagram", csvbytes);
                                // return File(csvbytes, "application/pdf", $"General Circuit Diagram.pdf");
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            #endregion

            #region RDPMSManual
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CommissioningDocument/DownloadRDPMSManual")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] pdfbytes = System.Convert.FromBase64String(stringInBase64);
                            if (pdfbytes != null && pdfbytes.Length > 0)
                            {
                                byteArrayList.Add("RDPMSManual", pdfbytes);
                            }
                        }

                    }
                }
            }
            catch (Exception)
            {
            }
            #endregion

            #region RDPMSArchitecture
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CommissioningDocument/DownloadRDPMSArchitecture")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                byteArrayList.Add("RDPMSArchitecture", csvbytes);
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            #endregion

            #region RDPMS Booklet
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CommissioningDocument/DownloadRDPMSBooklet")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                byteArrayList.Add("RDPMS Booklet", csvbytes);
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            #endregion

            #region LabTestReport
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CommissioningDocument/DownloadLabTestReport")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                byteArrayList.Add("LabTestReport", csvbytes);
                            }
                        }

                    }
                }
            }
            catch (Exception)
            {
            }
            #endregion

            var mSite = siteService.Get(siteId);
            if (mSite != null && mSite.Id > 0 && byteArrayList != null && byteArrayList.Count > 0)
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
                    {
                        foreach (var file in byteArrayList)
                        {
                            var entry = archive.CreateEntry($"{file.Key}.pdf", CompressionLevel.Fastest);
                            using (var zipStream = entry.Open())
                            {
                                zipStream.Write(file.Value, 0, file.Value.Length);
                            }
                        }
                    }
                    return File(ms.ToArray(), "application/zip", $"{mSite.Name}-CommissioningDocument.zip");
                }
            }


            return RedirectToAction("Index");
        }

        public List<Domain.CommissioningDocument> BindDropDown(int siteId)
        {
            List<Domain.CommissioningDocument> mCommissioningDocuments = new List<Domain.CommissioningDocument>();
            if (siteId > 0)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = hcf.client.GetAsync(String.Format("CommissioningDocument/SiteId/{0}", siteId)).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            mCommissioningDocuments = JsonConvert.DeserializeObject<List<Domain.CommissioningDocument>>(jsonString);
                        }
                    }
                }
                catch (Exception ex)
                {
                }
            }

            ViewBag.CommissioningDocuments = E7FRSAdvance.Utility.EnumHelper.GetEnumDisplayNames(new E7FRSAdvance.Utility.Utility.CommissioningDocument());
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");


            return mCommissioningDocuments;
        }
    }
}