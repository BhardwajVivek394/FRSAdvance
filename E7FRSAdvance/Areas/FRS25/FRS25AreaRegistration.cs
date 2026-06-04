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
            context.MapRoute(
                "FRS25_default",
                "FRS25/{controller}/{action}/{id}",
                new { action = "Index", id = UrlParameter.Optional }
            );
        }
    }
}