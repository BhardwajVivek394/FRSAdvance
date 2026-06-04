namespace E7FRSAdvance.Interface
{
    public interface IFRSAlertService
    {
        Domain.FRSAlertLister GetListerWithTimeFilter(Domain.FRSAlertLister mFRSAlertLister);

        Domain.FRSAlertLister GetWithOutAcknowledgementAlert(Domain.FRSAlertLister mFRSAlertLister);

        Domain.FRSAlertLister GetFalseAcknowledgementFRSAlert(Domain.FRSAlertLister mFRSAlertLister);

        Domain.FRSAlertLister GetWithAcknowledgementAlert(Domain.FRSAlertLister mFRSAlertLister);

        Domain.FRSAlertLister GetListerWithPagination(Domain.FRSAlertLister mFRSAlertLister);

        Domain.FRSAlertLister GetAll(Domain.FRSAlertLister mFRSAlertLister);

        Domain.FRSAlertLister GetWithOutAcknowledgementAlertList(Domain.FRSAlertLister mFRSAlertLister);
        Domain.FRSAlertLister GetWithAcknowledgementAlertList(Domain.FRSAlertLister mFRSAlertLister);
        Domain.FRSAlertLister GetWithAcknowledgementAlertWithManual(Domain.FRSAlertLister mFRSAlertLister);
        Domain.FRSAlertLister GetAcknowledgementAllAlert(Domain.FRSAlertLister mFRSAlertLister);
    }
}
