using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Service;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;
using System.Web.UI.WebControls.WebParts;

namespace E7FRSAdvance.Controllers
{
    [Utility.Authorization]
    public class PointClusteringController : Controller
    {
        // GET: PointClustering
        private readonly ISiteService _siteService;
        private readonly IZoneService _zoneService;
        private readonly IDivisionService _divisionService;
        private readonly IAssetTypeService _assetTypeService;
        private readonly IAssetService _assetService;
        private readonly ISectionService _sectionService;
        public PointClusteringController(ISiteService siteService, IZoneService zoneService, IDivisionService divisionService, IAssetTypeService assetTypeService, IAssetService assetService, ISectionService sectionService)
        {
            _siteService = siteService;
            _zoneService = zoneService;
            _divisionService = divisionService;
            _assetTypeService = assetTypeService;
            _assetService = assetService;
            _sectionService = sectionService;
        }

        public ActionResult Index()
        {
            ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
            ViewBag.Zones = new SelectList(_zoneService.GetAll(), "Id", "Name");
            ViewBag.Divisions = new SelectList(_divisionService.GetAll(), "Id", "Name");
            ViewBag.AssetTypes = new SelectList(_assetTypeService.GetAll(), "Id", "Name");
            return View(new Domain.SearchCriteria());
        }

