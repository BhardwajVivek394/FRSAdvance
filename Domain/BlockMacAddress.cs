using System.Collections.Generic;

namespace Domain
{
    public class BlockMacAddress
    {
        public int Id { get; set; }
        public string MacAddress { get; set; }
        public int CreatedBy { get; set; }
    }


    public class BlockMacAddressLister
    {
        public BlockMacAddressLister()
        {
            BlockMacAddress = new List<BlockMacAddress>();
            SearchCriteria = new BlockMacAddress();
            Pager = new Pager();
        }
        public List<BlockMacAddress> BlockMacAddress { get; set; }
        public BlockMacAddress SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}
