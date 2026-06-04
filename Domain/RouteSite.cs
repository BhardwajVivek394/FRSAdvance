using System;
using System.Collections.Generic;

namespace Domain
{
    public class RouteSite : APIResponse
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public string RouteName { get; set; }
        public int CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }
        public int LastModifiedBy { get; set; }
        public DateTime LastModifiedDate { get; set; }
        public int? DeletedBy { get; set; }
        public DateTime? DeletedDate { get; set; }

        //extra
        public int AssetId { get; set; }
        public List<RouteTrack> RouteTracks { get; set; }
        public List<Asset> Assets { get; set; }
        public RouteSite()
        {
            RouteTracks = new List<RouteTrack>();
            Assets = new List<Asset>();
        }
    }

    public class RouteSiteLister : APIResponse
    {
        public RouteSiteLister()
        {
            RouteSites = new List<RouteSite>();
            SearchCriteria = new RouteSite();
            Pager = new Pager();
        }
        public List<RouteSite> RouteSites { get; set; }
        public RouteSite SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}