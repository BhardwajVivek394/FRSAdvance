using Domain;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Dynamic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;

namespace E7FRSAdvance.Controllers
{
    public class ChatBotUrlController : Controller
    {
        // GET: ChatBotUrl
        public ActionResult Index()
        {
            return View();
        }

        public PartialViewResult List(ChatBotUrlLister mLister)
        {
            mLister.Pager.Take = mLister.Pager.PageSize;
            mLister.SearchCriteria.CreatedBy = ClsHttpContent.LoginUser.Id;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("ChatBotUrl/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mLister = JsonConvert.DeserializeObject<ChatBotUrlLister>(jsonString);
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
            return PartialView(mLister);
        }


        public ActionResult Update(Domain.ChatBotUrl mChatBotUrl)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mChatBotUrl.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mChatBotUrl);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format("ChatBotUrl"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Url has been approved." };
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

        public ActionResult Delete(int id)
        {
            dynamic data = new ExpandoObject();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Asset/GetAssetById/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Url has been approved." };
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
    }
}