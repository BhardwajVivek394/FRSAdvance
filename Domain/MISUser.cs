using System;
using System.Collections.Generic;

namespace Domain
{
    public class MISUser
    {
        public int Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string EmailAddress { get; set; }
        public string PhoneNumber { get; set; }
        public bool IsActive { get; set; }
        public string Password { get; set; }
        public int RoleId { get; set; }
        public int? UserClassId { get; set; }
        public DateTime? ActivationDate { get; set; }
        public bool? IsUserEnterGPS { get; set; }
        public string ImageScan { get; set; }
        public string Designation { get; set; }
        public bool? IsSiteKeeping { get; set; }
        public DateTime CreatedDate { get; set; }
        public int CreatedBy { get; set; }
        public int LastModifiedBy { get; set; }
        public DateTime LastModifiedDate { get; set; }
        public int? DeletedBy { get; set; }
        public DateTime? DeletedDate { get; set; }

        //extra
        public string RoleName { get; set; }
        public string Title { get; set; }
        public string Token { get; set; }
        public List<SMSLog> mSMSLogs { get; set; }
        public string ColumnName { get; set; }
        public string SortDirection { get; set; }
        public bool IsBlockIp { get; set; }
        public bool IsMacIdApproved { get; set; }
        public bool IsGPSLocation { get; set; }
        public bool IsEmployee { get; set; }
        public bool? IsUploadSitePDF { get; set; }
    }
}
