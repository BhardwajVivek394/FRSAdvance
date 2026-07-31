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

        // ──────────────────────────────────────────────
        //  ALL Zones  (used by 12 controllers)
        // ──────────────────────────────────────────────
        public static List<Zone> GetAllZones(IZoneService zoneService)
        {
            string key = "FRS25_AllZones" + ClsHttpContent.LoginUser.Id;
            var cached = _cache.Get(key) as List<Zone>;
            if (cached != null) return cached;

            var data = zoneService.GetAllZones() ?? new List<Zone>();
            _cache.Set(key, data, DateTimeOffset.UtcNow.AddSeconds(CacheTtlSeconds));
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
            _cache.Set(key, data, DateTimeOffset.UtcNow.AddSeconds(CacheTtlSeconds));
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
            _cache.Set(key, data, DateTimeOffset.UtcNow.AddSeconds(CacheTtlSeconds));
            return data;
        }

        // ──────────────────────────────────────────────
        //  Divisions by ZoneId  (AJAX cascade dropdown)
        // ──────────────────────────────────────────────
        public static List<Division> GetDivisionsByZoneId(IDivisionService divisionService, int zoneId)
        {
            string key = "FRS25_DivByZone_" + zoneId + ClsHttpContent.LoginUser.Id;
            var cached = _cache.Get(key) as List<Division>;
            if (cached != null) return cached;

            var data = divisionService.GetByZoneId(zoneId) ?? new List<Division>();
            _cache.Set(key, data, DateTimeOffset.UtcNow.AddSeconds(CacheTtlSeconds));
            return data;
        }

        // ──────────────────────────────────────────────
        //  Sites by DivisionId  (AJAX cascade dropdown)
        // ──────────────────────────────────────────────
        public static List<Site> GetSitesByDivisionId(ISiteService siteService, int divisionId)
        {
            string key = "FRS25_SiteByDiv_" + divisionId + ClsHttpContent.LoginUser.Id;
            var cached = _cache.Get(key) as List<Site>;
            if (cached != null) return cached;

            var data = siteService.GetBy(divisionId) ?? new List<Site>();
            _cache.Set(key, data, DateTimeOffset.UtcNow.AddSeconds(CacheTtlSeconds));
            return data;
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
    }
}