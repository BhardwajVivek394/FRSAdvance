using Domain;
using System.Collections.Generic;

namespace E7FRSAdvance.Interface
{
    public interface IDivisionService
    {
        Division Get(int id);

        List<Division> GetAll();

        List<Division> GetAllDivisions();

        List<Division> GetByZoneId(int zoneId);

        List<Domain.Division> GetDivisionList(DivisionLister mDivisionLister);
    }
}
