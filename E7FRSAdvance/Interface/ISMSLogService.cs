using Domain;

namespace E7FRSAdvance.Interface
{
    public interface ISMSLogService
    {
        SMSLogLister GetAll(SMSLogLister mSMSLogLister);
    }
}
