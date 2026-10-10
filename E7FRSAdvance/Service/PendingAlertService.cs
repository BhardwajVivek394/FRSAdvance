using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;

namespace E7FRSAdvance.Service
{
    public class PendingAlertService : IPendingAlertService
    {

        public Domain.PendingAlertLister GetWithOutAcknowledgementAlert(Domain.PendingAlertLister mFRSAlertLister)
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
                        mFRSAlertLister = JsonConvert.DeserializeObject<PendingAlertLister>(jsonString);


                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mFRSAlertLister;
        }
        public Domain.PendingAlertLister GetListerWithPagination(Domain.PendingAlertLister mFRSAlertLister)
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
                    var response = hcf.client.PostAsync(String.Format("PendingAlert/GetListerWithPagination"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mFRSAlertLister = JsonConvert.DeserializeObject<PendingAlertLister>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mFRSAlertLister;
        }
        public Domain.PendingAlertLister GetAcknowledgementAllAlert(Domain.PendingAlertLister mFRSAlertLister)
        {
            try
            {
                mFRSAlertLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
                mFRSAlertLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
                if (ClsHttpContent.LoginUser.IsSiteKeeping)
                    mFRSAlertLister.SearchCriteria.RoleId = (int)E7FRSAdvance.Utility.Utility.Role.Admin;
                mFRSAlertLister.Pager.Take = -1;

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mFRSAlertLister);
                    System.Diagnostics.Debug.WriteLine("REQUEST URL: " + hcf.client.BaseAddress + "PendingAlert/GetListerWithPagination");
                    System.Diagnostics.Debug.WriteLine("REQUEST BODY: " + jsonStr);

                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync("PendingAlert/GetListerWithPagination", str).Result;

                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    System.Diagnostics.Debug.WriteLine("STATUS: " + (int)response.StatusCode);
                    System.Diagnostics.Debug.WriteLine("RESPONSE BODY: " + jsonString);

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mFRSAlertLister = JsonConvert.DeserializeObject<PendingAlertLister>(jsonString);
                    }
                    else
                    {
                        throw new Exception($"API failed [{(int)response.StatusCode}]: {jsonString}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("EXCEPTION: " + ex.ToString());
                throw;
            }
            return mFRSAlertLister;
        }
    }
}