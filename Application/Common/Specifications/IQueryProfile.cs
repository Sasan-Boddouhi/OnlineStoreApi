using System.Linq.Expressions;

namespace Application.Common.Specifications;

public interface IQueryProfile<TEntity> where TEntity : class
{
    Expression<Func<TEntity, bool>>? BaseCriteria { get; }
    IReadOnlyList<Expression<Func<TEntity, object>>> Includes { get; }
    IReadOnlyCollection<string> AllowedFields { get; }
}