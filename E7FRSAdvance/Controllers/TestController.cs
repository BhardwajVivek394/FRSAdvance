using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Utility;
using MQTTnet.Client;
using MQTTnet.Client.Options;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;
using YamlDotNet.Core.Tokens;

namespace E7FRSAdvance.Controllers
{
    public class TestController : Controller
    {
        // GET: Test
        private static IMqttClient _client;
        private static IMqttClientOptions _options;
        private readonly IAssetAttributeService assetAttributeService;
        private readonly ISiteService siteService;
        private readonly ISiteKeepingService keepingService;
        private readonly IGraphService graphService;
        private readonly IAssetService assetService;
        private readonly IFRSAlertService fRSAlertService;
        public TestController(IAssetAttributeService assetAttributeService, ISiteService siteService, ISiteKeepingService keepingService, IGraphService graphService, IAssetService assetService, IFRSAlertService fRSAlertService)
        {
            this.assetAttributeService = assetAttributeService;
            this.siteService = siteService;
            this.keepingService = keepingService;
            this.graphService = graphService;
            this.assetService = assetService;
            this.fRSAlertService = fRSAlertService;
        }
        public ActionResult Index(int assetTypeId, int assetId, string token)
        {
            Domain.AssetTypeCircuitDiagram mAssetTypeCircuitDiagram = new Domain.AssetTypeCircuitDiagram();
            if (token.IsNullOrEmpty())
            {
                token = ClsHttpContent.LoginUser.Token;
            }
            try
            {
                using (var hcf = new HttpClientFactory(token: token))
                {
                    var response = hcf.client.GetAsync(String.Format("AssetType/GetAssetTypeCircuitDiagram/{0}", assetTypeId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssetTypeCircuitDiagram = JsonConvert.DeserializeObject<AssetTypeCircuitDiagram>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            finally
            {
                var mAsset = Get(assetId, token);
                if (mAsset != null)
                {
                    ViewBag.Asset = mAsset;
                    ViewBag.Site = GetSite(mAsset.SiteId, token);

                    AssetLister mAssetLister = new AssetLister();
                    mAssetLister.SearchCriteria.SiteId = mAsset.SiteId;
                    mAssetLister.SearchCriteria.AssetTypeId = assetTypeId;
                    var assets = GetAssetDetails(mAssetLister, token);
                    if (assets != null)
                    {
                        ViewBag.Assets = assets.mAssets;
                    }
                }
                ViewBag.MQTTDetail = GetMQTTDetailList(token).mQTTDetailWeb;
                var assetAttributes = GetAllAssetAttributeBy(assetTypeId, token);
                if (assetAttributes != null && assetAttributes.Count > 0)
                {
                    ViewBag.AssetAttributes = assetAttributes.Where(x => x.IsDerived != null && x.IsDerived.Value);
                }
                ViewBag.Token = token;

            }
            return PartialView(mAssetTypeCircuitDiagram);
        }

        public MQTTDetailList GetMQTTDetailList(string token)
        {

            MQTTDetailList mMQTTDetail = new MQTTDetailList();
            try
            {
                using (var hcf = new HttpClientFactory(token: token))
                {
                    var response = hcf.client.GetAsync(String.Format("User/GetMQTTDetail")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mMQTTDetail = JsonConvert.DeserializeObject<MQTTDetailList>(jsonString);
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
            return mMQTTDetail;
        }

        public Domain.Site GetSite(int id, string token)
        {
            Domain.Site mSite = new Domain.Site();
            try
            {
                using (var hcf = new HttpClientFactory(token: token))
                {
                    var response = hcf.client.GetAsync(String.Format("Site/GetSiteById/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mSite = JsonConvert.DeserializeObject<Domain.Site>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mSite;
        }

        public Asset Get(int id, string token)
        {
            Asset mAsset = new Asset();
            try
            {
                using (var hcf = new HttpClientFactory(token: token))
                {
                    var response = hcf.client.GetAsync(String.Format("Asset/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAsset = JsonConvert.DeserializeObject<Asset>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mAsset;
        }

        public List<AssetAttribute> GetAllAssetAttributeBy(int assetTypeId, string token)
        {
            List<AssetAttribute> mAssetAttributes = new List<AssetAttribute>();
            try
            {
                using (var hcf = new HttpClientFactory(token: token))
                {
                    var response = hcf.client.GetAsync($"AssetAttribute/GetAllAssetAttributeBy/AssetTypeId/{assetTypeId}").Result;
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

        public AssetLister GetAssetDetails(AssetLister mAssetLister, string token)
        {
            mAssetLister.Pager.Take = -1;// mAssetLister.Pager.PageSize;

            try
            {
                using (var hcf = new HttpClientFactory(token: token))
                {
                    //mAssetLister.SearchCriteria.CreatedBy = ClsHttpContent.LoginUser.Id;
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


                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return mAssetLister;
        }

        public ActionResult GetFRSAlertList(int assetId, string token)
        {

            Domain.FRSAlertLister mFRSAlertLister = new FRSAlertLister();

            mFRSAlertLister.SearchCriteria.AssetId = assetId;
            mFRSAlertLister.SearchCriteria.AlertStatus = (int)E7FRSAdvance.Utility.Utility.AlertStatus.Active;
            try
            {
                mFRSAlertLister.SearchCriteria.IsAcknowledgement = false;
                mFRSAlertLister.SearchCriteria.RoleId = (int)E7FRSAdvance.Utility.Utility.Role.Admin;
                using (var hcf = new HttpClientFactory(token: token))
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
            catch (Exception ex)
            {
                throw ex;
            }

            return Json(mFRSAlertLister);
        }
    }
}