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

    public static string ToCacheKey<TEntity>(this QueryContract<TEntity> query, string prefix) where TEntity : class
    {
        var p = query.Page ?? 1;
        var s = query.Size ?? 20;
        var skip = query.Skip?.ToString() ?? "0";
        var take = query.Take?.ToString() ?? s.ToString();
        var filter = query.Filter != null ? query.Filter.ToString() : "none";
        var sorts = query.Sorts != null && query.Sorts.Count > 0
            ? string.Join(",", query.Sorts.Select(x => $"{x.Descending}:{x}"))
            : "none";

        return $"{prefix}:all:p{p}:s{s}:sk{skip}:tk{take}:f{filter}:o{sorts}";
    }
}