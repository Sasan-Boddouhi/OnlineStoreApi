namespace Application.Common.Queries;

public static class QueryContractExtensions
{
    public static (int Skip, int Take, int Page, int Size) ToPaging<TEntity>(
        this QueryContract<TEntity> query)
        where TEntity : class
    {
        var page = query.Page ?? 1;
        var size = query.Size ?? 20;

        return (
            (page - 1) * size,
            size,
            page,
            size
        );
    }
}