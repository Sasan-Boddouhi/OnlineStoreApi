namespace Application.Common.Queries;

public class QueryContract
{
    public string? Filter { get; init; }
    public string? Sort { get; init; }

    public int? Page { get; init; }
    public int? Size { get; init; }
    public int? Skip { get; init; }
    public int? Take { get; init; }

    public Paging ToPaging()
    {
        bool hasPage = Page.HasValue || Size.HasValue;
        bool hasSkip = Skip.HasValue || Take.HasValue;

        if (hasPage && hasSkip)
            throw new ArgumentException("Cannot use Page/Size with Skip/Take together.");

        if (Skip.HasValue && Take.HasValue)
            return Paging.FromSkipTake(Skip.Value, Take.Value);

        if (Page.HasValue || Size.HasValue)
            return Paging.FromPage(Page ?? 1, Size ?? 20);

        return Paging.FromPage(1, 20);
    }
}