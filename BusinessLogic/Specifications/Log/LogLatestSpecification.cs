using Application.Common.Specifications;
using Application.Entities;
using BusinessLogic.DTOs.Log;
using BusinessLogic.Specifications.Log;

namespace BusinessLogic.Specifications.Log
{
    public sealed class LogLatestSpecification : BaseProjectionSpecification<Logs, LogEntryDto>
    {
        public LogLatestSpecification(int count)
            : base(LogQueryConfig.AllowedFields)
        {
            ApplyOrderBy(l => l.TimeStamp, descending: true);
            ApplyPaging(0, count);
            SetProjection(LogQueryConfig.Projection);
        }
    }
}