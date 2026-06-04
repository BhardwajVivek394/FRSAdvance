using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;
using System.Web.UI.WebControls;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class ChatBotController : Controller
    {
        private readonly IDivisionService divisionService;
        private readonly IZoneService zoneService;
        private readonly ISiteService siteService;
        private static List<Domain.ChatBot> mChatBots = new List<ChatBot>();
        private static bool isCheck = true;

        public ChatBotController(IDivisionService divisionService, IZoneService zoneService, ISiteService siteService)
        {
            this.divisionService = divisionService;
            this.zoneService = zoneService;
            this.siteService = siteService;
        }

        // GET: ChatBot
        public ActionResult Index()
        {
            mChatBots = new List<Domain.ChatBot>();
            isCheck = true;
            mChatBots.Add(new Domain.ChatBot() { Message = "Hi, welcome to AI-Bot! Go ahead and ask me a question. 😄", Name = "ChatBot" });
            return View();
        }

        public ActionResult BindMainMessage(string message = null)
        {
            //var sites = new List<Site>();
            if (!string.IsNullOrEmpty(message))
            {
                mChatBots.Add(new Domain.ChatBot() { Message = message, UserId = ClsHttpContent.LoginUser.Id, Name = $"{ClsHttpContent.LoginUser.FirstName} {ClsHttpContent.LoginUser.LastName}", MessageType = "Question" });

            }


            return PartialView("List", mChatBots);
        }

        public ActionResult List(string message = null, string fileType = null)
        {
            if (!string.IsNullOrEmpty(message))
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(new Domain.ChatBot() { Message = message, FileType = fileType });
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("ChatBot"), str).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            var outpustr = JsonConvert.DeserializeObject<string>(jsonString);
                            mChatBots.Add(new Domain.ChatBot() { Message = outpustr, Name = "ChatBot", MessageType = "Answer", Question = message });

                        }
                        else
                        {
                            ViewBag.Error = "Internal server error.";
                        }
                    }
                }
                catch (Exception ex)
                {
                    ViewBag.Error = ex.Message.ToString();
                }
            }


            return PartialView(mChatBots);
        }

        //public ActionResult GetMessageList(string message = null, string fileType = null)
        //{
        //    string answer = string.Empty;

        //    if (!string.IsNullOrEmpty(message))
        //    {
        //        try
        //        {
        //            using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
        //            {
        //                var jsonStr = JsonConvert.SerializeObject(new Domain.ChatBot() { Message = message, FileType = fileType });
        //                StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
        //                var response = hcf.client.PostAsync(String.Format("ChatBot"), str).Result;
        //                string jsonString = response.Content.ReadAsStringAsync().Result;
        //                if (response.StatusCode == HttpStatusCode.OK)
        //                {
        //                    var outpustr = JsonConvert.DeserializeObject<string>(jsonString);
        //                    mChatBots.Add(new Domain.ChatBot() { Message = outpustr, Name = "ChatBot", MessageType = "Answer", Question = message });
        //                    answer = outpustr;
        //                }
        //                else
        //                {
        //                    ViewBag.Error = "Internal server error.";
        //                }
        //            }
        //        }
        //        catch (Exception ex)
        //        {
        //            ViewBag.Error = ex.Message.ToString();
        //        }
        //    }


        //    return Json(answer, JsonRequestBehavior.AllowGet);
        //}

        public ActionResult Upload()
        {
            if (ClsHttpContent.LoginUser.RoleId == (int)E7FRSAdvance.Utility.Utility.Role.User)
            {
                var sites = siteService.GetAll();
                if (sites != null && sites.Count > 0)
                {
                    ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
                    var divisionIdGroup = sites.GroupBy(x => x.DivisionId).Select(x => x.Key).ToList();
                    var zoneIdGroup = sites.GroupBy(x => x.ZoneId).Select(x => x.Key).ToList();

                    var divisions = divisionService.GetAll();
                    if (divisions != null && divisions.Count > 0)
                        ViewBag.Divisions = new SelectList(divisions.Where(x => divisionIdGroup.Contains(x.Id)), "Id", "Name");
                    else
                        ViewBag.Divisions = new SelectList(divisions, "Id", "Name");

                    var zones = zoneService.GetAll();
                    if (zones != null && zones.Count > 0)
                        ViewBag.Zones = new SelectList(zones.Where(x => zoneIdGroup.Contains(x.Id)), "Id", "Name");
                    else
                        ViewBag.Zones = new SelectList(zones, "Id", "Name");

                }
            }
            else
            {
                ViewBag.Zones = new SelectList(zoneService.GetAll(), "Id", "Name");
                ViewBag.Divisions = new SelectList(divisionService.GetAll(), "Id", "Name");
                ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            }
            return View();
        }

        public ActionResult UploadPDF(Domain.ChatBot chatBot)
        {
            dynamic data = new ExpandoObject();
            string responseStatus = string.Empty;
            for (int i = 0; i < Request.Files.Count; i++)
            {
                var file = Request.Files[i];
                string base64 = string.Empty;
                using (BinaryReader b = new BinaryReader(file.InputStream))
                {
                    byte[] binData = b.ReadBytes(file.ContentLength);
                    base64 = Convert.ToBase64String(binData);

                    chatBot.FileName = file.FileName;
                    chatBot.FileBase64 = base64;
                }
                try
                {
                    chatBot.CreatedBy = ClsHttpContent.LoginUser.Id;
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(chatBot);
                        System.IO.File.WriteAllText(@"D:\path.json", jsonStr);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PutAsync(String.Format("ChatBot"), str).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            var newjsonstring = jsonString.Replace("\\n    ", "").Replace("\\n", "").Replace("\\", "");
                            string output = $"[{newjsonstring.Split('[', ']')[1]}]";
                            var mChatBots = JsonConvert.DeserializeObject<List<Domain.ChatBot>>(output);
                            if (mChatBots != null && mChatBots.Count > 0)
                                responseStatus = mChatBots.Select(x => x.Status).FirstOrDefault();

                            data = new { type = "success", result = responseStatus };
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

            }

            return Json(data);
        }

        public ActionResult UploadUrl(Domain.ChatBotUrl mChatBotUrl)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mChatBotUrl.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mChatBotUrl);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("ChatBotUrl"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        data = new { type = "success", result = "Url has been saved." };
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

        public ActionResult _PDFList(ChatBot mChatBot)
        {
            //var sites = new List<Site>();
            var pdfList = new List<string>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mChatBot);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("ChatBot/GetPDFList"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        //var dsdsd = jsonString.Replace("\\n    ", "").Replace("\\n", "").Replace("\\", "");
                        //dsdsd = dsdsd.Substring(1, dsdsd.Length - 2);
                        //string output = $"[{dsdsd.Split('[', ']')[1]}]";
                        pdfList = JsonConvert.DeserializeObject<List<string>>(jsonString);
                        //ViewBag.PDFList = pdfList;

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
                ViewBag.Error = ex.Message.ToString();
            }

            return PartialView(pdfList);
        }

        [HttpPost]
        public ActionResult SaveRemark(ChatBotRemark mChatBotRemark)
        {
            dynamic data = new ExpandoObject();
            try
            {
                if (mChatBotRemark.Question.Contains('"'))
                {
                    mChatBotRemark.Question = mChatBotRemark.Question.Replace("\"", "'");
                }

                if (mChatBots != null && mChatBots.Count > 0)
                {
                    var mChatBot = mChatBots.Where(x => x.Question != null && x.Question.Trim().ToLower() == mChatBotRemark.Question.Trim().ToLower()).FirstOrDefault();
                    if (mChatBot != null && mChatBot.Message.IsNotNullOrEmpty())
                        mChatBotRemark.Answer = mChatBot.Message;
                }
                mChatBotRemark.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mChatBotRemark);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("ChatBotRemark"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mChatBotRemark = JsonConvert.DeserializeObject<ChatBotRemark>(jsonString);
                        if (mChatBotRemark != null && mChatBotRemark.Id > 0)
                        {
                            data = new { type = "success", result = "Remark has been saved." };
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
            catch (Exception)
            {
                data = new { type = "error", result = "Internal server error." };
            }
            return Json(data, JsonRequestBehavior.AllowGet);
        }


        [HttpPost]
        public ActionResult GetMessageList(string message = null, string fileType = null)
        {
            string strings = string.Empty;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(new Domain.E7ChatBot() { message = message, checkpoint_id = ClsHttpContent.LoginUser.EmailAddress, reset_memory = isCheck });
                   
                    if (isCheck)
                        isCheck = false;

                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("ChatBot/GetE7Chat"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var outputstr = JsonConvert.DeserializeObject<string>(jsonString);
                        if (outputstr.IsNotNullOrEmpty())
                        {
                            var mDivisionPdfResponse = JsonConvert.DeserializeObject<DivisionPdfResponse>(outputstr);
                            if (mDivisionPdfResponse != null)
                            {
                                strings = mDivisionPdfResponse.Text;
                                mChatBots.Add(new Domain.ChatBot() { Message = mDivisionPdfResponse.Text, Name = "ChatBot", MessageType = "Answer", Question = message, Files = mDivisionPdfResponse.Files });
                            }

                        }

                    }
                    else
                    {
                        ViewBag.Error = "Internal server error.";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }


            return Json(strings, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetPDFDownload(string fileName = null, string filePath = null, string fileType = null)
        {
            string strings = string.Empty;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(new Domain.ChatBot() { FileName = fileName, FilePath = filePath });
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("ChatBot/GetPDFDownload"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {

                            byte[] filebytes = System.Convert.FromBase64String(stringInBase64);
                            if (filebytes != null && filebytes.Length > 0 && fileType.IsNotNullOrEmpty())
                            {
                                if (fileType.Trim().ToLower() == "csv")
                                    return File(filebytes, "text/csv", $"{fileName}");
                                else if (fileType.Trim().ToLower() == "pdf")
                                    return File(filebytes, "application/pdf", $"{fileName}");
                                else
                                    return File(filebytes, $"application/{fileType}", $"{fileName}");
                            }
                        }

                    }
                    else
                    {
                        ViewBag.Error = "Internal server error.";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }


            return RedirectToAction("Index");
        }

        public async void ReadStream(System.IO.Stream stream)
        {
            using (var reader = new System.IO.StreamReader(stream))
            {
                string line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    // Process each line (each word in this case)
                    Console.WriteLine(line);
                }
            }
        }

    }
}