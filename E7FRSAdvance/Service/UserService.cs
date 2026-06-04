using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;

namespace E7FRSAdvance.Service
{
    public class UserService : IUserService
    {
        public Domain.User GetBy(int id)
        {
            Domain.User mUser = new Domain.User();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("User/GetUserById/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mUser = JsonConvert.DeserializeObject<Domain.User>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
            }
            return mUser;
        }

        public List<Domain.User> GetSiteKeepingUser()
        {
            var mUsers = new List<Domain.User>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"User/GetSiteKeepingUser")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mUsers = JsonConvert.DeserializeObject<List<Domain.User>>(jsonString);
                        if (mUsers != null && mUsers.Count > 0)
                        {
                            foreach (var mUser in mUsers)
                            {
                                mUser.Name = $"{mUser.FirstName} {mUser.LastName}";
                            }

                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mUsers;
        }

        public List<Domain.User> GetAuditUser()
        {
            var mUsers = new List<Domain.User>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"User/GetAuditUser")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mUsers = JsonConvert.DeserializeObject<List<Domain.User>>(jsonString);
                        if (mUsers != null && mUsers.Count > 0)
                        {
                            foreach (var mUser in mUsers)
                            {
                                mUser.Name = $"{mUser.FirstName}";
                            }

                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mUsers;
        }
    }
}