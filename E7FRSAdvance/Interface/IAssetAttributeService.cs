using Domain;
using Domain.Dto;
using System.Collections.Generic;

namespace E7FRSAdvance.Interface
{
    public interface IAssetAttributeService
    {
        List<AssetAttribute> GetAll();

        AssetAttribute Get(int id);

        List<AssetAttribute> GetAssetAttributesBy(int assetTypeId);

        List<AssetAttribute> GetAllAssetAttributeBy(int assetTypeId);

        List<AssetAttribute> GetAssetAttributes(int siteId, int assetId);

        void PrepareGraphAttribute(List<AssetAttribute> assetAttributes, List<AssetAttribute> allAssetAttributes, string vals);

        void PrepareWithDataArrayGraphAttribute(List<AssetAttribute> assetAttributes, List<AssetAttribute> allAssetAttributes, string vals);

        List<AssetAttribute> GetGraphAttribute(List<AssetAttribute> assetAttributes, List<AssetAttribute> allAssetAttributes, string vals);

        void PrepareGraphAttributeWithActualTime(List<AssetAttribute> assetAttributes, List<AssetAttribute> allAssetAttributes, string vals, string actualTime, string changeTimestamp);

        string PrepareCSV(List<Asset> assets);
        string PrepareCSVForFastDataProvider(List<Asset> assets);

        string PrepareHtml(List<Asset> assets);

        List<AssetAttribute> GetByIsDerived(int assetTypeId);


        string PrepareCSVWithEdgex(List<Asset> assets, RdpmsResponse rdpmsResponse);
    }
}
