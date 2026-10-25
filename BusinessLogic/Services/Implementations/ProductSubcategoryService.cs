using Application.Common.Queries;
using Application.Common.Specifications;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.ProductSubcategory;
using BusinessLogic.DTOs.Shared;
using BusinessLogic.Services.Interfaces;
using BusinessLogic.Specifications.ProductSubcategories;
using Microsoft.Extensions.Logging;

namespace BusinessLogic.Services.Implementations;

public sealed class ProductSubcategoryService : IProductSubcategoryService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<ProductSubcategoryService> _logger;

    public ProductSubcategoryService(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ICurrentUserService currentUserService,
        ILogger<ProductSubcategoryService> logger)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    #region GetByQueryAsync

    public async Task<PagedResult<ProductSubcategoryDto>> GetByQueryAsync(
        QueryContract<ProductSubcategory> query,
        CancellationToken cancellationToken = default)
    {
        var spec = query.ToSpec();
        var projection = ProductSubcategoryQueryConfig.Projection;
        var items = await _unitOfWork.Repository<ProductSubcategory>()
            .ListAsync(spec, projection, cancellationToken);
        var totalCount = await _unitOfWork.Repository<ProductSubcategory>()
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

        return new PagedResult<ProductSubcategoryDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    #endregion

    #region GetByIdAsync

    public async Task<ProductSubcategoryDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var spec = new Spec<ProductSubcategory>()
            .Where(ps => ps.SubcategoryId == id)
            .Where(ps => ps.IsActive);

        return await _unitOfWork.Repository<ProductSubcategory>()
            .FirstOrDefaultAsync(spec, ProductSubcategoryQueryConfig.Projection, cancellationToken);
    }

    #endregion

    #region GetCountByCategoryIdAsync

    public async Task<int> GetCountByCategoryIdAsync(int categoryId, CancellationToken cancellationToken = default)
    {
        var spec = new Spec<ProductSubcategory>()
            .Where(ps => ps.CategoryId == categoryId)
            .Where(ps => ps.IsActive);

        return await _unitOfWork.Repository<ProductSubcategory>().CountAsync(spec, cancellationToken);
    }

    #endregion

    #region CreateAsync

    public async Task<ProductSubcategoryDto> CreateAsync(CreateProductSubcategoryDto dto, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating product subcategory: {SubcategoryName}", dto.SubcategoryName);
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await ValidateCreationAsync(dto, cancellationToken);
            var entity = _mapper.Map<ProductSubcategory>(dto);
            entity.CreatedOn = DateTime.UtcNow;
            entity.CreatedById = _currentUserService.GetCurrentUserId();

            await _unitOfWork.Repository<ProductSubcategory>().AddAsync(entity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            _logger.LogInformation("Product subcategory created with ID: {Id}", entity.SubcategoryId);
            return await GetByIdAsync(entity.SubcategoryId, cancellationToken)
                   ?? throw new BusinessException("خطا در بازیابی زیردسته‌بندی ایجاد شده", "SUBCATEGORY_RETRIEVAL_ERROR");
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Failed to create product subcategory: {SubcategoryName}", dto.SubcategoryName);
            throw new BusinessException("خطا در ایجاد زیردسته‌بندی", "SUBCATEGORY_CREATE_ERROR");
        }
    }

    #endregion

    #region UpdateAsync

    public async Task<ProductSubcategoryDto> UpdateAsync(UpdateProductSubcategoryDto dto, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating product subcategory ID: {Id}", dto.SubcategoryId);
        var entity = await _unitOfWork.Repository<ProductSubcategory>().GetByIdAsync(dto.SubcategoryId, cancellationToken);
        if (entity == null || !entity.IsActive)
            throw new BusinessException("زیردسته‌بندی یافت نشد.", "SUBCATEGORY_NOT_FOUND");

        _mapper.Map(dto, entity);
        _unitOfWork.Repository<ProductSubcategory>().Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Product subcategory updated: {Id}", dto.SubcategoryId);
        return await GetByIdAsync(entity.SubcategoryId, cancellationToken)
               ?? throw new BusinessException("خطا در بازیابی زیردسته‌بندی به‌روز شده", "SUBCATEGORY_RETRIEVAL_ERROR");
    }

    #endregion

    #region DeleteAsync

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting product subcategory ID: {Id}", id);
        var entity = await _unitOfWork.Repository<ProductSubcategory>().GetByIdAsync(id, cancellationToken);
        if (entity == null || !entity.IsActive)
            return false;

        _unitOfWork.Repository<ProductSubcategory>().Delete(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Product subcategory deleted: {Id}", id);
        return true;
    }

    #endregion

    #region ExistsAsync

    public async Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default)
    {
        var spec = new Spec<ProductSubcategory>()
            .Where(ps => ps.SubcategoryId == id)
            .Where(ps => ps.IsActive);
        return await _unitOfWork.Repository<ProductSubcategory>().AnyAsync(spec, cancellationToken);
    }

    #endregion

    #region Validation

    private async Task ValidateCreationAsync(CreateProductSubcategoryDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.SubcategoryName))
            throw new BusinessException("نام زیردسته‌بندی الزامی است.", "SUBCATEGORY_NAME_REQUIRED");

        var exists = await _unitOfWork.Repository<ProductSubcategory>()
            .AnyAsync(ps => ps.SubcategoryName == dto.SubcategoryName, cancellationToken);
        if (exists)
            throw new BusinessException("زیردسته‌بندی با این نام قبلاً ثبت شده است.", "SUBCATEGORY_NAME_DUPLICATE");

        var categoryExists = await _unitOfWork.Repository<ProductCategory>()
            .AnyAsync(pc => pc.CategoryId == dto.CategoryId, cancellationToken);
        if (!categoryExists)
            throw new BusinessException("دسته‌بندی والد وجود ندارد.", "PARENT_CATEGORY_NOT_FOUND");
    }

    #endregion
}