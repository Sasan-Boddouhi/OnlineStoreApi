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
using BusinessLogic.Specifications.Log;

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

        public async Task<PagedResult<LogEntryDto>> GetPagedAsync(LogFilterDto filter, CancellationToken cancellationToken = default)
        {
            var spec = new LogProjectionSpecification(filter);

            var items = await _unitOfWork.Repository<Logs>()
                .ListAsync(spec, cancellationToken);

            var totalCount = await _unitOfWork.Repository<Logs>()
                .CountAsync(spec.Criteria, cancellationToken);

            return new PagedResult<LogEntryDto>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = filter.PageNumber,
                PageSize = filter.PageSize
            };
        }

        public async Task<LogEntryDto> CreateAsync(LogEntryDto dto)
        {
            var log = _mapper.Map<Logs>(dto);
            await _unitOfWork.Repository<Logs>().AddAsync(log);
            await _unitOfWork.SaveChangesAsync();

            return _mapper.Map<LogEntryDto>(log);
        }

        public async Task<IEnumerable<LogEntryDto>> GetLatestAsync(int count = 100)
        {
            var spec = new LatestLogsSpecification(count);
            var items = await _unitOfWork.Repository<Logs>().ListAsync(spec);

            return _mapper.Map<IEnumerable<LogEntryDto>>(items);
        }
    }
}
