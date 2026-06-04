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

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class TrainMomentDetailController : Controller
    {
        // GET: TrainMomentDetail
        private readonly ISiteService _siteService;
        public TrainMomentDetailController(ISiteService siteService)
        {
            _siteService = siteService;
        }
        public ActionResult Index()
        {
            var sites = new List<Domain.SiteTrainMoment>();
            // if (mSite != null && mSite.Id > 0)
            {
                int[] siteIds = { 75, 78, 79, 80 };
                foreach (var siteId in siteIds)
                {
                    //var siteId = 75;
                    try
                    {
                        using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                        {
                            var response = hcf.client.GetAsync(String.Format("Site/GetSiteById/{0}", siteId)).Result;
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            if (response.StatusCode == HttpStatusCode.OK)
                            {
                                var mSite = JsonConvert.DeserializeObject<Domain.SiteTrainMoment>(jsonString);
                                if (mSite != null && mSite.Id > 0)
                                {
                                    if (siteId == 75)
                                    {
                                        mSite.DownAsset = "OC12T";
                                        mSite.UpAsset = "OC1T";

                                        mSite.DownLineAssets = GetDownLineTrackAsset(siteId, mSite.DownAsset);
                                        mSite.UpLineAssets = GetUpLineTrackAsset(siteId, mSite.UpAsset);
                                        sites.Add(mSite);
                                    }
                                    else if (siteId == 78)
                                    {
                                        //mSite.DownAsset = "OC1T";
                                        //mSite.UpAsset = "OC8T";

                                        mSite.DownAsset = "OC8T";
                                        mSite.UpAsset = "OC1T";

                                        mSite.DownLineAssets = GetUpLineTrackAsset(siteId, mSite.DownAsset);
                                        mSite.UpLineAssets = GetDownLineTrackAsset(siteId, mSite.UpAsset);
                                        sites.Add(mSite);
                                    }
                                    else if (siteId == 79)
                                    {
                                        mSite.DownAsset = "OC10T";
                                        mSite.UpAsset = "OC1T";

                                        mSite.DownLineAssets = GetDownLineTrackAsset(siteId, mSite.DownAsset);
                                        mSite.UpLineAssets = GetUpLineTrackAsset(siteId, mSite.UpAsset);
                                        sites.Add(mSite);
                                    }
                                    else if (siteId == 80)
                                    {
                                        mSite.DownAsset = "10T";
                                        mSite.UpAsset = "1T";

                                        mSite.DownLineAssets = GetDownLineTrackAsset(siteId, mSite.DownAsset);
                                        mSite.UpLineAssets = GetUpLineTrackAsset(siteId, mSite.UpAsset);
                                        sites.Add(mSite);
                                    }



                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        throw ex;
                    }

                }


            }
            ViewBag.MQTTDetail = GetMQTTDetailList().mQTTDetailWeb;
            return View(sites);
        }

        public ActionResult Automatic()
        {
            var sites = new List<Domain.SiteTrainMoment>();
            // if (mSite != null && mSite.Id > 0)
            {
                int[] siteIds = { 75, 78, 79, 80 };
                foreach (var siteId in siteIds)
                {
                    //var siteId = 75;
                    try
                    {
                        using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                        {
                            var response = hcf.client.GetAsync(String.Format("Site/GetSiteById/{0}", siteId)).Result;
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            if (response.StatusCode == HttpStatusCode.OK)
                            {
                                var mSite = JsonConvert.DeserializeObject<Domain.SiteTrainMoment>(jsonString);
                                if (mSite != null && mSite.Id > 0)
                                {
                                    if (siteId == 75)
                                    {
                                        mSite.DownAsset = "OC12T";
                                        mSite.UpAsset = "OC1T";

                                        mSite.DownLineAssets = GetDownLineTrackAsset(siteId, mSite.DownAsset);
                                        mSite.UpLineAssets = GetUpLineTrackAsset(siteId, mSite.UpAsset);
                                        sites.Add(mSite);
                                    }
                                    else if (siteId == 78)
                                    {
                                        //mSite.DownAsset = "OC1T";
                                        //mSite.UpAsset = "OC8T";

                                        mSite.DownAsset = "OC8T";
                                        mSite.UpAsset = "OC1T";

                                        mSite.DownLineAssets = GetUpLineTrackAsset(siteId, mSite.DownAsset);
                                        mSite.UpLineAssets = GetDownLineTrackAsset(siteId, mSite.UpAsset);
                                        sites.Add(mSite);
                                    }
                                    else if (siteId == 79)
                                    {
                                        mSite.DownAsset = "OC10T";
                                        mSite.UpAsset = "OC1T";

                                        mSite.DownLineAssets = GetDownLineTrackAsset(siteId, mSite.DownAsset);
                                        mSite.UpLineAssets = GetUpLineTrackAsset(siteId, mSite.UpAsset);
                                        sites.Add(mSite);
                                    }
                                    else if (siteId == 80)
                                    {
                                        mSite.DownAsset = "10T";
                                        mSite.UpAsset = "1T";

                                        mSite.DownLineAssets = GetDownLineTrackAsset(siteId, mSite.DownAsset);
                                        mSite.UpLineAssets = GetUpLineTrackAsset(siteId, mSite.UpAsset);
                                        sites.Add(mSite);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        throw ex;
                    }

                }


            }
            ViewBag.MQTTDetail = GetMQTTDetailList().mQTTDetailWeb;
            return View(sites);
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

        public List<Domain.Asset> GetDownLineTrackAsset(int siteId, string trackName)
        {
            List<Domain.Asset> mAssets = new List<Domain.Asset>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Asset/GetDownLineTrack/SiteId/{siteId}/TrackName/{trackName}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssets = JsonConvert.DeserializeObject<List<Domain.Asset>>(jsonString);
                    }
                }
            }
            catch (Exception)
            {

            }
            return mAssets;
        }

        public List<Domain.Asset> GetUpLineTrackAsset(int siteId, string trackName)
        {
            List<Domain.Asset> mAssets = new List<Domain.Asset>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Asset/GetUpLineTrack/SiteId/{siteId}/TrackName/{trackName}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssets = JsonConvert.DeserializeObject<List<Domain.Asset>>(jsonString);
                    }
                }
            }
            catch (Exception)
            {

            }

            return mAssets;
        }

        public ActionResult GetDownLineTrack(int siteId, string trackName)
        {
            List<Domain.Asset> mAssets = new List<Domain.Asset>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Asset/GetDownLineTrack/SiteId/{siteId}/TrackName/{trackName}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssets = JsonConvert.DeserializeObject<List<Domain.Asset>>(jsonString);
                    }
                }
            }
            catch (Exception)
            {

            }
            return PartialView("_AssetLineTrack", mAssets);
        }

        public ActionResult GetUpLineTrack(int siteId, string trackName)
        {
            List<Domain.Asset> mAssets = new List<Domain.Asset>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Asset/GetUpLineTrack/SiteId/{siteId}/TrackName/{trackName}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssets = JsonConvert.DeserializeObject<List<Domain.Asset>>(jsonString);
                    }
                }
            }
            catch (Exception)
            {

            }

            return PartialView("_AssetLineTrack", mAssets);
        }

        [HttpPost]
        public ActionResult SaveTrainMomentDetail(Domain.TrainMoment mTrainMoment)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mTrainMoment.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mTrainMoment);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("TrainMoment"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mTrainMoment = JsonConvert.DeserializeObject<Domain.TrainMoment>(jsonString);
                        if (mTrainMoment != null && mTrainMoment.Id > 0)
                        {
                            data = new { type = "success", result = "Record has been Saved." };
                        }
                        else
                        {
                            data = new { type = "error", result = "Internal server error." };
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.BadRequest)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        data = new { type = "error", result = JsonConvert.DeserializeObject<string>(jsonString) };
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
            return Json(data, JsonRequestBehavior.AllowGet);

        }

        public ActionResult GetTrainMomentView(Domain.SearchCriteria searchCriteria)
        {
            List<Domain.TrainMoment> mTrainMoments = new List<Domain.TrainMoment>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(searchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("TrainMoment/GetList"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mTrainMoments = JsonConvert.DeserializeObject<List<Domain.TrainMoment>>(jsonString);
                    }
                }
            }
            catch (Exception)
            {

            }
            return PartialView("TrainMomentView", mTrainMoments);
        }

        public ActionResult DownloadTrainMomentView(int siteId)
        {
            List<Domain.TrainMoment> mTrainMoments = new List<Domain.TrainMoment>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"TrainMoment/SiteId/{siteId}/Date/{DateTime.Now.Date.ToString("d-M-yyyy")}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mTrainMoments = JsonConvert.DeserializeObject<List<Domain.TrainMoment>>(jsonString);
                        if (mTrainMoments != null && mTrainMoments.Count > 0)
                        {
                            var mSite = _siteService.Get(siteId);
                            if (mSite != null && mSite.Id > 0)
                            {
                                var csv = new StringBuilder();
                                var socsvstring = string.Format("{0},{1},{2},{3},{4},{5}", "Date Time", "Unique Id", "Train Number", "Line Type", $"{mSite.StationCode} In Time", $"{mSite.StationCode} Out Time");
                                csv.AppendLine(socsvstring);
                                foreach (var mTrainMoment in mTrainMoments)
                                {
                                    string lineType = string.Empty;
                                    if (mTrainMoment.LineTypeId == 1)
                                    {
                                        lineType = "UP Line";
                                    }
                                    else if (mTrainMoment.LineTypeId == 2)
                                    {
                                        lineType = "Down Line";
                                    }

                                    socsvstring = string.Format("{0},{1},{2},{3},{4},{5}", $"{mTrainMoment.CreatedDate.ToString()}", $"{mTrainMoment.UniqueId}", $"{mTrainMoment.TrainNumber}", $"{lineType}", $"{mTrainMoment.EntryTime}", $"{mTrainMoment.ExitTime}");
                                    csv.AppendLine(socsvstring);
                                }


                                Response.Clear();
                                Response.Buffer = true;
                                Response.AddHeader("content-disposition", $"attachment;filename={mSite.StationCode}TrainMoment" + DateTime.Now.Ticks + ".csv");
                                Response.Charset = "utf-8";
                                Response.ContentType = "text/csv";
                                Response.Output.Write(csv);
                                Response.Flush();
                                Response.End();

                            }

                        }
                    }
                }
            }
            catch (Exception)
            {

            }

            return RedirectToAction("Index");
        }

        public ActionResult DownloadTrainMomentAllSite(Domain.SearchCriteria searchCriteria)
        {
            List<Domain.TrainMoment> mTrainMoments = new List<Domain.TrainMoment>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(searchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("TrainMoment/GetList"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mTrainMoments = JsonConvert.DeserializeObject<List<Domain.TrainMoment>>(jsonString);
                        if (mTrainMoments != null && mTrainMoments.Count > 0)
                        {
                            var stationCodes = new List<string>() { "STLR", "DGA", "BAGL", "GJH" };
                            if (stationCodes != null && stationCodes.Count() > 0)
                            {
                                var csv = new StringBuilder();
                                var socsvstring = string.Format("{0},{1},{2},{3}", "Date Time", "Unique Id", "Train Number", "Line Type");

                                foreach (var item in stationCodes)
                                {
                                    socsvstring += $",{item} In Time";
                                    socsvstring += $",{item} Out Time";
                                }
                                csv.AppendLine(socsvstring);

                                socsvstring = string.Empty;
                                foreach (var uniqueId in mTrainMoments.GroupBy(x => x.UniqueId).Select(x => x.Key).ToList())
                                {
                                    bool isUpload = false;
                                    foreach (var item in stationCodes)
                                    {
                                        var mTrainMoment = mTrainMoments.Where(x => x.UniqueId == uniqueId && x.StationCode == item).FirstOrDefault();

                                        if (mTrainMoment != null)
                                        {
                                            if (!isUpload)
                                            {
                                                socsvstring = string.Empty;
                                                socsvstring += $"{mTrainMoment.CreatedDate}";
                                                socsvstring += $",{mTrainMoment.UniqueId}";
                                                socsvstring += $",{mTrainMoment.TrainNumber}";
                                                if (mTrainMoment.LineTypeId == 1)
                                                {
                                                    socsvstring += $",UP Line";
                                                }
                                                else if (mTrainMoment.LineTypeId == 2)
                                                {
                                                    socsvstring += $",Down Line";
                                                }
                                                isUpload = true;
                                            }

                                            socsvstring += $",{mTrainMoment.EntryTime}";
                                            socsvstring += $",{mTrainMoment.ExitTime}";

                                            //socsvstring = string.Format("{0},{1},{2},{3},{4}", $"{mTrainMoment.CreatedDate.ToString()}", $"{mTrainMoment.UniqueId}", $"{mTrainMoment.TrainNumber}", $"{mTrainMoment.EntryTime}", $"{mTrainMoment.ExitTime}");
                                        }
                                        else
                                        {
                                            socsvstring += $",";
                                            socsvstring += $",";
                                        }
                                    }
                                    csv.AppendLine(socsvstring);

                                }


                                Response.Clear();
                                Response.Buffer = true;
                                Response.AddHeader("content-disposition", $"attachment;filename=AllTrainMoment" + DateTime.Now.Ticks + ".csv");
                                Response.Charset = "utf-8";
                                Response.ContentType = "text/csv";
                                Response.Output.Write(csv);
                                Response.Flush();
                                Response.End();
                            }

                            //var mSite = _siteService.Get(siteId);
                            //if (mSite != null && mSite.Id > 0)
                            //{


                            //}

                        }
                    }
                }
            }
            catch (Exception)
            {

            }

            return RedirectToAction("Index");
        }

        public ActionResult DownloadTrainMomentLatestData()
        {
            List<Domain.TrainMoment> mTrainMoments = new List<Domain.TrainMoment>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"TrainMoment/GetLatestRecord/Date/{DateTime.Now.Date.ToString("d-M-yyyy")}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mTrainMoments = JsonConvert.DeserializeObject<List<Domain.TrainMoment>>(jsonString);

                    }
                }
            }
            catch (Exception)
            {

            }

            return Json(mTrainMoments, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetTrainMomentLatestData()
        {
            List<Domain.TrainMoment> mTrainMoments = new List<Domain.TrainMoment>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"TrainMoment/GetLatestRecord/Date/{DateTime.Now.Date.ToString("d-M-yyyy")}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mTrainMoments = JsonConvert.DeserializeObject<List<Domain.TrainMoment>>(jsonString);

                    }
                }
            }
            catch (Exception)
            {

            }

            return Json(mTrainMoments, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public ActionResult UpdateTrainMomentDetail(Domain.TrainMoment mTrainMoment)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mTrainMoment.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mTrainMoment);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format("TrainMoment/UpdateTrainNumber"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mTrainMoment = JsonConvert.DeserializeObject<Domain.TrainMoment>(jsonString);
                        if (mTrainMoment != null && mTrainMoment.Id > 0)
                        {
                            data = new { type = "success", result = "Record has been Updated." };
                        }
                        else
                        {
                            data = new { type = "error", result = "Internal server error." };
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.BadRequest)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        data = new { type = "error", result = JsonConvert.DeserializeObject<string>(jsonString) };
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
            return Json(data, JsonRequestBehavior.AllowGet);

        }
    }
}