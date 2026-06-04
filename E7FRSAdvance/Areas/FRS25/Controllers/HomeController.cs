using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Mvc;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    [E7FRSAdvance.Areas.FRS25.Filter.Authenticate]
    public class HomeController : Controller
    {
        // GET: FRS25/Home
        private readonly ISiteService _siteService;
        private readonly IZoneService _zoneService;
        private readonly IDivisionService _divisionService;
        private readonly IFRSAlertService _alertService;
        public HomeController(ISiteService siteService, IZoneService zoneService, IDivisionService divisionService, IFRSAlertService alertService)
        {
            _siteService = siteService;
            _zoneService = zoneService;
            _divisionService = divisionService;
            _alertService = alertService;
        }

        public ActionResult Index()
        {
            ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
            ViewBag.Zones = new SelectList(_zoneService.GetAllZones(), "Id", "Name");
            ViewBag.Divisions = new SelectList(_divisionService.GetAllDivisions(), "Id", "Name");
            ViewBag.FRSAssetType = GetFRSAssetType();


            return View();
        }

        public JsonResult GetAssetList(Domain.SearchCriteria mSearchCriteria)
        {
            var data = new object();
            try
            {
                mSearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
                mSearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;

                if (ClsHttpContent.LoginUser.IsSiteKeeping)
                    mSearchCriteria.RoleId = (int)E7FRSAdvance.Utility.Utility.Role.Admin;

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSearchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("RailwayBordDashbord/GetAssetCount"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;

                        data = new { Status = "success", Record = jsonString };

                    }
                }
            }
            catch (Exception)
            {
            }
            var jsonResult = Json(data, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;
        }

        public JsonResult GetWithOutAcknowledgementAlert(Domain.FRSAlertLister mFRSAlertLister)
        {
            var mFRSAlerts = new List<Domain.FRSAlert>();
            mFRSAlertLister.SearchCriteria.AlertStatus = (int)E7FRSAdvance.Utility.Utility.AlertStatus.Active;
            mFRSAlertLister = _alertService.GetWithOutAcknowledgementAlert(mFRSAlertLister);

            if (mFRSAlertLister != null && mFRSAlertLister.mFRSAlerts.Count > 0)
            {
                mFRSAlerts = mFRSAlertLister.mFRSAlerts.GroupBy(x => x.AssetId).Select(g => g.First()).ToList();
            }

            var jsonResult = Json(mFRSAlerts, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;
        }

        //public JsonResult GetDivisionByZoneId(int zoneId)
        //{
        //    return Json(_divisionService.GetByZoneId(zoneId), JsonRequestBehavior.AllowGet);
        //}
        public JsonResult GetDivisionByZoneId(DivisionLister mDivisionLister)
        {
            List<Division> mDivisions = new List<Division>();
            if (mDivisionLister.SearchCriteria.ZoneIds != null && mDivisionLister.SearchCriteria.ZoneIds.Count() > 0)
                mDivisions = _divisionService.GetDivisionList(mDivisionLister);
            else
                mDivisions = new List<Division>();

            return Json(mDivisions, JsonRequestBehavior.AllowGet);
        }

        //public JsonResult GetSiteByDivisionId(int divisionId)
        //{
        //    var mSites = new List<Domain.Site>();
        //    try
        //    {
        //        mSites = _siteService.GetBy(divisionId);
        //    }
        //    catch (Exception)
        //    {
        //        mSites = new List<Domain.Site>();
        //    }
        //    return Json(mSites, JsonRequestBehavior.AllowGet);
        //}
        public JsonResult GetSiteByDivisionId(SiteLister mSiteLister)
        {
            var mSites = new List<Domain.Site>();
            try
            {
                if (mSiteLister.SearchCriteria.DivisionIds != null && mSiteLister.SearchCriteria.DivisionIds.Count() > 0)
                    mSites = _siteService.GetSiteLister(mSiteLister);
                else
                    mSites = new List<Domain.Site>();
            }
            catch (Exception)
            {
                mSites = new List<Domain.Site>();
            }
            return Json(mSites, JsonRequestBehavior.AllowGet);
        }

        private List<Domain.AssetType> GetFRSAssetType()
        {

            return (from E7FRSAdvance.Utility.Utility.FRSAssetType e in Enum.GetValues(typeof(E7FRSAdvance.Utility.Utility.FRSAssetType))
                    select new Domain.AssetType
                    {
                        Id = (int)e,
                        Name = e.ToString().Replace("_", " ")
                    }).ToList();
        }

        public JsonResult MakeSessionLive()
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync("Login/CheckToken").Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return Json(new { Result = "Success", Message = "Live" }, JsonRequestBehavior.AllowGet);
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        return Json(new { Result = "Forbidden", Message = "Expired" }, JsonRequestBehavior.AllowGet);
                    }
                    else
                    {
                        return Json(new { Result = "Error", Message = "Internal server error." }, JsonRequestBehavior.AllowGet);
                    }
                }
            }
            catch (Exception)
            {
                return Json(new { Result = "Error", Message = "Internal server error." }, JsonRequestBehavior.AllowGet);
            }
        }

        #region Advanced Dashboard

        /// <summary>
        /// Advanced Dashboard View - Modern UI with enhanced features
        /// </summary>
        public ActionResult IndexAdvanced()
        {
            try
            {
                // Load common dashboard data
                LoadDashboardViewBag();

                // Set advanced mode specific ViewBag properties
                ViewBag.IsAdvancedMode = true;
                ViewBag.ActiveMenu = "Dashboard";

                // Get theme preference
                string theme = GetUserPreferenceValue("theme");
                ViewBag.Theme = string.IsNullOrEmpty(theme) ? "dark" : theme;

                // Get user info for sidebar display
                if (ClsHttpContent.LoginUser != null)
                {
                    ViewBag.UserName = ClsHttpContent.LoginUser.FirstName ?? "User";
                    ViewBag.UserRole = ClsHttpContent.LoginUser.RoleName ?? "Administrator";
                    ViewBag.UserInitials = GetUserInitials(ViewBag.UserName);
                }
                else
                {
                    ViewBag.UserName = "User";
                    ViewBag.UserRole = "Administrator";
                    ViewBag.UserInitials = "U";
                }

                return View("IndexAdvanced");
            }
            catch (Exception ex)
            {
                // Log exception if you have logging
                System.Diagnostics.Debug.WriteLine("Error in IndexAdvanced: " + ex.Message);

                // Fallback to basic dashboard
                return RedirectToAction("Index");
            }
        }

        #endregion

        #region User Preferences

        /// <summary>
        /// Save user preference (theme, advanced mode, sidebar state, etc.)
        /// </summary>
        [HttpPost]
        public JsonResult SaveUserPreference(UserPreferenceModel model)
        {
            try
            {
                if (model == null || string.IsNullOrEmpty(model.Key))
                {
                    return Json(new { success = false, message = "Invalid preference data" });
                }

                string userId = null;
                if (ClsHttpContent.LoginUser != null)
                {
                    userId = ClsHttpContent.LoginUser.Id.ToString();
                }

                // Save to Session
                Session["UserPref_" + model.Key] = model.Value;

                // Save to Cookie for persistence across sessions
                // Using Response.Cookies directly (ASP.NET MVC way)
                string cookieName = "FRS25_" + model.Key;
                System.Web.HttpCookie cookie = new System.Web.HttpCookie(cookieName);
                cookie.Value = model.Value;
                cookie.Expires = DateTime.Now.AddYears(1);
                cookie.HttpOnly = true;
                cookie.Path = "/";

                // Remove existing cookie first if present
                if (Response.Cookies[cookieName] != null)
                {
                    Response.Cookies[cookieName].Expires = DateTime.Now.AddDays(-1);
                }
                Response.Cookies.Add(cookie);

                // TODO: Optionally save to database for cross-device persistence
                // SavePreferenceToDatabase(userId, model.Key, model.Value);

                return Json(new { success = true, message = "Preference saved successfully" });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error saving preference: " + ex.Message);
                return Json(new { success = false, message = ex.Message });
            }
        }


        /// <summary>
        /// Get user preference value
        /// </summary>
        [HttpGet]
        public JsonResult GetUserPreference(string key)
        {
            try
            {
                string value = GetUserPreferenceValue(key);
                return Json(new { success = true, value = value }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Check if advanced mode is enabled for current user
        /// </summary>
        [HttpGet]
        public JsonResult CheckAdvancedMode()
        {
            try
            {
                string value = GetUserPreferenceValue("advancedMode");
                bool isAdvanced = (value == "true");
                return Json(new { success = true, isAdvanced = isAdvanced }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, isAdvanced = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }


        /// <summary>
        /// Get all user preferences
        /// </summary>
        [HttpGet]
        public JsonResult GetAllPreferences()
        {
            try
            {
                var preferences = new Dictionary<string, string>();
                preferences["advancedMode"] = GetUserPreferenceValue("advancedMode") ?? "false";
                preferences["theme"] = GetUserPreferenceValue("theme") ?? "dark";
                preferences["sidebarCollapsed"] = GetUserPreferenceValue("sidebarCollapsed") ?? "false";

                return Json(new { success = true, preferences = preferences }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }


        #endregion

        /// <summary>
        /// Get user preference value from Session, Cookie, or Database
        /// C# 5 compatible - no null-conditional operators
        /// </summary>
        private string GetUserPreferenceValue(string key)
        {
            // 1. First check Session (fastest)
            if (Session != null)
            {
                object sessionValue = Session["UserPref_" + key];
                if (sessionValue != null)
                {
                    string value = sessionValue.ToString();
                    if (!string.IsNullOrEmpty(value))
                    {
                        return value;
                    }
                }
            }

            // 2. Then check Cookie (persists across sessions)
            if (Request != null && Request.Cookies != null)
            {
                System.Web.HttpCookie cookie = Request.Cookies["FRS25_" + key];
                if (cookie != null && !string.IsNullOrEmpty(cookie.Value))
                {
                    // Cache in session for faster subsequent access
                    if (Session != null)
                    {
                        Session["UserPref_" + key] = cookie.Value;
                    }
                    return cookie.Value;
                }
            }

            // 3. TODO: Check database (for cross-device persistence)
            // string userId = GetCurrentUserId();
            // if (!string.IsNullOrEmpty(userId))
            // {
            //     return GetPreferenceFromDatabase(userId, key);
            // }

            return null;
        }

        /// <summary>
        /// Get user initials for avatar display
        /// C# 5 compatible
        /// </summary>
        private string GetUserInitials(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "U";
            }

            string trimmedName = name.Trim();
            string[] parts = trimmedName.Split(' ');

            if (parts.Length >= 2)
            {
                char firstInitial = parts[0][0];
                char lastInitial = parts[parts.Length - 1][0];
                return (firstInitial.ToString() + lastInitial.ToString()).ToUpper();
            }

            if (trimmedName.Length >= 2)
            {
                return trimmedName.Substring(0, 2).ToUpper();
            }

            return trimmedName.ToUpper();
        }

        /// <summary>
        /// Get current user ID safely
        /// C# 5 compatible
        /// </summary>
        private string GetCurrentUserId()
        {
            if (ClsHttpContent.LoginUser != null)
            {
                return ClsHttpContent.LoginUser.Id.ToString();
            }
            return null;
        }
        private void LoadDashboardViewBag()
        {
            ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
            ViewBag.Zones = new SelectList(_zoneService.GetAllZones(), "Id", "Name");
            ViewBag.Divisions = new SelectList(_divisionService.GetAllDivisions(), "Id", "Name");
            ViewBag.FRSAssetType = GetFRSAssetType();
        }

        public RDPMSHealthLiveLister GetRDPMSHealthLive(Domain.SearchCriteria searchCriteria)
        {
            RDPMSHealthLiveLister mRDPMSHealthLiveLister = new RDPMSHealthLiveLister();
            try
            {

                mRDPMSHealthLiveLister.Pager.Take = -1;
                mRDPMSHealthLiveLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
                mRDPMSHealthLiveLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
                mRDPMSHealthLiveLister.SearchCriteria.ZoneIds = searchCriteria.ZoneIds;
                mRDPMSHealthLiveLister.SearchCriteria.DivisionIds = searchCriteria.DivisionIds;
                mRDPMSHealthLiveLister.SearchCriteria.SiteIds = searchCriteria.SiteIds;

                if (ClsHttpContent.LoginUser.RoleId != 1 || (mRDPMSHealthLiveLister.SearchCriteria.SiteIds != null && mRDPMSHealthLiveLister.SearchCriteria.SiteIds.Count > 0))
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mRDPMSHealthLiveLister);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("Asset/GetRDPMSHealthLiveDetails"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mRDPMSHealthLiveLister = JsonConvert.DeserializeObject<RDPMSHealthLiveLister>(jsonString);
                        }
                    }
                }

            }
            catch (Exception)
            {
            }
            finally
            {
            }

            return mRDPMSHealthLiveLister;
        }

        // Create a model class
        public class IOTCountResponse
        {
            public int TotalADC { get; set; }
            public int HelthyCount { get; set; }
            public int FailurCount { get; set; }
        }

        // Controller method
        [HttpPost]
        public JsonResult GetIOTCount(Domain.SearchCriteria searchCriteria)
        {
            try
            {
                var jsonResult = Json(GetRDPMSHealthLive(searchCriteria), JsonRequestBehavior.AllowGet);
                jsonResult.MaxJsonLength = Int32.MaxValue;
                return jsonResult;
            }
            catch (Exception ex)
            {
                var jsonResult = Json(GetRDPMSHealthLive(searchCriteria), JsonRequestBehavior.AllowGet);
                jsonResult.MaxJsonLength = Int32.MaxValue;
                return jsonResult;
            }
        }
        [HttpPost]
        public ActionResult GetAssetData(int ZoneId = 0, int DivisionId = 0, int SiteId = 0, string SearchDate = "")
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var requestData = new
                    {
                        ZoneId = ZoneId,
                        DivisionId = DivisionId,
                        SiteId = SiteId,
                        SearchDate = SearchDate
                    };

                    var jsonStr = JsonConvert.SerializeObject(requestData);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync("RailwayBordDashbord/GetAssetData", str).Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        return Content(jsonString, "application/json");
                    }
                }

                return Json(new { TotalAsset = 0, Predictive = 0, Fail = 0 }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { TotalAsset = 0, Predictive = 0, Fail = 0 }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        public JsonResult GetAlertDetails(Domain.FRSAlertLister mFRSAlertLister)
        {
            var mFRSAlerts = new List<Domain.FRSAlert>();
            mFRSAlertLister.SearchCriteria.AlertStatus = (int)E7FRSAdvance.Utility.Utility.AlertStatus.Active;
            mFRSAlertLister = _alertService.GetWithOutAcknowledgementAlert(mFRSAlertLister);

            if (mFRSAlertLister != null && mFRSAlertLister.mFRSAlerts.Count > 0)
            {
                mFRSAlerts = mFRSAlertLister.mFRSAlerts.GroupBy(x => x.AssetId).Select(g => g.First()).ToList();
            }

            var jsonResult = Json(mFRSAlerts, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = Int32.MaxValue;
            return jsonResult;
        }




        public class SystemDataResponse
        {
            public double CpuUsagePercent { get; set; }
            public RamInfo Ram { get; set; }
            public DiskInfo Disk { get; set; }
        }

        public class RamInfo
        {
            public double TotalMB { get; set; }
            public double UsedMB { get; set; }
            public double FreeMB { get; set; }
            public double UsagePercent { get; set; }
        }

        public class DiskInfo
        {
            public double TotalGB { get; set; }
            public double UsedGB { get; set; }
            public double FreeGB { get; set; }
            public double UsagePercent { get; set; }
        }

        [HttpPost]
        public ActionResult GetSystemData()
        {
            try
            {
                SystemDataResponse result = new SystemDataResponse
                {
                    CpuUsagePercent = GetCpuPercent(),
                    Ram = GetRamInfo(),
                    Disk = GetDiskInfo()
                };

                return Json(result, JsonRequestBehavior.AllowGet);
            }
            catch (Exception)
            {
                // Return zeros — UI must never crash because of this call
                return Json(new SystemDataResponse
                {
                    CpuUsagePercent = 0,
                    Ram = new RamInfo(),
                    Disk = new DiskInfo()
                }, JsonRequestBehavior.AllowGet);
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  CPU
        // ─────────────────────────────────────────────────────────────

        private static double GetCpuPercent()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return GetCpuWindows();

            return GetCpuLinux();
        }

        private static double GetCpuWindows()
        {
            // PerformanceCounter first NextValue() always returns 0.
            // Must read twice with a short sleep to get a real sample.
            PerformanceCounter counter = null;
            try
            {
                counter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
                counter.NextValue();     // discard — always 0 on first call
                Thread.Sleep(100);
                float pct = counter.NextValue();
                return Math.Round(Math.Min(100.0, Math.Max(0.0, (double)pct)), 1);
            }
            catch
            {
                return 0;
            }
            finally
            {
                if (counter != null)
                    counter.Dispose();
            }
        }

        private static double GetCpuLinux()
        {
            try
            {
                long[] s1 = ReadProcStatCpu();
                Thread.Sleep(200);
                long[] s2 = ReadProcStatCpu();

                long idle1 = s1[3];
                long idle2 = s2[3];
                long total1 = 0;
                long total2 = 0;

                for (int i = 0; i < s1.Length; i++) total1 += s1[i];
                for (int i = 0; i < s2.Length; i++) total2 += s2[i];

                long dIdle = idle2 - idle1;
                long dTotal = total2 - total1;

                if (dTotal == 0) return 0;

                return Math.Round((1.0 - (double)dIdle / dTotal) * 100.0, 1);
            }
            catch
            {
                return 0;
            }
        }

        // Reads the first "cpu " line from /proc/stat, returns 7 time fields.
        private static long[] ReadProcStatCpu()
        {
            string cpuLine = null;

            foreach (string line in System.IO.File.ReadLines("/proc/stat"))
            {
                if (line.StartsWith("cpu "))
                {
                    cpuLine = line;
                    break;
                }
            }

            long[] values = new long[7];

            if (cpuLine == null)
                return values;

            string[] parts = cpuLine.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            // parts[0] = "cpu", parts[1..7] = user nice system idle iowait irq softirq
            for (int i = 0; i < 7; i++)
            {
                long val;
                if ((i + 1) < parts.Length && long.TryParse(parts[i + 1], out val))
                    values[i] = val;
            }

            return values;
        }

        // ─────────────────────────────────────────────────────────────
        //  RAM
        // ─────────────────────────────────────────────────────────────

        private static RamInfo GetRamInfo()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return GetRamWindows();

            return GetRamLinux();
        }

        private static RamInfo GetRamWindows()
        {
            try
            {
                MEMORYSTATUSEX status = new MEMORYSTATUSEX();
                status.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                GlobalMemoryStatusEx(ref status);

                double totalMB = status.ullTotalPhys / 1024.0 / 1024.0;
                double freeMB = status.ullAvailPhys / 1024.0 / 1024.0;
                double usedMB = totalMB - freeMB;

                return new RamInfo
                {
                    TotalMB = Math.Round(totalMB, 1),
                    FreeMB = Math.Round(freeMB, 1),
                    UsedMB = Math.Round(usedMB, 1),
                    UsagePercent = totalMB > 0 ? Math.Round(usedMB / totalMB * 100.0, 1) : 0
                };
            }
            catch
            {
                return new RamInfo();
            }
        }

        private static RamInfo GetRamLinux()
        {
            try
            {
                string[] lines = System.IO.File.ReadAllLines("/proc/meminfo");

                long totalKb = ParseMemInfoKb(lines, "MemTotal:");
                long freeKb = ParseMemInfoKb(lines, "MemFree:");
                long buffersKb = ParseMemInfoKb(lines, "Buffers:");
                long cachedKb = ParseMemInfoKb(lines, "Cached:");
                long reclaimKb = ParseMemInfoKb(lines, "SReclaimable:");

                // Available = free + buffers + cached + slab-reclaimable
                long availableKb = freeKb + buffersKb + cachedKb + reclaimKb;
                long usedKb = totalKb - availableKb;

                double totalMB = totalKb / 1024.0;
                double usedMB = usedKb / 1024.0;
                double freeMB = availableKb / 1024.0;

                return new RamInfo
                {
                    TotalMB = Math.Round(totalMB, 1),
                    UsedMB = Math.Round(usedMB, 1),
                    FreeMB = Math.Round(freeMB, 1),
                    UsagePercent = totalMB > 0 ? Math.Round(usedMB / totalMB * 100.0, 1) : 0
                };
            }
            catch
            {
                return new RamInfo();
            }
        }

        // Finds a key like "MemTotal:" in /proc/meminfo lines, returns the kB value.
        private static long ParseMemInfoKb(string[] lines, string key)
        {
            foreach (string line in lines)
            {
                if (line.StartsWith(key))
                {
                    string[] parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    long result;
                    if (parts.Length >= 2 && long.TryParse(parts[1], out result))
                        return result;
                }
            }
            return 0;
        }

        // ─────────────────────────────────────────────────────────────
        //  DISK  (DriveInfo works on both Windows and Linux)
        // ─────────────────────────────────────────────────────────────

        private static DiskInfo GetDiskInfo()
        {
            try
            {
                string appPath = AppDomain.CurrentDomain.BaseDirectory;
                string appRoot = System.IO.Path.GetPathRoot(appPath);
                if (string.IsNullOrEmpty(appRoot))
                    appRoot = "C:\\";

                System.IO.DriveInfo targetDrive = null;

                foreach (System.IO.DriveInfo d in System.IO.DriveInfo.GetDrives())
                {
                    if (!d.IsReady || d.DriveType != System.IO.DriveType.Fixed)
                        continue;

                    if (d.RootDirectory.FullName.Equals(appRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        targetDrive = d;
                        break;
                    }

                    // Keep first fixed+ready drive as fallback
                    if (targetDrive == null)
                        targetDrive = d;
                }

                if (targetDrive == null)
                    return new DiskInfo();

                double totalGB = targetDrive.TotalSize / 1024.0 / 1024.0 / 1024.0;
                double freeGB = targetDrive.TotalFreeSpace / 1024.0 / 1024.0 / 1024.0;
                double usedGB = totalGB - freeGB;

                return new DiskInfo
                {
                    TotalGB = Math.Round(totalGB, 1),
                    UsedGB = Math.Round(usedGB, 1),
                    FreeGB = Math.Round(freeGB, 1),
                    UsagePercent = totalGB > 0 ? Math.Round(usedGB / totalGB * 100.0, 1) : 0
                };
            }
            catch
            {
                return new DiskInfo();
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Windows P/Invoke  —  GlobalMemoryStatusEx  (RAM)
        // ─────────────────────────────────────────────────────────────

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);



        /// <summary>
        /// Model for user preference save/load operations
        /// </summary>
        public class UserPreferenceModel
        {
            public string Key { get; set; }
            public string Value { get; set; }
        }
    }
}