using Application.Common.Specifications;
using System;

namespace Application.Common.Helpers
{
    public static class SortStringParser
    {
        public static void ApplySortString<T>(
            this BaseSpecification<T> specification,
            string? sortString)
        {
            if (string.IsNullOrWhiteSpace(sortString))
                return;

            var parts = sortString.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                bool descending = trimmed.StartsWith('-');
                var propertyName = descending ? trimmed.Substring(1) : trimmed;
                specification.ApplyOrderBy(propertyName, descending);
            }
        }
    }
}