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

namespace E7FRSAdvance.Service
{
    public class SiteKeepingService : ISiteKeepingService
    {

        public TrainRunningMomentLister TrainRunningMoment(TrainRunningMomentLister mTrainRunningMomentLister)
        {

            if (mTrainRunningMomentLister != null && mTrainRunningMomentLister.SearchCriteria != null && mTrainRunningMomentLister.SearchCriteria.TimeStamp != null && mTrainRunningMomentLister.SearchCriteria.TimeStamp == DateTime.MinValue)
                mTrainRunningMomentLister.SearchCriteria.TimeStamp = DateTime.Now;

            if (mTrainRunningMomentLister != null && mTrainRunningMomentLister.SearchCriteria != null && mTrainRunningMomentLister.SearchCriteria.SiteId > 0)
            {
                mTrainRunningMomentLister.Pager.Take = -1;
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mTrainRunningMomentLister);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("SiteKeeping/GetTrainRunningMomentLister"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mTrainRunningMomentLister = JsonConvert.DeserializeObject<TrainRunningMomentLister>(jsonString);
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
            return mTrainRunningMomentLister;
        }

        public GluedLogDataLister GluedLogDataLister(GluedLogDataLister mGluedLogDataLister)
        {

            if (mGluedLogDataLister != null && mGluedLogDataLister.SearchCriteria != null && mGluedLogDataLister.SearchCriteria.TimeStamp != null && mGluedLogDataLister.SearchCriteria.TimeStamp == DateTime.MinValue)
                mGluedLogDataLister.SearchCriteria.TimeStamp = DateTime.Now;

            //mGluedLogDataLister.SearchCriteria.IsApp = true;
            mGluedLogDataLister.Pager.Take = -1;
            if (mGluedLogDataLister != null && mGluedLogDataLister.SearchCriteria != null && mGluedLogDataLister.SearchCriteria.SiteId > 0)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mGluedLogDataLister);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("SiteKeeping/GetGluedLogDataLister"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mGluedLogDataLister = JsonConvert.DeserializeObject<GluedLogDataLister>(jsonString);
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }

            return mGluedLogDataLister;
        }

