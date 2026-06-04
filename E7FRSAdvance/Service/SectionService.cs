using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;

namespace E7FRSAdvance.Service
{
    public class SectionService : ISectionService
    {
        public List<Domain.Section> GetAll()
        {
            List<Domain.Section> mSections = new List<Domain.Section>();
            try
            {
                using (var mHttpClientFactory = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = mHttpClientFactory.client.GetAsync(String.Format("Section/GetAll")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mSections = JsonConvert.DeserializeObject<List<Domain.Section>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }

            return mSections;
        }
    }
}