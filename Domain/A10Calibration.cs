using Newtonsoft.Json;
using System.Collections.Generic;

namespace Domain
{
    public class A10Calibration
    {
        public string id { get; set; }

        [JsonProperty("params")]
        public string paramsvalue { get; set; }
        public string device_type_id { get; set; }
        public int[] registers { get; set; }
        //Extra
        public string A10Multiplication { get; set; }
    }

    public class CalibData
    {
        public List<A10Calibration> calib { get; set; }
    }
}
