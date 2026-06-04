using System.Collections.Generic;

namespace Domain
{
    public class ADC
    {
        public int Id { get; set; }
        public int ClusterId { get; set; }
        public int SequenceNumber { get; set; }
        public int ADCNumber { get; set; }
        public int ADCTypeId { get; set; }
        public int CreatedBy { get; set; }

        //Extra
        public int SiteId { get; set; }
        public string ClusterName { get; set; }
        public string SortStr { get; set; }
        
    }
}
