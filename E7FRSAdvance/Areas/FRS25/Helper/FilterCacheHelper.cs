using Domain;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Caching;
using System.Web.Mvc;

namespace E7FRSAdvance.Areas.FRS25.Helper
{
    public static class FilterCacheHelper
    {
        private static readonly ObjectCache _cache = MemoryCache.Default;
        private const int CacheTtlSeconds = 500;

        // 616 parity: don't cache an empty list. It usually means the API call failed (or
        // the user had no sites yet), and caching it would leave the Zone / Division /
        // Station dropdowns blank for the full TTL.
        private static void SetIfNotEmpty<T>(string key, List<T> data, DateTimeOffset expiry)
        {
            if (data != null && data.Count > 0)
                _cache.Set(key, data, expiry);
        }

        // ──────────────────────────────────────────────
        //  ALL Zones  (used by 12 controllers)
        // ──────────────────────────────────────────────
        public static List<Zone> GetAllZones(IZoneService zoneService)
        {
            string key = "FRS25_AllZones" + ClsHttpContent.LoginUser.Id;
            var cached = _cache.Get(key) as List<Zone>;
            if (cached != null) return cached;

            var data = zoneService.GetAllZones() ?? new List<Zone>();
            SetIfNotEmpty(key, data, DateTimeOffset.UtcNow.AddSeconds(CacheTtlSeconds));
            return data;
        }

        // ──────────────────────────────────────────────
        //  ALL Divisions  (used by 12 controllers)
        // ──────────────────────────────────────────────
        public static List<Division> GetAllDivisions(IDivisionService divisionService)
        {
            string key = "FRS25_AllDivisions" + ClsHttpContent.LoginUser.Id;
            var cached = _cache.Get(key) as List<Division>;
            if (cached != null) return cached;

            var data = divisionService.GetAllDivisions() ?? new List<Division>();
            SetIfNotEmpty(key, data, DateTimeOffset.UtcNow.AddSeconds(CacheTtlSeconds));
            return data;
        }

        // ──────────────────────────────────────────────
        //  ALL Sites  (used by 12 controllers)
        // ──────────────────────────────────────────────
        public static List<Site> GetAllSites(ISiteService siteService)
        {
            string key = "FRS25_AllSites" + ClsHttpContent.LoginUser.Id;
            var cached = _cache.Get(key) as List<Site>;
            if (cached != null) return cached;

            var data = siteService.GetAll() ?? new List<Site>();
            SetIfNotEmpty(key, data, DateTimeOffset.UtcNow.AddSeconds(CacheTtlSeconds));
            return data;
        }

        // ──────────────────────────────────────────────
        //  Divisions by ZoneId  (AJAX cascade dropdown)
        // ──────────────────────────────────────────────
        public static List<Division> GetDivisionsByZoneId(IDivisionService divisionService, int zoneId)
        {
            // "_" between the ids: zone 1 + user 23 and zone 12 + user 3 used to share "…123"
            string key = "FRS25_DivByZone_" + zoneId + "_" + ClsHttpContent.LoginUser.Id;
            var cached = _cache.Get(key) as List<Division>;
            if (cached != null) return cached;

            // Same binding as Home: only divisions holding a station assigned to the user
            var lister = new DivisionLister();
            lister.SearchCriteria.ZoneIds = new List<int> { zoneId };
            var data = divisionService.GetDivisionList(lister) ?? new List<Division>();
            SetIfNotEmpty(key, data, DateTimeOffset.UtcNow.AddSeconds(CacheTtlSeconds));
            return data;
        }

        // ──────────────────────────────────────────────
        //  Sites by DivisionId  (AJAX cascade dropdown)
        // ──────────────────────────────────────────────
        public static List<Site> GetSitesByDivisionId(ISiteService siteService, int divisionId)
        {
            // "_" between the ids: division 1 + user 23 and division 12 + user 3 used to share "…123"
            string key = "FRS25_SiteByDiv_" + divisionId + "_" + ClsHttpContent.LoginUser.Id;
            var cached = _cache.Get(key) as List<Site>;
            if (cached != null) return cached;

            var data = siteService.GetBy(divisionId) ?? new List<Site>();
            SetIfNotEmpty(key, data, DateTimeOffset.UtcNow.AddSeconds(CacheTtlSeconds));
            return data;
        }

        // ──────────────────────────────────────────────
        //  Sites for several divisions (multi-select Division → Station cascade).
        //  Same source as Telemetry Live (Site/GetSites/DivisionId + the user's
        //  Site/GetSiteByUserId list), merged, de-duplicated and sorted by name.
        //  Site/GetAllSiteLister was used here before and dropped some stations
        //  that are assigned to the user and shown everywhere else.
        //  NOT cached — 616 parity: 616's Division → Station call is live, so a site
        //  assigned a moment ago shows as soon as its Division is ticked.
        // ──────────────────────────────────────────────
        public static List<Site> GetSitesByDivisionIds(ISiteService siteService, IEnumerable<int> divisionIds)
        {
            var result = new List<Site>();
            if (divisionIds == null) return result;

            var seen = new HashSet<int>();
            foreach (var divisionId in divisionIds.Distinct())
            {
                var sites = siteService.GetBy(divisionId);
                if (sites == null) continue;
                foreach (var s in sites)
                {
                    if (s != null && seen.Add(s.Id))
                        result.Add(s);
                }
            }
            return result.OrderBy(s => s.Name).ToList();
        }

