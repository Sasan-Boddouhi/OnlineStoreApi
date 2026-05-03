namespace Application.Common.Queries;

public static class QueryContractExtensions
{
    public static void Validate(this QueryContract query)
    {
        if (query.Page.HasValue && query.Page < 1)
            throw new ArgumentException("Page must be >= 1");

        if (query.Size.HasValue && query.Size <= 0)
            throw new ArgumentException("Size must be > 0");

        if (query.Size is > 200)
            throw new ArgumentException("Size cannot exceed 200");

        if (query.Take is > 500)
            throw new ArgumentException("Take cannot exceed 500");
    }
}