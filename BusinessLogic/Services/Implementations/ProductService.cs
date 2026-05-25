using Application.Common.Queries;
using Application.Common.Specifications;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.Product;
using BusinessLogic.DTOs.Shared;
using BusinessLogic.Services.Interfaces;
using BusinessLogic.Specifications.Products;
using Microsoft.Extensions.Logging;

namespace BusinessLogic.Services.Implementations;

public sealed class ProductService : IProductService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<ProductService> _logger;

    public ProductService(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ICurrentUserService currentUserService,
        ILogger<ProductService> logger)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    #region Query

    public async Task<PagedResult<ProductDto>> GetByQueryAsync(
        QueryContract<Product> query,
        CancellationToken cancellationToken = default)
    {
        var spec = query.ToSpec();

        var items = await _unitOfWork.Repository<Product>()
            .ListAsync(spec, ProductQueryConfig.Projection, cancellationToken);

        var totalCount = await _unitOfWork.Repository<Product>()
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

        return new PagedResult<ProductDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    public async Task<ProductDto?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var spec = new Spec<Product>().Where(p => p.ProductId == id).Where(p => p.IsActive);

        return await _unitOfWork.Repository<Product>()
            .FirstOrDefaultAsync(spec, ProductQueryConfig.Projection, cancellationToken);
    }

    #endregion

    #region Commands

    public async Task<ProductDto> CreateAsync(CreateProductDto dto, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating product with name: {ProductName}", dto.Name);
        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            await EnsureValidProductCreationAsync(dto, cancellationToken);
            var entity = _mapper.Map<Product>(dto);
            await _unitOfWork.Repository<Product>().AddAsync(entity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            _logger.LogInformation("Product created successfully with ID: {ProductId}", entity.ProductId);

            return _mapper.Map<ProductDto>(entity);
        }
        catch (Exception ex) when (ex is not BusinessException)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Error creating product: {ProductName}", dto.Name);
            throw new BusinessException("خطا در ایجاد محصول", ex);
        }
    }

    public async Task<ProductDto?> UpdateAsync(
        UpdateProductDto dto,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating product with ID: {ProductId}", dto.ProductId);

        try
        {
            var entity = await _unitOfWork.Repository<Product>().GetByIdAsync(dto.ProductId, cancellationToken);
            if (entity == null)
            {
                _logger.LogWarning("Product not found: {ProductId}", dto.ProductId);
                return null;
            }

            if (!string.Equals(entity.Name, dto.Name, StringComparison.OrdinalIgnoreCase))
            {
                var exists = await _unitOfWork.Repository<Product>()
                    .AnyAsync(x => x.Name == dto.Name && x.ProductId != dto.ProductId, cancellationToken);
                if (exists)
                    throw new BusinessException("محصولی با این نام قبلاً ثبت شده است");
            }

            _mapper.Map(dto, entity);
            _unitOfWork.Repository<Product>().Update(entity);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Product updated successfully: {ProductId}", dto.ProductId);
            return await GetByIdAsync(entity.ProductId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating product: {ProductId}", dto.ProductId);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting product: {ProductId}", id);
        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            var entity = await _unitOfWork.Repository<Product>().GetByIdAsync(id, cancellationToken);
            if (entity == null || !entity.IsActive)
            {
                _logger.LogWarning("Product not found or inactive: {ProductId}", id);
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return false;
            }

            entity.IsActive = false;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            _logger.LogInformation("Product soft deleted successfully: {ProductId}", id);
            return true;
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Error deleting product: {ProductId}", id);
            throw;
        }
    }

    #endregion

    #region Validation

    private async Task EnsureValidProductCreationAsync(
        CreateProductDto dto,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new BusinessException("نام محصول الزامی است.");

        if (dto.Price <= 0)
            throw new BusinessException("قیمت محصول باید بیشتر از صفر باشد.");

        if (dto.SubcategoryId <= 0)
            throw new BusinessException("زیردسته‌بندی نامعتبر است.");

        var subcategoryExists = await _unitOfWork.Repository<ProductSubcategory>()
            .AnyAsync(x => x.SubcategoryId == dto.SubcategoryId, cancellationToken);
        if (!subcategoryExists)
            throw new BusinessException("زیردسته‌بندی انتخاب‌شده وجود ندارد.");

        var duplicateName = await _unitOfWork.Repository<Product>()
            .AnyAsync(x => x.Name == dto.Name, cancellationToken);
        if (duplicateName)
            throw new BusinessException("محصولی با این نام قبلاً ثبت شده است.");
    }

    #endregion
}