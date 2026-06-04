using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class HooterSetting
    {
        public int DataInterval { get; set; }
        public int LastMinuteData { get; set; }
        public bool AlertSound { get; set; }
        public int AudioDuration { get; set; }
        public int ReminderFrequency { get; set; }
    }
}
