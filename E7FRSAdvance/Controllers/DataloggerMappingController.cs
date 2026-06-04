using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Service;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;
using static E7FRSAdvance.Utility.Utility;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class DataloggerMappingController : Controller
    {
        // GET: DataloggerMapping
        private readonly ISiteService siteService;
        private readonly IZoneService zoneService;
        private readonly IDivisionService divisionService;
        private readonly IAssetService assetService;
        public DataloggerMappingController(ISiteService siteService, IZoneService zoneService, IDivisionService divisionService, IAssetService assetService)
        {
            this.siteService = siteService;
            this.zoneService = zoneService;
            this.divisionService = divisionService;
            this.assetService = assetService;
        }
        public ActionResult Index()
        {
            var sites = siteService.GetAll();
            ViewBag.Sites = new SelectList(sites, "Id", "Name");
            return View();
        }

        public ActionResult GetAssetType(int siteId)
        {
            RailwayAsset mRailwayAsset = new RailwayAsset();

            var mSite = siteService.Get(siteId);
            if (mSite != null && mSite.Id > 0)
            {
                var mZone = zoneService.GetZoneById(mSite.ZoneId);
                var mDivision = divisionService.Get(mSite.DivisionId);
                if (mZone != null && mZone.Id > 0 && mDivision != null && mDivision.Id > 0)
                {
                    var divisions = ExtensionMethod.BuildDivision(mZone.RepresentationCode);
                    var zones = ExtensionMethod.BuildZones();
                    if (divisions != null && divisions.Count > 0)
                    {
                        var divi = divisions.Where(x => x.Id == mDivision.RepresentationCode).FirstOrDefault();
                        var zon = zones.Where(x => x.Id == mZone.RepresentationCode).FirstOrDefault();
                        if (divi != null && zon != null)
                        {
                            try
                            {
                                var client = new HttpClient();

                                var request = new HttpRequestMessage(
                                    HttpMethod.Post,
                                    $"{ConfigurationManager.AppSettings.Get("RailwaySMMSAPIUrl")}/{zon.Code}/{divi.Code}/{mSite.StationCode}"
                                );

                                // Add cookie
                                request.Headers.Add("Cookie", "TS015f053d=01ee28b4440dd833dc70827baa28a93f79d0c659f6d69b28645118a5d2938e8a16395315ce2ddf4f9024e8f20649e2c939e0ce84b2");

                                // If API requires body, uncomment this line
                                // request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

                                var response = client.SendAsync(request).GetAwaiter().GetResult();
                                response.EnsureSuccessStatusCode();

                                string result = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                                if (response.StatusCode == HttpStatusCode.OK)
                                    mRailwayAsset = JsonConvert.DeserializeObject<RailwayAsset>(result);
                                else
                                {
                                    ViewBag.Type = "Error";
                                    ViewBag.Message = "Internal server error!";
                                }

                            }
                            catch (Exception ex)
                            {
                                ViewBag.Type = "Error";
                                ViewBag.Message = "Internal server error!";
                            }
                            finally
                            {
                                ViewBag.Assets = GetAssetBy(siteId);
                            }
                        }                
                    }
                }
            }

            return PartialView("_AssetType", mRailwayAsset);
        }

        public ActionResult GetAsset(int siteId, string assetTypeName)
        {
            var mDataloggerAssets = new List<DataloggerAsset>();
            RailwayAsset mRailwayAsset = new RailwayAsset();


            var mSite = siteService.Get(siteId);
            if (mSite != null && mSite.Id > 0)
            {
                var mZone = zoneService.GetZoneById(mSite.ZoneId);
                var mDivision = divisionService.Get(mSite.DivisionId);
                if (mZone != null && mZone.Id > 0 && mDivision != null && mDivision.Id > 0)
                {
                    var divisions = ExtensionMethod.BuildDivision(mZone.RepresentationCode);
                    var zones = ExtensionMethod.BuildZones();
                    if (divisions != null && divisions.Count > 0)
                    {
                        var divi = divisions.Where(x => x.Id == mDivision.RepresentationCode).FirstOrDefault();
                        var zon = zones.Where(x => x.Id == mZone.RepresentationCode).FirstOrDefault();
                        if (divi != null && zon != null)
                        {
                            try
                            {
                                var client = new HttpClient();

                                var request = new HttpRequestMessage(
                                    HttpMethod.Post,
                                    $"{ConfigurationManager.AppSettings.Get("RailwaySMMSAPIUrl")}/{zon.Code}/{divi.Code}/{mSite.StationCode}"
                                );

                                // Add cookie
                                request.Headers.Add("Cookie", "TS015f053d=01ee28b4440dd833dc70827baa28a93f79d0c659f6d69b28645118a5d2938e8a16395315ce2ddf4f9024e8f20649e2c939e0ce84b2");


                                var response = client.SendAsync(request).GetAwaiter().GetResult();
                                response.EnsureSuccessStatusCode();

                                string result = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                                if (response.StatusCode == HttpStatusCode.OK)
                                {
                                    mRailwayAsset = JsonConvert.DeserializeObject<RailwayAsset>(result);
                                    if (mRailwayAsset != null && mRailwayAsset.info != null && mRailwayAsset.info.Count > 0)
                                    {

                                        var filtered = mRailwayAsset.info.Where(i => i.additional_info.Any(ai => ai.key == "menulabel" && ai.value == assetTypeName)).ToList();
                                        if (filtered != null && filtered.Count > 0)
                                        {
                                            mRailwayAsset.info = new List<Info>();
                                            mRailwayAsset.info = filtered;
                                        }

                                    }
                                }
                                else
                                {
                                    ViewBag.Type = "Error";
                                    ViewBag.Message = "Internal server error!";
                                }

                            }
                            catch (Exception ex)
                            {
                                ViewBag.Type = "Error";
                                ViewBag.Message = "Internal server error!";
                            }
                            finally
                            {
                                if (assetTypeName.IsNotNullOrEmpty())
                                {
                                    if (assetTypeName.Contains("Track"))
                                    {
                                        assetTypeName = E7FRSAdvance.Utility.Utility.AssetType.TRACK.ToString();
                                    }
                                    else if (assetTypeName.Contains("Signal"))
                                    {
                                        assetTypeName = E7FRSAdvance.Utility.Utility.AssetType.SIGNAL.ToString();
                                    }
                                    else if (assetTypeName.Contains("Point"))
                                    {
                                        assetTypeName = E7FRSAdvance.Utility.Utility.AssetType.POINT_MACHINE.ToString().Replace("_", " ");
                                    }
                                    var mAssets = GetAllAsset(siteId, assetTypeName);
                                    if (mAssets != null && mAssets.Count > 0)
                                        ViewBag.Assets = mAssets;
                                    else
                                        ViewBag.Assets = assetService.GetAssestBy(siteId);

                                    ViewBag.DataloggerAssetMappings = GetAssetBy(siteId);

                                    ViewBag.AssetTypeName = assetTypeName;
                                }
                            }
                        }

                    }

                }

            }

            return PartialView("_Asset", mRailwayAsset);
        }

        public List<Asset> GetAllAsset(int siteId, string assetTypeName)
        {
            var mAssets = new List<Asset>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync($"Asset/SiteId/{siteId}/AssetType/{assetTypeName}").Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                        mAssets = JsonConvert.DeserializeObject<List<Asset>>(jsonString);
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
            return mAssets;
        }

        public List<Domain.DataloggerAssetMapping> GetAssetBy(int siteId)
        {
            var mAssets = new List<Domain.DataloggerAssetMapping>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"DataloggerAssetMapping/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssets = JsonConvert.DeserializeObject<List<Domain.DataloggerAssetMapping>>(jsonString);

                    }
                }
            }
            catch (Exception ex)
            {
            }
            return mAssets;
        }

        [HttpPost]
        public ActionResult Create(int siteId, List<Domain.AssetMapping> mDataloggerMappings)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mDataloggerMappings);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format($"DataloggerAssetMapping/SiteId/{siteId}"), str).Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Asset has been mapping in RDPMS." };

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


        [HttpPost]
        public ActionResult UploadDataLogger(int siteId)
        {
            dynamic data = new ExpandoObject();
            for (int i = 0; i < Request.Files.Count; i++)
            {
                var file = Request.Files[i];
                var csvBody = string.Empty;


                string fileName = Path.GetFileName(file.FileName);
                string extension = Path.GetExtension(fileName);

                if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
                {
                    // Read file content directly from stream — no saving to disk
                    using (var reader = new StreamReader(file.InputStream))
                    {
                        string fileContent = reader.ReadToEnd();

                        // Example: Split lines if needed
                        var parsed = JToken.Parse(fileContent);
                        string singleLineJson = JsonConvert.SerializeObject(parsed, Formatting.None);

                        List<Domain.JsonData> mDataloggerMappings = new List<Domain.JsonData>();
                        mDataloggerMappings.Add(new JsonData() { JsonValue = singleLineJson, SiteId = siteId, CreateDate = DateTime.Now });
                        try
                        {
                            using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                            {
                                var jsonStr = JsonConvert.SerializeObject(mDataloggerMappings);
                                StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                                var response = hcf.client.PutAsync(String.Format($"JsonDataRemote/BulkInsert/{siteId}"), str).Result;
                                if (response.StatusCode == HttpStatusCode.NoContent)
                                {
                                    data = new { type = "success", result = "File has been uploaded." };

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
                    }
                }
                else
                {
                    data = new { type = "error", result = "Invalid file type. Only .txt or .csv supported." };
                }
            }


            return Json(data, JsonRequestBehavior.AllowGet);

        }

        [HttpPost]
        public ActionResult Get(int siteId)
        {
            List<Domain.DataloggerAssetMapping> mDataloggerMappings = new List<DataloggerAssetMapping>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync($"DataloggerAssetMapping/SiteId/{siteId}").Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                        mDataloggerMappings = JsonConvert.DeserializeObject<List<DataloggerAssetMapping>>(jsonString);
                }
            }
            catch (Exception)
            {
            }
            return Json(mDataloggerMappings, JsonRequestBehavior.AllowGet);

        }
    }
}