using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Service;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web;
using System.Web.Mvc;

namespace E7FRSAdvance.Controllers
{
    [Utility.Authorization]
    public class ProjectController : Controller
    {
        // GET: Project
        private readonly IDivisionService _divisionService;
        private readonly ISiteService _siteService;
        private readonly IAssetTypeService _assetTypeService;
        private readonly IAssetAttributeService _assetAttributeService;

        public ProjectController(IDivisionService divisionService, ISiteService siteService, IAssetTypeService assetTypeService, IAssetAttributeService assetAttributeService)
        {
            _divisionService = divisionService;
            _siteService = siteService;
            _assetTypeService = assetTypeService;
            _assetAttributeService = assetAttributeService;
        }

        public ActionResult Index()
        {
            ViewBag.Divisions = _divisionService.GetAllDivisions();
            return View();
        }

        public ActionResult List()
        {
            var mProjects = new List<Domain.Project>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync("Project/GetAll").Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mProjects = JsonConvert.DeserializeObject<List<Domain.Project>>(jsonString);
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
            return PartialView(mProjects);
        }

        public ActionResult Add()
        {
            Domain.Project mProjects = new Domain.Project();
            ViewBag.Divisions = _divisionService.GetAllDivisions();
            return PartialView(mProjects);

        }

        [HttpPost]
        public ActionResult Create(Domain.Project mProject)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mProject.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mProject);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Project"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mProject = JsonConvert.DeserializeObject<Domain.Project>(jsonString);
                        if (mProject != null && mProject.Id > 0)
                            data = new { type = "success", result = "Project has been created." };
                        else
                            data = new { type = "error", result = "Internal server error." };
                    }
                    else if (response.StatusCode == HttpStatusCode.BadRequest)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        data = new { type = "error", result = JsonConvert.DeserializeObject<string>(jsonString) };
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

        public ActionResult GetProjectById(int id)
        {
            Domain.Project mProject = new Domain.Project();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Project/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mProject = JsonConvert.DeserializeObject<Domain.Project>(jsonString);
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
                ViewBag.Divisions = _divisionService.GetAllDivisions();
                ViewBag.Sites = _siteService.GetAll();
            }
            return PartialView("Add", mProject);
        }

        public ActionResult GetSiteByDivisionId(int divisionId)
        {
            dynamic data = new ExpandoObject();
            try
            {
                data = _siteService.GetBy(divisionId);
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Something went wrong!";
            }
            return Json(data);
        }

        public ActionResult AssetAttributeMapping(int id)
        {
            Domain.Project mProject = new Domain.Project();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Project/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mProject = JsonConvert.DeserializeObject<Domain.Project>(jsonString);
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
                ViewBag.AssetTypes = _assetTypeService.GetAll();
            }
            return PartialView(mProject);
        }

        public ActionResult GetAssetAttributesBy(int assetTypeId)
        {
            var allAssetAttributes = _assetAttributeService.GetAssetAttributesBy(assetTypeId);
            return Json(allAssetAttributes, JsonRequestBehavior.AllowGet);
        }

        public ActionResult Delete(int id)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.DeleteAsync(String.Format($"Project/{id}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        data = new { type = "success", result = "Project has been deleted." };
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

        public void CreateAttribute(Domain.ProjectAttribute mProjectAttribute)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mProjectAttribute);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Project/CreateAttribute"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;

                }
            }
            catch (Exception ex)
            {
            }

        }

        public void DeleteAttribute(Domain.ProjectAttribute mProjectAttribute)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mProjectAttribute);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format("Project/DeleteAttribute"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;

                }
            }
            catch (Exception ex)
            {
            }

        }
    }
}