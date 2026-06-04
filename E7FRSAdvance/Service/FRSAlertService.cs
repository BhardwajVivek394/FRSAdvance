using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;

namespace E7FRSAdvance.Service
{
    public class FRSAlertService : IFRSAlertService
    {
        public Domain.FRSAlertLister GetListerWithTimeFilter(Domain.FRSAlertLister mFRSAlertLister)
        {
            try
            {
                mFRSAlertLister.SearchCriteria.IsAcknowledgement = false;
                mFRSAlertLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
                mFRSAlertLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mFRSAlertLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("FRSAlert/GetListerWithTimeFilter"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mFRSAlertLister = JsonConvert.DeserializeObject<FRSAlertLister>(jsonString);

                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mFRSAlertLister;
        }

        public Domain.FRSAlertLister GetWithOutAcknowledgementAlert(Domain.FRSAlertLister mFRSAlertLister)
        {
            try
            {
                mFRSAlertLister.SearchCriteria.IsAcknowledgement = false;
                mFRSAlertLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
                mFRSAlertLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;

                if (ClsHttpContent.LoginUser.IsSiteKeeping)
                    mFRSAlertLister.SearchCriteria.RoleId = (int)E7FRSAdvance.Utility.Utility.Role.Admin;

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mFRSAlertLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("FRSAlert/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mFRSAlertLister = JsonConvert.DeserializeObject<FRSAlertLister>(jsonString);
                    

                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mFRSAlertLister;
        }

        public Domain.FRSAlertLister GetWithAcknowledgementAlert(Domain.FRSAlertLister mFRSAlertLister)
        {
            try
            {
                mFRSAlertLister.SearchCriteria.IsAcknowledgement = true;
                mFRSAlertLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
                mFRSAlertLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
                if (ClsHttpContent.LoginUser.IsSiteKeeping)
                    mFRSAlertLister.SearchCriteria.RoleId = (int)E7FRSAdvance.Utility.Utility.Role.Admin;

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mFRSAlertLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("FRSAlert/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mFRSAlertLister = JsonConvert.DeserializeObject<FRSAlertLister>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mFRSAlertLister;
        }

        public Domain.FRSAlertLister GetFalseAcknowledgementFRSAlert(Domain.FRSAlertLister mFRSAlertLister)
        {
            try
            {
                mFRSAlertLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
                mFRSAlertLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;

                if (ClsHttpContent.LoginUser.IsSiteKeeping)
                    mFRSAlertLister.SearchCriteria.RoleId = (int)E7FRSAdvance.Utility.Utility.Role.Admin;

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mFRSAlertLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("FRSAlert/GetFalseAcknowledgementFRSAlert"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mFRSAlertLister = JsonConvert.DeserializeObject<FRSAlertLister>(jsonString);
                    

                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mFRSAlertLister;
        }

        public Domain.FRSAlertLister GetListerWithPagination(Domain.FRSAlertLister mFRSAlertLister)
        {
            try
            {
                mFRSAlertLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
                mFRSAlertLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;

                if (ClsHttpContent.LoginUser.IsSiteKeeping)
                    mFRSAlertLister.SearchCriteria.RoleId = (int)E7FRSAdvance.Utility.Utility.Role.Admin;

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mFRSAlertLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("FRSAlert/GetListerWithPagination"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mFRSAlertLister = JsonConvert.DeserializeObject<FRSAlertLister>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mFRSAlertLister;
        }

        public Domain.FRSAlertLister GetAll(Domain.FRSAlertLister mFRSAlertLister)
        {
            try
            {
                mFRSAlertLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
                mFRSAlertLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
                if (ClsHttpContent.LoginUser.IsSiteKeeping)
                    mFRSAlertLister.SearchCriteria.RoleId = (int)E7FRSAdvance.Utility.Utility.Role.Admin;

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mFRSAlertLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("FRSAlert/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mFRSAlertLister = JsonConvert.DeserializeObject<FRSAlertLister>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mFRSAlertLister;
        }


        public Domain.FRSAlertLister GetWithOutAcknowledgementAlertList(Domain.FRSAlertLister mFRSAlertLister)
        {
            try
            {
                mFRSAlertLister.SearchCriteria.IsAcknowledgement = false;
                mFRSAlertLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
                mFRSAlertLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;

                if (ClsHttpContent.LoginUser.IsSiteKeeping)
                    mFRSAlertLister.SearchCriteria.RoleId = (int)E7FRSAdvance.Utility.Utility.Role.Admin;

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mFRSAlertLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("FRSAlert/GetList"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mFRSAlertLister = JsonConvert.DeserializeObject<FRSAlertLister>(jsonString);


                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mFRSAlertLister;
        }

        public Domain.FRSAlertLister GetWithAcknowledgementAlertList(Domain.FRSAlertLister mFRSAlertLister)
        {
            try
            {
                mFRSAlertLister.SearchCriteria.IsAcknowledgement = true;
                mFRSAlertLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
                mFRSAlertLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
                if (ClsHttpContent.LoginUser.IsSiteKeeping)
                    mFRSAlertLister.SearchCriteria.RoleId = (int)E7FRSAdvance.Utility.Utility.Role.Admin;

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mFRSAlertLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("FRSAlert/GetList"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mFRSAlertLister = JsonConvert.DeserializeObject<FRSAlertLister>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mFRSAlertLister;
        }


        public Domain.FRSAlertLister GetWithAcknowledgementAlertWithManual(Domain.FRSAlertLister mFRSAlertLister)
        {
            try
            {
                mFRSAlertLister.SearchCriteria.IsAcknowledgement = true;
                mFRSAlertLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
                mFRSAlertLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
                if (ClsHttpContent.LoginUser.IsSiteKeeping)
                    mFRSAlertLister.SearchCriteria.RoleId = (int)E7FRSAdvance.Utility.Utility.Role.Admin;

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mFRSAlertLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("FRSAlert/GetFRSLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mFRSAlertLister = JsonConvert.DeserializeObject<FRSAlertLister>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mFRSAlertLister;
        }

        public Domain.FRSAlertLister GetAcknowledgementAllAlert(Domain.FRSAlertLister mFRSAlertLister)
        {
            try
            {
                mFRSAlertLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
                mFRSAlertLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
                if (ClsHttpContent.LoginUser.IsSiteKeeping)
                    mFRSAlertLister.SearchCriteria.RoleId = (int)E7FRSAdvance.Utility.Utility.Role.Admin;

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mFRSAlertLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("FRSAlert/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mFRSAlertLister = JsonConvert.DeserializeObject<FRSAlertLister>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mFRSAlertLister;
        }
    }
}