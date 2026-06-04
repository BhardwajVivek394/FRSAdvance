using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Service;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;

namespace E7FRSAdvance.Controllers
{
    public class CertificateController : Controller
    {
        private readonly ISiteService siteService;
        public CertificateController(ISiteService siteService)
        {
            this.siteService = siteService;
        }
        // GET: Certificate
        public ActionResult Index()
        {
            //List<Certificate> mCertificate = new List<Certificate>();
            //try
            //{
            //    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
            //    {
            //        var response = hcf.client.GetAsync(String.Format("CertificateGenerator/GetAll")).Result;
            //        string jsonString = response.Content.ReadAsStringAsync().Result;
            //        if (response.StatusCode == HttpStatusCode.OK)
            //        {
            //            mCertificate = JsonConvert.DeserializeObject<List<Certificate>>(jsonString);
            //        }
            //        else
            //        {
            //            ViewBag.Type = "Error";
            //            ViewBag.Message = "Internal server error!";
            //        }
            //    }
            //}
            //catch (Exception ex)
            //{
            //    ViewBag.Type = "Error";
            //    ViewBag.Message = ex.Message;
            //}

            // return PartialView("_YardConfigPartial", mCardLines);
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            return View();
        }

        public PartialViewResult List(CertificateLister mCertificateLister)
        {
            mCertificateLister.Pager.Take = mCertificateLister.Pager.PageSize;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mCertificateLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("CertificateGenerator/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mCertificateLister = JsonConvert.DeserializeObject<CertificateLister>(jsonString);
                       
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
                ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            }
            return PartialView(mCertificateLister);
        }

        public ActionResult _CertificateAudit(int certificateId)
        {
            List<CertificateAudit> mCertificateAudits = new List<CertificateAudit>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("CertificateGenerator/GetCertificateAudit/CertificateId/{0}", certificateId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mCertificateAudits = JsonConvert.DeserializeObject<List<CertificateAudit>>(jsonString);
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

            return PartialView(mCertificateAudits);

        }

        public ActionResult CertificateDownload(int certificateId)
        {
            List<CertificateAudit> mCertificateAudits = new List<CertificateAudit>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("CertificateGenerator/CertificateId/{0}", certificateId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var stringInBase64s = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonString);
                        if (stringInBase64s != null && stringInBase64s.Count > 0)
                        {
                            using (MemoryStream ms = new MemoryStream())
                            {
                                using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
                                {
                                    foreach (var file in stringInBase64s)
                                    {
                                        byte[] certBytes = System.Convert.FromBase64String(file.Value);

                                        var entry = archive.CreateEntry($"{file.Key}", CompressionLevel.Fastest);
                                        using (var zipStream = entry.Open())
                                        {
                                            zipStream.Write(certBytes, 0, certBytes.Length);
                                        }
                                    }
                                }
                                return File(ms.ToArray(), "application/zip", $"Certificate.zip");
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
                ViewBag.Message = ex.Message;
            }

            return PartialView(mCertificateAudits);

        }
    }
}