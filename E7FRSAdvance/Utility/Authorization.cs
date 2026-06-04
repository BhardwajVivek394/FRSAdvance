using E7FRSAdvance.Utility;
using System;
using System.Web.Mvc;
using System.Web.Routing;

namespace E7FRSAdvance.Utility
{
    public class Authorization : System.Web.Mvc.ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            if (ClsHttpContent.LoginUser == null)
            {
                filterContext.Result = new RedirectToRouteResult(new RouteValueDictionary
                {
                    { "action", "Index" },
                    { "controller", "Login" },
                     { "returnUrl",filterContext.HttpContext.Request.Url.GetComponents(UriComponents.PathAndQuery, UriFormat.SafeUnescaped)}
                });

                return;
            }
            base.OnActionExecuting(filterContext);
        }
    }
}