using Application.Common.Specifications;
using System.Linq.Expressions;

public class QueryProfile<TEntity, TDto> : IQueryProfile<TEntity>
    where TEntity : class
    where TDto : class
{
    public Expression<Func<TEntity, bool>>? BaseCriteria { get; init; }

    // همچنان List برای پشتیبانی از collection initializer
    public List<Expression<Func<TEntity, object>>> Includes { get; init; } = new();

    public Expression<Func<TEntity, TDto>> Projection { get; init; } = default!;
    public IReadOnlyCollection<string> AllowedFields { get; init; } = Array.Empty<string>();

    // پیاده‌سازی صریح اینترفیس: فقط یک نمای فقط‌خواندنی
    IReadOnlyList<Expression<Func<TEntity, object>>> IQueryProfile<TEntity>.Includes => Includes.AsReadOnly();
}