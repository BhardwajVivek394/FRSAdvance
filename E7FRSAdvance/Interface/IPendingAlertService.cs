using System.Collections.Generic;

namespace E7FRSAdvance.Interface
{
    public interface IPendingAlertService
    {
        Domain.PendingAlertLister GetWithOutAcknowledgementAlert(Domain.PendingAlertLister mFRSAlertLister);
        Domain.PendingAlertLister GetListerWithPagination(Domain.PendingAlertLister mFRSAlertLister);
        Domain.PendingAlertLister GetAcknowledgementAllAlert(Domain.PendingAlertLister mFRSAlertLister);

    }
}