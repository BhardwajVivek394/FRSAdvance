using Domain;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;

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

        public List<Domain.AssetType> GetLister(AssetTypeLister mAssetTypeLister)
        {
            var assetTypes = new List<Domain.AssetType>();
            mAssetTypeLister.Pager.Take = -1;
            mAssetTypeLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
            mAssetTypeLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssetTypeLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("AssetType/GetAll"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetTypeLister = JsonConvert.DeserializeObject<AssetTypeLister>(jsonString);
                        if (mAssetTypeLister != null && mAssetTypeLister.mAssetTypes != null)
                        {
                            assetTypes = mAssetTypeLister.mAssetTypes;
                        }


                    }
                }
            }
            catch (Exception)
            {
            }

            return assetTypes;
        }

    }
}