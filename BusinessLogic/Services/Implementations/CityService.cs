using Application.Common.Queries;
using Application.Common.Specifications;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Services;
using AutoMapper;
using BusinessLogic.DTOs.City;
using BusinessLogic.DTOs.Shared;
using BusinessLogic.Services.Interfaces;
using BusinessLogic.Specifications.Cities;
using Microsoft.Extensions.Logging;

namespace BusinessLogic.Services.Implementations;

public sealed class CityService : ICityService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ILogger<CityService> _logger;
    private readonly ICacheService? _cacheService;

    public CityService(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ILogger<CityService> logger,
        ICacheService? cacheService = null)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _logger = logger;
        _cacheService = cacheService;
    }

    #region Query

    public async Task<PagedResult<CityDto>> GetByQueryAsync(
        QueryContract<City> query,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"cities:all:{query.GetHashCode()}";
        if (_cacheService != null)
        {
            var cached = await _cacheService.GetAsync<PagedResult<CityDto>>(cacheKey, cancellationToken);
            if (cached != null)
                return cached;
        }

        var spec = query.ToSpec();
        var items = await _unitOfWork.Repository<City>()
            .ListAsync(spec, CityQueryConfig.Projection, cancellationToken);
        var totalCount = await _unitOfWork.Repository<City>()
            .CountAsync(spec, cancellationToken);

        int pageNumber, pageSize;
        if (query.Skip.HasValue || query.Take.HasValue)
        {
            pageSize = query.Take ?? 20;
            var skip = query.Skip ?? 0;
            pageNumber = skip / pageSize + 1;
        }
        else
        {
            pageNumber = query.Page ?? 1;
            pageSize = query.Size ?? 20;
        }

        var result = new PagedResult<CityDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };

        if (_cacheService != null)
            await _cacheService.SetAsync(cacheKey, result, TimeSpan.FromHours(1), cancellationToken);

        return result;
    }

    public async Task<CityDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"cities:{id}";
        if (_cacheService != null)
        {
            var cached = await _cacheService.GetAsync<CityDto>(cacheKey, cancellationToken);
            if (cached != null)
                return cached;
        }

        var spec = new Spec<City>().Where(c => c.CityId == id);
        var result = await _unitOfWork.Repository<City>()
            .FirstOrDefaultAsync(spec, CityQueryConfig.Projection, cancellationToken);

        if (_cacheService != null && result != null)
            await _cacheService.SetAsync(cacheKey, result, TimeSpan.FromHours(1), cancellationToken);

        return result;
    }

    #endregion

    #region Commands

    public async Task<CityDto> CreateAsync(CreateCityDto dto, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating city: {CityName}", dto.CityName);
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await ValidateCreationAsync(dto, cancellationToken);
            var entity = _mapper.Map<City>(dto);
            await _unitOfWork.Repository<City>().AddAsync(entity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            _logger.LogInformation("City created with ID: {Id}", entity.CityId);
            return await GetByIdAsync(entity.CityId, cancellationToken)
                   ?? throw new BusinessException("خطا در بازیابی شهر ایجاد شده", "CITY_RETRIEVAL_ERROR");
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Failed to create city: {CityName}", dto.CityName);
            throw new BusinessException("خطا در ایجاد شهر", "CITY_CREATE_ERROR");
        }
    }

    public async Task<CityDto?> UpdateAsync(UpdateCityDto dto, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating city ID: {Id}", dto.CityId);
        var entity = await _unitOfWork.Repository<City>().GetByIdAsync(dto.CityId, cancellationToken);
        if (entity == null)
        {
            _logger.LogWarning("City not found: {Id}", dto.CityId);
            return null;
        }

        if (entity.CityName != dto.CityName)
        {
            var exists = await _unitOfWork.Repository<City>()
                .AnyAsync(c => c.CityName == dto.CityName && c.CityId != dto.CityId, cancellationToken);
            if (exists)
                throw new BusinessException("شهری با این نام قبلاً ثبت شده است.", "CITY_NAME_DUPLICATE");
        }

        _mapper.Map(dto, entity);
        _unitOfWork.Repository<City>().Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (_cacheService != null)
            await _cacheService.RemoveAsync($"cities:{entity.CityId}", cancellationToken);

        _logger.LogInformation("City updated: {Id}", dto.CityId);
        return await GetByIdAsync(entity.CityId, cancellationToken);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting city ID: {Id}", id);
        var entity = await _unitOfWork.Repository<City>().GetByIdAsync(id, cancellationToken);
        if (entity == null)
        {
            _logger.LogWarning("Delete failed: city not found {Id}", id);
            return false;
        }

        var addressRepo = _unitOfWork.Repository<Address>();
        if (addressRepo != null && await addressRepo.AnyAsync(a => a.CityId == id, cancellationToken))
            throw new BusinessException("این شهر دارای آدرس است و قابل حذف نیست.", "CITY_HAS_ADDRESSES");

        _unitOfWork.Repository<City>().Delete(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (_cacheService != null)
            await _cacheService.RemoveAsync($"cities:{id}", cancellationToken);

        _logger.LogInformation("City deleted: {Id}", id);
        return true;
    }

    #endregion

    #region Validation

    private async Task ValidateCreationAsync(CreateCityDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.CityName))
            throw new BusinessException("نام شهر الزامی است.", "CITY_NAME_REQUIRED");
        if (dto.ProvinceId <= 0)
            throw new BusinessException("شناسه استان معتبر نیست.", "INVALID_PROVINCE_ID");

        var provinceExists = await _unitOfWork.Repository<Province>()
            .AnyAsync(p => p.ProvinceId == dto.ProvinceId, cancellationToken);
        if (!provinceExists)
            throw new BusinessException("استان انتخاب‌شده وجود ندارد.", "PROVINCE_NOT_FOUND");

        var cityExists = await _unitOfWork.Repository<City>()
            .AnyAsync(c => c.CityName == dto.CityName, cancellationToken);
        if (cityExists)
            throw new BusinessException("شهری با این نام قبلاً ثبت شده است.", "CITY_NAME_DUPLICATE");
    }

    #endregion
}