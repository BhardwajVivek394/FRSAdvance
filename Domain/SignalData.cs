using System;
using System.Collections.Generic;

namespace Domain
{
    public class SignalData
    {
        public string Date { get; set; }
       // public int AssetId { get; set; }
        public string AssetName { get; set; }
        public string RGV { get; set; }
        public string RGmA { get; set; }
        public string DGV { get; set; }
        public string DGmA { get; set; }
        public string HGV { get; set; }
        public string HGmA { get; set; }
        public string RGP { get; set; }
        public string DGP { get; set; }
        public string HGP { get; set; }
        public string AI_cluster_RG { get; set; }
        public string AI_cluster_DG { get; set; }
        public string AI_cluster_HG { get; set; }
        public string AI_status_RG { get; set; }
        public string AI_status_DG { get; set; }
        public string AI_status_HG { get; set; }
    }

    public class SignalDataLister : APIResponse
    {
        public SignalDataLister()
        {
            SignalDatas = new List<SignalData>();
            SearchCriteria = new SignalData();
            Pager = new Pager();
        }
        public List<SignalData> SignalDatas { get; set; }
        public SignalData SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}
