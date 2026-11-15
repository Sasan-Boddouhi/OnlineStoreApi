using System.Text.Encodings.Web;
using System.Text.Json;
using Application.Exceptions;
using FluentAssertions;

namespace OnlineStore.Tests.Unit.Contract;

/// <summary>
/// Batch 53 (API-06) — فایل <c>openapi/error-codes.json</c> از رجیستری تولید می‌شود.
/// تست، فایل را در صورت اختلاف بازنویسی می‌کند و همان لحظه شکست می‌خورد تا نسخهٔ تازه commit شود
/// (در CI فایل commit‌شده باید با رجیستری یکی باشد).
/// </summary>
public class ErrorCodeCatalogFileTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    [Fact]
    public void CommittedCatalog_MatchesRegistry()
    {
        var expected = JsonSerializer.Serialize(
            ErrorCodes.All.OrderBy(e => e.Code, StringComparer.Ordinal)
                .Select(e => new { e.Code, e.Status, e.Message, Type = ErrorCodes.TypeUri(e.Code) }),
            Options).ReplaceLineEndings("\n") + "\n";

        var path = Path.Combine(FindRepoRoot(), "openapi", "error-codes.json");
        var actual = File.Exists(path) ? File.ReadAllText(path).ReplaceLineEndings("\n") : null;

        if (actual != expected)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, expected);
        }

        actual.Should().Be(expected, "openapi/error-codes.json was regenerated from ErrorCodes; commit it");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.GetFiles("*.sln").Any())
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Solution root not found.");
    }
}
