using Application.Entities;
using Application.Interfaces;
using Application.Common.Specifications;
using AutoMapper;
using BusinessLogic.DTOs.Product;
using BusinessLogic.DTOs.Shared;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Logging;
using BusinessLogic.Specifications.Products;
using Application.Exceptions;
using Application.Common.Helpers;

namespace BusinessLogic.Services.Implementations
{
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

        #region Public Methods

        public async Task<ProductDto> CreateAsync(CreateProductDto dto, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Creating new product with name: {ProductName}", dto.Name);

            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                await ValidateProductCreationAsync(dto, cancellationToken);

                var entity = _mapper.Map<Product>(dto);

                await _unitOfWork.Repository<Product>().AddAsync(entity, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                _logger.LogInformation("Product created successfully with ID: {ProductId}", entity.ProductId);
                return _mapper.Map<ProductDto>(entity);
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                _logger.LogError(ex, "Failed to create product with name: {ProductName}", dto.Name);
                throw new BusinessException("خطا در ایجاد محصول", ex);
            }
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Attempting to delete product with ID: {ProductId}", id);

            try
            {
                var product = await _unitOfWork.Repository<Product>().GetByIdAsync(id, cancellationToken);
                if (product == null || !product.IsActive)
                {
                    _logger.LogWarning("Delete failed: Product not found or inactive with ID: {ProductId}", id);
                    return false;
                }

                // Soft Delete
                product.IsActive = false;
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Product deleted successfully: {ProductId}", id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting product with ID: {ProductId}", id);
                throw;
            }
        }

        public async Task<PagedResult<ProductDto>> GetByQueryAsync(
            string? filter,
            string? sort,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            // اعمال محدودیت‌های امنیتی
            QueryGuard.EnsureValid(filter, sort);

            _logger.LogInformation(
                "Retrieving products. Filter: {Filter}, Sort: {Sort}, Page: {Page}",
                filter, sort, pageNumber);

            try
            {
                var skip = (pageNumber - 1) * pageSize;

                // Specification برای داده‌ها
                var dataSpec = new QuerySpecification<Product, ProductDto>(
                    filter,
                    sort,
                    skip,
                    pageSize,
                    ProductQueryConfig.Projection,
                    ProductQueryConfig.AllowedFields);

                // استفاده از QueryCountSpecification برای شمارش
                var countSpec = new QueryCountSpecification<Product>(
                    filter,
                    ProductQueryConfig.AllowedFields);

                var items = await _unitOfWork
                    .Repository<Product>()
                    .ListAsync<ProductDto>(dataSpec, cancellationToken);

                var totalCount = await _unitOfWork
                    .Repository<Product>()
                    .CountAsync(countSpec, cancellationToken);

                return new PagedResult<ProductDto>
                {
                    Items = items,
                    TotalCount = totalCount,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving products");
                throw;
            }
        }

        public async Task<ProductDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Retrieving product by ID: {ProductId}", id);

            try
            {
                var spec = new QuerySpecification<Product, ProductDto>(
                    filter: $"id eq {id}",
                    sort: null,
                    skip: null,
                    take: null,
                    projection: ProductQueryConfig.Projection,
                    allowedFields: ProductQueryConfig.AllowedFields,
                    applyDefaultSoftDelete: true
                );

                // ذکر صریح نوع خروجی برای رفع ابهام
                var product = await _unitOfWork
                    .Repository<Product>()
                    .FirstOrDefaultAsync<ProductDto>(spec, cancellationToken);

                if (product == null)
                    _logger.LogDebug("Product not found with ID: {ProductId}", id);

                return product;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving product with ID: {ProductId}", id);
                throw;
            }
        }

        public async Task<ProductDto?> UpdateAsync(UpdateProductDto dto, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Updating product with ID: {ProductId}", dto.ProductId);

            try
            {
                var entity = await _unitOfWork.Repository<Product>().GetByIdAsync(dto.ProductId, cancellationToken);

                if (entity == null)
                {
                    _logger.LogWarning("Update failed: Product not found with ID: {ProductId}", dto.ProductId);
                    return null;
                }

                // Optional: check for duplicate name if changed
                if (!string.Equals(entity.Name, dto.Name, StringComparison.OrdinalIgnoreCase))
                {
                    var nameExists = await _unitOfWork.Repository<Product>()
                        .AnyAsync(p => p.Name == dto.Name && p.ProductId != dto.ProductId, cancellationToken);
                    if (nameExists)
                    {
                        _logger.LogWarning("Update failed: Duplicate product name '{ProductName}' for ID: {ProductId}", dto.Name, dto.ProductId);
                        throw new Exception("محصولی با این نام قبلاً ثبت شده است");
                    }
                }

                _logger.LogDebug("Mapping UpdateProductDto to existing product entity");
                _mapper.Map(dto, entity);

                _unitOfWork.Repository<Product>().Update(entity);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Product updated successfully: {ProductId}", dto.ProductId);
                return _mapper.Map<ProductDto>(entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating product with ID: {ProductId}", dto.ProductId);
                throw;
            }
        }

        #endregion

        #region Private Methods

        private async Task ValidateProductCreationAsync(CreateProductDto dto, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Checking for duplicate product name: {ProductName}", dto.Name);

            var exists = await _unitOfWork.Repository<Product>().AnyAsync(p => p.Name == dto.Name, cancellationToken);

            if (exists)
            {
                _logger.LogWarning("Duplicate product name detected: {ProductName}", dto.Name);
                throw new Exception("محصولی با این نام قبلاً ثبت شده است");
            }

            _logger.LogDebug("Product name validation passed for: {ProductName}", dto.Name);
        }

        #endregion
    }
}