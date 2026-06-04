using Domain;
using System.Collections.Generic;

namespace E7FRSAdvance.Interface
{
    public interface ISiteKeepingService
    {
        TrainRunningMomentLister TrainRunningMoment(TrainRunningMomentLister mTrainRunningMomentLister);

        GluedLogDataLister GluedLogDataLister(GluedLogDataLister mGluedLogDataLister);

        IndexAlertLogLister IndexAlertLogLister(IndexAlertLogLister mIndexAlertLogLister);

        WatchListLister AxleCounterLister(WatchListLister mWatchListLister);

        SMSLogLister EarthFaultLister(SMSLogLister mSMSLogLister);

        MomentLister SignalMomentLister(MomentLister mMomentLister);

        MomentLister TPRMomentLister(MomentLister mMomentLister);

        MomentLister PointIndicationLister(MomentLister mMomentLister);

        SMSLogLister GetAlertLogLister(SMSLogLister mSMSLogLister);

        ProbabilityLister GetProbabilityLister(ProbabilityLister mProbabilityLister);

        PerformanceLister PerformanceLister(PerformanceLister mPerformanceLister);

        A10Calibration GetA10Multiplayer(CalibData mA10Calibration, List<CardLine> mYardConfigs, int assetId, int attributeId);
    }
}
