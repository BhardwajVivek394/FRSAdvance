using Domain;
using System;
using System.Collections.Generic;
using System.Web;

namespace E7FRSAdvance.Utility
{
    public class ClsHttpContent
    {
        public static User LoginUser
        {
            get
            {
                if (HttpContext.Current.Session["LoginUser"] != null)
                    return HttpContext.Current.Session["LoginUser"] as User;
                else
                    return null;
            }
            set
            {
                HttpContext.Current.Session["LoginUser"] = value;
            }
        }

        public static List<MenuBuilder.Menu> Menus
        {
            get
            {
                if (HttpContext.Current.Session["Menu"] != null)
                    return HttpContext.Current.Session["Menu"] as List<MenuBuilder.Menu>;
                else
                    return null;
            }
            set
            {
                HttpContext.Current.Session["Menu"] = value;
            }
        }


        // FRS25 Website Menus added by vivek
        public static List<MenuBuilder.Menu> FRS25Menus
        {
            get
            {
                if (HttpContext.Current.Session["FRS25Menu"] != null)
                    return HttpContext.Current.Session["FRS25Menu"] as List<MenuBuilder.Menu>;
                else
                    return null;
            }
            set
            {
                HttpContext.Current.Session["FRS25Menu"] = value;
            }
        }
        public static List<MenuBuilder.Menu> FRS25
        {
            get
            {
                if (HttpContext.Current.Session["FRS25"] != null)
                    return HttpContext.Current.Session["FRS25"] as List<MenuBuilder.Menu>;
                else
                    return null;
            }
            set
            {
                HttpContext.Current.Session["FRS25"] = value;
            }
        }
        // ✅ NEW: Helper method to get menus based on area added by vivek
        public static List<MenuBuilder.Menu> GetMenusForArea(string area)
        {
            if (!string.IsNullOrEmpty(area) && area.Equals("FRS25", StringComparison.OrdinalIgnoreCase))
            {
                return FRS25Menus;
            }
            return Menus;
        }

        public static int MakeMeLive
        {
            set
            {
                HttpContext.Current.Session["MakeMeLive"] = 1;
            }
        }
    }
}