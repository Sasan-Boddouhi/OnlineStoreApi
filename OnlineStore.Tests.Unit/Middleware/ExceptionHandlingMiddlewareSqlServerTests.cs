using System.Net;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Online_Store_Application.Middleware;

namespace OnlineStore.Tests.Unit.Middleware;

/// <summary>
/// Batch 7b — نگاشت نقض کلید یکتا به 409 DUPLICATE_RECORD روی SQL Server.
/// تست‌های یکپارچه فقط SQLite را می‌پوشانند؛ این‌جا یک SqlException واقعی (با شماره‌های 2601/2627)
/// ساخته و به middleware داده می‌شود، هم پیچیده در DbUpdateException و هم بدون آن.
/// نام جدول و ایندکس در پیام خطا هست ولی نباید به پاسخ برسد.
/// </summary>
public class ExceptionHandlingMiddlewareSqlServerTests
{
    private const string LeakyTable = "dbo.Users";
    private const string LeakyIndex = "IX_Users_Email";

    [Theory]
    [InlineData(2601)]
    [InlineData(2627)]
    public async Task UniqueViolation_WrappedInDbUpdateException_Returns409DuplicateRecord(int number)
    {
        var sql = CreateSqlException(number);
        var wrapped = new DbUpdateException("An error occurred while saving the entity changes.", sql);

        var (status, body) = await InvokeAsync(wrapped);

        status.Should().Be((int)HttpStatusCode.Conflict);
        body.GetProperty("code").GetString().Should().Be("DUPLICATE_RECORD");
        AssertNoDatabaseDetails(body);
    }

    [Fact]
    public async Task UniqueViolation_UnwrappedSqlException_Returns409DuplicateRecord()
    {
        // مسیرهایی مثل ExecuteUpdate/ExecuteSql استثنا را بدون DbUpdateException پرتاب می‌کنند.
        var (status, body) = await InvokeAsync(CreateSqlException(2627));

        status.Should().Be((int)HttpStatusCode.Conflict);
        body.GetProperty("code").GetString().Should().Be("DUPLICATE_RECORD");
        AssertNoDatabaseDetails(body);
    }

    [Fact]
    public async Task OtherSqlServerError_StaysInternalServerError()
    {
        // 547 = نقض کلید خارجی؛ تعارض یکتا نیست و نباید 409 شود.
        var wrapped = new DbUpdateException("save failed", CreateSqlException(547));

        var (status, body) = await InvokeAsync(wrapped);

        status.Should().Be((int)HttpStatusCode.InternalServerError);
        // Batch 53 (API-04): 500 پاسخ ProblemDetails یکپارچه با کد خطا «INTERNAL_ERROR» دارد.
        body.GetProperty("code").GetString().Should().Be("INTERNAL_ERROR");
        AssertNoDatabaseDetails(body);
    }

    private static void AssertNoDatabaseDetails(JsonElement body)
    {
        var raw = body.GetRawText();
        raw.Should().NotContain(LeakyTable);
        raw.Should().NotContain(LeakyIndex);
        raw.Should().NotContain("duplicate key");
    }

    private static async Task<(int Status, JsonElement Body)> InvokeAsync(Exception toThrow)
    {
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw toThrow,
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, doc.RootElement.Clone());
    }

    /// <summary>
    /// SqlException سازندهٔ عمومی ندارد؛ با reflection همان مسیری که SqlClient خودش استفاده می‌کند
    /// (SqlError → SqlErrorCollection → SqlException.CreateException) پیموده می‌شود.
    /// </summary>
    private static SqlException CreateSqlException(int number)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
        var message = $"Cannot insert duplicate key row in object '{LeakyTable}' with unique index '{LeakyIndex}'.";

        var errorCtor = typeof(SqlError).GetConstructors(flags)
            .Where(c => c.GetParameters().FirstOrDefault()?.ParameterType == typeof(int))
            .OrderByDescending(c => c.GetParameters().Length)
            .First();
        var errorArgs = errorCtor.GetParameters().Select(p => p.Name switch
        {
            "infoNumber" => number,
            "errorMessage" => message,
            "server" => "test-server",
            "procedure" => string.Empty,
            _ => p.HasDefaultValue ? p.DefaultValue
                : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType)
                : null
        }).ToArray();
        var error = (SqlError)errorCtor.Invoke(errorArgs);

        var collection = (SqlErrorCollection)typeof(SqlErrorCollection)
            .GetConstructor(flags, Type.EmptyTypes)!
            .Invoke(null);
        typeof(SqlErrorCollection).GetMethod("Add", flags)!.Invoke(collection, new object[] { error });

        var create = typeof(SqlException)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(m => m.Name == "CreateException"
                && m.GetParameters().Select(p => p.ParameterType)
                    .SequenceEqual(new[] { typeof(SqlErrorCollection), typeof(string) }));
        var exception = (SqlException)create.Invoke(null, new object[] { collection, "16.0.0" })!;

        exception.Number.Should().Be(number, "the constructed exception must look exactly like SQL Server's");
        return exception;
    }
}
