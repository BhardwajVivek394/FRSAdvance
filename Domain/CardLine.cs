using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class CardLine
    {
        public int Id { get; set; }
        public int CardId { get; set; }
        public int Number { get; set; }
        public int? AssetTypeId { get; set; }
        public int? AssetId { get; set; }
        public int? AttributeId { get; set; }
        public int? ADCId { get; set; }
        public int? Pin { get; set; }
        public int CreatedBy { get; set; }
        public bool? ClusterValidation { get; set; }
        public bool? JointCalibration { get; set; }
        public string LocationName { get; set; }
        public string RowNumber { get; set; }
        public int? TerminalNumberPositive { get; set; }
        public int? TerminalNumberNegative { get; set; }
        public int? CurrentSequenceNumber { get; set; }

        //Extra
        public int? ModemVersion { get; set; }
        public string ModemId { get; set; }
        public string CardName { get; set; }
        public string ClusterName { get; set; }
        public string AssetTypeName { get; set; }
        public string AssetName { get; set; }
        public string AttributeName { get; set; }
        public string Role { get; set; }
        public int? SequenceNumber { get; set; }
        public int? ADCNumber { get; set; }
        public int? ADCTypeId { get; set; }
        public int? SiteId { get; set; }
        public int? ClusterId { get; set; }
        public int? ValueType { get; set; }
        public bool? IsDigital { get; set; }
        public decimal? Multiplication { get; set; }
        public List<ADC> mADCs { get; set; } = new List<ADC>();
        public List<Domain.AssetType> AssetTypes { get; set; } = new List<Domain.AssetType>();
        public List<Domain.Asset> Assets { get; set; } = new List<Domain.Asset>();
        public List<Domain.AssetAttribute> AssetAttributes { get; set; } = new List<Domain.AssetAttribute>();



        public bool ModemStatus { get; set; }    // TRUE = online, FALSE = offline

    }
}
