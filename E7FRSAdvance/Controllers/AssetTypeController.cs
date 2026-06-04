using Domain;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;
using System.Linq;
using E7FRSAdvance.Utility;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class AssetTypeController : Controller
    {
        // GET: AssetType
        public ActionResult Index()
        {
            ViewBag.AssetType = new SelectList(ExtensionMethod.BuildAssetType(), "Id", "Name");
            return View();
        }

        public PartialViewResult AssetTypeList(AssetTypeLister mAssetTypeLister)
        {
            mAssetTypeLister.Pager.Take = mAssetTypeLister.Pager.PageSize;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssetTypeLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("AssetType/GetAllAssetTypeLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetTypeLister = JsonConvert.DeserializeObject<AssetTypeLister>(jsonString);
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
            return PartialView("_AssetTypeListPartial", mAssetTypeLister);
        }

        [HttpPost]
        public ActionResult SaveAssetType(AssetType mAssetType)
        {
            APIResponse mAPIResponse = new APIResponse();
            try
            {
                mAssetType.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssetType);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("AssetType/SaveAssetType"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAPIResponse = JsonConvert.DeserializeObject<APIResponse>(jsonString);
                        if (mAPIResponse.Value != null && mAPIResponse.IsSuccess)
                        {
                            ViewBag.Message = "Asset type has been saved.";
                            ViewBag.Type = "Success";
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "Error occured while saving Asset type!";
                        }
                    }
                    else
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error.";
                    }
                }
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error.";
            }
            finally
            {
                ViewBag.AssetType = new SelectList(ExtensionMethod.BuildAssetType(), "Id", "Name");
            }
            return PartialView("_AddAssetTypePartial", new AssetType());
        }

        public ActionResult GetAssetTypeById(int id)
        {
            AssetType mAssetType = new AssetType();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("AssetType/GetAssetTypeById/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssetType = JsonConvert.DeserializeObject<AssetType>(jsonString);
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
                ViewBag.AssetType = new SelectList(ExtensionMethod.BuildAssetType(), "Id", "Name");
            }
            return PartialView("~/Views/AssetType/_AddAssetTypePartial.cshtml", mAssetType);
        }

        public ActionResult DeleteAssetTypeById(int id)
        {
            APIResponse mAPIResponse = new APIResponse();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("AssetType/DeleteAssetTypeById/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAPIResponse = JsonConvert.DeserializeObject<APIResponse>(jsonString);
                    }
                    else
                    {
                        mAPIResponse.IsSuccess = false;
                        mAPIResponse.Message = "Internal server error.";
                    }
                }
            }
            catch (Exception ex)
            {
                mAPIResponse.Message = ex.Message.ToString();
            }
            return Json(mAPIResponse, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetAttributesByAssestId(int id)
        {
            List<AssetAttribute> mAssetAttributes = new List<AssetAttribute>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("AssetType/GetAttributesByAssestId/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssetAttributes = JsonConvert.DeserializeObject<List<AssetAttribute>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return Json(mAssetAttributes, JsonRequestBehavior.AllowGet);
        }

        public ActionResult AddAttributes(int id)
        {
            AssetAttribute mAssetAttribute = new AssetAttribute();
            mAssetAttribute.AssetTypeId = id;
            return PartialView("~/Views/AssetType/_AddAttributesPartial.cshtml", mAssetAttribute);
        }

        public ActionResult SaveAttributes(AssetAttribute mAssetAttribute)
        {
            APIResponse mAPIResponse = new APIResponse();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    mAssetAttribute.CreatedBy = ClsHttpContent.LoginUser.Id;
                    var jsonStr = JsonConvert.SerializeObject(mAssetAttribute);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("AssetType/SaveAttributes"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAPIResponse = JsonConvert.DeserializeObject<APIResponse>(jsonString);
                        if (mAPIResponse.Value != null && mAPIResponse.IsSuccess)
                        {
                            mAPIResponse.Message = "Attribute has been saved.";
                            mAPIResponse.IsSuccess = true;
                        }
                        else
                        {
                            mAPIResponse.IsSuccess = false;
                            mAPIResponse.Message = "Error occured while saving attribute!";
                        }
                    }
                    else
                    {
                        mAPIResponse.IsSuccess = false;
                        mAPIResponse.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                mAPIResponse.IsSuccess = false;
                mAPIResponse.Message = ex.Message;
            }
            return Json(mAPIResponse, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetAttributesById(int id)
        {
            AssetAttribute mAssetAttribute = new AssetAttribute();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("AssetType/GetAttributesById/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssetAttribute = JsonConvert.DeserializeObject<AssetAttribute>(jsonString);
                      
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
            return PartialView("~/Views/AssetType/_AddAttributesPartial.cshtml", mAssetAttribute);
        }

        public ActionResult DeleteAttributesById(int id)
        {
            APIResponse mAPIResponse = new APIResponse();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("AssetType/DeleteAttributesById/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAPIResponse = JsonConvert.DeserializeObject<APIResponse>(jsonString);
                    }
                    else
                    {
                        mAPIResponse.IsSuccess = false;
                        mAPIResponse.Message = "Internal server error.";
                    }
                }
            }
            catch (Exception ex)
            {
                mAPIResponse.Message = ex.Message.ToString();
            }
            return Json(mAPIResponse, JsonRequestBehavior.AllowGet);
        }

        public ActionResult CircuitDiagramCreator(int assetTypeId)
        {
            ViewBag.AssetTypeId = assetTypeId;
            if (assetTypeId == 1)
            {
                ViewBag.DlAssetTypeId = 5;

            }
            else if (assetTypeId == 2)
            {
                ViewBag.DlAssetTypeId = 6;

            }
            else if (assetTypeId == 3)
            {
                ViewBag.DlAssetTypeId = 4;

            }
            return View();
        }

        public ActionResult SaveCircuitDiagramCreator(Domain.AssetTypeCircuitDiagram mAssetTypeCircuitDiagram)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    mAssetTypeCircuitDiagram.CreatedBy = ClsHttpContent.LoginUser.Id;
                    var jsonStr = JsonConvert.SerializeObject(mAssetTypeCircuitDiagram);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("AssetType/CreateAssetTypeCircuitDiagram"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetTypeCircuitDiagram = JsonConvert.DeserializeObject<Domain.AssetTypeCircuitDiagram>(jsonString);
                        if (mAssetTypeCircuitDiagram != null && mAssetTypeCircuitDiagram.Id > 0)
                        {
                            data = new { type = "success", result = "Circuit Diagram has been Created." };
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
                data = new { type = "error", result = "Internal server error." };
            }
            return Json(data, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetAssetTypeCircuitDiagram(int assetTypeId)
        {
            Domain.AssetTypeCircuitDiagram mAssetTypeCircuitDiagram = new Domain.AssetTypeCircuitDiagram();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
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
            }
            return Json(mAssetTypeCircuitDiagram, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetDataloggerAttribute(int id)
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

        public JsonResult GetSensorType(int sensorType)
        {
            if (sensorType == (int)E7FRSAdvance.Utility.Utility.ValueType.Current)
            {
                var currentSensorTypeEnumData = (from E7FRSAdvance.Utility.Utility.CurrentSensorType e in Enum.GetValues(typeof(E7FRSAdvance.Utility.Utility.CurrentSensorType))
                                                 select new
                                                 {
                                                     Id = (int)e,
                                                     Name = EnumHelper.GetDisplayName(e)
                                                 }).ToList();

                return Json(currentSensorTypeEnumData, JsonRequestBehavior.AllowGet);
            }
            else if (sensorType == (int)E7FRSAdvance.Utility.Utility.ValueType.Voltage)
            {
                var currentSensorTypeEnumData = (from E7FRSAdvance.Utility.Utility.VoltageSensorType e in Enum.GetValues(typeof(E7FRSAdvance.Utility.Utility.VoltageSensorType))
                                                 select new
                                                 {
                                                     Id = (int)e,
                                                     Name = EnumHelper.GetDisplayName(e)
                                                 }).ToList();

                return Json(currentSensorTypeEnumData, JsonRequestBehavior.AllowGet);
            }

            return Json(string.Empty, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetFRSAttribute(string code)
        {
            return Json(ExtensionMethod.BuildAssetAttribute(code), JsonRequestBehavior.AllowGet);
        }

        public AssetType GetBy(int id)
        {
            AssetType mAssetType = new AssetType();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("AssetType/GetAssetTypeById/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssetType = JsonConvert.DeserializeObject<AssetType>(jsonString);
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

            return mAssetType;
        }
    }
}