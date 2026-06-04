using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Utility;
using Microsoft.VisualBasic.FileIO;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web;
using System.Web.Mvc;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class DataloggerController : Controller
    {
        // GET: Datalogger
        private readonly ISiteService siteService;
        public DataloggerController(ISiteService siteService)
        {
            this.siteService = siteService;
        }
        public ActionResult Index()
        {
            ViewBag.Sites = new SelectList(siteService.GetDataLoggerSite(), "Id", "Name");
            return View();
        }

        public ActionResult Get(Domain.DataloggerAssetLister mDataloggerAssetLister)
        {
            try
            {
                mDataloggerAssetLister.Pager.Take = -1;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mDataloggerAssetLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("DataloggerAsset/GetAll"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mDataloggerAssetLister = JsonConvert.DeserializeObject<DataloggerAssetLister>(jsonString);
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
            return PartialView("_List", mDataloggerAssetLister);
        }

        public ActionResult _DataloggerFileFormatPartial(int siteId)
        {

            var mSite = siteService.Get(siteId);
            if (mSite != null && mSite.Id > 0)
            {
                ViewBag.IsDataLoggerJsonReverse = mSite.IsDataLoggerJsonReverse;
                ViewBag.DataLoggerVersion = mSite.DataLoggerVersion;
                ViewBag.MQTTBasePath = mSite.MQTTBasePath;
            }

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
            finally
            {
                ViewBag.MQTTDetail = GetMQTTDetailList().mQTTDetailWeb;
            }
            return PartialView(mDataloggerFileFormats);
        }

        public ActionResult GetAssetType(int siteId)
        {
            List<DataloggerAssetType> mDataloggerAssetTypes = new List<DataloggerAssetType>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("DataloggerAssetType/SiteId/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mDataloggerAssetTypes = JsonConvert.DeserializeObject<List<DataloggerAssetType>>(jsonString);
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
            return PartialView("_AssetType", mDataloggerAssetTypes);
        }

        public ActionResult Download(int siteId)
        {
            List<DataloggerFileFormat> mDataloggerFileFormats = new List<DataloggerFileFormat>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("DataloggerFileFormat/SiteId/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mDataloggerFileFormats = JsonConvert.DeserializeObject<List<DataloggerFileFormat>>(jsonString);
                        if (mDataloggerFileFormats != null && mDataloggerFileFormats.Count > 0)
                        {
                            var csv = new StringBuilder();
                            var socsvstring = string.Empty;
                            socsvstring = string.Format("{0},{1},{2}", "SNO", "RELAY NAME", "CONTACT TYPE");
                            csv.AppendLine(socsvstring);

                            foreach (var mDataloggerFileFormat in mDataloggerFileFormats)
                            {
                                socsvstring = string.Format("{0},{1},{2}", mDataloggerFileFormat.SrNo, mDataloggerFileFormat.RelayName, mDataloggerFileFormat.ContactType.RemoveComma());
                                csv.AppendLine(socsvstring);
                            }


                            Response.Clear();
                            Response.Buffer = true;
                            Response.AddHeader("content-disposition", "attachment;filename=Datalogger" + DateTime.Now.Ticks + ".csv");
                            Response.Charset = "utf-8";
                            Response.ContentType = "text/csv";
                            Response.Output.Write(csv);
                            Response.Flush();
                            Response.End();
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
            return RedirectToAction("Index");
        }

        public ActionResult GetAsset(int assetId)
        {
            DataloggerAsset mDataloggerAsset = new DataloggerAsset();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("DataloggerAsset/{0}", assetId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mDataloggerAsset = JsonConvert.DeserializeObject<DataloggerAsset>(jsonString);
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
            return PartialView("_Asset", mDataloggerAsset);
        }

        public MQTTDetailList GetMQTTDetailList()
        {

            MQTTDetailList mMQTTDetail = new MQTTDetailList();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
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

        public ActionResult UploadDatalogger(int siteId)
        {
            dynamic data = new ExpandoObject();
            try
            {
                for (int i = 0; i < Request.Files.Count; i++)
                {
                    var file = Request.Files[i];
                    var csvBody = string.Empty;

                    var mDataloggerAssets = ReadDataloggerFromCSVFile(file, siteId);
                    if (mDataloggerAssets != null && mDataloggerAssets.Count > 0)
                    {
                        data = new { type = "success", result = mDataloggerAssets };
                    }

                }
            }
            catch (ArgumentNullException ex)
            {
                data = new { type = "error", result = ex.ParamName };
            }
            catch (Exception)
            {
                data = new { type = "error", result = "Internal server error!" };
            }

            return Json(data, JsonRequestBehavior.AllowGet);

        }

        public List<Domain.DataloggerAssetType> GetAssetTypes()
        {
            List<Domain.DataloggerAssetType> mDataloggerAssetTypes = new List<Domain.DataloggerAssetType>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("DataloggerAssetType/GetAll")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mDataloggerAssetTypes = JsonConvert.DeserializeObject<List<Domain.DataloggerAssetType>>(jsonString);
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
            return mDataloggerAssetTypes;
        }

        [HttpPost]
        public JsonResult CreateAsset(Domain.DataloggerFileFormat mDataloggerFileFormat)
        {
            dynamic data = new { type = "", result = "" };
            try
            {
                mDataloggerFileFormat.CreatedBy = ClsHttpContent.LoginUser.Id;
                HttpResponseMessage response;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mDataloggerFileFormat);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    response = hcf.client.PutAsync("DataloggerFileFormat", str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string result = response.Content.ReadAsStringAsync().Result;
                        var outcome = JsonConvert.DeserializeObject<Dictionary<int, string>>(result);
                        if (outcome != null && outcome.FirstOrDefault().Key > 0)
                            data = new { type = "success", result = outcome.Select(x => x.Value).FirstOrDefault() };
                        else
                            data = new { type = "error", result = outcome.Select(x => x.Value).FirstOrDefault() };
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                        data = new { type = "error", result = "Internal server error." };
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
            }
            return Json(mAssetAttributes, JsonRequestBehavior.AllowGet);
        }

        public ActionResult DeleteAssetById(int id)
        {
            dynamic data = new ExpandoObject();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.DeleteAsync($"DataloggerAsset/{id}").Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var result = JsonConvert.DeserializeObject<bool>(jsonString);

                        if (result)
                            data = new { type = "success", result = "Asset has been Deleted." };
                        else
                            data = new { type = "error", result = "Internal server error." };
                    }
                    else
                        data = new { type = "error", result = "Internal server error." };
                }
            }
            catch (Exception ex)
            {
                data = new { type = "error", result = "Internal server error." };
            }
            return Json(data, JsonRequestBehavior.AllowGet);
        }


        public ActionResult DeleteAssetBySiteId(int siteId)
        {
            dynamic data = new ExpandoObject();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.DeleteAsync($"DataloggerAsset/DeleteBySiteId/SiteId/{siteId}").Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var result = JsonConvert.DeserializeObject<bool>(jsonString);

                        if (result)
                            data = new { type = "success", result = "Asset has been Deleted." };
                        else
                            data = new { type = "error", result = "Internal server error." };
                    }
                    else
                        data = new { type = "error", result = "Internal server error." };
                }
            }
            catch (Exception ex)
            {
                data = new { type = "error", result = "Internal server error." };
            }
            return Json(data, JsonRequestBehavior.AllowGet);
        }

        public ActionResult _DataLoggerEvent(Domain.SearchCriteria searchCriteria)
        {
            List<Domain.Datalogger> mDataloggers = new List<Domain.Datalogger>();
            // if (searchDate.IsNullOrEmpty())
            searchCriteria.SearchDate = DateTime.Now.ToString("d-M-yyyy");
            ViewBag.siteId = searchCriteria.SiteId;

            searchCriteria.StartDate = DateTime.Now.AddHours(-1);
            searchCriteria.EndDate = DateTime.Now;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(searchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("DataloggerAsset/GetTempGraphData"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mDataloggers = JsonConvert.DeserializeObject<List<Domain.Datalogger>>(jsonString);
                        if (mDataloggers != null && mDataloggers.Count > 0)
                        {
                            mDataloggers = mDataloggers.OrderByDescending(x => x.Date).ToList();
                            TempData.Remove("DataloggerSearchCriteria");
                        }

                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView(mDataloggers);
        }

        public ActionResult GetDataLoggerAllEvent(Domain.SearchCriteria searchCriteria)
        {
            List<Domain.Datalogger> mDataloggers = new List<Domain.Datalogger>();
            // if (searchDate.IsNullOrEmpty())
            searchCriteria.SearchDate = DateTime.Now.ToString("d-M-yyyy");
            var sdateTime = DateTime.Now.ToShortDateString();
            ViewBag.siteId = searchCriteria.SiteId;

            searchCriteria.StartDate = DateTime.Now.Date;
            searchCriteria.EndDate = DateTime.Now.AddHours(-2);

            //if (searchCriteria.StartTime.IsNotNullOrEmpty())
            //{

            //    var date = $"{sdateTime} {searchCriteria.StartTime}";
            //    searchCriteria.StartDate = Convert.ToDateTime(date);
            //}
            //if (searchCriteria.EndTime.IsNotNullOrEmpty())
            //{
            //    var date = $"{sdateTime} {searchCriteria.EndTime}";
            //    searchCriteria.EndDate = Convert.ToDateTime(date);
            //}

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(searchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("DataloggerAsset/GetTempGraphData"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mDataloggers = JsonConvert.DeserializeObject<List<Domain.Datalogger>>(jsonString);
                        if (mDataloggers != null && mDataloggers.Count > 0)
                        {
                            mDataloggers = mDataloggers.OrderByDescending(x => x.Date).ToList();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            //return Json(mDataloggers, JsonRequestBehavior.AllowGet);
            var jsonResult = Json(mDataloggers, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;
        }

        public ActionResult SearchEvent(Domain.SearchCriteria searchCriteria)
        {
            var mDataloggerFileFormats = new List<Domain.DataloggerFileFormat>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"DataloggerFileFormat/siteId/{searchCriteria.SiteId}/Search/{searchCriteria.Type}")).Result;
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
            var jsonResult = Json(mDataloggerFileFormats, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;
        }

        public ActionResult _EventGraphDeadband(Domain.SearchCriteria searchCriteria)
        {
            List<Domain.Datalogger> mDataloggers = new List<Domain.Datalogger>();
            ViewBag.siteId = searchCriteria.SiteId;

            // if (searchDate.IsNullOrEmpty())
            var sdateTime = DateTime.Now;
            if (searchCriteria.SearchDate.IsNotNullOrEmpty())
            {
                sdateTime = ExtensionMethod.ConvertToDateTimeFormat(searchCriteria.SearchDate, "d/M/yyyy");
                searchCriteria.SearchDate = sdateTime.ToString("d-M-yyyy");
            }
            else
            {
                searchCriteria.SearchDate = DateTime.Now.ToString("d-M-yyyy");
            }

            if (searchCriteria.StartTime.IsNotNullOrEmpty())
            {

                var date = $"{sdateTime.ToShortDateString()} {searchCriteria.StartTime}";
                searchCriteria.StartDate = Convert.ToDateTime(date);
            }
            if (searchCriteria.EndTime.IsNotNullOrEmpty())
            {
                var date = $"{sdateTime.ToShortDateString()} {searchCriteria.EndTime}";
                searchCriteria.EndDate = Convert.ToDateTime(date);
            }

            TempData.Remove("DataloggerSearchCriteria");
            TempData["DataloggerSearchCriteria"] = searchCriteria;
            TempData.Keep();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(searchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("DataloggerAsset/GetTempGraphData"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mDataloggers = JsonConvert.DeserializeObject<List<Domain.Datalogger>>(jsonString);
                        if (mDataloggers != null && mDataloggers.Count > 0)
                        {
                            mDataloggers = mDataloggers.OrderByDescending(x => x.Date).ToList();
                        }


                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView(mDataloggers);
        }

        public ActionResult _SeachEventGraphDeadband(int siteId)
        {
            var tempSearchCriteria = TempData.Peek("DataloggerSearchCriteria");
            if (tempSearchCriteria != null)
            {
                var searchCriteria = tempSearchCriteria as SearchCriteria;
                if (searchCriteria != null && searchCriteria.SrNumbers != null)
                {
                    //var split
                    ViewBag.SrNumbers = searchCriteria.SrNumbers;
                }
                if (searchCriteria != null && searchCriteria.RelayNames != null)
                {
                    //var split
                    ViewBag.RelayNames = searchCriteria.RelayNames;
                }

            }


            return PartialView();
        }

        public ActionResult AddSeachEventGraphDeadband(List<Domain.Datalogger> dataloggers)
        {
            return PartialView();
        }

        public ActionResult Downloadcsv(int siteId)
        {
            var searchDate = DateTime.Now.ToString("d-M-yyyy");
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("DataloggerAsset/GetDownloadTempGraphData/{0}/{1}", siteId, searchDate.Replace("/", "-"))).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {

                                return File(csvbytes, "application/csv", $"Datalogger.csv");
                            }
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "File not found!";
                        }

                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
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
            return View("Index");
        }

        #region Private Method

        private List<DataloggerAsset> ReadDataloggerFromCSVFile(HttpPostedFileBase file, int siteId)
        {
            List<DataloggerAsset> mDataloggers = new List<DataloggerAsset>();
            string[] expectedHeaders = new string[] { "SNO", "RELAY NAME", "RELAY TYPE", "CONTACT TYPE" };

            // Read bytes from http input stream
            var csvBody = string.Empty;

            using (BinaryReader b = new BinaryReader(file.InputStream))
            {
                byte[] binData = b.ReadBytes(file.ContentLength);
                csvBody = Encoding.UTF8.GetString(binData);
            }

            var memoryStream = new MemoryStream();
            var streamWriter = new StreamWriter(memoryStream);
            streamWriter.Write(csvBody);
            streamWriter.Flush();
            memoryStream.Position = 0;

            using (TextFieldParser csvReader = new TextFieldParser(memoryStream))
            {
                csvReader.SetDelimiters(new string[] { "," });
                csvReader.HasFieldsEnclosedInQuotes = true;
                string[] colFields = csvReader.ReadFields();


                if (colFields == null || colFields.Length < expectedHeaders.Length)
                    throw new ArgumentNullException("Invalid CSV header");

                for (int i = 0; i < expectedHeaders.Length; i++)
                {
                    if (!string.Equals(colFields[i], expectedHeaders[i], StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentNullException($"Invalid header at position {i}: {colFields[i]} expected: {expectedHeaders[i]}");
                }

                while (!csvReader.EndOfData)
                {
                    try
                    {
                        string[] fieldData = csvReader.ReadFields();
                        if (fieldData.Length > 1)
                        {
                            DataloggerAsset mDatalogger = new DataloggerAsset();
                            mDatalogger.SrNo = Convert.ToInt32(fieldData[0]);
                            mDatalogger.RelayName = fieldData[1];
                            mDatalogger.RelayType = fieldData[2];
                            mDatalogger.ContactType = fieldData[3];
                            mDatalogger.SiteId = siteId;
                            mDatalogger.CreatedBy = ClsHttpContent.LoginUser.Id;
                            mDataloggers.Add(mDatalogger);
                        }
                    }
                    catch (Exception ex)
                    {
                    }

                }
            }

            return mDataloggers;
        }

        //private List<DataloggerAsset> SetDataloggerAsset(List<DataloggerAsset> mDataloggerAssets)
        //{
        //    if (mDataloggerAssets != null && mDataloggerAssets.Count > 0)
        //    {
        //        foreach (var mDataloggerAsset in mDataloggerAssets)
        //        {
        //            var relayName = mDataloggerAsset.RelayName.Split(' ');
        //            if (relayName != null && relayName.Length > 0)
        //            {
        //                string assetName = string.Empty;
        //                assetName = mDataloggerAsset.RelayName.Replace(relayName.LastOrDefault(), "");
        //                if (assetName.IsNullOrEmpty())
        //                {
        //                    foreach (var mAttribute in mDataloggerAttributes)
        //                    {
        //                        if (relayName.LastOrDefault().Trim().ToLower().Contains(mAttribute.Title.Trim().ToLower()))
        //                        {
        //                            assetName = relayName.LastOrDefault().Replace(mAttribute.Title, "");
        //                            if (assetName.IsNotNullOrEmpty())
        //                            {
        //                                //assetName = assetName.RemoveSpecialCharacters();

        //                                mDataloggerAsset.Name = assetName.Trim();
        //                                mDataloggerAsset.DataloggerAssetTypeId = mAttribute.DataloggerAssetTypeId;
        //                                mDataloggerAsset.DataloggerAttributeId = mAttribute.Id;
        //                                mDataloggerAsset.AttributeName = mAttribute.Title;
        //                                mDataloggerAsset.AssetTypeName = mAttribute.DataloggerAssetType;
        //                            }
        //                            break;
        //                        }
        //                    }

        //                    //var mDataloggersdsAttribute = mDataloggerAttributes.Where(x => x.Title.Trim().ToLower() == relayName.LastOrDefault().Trim().ToLower()).FirstOrDefault();
        //                }
        //                else
        //                {
        //                    var mDataloggerAttribute = mDataloggerAttributes.Where(x => x.Title.Trim().ToLower() == relayName.LastOrDefault().Trim().ToLower()).FirstOrDefault();
        //                    if (mDataloggerAttribute != null && mDataloggerAttribute.Id > 0 && assetName.IsNotNullOrEmpty())
        //                    {
        //                        mDataloggerAsset.Name = assetName.Trim();
        //                        mDataloggerAsset.DataloggerAssetTypeId = mDataloggerAttribute.DataloggerAssetTypeId;
        //                        mDataloggerAsset.DataloggerAttributeId = mDataloggerAttribute.Id;
        //                        mDataloggerAsset.AttributeName = mDataloggerAttribute.Title;
        //                        mDataloggerAsset.AssetTypeName = mDataloggerAttribute.DataloggerAssetType;
        //                    }
        //                }


        //            }
        //        }


        //        //if (mDataloggerAssets != null)
        //        //{
        //        //    var assetGroup = mDataloggerAssets.Where(x => x.Name != null).GroupBy(x => new { x.SiteId, x.DataloggerAssetTypeId, x.Name }).Select(x => x.Key).ToList();
        //        //    if (assetGroup != null && assetGroup.Count > 0)
        //        //    {
        //        //        foreach (var asset in assetGroup)
        //        //        {
        //        //            var mDataloggerInfos = new List<DataloggerInfo>();
        //        //            var mAssetAttributes = mDataloggerAssets.Where(x => x.Name != null && x.SiteId == asset.SiteId && x.DataloggerAssetTypeId == asset.DataloggerAssetTypeId && x.Name.Trim().ToLower() == asset.Name.Trim().ToLower()).ToList();
        //        //            if (mAssetAttributes != null && mAssetAttributes.Count > 0)
        //        //            {
        //        //                foreach (var mAssetAttribute in mAssetAttributes)
        //        //                {
        //        //                    mDataloggerInfos.Add(new DataloggerInfo() { DataloggerAttributeId = mAssetAttribute.DataloggerAttributeId, SrNo = mAssetAttribute.SrNo, ContactType = mAssetAttribute.ContactType, CreatedBy = ClsHttpContent.LoginUser.Id });
        //        //                }
        //        //            }
        //        //            dataloggerAsset.Add(new Domain.DataloggerAsset() { SiteId = asset.SiteId, DataloggerAssetTypeId = asset.DataloggerAssetTypeId, Name = asset.Name, CreatedBy = ClsHttpContent.LoginUser.Id, mDataloggerInfos = mDataloggerInfos });


        //        //        }
        //        //    }

        //        //}
        //    }

        //    return mDataloggerAssets;

        //}

        //private List<DataloggerAsset> PrepareDataloggerAsset(List<DataloggerAsset> mDataloggerAssets)
        //{
        //    var dataloggerAsset = new List<Domain.DataloggerAsset>();
        //    if (mDataloggerAssets != null && mDataloggerAssets.Count > 0)
        //    {
        //        var assetGroup = mDataloggerAssets.Where(x => x.Name != null).GroupBy(x => new { x.SiteId, x.DataloggerAssetTypeId, x.Name }).Select(x => x.Key).ToList();
        //        if (assetGroup != null && assetGroup.Count > 0)
        //        {
        //            foreach (var asset in assetGroup)
        //            {
        //                var mDataloggerInfos = new List<DataloggerInfo>();
        //                string assetTypeName = string.Empty;
        //                var mAssetAttributes = mDataloggerAssets.Where(x => x.Name != null && x.SiteId == asset.SiteId && x.DataloggerAssetTypeId == asset.DataloggerAssetTypeId && x.Name.Trim().ToLower() == asset.Name.Trim().ToLower()).ToList();
        //                if (mAssetAttributes != null && mAssetAttributes.Count > 0)
        //                {
        //                    foreach (var mAssetAttribute in mAssetAttributes)
        //                    {
        //                        mDataloggerInfos.Add(new DataloggerInfo() { DataloggerAttributeId = mAssetAttribute.DataloggerAttributeId, SrNo = mAssetAttribute.SrNo, ContactType = mAssetAttribute.ContactType, DataloggerAttribute = mAssetAttribute.AttributeName, CreatedBy = ClsHttpContent.LoginUser.Id });

        //                        assetTypeName = mAssetAttribute.AssetTypeName;

        //                    }
        //                }
        //                dataloggerAsset.Add(new Domain.DataloggerAsset() { SiteId = asset.SiteId, DataloggerAssetTypeId = asset.DataloggerAssetTypeId, Name = asset.Name, CreatedBy = ClsHttpContent.LoginUser.Id, mDataloggerInfos = mDataloggerInfos, AssetTypeName = assetTypeName });


        //            }
        //        }


        //    }

        //    return dataloggerAsset;

        //}

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

        private void CreateDataloggerFileFormat(List<Domain.DataloggerFileFormat> mDataloggerFileFormats)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mDataloggerFileFormats);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format("DataloggerFileFormat"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;

                }
            }
            catch (Exception ex)
            {
            }

        }

        private List<Domain.DataloggerFileFormat> PrepareDataloggerFileFormat(List<DataloggerAsset> mDataloggerAssets)
        {
            List<Domain.DataloggerFileFormat> mDataloggerFileFormats = new List<DataloggerFileFormat>();
            if (mDataloggerAssets != null && mDataloggerAssets.Count > 0)
            {
                foreach (var assets in mDataloggerAssets)
                {
                    mDataloggerFileFormats.Add(new Domain.DataloggerFileFormat() { SrNo = assets.SrNo, RelayName = assets.RelayName, ContactType = assets.ContactType, SiteId = assets.SiteId, CreatedBy = ClsHttpContent.LoginUser.Id });
                }
            }

            return mDataloggerFileFormats;
        }

        #endregion
    }
}