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
        // Controllers with no menu entry of their own, keyed to the menu controller that grants access.
        // MaintenceRoster only serves the Telemetry Live drawer (asset health / roster sheet), so Telemetry access covers it.
        // TrackLeakage likewise only serves the drawer's track shorting (leakage) report, and ChatBot its Ask AI.
        // The alias is a fallback: a menu entry for the controller itself still grants access.
        private static readonly Dictionary<string, string> MenuAccessAlias = new Dictionary<string, string>
        {
            { "maintenceroster", "telemetry" },
            { "trackleakage", "telemetry" },
            { "chatbot", "telemetry" }
        };

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
                string aliasName;
                if (!MenuAccessAlias.TryGetValue(controllerName, out aliasName))
                    aliasName = controllerName;

                int cnt = 0;
                foreach (var menu in ClsHttpContent.FRS25Menus)
                {
                    string mainName = menu.Main.ControllerName.Trim().ToLower();
                    if (mainName == controllerName || mainName == aliasName)
                        cnt = cnt + 1;

                    foreach (var subMenu in menu.Childs)
                    {
                        string subName = subMenu.ControllerName.Trim().ToLower();
                        if (subName == controllerName || subName == aliasName)
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