        public IndexAlertLogLister IndexAlertLogLister(IndexAlertLogLister mIndexAlertLogLister)
        {
            if (mIndexAlertLogLister != null && mIndexAlertLogLister.SearchCriteria != null && mIndexAlertLogLister.SearchCriteria.TimeStamp != null && mIndexAlertLogLister.SearchCriteria.TimeStamp == DateTime.MinValue)
                mIndexAlertLogLister.SearchCriteria.TimeStamp = DateTime.Now;

            mIndexAlertLogLister.Pager.Take = -1;
            if (mIndexAlertLogLister != null && mIndexAlertLogLister.SearchCriteria != null)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mIndexAlertLogLister);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("SiteKeeping/GetIndexAlertLogLister"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mIndexAlertLogLister = JsonConvert.DeserializeObject<IndexAlertLogLister>(jsonString);
                        }
                    }
                }
                catch (Exception)
                {

                }
            }

            return mIndexAlertLogLister;
        }

        public SMSLogLister EarthFaultLister(SMSLogLister mSMSLogLister)
        {
            mSMSLogLister.SearchCriteria.AssetTypeId = (int)E7FRSAdvance.Utility.Utility.AssetType.Earth_Fault;
            mSMSLogLister.SearchCriteria.IsSmsLogActive = true;
            mSMSLogLister.Pager.Take = -1;
            if (mSMSLogLister != null && mSMSLogLister.SearchCriteria != null)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mSMSLogLister);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("SMSLog/GetAllSMSLogLister"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mSMSLogLister = JsonConvert.DeserializeObject<SMSLogLister>(jsonString);

                        }
                    }
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }

            return mSMSLogLister;
        }

        public WatchListLister AxleCounterLister(WatchListLister mWatchListLister)
        {
            if (mWatchListLister != null && mWatchListLister.SearchCriteria != null && mWatchListLister.SearchCriteria.CreatedDate != null && mWatchListLister.SearchCriteria.CreatedDate == DateTime.MinValue)
                mWatchListLister.SearchCriteria.CreatedDate = DateTime.Now;

            mWatchListLister.Pager.Take = -1;
            if (mWatchListLister != null && mWatchListLister.SearchCriteria != null)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mWatchListLister);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("SiteKeeping/GetAxleCounterIssuesLister"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mWatchListLister = JsonConvert.DeserializeObject<WatchListLister>(jsonString);

                        }

                    }
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }

            return mWatchListLister;
        }

        public MomentLister SignalMomentLister(MomentLister mMomentLister)
        {

            if (mMomentLister != null && mMomentLister.SearchCriteria != null && mMomentLister.SearchCriteria.TimeStamp != null && mMomentLister.SearchCriteria.TimeStamp == DateTime.MinValue)
                mMomentLister.SearchCriteria.TimeStamp = DateTime.Now;

            if (mMomentLister != null && mMomentLister.SearchCriteria != null && mMomentLister.SearchCriteria.SiteId > 0)
            {
                mMomentLister.Pager.Take = -1;
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mMomentLister);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("SiteKeeping/GetSignalMomentLister"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mMomentLister = JsonConvert.DeserializeObject<MomentLister>(jsonString);
                            if (mMomentLister != null && mMomentLister.mMoments != null && mMomentLister.mMoments.Count > 0)
                            {
                                var moments = mMomentLister.mMoments.ToList();
                                mMomentLister.mMoments = new List<Moment>();
                                foreach (var moment in moments.OrderBy(x => x.TimeStamp).ToList())
                                {
                                    if (mMomentLister.mMoments.Where(x => x.TimeStamp == moment.TimeStamp && x.Aspect == moment.Aspect).Count() <= 0)
                                        mMomentLister.mMoments.Add(moment);

                                }

                            }
                        }

                    }
                }
                catch (Exception)
                {
                }
            }
            return mMomentLister;
        }

        public MomentLister TPRMomentLister(MomentLister mMomentLister)
        {
            mMomentLister.Pager.Take = mMomentLister.Pager.PageSize;

            if (mMomentLister != null && mMomentLister.SearchCriteria != null && mMomentLister.SearchCriteria.TimeStamp != null && mMomentLister.SearchCriteria.TimeStamp == DateTime.MinValue)
                mMomentLister.SearchCriteria.TimeStamp = DateTime.Now;

            if (mMomentLister != null && mMomentLister.SearchCriteria != null && mMomentLister.SearchCriteria.SiteId > 0)
            {
                mMomentLister.Pager.Take = -1;
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mMomentLister);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("SiteKeeping/GetTPRMomentLister"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mMomentLister = JsonConvert.DeserializeObject<MomentLister>(jsonString);
                            if (mMomentLister != null && mMomentLister.mMoments != null && mMomentLister.mMoments.Count > 0)
                            {
                                var moments = mMomentLister.mMoments.ToList();
                                mMomentLister.mMoments = new List<Moment>();
                                foreach (var moment in moments.OrderBy(x => x.TimeStamp).ToList())
                                {
                                    if (mMomentLister.mMoments.Where(x => x.TimeStamp == moment.TimeStamp && x.Aspect == moment.Aspect).Count() <= 0)
                                        mMomentLister.mMoments.Add(moment);
                                    else
                                    {

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
            return mMomentLister;
        }

        public MomentLister PointIndicationLister(MomentLister mMomentLister)
        {
            mMomentLister.Pager.Take = mMomentLister.Pager.PageSize;

            if (mMomentLister != null && mMomentLister.SearchCriteria != null && mMomentLister.SearchCriteria.TimeStamp != null && mMomentLister.SearchCriteria.TimeStamp == DateTime.MinValue)
                mMomentLister.SearchCriteria.TimeStamp = DateTime.Now;

            if (mMomentLister != null && mMomentLister.SearchCriteria != null && mMomentLister.SearchCriteria.SiteId > 0)
            {
                mMomentLister.Pager.Take = -1;
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mMomentLister);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("SiteKeeping/GetPointMachineMomentLister"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mMomentLister = JsonConvert.DeserializeObject<MomentLister>(jsonString);
                            if (mMomentLister != null && mMomentLister.mMoments != null && mMomentLister.mMoments.Count > 0)
                            {
                                var moments = mMomentLister.mMoments.ToList();
                                mMomentLister.mMoments = new List<Moment>();
                                foreach (var moment in moments.OrderBy(x => x.TimeStamp).ToList())
                                {
                                    if (mMomentLister.mMoments.Where(x => x.TimeStamp == moment.TimeStamp && x.Aspect == moment.Aspect).Count() <= 0)
                                        mMomentLister.mMoments.Add(moment);



                                }

                            }
                        }

                    }
                }
                catch (Exception)
                {
                }
            }
            return mMomentLister;
        }

        public SMSLogLister GetAlertLogLister(SMSLogLister mSMSLogLister)
        {
            mSMSLogLister.SearchCriteria.IsSmsLogActive = true;
            mSMSLogLister.Pager.Take = -1;
            if (mSMSLogLister != null && mSMSLogLister.SearchCriteria != null)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mSMSLogLister);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("SMSLog/GetAll"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mSMSLogLister = JsonConvert.DeserializeObject<SMSLogLister>(jsonString);

                        }
                    }
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }

            return mSMSLogLister;
        }

        public ProbabilityLister GetProbabilityLister(ProbabilityLister mProbabilityLister)
        {
            mProbabilityLister.Pager.Take = -1;
            mProbabilityLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
            mProbabilityLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;

            if (mProbabilityLister != null && mProbabilityLister.SearchCriteria != null && mProbabilityLister.SearchCriteria.TimeStamp != null && mProbabilityLister.SearchCriteria.TimeStamp == DateTime.MinValue)
                mProbabilityLister.SearchCriteria.TimeStamp = DateTime.Now;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mProbabilityLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SiteKeeping/GetProbability"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mProbabilityLister = JsonConvert.DeserializeObject<ProbabilityLister>(jsonString);

                    }
                }
            }
            catch (Exception)
            {

            }
            return mProbabilityLister;
        }

        public PerformanceLister PerformanceLister(PerformanceLister mPerformanceLister)
        {

            if (mPerformanceLister != null && mPerformanceLister.SearchCriteria != null && mPerformanceLister.SearchCriteria.TimeStamp != null && mPerformanceLister.SearchCriteria.TimeStamp == DateTime.MinValue)
                mPerformanceLister.SearchCriteria.TimeStamp = DateTime.Now;

            if (mPerformanceLister != null && mPerformanceLister.SearchCriteria != null && mPerformanceLister.SearchCriteria.SiteId > 0)
            {
                mPerformanceLister.Pager.Take = -1;
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mPerformanceLister);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("SiteKeeping/GetPerformance"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mPerformanceLister = JsonConvert.DeserializeObject<PerformanceLister>(jsonString);
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
            return mPerformanceLister;
        }

        public A10Calibration GetA10Multiplayer(CalibData mA10Calibration, List<CardLine> mYardConfigs, int assetId, int attributeId)
        {
            A10Calibration a10Calibration = new A10Calibration();
            //string a10Id = string.Empty;
            try
            {
                if (mYardConfigs != null && mYardConfigs.Count > 0 && mA10Calibration != null && mA10Calibration.calib != null && mA10Calibration.calib.Count > 0)
                {
                    var mYardConfig = mYardConfigs.Where(x => x.AssetId == assetId && x.AttributeId == attributeId).FirstOrDefault();
                    if (mYardConfig != null && mYardConfig.Id > 0)
                    {
                        a10Calibration = mA10Calibration.calib.Where(x => x.id == Convert.ToString(mYardConfig.ADCNumber)).FirstOrDefault();
                        if (a10Calibration != null && a10Calibration.registers != null)
                        {
                            if (a10Calibration.device_type_id == "1505")
                            {
                                if (a10Calibration.registers.Length > 4)
                                {
                                    var batchedTags = ExtensionMethod.BatchItems(a10Calibration.registers, 4).ToList();
                                    if (batchedTags != null && batchedTags.Count() > mYardConfig.Pin.Value - 1)
                                    {
                                        var dsds = batchedTags[mYardConfig.Pin.Value - 1];
                                        //a10Id = dsds[1].ToString();
                                        a10Calibration.A10Multiplication = dsds[1].ToString();

                                    }
                                }
                            }
                            else if (a10Calibration.device_type_id == "1507")
                            {
                                if (a10Calibration.registers.Length > 5)
                                {
                                    var batchedTags = ExtensionMethod.BatchItems(a10Calibration.registers, 5).ToList();
                                    if (batchedTags != null && batchedTags.Count() > mYardConfig.Pin.Value - 1)
                                    {
                                        var dsds = batchedTags[mYardConfig.Pin.Value - 1];
                                        a10Calibration.A10Multiplication = dsds[1].ToString();
                                    }
                                }
                            }
                            else if (a10Calibration.device_type_id == "1056")
                            {
                                if (a10Calibration.registers.Length > 5)
                                {
                                    var batchedTags = ExtensionMethod.BatchItems(a10Calibration.registers, 5).ToList();
                                    if (batchedTags != null && batchedTags.Count() > mYardConfig.Pin.Value - 1)
                                    {
                                        var dsds = batchedTags[mYardConfig.Pin.Value - 1];
                                        a10Calibration.A10Multiplication = dsds[1].ToString();
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


            return a10Calibration;
        }
    }
}