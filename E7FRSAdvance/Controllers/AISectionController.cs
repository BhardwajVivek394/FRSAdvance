using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;
using System.Web.UI.WebControls;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class AISectionController : Controller
    {
        // GET: AISection
        private readonly ISiteService siteService;
        public AISectionController(ISiteService siteService)
        {
            this.siteService = siteService;
        }
        public ActionResult Index()
        {
            return View();
        }

        public ActionResult Learning()
        {
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            return View();
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
                        mAssetLister.Pager.PageSize = -1;

                        if (mAssetLister.SearchCriteria != null && !string.IsNullOrEmpty(mAssetLister.SearchCriteria.AssetTypeName) && mAssetLister.SearchCriteria.AssetTypeName.ToLower() == "Point Machine".ToLower())
                        {
                            //GetPMPrediction();
                            //GetPMIsThickWayPrediction();
                            GetAllAIRemark(mAssetLister.SearchCriteria.AssetTypeId);
                            return PartialView("_GetPointMachineDashboard", mAssetLister);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }

            return PartialView("_GetPointMachineDashboard", mAssetLister);
        }

        public ActionResult Predict()
        {
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            return View();
        }

        public ActionResult GetPrediction(PointMachineDataLister mPointMachineDataLister)
        {
            mPointMachineDataLister.Pager.Take = -1;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    if (mPointMachineDataLister.SearchCriteria.StartDate == null || mPointMachineDataLister.SearchCriteria.StartDate == DateTime.MinValue)
                        mPointMachineDataLister.SearchCriteria.StartDate = DateTime.Now;

                    if (mPointMachineDataLister.SearchCriteria.EndDate == null || mPointMachineDataLister.SearchCriteria.EndDate == DateTime.MinValue)
                        mPointMachineDataLister.SearchCriteria.EndDate = DateTime.Now;


                    var jsonStr = JsonConvert.SerializeObject(mPointMachineDataLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("AISection/GetPrediction"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mPointMachineDataLister = JsonConvert.DeserializeObject<PointMachineDataLister>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView("_GetPredictionPointMachine", mPointMachineDataLister);
        }

        public ActionResult UpdatePointMachine(List<PointMachineData> mPointMachineDatas)
        {
            dynamic data = new ExpandoObject();
            if (mPointMachineDatas != null && mPointMachineDatas.Count > 0)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mPointMachineDatas);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("AISection/UpdatePointMachineCSV"), str).Result;
                        if (response.StatusCode == HttpStatusCode.NoContent)
                        {
                            data = new { type = "success", result = "Updated." };
                        }
                        else
                        {
                            data = new { type = "error", result = "Internal server error!" };
                        }
                    }
                }
                catch (Exception)
                {
                    data = new { type = "error", result = "Internal server error!" };
                }
            }
            return Json(data, JsonRequestBehavior.AllowGet);
        }

        public void GetAllAIRemark(int assetTypeId)
        {
            var mAIRemarks = new List<AIRemark>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"AIRemark/AssetTypeId/{assetTypeId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAIRemarks = JsonConvert.DeserializeObject<List<AIRemark>>(jsonString);
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
            ViewBag.mAIRemarks = mAIRemarks;
        }

        public void GetPMPrediction()
        {
            try
            {
                var mPMPrediction = Enum.GetValues(typeof(E7FRSAdvance.Utility.Utility.PMPrediction))
               .Cast<E7FRSAdvance.Utility.Utility.PMPrediction>()
               .Select(t => new TypeViewModel
               {
                   Id = ((int)t),
                   Name = t.ToString().Replace("_", " ")
               }).ToList();

                ViewBag.PMPrediction = mPMPrediction;

            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
        }

        public void GetPMIsThickWayPrediction()
        {
            try
            {
                var mPMIsThickWayPrediction = Enum.GetValues(typeof(E7FRSAdvance.Utility.Utility.PMIsThickWayPrediction))
               .Cast<E7FRSAdvance.Utility.Utility.PMIsThickWayPrediction>()
               .Select(t => new TypeViewModel
               {
                   Id = ((int)t),
                   Name = t.ToString().Replace("_", " ")
               }).ToList();

                ViewBag.PMIsThickWayPrediction = mPMIsThickWayPrediction;

            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
        }
    }
}