using BusinessLogic.DTOs.Log;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Dapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BusinessLogic.DTOs.Shared;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Application.Interfaces;
using Application.Entities;

namespace BusinessLogic.Services.Implementations
{
    public class LogService : ILogService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        public LogService(IUnitOfWork unitOfWork, IMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
        }

        public async Task<PagedResult<LogEntryDto>> GetPagedAsync(LogFilterDto filter)
        {
            var query = _unitOfWork.Logs.Query();

            if(!string.IsNullOrWhiteSpace(filter.Search))
                query = query.Where(l => 
                    l.Message.Contains(filter.Search) || 
                    l.Exception != null && l.Exception.Contains(filter.Search));

            if(!string.IsNullOrEmpty(filter.Level))
                query = query.Where(l => l.Level == filter.Level);

            if (filter.From.HasValue)
                query = query.Where(l => l.TimeStamp >= filter.From.Value);

            if (filter.To.HasValue)
                query = query.Where(l => l.TimeStamp <= filter.To.Value);

            query = filter.SortOrder == "asc"
                ? query.OrderBy(l => l.TimeStamp)
                :   query.OrderByDescending(l => l.TimeStamp);

            var totalCount = await query.CountAsync();
            var items = await query.Skip((filter.PageNumber - 1) * (filter.PageSize)).Take(filter.PageSize).ToListAsync();

            return new PagedResult<LogEntryDto>
            {
                Items = _mapper.Map<IEnumerable<LogEntryDto>>(items),
                TotalCount = totalCount,
                PageNumber = filter.PageNumber,
                PageSize = filter.PageSize
            };
        }

        public async Task<LogEntryDto> CreateAsync(LogEntryDto dto)
        {
            var log = _mapper.Map<Logs>(dto);

            await _unitOfWork.Logs.AddAsync(log);
            await _unitOfWork.SaveChangesAsync();

            return _mapper.Map<LogEntryDto>(log);
        }

        public async Task<IEnumerable<LogEntryDto>> GetLatestAsync(int count = 100)
        {
            var items = await _unitOfWork.Logs.Query()
                .OrderByDescending(l => l.TimeStamp)
                .Take(count)
                .ToListAsync();

            return _mapper.Map<IEnumerable<LogEntryDto>>(items);
        }
    }
}
