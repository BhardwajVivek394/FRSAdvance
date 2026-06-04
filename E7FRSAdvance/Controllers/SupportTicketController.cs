using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Dynamic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class SupportTicketController : Controller
    {
        // GET: SupportTicket
        public ActionResult Index()
        {
            return View();
        }

        public PartialViewResult TicketList(SupportTicketLister mSupportTicketLister)
        {
            mSupportTicketLister.Pager.Take = mSupportTicketLister.Pager.PageSize;
            mSupportTicketLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
            mSupportTicketLister.SearchCriteria.CreatedBy = ClsHttpContent.LoginUser.Id;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSupportTicketLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SupportTicket/GetAllSupportTicketLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSupportTicketLister = JsonConvert.DeserializeObject<SupportTicketLister>(jsonString);
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
            return PartialView("_TicketListPartial", mSupportTicketLister);
        }

        public ActionResult GetById(int id)
        {
            SupportTicket mSupportTicket = new SupportTicket();
            if (id > 0)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = hcf.client.GetAsync(String.Format("SupportTicket/{0}", id)).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            mSupportTicket = JsonConvert.DeserializeObject<SupportTicket>(jsonString);
                            if (mSupportTicket != null && mSupportTicket.Id > 0 && mSupportTicket.SupportTicketImages != null && mSupportTicket.SupportTicketImages.Count > 0)
                            {
                                mSupportTicket.SupportTicketImages.ForEach(x =>
                                {
                                    if (x.ImagePath.IsNotNullOrEmpty())
                                        x.ImagePath = ConfigurationManager.AppSettings.Get("FileBaseUrl") + x.ImagePath;
                                });
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
            }

            return PartialView("_AddTicketPartial", mSupportTicket);
        }

        [HttpPost]
        public ActionResult SaveTicket(SupportTicket mTicket)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mTicket.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mTicket);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SupportTicket"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mTicket = JsonConvert.DeserializeObject<SupportTicket>(jsonString);
                        if (mTicket != null && mTicket.Id > 0)
                        {
                            data = new { type = "success", result = "Support Ticket has been saved." };
                        }
                        else
                        {
                            data = new { type = "error", result = "Error occured while saving Ticket!" };
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

        public ActionResult Delete(int id)
        {
            bool result = false;
            dynamic data = new ExpandoObject();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.DeleteAsync(String.Format("SupportTicket/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        result = JsonConvert.DeserializeObject<bool>(jsonString);
                        if (result)
                        {
                            data = new { type = "success", result = "Support Ticket has been saved." };
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

        public ActionResult UploadFile()
        {
            var data = new List<SupportTicketImage>();

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
                data.Add(new SupportTicketImage() { FileBase64 = fileBase64, FileExtension = extension });
            }

            return Json(data, JsonRequestBehavior.AllowGet);
        }

    }
}