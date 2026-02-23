using BusinessLogic.DTOs.Log;
using BusinessLogic.DTOs.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Services.Interfaces
{
    public interface ILogService
    {
        Task<PagedResult<LogEntryDto>> GetPagedAsync(LogFilterDto filter, CancellationToken cancellationToken = default);
        Task<LogEntryDto> CreateAsync(LogEntryDto dto);
        Task<IEnumerable<LogEntryDto>> GetLatestAsync(int count = 100);
    }
}
