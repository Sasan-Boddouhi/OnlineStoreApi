using Application.Common.Queries;
using Application.Common.Specifications;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Services;
using AutoMapper;
using BusinessLogic.DTOs.Province;
using BusinessLogic.DTOs.Shared;
using BusinessLogic.Services.Interfaces;
using BusinessLogic.Specifications.Provinces;
using Microsoft.Extensions.Logging;

namespace BusinessLogic.Services.Implementations;

public sealed class ProvinceService : IProvinceService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ILogger<ProvinceService> _logger;
    private readonly ICacheService? _cacheService;

    public ProvinceService(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ILogger<ProvinceService> logger,
        ICacheService? cacheService = null)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _logger = logger;
        _cacheService = cacheService;
    }

    #region GetByQueryAsync

    public async Task<PagedResult<ProvinceDto>> GetByQueryAsync(
        QueryContract<Province> query,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"provinces:all:{query.GetHashCode()}";
        if (_cacheService != null)
        {
            var cached = await _cacheService.GetAsync<PagedResult<ProvinceDto>>(cacheKey, cancellationToken);
            if (cached != null)
                return cached;
        }

        var spec = query.ToSpec();
        var items = await _unitOfWork.Repository<Province>()
            .ListAsync(spec, ProvinceQueryConfig.Projection, cancellationToken);
        var totalCount = await _unitOfWork.Repository<Province>()
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

        var result = new PagedResult<ProvinceDto>
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

    #endregion

    #region GetByIdAsync

    public async Task<ProvinceDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"provinces:{id}";
        if (_cacheService != null)
        {
            var cached = await _cacheService.GetAsync<ProvinceDto>(cacheKey, cancellationToken);
            if (cached != null)
                return cached;
        }

        var spec = new Spec<Province>().Where(p => p.ProvinceId == id);
        var result = await _unitOfWork.Repository<Province>()
            .FirstOrDefaultAsync(spec, ProvinceQueryConfig.Projection, cancellationToken);

        if (_cacheService != null && result != null)
            await _cacheService.SetAsync(cacheKey, result, TimeSpan.FromHours(1), cancellationToken);

        return result;
    }

    #endregion

    #region CreateAsync

    public async Task<ProvinceDto> CreateAsync(CreateProvinceDto dto, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating province: {ProvinceName}", dto.ProvinceName);
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await ValidateCreationAsync(dto, cancellationToken);
            var entity = _mapper.Map<Province>(dto);
            await _unitOfWork.Repository<Province>().AddAsync(entity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            _logger.LogInformation("Province created with ID: {Id}", entity.ProvinceId);
            return await GetByIdAsync(entity.ProvinceId, cancellationToken)
                   ?? throw new BusinessException("خطا در بازیابی استان ایجاد شده", "PROVINCE_RETRIEVAL_ERROR");
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Failed to create province: {ProvinceName}", dto.ProvinceName);
            throw new BusinessException("خطا در ایجاد استان", "PROVINCE_CREATE_ERROR");
        }
    }

    #endregion

    #region UpdateAsync

    public async Task<ProvinceDto?> UpdateAsync(UpdateProvinceDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.ProvinceId == null)
            throw new BusinessException("شناسه استان الزامی است.", "PROVINCE_ID_REQUIRED");

        _logger.LogInformation("Updating province ID: {Id}", dto.ProvinceId);
        var entity = await _unitOfWork.Repository<Province>().GetByIdAsync(dto.ProvinceId.Value, cancellationToken);
        if (entity == null)
        {
            _logger.LogWarning("Province not found: {Id}", dto.ProvinceId);
            return null;
        }

        if (entity.ProvinceName != dto.ProvinceName)
        {
            var exists = await _unitOfWork.Repository<Province>()
                .AnyAsync(p => p.ProvinceName == dto.ProvinceName && p.ProvinceId != dto.ProvinceId, cancellationToken);
            if (exists)
                throw new BusinessException("استانی با این نام قبلاً ثبت شده است.", "PROVINCE_NAME_DUPLICATE");
        }

        _mapper.Map(dto, entity);
        _unitOfWork.Repository<Province>().Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (_cacheService != null)
            await _cacheService.RemoveAsync($"provinces:{entity.ProvinceId}", cancellationToken);

        _logger.LogInformation("Province updated: {Id}", dto.ProvinceId);
        return await GetByIdAsync(entity.ProvinceId, cancellationToken);
    }

    #endregion

    #region DeleteAsync

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting province ID: {Id}", id);
        var entity = await _unitOfWork.Repository<Province>().GetByIdAsync(id, cancellationToken);
        if (entity == null)
            return false;

        var cityRepo = _unitOfWork.Repository<City>();
        if (cityRepo != null && await cityRepo.AnyAsync(c => c.ProvinceId == id, cancellationToken))
            throw new BusinessException("این استان دارای شهر است و قابل حذف نیست.", "PROVINCE_HAS_CITIES");

        _unitOfWork.Repository<Province>().Delete(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (_cacheService != null)
            await _cacheService.RemoveAsync($"provinces:{id}", cancellationToken);

        _logger.LogInformation("Province deleted: {Id}", id);
        return true;
    }

    #endregion

    #region Validation

    private async Task ValidateCreationAsync(CreateProvinceDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.ProvinceName))
            throw new BusinessException("نام استان الزامی است.", "PROVINCE_NAME_REQUIRED");

        var exists = await _unitOfWork.Repository<Province>()
            .AnyAsync(p => p.ProvinceName == dto.ProvinceName, cancellationToken);
        if (exists)
            throw new BusinessException("استانی با این نام قبلاً ثبت شده است.");
    }

    #endregion
}