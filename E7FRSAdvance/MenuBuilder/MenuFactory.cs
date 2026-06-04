using Domain;

namespace E7FRSAdvance.MenuBuilder
{
    public class MenuFactory
    {
        private User loginUser;
        public MenuFactory(User loginUser)
        {
            this.loginUser = loginUser;
        }

        public IMenuBuilder CreateInstance()
        {
            IMenuBuilder instance = null;

            if (loginUser.RoleId == E7FRSAdvance.Utility.Utility.Role.User.GetHashCode())
            {
                instance = new Users();
            }
            else if (loginUser.RoleId == E7FRSAdvance.Utility.Utility.Role.Admin.GetHashCode())
            {
                instance = new Admin();
            }

            return instance;
        }

        public IMenuBuilder CreateInstanceAnnexure(int annexureId)
        {
            IMenuBuilder instance = null;

            if (annexureId == E7FRSAdvance.Utility.Utility.Annexure.Basic.GetHashCode())
            {
                if (loginUser.RoleId == E7FRSAdvance.Utility.Utility.Role.User.GetHashCode())
                    instance = new Users();
                else if (loginUser.RoleId == E7FRSAdvance.Utility.Utility.Role.Admin.GetHashCode())
                    instance = new Admin();
            }
            else if (annexureId == E7FRSAdvance.Utility.Utility.Annexure.FRS_25.GetHashCode())
            {
                instance = new E7FRSAdvance.MenuBuilder.FRS25();
            }

            return instance;
        }
    }
}