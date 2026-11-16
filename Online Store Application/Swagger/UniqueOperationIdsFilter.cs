using System.Text.RegularExpressions;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Online_Store_Application.Swagger;

/// <summary>
/// Batch 67 (API-05): اکشنی که دو مسیر دارد (مثلاً <c>seller-payouts</c> و <c>[controller]</c>) دو عملیات با یک
/// <c>operationId</c> می‌سازد. مسیر اصلی (بدون حرف بزرگ در بخش‌های ثابت) شناسهٔ ساده را نگه می‌دارد و
/// مسیرهای دیگر پسوند <c>_Alias</c> (و در صورت لزوم شماره) می‌گیرند. ترتیب قطعی است.
/// </summary>
public sealed class UniqueOperationIdsFilter : IDocumentFilter
{
    private static readonly Regex Parameter = new(@"\{[^}]*\}", RegexOptions.Compiled);

    public void Apply(OpenApiDocument document, DocumentFilterContext context)
    {
        var operations = document.Paths
            .SelectMany(p => p.Value.Operations.Select(o => (Path: p.Key, Operation: o.Value)))
            .Where(x => x.Operation.OperationId is not null)
            .GroupBy(x => x.Operation.OperationId!)
            .Where(g => g.Count() > 1);

        foreach (var group in operations)
        {
            var ordered = group
                .OrderBy(x => Parameter.Replace(x.Path, string.Empty).Any(char.IsUpper) ? 1 : 0)
                .ThenBy(x => x.Path, StringComparer.Ordinal)
                .ToList();

            for (var i = 1; i < ordered.Count; i++)
                ordered[i].Operation.OperationId = i == 1 ? $"{group.Key}_Alias" : $"{group.Key}_Alias{i}";
        }

        // Same file on every OS/ICU setup: paths in ordinal order (the discovery order is culture-dependent).
        var sorted = document.Paths.OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
        document.Paths.Clear();
        foreach (var (key, value) in sorted)
            document.Paths.Add(key, value);
    }
}
