using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class BlockIPAddress
    {
        public int Id { get; set; }
        public string IpAddress { get; set; }
    }

    public class BlockIPAddressLister : APIResponse
    {
        public BlockIPAddressLister()
        {
            BlockIPAddress = new List<BlockIPAddress>();
            SearchCriteria = new BlockIPAddress();
            Pager = new Pager();
        }
        public List<BlockIPAddress> BlockIPAddress { get; set; }
        public BlockIPAddress SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}
