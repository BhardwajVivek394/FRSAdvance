using System;
using System.Collections.Generic;

namespace Domain
{
    public class Certificate
    {
        public int Id { get; set; }
        public string DeviceId { get; set; }
        public string CAStore { get; set; }
        public string CertPem { get; set; }
        public string CertKeyPem { get; set; }
        public DateTime NotBefore { get; set; }
        public DateTime NotAfter { get; set; }
        public int Status { get; set; }
        public int DownloadCount { get; set; }
        public int GeneratedBy { get; set; }
        public System.DateTime GeneratedAt { get; set; }
        public int? ReplacedByCertId { get; set; }
        public int SiteId { get; set; }
        public int? ClusterId { get; set; }

        //Extra
        public string GeneratedName { get; set; }

        public string ColumnName { get; set; }
        public string SortDirection { get; set; }
    }

    public class CertificateAudit
    {
        public int Id { get; set; }
        public int CertificateId { get; set; }
        public int DownloadBy { get; set; }
        public int? RevokeBy { get; set; }
        public System.DateTime DownloadAt { get; set; }
        public string DownloadName { get; set; }

    }

    public class CertificateLister
    {
        public CertificateLister()
        {
            Certificates = new List<Certificate>();
            SearchCriteria = new Certificate();
            Pager = new Pager();
        }
        public List<Certificate> Certificates { get; set; }
        public Certificate SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}
