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
    public class LatestLogsSpecification : BaseProjectionSpecification<Logs, LogEntryDto>
    {
        public LatestLogsSpecification(int count)
        {
            ApplyOrderByDescending(l => l.TimeStamp);
            ApplyPaging(0, count);
            Selector = l => new LogEntryDto
            {
                Id = l.Id,
                Message = l.Message,
                Level = l.Level,
                Exception = l.Exception,
                TimeStamp = l.TimeStamp
            };
        }
    }
}
