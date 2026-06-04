using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Utility;
using Microsoft.VisualBasic.FileIO;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class AIVisualizationController : Controller
    {
        // GET: AIVisualization
        private readonly ISiteService siteService;
        public AIVisualizationController(ISiteService siteService)
        {
            this.siteService = siteService;
        }

        public ActionResult Index()
        {
            //try
            //{
            //    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
            //    {
            //        var response = hcf.client.GetAsync(String.Format("AISection/RemoveCache")).Result;
            //        string jsonString = response.Content.ReadAsStringAsync().Result;

            //    }
            //}
            //catch (Exception ex)
            //{
            //}

            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            ViewBag.AssetTypes = new SelectList(GetAssetType(), "Id", "Name");
            return View();
        }


        #region Track

        //public ActionResult UploadTrackCSV(string cluster)
        //{
        //    var mTrackDataLister = new TrackDataLister();
        //    for (int i = 0; i < Request.Files.Count; i++)
        //    {
        //        var file = Request.Files[i];

        //        DataTable csvDataTable = new DataTable();
        //        var csvBody = string.Empty;

        //        using (BinaryReader b = new BinaryReader(file.InputStream))
        //        {
        //            byte[] binData = b.ReadBytes(file.ContentLength);
        //            csvBody = Encoding.UTF8.GetString(binData);
        //        }

        //        var memoryStream = new MemoryStream();
        //        var streamWriter = new StreamWriter(memoryStream);
        //        streamWriter.Write(csvBody);
        //        streamWriter.Flush();
        //        memoryStream.Position = 0;
        //        using (TextFieldParser csvReader = new TextFieldParser(memoryStream))
        //        {
        //            csvReader.SetDelimiters(new string[] { "," });
        //            csvReader.HasFieldsEnclosedInQuotes = true;
        //            string[] colFields = csvReader.ReadFields();

        //            foreach (string column in colFields)
        //            {
        //                DataColumn datecolumn = new DataColumn(column);
        //                datecolumn.AllowDBNull = true;
        //                csvDataTable.Columns.Add(datecolumn);
        //                csvDataTable.PrimaryKey = new DataColumn[] { csvDataTable.Columns["[roles]"] };
        //            }
        //            string section = string.Empty;
        //            while (!csvReader.EndOfData)
        //            {

        //                string[] fields = csvReader.ReadFields();

        //                mTrackDataLister.TrackDatas.Add(new TrackData() { Date = Convert.ToDateTime(fields[0]), AssetId = Convert.ToInt32(fields[1]), AssetName = Convert.ToString(fields[2]), IfmA = Convert.ToDouble(fields[3]), IrmA = Convert.ToDouble(fields[4]), Vr = Convert.ToDouble(fields[5]), ChokeV = Convert.ToDouble(fields[6]), ChargermA = Convert.ToDouble(fields[7]), TPRV = Convert.ToDouble(fields[8]), AI_cluster = fields[9] });
        //            }
        //        }
        //    }

        //    if (mTrackDataLister.TrackDatas != null && mTrackDataLister.TrackDatas.Count > 0)
        //    {
        //        var mTempTrackDataLister = new TrackDataLister();
        //        mTempTrackDataLister.TrackDatas.AddRange(mTrackDataLister.TrackDatas);
        //        TempData["tempAIVisualizationTrack"] = mTempTrackDataLister;
        //        TempData.Keep();


        //        if (cluster.IsNotNullOrEmpty())
        //            mTrackDataLister.TrackDatas = mTrackDataLister.TrackDatas.Where(x => x.AI_cluster.Trim().ToLower() == cluster.Trim().ToLower()).ToList();

        //    }


        //    return PartialView("_TrackDashboard", mTrackDataLister);

        //}

        //[HttpPost]
        //public ActionResult FilterTrack(SearchCriteria mSearchCriteria)
        //{
        //    var mTrackDataLister = new TrackDataLister();
        //    try
        //    {
        //        var mAssetListerTemp = TempData.Peek("tempAIVisualizationTrack");
        //        if (mAssetListerTemp != null)
        //        {
        //            var mTempAssetLister = mAssetListerTemp as TrackDataLister;
        //            var jsonStr = JsonConvert.SerializeObject(mTempAssetLister);
        //            mTrackDataLister = JsonConvert.DeserializeObject<TrackDataLister>(jsonStr);
        //            if (mTrackDataLister != null && mTrackDataLister.TrackDatas != null)
        //            {
        //                if (mSearchCriteria.AssetName.IsNotNullOrEmpty())
        //                {
        //                    mTrackDataLister.TrackDatas = mTrackDataLister.TrackDatas.Where(x => x.AssetName.Trim().ToLower() == mSearchCriteria.AssetName.Trim().ToLower()).ToList();
        //                }

        //                if (mSearchCriteria.AI_Cluster.IsNotNullOrEmpty())
        //                {
        //                    mTrackDataLister.TrackDatas = mTrackDataLister.TrackDatas.Where(x => x.AI_cluster.Trim().ToLower() == mSearchCriteria.AI_Cluster.Trim().ToLower()).ToList();
        //                }

        //            }
        //        }
        //    }
        //    catch (Exception)
        //    {
        //        throw;
        //    }

        //    return PartialView("_TrackDashboard", mTrackDataLister);
        //}

        public ActionResult _TrackDashboard(TrackDataLister mTrackDataLister)
        {
            try
            {
                mTrackDataLister.Pager.Take = mTrackDataLister.Pager.PageSize;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mTrackDataLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("AISection/GetTrackData"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mTrackDataLister = JsonConvert.DeserializeObject<TrackDataLister>(jsonString);
                        if (mTrackDataLister != null && mTrackDataLister.TrackDatas != null && mTrackDataLister.TrackDatas.Count > 0)
                        {

                        }
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
            return PartialView("_TrackDashboard", mTrackDataLister);
        }

        public ActionResult DownloadTrack()
        {
            try
            {
                var mTrackDataLister = new TrackDataLister();
                mTrackDataLister.Pager.Take = -1;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("AISection/DownloadTrackData")).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        var path = JsonConvert.DeserializeObject<string>(jsonString);
                        if (path.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = null;

                            using (WebClient client = new WebClient())
                            {
                                csvbytes = client.DownloadData(path);
                            }
                            return File(csvbytes, "text/csv", $"{Path.GetFileName(path)}.csv");
                        }
                       
                    }

                }
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Something went wrong!";
            }
            return RedirectToAction("Index");
        }

        #endregion

        #region Signal

        public ActionResult UploadSignalCSV(SearchCriteria searchCriteria)
        {
            var mSignalDataLister = new SignalDataLister();
            for (int i = 0; i < Request.Files.Count; i++)
            {
                var file = Request.Files[i];
                DataTable csvDataTable = new DataTable();
                var csvBody = string.Empty;

                using (BinaryReader b = new BinaryReader(file.InputStream))
                {
                    byte[] binData = b.ReadBytes(file.ContentLength);
                    csvBody = Encoding.UTF8.GetString(binData);
                }

                Utility.CSVFileReader csvFileReader = new Utility.CSVFileReader();
                var dataTable = csvFileReader.ReadWithSpecialChar(csvBody, ",");
                if (dataTable != null)
                {
                    foreach (var dataRow in dataTable.Select())
                    {
                        var mSignalData = new SignalData();
                        mSignalData.Date = GetValue(dataRow, "Date").ToTrim();
                        mSignalData.AssetName = GetValue(dataRow, "AssetName").ToTrim();
                        mSignalData.RGV = GetValue(dataRow, "RG V").ToTrim();
                        mSignalData.RGmA = GetValue(dataRow, "RG mA").ToTrim();
                        mSignalData.DGV = GetValue(dataRow, "DG V").ToTrim();
                        mSignalData.DGmA = GetValue(dataRow, "DG mA").ToTrim();
                        mSignalData.HGV = GetValue(dataRow, "HG V").ToTrim();
                        mSignalData.HGmA = GetValue(dataRow, "HG mA").ToTrim();
                        mSignalData.RGP = GetValue(dataRow, "RG P").ToTrim();
                        mSignalData.DGP = GetValue(dataRow, "DG P").ToTrim();
                        mSignalData.HGP = GetValue(dataRow, "HG P").ToTrim();
                        mSignalData.AI_cluster_RG = GetValue(dataRow, "AI_cluster_RG").ToTrim();
                        mSignalData.AI_cluster_DG = GetValue(dataRow, "AI_cluster_DG").ToTrim();
                        mSignalData.AI_cluster_HG = GetValue(dataRow, "AI_cluster_HG").ToTrim();
                        mSignalData.AI_status_RG = GetValue(dataRow, "AI_status_RG").ToTrim();
                        mSignalData.AI_status_DG = GetValue(dataRow, "AI_status_DG").ToTrim();
                        mSignalData.AI_status_HG = GetValue(dataRow, "AI_status_HG").ToTrim();

                        mSignalDataLister.SignalDatas.Add(mSignalData);
                    }
                }
                //var memoryStream = new MemoryStream();
                //var streamWriter = new StreamWriter(memoryStream);
                //streamWriter.Write(csvBody);
                //streamWriter.Flush();
                //memoryStream.Position = 0;
                //using (TextFieldParser csvReader = new TextFieldParser(memoryStream))
                //{


                //    csvReader.SetDelimiters(new string[] { "," });
                //    csvReader.HasFieldsEnclosedInQuotes = true;
                //    string[] colFields = csvReader.ReadFields();
                //    foreach (string column in colFields)
                //    {
                //        DataColumn datecolumn = new DataColumn(column);
                //        datecolumn.AllowDBNull = true;
                //        csvDataTable.Columns.Add(datecolumn);
                //    }
                //    while (!csvReader.EndOfData)
                //    {
                //        string[] fields = csvReader.ReadFields();
                //        //Making empty value as null

                //        mSignalDataLister.SignalDatas.Add(new SignalData() { Date = Convert.ToDateTime(fields[0]), AssetName = Convert.ToString(fields[1]), RGV = Convert.ToDouble(fields[3]), IrmA = Convert.ToDouble(fields[4]), Vr = Convert.ToDouble(fields[5]), ChokeV = Convert.ToDouble(fields[6]), ChargermA = Convert.ToDouble(fields[7]), TPRV = Convert.ToDouble(fields[8]), AI_cluster = fields[9] });
                //    }
                //}
            }

            if (mSignalDataLister.SignalDatas != null && mSignalDataLister.SignalDatas.Count > 0)
            {
                var mTempSignalDataLister = new SignalDataLister();
                mTempSignalDataLister.SignalDatas.AddRange(mSignalDataLister.SignalDatas);
                TempData["tempAIVisualizationSignal"] = mTempSignalDataLister;
                TempData.Keep();


                if (searchCriteria.ClusterRG.IsNotNullOrEmpty())
                    mSignalDataLister.SignalDatas = mSignalDataLister.SignalDatas.Where(x => x.AI_cluster_RG.Trim().ToLower() == searchCriteria.ClusterRG.Trim().ToLower()).ToList();


                if (searchCriteria.ClusterDG.IsNotNullOrEmpty())
                    mSignalDataLister.SignalDatas = mSignalDataLister.SignalDatas.Where(x => x.AI_cluster_DG.Trim().ToLower() == searchCriteria.ClusterDG.Trim().ToLower()).ToList();

                if (searchCriteria.ClusterHG.IsNotNullOrEmpty())
                    mSignalDataLister.SignalDatas = mSignalDataLister.SignalDatas.Where(x => x.AI_cluster_HG.Trim().ToLower() == searchCriteria.ClusterHG.Trim().ToLower()).ToList();
            }


            return PartialView("_SignalDashboard", mSignalDataLister);
        }


        #endregion

        #region Point Machine

        public ActionResult UploadPointMachineCSV()
        {
            var mAssetLister = new AssetLister();
            var mPointMachineDatas = new List<PointMachineData>();

            try
            {
                for (int i = 0; i < Request.Files.Count; i++)
                {
                    var file = Request.Files[i];
                    DataTable csvDataTable = new DataTable();
                    var csvBody = string.Empty;

                    using (BinaryReader b = new BinaryReader(file.InputStream))
                    {
                        byte[] binData = b.ReadBytes(file.ContentLength);
                        csvBody = Encoding.UTF8.GetString(binData);
                    }

                    Utility.CSVFileReader csvFileReader = new Utility.CSVFileReader();
                    var dataTable = csvFileReader.ReadWithSpecialChar(csvBody, ",");
                    if (dataTable != null)
                    {
                        foreach (var dataRow in dataTable.Select())
                        {
                            try
                            {
                                var mPointMachineData = new PointMachineData();
                                mPointMachineData.TimeStamp = Convert.ToDateTime(GetValue(dataRow, "TimeStamp").ToTrim());
                                mPointMachineData.AssetId = Convert.ToInt32(GetValue(dataRow, "AssetId").ToTrim());
                                mPointMachineData.Direction = GetValue(dataRow, "Direction").ToTrim();
                                mPointMachineData.ArrayData = GetValue(dataRow, "ArrayData").ToTrim();
                                mPointMachineData.TsTime = GetValue(dataRow, "TsTime").ToTrim();
                                mPointMachineData.Date = GetValue(dataRow, "Date").ToTrim();
                                mPointMachineData.ClusterA = GetValue(dataRow, "clusterA").ToTrim();
                                mPointMachineData.AI_status_A = GetValue(dataRow, "AI_statusA").ToTrim();
                                mPointMachineData.ClusterB = GetValue(dataRow, "clusterB").ToTrim();
                                mPointMachineData.AI_status_B = GetValue(dataRow, "AI_statusB").ToTrim();

                                mPointMachineDatas.Add(mPointMachineData);
                            }
                            catch (Exception)
                            {

                            }

                        }
                    }


                    //var file = Request.Files[i];

                    //DataTable csvDataTable = new DataTable();
                    //var csvBody = string.Empty;

                    //using (BinaryReader b = new BinaryReader(file.InputStream))
                    //{
                    //    byte[] binData = b.ReadBytes(file.ContentLength);
                    //    csvBody = Encoding.UTF8.GetString(binData);
                    //}

                    //var memoryStream = new MemoryStream();
                    //var streamWriter = new StreamWriter(memoryStream);
                    //streamWriter.Write(csvBody);
                    //streamWriter.Flush();
                    //memoryStream.Position = 0;
                    //using (TextFieldParser csvReader = new TextFieldParser(memoryStream))
                    //{
                    //    csvReader.SetDelimiters(new string[] { "," });
                    //    csvReader.HasFieldsEnclosedInQuotes = true;
                    //    string[] colFields = csvReader.ReadFields();

                    //    foreach (string column in colFields)
                    //    {
                    //        DataColumn datecolumn = new DataColumn(column);
                    //        datecolumn.AllowDBNull = true;
                    //        csvDataTable.Columns.Add(datecolumn);
                    //        csvDataTable.PrimaryKey = new DataColumn[] { csvDataTable.Columns["[roles]"] };
                    //    }
                    //    string section = string.Empty;
                    //    while (!csvReader.EndOfData)
                    //    {

                    //        string[] fields = csvReader.ReadFields();

                    //        mPointMachineDatas.Add(new PointMachineData() { TimeStamp = Convert.ToDateTime(fields[0]), AssetId = Convert.ToInt32(fields[1]), Direction = fields[2], ArrayData = fields[3].Replace("'", "\""), TsTime = fields[4], Date = fields[5], ClusterA = fields[6], AI_status_A = fields[7], ClusterB = fields[8], AI_status_B = fields[9] });
                    //    }
                    //}
                }
                mAssetLister = GetAllSiteDetailsBySiteId(mPointMachineDatas);
                if (mAssetLister != null && mAssetLister.mAssets != null && mAssetLister.mAssets.Count > 0)
                {
                    var mTempAssetLister = new AssetLister();
                    mTempAssetLister.mAssets.AddRange(mAssetLister.mAssets);
                    TempData["tempAIVisualizationPointMachine"] = mTempAssetLister;
                    TempData.Keep();

                    mAssetLister.mAssets = mAssetLister.mAssets.Where(x => x.IsThickWave == null || x.IsThickWave == false).ToList();
                }
            }
            catch (Exception)
            {

                throw;
            }



            return PartialView("_GetPointMachineDashboard", mAssetLister);

        }

        [HttpPost]
        public ActionResult FilterPointMachine(SearchCriteria searchCriteria)
        {
            var mAssetLister = new AssetLister();
            try
            {
                var mAssetListerTemp = TempData.Peek("tempAIVisualizationPointMachine");
                if (mAssetListerTemp != null)
                {
                    var mTempAssetLister = mAssetListerTemp as AssetLister;
                    var jsonStr = JsonConvert.SerializeObject(mTempAssetLister);
                    mAssetLister = JsonConvert.DeserializeObject<AssetLister>(jsonStr);
                    if (mAssetLister != null && mAssetLister.mAssets != null)
                    {
                        if (searchCriteria.IsThickWave != null && searchCriteria.IsThickWave.Value)
                            mAssetLister.mAssets = mAssetLister.mAssets.Where(x => x.IsThickWave == searchCriteria.IsThickWave).ToList();

                        foreach (var asset in mAssetLister.mAssets)
                        {
                            if (searchCriteria.Status_A.IsNotNullOrEmpty())
                                asset.mPointMachineData = asset.mPointMachineData.Where(x => x.AI_status_A.Trim().ToLower() == searchCriteria.Status_A.Trim().ToLower()).ToList();

                            if (searchCriteria.Status_B.IsNotNullOrEmpty())
                                asset.mPointMachineData = asset.mPointMachineData.Where(x => x.AI_status_B.Trim().ToLower() == searchCriteria.Status_B.Trim().ToLower()).ToList();

                            if (searchCriteria.ClusterA.IsNotNullOrEmpty())
                                asset.mPointMachineData = asset.mPointMachineData.Where(x => x.ClusterA.Trim().ToLower() == searchCriteria.ClusterA.Trim().ToLower()).ToList();

                            if (searchCriteria.ClusterB.IsNotNullOrEmpty())
                                asset.mPointMachineData = asset.mPointMachineData.Where(x => x.ClusterB.Trim().ToLower() == searchCriteria.ClusterB.Trim().ToLower()).ToList();


                        }
                    }
                }

            }
            catch (Exception)
            {

                throw;
            }

            return PartialView("_GetPointMachineDashboard", mAssetLister);
        }

        public AssetLister GetAllSiteDetailsBySiteId(List<PointMachineData> mPointMachineDatas)
        {
            var mAssetLister = new AssetLister();

            List<DateTime> dateTimes = new List<DateTime>();
            var mAssets = GetAsset(3);
            if (mAssets != null && mAssets.Count > 0)
            {

                foreach (var item in mAssets)
                {
                    item.mPointMachineData = mPointMachineDatas.Where(x => x.AssetId == item.Id).ToList();

                    if (item.mPointMachineData != null && item.mPointMachineData.Count > 0)
                    {
                        foreach (var pointMachine in item.mPointMachineData)
                        {
                            pointMachine.AssetType = item.Name;
                            if (pointMachine != null && pointMachine.Direction.ToLower() == "Reverse".ToLower())
                            {
                                pointMachine.PointMachineJson = PrepareModelFromJson(pointMachine.ArrayData);
                            }
                            if (pointMachine != null && pointMachine.Direction.ToLower() == "Normal".ToLower())
                            {
                                pointMachine.PointMachineJson = PrepareModelFromNormalJson(pointMachine.ArrayData);
                            }
                            if (string.IsNullOrEmpty(pointMachine.PointMachineJson.A_V_MAX))
                            {
                                pointMachine.PointMachineJson.A_V_MAX = "0";
                            }
                            else
                            {
                                pointMachine.PointMachineJson.A_V_MAX = Convert.ToString(Math.Round(Convert.ToDecimal(pointMachine.PointMachineJson.A_V_MAX), 2));
                            }
                            if (string.IsNullOrEmpty(pointMachine.PointMachineJson.B_V_MAX))
                            {
                                pointMachine.PointMachineJson.B_V_MAX = "0";
                            }
                            else
                            {
                                pointMachine.PointMachineJson.B_V_MAX = Convert.ToString(Math.Round(Convert.ToDecimal(pointMachine.PointMachineJson.B_V_MAX), 2));
                            }
                            if (Double.IsNaN(pointMachine.PointMachineJson.A_V_AVERAGE))
                            {
                                pointMachine.PointMachineJson.A_V_AVERAGE = 0;
                            }
                            if (Double.IsNaN(pointMachine.PointMachineJson.B_V_AVERAGE))
                            {
                                pointMachine.PointMachineJson.B_V_AVERAGE = 0;
                            }
                        }

                        mAssetLister.mAssets.Add(item);

                    }




                }
            }
            return mAssetLister;
        }

        public List<Asset> GetAsset(int typeId)
        {
            List<Asset> mAssets = new List<Asset>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Asset/AssetTypeId/{0}", typeId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssets = JsonConvert.DeserializeObject<List<Asset>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return mAssets;
        }

        public ActionResult GetSitePointMachineGraph(SearchCriteria searchCriteria)
        {
            List<PointMachineData> mPointMachineDatas = new List<PointMachineData>();
            var mAssetLister = new AssetLister();
            try
            {
                var mAssetListerTemp = TempData.Peek("tempAIVisualizationPointMachine");
                if (mAssetListerTemp != null)
                {
                    var mTempAssetLister = mAssetListerTemp as AssetLister;
                    var jsonStr = JsonConvert.SerializeObject(mTempAssetLister);
                    mAssetLister = JsonConvert.DeserializeObject<AssetLister>(jsonStr);
                    if (mAssetLister != null && mAssetLister.mAssets != null && searchCriteria != null && searchCriteria.AssetId > 0)
                    {
                        var mAsset = mAssetLister.mAssets.Where(x => x.Id == searchCriteria.AssetId).FirstOrDefault();
                        if (mAsset != null && mAsset.Id > 0 && mAsset.mPointMachineData != null && mAsset.mPointMachineData.Count > 0)
                        {
                            mPointMachineDatas.AddRange(mAsset.mPointMachineData);

                            if (searchCriteria.Status_A.IsNotNullOrEmpty())
                                mPointMachineDatas = mPointMachineDatas.Where(x => x.AI_status_A.Trim().ToLower() == searchCriteria.Status_A.Trim().ToLower()).ToList();

                            if (searchCriteria.Status_B.IsNotNullOrEmpty())
                                mPointMachineDatas = mPointMachineDatas.Where(x => x.AI_status_B.Trim().ToLower() == searchCriteria.Status_B.Trim().ToLower()).ToList();

                            if (searchCriteria.ClusterA.IsNotNullOrEmpty())
                                mPointMachineDatas = mPointMachineDatas.Where(x => x.ClusterA.Trim().ToLower() == searchCriteria.ClusterA.Trim().ToLower()).ToList();

                            if (searchCriteria.ClusterB.IsNotNullOrEmpty())
                                mPointMachineDatas = mPointMachineDatas.Where(x => x.ClusterB.Trim().ToLower() == searchCriteria.ClusterB.Trim().ToLower()).ToList();

                            if (searchCriteria.Direction.IsNotNullOrEmpty())
                                mPointMachineDatas = mPointMachineDatas.Where(x => x.Direction.Trim().ToLower() == searchCriteria.Direction.Trim().ToLower()).ToList();

                            if (searchCriteria.MultipleClusterA != null && searchCriteria.MultipleClusterA.Count > 0)
                                mPointMachineDatas = mPointMachineDatas.Where(x => searchCriteria.MultipleClusterA.Contains(x.ClusterA)).ToList();

                            if (searchCriteria.MultipleClusterB != null && searchCriteria.MultipleClusterB.Count > 0)
                                mPointMachineDatas = mPointMachineDatas.Where(x => searchCriteria.MultipleClusterB.Contains(x.ClusterB)).ToList();
                        }
                    }
                }

            }
            catch (Exception)
            {
                throw;
            }

            var jsonResult = Json(mPointMachineDatas, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;
        }

        public ActionResult GetAllSitePointMachineGraph(SearchCriteria searchCriteria)
        {
            List<PointMachineData> mPointMachineDatas = new List<PointMachineData>();
            try
            {
                var mAssetListerTemp = TempData.Peek("tempAIVisualizationPointMachine");
                if (mAssetListerTemp != null)
                {
                    var mTempAssetLister = mAssetListerTemp as AssetLister;
                    var jsonStr = JsonConvert.SerializeObject(mTempAssetLister);
                    var mAssetLister = JsonConvert.DeserializeObject<AssetLister>(jsonStr);
                    if (mAssetLister != null && mAssetLister.mAssets != null)
                    {
                        foreach (var mAsset in mAssetLister.mAssets)
                        {
                            mPointMachineDatas.AddRange(mAsset.mPointMachineData);
                        }

                        if (searchCriteria.Status_A.IsNotNullOrEmpty())
                            mPointMachineDatas = mPointMachineDatas.Where(x => x.AI_status_A.Trim().ToLower() == searchCriteria.Status_A.Trim().ToLower()).ToList();

                        if (searchCriteria.Status_B.IsNotNullOrEmpty())
                            mPointMachineDatas = mPointMachineDatas.Where(x => x.AI_status_B.Trim().ToLower() == searchCriteria.Status_B.Trim().ToLower()).ToList();

                        if (searchCriteria.ClusterA.IsNotNullOrEmpty())
                            mPointMachineDatas = mPointMachineDatas.Where(x => x.ClusterA.Trim().ToLower() == searchCriteria.ClusterA.Trim().ToLower()).ToList();

                        if (searchCriteria.ClusterB.IsNotNullOrEmpty())
                            mPointMachineDatas = mPointMachineDatas.Where(x => x.ClusterB.Trim().ToLower() == searchCriteria.ClusterB.Trim().ToLower()).ToList();

                        if (searchCriteria.Direction.IsNotNullOrEmpty())
                            mPointMachineDatas = mPointMachineDatas.Where(x => x.Direction.Trim().ToLower() == searchCriteria.Direction.Trim().ToLower()).ToList();


                        if (searchCriteria.MultipleClusterA != null && searchCriteria.MultipleClusterA.Count > 0)
                            mPointMachineDatas = mPointMachineDatas.Where(x => searchCriteria.MultipleClusterA.Contains(x.ClusterA)).ToList();

                        if (searchCriteria.MultipleClusterB != null && searchCriteria.MultipleClusterB.Count > 0)
                            mPointMachineDatas = mPointMachineDatas.Where(x => searchCriteria.MultipleClusterB.Contains(x.ClusterB)).ToList();
                    }
                }

            }
            catch (Exception)
            {

                throw;
            }

            var jsonResult = Json(mPointMachineDatas, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;
        }

        public PointMachineJson PrepareModelFromNormalJson(string json)
        {
            try
            {
                if (string.IsNullOrEmpty(json))
                    return new PointMachineJson();
                else
                {
                    PointMachineNormal mPointMachins = JsonConvert.DeserializeObject<PointMachineNormal>(json);
                    return mPointMachins.pointMachineJson;
                }
            }
            catch (Exception ex)
            {
                return new PointMachineJson();
            }
        }

        public PointMachineJson PrepareModelFromJson(string json)
        {
            try
            {
                if (string.IsNullOrEmpty(json))
                    return new PointMachineJson();
                else
                {
                    List<PointMachineJson> mPointMachins = JsonConvert.DeserializeObject<List<PointMachineJson>>(json);
                    return mPointMachins.FirstOrDefault();
                }


            }
            catch (Exception ex)
            {
                var PointMachineJson = JsonConvert.DeserializeObject<RootMain>(json);
                return PointMachineJson._1;
            }
        }

        #endregion


        private Asset PrapareAsset(Asset asset)
        {
            var mAsset = new Asset();
            mAsset.SiteName = asset.SiteName;
            mAsset.AssetTypeName = asset.AssetTypeName;
            mAsset.Name = asset.Name;
            mAsset.IsHalfPointMachine = asset.IsHalfPointMachine;
            mAsset.AliasDirectionA = asset.AliasDirectionA;
            mAsset.AliasDirectionB = asset.AliasDirectionB;
            mAsset.IsSeriesOpration = asset.IsSeriesOpration;
            mAsset.AssetName = asset.AssetName;
            mAsset.AssetName = asset.AssetName;
            return mAsset;
        }

        private List<Domain.AssetType> GetAssetType()
        {
            List<Domain.AssetType> mAssetTypes = new List<Domain.AssetType>();
            try
            {
                using (var mHttpClientFactory = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = mHttpClientFactory.client.GetAsync(String.Format("AssetType/GetAllAssestType")).Result;
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

            return mAssetTypes;
        }

        private string GetValue(DataRow dataRow, string columnName)
        {
            string value = string.Empty;
            if (dataRow != null && dataRow.Table != null && dataRow.Table.Columns != null)
            {
                if (dataRow.Table.Columns.Contains(columnName) && dataRow[columnName] != DBNull.Value)
                    value = Convert.ToString(dataRow[columnName]).ToTrim();
            }
            return value;
        }

        public void RemoveCache()
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("AISection/RemoveCache")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;

                }
            }
            catch (Exception ex)
            {
            }
        }
    }
}