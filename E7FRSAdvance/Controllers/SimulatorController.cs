using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Service;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;

namespace E7FRSAdvance.Controllers
{
    //  [Authenticate]
    public class SimulatorController : Controller
    {
        // GET: Simulator
        private readonly IAssetService assetService;
        private readonly IAssetTypeService assetTypeService;
        private readonly IAssetAttributeService assetAttributeService;
        public SimulatorController(IAssetService assetService, IAssetTypeService assetTypeService, IAssetAttributeService assetAttributeService)
        {
            this.assetService = assetService;
            this.assetTypeService = assetTypeService;
            this.assetAttributeService = assetAttributeService;
        }

        public ActionResult Index(int siteId = 0)
        {
            Site mSite = new Site();

            if (siteId == 150)
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
                catch (Exception ex)
                {
                    ViewBag.Type = "Error";
                    ViewBag.Message = "Internal server error!";
                }
            }
            else
            {
                return RedirectToAction("Index", "Site");
            }

            return View(mSite);
        }

        public ActionResult _GetAssetType(int siteId)
        {
            List<Domain.AssetType> mAssetType = new List<Domain.AssetType>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("AssetType/GetAllAssestType/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssetType = JsonConvert.DeserializeObject<List<Domain.AssetType>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView(mAssetType);
        }

        public ActionResult _GetAssetDetails(AssetLister mAssetLister)
        {
            mAssetLister.Pager.Take = -1;// mAssetLister.Pager.PageSize;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
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

                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            finally
            {
                ViewBag.DefaultValues = GetDefaultValues(mAssetLister.SearchCriteria.SiteId);

            }
            return PartialView("~/Views/Simulator/_GetAssetDetails.cshtml", mAssetLister);
        }

        [HttpPost]
        public ActionResult UploadPointEventFile(int siteId)
        {
            dynamic data = new ExpandoObject();
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
                        UploadFile mAutCalINI = new UploadFile() { FileBase64 = thePictureDataAsString, SiteId = siteId };

                        var jsonStr = JsonConvert.SerializeObject(mAutCalINI);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("PLCConfig/UploadPointEventFile"), str).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.NoContent)
                        {
                            data = new { type = "success", result = "Point Event File has been uploaded." };

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

            return Json(data, JsonRequestBehavior.AllowGet);
        }

