namespace Domain
{
    public class Pager
    {
        public string PageSizeText { get; set; }
        public int PageSize { get; set; }
        public int Take { get; set; }
        public int Skip { get; set; }
        public decimal CurrentPage { get; set; }
        public string TotalPage { get; set; }
        public decimal TotalRecord { get; set; }
        public Pager()
        {
            PageSize = 20;
            Skip = 0;
            CurrentPage = 1;
            Take = 10;
            TotalPage = "1";
            TotalRecord = 0;
        }
    }
}