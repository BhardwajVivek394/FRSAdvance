using Domain;
using System.Collections.Generic;

namespace E7FRSAdvance.Interface
{
    public interface ICardLineService
    {
        List<CardLine> Get(int siteId);
        List<CardLine> GetByAssetId(int assetId);
    }
}
