using Domain;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System.Net.Http;
using System.Net;
using System.Text;
using System;
using System.Web.Mvc;
using System.Dynamic;

namespace E7FRSAdvance.Controllers
{
    public class ChatBotRemarkController : Controller
    {
        // GET: ChatBotRemark
        public ActionResult Index()
        {
            return View();
        }

        public PartialViewResult _List(ChatBotRemarkLister mChatBotRemarkLister)
        {
            mChatBotRemarkLister.Pager.Take = mChatBotRemarkLister.Pager.PageSize;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mChatBotRemarkLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("ChatBotRemark/GetAll"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mChatBotRemarkLister = JsonConvert.DeserializeObject<ChatBotRemarkLister>(jsonString);
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
            return PartialView(mChatBotRemarkLister);
        }

        public ActionResult Delete(int id)
        {
            bool result = false;
            dynamic data = new ExpandoObject();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.DeleteAsync(String.Format("ChatBotRemark/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        result = JsonConvert.DeserializeObject<bool>(jsonString);
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