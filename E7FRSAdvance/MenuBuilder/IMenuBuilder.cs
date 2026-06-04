using Domain;
using System.Collections.Generic;

namespace E7FRSAdvance.MenuBuilder
{
    public interface IMenuBuilder
    {
        List<Menu> Build(User loginUser);
    }
}