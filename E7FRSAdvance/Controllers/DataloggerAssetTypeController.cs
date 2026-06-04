using Domain;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System.Net.Http;
using System.Net;
using System.Text;
using System;
using System.Web.Mvc;
using System.Collections.Generic;
using System.Dynamic;
using E7FRSAdvance.Utility;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class DataloggerAssetTypeController : Controller
    {
        // GET: DataloggerAssetType
        public ActionResult Index()
        {
            ViewBag.AssetType = new SelectList(ExtensionMethod.BuildAssetType(), "Id", "Name");
            return View();
        }

        public PartialViewResult List(DataloggerAssetTypeLister mAssetTypeLister)
        {
            mAssetTypeLister.Pager.Take = mAssetTypeLister.Pager.PageSize;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssetTypeLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("DataloggerAssetType/GetAll"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetTypeLister = JsonConvert.DeserializeObject<DataloggerAssetTypeLister>(jsonString);
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
            return PartialView(mAssetTypeLister);
        }

        [HttpPost]
        public ActionResult Save(DataloggerAssetType mAssetType)
        {
            try
            {
                mAssetType.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssetType);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("DataloggerAssetType"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetType = JsonConvert.DeserializeObject<DataloggerAssetType>(jsonString);
                        if (mAssetType != null && mAssetType.Id > 0)
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
            return PartialView("_Add", new DataloggerAssetType());
        }

        public ActionResult GetAssetTypeById(int id)
        {
            DataloggerAssetType mAssetType = new DataloggerAssetType();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("DataloggerAssetType/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssetType = JsonConvert.DeserializeObject<DataloggerAssetType>(jsonString);
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
            return PartialView("_Add", mAssetType);
        }

        public ActionResult DeleteAssetTypeById(int id)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.DeleteAsync(String.Format("DataloggerAssetType/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var result = JsonConvert.DeserializeObject<bool>(jsonString);
                        if (result)
                        {
                            data = new { type = "success", result = "Attribute has been Deleted." };
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

        public ActionResult GetAttributesByAssestId(int id)
        {
            List<Domain.DataloggerAttribute> mAssetAttributes = new List<Domain.DataloggerAttribute>();
            ViewBag.AssetTypeId = id;
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
            return PartialView("_AttributesPartial", mAssetAttributes);
        }

        public ActionResult AddAttributes(int id)
        {
            Domain.DataloggerAttribute mAssetAttribute = new Domain.DataloggerAttribute();
            mAssetAttribute.DataloggerAssetTypeId = id;
            mAssetAttribute.Id = 0;

            var DataloggerAssetType = GetDataloggerAssetType(id);
            if (DataloggerAssetType != null && DataloggerAssetType.Id > 0)
            {
                mAssetAttribute.AssetTypeRepresentationCode = DataloggerAssetType.RepresentationCode;
            }

            return PartialView("_AddAttributesPartial", mAssetAttribute);
        }

        public ActionResult EditAttributes(int id)
        {
            Domain.DataloggerAttribute mAssetAttribute = new Domain.DataloggerAttribute();

            if (id > 0)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = hcf.client.GetAsync(String.Format("DataloggerAssetType/GetDataloggerAttributeBy/{0}", id)).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            mAssetAttribute = JsonConvert.DeserializeObject<Domain.DataloggerAttribute>(jsonString);
                        }
                    }
                }
                catch (Exception ex)
                {
                    ViewBag.Error = ex.Message.ToString();
                }
                finally
                {
                    var DataloggerAssetType = GetDataloggerAssetType(mAssetAttribute.DataloggerAssetTypeId);
                    if (DataloggerAssetType != null && DataloggerAssetType.Id > 0)
                    {
                        mAssetAttribute.AssetTypeRepresentationCode = DataloggerAssetType.RepresentationCode;
                    }
                }

            }


            return PartialView("_AddAttributesPartial", mAssetAttribute);
        }

        public ActionResult SaveAttributes(DataloggerAttribute mAssetAttribute)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    mAssetAttribute.CreatedBy = ClsHttpContent.LoginUser.Id;
                    var jsonStr = JsonConvert.SerializeObject(mAssetAttribute);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("DataloggerAssetType/SaveDataloggerAttribute"), str).Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        ViewBag.Message = "Attribute has been saved.";
                        ViewBag.Type = "Success";
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
            return PartialView("_AddAttributesPartial", mAssetAttribute);
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
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("DataloggerAssetType/DeleteDataloggerAttribute/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var result = JsonConvert.DeserializeObject<bool>(jsonString);
                        if (result)
                        {
                            data = new { type = "success", result = "Attribute has been Deleted." };
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

        public DataloggerAssetType GetDataloggerAssetType(int id)
        {
            DataloggerAssetType mAssetType = new DataloggerAssetType();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("DataloggerAssetType/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssetType = JsonConvert.DeserializeObject<DataloggerAssetType>(jsonString);
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