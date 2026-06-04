using System;
using System.Collections.Generic;

namespace Domain
{
    public class TrackData
    {
        public DateTime Date { get; set; }
        public string SiteName { get; set; }
        public int AssetId { get; set; }
        public string AssetName { get; set; }
        public double IfmA { get; set; }
        public double IrmA { get; set; }
        public double Vr { get; set; }
        public double ChokeV { get; set; }
        public double ChargermA { get; set; }
        public double TPRV { get; set; }
        public double Leakage { get; set; }
        public string Cluster { get; set; }
        public string OutlierCluster { get; set; }

        //Extra
        public List<string> SiteNames { get; set; }
        public List<string> AssetNames { get; set; }
        public List<string> Clusters { get; set; }
        public List<string> OutlierClusters { get; set; }
        public int SiteId { get; set; }
        public int Week { get; set; }
        public DateTime TimeStamp { get; set; }
    }
    public class TrackDataLister : APIResponse
    {
        public TrackDataLister()
        {
            TrackDatas = new List<TrackData>();
            SearchCriteria = new TrackData();
            Pager = new Pager();
        }
        public List<TrackData> TrackDatas { get; set; }
        public TrackData SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}
