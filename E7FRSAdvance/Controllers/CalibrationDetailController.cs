using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class CalibrationDetailController : Controller
    {
        // GET: CalibrationDetail
        private readonly ISiteService siteService;
        public CalibrationDetailController(ISiteService siteService)
        {
            this.siteService = siteService;
        }

        public ActionResult Index(int? siteId = 0)
        {
            Domain.Site mSite = new Domain.Site();
            if (siteId != null && siteId.Value > 0)
                mSite = siteService.Get(siteId.Value);

            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");

            return View(mSite);
        }

        public ActionResult _GetAssetType(int siteId)
        {
            List<AssetType> mAssetType = new List<AssetType>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("AssetType/GetAllAssestType/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssetType = JsonConvert.DeserializeObject<List<AssetType>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView(mAssetType);
        }

        public ActionResult _List(AssetLister mAssetLister)
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

                        mAssetLister.Pager.PageSize = -1;
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView(mAssetLister);
        }

        public ActionResult _Detail(int assetId, int assetAttributeId, string date = null)
        {
            List<Domain.CalibrationDetail> mCalibrationDetails = new List<Domain.CalibrationDetail>();


            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CalibrationDetail/AssetId/{assetId}/AssetAttributeId/{assetAttributeId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mCalibrationDetails = JsonConvert.DeserializeObject<List<Domain.CalibrationDetail>>(jsonString);

                        if (mCalibrationDetails != null && mCalibrationDetails.Count > 0 && date.IsNotNullOrEmpty())
                        {
                            var selectedDate = ExtensionMethod.ConvertToDateTimeFormat(date, "d/M/yyyy");
                            if (selectedDate != null && selectedDate != DateTime.MinValue)
                                mCalibrationDetails = mCalibrationDetails.Where(x => x.CreatedDate.Date == selectedDate.Date).ToList();

                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView(mCalibrationDetails);
        }

        public ActionResult DownloadPDF(int siteId, int assetTypeId, string date = null)
        {
            try
            {
                if (date.IsNotNullOrEmpty())
                {
                    date = date.Replace("/", "-");
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = hcf.client.GetAsync(String.Format($"CalibrationDetail/DownloadPDF/SiteId/{siteId}/AssetTypeId/{assetTypeId}/Date/{date}")).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                var mSite = siteService.Get(siteId);
                                if (mSite != null && mSite.Id > 0)
                                    return File(csvbytes, "application/pdf", $"{mSite.Name}-CalibrationDetail.pdf");
                            }
                        }
                    }
                }
                else
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = hcf.client.GetAsync(String.Format($"CalibrationDetail/DownloadPDF/SiteId/{siteId}/AssetTypeId/{assetTypeId}")).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                var mSite = siteService.Get(siteId);
                                if (mSite != null && mSite.Id > 0)
                                    return File(csvbytes, "application/pdf", $"{mSite.Name}-CalibrationDetail.pdf");
                            }
                        }
                    }
                }

            }
            catch (Exception ex)
            {
            }


            return RedirectToAction("PDF");
        }
    }
}