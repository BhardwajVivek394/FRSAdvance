using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Net.Http;
using System.Net;
using System.Text;
using System;
using System.Web.Mvc;
using System.Dynamic;
using Domain;
using System.Linq;

namespace E7FRSAdvance.Controllers
{
    [Utility.Authorization]
    public class WorksheetStoreController : Controller
    {
        // GET: WorksheetStore
        public ActionResult Index()
        {
            return View();
        }

        public ActionResult List()
        {
            var mDivision = new List<Domain.Division>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync("Project/GetProjectDivision").Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mDivision = JsonConvert.DeserializeObject<List<Domain.Division>>(jsonString);
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
            return PartialView(mDivision);
        }

        [HttpGet]
        public ActionResult AssignMaterial(int divisionId)
        {
            List<Domain.WorksheetMaterial> mWorksheetMaterials = new List<Domain.WorksheetMaterial>();
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
            finally
            {
                ViewBag.WorksheetStoreMaterials = GetStoreMaterialBy(divisionId);
                ViewBag.DivisionId = divisionId;
            }

            return PartialView(mWorksheetMaterials);

        }
        [HttpPost]
        public ActionResult AssignMaterial(List<Domain.WorksheetStoreMaterial> mWorksheetStoreMaterials)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mWorksheetStoreMaterials);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("WorksheetStoreMaterial"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Material has been Assigned." };
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

            return Json(data);
        }

        [HttpGet]
        public ActionResult AssignWorksheetStoreMaterial(int divisionId)
        {
            var mWorksheetStoreMaterial = GetStoreMaterialBy(divisionId);

            return PartialView(mWorksheetStoreMaterial);

        }

        private List<WorksheetStoreMaterial> GetStoreMaterialBy(int divisionId)
        {
            var mWorksheetStoreMaterials = new List<WorksheetStoreMaterial>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"WorksheetStoreMaterial/{divisionId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mWorksheetStoreMaterials = JsonConvert.DeserializeObject<List<WorksheetStoreMaterial>>(jsonString);
                    }

                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mWorksheetStoreMaterials;
        }


        [HttpPost]
        public ActionResult AssignMaterialQuantity(int divisionId)
        {
            var mWorksheetStoreMaterials = GetStoreMaterialBy(divisionId);
            if (mWorksheetStoreMaterials != null && mWorksheetStoreMaterials.Count > 0)
            {
                mWorksheetStoreMaterials = mWorksheetStoreMaterials.Where(x => x.IsActive).ToList();
            }

            return PartialView(mWorksheetStoreMaterials);

        }

        [HttpPost]
        public ActionResult UpdateMaterialStatus(Domain.WorksheetStoreMaterial mWorksheetStoreMaterial)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mWorksheetStoreMaterial);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format($"WorksheetStoreMaterial/{mWorksheetStoreMaterial.Id}"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Material Status has been Assigned." };
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

            return Json(data);
        }

        private List<Domain.WorksheetMaterial> GetAllMaterial()
        {
            List<Domain.WorksheetMaterial> mWorksheetMaterials = new List<Domain.WorksheetMaterial>();
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

            return mWorksheetMaterials;
        }
    }
}