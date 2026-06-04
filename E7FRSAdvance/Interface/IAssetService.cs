using Domain;
using System.Collections.Generic;

namespace E7FRSAdvance.Interface
{
    public interface IAssetService
    {
        List<Asset> GetAsset(int typeId);

        List<Asset> GetAssestBy(int siteId, int assetTypeId);

        List<Asset> GetAssestBy(int siteId);

        Asset Get(int id);

        Asset GetSignalAspectData(int id);
    }
}
