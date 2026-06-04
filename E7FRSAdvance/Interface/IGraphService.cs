using Domain;
using System.Collections.Generic;

namespace E7FRSAdvance.Interface
{
    public interface IGraphService
    {
        void PrepareGraphAttribute(List<AssetAttribute> assetAttributes, List<AssetAttribute> allAssetAttributes, string vals);

        void PrepareWithDataArrayGraphAttribute(List<AssetAttribute> assetAttributes, List<AssetAttribute> allAssetAttributes, string vals);
    }
}
