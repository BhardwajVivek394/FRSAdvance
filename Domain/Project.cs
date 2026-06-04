using System.Collections.Generic;

namespace Domain
{
    public class Project
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string ContractNumber { get; set; }
        public int CreatedBy { get; set; }
        public System.DateTime CreatedDate { get; set; }
        public int LastModifiedBy { get; set; }
        public List<ProjectDivision> ProjectDivisions { get; set; } = new List<ProjectDivision>();

    }

    public class ProjectDivision
    {
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public int DivisionId { get; set; }

        public List<Domain.ProjectSite> ProjectSites { get; set; } = new List<ProjectSite>();
    }

    public class ProjectSite
    {
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public int SiteId { get; set; }
    }
}
