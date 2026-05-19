namespace Application.Common.Queries;

public sealed class QueryParseContext<TEntity>
    where TEntity : class
{
    public IReadOnlySet<string> AllowedFields { get; init; }
        = new HashSet<string>();

    public bool CaseInsensitive { get; init; } = true;

    public int MaxPageSize { get; init; } = 100;
}