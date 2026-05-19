namespace Application.Common.Queries;

public class PagingResult
{
    public int Skip { get; set; }
    public int Take { get; set; }
    public int Page { get; set; }
    public int Size { get; set; }
}