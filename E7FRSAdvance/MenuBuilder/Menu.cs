using System;
using System.Collections.Generic;

namespace E7FRSAdvance.MenuBuilder
{
    public class Menu
    {
        public MenuAttribute Main { get; set; }
        public List<MenuAttribute> Childs { get; set; }

        public Menu()
        {
            Childs = new List<MenuAttribute>();
        }

        public MenuAttribute SetMenuAttribute(string menuName, string controllerName, string actionName, string iconName, string area = null)
        {
            MenuAttribute ma = new MenuAttribute();
            ma.MenuName = menuName;
            ma.ControllerName = controllerName;
            ma.ActionName = actionName;
            ma.IconName = iconName;
            ma.Area = area;
            return ma;
        }

        internal MenuAttribute SetMenuAttribute(string v, string certificate1, string index, object certificate2)
        {
            throw new NotImplementedException();
        }
    }
}