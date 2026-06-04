using Domain;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System.Net.Http;
using System.Net;
using System.Text;
using System;
using System.Web.Mvc;
using E7FRSAdvance.Service;
using System.Collections.Generic;
using System.Dynamic;

namespace E7FRSAdvance.Controllers
{
    [Utility.Authorization]
    public class WorksheetMaterialController : Controller
    {
        // GET: WorksheetMaterial
        public ActionResult Index()
        {
            return View();
        }

        public ActionResult List()
        {
            var mWorksheetMaterials = new List<Domain.WorksheetMaterial>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync("WorksheetMaterial/GetAll").Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mWorksheetMaterials = JsonConvert.DeserializeObject<List<Domain.WorksheetMaterial>>(jsonString);
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
            return PartialView(mWorksheetMaterials);
        }

        public ActionResult Add(int typeId)
        {
            Domain.WorksheetMaterial mWorksheetMaterial = new Domain.WorksheetMaterial();
            mWorksheetMaterial.TypeId = typeId;
            return PartialView(mWorksheetMaterial);

        }

        public ActionResult Get(int id)
        {
            Domain.WorksheetMaterial mWorksheetMaterial = new Domain.WorksheetMaterial();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("WorksheetMaterial/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mWorksheetMaterial = JsonConvert.DeserializeObject<Domain.WorksheetMaterial>(jsonString);
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

            return PartialView("Add", mWorksheetMaterial);

        }

        [HttpPost]
        public ActionResult Create(WorksheetMaterial mWorksheetMaterial)
        {
            try
            {
                mWorksheetMaterial.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mWorksheetMaterial);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("WorksheetMaterial"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mWorksheetMaterial = JsonConvert.DeserializeObject<Domain.WorksheetMaterial>(jsonString);
                        if (mWorksheetMaterial != null && mWorksheetMaterial.Id > 0)
                        {
                            ViewBag.Message = "Material has been saved.";
                            ViewBag.Type = "Success";
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "Error occured while saving material!";
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.BadRequest)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        ViewBag.Type = "Error";
                        ViewBag.Message = JsonConvert.DeserializeObject<string>(jsonString);
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

            return PartialView("Add", mWorksheetMaterial);
        }


        public ActionResult Delete(int id)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.DeleteAsync(String.Format($"WorksheetMaterial/{id}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        data = new { type = "success", result = "Material has been deleted." };
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