using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Dto
{
    public class StationDeviceVM
    {
        public int SiteId { get; set; }
        public string SiteName { get; set; }
        public string DivisionName { get; set; }
        public string ZoneName { get; set; }
        public string ZoneDescription { get; set; }
        public int A10Count { get; set; }

        public List<ModemVM> Modems { get; set; }
    }

    public class ModemVM
    {
        public string ModemId { get; set; }
        public string ModemName { get; set; }
        public string Version { get; set; }
        public bool IsOnline { get; set; }
        public int A10Count { get; set; }
        public decimal? SignalStrength { get; set; }
        public int ClusterId { get; set; }
        public string ClusterName { get; set; }
        public string SiteName { get; set; }
        public string DivisionName { get; set; }
        public string ZoneName { get; set; }
        public string MqttBasePath { get; set; }
        public bool TcpStatus { get; set; }
        public bool MqttStatus { get; set; }
        public string SimNumber { get; set; }
        public string TCPSendPort { get; set; }
        public string TCPReceivePort { get; set; }
        public string SubscribeTopic { get; set; }
        public string PublishTopic { get; set; }
        public List<A10VM> A10List { get; set; }

        public ModemVM()
        {
            A10List = new List<A10VM>();
        }
    }

    //public class A10VM
    //{
    //    public int Id { get; set; }
    //    public string ADCNumber { get; set; }
    //    public int ADCTypeId { get; set; }
    //    public int SequenceNumber { get; set; }
    //    public string ADCTypeName { get; set; }
    //}

    public class A10VM
    {
        public int? Id { get; set; }
        public string ADCNumber { get; set; }
        public int? ADCTypeId { get; set; }
        public string ADCTypeName { get; set; }
        public string SlaveAddress { get; set; }
        public string FirmwareVersion { get; set; }
        public int ChannelCount { get; set; }

        // Cluster info
        public int ClusterId { get; set; }
        public string ClusterName { get; set; }

        // Modem info
        public string ModemId { get; set; }
        public string ModemName { get; set; }

        // Site info
        public int? SiteId { get; set; }
        public string StationName { get; set; }
        public string DivisionName { get; set; }
        public string MQTTBasePath { get; set; }

        // Status
        public bool TcpStatus { get; set; }
        public bool MqttStatus { get; set; }
        public DateTime? LastDataTime { get; set; }
        public string MqttSubscribe { get; set; }  // BasePath/ClusterName/P
        public string MqttPublish { get; set; }    // BasePath/ClusterName/S
        public string PollTime { get; set; }
        public string ResponseTime { get; set; }
        public string Uptime { get; set; }
        // Computed property
        public bool IsOnline
        {
            get { return TcpStatus || MqttStatus; }
        }
    }






    // API Response DTOs
    public class ApiStatusResponse
    {
        public int? SiteId { get; set; }
        public string SiteName { get; set; }
        public string DivisionName { get; set; }
        public string ZoneName { get; set; }
        public List<ApiModemData> Modems { get; set; }
    }

    public class ApiModemData
    {
        public int ClusterId { get; set; }
        public string ModemId { get; set; }
        public int? TypeId { get; set; }
        public decimal? SignalStrength { get; set; }
        public DateTime TimeStamp { get; set; }
        public string UtilityTime { get; set; }
        public int? SignalPercent { get; set; }
        public bool Status { get; set; }
        public string ModemName { get; set; }
        public string ModemVersion { get; set; }
        public string ClusterName { get; set; }
        public bool TcpStatus { get; set; }
        public bool MqttStatus { get; set; }
        public string SimNumber { get; set; }
        public List<ApiA10Data> A10List { get; set; }
    }

    public class ApiA10Data
    {
        public int? TypeId { get; set; }
        public int? SiteId { get; set; }
        public int? A10Id { get; set; }
        public bool Status { get; set; }
        //public bool TcpStatusStatus { get; set; }
        //public bool MQTTStatusStatus { get; set; }
        public bool TcpStatus { get; set; }
        public bool MQTTStatus { get; set; }
        public DateTime TimeStamp { get; set; }
        public DateTime UtilityTime { get; set; }
        public string Uptime { get; set; }
        public string PollTime { get; set; }
        public string ResponseTime { get; set; }
        public string FirmwareVersion { get; set; }
        public int? ADCTypeId { get; set; }
    }
}
