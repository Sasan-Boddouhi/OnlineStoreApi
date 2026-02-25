using Application.Common.Specifications;
using Application.Entities;
using BusinessLogic.DTOs.Log;
using Application.Common.Helpers;
using BusinessLogic.Specifications.Log;
using System;
using System.Linq.Expressions;

namespace BusinessLogic.Specifications.Log
{
    public sealed class LogCountSpecification : BaseSpecification<Logs>
    {
        public LogCountSpecification(LogFilterDto filter)
            : base(LogQueryConfig.AllowedFields)
        {
            Expression<Func<Logs, bool>> criteria = l => true;

            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                criteria = criteria.And(l =>
                    l.Message.Contains(filter.Search) ||
                    (l.Exception != null && l.Exception.Contains(filter.Search)));
            }

            if (!string.IsNullOrEmpty(filter.Level))
            {
                criteria = criteria.And(l => l.Level == filter.Level);
            }

            if (filter.From.HasValue)
            {
                criteria = criteria.And(l => l.TimeStamp >= filter.From.Value);
            }

            if (filter.To.HasValue)
            {
                criteria = criteria.And(l => l.TimeStamp <= filter.To.Value);
            }

            AddCriteria(criteria);
        }
    }
}