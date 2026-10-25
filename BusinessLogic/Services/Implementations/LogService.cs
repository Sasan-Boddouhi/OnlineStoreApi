using Application.Entities;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.Log;
using BusinessLogic.DTOs.Shared;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace BusinessLogic.Services.Implementations;

public sealed class LogService : ILogService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ILogger<LogService> _logger;

    public LogService(IUnitOfWork unitOfWork, IMapper mapper, ILogger<LogService> logger)
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
            var query = _unitOfWork.Repository<Logs>().Query();

            // فیلترها
            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                query = query.Where(l =>
                    l.Message.Contains(filter.Search) ||
                    (l.Exception != null && l.Exception.Contains(filter.Search)));
            }

            if (!string.IsNullOrEmpty(filter.Level))
                query = query.Where(l => l.Level == filter.Level);

            if (filter.From.HasValue)
                query = query.Where(l => l.TimeStamp >= filter.From.Value);

            if (filter.To.HasValue)
                query = query.Where(l => l.TimeStamp <= filter.To.Value);

            // مرتب‌سازی
            query = filter.SortOrder?.ToLower() == "asc"
                ? query.OrderBy(l => l.TimeStamp)
                : query.OrderByDescending(l => l.TimeStamp);

            // شمارش کل
            var totalCount = await query.CountAsync(cancellationToken);

            // صفحه‌بندی
            var items = await query
                .Skip((filter.PageNumber - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .ToListAsync(cancellationToken);

            return new PagedResult<LogEntryDto>
            {
                Items = _mapper.Map<IEnumerable<LogEntryDto>>(items),
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
            log.TimeStamp = DateTime.UtcNow;
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
            var logs = await _unitOfWork.Repository<Logs>()
                .Query()
                .OrderByDescending(l => l.TimeStamp)
                .Take(count)
                .ToListAsync(cancellationToken);
            return _mapper.Map<IEnumerable<LogEntryDto>>(logs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving latest logs");
            throw;
        }
    }

    #endregion
}