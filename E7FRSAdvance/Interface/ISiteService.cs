using Domain;
using System.Collections.Generic;

namespace E7FRSAdvance.Interface
{
    public interface ISiteService
    {
        List<Domain.Site> GetAll();

        Domain.Site GetBy(string siteName);

        Domain.Site Get(int id);

        List<Domain.Site> GetBy(int zoneId, int divisionId);

        List<Domain.Site> GetBy(int divisionId);

        List<Domain.Site> GetSite();

        List<Domain.Site> GetDataLoggerSite();

        List<Domain.Site> GetSiteLister(SiteLister mSiteLister);
    }
}
