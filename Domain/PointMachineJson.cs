using Newtonsoft.Json;
using System.Collections.Generic;

namespace Domain
{
    public class PointMachineJson
    {
        public List<string> A_C { get; set; } = new List<string>();
        public List<string> A_V { get; set; } = new List<string>();
        public List<string> B_C { get; set; } = new List<string>();
        public List<string> B_V { get; set; } = new List<string>();
        public double A_C_AVERAGE { get; set; }
        public int A_C_TIME { get; set; }
        public string A_C_MAX { get; set; }
        public double A_C_CurveArea { get; set; }
        public double B_C_AVERAGE { get; set; }
        public int B_C_TIME { get; set; }
        public string B_C_MAX { get; set; }
        public double B_C_CurveArea { get; set; }
        public double A_V_AVERAGE { get; set; }
        public int A_V_TIME { get; set; }
        public string A_V_MAX { get; set; }
        public double B_V_AVERAGE { get; set; }
        public int B_V_TIME { get; set; }
        public string B_V_MAX { get; set; }

        public double Combine_C_AVERAGE { get; set; }
        public int Combine_C_TIME { get; set; }
        public string Combine_C_MAX { get; set; }
        public double Combine_V_AVERAGE { get; set; }
        public int Combine_V_TIME { get; set; }
        public string Combine_V_MAX { get; set; }
        public string ClusterA { get; set; }
        public string ClusterB { get; set; }
        public string DeviationA { get; set; }
        public string DeviationB { get; set; }

        public double Combine_A_C_AVERAGE { get; set; }
        public int Combine_A_C_TIME { get; set; }
        public string Combine_A_C_MAX { get; set; }
        public double Combine_A_V_AVERAGE { get; set; }
        public int Combine_A_V_TIME { get; set; }
        public string Combine_A_V_MAX { get; set; }

        public double Combine_B_C_AVERAGE { get; set; }
        public int Combine_B_C_TIME { get; set; }
        public string Combine_B_C_MAX { get; set; }
        public double Combine_B_V_AVERAGE { get; set; }
        public int Combine_B_V_TIME { get; set; }
        public string Combine_B_V_MAX { get; set; }

        public bool IsCombineOperation_A { get; set; }
        public bool IsCombineOperation_B { get; set; }

        public int? A_CTypeId { get; set; }
        public int? B_CTypeId { get; set; }
        public int? A_VTypeId { get; set; }
        public int? B_VTypeId { get; set; }

    }
    public class PointMachineNormal
    {
        [JsonProperty("1")]
        public PointMachineJson pointMachineJson { get; set; }
    }
    public class RootMain
    {
        [JsonProperty("1")]
        public PointMachineJson _1 { get; set; }
    }
}
