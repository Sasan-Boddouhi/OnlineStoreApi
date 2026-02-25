using Application.Entities;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.Log;
using BusinessLogic.DTOs.Shared;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using BusinessLogic.Specifications.Log;

namespace BusinessLogic.Services.Implementations
{
    public sealed class LogService : ILogService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly ILogger<LogService> _logger;

        public LogService(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            ILogger<LogService> logger)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _logger = logger;
        }

        #region GetPagedAsync

        public async Task<PagedResult<LogEntryDto>> GetPagedAsync(
            LogFilterDto filter,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Retrieving paged logs. Page: {PageNumber}, Size: {PageSize}, Level: {Level}",
                filter.PageNumber, filter.PageSize, filter.Level ?? "All");

            try
            {
                var itemSpec = new LogProjectionSpecification(filter);
                var countSpec = new LogCountSpecification(filter);

                var items = await _unitOfWork
                    .Repository<Logs>()
                    .ListAsync<LogEntryDto>(itemSpec, cancellationToken);

                var totalCount = await _unitOfWork
                    .Repository<Logs>()
                    .CountAsync(countSpec, cancellationToken);

                return new PagedResult<LogEntryDto>
                {
                    Items = items,
                    TotalCount = totalCount,
                    PageNumber = filter.PageNumber,
                    PageSize = filter.PageSize
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving paged logs");
                throw;
            }
        }

        #endregion

        #region CreateAsync

        public async Task<LogEntryDto> CreateAsync(LogEntryDto dto, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Creating log entry with level: {Level}", dto.Level);

            try
            {
                var log = _mapper.Map<Logs>(dto);
                log.TimeStamp = DateTime.UtcNow; // اطمینان از تنظیم زمان

                await _unitOfWork.Repository<Logs>().AddAsync(log, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Log entry created with ID: {Id}", log.Id);
                return _mapper.Map<LogEntryDto>(log);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating log entry");
                throw;
            }
        }

        #endregion

        #region GetLatestAsync

        public async Task<IEnumerable<LogEntryDto>> GetLatestAsync(int count = 100, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Retrieving latest {Count} logs", count);

            try
            {
                var spec = new LogLatestSpecification(count);
                var logs = await _unitOfWork
                    .Repository<Logs>()
                    .ListAsync<LogEntryDto>(spec, cancellationToken);

                return logs;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving latest logs");
                throw;
            }
        }

        #endregion
    }
}