using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;
using System.Linq;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class StockOprationController : Controller
    {
        // GET: StockOpration
        private readonly IZoneService _zoneService;
        public StockOprationController(IZoneService zoneService)
        {
            _zoneService = zoneService;
        }
        public ActionResult Index()
        {
            return View();
        }

        public PartialViewResult _List(StockLister mStockLister)
        {
            mStockLister.Pager.Take = -1;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mStockLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Stock/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mStockLister = JsonConvert.DeserializeObject<StockLister>(jsonString);
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
            return PartialView(mStockLister);
        }

        public ActionResult AddStock()
        {
            ViewBag.Zones = new SelectList(_zoneService.GetAll(), "Id", "Name");
            ViewBag.Materials = GetMaterial();
            return View();
        }

        public ActionResult EditStock(int id)
        {
            Domain.Stock mStock = new Domain.Stock();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Stock/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mStock = JsonConvert.DeserializeObject<Domain.Stock>(jsonString);
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
                ViewBag.Zones = new SelectList(_zoneService.GetAll(), "Id", "Name");
                ViewBag.Materials = GetMaterial();
            }

            return View(mStock);
        }

        public ActionResult _DispatchDetail(int id)
        {
            Domain.Stock mStock = new Domain.Stock();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Stock/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mStock = JsonConvert.DeserializeObject<Domain.Stock>(jsonString);
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
                BindDispatchType();
            }

            return PartialView(mStock);
        }

        public ActionResult _MaterialList(int id)
        {
            List<Domain.StockWorksheetMaterial> mStockWorksheetMaterials = new List<Domain.StockWorksheetMaterial>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("StockWorksheetMaterial/StockId/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mStockWorksheetMaterials = JsonConvert.DeserializeObject<List<Domain.StockWorksheetMaterial>>(jsonString);
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
            return PartialView(mStockWorksheetMaterials);
        }

        public JsonResult SearchMaterial()
        {
            return Json(GetMaterial(), JsonRequestBehavior.AllowGet);
        }

        public List<Domain.WorksheetMaterial> GetMaterial()
        {
            List<Domain.WorksheetMaterial> mWorksheetMaterials = new List<Domain.WorksheetMaterial>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync("WorksheetMaterial/GetAll").Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mWorksheetMaterials = JsonConvert.DeserializeObject<List<Domain.WorksheetMaterial>>(jsonString);
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
            return mWorksheetMaterials;
        }

        public ActionResult Create(Domain.Stock mStock)
        {
            try
            {
                mStock.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mStock);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync($"Stock", str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mStock = JsonConvert.DeserializeObject<Domain.Stock>(jsonString);
                    }
                }
            }
            catch (Exception)
            {
            }

            return Json(mStock);
        }

        public ActionResult UpdateMaterial(List<Domain.StockWorksheetMaterial> mStockWorksheetMaterials)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mStockWorksheetMaterials);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync($"StockWorksheetMaterial", str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Quantity has been Updated." };
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

        public JsonResult SearchProject(string name)
        {
            List<Domain.Project> mProjects = new List<Domain.Project>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync($"Project/Name/{name}").Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mProjects = JsonConvert.DeserializeObject<List<Domain.Project>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
            }

            return Json(mProjects, JsonRequestBehavior.AllowGet);
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

        //public JsonResult GetSiteByDivisionId(int divisionId)
        //{
        //    var mSites = new List<Domain.Site>();
        //    try
        //    {
        //        mSites = _siteService.GetBy(divisionId);
        //    }
        //    catch (Exception ex)
        //    {
        //        ViewBag.Error = ex.Message;
        //    }
        //    return Json(mSites, JsonRequestBehavior.AllowGet);
        //}

        #region Audit

        public ActionResult Audit()
        {
            return View();
        }

        public PartialViewResult _AuditList(StockLister mStockLister)
        {
            mStockLister.Pager.Take = -1;
            mStockLister.SearchCriteria.IsAudit = true;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mStockLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Stock/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mStockLister = JsonConvert.DeserializeObject<StockLister>(jsonString);
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
            return PartialView(mStockLister);
        }

        public ActionResult _AuditMaterialList(int id)
        {
            List<Domain.StockWorksheetMaterial> mStockWorksheetMaterials = new List<Domain.StockWorksheetMaterial>();
            ViewBag.StockId = id;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("StockWorksheetMaterial/StockId/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mStockWorksheetMaterials = JsonConvert.DeserializeObject<List<Domain.StockWorksheetMaterial>>(jsonString);
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
                ViewBag.WorksheetMaterials = GetMaterial();
            }
            return PartialView(mStockWorksheetMaterials);
        }

        public ActionResult UpdateAuditMaterial(List<Domain.StockWorksheetMaterial> mStockWorksheetMaterials)
        {
            dynamic data = new ExpandoObject();
            try
            {
                if (mStockWorksheetMaterials != null)
                {
                    foreach (var item in mStockWorksheetMaterials)
                    {
                        item.AuditedBy = ClsHttpContent.LoginUser.Id;
                    }
                }
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mStockWorksheetMaterials);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync($"StockWorksheetMaterial/UpdateAudit", str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Record has been Updated." };
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

        #endregion

        #region Dispatch

        public ActionResult Dispatch()
        {
            return View(new StockLister());
        }

        public PartialViewResult _DispatchList(StockLister mStockLister)
        {
            mStockLister.Pager.Take = -1;
            try
            {
                mStockLister.SearchCriteria.IsDispatch = true;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mStockLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Stock/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mStockLister = JsonConvert.DeserializeObject<StockLister>(jsonString);
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
            return PartialView(mStockLister);
        }

        public ActionResult _DispatchMaterialList(int id)
        {
            List<Domain.StockWorksheetMaterial> mStockWorksheetMaterials = new List<Domain.StockWorksheetMaterial>();
            ViewBag.StockId = id;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("StockWorksheetMaterial/StockId/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mStockWorksheetMaterials = JsonConvert.DeserializeObject<List<Domain.StockWorksheetMaterial>>(jsonString);
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
            return PartialView(mStockWorksheetMaterials);
        }

        public ActionResult _AddDispatch(int id)
        {
            Domain.Stock stockDispatch = new Domain.Stock();

            List<Domain.StockWorksheetMaterial> mStockWorksheetMaterials = new List<Domain.StockWorksheetMaterial>();
            ViewBag.StockId = id;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("StockWorksheetMaterial/StockId/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mStockWorksheetMaterials = JsonConvert.DeserializeObject<List<Domain.StockWorksheetMaterial>>(jsonString);
                        if (mStockWorksheetMaterials != null)
                        {
                            ViewBag.StockWorksheetMaterials = mStockWorksheetMaterials;
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
                BindDispatchType();
            }
            return PartialView(stockDispatch);
        }

        public ActionResult SaveDispatch(Domain.Stock mStock)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mStock.CreatedBy = ClsHttpContent.LoginUser.Id;
                mStock.DispatchedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mStock);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format($"Stock/Dispatch"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Stock has been Dispatched" };
                    }
                    else if (response.StatusCode == HttpStatusCode.BadRequest)
                    {
                        var error = JsonConvert.DeserializeObject<string>(jsonString);
                        data = new { type = "error", result = error };
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

        public ActionResult UpdateDispatchMaterial(List<Domain.StockWorksheetMaterial> mStockWorksheetMaterials)
        {
            dynamic data = new ExpandoObject();
            try
            {
                if (mStockWorksheetMaterials != null)
                {
                    foreach (var item in mStockWorksheetMaterials)
                    {
                        item.DispatchedBy = ClsHttpContent.LoginUser.Id;
                    }
                }
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mStockWorksheetMaterials);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync($"StockWorksheetMaterial/UpdateDispatch", str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Quantity has been Updated." };
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

        private void BindDispatchType()
        {

            var enumlist = (from E7FRSAdvance.Utility.Utility.DispatchType e in Enum.GetValues(typeof(E7FRSAdvance.Utility.Utility.DispatchType))
                            select new Domain.AssetType
                            {
                                Id = (int)e,
                                Name = e.ToString().Replace("_", " ")
                            }).ToList();

            ViewBag.DispatchTypeEnumList = new SelectList(enumlist, "Id", "Name");

            // return new SelectList(enumData, "Id", "Name");
        }



        #endregion
    }
}