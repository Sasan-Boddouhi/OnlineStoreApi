using System;
using System.Linq;

namespace Application.Common.Helpers
{
    public static class QueryGuard
    {
        public const int MaxFilterLength = 500;
        public const int MaxSortLength = 200;
        public const int MaxSortFields = 3;
        public const int MaxNestingDepth = 3;
        public const int MaxFilterConditions = 10;

        public static void EnsureValid(string? filter, string? sort)
        {
            if (!string.IsNullOrWhiteSpace(filter))
            {
                if (filter.Length > MaxFilterLength)
                    throw new ArgumentException($"Filter string too long. Max {MaxFilterLength} characters.");

                int conditionCount = filter.Split(new[] { " and ", " or " }, StringSplitOptions.None).Length;
                if (conditionCount > MaxFilterConditions)
                    throw new ArgumentException($"Too many conditions in filter. Max {MaxFilterConditions}.");

                if (filter.Contains("--") || filter.Contains(";") || filter.Contains("/*") || filter.Contains("*/"))
                    throw new ArgumentException("Filter contains dangerous characters.");
            }

            if (!string.IsNullOrWhiteSpace(sort))
            {
                if (sort.Length > MaxSortLength)
                    throw new ArgumentException($"Sort string too long. Maximum allowed length is {MaxSortLength} characters.");

                var fields = sort.Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length > MaxSortFields)
                    throw new ArgumentException($"Too many sort fields. Maximum allowed is {MaxSortFields}.");

                foreach (var field in fields)
                {
                    var property = field.Trim().TrimStart('-');
                    if (property.Count(c => c == '.') >= MaxNestingDepth)
                        throw new ArgumentException($"Nesting depth too deep. Maximum allowed is {MaxNestingDepth} levels.");
                }
            }
        }
    }
}