        public ActionResult _List(Domain.SearchCriteria SearchCriteria)
        {
            AssetLister mAssetLister = new AssetLister();
            mAssetLister.Pager.Take = -1;// mAssetLister.Pager.PageSize;
            ViewBag.Type = SearchCriteria.Type;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    mAssetLister.SearchCriteria.CreatedBy = ClsHttpContent.LoginUser.Id;
                    mAssetLister.SearchCriteria.IsMobileView = true;
                    mAssetLister.SearchCriteria.SiteId = SearchCriteria.SiteId;
                    mAssetLister.SearchCriteria.AssetTypeId = (int)E7FRSAdvance.Utility.Utility.AssetType.POINT_MACHINE;
                    mAssetLister.SearchCriteria.StartDate = SearchCriteria.StartDate.ToShortDateString();
                    mAssetLister.SearchCriteria.EndDate = SearchCriteria.EndDate.ToShortDateString();
                    if (string.IsNullOrEmpty(mAssetLister.SearchCriteria.StartDate))
                        mAssetLister.SearchCriteria.StartDate = DateTime.Now.ToShortDateString();

                    if (string.IsNullOrEmpty(mAssetLister.SearchCriteria.EndDate))
                        mAssetLister.SearchCriteria.EndDate = DateTime.Now.ToShortDateString();


                    var jsonStr = JsonConvert.SerializeObject(mAssetLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GetAllSiteDetailsBySiteId"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetLister = JsonConvert.DeserializeObject<AssetLister>(jsonString);
                        if (mAssetLister != null && mAssetLister.mAssets != null && mAssetLister.mAssets.Count > 0)
                        {
                            mAssetLister.mAssets = mAssetLister.mAssets.OrderBy(x => x.Sequence).ToList();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView(mAssetLister);
        }

        public ActionResult _ListByCluster(Domain.SearchCriteria SearchCriteria)
        {
            List<Domain.Asset> mPointMachineDatas = new List<Domain.Asset>();
            ViewBag.Type = SearchCriteria.Type;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {

                    var jsonStr = JsonConvert.SerializeObject(SearchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("PointMachineData/GetByCluster"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mPointMachineDatas = JsonConvert.DeserializeObject<List<Domain.Asset>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView(mPointMachineDatas);
        }

        public ActionResult _AIClustersList(Domain.AIClustersLister mAIClustersLister)
        {
            mAIClustersLister.Pager.Take = mAIClustersLister.Pager.PageSize;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAIClustersLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("AICluster/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAIClustersLister = JsonConvert.DeserializeObject<Domain.AIClustersLister>(jsonString);

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
            return PartialView(mAIClustersLister);
        }

        public ActionResult GetById(int id)
        {
            Domain.AIClusters mClusterCategory = new Domain.AIClusters();
            try
            {
                if (id > 0)
                {


                }

            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = ex.Message;
            }
            finally
            {
                ViewBag.AssetTypes = new SelectList(_assetTypeService.GetAll(), "Id", "Name");
            }


            return PartialView("_AddAIClusters", mClusterCategory);
        }

        public ActionResult _AddAIClusters()
        {
            ViewBag.AssetTypes = new SelectList(_assetTypeService.GetAll(), "Id", "Name");
            return PartialView("_AddAIClusters", new Domain.AIClusters());
        }

        public ActionResult _PointMachineEventData(SearchCriteria searchCriteria)
        {
            var asset = _assetService.Get(searchCriteria.AssetId);
            if (asset != null && asset.Id > 0)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(searchCriteria);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("PointMachineData/GetBy"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            var mPointMachineDatas = JsonConvert.DeserializeObject<List<Domain.PointMachineData>>(jsonString);
                            if (mPointMachineDatas != null && searchCriteria.Clusters.IsNotNullOrEmpty())
                            {
                                ViewBag.Clusters = searchCriteria.Clusters;
                                foreach (var mPointMachineData in mPointMachineDatas)
                                {
                                    if (mPointMachineData.PointMachineJson != null)
                                    {
                                        string clusterA = string.Empty;
                                        string clusterB = string.Empty;

                                        if (mPointMachineData.PointMachineJson != null && mPointMachineData.PointMachineJson.ClusterA.IsNotNullOrEmpty())
                                            clusterA = mPointMachineData.PointMachineJson.ClusterA.Split('-').FirstOrDefault().Trim();

                                        if (mPointMachineData.PointMachineJson != null && mPointMachineData.PointMachineJson.ClusterA.IsNotNullOrEmpty())
                                            clusterB = mPointMachineData.PointMachineJson.ClusterB.Split('-').FirstOrDefault().Trim();

                                        if (searchCriteria.Clusters == clusterA || searchCriteria.Clusters == clusterB)
                                        {
                                            asset.mPointMachineData.Add(mPointMachineData);

                                        }
                                    }

                                }
                            }

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
            }

            return PartialView(asset);
        }

        [HttpPost]
        public ActionResult Save(Domain.AIClusters mAIClusters)
        {
            try
            {
                mAIClusters.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAIClusters);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("AICluster"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAIClusters = JsonConvert.DeserializeObject<AIClusters>(jsonString);
                        if (mAIClusters != null && mAIClusters.Id > 0)
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
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        ViewBag.Message = JsonConvert.DeserializeObject<string>(jsonString);
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
                ViewBag.AssetTypes = new SelectList(_assetTypeService.GetAll(), "Id", "Name");
            }
            return PartialView("_AddAIClusters", mAIClusters);
        }

        public ActionResult GetAIClusters(string cluster)
        {
            AIClusters mAIClusters = new AIClusters();
            using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
            {
                var response = hcf.client.GetAsync(String.Format("AICluster/Cluster/{0}", cluster)).Result;
                string jsonString = response.Content.ReadAsStringAsync().Result;
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    mAIClusters = JsonConvert.DeserializeObject<AIClusters>(jsonString);
                    if (mAIClusters == null)
                        mAIClusters = new AIClusters();
                }
                else
                {
                    ViewBag.Type = "Error";
                    ViewBag.Message = "Internal server error!";
                }
            }
            return Json(mAIClusters, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetAIClustersById(int id)
        {
            AIClusters mAIClusters = new AIClusters();
            using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
            {
                var response = hcf.client.GetAsync(String.Format("AICluster/{0}", id)).Result;
                string jsonString = response.Content.ReadAsStringAsync().Result;
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    mAIClusters = JsonConvert.DeserializeObject<AIClusters>(jsonString);

                }
                else
                {
                    ViewBag.Type = "Error";
                    ViewBag.Message = "Internal server error!";
                }
            }
            return Json(mAIClusters, JsonRequestBehavior.AllowGet);
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

        public ActionResult Delete(int id)
        {
            dynamic data = new { type = "", result = "" };
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.DeleteAsync(String.Format("AICluster/{0}", id)).Result;
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


        public JsonResult GetDivisionByZoneId(int zoneId)
        {
            List<Division> mDivisions = new List<Division>();
            if (zoneId > 0)
            {
                string result;
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = hcf.client.GetAsync(String.Format("Division/GetAllDivisionsByZoneId/{0}", zoneId)).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            mDivisions = JsonConvert.DeserializeObject<List<Division>>(jsonString);
                        }
                        else
                        {
                            result = "Internal server error.";
                        }
                    }

                }
                catch (Exception ex)
                {
                    result = ex.Message.ToString();
                }
            }
            else
            {
                // mDivisions = GetDivisions();
            }

            return Json(mDivisions, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetSiteByDivisionId(int divisionId)
        {
            var mSites = new List<Domain.Site>();
            try
            {
                mSites = _siteService.GetBy(divisionId);
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }
            return Json(mSites, JsonRequestBehavior.AllowGet);
        }
    }
}