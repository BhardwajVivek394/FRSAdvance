using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Service;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Mvc;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class AnnexureBController : Controller
    {
        // GET: AnnexureB
        private readonly ISiteService _siteService;
        private readonly IZoneService _zoneService;
        private readonly IDivisionService _divisionService;
        private readonly IAssetTypeService _assetTypeService;
        private readonly IAssetService _assetService;
        private readonly ISectionService _sectionService;
        private readonly IFRSAlertService _frsAlertService;
        private readonly ICardLineService _cardLineService;
        private readonly IAssetAttributeService _assetAttributeService;

        public AnnexureBController(ISiteService siteService, IZoneService zoneService, IDivisionService divisionService, IAssetTypeService assetTypeService, IAssetService assetService, ISectionService sectionService, IFRSAlertService frsAlertService, ICardLineService cardLineService, IAssetAttributeService assetAttributeService)
        {
            _siteService = siteService;
            _zoneService = zoneService;
            _divisionService = divisionService;
            _assetTypeService = assetTypeService;
            _assetService = assetService;
            _sectionService = sectionService;
            _frsAlertService = frsAlertService;
            _cardLineService = cardLineService;
            _assetAttributeService = assetAttributeService;
        }

        public ActionResult Index()
        {
            var mZones = _zoneService.GetAllZones();
            var Divisions = _divisionService.GetAllDivisions();
            if (Divisions != null && Divisions.Count > 0)
            {
                foreach (var Division in Divisions)
                {
                    var mDivisionA = ExtensionMethod.BuildDivisionByDivisionCode(Division.RepresentationCode);
                    if (mDivisionA != null)
                        Division.DivisionCode = mDivisionA.Code;
                }
                ViewBag.Divisions = Divisions;
            }
            var mSites = _siteService.GetAll();
            if (mZones != null && mZones.Count > 0)
            {
                foreach (var mZone in mZones)
                {
                    var divisions = Divisions.Where(x => x.ZoneId == mZone.Id).ToList();
                    if (divisions != null && divisions.Count > 0)
                    {
                        foreach (var division in divisions)
                        {
                            int counter = 0;
                            foreach (var mSite in mSites.Where(x => x.ZoneId == mZone.Id && x.DivisionId == division.Id).OrderBy(x => x.Id).ToList())
                            {
                                string assetHexValue = Convert.ToString(counter, 16).ToUpper();

                                mSite.StationGatewayId = $"{mZone.RepresentationCode}{division.RepresentationCode}{assetHexValue.MakeTwoChars()}01";
                                counter++;
                            }

                        }
                    }
                }
            }
            if (mSites != null && mSites.Count > 0)
            {
                ViewBag.Sites = mSites;
            }
            if (mZones != null && mZones.Count() > 0)
            {
                ViewBag.Zones = new SelectList(mZones, "Id", "Name");
            }

            ViewBag.AssetTypes = new SelectList(_assetTypeService.GetAll(), "Id", "Name");
            return View();
        }

        public ActionResult GetMQTTDetailList()
        {

            MQTTDetail mMQTTDetail = new MQTTDetail();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("User/GetMQTTDetail")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var mMQTTDetailList = JsonConvert.DeserializeObject<MQTTDetailList>(jsonString);
                        if (mMQTTDetailList != null && mMQTTDetailList.mQTTDetailWeb != null)
                            mMQTTDetail = mMQTTDetailList.mQTTDetailWeb;
                    }

                }
            }
            catch (Exception ex)
            {
            }
            return Json(mMQTTDetail);
        }

        public ActionResult GetAssestBy(int siteId, int assetTypeId)
        {
            var mAssets = _assetService.GetAssestBy(siteId, assetTypeId);
            if (mAssets != null && mAssets.Count > 0)
            {
                var mDataloggerAssetMappings = GetAssetBySiteId(siteId);
                if (mDataloggerAssetMappings != null)
                {
                    foreach (var mAsset in mAssets)
                    {
                        var mDataloggerAssetMapping = mDataloggerAssetMappings.Where(x => x.AssetId == mAsset.Id).FirstOrDefault();
                        if (mDataloggerAssetMapping != null)
                        {
                            mAsset.RailwayAssetcode = mDataloggerAssetMapping.RailwayAssetcode;
                        }
                    }
                }
            }
            return Json(mAssets, JsonRequestBehavior.AllowGet);
        }

        public ActionResult Certificate()
        {
            return View();
        }


        public JsonResult GetSiteByDivisionId(int divisionId)
        {
            var mSites = new List<Domain.Site>();
            try
            {
                var mZones = _zoneService.GetAllZones();
                var Divisions = _divisionService.GetAllDivisions();
                if (Divisions != null && Divisions.Count > 0)
                {
                    foreach (var Division in Divisions)
                    {
                        var mDivisionA = ExtensionMethod.BuildDivisionByDivisionCode(Division.RepresentationCode);
                        if (mDivisionA != null)
                            Division.DivisionCode = mDivisionA.Code;
                    }
                    ViewBag.Divisions = Divisions;
                }
                mSites = _siteService.GetBy(divisionId);
                if (mZones != null && mZones.Count > 0 && mSites != null)
                {
                    foreach (var mZone in mZones)
                    {
                        var divisions = Divisions.Where(x => x.ZoneId == mZone.Id).ToList();
                        if (divisions != null && divisions.Count > 0)
                        {
                            foreach (var division in divisions)
                            {
                                int counter = 0;
                                foreach (var mSite in mSites.Where(x => x.ZoneId == mZone.Id && x.DivisionId == division.Id).OrderBy(x => x.Id).ToList())
                                {
                                    string assetHexValue = Convert.ToString(counter, 16).ToUpper();

                                    mSite.StationGatewayId = $"{mZone.RepresentationCode}{division.RepresentationCode}{assetHexValue.MakeTwoChars()}01";
                                    counter++;
                                }

                            }
                        }
                    }
                }

            }
            catch (Exception ex)
            {
            }
            return Json(mSites, JsonRequestBehavior.AllowGet);
        }

        public ActionResult _Discovery(int siteId)
        {
            var mRailwayInfos = GetRailwayInfo(siteId);

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("User/GetMQTTDetail")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var mMQTTDetailList = JsonConvert.DeserializeObject<MQTTDetailList>(jsonString);
                        if (mMQTTDetailList != null && mMQTTDetailList.mQTTDetailWeb != null)
                            ViewBag.mMQTTDetail = mMQTTDetailList.mQTTDetailWeb;
                    }

                }
            }
            catch (Exception ex)
            {
            }
            return PartialView();
        }

        public ActionResult _SMMSTelemetry()
        {
            var mZones = _zoneService.GetAllZones();
            var Divisions = _divisionService.GetAllDivisions();
            if (Divisions != null && Divisions.Count > 0)
            {
                foreach (var Division in Divisions)
                {
                    var mDivisionA = ExtensionMethod.BuildDivisionByDivisionCode(Division.RepresentationCode);
                    if (mDivisionA != null)
                        Division.DivisionCode = mDivisionA.Code;
                }
                ViewBag.Divisions = Divisions;
            }
            var mSites = _siteService.GetAll();
            if (mZones != null && mZones.Count > 0)
            {
                foreach (var mZone in mZones)
                {
                    var divisions = Divisions.Where(x => x.ZoneId == mZone.Id).ToList();
                    if (divisions != null && divisions.Count > 0)
                    {
                        foreach (var division in divisions)
                        {
                            int counter = 0;
                            foreach (var mSite in mSites.Where(x => x.ZoneId == mZone.Id && x.DivisionId == division.Id))
                            {
                                string assetHexValue = Convert.ToString(counter, 16).ToUpper();

                                mSite.StationGatewayId = $"{mZone.RepresentationCode}{division.RepresentationCode}{assetHexValue.MakeTwoChars()}01";
                                counter++;
                            }

                        }
                    }
                }
            }
            if (mSites != null && mSites.Count > 0)
            {
                ViewBag.Sites = mSites;
            }
            if (mZones != null && mZones.Count() > 0)
            {
                ViewBag.Zones = new SelectList(mZones, "Id", "Name");
            }

            ViewBag.AssetTypes = new SelectList(_assetTypeService.GetAll(), "Id", "Name");

            return PartialView();
        }

        public ActionResult _SMMSList()
        {
            var mZones = _zoneService.GetAllZones();
            var Divisions = _divisionService.GetAllDivisions();
            if (Divisions != null && Divisions.Count > 0)
            {
                foreach (var Division in Divisions)
                {
                    var mDivisionA = ExtensionMethod.BuildDivisionByDivisionCode(Division.RepresentationCode);
                    if (mDivisionA != null)
                        Division.DivisionCode = mDivisionA.Code;
                }
                ViewBag.Divisions = Divisions;
            }
            var mSites = _siteService.GetAll();
            if (mZones != null && mZones.Count > 0)
            {
                foreach (var mZone in mZones)
                {
                    var divisions = Divisions.Where(x => x.ZoneId == mZone.Id).ToList();
                    if (divisions != null && divisions.Count > 0)
                    {
                        foreach (var division in divisions)
                        {
                            int counter = 0;
                            foreach (var mSite in mSites.Where(x => x.ZoneId == mZone.Id && x.DivisionId == division.Id))
                            {
                                string assetHexValue = Convert.ToString(counter, 16).ToUpper();

                                mSite.StationGatewayId = $"{mZone.RepresentationCode}{division.RepresentationCode}{assetHexValue.MakeTwoChars()}01";
                                counter++;
                            }

                        }
                    }
                }
            }
            if (mSites != null && mSites.Count > 0)
            {
                ViewBag.Sites = mSites;
            }
            if (mZones != null && mZones.Count() > 0)
            {
                ViewBag.Zones = new SelectList(mZones, "Id", "Name");
            }
            return PartialView();
        }

        public ActionResult _SMMSListPartial(int siteId)
        {
            RailwayAsset mRailwayAsset = new RailwayAsset();

            var mSite = _siteService.Get(siteId);
            if (mSite != null && mSite.Id > 0)
            {
                var mZone = _zoneService.GetZoneById(mSite.ZoneId);
                var mDivision = _divisionService.Get(mSite.DivisionId);
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

            return PartialView("_SMMSListPartial", mRailwayAsset);
        }

        public ActionResult _ParameterPartial(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("User/GetMQTTDetail")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var mMQTTDetailList = JsonConvert.DeserializeObject<MQTTDetailList>(jsonString);
                        if (mMQTTDetailList != null && mMQTTDetailList.mQTTDetailWeb != null)
                            ViewBag.mMQTTDetail = mMQTTDetailList.mQTTDetailWeb;
                    }

                }
            }
            catch (Exception ex)
            {
            }
            return PartialView();
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

        public JsonResult GetDivisionByZoneId(int zoneId)
        {
            List<Division> mDivisions = new List<Division>();
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
                        if (mDivisions != null && mDivisions.Count > 0)
                        {
                            foreach (var Division in mDivisions)
                            {
                                var mDivisionA = ExtensionMethod.BuildDivisionByDivisionCode(Division.RepresentationCode);
                                if (mDivisionA != null)
                                    Division.DivisionCode = mDivisionA.Code;
                            }
                        }

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

            return Json(mDivisions, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetAssetData(int siteId, int assetId)
        {
            var mRailwayInfoData = new Domain.RailwayInfo();
            var mRailwayInfos = GetRailwayInfo(siteId);

            if (mRailwayInfos != null && mRailwayInfos.Count > 0)
            {
                mRailwayInfoData = mRailwayInfos.Where(x => x.AssetId == assetId).FirstOrDefault();
            }

            return Json(mRailwayInfoData);
        }

        private string SetParmsName(string clusterName, List<ClusterConfig> clusterConfigs)
        {
            string parmsName = string.Empty;
            string[] ClusterNames = clusterName.Split('/');
            if (ClusterNames != null && ClusterNames.Length > 0)
            {
                string[] parmsNames = ClusterNames[0].Split('_');

                if (clusterConfigs != null && clusterConfigs.Count > 0)
                {
                    var mClusterConfig = clusterConfigs.Where(x => x.ClusterName != null && x.ClusterName.Trim().ToUpper() == ClusterNames[0].Trim().ToUpper()).FirstOrDefault();
                    if (mClusterConfig != null && mClusterConfig.Id > 0)
                    {
                        parmsName = mClusterConfig.Param;
                    }
                }

            }

            return parmsName;
        }

        public List<Domain.ClusterConfig> GetClusterConfigBySiteId(int siteId)
        {
            var mClusterConfigs = new List<Domain.ClusterConfig>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"ClusterConfig/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mClusterConfigs = JsonConvert.DeserializeObject<List<Domain.ClusterConfig>>(jsonString);

                    }
                }
            }
            catch (Exception ex)
            {
            }
            return mClusterConfigs;
        }

        public List<Domain.DataloggerAssetMapping> GetAssetBySiteId(int siteId)
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

        private List<Domain.RailwayInfo> GetRailwayInfo(int siteId)
        {
            var mRailwayInfos = new List<Domain.RailwayInfo>();

            var mSite = _siteService.Get(siteId);
            if (mSite != null && mSite.Id > 0)
            {
                var mClusterConfigs = GetClusterConfigBySiteId(mSite.Id);
                var mCardLines = _cardLineService.Get(siteId);
                if (mCardLines != null && mCardLines.Count > 0)
                {
                    var mAssetAttributes = _assetAttributeService.GetAll();
                    var mAssetTypes = _assetTypeService.GetAll();
                    var mDataloggerAssetMappings = GetAssetBySiteId(siteId);


                    var assets = mCardLines.GroupBy(x => new { x.AssetId, x.AssetName, x.AssetTypeId, x.AssetTypeName }).Select(x => x.Key).ToList();
                    if (assets != null && assets.Count > 0 && mDataloggerAssetMappings != null && mDataloggerAssetMappings.Count > 0)
                    {
                        foreach (var mDataloggerAssetMapping in mDataloggerAssetMappings)
                        {
                            var assetTest = assets.Where(x => x.AssetId == mDataloggerAssetMapping.AssetId).FirstOrDefault();
                            if (assetTest != null && assetTest.AssetTypeId != null)
                            {
                                mDataloggerAssetMapping.AssetTypeId = assetTest.AssetTypeId.Value;
                            }
                        }

                        foreach (var mDataloggerAssetMapping in mDataloggerAssetMappings.GroupBy(x => x.AssetTypeId).Select(x => x.Key).ToList())
                        {
                            int dlAssetCount = 0;
                            foreach (var dlatype in mDataloggerAssetMappings.Where(x => x.AssetTypeId == mDataloggerAssetMapping).ToList())
                            {

                                string assetHexValue = Convert.ToString(dlAssetCount, 16).ToUpper();

                                dlatype.Code = assetHexValue.MakeTwoChars();

                                dlAssetCount++;
                            }
                        }

                        int assetCount = 0;
                        foreach (var asset in assets)
                        {
                            var mDataloggerAssetMapping = mDataloggerAssetMappings.Where(x => x.AssetId == asset.AssetId).FirstOrDefault();
                            if (mDataloggerAssetMapping != null)
                            {
                                string smmsAssetCode = mDataloggerAssetMapping.RailwayAssetcode;
                                if (asset.AssetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.POINT_MACHINE)
                                {
                                    var dataloggerAssetMapping = mDataloggerAssetMappings.Where(x => x.AssetId == asset.AssetId && x.RailwayAssetcode != null && x.RailwayAssetcode != string.Empty).ToList();
                                    if (dataloggerAssetMapping.Count >= 2)
                                    {
                                        smmsAssetCode = string.Join(",", dataloggerAssetMapping.Select(x => x.RailwayAssetcode).ToList());
                                    }
                                }

                                var mRailwayInfo = new Domain.RailwayInfo();
                                mRailwayInfo.AssetId = asset.AssetId.Value;
                                mRailwayInfo.AssetTypeId = asset.AssetTypeId.Value;
                                mRailwayInfo.AssetType = asset.AssetTypeName;
                                mRailwayInfo.AssetName = asset.AssetName;
                                mRailwayInfo.Asnc = mDataloggerAssetMapping.RailwayAssetName;
                                mRailwayInfo.Asni = mDataloggerAssetMapping.Code;
                                mRailwayInfo.Astc = mDataloggerAssetMapping.RailwayAssetTypeCode;
                                mRailwayInfo.SmmsAssetCode = smmsAssetCode;

                                var cardLines = mCardLines.Where(x => x.AssetId == asset.AssetId).ToList();
                                if (cardLines != null && cardLines.Count > 0)
                                {
                                    foreach (var cardLine in cardLines)
                                    {
                                        int adcType = 0;
                                        if (cardLine.ADCTypeId == (int)E7FRSAdvance.Utility.Utility.ADCType.Special_1056)
                                        {
                                            adcType = 1056;
                                        }
                                        else if (cardLine.ADCTypeId == (int)E7FRSAdvance.Utility.Utility.ADCType.Special_1507)
                                        {
                                            adcType = 1507;
                                        }
                                        else if (cardLine.ADCTypeId == (int)E7FRSAdvance.Utility.Utility.ADCType.Normal)
                                        {
                                            adcType = 1505;
                                        }

                                        string assetRepresentCode = string.Empty;
                                        string attrParameterRepCode = string.Empty;
                                        string atypeParameterRepCode = string.Empty;
                                        string parameterTyprRepCode = string.Empty;
                                        if (mAssetAttributes != null && mAssetAttributes.Count > 0)
                                        {
                                            var mAssetAttribute = mAssetAttributes.Where(x => x.Id == cardLine.AttributeId).FirstOrDefault();
                                            if (mAssetAttribute != null && mAssetAttribute.Id > 0 && mAssetAttribute.RepresentationCode != null)
                                            {
                                                attrParameterRepCode = mAssetAttribute.RepresentationCode;
                                            }

                                            if (mAssetAttribute != null && mAssetAttribute.Id > 0 && mAssetAttribute.ParameterRepresentationCode != null)
                                            {
                                                parameterTyprRepCode = mAssetAttribute.ParameterRepresentationCode;
                                            }

                                        }

                                        if (mAssetTypes != null && mAssetTypes.Count > 0)
                                        {
                                            var mAssetType = mAssetTypes.Where(x => x.Id == cardLine.AssetTypeId).FirstOrDefault();
                                            if (mAssetType != null && mAssetType.Id > 0 && mAssetType.RepresentationCode != null)
                                            {
                                                atypeParameterRepCode = mAssetType.RepresentationCode;
                                            }
                                        }

                                        string assetHexValue = Convert.ToString(assetCount, 16).ToUpper();

                                        var prid = $"{atypeParameterRepCode.MakeTwoChars()}{assetHexValue.MakeTwoChars()}{parameterTyprRepCode.MakeTwoChars()}{attrParameterRepCode.MakeTwoChars()}";

                                        var Parameters = new Domain.Parameter
                                        {
                                            AttributeId = cardLine.AttributeId.Value,
                                            Attribute = cardLine.AttributeName,
                                            Prid = prid,
                                            Prloc = cardLine.LocationName,
                                            ParameterRepCode = attrParameterRepCode,
                                            OriginalRole = cardLine.Role,
                                            A0TypeId = adcType,
                                            Address = cardLine.ADCNumber ?? 0,
                                            A10Pin = cardLine.Pin ?? 0,
                                            Tcp = SetParmsName(cardLine.ClusterName, mClusterConfigs)
                                        };
                                        mRailwayInfo.Parameters.Add(Parameters);

                                    }
                                }


                                mRailwayInfos.Add(mRailwayInfo);
                                assetCount++;
                            }


                        }

                    }
                }

                // Convert the string content to a byte array

            }

            return mRailwayInfos;
        }

        public ActionResult GetAssetById(int id)
        {
            Asset mAsset = new Asset();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Asset/GetAssetById/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAsset = JsonConvert.DeserializeObject<Asset>(jsonString);

                    }

                }
            }
            catch (Exception ex)
            {
            }
            return Json(mAsset);
        }
    }
}