using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Service;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using PayPal.Api;
using PdfSharp.Forms;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Text;
using System.Web.ApplicationServices;
using System.Web.Mvc;
using System.Web.UI.WebControls;
using static E7FRSAdvance.Utility.Utility;

namespace E7FRSAdvance.Controllers
{

    public class RdpmsViewModel
    {
        public List<ZoneA> Zones { get; set; } = new List<ZoneA>();
        public List<AssetTypeA> AssetTypes { get; set; } = new List<AssetTypeA>();
        public List<ParamType> ParamTypes { get; set; } = new List<ParamType>();
        public List<AssetAttributeA> ParameterRepresentations { get; set; } = new List<AssetAttributeA>();
        public Dictionary<string, List<ParamRepHelper>> ParamRepHelpers { get; set; } = new Dictionary<string, List<ParamRepHelper>>();
        public List<GatwayType> GatwayTypes { get; set; } = new List<GatwayType>();
    }

    public class ZoneA
    {
        public string Name { get; set; } = "";
        public string Code { get; set; } = "";
        public string Id { get; set; } = "";
        public int RDPMSId { get; set; }
        public List<DivisionA> Divisions { get; set; } = new List<DivisionA>();
    }

    public class DivisionA
    {
        public string Name { get; set; } = "";
        public string Code { get; set; } = "";
        public string Id { get; set; } = "";
        public int RDPMSId { get; set; }
    }

    public class AssetTypeA
    {
        public string Id { get; set; } = "";
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public int RDPMSId { get; set; }
        public List<AssetAttributeA> AssetAttribute { get; set; } = new List<AssetAttributeA>();
    }

    public class AssetAttributeA
    {
        public string Id { get; set; } = "";
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
    }

