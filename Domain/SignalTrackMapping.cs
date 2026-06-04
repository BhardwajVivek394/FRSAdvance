using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class SignalTrackMapping
    {
        public int Id { get; set; }
        public int AssetId { get; set; }
        public int SignalAssetId { get; set; }
        //Extra properties
        public string AssetName { get; set; }
        public string SignalAssetName { get; set; }
        public bool IsChecked { get; set; }
    }
}
