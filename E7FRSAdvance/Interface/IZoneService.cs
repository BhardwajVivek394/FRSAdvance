using System.Collections.Generic;

namespace E7FRSAdvance.Interface
{
    public interface IZoneService
    {
        List<Domain.Zone> GetAll();

        List<Domain.Zone> GetAllZones();

        Domain.Zone GetZoneById(int id);
    }
}
