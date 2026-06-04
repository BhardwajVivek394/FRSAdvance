using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Utility;
using Microsoft.VisualBasic.FileIO;
using Newtonsoft.Json;
using NReco.PdfGenerator;
using System;
using System.Collections.Generic;
using System.Data;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Mvc;
using WebGrease.Activities;
using static E7FRSAdvance.Utility.Utility;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class MaintenaceController : Controller
    {
        // GET: Maintenace
        private readonly IAssetAttributeService assetAttributeService;
        private readonly IZoneService zoneService;
        private readonly IDivisionService divisionService;
        private readonly ISMSLogService smsLogService;
        private readonly ISiteService siteService;
        private readonly IUserService userService;
        private readonly ISiteKeepingService keepingService;
        public MaintenaceController(IAssetAttributeService assetAttributeService, IZoneService zoneService, IDivisionService divisionService, ISMSLogService smsLogService, ISiteService siteService, IUserService userService, ISiteKeepingService keepingService)
        {
            this.assetAttributeService = assetAttributeService;
            this.zoneService = zoneService;
            this.divisionService = divisionService;
            this.smsLogService = smsLogService;
            this.siteService = siteService;
            this.userService = userService;
            this.keepingService = keepingService;
        }
        public ActionResult Index()
        {
            return View();
        }

        public PartialViewResult SiteList(SiteLister mSiteLister)
        {
            mSiteLister.Pager.Take = -1;
            //mSiteLister.Pager.Take = mSiteLister.Pager.PageSize;
            mSiteLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
            mSiteLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSiteLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Site/GetAllSiteLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSiteLister = JsonConvert.DeserializeObject<SiteLister>(jsonString);
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
            finally
            {
                ViewBag.Zones = new SelectList(zoneService.GetAll(), "Id", "Name");
                ViewBag.Divisions = new SelectList(divisionService.GetAll(), "Id", "Name");
            }
            return PartialView("_SiteListPartial", mSiteLister);
        }

        public ActionResult DeviceTime(int siteId = 0)
        {
            AssetLister mAssetLister = new AssetLister();
            if (siteId > 0)
                mAssetLister.SearchCriteria.SiteId = siteId;

            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            return View(mAssetLister);
        }

        public ActionResult _DeviceTimeDetails(AssetLister mAssetLister)
        {
            mAssetLister.Pager.Take = -1;// mAssetLister.Pager.PageSize;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    mAssetLister.SearchCriteria.AssetTypeId = 7;
                    mAssetLister.SearchCriteria.CreatedBy = ClsHttpContent.LoginUser.Id;
                    mAssetLister.SearchCriteria.IsMobileView = true;
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
                        mAssetLister.Pager.PageSize = -1;
                        mAssetLister.Site = SiteAssetData(mAssetLister.SearchCriteria.SiteId);
                        //TempData.Remove("AssetLister");
                        //TempData["AssetLister"] = mAssetLister;
                        //TempData.Keep();

                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            finally
            {
                ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            }
            return PartialView(mAssetLister);
        }

        public void GetSiteKeepingUser()
        {
            var mUsers = new List<User>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"User/GetSiteKeepingUser")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mUsers = JsonConvert.DeserializeObject<List<User>>(jsonString);
                        // mUsers.Insert(0, new Site { Id = 0, Name = "Select site" });
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
            ViewBag.Users = mUsers;
        }

        public JsonResult MaintencePdf(List<Maintenace> pdfDatas)
        {

            TempData.Remove("pdfDatas");
            TempData["pdfDatas"] = pdfDatas;
            TempData.Keep();

            return Json(true, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GeneratePdf()
        {

            string path = HttpContext.Server.MapPath("~/Template/Maintenance.html");
            FileReader fileReader = new FileReader(path);
            string pdfTemplate = fileReader.Read();

            List<Maintenace> pdfDatas = TempData["pdfDatas"] as List<Maintenace>;


            string temp = "";

            foreach (var item in pdfDatas)
            {
                temp += $"<tr>";
                temp += $"<td style='padding:3px;'>{item.SiteName}</td>";
                temp += $"<td style='padding:3px;'>{item.HttpDown}";
                if (!string.IsNullOrEmpty(item.HttpDownRfs) && item.HttpDownRfs != "0")
                {
                    temp += $"<p>{item.HttpDownRfs}</p>";
                }
                temp += $"</td>";
                temp += $"<td style='padding:3px;'>{item.ChannelDown}";
                if (!string.IsNullOrEmpty(item.Channels) && item.Channels != "0")
                {
                    temp += $"<p>{item.Channels}</p>";
                }
                temp += $"</td>";
                temp += $"<td style='padding:3px;'>{item.RfDown}";
                if (!string.IsNullOrEmpty(item.Rfs) && item.Rfs != "0")
                {
                    temp += $"<p>{item.Rfs}</p>";
                }
                temp += $"</td>";
                temp += "</tr>";
            }
            pdfTemplate = pdfTemplate.Replace("{%tbodyData%}", temp);
            try
            {
                Byte[] res = null;
                using (MemoryStream ms = new MemoryStream())
                {
                    var pdf = TheArtOfDev.HtmlRenderer.PdfSharp.PdfGenerator.GeneratePdf(pdfTemplate, PdfSharp.PageSize.A4, 5);
                    pdf.Save(ms);
                    res = ms.ToArray();
                }

                var responses = System.Web.HttpContext.Current.Response;
                responses.BufferOutput = true;
                responses.Clear();
                responses.ClearHeaders();
                responses.AddHeader("content-disposition", $"attachment;filename=Maintenance_{DateTime.Now.Ticks}.pdf");
                responses.ContentType = System.Net.Mime.MediaTypeNames.Application.Pdf;
                responses.ContentEncoding = System.Text.Encoding.UTF8;
                responses.BinaryWrite(res);
                responses.End();
            }
            catch (Exception ex)
            {
                return RedirectToAction("Index");
            }
            return View("Index");
        }

        public JsonResult AllSiteCheckChannelDown()
        {
            List<Site> mSites = new List<Site>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(string.Format("Site/SiteCheckChannelDown")).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSites = JsonConvert.DeserializeObject<List<Site>>(jsonString);
                    }

                }
            }
            catch (Exception)
            {

            }
            return Json(mSites, JsonRequestBehavior.AllowGet);
        }

        public JsonResult SiteCheckHttpPostChannelDown()
        {
            List<Site> mSites = new List<Site>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(string.Format("Site/SiteCheckHttpPostChannelDown")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mSites = JsonConvert.DeserializeObject<List<Site>>(jsonString);
                    }
                    else
                    {
                        var erro = JsonConvert.DeserializeObject<APIResponse>(jsonString);
                    }
                }
            }
            catch (Exception)
            {

            }
            return Json(mSites, JsonRequestBehavior.AllowGet);
        }

        public Site SiteAssetData(int siteId = 0)
        {
            Site mSite = new Site();
            if (siteId > 0)
            {
                try
                {
                    using (var mHttpClientFactory = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = mHttpClientFactory.client.GetAsync(String.Format("Site/GetSiteById/{0}", siteId)).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            mSite = JsonConvert.DeserializeObject<Site>(jsonString);
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

            return mSite;
        }

        //public ActionResult ImportDataLog()
        //{
        //    GetAllSites();
        //    return View();
        //}

        public ActionResult UploadDataLog()
        {
            dynamic data = new ExpandoObject();
            string filePath = string.Empty;
            List<DataLog> mDataLogs = new List<DataLog>();
            try
            {
                HttpPostedFile file = System.Web.HttpContext.Current.Request.Files[0];
                if (file != null && file.ContentLength > 0)
                {
                    filePath = SaveFile(file);
                }

                if (!string.IsNullOrEmpty(filePath))
                {
                    Utility.CSVFileReader csvFileReader = new Utility.CSVFileReader();
                    var dataTable = csvFileReader.ReadFile(filePath);
                    if (dataTable != null && dataTable.Rows != null && dataTable.Rows.Count > 0)
                    {
                        dataTable = csvFileReader.RemoveEscapeSequences(dataTable);
                        ValidateColumnName(dataTable);
                        mDataLogs = dataTable.ConvertToList<DataLog>();

                    }

                    data = new { type = "success", result = mDataLogs };
                }

            }
            catch (InvalidOperationException ex)
            {
                data = new { type = "error", result = ex.Message };
            }
            catch (Exception ex)
            {
                data = new { type = "error", result = "Something went wrong with inputted file." };
            }
            return Json(data, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public ActionResult SaveDataLog(DataLog mDataLog)
        {
            APIResponse mAPIResponse = new APIResponse();
            dynamic data = new { type = "", result = "" };
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mDataLog);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("AssetInfo/UpdateDataLog"), str).Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "success" };
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

        public string SaveFile(HttpPostedFile file)
        {
            var length = file.ContentLength;
            string filePath = System.Web.HttpContext.Current.Server.MapPath("~/Upload/Temp/") + Path.GetFileName(file.FileName);
            if (System.IO.File.Exists(filePath))
                System.IO.File.Delete(filePath);
            file.SaveAs(filePath);
            return filePath;
        }

        public ActionResult DefaultValue()
        {
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            return View();
        }

        public ActionResult GetAssetTypeBySiteId(int siteId)
        {
            List<Domain.AssetType> mAssetTypes = new List<Domain.AssetType>();
            if (siteId > 0)
            {
                try
                {
                    using (var mHttpClientFactory = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = mHttpClientFactory.client.GetAsync(String.Format("AssetType/GetAllAssestType/{0}", siteId)).Result;
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
            }

            return Json(mAssetTypes, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetAssetById(Domain.SearchCriteria searchCriteria)
        {
            Asset mAsset = new Asset();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Asset/SiteId/{0}/AssetTypeId/{1}", searchCriteria.SiteId, searchCriteria.AssetTypeId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAsset = JsonConvert.DeserializeObject<Asset>(jsonString);
                        if (searchCriteria.AssetTypeIds != null && searchCriteria.AssetTypeIds.Count > 0 && mAsset != null && mAsset.assetAttributes != null && mAsset.assetAttributes.Count > 0)
                        {
                            if (searchCriteria.AssetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.SIGNAL)
                            {
                                mAsset.assetAttributes = mAsset.assetAttributes.Where(x => x.Type != null && searchCriteria.AssetTypeIds.Contains(x.Type.Value)).ToList();
                            }

                        }
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
                var mAssetAttributes = assetAttributeService.GetAllAssetAttributeBy(searchCriteria.AssetTypeId);

                if (searchCriteria.AssetTypeIds != null && searchCriteria.AssetTypeIds.Count > 0 && mAssetAttributes != null && mAssetAttributes.Count > 0)
                {
                    if (searchCriteria.AssetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.SIGNAL)
                    {
                        mAssetAttributes = mAssetAttributes.Where(x => x.Type != null && searchCriteria.AssetTypeIds.Contains(x.Type.Value)).ToList();
                    }
                }

                ViewBag.AssetAttributes = mAssetAttributes;
            }
            return PartialView("_AddAssetPartial", mAsset);
        }

        public ActionResult SaveAssetType(Asset mAsset)
        {
            APIResponse mAPIResponse = new APIResponse();
            try
            {
                mAsset.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAsset);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/SaveAssetType"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAPIResponse = JsonConvert.DeserializeObject<APIResponse>(jsonString);
                        if (mAPIResponse.Value != null && mAPIResponse.IsSuccess)
                        {
                            mAPIResponse.Message = "Asset Type has been saved.";
                            mAPIResponse.IsSuccess = true;
                        }
                        else
                        {
                            mAPIResponse.IsSuccess = false;
                            mAPIResponse.Message = "Error occured while saving Asset Type!";
                        }
                    }
                    else
                    {
                        mAPIResponse.IsSuccess = false;
                        mAPIResponse.Message = "Internal server error.";
                    }
                }
            }
            catch (Exception)
            {
                mAPIResponse.IsSuccess = false;
                mAPIResponse.Message = "Internal server error.";
            }
            return Json(mAPIResponse, JsonRequestBehavior.AllowGet);
        }

        public ActionResult ClusterConfig()
        {
            ViewBag.Sites = siteService.GetAll();
            return View();
        }

        public ActionResult GetCluster(int siteId)
        {
            var mClustersStr = new List<string>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Cluster/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var mClusters = JsonConvert.DeserializeObject<List<Cluster>>(jsonString);
                        if (mClusters != null && mClusters.Count > 0)
                        {
                            foreach (var cluster in mClusters)
                            {
                                var clusterNameSplit = cluster.Name.Split('/');
                                if (clusterNameSplit.Length > 1)
                                {
                                    if (!mClustersStr.Contains(clusterNameSplit[0].Trim()))
                                    {
                                        mClustersStr.Add(clusterNameSplit[0].Trim());
                                    }
                                }
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
            return Json(mClustersStr, JsonRequestBehavior.AllowGet);
        }

        public ActionResult CreateClusterConfig(ClusterConfig mClusterConfig)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mClusterConfig);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("ClusterConfig"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mClusterConfig = JsonConvert.DeserializeObject<ClusterConfig>(jsonString);
                    }
                }
            }
            catch (Exception)
            {
            }
            return Json(mClusterConfig, JsonRequestBehavior.AllowGet);
        }

        private void ValidateColumnName(DataTable dataTable)
        {
            if (dataTable != null && dataTable.Columns != null && dataTable.Columns.Count > 0)
            {
                var mColumns = ExtensionMethod.GetEnumDisplayNames(new E7FRSAdvance.Utility.Utility.DataLogEnum());
                foreach (DataColumn column in dataTable.Columns)
                {
                    if (mColumns.Where(x => x.Equals(column.ColumnName)).Count() == 0)
                        throw new InvalidOperationException($"Invalid Column Name '{column.ColumnName}'. Please correct them and upload file again.");
                }

                if (mColumns != null && mColumns.Count != dataTable.Columns.Count)
                    throw new InvalidOperationException($"Invalid import file format. Please correct them and upload file again");
            }
        }

        public ActionResult GetClusterConfig(int siteId)
        {
            var mClusterConfig = new List<ClusterConfig>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"ClusterConfig/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mClusterConfig = JsonConvert.DeserializeObject<List<ClusterConfig>>(jsonString);
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

            return Json(mClusterConfig);
        }

        public ActionResult IndexConfig()
        {
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            return View();
        }

        public ActionResult SaveIndex(int siteId, int indexScore)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Maintenance/SaveIndex/SiteId/{0}/IndexScore/{1}", siteId, indexScore)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Updated." };
                        //mAsset = JsonConvert.DeserializeObject<Asset>(jsonString);
                    }
                    else
                    {
                        data = new { type = "Error", result = "Something went wrong!" };
                    }
                }
            }
            catch (Exception ex)
            {
                data = new { type = "Error", result = "Something went wrong!" };
            }
            return Json(data, JsonRequestBehavior.AllowGet);
        }

        public ActionResult Modbus()
        {
            return View();
        }

        private Site GetSiteById(int id)
        {
            Site mSite = new Site();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Site/GetSiteById/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mSite = JsonConvert.DeserializeObject<Site>(jsonString);
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
            return mSite;
        }

        private List<Asset> GetAssestBy(int siteId, int assetTypeId)
        {
            List<Asset> mAssets = new List<Asset>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Asset/GetAllAssest/{0}/{1}", siteId, assetTypeId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssets = JsonConvert.DeserializeObject<List<Asset>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return mAssets;
        }

        public List<AssetAttribute> GetAllAssetAttributes()
        {
            List<AssetAttribute> mAssetAttributes = new List<AssetAttribute>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync($"AssetAttribute/GetAll").Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetAttributes = JsonConvert.DeserializeObject<List<AssetAttribute>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {

            }
            return mAssetAttributes;
        }

        public JsonResult GetAlarmStatus(int assetId)
        {
            bool result = false;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {

                    var response = hcf.client.GetAsync(String.Format("SMSLog/GetSMSLogByAssetId/{0}", assetId)).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        result = JsonConvert.DeserializeObject<bool>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetSiteByDivisionId(int divisionId)
        {
            var mSites = new List<Domain.Site>();
            try
            {
                mSites = siteService.GetBy(divisionId);
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }
            return Json(mSites, JsonRequestBehavior.AllowGet);
        }

        #region PDF
        public ActionResult PDF()
        {
            BindPDFDropDown();
            return View();
        }

        public ActionResult DownloadPDF(string date, string type)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Maintenance/DownloadMaintancePDF/Date/{0}/Type/{1}", date, type)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                if (type == "SiteSummary")
                                    return File(csvbytes, "text/csv", $"{type}.csv");
                                else
                                    return File(csvbytes, "application/pdf", $"{type}.pdf");
                            }
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "File not found!";
                        }

                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }

                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }
            finally
            {
                BindPDFDropDown();
            }
            return View("PDF");
        }

        public ActionResult DownloadDivisionReportPDF(int divisionId, string date)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Maintenance/DownloadDivisionReportPDF/DivisionId/{divisionId}/Date/{date.Replace("/", "-")}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                var mDivision = divisionService.Get(divisionId);
                                if (mDivision != null && mDivision.Id > 0)
                                    return File(csvbytes, "application/pdf", $"{mDivision.Name}-MaintenaceReport.pdf");
                            }
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "File not found!";
                        }

                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }

                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }
            finally
            {
                BindPDFDropDown();
            }
            return View("PDF");
        }

        public ActionResult DownloadNewDivisionReportPDF(int divisionId, string date, bool isAlertInsight = false)
        {
            var mSearchCriteria = new Domain.SearchCriteria();
            mSearchCriteria.DivisionId = divisionId;
            mSearchCriteria.SearchDate = date.Replace("/", "-");
            mSearchCriteria.IsAlertInsight = isAlertInsight;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    //var response = hcf.client.PostAsync(String.Format($"Maintenance/DownloadNewDivisionReportPDF/DivisionId/{divisionId}/Date/{date.Replace("/", "-")}")).Result;
                    //string jsonString = response.Content.ReadAsStringAsync().Result;

                    var jsonStr = JsonConvert.SerializeObject(mSearchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.GetAsync(String.Format($"Maintenance/DownloadNewDivisionReportPDFByPath/DivisionId/{divisionId}/Date/{date.Replace("/", "-")}")).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                var mDivision = divisionService.Get(divisionId);
                                if (mDivision != null && mDivision.Id > 0)
                                    return File(csvbytes, "application/pdf", $"{mDivision.Name}-MaintenaceReport.pdf");
                            }
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "File not found!";
                        }

                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }

                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }
            finally
            {
                BindPDFDropDown();
            }
            return View("PDF");
        }

        public ActionResult DownloadProbabilityStatusPDF(string year, string month)
        {
            string stringInBase64 = string.Empty;
            Domain.SearchCriteria mSearchCriteria = new Domain.SearchCriteria();
            if (month.IsNotNullOrEmpty() && year.IsNotNullOrEmpty())
                mSearchCriteria.TimeStamp = DateTime.ParseExact($"{month}/{year}", "MMMM/yyyy", CultureInfo.CurrentCulture);
            else
                mSearchCriteria.TimeStamp = DateTime.Now;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSearchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("AISection/DownloadProbabilityStatusPDF"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);

                        var responses = System.Web.HttpContext.Current.Response;
                        responses.BufferOutput = true;
                        responses.Clear();
                        responses.ClearHeaders();
                        responses.AddHeader("content-disposition", $"attachment;filename=ProbabilityStatus.pdf");
                        responses.ContentType = System.Net.Mime.MediaTypeNames.Application.Pdf;
                        responses.ContentEncoding = System.Text.Encoding.UTF8;
                        responses.BinaryWrite(System.Convert.FromBase64String(stringInBase64));
                        responses.End();
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }

                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }
            finally
            {
                BindPDFDropDown();
            }
            return View("PDF");
        }

        public ActionResult DownloadUserReportPDF(int divisionId, string date)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Maintenance/DownloadUserReportPDF/DivisionId/{divisionId}/Date/{date.Replace("/", "-")}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                var mDivision = divisionService.Get(divisionId);
                                if (mDivision != null && mDivision.Id > 0)
                                    return File(csvbytes, "application/pdf", $"{mDivision.Name}-UserReport.pdf");
                            }
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "File not found!";
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }
            finally
            {
                BindPDFDropDown();
            }
            return View("PDF");
        }

        public ActionResult DownloadPerformanceReportPDF(int siteId, string date)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Maintenance/DownloadPerformanceReportPDF/SiteId/{siteId}/Date/{date.Replace("/", "-")}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                var mSite = siteService.Get(siteId);
                                if (mSite != null && mSite.Id > 0)
                                    return File(csvbytes, "application/pdf", $"{mSite.Name}-PerformanceReport.pdf");
                            }
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "File not found!";
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }
            finally
            {
                BindPDFDropDown();
            }
            return View("PDF");
        }

        public ActionResult DownloadDivisionPerformanceReportPDF(int divisionId, string date)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Maintenance/DownloadDivisionPerformanceReportPDF/DivisionId/{divisionId}/Date/{date.Replace("/", "-")}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                var mDivision = divisionService.Get(divisionId);
                                if (mDivision != null && mDivision.Id > 0)
                                    return File(csvbytes, "application/pdf", $"{mDivision.Name}-Division-PerformanceReport.pdf");

                            }

                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "File not found!";
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }
            finally
            {
                BindPDFDropDown();
            }
            return View("PDF");
        }

        public ActionResult DownloadPointMachineGraphsPDF(int divisionId, string date)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Maintenance/DownloadPointMachineGraphsPDF/DivisionId/{divisionId}/Date/{date.Replace("/", "-")}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                var mDivision = divisionService.Get(divisionId);
                                if (mDivision != null && mDivision.Id > 0)
                                    return File(csvbytes, "application/pdf", $"{mDivision.Name}-PointMachineGraph.pdf");
                            }
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "File not found!";
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }
            finally
            {
                BindPDFDropDown();
            }
            return View("PDF");
        }

        public ActionResult RegenerateDivisionReportPDF(int divisionId)
        {
            var mDivision = new Division();
            mDivision.PDFGenerationType = (int)E7FRSAdvance.Utility.Utility.PDFGenerationType.PDF;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mDivision);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format($"Division/Id/{divisionId}"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        ViewBag.Message = "Division pdf generation is under processing please wait for some minutes to re-download.";
                        ViewBag.Type = "Success";

                        ViewBag.Years = new SelectList(GetYears());
                        ViewBag.Months = new SelectList(DateTimeFormatInfo.CurrentInfo.MonthNames.ToList());
                        ViewBag.Zones = new SelectList(zoneService.GetAll(), "Id", "Name");
                        ViewBag.Divisions = new SelectList(divisionService.GetAll(), "Id", "Name");
                        ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");

                        return View("PDF");
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }
            finally
            {
                BindPDFDropDown();
            }
            return View("PDF");
        }

        public ActionResult RegenerateDivisionReportPDFWithoutPM(int divisionId)
        {
            var mDivision = new Division();
            mDivision.PDFGenerationType = (int)E7FRSAdvance.Utility.Utility.PDFGenerationType.Exception;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mDivision);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format($"Division/Id/{divisionId}"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        ViewBag.Message = "Division pdf without point machine generation is under processing please wait for some minutes to re-download.";
                        ViewBag.Type = "Success";

                        ViewBag.Years = new SelectList(GetYears());
                        ViewBag.Months = new SelectList(DateTimeFormatInfo.CurrentInfo.MonthNames.ToList());
                        ViewBag.Zones = new SelectList(zoneService.GetAll(), "Id", "Name");
                        ViewBag.Divisions = new SelectList(divisionService.GetAll(), "Id", "Name");
                        ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");

                        return View("PDF");
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }
            finally
            {
                BindPDFDropDown();
            }
            return View("PDF");
        }

        public ActionResult GetDivisionReportLastTime(int divisionId, string date)
        {
            string message = string.Empty;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Maintenance/GetDivisionReportLastTime/DivisionId/{divisionId}/Date/{date.Replace("/", "-")}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var dateTime = JsonConvert.DeserializeObject<DateTime>(jsonString);
                        if (dateTime != null && dateTime != DateTime.MinValue)
                        {
                            var mDivision = divisionService.Get(divisionId);
                            if (mDivision != null && mDivision.Id > 0)
                                message = $"{mDivision.Name} PDF Last Update Time: {dateTime.ToString()}";
                        }

                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }


            return Json(message, JsonRequestBehavior.AllowGet);
        }

        public ActionResult DownloadTabulationPDF(int zoneId, int divisionId, string startdate, string endDate)
        {
            var mSearchCriteria = new Domain.SearchCriteria();
            mSearchCriteria.ZoneId = zoneId;
            mSearchCriteria.DivisionId = divisionId;
            mSearchCriteria.StartDate = ExtensionMethod.ConvertToDateTimeFormat(startdate, "d/M/yyyy");
            mSearchCriteria.EndDate = ExtensionMethod.ConvertToDateTimeFormat(endDate, "d/M/yyyy");
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSearchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Maintenance/DownloadTabulationPDF"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                if (mSearchCriteria.DivisionId <= 0 && mSearchCriteria.ZoneId > 0)
                                {
                                    var mZone = zoneService.GetZoneById(mSearchCriteria.ZoneId);
                                    if (mZone != null && mZone.Id > 0)
                                        return File(csvbytes, "application/pdf", $"{mZone.Name}-TabulationPDF.pdf");
                                }
                                else if (mSearchCriteria.DivisionId > 0 && mSearchCriteria.ZoneId <= 0)
                                {
                                    var mDivision = divisionService.Get(divisionId);
                                    if (mDivision != null && mDivision.Id > 0)
                                        return File(csvbytes, "application/pdf", $"{mDivision.Name}-TabulationPDF.pdf");
                                }
                                else if (mSearchCriteria.DivisionId > 0 && mSearchCriteria.ZoneId > 0)
                                {
                                    var mDivision = divisionService.Get(divisionId);
                                    if (mDivision != null && mDivision.Id > 0)
                                        return File(csvbytes, "application/pdf", $"{mDivision.Name}-TabulationPDF.pdf");
                                }
                                else
                                {
                                    return File(csvbytes, "application/pdf", $"Alert-TabulationPDF.pdf");
                                }

                            }
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "File not found!";
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }
            finally
            {
                BindPDFDropDown();
            }
            return View("PDF");
        }

        public ActionResult DownloadRailwayReportPDF(int divisionId, int siteId, string startdate, string endDate)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Maintenance/DownloadRailwayReportPDF/DivisionId/{divisionId}/SiteId/{siteId}/FromDate/{startdate.Replace("/", "-")}/ToDate/{endDate.Replace("/", "-")}")).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                var mDivision = divisionService.Get(divisionId);
                                if (mDivision != null && mDivision.Id > 0)
                                    return File(csvbytes, "application/pdf", $"{mDivision.Name}-RailwayReportPDF.pdf");

                            }
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "File not found!";
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }
            finally
            {
                BindPDFDropDown();
            }
            return View("PDF");
        }

        public ActionResult GetRailwayReport(int divisionId, string startdate, string endDate)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Maintenance/GetRailwayReport/DivisionId/{divisionId}/FromDate/{startdate.Replace("/", "-")}/ToDate/{endDate.Replace("/", "-")}")).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        ViewBag.StringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);

                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }

            return View("UpdateRailwayReportPDF");
        }

        public ActionResult DownloadPortfolioCSV(int userId, string searchDate)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Maintenance/GetPortfolioCSVReport/UserId/{userId}/SearchDate/{searchDate.Replace("/", "-")}")).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        string stringCSV = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringCSV.IsNotNullOrEmpty())
                        {
                            var date = ExtensionMethod.ConvertToDateTimeFormat(searchDate, "d/m/yyyy");
                            var mUser = userService.GetBy(userId);
                            if (mUser != null && mUser.Id > 0)
                            {
                                Response.Clear();
                                Response.Buffer = true;
                                Response.AddHeader("content-disposition", $"attachment;filename={date.ToShortDateString()} {mUser.FirstName}{mUser.LastName}portfolio's.csv");
                                Response.Charset = "utf-8";
                                Response.ContentType = "text/csv";
                                Response.Output.Write(stringCSV);
                                Response.Flush();
                                Response.End();
                            }
                        }

                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }
            finally
            {
                BindPDFDropDown();
            }
            return View("PDF");
        }

        public ActionResult DownloadTrueFRSAlertPDF(int divisionId, string startdate, string endDate)
        {
            var mSearchCriteria = new Domain.SearchCriteria();
            //mSearchCriteria.ZoneId = zoneId;
            mSearchCriteria.DivisionId = divisionId;
            mSearchCriteria.StartDate = ExtensionMethod.ConvertToDateTimeFormat(startdate, "d/M/yyyy");
            mSearchCriteria.EndDate = ExtensionMethod.ConvertToDateTimeFormat(endDate, "d/M/yyyy");
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSearchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Maintenance/DownloadTrueFRSAlertPDF"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                if (mSearchCriteria.DivisionId <= 0 && mSearchCriteria.ZoneId > 0)
                                {
                                    var mZone = zoneService.GetZoneById(mSearchCriteria.ZoneId);
                                    if (mZone != null && mZone.Id > 0)
                                        return File(csvbytes, "application/pdf", $"{mZone.Name}-TrueFRSAlert.pdf");
                                }
                                else if (mSearchCriteria.DivisionId > 0 && mSearchCriteria.ZoneId <= 0)
                                {
                                    var mDivision = divisionService.Get(divisionId);
                                    if (mDivision != null && mDivision.Id > 0)
                                        return File(csvbytes, "application/pdf", $"{mDivision.Name}-TrueFRSAlert.pdf");
                                }
                                else if (mSearchCriteria.DivisionId > 0 && mSearchCriteria.ZoneId > 0)
                                {
                                    var mDivision = divisionService.Get(divisionId);
                                    if (mDivision != null && mDivision.Id > 0)
                                        return File(csvbytes, "application/pdf", $"{mDivision.Name}-TrueFRSAlert.pdf");
                                }

                            }
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "File not found!";
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }
            finally
            {
                BindPDFDropDown();
            }
            return View("PDF");
        }

        public ActionResult DownloadFRSAlertWithInstancePDF(int siteId, string startdate, string endDate)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Maintenance/DownloadFRSAlertWithInstancePDF/SiteId/{siteId}/FromDate/{startdate.Replace("/", "-")}/ToDate/{endDate.Replace("/", "-")}")).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {

                                var mSite = siteService.Get(siteId);
                                if (mSite != null && mSite.Id > 0)
                                    return File(csvbytes, "application/pdf", $"{mSite.Name}-RailwayReportPDF.pdf");

                            }
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "File not found!";
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }
            finally
            {
                BindPDFDropDown();
            }
            return View("PDF");
        }

        public ActionResult DownloadFeedBackFRSAlertPDF(int divisionId, string startdate, string endDate)
        {
            var mSearchCriteria = new Domain.SearchCriteria();
            //mSearchCriteria.ZoneId = zoneId;
            mSearchCriteria.DivisionId = divisionId;
            mSearchCriteria.StartDate = ExtensionMethod.ConvertToDateTimeFormat(startdate, "d/M/yyyy");
            mSearchCriteria.EndDate = ExtensionMethod.ConvertToDateTimeFormat(endDate, "d/M/yyyy");
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSearchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Maintenance/DownloadFeedBackFRSAlertPDF"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                if (mSearchCriteria.DivisionId <= 0 && mSearchCriteria.ZoneId > 0)
                                {
                                    var mZone = zoneService.GetZoneById(mSearchCriteria.ZoneId);
                                    if (mZone != null && mZone.Id > 0)
                                        return File(csvbytes, "application/pdf", $"{mZone.Name}-FeedBackFRSAlert.pdf");
                                }
                                else if (mSearchCriteria.DivisionId > 0 && mSearchCriteria.ZoneId <= 0)
                                {
                                    var mDivision = divisionService.Get(divisionId);
                                    if (mDivision != null && mDivision.Id > 0)
                                        return File(csvbytes, "application/pdf", $"{mDivision.Name}-FeedBackFRSAlert.pdf");
                                }
                                else if (mSearchCriteria.DivisionId > 0 && mSearchCriteria.ZoneId > 0)
                                {
                                    var mDivision = divisionService.Get(divisionId);
                                    if (mDivision != null && mDivision.Id > 0)
                                        return File(csvbytes, "application/pdf", $"{mDivision.Name}-FeedBackFRSAlert.pdf");
                                }

                            }
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "File not found!";
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }
            finally
            {
                BindPDFDropDown();
            }
            return View("PDF");
        }

        public ActionResult DownloadFaultyMaterial(int siteId, string startdate, string endDate)
        {
            var mSearchCriteria = new Domain.SearchCriteria();
            mSearchCriteria.SiteId = siteId;
            mSearchCriteria.StartDate = ExtensionMethod.ConvertToDateTimeFormat(startdate, "d/M/yyyy");
            mSearchCriteria.EndDate = ExtensionMethod.ConvertToDateTimeFormat(endDate, "d/M/yyyy");
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSearchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("JobCardOverall/GetList"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        var mJobCardOverallTargets = JsonConvert.DeserializeObject<List<Domain.JobCardOverallTarget>>(jsonString);
                        if (mJobCardOverallTargets != null && mJobCardOverallTargets.Count > 0)
                        {
                            var csv = new StringBuilder();
                            var socsvstring = string.Empty;
                            socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7}", "Type", "Site", "Asset type", "Asset", "Cluster", "A10", "Issue Type", "Code");
                            csv.AppendLine(socsvstring);

                            foreach (var mJobCardOverallTarget in mJobCardOverallTargets.Where(x => x.MatrialCode != null && x.MatrialCode != string.Empty))
                            {
                                string strType = string.Empty;
                                var typeStr = (E7FRSAdvance.Utility.Utility.JobCardOverallTargetType)mJobCardOverallTarget.TypeId;
                                if (typeStr != null)
                                {
                                    strType = Convert.ToString(typeStr);
                                }


                                socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7}", strType, mJobCardOverallTarget.SiteName, mJobCardOverallTarget.AssetTypeName.RemoveComma(), mJobCardOverallTarget.AssetName, mJobCardOverallTarget.ClusterName.RemoveComma(), mJobCardOverallTarget.A10Id, mJobCardOverallTarget.IssueType.RemoveComma(), mJobCardOverallTarget.MatrialCode);
                                csv.AppendLine(socsvstring);
                            }

                            Response.Clear();
                            Response.Buffer = true;
                            Response.AddHeader("content-disposition", "attachment;filename=FaultyMaterial" + DateTime.Now.Ticks + ".csv");
                            Response.Charset = "utf-8";
                            Response.ContentType = "text/csv";
                            Response.Output.Write(csv);
                            Response.Flush();
                            Response.End();

                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "File not found!";
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }
            finally
            {
                BindPDFDropDown();
            }
            return View("PDF");
        }

        public JsonResult ExcludeSMSLogById(int id)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"SMSLog/Exclude/{id}")).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        var result = JsonConvert.DeserializeObject<bool>(jsonString);
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
                ViewBag.Error = ex.Message;
            }
            return Json(data, JsonRequestBehavior.AllowGet);
        }

        private List<int> GetYears()
        {
            List<int> Years = new List<int>();
            DateTime startYear = DateTime.Now.AddYears(-3);
            while (startYear.Year <= DateTime.Now.Year)
            {
                Years.Add(startYear.Year);
                startYear = startYear.AddYears(1);
            }
            return Years;
        }

        private void BindPDFDropDown()
        {
            ViewBag.Years = new SelectList(GetYears());
            ViewBag.Months = new SelectList(DateTimeFormatInfo.CurrentInfo.MonthNames.ToList());
            ViewBag.Zones = new SelectList(zoneService.GetAll(), "Id", "Name");
            ViewBag.Divisions = new SelectList(divisionService.GetAll(), "Id", "Name");
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            GetSiteKeepingUser();
        }

        #endregion

        #region Tabulation
        public ActionResult ViewTabulationDetail(int zoneId, int divisionId, string startdate, string endDate)
        {
            var mSearchCriteria = new Domain.SearchCriteria();
            mSearchCriteria.ZoneId = zoneId;
            mSearchCriteria.DivisionId = divisionId;
            mSearchCriteria.StartDate = ExtensionMethod.ConvertToDateTimeFormat(startdate, "d/M/yyyy");
            mSearchCriteria.EndDate = ExtensionMethod.ConvertToDateTimeFormat(endDate, "d/M/yyyy");
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSearchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Maintenance/ViewTabulationPDF"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        ViewBag.StringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }

            return View(mSearchCriteria);
        }

        public ActionResult _TabulationAlertList(SearchCriteria mSearchCriteria)
        {
            SMSLogLister mSMSLogLister = new SMSLogLister();
            mSMSLogLister.Pager.Take = -1;
            mSMSLogLister.SearchCriteria.Validity = true;
            mSMSLogLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
            mSMSLogLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
            mSMSLogLister.SearchCriteria.SiteId = mSearchCriteria.SiteId;
            mSMSLogLister.SearchCriteria.AlertTypeId = mSearchCriteria.AlertTypeId;

            mSMSLogLister.SearchCriteria.FromDate = mSearchCriteria.StartDate;
            mSMSLogLister.SearchCriteria.ToDate = mSearchCriteria.EndDate;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSMSLogLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SMSLog/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSMSLogLister = JsonConvert.DeserializeObject<SMSLogLister>(jsonString);
                        if (mSMSLogLister != null && mSMSLogLister.mSMSLogs != null)
                        {
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

            return PartialView(mSMSLogLister);

        }

        public ActionResult EditTabulationDetail(int zoneId, int divisionId, string startdate, string endDate)
        {
            var mSearchCriteria = new Domain.SearchCriteria();
            mSearchCriteria.ZoneId = zoneId;
            mSearchCriteria.DivisionId = divisionId;
            mSearchCriteria.StartDate = ExtensionMethod.ConvertToDateTimeFormat(startdate, "d/M/yyyy");
            mSearchCriteria.EndDate = ExtensionMethod.ConvertToDateTimeFormat(endDate, "d/M/yyyy");
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSearchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Maintenance/ViewTabulationPDF"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        ViewBag.StringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }

            return View(mSearchCriteria);
        }

        public ActionResult _EditTabulationAlertList(SearchCriteria mSearchCriteria)
        {
            SMSLogLister mSMSLogLister = new SMSLogLister();
            mSMSLogLister.Pager.Take = -1;
            mSMSLogLister.SearchCriteria.Validity = true;
            mSMSLogLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
            mSMSLogLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
            mSMSLogLister.SearchCriteria.SiteId = mSearchCriteria.SiteId;
            mSMSLogLister.SearchCriteria.AlertTypeId = mSearchCriteria.AlertTypeId;

            mSMSLogLister.SearchCriteria.FromDate = mSearchCriteria.StartDate;
            mSMSLogLister.SearchCriteria.ToDate = mSearchCriteria.EndDate;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSMSLogLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SMSLog/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSMSLogLister = JsonConvert.DeserializeObject<SMSLogLister>(jsonString);
                        if (mSMSLogLister != null && mSMSLogLister.mSMSLogs != null)
                        {
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

            return PartialView(mSMSLogLister);

        }

        public ActionResult UpdateTabulationAlertRemark(AlertAudit mAlertAudit)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mAlertAudit.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAlertAudit);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync("AlertAudit", str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        data = new { type = "success", result = "Alert remark has been Update." };
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

        public ActionResult UpdateAllTabulationAlertRemark(List<AlertAudit> mAlertAudits)
        {
            dynamic data = new ExpandoObject();
            try
            {
                if (mAlertAudits != null && mAlertAudits.Count > 0)
                {
                    foreach (var mAlertAudit in mAlertAudits)
                    {
                        mAlertAudit.CreatedBy = ClsHttpContent.LoginUser.Id;
                        using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                        {
                            var jsonStr = JsonConvert.SerializeObject(mAlertAudit);
                            StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                            var response = hcf.client.PostAsync("AlertAudit", str).Result;
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            if (response.StatusCode == HttpStatusCode.OK)
                            {
                                data = new { type = "success", result = "Alert remark has been Update." };
                            }
                            else
                            {
                                data = new { type = "error", result = "Internal server error." };
                            }
                        }
                    }
                }

            }
            catch (Exception)
            {
                data = new { type = "error", result = "Internal server error." };
            }

            return Json(data);
        }

        public ActionResult ViewTabulationAlertRemark(Domain.SearchCriteria searchCriteria)
        {
            List<AlertAudit> mAlertAudits = new List<AlertAudit>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"AlertAudit/SiteId/{searchCriteria.SiteId}/StartDate/{searchCriteria.StartDate.ToShortDateString().Replace("/", "-")}/EndDate/{searchCriteria.EndDate.ToShortDateString().Replace("/", "-")}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAlertAudits = JsonConvert.DeserializeObject<List<AlertAudit>>(jsonString);
                        if (mAlertAudits != null && mAlertAudits.Count > 0 && searchCriteria.Type.IsNotNullOrEmpty())
                        {
                            if (searchCriteria.Type == "open")
                            {
                                mAlertAudits = mAlertAudits.Where(x => x.IsOpenClose).ToList();
                            }
                            else if (searchCriteria.Type == "close")
                            {
                                mAlertAudits = mAlertAudits.Where(x => !x.IsOpenClose).ToList();

                            }
                        }
                        if (mAlertAudits != null && mAlertAudits.Count > 0 && searchCriteria.AssetType.IsNotNullOrEmpty())
                        {
                            mAlertAudits = mAlertAudits.Where(x => x.AssetType.Trim().ToUpper().StartsWith(searchCriteria.AssetType.Trim().ToUpper())).ToList();
                        }
                    }
                }
            }
            catch (Exception)
            {
                ViewBag.Error = "Internal server error.";
            }
            return PartialView(mAlertAudits);
        }

        public ActionResult PortfolioAlertAudit(int userId, string searchDate)
        {
            ViewBag.User = userService.GetBy(userId);
            SearchCriteria searchCriteria = new SearchCriteria();
            searchCriteria.UserId = userId;
            searchCriteria.StartDate = ExtensionMethod.ConvertToDateTimeFormat(searchDate, "d/M/yyyy");
            searchCriteria.EndDate = ExtensionMethod.ConvertToDateTimeFormat(searchDate, "d/M/yyyy");
            return View(searchCriteria);
        }

        public ActionResult PortfolioAlertAuditList(SearchCriteria searchCriteria)
        {
            List<AlertAudit> mAlertAudits = new List<AlertAudit>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"AlertAudit/UserId/{searchCriteria.UserId}/StartDate/{searchCriteria.StartDate.ToShortDateString().Replace("/", "-")}/EndDate/{searchCriteria.EndDate.ToShortDateString().Replace("/", "-")}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAlertAudits = JsonConvert.DeserializeObject<List<AlertAudit>>(jsonString);
                    }
                }
            }
            catch (Exception)
            {
                ViewBag.Error = "Internal server error.";
            }
            return PartialView(mAlertAudits);
        }

        public ActionResult DownloadPortfolioAlertAudit(int userId, string startdate, string endDate)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"AlertAudit/DownloadPDF/UserId/{userId}/StartDate/{startdate.Replace("/", "-")}/EndDate/{endDate.Replace("/", "-")}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] pdfbytes = System.Convert.FromBase64String(stringInBase64);
                            if (pdfbytes != null && pdfbytes.Length > 0)
                            {
                                var mUser = userService.GetBy(userId);
                                if (mUser != null && mUser.Id > 0)
                                    return File(pdfbytes, "application/pdf", $"{mUser.FirstName}{mUser.LastName}-AlertAuditReport.pdf");
                            }
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "File not found!";
                        }
                    }
                }
            }
            catch (Exception)
            {
                ViewBag.Error = "Internal server error.";
            }
            return View();
        }

        public ActionResult AddPortfolioAlertAuditRemark(int id)
        {
            var mAlertAudit = new AlertAudit();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("AlertAudit/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAlertAudit = JsonConvert.DeserializeObject<Domain.AlertAudit>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
            }
            return PartialView(mAlertAudit);
        }

        public ActionResult UpdatePortfolioAlertAudit(AlertAudit mAlertAudit)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mAlertAudit.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAlertAudit);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format($"AlertAudit/Id/{mAlertAudit.Id}"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        //var deviceINIs = JsonConvert.DeserializeObject<List<AssetInfo>>(jsonString);
                        data = new { type = "success", result = "Status Updated" };
                    }
                    else
                    {
                        data = new { type = "error", result = "Internal Server error." };
                    }
                }
            }
            catch (Exception)
            {
                data = new { type = "error", result = "Internal Server error." };
            }


            return Json(data, JsonRequestBehavior.AllowGet);
        }

        #endregion

        #region PLCConfig

        public ActionResult PLCConfig()
        {
            TempData.Remove("ADCTimeRoles");
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            return View();
        }

        public ActionResult GetExportRole(int siteId, bool updateToWeb)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"PLCConfig/GetRole/SiteId/{siteId}/UpdateToWeb/{updateToWeb}/")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var roles = JsonConvert.DeserializeObject<List<string>>(jsonString);
                        if (roles != null && roles.Count > 0)
                        {
                            if (roles.Count <= 1)
                            {
                                byte[] csvBytes = Encoding.UTF8.GetBytes(roles.FirstOrDefault());
                                string base64String = Convert.ToBase64String(csvBytes);

                                // Option 1: Download directly from Base64
                                Response.Clear();
                                Response.Buffer = true;
                                Response.AddHeader("content-disposition", "attachment;filename=roles_" + DateTime.Now.Ticks + ".csv");
                                Response.Charset = "";
                                Response.ContentType = "application/csv";
                                byte[] fileBytes = Convert.FromBase64String(roles.FirstOrDefault());
                                Response.BinaryWrite(fileBytes);
                                Response.Flush();
                                Response.End();
                            }
                            else
                            {
                                Dictionary<string, byte[]> byteArrayList = new Dictionary<string, byte[]>();
                                int count = 0;
                                foreach (var role in roles)
                                {
                                    count++;
                                    byte[] filebytes = System.Convert.FromBase64String(role);
                                    if (filebytes != null && filebytes.Length > 0)
                                    {
                                        byteArrayList.Add($"channel{count}.csv", filebytes);
                                    }
                                }

                                using (MemoryStream ms = new MemoryStream())
                                {
                                    using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
                                    {
                                        foreach (var file in byteArrayList)
                                        {
                                            var entry = archive.CreateEntry($"{file.Key}", CompressionLevel.Fastest);
                                            using (var zipStream = entry.Open())
                                            {
                                                zipStream.Write(file.Value, 0, file.Value.Length);
                                            }
                                        }
                                    }
                                    return File(ms.ToArray(), "application/zip", $"Role.zip");
                                }
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
            return RedirectToAction("PLCConfig");
        }

        public ActionResult GetAmendentRole(int siteId, bool updateToWeb)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"PLCConfig/GetAmendentRole/SiteId/{siteId}/UpdateToWeb/{updateToWeb}/")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var roles = JsonConvert.DeserializeObject<List<string>>(jsonString);
                        if (roles != null && roles.Count > 0)
                        {
                            var csv = new StringBuilder();
                            foreach (var role in roles)
                            {
                                if (role.Contains(","))
                                {
                                    csv.AppendLine(String.Format("\"{0}\",", role));
                                }
                                else
                                {
                                    csv.AppendLine(String.Format("{0},", role));
                                }
                                //csv.AppendLine($"{role},");
                            }

                            Response.Clear();
                            Response.Buffer = true;
                            Response.AddHeader("content-disposition", "attachment;filename=AmendentRole_" + DateTime.Now.Ticks + ".csv");
                            Response.Charset = "";
                            Response.ContentType = "application/csv";
                            Response.Output.Write(csv);
                            Response.Flush();
                            Response.End();

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
            return RedirectToAction("PLCConfig");
        }

        public ActionResult GetExistingExportRole(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"PLCConfig/GetRole/SiteId/{siteId}/")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var roles = JsonConvert.DeserializeObject<List<string>>(jsonString);
                        if (roles != null && roles.Count > 0)
                        {
                            var csv = new StringBuilder();
                            foreach (var role in roles)
                            {
                                if (role.Contains(","))
                                {
                                    csv.AppendLine(String.Format("\"{0}\",", role));
                                }
                                else
                                {
                                    csv.AppendLine(String.Format("{0},", role));
                                }
                                //csv.AppendLine($"{role},");
                            }

                            Response.Clear();
                            Response.Buffer = true;
                            Response.AddHeader("content-disposition", "attachment;filename=Role_" + DateTime.Now.Ticks + ".csv");
                            Response.Charset = "";
                            Response.ContentType = "application/csv";
                            Response.Output.Write(csv);
                            Response.Flush();
                            Response.End();

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
            return RedirectToAction("PLCConfig");
        }

        public ActionResult GetExportDataLog(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"PLCConfig/GetDataLog/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var dataLogs = JsonConvert.DeserializeObject<List<string>>(jsonString);
                        if (dataLogs != null && dataLogs.Count > 0)
                        {
                            var csv = new StringBuilder();
                            foreach (var dataLog in dataLogs)
                            {
                                csv.AppendLine($"{dataLog},");
                            }

                            Response.Clear();
                            Response.Buffer = true;
                            Response.AddHeader("content-disposition", "attachment;filename=DataLog_" + DateTime.Now.Ticks + ".csv");
                            Response.Charset = "";
                            Response.ContentType = "application/csv";
                            Response.Output.Write(csv);
                            Response.Flush();
                            Response.End();

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
            return RedirectToAction("PLCConfig");
        }

        public ActionResult GetExportLogScale(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"PLCConfig/GetLogScale/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var dataLogs = JsonConvert.DeserializeObject<List<string>>(jsonString);
                        if (dataLogs != null && dataLogs.Count > 0)
                        {
                            var csv = new StringBuilder();
                            foreach (var dataLog in dataLogs)
                            {
                                csv.AppendLine($"{dataLog},");
                            }

                            Response.Clear();
                            Response.Buffer = true;
                            Response.AddHeader("content-disposition", "attachment;filename=LogScale_" + DateTime.Now.Ticks + ".csv");
                            Response.Charset = "";
                            Response.ContentType = "application/csv";
                            Response.Output.Write(csv);
                            Response.Flush();
                            Response.End();

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
            return RedirectToAction("PLCConfig");
        }

        public ActionResult GetExportDeviceINI(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"PLCConfig/GetExportDeviceINI/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var deviceINIs = JsonConvert.DeserializeObject<List<string>>(jsonString);
                        if (deviceINIs != null && deviceINIs.Count > 0)
                        {
                            if (deviceINIs.Count <= 1)
                            {
                                var deviceINI = deviceINIs.FirstOrDefault();
                                if (deviceINI.IsNotNullOrEmpty())
                                {
                                    byte[] bytes = Encoding.ASCII.GetBytes(deviceINI);


                                    var fileArray = Convert.FromBase64String(deviceINI);
                                    if (fileArray != null && fileArray.Count() > 0)
                                        return File(fileArray, "text/plain", $"DeviceINI_{DateTime.Now.Ticks}" + ".ini");


                                }
                            }
                            else
                            {
                                Dictionary<string, byte[]> byteArrayList = new Dictionary<string, byte[]>();
                                int count = 1;
                                foreach (var base64 in deviceINIs)
                                {
                                    byte[] filebytes = System.Convert.FromBase64String(base64);
                                    if (filebytes != null && filebytes.Length > 0)
                                    {
                                        // return File(filebytes, "application/zip", $"{mDivision.Name}-PLCBackUp.tar");
                                        //                         return File(
                                        //filebytes, System.Net.Mime.MediaTypeNames.Application.Octet, outcome.FirstOrDefault().Key);


                                        if (filebytes != null && filebytes.Length > 0)
                                        {
                                            byteArrayList.Add($"Channel{count}.ini", filebytes);
                                        }
                                    }
                                    count++;

                                }

                                if (byteArrayList != null && byteArrayList.Count > 0)
                                {
                                    using (MemoryStream ms = new MemoryStream())
                                    {
                                        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
                                        {
                                            foreach (var file in byteArrayList)
                                            {
                                                var entry = archive.CreateEntry($"{file.Key}", CompressionLevel.Fastest);
                                                using (var zipStream = entry.Open())
                                                {
                                                    zipStream.Write(file.Value, 0, file.Value.Length);
                                                }
                                            }
                                        }
                                        return File(ms.ToArray(), "application/zip", $"DeviceINIs.zip");
                                    }
                                }
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
            return RedirectToAction("PLCConfig");
        }

        public ActionResult GetExportPointMachineConfig(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"PLCConfig/GetExportPointMachineConfig/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var deviceINIs = JsonConvert.DeserializeObject<List<string>>(jsonString);
                        if (deviceINIs != null && deviceINIs.Count > 0)
                        {
                            if (deviceINIs.Count <= 1)
                            {
                                var deviceINI = deviceINIs.FirstOrDefault();
                                if (deviceINI.IsNotNullOrEmpty())
                                {
                                    byte[] bytes = Encoding.ASCII.GetBytes(deviceINI);


                                    var fileArray = Convert.FromBase64String(deviceINI);
                                    if (fileArray != null && fileArray.Count() > 0)
                                        return File(fileArray, "text/plain", $"PointMachineConfig_{DateTime.Now.Ticks}" + ".ini");


                                }
                            }
                            else
                            {
                                Dictionary<string, byte[]> byteArrayList = new Dictionary<string, byte[]>();
                                int count = 1;
                                foreach (var base64 in deviceINIs)
                                {
                                    byte[] filebytes = System.Convert.FromBase64String(base64);
                                    if (filebytes != null && filebytes.Length > 0)
                                    {
                                        if (filebytes != null && filebytes.Length > 0)
                                        {
                                            byteArrayList.Add($"Channel{count}.ini", filebytes);
                                        }
                                    }
                                    count++;

                                }

                                if (byteArrayList != null && byteArrayList.Count > 0)
                                {
                                    using (MemoryStream ms = new MemoryStream())
                                    {
                                        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
                                        {
                                            foreach (var file in byteArrayList)
                                            {
                                                var entry = archive.CreateEntry($"{file.Key}", CompressionLevel.Fastest);
                                                using (var zipStream = entry.Open())
                                                {
                                                    zipStream.Write(file.Value, 0, file.Value.Length);
                                                }
                                            }
                                        }
                                        return File(ms.ToArray(), "application/zip", $"PointMachineConfigs.zip");
                                    }
                                }
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
            return RedirectToAction("PLCConfig");
        }

        [HttpPost]
        public ActionResult UploadAutINI()
        {
            string fileName = string.Empty;
            for (int i = 0; i < Request.Files.Count; i++)
            {
                var file = Request.Files[i];

                byte[] thePictureAsBytes = new byte[file.ContentLength];
                using (BinaryReader theReader = new BinaryReader(file.InputStream))
                {
                    thePictureAsBytes = theReader.ReadBytes(file.ContentLength);
                }
                string thePictureDataAsString = Convert.ToBase64String(thePictureAsBytes);


                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        AutCalINI mAutCalINI = new AutCalINI() { FileBase64 = thePictureDataAsString };

                        var jsonStr = JsonConvert.SerializeObject(mAutCalINI);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("PLCConfig/UploadAutINI"), str).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            fileName = JsonConvert.DeserializeObject<string>(jsonString);

                        }
                    }
                }
                catch (Exception)
                {

                    throw;
                }
            }

            return Json(fileName, JsonRequestBehavior.AllowGet);
        }

        public ActionResult DownLoadExportAutINI(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {

                    //AutCalINI mAutCalINI = new AutCalINI() { FileName = fileName };

                    var response = hcf.client.GetAsync(String.Format($"PLCConfig/DownloadAutCalINI/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var autCalINIs = JsonConvert.DeserializeObject<string>(jsonString);
                        if (autCalINIs.IsNotNullOrEmpty())
                        {
                            byte[] bytes = Encoding.ASCII.GetBytes(autCalINIs);


                            var fileArray = Convert.FromBase64String(autCalINIs);
                            if (fileArray != null && fileArray.Count() > 0)
                                return File(fileArray, "text/plain", $"AutCal_{DateTime.Now.Ticks.ToString()}" + ".ini");


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

            return RedirectToAction("PLCConfig");
        }

        public ActionResult CreateDeviceTime(DeviceTime mDeviceTime)
        {
            dynamic data = new { type = "", result = "" };
            try
            {
                mDeviceTime.UserId = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mDeviceTime);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("PLCConfig/CreateDeviceTime"), str).Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success" };
                    }
                    else
                    {
                        data = new { type = "error" };
                    }
                }
            }
            catch (Exception ex)
            {
                data = new { type = "error" };
            }
            return Json(data, JsonRequestBehavior.AllowGet);
        }

        public ActionResult CompareRoleINI(int siteId)
        {
            List<CompareRoleINI> mCompareRoleINI = new List<CompareRoleINI>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"PLCConfig/GetRole/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var roles = JsonConvert.DeserializeObject<List<string>>(jsonString);
                        if (roles != null && roles.Count > 0)
                        {
                            for (int i = 0; i < Request.Files.Count; i++)
                            {
                                var file = Request.Files[i];
                                var mDeviceDataTable = GetRoleFromCSVFile(file);
                                if (mDeviceDataTable != null)
                                {
                                    string role = string.Empty;
                                    foreach (DataRow dtRow in mDeviceDataTable.Rows)
                                    {
                                        var dtRows = dtRow[0].ToString();
                                        var splitRoles = dtRows.Split('=')[0];
                                        var roleSplit = Regex.Match(splitRoles, @"\d+").Value;
                                        if (roleSplit.IsNotNullOrEmpty() && role != roleSplit)
                                        {
                                            role = roleSplit;

                                            var searchRoles = roles.Where(x => x.Contains("role" + roleSplit + "=")).FirstOrDefault();
                                            if (searchRoles != null)
                                            {
                                                if (searchRoles.Trim().ToUpper() != dtRows.Trim().ToUpper())
                                                {
                                                    mCompareRoleINI.Add(new CompareRoleINI() { Role = "role" + role, Web = searchRoles, Plc = dtRows });

                                                }
                                            }
                                            else
                                                mCompareRoleINI.Add(new CompareRoleINI() { Role = "role" + role, Web = searchRoles, Plc = dtRows });

                                        }
                                    }

                                }
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

            return PartialView("_CompareRolePartial", mCompareRoleINI);
        }

        public ActionResult UpdateCompareRole(CompareRoleINI mCompareRoleINI)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mCompareRoleINI);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("PLCConfig/UpdateCompareRole"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {

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

            return Json("");
        }

        public ActionResult CompareDeviceINI(int siteId)
        {
            List<DeviceINI> mCompareDeviceINI = new List<DeviceINI>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"PLCConfig/GetDeviceINI/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var deviceINIs = JsonConvert.DeserializeObject<List<DeviceINI>>(jsonString);
                        if (deviceINIs != null && deviceINIs.Count > 0)
                        {
                            for (int i = 0; i < Request.Files.Count; i++)
                            {
                                var file = Request.Files[i];
                                var mFiledeviceINIs = GetDeviceINIFromCSVFile(file);
                                if (mFiledeviceINIs != null && mFiledeviceINIs.Count > 0)
                                {

                                    foreach (var section in mFiledeviceINIs.GroupBy(x => x.Section).Select(x => x.Key))
                                    {
                                        if (section.Contains("Device"))
                                        {
                                            var device = mFiledeviceINIs.Where(x => x.Section == section).ToList();
                                            if (device != null && device.Count > 0)
                                            {
                                                var address = device.Where(x => x.Key == "address").FirstOrDefault();
                                                if (address != null)
                                                {
                                                    foreach (var item in device.Where(x => x.Key.Contains("role")))
                                                    {
                                                        var selectAddress = deviceINIs.Where(x => x.Key == "address" && x.Value == address.Value).FirstOrDefault();

                                                        if (selectAddress != null)
                                                        {
                                                            var deviceAddressList = deviceINIs.Where(x => x.Section == selectAddress.Section).ToList();
                                                            if (deviceAddressList != null && deviceAddressList.Count > 0)
                                                            {
                                                                if (deviceAddressList.Where(m => m.Key == item.Key && m.Value == item.Value).Count() <= 0)
                                                                {
                                                                    var webValue = deviceAddressList.Where(m => m.Key == item.Key).FirstOrDefault();
                                                                    if (webValue != null)
                                                                    {
                                                                        mCompareDeviceINI.Add(new DeviceINI() { Section = item.Section, Key = item.Key, Value = item.Value, Address = address.Value, WebValue = webValue.Value, AssetInfoId = webValue.AssetInfoId });
                                                                    }
                                                                    else
                                                                    {
                                                                        mCompareDeviceINI.Add(new DeviceINI() { Section = item.Section, Key = item.Key, Value = item.Value, Address = address.Value });
                                                                    }


                                                                }

                                                            }
                                                            else
                                                            {
                                                                mCompareDeviceINI.Add(new DeviceINI() { Section = item.Section, Key = item.Key, Value = item.Value, Address = address.Value });

                                                            }

                                                        }
                                                        else
                                                        {
                                                            mCompareDeviceINI.Add(new DeviceINI() { Section = item.Section, Key = item.Key, Value = item.Value, Address = address.Value });
                                                        }



                                                    }
                                                }


                                            }
                                        }
                                    }
                                }
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

            return PartialView("_CompareDevicePartial", mCompareDeviceINI);

        }

        public ActionResult UpdateCompareDeviceINI(DeviceINI mDeviceINI)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mDeviceINI);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("PLCConfig/UpdateCompareDeviceINI"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {

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

            return Json("");
        }

        public ActionResult UploadDeviceTimeRoleINI()
        {
            var roles = new List<string>();
            for (int i = 0; i < Request.Files.Count; i++)
            {
                var file = Request.Files[i];

                DataTable csvDataTable = new DataTable();

                // Read bytes from http input stream
                var csvBody = string.Empty;

                using (BinaryReader b = new BinaryReader(file.InputStream))
                {
                    byte[] binData = b.ReadBytes(file.ContentLength);
                    csvBody = Encoding.UTF8.GetString(binData);
                }

                var memoryStream = new MemoryStream();
                var streamWriter = new StreamWriter(memoryStream);
                streamWriter.Write(csvBody);
                streamWriter.Flush();
                memoryStream.Position = 0;

                using (TextFieldParser csvReader = new TextFieldParser(memoryStream))
                {
                    csvReader.SetDelimiters(new string[] { "," });
                    csvReader.HasFieldsEnclosedInQuotes = true;
                    string[] colFields = csvReader.ReadFields();

                    foreach (string column in colFields)
                    {
                        DataColumn datecolumn = new DataColumn(column);
                        datecolumn.AllowDBNull = true;
                        csvDataTable.Columns.Add(datecolumn);
                        csvDataTable.PrimaryKey = new DataColumn[] { csvDataTable.Columns["[roles]"] };
                    }
                    while (!csvReader.EndOfData)
                    {
                        string[] fieldData = csvReader.ReadFields();
                        if (fieldData.Length > 1)
                        {
                            fieldData = new string[] { string.Join(",", fieldData) };
                            //fieldData[0] = string.Join(",", fieldData);
                        }
                        csvDataTable.Rows.Add(fieldData);
                    }
                }

                roles = csvDataTable.AsEnumerable()
                          .Select(r => r.Field<string>("[roles]"))
                          .ToList();


            }

            return Json(roles, JsonRequestBehavior.AllowGet);
        }

        public ActionResult UploadDeviceTimeADCTimeValue()
        {
            var roles = new List<string>();
            for (int i = 0; i < Request.Files.Count; i++)
            {
                var file = Request.Files[i];

                DataTable csvDataTable = new DataTable();

                // Read bytes from http input stream
                var csvBody = string.Empty;

                using (BinaryReader b = new BinaryReader(file.InputStream))
                {
                    byte[] binData = b.ReadBytes(file.ContentLength);
                    csvBody = Encoding.UTF8.GetString(binData);
                }

                var memoryStream = new MemoryStream();
                var streamWriter = new StreamWriter(memoryStream);
                streamWriter.Write(csvBody);
                streamWriter.Flush();
                memoryStream.Position = 0;
                using (TextFieldParser csvReader = new TextFieldParser(memoryStream))
                {
                    csvReader.SetDelimiters(new string[] { "," });
                    csvReader.HasFieldsEnclosedInQuotes = true;
                    string[] colFields = csvReader.ReadFields();

                    foreach (string column in colFields)
                    {
                        DataColumn datecolumn = new DataColumn(column);
                        datecolumn.AllowDBNull = true;
                        csvDataTable.Columns.Add(datecolumn);
                        csvDataTable.PrimaryKey = new DataColumn[] { csvDataTable.Columns["[roles]"] };
                    }
                    string section = string.Empty;
                    while (!csvReader.EndOfData)
                    {
                        string[] fieldData = csvReader.ReadFields();
                        if (fieldData.Length > 1)
                        {
                            fieldData = new string[] { string.Join(",", fieldData) };
                            //fieldData[0] = string.Join(",", fieldData);
                        }
                        roles.Add(fieldData[0]);
                    }
                }

            }
            TempData["ADCTimeRoles"] = roles;
            TempData.Keep();
            return Json(roles, JsonRequestBehavior.AllowGet);
        }

        public ActionResult UpdateADCTimeValue(int siteId)
        {
            dynamic data = new ExpandoObject();
            var roles = TempData.Peek("ADCTimeRoles") as List<string>;
            if (roles != null && roles.Count > 0)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(roles);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PutAsync(String.Format($"PLCConfig/UpdateADCTimeValue/SiteId/{siteId}"), str).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            //var deviceINIs = JsonConvert.DeserializeObject<List<AssetInfo>>(jsonString);
                            data = new { type = "success", result = "Update ADC Real Time Value is success" };
                        }
                        else
                        {
                            data = new { type = "error", result = "Internal Server error." };
                        }
                    }
                }
                catch (Exception ex)
                {
                    data = new { type = "error", result = "Internal Server error." };
                }
            }
            else
            {
                data = new { type = "error", result = "Please upload file." };
            }

            return Json(data, JsonRequestBehavior.AllowGet);
        }

        public ActionResult DownloadADCTimeValue(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"PLCConfig/GetADCTimeValue/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var mAssetInfos = JsonConvert.DeserializeObject<List<AssetInfo>>(jsonString);
                        if (mAssetInfos != null && mAssetInfos.Count > 0)
                        {
                            var csv = new StringBuilder();
                            var socsvstring = string.Empty;
                            socsvstring = string.Format("{0},{1},{2},{3},{4}", "Asset Type", "Asset Name", "Attribute Name", "Role", "ADC Real Time Value");
                            csv.AppendLine(socsvstring);
                            foreach (var mAssetInfo in mAssetInfos)
                            {
                                socsvstring = string.Format("{0},{1},{2},{3},{4}", mAssetInfo.AssestTypeName, mAssetInfo.AssetName, mAssetInfo.AttributeName, mAssetInfo.Value, mAssetInfo.ADC_RTime_Value);
                                csv.AppendLine(socsvstring);

                            }

                            Response.Clear();
                            Response.Buffer = true;
                            Response.AddHeader("content-disposition", "attachment;filename=ADCTimeValue_" + DateTime.Now.Ticks + ".csv");
                            Response.Charset = "";
                            Response.ContentType = "application/csv";
                            Response.Output.Write(csv);
                            Response.Flush();
                            Response.End();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
            }

            return RedirectToAction("PLCConfig");
        }

        public ActionResult DownloadFamilyTrackPDF(int siteId)
        {
            var mFamilyTracks = new List<Domain.FamilyTrack>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"FamilyTrack/SiteId/{siteId}")).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mFamilyTracks = JsonConvert.DeserializeObject<List<Domain.FamilyTrack>>(jsonString);
                        if (mFamilyTracks != null && mFamilyTracks.Count > 0)
                        {
                            string path = HttpContext.Server.MapPath("~/Template/FamilyTrack.html");
                            FileReader fileReader = new FileReader(path);
                            string pdfTemplate = fileReader.Read();
                            string temp = string.Empty;
                            var site = GetSiteById(siteId);
                            if (site != null && site.Id > 0)
                            {
                                pdfTemplate = pdfTemplate.Replace("{%siteName%}", site.Name);
                                var asssets = GetAssestBy(site.Id, (int)E7FRSAdvance.Utility.Utility.AssetType.TRACK);
                                if (asssets != null && asssets.Count > 0)
                                {
                                    foreach (var assset in asssets)
                                    {
                                        int counter = 0;
                                        var recLength = mFamilyTracks.Where(x => x.AssetId == assset.Id).Count();
                                        var familyTrackName = string.Empty;
                                        if (recLength > 0)
                                        {
                                            foreach (var item in mFamilyTracks.Where(x => x.AssetId == assset.Id).ToList())
                                            {
                                                counter++;
                                                if (counter == recLength)
                                                    familyTrackName += $"{item.AssetName} ";
                                                else
                                                    familyTrackName += $"{item.AssetName}, ";

                                            }
                                        }

                                        temp += "<tr>";
                                        temp += $"<td style='padding:3px;text-align: center;'><h4>{assset.Name}</h4></td>";
                                        temp += $"<td style='padding:3px;text-align: left;'><h4>{familyTrackName}</h4></td>";
                                        temp += "</tr>";


                                    }
                                }
                            }

                            pdfTemplate = pdfTemplate.Replace("{%tbodyData%}", temp);

                            Byte[] res = null;
                            using (MemoryStream ms = new MemoryStream())
                            {
                                var pdf = TheArtOfDev.HtmlRenderer.PdfSharp.PdfGenerator.GeneratePdf(pdfTemplate, PdfSharp.PageSize.A4, 5);
                                pdf.Save(ms);
                                res = ms.ToArray();
                            }

                            var responses = System.Web.HttpContext.Current.Response;
                            responses.BufferOutput = true;
                            responses.Clear();
                            responses.ClearHeaders();
                            responses.AddHeader("content-disposition", $"attachment;filename=FamilyTrack_{DateTime.Now.Ticks}.pdf");
                            responses.ContentType = System.Net.Mime.MediaTypeNames.Application.Pdf;
                            responses.ContentEncoding = System.Text.Encoding.UTF8;
                            responses.BinaryWrite(res);
                            responses.End();
                        }




                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }

            return RedirectToAction("PLCConfig");
        }

        public ActionResult DownloadPLCBackUp(int siteId)
        {
            Dictionary<string, byte[]> byteArrayList = new Dictionary<string, byte[]>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Maintenance/DownloadPLCBackUp/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] filebytes = System.Convert.FromBase64String(stringInBase64);
                            if (filebytes != null && filebytes.Length > 0)
                            {
                                var mDivision = siteService.Get(siteId);
                                if (mDivision != null && mDivision.Id > 0)
                                {
                                    if (filebytes != null && filebytes.Length > 0)
                                    {
                                        byteArrayList.Add($"{mDivision.Name}PLCBackUp.tar", filebytes);
                                    }
                                }


                            }
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "File not found!";
                        }

                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }

                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Maintenance/DownloadBaseFile")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var outcome = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonString);
                        if (outcome != null)
                        {
                            byte[] filebytes = System.Convert.FromBase64String(outcome.FirstOrDefault().Value);
                            if (filebytes != null && filebytes.Length > 0)
                            {
                                // return File(filebytes, "application/zip", $"{mDivision.Name}-PLCBackUp.tar");
                                //                         return File(
                                //filebytes, System.Net.Mime.MediaTypeNames.Application.Octet, outcome.FirstOrDefault().Key);


                                if (filebytes != null && filebytes.Length > 0)
                                {
                                    byteArrayList.Add(outcome.FirstOrDefault().Key, filebytes);
                                }
                            }
                        }


                    }

                }
            }
            catch (Exception)
            {

            }
            finally
            {
                ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            }
            if (byteArrayList != null && byteArrayList.Count > 0)
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
                    {
                        foreach (var file in byteArrayList)
                        {
                            var entry = archive.CreateEntry($"{file.Key}", CompressionLevel.Fastest);
                            using (var zipStream = entry.Open())
                            {
                                zipStream.Write(file.Value, 0, file.Value.Length);
                            }
                        }
                    }
                    return File(ms.ToArray(), "application/zip", $"Back Up.zip");
                }
            }


            return View("PLCConfig");
        }

        public ActionResult DownloadBaseFile()
        {


            return View("PLCConfig");
        }

        public ActionResult UpdatePointScale(int siteId)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"PLCConfig/SavePointMachineScale/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        //var deviceINIs = JsonConvert.DeserializeObject<List<AssetInfo>>(jsonString);
                        data = new { type = "success", result = "Point Machine Scale has been Update success." };
                    }
                    else
                    {
                        data = new { type = "error", result = "Internal Server error." };
                    }
                }
            }
            catch (Exception ex)
            {
                data = new { type = "error", result = "Internal Server error." };
            }

            return Json(data, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public ActionResult UpdateADCType(Domain.CardLine mCardLine)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mCardLine);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format($"CardLine/UpdateADC/Id/{mCardLine.Id}"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        //var deviceINIs = JsonConvert.DeserializeObject<List<AssetInfo>>(jsonString);
                        data = new { type = "success", result = "ADC type has been Update success." };
                    }
                    else
                    {
                        data = new { type = "error", result = "Internal Server error." };
                    }
                }
            }
            catch (Exception ex)
            {
                data = new { type = "error", result = "Internal Server error." };
            }

            return Json(data, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetYardConfigByAssetType(int siteId, int assetTypeId)
        {
            List<CardLine> mCardLines = new List<CardLine>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("CardLine/SiteId/{0}/assetTypeId/{1}", siteId, assetTypeId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mCardLines = JsonConvert.DeserializeObject<List<CardLine>>(jsonString);
                        if (mCardLines != null && mCardLines.Count > 0)
                        {
                            var adcs = new List<ADC>();
                            foreach (var clusterId in mCardLines.Where(x => x.ClusterId != null).GroupBy(x => x.ClusterId).Select(x => x.Key).ToList())
                            {
                                var adc = GetADC(clusterId.Value);
                                if (adc != null && adc.Count > 0)
                                {
                                    adcs.AddRange(adc);
                                }


                            }
                            foreach (var mCardLine in mCardLines)
                            {
                                mCardLine.mADCs = adcs.Where(x => x.ClusterId == mCardLine.ClusterId).ToList();
                            }
                        }

                        //var asset=assetAttributeService
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

            return PartialView("_YardConfigByAssetTypePartial", mCardLines);

        }

        public ActionResult DownloadPcalScale(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"PLCConfig/DownloadPcalINI/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var deviceINIs = JsonConvert.DeserializeObject<string>(jsonString);
                        if (deviceINIs.IsNotNullOrEmpty())
                        {
                            var fileArray = Convert.FromBase64String(deviceINIs);
                            var mDivision = siteService.Get(siteId);
                            if (mDivision != null && mDivision.Id > 0)
                            {
                                if (fileArray != null && fileArray.Count() > 0)
                                    return File(fileArray, "text/plain", $"PcalINI_{mDivision.StationCode}" + ".ini");
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

            return RedirectToAction("PLCConfig");
        }

        private DataTable GetRoleFromCSVFile(HttpPostedFileBase file)
        {
            DataTable csvDataTable = new DataTable();

            // Read bytes from http input stream
            var csvBody = string.Empty;

            using (BinaryReader b = new BinaryReader(file.InputStream))
            {
                byte[] binData = b.ReadBytes(file.ContentLength);
                csvBody = Encoding.UTF8.GetString(binData);
            }

            var memoryStream = new MemoryStream();
            var streamWriter = new StreamWriter(memoryStream);
            streamWriter.Write(csvBody);
            streamWriter.Flush();
            memoryStream.Position = 0;

            using (TextFieldParser csvReader = new TextFieldParser(memoryStream))
            {
                csvReader.SetDelimiters(new string[] { "," });
                csvReader.HasFieldsEnclosedInQuotes = true;
                string[] colFields = csvReader.ReadFields();

                foreach (string column in colFields)
                {
                    DataColumn datecolumn = new DataColumn(column);
                    datecolumn.AllowDBNull = true;
                    csvDataTable.Columns.Add(datecolumn);
                    csvDataTable.PrimaryKey = new DataColumn[] { csvDataTable.Columns["[roles]"] };
                }
                while (!csvReader.EndOfData)
                {
                    string[] fieldData = csvReader.ReadFields();
                    if (fieldData.Length > 1)
                    {
                        fieldData = new string[] { string.Join(",", fieldData) };
                        //fieldData[0] = string.Join(",", fieldData);
                    }
                    csvDataTable.Rows.Add(fieldData);
                }
            }

            return csvDataTable;
        }

        private List<DeviceINI> GetDeviceINIFromCSVFile(HttpPostedFileBase file)
        {
            List<DeviceINI> deviceINIs = new List<DeviceINI>();

            // Read bytes from http input stream
            var csvBody = string.Empty;

            using (BinaryReader b = new BinaryReader(file.InputStream))
            {
                byte[] binData = b.ReadBytes(file.ContentLength);
                csvBody = Encoding.UTF8.GetString(binData);

                string[] stringSeparators = new string[] { "\r\n" };
                string[] lines = csvBody.Split(stringSeparators, StringSplitOptions.None);
                string deviceName = string.Empty;
                foreach (var item in lines)
                {
                    string line = item.Trim();

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        deviceName = line;
                        continue;
                    }

                    int index = line.IndexOf('=');
                    if (index < 0)
                        continue;

                    string key = line.Substring(0, index).Trim();

                    string value = line.Substring(index + 1).Trim();
                    string comment = "";
                    index = value.IndexOf("//");
                    if (index > -1)
                    {
                        comment = value.Substring(index).Trim();
                        value = value.Substring(0, index).Trim();
                    }

                    deviceINIs.Add(new DeviceINI() { Section = deviceName.Replace("[", "").Replace("]", ""), Key = key, Value = value });
                }
            }

            //var memoryStream = new MemoryStream();
            //var streamWriter = new StreamWriter(memoryStream);
            //streamWriter.Write(csvBody);
            //streamWriter.Flush();
            //memoryStream.Position = 0;

            //using (TextFieldParser csvReader = new TextFieldParser(memoryStream))
            //{
            //    csvReader.SetDelimiters(new string[] { "," });
            //    csvReader.HasFieldsEnclosedInQuotes = true;
            //    string[] colFields = csvReader.ReadFields();



            //    foreach (string column in colFields)
            //    {
            //        DataColumn datecolumn = new DataColumn(column);
            //        datecolumn.AllowDBNull = true;
            //        csvDataTable.Columns.Add(datecolumn);
            //        csvDataTable.PrimaryKey = new DataColumn[] { csvDataTable.Columns["[general]"] };
            //    }
            //    while (!csvReader.EndOfData)
            //    {
            //        try
            //        {
            //            string[] fieldData = csvReader.ReadFields();
            //            if (fieldData.Length > 1)
            //            {
            //                fieldData = new string[] { string.Join(",", fieldData) };
            //                //fieldData[0] = string.Join(",", fieldData);
            //            }
            //            csvDataTable.Rows.Add(fieldData);

            //        }
            //        catch (Exception ex)
            //        {

            //            //throw;
            //        }

            //    }
            //}

            return deviceINIs;
        }

        private List<ADC> GetADC(int clusterId)
        {
            var mADCs = new List<ADC>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"ADC/ClusterId/{clusterId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mADCs = JsonConvert.DeserializeObject<List<ADC>>(jsonString);
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

            return mADCs;
        }


        #endregion

        #region Download Calibration PDF

        public ActionResult DownloadPannelTestPDF(int siteId)
        {
            var mAssetLister = new AssetLister();
            mAssetLister.Pager.Take = -1;// mAssetLister.Pager.PageSize;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    mAssetLister.SearchCriteria.CreatedBy = ClsHttpContent.LoginUser.Id;
                    mAssetLister.SearchCriteria.IsMobileView = true;
                    mAssetLister.SearchCriteria.SiteId = siteId;
                    //mAssetLister.SearchCriteria.AssetTypeId = 1;
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
                            var allAssetAttributes = GetAllAssetAttributes();

                            if (allAssetAttributes != null && allAssetAttributes.Count > 0)
                            {
                                string path = HttpContext.Server.MapPath("~/Template/PannelTest.html");
                                FileReader fileReader = new FileReader(path);
                                string pdfTemplate = fileReader.Read();
                                string tbodyData = string.Empty;
                                pdfTemplate = pdfTemplate.Replace("{%SiteName%}", mAssetLister.mAssets.Select(x => x.SiteName).FirstOrDefault());
                                pdfTemplate = pdfTemplate.Replace("{%DateTime%}", DateTime.Now.ToString());
                                pdfTemplate = pdfTemplate.Replace("{%tbodyPannelTest%}", BindPannelTest(siteId, mAssetLister, allAssetAttributes));

                                Byte[] res = null;
                                using (MemoryStream ms = new MemoryStream())
                                {
                                    var pdf = TheArtOfDev.HtmlRenderer.PdfSharp.PdfGenerator.GeneratePdf(pdfTemplate, PdfSharp.PageSize.A2, 5);
                                    pdf.Save(ms);
                                    res = ms.ToArray();
                                }

                                var responses = System.Web.HttpContext.Current.Response;
                                responses.BufferOutput = true;
                                responses.Clear();
                                responses.ClearHeaders();
                                responses.AddHeader("content-disposition", $"attachment;filename=PannelTest.pdf");
                                responses.ContentType = System.Net.Mime.MediaTypeNames.Application.Pdf;
                                responses.ContentEncoding = System.Text.Encoding.UTF8;
                                responses.BinaryWrite(res);
                                responses.End();

                            }
                        }

                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }

            return RedirectToAction("PLCConfig");
        }

        public ActionResult DownloadCalibrationPDF(int siteId, string date)
        {
            if (date.IsNullOrEmpty())
                date = DateTime.Now.ToString("d-M-yyyy");

            if (date.IsNotNullOrEmpty())
            {
                var selectedDate = ExtensionMethod.ConvertToDateTimeFormat(date.Replace("/", "-"), "d-M-yyyy");
                if (selectedDate != DateTime.MinValue && selectedDate.Date != DateTime.Now.Date)
                {
                    try
                    {
                        using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                        {
                            var response = hcf.client.GetAsync(String.Format($"Maintenance/DownloadCalibrationPDF/SiteId/{siteId}/Date/{date.Replace("/", "-")}")).Result;
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            if (response.StatusCode == HttpStatusCode.OK)
                            {
                                string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                                byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                                if (csvbytes != null && csvbytes.Length > 0)
                                {
                                    var mSite = siteService.Get(siteId);
                                    if (mSite != null && mSite.Id > 0)
                                        return File(csvbytes, "application/pdf", $"{mSite.Name}-{selectedDate.Date.ToString("dd-MM-yyyy")}.pdf");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                    }
                }
                else
                {
                    var mAssetLister = new AssetLister();
                    mAssetLister.Pager.Take = -1;// mAssetLister.Pager.PageSize;

                    try
                    {
                        using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                        {
                            mAssetLister.SearchCriteria.CreatedBy = ClsHttpContent.LoginUser.Id;
                            mAssetLister.SearchCriteria.IsMobileView = true;
                            mAssetLister.SearchCriteria.SiteId = siteId;
                            //mAssetLister.SearchCriteria.AssetTypeId = 1;
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
                                    var allAssetAttributes = GetAllAssetAttributes();
                                    var mDynamoTables = GetDynamoTable(siteId);
                                    var mAllDynamoTablesPM = GetAllGraphData(siteId, (int)E7FRSAdvance.Utility.Utility.AssetType.POINT_MACHINE);
                                    var mAllDynamoTablesSignal = GetAllGraphData(siteId, (int)E7FRSAdvance.Utility.Utility.AssetType.SIGNAL);
                                    var mAllDynamoTablesTrack = GetAllGraphData(siteId, (int)E7FRSAdvance.Utility.Utility.AssetType.TRACK);

                                    var mA10Calibrations = GetA10Calibration(siteId);
                                    var mYardConfigs = GetYardConfig(siteId);

                                    if (allAssetAttributes != null && allAssetAttributes.Count > 0 && mDynamoTables != null && mDynamoTables.Count > 0)
                                    {
                                        int redCount = 0;
                                        List<(int, string, string, string, string)> allError = new List<(int, string, string, string, string)>();
                                        string path = HttpContext.Server.MapPath("~/Template/Calibration.html");
                                        FileReader fileReader = new FileReader(path);
                                        string pdfTemplate = fileReader.Read();
                                        string tbodyData = string.Empty;
                                        string tbodyPMData = string.Empty;
                                        pdfTemplate = pdfTemplate.Replace("{%SiteName%}", mAssetLister.mAssets.Select(x => x.SiteName).FirstOrDefault());
                                        pdfTemplate = pdfTemplate.Replace("{%DateTime%}", DateTime.Now.ToString());
                                        foreach (var assetTypeId in mAssetLister.mAssets.GroupBy(x => x.AssetTypeId).Select(x => x.Key))
                                        {

                                            string stringValue = Enum.GetName(typeof(E7FRSAdvance.Utility.Utility.AssetType), assetTypeId).Replace("_", " ");

                                            List<Domain.AssetAttribute> mAssetAttributes = new List<Domain.AssetAttribute>();
                                            foreach (var mAsset in mAssetLister.mAssets.Where(x => x.AssetTypeId == assetTypeId).ToList())
                                            {
                                                foreach (var assetAttribute in mAsset.assetAttributes)
                                                {
                                                    if (mAssetAttributes.Where(x => x.Title == assetAttribute.Title).Count() == 0)
                                                    {
                                                        if (assetAttribute.Title.Contains("Last Update Relay End( sec ago)") ||
                                            assetAttribute.Title.Contains("Last Update Feed End( sec ago)") || assetAttribute.Title.Contains("Zero offset") || assetAttribute.Title.Contains("Last Updated")
                                            || assetAttribute.Title.Contains("Last Access (Sec)") || assetAttribute.Title == "Last Access"
                                            )
                                                        {
                                                        }
                                                        else if (assetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.SIGNAL && assetAttribute.Title.Contains(")"))
                                                        {

                                                        }
                                                        else
                                                        {
                                                            mAssetAttributes.Add(assetAttribute);
                                                        }

                                                    }
                                                }
                                            }

                                            if (mAssetAttributes != null && mAssetAttributes.Count > 20)
                                            {
                                                int count = 0;
                                                var length = mAssetAttributes.Count / 20;
                                                for (int i = 0; i <= length; i++)
                                                {
                                                    tbodyData += "<table class='tbl' style='background-color:white;color:black;margin-top:10px'>";
                                                    tbodyData += "<tr>";
                                                    tbodyData += $"<th>{stringValue}</th>";

                                                    foreach (var assetAttribute in mAssetAttributes.Skip(count).Take(20))
                                                    {
                                                        tbodyData += $"<th>{assetAttribute.Title}</th>";
                                                    }
                                                    tbodyData += "</tr>";

                                                    foreach (var mAsset in mAssetLister.mAssets.Where(x => x.AssetTypeId == assetTypeId).ToList())
                                                    {
                                                        var mDynamoTable = mDynamoTables.Where(x => x.AssetId == mAsset.Id).FirstOrDefault();
                                                        if (mDynamoTable != null)
                                                        {
                                                            tbodyData += "<tr>";
                                                            tbodyData += $"<td>{mAsset.Name}</td>";
                                                            assetAttributeService.PrepareGraphAttribute(mAsset.assetAttributes, allAssetAttributes.Where(x => x.AssetTypeId == assetTypeId).ToList(), mDynamoTable.CsvData);
                                                            foreach (var assetAttribute in mAssetAttributes.Skip(count).Take(20))
                                                            {
                                                                var assetInfo = mAsset.assetAttributes.Where(x => x.Id == assetAttribute.Id).FirstOrDefault();
                                                                if (assetInfo != null && assetInfo.Id > 0)
                                                                {
                                                                    if (assetInfo.Data != null && assetInfo.Data.Count > 0)
                                                                        tbodyData += $"<td>{assetInfo.Data.FirstOrDefault()} - {assetInfo.Multiplication}</td>";
                                                                    else
                                                                        tbodyData += $"<td>{assetInfo.Multiplication}</td>";

                                                                }
                                                                else
                                                                    tbodyData += $"<td>N/A</td>";

                                                            }
                                                            tbodyData += "</tr>";
                                                        }


                                                    }

                                                    tbodyData += "</table><br /><br />";
                                                    count = count + 20;
                                                }
                                            }
                                            else
                                            {
                                                if (assetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.POINT_MACHINE)
                                                {
                                                    foreach (var mAsset in mAssetLister.mAssets.Where(x => x.AssetTypeId == assetTypeId).ToList())
                                                    {
                                                        var mDynamoTable = mDynamoTables.Where(x => x.AssetId == mAsset.Id).FirstOrDefault();
                                                        if (mDynamoTable != null && mAllDynamoTablesPM != null && mAllDynamoTablesPM.Count > 0)
                                                        {
                                                            //SetPointMachineAttribute(mAsset.assetAttributes, mDynamoTable.CsvData);
                                                            assetAttributeService.PrepareGraphAttribute(mAsset.assetAttributes, allAssetAttributes.Where(x => x.AssetTypeId == assetTypeId).ToList(), mDynamoTable.CsvData);

                                                            string a10EN = string.Empty;
                                                            string a10ER = string.Empty;
                                                            string b10EN = string.Empty;
                                                            string b10ER = string.Empty;

                                                            var aEN = 0.0m;
                                                            var aER = 0.0m;
                                                            var bEN = 0.0m;
                                                            var bER = 0.0m;
                                                            var csvData = mAllDynamoTablesPM.Where(x => x.AssetId == mAsset.Id).OrderByDescending(x => x.TimeStamp).Select(x => x.CsvData).ToList();
                                                            foreach (var assetAttribute in mAssetAttributes)
                                                            {
                                                                var assetInfo = mAsset.assetAttributes.Where(x => x.Id == assetAttribute.Id).FirstOrDefault();
                                                                if (assetInfo != null && assetInfo.Id > 0)
                                                                {
                                                                    if (assetInfo.Data != null && assetInfo.Data.Count > 0)
                                                                    {
                                                                        var a10Calibration = keepingService.GetA10Multiplayer(mA10Calibrations, mYardConfigs, mAsset.Id, assetAttribute.Id);
                                                                        if (assetAttribute.Title == "A End - NWKR")
                                                                        {
                                                                            aEN = Convert.ToDecimal(assetInfo.Data.FirstOrDefault());
                                                                            if (aEN <= 4.5m)
                                                                            {

                                                                                var vale = GetPMAssetAttribute(allAssetAttributes.Where(x => x.AssetTypeId == assetTypeId).ToList(), csvData, assetAttribute.Id);
                                                                                if (vale > 0)
                                                                                {
                                                                                    aEN = vale;
                                                                                }
                                                                            }
                                                                            if (a10Calibration != null && a10Calibration.A10Multiplication.IsNotNullOrEmpty())
                                                                                a10EN = $"({a10Calibration.A10Multiplication})";

                                                                        }
                                                                        if (assetAttribute.Title == "A End - RWKR")
                                                                        {
                                                                            aER = Convert.ToDecimal(assetInfo.Data.FirstOrDefault());
                                                                            if (aER <= 4.5m)
                                                                            {
                                                                                var vale = GetPMAssetAttribute(allAssetAttributes.Where(x => x.AssetTypeId == assetTypeId).ToList(), csvData, assetAttribute.Id);
                                                                                if (vale > 0)
                                                                                {
                                                                                    aER = vale;
                                                                                }
                                                                            }
                                                                            if (a10Calibration != null && a10Calibration.A10Multiplication.IsNotNullOrEmpty())
                                                                                a10ER = $"({a10Calibration.A10Multiplication})";
                                                                        }
                                                                        if (assetAttribute.Title == "B End - NWKR")
                                                                        {
                                                                            bEN = Convert.ToDecimal(assetInfo.Data.FirstOrDefault());
                                                                            if (bEN <= 4.5m)
                                                                            {
                                                                                var vale = GetPMAssetAttribute(allAssetAttributes.Where(x => x.AssetTypeId == assetTypeId).ToList(), csvData, assetAttribute.Id);
                                                                                if (vale > 0)
                                                                                {
                                                                                    bEN = vale;
                                                                                }
                                                                            }
                                                                            if (a10Calibration != null && a10Calibration.A10Multiplication.IsNotNullOrEmpty())
                                                                                b10EN = $"({a10Calibration.A10Multiplication})";
                                                                        }
                                                                        if (assetAttribute.Title == "B End - RWKR")
                                                                        {
                                                                            bER = Convert.ToDecimal(assetInfo.Data.FirstOrDefault());
                                                                            if (bER <= 4.5m)
                                                                            {
                                                                                var vale = GetPMAssetAttribute(allAssetAttributes.Where(x => x.AssetTypeId == assetTypeId).ToList(), csvData, assetAttribute.Id);
                                                                                if (vale > 0)
                                                                                {
                                                                                    bER = vale;
                                                                                }
                                                                            }

                                                                            if (a10Calibration != null && a10Calibration.A10Multiplication.IsNotNullOrEmpty())
                                                                                b10ER = $"({a10Calibration.A10Multiplication})";
                                                                        }
                                                                    }

                                                                }
                                                            }

                                                            tbodyPMData += "<h5 style='color:black;'>" + mAsset.Name + "</h5>";

                                                            tbodyPMData += "<table class='tbl' style='background-color:white;color:black;margin-top:10px'>";
                                                            tbodyPMData += "<tr>";

                                                            tbodyPMData += $"<td colspan='8'>A 10 Multiplayer</td>";
                                                            tbodyPMData += "</tr>";
                                                            tbodyPMData += "<tr>";

                                                            foreach (var pointattribute in allAssetAttributes.Where(x => x.AssetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.POINT_MACHINE))
                                                            {
                                                                var a10Calibration = keepingService.GetA10Multiplayer(mA10Calibrations, mYardConfigs, mAsset.Id, pointattribute.Id);
                                                                if (pointattribute.Title == "A End - NW-V")
                                                                {
                                                                    if (a10Calibration != null && a10Calibration.A10Multiplication.IsNotNullOrEmpty())
                                                                        tbodyPMData += $"<td>A End - NW-V : {a10Calibration.A10Multiplication}</td>";
                                                                }
                                                                else if (pointattribute.Title == "A End - NW-C")
                                                                {
                                                                    if (a10Calibration != null && a10Calibration.A10Multiplication.IsNotNullOrEmpty())
                                                                        tbodyPMData += $"<td>A End - NW-C : {a10Calibration.A10Multiplication}</td>";
                                                                }
                                                                else if (pointattribute.Title == "A End - RW-V")
                                                                {
                                                                    if (a10Calibration != null && a10Calibration.A10Multiplication.IsNotNullOrEmpty())
                                                                        tbodyPMData += $"<td>A End - RW-V : {a10Calibration.A10Multiplication}</td>";
                                                                }
                                                                else if (pointattribute.Title == "A End - RW-C")
                                                                {
                                                                    if (a10Calibration != null && a10Calibration.A10Multiplication.IsNotNullOrEmpty())
                                                                        tbodyPMData += $"<td>A End - RW-C : {a10Calibration.A10Multiplication}</td>";
                                                                }
                                                                else if (pointattribute.Title == "B End - NW-V")
                                                                {
                                                                    if (a10Calibration != null && a10Calibration.A10Multiplication.IsNotNullOrEmpty())
                                                                        tbodyPMData += $"<td>B End - NW-V : {a10Calibration.A10Multiplication}</td>";
                                                                }
                                                                else if (pointattribute.Title == "B End - NW-C")
                                                                {
                                                                    if (a10Calibration != null && a10Calibration.A10Multiplication.IsNotNullOrEmpty())
                                                                        tbodyPMData += $"<td>B End - NW-C : {a10Calibration.A10Multiplication}</td>";
                                                                }
                                                                else if (pointattribute.Title == "B End - RW-V")
                                                                {
                                                                    if (a10Calibration != null && a10Calibration.A10Multiplication.IsNotNullOrEmpty())
                                                                        tbodyPMData += $"<td>B End - RW-V : {a10Calibration.A10Multiplication}</td>";
                                                                }
                                                                else if (pointattribute.Title == "B End - RW-C")
                                                                {
                                                                    if (a10Calibration != null && a10Calibration.A10Multiplication.IsNotNullOrEmpty())
                                                                        tbodyPMData += $"<td>B End - RW-C : {a10Calibration.A10Multiplication}</td>";
                                                                }
                                                            }


                                                            tbodyPMData += "</tr>";
                                                            tbodyPMData += "</table>";

                                                            tbodyPMData += "<table class='tbl' style='background-color:white;color:black;margin-top:10px'>";
                                                            tbodyPMData += "<tr>";

                                                            //Bind A Normal
                                                            if (aEN > 4.5m)
                                                            {
                                                                tbodyPMData += "<td>";
                                                                tbodyPMData += "<div class='row'><div class='col-md-6'> <span class='badge  badge-success'>A POINT IN NORMAL</span></div>";
                                                                if (aEN < 20 && aEN > 34)
                                                                {
                                                                    tbodyPMData += "<div class='col-md-6'><span style='background-color:#FF6666;color:white;'>" + aEN + " V</span><span>" + a10EN + "</span></div></div>";
                                                                    redCount++;
                                                                    allError.Add((redCount, mAsset.AssetTypeName, mAsset.Name, "A Normal", "The current between 20 to 34"));

                                                                }
                                                                else
                                                                    tbodyPMData += "<div class='col-md-6'><span>" + aEN + " V</span><span>" + a10EN + "</span></div></div>";

                                                                tbodyPMData += "</td>";
                                                            }
                                                            else
                                                            {
                                                                tbodyPMData += "<td>";
                                                                tbodyPMData += "<div class='row'><div class='col-md-6'> <span class='badge  badge-success'>A POINT IN NORMAL</span></div>";
                                                                tbodyPMData += "<div class='col-md-6'><span style='background-color:#FF6666;color:white;'>A normal is missing</span></div></div>";
                                                                tbodyPMData += "</td>";
                                                                redCount++;
                                                                allError.Add((redCount, mAsset.AssetTypeName, mAsset.Name, "A normal", "A normal is missing"));
                                                            }

                                                            //Bind A Reverce
                                                            if (aER > 4.5m)
                                                            {
                                                                tbodyPMData += "<td>";
                                                                tbodyPMData += "<div class='row'><div class='col-md-6'><span class='badge  badge-warning'>A POINT IN REVERSE</span></div>";

                                                                if (aER < 20 && aER > 34)
                                                                {
                                                                    tbodyPMData += "<div class='col-md-6'><span style='background-color:#FF6666;color:white;'>" + aER + " V</span><span>" + a10ER + "</span></div></div>";
                                                                    redCount++;
                                                                    allError.Add((redCount, mAsset.AssetTypeName, mAsset.Name, "A Reverce", "The current between 20 to 34"));
                                                                }
                                                                else
                                                                    tbodyPMData += "<div class='col-md-6'><span>" + aER + " V</span><span>" + a10ER + "</span></div></div>";

                                                                tbodyPMData += "</td>";
                                                            }
                                                            else
                                                            {
                                                                tbodyPMData += "<td>";
                                                                tbodyPMData += "<div class='row'><div class='col-md-6'><span class='badge  badge-warning'>A POINT IN REVERSE</span></div>";
                                                                tbodyPMData += "<div class='col-md-6'><span style='background-color:#FF6666;color:white;'>A Reverce is missing</span></div></div>";
                                                                tbodyPMData += "</td>";
                                                                redCount++;
                                                                allError.Add((redCount, mAsset.AssetTypeName, mAsset.Name, "A Reverce", "A Reverce is missing"));
                                                            }

                                                            //Bind B Normal
                                                            if (bEN > 4.5m)
                                                            {
                                                                tbodyPMData += "<td>";
                                                                tbodyPMData += "<div class='row'><div class='col-md-6'><span class='badge  badge-success'>B POINT IN NORMAL</span></div>";

                                                                if (bEN < 20 && bEN > 34)
                                                                {
                                                                    tbodyPMData += "<div class='col-md-6'><span background-color:#FF6666;color:white;'>" + bEN + " V</span><span>" + b10EN + "</span></div></div>";
                                                                    redCount++;
                                                                    allError.Add((redCount, mAsset.AssetTypeName, mAsset.Name, "B Normal", "The current between 20 to 34"));
                                                                }
                                                                else
                                                                    tbodyPMData += "<div class='col-md-6'><span>" + bEN + " V</span><span>" + b10EN + "</span></div></div>";

                                                                tbodyPMData += "</td>";
                                                            }
                                                            else
                                                            {
                                                                if (mAsset.IsHalfPointMachine == null || (mAsset.IsHalfPointMachine != null && !mAsset.IsHalfPointMachine.Value))
                                                                {
                                                                    tbodyPMData += "<td>";
                                                                    tbodyPMData += "<div class='row'><div class='col-md-6'><span class='badge  badge-success'>B POINT IN NORMAL</span></div>";
                                                                    tbodyPMData += "<div class='col-md-6'><span style='background-color:#FF6666;color:white;'>B Nomal is missing</span></div></div>";
                                                                    tbodyPMData += "</td>";
                                                                    redCount++;
                                                                    allError.Add((redCount, mAsset.AssetTypeName, mAsset.Name, "B Nomal", "B Nomal is missing"));
                                                                }

                                                            }

                                                            //Bind B Reverce
                                                            if (bER > 4.5m)
                                                            {
                                                                tbodyPMData += "<td>";
                                                                tbodyPMData += "<div class='row'><div class='col-md-6'><span class='badge  badge-warning'>B POINT IN REVERSE</span></div>";
                                                                if (bER < 20 && bER > 34)
                                                                {
                                                                    tbodyPMData += "<div class='col-md-6'><span background-color:#FF6666;color:white;'>" + bER + " V</span><span>" + b10ER + "</span></div></div>";
                                                                    redCount++;
                                                                    allError.Add((redCount, mAsset.AssetTypeName, mAsset.Name, "B Reverce", "The current between 20 to 34"));
                                                                }
                                                                else
                                                                    tbodyPMData += "<div class='col-md-6'><span>" + bER + " V</span><span>" + b10ER + "</span></div></div>";

                                                                tbodyPMData += "</td>";
                                                            }
                                                            else
                                                            {
                                                                if (mAsset.IsHalfPointMachine == null || (mAsset.IsHalfPointMachine != null && !mAsset.IsHalfPointMachine.Value))
                                                                {
                                                                    tbodyPMData += "<td>";
                                                                    tbodyPMData += "<div class='row'><div class='col-md-6'><span class='badge  badge-warning'>B POINT IN REVERSE</span></div>";
                                                                    tbodyPMData += "<div class='col-md-6'><span style='background-color:#FF6666;color:white;'>B Reverce is missing</span></div></div>";
                                                                    tbodyPMData += "</td>";
                                                                    redCount++;
                                                                    allError.Add((redCount, mAsset.AssetTypeName, mAsset.Name, "B Reverce", "B Reverce is missing"));
                                                                }

                                                            }

                                                            tbodyPMData += "<tr>";
                                                            tbodyPMData += "</table>";

                                                            tbodyPMData += "<table class='tbl' style='background-color:white;color:black;'>";
                                                            //tbodyPMData += "<tr></tr>";
                                                            tbodyPMData += "<tr>";
                                                            tbodyPMData += "<th>Date</th><th>Name</th><th>Direction</th><th>A Current(Max/Avg) </th><th>A Voltage</th><th>A Time (ms)</th>";
                                                            if (mAsset.IsHalfPointMachine == null || (mAsset.IsHalfPointMachine != null && !mAsset.IsHalfPointMachine.Value))
                                                            {
                                                                tbodyPMData += "<th>B Current(Max/Avg) </th><th>B Voltage</th><th>B Time (ms)</th>";
                                                            }
                                                            tbodyPMData += "</tr>";

                                                            bool aCurrentValid = false;
                                                            bool aVoltageValid = false;
                                                            bool aTimeValid = false;
                                                            bool bCurrentValid = false;
                                                            bool bVoltageValid = false;
                                                            bool bTimeValid = false;
                                                            foreach (var pointMachineData in mAsset.mPointMachineData)
                                                            {
                                                                tbodyPMData += "<tr>";

                                                                tbodyPMData += $"<td>{pointMachineData.Date}</td><td>{pointMachineData.AssetType}</td><td>{pointMachineData.Direction} Operation</td>";

                                                                if (Convert.ToDouble(pointMachineData.PointMachineJson.A_C_MAX) > 6 || pointMachineData.PointMachineJson.A_C_AVERAGE < 1)
                                                                {
                                                                    aCurrentValid = true;
                                                                    tbodyPMData += $"<td style='background-color:#FF6666;color:white;'>{string.Format("{0:0.00}", pointMachineData.PointMachineJson.A_C_MAX)} / {string.Format("{0:0.00}", pointMachineData.PointMachineJson.A_C_AVERAGE)}</td>";
                                                                }
                                                                else
                                                                {
                                                                    tbodyPMData += $"<td>{string.Format("{0:0.00}", pointMachineData.PointMachineJson.A_C_MAX)} / {string.Format("{0:0.00}", pointMachineData.PointMachineJson.A_C_AVERAGE)}</td>";
                                                                }

                                                                if (pointMachineData.PointMachineJson.A_V_AVERAGE < 100)
                                                                {
                                                                    aVoltageValid = true;
                                                                    tbodyPMData += $"<td style='background-color:#FF6666;color:white;'>{pointMachineData.PointMachineJson.A_V_AVERAGE}</td>";
                                                                }
                                                                else
                                                                {
                                                                    tbodyPMData += $"<td>{pointMachineData.PointMachineJson.A_V_AVERAGE}</td>";
                                                                }

                                                                if (pointMachineData.PointMachineJson.A_C_TIME < 1500 || pointMachineData.PointMachineJson.A_C_TIME > 6000)
                                                                {
                                                                    aTimeValid = true;
                                                                    tbodyPMData += $"<td style='background-color:#FF6666;color:white;'>{pointMachineData.PointMachineJson.A_C_TIME}</td>";
                                                                }
                                                                else
                                                                {
                                                                    tbodyPMData += $"<td>{pointMachineData.PointMachineJson.A_C_TIME}</td>";
                                                                }

                                                                if (mAsset.IsHalfPointMachine == null || (mAsset.IsHalfPointMachine != null && !mAsset.IsHalfPointMachine.Value))
                                                                {
                                                                    if (Convert.ToDouble(pointMachineData.PointMachineJson.B_C_MAX) > 6 || pointMachineData.PointMachineJson.B_C_AVERAGE < 1)
                                                                    {
                                                                        bCurrentValid = true;
                                                                        tbodyPMData += $"<td style='background-color:#FF6666;color:white;'>{string.Format("{0:0.00}", pointMachineData.PointMachineJson.B_C_MAX)} / {string.Format("{0:0.00}", pointMachineData.PointMachineJson.B_C_AVERAGE)}</td>";
                                                                    }
                                                                    else
                                                                    {
                                                                        tbodyPMData += $"<td>{string.Format("{0:0.00}", pointMachineData.PointMachineJson.B_C_MAX)} / {string.Format("{0:0.00}", pointMachineData.PointMachineJson.B_C_AVERAGE)}</td>";
                                                                    }


                                                                    if (pointMachineData.PointMachineJson.B_V_AVERAGE < 100)
                                                                    {
                                                                        bVoltageValid = true;
                                                                        tbodyPMData += $"<td style='background-color:#FF6666;color:white;'>{pointMachineData.PointMachineJson.B_V_AVERAGE}</td>";
                                                                    }
                                                                    else
                                                                        tbodyPMData += $"<td>{pointMachineData.PointMachineJson.B_V_AVERAGE}</td>";

                                                                    if (pointMachineData.PointMachineJson.B_C_TIME < 1500 || pointMachineData.PointMachineJson.B_C_TIME > 6000)
                                                                    {
                                                                        bTimeValid = true;
                                                                        tbodyPMData += $"<td style='background-color:#FF6666;color:white;'>{pointMachineData.PointMachineJson.B_C_TIME}</td>";
                                                                    }
                                                                    else
                                                                    {
                                                                        tbodyPMData += $"<td>{pointMachineData.PointMachineJson.B_C_TIME}</td>";
                                                                    }
                                                                }
                                                                tbodyPMData += "</tr>";
                                                            }

                                                            if (aCurrentValid)
                                                            {
                                                                allError.Add((redCount, mAsset.AssetTypeName, mAsset.Name, "A Current(Max/Avg)", "A Current Max less than 6 or Avg greater than 1"));
                                                                redCount++;
                                                            }

                                                            if (aVoltageValid)
                                                            {
                                                                allError.Add((redCount, mAsset.AssetTypeName, mAsset.Name, "A Voltage", "A Voltage greater than 100"));
                                                                redCount++;
                                                            }

                                                            if (aTimeValid)
                                                            {
                                                                allError.Add((redCount, mAsset.AssetTypeName, mAsset.Name, "A Time (ms)", "A Time less than 6000 or greater than 1500"));
                                                                redCount++;
                                                            }


                                                            if (mAsset.IsHalfPointMachine == null || (mAsset.IsHalfPointMachine != null && !mAsset.IsHalfPointMachine.Value))
                                                            {

                                                                if (bCurrentValid)
                                                                {
                                                                    allError.Add((redCount, mAsset.AssetTypeName, mAsset.Name, "B Current(Max/Avg)", "B Current Max less than 6 or Avg greater than 1"));
                                                                    redCount++;
                                                                }

                                                                if (bVoltageValid)
                                                                {
                                                                    allError.Add((redCount, mAsset.AssetTypeName, mAsset.Name, "B Voltage", "B Voltage greater than 100"));
                                                                    redCount++;
                                                                }

                                                                if (bTimeValid)
                                                                {
                                                                    allError.Add((redCount, mAsset.AssetTypeName, mAsset.Name, "B Time (ms)", "B Time less than 6000 or greater than 1500"));
                                                                    redCount++;
                                                                }
                                                            }


                                                            tbodyPMData += "</table><br /><br />";
                                                        }


                                                    }
                                                }
                                                else
                                                {
                                                    tbodyData += "<table class='tbl' style='background-color:white;color:black;margin-top:10px'>";
                                                    tbodyData += "<tr>";
                                                    tbodyData += $"<th>{stringValue}</th>";

                                                    if (assetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.TRACK)
                                                    {
                                                        var leakage = new Domain.AssetAttribute();
                                                        leakage.AssetTypeId = (int)E7FRSAdvance.Utility.Utility.AssetType.TRACK;
                                                        leakage.Title = "Leakage";
                                                        mAssetAttributes.Add(leakage);
                                                        allAssetAttributes.Add(leakage);
                                                    }
                                                    foreach (var assetAttribute in mAssetAttributes)
                                                    {
                                                        if (assetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.Device_Time)
                                                            tbodyData += $"<th>{assetAttribute.Title} (RT)</th>";
                                                        else
                                                            tbodyData += $"<th>{assetAttribute.Title} (RT - MV)</th>";

                                                    }
                                                    tbodyData += "</tr>";

                                                    foreach (var mAsset in mAssetLister.mAssets.Where(x => x.AssetTypeId == assetTypeId).ToList())
                                                    {
                                                        bool isTprChange = false;

                                                        if (assetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.TRACK && mAsset.assetAttributes != null)
                                                        {
                                                            var leakage = new Domain.AssetAttribute();
                                                            leakage.AssetTypeId = (int)E7FRSAdvance.Utility.Utility.AssetType.TRACK;
                                                            leakage.Title = "Leakage";
                                                            mAsset.assetAttributes.Add(leakage);
                                                        }

                                                        var mDynamoTable = mDynamoTables.Where(x => x.AssetId == mAsset.Id).FirstOrDefault();
                                                        if (mDynamoTable != null)
                                                        {

                                                            if (mAsset.assetAttributes != null && mAsset.assetAttributes.Count > 0)
                                                            {
                                                                var attributeIds = mAssetAttributes.Select(x => x.Id).ToList();
                                                                mAsset.assetAttributes = mAsset.assetAttributes.Where(x => attributeIds.Contains(x.Id)).ToList();
                                                            }
                                                            assetAttributeService.PrepareGraphAttribute(mAsset.assetAttributes, allAssetAttributes.Where(x => x.AssetTypeId == assetTypeId).ToList(), mDynamoTable.CsvData);

                                                            #region Set TPR

                                                            if (assetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.TRACK)
                                                            {

                                                                var tprAttribute = mAsset.assetAttributes.Where(x => x.Title.ToUpper().Contains("TPR")).FirstOrDefault();
                                                                if (tprAttribute != null && tprAttribute.Id > 0)
                                                                {
                                                                    if (tprAttribute.Data != null && Convert.ToDecimal(tprAttribute.Data.FirstOrDefault()) < 1)
                                                                    {
                                                                        var csvData = mAllDynamoTablesTrack.Where(x => x.AssetId == mAsset.Id).OrderByDescending(x => x.TimeStamp).Select(x => x.CsvData).ToList();

                                                                        var tprAttr = GetTPRAssetAttribute(allAssetAttributes.Where(x => x.AssetTypeId == assetTypeId).ToList(), csvData, tprAttribute.Id);
                                                                        if (tprAttr != null && tprAttr.Count > 0)
                                                                        {
                                                                            foreach (var attribute in mAsset.assetAttributes)
                                                                            {
                                                                                var newAttribute = tprAttr.Where(x => x.Id == attribute.Id).FirstOrDefault();
                                                                                if (newAttribute != null && newAttribute.Id > 0 && newAttribute.Data != null)
                                                                                {
                                                                                    attribute.Data = new List<string>();
                                                                                    attribute.Data.AddRange(newAttribute.Data);
                                                                                }

                                                                            }
                                                                            isTprChange = true;
                                                                        }
                                                                    }

                                                                }
                                                            }
                                                            #endregion
                                                            tbodyData += "<tr>";
                                                            if (isTprChange)
                                                                tbodyData += $"<td style='background-color:#FFBC44;color:white;'>{mAsset.Name}</td>";
                                                            else
                                                                tbodyData += $"<td>{mAsset.Name}</td>";

                                                            foreach (var assetAttribute in mAssetAttributes)
                                                            {
                                                                var assetInfo = mAsset.assetAttributes.Where(x => x.Id == assetAttribute.Id).FirstOrDefault();
                                                                if (assetInfo != null && assetInfo.Id > 0)
                                                                {
                                                                    if (assetInfo.Data != null && assetInfo.Data.Count > 0)
                                                                    {

                                                                        if (assetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.SIGNAL && assetInfo.Data != null && Convert.ToDecimal(assetInfo.Data.FirstOrDefault()) < 50)
                                                                        {
                                                                            var csvData = mAllDynamoTablesSignal.Where(x => x.AssetId == mAsset.Id).OrderByDescending(x => x.TimeStamp).Select(x => x.CsvData).ToList();
                                                                            var vale = GetSignalAssetAttribute(allAssetAttributes.Where(x => x.AssetTypeId == assetTypeId).ToList(), csvData, assetAttribute.Id);
                                                                            if (vale > 0)
                                                                            {
                                                                                assetInfo.Data = new List<string>();
                                                                                assetInfo.Data.Add(Convert.ToString(vale));
                                                                            }

                                                                        }

                                                                        if (assetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.Device_Time)
                                                                        {
                                                                            if (assetAttribute.Title == "Last Access time")
                                                                            {
                                                                                if (CheckHighlight(assetInfo, mAsset))
                                                                                {
                                                                                    tbodyData += $"<td>{assetInfo.Data.FirstOrDefault()}</td>";
                                                                                }
                                                                                else
                                                                                {
                                                                                    redCount++;
                                                                                    allError.Add((redCount, mAsset.AssetTypeName, mAsset.Name, assetAttribute.Title, assetInfo.Data.FirstOrDefault()));
                                                                                    tbodyData += $"<td style='background-color:#FF6666;color:white;'>{assetInfo.Data.FirstOrDefault()}</td>";
                                                                                }
                                                                            }
                                                                            else
                                                                                tbodyData += $"<td>{assetInfo.AssetInfoValue}</td>";


                                                                        }
                                                                        else
                                                                        {

                                                                            var a10Calibration = keepingService.GetA10Multiplayer(mA10Calibrations, mYardConfigs, mAsset.Id, assetAttribute.Id);
                                                                            if (!CheckHighlightA10Multiplayer(a10Calibration, assetTypeId))
                                                                            {
                                                                                if (a10Calibration != null && a10Calibration.A10Multiplication.IsNotNullOrEmpty())
                                                                                {
                                                                                    tbodyData += $"<td style='background-color:#00A4E0;color:white;'>{assetInfo.Data.FirstOrDefault()} - {assetInfo.Multiplication}({a10Calibration.A10Multiplication})</td>";
                                                                                }
                                                                                else
                                                                                {
                                                                                    tbodyData += $"<td style='background-color:#00A4E0;color:white;'>{assetInfo.Data.FirstOrDefault()} - {assetInfo.Multiplication}</td>";
                                                                                }
                                                                            }
                                                                            else if (CheckHighlight(assetInfo, mAsset))
                                                                            {
                                                                                if (a10Calibration != null && a10Calibration.A10Multiplication.IsNotNullOrEmpty())
                                                                                {
                                                                                    tbodyData += $"<td>{assetInfo.Data.FirstOrDefault()} - {assetInfo.Multiplication}({a10Calibration.A10Multiplication})</td>";
                                                                                }
                                                                                else
                                                                                {
                                                                                    tbodyData += $"<td>{assetInfo.Data.FirstOrDefault()} - {assetInfo.Multiplication}</td>";
                                                                                }

                                                                            }
                                                                            else
                                                                            {
                                                                                redCount++;
                                                                                allError.Add((redCount, mAsset.AssetTypeName, mAsset.Name, assetAttribute.Title, $"{assetInfo.Data.FirstOrDefault()}"));
                                                                                if (a10Calibration != null && a10Calibration.A10Multiplication.IsNotNullOrEmpty())
                                                                                {
                                                                                    tbodyData += $"<td style='background-color:#FF6666;color:white;'>{assetInfo.Data.FirstOrDefault()} - {assetInfo.Multiplication}({a10Calibration.A10Multiplication})</td>";
                                                                                }
                                                                                else
                                                                                {
                                                                                    tbodyData += $"<td style='background-color:#FF6666;color:white;'>{assetInfo.Data.FirstOrDefault()} - {assetInfo.Multiplication}</td>";
                                                                                }

                                                                            }

                                                                        }
                                                                    }
                                                                    else
                                                                        tbodyData += $"<td>{assetInfo.Multiplication}</td>";

                                                                }
                                                                else if (assetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.TRACK && assetAttribute.Title == "Leakage" && assetInfo.Data != null && assetInfo.Data.Count > 0)
                                                                {
                                                                    var leakageData = assetInfo.Data.FirstOrDefault();
                                                                    if (Convert.ToDecimal(leakageData) <= 150)
                                                                    {
                                                                        tbodyData += $"<td>{assetInfo.Data.FirstOrDefault()}</td>";
                                                                    }
                                                                    else
                                                                    {
                                                                        redCount++;
                                                                        allError.Add((redCount, mAsset.AssetTypeName, mAsset.Name, assetAttribute.Title, $"{leakageData}"));

                                                                        tbodyData += $"<td style='background-color:#FF6666;color:white;'>{leakageData}</td>";
                                                                    }


                                                                }
                                                                else
                                                                    tbodyData += $"<td>N/A</td>";

                                                            }
                                                            tbodyData += "</tr>";
                                                        }


                                                    }
                                                    tbodyData += "</table><br /><br />";
                                                }

                                            }


                                        }
                                        pdfTemplate = pdfTemplate.Replace("{%tbodyData%}", tbodyData);
                                        pdfTemplate = pdfTemplate.Replace("{%tbodyAlert%}", PrepareAlert(mAssetLister));
                                        pdfTemplate = pdfTemplate.Replace("{%tbodyErrorLog%}", PrepareError(allError));
                                        pdfTemplate = pdfTemplate.Replace("{%tbodyPointData%}", tbodyPMData);

                                        Byte[] res = null;
                                        //using (MemoryStream ms = new MemoryStream())
                                        //{
                                        //    var pdf = TheArtOfDev.HtmlRenderer.PdfSharp.PdfGenerator.GeneratePdf(pdfTemplate, PdfSharp.PageSize.A2, 5);
                                        //    pdf.Save(ms);
                                        //    res = ms.ToArray();
                                        //}
                                        HtmlToPdfConverter htmlToPdf = new NReco.PdfGenerator.HtmlToPdfConverter();
                                        htmlToPdf.Margins.Top = 0;
                                        htmlToPdf.Margins.Bottom = 0;
                                        htmlToPdf.Margins.Left = 0;
                                        htmlToPdf.Margins.Right = 0;
                                        htmlToPdf.Size = NReco.PdfGenerator.PageSize.A3;
                                        res = htmlToPdf.GeneratePdf(pdfTemplate);

                                        var responses = System.Web.HttpContext.Current.Response;
                                        responses.BufferOutput = true;
                                        responses.Clear();
                                        responses.ClearHeaders();
                                        responses.AddHeader("content-disposition", $"attachment;filename={mAssetLister.mAssets.Select(x => x.SiteName).FirstOrDefault()}({redCount})-{DateTime.Now.ToString("dd-MM-yyyy h-mm tt")}.pdf");
                                        responses.ContentType = System.Net.Mime.MediaTypeNames.Application.Pdf;
                                        responses.ContentEncoding = System.Text.Encoding.UTF8;
                                        responses.BinaryWrite(res);
                                        responses.End();

                                    }
                                }

                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ViewBag.Error = ex.Message.ToString();
                    }
                }
            }

            return RedirectToAction("PDF");
        }

        public ActionResult DeleteSMSLogById(int id)
        {
            bool result = false;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("SMSLog/DeleteSmsLog/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        result = JsonConvert.DeserializeObject<bool>(jsonString);
                    }
                    else
                    {
                        result = false;
                    }
                }
            }
            catch (Exception ex)
            {

            }
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        private List<MultipleLog> GetDynamoTable(int siteId)
        {
            List<MultipleLog> mDynamoTables = new List<MultipleLog>();
            if (siteId > 0)
            {
                try
                {
                    using (var mHttpClientFactory = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = mHttpClientFactory.client.GetAsync(String.Format("Asset/GetGraphData/{0}", siteId)).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            mDynamoTables = JsonConvert.DeserializeObject<List<MultipleLog>>(jsonString);
                        }

                    }
                }
                catch (Exception ex)
                {
                }
            }

            return mDynamoTables;
        }

        private List<MultipleLog> GetAllGraphData(int siteId, int assetTypeId)
        {
            List<MultipleLog> mDynamoTables = new List<MultipleLog>();
            if (siteId > 0)
            {
                try
                {
                    using (var mHttpClientFactory = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = mHttpClientFactory.client.GetAsync(String.Format("Asset/GetAllGraphData/{0}/{1}", siteId, assetTypeId)).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            mDynamoTables = JsonConvert.DeserializeObject<List<MultipleLog>>(jsonString);
                        }

                    }
                }
                catch (Exception ex)
                {
                }
            }

            return mDynamoTables;
        }

        private decimal GetSignalAssetAttribute(List<AssetAttribute> allAssetAttributes, List<string> vals, int attributeId)
        {
            decimal value = 0.0m;
            foreach (var csvData in vals)
            {
                try
                {
                    List<AssetAttribute> attributes = new List<AssetAttribute>();
                    var array = csvData.Split('~');
                    int i = 3;
                    foreach (var item in allAssetAttributes)
                    {
                        var at = new AssetAttribute();
                        at.Title = item.Title;
                        at.Id = item.Id;
                        if (array.Length > i && !string.IsNullOrEmpty(array[i]))
                        {
                            at.Data.Add(Convert.ToString(array[i]));
                        }
                        else
                        {
                            at.Data.Add("0");
                        }
                        i++;

                        attributes.Add(at);
                    }

                    var sds = attributes.Where(x => x.Id == attributeId).FirstOrDefault();
                    if (sds != null)
                    {
                        var dsd = sds.Data.FirstOrDefault();
                        if (Convert.ToDecimal(dsd) > 50)
                        {
                            return Convert.ToDecimal(dsd);

                        }
                    }
                }
                catch (Exception)
                {

                }

            }
            return value;

        }

        private decimal GetPMAssetAttribute(List<AssetAttribute> assetAttributes, List<string> vals, int attributeId)
        {
            decimal value = 0.0m;
            foreach (var csvData in vals)
            {
                try
                {
                    List<AssetAttribute> attributes = new List<AssetAttribute>();
                    var array = csvData.Split('~');
                    int i = 3;
                    decimal ifma = 0;
                    decimal irma = 0;

                    foreach (var item in assetAttributes)
                    {
                        var at = new AssetAttribute();
                        at.Title = item.Title;
                        at.Id = item.Id;
                        if (array.Length > i && !string.IsNullOrEmpty(array[i]))
                        {
                            at.Data.Add(Convert.ToString(array[i]));
                        }
                        else
                        {
                            at.Data.Add("0");
                        }
                        i++;

                        attributes.Add(at);
                    }

                    var sds = attributes.Where(x => x.Id == attributeId).FirstOrDefault();
                    if (sds != null)
                    {
                        var dsd = sds.Data.FirstOrDefault();
                        if (Convert.ToDecimal(dsd) > 4.5m)
                        {
                            return Convert.ToDecimal(dsd);

                        }
                    }
                }
                catch (Exception)
                {

                }



            }
            return value;

        }

        private List<AssetAttribute> GetTPRAssetAttribute(List<AssetAttribute> assetAttributes, List<string> vals, int attributeId)
        {
            int counter = 0;
            foreach (var csvData in vals)
            {
                try
                {
                    List<AssetAttribute> attributes = new List<AssetAttribute>();
                    var array = csvData.Split('~');
                    int i = 3;
                    decimal ifma = 0;
                    decimal irma = 0;

                    foreach (var item in assetAttributes)
                    {
                        var at = new AssetAttribute();
                        at.Title = item.Title;
                        at.Id = item.Id;
                        if (array.Length > i && !string.IsNullOrEmpty(array[i]))
                        {
                            at.Data.Add(Convert.ToString(array[i]));
                        }
                        else
                        {
                            at.Data.Add("0");
                        }
                        i++;

                        attributes.Add(at);
                    }

                    var sds = attributes.Where(x => x.Id == attributeId).FirstOrDefault();
                    if (sds != null)
                    {
                        var dsd = sds.Data.FirstOrDefault();
                        if (Convert.ToDecimal(dsd) > 5m)
                        {
                            counter++;
                            if (counter > 2)
                                return attributes;

                        }
                    }
                }
                catch (Exception)
                {

                }


            }
            return new List<AssetAttribute>();

        }

        private bool CheckHighlight(AssetAttribute mAssetAttribute, Asset mAsset)
        {
            bool isValid = true;
            if (mAssetAttribute != null && mAsset != null)
            {
                decimal value = 0;

                if (mAssetAttribute.Data != null && mAssetAttribute.Data.Count > 0)
                {
                    value = Convert.ToDecimal(mAssetAttribute.Data.FirstOrDefault());

                }
                if (mAssetAttribute.AssetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.Device_Time)
                {
                    if (mAssetAttribute.Title.Contains("Last Access time"))
                    {
                        if (value == -1 || value > 60)
                            isValid = false;

                    }
                }
                if (mAssetAttribute.AssetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.TRACK)
                {
                    if (mAssetAttribute.Title.Contains("If"))
                    {
                        if (value < 150 || value > 400)
                            isValid = false;

                    }
                    else if (mAssetAttribute.Title.Contains("Ir"))
                    {
                        if (value < 150 || value > 400)
                            isValid = false;
                    }
                    else if (mAssetAttribute.Title.Contains("Vr"))
                    {
                        if (value < 2 || value > 4.2m)
                            isValid = false;
                    }
                    else if (mAssetAttribute.Title.Contains("TPR"))
                    {
                        if (value < 18 || value > 32)
                            isValid = false;
                    }
                    else if (mAssetAttribute.Title.Contains("Charger"))
                    {
                        if (value < 100 || value > 2000)
                            isValid = false;
                    }
                    else if (mAssetAttribute.Title.Contains("Vf"))
                    {
                        if (value < 2 || value > 6)
                            isValid = false;
                    }
                    else if (mAssetAttribute.Title.Contains("Choke"))
                    {
                        if (value < 0.5m || value > 2)
                            isValid = false;
                    }

                }
                if (mAssetAttribute.AssetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.SIGNAL)
                {
                    if (mAsset.Name.ToUpper().StartsWith("SH"))
                    {
                        if (value < 50)
                            isValid = false;
                    }
                    else
                    {
                        if (mAssetAttribute.Title.Contains("mA"))
                        {
                            if (value < 105 || value > 150)
                                isValid = false;
                        }
                        else if (mAssetAttribute.Title.Contains("V"))
                        {
                            if (value < 105 || value > 150)
                                isValid = false;
                        }
                    }

                }
                if (mAssetAttribute.AssetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.Device_Time)
                {
                    if (mAssetAttribute.Title.Contains("Last Access time"))
                    {
                        if (value > 100)
                            isValid = false;
                    }
                }


            }

            return isValid;
        }

        private string BindPannelTest(int siteId, AssetLister mAssetLister, List<AssetAttribute> allAssetAttributes)
        {
            string tbodyData = string.Empty;
            tbodyData += "<table class='tbl' style='background-color:white;color:black;margin-top:10px'>";
            tbodyData += "<tr>";
            tbodyData += $"<th>TimeStamp</th>";
            tbodyData += $"<th>Asset</th>";
            tbodyData += $"<th>If mA</th>";
            tbodyData += $"<th>Ir mA</th>";
            tbodyData += $"<th>Vr</th>";
            tbodyData += $"<th>TPR V</th>";
            tbodyData += $"<th>Choke V</th>";
            tbodyData += $"<th>Vf</th>";
            tbodyData += "</tr>";
            var trackAssetAttributes = allAssetAttributes.Where(x => x.AssetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.TRACK).ToList();
            var mDynamoTablesTrack = GetAllGraphData(new Domain.SearchCriteria() { SiteId = siteId, AssetTypeId = (int)E7FRSAdvance.Utility.Utility.AssetType.TRACK, StartDate = DateTime.Now.AddDays(-1), EndDate = DateTime.Now.AddDays(-1) });
            if (mDynamoTablesTrack != null && mDynamoTablesTrack.Count > 0 && mAssetLister != null && mAssetLister.mAssets != null)
            {
                foreach (var mAsset in mAssetLister.mAssets.Where(x => x.AssetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.TRACK).ToList())
                {
                    var mDynamoTablesTracks = mDynamoTablesTrack.Where(x => x.AssetId == mAsset.Id).OrderBy(x => x.TimeStamp).ToList();
                    if (mDynamoTablesTracks != null && mDynamoTablesTracks.Count > 0)
                    {
                        int counter = 0;
                        foreach (var mDynamoTable in mDynamoTablesTracks)
                        {
                            var mAssetAttribute = assetAttributeService.GetGraphAttribute(mAsset.assetAttributes, trackAssetAttributes, mDynamoTable.CsvData);
                            if (mAssetAttribute != null && mAssetAttribute.Count > 0)
                            {
                                var ifValue = mAssetAttribute.Where(x => x.Title.Trim() == "If mA").Select(x => x.Data).FirstOrDefault();
                                var irValue = mAssetAttribute.Where(x => x.Title.Trim() == "Ir mA").Select(x => x.Data).FirstOrDefault();
                                var vrValue = mAssetAttribute.Where(x => x.Title.Trim() == "Vr").Select(x => x.Data).FirstOrDefault();
                                var chokeValue = mAssetAttribute.Where(x => x.Title.Trim() == "Choke V").Select(x => x.Data).FirstOrDefault();
                                var tprValue = mAssetAttribute.Where(x => x.Title.Trim() == "TPR V").Select(x => x.Data).FirstOrDefault();
                                var vfValue = mAssetAttribute.Where(x => x.Title.Trim() == "Vf").Select(x => x.Data).FirstOrDefault();
                                if (tprValue != null && Convert.ToDecimal(tprValue.FirstOrDefault()) < 2)
                                {
                                    if ((ifValue != null && Convert.ToDecimal(ifValue.FirstOrDefault()) < 400) || (irValue != null && Convert.ToDecimal(irValue.FirstOrDefault()) > 100) || (vrValue != null && Convert.ToDecimal(vrValue.FirstOrDefault()) > 1.5m) || (chokeValue != null && Convert.ToDecimal(chokeValue.FirstOrDefault()) < 1.8m) || (vfValue != null && Convert.ToDecimal(vfValue.FirstOrDefault()) > 1.5m))
                                    {
                                        counter++;
                                        if ((ifValue != null && Convert.ToDecimal(ifValue.FirstOrDefault()) <= 0) && (irValue != null && Convert.ToDecimal(irValue.FirstOrDefault()) <= 0) && (vrValue != null && Convert.ToDecimal(vrValue.FirstOrDefault()) <= 0) && ((chokeValue != null && Convert.ToDecimal(chokeValue.FirstOrDefault()) <= 0) || (vfValue != null && Convert.ToDecimal(vfValue.FirstOrDefault()) <= 0)))
                                        {

                                        }
                                        else
                                        {
                                            tbodyData += "<tr>";
                                            tbodyData += $"<td>{mDynamoTable.TimeStamp}</td>";
                                            tbodyData += $"<td>{mAsset.Name}</td>";

                                            if (ifValue != null)
                                                tbodyData += $"<td>{ifValue.FirstOrDefault()}</td>";
                                            else
                                                tbodyData += $"<td></td>";

                                            if (irValue != null)
                                                tbodyData += $"<td>{irValue.FirstOrDefault()}</td>";
                                            else
                                                tbodyData += $"<td></td>";

                                            if (vrValue != null)
                                                tbodyData += $"<td>{vrValue.FirstOrDefault()}</td>";
                                            else
                                                tbodyData += $"<td></td>";

                                            if (tprValue != null)
                                                tbodyData += $"<td>{tprValue.FirstOrDefault()}</td>";
                                            else
                                                tbodyData += $"<td></td>";

                                            if (chokeValue != null)
                                                tbodyData += $"<td>{chokeValue.FirstOrDefault()}</td>";
                                            else
                                                tbodyData += $"<td></td>";

                                            if (vfValue != null)
                                                tbodyData += $"<td>{vfValue.FirstOrDefault()}</td>";
                                            else
                                                tbodyData += $"<td></td>";

                                            tbodyData += "</tr>";
                                        }

                                    }
                                }
                            }
                        }
                    }
                }
            }
            tbodyData += "</table>";

            return tbodyData;
        }

        private List<MultipleLog> GetAllGraphData(Domain.SearchCriteria searchCriteria)
        {
            List<MultipleLog> mDynamoTables = new List<MultipleLog>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(searchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GetAllGraphData"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mDynamoTables = JsonConvert.DeserializeObject<List<MultipleLog>>(jsonString);
                    }

                }
            }
            catch (Exception ex)
            {
            }

            return mDynamoTables;
        }

        private string PrepareAlert(AssetLister mAssetLister)
        {
            string tbodyData = string.Empty;
            if (mAssetLister != null && mAssetLister.mAssets != null && mAssetLister.mAssets.Count > 0)
            {
                var mSMSLogLister = new SMSLogLister();
                mSMSLogLister.SearchCriteria.SiteId = mAssetLister.SearchCriteria.SiteId;
                mSMSLogLister.SearchCriteria.FromDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
                mSMSLogLister.SearchCriteria.ToDate = mSMSLogLister.SearchCriteria.FromDate.AddMonths(1).AddDays(-1);
                mSMSLogLister = smsLogService.GetAll(mSMSLogLister);
                if (mSMSLogLister != null && mSMSLogLister.mSMSLogs != null && mSMSLogLister.mSMSLogs.Count > 0)
                {
                    var assetTypes = mSMSLogLister.AssetTypes.GroupBy(x => x.Name).Select(x => x.Key).ToList();
                    if (assetTypes != null && assetTypes.Count > 0)
                    {
                        foreach (var assetType in assetTypes)
                        {
                            int i = 0;
                            tbodyData += "<table class='tbl' style='background-color:white;color:black;margin-top:10px'>";
                            tbodyData += "<tr><th>sr. no</th><th>Asset name </th><th>Alert</th></tr>";
                            foreach (var mAsset in mAssetLister.mAssets.Where(x => x.AssetTypeName.Trim().ToLower() == assetType.Trim().ToLower()))
                            {
                                var smsLogs = mSMSLogLister.mSMSLogs.Where(x => x.AssetId == mAsset.Id).OrderByDescending(x => x.TimeStamp).Take(5).ToList();

                                if (smsLogs != null && smsLogs.Count > 0)
                                {
                                    i++;
                                    tbodyData += "<tr>";
                                    tbodyData += $"<td>{i}</td>";
                                    tbodyData += $"<td>{mAsset.Name}</td>";
                                    tbodyData += "<td>";
                                    tbodyData += "<table>";
                                    foreach (var smsLog in smsLogs)
                                    {
                                        tbodyData += "<tr>";
                                        tbodyData += $"<td>{smsLog.Message}</td>";
                                        tbodyData += "</tr>";
                                    }
                                    tbodyData += "</table>";

                                    tbodyData += "</td>";
                                    tbodyData += "</tr>";
                                }

                            }

                            tbodyData += "</table>";

                        }
                    }
                }
            }

            return tbodyData;
        }


        private string PrepareError(List<(int, string, string, string, string)> allErrors)
        {
            string tbodyData = string.Empty;
            if (allErrors != null && allErrors.Count > 0)
            {
                tbodyData += "<table class='tbl' style='background-color:white;color:black;margin-top:10px'>";
                tbodyData += "<tr><th>sr. no</th><th>Asset Type </th><th>Asset name </th><th>Attribute name </th><th>Remark</th></tr>";
                foreach (var error in allErrors)
                {
                    string message = string.Empty;
                    try
                    {
                        if (error.Item5.IsNotNullOrEmpty())
                        {
                            decimal value = Convert.ToDecimal(error.Item5);
                            if (error.Item2.ToUpper() == E7FRSAdvance.Utility.Utility.AssetType.Device_Time.ToString().ToUpper().Replace("_", " "))
                            {
                                if (error.Item4.Contains("Last Access time"))
                                {
                                    if (value == -1 || value > 60)
                                        message = $"current val {value}v,has to be not equal to -1v or less than 60v";

                                }

                            }
                            if (error.Item2.ToUpper() == E7FRSAdvance.Utility.Utility.AssetType.TRACK.ToString())
                            {
                                if (error.Item4.Contains("If"))
                                {
                                    if (value < 150 || value > 400)
                                        message = $"current val {value}v,has to be greater than 150v or less than 400v";

                                }
                                else if (error.Item4.Contains("Ir"))
                                {
                                    if (value < 150 || value > 400)
                                        message = $"current val {value}v,has to be greater than 150v or less than 400v";
                                }
                                else if (error.Item4.Contains("Vr"))
                                {
                                    if (value < 2 || value > 4.2m)
                                        message = $"current val {value}v,has to be greater than 2v or less than 4.2v";
                                }
                                else if (error.Item4.Contains("TPR"))
                                {
                                    if (value < 18 || value > 32)
                                        message = $"current val {value}v,has to be greater than 18v or less than 32v";
                                }
                                else if (error.Item4.Contains("Charger"))
                                {
                                    if (value < 100 || value > 2000)
                                        message = $"current val {value}v,has to be greater than 100v or less than 2000v";
                                }
                                else if (error.Item4.Contains("Vf"))
                                {
                                    if (value < 2 || value > 6)
                                        message = $"current val {value}v,has to be greater than 2v or less than 6v";
                                }
                                else if (error.Item4.Contains("Choke"))
                                {
                                    if (value < 0.5m || value > 2)
                                        message = $"current val {value}v,has to be greater than 0.5v or less than 2v";
                                }
                                else if (error.Item4.Contains("Leakage"))
                                {
                                    if (value >= 150)
                                        message = $"current val {value}v,has to be less than 150v";
                                }

                            }
                            if (error.Item2.ToUpper() == E7FRSAdvance.Utility.Utility.AssetType.SIGNAL.ToString())
                            {
                                if (error.Item3.ToUpper().StartsWith("SH"))
                                {
                                    if (value < 50)
                                        message = $"current val {value}v,has to be greater than 50v";
                                }
                                else
                                {
                                    if (error.Item4.Contains("mA"))
                                    {
                                        if (value < 105 || value > 150)
                                            message = $"current val {value}v,has to be greater than 105v or less than 150v";
                                    }
                                    else if (error.Item4.Contains("V"))
                                    {
                                        if (value < 105 || value > 150)
                                            message = $"current val {value}v,has to be greater than 105v or less than 150v";
                                    }
                                }

                            }
                            if (error.Item2.ToUpper() == E7FRSAdvance.Utility.Utility.AssetType.Device_Time.ToString())
                            {
                                if (error.Item4.Contains("Last Access time"))
                                {
                                    if (value > 100)
                                        message = $"current val {value}v,has to be greater than 100v";
                                }
                            }
                        }
                        else
                        {

                        }
                    }
                    catch (Exception)
                    {
                        message = error.Item5;
                    }



                    tbodyData += "<tr>";
                    tbodyData += $"<td>{error.Item1}</td>";
                    tbodyData += $"<td>{error.Item2}</td>";
                    tbodyData += $"<td>{error.Item3}</td>";
                    tbodyData += $"<td>{error.Item4}</td>";
                    tbodyData += $"<td>{message}</td>";
                    tbodyData += "</tr>";
                }
            }
            return tbodyData;
        }

        private CalibData GetA10Calibration(int siteId)
        {
            CalibData mCalibData = new CalibData();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Maintenance/GetA10Calibration/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var str = JsonConvert.DeserializeObject<string>(jsonString);

                        mCalibData = JsonConvert.DeserializeObject<CalibData>(str);
                        if (mCalibData != null && mCalibData.calib != null)
                        {
                            mCalibData.calib = mCalibData.calib.Where(x => x != null).ToList();
                        }

                        //var dsds = jsonDe.calib;
                        //var dsddsdsd = Convert.ToString(dsds);

                        //var dsdsd = JsonConvert.DeserializeObject<List<A10Calibration>>(dsds);
                        //if (deviceINIs.IsNotNullOrEmpty())
                        //{
                        //    byte[] bytes = Encoding.ASCII.GetBytes(deviceINIs);


                        //    var fileArray = Convert.FromBase64String(deviceINIs);
                        //    if (fileArray != null && fileArray.Count() > 0)
                        //        return File(fileArray, "text/plain", $"DeviceINI_{DateTime.Now.Ticks}" + ".ini");


                        //}
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
            return mCalibData;
        }

        private List<CardLine> GetYardConfig(int siteId)
        {
            List<CardLine> mCardLines = new List<CardLine>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("CardLine/SiteId/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mCardLines = JsonConvert.DeserializeObject<List<CardLine>>(jsonString);
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

            return mCardLines;

        }

        private bool CheckHighlightA10Multiplayer(A10Calibration a10Calibration, int assetTypeId)
        {
            bool isValid = true;
            decimal value = 0;

            if (a10Calibration != null)
            {
                if (a10Calibration.A10Multiplication.IsNotNullOrEmpty())
                {
                    decimal.TryParse(a10Calibration.A10Multiplication, out value);

                }

                if (a10Calibration.device_type_id == "1507")
                {
                    if (assetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.POINT_MACHINE)
                    {
                        if (value < 350 || value > 430)
                            isValid = false;
                    }
                    else
                    {
                        if (value < 35 || value > 43)
                            isValid = false;
                    }

                }
                else if (a10Calibration.device_type_id == "1505")
                {
                    if (value < 35 || value > 43)
                        isValid = false;
                }
                else if (a10Calibration.device_type_id == "1056")
                {
                    if (value < 350 || value > 430)
                        isValid = false;
                }
            }
            return isValid;
        }
        #endregion


    }
}