        // ──────────────────────────────────────────────
        //  FRS Asset Types  (enum-based, never changes at runtime)
        // ──────────────────────────────────────────────
        public static List<AssetType> GetFRSAssetTypes()
        {
            string key = "FRS25_FRSAssetTypes" + ClsHttpContent.LoginUser.Id;
            var cached = _cache.Get(key) as List<AssetType>;
            if (cached != null) return cached;

            var data = (from E7FRSAdvance.Utility.Utility.FRSAssetType e
                        in Enum.GetValues(typeof(E7FRSAdvance.Utility.Utility.FRSAssetType))
                        select new AssetType
                        {
                            Id = (int)e,
                            Name = ExtensionMethod.GetDisplayName(e)
                        }).ToList();

            // Enum never changes — cache for 1 hour
            _cache.Set(key, data, DateTimeOffset.UtcNow.AddHours(1));
            return data;
        }


        // ──────────────────────────────────────────────
        //  Asset Types  (enum-based, never changes at runtime)
        // ──────────────────────────────────────────────
        public static List<AssetType> GetAllAssetTypes(IAssetTypeService assetTypeService)
        {
            string key = "FRS25_AssetTypes" + ClsHttpContent.LoginUser.Id;
            var cached = _cache.Get(key) as List<AssetType>;
            if (cached != null) return cached;

            var data = assetTypeService.GetLister(new AssetTypeLister());
            _cache.Set(key, data, DateTimeOffset.UtcNow.AddSeconds(CacheTtlSeconds));
            return data;
        }

        // ──────────────────────────────────────────────
        //  Convenience: set all three ViewBag filters at once
        // ──────────────────────────────────────────────
        public static void SetFilterViewBag(
            dynamic viewBag,
            ISiteService siteService,
            IZoneService zoneService,
            IDivisionService divisionService,
            bool includeAssetType = true)
        {
            viewBag.Sites = new SelectList(GetAllSites(siteService), "Id", "Name");
            viewBag.Zones = new SelectList(GetAllZones(zoneService), "Id", "Name");
            viewBag.Divisions = new SelectList(GetAllDivisions(divisionService), "Id", "Name");
            if (includeAssetType)
                viewBag.FRSAssetType = GetFRSAssetTypes();
        }

        // ──────────────────────────────────────────────
        //  Clear ALL FRS25 filter cache entries for the CURRENT logged-in user
        // ──────────────────────────────────────────────
        public static void ClearCacheForCurrentUser()
        {
            ClearCacheByUserId(ClsHttpContent.LoginUser.Id.ToString());
        }

        // ──────────────────────────────────────────────
        //  Clear ALL FRS25 filter cache entries for a SPECIFIC user id
        // ──────────────────────────────────────────────
        public static void ClearCacheByUserId(string userId)
        {
            var keysToRemove = _cache
                .Where(kvp => kvp.Key.StartsWith("FRS25_") && kvp.Key.EndsWith(userId))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _cache.Remove(key);
            }
        }

        // ──────────────────────────────────────────────
        //  Clear cache entries matching a specific PREFIX (e.g. "FRS25_DivByZone_")
        //  across all users
        // ──────────────────────────────────────────────
        public static void ClearCacheByPrefix(string prefix)
        {
            var keysToRemove = _cache
                .Where(kvp => kvp.Key.StartsWith(prefix))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _cache.Remove(key);
            }
        }

        // ──────────────────────────────────────────────
        //  Telemetry GetUserAssetInfo cache (configured attributes, aliases, DataLoggers).
        //  Cleared after any save of asset attribute / DataLogger config so the Telemetry
        //  Live drawer and PM cards bind the new config at once. Never throws.
        // ──────────────────────────────────────────────
        public const string UserAssetInfoCachePrefix = "FRS25_UserAssetInfo_";
        public static void ClearUserAssetInfoCache()
        {
            try { ClearCacheByPrefix(UserAssetInfoCachePrefix); } catch { /* cache only */ }
        }

        // ──────────────────────────────────────────────
        //  Clear EVERYTHING under the "FRS25_" namespace, for ALL users
        //  (use with caution — wipes cache for every logged-in user)
        // ──────────────────────────────────────────────
        public static void ClearAllFRS25Cache()
        {
            var keysToRemove = _cache
                .Where(kvp => kvp.Key.StartsWith("FRS25_"))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _cache.Remove(key);
            }
        }
    }
}