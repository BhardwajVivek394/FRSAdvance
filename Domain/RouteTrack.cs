using System.Collections.Generic;

namespace Domain
{
    public class RouteTrack
    {
        public int Id { get; set; }
        public int RouteId { get; set; }
        public int AssetId { get; set; }
        public int SequenceNumber { get; set; }

        //extra
        public List<Asset> Assets { get; set; }
        public RouteTrack()
        {
            Assets = new List<Asset>();
        }
    }

    public class RouteTrackLister : APIResponse
    {
        public RouteTrackLister()
        {
            RouteTracks = new List<RouteTrack>();
            SearchCriteria = new RouteTrack();
            Pager = new Pager();
        }
        public List<RouteTrack> RouteTracks { get; set; }
        public RouteTrack SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}