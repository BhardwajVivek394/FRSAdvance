using Domain;
using System.Collections.Generic;

namespace E7FRSAdvance.Interface
{
    public interface IAssetTypeService
    {
        List<Domain.AssetType> GetAll();

        List<Domain.AssetType> GetBy(int siteId);
        List<Domain.AssetType> GetLister(AssetTypeLister mAssetTypeLister);
    }
}
