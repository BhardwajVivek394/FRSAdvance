using System.Web.Mvc;

namespace E7FRSAdvance.Areas.FRS25
{
    public class FRS25AreaRegistration : AreaRegistration 
    {
        public override string AreaName 
        {
            get 
            {
                return "FRS25";
            }
        }

        public override void RegisterArea(AreaRegistrationContext context) 
        {
            // Correctly spelled alias for the MaintenceRoster controller; the old URL keeps working via the default route
            context.MapRoute(
                "FRS25_MaintenanceRoster",
                "FRS25/MaintenanceRoster/{action}/{id}",
                new { controller = "MaintenceRoster", action = "Index", id = UrlParameter.Optional }
            );

            context.MapRoute(
                "FRS25_default",
                "FRS25/{controller}/{action}/{id}",
                new { action = "Index", id = UrlParameter.Optional }
            );
        }
    }
}