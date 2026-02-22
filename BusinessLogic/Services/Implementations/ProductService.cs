using Application.Entities;
using Application.Interfaces;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using BusinessLogic.DTOs.Product;
using BusinessLogic.DTOs.Shared;
using BusinessLogic.Services.Interfaces;
using BusinessLogic.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BusinessLogic.Services.Implementations
{
    public class ProductService : IProductService
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

                await _unitOfWork.Product.AddAsync(entity);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                _logger.LogInformation("Product created successfully with ID: {ProductId}", entity.ProductId);
                return _mapper.Map<ProductDto>(entity);
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                _logger.LogError(ex, "Failed to create product with name: {ProductName}", dto.Name);
                throw new Exception($"خطا در ایجاد محصول: {ex.Message}", ex);
            }
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Attempting to delete product with ID: {ProductId}", id);

            try
            {
                // Check existence efficiently
                var exists = await _unitOfWork.Product.AnyAsync(p => p.ProductId == id && p.IsActive, cancellationToken);
                if (!exists)
                {
                    _logger.LogWarning("Delete failed: Product not found or inactive with ID: {ProductId}", id);
                    return false;
                }

                var entity = await _unitOfWork.Product.GetByIdAsync(id, cancellationToken);
                // entity is not null because exists check passed
                await _unitOfWork.Product.DeleteAsync(entity!);
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

        public async Task<PagedResult<ProductDto>> GetFilteredAsync(ProductFilterDto filter, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Retrieving filtered products. Page: {PageNumber}, Size: {PageSize}", filter.PageNumber, filter.PageSize);

            try
            {
                var query = _unitOfWork.Product.Query(); // No Include needed with ProjectTo

                // Apply filters
                if (!string.IsNullOrWhiteSpace(filter.Search) && filter.Search.Length > 2)
                {
                    query = query.Where(p => p.Name.Contains(filter.Search));
                    _logger.LogDebug("Applied search filter: {Search}", filter.Search);
                }

                if (filter.CategoryId.HasValue)
                {
                    query = query.Where(p => p.Subcategory.CategoryId == filter.CategoryId.Value);
                    _logger.LogDebug("Applied category filter: {CategoryId}", filter.CategoryId);
                }

                if (filter.SubcategoryId.HasValue)
                {
                    query = query.Where(p => p.SubcategoryId == filter.SubcategoryId.Value);
                    _logger.LogDebug("Applied subcategory filter: {SubcategoryId}", filter.SubcategoryId);
                }

                if (filter.MinPrice.HasValue)
                {
                    query = query.Where(p => p.Price >= filter.MinPrice.Value);
                    _logger.LogDebug("Applied min price filter: {MinPrice}", filter.MinPrice);
                }

                if (filter.MaxPrice.HasValue)
                {
                    query = query.Where(p => p.Price <= filter.MaxPrice.Value);
                    _logger.LogDebug("Applied max price filter: {MaxPrice}", filter.MaxPrice);
                }

                // Apply sorting
                string sortBy;
                bool ascending;

                switch (filter.SortOrder?.ToLower())
                {
                    case "asc":
                        sortBy = "Price";
                        ascending = true;
                        break;
                    case "desc":
                        sortBy = "Price";
                        ascending = false;
                        break;
                    default:
                        sortBy = "ProductId";
                        ascending = true;
                        break;
                }

                query = query.ApplySorting(sortBy, ascending);
                _logger.LogDebug("Applied sorting: {SortBy} {SortDirection}", sortBy, ascending ? "ASC" : "DESC");

                var pagedResult = await query
                    .ProjectTo<ProductDto>(_mapper.ConfigurationProvider)
                    .ToPagedResultAsync(filter.PageNumber, filter.PageSize, cancellationToken);

                _logger.LogDebug("Retrieved {ItemCount} products out of {TotalCount} total", pagedResult.Items.Count(), pagedResult.TotalCount);
                return pagedResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving filtered products");
                throw;
            }
        }

        public async Task<IEnumerable<ProductDto>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Retrieving all active products");

            try
            {
                var products = await _unitOfWork.Product.Query()
                    .ProjectTo<ProductDto>(_mapper.ConfigurationProvider)
                    .ToListAsync(cancellationToken);

                _logger.LogDebug("Retrieved {ProductCount} products", products.Count);
                return products;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all products");
                throw;
            }
        }

        public async Task<ProductDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Retrieving product by ID: {ProductId}", id);

            try
            {
                var product = await _unitOfWork.Product.Query()
                    .Where(p => p.ProductId == id)
                    .ProjectTo<ProductDto>(_mapper.ConfigurationProvider)
                    .FirstOrDefaultAsync(cancellationToken);

                if (product == null)
                {
                    _logger.LogDebug("Product not found with ID: {ProductId}", id);
                    return null;
                }

                _logger.LogDebug("Product found: {ProductId} - {ProductName}", id, product.Name);
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
                var entity = await _unitOfWork.Product.GetByIdAsync(dto.ProductId, cancellationToken);

                if (entity == null)
                {
                    _logger.LogWarning("Update failed: Product not found with ID: {ProductId}", dto.ProductId);
                    return null;
                }

                // Optional: check for duplicate name if changed
                if (!string.Equals(entity.Name, dto.Name, StringComparison.OrdinalIgnoreCase))
                {
                    var nameExists = await _unitOfWork.Product.AnyAsync(p => p.Name == dto.Name && p.ProductId != dto.ProductId, cancellationToken);
                    if (nameExists)
                    {
                        _logger.LogWarning("Update failed: Duplicate product name '{ProductName}' for ID: {ProductId}", dto.Name, dto.ProductId);
                        throw new Exception("محصولی با این نام قبلاً ثبت شده است");
                    }
                }

                _logger.LogDebug("Mapping UpdateProductDto to existing product entity");
                _mapper.Map(dto, entity);

                await _unitOfWork.Product.UpdateAsync(entity);
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

            var exists = await _unitOfWork.Product.AnyAsync(p => p.Name == dto.Name, cancellationToken);

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