using Domain;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System.Dynamic;
using System.Net.Http;
using System.Net;
using System.Text;
using System;
using System.Web.Mvc;
using System.Collections.Generic;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class AIRemarkController : Controller
    {
        // GET: AIRemark
        public ActionResult Index()
        {
            ViewBag.AssetTypes = new SelectList(GetAssetType(), "Id", "Name");
            return View();
        }

        public PartialViewResult AIRemarkList(AIRemarkLister mAIRemarkLister)
        {
            mAIRemarkLister.Pager.Take = mAIRemarkLister.Pager.PageSize;
            mAIRemarkLister.SearchCriteria.CreatedBy = ClsHttpContent.LoginUser.Id;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAIRemarkLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("AIRemark/GetAllAIRemarkLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAIRemarkLister = JsonConvert.DeserializeObject<AIRemarkLister>(jsonString);
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
            return PartialView("_AIRemarkListPartial", mAIRemarkLister);
        }

        public ActionResult GetById(int id)
        {
            AIRemark mAIRemark = new AIRemark();
            if (id > 0)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = hcf.client.GetAsync(String.Format("AIRemark/{0}", id)).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            mAIRemark = JsonConvert.DeserializeObject<AIRemark>(jsonString);

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
            ViewBag.AssetTypes = new SelectList(GetAssetType(), "Id", "Name");
            return PartialView("_AddAIRemarkPartial", mAIRemark);
        }

        [HttpPost]
        public ActionResult SaveAIRemark(AIRemark mAIRemark)
        {
            try
            {
                mAIRemark.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAIRemark);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("AIRemark"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAIRemark = JsonConvert.DeserializeObject<AIRemark>(jsonString);
                        if (mAIRemark != null && mAIRemark.Id > 0)
                        {
                            ViewBag.Message = "Remark has been saved.";
                            ViewBag.Type = "Success";
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "Error occured while saving Remark!";
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
            ViewBag.AssetTypes = new SelectList(GetAssetType(), "Id", "Name");
            return PartialView("_AddAIRemarkPartial", mAIRemark);
        }

        public ActionResult Delete(int id)
        {
            bool result = false;
            dynamic data = new ExpandoObject();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.DeleteAsync(String.Format("AIRemark/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        result = JsonConvert.DeserializeObject<bool>(jsonString);
                        if (result)
                        {
                            data = new { type = "success", result = "Remark has been Deleted." };
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

        public List<Domain.AssetType> GetAssetType()
        {
            List<Domain.AssetType> mAssetTypes = new List<Domain.AssetType>();
            try
            {
                using (var mHttpClientFactory = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = mHttpClientFactory.client.GetAsync(String.Format("AssetType/GetAllAssestType")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssetTypes = JsonConvert.DeserializeObject<List<Domain.AssetType>>(jsonString);
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

            return mAssetTypes;
        }
    }
}