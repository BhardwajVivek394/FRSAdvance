using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class ActualFailureRow
    {
        public int Id { get; set; }         // 0 = new insert, >0 = update existing
        public int RowNo { get; set; }
        public int SiteId { get; set; }
        public int AssetTypeId { get; set; }
        public int AssetId { get; set; }
        public string DateFrom { get; set; }    // "yyyy-MM-dd" from <input type="date">
        public string DateTo { get; set; }      // "yyyy-MM-dd"
        public string TimeFrom { get; set; }    // "HH:mm"     from <input type="time">
        public string TimeTo { get; set; }      // "HH:mm"
        public int AlertInfoId { get; set; }    // FK to AlertInfo table (cause code ID)
        public string CauseCode { get; set; }   // Display string only (e.g. "EC", "HC")
        public string Remark { get; set; }
    }

    public class SaveActualFailuresRequest
    {
        public List<ActualFailureRow> Failures { get; set; } = new List<ActualFailureRow>();
    }
}
