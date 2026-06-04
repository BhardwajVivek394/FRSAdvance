using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Service;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Policy;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Mvc;
using System.Web.Services.Description;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class YardConfigController : Controller
    {
        // GET: YardConfig
        private readonly ISiteService siteService;
        private readonly IAssetTypeService assetTypeService;
        private readonly IAssetAttributeService assetAttributeService;
        private readonly IAssetService assetService;
        public YardConfigController(ISiteService siteService, IAssetTypeService assetTypeService, IAssetAttributeService assetAttributeService, IAssetService assetService)
        {
            this.siteService = siteService;
            this.assetTypeService = assetTypeService;
            this.assetAttributeService = assetAttributeService;
            this.assetService = assetService;
        }

        public ActionResult Index()
        {
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            return View();
        }

        public ActionResult GetYardConfig(int siteId)
        {
            List<CardLine> mCardLines = new List<CardLine>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("CardLine/SiteId/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mCardLines = JsonConvert.DeserializeObject<List<CardLine>>(jsonString);
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

            return PartialView("_YardConfigPartial", mCardLines);

        }

        public ActionResult GetYardConfigByAssetType(int siteId, int assetTypeId)
        {
            List<CardLine> mCardLines = new List<CardLine>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("CardLine/SiteId/{0}/assetTypeId/{1}", siteId, assetTypeId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mCardLines = JsonConvert.DeserializeObject<List<CardLine>>(jsonString);
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

            return PartialView("_YardConfigByAssetTypePartial", mCardLines);

        }

        public ActionResult GetAssetTypeBySiteId(int siteId)
        {
            List<Domain.AssetType> mAssetTypes = new List<Domain.AssetType>();
            if (siteId > 0)
            {
                try
                {
                    using (var mHttpClientFactory = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = mHttpClientFactory.client.GetAsync(String.Format("AssetType/GetAllAssestType/{0}", siteId)).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            mAssetTypes = JsonConvert.DeserializeObject<List<Domain.AssetType>>(jsonString);
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
            }

            return Json(mAssetTypes, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetAssetById(int assetTypeId, int siteId)
        {
            Asset mAsset = new Asset();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Asset/SiteId/{0}/AssetTypeId/{1}", siteId, assetTypeId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAsset = JsonConvert.DeserializeObject<Asset>(jsonString);
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
            return PartialView("_AddAssetPartial", mAsset);
        }

        public ActionResult DownloadYardConfig(int siteId)
        {
            List<CardLine> mCardLines = new List<CardLine>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("CardLine/SiteId/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mCardLines = JsonConvert.DeserializeObject<List<CardLine>>(jsonString);
                        if (mCardLines != null && mCardLines.Count > 0)
                        {
                            var csv = new StringBuilder();
                            var socsvstring = string.Empty;
                            var ClusterNameGroup = mCardLines.GroupBy(x => x.ClusterName).ToList();
                            socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9}", "", "Cluster", "Type", "Asset", "Attribute", "ADC", "Pin", "Role", "ClusterValidation", "JointCalibration");
                            csv.AppendLine(socsvstring);
                            foreach (var clusterName in ClusterNameGroup)
                            {
                                socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9}", "\"" + clusterName.Key + "\"", "", "", "", "", "", "", "", "", "");
                                csv.AppendLine(socsvstring);
                                foreach (var item in mCardLines.Where(x => x.ClusterName.Trim().ToUpper() == clusterName.Key.Trim().ToUpper()).GroupBy(x => x.CardName).Select(y => y.Key).ToList())
                                {
                                    foreach (var cardLine in mCardLines.Where(x => x.ClusterName.Trim().ToUpper() == clusterName.Key.Trim().ToUpper() && x.CardName == item).OrderBy(x => x.Pin).ToList())
                                    {
                                        string adc = string.Empty;
                                        string jointCalibration = string.Empty;
                                        string clusterValidation = string.Empty;
                                        if (cardLine.ADCNumber != null)
                                            adc = $"ADC {cardLine.SequenceNumber}({cardLine.ADCNumber})";

                                        if (cardLine.JointCalibration != null && cardLine.JointCalibration.Value)
                                            jointCalibration = "true";
                                        else
                                            jointCalibration = "false";

                                        if (cardLine.ClusterValidation != null && cardLine.ClusterValidation.Value)
                                            clusterValidation = "true";
                                        else
                                            clusterValidation = "false";

                                        socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9}", cardLine.Number, cardLine.CardName.RemoveComma(), cardLine.AssetTypeName, cardLine.AssetName.RemoveComma(), cardLine.AttributeName.RemoveComma(), adc.RemoveComma(), cardLine.Pin, cardLine.Role.RemoveComma(), clusterValidation, jointCalibration);
                                        csv.AppendLine(socsvstring);
                                    }

                                }
                            }

                            Response.Clear();
                            Response.Buffer = true;
                            Response.AddHeader("content-disposition", "attachment;filename=YardConfig" + DateTime.Now.Ticks + ".csv");
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
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            return View("YardConfig");

        }

        public ActionResult DownloadYardConfigByAssetType(int siteId, int assetTypeId)
        {
            List<CardLine> mCardLines = new List<CardLine>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("CardLine/SiteId/{0}/assetTypeId/{1}", siteId, assetTypeId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mCardLines = JsonConvert.DeserializeObject<List<CardLine>>(jsonString);
                        if (mCardLines != null && mCardLines.Count > 0)
                        {
                            var csv = new StringBuilder();
                            var socsvstring = string.Empty;
                            var assetGroup = mCardLines.GroupBy(x => x.AssetId).ToList();

                            foreach (var asset in assetGroup)
                            {
                                var attributeNameGroup = mCardLines.Where(x => x.AttributeName != null).GroupBy(x => x.AttributeName).ToList();

                                if (asset.FirstOrDefault().AssetName.IsNullOrEmpty())
                                    continue;

                                socsvstring = string.Empty;
                                socsvstring += $"{asset.FirstOrDefault().AssetName},";
                                foreach (var attributeName in attributeNameGroup)
                                {
                                    socsvstring += attributeName.Key + ",";
                                }
                                csv.AppendLine(socsvstring);


                                //Cluster
                                socsvstring = string.Empty;
                                socsvstring += "Cluster" + ",";
                                foreach (var attributeName in attributeNameGroup)
                                {
                                    foreach (var item in mCardLines.Where(x => x.AssetId == asset.Key && x.AttributeName != null && x.AttributeName.Trim().ToUpper() == attributeName.Key.Trim().ToUpper()))
                                    {
                                        socsvstring += "\"" + item.ClusterName + "\"" + ",";
                                    }
                                    if (mCardLines.Where(x => x.AssetId == asset.Key && x.AttributeName != null && x.AttributeName.Trim().ToUpper() == attributeName.Key.Trim().ToUpper()).Count() <= 0)
                                        socsvstring += "" + ",";

                                }
                                csv.AppendLine(socsvstring);


                                //Card
                                socsvstring = string.Empty;
                                socsvstring += "Card" + ",";
                                foreach (var attributeName in attributeNameGroup)
                                {
                                    foreach (var item in mCardLines.Where(x => x.AssetId == asset.Key && x.AttributeName != null && x.AttributeName.Trim().ToUpper() == attributeName.Key.Trim().ToUpper()))
                                    {
                                        socsvstring += $"{"\"" + item.CardName + "\""} ({item.Number})" + ",";
                                    }
                                    if (mCardLines.Where(x => x.AssetId == asset.Key && x.AttributeName != null && x.AttributeName.Trim().ToUpper() == attributeName.Key.Trim().ToUpper()).Count() <= 0)
                                        socsvstring += "" + ",";

                                }
                                csv.AppendLine(socsvstring);


                                //ADC
                                socsvstring = string.Empty;
                                socsvstring += "ADC" + ",";
                                foreach (var attributeName in attributeNameGroup)
                                {
                                    foreach (var item in mCardLines.Where(x => x.AssetId == asset.Key && x.AttributeName != null && x.AttributeName.Trim().ToUpper() == attributeName.Key.Trim().ToUpper()))
                                    {
                                        socsvstring += $"ADC {item.SequenceNumber} ({item.ADCNumber})" + ",";
                                    }
                                    if (mCardLines.Where(x => x.AssetId == asset.Key && x.AttributeName != null && x.AttributeName.Trim().ToUpper() == attributeName.Key.Trim().ToUpper()).Count() <= 0)
                                        socsvstring += "" + ",";

                                }
                                csv.AppendLine(socsvstring);


                                //Pin
                                socsvstring = string.Empty;
                                socsvstring += "Pin" + ",";
                                foreach (var attributeName in attributeNameGroup)
                                {
                                    foreach (var item in mCardLines.Where(x => x.AssetId == asset.Key && x.AttributeName != null && x.AttributeName.Trim().ToUpper() == attributeName.Key.Trim().ToUpper()))
                                    {
                                        socsvstring += item.Pin + ",";
                                    }
                                    if (mCardLines.Where(x => x.AssetId == asset.Key && x.AttributeName != null && x.AttributeName.Trim().ToUpper() == attributeName.Key.Trim().ToUpper()).Count() <= 0)
                                        socsvstring += "" + ",";

                                }
                                csv.AppendLine(socsvstring);

                                //Role
                                socsvstring = string.Empty;
                                socsvstring += "Role" + ",";
                                foreach (var attributeName in attributeNameGroup)
                                {
                                    foreach (var item in mCardLines.Where(x => x.AssetId == asset.Key && x.AttributeName != null && x.AttributeName.Trim().ToUpper() == attributeName.Key.Trim().ToUpper()))
                                    {
                                        socsvstring += item.Role + ",";
                                    }
                                    if (mCardLines.Where(x => x.AssetId == asset.Key && x.AttributeName != null && x.AttributeName.Trim().ToUpper() == attributeName.Key.Trim().ToUpper()).Count() <= 0)
                                        socsvstring += "" + ",";

                                }
                                csv.AppendLine(socsvstring);

                                csv.AppendLine();
                            }

                            Response.Clear();
                            Response.Buffer = true;
                            Response.AddHeader("content-disposition", "attachment;filename=YardConfigByAssetType" + DateTime.Now.Ticks + ".csv");
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
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            return View("YardConfig");

        }

        public ActionResult DownloadMisssingWebConfig(int siteId, int? assetTypeId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    HttpResponseMessage response;

                    response = hcf.client.GetAsync(String.Format("CardLine/GetMisssingWebConfig/SiteId/{0}", siteId)).Result;


                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var mCardLines = JsonConvert.DeserializeObject<List<CardLine>>(jsonString);
                        if (mCardLines != null && mCardLines.Count > 0)
                        {

                            var csv = new StringBuilder();
                            var socsvstring = string.Empty;
                            socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7}", "Asset Type", "Asset Name", "Attribute Name", "Cluster Name", "Card", "ADC", "Pin", "Role");
                            csv.AppendLine(socsvstring);
                            foreach (var mCardLine in mCardLines)
                            {

                                string adc = string.Empty;
                                //if (mCardLine.ADCNumber != null)
                                //    adc = $"ADC {mCardLine.SequenceNumber}({mCardLine.ADCNumber})";

                                socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7}", mCardLine.AssetTypeName.RemoveComma(), mCardLine.AssetName.RemoveComma(), mCardLine.AttributeName.RemoveComma(), mCardLine.ClusterName.RemoveComma(), mCardLine.CardName.RemoveComma(), adc.RemoveComma(), mCardLine.Pin, "");
                                csv.AppendLine(socsvstring);

                            }

                            Response.Clear();
                            Response.Buffer = true;
                            Response.AddHeader("content-disposition", "attachment;filename=MisssingWebConfig" + DateTime.Now.Ticks + ".csv");
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
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            return View("YardConfig");
        }

        public ActionResult DownloadMisssingClusterConfig(int siteId, int? assetTypeId)
        {
            List<CardLine> mCardLines = new List<CardLine>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    HttpResponseMessage response;

                    if (assetTypeId != null && assetTypeId.Value > 0)
                        response = hcf.client.GetAsync(String.Format("CardLine/SiteId/{0}/assetTypeId/{1}", siteId, assetTypeId)).Result;
                    else
                        response = hcf.client.GetAsync(String.Format("CardLine/SiteId/{0}", siteId)).Result;

                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mCardLines = JsonConvert.DeserializeObject<List<CardLine>>(jsonString);
                        if (mCardLines != null && mCardLines.Count > 0)
                        {
                            //mCardLines = mCardLines.Where(x => x.Pin == null).ToList();
                            var csv = new StringBuilder();
                            var socsvstring = string.Empty;
                            socsvstring = string.Format("{0},{1},{2}", "Asset Type", "Asset Name", "Attribute Name");
                            csv.AppendLine(socsvstring);

                            var assetGroup = mCardLines.GroupBy(x => x.AssetId).ToList();

                            foreach (var asset in assetGroup)
                            {
                                var attributeNameGroup = mCardLines.Where(x => x.AttributeName != null).GroupBy(x => x.AttributeName).ToList();
                                foreach (var attributeName in attributeNameGroup)
                                {

                                    if (mCardLines.Where(x => x.AssetId == asset.Key && x.AttributeName != null && x.AttributeName.Trim().ToUpper() == attributeName.Key.Trim().ToUpper()).Count() <= 0)
                                    {
                                        //var mAssetsd = mCardLines.Where(x => x.AssetId == asset.Key && (x.AssetName != null || x.AssetName != string.Empty)).ToList();
                                        var mAsset = mCardLines.Where(x => x.AssetId == asset.Key && x.AssetName != null).FirstOrDefault();
                                        if (mAsset != null)
                                        {
                                            socsvstring = string.Format("{0},{1},{2}", mAsset.AssetTypeName.RemoveComma(), mAsset.AssetName.RemoveComma(), attributeName.Key.RemoveComma());
                                            csv.AppendLine(socsvstring);

                                        }


                                    }


                                }

                            }
                            //foreach (var mCardLine in mCardLines)
                            //{

                            //    if (string.IsNullOrEmpty(mCardLine.ClusterName) || string.IsNullOrWhiteSpace(mCardLine.ClusterName))
                            //    {
                            //        string adc = string.Empty;
                            //        if (mCardLine.ADCNumber != null)
                            //            adc = $"ADC {mCardLine.SequenceNumber}({mCardLine.ADCNumber})";

                            //        socsvstring = string.Format("{0},{1},{2}", mCardLine.AssetTypeName.RemoveComma(), mCardLine.AssetName.RemoveComma(), mCardLine.AttributeName.RemoveComma());
                            //        csv.AppendLine(socsvstring);
                            //    }                           

                            //}

                            Response.Clear();
                            Response.Buffer = true;
                            Response.AddHeader("content-disposition", "attachment;filename=MisssingClusterConfig" + DateTime.Now.Ticks + ".csv");
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
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            return View("YardConfig");
        }

        public ActionResult DownloadMaterialPositioning(int siteId)
        {
            List<CardLine> mCardLines = new List<CardLine>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Maintenance/DownloadMaterialPositioning/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var strMaterialPositioning = JsonConvert.DeserializeObject<string>(jsonString);
                        if (strMaterialPositioning.IsNotNullOrEmpty())
                        {
                            Response.Clear();
                            Response.Buffer = true;
                            Response.AddHeader("content-disposition", "attachment;filename=MaterialPositioning" + DateTime.Now.Ticks + ".csv");
                            Response.Charset = "utf-8";
                            Response.ContentType = "text/csv";
                            Response.Output.Write(strMaterialPositioning);
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
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            return View("YardConfig");

        }

        public ActionResult DownloadMaterialPositioningSensorWise(int siteId)
        {
            List<CardLine> mCardLines = new List<CardLine>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Maintenance/DownloadMaterialPositioningSensorWiseExcel/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                            if (stringInBase64.IsNotNullOrEmpty())
                            {
                                byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                                if (csvbytes != null && csvbytes.Length > 0)
                                {
                                    Response.Clear();
                                    Response.ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                                    Response.AddHeader("Content-Disposition", $"attachment;filename=MaterialPositioning{DateTime.Now.Ticks}.xlsx");
                                    Response.BinaryWrite(csvbytes);
                                    Response.End();
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
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            return View("YardConfig");

        }

        public ActionResult DownloadMaterialPositioningByAssetType(int siteId, int assetTypeId)
        {
            List<CardLine> mCardLines = new List<CardLine>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("CardLine/SiteId/{0}/assetTypeId/{1}", siteId, assetTypeId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mCardLines = JsonConvert.DeserializeObject<List<CardLine>>(jsonString);
                        if (mCardLines != null && mCardLines.Count > 0)
                        {
                            var csv = new StringBuilder();
                            var socsvstring = string.Empty;
                            var assetGroup = mCardLines.GroupBy(x => x.AssetId).ToList();

                            foreach (var asset in assetGroup)
                            {
                                var attributeNameGroup = mCardLines.Where(x => x.AttributeName != null).GroupBy(x => x.AttributeName).ToList();

                                if (asset.FirstOrDefault().AssetName.IsNullOrEmpty())
                                    continue;

                                socsvstring = string.Empty;
                                socsvstring += $"{asset.FirstOrDefault().AssetName},";
                                foreach (var attributeName in attributeNameGroup)
                                {
                                    socsvstring += attributeName.Key + ",";
                                }
                                csv.AppendLine(socsvstring);


                                //Cluster
                                socsvstring = string.Empty;
                                socsvstring += "Cluster" + ",";
                                foreach (var attributeName in attributeNameGroup)
                                {
                                    foreach (var item in mCardLines.Where(x => x.AssetId == asset.Key && x.AttributeName != null && x.AttributeName.Trim().ToUpper() == attributeName.Key.Trim().ToUpper()))
                                    {
                                        socsvstring += "\"" + item.ClusterName + "\"" + ",";
                                    }
                                    if (mCardLines.Where(x => x.AssetId == asset.Key && x.AttributeName != null && x.AttributeName.Trim().ToUpper() == attributeName.Key.Trim().ToUpper()).Count() <= 0)
                                        socsvstring += "" + ",";

                                }
                                csv.AppendLine(socsvstring);


                                //Card
                                socsvstring = string.Empty;
                                socsvstring += "Card" + ",";
                                foreach (var attributeName in attributeNameGroup)
                                {
                                    foreach (var item in mCardLines.Where(x => x.AssetId == asset.Key && x.AttributeName != null && x.AttributeName.Trim().ToUpper() == attributeName.Key.Trim().ToUpper()))
                                    {
                                        socsvstring += $"{"\"" + item.CardName + "\""} ({item.Number})" + ",";
                                    }
                                    if (mCardLines.Where(x => x.AssetId == asset.Key && x.AttributeName != null && x.AttributeName.Trim().ToUpper() == attributeName.Key.Trim().ToUpper()).Count() <= 0)
                                        socsvstring += "" + ",";

                                }
                                csv.AppendLine(socsvstring);


                                //ADC
                                socsvstring = string.Empty;
                                socsvstring += "ADC" + ",";
                                foreach (var attributeName in attributeNameGroup)
                                {
                                    foreach (var item in mCardLines.Where(x => x.AssetId == asset.Key && x.AttributeName != null && x.AttributeName.Trim().ToUpper() == attributeName.Key.Trim().ToUpper()))
                                    {
                                        socsvstring += $"ADC {item.SequenceNumber} ({item.ADCNumber})" + ",";
                                    }
                                    if (mCardLines.Where(x => x.AssetId == asset.Key && x.AttributeName != null && x.AttributeName.Trim().ToUpper() == attributeName.Key.Trim().ToUpper()).Count() <= 0)
                                        socsvstring += "" + ",";

                                }
                                csv.AppendLine(socsvstring);


                                //Pin
                                socsvstring = string.Empty;
                                socsvstring += "Pin" + ",";
                                foreach (var attributeName in attributeNameGroup)
                                {
                                    foreach (var item in mCardLines.Where(x => x.AssetId == asset.Key && x.AttributeName != null && x.AttributeName.Trim().ToUpper() == attributeName.Key.Trim().ToUpper()))
                                    {
                                        socsvstring += item.Pin + ",";
                                    }
                                    if (mCardLines.Where(x => x.AssetId == asset.Key && x.AttributeName != null && x.AttributeName.Trim().ToUpper() == attributeName.Key.Trim().ToUpper()).Count() <= 0)
                                        socsvstring += "" + ",";

                                }
                                csv.AppendLine(socsvstring);

                                //Role
                                socsvstring = string.Empty;
                                socsvstring += "Role" + ",";
                                foreach (var attributeName in attributeNameGroup)
                                {
                                    foreach (var item in mCardLines.Where(x => x.AssetId == asset.Key && x.AttributeName != null && x.AttributeName.Trim().ToUpper() == attributeName.Key.Trim().ToUpper()))
                                    {
                                        socsvstring += item.Role + ",";
                                    }
                                    if (mCardLines.Where(x => x.AssetId == asset.Key && x.AttributeName != null && x.AttributeName.Trim().ToUpper() == attributeName.Key.Trim().ToUpper()).Count() <= 0)
                                        socsvstring += "" + ",";

                                }
                                csv.AppendLine(socsvstring);

                                csv.AppendLine();
                            }

                            Response.Clear();
                            Response.Buffer = true;
                            Response.AddHeader("content-disposition", "attachment;filename=YardConfigByAssetType" + DateTime.Now.Ticks + ".csv");
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
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            return View("YardConfig");

        }

        public ActionResult DownloadCertificateGenerator(int siteId, int? clusterId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    string url = string.Empty;
                    if (clusterId != null && clusterId.HasValue)
                        url = $"CertificateGenerator/SiteId/{siteId}/ClusterId/{clusterId}";
                    else
                        url = $"CertificateGenerator/SiteId/{siteId}";

                    var response = hcf.client.GetAsync(String.Format(url)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var stringInBase64s = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonString);
                        if (stringInBase64s != null && stringInBase64s.Count > 0)
                        {
                            string fileName = string.Empty;
                            if (clusterId != null && clusterId.Value > 0)
                            {
                                var mCluster = GetCluster(clusterId.Value);
                                if (mCluster != null && mCluster.Id > 0)
                                    fileName = mCluster.Name;
                            }
                            else
                            {
                                var mSite = siteService.Get(siteId);
                                if (mSite != null && mSite.Id > 0)
                                    fileName = mSite.Name;

                            }
                            using (MemoryStream ms = new MemoryStream())
                            {
                                using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
                                {
                                    foreach (var file in stringInBase64s)
                                    {
                                        byte[] certBytes = System.Convert.FromBase64String(file.Value);

                                        var entry = archive.CreateEntry($"{file.Key}", CompressionLevel.Fastest);
                                        using (var zipStream = entry.Open())
                                        {
                                            zipStream.Write(certBytes, 0, certBytes.Length);
                                        }
                                    }
                                }
                                return File(ms.ToArray(), "application/zip", $"{fileName}.zip");
                            }
                        }
                        //if (stringInBase64.IsNotNullOrEmpty())
                        //{
                        //    var mSite = siteService.Get(siteId);
                        //    if (mSite != null && mSite.Id > 0)
                        //    {
                        //        byte[] certBytes = System.Convert.FromBase64String(stringInBase64);
                        //        if (certBytes != null && certBytes.Length > 0)
                        //        {
                        //            Response.Clear();
                        //            // MIME type for certificate files
                        //            Response.ContentType = "application/x-x509-ca-cert";
                        //            Response.AddHeader("Content-Disposition", $"attachment;filename={mSite.Name}Certificate_{DateTime.Now.Ticks}.crt");
                        //            Response.BinaryWrite(certBytes);
                        //            Response.End();
                        //        }
                        //    }

                        //}
                        //else
                        //{
                        //    ViewBag.Type = "Error";
                        //    ViewBag.Message = "File not found!";
                        //}

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
                ViewBag.Message = ex.Message;
            }
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");

            return View("YardConfig");

        }

        public ActionResult DownloadCertificateGeneratorWithOutSSL(int siteId, int clusterId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    string url = string.Empty;

                    url = $"CertificateGenerator/GenerateWithOutSSL/SiteId/{siteId}/ClusterId/{clusterId}";
                    var response = hcf.client.GetAsync(String.Format(url)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var stringInBase64s = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonString);
                        if (stringInBase64s != null && stringInBase64s.Count > 0)
                        {
                            string fileName = string.Empty;
                            if (clusterId > 0)
                            {
                                var mCluster = GetCluster(clusterId);
                                if (mCluster != null && mCluster.Id > 0)
                                    fileName = mCluster.Name;
                            }
                            else
                            {
                                var mSite = siteService.Get(siteId);
                                if (mSite != null && mSite.Id > 0)
                                    fileName = mSite.Name;

                            }

                            using (MemoryStream ms = new MemoryStream())
                            {
                                using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
                                {
                                    foreach (var file in stringInBase64s)
                                    {
                                        byte[] certBytes = System.Convert.FromBase64String(file.Value);

                                        var entry = archive.CreateEntry($"{file.Key}", CompressionLevel.Fastest);
                                        using (var zipStream = entry.Open())
                                        {
                                            zipStream.Write(certBytes, 0, certBytes.Length);
                                        }
                                    }
                                }
                                return File(ms.ToArray(), "application/zip", $"{fileName}.zip");
                            }
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
                ViewBag.Message = ex.Message;
            }
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");

            return View("YardConfig");

        }


        public ActionResult DownloadWireguardServer(int siteId,int clusterId)
        {
            List<CardLine> mCardLines = new List<CardLine>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CertificateGenerator/GenerateWireguardServer/SiteId/{siteId}/ClusterId/{clusterId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var strMaterialPositioning = JsonConvert.DeserializeObject<string>(jsonString);
                        if (strMaterialPositioning.IsNotNullOrEmpty())
                        {
                            byte[] fileBytes = Convert.FromBase64String(strMaterialPositioning);
                            string contentType = "text/plain";

                            // Define the name of the file the user will download
                            string downloadFileName = $"Wireguard_s.txt";

                            // Return the file for download
                            return File(fileBytes, contentType, downloadFileName);

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
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            return View("YardConfig");

        }

        [HttpPost]
        public JsonResult UpdateCardLine(Domain.CardLine mCardLine)
        {

            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mCardLine);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format($"CardLine/Id/{mCardLine.Id}"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        data = new { type = "success", result = "Multiplication is updated." };
                    }
                    else
                    {
                        data = new { type = "error", result = "Error occured while updating multiplication!" };
                    }

                }
            }
            catch (Exception ex)
            {
                data = new { type = "error", result = "Error occured while updating multiplication!" };
            }
            return Json(data, JsonRequestBehavior.AllowGet);

        }

        [HttpPost]
        public ActionResult UploadCertificateHTTP(int clusterId)
        {
            string fileName = string.Empty;
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
                        AutCalINI mAutCalINI = new AutCalINI() { FileBase64 = thePictureDataAsString, ClusterId = clusterId };

                        var jsonStr = JsonConvert.SerializeObject(mAutCalINI);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("CertificateGenerator/UploadCertificateHTTP"), str).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.NoContent)
                        {
                            //fileName = JsonConvert.DeserializeObject<string>(jsonString);
                            data = new { type = "success", result = "File has been uploaded." };
                        }
                        else
                        {
                            data = new { type = "error", result = "Error occured while upload file!" };
                        }
                    }
                }
                catch (Exception)
                {
                    data = new { type = "error", result = "Error occured while upload file!" };
                }
            }

            return Json(data, JsonRequestBehavior.AllowGet);
        }

        #region CardLine Circuit 

        public ActionResult EditCardLineCircuit(int id)
        {
            var mCardLine = new Domain.CardLine();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("CardLine/Id/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mCardLine = JsonConvert.DeserializeObject<CardLine>(jsonString);
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

            return PartialView(mCardLine);
        }

        [HttpPost]
        public ActionResult UpdateCardLineCircuit(Domain.CardLine mCardLine)
        {

            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mCardLine);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format($"CardLine/UpdateLocation/Id/{mCardLine.Id}"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        data = new { type = "success", result = "Circuit is updated." };
                    }
                    else
                    {
                        data = new { type = "error", result = "Error occured while updating circuit!" };
                    }

                }
            }
            catch (Exception ex)
            {
                data = new { type = "error", result = "Error occured while updating circuit!" };
            }
            return Json(data, JsonRequestBehavior.AllowGet);

        }

        public ActionResult DownloadCardLineCircuit(int id)
        {
            var mCardLine = new Domain.CardLine();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Cluster/DownloadCircuitYCToFTUPDF/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                        if (csvbytes != null && csvbytes.Length > 0)
                        {
                            return File(csvbytes, "application/pdf", $"Circuit.pdf");
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

        public ActionResult DownloadCircuitYCToFTUPDF(int id)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Cluster/DownloadCircuitYCToFTUPDF/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                        if (csvbytes != null && csvbytes.Length > 0)
                        {
                            return File(csvbytes, "application/pdf", $"Circuit.pdf");
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

        public ActionResult DownloadCircuitYCToLocationPDF(int id)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Cluster/DownloadCircuitYCToLocationPDF/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                        if (csvbytes != null && csvbytes.Length > 0)
                        {
                            return File(csvbytes, "application/pdf", $"Circuit.pdf");
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

        #endregion

        #region Create Cluster

        public ActionResult Create()
        {
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            return View();
        }

        public ActionResult _ClusterList(int siteId)
        {
            List<Cluster> mClusters = new List<Cluster>();
            if (siteId > 0)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = hcf.client.GetAsync(String.Format("Cluster/SiteId/{0}", siteId)).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            mClusters = JsonConvert.DeserializeObject<List<Cluster>>(jsonString);
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
            }


            return PartialView(mClusters);
        }

        public ActionResult _CreateCluster(int siteId)
        {
            ViewBag.CurrentCardNumbers = new List<string>() { "C-1", "C-2", "C-3", "C-4", "C-5", "C-6", "C-7", "C-8", "C-9", "C-10" };
            ViewBag.MintCardNumbers = new List<string>() { "MintCard-1", "MintCard-2", "MintCard-3", "MintCard-4", "MintCard-5", "MintCard-6", "MintCard-7", "MintCard-8", "MintCard-9", "MintCard-10" };
            ViewBag.V5Numbers = new List<string>() { "V5-1", "V5-2", "V5-3", "V5-4", "V5-5" };
            ViewBag.V82Numbers = new List<string>() { "V82-1", "V82-2", "V82-3", "V82-4", "V82-5", "V82-6", "V82-7", "V82-8", "V82-9", "V82-10" };
            ViewBag.DINumbers = new List<string>() { "DI-1", "DI-2", "DI-3", "DI-4", "DI-5", "DI-6", "V82-7", "DI-8", "DI-9", "DI-10" };
            BindPowerSupplyType();
            BindModemVersion();
            return PartialView(new Domain.Cluster() { SiteId = siteId });
        }

        public ActionResult _EditCluster(int clusterId)
        {
            Cluster mCluster = new Cluster();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Cluster/{0}", clusterId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mCluster = JsonConvert.DeserializeObject<Cluster>(jsonString);
                        if (mCluster != null && mCluster.Id > 0)
                        {
                            var cluterNumber = Regex.Match(mCluster.Name.Split('/').FirstOrDefault(), @"\d+").Value;
                            mCluster.Name = mCluster.Name.Split('/').LastOrDefault();
                            mCluster.RFClusterId = Convert.ToInt32(cluterNumber);
                            var mCards = GetCard(mCluster.Id);
                            if (mCards != null && mCards.Count > 0)
                            {
                                foreach (var mCard in mCards)
                                {
                                    int number = 0;
                                    int.TryParse(mCard.CardName.Split('-').LastOrDefault(), out number);
                                    if (mCard.CardName.Split('-').FirstOrDefault() == "C")
                                    {
                                        if (mCluster.CurrentCardNumbers == null)
                                            mCluster.CurrentCardNumbers = new List<int>();

                                        mCluster.CurrentCardNumbers.Add(number);
                                    }
                                    if (mCard.CardName.Split('-').FirstOrDefault() == "MintCard")
                                    {
                                        if (mCluster.MintCardNumbers == null)
                                            mCluster.MintCardNumbers = new List<int>();

                                        mCluster.MintCardNumbers.Add(number);
                                    }
                                    if (mCard.CardName.Split('-').FirstOrDefault() == "V5")
                                    {
                                        if (mCluster.V5Numbers == null)
                                            mCluster.V5Numbers = new List<int>();

                                        mCluster.V5Numbers.Add(number);
                                    }
                                    if (mCard.CardName.Split('-').FirstOrDefault() == "V82")
                                    {
                                        if (mCluster.V82Numbers == null)
                                            mCluster.V82Numbers = new List<int>();

                                        mCluster.V82Numbers.Add(number);
                                    }
                                    if (mCard.CardName.Split('-').FirstOrDefault() == "DI")
                                    {
                                        if (mCluster.DINumbers == null)
                                            mCluster.DINumbers = new List<int>();

                                        mCluster.DINumbers.Add(number);
                                    }
                                }
                            }
                            var mADCs = GetADC(mCluster.Id);
                            if (mADCs != null && mADCs.Count > 0)
                                mCluster.ADCs = mADCs;
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

            ViewBag.CurrentCardNumbers = new List<string>() { "C-1", "C-2", "C-3", "C-4", "C-5", "C-6", "C-7", "C-8", "C-9", "C-10" };
            ViewBag.MintCardNumbers = new List<string>() { "MintCard-1", "MintCard-2", "MintCard-3", "MintCard-4", "MintCard-5", "MintCard-6", "MintCard-7", "MintCard-8", "MintCard-9", "MintCard-10" };
            ViewBag.V5Numbers = new List<string>() { "V5-1", "V5-2", "V5-3", "V5-4", "V5-5" };
            ViewBag.V82Numbers = new List<string>() { "V82-1", "V82-2", "V82-3", "V82-4", "V82-5", "V82-6", "V82-7", "V82-8", "V82-9", "V82-10" };
            ViewBag.DINumbers = new List<string>() { "DI-1", "DI-2", "DI-3", "DI-4", "DI-5", "DI-6", "V82-7", "DI-8", "DI-9", "DI-10" };
            BindPowerSupplyType();
            BindModemVersion();

            return PartialView("_CreateCluster", mCluster);
        }

        public ActionResult CreateCluster(Domain.Cluster mCluster)
        {
            DeleteCard(mCluster);
            mCluster.CreatedBy = ClsHttpContent.LoginUser.Id;
            var jsonStr = JsonConvert.SerializeObject(mCluster);
            StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
            using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
            {
                var response = hcf.client.PostAsync(String.Format("Cluster"), str).Result;
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    mCluster = JsonConvert.DeserializeObject<Cluster>(jsonString);

                }
            }


            return Json(mCluster);
        }

        public ActionResult CreateADC(List<Domain.ADC> mADCs)
        {
            dynamic data = new ExpandoObject();
            foreach (var mADC in mADCs)
            {
                mADC.CreatedBy = ClsHttpContent.LoginUser.Id;
                var jsonStr = JsonConvert.SerializeObject(mADC);
                StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.PostAsync(String.Format("ADC"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        var adc = JsonConvert.DeserializeObject<ADC>(jsonString);

                    }
                }

            }

            return Json(data);
        }

        public ActionResult _EditCard(int clusterId)
        {
            var mCluster = GetCluster(clusterId);
            if (mCluster != null && mCluster.Id > 0)
            {
                ViewBag.ClusterName = mCluster.Name;
            }

            var mADCs = GetADC(clusterId);
            if (mADCs != null && mADCs.Count > 0)
                ViewBag.ADCs = mADCs;

            return PartialView(GetCard(clusterId));
        }

        public ActionResult _EditCardLine(int clusterId, int cardId, int siteId, string cardName = null)
        {
            ViewBag.cardId = cardId;
            if (cardName.IsNotNullOrEmpty())
                ViewBag.cardName = cardName;

            var mCardLines = GetCardLine(cardId);
            var mAssetTypes = assetTypeService.GetBy(siteId);
            var mAssetAttributes = assetAttributeService.GetAll();
            var mAssets = assetService.GetAssestBy(siteId);
            var mADCs = GetADC(clusterId);

            if (mCardLines != null && mCardLines.Count > 0)
            {
                foreach (var mCardLine in mCardLines)
                {
                    if (mAssetTypes != null && mAssetTypes.Count > 0)
                        mCardLine.AssetTypes.AddRange(mAssetTypes);

                    if (mADCs != null && mADCs.Count > 0)
                        mCardLine.mADCs.AddRange(mADCs);

                    if (mCardLine.AssetTypeId != null && mCardLine.AssetTypeId.Value > 0)
                    {
                        var mAssetAttribute = mAssetAttributes.Where(x => x.AssetTypeId == mCardLine.AssetTypeId).ToList();
                        if (mAssetAttribute != null && mAssetAttribute.Count > 0)
                            mCardLine.AssetAttributes.AddRange(mAssetAttribute);

                        var mAsset = mAssets.Where(x => x.AssetTypeId == mCardLine.AssetTypeId.Value).ToList();
                        if (mAsset != null && mAsset.Count > 0)
                            mCardLine.Assets.AddRange(mAsset);
                    }
                }
            }

            return PartialView(mCardLines);
        }

        public ActionResult CreateCardLine(List<CardLine> mCardLines)
        {
            dynamic data = new ExpandoObject();
            if (mCardLines != null && mCardLines.Count > 0)
            {
                foreach (var mCardLine in mCardLines)
                {
                    mCardLine.CreatedBy = ClsHttpContent.LoginUser.Id;
                    var jsonStr = JsonConvert.SerializeObject(mCardLine);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = hcf.client.PostAsync("CardLine", str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                        }
                        else
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            var message = JsonConvert.DeserializeObject<string>(jsonString);
                            data = new { type = "error", result = message };
                            break;
                        }
                    }

                }
            }


            return Json(data);

        }

        public JsonResult GetAllAssest(int siteId, int assetTypeId)
        {
            List<Asset> mAssets = new List<Asset>();
            mAssets = assetService.GetAssestBy(siteId, assetTypeId);
            return Json(mAssets, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetAssetAttribute(int assetTypeId)
        {
            var mAssetAttributes = assetAttributeService.GetAssetAttributesBy(assetTypeId);
            return Json(mAssetAttributes, JsonRequestBehavior.AllowGet);
        }

        public ActionResult Delete(int id)
        {
            dynamic data = new ExpandoObject();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.DeleteAsync(String.Format($"Cluster/{id}/UserId/{ClsHttpContent.LoginUser.Id}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Cluster has been deleted." };
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

        public ActionResult DeleteADC(int id)
        {
            dynamic data = new ExpandoObject();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.DeleteAsync(String.Format($"ADC/{id}/UserId/{ClsHttpContent.LoginUser.Id}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "ADC has been deleted." };
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

        private List<Card> GetCard(int clusterId)
        {
            var mCards = new List<Card>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Card/ClusterId/{clusterId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mCards = JsonConvert.DeserializeObject<List<Card>>(jsonString);
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

            return mCards;
        }

        private List<CardLine> GetCardLine(int cardId)
        {
            var mCardLines = new List<CardLine>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CardLine/CardId/{cardId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mCardLines = JsonConvert.DeserializeObject<List<CardLine>>(jsonString);
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

            return mCardLines;
        }

        private List<ADC> GetADC(int clusterId)
        {
            var mADCs = new List<ADC>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"ADC/ClusterId/{clusterId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mADCs = JsonConvert.DeserializeObject<List<ADC>>(jsonString);
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

            return mADCs;
        }

        private Cluster GetCluster(int clusterId)
        {
            Cluster mCluster = new Cluster();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Cluster/{0}", clusterId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mCluster = JsonConvert.DeserializeObject<Cluster>(jsonString);
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

            return mCluster;
        }

        private void DeleteCard(Domain.Cluster mCluster)
        {
            try
            {
                if (mCluster != null && mCluster.Id > 0)
                {
                    var mCards = GetCard(mCluster.Id);
                    if (mCards != null && mCards.Count > 0)
                    {
                        foreach (var mCard in mCards)
                        {
                            var splitCardName = mCard.CardName.Split('-');
                            if (splitCardName != null && splitCardName.Length > 1)
                            {
                                if (splitCardName.FirstOrDefault() == "C")
                                {
                                    if (!mCluster.CurrentCardNumbers.Contains(Convert.ToInt32(splitCardName.LastOrDefault())))
                                    {
                                        DeleteCard(mCard.Id);
                                    }
                                }
                                if (splitCardName.FirstOrDefault() == "MintCard")
                                {
                                    if (!mCluster.MintCardNumbers.Contains(Convert.ToInt32(splitCardName.LastOrDefault())))
                                    {
                                        DeleteCard(mCard.Id);
                                    }
                                }
                                if (splitCardName.FirstOrDefault() == "V5")
                                {
                                    if (!mCluster.V5Numbers.Contains(Convert.ToInt32(splitCardName.LastOrDefault())))
                                    {
                                        DeleteCard(mCard.Id);
                                    }
                                }
                                if (splitCardName.FirstOrDefault() == "V82")
                                {
                                    if (!mCluster.V82Numbers.Contains(Convert.ToInt32(splitCardName.LastOrDefault())))
                                    {
                                        DeleteCard(mCard.Id);
                                    }
                                }
                                if (splitCardName.FirstOrDefault() == "DI")
                                {
                                    if (!mCluster.DINumbers.Contains(Convert.ToInt32(splitCardName.LastOrDefault())))
                                    {
                                        DeleteCard(mCard.Id);
                                    }
                                }
                            }

                        }
                    }
                }
            }
            catch (Exception)
            {
            }

        }

        private void DeleteCard(int cardId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.DeleteAsync(String.Format($"Card/{cardId}/UserId/{ClsHttpContent.LoginUser.Id}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                    }
                    else
                    {
                    }
                }
            }
            catch (Exception ex)
            {
            }
        }

        private void BindPowerSupplyType()
        {

            var enumlist = (from E7FRSAdvance.Utility.Utility.PowerSupplyType e in Enum.GetValues(typeof(E7FRSAdvance.Utility.Utility.PowerSupplyType))
                            select new Domain.AssetType
                            {
                                Id = (int)e,
                                Name = EnumHelper.GetDisplayName(e)
                            }).ToList();

            ViewBag.PowerSupplyTypeList = new SelectList(enumlist, "Id", "Name");

            // return new SelectList(enumData, "Id", "Name");
        }

        private void BindModemVersion()
        {

            var enumlist = (from E7FRSAdvance.Utility.Utility.ModemVersion e in Enum.GetValues(typeof(E7FRSAdvance.Utility.Utility.ModemVersion))
                            select new 
                            {
                                Id = (int)e,
                                Name = e
                            }).ToList();

            ViewBag.ModemVersionList = new SelectList(enumlist, "Id", "Name");

            // return new SelectList(enumData, "Id", "Name");
        }
        #endregion



    }
}