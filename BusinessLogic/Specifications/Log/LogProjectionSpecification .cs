using Application.Common.Specifications;
using Application.Entities;
using BusinessLogic.DTOs.Log;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Specifications.Log
{
    public class LogProjectionSpecification
            : BaseProjectionSpecification<Logs, LogEntryDto>
    {
        public LogProjectionSpecification(LogFilterDto filter)
        {
            // ===== Criteria =====
            Criteria = l =>
                (string.IsNullOrWhiteSpace(filter.Search) ||
                    l.Message.Contains(filter.Search) ||
                    (l.Exception != null && l.Exception.Contains(filter.Search)))
                &&
                (string.IsNullOrEmpty(filter.Level) || l.Level == filter.Level)
                &&
                (filter.From == null || l.TimeStamp >= filter.From)
                &&
                (filter.To == null || l.TimeStamp <= filter.To);

            // ===== Sorting =====
            if (filter.SortOrder == "asc")
                ApplyOrderBy(l => l.TimeStamp);
            else
                ApplyOrderByDescending(l => l.TimeStamp);

            Selector = l => new LogEntryDto
            {
                Id = l.Id,
                Message = l.Message,
                Level = l.Level,
                Exception = l.Exception,
                TimeStamp = l.TimeStamp
            };

            // ===== Paging =====
            ApplyPaging(
                (filter.PageNumber - 1) * filter.PageSize,
                filter.PageSize
            );
        }
    }
}
