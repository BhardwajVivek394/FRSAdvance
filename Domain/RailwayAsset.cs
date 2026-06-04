using Newtonsoft.Json;
using System.Collections.Generic;

namespace Domain
{
    public class RailwayAsset
    {
        public string status { get; set; }
        public string zc { get; set; }
        public string dc { get; set; }
        public string sc { get; set; }
        public int count { get; set; }
        public List<Info> info { get; set; }
    }

    public class Info
    {
        public string smms_asset_number { get; set; }
        public string smms_asset_code { get; set; }
        public List<AdditionalInfo> additional_info { get; set; }
    }

    public class AdditionalInfo
    {
        public string key { get; set; }
        public string value { get; set; }
    }


    public class Root
    {
        [JsonProperty("stgwi")]
        public string Stgwi { get; set; }

        [JsonProperty("vcc")]
        public string Vcc { get; set; }

        [JsonProperty("vgc")]
        public string Vgc { get; set; }

        [JsonProperty("sn")]
        public string Sn { get; set; }

        [JsonProperty("info")]
        public List<RailwayInfo> Info { get; set; }
    }

    public class RailwayInfo
    {
        [JsonProperty("asnc")]
        public string Asnc { get; set; }

        [JsonProperty("asni")]
        public string Asni { get; set; }

        [JsonProperty("astc")]
        public string Astc { get; set; }

        [JsonProperty("smms_asset_code")]
        public string SmmsAssetCode { get; set; }

        [JsonProperty("parameters")]
        public List<Parameter> Parameters { get; set; } = new List<Parameter>();

        [JsonIgnore]
        public int AssetTypeId { get; set; }
        [JsonIgnore]
        public int AssetId { get; set; }
        [JsonIgnore]
        public string AssetType { get; set; }
        [JsonIgnore]
        public string AssetName { get; set; }
    }

    public class Parameter
    {
        [JsonProperty("prid")]
        public string Prid { get; set; }

        [JsonProperty("prloc")]
        public string Prloc { get; set; }

        [JsonProperty("parameter_rep_code")]
        public string ParameterRepCode { get; set; }

        [JsonProperty("original_role")]
        public string OriginalRole { get; set; }

        [JsonProperty("a0_type_id")]
        public int A0TypeId { get; set; }

        [JsonProperty("address")]
        public int Address { get; set; }

        [JsonProperty("a10_pin")]
        public int A10Pin { get; set; }

        [JsonProperty("tcp")]
        public string Tcp { get; set; }

        [JsonProperty("type")]
        public string type { get; set; }
        [JsonProperty("channel")]
        public string channel { get; set; }
        [JsonProperty("astc")]
        public string astc { get; set; }


        [JsonIgnore]
        public int AttributeId { get; set; }
        [JsonIgnore]
        public string Attribute { get; set; }
    }

}
