using Application.Entities;
using BusinessLogic.DTOs.Log;
using System;
using System.Linq.Expressions;

namespace BusinessLogic.Specifications.Log
{
    public static class LogQueryConfig
    {
        public static readonly string[] AllowedFields =
        {
            "timestamp",
            "level",
            "message"
        };

        public static Expression<Func<Logs, LogEntryDto>> Projection =>
            l => new LogEntryDto
            {
                Id = l.Id,
                Message = l.Message,
                Level = l.Level,
                Exception = l.Exception,
                TimeStamp = l.TimeStamp
            };
    }
}