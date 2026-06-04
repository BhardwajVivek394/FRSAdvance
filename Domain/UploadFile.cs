using System.Collections.Generic;

namespace Domain
{
    public class UploadFile
    {
        public int SiteId { get; set; }
        public string FileBase64 { get; set; }
        public List<Domain.DataloggerFileFormat> mDataloggerFileFormats { get; set; } = new List<DataloggerFileFormat>();
        public List<Domain.PointMachineData> mPointMachineDatas { get; set; } = new List<PointMachineData>();
    }
}
