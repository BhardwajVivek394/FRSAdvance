using System.Collections.Generic;

namespace Domain
{
    public class AssetInfoDatalogger
    {
        public int Id { get; set; }
        public int AssetId { get; set; }
        public int DataloggerAttributeId { get; set; }
        public string Value { get; set; }
        public string ContactType { get; set; }
        public int CreatedBy { get; set; }
        public int? Type { get; set; }
        //Extra
        public string Role { get; set; }
        public string DataloggerAttribute { get; set; }
        public string DataloggerAssetName { get; set; }
        public string DLStatus { get; set; }
        public List<Domain.Datalogger> mDataloggers { get; set; } = new List<Datalogger>();
    }
}
