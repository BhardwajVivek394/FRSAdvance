using System.Collections.Generic;

namespace Domain
{
    public class JobCard
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public bool IsLock { get; set; }
        public int CreatedBy { get; set; }
        public System.DateTime CreatedDate { get; set; }

        //Extra
        public int DivisionId { get; set; }
        public string StationCode { get; set; }
        public string SiteName { get; set; }
        public string SiteKippingUser { get; set; }
        public string MaintainerUser { get; set; }
        public List<JobCardDescription> mJobCardDescriptions { get; set; } = new List<JobCardDescription>();
        public List<JobCardUser> mJobCardUsers { get; set; } = new List<JobCardUser>();
        public List<JobCardOverallTarget> mJobCardOverallTargets { get; set; } = new List<JobCardOverallTarget>();
        public List<JobCardStatus> mJobCardStatuses { get; set; } = new List<JobCardStatus>();
        public List<Domain.JobCardMaintainerUser> mJobCardMaintainerUsers { get; set; } = new List<JobCardMaintainerUser>();
        public List<Domain.JobCardMaterial> mJobCardMaterials { get; set; } = new List<Domain.JobCardMaterial>();
    }

    public class JobCardLister
    {
        public JobCardLister()
        {
            mJobCards = new List<JobCard>();
            SearchCriteria = new JobCard();
            Pager = new Pager();
        }
        public List<JobCard> mJobCards { get; set; }
        public JobCard SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}
