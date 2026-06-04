using System.Collections.Generic;

namespace Domain
{
    public class Cluster
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public string Name { get; set; }
        public int CurrentCard { get; set; }
        public int V5 { get; set; }
        public int V82 { get; set; }
        public int DI { get; set; }
        public int MintCard { get; set; }
        public int CreatedBy { get; set; }
        public int? PowerSupplyId { get; set; }
        public string ModemId { get; set; }
        public string ModemName { get; set; }
        public string SimNumber { get; set; }
        public string ConnectorStatus { get; set; }
        public string IP { get; set; }
        public string Password { get; set; }

        //Extra
        public int? ModemVersion { get; set; }
        public string AttributeName { get; set; }
        public string CardName { get; set; }
        public string ADC { get; set; }
        public int? Pin { get; set; }
        public int RFClusterId { get; set; }

        public List<int> CurrentCardNumbers { get; set; } = new List<int>();
        public List<int> V5Numbers { get; set; } = new List<int>();
        public List<int> V82Numbers { get; set; } = new List<int>();
        public List<int> MintCardNumbers { get; set; } = new List<int>();
        public List<int> DINumbers { get; set; } = new List<int>();
        public List<ADC> ADCs { get; set; } = new List<ADC>();
        public Cluster()
        {
            CurrentCardNumbers = new List<int>();
            V5Numbers = new List<int>();
            V82Numbers = new List<int>();
            DINumbers = new List<int>();
        }
    }
}
