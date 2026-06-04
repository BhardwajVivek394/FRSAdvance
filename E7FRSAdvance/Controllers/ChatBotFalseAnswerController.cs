using Domain;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System.Net.Http;
using System.Net;
using System.Text;
using System;
using System.Web.Mvc;

namespace E7FRSAdvance.Controllers
{
    public class ChatBotFalseAnswerController : Controller
    {
        // GET: ChatBotFalseAnswer
        public ActionResult Index()
        {
            return View();
        }

        public PartialViewResult _List(ChatBotFalseAnswerLister mChatBotFalseAnswerLister)
        {
            mChatBotFalseAnswerLister.Pager.Take = mChatBotFalseAnswerLister.Pager.PageSize;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mChatBotFalseAnswerLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("ChatBotFalseAnswer/GetAll"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mChatBotFalseAnswerLister = JsonConvert.DeserializeObject<ChatBotFalseAnswerLister>(jsonString);
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
            return PartialView(mChatBotFalseAnswerLister);
        }
    }
}