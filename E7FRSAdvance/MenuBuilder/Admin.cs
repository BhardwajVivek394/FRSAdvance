using Domain;
using System.Collections.Generic;
using static E7FRSAdvance.MenuBuilder.MenuStaticData;

namespace E7FRSAdvance.MenuBuilder
{
    public class Admin : IMenuBuilder
    {
        public List<Menu> Build(User loginUser)
        {
            List<Menu> menus = new List<Menu>();
            Menu user = new Menu();

            //Zone Menu
            Menu zone = new Menu();
            zone.Main = zone.SetMenuAttribute("Zone", ControllerName.Zone, ActionName.Index, IconName.Zone);
            menus.Add(zone);

            //Division Menu
            Menu division = new Menu();
            division.Main = division.SetMenuAttribute("Division", ControllerName.Division, ActionName.Index, IconName.Division);
            menus.Add(division);

            //Section Menu
            Menu section = new Menu();
            section.Main = section.SetMenuAttribute("Section", ControllerName.Section, ActionName.Index, IconName.Section);
            menus.Add(section);

            //Division Menu
            Menu divisionView = new Menu();
            divisionView.Main = divisionView.SetMenuAttribute("Division View", ControllerName.DivisionView, ActionName.Index, IconName.DivisionView);
            menus.Add(divisionView);

            //Division Menu
            Menu railwayBordDashbord = new Menu();
            railwayBordDashbord.Main = railwayBordDashbord.SetMenuAttribute("RDPMS Dashbord", ControllerName.RailwayBordDashbord, ActionName.Index, IconName.DivisionView);
            menus.Add(railwayBordDashbord);


            Menu testimonial = new Menu();
            testimonial.Main = testimonial.SetMenuAttribute("Testimonial", ControllerName.Testimonial, ActionName.Index, IconName.Testimonial);
            menus.Add(testimonial);

            // TrainMomentDetail
            Menu TrainMomentDetail = new Menu();
            TrainMomentDetail.Main = TrainMomentDetail.SetMenuAttribute("Train Movement Details", ControllerName.TrainMomentDetail, ActionName.Index, IconName.TrainMomentDetail);
            menus.Add(TrainMomentDetail);

            //Site Menu
            Menu site = new Menu();
            site.Main = site.SetMenuAttribute("Sites", ControllerName.Site, ActionName.Index, IconName.Site);
            menus.Add(site);

            //Usersite Menu
            Menu usersite = new Menu();
            usersite.Main = usersite.SetMenuAttribute("User Sites", ControllerName.Usersite, ActionName.Index, IconName.Usersite);
            menus.Add(usersite);

            //SMSLog Menu
            Menu smsLog = new Menu();
            smsLog.Main = smsLog.SetMenuAttribute("Reporting", "#", "#", IconName.Maintenance);
            //smsLog.Childs.Add(smsLog.SetMenuAttribute("Failure Alert - Old", ControllerName.SMSLog, ActionName.Index, IconName.SMSLog));
            smsLog.Childs.Add(smsLog.SetMenuAttribute("Maintenance Watchlist", ControllerName.Watchlist, ActionName.Index, IconName.Dashboard));
            smsLog.Childs.Add(smsLog.SetMenuAttribute("Maintenance Alert", ControllerName.SMSLog, ActionName.FailureAlert, IconName.SMSLog));
            smsLog.Childs.Add(smsLog.SetMenuAttribute("Alert List View", ControllerName.SMSLog, ActionName.FailureAlertList, IconName.Dashboard));
            smsLog.Childs.Add(smsLog.SetMenuAttribute("Predictive Alert", ControllerName.Probability, ActionName.Index, IconName.Dashboard));
            smsLog.Childs.Add(smsLog.SetMenuAttribute("Consolidated", ControllerName.ConsolidatedReport, ActionName.Index, IconName.Dashboard));
            smsLog.Childs.Add(smsLog.SetMenuAttribute("Maintenance Report", ControllerName.FRSReport, ActionName.Index, IconName.Dashboard));

            menus.Add(smsLog);

            //Users Menu
            Menu users = new Menu();
            users.Main = users.SetMenuAttribute("Users", ControllerName.User, ActionName.Index, IconName.Users);
            menus.Add(users);

            //Maintainer User
            //Menu maintainerUser = new Menu();
            //maintainerUser.Main = maintainerUser.SetMenuAttribute("Maintainer User", ControllerName.MaintainerUser, ActionName.Index, IconName.Users);
            //menus.Add(maintainerUser);

            //AssetType Menu
            Menu assetType = new Menu();
            assetType.Main = assetType.SetMenuAttribute("Asset Types", ControllerName.AssetType, ActionName.Index, IconName.AssetType);
            menus.Add(assetType);

            Menu mDataloggerAssetType = new Menu();
            mDataloggerAssetType.Main = mDataloggerAssetType.SetMenuAttribute("Data Logger", "#", "#", IconName.Datalogger);
            mDataloggerAssetType.Childs.Add(mDataloggerAssetType.SetMenuAttribute("Asset Types", ControllerName.DataloggerAssetType, ActionName.Index, IconName.Dashboard));
            mDataloggerAssetType.Childs.Add(mDataloggerAssetType.SetMenuAttribute("Upload", ControllerName.Datalogger, ActionName.Index, IconName.Dashboard));
            //mDataloggerAssetType.Childs.Add(mDataloggerAssetType.SetMenuAttribute("RDPMS Mapping", ControllerName.DataloggerMapping, ActionName.Index, IconName.Dashboard));
            menus.Add(mDataloggerAssetType);

            //AssetType Menu
            Menu userClass = new Menu();
            userClass.Main = userClass.SetMenuAttribute("User Class", ControllerName.UserClass, ActionName.Index, IconName.Users);
            menus.Add(userClass);

            //MyProfile Menu
            Menu myProfile = new Menu();
            myProfile.Main = myProfile.SetMenuAttribute("My Profile", ControllerName.User, ActionName.MyProfile, IconName.MyProfile);
            menus.Add(myProfile);

            //Change Password Menu
            Menu changePassword = new Menu();
            changePassword.Main = changePassword.SetMenuAttribute("Change Password", ControllerName.User, ActionName.ChangePassword, IconName.ChangePassword);
            menus.Add(changePassword);

            //Reporting Menu
            Menu reporting = new Menu();
            reporting.Main = reporting.SetMenuAttribute("Analytics", ControllerName.Reporting, ActionName.Index, IconName.Reporting);
            menus.Add(reporting);

            //Commissioning Document
            Menu commissioningDocument = new Menu();
            commissioningDocument.Main = commissioningDocument.SetMenuAttribute("Commissioning Document", ControllerName.CommissioningDocument, ActionName.Index, IconName.CommissioningDocument);
            menus.Add(commissioningDocument);

            Menu TCPLink = new Menu();
            TCPLink.Main = TCPLink.SetMenuAttribute("TCP Link", ControllerName.TCPLink, ActionName.Index, IconName.TCPLink);
            menus.Add(TCPLink);

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
            ProjectWorksheet.Childs.Add(ProjectWorksheet.SetMenuAttribute("Project", ControllerName.Project, ActionName.Index, IconName.Dashboard));
            menus.Add(ProjectWorksheet);



            Menu mDashBoard = new Menu();
            mDashBoard.Main = mDashBoard.SetMenuAttribute("Maintenace", "#", "#", IconName.Maintenance);
            mDashBoard.Childs.Add(mDashBoard.SetMenuAttribute("DashBoard", ControllerName.Maintenace, ActionName.Index, IconName.Dashboard));
            mDashBoard.Childs.Add(mDashBoard.SetMenuAttribute("A10 Status", ControllerName.A10Status, ActionName.Index, IconName.Dashboard));
            mDashBoard.Childs.Add(mDashBoard.SetMenuAttribute("Device Time", ControllerName.Maintenace, ActionName.DeviceTime, IconName.Dashboard));
            //mDashBoard.Childs.Add(mDashBoard.SetMenuAttribute("Import Data Log", ControllerName.Maintenace, ActionName.ImportDataLog, IconName.Dashboard));
            mDashBoard.Childs.Add(mDashBoard.SetMenuAttribute("PLC Config", ControllerName.Maintenace, ActionName.PLCConfig, IconName.Dashboard));
            mDashBoard.Childs.Add(mDashBoard.SetMenuAttribute("Default Value", ControllerName.Maintenace, ActionName.DefaultValue, IconName.Dashboard));
            mDashBoard.Childs.Add(mDashBoard.SetMenuAttribute("Yard Config", ControllerName.YardConfig, ActionName.Index, IconName.Dashboard));
            mDashBoard.Childs.Add(mDashBoard.SetMenuAttribute("Cluster Config", ControllerName.Maintenace, ActionName.ClusterConfig, IconName.Dashboard));
            mDashBoard.Childs.Add(mDashBoard.SetMenuAttribute("Site Keeping", ControllerName.SiteKeeping, ActionName.Index, IconName.Dashboard));
            mDashBoard.Childs.Add(mDashBoard.SetMenuAttribute("Vibration Config", ControllerName.VibrationConfig, ActionName.Index, IconName.Dashboard));
            mDashBoard.Childs.Add(mDashBoard.SetMenuAttribute("Index Config", ControllerName.Maintenace, ActionName.IndexConfig, IconName.Dashboard));
            mDashBoard.Childs.Add(mDashBoard.SetMenuAttribute("PDF", ControllerName.Maintenace, ActionName.PDF, IconName.Dashboard));
            mDashBoard.Childs.Add(mDashBoard.SetMenuAttribute("Modbus", ControllerName.Modbus, ActionName.Index, IconName.Dashboard));
            mDashBoard.Childs.Add(mDashBoard.SetMenuAttribute("Lux Config", ControllerName.LuxConfig, ActionName.Index, IconName.Dashboard));
            mDashBoard.Childs.Add(mDashBoard.SetMenuAttribute("Division Remark", ControllerName.DivisionRemark, ActionName.Index, IconName.Dashboard));
            mDashBoard.Childs.Add(mDashBoard.SetMenuAttribute("Point Exception", ControllerName.PointException, ActionName.Index, IconName.Dashboard));
            mDashBoard.Childs.Add(mDashBoard.SetMenuAttribute("Calibration Detail", ControllerName.CalibrationDetail, ActionName.Index, IconName.Dashboard));
            mDashBoard.Childs.Add(mDashBoard.SetMenuAttribute("Railway Observation Tally", ControllerName.SiteKeeping, ActionName.RailwayObservationTally, IconName.Dashboard));
            mDashBoard.Childs.Add(mDashBoard.SetMenuAttribute("Actual Time Monitor", ControllerName.ActualTimeMonitor, ActionName.Index, IconName.Dashboard));
            mDashBoard.Childs.Add(mDashBoard.SetMenuAttribute("User History", ControllerName.UserHistory, ActionName.Index, IconName.Dashboard));
            mDashBoard.Childs.Add(mDashBoard.SetMenuAttribute("InActive Asset & Alert", ControllerName.Asset, ActionName.Index, IconName.Dashboard));
            menus.Add(mDashBoard);

            //AI Section
            Menu mAISection = new Menu();
            mAISection.Main = mAISection.SetMenuAttribute("AI Section", "#", "#", IconName.AISection);
            mAISection.Childs.Add(mAISection.SetMenuAttribute("Remark", ControllerName.AIRemark, ActionName.Index, IconName.AISection));
            mAISection.Childs.Add(mAISection.SetMenuAttribute("Cluster Category", ControllerName.ClusterCategory, ActionName.Index, IconName.AISection));
            mAISection.Childs.Add(mAISection.SetMenuAttribute("Learning", ControllerName.AISection, ActionName.Learning, IconName.AISection));
            mAISection.Childs.Add(mAISection.SetMenuAttribute("Visualization", ControllerName.AIVisualization, ActionName.Index, IconName.AISection));
            mAISection.Childs.Add(mAISection.SetMenuAttribute("Predict", ControllerName.AISection, ActionName.Predict, IconName.AISection));
            mAISection.Childs.Add(mAISection.SetMenuAttribute("Benchmarking", ControllerName.BenchMarking, ActionName.Index, IconName.AISection));
            mAISection.Childs.Add(mAISection.SetMenuAttribute("ChatBot Remark", ControllerName.ChatBotRemark, ActionName.Index, IconName.AISection));
            mAISection.Childs.Add(mAISection.SetMenuAttribute("ChatBot False Answer", ControllerName.ChatBotFalseAnswer, ActionName.Index, IconName.AISection));
            mAISection.Childs.Add(mAISection.SetMenuAttribute("P/m Clustering", ControllerName.PointClustering, ActionName.Index, IconName.AISection));
            menus.Add(mAISection);


            //Mqqt Menu
            Menu mMQQT = new Menu();
            mMQQT.Main = reporting.SetMenuAttribute("MQTT Detail", ControllerName.User, ActionName.MQTT, IconName.Dashboard);
            menus.Add(mMQQT);

            //Mqqt Menu
            Menu profile = new Menu();
            profile.Main = reporting.SetMenuAttribute("Login Profile", ControllerName.User, ActionName.LoginProfile, IconName.Profile);
            menus.Add(profile);

            // Utility Menu
            Menu utility = new Menu();
            utility.Main = utility.SetMenuAttribute("Utility Log", ControllerName.UtilityLog, ActionName.Index, IconName.UtilityLog);
            menus.Add(utility);

            // My Daily
            Menu myDaily = new Menu();
            myDaily.Main = myDaily.SetMenuAttribute("My Daily", ControllerName.MyDaily, ActionName.Index, IconName.MyDaily);
            menus.Add(myDaily);

            // MaintenanceTabulationMenu
            Menu maintenanceTabulation = new Menu();
            maintenanceTabulation.Main = maintenanceTabulation.SetMenuAttribute("Maintenance Tabulation", ControllerName.MaintenanceTabulation, ActionName.Index, IconName.MaintenanceTabulation);
            menus.Add(maintenanceTabulation);

            Menu performance = new Menu();
            performance.Main = performance.SetMenuAttribute("Performance", ControllerName.Performance, ActionName.Index, IconName.Performance);
            menus.Add(performance);


            //ChatBot Menu
            Menu ChatBot = new Menu();
            ChatBot.Main = ChatBot.SetMenuAttribute("E7 Chatbot", "#", "#", IconName.ChatBot);
            ChatBot.Childs.Add(ChatBot.SetMenuAttribute("Chat", ControllerName.ChatBot, ActionName.Index, IconName.ChatBot));
            ChatBot.Childs.Add(ChatBot.SetMenuAttribute("Upload", ControllerName.ChatBot, ActionName.Upload, IconName.ChatBot));
            ChatBot.Childs.Add(ChatBot.SetMenuAttribute("Url", ControllerName.ChatBotUrl, ActionName.Index, IconName.ChatBot));
            menus.Add(ChatBot);

            //AnnexureA Menu
            Menu annexure = new Menu();
            annexure.Main = annexure.SetMenuAttribute("Annexure", "#", "#", IconName.Testimonial);
            annexure.Childs.Add(annexure.SetMenuAttribute("Annexure A", ControllerName.AnnexureA, ActionName.Index, IconName.ChatBot));
            annexure.Childs.Add(annexure.SetMenuAttribute("Annexure B", ControllerName.AnnexureB, ActionName.Index, IconName.ChatBot));
            annexure.Childs.Add(annexure.SetMenuAttribute("RDPMS Mapping", ControllerName.DataloggerMapping, ActionName.Index, IconName.ChatBot));
            annexure.Childs.Add(annexure.SetMenuAttribute("Certificate", ControllerName.Certificate, ActionName.Index, IconName.Certificate));
            menus.Add(annexure);



            //network Menu
            Menu network = new Menu();
            network.Main = network.SetMenuAttribute("Network Dashboard", ControllerName.NetworkDashboard, ActionName.Index, IconName.Performance);
            menus.Add(network);


            //Log out Menu
            Menu logout = new Menu();
            logout.Main = logout.SetMenuAttribute("Log out", ControllerName.Login, ActionName.Index, IconName.Logout);
            menus.Add(logout);



            return menus;
        }
    }
}