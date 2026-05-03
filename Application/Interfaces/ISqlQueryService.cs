using Application.Interfaces;

namespace Application.Interfaces;

public interface ISqlQueryService
{
    /// <summary>اجرای یک TVF یا کوئری SQL و نگاشت خودکار به DTO مستقل.</summary>
    Task<IReadOnlyList<TResult>> QueryAsync<TResult>(
        string sql,
        CancellationToken cancellationToken = default,
        params (string Name, object Value)[] parameters)
        where TResult : class, ISqlResult;

    /// <summary>اجرای یک دستور SQL (Stored Procedure یا Command).</summary>
    Task<int> ExecuteAsync(
        string sql,
        CancellationToken cancellationToken = default,
        params (string Name, object Value)[] parameters);
}