using Application.Common.Queries;
using Application.Common.Specifications;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.EmployeeType;
using BusinessLogic.DTOs.Shared;
using BusinessLogic.Services.Interfaces;
using BusinessLogic.Specifications.EmployeeTypes;
using Microsoft.Extensions.Logging;

namespace BusinessLogic.Services.Implementations;

public sealed class EmployeeTypeService : IEmployeeTypeService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ILogger<EmployeeTypeService> _logger;

    public EmployeeTypeService(IUnitOfWork unitOfWork, IMapper mapper, ILogger<EmployeeTypeService> logger)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _logger = logger;
    }

    #region Query

    public async Task<PagedResult<EmployeeTypeDto>> GetByQueryAsync(
        QueryContract<EmployeeType> query,
        CancellationToken cancellationToken = default)
    {
        var spec = query.ToSpec();
        var items = await _unitOfWork.Repository<EmployeeType>()
            .ListAsync(spec, EmployeeTypeQueryConfig.Projection, cancellationToken);
        var totalCount = await _unitOfWork.Repository<EmployeeType>()
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

        return new PagedResult<EmployeeTypeDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    public async Task<EmployeeTypeDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var spec = new Spec<EmployeeType>().Where(et => et.EmployeeTypeId == id);
        return await _unitOfWork.Repository<EmployeeType>()
            .FirstOrDefaultAsync(spec, EmployeeTypeQueryConfig.Projection, cancellationToken);
    }

    #endregion

    #region Commands

    public async Task<EmployeeTypeDto> CreateAsync(
    CreateEmployeeTypeDto dto,
    CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating employee type: {TypeName}", dto.TypeName);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            await ValidateCreationAsync(dto, cancellationToken);

            var entity = _mapper.Map<EmployeeType>(dto);

            await _unitOfWork.Repository<EmployeeType>()
                .AddAsync(entity, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            _logger.LogInformation("Employee type created: {Id}", entity.EmployeeTypeId);

            var result = await _unitOfWork.Repository<EmployeeType>()
                .FirstOrDefaultAsync(
                    new Spec<EmployeeType>()
                        .Where(x => x.EmployeeTypeId == entity.EmployeeTypeId),
                    EmployeeTypeQueryConfig.Projection,
                    cancellationToken);

            return result!;
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);

            _logger.LogError(ex, "Failed to create employee type");

            throw new BusinessException("خطا در ایجاد نوع کارمند", ex);
        }
    }

    public async Task<EmployeeTypeDto?> UpdateAsync(
    UpdateEmployeeTypeDto dto,
    CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating employee type ID: {Id}", dto.EmployeeTypeId);

        var entity = await _unitOfWork.Repository<EmployeeType>()
            .GetByIdAsync(dto.EmployeeTypeId, cancellationToken);

        if (entity is null)
            return null;

        _mapper.Map(dto, entity);

        _unitOfWork.Repository<EmployeeType>().Update(entity);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await _unitOfWork.Repository<EmployeeType>()
            .FirstOrDefaultAsync(
                new Spec<EmployeeType>()
                    .Where(x => x.EmployeeTypeId == entity.EmployeeTypeId),
                EmployeeTypeQueryConfig.Projection,
                cancellationToken);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting employee type ID: {Id}", id);
        var entity = await _unitOfWork.Repository<EmployeeType>().GetByIdAsync(id, cancellationToken);
        if (entity == null)
        {
            _logger.LogWarning("Delete failed: employee type not found {Id}", id);
            return false;
        }

        _unitOfWork.Repository<EmployeeType>().Delete(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Employee type deleted: {Id}", id);
        return true;
    }

    #endregion

    #region Validation

    private async Task ValidateCreationAsync(CreateEmployeeTypeDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.TypeName))
            throw new BusinessException("نام نوع کارمند الزامی است.");

        var exists = await _unitOfWork.Repository<EmployeeType>()
            .AnyAsync(et => et.TypeName == dto.TypeName, cancellationToken);
        if (exists)
            throw new BusinessException("نوع کارمند با این نام قبلاً ثبت شده است.");
    }

    #endregion
}