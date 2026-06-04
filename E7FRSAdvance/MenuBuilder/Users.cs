using Domain;
using System.Collections.Generic;
using System.Linq;
using static E7FRSAdvance.MenuBuilder.MenuStaticData;

namespace E7FRSAdvance.MenuBuilder
{
    public class Users : IMenuBuilder
    {
        public List<Menu> Build(User loginUser)
        {
            List<Menu> menus = new List<Menu>();
            Menu user = new Menu();

            if (loginUser.IsSiteKeeping)
            {
                Menu testimonial = new Menu();
                testimonial.Main = testimonial.SetMenuAttribute("Testimonial", ControllerName.Testimonial, ActionName.Index, IconName.Testimonial);
                menus.Add(testimonial);
            }

            if (loginUser.IsSiteKeeping)
            {
                Menu divisionView = new Menu();
                divisionView.Main = divisionView.SetMenuAttribute("Division View", ControllerName.DivisionView, ActionName.Index, IconName.DivisionView);
                menus.Add(divisionView);
            }

            if (loginUser.IsSiteKeeping)
            {
                Menu railwayBordDashbord = new Menu();
                railwayBordDashbord.Main = railwayBordDashbord.SetMenuAttribute("RDPMS Dashbord", ControllerName.RailwayBordDashbord, ActionName.Index, IconName.DivisionView);
                menus.Add(railwayBordDashbord);

                Menu section = new Menu();
                section.Main = section.SetMenuAttribute("Section", ControllerName.Section, ActionName.Index, IconName.Section);
                menus.Add(section);
            }

            //Site Menu
            Menu site = new Menu();
            site.Main = site.SetMenuAttribute("Sites", ControllerName.Site, ActionName.Index, IconName.Site);
            menus.Add(site);

            //Site Menu
            Menu usersite = new Menu();
            usersite.Main = usersite.SetMenuAttribute("User Sites", ControllerName.Usersite, ActionName.Index, IconName.Site);
            menus.Add(usersite);



            ////SMSLog Menu
            //Menu smsLog = new Menu();
            //smsLog.Main = site.SetMenuAttribute("Alert Logs", ControllerName.SMSLog, ActionName.Index, IconName.SMSLog);
            //menus.Add(smsLog);
                       
            if (loginUser.IsSiteKeeping)
            {
                Menu smsLog = new Menu();
                smsLog.Main = smsLog.SetMenuAttribute("Reporting", "#", "#", IconName.Maintenance);
                smsLog.Childs.Add(smsLog.SetMenuAttribute("Maintenance Watchlist", ControllerName.Watchlist, ActionName.Index, IconName.Dashboard));
                smsLog.Childs.Add(smsLog.SetMenuAttribute("Maintenance Alert", ControllerName.SMSLog, ActionName.FailureAlert, IconName.SMSLog));
                smsLog.Childs.Add(smsLog.SetMenuAttribute("Alert List View", ControllerName.SMSLog, ActionName.FailureAlertList, IconName.Dashboard));
                smsLog.Childs.Add(smsLog.SetMenuAttribute("Predictive Alert", ControllerName.Probability, ActionName.Index, IconName.Dashboard));
                smsLog.Childs.Add(smsLog.SetMenuAttribute("Consolidated", ControllerName.ConsolidatedReport, ActionName.Index, IconName.Dashboard));
                smsLog.Childs.Add(smsLog.SetMenuAttribute("Maintenance Report", ControllerName.FRSReport, ActionName.Index, IconName.Dashboard));

                menus.Add(smsLog);
            }
            else
            {
                if (loginUser.UserSites != null && loginUser.UserSites.Where(x => x.IsFRSAlert != null && x.IsFRSAlert.Value).Count() > 0)
                {
                    Menu smsLog = new Menu();
                    smsLog.Main = smsLog.SetMenuAttribute("Maintenance Report", ControllerName.FRSReport, ActionName.Index, IconName.Maintenance);
                    menus.Add(smsLog);
                }

            }

            if (loginUser.IsSiteKeeping)
            {
                Menu mDataloggerAssetType = new Menu();
                mDataloggerAssetType.Main = mDataloggerAssetType.SetMenuAttribute("Data Logger", "#", "#", IconName.Datalogger);
                mDataloggerAssetType.Childs.Add(mDataloggerAssetType.SetMenuAttribute("Asset Types", ControllerName.DataloggerAssetType, ActionName.Index, IconName.Dashboard));
                mDataloggerAssetType.Childs.Add(mDataloggerAssetType.SetMenuAttribute("Upload", ControllerName.Datalogger, ActionName.Index, IconName.Dashboard));
                mDataloggerAssetType.Childs.Add(mDataloggerAssetType.SetMenuAttribute("RDPMS Mapping", ControllerName.DataloggerMapping, ActionName.Index, IconName.Dashboard));
                menus.Add(mDataloggerAssetType);
            }
            else
            {
                if (loginUser.UserSites != null && loginUser.UserSites.Where(x => x.IsFRSAlert != null && x.IsFRSAlert.Value).Count() > 0)
                {
                    Menu smsLog = new Menu();
                    smsLog.Main = smsLog.SetMenuAttribute("Data Logger", ControllerName.Datalogger, ActionName.Index, IconName.Datalogger);
                    menus.Add(smsLog);

                }
            }


            //MyProfile Menu
            Menu myProfile = new Menu();
            myProfile.Main = myProfile.SetMenuAttribute("My Profile", ControllerName.User, ActionName.MyProfile, IconName.MyProfile);
            menus.Add(myProfile);

            //Change Password Menu
            Menu changePassword = new Menu();
            changePassword.Main = changePassword.SetMenuAttribute("Change Password", ControllerName.User, ActionName.ChangePassword, IconName.ChangePassword);
            menus.Add(changePassword);

            if (loginUser.IsSiteKeeping)
            {
                //Reporting Menu
                Menu reporting = new Menu();
                reporting.Main = reporting.SetMenuAttribute("Analytics", ControllerName.Reporting, ActionName.Index, IconName.Reporting);
                menus.Add(reporting);
            }

            //Commissioning Document
            if (loginUser.IsSiteKeeping)
            {
                Menu commissioningDocument = new Menu();
                commissioningDocument.Main = commissioningDocument.SetMenuAttribute("Commissioning Document", ControllerName.CommissioningDocument, ActionName.Index, IconName.CommissioningDocument);
                menus.Add(commissioningDocument);
            }

            //TCP Link
            if (loginUser.IsSiteKeeping)
            {
                Menu TCPLink = new Menu();
                TCPLink.Main = TCPLink.SetMenuAttribute("TCP Link", ControllerName.TCPLink, ActionName.Index, IconName.TCPLink);
                menus.Add(TCPLink);
            }

            if (loginUser.IsSiteKeeping)
            {
                Menu StockOpration = new Menu();
                StockOpration.Main = StockOpration.SetMenuAttribute("Stock Opration", ControllerName.StockOpration, ActionName.Index, IconName.StockOpration);
                menus.Add(StockOpration);

                Menu MaintenaceOperation = new Menu();
                MaintenaceOperation.Main = MaintenaceOperation.SetMenuAttribute("Maintenace Operation", ControllerName.MaintenaceOperation, ActionName.Index, IconName.MaintenaceOperation);
                menus.Add(MaintenaceOperation);


                Menu ProjectWorksheet = new Menu();
                ProjectWorksheet.Main = ProjectWorksheet.SetMenuAttribute("Project Worksheet", "#", "#", IconName.Maintenance);
                ProjectWorksheet.Childs.Add(ProjectWorksheet.SetMenuAttribute("Material Catalog", ControllerName.WorksheetMaterial, ActionName.Index, IconName.Dashboard));
                ProjectWorksheet.Childs.Add(ProjectWorksheet.SetMenuAttribute("Store", ControllerName.WorksheetStore, ActionName.Index, IconName.Dashboard));
                menus.Add(ProjectWorksheet);
            }



            Menu mMaintenace = new Menu();
            mMaintenace.Main = mMaintenace.SetMenuAttribute("Maintenace", "#", "#", IconName.Maintenance);

            if (loginUser.IsSiteKeeping)
            {
                mMaintenace.Childs.Add(mMaintenace.SetMenuAttribute("DashBoard", ControllerName.Maintenace, ActionName.Index, IconName.Dashboard));
                mMaintenace.Childs.Add(mMaintenace.SetMenuAttribute("A10 Status", ControllerName.A10Status, ActionName.Index, IconName.Dashboard));
                mMaintenace.Childs.Add(mMaintenace.SetMenuAttribute("Device Time", ControllerName.Maintenace, ActionName.DeviceTime, IconName.Dashboard));
                //mDashBoard.Childs.Add(mDashBoard.SetMenuAttribute("Import Data Log", ControllerName.Maintenace, ActionName.ImportDataLog, IconName.Dashboard));
                mMaintenace.Childs.Add(mMaintenace.SetMenuAttribute("PLC Config", ControllerName.Maintenace, ActionName.PLCConfig, IconName.Dashboard));
                mMaintenace.Childs.Add(mMaintenace.SetMenuAttribute("Default Value", ControllerName.Maintenace, ActionName.DefaultValue, IconName.Dashboard));
                mMaintenace.Childs.Add(mMaintenace.SetMenuAttribute("Cluster Config", ControllerName.Maintenace, ActionName.ClusterConfig, IconName.Dashboard));
                mMaintenace.Childs.Add(mMaintenace.SetMenuAttribute("Site Keeping", ControllerName.SiteKeeping, ActionName.Index, IconName.Dashboard));
                mMaintenace.Childs.Add(mMaintenace.SetMenuAttribute("Vibration Config", ControllerName.VibrationConfig, ActionName.Index, IconName.Dashboard));
                mMaintenace.Childs.Add(mMaintenace.SetMenuAttribute("Index Config", ControllerName.Maintenace, ActionName.IndexConfig, IconName.Dashboard));
                mMaintenace.Childs.Add(mMaintenace.SetMenuAttribute("Modbus", ControllerName.Modbus, ActionName.Index, IconName.Dashboard));
                mMaintenace.Childs.Add(mMaintenace.SetMenuAttribute("Lux Config", ControllerName.LuxConfig, ActionName.Index, IconName.Dashboard));
                mMaintenace.Childs.Add(mMaintenace.SetMenuAttribute("Division Remark", ControllerName.DivisionRemark, ActionName.Index, IconName.Dashboard));
                mMaintenace.Childs.Add(mMaintenace.SetMenuAttribute("Point Exception", ControllerName.PointException, ActionName.Index, IconName.Dashboard));
                mMaintenace.Childs.Add(mMaintenace.SetMenuAttribute("User History", ControllerName.UserHistory, ActionName.Index, IconName.Dashboard));
                mMaintenace.Childs.Add(mMaintenace.SetMenuAttribute("InActive Asset & Alert", ControllerName.Asset, ActionName.Index, IconName.Dashboard));
            }
            mMaintenace.Childs.Add(mMaintenace.SetMenuAttribute("Yard Config", ControllerName.YardConfig, ActionName.Index, IconName.Dashboard));
            mMaintenace.Childs.Add(mMaintenace.SetMenuAttribute("PDF", ControllerName.Maintenace, ActionName.PDF, IconName.Dashboard));
            if (loginUser.IsSiteKeeping)
            {
                mMaintenace.Childs.Add(mMaintenace.SetMenuAttribute("Calibration Detail", ControllerName.CalibrationDetail, ActionName.Index, IconName.Dashboard));
            }
            mMaintenace.Childs.Add(mMaintenace.SetMenuAttribute("Railway Observation Tally", ControllerName.SiteKeeping, ActionName.RailwayObservationTally, IconName.Dashboard));
            menus.Add(mMaintenace);


            if (loginUser.IsSiteKeeping)
            {
                //AI Section
                Menu mAISection = new Menu();
                mAISection.Main = mAISection.SetMenuAttribute("AI Section", "#", "#", IconName.AISection);
                mAISection.Childs.Add(mAISection.SetMenuAttribute("Benchmarking", ControllerName.BenchMarking, ActionName.Index, IconName.AISection));
                mAISection.Childs.Add(mAISection.SetMenuAttribute("P/m Clustering", ControllerName.PointClustering, ActionName.Index, IconName.AISection));
                menus.Add(mAISection);
            }


            //Menu mMaintenace = new Menu();
            //mMaintenace.Main = mMaintenace.SetMenuAttribute("Maintenace", "#", "#", IconName.Dashboard);
            //mMaintenace.Childs.Add(mMaintenace.SetMenuAttribute("Yard Config", ControllerName.Maintenace, ActionName.YardConfig, IconName.Dashboard));
            //if (loginUser.IsSiteKeeping)
            //{
            //    mMaintenace.Childs.Add(mMaintenace.SetMenuAttribute("Site Keeping", ControllerName.SiteKeeping, ActionName.Index, IconName.Dashboard));

            //}
            //menus.Add(mMaintenace);


            // Utility Menu
            if (loginUser.IsSiteKeeping)
            {
                Menu utility = new Menu();
                utility.Main = utility.SetMenuAttribute("Utility Log", ControllerName.UtilityLog, ActionName.Index, IconName.UtilityLog);
                menus.Add(utility);
            }

            if (loginUser.IsSiteKeeping)
            {
                // MaintenanceTabulationMenu
                Menu maintenanceTabulation = new Menu();
                maintenanceTabulation.Main = maintenanceTabulation.SetMenuAttribute("Maintenance Tabulation", ControllerName.MaintenanceTabulation, ActionName.Index, IconName.MaintenanceTabulation);
                menus.Add(maintenanceTabulation);

                //network Menu
                Menu network = new Menu();
                network.Main = network.SetMenuAttribute("Network Dashboard", ControllerName.NetworkDashboard, ActionName.Index, IconName.Performance);
                menus.Add(network);
            }



            Menu learning = new Menu();
            learning.Main = learning.SetMenuAttribute("Learning", ControllerName.Learning, ActionName.Index, IconName.Learning);
            menus.Add(learning);

            Menu supportTicket = new Menu();
            supportTicket.Main = supportTicket.SetMenuAttribute("Support Ticket", ControllerName.SupportTicket, ActionName.Index, IconName.SupportTicket);
            menus.Add(supportTicket);



            //ChatBot Menu
            Menu ChatBot = new Menu();
            ChatBot.Main = ChatBot.SetMenuAttribute("E7 Chatbot", "#", "#", IconName.ChatBot);
            ChatBot.Childs.Add(ChatBot.SetMenuAttribute("Chat", ControllerName.ChatBot, ActionName.Index, IconName.ChatBot));
            ChatBot.Childs.Add(ChatBot.SetMenuAttribute("Upload", ControllerName.ChatBot, ActionName.Upload, IconName.ChatBot));
            menus.Add(ChatBot);


            //Blog Menu
            Menu blog = new Menu();
            blog.Main = blog.SetMenuAttribute("Blog", "#", "#", IconName.Blog);
            blog.Childs.Add(blog.SetMenuAttribute("All", ControllerName.Blog, ActionName.Index, IconName.Blog));
            blog.Childs.Add(blog.SetMenuAttribute("My", ControllerName.Blog, ActionName.Index, IconName.Blog));
            menus.Add(blog);


            //Log out Menu
            Menu logout = new Menu();
            logout.Main = logout.SetMenuAttribute("Log out", ControllerName.Login, ActionName.Index, IconName.Logout);
            menus.Add(logout);

            return menus;
        }
    }
}