        public ActionResult UploadDataLoggerEventFile(UploadFile mUploadFile)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mUploadFile);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("PLCConfig/UploadDataLoggerEventFile"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "DataLogger has been saved." };
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

        public ActionResult _PointFunction(int siteId)
        {
            var assets = assetService.GetAssestBy(siteId, (int)E7FRSAdvance.Utility.Utility.AssetType.POINT_MACHINE);
            if (assets != null && assets.Count > 0)
                ViewBag.PointAssets = new SelectList(assets, "Id", "Name");

            //var enumData = from E7FRSAdvance.Utility.Utility.PointAlertAlias e in Enum.GetValues(typeof(E7FRSAdvance.Utility.Utility.PointAlertAlias))
            //               select new
            //               {
            //                   Id = (int)e,
            //                   Name = e.ToString().Replace("_", " ")
            //               };

            //ViewBag.PointAlertAlias = enumData;

            return PartialView();
        }

        public ActionResult _AlertFunction(int siteId)
        {
            var assets = assetTypeService.GetBy(siteId);
            if (assets != null && assets.Count > 0)
                ViewBag.SiteId = siteId;
            ViewBag.AlertAssetType = new SelectList(assets, "Id", "Name");

            return PartialView();
        }

        public JsonResult GetAssetByAssetTypeId(int assetTypeId, int siteId)
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
                throw ex;
            }
            return Json(mAssets, JsonRequestBehavior.AllowGet);
        }

        public ActionResult SaveDefaultValue(List<Domain.DefaultValue> defaultValues)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(defaultValues);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("DefaultValue"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Default Value has been saved." };
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

        public List<Domain.DefaultValue> GetDefaultValues(int siteId)
        {
            List<DefaultValue> mDefaultValues = new List<DefaultValue>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("DefaultValue/SiteId/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mDefaultValues = JsonConvert.DeserializeObject<List<DefaultValue>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mDefaultValues;
        }

        public ActionResult UploadPointEventFileData(UploadFile mUploadFile)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mUploadFile);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("PLCConfig/UploadPointEventFileData"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Point data has been saved." };
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

        public ActionResult UpdateDefaultValue(Domain.DefaultValue mDefaultValue)
        {
            dynamic data = new ExpandoObject();
            try
            {
                var mDefaultValues = GetDefaultValues(mDefaultValue);
                if (mDefaultValues != null && mDefaultValues.Count > 0)
                {
                    try
                    {
                        using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                        {
                            var jsonStr = JsonConvert.SerializeObject(mDefaultValues);
                            StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                            var response = hcf.client.PostAsync(String.Format("DefaultValue"), str).Result;
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            if (response.StatusCode == HttpStatusCode.NoContent)
                            {
                                data = new { type = "success", result = "Alert function has been updated." };
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
                }
                else
                {
                    data = new { type = "error", result = "Internal server error." };
                }
            }
            catch (Exception ex)
            {

                data = new { type = "error", result = ex.Message };
            }


            return Json(data);
        }

        public List<Domain.DefaultValue> GetDefaultValues(Domain.DefaultValue mDefaultValue)
        {
            var mDefaultValues = new List<Domain.DefaultValue>();
            var mDataloggerAttributes = GetAllAttribute();

            #region Set Track Default value

            if (mDefaultValue.AssetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.TRACK)
            {
                var mAssetAttributes = assetAttributeService.GetAssetAttributesBy(mDefaultValue.AssetTypeId);
                if (mAssetAttributes != null && mAssetAttributes.Count > 0)
                {
                    var dlTpr = mDataloggerAttributes.Where(x => x.Title == "TPR").FirstOrDefault();
                    var tprAttr = mAssetAttributes.Where(x => x.Title == "TPR V").FirstOrDefault();
                    var chargermAAttr = mAssetAttributes.Where(x => x.Title == "Charger mA").FirstOrDefault();
                    var ifmAAttr = mAssetAttributes.Where(x => x.Title == "If mA").FirstOrDefault();
                    var irmAAttr = mAssetAttributes.Where(x => x.Title == "Ir mA").FirstOrDefault();
                    var chargerVAttr = mAssetAttributes.Where(x => x.Title == "Charger V").FirstOrDefault();
                    var chargerOPVAttr = mAssetAttributes.Where(x => x.Title == "Charger OP V").FirstOrDefault();
                    var vrAttr = mAssetAttributes.Where(x => x.Title == "Vr").FirstOrDefault();
                    var chokeV = mAssetAttributes.Where(x => x.Title == "Choke V").FirstOrDefault();
                    var vF = mAssetAttributes.Where(x => x.Title == "Vf").FirstOrDefault();
                    var lastUpdateFeedEnd = mAssetAttributes.Where(x => x.Title.Contains("Last Update Feed End")).FirstOrDefault();
                    var tprVLoc = mAssetAttributes.Where(x => x.Title == "TPR V (Loc)").FirstOrDefault();
                    var lastUpdateRelayEnd = mAssetAttributes.Where(x => x.Title.Contains("Last Update Relay End")).FirstOrDefault();
                    var trVRelay = mAssetAttributes.Where(x => x.Title == "TR V (Relay)").FirstOrDefault();

                    //Set Default value
                    #region Set Default value


                    if (dlTpr != null && dlTpr.Id > 0)
                        mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, DataLoggerAttributeId = dlTpr.Id, Value = 0 });
                    if (tprAttr != null && tprAttr.Id > 0)
                        mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = tprAttr.Id, Value = 26.7m });
                    if (ifmAAttr != null && ifmAAttr.Id > 0)
                        mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = ifmAAttr.Id, Value = 269.4m });
                    if (chargermAAttr != null && chargermAAttr.Id > 0)
                        mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = chargermAAttr.Id, Value = 962.4m });
                    if (irmAAttr != null && irmAAttr.Id > 0)
                        mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = irmAAttr.Id, Value = 235.3m });
                    if (chargerVAttr != null && chargerVAttr.Id > 0)
                        mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = chargerVAttr.Id, Value = 116.1m });
                    if (vrAttr != null && vrAttr.Id > 0)
                        mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = vrAttr.Id, Value = 2.6m });
                    if (chokeV != null && chokeV.Id > 0)
                        mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = chokeV.Id, Value = 0.9m });
                    if (chargerOPVAttr != null && chargerOPVAttr.Id > 0)
                        mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = chargerOPVAttr.Id, Value = 4.50m });
                    if (lastUpdateFeedEnd != null && lastUpdateFeedEnd.Id > 0)
                        mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = lastUpdateFeedEnd.Id, Value = 7.1m });
                    if (tprVLoc != null && tprVLoc.Id > 0)
                        mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = tprVLoc.Id, Value = 29.0m });
                    if (lastUpdateRelayEnd != null && lastUpdateRelayEnd.Id > 0)
                        mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = lastUpdateRelayEnd.Id, Value = 7.1m });
                    if (trVRelay != null && trVRelay.Id > 0)
                        mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = trVRelay.Id, Value = 2.65m });
                    if (vF != null && vF.Id > 0)
                        mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = vF.Id, Value = 3.04m });
                    #endregion

                    if (mDefaultValue.ConditionTypeId == 1)
                    {
                        if (mDefaultValue.CauseCode == "TC TFC I/P LOW")
                        {
                            if (tprAttr != null && tprAttr.Id > 0)
                            {
                                var tprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == tprAttr.Id).FirstOrDefault();
                                if (tprDefaultValue != null)
                                    tprDefaultValue.Value = 25;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = tprAttr.Id, Value = 25 });
                            }


                            if (chargerVAttr != null && chargerVAttr.Id > 0)
                            {
                                var chargerVDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == chargerVAttr.Id).FirstOrDefault();
                                if (chargerVDefaultValue != null)
                                    chargerVDefaultValue.Value = 70;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = chargerVAttr.Id, Value = 70 });
                            }


                            //Datalogger
                            if (dlTpr != null && dlTpr.Id > 0)
                            {
                                var dlTprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.DataLoggerAttributeId == dlTpr.Id).FirstOrDefault();
                                if (dlTprDefaultValue != null)
                                    dlTprDefaultValue.Value = 0;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, DataLoggerAttributeId = dlTpr.Id, Value = 0 });
                            }

                        }
                        else if (mDefaultValue.CauseCode == "TC TFC O/P OFF")
                        {
                            if (tprAttr != null && tprAttr.Id > 0)
                            {
                                var tprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == tprAttr.Id).FirstOrDefault();
                                if (tprDefaultValue != null)
                                    tprDefaultValue.Value = 25;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = tprAttr.Id, Value = 25 });
                            }


                            if (chargermAAttr != null && chargermAAttr.Id > 0)
                            {
                                var chargermADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == chargermAAttr.Id).FirstOrDefault();
                                if (chargermADefaultValue != null)
                                    chargermADefaultValue.Value = 45;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = chargermAAttr.Id, Value = 45 });
                            }


                            //Datalogger
                            if (dlTpr != null && dlTpr.Id > 0)
                            {
                                var dlTprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.DataLoggerAttributeId == dlTpr.Id).FirstOrDefault();
                                if (dlTprDefaultValue != null)
                                    dlTprDefaultValue.Value = 0;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, DataLoggerAttributeId = dlTpr.Id, Value = 0 });
                            }

                        }
                        else if (mDefaultValue.CauseCode == "TC TFC O/P LOW")
                        {
                            if (tprAttr != null && tprAttr.Id > 0)
                            {
                                var tprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == tprAttr.Id).FirstOrDefault();
                                if (tprDefaultValue != null)
                                    tprDefaultValue.Value = 25;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = tprAttr.Id, Value = 25 });
                            }

                            if (chargerVAttr != null && chargerVAttr.Id > 0)
                            {
                                var chargerVDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == chargerVAttr.Id).FirstOrDefault();
                                if (chargerVDefaultValue != null)
                                    chargerVDefaultValue.Value = 95;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = chargerVAttr.Id, Value = 95 });
                            }

                            if (chargerOPVAttr != null && chargerOPVAttr.Id > 0)
                            {
                                var chargerOPVDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == chargerOPVAttr.Id).FirstOrDefault();
                                if (chargerOPVDefaultValue != null)
                                    chargerOPVDefaultValue.Value = 3.2m;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = chargerOPVAttr.Id, Value = 3.2m });
                            }

                            //Datalogger
                            if (dlTpr != null && dlTpr.Id > 0)
                            {
                                var dlTprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.DataLoggerAttributeId == dlTpr.Id).FirstOrDefault();
                                if (dlTprDefaultValue != null)
                                    dlTprDefaultValue.Value = 0;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, DataLoggerAttributeId = dlTpr.Id, Value = 0 });
                            }

                        }
                        else if (mDefaultValue.CauseCode == "TC BT CHG CUR HIGH")
                        {
                            if (tprAttr != null && tprAttr.Id > 0)
                            {
                                var tprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == tprAttr.Id).FirstOrDefault();
                                if (tprDefaultValue != null)
                                    tprDefaultValue.Value = 25;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = tprAttr.Id, Value = 25 });
                            }


                            if (chargermAAttr != null && chargermAAttr.Id > 0)
                            {
                                var chargermADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == chargermAAttr.Id).FirstOrDefault();
                                if (chargermADefaultValue != null)
                                    chargermADefaultValue.Value = 2000;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = chargermAAttr.Id, Value = 2000 });
                            }


                            if (ifmAAttr != null && ifmAAttr.Id > 0)
                            {
                                var ifmADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == ifmAAttr.Id).FirstOrDefault();
                                if (ifmADefaultValue != null)
                                    ifmADefaultValue.Value = 100;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = ifmAAttr.Id, Value = 100 });
                            }


                            //Datalogger
                            if (dlTpr != null && dlTpr.Id > 0)
                            {
                                var dlTprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.DataLoggerAttributeId == dlTpr.Id).FirstOrDefault();
                                if (dlTprDefaultValue != null)
                                    dlTprDefaultValue.Value = 0;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, DataLoggerAttributeId = dlTpr.Id, Value = 0 });
                            }

                        }
                        else if (mDefaultValue.CauseCode == "TC BT CHG CUR LOW")
                        {
                            if (tprAttr != null && tprAttr.Id > 0)
                            {
                                var tprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == tprAttr.Id).FirstOrDefault();
                                if (tprDefaultValue != null)
                                    tprDefaultValue.Value = 25;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = tprAttr.Id, Value = 25 });
                            }


                            if (chargermAAttr != null && chargermAAttr.Id > 0)
                            {
                                var chargermADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == chargermAAttr.Id).FirstOrDefault();
                                if (chargermADefaultValue != null)
                                    chargermADefaultValue.Value = 200;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = chargermAAttr.Id, Value = 200 });
                            }


                            if (ifmAAttr != null && ifmAAttr.Id > 0)
                            {
                                var ifmADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == ifmAAttr.Id).FirstOrDefault();
                                if (ifmADefaultValue != null)
                                    ifmADefaultValue.Value = 180;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = ifmAAttr.Id, Value = 180 });
                            }


                            //Datalogger
                            if (dlTpr != null && dlTpr.Id > 0)
                            {
                                var dlTprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.DataLoggerAttributeId == dlTpr.Id).FirstOrDefault();
                                if (dlTprDefaultValue != null)
                                    dlTprDefaultValue.Value = 0;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, DataLoggerAttributeId = dlTpr.Id, Value = 0 });
                            }

                        }
                        else if (mDefaultValue.CauseCode == "TC BT CHG CUR OFF")
                        {
                            if (tprAttr != null && tprAttr.Id > 0)
                            {
                                var tprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == tprAttr.Id).FirstOrDefault();
                                if (tprDefaultValue != null)
                                    tprDefaultValue.Value = 25;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = tprAttr.Id, Value = 25 });
                            }


                            if (chargermAAttr != null && chargermAAttr.Id > 0)
                            {
                                var chargermADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == chargermAAttr.Id).FirstOrDefault();
                                if (chargermADefaultValue != null)
                                    chargermADefaultValue.Value = 200;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = chargermAAttr.Id, Value = 200 });
                            }


                            if (ifmAAttr != null && ifmAAttr.Id > 0)
                            {
                                var ifmADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == ifmAAttr.Id).FirstOrDefault();
                                if (ifmADefaultValue != null)
                                    ifmADefaultValue.Value = 250;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = ifmAAttr.Id, Value = 250 });
                            }


                            //Datalogger
                            if (dlTpr != null && dlTpr.Id > 0)
                            {
                                var dlTprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.DataLoggerAttributeId == dlTpr.Id).FirstOrDefault();
                                if (dlTprDefaultValue != null)
                                    dlTprDefaultValue.Value = 0;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, DataLoggerAttributeId = dlTpr.Id, Value = 0 });
                            }

                        }
                        else if (mDefaultValue.CauseCode == "TC BLST/SLPR RES LOW")
                        {
                            if (tprAttr != null && tprAttr.Id > 0)
                            {
                                var tprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == tprAttr.Id).FirstOrDefault();
                                if (tprDefaultValue != null)
                                    tprDefaultValue.Value = 25;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = tprAttr.Id, Value = 25 });
                            }


                            if (ifmAAttr != null && ifmAAttr.Id > 0)
                            {
                                var ifmADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == ifmAAttr.Id).FirstOrDefault();
                                if (ifmADefaultValue != null)
                                    ifmADefaultValue.Value = 600;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = ifmAAttr.Id, Value = 600 });
                            }


                            if (irmAAttr != null && irmAAttr.Id > 0)
                            {
                                var irmADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == irmAAttr.Id).FirstOrDefault();
                                if (irmADefaultValue != null)
                                    irmADefaultValue.Value = 50;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = irmAAttr.Id, Value = 50 });
                            }


                            //Datalogger
                            if (dlTpr != null && dlTpr.Id > 0)
                            {
                                var dlTprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.DataLoggerAttributeId == dlTpr.Id).FirstOrDefault();
                                if (dlTprDefaultValue != null)
                                    dlTprDefaultValue.Value = 0;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, DataLoggerAttributeId = dlTpr.Id, Value = 0 });
                            }

                        }
                        else if (mDefaultValue.CauseCode == "TC OVER ENERIZATION")
                        {
                            if (tprAttr != null && tprAttr.Id > 0)
                            {
                                var tprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == tprAttr.Id).FirstOrDefault();
                                if (tprDefaultValue != null)
                                    tprDefaultValue.Value = 25;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = tprAttr.Id, Value = 25 });
                            }


                            if (vrAttr != null && vrAttr.Id > 0)
                            {
                                var vrADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == vrAttr.Id).FirstOrDefault();
                                if (vrADefaultValue != null)
                                    vrADefaultValue.Value = 5;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = vrAttr.Id, Value = 5 });
                            }


                            //Datalogger
                            if (dlTpr != null && dlTpr.Id > 0)
                            {
                                var dlTprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.DataLoggerAttributeId == dlTpr.Id).FirstOrDefault();
                                if (dlTprDefaultValue != null)
                                    dlTprDefaultValue.Value = 0;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, DataLoggerAttributeId = dlTpr.Id, Value = 0 });
                            }

                        }
                        else if (mDefaultValue.CauseCode == "TC UNDER ENERIZATION")
                        {
                            if (tprAttr != null && tprAttr.Id > 0)
                            {
                                var tprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == tprAttr.Id).FirstOrDefault();
                                if (tprDefaultValue != null)
                                    tprDefaultValue.Value = 25;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = tprAttr.Id, Value = 25 });
                            }


                            if (vrAttr != null && vrAttr.Id > 0)
                            {
                                var vrADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == vrAttr.Id).FirstOrDefault();
                                if (vrADefaultValue != null)
                                    vrADefaultValue.Value = 0.8m;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = vrAttr.Id, Value = 0.8m });
                            }


                            //Datalogger
                            if (dlTpr != null && dlTpr.Id > 0)
                            {
                                var dlTprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.DataLoggerAttributeId == dlTpr.Id).FirstOrDefault();
                                if (dlTprDefaultValue != null)
                                    dlTprDefaultValue.Value = 0;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, DataLoggerAttributeId = dlTpr.Id, Value = 0 });
                            }

                        }
                        else if (mDefaultValue.CauseCode == "TC TR CONTACT RES HIGH")
                        {
                            if (tprAttr != null && tprAttr.Id > 0)
                            {
                                var tprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == tprAttr.Id).FirstOrDefault();
                                if (tprDefaultValue != null)
                                    tprDefaultValue.Value = 10;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = tprAttr.Id, Value = 10 });
                            }


                            if (tprVLoc != null && tprVLoc.Id > 0)
                            {
                                var tprVLocDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == tprVLoc.Id).FirstOrDefault();
                                if (tprVLocDefaultValue != null)
                                    tprVLocDefaultValue.Value = 17;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = tprVLoc.Id, Value = 17 });
                            }


                            //Datalogger
                            if (dlTpr != null && dlTpr.Id > 0)
                            {
                                var dlTprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.DataLoggerAttributeId == dlTpr.Id).FirstOrDefault();
                                if (dlTprDefaultValue != null)
                                    dlTprDefaultValue.Value = 0;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, DataLoggerAttributeId = dlTpr.Id, Value = 0 });
                            }

                        }
                        else if (mDefaultValue.CauseCode == "TC TPR I/P LOW")
                        {
                            if (tprAttr != null && tprAttr.Id > 0)
                            {
                                var tprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == tprAttr.Id).FirstOrDefault();
                                if (tprDefaultValue != null)
                                    tprDefaultValue.Value = 17;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = tprAttr.Id, Value = 17 });
                            }


                            if (tprVLoc != null && tprVLoc.Id > 0)
                            {
                                var tprVLocDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == tprVLoc.Id).FirstOrDefault();
                                if (tprVLocDefaultValue != null)
                                    tprVLocDefaultValue.Value = 27;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = tprVLoc.Id, Value = 27 });

                            }


                            //Datalogger
                            if (dlTpr != null && dlTpr.Id > 0)
                            {
                                var dlTprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.DataLoggerAttributeId == dlTpr.Id).FirstOrDefault();
                                if (dlTprDefaultValue != null)
                                    dlTprDefaultValue.Value = 0;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, DataLoggerAttributeId = dlTpr.Id, Value = 0 });
                            }

                        }
                        else if (mDefaultValue.CauseCode == "TC SHORT")
                        {
                            if (tprAttr != null && tprAttr.Id > 0)
                            {
                                var tprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == tprAttr.Id).FirstOrDefault();
                                if (tprDefaultValue != null)
                                    tprDefaultValue.Value = 1.86m;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = tprAttr.Id, Value = 1.86m });
                            }


                            if (ifmAAttr != null && ifmAAttr.Id > 0)
                            {
                                var ifmADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == ifmAAttr.Id).FirstOrDefault();
                                if (ifmADefaultValue != null)
                                    ifmADefaultValue.Value = 623.96m;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = ifmAAttr.Id, Value = 623.96m });
                            }


                            if (irmAAttr != null && irmAAttr.Id > 0)
                            {
                                var irmADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == irmAAttr.Id).FirstOrDefault();
                                if (irmADefaultValue != null)
                                    irmADefaultValue.Value = 88.59m;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = irmAAttr.Id, Value = 88.59m });
                            }


                            if (vrAttr != null && vrAttr.Id > 0)
                            {
                                var vrAttrDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == vrAttr.Id).FirstOrDefault();
                                if (vrAttrDefaultValue != null)
                                    vrAttrDefaultValue.Value = 0.6m;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = vrAttr.Id, Value = 0.6m });
                            }


                            if (chokeV != null && chokeV.Id > 0)
                            {
                                var chokeVAttrDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == chokeV.Id).FirstOrDefault();
                                if (chokeVAttrDefaultValue != null)
                                    chokeVAttrDefaultValue.Value = 1.8m;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = chokeV.Id, Value = 1.8m });
                            }


                            if (vF != null && vF.Id > 0)
                            {
                                var vFAttrDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == vF.Id).FirstOrDefault();
                                if (vFAttrDefaultValue != null)
                                    vFAttrDefaultValue.Value = 1.21m;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = vF.Id, Value = 1.21m });
                            }


                            if (tprVLoc != null && tprVLoc.Id > 0)
                            {
                                var tprVLocAttrDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == tprVLoc.Id).FirstOrDefault();
                                if (tprVLocAttrDefaultValue != null)
                                    tprVLocAttrDefaultValue.Value = 0;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = tprVLoc.Id, Value = 0 });
                            }

                            //Datalogger
                            if (dlTpr != null && dlTpr.Id > 0)
                            {
                                var dlTprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.DataLoggerAttributeId == dlTpr.Id).FirstOrDefault();
                                if (dlTprDefaultValue != null)
                                    dlTprDefaultValue.Value = 0;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, DataLoggerAttributeId = dlTpr.Id, Value = 0 });
                            }

                        }
                        else if (mDefaultValue.CauseCode == "TC TFC O/P FAIL")
                        {
                            if (tprAttr != null && tprAttr.Id > 0)
                            {
                                var tprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == tprAttr.Id).FirstOrDefault();
                                if (tprDefaultValue != null)
                                    tprDefaultValue.Value = 0.5m;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = tprAttr.Id, Value = 0.5m });
                            }


                            if (ifmAAttr != null && ifmAAttr.Id > 0)
                            {
                                var ifmADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == ifmAAttr.Id).FirstOrDefault();
                                if (ifmADefaultValue != null)
                                    ifmADefaultValue.Value = 250;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = ifmAAttr.Id, Value = 250 });
                            }


                            if (irmAAttr != null && irmAAttr.Id > 0)
                            {
                                var irmADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == irmAAttr.Id).FirstOrDefault();
                                if (irmADefaultValue != null)
                                    irmADefaultValue.Value = 240;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = irmAAttr.Id, Value = 240 });
                            }



                            if (chargerOPVAttr != null && chargerOPVAttr.Id > 0)
                            {
                                var chargerOPVDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == chargerOPVAttr.Id).FirstOrDefault();
                                if (chargerOPVDefaultValue != null)
                                    chargerOPVDefaultValue.Value = 3;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = chargerOPVAttr.Id, Value = 3 });
                            }


                            //Datalogger
                            if (dlTpr != null && dlTpr.Id > 0)
                            {
                                var dlTprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.DataLoggerAttributeId == dlTpr.Id).FirstOrDefault();
                                if (dlTprDefaultValue != null)
                                    dlTprDefaultValue.Value = 1;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, DataLoggerAttributeId = dlTpr.Id, Value = 1 });
                            }

                        }
                        else if (mDefaultValue.CauseCode == "TC TR DEFECT")
                        {
                            if (tprAttr != null && tprAttr.Id > 0)
                            {
                                var tprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == tprAttr.Id).FirstOrDefault();
                                if (tprDefaultValue != null)
                                    tprDefaultValue.Value = 0.2m;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = tprAttr.Id, Value = 0.2m });
                            }


                            if (tprVLoc != null && tprVLoc.Id > 0)
                            {
                                var tprVLocDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == tprVLoc.Id).FirstOrDefault();
                                if (tprVLocDefaultValue != null)
                                    tprVLocDefaultValue.Value = 0;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = tprVLoc.Id, Value = 0 });
                            }


                            //Datalogger
                            if (dlTpr != null && dlTpr.Id > 0)
                            {
                                var dlTprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.DataLoggerAttributeId == dlTpr.Id).FirstOrDefault();
                                if (dlTprDefaultValue != null)
                                    dlTprDefaultValue.Value = 1;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, DataLoggerAttributeId = dlTpr.Id, Value = 1 });
                            }

                        }
                        else if (mDefaultValue.CauseCode == "TC TR UP TPR DN")
                        {
                            if (tprAttr != null && tprAttr.Id > 0)
                            {
                                var tprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == tprAttr.Id).FirstOrDefault();
                                if (tprDefaultValue != null)
                                    tprDefaultValue.Value = 0.2m;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = tprAttr.Id, Value = 0.2m });
                            }


                            //Datalogger
                            if (dlTpr != null && dlTpr.Id > 0)
                            {
                                var dlTprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.DataLoggerAttributeId == dlTpr.Id).FirstOrDefault();
                                if (dlTprDefaultValue != null)
                                    dlTprDefaultValue.Value = 0;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, DataLoggerAttributeId = dlTpr.Id, Value = 0 });
                            }

                        }
                        else if (mDefaultValue.CauseCode == "TC TPR DEFECT")
                        {

                            //Datalogger
                            if (dlTpr != null && dlTpr.Id > 0)
                            {
                                var dlTprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.DataLoggerAttributeId == dlTpr.Id).FirstOrDefault();
                                if (dlTprDefaultValue != null)
                                    dlTprDefaultValue.Value = 0;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, DataLoggerAttributeId = dlTpr.Id, Value = 0 });
                            }

                        }
                        else
                        {
                            throw new Exception($"{mDefaultValue.CauseCode} Cause Code logic not found");
                        }
                    }
                }
            }


            #endregion

            #region Set Signal Default Value

            if (mDefaultValue.AssetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.SIGNAL)
            {
                var mSignalAssetAttributes = assetAttributeService.GetAssetAttributesBy(mDefaultValue.AssetTypeId);
                if (mSignalAssetAttributes != null && mSignalAssetAttributes.Count > 0)
                {
                    //var dlTpr = mDataloggerAttributes.Where(x => x.Title == "TPR").FirstOrDefault();
                    var rgVAttr = mSignalAssetAttributes.Where(x => x.Title == "RG V").FirstOrDefault();
                    var rgmAAttr = mSignalAssetAttributes.Where(x => x.Title == "RG mA").FirstOrDefault();
                    var hgVAttr = mSignalAssetAttributes.Where(x => x.Title == "HG V").FirstOrDefault();
                    var hgmAAttr = mSignalAssetAttributes.Where(x => x.Title == "HG mA").FirstOrDefault();
                    var hprAttr = mSignalAssetAttributes.Where(x => x.Title == "HPR").FirstOrDefault();
                    var dgVAttr = mSignalAssetAttributes.Where(x => x.Title == "DG V").FirstOrDefault();
                    var dgmAAttr = mSignalAssetAttributes.Where(x => x.Title == "DG mA").FirstOrDefault();
                    var dprAttr = mSignalAssetAttributes.Where(x => x.Title == "DPR").FirstOrDefault();
                    var hhgVAttr = mSignalAssetAttributes.Where(x => x.Title == "HHG V").FirstOrDefault();
                    var hhgmAAttr = mSignalAssetAttributes.Where(x => x.Title == "HHG mA").FirstOrDefault();
                    var hhprAttr = mSignalAssetAttributes.Where(x => x.Title == "HHPR").FirstOrDefault();

                    //Set Default value
                    #region Set Default value

                    //if (rgVAttr != null && rgVAttr.Id > 0)
                    //    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = rgVAttr.Id, Value = 127.65m });
                    //if (rgmAAttr != null && rgmAAttr.Id > 0)
                    //    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = rgmAAttr.Id, Value = 144.03m });
                    //if (hgVAttr != null && hgVAttr.Id > 0)
                    //    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hgVAttr.Id, Value = 117.9m });
                    //if (hgmAAttr != null && hgmAAttr.Id > 0)
                    //    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hgmAAttr.Id, Value = 138.19m });
                    //if (hprAttr != null && hprAttr.Id > 0)
                    //    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hprAttr.Id, Value = 28.45m });
                    //if (dgVAttr != null && dgVAttr.Id > 0)
                    //    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = dgVAttr.Id, Value = 120.56m });
                    //if (dgmAAttr != null && hgVAttr.Id > 0)
                    //    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = dgmAAttr.Id, Value = 147.93m });
                    //if (dprAttr != null && dprAttr.Id > 0)
                    //    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = dprAttr.Id, Value = 27.95m });
                    //if (hhgVAttr != null && hhgVAttr.Id > 0)
                    //    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hhgVAttr.Id, Value = 119.11m });
                    //if (hhgmAAttr != null && hhgmAAttr.Id > 0)
                    //    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hhgmAAttr.Id, Value = 148.59m });
                    //if (hhprAttr != null && hhprAttr.Id > 0)
                    //    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hhprAttr.Id, Value = 27.56m });

                    if (rgVAttr != null && rgVAttr.Id > 0)
                        mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = rgVAttr.Id, Value = 0 });
                    if (rgmAAttr != null && rgmAAttr.Id > 0)
                        mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = rgmAAttr.Id, Value = 0 });
                    if (hgVAttr != null && hgVAttr.Id > 0)
                        mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hgVAttr.Id, Value = 0 });
                    if (hgmAAttr != null && hgmAAttr.Id > 0)
                        mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hgmAAttr.Id, Value = 0 });
                    if (hprAttr != null && hprAttr.Id > 0)
                        mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hprAttr.Id, Value = 0 });
                    if (dgVAttr != null && dgVAttr.Id > 0)
                        mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = dgVAttr.Id, Value = 0 });
                    if (dgmAAttr != null && hgVAttr.Id > 0)
                        mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = dgmAAttr.Id, Value = 0 });
                    if (dprAttr != null && dprAttr.Id > 0)
                        mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = dprAttr.Id, Value = 0 });
                    if (hhgVAttr != null && hhgVAttr.Id > 0)
                        mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hhgVAttr.Id, Value = 0 });
                    if (hhgmAAttr != null && hhgmAAttr.Id > 0)
                        mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hhgmAAttr.Id, Value = 0 });
                    if (hhprAttr != null && hhprAttr.Id > 0)
                        mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hhprAttr.Id, Value = 0 });

                    #endregion

                    if (mDefaultValue.ConditionTypeId == 1)
                    {
                        if (mDefaultValue.CauseCode == "SIG RG V/I LOW")
                        {
                            if (rgmAAttr != null && rgmAAttr.Id > 0)
                            {
                                var rgmADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == rgmAAttr.Id).FirstOrDefault();
                                if (rgmADefaultValue != null)
                                    rgmADefaultValue.Value = 112;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = rgmAAttr.Id, Value = 112 });
                            }

                            if (rgVAttr != null && rgVAttr.Id > 0)
                            {
                                var rgVDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == rgVAttr.Id).FirstOrDefault();
                                if (rgVDefaultValue != null)
                                    rgVDefaultValue.Value = 127.65m;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = rgVAttr.Id, Value = 127.65m });
                            }

                        }
                        else if (mDefaultValue.CauseCode == "SIG HG V/I LOW")
                        {
                            if (hgmAAttr != null && hgmAAttr.Id > 0)
                            {
                                var hgmADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == hgmAAttr.Id).FirstOrDefault();
                                if (hgmADefaultValue != null)
                                    hgmADefaultValue.Value = 109;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hgmAAttr.Id, Value = 109 });
                            }
                            if (hgVAttr != null && hgVAttr.Id > 0)
                            {
                                var hgVDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == hgVAttr.Id).FirstOrDefault();
                                if (hgVDefaultValue != null)
                                    hgVDefaultValue.Value = 117.9m;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hgVAttr.Id, Value = 117.9m });
                            }


                        }
                        else if (mDefaultValue.CauseCode == "SIG HHG V/I LOW")
                        {
                            if (hhgVAttr != null && hhgVAttr.Id > 0)
                            {
                                var hhgVDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == hhgVAttr.Id).FirstOrDefault();
                                if (hhgVDefaultValue != null)
                                    hhgVDefaultValue.Value = 50;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hhgVAttr.Id, Value = 50 });
                            }
                            if (hhgmAAttr != null && hhgmAAttr.Id > 0)
                            {
                                var hhgmADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == hhgmAAttr.Id).FirstOrDefault();
                                if (hhgmADefaultValue != null)
                                    hhgmADefaultValue.Value = 148.59m;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hhgmAAttr.Id, Value = 148.59m });
                            }


                        }
                        else if (mDefaultValue.CauseCode == "SIG DG V/I LOW")
                        {
                            if (dgVAttr != null && dgVAttr.Id > 0)
                            {
                                var dgVDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == dgVAttr.Id).FirstOrDefault();
                                if (dgVDefaultValue != null)
                                    dgVDefaultValue.Value = 70;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = dgVAttr.Id, Value = 70 });
                            }
                            if (dgmAAttr != null && dgmAAttr.Id > 0)
                            {
                                var dgmADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == dgmAAttr.Id).FirstOrDefault();
                                if (dgmADefaultValue != null)
                                    dgmADefaultValue.Value = 147.93m;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = dgmAAttr.Id, Value = 147.93m });
                            }


                        }
                        else if (mDefaultValue.CauseCode == "SIG RG CURR HIGH")
                        {
                            if (rgmAAttr != null && rgmAAttr.Id > 0)
                            {
                                var rgmADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == rgmAAttr.Id).FirstOrDefault();
                                if (rgmADefaultValue != null)
                                    rgmADefaultValue.Value = 155;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = rgmAAttr.Id, Value = 155 });
                            }
                            if (rgVAttr != null && rgVAttr.Id > 0)
                            {
                                var rgVDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == rgVAttr.Id).FirstOrDefault();
                                if (rgVDefaultValue != null)
                                    rgVDefaultValue.Value = 127.65m;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = rgVAttr.Id, Value = 127.65m });
                            }


                        }
                        else if (mDefaultValue.CauseCode == "SIG HG CURR HIGH")
                        {
                            if (hgmAAttr != null && hgmAAttr.Id > 0)
                            {
                                var hgmADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == hgmAAttr.Id).FirstOrDefault();
                                if (hgmADefaultValue != null)
                                    hgmADefaultValue.Value = 152;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hgmAAttr.Id, Value = 152 });
                            }
                            if (hgVAttr != null && hgVAttr.Id > 0)
                            {
                                var hgVDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == hgVAttr.Id).FirstOrDefault();
                                if (hgVDefaultValue != null)
                                    hgVDefaultValue.Value = 117.9m;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hgVAttr.Id, Value = 117.9m });
                            }


                        }
                        else if (mDefaultValue.CauseCode == "SIG HHG CURR HIGH")
                        {
                            if (hhgmAAttr != null && hhgmAAttr.Id > 0)
                            {
                                var hhgmADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == hhgmAAttr.Id).FirstOrDefault();
                                if (hhgmADefaultValue != null)
                                    hhgmADefaultValue.Value = 151;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hhgmAAttr.Id, Value = 151 });
                            }

                            if (hhgVAttr != null && hhgVAttr.Id > 0)
                            {
                                var hhgVDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == hhgVAttr.Id).FirstOrDefault();
                                if (hhgVDefaultValue != null)
                                    hhgVDefaultValue.Value = 119.11m;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hhgVAttr.Id, Value = 119.11m });
                            }

                        }
                        else if (mDefaultValue.CauseCode == "SIG DG CURR HIGH")
                        {
                            if (dgmAAttr != null && dgmAAttr.Id > 0)
                            {
                                var dgmADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == dgmAAttr.Id).FirstOrDefault();
                                if (dgmADefaultValue != null)
                                    dgmADefaultValue.Value = 156;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = dgmAAttr.Id, Value = 156 });
                            }
                            if (dgVAttr != null && dgVAttr.Id > 0)
                            {
                                var dgVDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == dgVAttr.Id).FirstOrDefault();
                                if (dgVDefaultValue != null)
                                    dgVDefaultValue.Value = 120.56m;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = dgVAttr.Id, Value = 120.56m });
                            }


                        }
                        else if (mDefaultValue.CauseCode == "SIG HPR LOW")
                        {
                            if (hprAttr != null && hprAttr.Id > 0)
                            {
                                var hprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == hprAttr.Id).FirstOrDefault();
                                if (hprDefaultValue != null)
                                    hprDefaultValue.Value = 5;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hprAttr.Id, Value = 5 });
                            }


                        }
                        else if (mDefaultValue.CauseCode == "SIG HHPR LOW")
                        {
                            if (hhprAttr != null && hhprAttr.Id > 0)
                            {
                                var hhprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == hhprAttr.Id).FirstOrDefault();
                                if (hhprDefaultValue != null)
                                    hhprDefaultValue.Value = 7;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hhprAttr.Id, Value = 7 });
                            }


                        }
                        else if (mDefaultValue.CauseCode == "SIG DPR LOW")
                        {
                            if (dprAttr != null && dprAttr.Id > 0)
                            {
                                var dprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == dprAttr.Id).FirstOrDefault();
                                if (dprDefaultValue != null)
                                    dprDefaultValue.Value = 1;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = dprAttr.Id, Value = 1 });
                            }

                        }

                        else if (mDefaultValue.CauseCode == "SIG RG V/I FAIL")
                        {
                            if (rgmAAttr != null && rgmAAttr.Id > 0)
                            {
                                var rgmADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == rgmAAttr.Id).FirstOrDefault();
                                if (rgmADefaultValue != null)
                                    rgmADefaultValue.Value = 102;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = rgmAAttr.Id, Value = 102 });
                            }

                            if (rgVAttr != null && rgVAttr.Id > 0)
                            {
                                var rgVDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == rgVAttr.Id).FirstOrDefault();
                                if (rgVDefaultValue != null)
                                    rgVDefaultValue.Value = 127.65m;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = rgVAttr.Id, Value = 127.65m });
                            }
                        }
                        else if (mDefaultValue.CauseCode == "SIG RECR DN")
                        {
                            if (rgmAAttr != null && rgmAAttr.Id > 0)
                            {
                                var rgmADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == rgmAAttr.Id).FirstOrDefault();
                                if (rgmADefaultValue != null)
                                    rgmADefaultValue.Value = 122;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = rgmAAttr.Id, Value = 122 });
                            }

                            if (rgVAttr != null && rgVAttr.Id > 0)
                            {
                                var rgVDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == rgVAttr.Id).FirstOrDefault();
                                if (rgVDefaultValue != null)
                                    rgVDefaultValue.Value = 95;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = rgVAttr.Id, Value = 95 });
                            }
                        }
                        else if (mDefaultValue.CauseCode == "SIG HG V/I FAIL")
                        {
                            if (hprAttr != null && hprAttr.Id > 0)
                            {
                                var hprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == hprAttr.Id).FirstOrDefault();
                                if (hprDefaultValue != null)
                                    hprDefaultValue.Value = 22;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hprAttr.Id, Value = 22 });
                            }
                            if (hgVAttr != null && hgVAttr.Id > 0)
                            {
                                var hgVDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == hgVAttr.Id).FirstOrDefault();
                                if (hgVDefaultValue != null)
                                    hgVDefaultValue.Value = 85;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hgVAttr.Id, Value = 85 });
                            }
                            if (hgmAAttr != null && hgmAAttr.Id > 0)
                            {
                                var hgmADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == hgmAAttr.Id).FirstOrDefault();
                                if (hgmADefaultValue != null)
                                    hgmADefaultValue.Value = 138.19m;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hgmAAttr.Id, Value = 138.19m });
                            }
                        }
                        else if (mDefaultValue.CauseCode == "SIG HPR DN")
                        {
                            if (hprAttr != null && hprAttr.Id > 0)
                            {
                                var hprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == hprAttr.Id).FirstOrDefault();
                                if (hprDefaultValue != null)
                                    hprDefaultValue.Value = 10;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hprAttr.Id, Value = 10 });
                            }

                        }
                        else if (mDefaultValue.CauseCode == "SIG HECR DN")
                        {
                            if (hprAttr != null && hprAttr.Id > 0)
                            {
                                var hprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == hprAttr.Id).FirstOrDefault();
                                if (hprDefaultValue != null)
                                    hprDefaultValue.Value = 26;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hprAttr.Id, Value = 26 });
                            }
                            if (hgVAttr != null && hgVAttr.Id > 0)
                            {
                                var hgVDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == hgVAttr.Id).FirstOrDefault();
                                if (hgVDefaultValue != null)
                                    hgVDefaultValue.Value = 110;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hgVAttr.Id, Value = 110 });
                            }
                            if (hgmAAttr != null && hgmAAttr.Id > 0)
                            {
                                var hgmADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == hgmAAttr.Id).FirstOrDefault();
                                if (hgmADefaultValue != null)
                                    hgmADefaultValue.Value = 125;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hgmAAttr.Id, Value = 125 });
                            }
                        }
                        else if (mDefaultValue.CauseCode == "SIG HHG V/I FAIL")
                        {
                            if (hprAttr != null && hprAttr.Id > 0)
                            {
                                var hprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == hprAttr.Id).FirstOrDefault();
                                if (hprDefaultValue != null)
                                    hprDefaultValue.Value = 22;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hprAttr.Id, Value = 22 });
                            }
                            if (hgVAttr != null && hgVAttr.Id > 0)
                            {
                                var hgVDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == hgVAttr.Id).FirstOrDefault();
                                if (hgVDefaultValue != null)
                                    hgVDefaultValue.Value = 85;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hgVAttr.Id, Value = 85 });
                            }
                            if (hgmAAttr != null && hgmAAttr.Id > 0)
                            {
                                var hgmADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == hgmAAttr.Id).FirstOrDefault();
                                if (hgmADefaultValue != null)
                                    hgmADefaultValue.Value = 138.19m;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hgmAAttr.Id, Value = 138.19m });
                            }
                            if (hhprAttr != null && hhprAttr.Id > 0)
                            {
                                var hhprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == hhprAttr.Id).FirstOrDefault();
                                if (hhprDefaultValue != null)
                                    hhprDefaultValue.Value = 22;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hhprAttr.Id, Value = 22 });
                            }
                            if (hhgmAAttr != null && hhgmAAttr.Id > 0)
                            {
                                var hhgmADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == hhgmAAttr.Id).FirstOrDefault();
                                if (hhgmADefaultValue != null)
                                    hhgmADefaultValue.Value = 100;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hhgmAAttr.Id, Value = 100 });
                            }

                            if (hhgVAttr != null && hhgVAttr.Id > 0)
                            {
                                var hhgVDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == hhgVAttr.Id).FirstOrDefault();
                                if (hhgVDefaultValue != null)
                                    hhgVDefaultValue.Value = 119.11m;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hhgVAttr.Id, Value = 119.11m });
                            }

                        }
                        else if (mDefaultValue.CauseCode == "SIG HHPR DN")
                        {
                            if (hprAttr != null && hprAttr.Id > 0)
                            {
                                var hprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == hprAttr.Id).FirstOrDefault();
                                if (hprDefaultValue != null)
                                    hprDefaultValue.Value = 22;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hprAttr.Id, Value = 22 });
                            }
                            if (hgVAttr != null && hgVAttr.Id > 0)
                            {
                                var hgVDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == hgVAttr.Id).FirstOrDefault();
                                if (hgVDefaultValue != null)
                                    hgVDefaultValue.Value = 75;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hgVAttr.Id, Value = 75 });
                            }
                            if (hgmAAttr != null && hgmAAttr.Id > 0)
                            {
                                var hgmADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == hgmAAttr.Id).FirstOrDefault();
                                if (hgmADefaultValue != null)
                                    hgmADefaultValue.Value = 138.19m;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hgmAAttr.Id, Value = 138.19m });
                            }
                            if (hhprAttr != null && hhprAttr.Id > 0)
                            {
                                var hhprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == hhprAttr.Id).FirstOrDefault();
                                if (hhprDefaultValue != null)
                                    hhprDefaultValue.Value = 0.5m;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hhprAttr.Id, Value = 0.5m });
                            }

                        }
                        else if (mDefaultValue.CauseCode == "SIG HHECR DN")
                        {
                            if (hprAttr != null && hprAttr.Id > 0)
                            {
                                var hprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == hprAttr.Id).FirstOrDefault();
                                if (hprDefaultValue != null)
                                    hprDefaultValue.Value = 26;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hprAttr.Id, Value = 26 });
                            }
                            if (hhprAttr != null && hhprAttr.Id > 0)
                            {
                                var hhprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == hhprAttr.Id).FirstOrDefault();
                                if (hhprDefaultValue != null)
                                    hhprDefaultValue.Value = 27;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hhprAttr.Id, Value = 27 });
                            }
                            if (hgVAttr != null && hgVAttr.Id > 0)
                            {
                                var hgVDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == hgVAttr.Id).FirstOrDefault();
                                if (hgVDefaultValue != null)
                                    hgVDefaultValue.Value = 110;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hgVAttr.Id, Value = 110 });
                            }
                            if (hgmAAttr != null && hgmAAttr.Id > 0)
                            {
                                var hgmADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == hgmAAttr.Id).FirstOrDefault();
                                if (hgmADefaultValue != null)
                                    hgmADefaultValue.Value = 125;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hgmAAttr.Id, Value = 125 });
                            }
                            if (hhgmAAttr != null && hhgmAAttr.Id > 0)
                            {
                                var hhgmADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == hhgmAAttr.Id).FirstOrDefault();
                                if (hhgmADefaultValue != null)
                                    hhgmADefaultValue.Value = 125;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hhgmAAttr.Id, Value = 125 });
                            }

                            if (hhgVAttr != null && hhgVAttr.Id > 0)
                            {
                                var hhgVDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == hhgVAttr.Id).FirstOrDefault();
                                if (hhgVDefaultValue != null)
                                    hhgVDefaultValue.Value = 110;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = hhgVAttr.Id, Value = 110 });
                            }
                        }
                        else if (mDefaultValue.CauseCode == "SIG DG V/I FAIL")
                        {
                            if (dprAttr != null && dprAttr.Id > 0)
                            {
                                var dprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == dprAttr.Id).FirstOrDefault();
                                if (dprDefaultValue != null)
                                    dprDefaultValue.Value = 22;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = dprAttr.Id, Value = 22 });
                            }
                            if (dgmAAttr != null && dgmAAttr.Id > 0)
                            {
                                var dgmADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == dgmAAttr.Id).FirstOrDefault();
                                if (dgmADefaultValue != null)
                                    dgmADefaultValue.Value = 147.93m;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = dgmAAttr.Id, Value = 147.93m });
                            }
                            if (dgVAttr != null && dgVAttr.Id > 0)
                            {
                                var dgVDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == dgVAttr.Id).FirstOrDefault();
                                if (dgVDefaultValue != null)
                                    dgVDefaultValue.Value = 85;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = dgVAttr.Id, Value = 85 });
                            }
                        }
                        else if (mDefaultValue.CauseCode == "SIG DPR DN")
                        {
                            if (dprAttr != null && dprAttr.Id > 0)
                            {
                                var dprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == dprAttr.Id).FirstOrDefault();
                                if (dprDefaultValue != null)
                                    dprDefaultValue.Value = 10;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = dprAttr.Id, Value = 10 });
                            }
                        }
                        else if (mDefaultValue.CauseCode == "SIG DECR DN")
                        {
                            if (dprAttr != null && dprAttr.Id > 0)
                            {
                                var dprDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == dprAttr.Id).FirstOrDefault();
                                if (dprDefaultValue != null)
                                    dprDefaultValue.Value = 26;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = dprAttr.Id, Value = 26 });
                            }
                            if (dgmAAttr != null && dgmAAttr.Id > 0)
                            {
                                var dgmADefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == dgmAAttr.Id).FirstOrDefault();
                                if (dgmADefaultValue != null)
                                    dgmADefaultValue.Value = 125;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = dgmAAttr.Id, Value = 125 });
                            }
                            if (dgVAttr != null && dgVAttr.Id > 0)
                            {
                                var dgVDefaultValue = mDefaultValues.Where(x => x.AssetId == mDefaultValue.AssetId && x.AttributeId == dgVAttr.Id).FirstOrDefault();
                                if (dgVDefaultValue != null)
                                    dgVDefaultValue.Value = 110;
                                else
                                    mDefaultValues.Add(new Domain.DefaultValue() { AssetId = mDefaultValue.AssetId, SiteId = mDefaultValue.SiteId, AttributeId = dgVAttr.Id, Value = 110 });
                            }
                        }
                    }
                }
            }

            #endregion

            return mDefaultValues;

        }

        public void UpdateData(Domain.DefaultValue mDefaultValue)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mDefaultValue);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format("DefaultValue"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;

                }
            }
            catch
            {
            }
        }

        public void UpdateDataDL(Domain.DefaultValue mDefaultValue)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mDefaultValue);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format("DefaultValue/UpdateDL"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;

                }
            }
            catch
            {
            }
        }

        public JsonResult GetCauseCodeByAssetType(int assetTypeId, int alertTypeId)
        {
            List<Domain.AlertInfo> mAlertInfos = new List<Domain.AlertInfo>();
            try
            {
                using (var mHttpClientFactory = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = mHttpClientFactory.client.GetAsync(String.Format($"AlertInfo/AssetTypeId/{assetTypeId}/AlertTypeId/{alertTypeId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAlertInfos = JsonConvert.DeserializeObject<List<Domain.AlertInfo>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return Json(mAlertInfos, JsonRequestBehavior.AllowGet);
        }

        public void GetPointAlertAlias()
        {
            var enumData = from E7FRSAdvance.Utility.Utility.PointAlertAlias e in Enum.GetValues(typeof(E7FRSAdvance.Utility.Utility.PointAlertAlias))
                           select new
                           {
                               Id = (int)e,
                               Name = e.ToString().Replace("_", " ")
                           };

            ViewBag.PointAlertAlias = new SelectList(enumData, "Id", "Name");
        }

        public ActionResult _AlertList(Domain.FRSAlertLister mFRSAlertLister)
        {
            try
            {
                mFRSAlertLister.SearchCriteria.IsAcknowledgement = false;
                mFRSAlertLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
                mFRSAlertLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mFRSAlertLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("FRSAlert/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mFRSAlertLister = JsonConvert.DeserializeObject<FRSAlertLister>(jsonString);

                    }
                }
            }
            catch (Exception)
            {
            }
            return Json(mFRSAlertLister);
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
            return mDataloggers;
        }
    }
}