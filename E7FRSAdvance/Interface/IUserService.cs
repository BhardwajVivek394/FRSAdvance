using System.Collections.Generic;

namespace E7FRSAdvance.Interface
{
    public interface IUserService
    {
        Domain.User GetBy(int id);

        List<Domain.User> GetSiteKeepingUser();

        List<Domain.User> GetAuditUser();
    }
}