    public class ParamType
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
    }

    public class GatwayType
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
    }

    public class ParamRepHelper
    {
        public string Id { get; set; } = "";
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
    }
    [Authenticate]
    public class AnnexureAController : Controller
    {
        private readonly IZoneService _zoneService;
        private readonly IDivisionService _divisionService;
        private readonly ISiteService _siteService;
        private readonly IAssetTypeService _assetTypeService;
        private readonly IAssetAttributeService _assetAttributeService;
        private readonly ICardLineService _cardLineService;
        public AnnexureAController(IZoneService zoneService, IDivisionService divisionService, ISiteService siteService, IAssetAttributeService assetAttributeService, IAssetTypeService assetTypeService, ICardLineService cardLineService)
        {
            _divisionService = divisionService;
            _siteService = siteService;
            _zoneService = zoneService;
            _assetTypeService = assetTypeService;
            _cardLineService = cardLineService;
            _assetAttributeService = assetAttributeService;
        }

        public ActionResult Index()
        {
            var vm = new RdpmsViewModel
            {
                Zones = BuildZones(),
                AssetTypes = E7FRSAdvance.Utility.ExtensionMethod.BuildAssetTypes(),
                ParamTypes = E7FRSAdvance.Utility.ExtensionMethod.BuildParamTypes(),
                ParamRepHelpers = E7FRSAdvance.Utility.ExtensionMethod.BuildParamRepHelpers(),
                ParameterRepresentations = E7FRSAdvance.Utility.ExtensionMethod.BuildParameterRepresentation(),
                GatwayTypes = E7FRSAdvance.Utility.ExtensionMethod.BuildGatwayType()
            };
            if (vm != null && vm.Zones != null && vm.Zones.Count > 0)
            {
                var zones = _zoneService.GetAllZones();
                var mDivisions = _divisionService.GetAllDivisions();
                var assetTypes = _assetTypeService.GetAll();
                if (zones != null && zones.Count > 0 && mDivisions != null && mDivisions.Count > 0)
                {
                    foreach (var zone in vm.Zones)
                    {
                        var mzones = zones.Where(x => x.RepresentationCode != null && x.RepresentationCode == zone.Id).FirstOrDefault();
                        if (mzones != null && mzones.Id > 0)
                            zone.RDPMSId = mzones.Id;

                        foreach (var div in zone.Divisions)
                        {
                            if (zone.RDPMSId > 0)
                            {
                                var mDivision = mDivisions.Where(x => x.ZoneId == zone.RDPMSId && x.RepresentationCode != null && x.RepresentationCode == div.Id).FirstOrDefault();
                                if (mDivision != null && mDivision.Id > 0)
                                    div.RDPMSId = mDivision.Id;
                            }


                        }
                    }

                    foreach (var assetType in vm.AssetTypes)
                    {
                        var enumAttributeType = (from E7FRSAdvance.Utility.Utility.AttributeType e in Enum.GetValues(typeof(E7FRSAdvance.Utility.Utility.AttributeType))
                                                 let displayAttr = e.GetType()
                                                                     .GetField(e.ToString())
                                                                     .GetCustomAttribute<DisplayAttribute>()
                                                 select new
                                                 {
                                                     Id = (int)e,
                                                     Name = e.ToString().Replace("_", " "),
                                                     DisplayName = displayAttr.Name ?? e.ToString()
                                                 }).ToList();

                        var mAssetType = assetTypes.Where(x => x.RepresentationCode != null && x.RepresentationCode == assetType.Id).FirstOrDefault();
                        if (mAssetType != null && mAssetType.Id > 0)
                            assetType.RDPMSId = mAssetType.Id;
                        else
                        {
                            var signalAttr = enumAttributeType.Where(x => x.DisplayName == assetType.Id).FirstOrDefault();
                            if (signalAttr != null)
                            {
                                assetType.RDPMSId = (int)E7FRSAdvance.Utility.Utility.AssetType.SIGNAL;
                            }
                        }
                    }
                }

            }

            return View("Index", vm);
            //return View("IndexNew", vm);
        }

        public ActionResult _AssetPartial(int siteId)
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

            return PartialView(mRailwayInfos);
        }

        public ActionResult DownloadINI(int siteId, string sgid)
        {
            string iniFilePath = string.Empty;
            var deviceINIPath = Path.Combine(System.Configuration.ConfigurationManager.AppSettings.Get("UploadFilePath") + "DeviceINI", $"DeviceINI{DateTime.Now.Ticks}.txt");

            string json = string.Empty;
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


                    var assets = mCardLines.GroupBy(x => new { x.AssetId, x.AssetName, x.AssetTypeId }).Select(x => x.Key).ToList();
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

                        var mRailwayInfos = new List<Domain.RailwayInfo>();
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

                        var root = new Domain.Root
                        {
                            Stgwi = sgid,
                            Vcc = "XYZ",
                            Vgc = "ABC",
                            Sn = mSite.Name,
                            Info = mRailwayInfos
                        };
                        // Convert to JSON string
                        // json = JsonConvert.SerializeObject(root, Formatting.Indented);

                        var sb = new StringBuilder();

                        // --- CONFIG SECTION ---
                        sb.AppendLine("[config]");
                        sb.AppendLine($"STGWI={root.Stgwi}");
                        sb.AppendLine($"VCC={root.Vcc}");
                        sb.AppendLine($"VGC={root.Vgc}");
                        sb.AppendLine($"STATION_NAME={root.Sn}");
                        sb.AppendLine();

                        int assetIndex = 1;

                        // --- ASSET SECTIONS ---
                        foreach (var asset in root.Info)
                        {
                            string assetSection = $"asset_{asset.Asnc}";
                            sb.AppendLine($"[{assetSection}]");
                            sb.AppendLine($"ASNC={asset.Asnc}");
                            sb.AppendLine($"ASNI={asset.Asni}");
                            sb.AppendLine($"ASTC={asset.Astc}");
                            sb.AppendLine($"SMMS_ASSET_CODE={asset.SmmsAssetCode}");
                            sb.AppendLine();

                            int parameterIndex = 1;
                            foreach (var param in asset.Parameters)
                            {
                                sb.AppendLine($"[{assetSection}.parameter{parameterIndex}]");
                                sb.AppendLine($"PRID={param.Prid}");
                                sb.AppendLine($"PRLOC={(string.IsNullOrEmpty(param.Prloc) ? "" : param.Prloc)}");
                                sb.AppendLine($"PARAMETER_REP_CODE={param.ParameterRepCode}");
                                sb.AppendLine($"ORIGINAL_ROLE={param.OriginalRole}");
                                sb.AppendLine($"A0_TYPE_ID={param.A0TypeId}");
                                sb.AppendLine($"ADDRESS={param.Address}");
                                sb.AppendLine($"A10_PIN={param.A10Pin}");
                                sb.AppendLine($"TCP={param.Tcp}");
                                sb.AppendLine();

                                parameterIndex++;
                            }

                            assetIndex++;
                        }

                        iniFilePath = System.Web.HttpContext.Current.Server.MapPath("~/Upload/Temp/") + Path.GetFileName("output.ini");

                        System.IO.File.WriteAllText(iniFilePath, sb.ToString(), Encoding.UTF8);
                    }
                }

                // Convert the string content to a byte array

            }

            string fileName = "generated.ini";

            return File(iniFilePath, "text/plain", fileName);
        }

        public ActionResult DownloadJson(int siteId, string sgid)
        {
            string filePath = "";
            string json = "";

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

                    var assets = mCardLines
                                 .GroupBy(x => new { x.AssetId, x.AssetName, x.AssetTypeId })
                                 .Select(x => x.Key)
                                 .ToList();

                    if (assets != null && assets.Count > 0 &&
                        mDataloggerAssetMappings != null && mDataloggerAssetMappings.Count > 0)
                    {
                        // Assign AssetTypeId
                        foreach (var map in mDataloggerAssetMappings)
                        {
                            var assetTest = assets.FirstOrDefault(x => x.AssetId == map.AssetId);
                            if (assetTest != null && assetTest.AssetTypeId != null)
                            {
                                map.AssetTypeId = assetTest.AssetTypeId.Value;
                            }
                        }

                        // Generate hex codes per asset type
                        foreach (var typeGroup in mDataloggerAssetMappings
                                                   .GroupBy(x => x.AssetTypeId)
                                                   .Select(x => x.Key))
                        {
                            int dlAssetCount = 0;
                            foreach (var d in mDataloggerAssetMappings.Where(x => x.AssetTypeId == typeGroup))
                            {
                                string hex = Convert.ToString(dlAssetCount, 16).ToUpper();
                                d.Code = hex.MakeTwoChars();
                                dlAssetCount++;
                            }
                        }

                        // Build final railway info JSON
                        var mRailwayInfos = new List<Domain.RailwayInfo>();
                        int assetCount = 0;

                        foreach (var asset in assets)
                        {
                            var mD = mDataloggerAssetMappings
                                     .FirstOrDefault(x => x.AssetId == asset.AssetId);

                            if (mD == null)
                                continue;

                            string smmsAssetCode = mD.RailwayAssetcode;

                            // If point machine with 2 asset codes
                            if (asset.AssetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.POINT_MACHINE)
                            {
                                var list = mDataloggerAssetMappings
                                           .Where(x => x.AssetId == asset.AssetId &&
                                                   !string.IsNullOrEmpty(x.RailwayAssetcode))
                                           .ToList();

                                if (list.Count >= 2)
                                {
                                    smmsAssetCode = string.Join(",", list.Select(x => x.RailwayAssetcode));
                                }
                            }

                            var rInfo = new Domain.RailwayInfo();
                            rInfo.Asnc = mD.RailwayAssetName;
                            rInfo.Asni = mD.Code;
                            rInfo.Astc = mD.RailwayAssetTypeCode;
                            rInfo.SmmsAssetCode = smmsAssetCode;

                            var cardLines = mCardLines.Where(x => x.AssetId == asset.AssetId).ToList();

                            foreach (var cl in cardLines)
                            {
                                int adcType = 0;
                                if (cl.ADCTypeId == (int)E7FRSAdvance.Utility.Utility.ADCType.Special_1056) adcType = 1056;
                                else if (cl.ADCTypeId == (int)E7FRSAdvance.Utility.Utility.ADCType.Special_1507) adcType = 1507;
                                else if (cl.ADCTypeId == (int)E7FRSAdvance.Utility.Utility.ADCType.Normal) adcType = 1505;

                                string attrCode = "";
                                string parameterType = "";
                                string assetTypeCode = "";

                                var attr = mAssetAttributes.FirstOrDefault(x => x.Id == cl.AttributeId);
                                if (attr != null)
                                {
                                    attrCode = attr.RepresentationCode;
                                    parameterType = attr.ParameterRepresentationCode;
                                }

                                var aType = mAssetTypes.FirstOrDefault(x => x.Id == cl.AssetTypeId);
                                if (aType != null)
                                {
                                    assetTypeCode = aType.RepresentationCode;
                                }

                                string hexAsset = Convert.ToString(assetCount, 16).ToUpper().MakeTwoChars();

                                string prid = $"{assetTypeCode.MakeTwoChars()}{hexAsset}{parameterType.MakeTwoChars()}{attrCode.MakeTwoChars()}";

                                rInfo.Parameters.Add(new Domain.Parameter
                                {
                                    Prid = prid,
                                    Prloc = cl.LocationName,
                                    ParameterRepCode = attrCode,
                                    OriginalRole = cl.Role,
                                    A0TypeId = adcType,
                                    Address = cl.ADCNumber ?? 0,
                                    A10Pin = cl.Pin ?? 0,
                                    Tcp = SetParmsName(cl.ClusterName, mClusterConfigs)
                                });
                            }

                            mRailwayInfos.Add(rInfo);
                            assetCount++;
                        }

                        var root = new Domain.Root
                        {
                            Stgwi = sgid,
                            Vcc = "XYZ",
                            Vgc = "ABC",
                            Sn = mSite.Name,
                            Info = mRailwayInfos
                        };

                        // --- JSON SERIALIZATION ---
                        json = JsonConvert.SerializeObject(root, Formatting.Indented);

                        // --- WRITE TO TXT / JSON FILE ---
                        filePath = Server.MapPath("~/Upload/Temp/") + $"Site_{siteId}_config.txt";
                        System.IO.File.WriteAllText(filePath, json, Encoding.UTF8);
                    }
                }
            }

            return File(filePath, "text/plain", "generated_json.txt");
        }


        public ActionResult DownloadDLJson(int siteId, string sgid)
        {
            string iniFilePath = string.Empty;
            var deviceINIPath = Path.Combine(System.Configuration.ConfigurationManager.AppSettings.Get("UploadFilePath") + "DeviceINI", $"DeviceINI{DateTime.Now.Ticks}.txt");

            string json = string.Empty;
            var mSite = _siteService.Get(siteId);
            if (mSite != null && mSite.Id > 0 && mSite.IsDatalogger)
            {
                var mRailwayInfos = new List<Domain.RailwayInfo>();
                int assetCount = 0;
                {
                    var mDataloggerAttributes = GetAllAttribute();
                    var mAssetInfoDataloggers = GetUserAssetInfoDatalogger(siteId);
                    var mDataloggerAssetMappings = GetAssetBySiteId(siteId);
                    foreach (var assetId in mAssetInfoDataloggers.GroupBy(x => x.AssetId).Select(x => x.Key).ToList())
                    {

                        var mDataloggerAssetMapping = mDataloggerAssetMappings.Where(x => x.AssetId == assetId).FirstOrDefault();
                        if (mDataloggerAssetMapping != null)
                        {
                            string smmsAssetCode = mDataloggerAssetMapping.RailwayAssetcode;

                            //if (mAssetInfoDatalogger.AssetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.POINT_MACHINE)
                            //{
                            //    var dataloggerAssetMapping = mDataloggerAssetMappings.Where(x => x.AssetId == asset.AssetId && x.RailwayAssetcode != null && x.RailwayAssetcode != string.Empty).ToList();
                            //    if (dataloggerAssetMapping.Count >= 2)
                            //    {
                            //        smmsAssetCode = string.Join(",", dataloggerAssetMapping.Select(x => x.RailwayAssetcode).ToList());
                            //    }
                            //}

                            var mRailwayInfo = new Domain.RailwayInfo();
                            mRailwayInfo.Asnc = mDataloggerAssetMapping.AssetName;
                            mRailwayInfo.Asni = mDataloggerAssetMapping.Code;
                            mRailwayInfo.Astc = mDataloggerAssetMapping.RailwayAssetTypeCode;
                            mRailwayInfo.SmmsAssetCode = smmsAssetCode;


                            foreach (var mAssetInfoDatalogger in mAssetInfoDataloggers.Where(x => x.AssetId == assetId))
                            {
                                if (mAssetInfoDatalogger != null && mAssetInfoDatalogger.DataloggerAttributeId > 0)
                                {
                                    var mDataloggerAttribute = mDataloggerAttributes.Where(x => x.Id == mAssetInfoDatalogger.DataloggerAttributeId).FirstOrDefault();
                                    if (mDataloggerAttribute != null && mDataloggerAttribute.Id > 0 && mDataloggerAttribute.RepresentationCode.IsNotNullOrEmpty() && mDataloggerAttribute.AssetTypeRepresentationCode.IsNotNullOrEmpty())
                                    {
                                        string assetHexValue = Convert.ToString(assetCount, 16).ToUpper();
                                        assetCount++;

                                        string assetRepresentCode = string.Empty;
                                        string attrParameterRepCode = string.Empty;
                                        string atypeParameterRepCode = string.Empty;
                                        string parameterTyprRepCode = string.Empty;

                                        attrParameterRepCode = mDataloggerAttribute.RepresentationCode;
                                        parameterTyprRepCode = mDataloggerAttribute.ParameterRepresentationCode;
                                        atypeParameterRepCode = mDataloggerAttribute.AssetTypeRepresentationCode;


                                        var prid = $"{atypeParameterRepCode.MakeTwoChars()}{assetHexValue.MakeTwoChars()}{parameterTyprRepCode.MakeTwoChars()}{attrParameterRepCode.MakeTwoChars()}";

                                        var Parameters = new Domain.Parameter
                                        {
                                            astc = mDataloggerAssetMapping.AssetName,
                                            Prloc = "",
                                            Prid = prid,
                                            ParameterRepCode = attrParameterRepCode,
                                            type = "digital",
                                            channel = Convert.ToString(mAssetInfoDatalogger.Role)
                                        };

                                        mRailwayInfo.Parameters.Add(Parameters);

                                    }
                                }

                            }

                            mRailwayInfos.Add(mRailwayInfo);
                        }

                    }

                    var root = new Domain.Root
                    {
                        Stgwi = sgid,
                        Vcc = "XYZ",
                        Vgc = "ABC",
                        Sn = mSite.Name,
                        Info = mRailwayInfos
                    };
                    // Convert to JSON string
                    // json = JsonConvert.SerializeObject(root, Formatting.Indented);

                    var sb = new StringBuilder();

                    // --- CONFIG SECTION ---
                    sb.AppendLine("[config]");
                    sb.AppendLine($"STGWI={root.Stgwi}");
                    sb.AppendLine($"VCC={root.Vcc}");
                    sb.AppendLine($"VGC={root.Vgc}");
                    sb.AppendLine($"STATION_NAME={root.Sn}");
                    sb.AppendLine();

                    int parameterIndex = 1;

                    foreach (var asset in root.Info)
                    {
                        string assetSection = $"asset_{asset.Asnc}";
                        sb.AppendLine($"[{assetSection}]");
                        sb.AppendLine($"ASNC={asset.Asnc}");
                        sb.AppendLine($"ASNI={asset.Asni}");
                        sb.AppendLine($"ASTC={asset.Astc}");
                        sb.AppendLine($"SMMS_ASSET_CODE={asset.SmmsAssetCode}");
                        sb.AppendLine();

                        foreach (var param in asset.Parameters)
                        {
                            sb.AppendLine($"[{assetSection}.parameter{parameterIndex}]");
                            sb.AppendLine($"PRID={param.Prid}");
                            sb.AppendLine($"PRLOC=");
                            sb.AppendLine($"PARAMETER_REP_CODE={param.ParameterRepCode}");
                            sb.AppendLine($"type={param.type}");
                            sb.AppendLine($"channel={param.channel.Trim()}");
                            sb.AppendLine();

                        }
                        parameterIndex++;
                    }


                    // --- ASSET SECTIONS ---
                    //foreach (var param in root.Info)
                    //{
                    //    string assetSection = $"asset_{param.Astc}";
                    //    sb.AppendLine($"[{assetSection}.parameter{parameterIndex}]");
                    //    sb.AppendLine($"PRID={param.Prid}");
                    //    sb.AppendLine($"PRLOC=");
                    //    sb.AppendLine($"PARAMETER_REP_CODE={param.ParameterRepCode}");
                    //    sb.AppendLine($"type={param.type}");
                    //    sb.AppendLine($"channel={param.channel}");
                    //    sb.AppendLine();

                    //    parameterIndex++;
                    //}

                    iniFilePath = System.Web.HttpContext.Current.Server.MapPath("~/Upload/Temp/") + Path.GetFileName("dloutput.ini");

                    System.IO.File.WriteAllText(iniFilePath, sb.ToString(), Encoding.UTF8);
                }

                // Convert the string content to a byte array

            }

            string fileName = "generated_config.ini";

            return File(iniFilePath, "text/plain", fileName);
        }

        // ---- Seed data (from your React constants) ----
        public static List<ZoneA> BuildZones() => new List<ZoneA>()
        {
            new ZoneA{ Name="CENTRAL RAILWAY", Code="CR", Id="00", Divisions=new List<DivisionA>{
                new DivisionA{ Name="BHUSAVAL", Code="BSL", Id="00"},
                new DivisionA{ Name="MUMBAI CSTM", Code="CSTM", Id="01"},
                new DivisionA{ Name="NAGPUR", Code="NGP", Id="02"},
                new DivisionA{ Name="PUNE", Code="PUNE", Id="03"},
                new DivisionA{ Name="SOLAPUR", Code="SUR", Id="04"},
            }},
            new ZoneA{ Name="EAST CENTRAL RAILWAY", Code="ECR", Id="01", Divisions=new List<DivisionA>{
                new DivisionA{ Name="DANAPUR", Code="DNR", Id="00"},
                new DivisionA{ Name="DHANBAD", Code="DHN", Id="01"},
                new DivisionA{ Name="PT.DEEN DAYAL UPADHYAYA", Code="DDU", Id="02"},
                new DivisionA{ Name="SAMASTIPUR", Code="SPJ", Id="03"},
                new DivisionA{ Name="SONPUR", Code="SEE", Id="04"},
            }},
            new ZoneA{ Name="EAST COAST RAILWAY", Code="ECoR", Id="02", Divisions=new List<DivisionA>{
                new DivisionA{ Name="KHURDA ROAD", Code="KUR", Id="00"},
                new DivisionA{ Name="SAMBALPUR", Code="SBP", Id="01"},
                new DivisionA{ Name="WALTAIR", Code="WAT", Id="02"},
            }},
            new ZoneA{ Name="EASTERN RAILWAY", Code="ER", Id="03", Divisions=new List<DivisionA>{
                new DivisionA{ Name="ASANSOL", Code="ASN", Id="00"},
                new DivisionA{ Name="HOWRAH", Code="HWH", Id="01"},
                new DivisionA{ Name="MALDA", Code="MLDT", Id="02"},
                new DivisionA{ Name="SEALDAH", Code="SDAH", Id="03"},
            }},
            new ZoneA{ Name="NORTH CENTRAL RAILWAY", Code="NCR", Id="04", Divisions=new List<DivisionA>{
                new DivisionA{ Name="AGRA", Code="AGRA", Id="00"},
                new DivisionA{ Name="JHANSI", Code="JHS", Id="01"},
                new DivisionA{ Name="PRAYAGRAJ", Code="PYRJ", Id="02"},
            }},
            new ZoneA{ Name="NORTH EASTERN RAILWAY", Code="NER", Id="05", Divisions=new List<DivisionA>{
                new DivisionA{ Name="IZZATNAGAR", Code="IZN", Id="00"},
                new DivisionA{ Name="LUCKNOW", Code="LJN", Id="01"},
                new DivisionA{ Name="VARANASI", Code="BSB", Id="02"},
            }},
            new ZoneA{ Name="NORTH FRONTIER RAILWAY", Code="NFR", Id="06", Divisions=new List<DivisionA>{
                new DivisionA{ Name="ALIPURDUAR", Code="APD", Id="00"},
                new DivisionA{ Name="KATIHAR", Code="KIR", Id="01"},
                new DivisionA{ Name="LUMDING", Code="LMG", Id="02"},
                new DivisionA{ Name="RANGIYA", Code="RNY", Id="03"},
                new DivisionA{ Name="TINSUKIA", Code="TSK", Id="04"},
            }},
            new ZoneA{ Name="NORTHERN RAILWAY", Code="NR", Id="07", Divisions=new List<DivisionA>{
                new DivisionA{ Name="AMBALA", Code="UMB", Id="00"},
                new DivisionA{ Name="DELHI", Code="DLI", Id="01"},
                new DivisionA{ Name="FEROZPUR", Code="FZP", Id="02"},
                new DivisionA{ Name="LUCKNOW", Code="LKO", Id="03"},
                new DivisionA{ Name="MORADABAD", Code="MB", Id="04"},
            }},
            new ZoneA{ Name="NORTH WESTERN RAILWAY", Code="NWR", Id="08", Divisions=new List<DivisionA>{
                new DivisionA{ Name="AJMER", Code="AII", Id="00"},
                new DivisionA{ Name="BIKANER", Code="BKN", Id="01"},
                new DivisionA{ Name="JAIPUR", Code="JP", Id="02"},
                new DivisionA{ Name="JODHPUR", Code="JU", Id="03"},
            }},
            new ZoneA{ Name="SOUTH CENTRAL RAILWAY", Code="SCR", Id="09", Divisions=new List<DivisionA>{
                new DivisionA{ Name="GUNTAKAL", Code="GTL", Id="00"},
                new DivisionA{ Name="GUNTUR", Code="JNT", Id="01"},
                new DivisionA{ Name="HYDERABAD", Code="HYB", Id="02"},
                new DivisionA{ Name="NANDED", Code="NED", Id="03"},
                new DivisionA{ Name="SECUNDERABAD", Code="SC", Id="04"},
                new DivisionA{ Name="VIJAYAWADA", Code="BZA", Id="05"},
            }},
            new ZoneA{ Name="SOUTH EAST CENTRAL RAILWAY", Code="SECR", Id="0A", Divisions=new List<DivisionA>{
                new DivisionA{ Name="BILASPUR", Code="BSP", Id="00"},
                new DivisionA{ Name="NAGPUR", Code="NGP", Id="01"},
                new DivisionA{ Name="RAIPUR", Code="R", Id="02"},
            }},
            new ZoneA{ Name="SOUTH EASTERN RAILWAY", Code="SER", Id="0B", Divisions=new List<DivisionA>{
                new DivisionA{ Name="ADRA", Code="ADRA", Id="00"},
                new DivisionA{ Name="CHAKARDHARPUR", Code="CKP", Id="01"},
                new DivisionA{ Name="KHARAGPUR", Code="KGP", Id="02"},
                new DivisionA{ Name="RANCHI", Code="RNC", Id="03"},
            }},
            new ZoneA{ Name="SOUTHERN RAILWAY", Code="SR", Id="0C", Divisions=new List<DivisionA>{
                new DivisionA{ Name="CHENNAI", Code="MAS", Id="00"},
                new DivisionA{ Name="MADURAI", Code="MDU", Id="01"},
                new DivisionA{ Name="PALAKKAD", Code="PGT", Id="02"},
                new DivisionA{ Name="SALEM", Code="SA", Id="03"},
                new DivisionA{ Name="THIRUVANANTHAPURAM", Code="TVC", Id="04"},
                new DivisionA{ Name="TIRUCHCHIRAPPALLI", Code="TPJ", Id="05"},
            }},
            new ZoneA{ Name="SOUTH WESTERN RAILWAY", Code="SWR", Id="0D", Divisions=new List<DivisionA>{
                new DivisionA{ Name="BENGALURU", Code="SBC", Id="00"},
                new DivisionA{ Name="HUBBALLI", Code="UBL", Id="01"},
                new DivisionA{ Name="MYSURU", Code="MYS", Id="02"},
            }},
            new ZoneA{ Name="WEST CENTRAL RAILWAY", Code="WCR", Id="0E", Divisions=new List<DivisionA>{
                new DivisionA{ Name="BHOPAL", Code="BPL", Id="00"},
                new DivisionA{ Name="JABALPUR", Code="JBP", Id="01"},
                new DivisionA{ Name="KOTA", Code="KOTA", Id="02"},
            }},
            new ZoneA{ Name="WESTERN RAILWAY", Code="WR", Id="0F", Divisions=new List<DivisionA>{
                new DivisionA{ Name="AHEMDABAD", Code="ADI", Id="00"},
                new DivisionA{ Name="BHAVNAGAR", Code="BVC", Id="01"},
                new DivisionA{ Name="MUMBAI CENTRAL", Code="BCT", Id="02"},
                new DivisionA{ Name="RAJKOT", Code="RJT", Id="03"},
                new DivisionA{ Name="RATLAM", Code="RTM", Id="04"},
                new DivisionA{ Name="VADODARA", Code="BRC", Id="05"},
            }},
        };

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
                //var mClusterConfig = clusterConfigRepo.GetBy(siteId, ClusterNames[0]);
                //if (mClusterConfig != null && mClusterConfig.Id > 0)
                //{
                //    parmsName = mClusterConfig.Param;
                //}

                //if (parmsNames != null && parmsNames.Length > 1)
                //{
                //    parmsName = parmsNames[1];
                //}

            }

            return parmsName;
        }

        public JsonResult GetSiteByDivisionId(int divisionId)
        {
            var mSites = new List<Domain.Site>();
            try
            {
                mSites = _siteService.GetBy(divisionId);
                if (mSites != null && mSites.Count > 0)
                    mSites = mSites.OrderBy(x => x.Id).ToList();
            }
            catch (Exception)
            {
                mSites = new List<Domain.Site>();
            }
            return Json(mSites, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetAssetByTypeId(int siteId, int typeId)
        {
            var mAssets = new List<Domain.DataloggerAssetMapping>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"DataloggerAssetMapping/SiteId/{siteId}/AssetTypeId/{typeId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssets = JsonConvert.DeserializeObject<List<Domain.DataloggerAssetMapping>>(jsonString);
                        if (mAssets != null && mAssets.Count > 0)
                            mAssets = mAssets.OrderBy(x => x.Id).ToList();
                    }
                }
            }
            catch (Exception ex)
            {
            }
            return Json(mAssets, JsonRequestBehavior.AllowGet);
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

        public ActionResult DownloadSMMSFile(int siteId)
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

                                // If API requires body, uncomment this line
                                // request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

                                var response = client.SendAsync(request).GetAwaiter().GetResult();
                                response.EnsureSuccessStatusCode();

                                string result = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                                if (response.StatusCode == HttpStatusCode.OK)
                                {
                                    mRailwayAsset = JsonConvert.DeserializeObject<RailwayAsset>(result);
                                    string iniText = ConvertToIni(mRailwayAsset);

                                    var iniFilePath = System.Web.HttpContext.Current.Server.MapPath("~/Upload/Temp/") + Path.GetFileName("smmsoutput.ini");

                                    System.IO.File.WriteAllText(iniFilePath, iniText, Encoding.UTF8);

                                    return File(iniFilePath, "text/plain", "SMMS.ini");
                                }

                            }
                            catch (Exception ex)
                            {

                            }
                        }
                    }
                }
            }

            return RedirectToAction("Index");
        }

        static string ConvertToIni(RailwayAsset asset)
        {
            StringBuilder sb = new StringBuilder();

            // Header info
            sb.AppendLine($"zc={asset.zc}");
            sb.AppendLine($"dc={asset.dc}");
            sb.AppendLine($"sc={asset.sc}");
            sb.AppendLine();

            // Loop over info list
            int infoIndex = 1;
            foreach (var info in asset.info)
            {
                sb.AppendLine($"[info_{infoIndex}]");
                sb.AppendLine($"smms_asset_number={info.smms_asset_number}");
                sb.AppendLine($"smms_asset_code={info.smms_asset_code}");

                int addIndex = 1;
                foreach (var add in info.additional_info)
                {
                    sb.AppendLine($"additional_info_{addIndex}_{add.key}={add.value}");
                    addIndex++;
                }

                sb.AppendLine(); // empty line between info sections
                infoIndex++;
            }

            return sb.ToString();
        }

        private List<Domain.DataloggerAssetMapping> GetAssetCode(int siteId)
        {
            var mDataloggerAssetMappings = new List<Domain.DataloggerAssetMapping>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"DataloggerAssetMapping/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mDataloggerAssetMappings = JsonConvert.DeserializeObject<List<Domain.DataloggerAssetMapping>>(jsonString);

                    }
                }
            }
            catch (Exception ex)
            {
            }
            return mDataloggerAssetMappings;
        }

        private List<Domain.DataloggerFileFormat> GetDataloggerFileFormat(int siteId)
        {
            var mDataloggerFileFormats = new List<Domain.DataloggerFileFormat>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"DataloggerFileFormat/siteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mDataloggerFileFormats = JsonConvert.DeserializeObject<List<Domain.DataloggerFileFormat>>(jsonString);
                    }

                }
            }
            catch (Exception ex)
            {
            }
            return mDataloggerFileFormats;
        }

        private List<Domain.AssetInfoDatalogger> GetUserAssetInfoDatalogger(int siteId)
        {
            var mDataloggerFileFormats = new List<Domain.AssetInfoDatalogger>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"AssetInfoDatalogger/GetUserAssetInfoDatalogger/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mDataloggerFileFormats = JsonConvert.DeserializeObject<List<Domain.AssetInfoDatalogger>>(jsonString);
                    }

                }
            }
            catch (Exception ex)
            {
            }
            return mDataloggerFileFormats;
        }

        private ActionResult GetDataloggerAttribute(int id)
        {
            List<Domain.DataloggerAttribute> mAssetAttributes = new List<Domain.DataloggerAttribute>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("DataloggerAssetType/GetDataloggerAttribute/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssetAttributes = JsonConvert.DeserializeObject<List<Domain.DataloggerAttribute>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return Json(mAssetAttributes);
        }

        private List<Domain.DataloggerAttribute> GetAllAttribute()
        {
            List<Domain.DataloggerAttribute> mDataloggers = new List<Domain.DataloggerAttribute>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("DataloggerAttribute/GetAll")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mDataloggers = JsonConvert.DeserializeObject<List<Domain.DataloggerAttribute>>(jsonString);
                    }
                    else
                    {
                    }
                }
            }
            catch (Exception ex)
            {
            }
            return mDataloggers;
        }
    }

}