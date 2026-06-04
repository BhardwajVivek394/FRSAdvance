using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;

namespace E7FRSAdvance.Service
{
    public class AssetTypeService : IAssetTypeService
    {
        public List<Domain.AssetType> GetAll()
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
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }

            return mAssetTypes;
        }

        public List<Domain.AssetType> GetBy(int siteId)
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
                    }
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }

            return mAssetTypes;
        }
    }
}