using System.Collections.Generic;

namespace Domain
{
    public class Analytics
    {
        public int AssetId { get; set; }
        public string AssetName { get; set; }
        public List<AlertValue> mAlertValues { get; set; }
        public Analytics()
        {
            mAlertValues = new List<AlertValue>();
        }
    }

    public class AnalyticsLister
    {
        public List<Analytics> Analytics { get; set; } = new List<Analytics>();
        public List<Alert> mAlerts { get; set; }

    }
}
