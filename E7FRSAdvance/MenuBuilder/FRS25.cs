using E7FRSAdvance.MenuBuilder;
using E7FRSAdvance.Utility;
using System.Collections.Generic;
using static E7FRSAdvance.MenuBuilder.MenuStaticData;

namespace E7FRSAdvance.MenuBuilder
{
    public class FRS25 : IMenuBuilder
    {
        public static int GetUserClassTypeId(int userClassId)
        {
            if (userClassId == (int)E7FRSAdvance.Utility.Utility.UserClass.ESM
             || userClassId == (int)E7FRSAdvance.Utility.Utility.UserClass.SSECSI
             || userClassId == (int)E7FRSAdvance.Utility.Utility.UserClass.JE)
                return (int)E7FRSAdvance.Utility.Utility.UserClassType.StationLevelUser;

            if (userClassId == (int)E7FRSAdvance.Utility.Utility.UserClass.ASTEDSTE
             || userClassId == (int)E7FRSAdvance.Utility.Utility.UserClass.SRDSTECONTROL)
                return (int)E7FRSAdvance.Utility.Utility.UserClassType.DivisionLevelUser;

            if (userClassId == (int)E7FRSAdvance.Utility.Utility.UserClass.ENERGY7CONTROL)
                return (int)E7FRSAdvance.Utility.Utility.UserClassType.VendorUser;

            if (userClassId == (int)E7FRSAdvance.Utility.Utility.UserClass.Guest)
                return (int)E7FRSAdvance.Utility.Utility.UserClassType.GuestUser;

            if (userClassId == (int)E7FRSAdvance.Utility.Utility.UserClass.OtherOfficer)
                return (int)E7FRSAdvance.Utility.Utility.UserClassType.HeadquarterLevelUser;

            return 0;
        }
        public List<Menu> Build(Domain.User loginUser)
        {
            int userId = (int)ClsHttpContent.LoginUser.UserClassId;
            int roleId = ClsHttpContent.LoginUser.RoleId;

            // ── Role flags ──────────────────────────────────────────────────────────
            bool isAdmin = roleId == (int)E7FRSAdvance.Utility.Utility.Role.Admin
                        || roleId == (int)E7FRSAdvance.Utility.Utility.Role.ZonalAdmin
                        || roleId == (int)E7FRSAdvance.Utility.Utility.Role.DivisionalAdmin;

            // ── UserClass flags ─────────────────────────────────────────────────────
            bool isJE = userId == (int)E7FRSAdvance.Utility.Utility.UserClass.JE;
            bool isESM = userId == (int)E7FRSAdvance.Utility.Utility.UserClass.ESM;
            bool isSSE = userId == (int)E7FRSAdvance.Utility.Utility.UserClass.SSECSI;
            bool isCSI = userId == (int)E7FRSAdvance.Utility.Utility.UserClass.SSECSI;
            bool isDSTE = userId == (int)E7FRSAdvance.Utility.Utility.UserClass.ASTEDSTE;
            bool isADSTE = userId == (int)E7FRSAdvance.Utility.Utility.UserClass.ASTEDSTE;
            bool isGuest = userId == (int)E7FRSAdvance.Utility.Utility.UserClass.Guest;
            bool isVendor = userId == (int)E7FRSAdvance.Utility.Utility.UserClass.ENERGY7CONTROL;

            // ── Menu-level access rules ─────────────────────────────────────────────
            // Admin menu + User Management: only visible to admin role users
            // JE, ESM, Guest, Vendor → never; SSE/CSI/DSTE/ADSTE → only if admin role
            bool isRestrictedBase = isJE || isESM || isGuest || isVendor
                                 || ((isSSE || isCSI) && !isAdmin)
                                 || ((isDSTE || isADSTE) && !isAdmin);

            bool canSeeAdminMenu = isAdmin && !isRestrictedBase;
            //bool canSeeUserMgmt = isAdmin && !isRestrictedBase;

            int userClassTypeId = GetUserClassTypeId(userId);

            bool isStationLevel = userClassTypeId == (int)E7FRSAdvance.Utility.Utility.UserClassType.StationLevelUser;
            bool isDivisionLevel = userClassTypeId == (int)E7FRSAdvance.Utility.Utility.UserClassType.DivisionLevelUser;
            bool isHeadquarter = userClassTypeId == (int)E7FRSAdvance.Utility.Utility.UserClassType.HeadquarterLevelUser;
            bool isGuestType = userClassTypeId == (int)E7FRSAdvance.Utility.Utility.UserClassType.GuestUser;
            bool isVendorType = userClassTypeId == (int)E7FRSAdvance.Utility.Utility.UserClassType.VendorUser;

            bool canSeeUserMgmt = isHeadquarter || isDivisionLevel || isVendorType;
            // ── Build menus ─────────────────────────────────────────────────────────
            List<Menu> menus = new List<Menu>();

            // Dashboard
            Menu home = new Menu();
            home.Main = home.SetMenuAttribute("Dashboard", ControllerName.Home, ActionName.Index, FRSIconName.Dashbord, "FRS25");
            menus.Add(home);

            // Alerts
            //Menu alerts = new Menu();
            //alerts.Main = alerts.SetMenuAttribute("Alerts", "#", "#", FRSIconName.Alert);
            //alerts.Childs.Add(alerts.SetMenuAttribute("Alert Live", ControllerName.AlertLive, ActionName.Index, IconName.Dashboard, "FRS25"));
            //alerts.Childs.Add(alerts.SetMenuAttribute("Alert History", ControllerName.AlertDetailReport, ActionName.Index, IconName.Dashboard, "FRS25"));
            //alerts.Childs.Add(alerts.SetMenuAttribute("Alert Summary", ControllerName.AlertSummaryReport, ActionName.Index, IconName.Dashboard, "FRS25"));
            //alerts.Childs.Add(alerts.SetMenuAttribute("Maintenance Mode", ControllerName.MaintenanceMode, ActionName.Index, IconName.Dashboard, "FRS25"));
            //menus.Add(alerts);

            Menu alerts = new Menu();
            alerts.Main = alerts.SetMenuAttribute("Alerts", ControllerName.Alerts, ActionName.Index, FRSIconName.Alerts, "FRS25");
            menus.Add(alerts);

            Menu usersite = new Menu();
            usersite.Main = usersite.SetMenuAttribute("User Sites", ControllerName.Usersite, ActionName.Index, IconName.Usersite);
            menus.Add(usersite);

            // Telemetry
            Menu telemetry = new Menu();
            telemetry.Main = telemetry.SetMenuAttribute("Telemetry", "#", "#", FRSIconName.Telemetry);
            telemetry.Childs.Add(telemetry.SetMenuAttribute("Telemetry Live", ControllerName.Telemetry, ActionName.Index, IconName.Dashboard, "FRS25"));
            telemetry.Childs.Add(telemetry.SetMenuAttribute("Telemetry History", ControllerName.TelemetryHistory, ActionName.Index, IconName.Dashboard, "FRS25"));
            menus.Add(telemetry);

            Menu sip = new Menu();
            sip.Main = sip.SetMenuAttribute("SIP Creation", ControllerName.Sip, ActionName.Index, FRSIconName.Sip, "FRS25");
            menus.Add(sip);

            // RDPMS Health
            Menu rdpmsHealth = new Menu();
            rdpmsHealth.Main = rdpmsHealth.SetMenuAttribute("RDPMS Health", "#", "#", FRSIconName.RDPMSHealth);
            rdpmsHealth.Childs.Add(rdpmsHealth.SetMenuAttribute("RDPMS Health Live", ControllerName.RDPMSHealthLive, ActionName.Index, IconName.Dashboard, "FRS25"));
            rdpmsHealth.Childs.Add(rdpmsHealth.SetMenuAttribute("RDPMS Health Summary", ControllerName.RDPMSHealthLive, ActionName.Detail, IconName.Dashboard, "FRS25"));
            menus.Add(rdpmsHealth);

            // Equipment Room
            Menu equipment = new Menu();
            equipment.Main = equipment.SetMenuAttribute("Equipment Room", "#", "#", FRSIconName.EquipmentRoom);
            equipment.Childs.Add(equipment.SetMenuAttribute("Equipment Room Live", ControllerName.EquipmentRoom, ActionName.Index, IconName.Dashboard, "FRS25"));
            equipment.Childs.Add(equipment.SetMenuAttribute("Equipment Room History", ControllerName.EquipmentRoom, ActionName.History, IconName.Dashboard, "FRS25"));
            menus.Add(equipment);

            // Asset
            Menu asset = new Menu();
            asset.Main = asset.SetMenuAttribute("Asset", "#", "#", IconName.Iot);
            asset.Childs.Add(asset.SetMenuAttribute("Asset Detail", ControllerName.AssetDetail, ActionName.Index, FRSIconName.Asset, "FRS25"));
            asset.Childs.Add(asset.SetMenuAttribute("Asset Utilization", ControllerName.AssetDetail, ActionName.Utilization, IconName.Dashboard, "FRS25"));
            menus.Add(asset);

            //Asset Type
            if (isVendor)
            {
                Menu assetType = new Menu();
                assetType.Main = assetType.SetMenuAttribute("Asset Types", ControllerName.AdvanceAssetType, ActionName.Index, FRSIconName.AdvanceAssetType, "FRS25");
                menus.Add(assetType);
            }

            // Performance
            Menu performance = new Menu();
            performance.Main = performance.SetMenuAttribute("Performance", ControllerName.Performance, ActionName.Index, FRSIconName.Performance, "FRS25");
            menus.Add(performance);

            // Admin (hidden for JE, ESM, Guest, Vendor and non-admin SSE/CSI/DSTE/ADSTE)
            if ((bool)loginUser.IsConfiguration)
            {
                Menu admin = new Menu();
                admin.Main = admin.SetMenuAttribute("Admin", "#", "#", FRSIconName.Admin);
                admin.Childs.Add(admin.SetMenuAttribute("Configuration", ControllerName.Configuration, ActionName.Index, FRSIconName.Configuration, "FRS25"));
                menus.Add(admin);
            }

            // User Profile (User Management hidden for non-admin users)
            Menu userProfile = new Menu();
            userProfile.Main = userProfile.SetMenuAttribute("User Profile", "#", "#", FRSIconName.UserMgmt);
            userProfile.Childs.Add(userProfile.SetMenuAttribute("About", ControllerName.About, ActionName.Index, FRSIconName.Dashbord, "FRS25"));
            if (canSeeUserMgmt)
                userProfile.Childs.Add(userProfile.SetMenuAttribute("User Management", ControllerName.UserManagement, ActionName.Index, FRSIconName.UserMgmt, "FRS25"));
            menus.Add(userProfile);

            return menus;
        }
    }
}