using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Service;
using Newtonsoft.Json;
using System;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class ClusterCategoryController : Controller
    {
        // GET: ClusterCategory
        private readonly IAssetTypeService assetTypeService;
        public ClusterCategoryController(IAssetTypeService assetTypeService)
        {
            this.assetTypeService = assetTypeService;
        }

        public ActionResult Index()
        {
            ViewBag.AssetTypes = new SelectList(assetTypeService.GetAll(), "Id", "Name");
            BindClasificationTypes();
            BindStatus();
            BindPointMachineType();
            return View();
        }

        public PartialViewResult List(ClusterCategoryLister mLister)
        {
            mLister.Pager.Take = mLister.Pager.PageSize;
            mLister.SearchCriteria.CreatedBy = ClsHttpContent.LoginUser.Id;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("ClusterCategory/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mLister = JsonConvert.DeserializeObject<ClusterCategoryLister>(jsonString);
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
            return PartialView("_List", mLister);
        }

        [HttpPost]
        public ActionResult Save(ClusterCategory mClusterCategory)
        {
            try
            {
                mClusterCategory.Name = $"{mClusterCategory.Cluster}-{mClusterCategory.SubCluster}";
                mClusterCategory.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mClusterCategory);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("ClusterCategory"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mClusterCategory = JsonConvert.DeserializeObject<ClusterCategory>(jsonString);
                        if (mClusterCategory != null && mClusterCategory.Id > 0)
                        {
                            ViewBag.Message = "Cluster Category has been saved.";
                            ViewBag.Type = "Success";
                        }
                        else
                        {
                            ViewBag.Message = "Error occured while Cluster Category!";
                            ViewBag.Type = "Error";
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.BadRequest)
                    {
                        ViewBag.Message = "This category already assigned.";
                        ViewBag.Type = "Error";
                    }
                    else
                    {
                        ViewBag.Message = "Internal server error.";
                        ViewBag.Type = "Error";
                    }
                }
            }
            catch (Exception)
            {
                ViewBag.Message = "Internal server error.";
                ViewBag.Type = "Error";
            }
            finally
            {
                ViewBag.AssetTypes = new SelectList(assetTypeService.GetAll(), "Id", "Name");
                BindClasificationTypes();
                BindStatus();
                BindPointMachineType();
            }
            return PartialView("_AddClusterCategoryPartial", mClusterCategory);
        }

        public ActionResult GetById(int id)
        {
            ClusterCategory mClusterCategory = new ClusterCategory();
            try
            {
                if (id > 0)
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = hcf.client.GetAsync(String.Format("ClusterCategory/{0}", id)).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            mClusterCategory = JsonConvert.DeserializeObject<ClusterCategory>(jsonString);
                            if (mClusterCategory != null && mClusterCategory.Id > 0)
                            {
                                mClusterCategory.Cluster = Convert.ToInt32(mClusterCategory.Name.Split('-')[0]);
                                mClusterCategory.SubCluster = Convert.ToInt32(mClusterCategory.Name.Split('-')[1]);
                            }
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "Internal server error!";
                        }
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
                ViewBag.AssetTypes = new SelectList(assetTypeService.GetAll(), "Id", "Name");
                BindClasificationTypes();
                BindStatus();
                BindPointMachineType();
            }


            return PartialView("_AddClusterCategoryPartial", mClusterCategory);
        }

        public ActionResult UploadFile()
        {
            var mDivision = new ClusterCategory();

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
                mDivision.ImageBase64 = fileBase64;
                mDivision.ImageExtension = extension;
            }

            var jsonResult = Json(mDivision, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;
        }

        public ActionResult Get(int id)
        {
            ClusterCategory mClusterCategory = new ClusterCategory();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("ClusterCategory/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mClusterCategory = JsonConvert.DeserializeObject<ClusterCategory>(jsonString);
                        if (mClusterCategory != null && mClusterCategory.Id > 0)
                        {
                            mClusterCategory.Cluster = Convert.ToInt32(mClusterCategory.Name.Split('-')[0]);
                            mClusterCategory.SubCluster = Convert.ToInt32(mClusterCategory.Name.Split('-')[1]);
                        }
                    }
                }

            }
            catch (Exception ex)
            {
            }


            return Json(mClusterCategory, JsonRequestBehavior.AllowGet);
        }

        public ActionResult Delete(int id)
        {
            dynamic data = new { type = "", result = "" };
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.DeleteAsync(String.Format("ClusterCategory/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Deleted" };
                    }
                    else
                        data = new { type = "error", result = "Internal server error." };
                }
            }
            catch (Exception ex)
            {
                data = new { type = "error", result = "Internal server error." };
            }
            return Json(data, JsonRequestBehavior.AllowGet);
        }

        public void BindClasificationTypes()
        {
            var enumData = from E7FRSAdvance.Utility.Utility.ClasificationTypes e in Enum.GetValues(typeof(E7FRSAdvance.Utility.Utility.ClasificationTypes))
                           select new
                           {
                               Id = (int)e,
                               Name = e.ToString().Replace("_", " ")
                           };

            ViewBag.ClasificationTypes = new SelectList(enumData, "Id", "Name");
        }

        public void BindStatus()
        {
            var enumData = from E7FRSAdvance.Utility.Utility.ClusterStatus e in Enum.GetValues(typeof(E7FRSAdvance.Utility.Utility.ClusterStatus))
                           select new
                           {
                               Id = (int)e,
                               Name = e.ToString().Replace("_", " ")
                           };

            ViewBag.Status = new SelectList(enumData, "Id", "Name");
        }

        public void BindPointMachineType()
        {
            var enumData = from E7FRSAdvance.Utility.Utility.PointMachineType e in Enum.GetValues(typeof(E7FRSAdvance.Utility.Utility.PointMachineType))
                           select new
                           {
                               Id = (int)e,
                               Name = e.ToString().Replace("_", " ")
                           };

            ViewBag.PointMachineTypes = new SelectList(enumData, "Id", "Name");
        }
    }
}