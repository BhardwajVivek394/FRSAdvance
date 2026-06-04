using System.Collections.Generic;

namespace Domain
{
    public class AlertsLister
    {
        public Asset Asset { get; set; } = new Asset();
        public AlertMessage SearchCriteria { get; set; } = new AlertMessage();
        public List<Domain.AlertMessage> alertMessages = new List<AlertMessage>();
        public AlertsLister()
        {
            Asset = new Asset();
            alertMessages = new List<AlertMessage>();
            SearchCriteria = new AlertMessage();
        }
    }

    public class USBDeviceInfo
    {
        public USBDeviceInfo(string deviceID, string pnpDeviceID, string description)
        {
            this.DeviceID = deviceID;
            this.PnpDeviceID = pnpDeviceID;
            this.Description = description;
        }

        public string DeviceID { get; private set; }
        public string PnpDeviceID { get; private set; }
        public string Description { get; private set; }
    }

}
