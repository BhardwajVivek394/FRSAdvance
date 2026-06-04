using E7FRSAdvance.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;

namespace E7FRSAdvance.Areas.FRS25.Filter
{
    public class Authenticate : System.Web.Mvc.ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            if (ClsHttpContent.LoginUser == null)
            {
                filterContext.Result = new RedirectToRouteResult(new RouteValueDictionary
                {
                    { "action", "Index" },
                    { "controller", "Login" },
                    {"area","FRS25" }
                });

                return;
            }
            else
            {
                //string actionName = filterContext.ActionDescriptor.ActionName.Trim(). ToLower();
                string controllerName = filterContext.ActionDescriptor.ControllerDescriptor.ControllerName.Trim().ToLower();

                int cnt = 0;
                foreach (var menu in ClsHttpContent.FRS25Menus)
                {
                    if (menu.Main.ControllerName.Trim().ToLower() == controllerName)
                        cnt = cnt + 1;

                    foreach (var subMenu in menu.Childs)
                    {
                        if (subMenu.ControllerName.Trim().ToLower() == controllerName)
                            cnt = cnt + 1;
                    }
                }

                if (cnt == 0)
                {
                    filterContext.Result = new RedirectToRouteResult(new RouteValueDictionary
                    {
                        { "action", "UnAuthorized" },
                        { "controller", "Message" },
                        { "returnUrl", ""}
                    });
                    return;
                }
            }

            base.OnActionExecuting(filterContext);
        }
    }
}