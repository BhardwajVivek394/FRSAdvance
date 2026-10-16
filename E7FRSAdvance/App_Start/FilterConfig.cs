using System.Web.Mvc;

namespace E7FRSAdvance
{
    public class FilterConfig
    {
        public static void RegisterGlobalFilters(GlobalFilterCollection filters)
        {
            filters.Add(new HandleErrorAttribute());
            filters.Add(new E7FRSAdvance.Utility.AiModelConfigFilter());   // 1.0.190.0: current AI model from AiModelConfig/GetDefault
        }
    }
}